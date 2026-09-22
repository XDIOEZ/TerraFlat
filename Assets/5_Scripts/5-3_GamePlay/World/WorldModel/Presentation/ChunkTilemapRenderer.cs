using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 新版区块的基础 BRG 地图表现适配器。
///
/// Ground / Water / Back / Blocking 的视觉由共享 BatchRendererGroup 绘制，格子变化只上传脏格实例。
/// TilemapRenderer 保留为 Prefab 兼容和材质配置来源，其中 Blocking Tilemap 继续专供 TilemapCollider2D。
/// 岸线方向与连续水深改为每实例数据，不再为每个 Chunk 创建水深纹理或整块 SetTilesBlock。
/// Surface Ground 复用 112 字节实例中的 Transform0.w / Data1 传递当前高度与四邻高度，负值关闭分层。
/// </summary>
public sealed class ChunkTilemapRenderer : MonoBehaviour, IChunkViewRenderer, IWorldAwareChunkViewRenderer
{
    #region 配置与状态

    [SerializeField] private ChunkTilePaletteSO palette;
    [SerializeField] private Tilemap groundTilemap;
    [SerializeField] private Tilemap waterTilemap;
    [SerializeField] private Tilemap caveWaterTilemap;
    [SerializeField] private Tilemap backTilemap;
    [SerializeField] private Tilemap blockingTilemap;

    private readonly List<NeighbourTerrainSubscription> neighbourTerrainSubscriptions = new(8);
    private WorldRuntime boundWorld;
    private ChunkRuntime boundChunk;
    private IDisposable chunkCommittedSubscription;
    private IDisposable chunkEvictedSubscription; // 邻区卸载后恢复边界高度回退值。
    private bool renderCaveWater;
    private bool renderGroundElevation; // 只有使用 Surface 生成语义的维度显示高度。
    private bool batchPresentationComplete;
    private bool batchBindingInProgress;

    /// <summary>Editor 与 Development Build 只记录 BRG 异常与手动诊断，正式非开发包保持静默。</summary>
    private static bool RenderDebugEnabled => Application.isEditor || Debug.isDebugBuild;
    private static readonly HashSet<string> renderDebugMissingVisualKeys = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRenderDebugState() => renderDebugMissingVisualKeys.Clear();

    /// <summary>阻挡层渲染器，为裂缝等格子表现提供一致的材质与排序基准。</summary>
    public TilemapRenderer BlockingTilemapRenderer =>
        blockingTilemap != null ? blockingTilemap.GetComponent<TilemapRenderer>() : null;

    /// <summary>当前区块的基础地形是否仍登记在全局 BRG 后端。</summary>
    public bool IsBatchPresentationRegistered =>
        boundChunk?.Terrain != null && ChunkBatchRendererGroupService.IsOwnerRegistered(this);

    #endregion

    #region 绑定与生命周期

    /// <summary>注入当前世界，用于计算跨 Chunk 的接触方向与连续水深边界。</summary>
    public void SetWorld(WorldRuntime worldRuntime)
    {
        if (ReferenceEquals(boundWorld, worldRuntime))
            return;

        chunkCommittedSubscription?.Dispose();
        chunkCommittedSubscription = null;
        chunkEvictedSubscription?.Dispose();
        chunkEvictedSubscription = null;
        boundWorld = worldRuntime;
        if (boundWorld != null)
        {
            chunkCommittedSubscription =
                boundWorld.Events.Subscribe<ChunkCommitted>(HandleChunkCommitted);
            chunkEvictedSubscription =
                boundWorld.Events.Subscribe<ChunkEvicted>(HandleChunkEvicted);
        }
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
            renderCaveWater = IsCaveDimension(chunk.Address.DimensionId);
            renderGroundElevation = IsSurfaceDimension(chunk.Address.DimensionId);
            // SetActive 会同步触发 WaterVisualStyleBinding.OnEnable。该回调只负责先把共享材质切到最新风格；
            // Bind 随后的全量 BRG 提交会直接读取这个材质，因此绑定期无需再走一次增量修复。
            if (waterTilemap != null)
                waterTilemap.gameObject.SetActive(!renderCaveWater);
            if (caveWaterTilemap != null)
                caveWaterTilemap.gameObject.SetActive(renderCaveWater);
            boundChunk.Terrain.Changed += HandleTerrainChanged;
            RefreshNeighbourTerrainSubscriptions();
            DisableVisualTilemapRenderers();
            ClearVisualTilemaps();
            batchPresentationComplete = false;
            ChunkBatchRendererGroupService.RegisterOwner(this);
            SyncBlockingCollisionAll(chunk.Terrain);
            RefreshAllBatchVisuals(chunk.Terrain);
            batchPresentationComplete = true;
        }
        finally
        {
            batchBindingInProgress = false;
        }
    }

    public void Unbind()
    {
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
        ClearNeighbourTerrainSubscriptions();
        batchBindingInProgress = false;
        batchPresentationComplete = false;
        ChunkBatchRendererGroupService.UnregisterOwner(this);
        if (groundTilemap != null)
            groundTilemap.ClearAllTiles();
        if (waterTilemap != null)
        {
            waterTilemap.ClearAllTiles();
            waterTilemap.gameObject.SetActive(false);
        }
        if (caveWaterTilemap != null)
        {
            caveWaterTilemap.ClearAllTiles();
            caveWaterTilemap.gameObject.SetActive(false);
        }
        if (backTilemap != null)
            backTilemap.ClearAllTiles();
        if (blockingTilemap != null)
            blockingTilemap.ClearAllTiles();
        renderCaveWater = false;
        renderGroundElevation = false;
        boundChunk = null;
    }

    /// <summary>
    /// BRG 后端因脚本热重载或异常生命周期被重建时，使用现有权威地形重新登记基础表现。
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
            int submitted = ChunkBatchRendererGroupService.GetOwnerVisualCount(this);
            Debug.LogWarning($"[ChunkRenderDebug] RepairPresentation reason={reason} chunk={GetDebugChunkLabel()} " +
                             $"registered={ChunkBatchRendererGroupService.IsOwnerRegistered(this)} " +
                             $"complete={batchPresentationComplete} submitted={submitted} " +
                             $"expectedSubmittable={expectedSubmittable}. " +
                             "将从权威 Terrain 全量重建 BRG 表现。");
        }

        DisableVisualTilemapRenderers();
        batchPresentationComplete = false;
        ChunkBatchRendererGroupService.UnregisterOwner(this);
        ChunkBatchRendererGroupService.RegisterOwner(this);
        RefreshAllBatchVisuals(terrain);
        batchPresentationComplete = true;
        ValidateBatchPresentation("Repair", terrain);
        return ChunkBatchRendererGroupService.IsOwnerRegistered(this) && batchPresentationComplete;
    }

    private void OnDestroy()
    {
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
        batchPresentationComplete = false;
        ChunkBatchRendererGroupService.UnregisterOwner(this);
        chunkCommittedSubscription?.Dispose();
        chunkCommittedSubscription = null;
        chunkEvictedSubscription?.Dispose();
        chunkEvictedSubscription = null;
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

        if (changed.Kind == TerrainChangeKind.Cell || changed.Kind == TerrainChangeKind.TileStack)
            SyncBlockingCollisionCell(boundChunk.Terrain, changed.LocalCell.X, changed.LocalCell.Y);
        if (!EnsureBatchPresentationForIncrementalRefresh("TerrainChanged"))
            return;
        // 高度边、岸线、墙脚和四角水深最多依赖一圈邻格，因此只刷新 3x3 脏区。
        RefreshBatchArea(boundChunk.Terrain, changed.LocalCell.X, changed.LocalCell.Y, 1);
    }

    /// <summary>水体风格切换后，把现有水格迁移到新材质批次。</summary>
    public void NotifyWaterVisualStyleChanged(Renderer changedRenderer)
    {
        if (boundChunk?.Terrain == null || changedRenderer == null)
            return;
        Tilemap activeWater = renderCaveWater ? caveWaterTilemap : waterTilemap;
        if (activeWater == null || !ReferenceEquals(activeWater.GetComponent<TilemapRenderer>(), changedRenderer))
            return;
        if (batchBindingInProgress)
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
        int deltaX = changed.X - origin.X;
        int deltaY = changed.Y - origin.Y;
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
        var address = new FlatWorld.WorldModel.WorldAddress(boundChunk.Address.DimensionId,
            new Int2(origin.X + offsetX, origin.Y + offsetY));
        if (!boundWorld.TryGetChunkTerrain(address, out ChunkTerrainData neighbourTerrain))
            return;

        Action<ChunkTerrainChanged> handler = changed =>
            HandleNeighbourTerrainChanged(neighbourTerrain, offsetX, offsetY, changed);
        neighbourTerrain.Changed += handler;
        neighbourTerrainSubscriptions.Add(
            new NeighbourTerrainSubscription(neighbourTerrain, handler));
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
        }

        neighbourTerrainSubscriptions.Clear();
    }

    #region BRG 表现与碰撞兼容

    /// <summary>视觉 TilemapRenderer 全部停用；Blocking Tilemap 仅继续喂给 TilemapCollider2D。</summary>
    private void DisableVisualTilemapRenderers()
    {
        SetRendererEnabled(groundTilemap, false);
        SetRendererEnabled(waterTilemap, false);
        SetRendererEnabled(caveWaterTilemap, false);
        SetRendererEnabled(backTilemap, false);
        SetRendererEnabled(blockingTilemap, false);
    }

    private static void SetRendererEnabled(Tilemap tilemap, bool enabled)
    {
        TilemapRenderer renderer = tilemap != null ? tilemap.GetComponent<TilemapRenderer>() : null;
        if (renderer != null)
            renderer.enabled = enabled;
    }

    /// <summary>清空旧视觉 Tilemap，避免热重载或池化后残留重复图像。</summary>
    private void ClearVisualTilemaps()
    {
        if (groundTilemap != null)
            groundTilemap.ClearAllTiles();
        if (waterTilemap != null)
            waterTilemap.ClearAllTiles();
        if (caveWaterTilemap != null)
            caveWaterTilemap.ClearAllTiles();
        if (backTilemap != null)
            backTilemap.ClearAllTiles();
        if (blockingTilemap != null)
            blockingTilemap.ClearAllTiles();
    }

    /// <summary>首次绑定时一次性建立 Blocking 碰撞格；视觉仍由 BRG 绘制。</summary>
    private void SyncBlockingCollisionAll(ChunkTerrainData terrain)
    {
        if (blockingTilemap == null)
            return;
        var tiles = new TileBase[terrain.CellCount];
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
        {
            TerrainCell cell = terrain.GetCell(x, y);
            if (cell.BlockingTileId != 0)
                palette.TryGetTile(cell.BlockingTileId, out tiles[y * terrain.Width + x]);
        }
        blockingTilemap.SetTilesBlock(new BoundsInt(0, 0, 0, terrain.Width, terrain.Height, 1), tiles);
    }

    private void SyncBlockingCollisionCell(ChunkTerrainData terrain, int x, int y)
    {
        if (blockingTilemap == null || x < 0 || x >= terrain.Width || y < 0 || y >= terrain.Height)
            return;
        TerrainCell cell = terrain.GetCell(x, y);
        TileBase tile = null;
        if (cell.BlockingTileId != 0)
            palette.TryGetTile(cell.BlockingTileId, out tile);
        blockingTilemap.SetTile(new Vector3Int(x, y, 0), tile);
    }

    private void RefreshAllBatchVisuals(ChunkTerrainData terrain)
    {
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
            RefreshBatchCell(terrain, x, y);
    }

    private void RefreshBatchArea(ChunkTerrainData terrain, int centerX, int centerY, int radius)
    {
        int minX = Mathf.Max(0, centerX - radius);
        int maxX = Mathf.Min(terrain.Width - 1, centerX + radius);
        int minY = Mathf.Max(0, centerY - radius);
        int maxY = Mathf.Min(terrain.Height - 1, centerY + radius);
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
            for (int y = 0; y < terrain.Height; y++)
                RefreshBatchArea(terrain, x, y, 1);
            return;
        }

        int edgeY = offsetY < 0 ? 0 : terrain.Height - 1;
        for (int x = 0; x < terrain.Width; x++)
            RefreshBatchArea(terrain, x, edgeY, 1);
    }

    private void RefreshBatchCell(ChunkTerrainData terrain, int x, int y)
    {
        TerrainCell cell = terrain.GetCell(x, y);

        if (cell.GroundTileId != 0)
        {
            SetBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Ground,
                cell.GroundTileId, groundTilemap, GetMaterial(groundTilemap),
                BuildBatchGroundContactMask(terrain, x, y, cell), Vector4.zero);
        }
        else
        {
            ClearBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Ground);
        }

        RefreshLiquidVisual(terrain, x, y);

        if (backTilemap != null && cell.BackTileId != 0)
        {
            SetBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Back,
                cell.BackTileId, backTilemap, GetMaterial(backTilemap), Vector4.zero, Vector4.zero);
        }
        else
        {
            ClearBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Back);
        }

        if (cell.BlockingTileId != 0)
        {
            SetBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Blocking,
                cell.BlockingTileId, blockingTilemap, GetMaterial(blockingTilemap), Vector4.zero, Vector4.zero);
        }
        else
        {
            ClearBatchVisual(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Blocking);
        }
    }

    /// <summary>Liquid BRG 完全从液体目录取外观，不再把 GroundTileId 当作水面贴图。</summary>
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
        Int2 origin = boundChunk.Address.ChunkOrigin;
        Matrix4x4 transform = Matrix4x4.Translate(new Vector3(origin.X + x + 0.5f, origin.Y + y + 0.5f, 0f));
        var data = ChunkBatchRendererGroupService.InstanceData.Create(transform,
            BuildWaterShoreMask(terrain, x, y), BuildLiquidDepthCorners(terrain, x, y), Color.white);
        SetWaterCurrentData(terrain, x, y, ref data);
        ChunkBatchRendererGroupService.SetVisual(this,
            GetBatchSlotKey(terrain, x, y, ChunkBatchRendererGroupService.VisualLayer.Water),
            new ChunkBatchRendererGroupService.Visual(ChunkBatchRendererGroupService.VisualLayer.Water,
                settings.Sprite, material, data));
    }

    private static WorldLiquidSettings ResolveLiquidVisual(ChunkTerrainData terrain, int x, int y)
    {
        string id = terrain.LiquidTypes.GetId(terrain.GetLiquidTypeIndex(x, y));
        return GameRes.ExistingInstance != null && GameRes.ExistingInstance.TryGetLiquidDefinition(id, out var definition)
            ? definition.WorldWater : null;
    }

    private void SetBatchVisual(ChunkTerrainData terrain, int x, int y,
        ChunkBatchRendererGroupService.VisualLayer layer, int tileId, Tilemap sourceTilemap,
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

        Int2 origin = boundChunk.Address.ChunkOrigin;
        Matrix4x4 localToWorld = Matrix4x4.Translate(new Vector3(
            origin.X + x + 0.5f,
            origin.Y + y + 0.5f,
            0f)) * tileTransform;
        Color tint = sourceTilemap != null ? tileColor * sourceTilemap.color : tileColor;
        var instanceData = ChunkBatchRendererGroupService.InstanceData.Create(localToWorld, data0, data1, tint);
        // Liquid 有独立提交入口；Back / Blocking 即使复用 Contact 材质也不能获得高度语义。
        instanceData.Transform0.w = -1f;
        if (layer == ChunkBatchRendererGroupService.VisualLayer.Ground)
            SetGroundElevationData(terrain, x, y, ref instanceData);
        ChunkBatchRendererGroupService.SetVisual(this, GetBatchSlotKey(terrain, x, y, layer),
            new ChunkBatchRendererGroupService.Visual(layer, sprite, sourceMaterial, instanceData));
    }

    private void ClearBatchVisual(ChunkTerrainData terrain, int x, int y,
        ChunkBatchRendererGroupService.VisualLayer layer)
    {
        ChunkBatchRendererGroupService.ClearVisual(this, GetBatchSlotKey(terrain, x, y, layer));
    }

    private static int GetBatchSlotKey(ChunkTerrainData terrain, int x, int y,
        ChunkBatchRendererGroupService.VisualLayer layer)
    {
        return (int)layer * terrain.CellCount + y * terrain.Width + x;
    }

    private Material GetActiveWaterMaterial()
    {
        return GetMaterial(renderCaveWater ? caveWaterTilemap : waterTilemap);
    }

    private static Material GetMaterial(Tilemap tilemap)
    {
        TilemapRenderer renderer = tilemap != null ? tilemap.GetComponent<TilemapRenderer>() : null;
        return renderer != null ? renderer.sharedMaterial : null;
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
        Material groundMaterial = GetMaterial(groundTilemap);
        Material backMaterial = GetMaterial(backTilemap);
        Material blockingMaterial = GetMaterial(blockingTilemap);
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
        {
            TerrainCell cell = terrain.GetCell(x, y);
            if (cell.GroundTileId != 0 && groundMaterial != null && HasPaletteVisual(cell.GroundTileId)) count++;
            if (terrain.GetLiquidDepth(x, y) > 0f && ResolveLiquidVisual(terrain, x, y)?.Sprite != null) count++;

            if (backTilemap != null && cell.BackTileId != 0 &&
                backMaterial != null && HasPaletteVisual(cell.BackTileId))
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
        int submitted = ChunkBatchRendererGroupService.GetOwnerVisualCount(this);
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
            return;

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

        Int2 origin = boundChunk.Address.ChunkOrigin;
        int targetOriginX = origin.X;
        int targetOriginY = origin.Y;
        int localX = x;
        int localY = y;
        if (x < 0)
            targetOriginX -= terrain.Width;
        else if (x >= terrain.Width)
            targetOriginX += terrain.Width;
        if (y < 0)
            targetOriginY -= terrain.Height;
        else if (y >= terrain.Height)
            targetOriginY += terrain.Height;

        var address = new FlatWorld.WorldModel.WorldAddress(boundChunk.Address.DimensionId,
            new Int2(targetOriginX, targetOriginY));
        if (!boundWorld.TryGetChunkTerrain(address, out ChunkTerrainData neighbourTerrain))
            return false;

        if (localX < 0)
            localX += neighbourTerrain.Width;
        else if (localX >= terrain.Width)
            localX -= terrain.Width;
        if (localY < 0)
            localY += neighbourTerrain.Height;
        else if (localY >= terrain.Height)
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
            Action<ChunkTerrainChanged> handler)
        {
            Terrain = terrain;
            Handler = handler;
        }

        public ChunkTerrainData Terrain { get; }
        public Action<ChunkTerrainChanged> Handler { get; }
    }

    #endregion
}
