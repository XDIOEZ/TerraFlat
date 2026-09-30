using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using Unity.Profiling;

/// <summary>
/// Ground / Water / Back / Blocking 共用 Chunk Mesh，扩展表现仍借用 BRG Owner。
/// 碰撞由 ChunkCollisionRenderer 独立映射；Ground 单格变更只上传 GPU Cell 数据。
/// </summary>
public sealed partial class ChunkTilemapRenderer : MonoBehaviour, IChunkViewRenderer, IWorldAwareChunkViewRenderer
{
    #region 配置与状态

    [SerializeField] private ChunkTilePaletteSO palette;
    [SerializeField] private Material groundMaterial;
    [SerializeField] private Material backMaterial;
    [SerializeField] private Material blockingMaterial;
    [SerializeField] private Material stylizedWaterMaterial;
    [SerializeField] private Material realisticWaterMaterial;

    private readonly List<NeighbourTerrainSubscription> neighbourTerrainSubscriptions = new(8);
    private readonly ChunkTerrainData[] neighbourTerrains = new ChunkTerrainData[9]; // 八方向邻区在订阅时解析一次。
    private readonly HashSet<int> liquidVisualDirty = new(); // 合并本块与相邻块的液体批次。
    private static readonly ProfilerMarker BatchOwnerUnregisterMarker =
        new("FlatWorld.ChunkStreaming.UnregisterBatchOwner");
    private WorldRuntime boundWorld;
    private GameRes boundResources; // 只订阅正式目录发布，不重建 ChunkRuntime。
    private ChunkRuntime boundChunk;
    private ChunkGroundMeshRenderer groundMesh;
    private ChunkGroundMeshRenderer waterMesh;
    private ChunkGroundMeshRenderer backMesh;
    private ChunkGroundMeshRenderer blockingMesh;
    private readonly List<IDisposable> neighbourChunkEventSubscriptions = new(16);
    private bool renderGroundElevation; // 只有使用 Surface 生成语义的维度显示高度。
    private bool batchPresentationComplete;
    private bool batchBindingInProgress;

    /// <summary>Editor 与 Development Build 只记录 BRG 异常与手动诊断，正式非开发包保持静默。</summary>
    private static bool RenderDebugEnabled => Application.isEditor || Debug.isDebugBuild;
    private static readonly HashSet<string> renderDebugMissingVisualKeys = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRenderDebugState()
    {
        renderDebugMissingVisualKeys.Clear();
        ResetMechanicalVisualCache();
    }

    /// <summary>阻挡层材质供裂缝等表现复用。</summary>
    public Material BlockingMaterial => blockingMaterial;

    /// <summary>当前区块的扩展表现 Owner 和基础网格是否就绪。</summary>
    public bool IsBatchPresentationRegistered =>
        boundChunk?.Terrain != null && ChunkBatchRendererGroupService.IsOwnerRegistered(this);

    /// <summary>基础地形网格完成提交且扩展表现 Owner 仍登记。</summary>
    public bool IsBatchPresentationComplete =>
        batchPresentationComplete && IsBatchPresentationRegistered;

    internal int BaseTerrainVisualCount =>
        (groundMesh?.ActiveCellCount ?? 0) + (waterMesh?.ActiveCellCount ?? 0) +
        (backMesh?.ActiveCellCount ?? 0) + (blockingMesh?.ActiveCellCount ?? 0);

    /// <summary>基础网格和 BRG Owner 重建后通知额外表现图层。</summary>
    internal event Action BatchPresentationRebuilt;

    #endregion

    #region 绑定与生命周期

    /// <summary>注入当前世界，用于计算跨 Chunk 的接触方向与连续水深边界。</summary>
    public void SetWorld(WorldRuntime worldRuntime)
    {
        if (ReferenceEquals(boundWorld, worldRuntime))
            return;

        ClearNeighbourChunkEventSubscriptions();
        boundWorld = worldRuntime;
        SubscribeNeighbourChunkEvents();
        RefreshNeighbourTerrainSubscriptions();
    }

    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new System.ArgumentNullException(nameof(chunk));
        if (chunk.Terrain == null)
            throw new System.InvalidOperationException("Cannot bind terrain rendering before data is ready.");
        if (ReferenceEquals(boundChunk, chunk))
            return;

        Unbind();
        batchBindingInProgress = true;
        try
        {
            boundChunk = chunk;
            SubscribeNeighbourChunkEvents();
            groundMesh = new ChunkGroundMeshRenderer(transform, chunk.Terrain.Width,
                chunk.Terrain.Height, groundMaterial, ChunkBatchRendererGroupService.VisualLayer.Ground);
            waterMesh = new ChunkGroundMeshRenderer(transform, chunk.Terrain.Width,
                chunk.Terrain.Height, null, ChunkBatchRendererGroupService.VisualLayer.Water);
            backMesh = new ChunkGroundMeshRenderer(transform, chunk.Terrain.Width,
                chunk.Terrain.Height, backMaterial, ChunkBatchRendererGroupService.VisualLayer.Back);
            blockingMesh = new ChunkGroundMeshRenderer(transform, chunk.Terrain.Width,
                chunk.Terrain.Height, blockingMaterial, ChunkBatchRendererGroupService.VisualLayer.Blocking);
            boundResources = GameRes.ExistingInstance;
            if (boundResources != null) boundResources.ResourcesReloaded += HandleResourcesReloaded;
            WaterVisualSettings.Changed += HandleWaterVisualStyleChanged;
            renderGroundElevation = IsSurfaceDimension(chunk.Address.DimensionId);
            boundChunk.Terrain.Changed += HandleTerrainChanged;
            boundChunk.Terrain.LiquidBatchChanged += HandleLiquidBatchChanged;
            WorldLiquidFlowExperiment.FlowChanged += HandleLiquidFlowChanged;
            RefreshNeighbourTerrainSubscriptions();
            batchPresentationComplete = false;
            ChunkBatchRendererGroupService.RegisterOwner(this, GetBatchWorldBounds(chunk.Terrain));
            RefreshAllBatchVisuals(chunk.Terrain);
            batchPresentationComplete = true;
            BindMechanicalPresentation();
            BatchPresentationRebuilt?.Invoke();
        }
        finally
        {
            batchBindingInProgress = false;
        }
    }

    public void Unbind()
    {
        naturalVisualSlots.Clear();
        freeNaturalVisualSlots.Clear();
        nextNaturalVisualSlot = 0;
        hasResourceVisualBounds = false;
        UnbindMechanicalPresentation();
        ReleaseDepthPresentation();
        if (boundResources != null) boundResources.ResourcesReloaded -= HandleResourcesReloaded;
        WaterVisualSettings.Changed -= HandleWaterVisualStyleChanged;
        boundResources = null;
        WorldLiquidFlowExperiment.FlowChanged -= HandleLiquidFlowChanged;
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.LiquidBatchChanged -= HandleLiquidBatchChanged;
        liquidVisualDirty.Clear();
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
        ClearNeighbourTerrainSubscriptions();
        ClearNeighbourChunkEventSubscriptions();
        batchBindingInProgress = false;
        batchPresentationComplete = false;
        groundMesh?.Dispose();
        groundMesh = null;
        waterMesh?.Dispose();
        waterMesh = null;
        backMesh?.Dispose();
        backMesh = null;
        blockingMesh?.Dispose();
        blockingMesh = null;
        using (BatchOwnerUnregisterMarker.Auto())
            ChunkBatchRendererGroupService.UnregisterOwner(this);
        renderGroundElevation = false;
        boundChunk = null;
    }

    /// <summary>F5 只从原权威地形重建视觉与碰撞映射，保留区块、液体、自然物及世界租约。</summary>
    private void HandleResourcesReloaded()
    {
        if (boundChunk?.Terrain == null || !batchPresentationComplete) return;
        ClearDepthPresentation();
        ResetMechanicalVisualCache();
        groundMesh?.Dispose();
        waterMesh?.Dispose();
        backMesh?.Dispose();
        blockingMesh?.Dispose();
        groundMesh = new ChunkGroundMeshRenderer(transform, boundChunk.Terrain.Width,
            boundChunk.Terrain.Height, groundMaterial, ChunkBatchRendererGroupService.VisualLayer.Ground);
        waterMesh = new ChunkGroundMeshRenderer(transform, boundChunk.Terrain.Width,
            boundChunk.Terrain.Height, null, ChunkBatchRendererGroupService.VisualLayer.Water);
        backMesh = new ChunkGroundMeshRenderer(transform, boundChunk.Terrain.Width,
            boundChunk.Terrain.Height, backMaterial, ChunkBatchRendererGroupService.VisualLayer.Back);
        blockingMesh = new ChunkGroundMeshRenderer(transform, boundChunk.Terrain.Width,
            boundChunk.Terrain.Height, blockingMaterial, ChunkBatchRendererGroupService.VisualLayer.Blocking);
        RefreshAllBatchVisuals(boundChunk.Terrain);
        BatchPresentationRebuilt?.Invoke();
    }

    /// <summary>
    /// BRG 后端因脚本热重载或异常生命周期被重建时，使用现有权威地形重新登记表现。
    /// 不重绑草地、自然物、导航等其它 ChunkView 子系统。
    /// </summary>
    public bool RepairBatchPresentationIfNeeded()
    {
        return RepairBatchPresentationIfNeeded("ExternalCheck");
    }

    /// <summary>按触发来源修复 BRG 表现；只在异常路径记录一次诊断。</summary>
    private bool RepairBatchPresentationIfNeeded(string reason)
    {
        if (boundChunk?.Terrain == null)
            return false;

        ChunkTerrainData terrain = boundChunk.Terrain;
        if (ChunkBatchRendererGroupService.IsOwnerRegistered(this) &&
            batchPresentationComplete)
        {
            return true;
        }

        if (RenderDebugEnabled)
        {
            int expectedSubmittable = CountSubmittableVisuals(terrain);
            int submitted = ChunkBatchRendererGroupService.GetOwnerTerrainVisualCount(this) +
                            (groundMesh?.ActiveCellCount ?? 0) +
                            (waterMesh?.ActiveCellCount ?? 0) +
                            (backMesh?.ActiveCellCount ?? 0) +
                            (blockingMesh?.ActiveCellCount ?? 0);
            Debug.LogWarning($"[ChunkRenderDebug] RepairPresentation reason={reason} chunk={GetDebugChunkLabel()} " +
                             $"registered={ChunkBatchRendererGroupService.IsOwnerRegistered(this)} " +
                             $"complete={batchPresentationComplete} submitted={submitted} " +
                             $"expectedSubmittable={expectedSubmittable}. " +
                             "将从权威 Terrain 全量重建 BRG 表现。");
        }

        batchPresentationComplete = false;
        groundMesh?.Dispose();
        waterMesh?.Dispose();
        backMesh?.Dispose();
        blockingMesh?.Dispose();
        groundMesh = new ChunkGroundMeshRenderer(transform, terrain.Width,
            terrain.Height, groundMaterial, ChunkBatchRendererGroupService.VisualLayer.Ground);
        waterMesh = new ChunkGroundMeshRenderer(transform, terrain.Width,
            terrain.Height, null, ChunkBatchRendererGroupService.VisualLayer.Water);
        backMesh = new ChunkGroundMeshRenderer(transform, terrain.Width,
            terrain.Height, backMaterial, ChunkBatchRendererGroupService.VisualLayer.Back);
        blockingMesh = new ChunkGroundMeshRenderer(transform, terrain.Width,
            terrain.Height, blockingMaterial, ChunkBatchRendererGroupService.VisualLayer.Blocking);
        ChunkBatchRendererGroupService.UnregisterOwner(this);
        ChunkBatchRendererGroupService.RegisterOwner(this, GetBatchWorldBounds(terrain));
        RefreshAllBatchVisuals(terrain);
        batchPresentationComplete = true;
        ValidateBatchPresentation("Repair", terrain);
        BatchPresentationRebuilt?.Invoke();
        return ChunkBatchRendererGroupService.IsOwnerRegistered(this) && batchPresentationComplete;
    }

    private void OnDestroy()
    {
        UnbindMechanicalPresentation();
        ReleaseDepthPresentation();
        WaterVisualSettings.Changed -= HandleWaterVisualStyleChanged;
        WorldLiquidFlowExperiment.FlowChanged -= HandleLiquidFlowChanged;
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.LiquidBatchChanged -= HandleLiquidBatchChanged;
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
        batchPresentationComplete = false;
        groundMesh?.Dispose();
        groundMesh = null;
        waterMesh?.Dispose();
        waterMesh = null;
        backMesh?.Dispose();
        backMesh = null;
        blockingMesh?.Dispose();
        blockingMesh = null;
        ChunkBatchRendererGroupService.UnregisterOwner(this);
        ClearNeighbourChunkEventSubscriptions();
        ClearNeighbourTerrainSubscriptions();
    }

    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (boundChunk?.Terrain == null)
            return;
        if (changed.Kind != TerrainChangeKind.Cell &&
            changed.Kind != TerrainChangeKind.TileStack &&
            changed.Kind != TerrainChangeKind.Environment && changed.Kind != TerrainChangeKind.Liquid)
            return;

        if (!EnsureBatchPresentationForIncrementalRefresh("TerrainChanged"))
            return;
        // 高度边、岸线、墙脚和四角水深最多依赖一圈邻格，因此只刷新 3x3 脏区。
        RefreshBatchArea(boundChunk.Terrain, changed.LocalCell.X, changed.LocalCell.Y, 1);
    }

    /// <summary>水体风格切换后，把现有水格迁移到新材质批次。</summary>
    private void HandleWaterVisualStyleChanged()
    {
        if (boundChunk?.Terrain == null || batchBindingInProgress)
            return;
        if (!EnsureBatchPresentationForIncrementalRefresh("WaterStyleChanged"))
            return;

        ChunkTerrainData terrain = boundChunk.Terrain;
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
            if (terrain.GetLiquidDepth(x, y) > 0f)
                RefreshBatchCell(terrain, x, y);
    }

    /// <summary>相邻区块就绪后补齐高度、岸线与四角水深。</summary>
    private void HandleChunkCommitted(ChunkCommitted committed)
    {
        RefreshNeighbourChunkAvailability(committed.Address);
    }

    /// <summary>相邻区块逐出后立即回退边界数据，避免保留已卸载邻格的高度边。</summary>
    private void HandleChunkEvicted(ChunkEvicted evicted)
    {
        RefreshNeighbourChunkAvailability(evicted.Address);
    }

    private void SubscribeNeighbourChunkEvents()
    {
        ClearNeighbourChunkEventSubscriptions();
        if (boundWorld == null || boundChunk?.Terrain == null) return;
        var unique = new HashSet<FlatWorld.WorldModel.WorldAddress>();
        Int2 origin = boundChunk.Address.ChunkOrigin;
        for (int offsetY = -1; offsetY <= 1; offsetY++)
        for (int offsetX = -1; offsetX <= 1; offsetX++)
        {
            if (offsetX == 0 && offsetY == 0) continue;
            Vector2Int canonical = WorldTopologyRuntime.NormalizeCell(new Vector2Int(
                origin.X + offsetX * boundChunk.Terrain.Width,
                origin.Y + offsetY * boundChunk.Terrain.Height));
            var address = new FlatWorld.WorldModel.WorldAddress(boundChunk.Address.DimensionId,
                new Int2(canonical.x, canonical.y));
            if (address == boundChunk.Address || !unique.Add(address)) continue;
            neighbourChunkEventSubscriptions.Add(boundWorld.Events.SubscribeChunkCommitted(
                address, HandleChunkCommitted));
            neighbourChunkEventSubscriptions.Add(boundWorld.Events.SubscribeChunkEvicted(
                address, HandleChunkEvicted));
        }
    }

    private void ClearNeighbourChunkEventSubscriptions()
    {
        for (int i = 0; i < neighbourChunkEventSubscriptions.Count; i++)
            neighbourChunkEventSubscriptions[i].Dispose();
        neighbourChunkEventSubscriptions.Clear();
    }

    /// <summary>复用既有边界脏区处理邻区加载和卸载，不增加逐帧查询。</summary>
    private void RefreshNeighbourChunkAvailability(FlatWorld.WorldModel.WorldAddress address)
    {
        if (boundChunk?.Terrain == null ||
            !string.Equals(address.DimensionId, boundChunk.Address.DimensionId,
                StringComparison.Ordinal))
        {
            return;
        }

        int width = boundChunk.Terrain.Width;
        int height = boundChunk.Terrain.Height;
        Int2 origin = boundChunk.Address.ChunkOrigin;
        Int2 changed = address.ChunkOrigin;
        Vector2Int neighbourDelta = WorldTopologyRuntime.ShortestDelta(
            new Vector2Int(origin.X, origin.Y), new Vector2Int(changed.X, changed.Y));
        int deltaX = neighbourDelta.x;
        int deltaY = neighbourDelta.y;
        bool isNeighbour =
            (deltaX == -width || deltaX == 0 || deltaX == width) &&
            (deltaY == -height || deltaY == 0 || deltaY == height) &&
            (deltaX != 0 || deltaY != 0);
        if (isNeighbour)
        {
            RefreshNeighbourTerrainSubscriptions();
            if (!EnsureBatchPresentationForIncrementalRefresh("NeighbourAvailabilityChanged"))
                return;
            RefreshCommittedBatchEdge(boundChunk.Terrain, deltaX, deltaY);
        }
    }

    /// <summary>订阅八个相邻区块；正交边界负责接触遮罩，对角区块补齐水深纹理边角。</summary>
    private void RefreshNeighbourTerrainSubscriptions()
    {
        ClearNeighbourTerrainSubscriptions();
        if (boundWorld == null || boundChunk?.Terrain == null)
            return;

        ChunkTerrainData terrain = boundChunk.Terrain;
        SubscribeNeighbourTerrain(-terrain.Width, 0);
        SubscribeNeighbourTerrain(terrain.Width, 0);
        SubscribeNeighbourTerrain(0, -terrain.Height);
        SubscribeNeighbourTerrain(0, terrain.Height);
        SubscribeNeighbourTerrain(-terrain.Width, -terrain.Height);
        SubscribeNeighbourTerrain(terrain.Width, -terrain.Height);
        SubscribeNeighbourTerrain(-terrain.Width, terrain.Height);
        SubscribeNeighbourTerrain(terrain.Width, terrain.Height);
    }

    private void SubscribeNeighbourTerrain(int offsetX, int offsetY)
    {
        Int2 origin = boundChunk.Address.ChunkOrigin;
        Vector2Int normalizedOrigin = WorldTopologyRuntime.NormalizeCell(new Vector2Int(origin.X + offsetX, origin.Y + offsetY));
        var address = new FlatWorld.WorldModel.WorldAddress(boundChunk.Address.DimensionId,
            new Int2(normalizedOrigin.x, normalizedOrigin.y));
        if (!boundWorld.TryGetChunkTerrain(address, out ChunkTerrainData neighbourTerrain))
            return;

        neighbourTerrains[(Math.Sign(offsetY) + 1) * 3 + Math.Sign(offsetX) + 1] = neighbourTerrain;
        Action<ChunkTerrainChanged> handler = changed =>
            HandleNeighbourTerrainChanged(neighbourTerrain, offsetX, offsetY, changed);
        neighbourTerrain.Changed += handler;
        Action<ChunkLiquidBatchChanged> liquidHandler = changed => QueueLiquidVisuals(changed, offsetX, offsetY);
        neighbourTerrain.LiquidBatchChanged += liquidHandler;
        neighbourTerrainSubscriptions.Add(
            new NeighbourTerrainSubscription(neighbourTerrain, handler, liquidHandler));
    }

    private void HandleNeighbourTerrainChanged(
        ChunkTerrainData neighbourTerrain,
        int offsetX,
        int offsetY,
        ChunkTerrainChanged changed)
    {
        if (boundChunk?.Terrain == null ||
            (changed.Kind != TerrainChangeKind.Cell &&
             changed.Kind != TerrainChangeKind.TileStack &&
             changed.Kind != TerrainChangeKind.Environment && changed.Kind != TerrainChangeKind.Liquid))
        {
            return;
        }

        bool touchesSharedX = offsetX == 0 || (offsetX < 0
            ? changed.LocalCell.X == neighbourTerrain.Width - 1
            : changed.LocalCell.X == 0);
        bool touchesSharedY = offsetY == 0 || (offsetY < 0
            ? changed.LocalCell.Y == neighbourTerrain.Height - 1
            : changed.LocalCell.Y == 0);
        if (touchesSharedX && touchesSharedY)
        {
            if (!EnsureBatchPresentationForIncrementalRefresh("NeighbourTerrainChanged"))
                return;
            RefreshBatchEdge(boundChunk.Terrain, offsetX, offsetY,
                changed.LocalCell.X, changed.LocalCell.Y);
        }
    }

    private void ClearNeighbourTerrainSubscriptions()
    {
        for (int i = 0; i < neighbourTerrainSubscriptions.Count; i++)
        {
            NeighbourTerrainSubscription subscription = neighbourTerrainSubscriptions[i];
            subscription.Terrain.Changed -= subscription.Handler;
            subscription.Terrain.LiquidBatchChanged -= subscription.LiquidHandler;
        }

        neighbourTerrainSubscriptions.Clear();
        Array.Clear(neighbourTerrains, 0, neighbourTerrains.Length);
    }

    #region BRG 表现

    /// <summary>延后到本帧所有液体 Chunk 写回之后，合并处理岸线、四角液深与高度边。</summary>
    private void HandleLiquidBatchChanged(ChunkLiquidBatchChanged changed) => QueueLiquidVisuals(changed, 0, 0);

    /// <summary>动态流向归零时也必须更新 Shader；本块和八邻区变化仍合并到帧末。</summary>
    private void HandleLiquidFlowChanged(ChunkTerrainData terrain)
    {
        if (boundChunk?.Terrain == null) return;
        bool related = ReferenceEquals(boundChunk.Terrain, terrain);
        for (int i = 0; !related && i < neighbourTerrainSubscriptions.Count; i++)
            related = ReferenceEquals(neighbourTerrainSubscriptions[i].Terrain, terrain);
        if (!related) return;
        // 流向也是四角共享数据；合并后每个水格只刷新一次，不为流向制造权威环境变化。
        for (int i = 0; i < boundChunk.Terrain.CellCount; i++)
            if (boundChunk.Terrain.GetLiquidDepth(i % boundChunk.Terrain.Width, i / boundChunk.Terrain.Width) > 0f)
                liquidVisualDirty.Add(i);
    }

    /// <summary>只登记去重后的可见脏格；借用的批次索引不会保留到下一帧。</summary>
    private void QueueLiquidVisuals(ChunkLiquidBatchChanged changed, int offsetX, int offsetY)
    {
        if (boundChunk?.Terrain == null) return;
        ChunkTerrainData terrain = boundChunk.Terrain;
        foreach (int index in changed.CellIndices.Span)
        {
            int centerX = index % changed.Terrain.Width + offsetX;
            int centerY = index / changed.Terrain.Width + offsetY;
            for (int y = Math.Max(0, centerY - 1); y <= Math.Min(terrain.Height - 1, centerY + 1); y++)
            for (int x = Math.Max(0, centerX - 1); x <= Math.Min(terrain.Width - 1, centerX + 1); x++)
                liquidVisualDirty.Add(y * terrain.Width + x);
        }
    }

    /// <summary>一帧最多刷新一次合并区域；空闲时没有遍历与 BRG 上传。</summary>
    private void LateUpdate()
    {
        if (liquidVisualDirty.Count == 0 || boundChunk?.Terrain == null) return;
        if (EnsureBatchPresentationForIncrementalRefresh("LiquidBatch"))
        {
            ChunkTerrainData terrain = boundChunk.Terrain;
            foreach (int index in liquidVisualDirty)
                RefreshBatchCell(terrain, index % terrain.Width, index / terrain.Width);
        }
        liquidVisualDirty.Clear();
    }

    private void RefreshAllBatchVisuals(ChunkTerrainData terrain)
    {
        ChunkBatchRendererGroupService.BeginBulkSubmit();
        groundMesh?.BeginBulkUpdate();
        waterMesh?.BeginBulkUpdate();
        backMesh?.BeginBulkUpdate();
        blockingMesh?.BeginBulkUpdate();
        try
        {
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
                RefreshBatchCell(terrain, x, y);
        }
        finally
        {
            groundMesh?.EndBulkUpdate();
            waterMesh?.EndBulkUpdate();
            backMesh?.EndBulkUpdate();
            blockingMesh?.EndBulkUpdate();
            ChunkBatchRendererGroupService.EndBulkSubmit();
        }
    }

    private void RefreshBatchArea(ChunkTerrainData terrain, int centerX, int centerY, int radius)
        => RefreshBatchRectangle(terrain, centerX - radius, centerX + radius,
            centerY - radius, centerY + radius);

    private void RefreshBatchRectangle(ChunkTerrainData terrain, int left, int right, int bottom, int top)
    {
        int minX = Mathf.Max(0, left);
        int maxX = Mathf.Min(terrain.Width - 1, right);
        int minY = Mathf.Max(0, bottom);
        int maxY = Mathf.Min(terrain.Height - 1, top);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
            RefreshBatchCell(terrain, x, y);
    }

    /// <summary>邻区变化映射到本区块最近边界格，再刷新一圈依赖格。</summary>
    private void RefreshBatchEdge(ChunkTerrainData terrain, int offsetX, int offsetY,
        int neighbourX, int neighbourY)
    {
        int x = offsetX < 0 ? 0 : offsetX > 0 ? terrain.Width - 1 : Mathf.Clamp(neighbourX, 0, terrain.Width - 1);
        int y = offsetY < 0 ? 0 : offsetY > 0 ? terrain.Height - 1 : Mathf.Clamp(neighbourY, 0, terrain.Height - 1);
        RefreshBatchArea(terrain, x, y, 1);
    }

    /// <summary>邻区首次提交会改变整条共享边界，而不是单一格。</summary>
    private void RefreshCommittedBatchEdge(ChunkTerrainData terrain, int offsetX, int offsetY)
    {
        if (offsetX != 0 && offsetY != 0)
        {
            int cornerX = offsetX < 0 ? 0 : terrain.Width - 1;
            int cornerY = offsetY < 0 ? 0 : terrain.Height - 1;
            RefreshBatchArea(terrain, cornerX, cornerY, 1);
            return;
        }

        if (offsetX != 0)
        {
            int x = offsetX < 0 ? 0 : terrain.Width - 1;
            // 连续边界的一圈依赖格只遍历一次，避免相邻 3x3 区域反复提交相同实例。
            RefreshBatchRectangle(terrain, x - 1, x + 1, 0, terrain.Height - 1);
            return;
        }

        int edgeY = offsetY < 0 ? 0 : terrain.Height - 1;
        RefreshBatchRectangle(terrain, 0, terrain.Width - 1, edgeY - 1, edgeY + 1);
    }

    private void RefreshBatchCell(ChunkTerrainData terrain, int x, int y)
    {
        TerrainCell cell = terrain.GetCell(x, y);

        if (cell.GroundTileId != 0)
        {
            SetBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Ground,
                cell.GroundTileId, groundMaterial,
                BuildBatchGroundContactMask(terrain, x, y, cell), Vector4.zero);
        }
        else
        {
            ClearBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Ground);
        }

        RefreshLiquidVisual(terrain, x, y);

        if (backMaterial != null && cell.BackTileId != 0)
        {
            SetBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Back,
                cell.BackTileId, backMaterial, Vector4.zero, Vector4.zero);
        }
        else
        {
            ClearBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Back);
        }

        if (cell.BlockingTileId != 0)
        {
            SetBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Blocking,
                cell.BlockingTileId, blockingMaterial, Vector4.zero, Vector4.zero);
        }
        else
        {
            ClearBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Blocking);
        }
    }

    /// <summary>水面 Mesh 从液体目录取外观，不把 GroundTileId 当作水面贴图。</summary>
    private void RefreshLiquidVisual(ChunkTerrainData terrain, int x, int y)
    {
        if (terrain.GetLiquidDepth(x, y) <= 0f)
        {
            ClearBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Water);
            return;
        }
        WorldLiquidSettings settings = ResolveLiquidVisual(terrain, x, y);
        if (settings?.Sprite == null || settings.Material == null)
            throw new System.InvalidOperationException($"液体外观尚未就绪：{terrain.GetLiquidId(x, y)}");
        Material material = settings.FollowWaterVisualStyle ? GetActiveWaterMaterial() : settings.Material;
        if (material == null) material = settings.Material;
        var data = ChunkBatchRendererGroupService.InstanceData.Create(Matrix4x4.identity,
            BuildWaterShoreMask(terrain, x, y), BuildLiquidDepthCorners(terrain, x, y), Color.white);
        SetWaterCurrentData(terrain, x, y, ref data);
        if (waterMesh == null || !waterMesh.TrySetWaterCell(x, y, settings.Sprite, material, data))
            throw new InvalidOperationException($"水面 Chunk Mesh 无法提交：{settings.Sprite.name}");
    }

    private static WorldLiquidSettings ResolveLiquidVisual(ChunkTerrainData terrain, int x, int y)
    {
        string id = terrain.LiquidTypes.GetId(terrain.GetLiquidTypeIndex(x, y));
        return GameRes.ExistingInstance != null && GameRes.ExistingInstance.TryGetLiquidDefinition(id, out var definition)
            ? definition.WorldWater : null;
    }

    private void SetBatchVisual(ChunkTerrainData terrain, int x, int y,
        ChunkBatchRendererGroupService.VisualLayer layer, int tileId,
        Material sourceMaterial, Vector4 data0, Vector4 data1)
    {
        if (sourceMaterial == null)
        {
            WarnMissingVisualOnce(layer, tileId, "sourceMaterial=null", x, y);
            ClearBatchVisual(terrain, x, y, layer);
            return;
        }
        if (palette == null)
        {
            WarnMissingVisualOnce(layer, tileId, "palette=null", x, y);
            ClearBatchVisual(terrain, x, y, layer);
            return;
        }
        if (!palette.TryGetVisual(tileId, out Sprite sprite, out Color tileColor, out Matrix4x4 tileTransform) ||
            sprite == null)
        {
            WarnMissingVisualOnce(layer, tileId, "palette.TryGetVisual=false", x, y);
            ClearBatchVisual(terrain, x, y, layer);
            return;
        }

        ChunkGroundMeshRenderer mesh = layer switch
        {
            ChunkBatchRendererGroupService.VisualLayer.Ground => groundMesh,
            ChunkBatchRendererGroupService.VisualLayer.Back => backMesh,
            ChunkBatchRendererGroupService.VisualLayer.Blocking => blockingMesh,
            _ => null
        };
        if (mesh == null)
            throw new InvalidOperationException($"基础地形 {layer} 的 Chunk Mesh 未创建。");
        float height = -1f;
        Vector4 neighbours = Vector4.zero;
        if (layer == ChunkBatchRendererGroupService.VisualLayer.Ground)
        {
            var elevation = default(ChunkBatchRendererGroupService.InstanceData);
            SetGroundElevationData(terrain, x, y, ref elevation);
            height = elevation.Transform0.w;
            neighbours = elevation.Data1;
        }
        if (!mesh.TrySetCell(x, y, sprite, tileColor, tileTransform,
                data0, height, neighbours))
            throw new InvalidOperationException($"基础地形 {layer} 不能提交非四角 Sprite：{sprite.name}");
    }

    /// <summary>向地形 Owner 提交无地块高度语义的扩展表现，供草花等区块图层共享裁剪与批次。</summary>
    internal void SetLayerVisual(ChunkBatchRendererGroupService.VisualLayer layer, int x, int y,
        Sprite sprite, Material sourceMaterial, Matrix4x4 localToWorld, Color tint)
        => SetLayerVisual(layer, x, y, sprite, sourceMaterial, localToWorld, tint,
            Vector4.zero, Vector4.zero);

    /// <summary>带实例动画参数的区块表现入口；共享材质无需逐节点复制。</summary>
    internal void SetLayerVisual(ChunkBatchRendererGroupService.VisualLayer layer, int x, int y,
        Sprite sprite, Material sourceMaterial, Matrix4x4 localToWorld, Color tint,
        Vector4 animation, Vector4 textureRegion)
        => SetLayerVisual(layer, x, y, sprite, sourceMaterial, localToWorld, tint,
            animation, textureRegion, null);

    /// <summary>带透明深度排序锚点的扩展表现；用于需要按世界 Y 轴动态排序的 BRG 实例。</summary>
    internal void SetLayerVisual(ChunkBatchRendererGroupService.VisualLayer layer, int x, int y,
        Sprite sprite, Material sourceMaterial, Matrix4x4 localToWorld, Color tint,
        Vector4 animation, Vector4 textureRegion, Vector3? sortingPosition)
    {
        ChunkTerrainData terrain = boundChunk?.Terrain;
        if (terrain == null)
            throw new InvalidOperationException("区块扩展表现提交前必须先绑定基础地形。");

        var instanceData = ChunkBatchRendererGroupService.InstanceData.Create(
            localToWorld, animation, textureRegion, tint);
        instanceData.Transform0.w = -1f;
        var visual = sortingPosition.HasValue
            ? new ChunkBatchRendererGroupService.Visual(layer, sprite, sourceMaterial, instanceData,
                sortingPosition.Value)
            : new ChunkBatchRendererGroupService.Visual(layer, sprite, sourceMaterial, instanceData);
        ChunkBatchRendererGroupService.SetVisual(this, GetBatchSlotKey(terrain, x, y, layer),
            visual);
    }

    /// <summary>清除指定扩展图层的单格实例。</summary>
    internal void ClearLayerVisual(ChunkBatchRendererGroupService.VisualLayer layer, int x, int y)
    {
        ChunkTerrainData terrain = boundChunk?.Terrain;
        if (terrain != null)
            ChunkBatchRendererGroupService.ClearVisual(this, GetBatchSlotKey(terrain, x, y, layer));
    }

    private readonly Dictionary<(int Entity, int Part), int> naturalVisualSlots = new();
    private readonly Stack<int> freeNaturalVisualSlots = new();
    private int nextNaturalVisualSlot;
    private Bounds resourceVisualBounds;
    private bool hasResourceVisualBounds;

    /// <summary>资源可超出格子和环世界原像，裁剪边界覆盖实际主体与太阳投影。</summary>
    internal void IncludeNaturalEntityBounds(Bounds logicalBounds, Vector3 projectionOffset)
    {
        if (boundChunk?.Terrain == null || !IsBatchPresentationRegistered) return;
        if (!hasResourceVisualBounds) { resourceVisualBounds = logicalBounds; hasResourceVisualBounds = true; }
        else resourceVisualBounds.Encapsulate(logicalBounds);
        Bounds local = resourceVisualBounds;
        local.center += projectionOffset;
        local.Expand(WorldRenderingConfigCatalog.Default.shadows.maximumDistance * 2f);
        Bounds bounds = GetBatchWorldBounds(boundChunk.Terrain);
        bounds.Encapsulate(local);
        ChunkBatchRendererGroupService.RegisterOwner(this, bounds);
    }

    /// <summary>实体与部件共同标识负槽位；树冠果实、阴影和同格植物不会互相覆盖。</summary>
    internal void SetNaturalEntityVisual(int guid, Sprite sprite, Material material,
        Matrix4x4 localToWorld, Color tint, Vector3 sortingPosition)
        => SetNaturalEntityPart(guid, 0, sprite, material, localToWorld, tint, sortingPosition,
            Vector4.zero, Vector4.zero, Vector4.zero);

    internal void SetNaturalEntityPart(int entityId, int part, Sprite sprite, Material material,
        Matrix4x4 localToWorld, Color tint, Vector3 sortingPosition,
        Vector4 crop, Vector4 uvRegion, Vector4 shadow, bool sunShadow = false,
        bool playerOccluder = false, bool highlighted = false)
    {
        if (entityId <= 0 || part < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
        if (boundChunk?.Terrain == null) throw new InvalidOperationException("自然物提交前必须绑定区块。");
        if (!sunShadow)
        {
            DepthMesh.Set(ChunkDepthMeshRenderer.NaturalDomain, entityId, part, sprite, material,
                localToWorld, sortingPosition, part, tint, crop, occluder: playerOccluder, highlighted: highlighted);
            return;
        }
        var key = (entityId, part);
        if (!naturalVisualSlots.TryGetValue(key, out int slot))
        {
            if (nextNaturalVisualSlot == int.MinValue && freeNaturalVisualSlots.Count == 0)
                throw new InvalidOperationException("区块资源表现槽位耗尽。");
            slot = freeNaturalVisualSlots.Count > 0 ? freeNaturalVisualSlots.Pop() : --nextNaturalVisualSlot;
            naturalVisualSlots.Add(key, slot);
        }
        var data = ChunkBatchRendererGroupService.InstanceData.Create(localToWorld, crop, uvRegion, tint);
        data.Transform0.w = -1f;
        data.FlowX = shadow;
        ChunkBatchRendererGroupService.SetVisual(this, slot,
            new ChunkBatchRendererGroupService.Visual(sunShadow
                    ? ChunkBatchRendererGroupService.VisualLayer.NaturalShadow
                    : ChunkBatchRendererGroupService.VisualLayer.NaturalStatic,
                sprite, material, data, sortingPosition));
    }

    internal void ClearNaturalEntityVisual(int guid) => ClearNaturalEntityPart(guid, 0);

    internal void ClearNaturalEntityPart(int entityId, int part)
    {
        depthMesh?.Remove(ChunkDepthMeshRenderer.NaturalDomain, entityId, part);
        if (!naturalVisualSlots.Remove((entityId, part), out int slot)) return;
        ChunkBatchRendererGroupService.ClearVisual(this, slot);
        freeNaturalVisualSlots.Push(slot);
    }

    private void ClearBatchVisual(ChunkTerrainData terrain, int x, int y,
        ChunkBatchRendererGroupService.VisualLayer layer)
    {
        switch (layer)
        {
            case ChunkBatchRendererGroupService.VisualLayer.Ground: groundMesh?.ClearCell(x, y); break;
            case ChunkBatchRendererGroupService.VisualLayer.Water: waterMesh?.ClearCell(x, y); break;
            case ChunkBatchRendererGroupService.VisualLayer.Back: backMesh?.ClearCell(x, y); break;
            case ChunkBatchRendererGroupService.VisualLayer.Blocking: blockingMesh?.ClearCell(x, y); break;
        }
    }

    private static int GetBatchSlotKey(ChunkTerrainData terrain, int x, int y,
        ChunkBatchRendererGroupService.VisualLayer layer)
    {
        return (int)layer * terrain.CellCount + y * terrain.Width + x;
    }

    /// <summary>给 BRG 提供整块地形的世界边界，留三格余量容纳机械高塔和地块 Sprite 外扩。</summary>
    private Bounds GetBatchWorldBounds(ChunkTerrainData terrain)
    {
        Int2 origin = boundChunk.Address.ChunkOrigin;
        return new Bounds(
            new Vector3(origin.X + terrain.Width * 0.5f, origin.Y + terrain.Height * 0.5f, 0f),
            new Vector3(terrain.Width + 6f, terrain.Height + 6f, 4f));
    }

    private Material GetActiveWaterMaterial()
    {
        return WaterVisualSettings.Style == WaterVisualStyle.Stylized
            ? stylizedWaterMaterial : realisticWaterMaterial;
    }

    #region BRG DEBUG

    /// <summary>增量刷新前验证后端登记；登记丢失或实例不完整时先从权威 Terrain 全量自愈。</summary>
    private bool EnsureBatchPresentationForIncrementalRefresh(string reason)
    {
        if (boundChunk?.Terrain == null)
            return false;

        if (batchPresentationComplete &&
            ChunkBatchRendererGroupService.IsOwnerRegistered(this))
        {
            return true;
        }
        return RepairBatchPresentationIfNeeded(reason);
    }

    /// <summary>统计当前权威地块中理论上能够提交到 BRG 的实例数。</summary>
    private int CountSubmittableVisuals(ChunkTerrainData terrain)
    {
        if (terrain == null || palette == null)
            return 0;

        int count = 0;
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
        {
            TerrainCell cell = terrain.GetCell(x, y);
            if (cell.GroundTileId != 0 && groundMaterial != null && HasPaletteVisual(cell.GroundTileId)) count++;
            if (terrain.GetLiquidDepth(x, y) > 0f && ResolveLiquidVisual(terrain, x, y)?.Sprite != null) count++;

            if (backMaterial != null && cell.BackTileId != 0 &&
                HasPaletteVisual(cell.BackTileId))
            {
                count++;
            }

            if (cell.BlockingTileId != 0 &&
                blockingMaterial != null && HasPaletteVisual(cell.BlockingTileId))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>只做静态外观映射检查，不创建任何运行时表现。</summary>
    private bool HasPaletteVisual(int tileId)
    {
        return palette != null &&
               palette.TryGetVisual(tileId, out Sprite sprite, out _, out _) &&
               sprite != null;
    }

    /// <summary>把权威地块数量、可提交数量与后端实际实例数放到同一条日志中。</summary>
    private void ValidateBatchPresentation(string reason, ChunkTerrainData terrain, bool forceLog = false)
    {
        if (!RenderDebugEnabled || terrain == null)
            return;

        int dataVisuals = CountDataVisuals(terrain);
        int submittable = CountSubmittableVisuals(terrain);
        int submitted = ChunkBatchRendererGroupService.GetOwnerTerrainVisualCount(this) +
                        (groundMesh?.ActiveCellCount ?? 0);
        string message = $"[ChunkRenderDebug] {reason} chunk={GetDebugChunkLabel()} " +
                         $"dataVisuals={dataVisuals} submittable={submittable} submitted={submitted}. " +
                         ChunkBatchRendererGroupService.BuildDebugSummary(this);
        if (submitted != submittable || submittable != dataVisuals)
            Debug.LogWarning(message);
        else if (forceLog)
            Debug.Log(message);
    }

    /// <summary>统计 Terrain 中非零的基础视觉槽，独立于材质和 Palette 是否有效。</summary>
    private int CountDataVisuals(ChunkTerrainData terrain)
    {
        int count = 0;
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
        {
            TerrainCell cell = terrain.GetCell(x, y);
            if (cell.GroundTileId != 0)
                count++;
            if (cell.BackTileId != 0)
                count++;
            if (cell.BlockingTileId != 0)
                count++;
        }
        return count;
    }

    /// <summary>同一类缺失只报警一次，避免一个坏 Tile 把运行时日志刷爆。</summary>
    private void WarnMissingVisualOnce(ChunkBatchRendererGroupService.VisualLayer layer,
        int tileId, string reason, int x, int y)
    {
        if (!RenderDebugEnabled)
            return;

        string key = $"{layer}:{tileId}:{reason}";
        if (!renderDebugMissingVisualKeys.Add(key))
            return;
        Debug.LogWarning($"[ChunkRenderDebug] VisualRejected chunk={GetDebugChunkLabel()} cell=({x},{y}) " +
                         $"layer={layer} tileId={tileId} reason={reason}");
    }

    /// <summary>供 Inspector 右键菜单手动打印当前 Chunk 的 BRG 对账快照。</summary>
    [ContextMenu("DEBUG/打印当前地块 BRG 状态")]
    public void LogBatchRenderDebugState()
    {
        if (boundChunk?.Terrain == null)
        {
            Debug.LogWarning($"[ChunkRenderDebug] {name} 当前没有绑定 Chunk/Terrain。");
            return;
        }
        ValidateBatchPresentation("ManualDump", boundChunk.Terrain, true);
    }

    /// <summary>生成便于搜索的稳定 Chunk 标签。</summary>
    private string GetDebugChunkLabel()
    {
        if (boundChunk == null)
            return "<unbound>";
        Int2 origin = boundChunk.Address.ChunkOrigin;
        return $"{boundChunk.Address.DimensionId}@({origin.X},{origin.Y})";
    }

    #endregion

    private Vector4 BuildBatchGroundContactMask(ChunkTerrainData terrain, int x, int y, TerrainCell cell)
    {
        bool cave = IsCaveDimension(boundChunk?.Address.DimensionId);
        if (cave)
        {
            bool receivesShadow = !IsBlocking(cell) && terrain.GetLiquidDepth(x, y) <= 0f;
            return receivesShadow ? (Vector4)BuildContactMask(terrain, x, y, ContactKind.Wall) : Vector4.zero;
        }

        if (terrain.GetLiquidDepth(x, y) > 0f)
            return (Vector4)BuildContactMask(terrain, x, y, ContactKind.Land);
        return !IsStone(cell)
            ? (Vector4)BuildContactMask(terrain, x, y, ContactKind.Stone)
            : Vector4.zero;
    }

    /// <summary>RGBA 依次保存左下、右下、左上、右上格角的连续水深。</summary>
    private Vector4 BuildLiquidDepthCorners(ChunkTerrainData terrain, int x, int y)
    {
        return new Vector4(
            ResolveCornerDepth(terrain, x - 1, y - 1),
            ResolveCornerDepth(terrain, x, y - 1),
            ResolveCornerDepth(terrain, x - 1, y),
            ResolveCornerDepth(terrain, x, y));
    }

    private float ResolveCornerDepth(ChunkTerrainData terrain, int leftCellX, int bottomCellY)
    {
        return (
            ResolveExtendedLiquidDepth(terrain, leftCellX, bottomCellY) +
            ResolveExtendedLiquidDepth(terrain, leftCellX + 1, bottomCellY) +
            ResolveExtendedLiquidDepth(terrain, leftCellX, bottomCellY + 1) +
            ResolveExtendedLiquidDepth(terrain, leftCellX + 1, bottomCellY + 1)) * 0.25f;
    }

    #endregion

    #endregion

    #region BRG Shader 数据

    #region 地表高度分层

    /// <summary>当前高度放 Transform0.w，左、右、下、上邻高放 Data1，保持实例布局与 Contact 数据不变。</summary>
    private void SetGroundElevationData(ChunkTerrainData terrain, int x, int y,
        ref ChunkBatchRendererGroupService.InstanceData instanceData)
    {
        instanceData.Transform0.w = -1f;
        instanceData.Data1 = Vector4.zero;
        if (!renderGroundElevation || !TryGetGroundHeight(terrain, x, y, out float height))
            return;

        instanceData.Transform0.w = height;
        instanceData.Data1 = new Vector4(
            ResolveNeighbourGroundHeight(terrain, x - 1, y, height),
            ResolveNeighbourGroundHeight(terrain, x + 1, y, height),
            ResolveNeighbourGroundHeight(terrain, x, y - 1, height),
            ResolveNeighbourGroundHeight(terrain, x, y + 1, height));
    }

    /// <summary>缺失邻区、无地面、无合法高度或有液体时回退到当前高度，避免假悬崖与重复水岸边。</summary>
    private float ResolveNeighbourGroundHeight(ChunkTerrainData terrain, int x, int y, float currentHeight)
    {
        return TryResolveTerrainCell(terrain, x, y, out ChunkTerrainData neighbour,
                   out int neighbourX, out int neighbourY, out _) &&
               TryGetGroundHeight(neighbour, neighbourX, neighbourY, out float height)
            ? height : currentHeight;
    }

    /// <summary>只读取真实干燥 Ground 的有限 0～1 高度；不按 Tile ID 或旧 Water 标记推断水格。</summary>
    private static bool TryGetGroundHeight(ChunkTerrainData terrain, int x, int y, out float height)
    {
        height = -1f;
        return terrain.GetCell(x, y).GroundTileId != 0 && terrain.GetLiquidDepth(x, y) <= 0f &&
               terrain.TryGetEnvironmentValue("height", x, y, out height) &&
               !float.IsNaN(height) && !float.IsInfinity(height) && height >= 0f && height <= 1f;
    }

    /// <summary>通过维度目录支持 MOD 的 Surface 维度；未知维度只有正式 surface 标识可启用。</summary>
    private static bool IsSurfaceDimension(string dimensionId)
    {
        if (DimensionManager.Instance != null &&
            DimensionManager.Instance.TryGetDefinition(dimensionId, out DimensionDefinition definition))
            return definition.GenerationMode == DimensionGenerationMode.Surface;

        return string.Equals(dimensionId, WorldAddress.SurfaceDimensionId, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    /// <summary>共享格角插值维持弯道与 Chunk 接缝连续；只上传实例数据，不创建水格对象。</summary>
    private void SetWaterCurrentData(ChunkTerrainData terrain, int x, int y,
        ref ChunkBatchRendererGroupService.InstanceData instanceData)
    {
        terrain.TryGetEnvironmentValue("riverKind", x, y, out float riverKind);
        RuntimeWaterCurrentKind kind = WaterEnvironmentRules.ResolveCurrentKind(
            Mathf.RoundToInt(riverKind), (SurfaceBiomeKind)terrain.GetCell(x, y).BiomeId == SurfaceBiomeKind.Ocean);
        instanceData.Transform0.w = (float)kind;
        instanceData.FlowX = Vector4.zero;
        instanceData.FlowY = Vector4.zero;
        // 湖泊仅在河口读取邻河的表面扰动，绝不写回权威水流或推动漂浮物。
        if (kind == RuntimeWaterCurrentKind.Ocean)
        {
            Vector2 worldCell = new(boundChunk.Address.ChunkOrigin.X + x, boundChunk.Address.ChunkOrigin.Y + y);
            if (ChunkMgr.ExistingInstance == null || !ChunkMgr.ExistingInstance.TryGetExperimentalLiquidFlow(worldCell, out _))
            {
                terrain.TryGetEnvironmentValue("windX", x, y, out float windX);
                terrain.TryGetEnvironmentValue("windY", x, y, out float windY);
                Vector2 direction = WaterEnvironmentRules.ResolveOceanCurrentDirection(new Vector2(windX, windY));
                instanceData.FlowX = new Vector4(direction.x, direction.x, direction.x, direction.x);
                instanceData.FlowY = new Vector4(direction.y, direction.y, direction.y, direction.y);
                return;
            }
            instanceData.Transform0.w = (float)RuntimeWaterCurrentKind.River;
        }

        Vector2 bottomLeft = ResolveCornerCurrent(terrain, x - 1, y - 1);
        Vector2 bottomRight = ResolveCornerCurrent(terrain, x, y - 1);
        Vector2 topLeft = ResolveCornerCurrent(terrain, x - 1, y);
        Vector2 topRight = ResolveCornerCurrent(terrain, x, y);
        instanceData.FlowX = new Vector4(bottomLeft.x, bottomRight.x, topLeft.x, topRight.x);
        instanceData.FlowY = new Vector4(bottomLeft.y, bottomRight.y, topLeft.y, topRight.y);
    }

    /// <summary>每个共享角只平均相邻河流格，陆地、湖泊与海洋不能稀释或反转下游。</summary>
    private Vector2 ResolveCornerCurrent(ChunkTerrainData terrain, int left, int bottom)
    {
        Vector2 velocity = Vector2.zero;
        int count = 0;
        for (int offsetY = 0; offsetY <= 1; offsetY++)
        {
            for (int offsetX = 0; offsetX <= 1; offsetX++)
            {
                if (!TryResolveTerrainCell(terrain, left + offsetX, bottom + offsetY,
                        out ChunkTerrainData source, out int localX, out int localY,
                        out TerrainCell cell) || source.GetLiquidDepth(localX, localY) <= 0f)
                    continue;
                Vector2 worldCell = new(boundChunk.Address.ChunkOrigin.X + left + offsetX,
                    boundChunk.Address.ChunkOrigin.Y + bottom + offsetY);
                if (ChunkMgr.ExistingInstance != null &&
                    ChunkMgr.ExistingInstance.TryGetExperimentalLiquidFlow(worldCell, out Vector2 liquidFlow))
                {
                    velocity += Vector2.ClampMagnitude(liquidFlow, 1f);
                    count++;
                    continue;
                }
                source.TryGetEnvironmentValue("riverKind", localX, localY, out float kind);
                if (Mathf.RoundToInt(kind) != 1)
                    continue;
                source.TryGetEnvironmentValue("riverFlowX", localX, localY, out float flowX);
                source.TryGetEnvironmentValue("riverFlowY", localX, localY, out float flowY);
                source.TryGetEnvironmentValue("riverFlow", localX, localY, out float flow);
                velocity += new Vector2(flowX, flowY).normalized *
                    WorldItemWaterRules.ResolveDriftSpeed(RuntimeWaterCurrentKind.River, flow);
                count++;
            }
        }
        return count > 0 ? velocity / count : Vector2.zero;
    }

    /// <summary>RGBA 分别记录左、右、下、上岸线方向。</summary>
    private Color BuildWaterShoreMask(ChunkTerrainData terrain, int x, int y)
    {
        return new Color(
            IsContactNeighbour(terrain, x - 1, y, ContactKind.Land) ? 1f : 0f,
            IsContactNeighbour(terrain, x + 1, y, ContactKind.Land) ? 1f : 0f,
            IsContactNeighbour(terrain, x, y - 1, ContactKind.Land) ? 1f : 0f,
            IsContactNeighbour(terrain, x, y + 1, ContactKind.Land) ? 1f : 0f);
    }

    /// <summary>非水格沿用周围水格平均深度，避免岸线参与颜色渐变。</summary>
    private float ResolveExtendedLiquidDepth(ChunkTerrainData terrain, int x, int y)
    {
        if (TryResolveLiquidDepth(terrain, x, y, out float depth))
            return depth;

        // 邻区边框落在陆地时，先向当前 Chunk 边缘延展，避免取样范围越过第二圈邻格。
        int sampleX = Mathf.Clamp(x, 0, terrain.Width - 1);
        int sampleY = Mathf.Clamp(y, 0, terrain.Height - 1);
        if ((sampleX != x || sampleY != y) &&
            TryResolveLiquidDepth(terrain, sampleX, sampleY, out depth))
        {
            return depth;
        }

        float depthTotal = 0f;
        int waterCount = 0;
        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (!TryResolveLiquidDepth(terrain, sampleX + offsetX, sampleY + offsetY,
                        out float neighbourDepth))
                {
                    continue;
                }

                depthTotal += neighbourDepth;
                waterCount++;
            }
        }

        return waterCount > 0 ? depthTotal / waterCount : 0f;
    }

    /// <summary>读取当前格或邻区水格的权威深度。</summary>
    private bool TryResolveLiquidDepth(ChunkTerrainData terrain, int x, int y, out float depth)
    {
        depth = 0f;
        if (!TryResolveTerrainCell(terrain, x, y,
                out ChunkTerrainData resolvedTerrain, out int localX, out int localY,
                out TerrainCell cell) || resolvedTerrain.GetLiquidDepth(localX, localY) <= 0f)
        {
            return false;
        }

        depth = ResolveLiquidDepth(resolvedTerrain, localX, localY);
        return true;
    }

    /// <summary>所有水域统一读取已生成或被玩家修改的权威液深。</summary>
    private static float ResolveLiquidDepth(ChunkTerrainData terrain, int x, int y)
    {
        return terrain.GetLiquidDepth(x, y);
    }

    /// <summary>RGBA 分别表示左、右、下、上是否需要绘制接触阴影。</summary>
    private Color BuildContactMask(ChunkTerrainData terrain, int x, int y, ContactKind kind)
    {
        return new Color(
            IsContactNeighbour(terrain, x - 1, y, kind) ? 1f : 0f,
            IsContactNeighbour(terrain, x + 1, y, kind) ? 1f : 0f,
            IsContactNeighbour(terrain, x, y - 1, kind) ? 1f : 0f,
            IsContactNeighbour(terrain, x, y + 1, kind) ? 1f : 0f);
    }

    private bool IsContactNeighbour(ChunkTerrainData terrain, int x, int y, ContactKind kind)
    {
        if (!TryResolveTerrainCell(terrain, x, y, out ChunkTerrainData neighbourTerrain,
                out int neighbourX, out int neighbourY, out TerrainCell neighbour))
            return false;
        return kind switch
        {
            ContactKind.Wall => IsBlocking(neighbour),
            ContactKind.Land => neighbourTerrain.GetLiquidDepth(neighbourX, neighbourY) <= 0f,
            ContactKind.Stone => IsStone(neighbour),
            _ => false
        };
    }

    /// <summary>读取本区块或已就绪的八方向相邻区块格子。</summary>
    private bool TryGetCell(ChunkTerrainData terrain, int x, int y, out TerrainCell cell)
    {
        return TryResolveTerrainCell(terrain, x, y, out _, out _, out _, out cell);
    }

    /// <summary>把越界坐标解析到相邻 Chunk，并返回实际地形与局部坐标。</summary>
    private bool TryResolveTerrainCell(ChunkTerrainData terrain, int x, int y,
        out ChunkTerrainData resolvedTerrain, out int resolvedX, out int resolvedY,
        out TerrainCell cell)
    {
        resolvedTerrain = null;
        resolvedX = 0;
        resolvedY = 0;
        cell = default;
        if (x >= 0 && x < terrain.Width && y >= 0 && y < terrain.Height)
        {
            resolvedTerrain = terrain;
            resolvedX = x;
            resolvedY = y;
            cell = terrain.GetCell(x, y);
            return true;
        }

        if (boundWorld == null || boundChunk == null)
            return false;

        int neighbourX = x < 0 ? -1 : x >= terrain.Width ? 1 : 0;
        int neighbourY = y < 0 ? -1 : y >= terrain.Height ? 1 : 0;
        ChunkTerrainData neighbourTerrain = neighbourTerrains[(neighbourY + 1) * 3 + neighbourX + 1];
        if (neighbourTerrain == null || neighbourTerrain.IsDisposed)
            return false;

        int localX = x;
        int localY = y;
        if (x < 0)
            localX += neighbourTerrain.Width;
        else if (x >= terrain.Width)
            localX -= terrain.Width;
        if (y < 0)
            localY += neighbourTerrain.Height;
        else if (y >= terrain.Height)
            localY -= terrain.Height;
        if (localX < 0 || localX >= neighbourTerrain.Width ||
            localY < 0 || localY >= neighbourTerrain.Height)
        {
            return false;
        }

        resolvedTerrain = neighbourTerrain;
        resolvedX = localX;
        resolvedY = localY;
        cell = resolvedTerrain.GetCell(resolvedX, resolvedY);
        return true;
    }

    private static bool IsCaveDimension(string dimensionId)
    {
        if (DimensionManager.Instance != null &&
            DimensionManager.Instance.TryGetDefinition(dimensionId,
                out DimensionDefinition definition))
        {
            return definition.GenerationMode == DimensionGenerationMode.Cave;
        }

        return string.Equals(dimensionId, "cave", StringComparison.OrdinalIgnoreCase);
    }


    private static bool IsBlocking(TerrainCell cell) =>
        cell.BlockingTileId != 0 && (cell.Flags & TerrainCellFlags.Blocking) != 0;

    /// <summary>通过稳定群系编号识别地表石地，避免依赖可变的 TileId 配置。</summary>
    private static bool IsStone(TerrainCell cell) =>
        cell.BiomeId == (int)SurfaceBiomeKind.Stone;

    private enum ContactKind
    {
        Wall,
        Land,
        Stone
    }

    private readonly struct NeighbourTerrainSubscription
    {
        public NeighbourTerrainSubscription(
            ChunkTerrainData terrain,
            Action<ChunkTerrainChanged> handler,
            Action<ChunkLiquidBatchChanged> liquidHandler)
        {
            Terrain = terrain;
            Handler = handler;
            LiquidHandler = liquidHandler;
        }

        public ChunkTerrainData Terrain { get; }
        public Action<ChunkTerrainChanged> Handler { get; }
        public Action<ChunkLiquidBatchChanged> LiquidHandler { get; }
    }

    #endregion
}
