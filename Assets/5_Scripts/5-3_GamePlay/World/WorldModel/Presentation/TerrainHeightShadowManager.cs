using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>地块高低阴影的唯一表现入口；按区块登记脏格，共享一个 BRG 阴影图层。</summary>
public sealed class TerrainHeightShadowManager : MonoBehaviour, IChunkViewRenderer,
    IWorldAwareChunkViewRenderer, IRecoverableChunkViewRenderer
{
    #region 绑定与生命周期

    [SerializeField] private Material material;
    private WorldRuntime world;
    private ChunkRuntime chunk;
    private ChunkTilemapRenderer owner;
    private bool useGeneratedHeight;
    private readonly TerrainShadowDirtyQueue dirty = new();
    private bool dirtyAny;
    private bool flushInProgress;
    private int submittedCount;
    private readonly List<IDisposable> chunkSubscriptions = new();
    private readonly List<NeighbourSubscription> terrainSubscriptions = new();

    public void SetWorld(WorldRuntime value)
    {
        if (ReferenceEquals(world, value)) return;
        ClearSubscriptions();
        world = value;
        if (chunk != null) SubscribeNeighbours();
    }

    public void Bind(ChunkRuntime value)
    {
        Unbind();
        chunk = value ?? throw new ArgumentNullException(nameof(value));
        owner = GetComponent<ChunkTilemapRenderer>();
        if (chunk.Terrain == null || owner == null || material == null)
            throw new InvalidOperationException("地表高低阴影缺少权威地形、BRG Owner 或共享材质。");
        useGeneratedHeight = UsesGeneratedHeight(chunk.Address.DimensionId);
        dirty.Reset(chunk.Terrain.Width * chunk.Terrain.Height);
        chunk.Terrain.Changed += HandleChanged;
        chunk.Terrain.LiquidBatchChanged += HandleLiquidChanged;
        owner.BatchPresentationRebuilt += MarkAll;
        SubscribeNeighbours();
        MarkAll();
        FlushDirty();
    }

    public void Unbind()
    {
        ClearSubscriptions();
        if (chunk?.Terrain != null)
        {
            chunk.Terrain.Changed -= HandleChanged;
            chunk.Terrain.LiquidBatchChanged -= HandleLiquidChanged;
        }
        if (owner != null)
        {
            owner.BatchPresentationRebuilt -= MarkAll;
            ChunkBatchRendererGroupService.ClearOwnerLayer(owner,
                ChunkBatchRendererGroupService.VisualLayer.TerrainHeightShadow);
        }
        chunk = null;
        owner = null;
        dirty.Clear();
        dirtyAny = false;
        submittedCount = 0;
        enabled = false;
    }

    private void OnDestroy() { Unbind(); world = null; }
    private void LateUpdate() => FlushDirty();

    public bool RepairPresentationIfNeeded(ChunkRuntime expected)
    {
        if (!ReferenceEquals(chunk, expected) || owner == null || !owner.IsBatchPresentationRegistered)
            return false;
        if (submittedCount != ChunkBatchRendererGroupService.GetOwnerLayerVisualCount(owner,
                ChunkBatchRendererGroupService.VisualLayer.TerrainHeightShadow)) MarkAll();
        FlushDirty();
        return !dirtyAny;
    }

    #endregion

    #region 统一高度查询与绘制

    /// <summary>所有消费者可读取同一份有效表面层级，生成高度仍保持原值。</summary>
    public static bool TryGetSurfaceHeight(ChunkTerrainData terrain, int x, int y, string dimensionId,
        out TerrainSurfaceHeight surface) =>
        ReadSurface(terrain, x, y, UsesGeneratedHeight(dimensionId), out surface);

    private static bool ReadSurface(ChunkTerrainData terrain, int x, int y, bool useGeneratedHeight,
        out TerrainSurfaceHeight surface)
    {
        surface = default;
        if (terrain == null || terrain.IsDisposed || (uint)x >= (uint)terrain.Width ||
            (uint)y >= (uint)terrain.Height) return false;
        int minimumDepth = 0;
        if (GameRes.ExistingInstance != null && GameRes.ExistingInstance.TryGetTileDefinition(
                terrain.GetCell(x, y).GroundTileId, out RuntimeTileDefinition definition))
            minimumDepth = definition.MinimumSurfaceDepth;
        return TerrainSurfaceHeightLayer.TryGetSurface(terrain, x, y, useGeneratedHeight, out surface, minimumDepth);
    }

    /// <summary>以已加载区块为起点查询邻格，跨区块和环绕边界共用绘制时的解析入口。</summary>
    public static bool TryGetSurfaceHeight(WorldRuntime world, RuntimeWorldAddress address, Int2 local,
        out TerrainSurfaceHeight surface)
    {
        surface = default;
        return world != null && world.TryGetChunkTerrain(address, out ChunkTerrainData terrain) &&
            TryResolveSurface(world, address, terrain, local.X, local.Y,
                UsesGeneratedHeight(address.DimensionId), out surface);
    }

    private static bool UsesGeneratedHeight(string dimensionId)
    {
        // 高度查询只借用已存在的维度服务，不能为读取数据创建游戏管理器。
        DimensionManager dimensions = DimensionManager.ExistingInstance;
        if (dimensions != null &&
            dimensions.TryGetDefinition(dimensionId, out DimensionDefinition definition))
            return definition.GenerationMode == DimensionGenerationMode.Surface;
        return string.Equals(dimensionId, global::WorldAddress.SurfaceDimensionId, StringComparison.OrdinalIgnoreCase);
    }

    private void FlushDirty()
    {
        if (flushInProgress || !dirtyAny || chunk?.Terrain == null || owner == null || !owner.IsBatchPresentationRegistered ||
            SharedSpriteMeshCache.IsSessionEnding) return;
        flushInProgress = true;
        // 将脏格移入独立工作批次，渲染期间再次标脏会留给下一帧。
        List<int> batch = dirty.BeginDrain();
        int processed = 0;
        try
        {
            ChunkBatchRendererGroupService.BeginBulkSubmit();
            try
            {
                int width = chunk.Terrain.Width;
                for (; processed < batch.Count; processed++)
                {
                    int index = batch[processed];
                    if (!Refresh(index % width, index / width)) dirty.Mark(index);
                }
                submittedCount = ChunkBatchRendererGroupService.GetOwnerLayerVisualCount(owner,
                    ChunkBatchRendererGroupService.VisualLayer.TerrainHeightShadow);
            }
            finally { ChunkBatchRendererGroupService.EndBulkSubmit(); }
        }
        finally
        {
            dirty.CompleteDrain(processed);
            dirtyAny = dirty.PendingCount > 0;
            enabled = dirtyAny;
            flushInProgress = false;
        }
    }

    private bool Refresh(int x, int y)
    {
        var layer = ChunkBatchRendererGroupService.VisualLayer.TerrainHeightShadow;
        if (!TrySample(x, y, out TerrainSurfaceHeight surface) || !surface.ReceivesShadow)
        {
            owner.ClearLayerVisual(layer, x, y);
            return true;
        }
        Vector4 edges = new(Delta(x - 1, y, surface), Delta(x + 1, y, surface),
            Delta(x, y - 1, surface), Delta(x, y + 1, surface));
        if (edges == Vector4.zero)
        {
            owner.ClearLayerVisual(layer, x, y);
            return true;
        }
        Int2 origin = chunk.Address.ChunkOrigin;
        Matrix4x4 matrix = Matrix4x4.Translate(new Vector3(origin.X + x + .5f, origin.Y + y + .5f));
        return owner.SetLayerVisual(layer, x, y, SharedSpriteMeshCache.GetTerrainShadowCell(),
            material, matrix, Color.white, edges, Vector4.zero);
    }

    private int Delta(int x, int y, TerrainSurfaceHeight receiver) =>
        TrySample(x, y, out TerrainSurfaceHeight neighbour)
            ? TerrainSurfaceHeightLayer.GetShadowDelta(receiver, neighbour) : 0;

    private bool TrySample(int x, int y, out TerrainSurfaceHeight surface)
        => TryResolveSurface(world, chunk.Address, chunk.Terrain, x, y, useGeneratedHeight, out surface);

    private static bool TryResolveSurface(WorldRuntime world, RuntimeWorldAddress address, ChunkTerrainData terrain,
        int x, int y, bool useGeneratedHeight, out TerrainSurfaceHeight surface)
    {
        if ((uint)x < (uint)terrain.Width && (uint)y < (uint)terrain.Height)
            return ReadSurface(terrain, x, y, useGeneratedHeight, out surface);
        surface = default;
        if (world == null) return false;
        int offsetX = x < 0 ? -terrain.Width : x >= terrain.Width ? terrain.Width : 0;
        int offsetY = y < 0 ? -terrain.Height : y >= terrain.Height ? terrain.Height : 0;
        RuntimeWorldAddress neighbourAddress = NeighbourAddress(address, offsetX, offsetY);
        return world.TryGetChunkTerrain(neighbourAddress, out ChunkTerrainData neighbour) &&
            ReadSurface(neighbour, x - offsetX, y - offsetY,
                useGeneratedHeight, out surface);
    }

    private void MarkAll()
    {
        if (chunk?.Terrain == null) return;
        dirty.MarkAll();
        dirtyAny = true;
        enabled = true;
    }

    private void Mark(int x, int y)
    {
        if (chunk?.Terrain == null || (uint)x >= (uint)chunk.Terrain.Width ||
            (uint)y >= (uint)chunk.Terrain.Height) return;
        dirty.Mark(y * chunk.Terrain.Width + x);
        dirtyAny = true;
        enabled = true;
    }

    private void MarkArea(int x, int y)
    {
        Mark(x, y); Mark(x - 1, y); Mark(x + 1, y); Mark(x, y - 1); Mark(x, y + 1);
    }

    private static bool AffectsHeight(ChunkTerrainChanged change) =>
        change.Kind == TerrainChangeKind.Cell || change.Kind == TerrainChangeKind.Liquid ||
        change.Kind == TerrainChangeKind.TileStack || change.AffectsEnvironmentLayer("height") ||
        change.AffectsEnvironmentLayer(TerrainSurfaceHeightLayer.NaturalDepthLevelsLayerId) ||
        change.AffectsEnvironmentLayer(TerrainSurfaceHeightLayer.ExcavatedLevelsLayerId) ||
        change.AffectsEnvironmentLayer(TerrainSurfaceHeightLayer.CoverLevelsLayerId) ||
        change.AffectsEnvironmentLayer(TerrainSupportLayer.TileIdLayer);

    private void HandleChanged(ChunkTerrainChanged change)
    {
        if (!AffectsHeight(change)) return;
        int width = chunk.Terrain.Width;
        foreach (int index in change.GetCellIndices(width)) MarkArea(index % width, index / width);
    }

    private void HandleLiquidChanged(ChunkLiquidBatchChanged change)
    {
        foreach (int index in change.CellIndices.Span) MarkArea(index % chunk.Terrain.Width, index / chunk.Terrain.Width);
    }

    #endregion

    #region 跨区块边界与回收

    private RuntimeWorldAddress NeighbourAddress(int x, int y)
        => NeighbourAddress(chunk.Address, x, y);

    private static RuntimeWorldAddress NeighbourAddress(RuntimeWorldAddress address, int x, int y)
    {
        Int2 origin = address.ChunkOrigin;
        Vector2Int canonical = WorldTopologyRuntime.NormalizeCell(new Vector2Int(origin.X + x, origin.Y + y));
        return new RuntimeWorldAddress(address.DimensionId, new Int2(canonical.x, canonical.y));
    }

    private void SubscribeNeighbours()
    {
        ClearSubscriptions();
        if (world == null || chunk?.Terrain == null) return;
        var seen = new HashSet<RuntimeWorldAddress>();
        int width = chunk.Terrain.Width, height = chunk.Terrain.Height;
        Subscribe(-width, 0, seen); Subscribe(width, 0, seen);
        Subscribe(0, -height, seen); Subscribe(0, height, seen);
    }

    private void Subscribe(int offsetX, int offsetY, HashSet<RuntimeWorldAddress> seen)
    {
        RuntimeWorldAddress address = NeighbourAddress(offsetX, offsetY);
        if (address != chunk.Address && seen.Add(address))
        {
            chunkSubscriptions.Add(world.Events.SubscribeChunkCommitted(address, _ => NeighboursChanged()));
            chunkSubscriptions.Add(world.Events.SubscribeChunkEvicted(address, _ => NeighboursChanged()));
        }
        if (!world.TryGetChunkTerrain(address, out ChunkTerrainData terrain)) return;
        Action<ChunkTerrainChanged> changed = value =>
        {
            if (!AffectsHeight(value)) return;
            foreach (int index in value.GetCellIndices(terrain.Width))
                MarkNeighbourCell(index % terrain.Width, index / terrain.Width, terrain, offsetX, offsetY);
        };
        Action<ChunkLiquidBatchChanged> liquid = value =>
        {
            foreach (int index in value.CellIndices.Span)
                MarkNeighbourCell(index % terrain.Width, index / terrain.Width, terrain, offsetX, offsetY);
        };
        terrain.Changed += changed;
        terrain.LiquidBatchChanged += liquid;
        terrainSubscriptions.Add(new NeighbourSubscription(terrain, changed, liquid));
    }

    private void MarkNeighbourCell(int x, int y, ChunkTerrainData terrain, int offsetX, int offsetY)
    {
        if (offsetX < 0 && x == terrain.Width - 1) Mark(0, y);
        else if (offsetX > 0 && x == 0) Mark(chunk.Terrain.Width - 1, y);
        else if (offsetY < 0 && y == terrain.Height - 1) Mark(x, 0);
        else if (offsetY > 0 && y == 0) Mark(x, chunk.Terrain.Height - 1);
    }

    private void NeighboursChanged() { SubscribeNeighbours(); MarkAll(); }

    private void ClearSubscriptions()
    {
        foreach (IDisposable subscription in chunkSubscriptions) subscription.Dispose();
        chunkSubscriptions.Clear();
        foreach (NeighbourSubscription subscription in terrainSubscriptions)
        {
            subscription.Terrain.Changed -= subscription.Changed;
            subscription.Terrain.LiquidBatchChanged -= subscription.Liquid;
        }
        terrainSubscriptions.Clear();
    }

    private readonly struct NeighbourSubscription
    {
        public NeighbourSubscription(ChunkTerrainData terrain, Action<ChunkTerrainChanged> changed,
            Action<ChunkLiquidBatchChanged> liquid)
        { Terrain = terrain; Changed = changed; Liquid = liquid; }
        public ChunkTerrainData Terrain { get; }
        public Action<ChunkTerrainChanged> Changed { get; }
        public Action<ChunkLiquidBatchChanged> Liquid { get; }
    }

    #endregion
}

/// <summary>阴影脏格双缓冲队列，只遍历有变化的格子，重入通知和失败重试不会丢失。</summary>
internal sealed class TerrainShadowDirtyQueue
{
    #region 脏格收集与提交

    private bool[] queuedFlags;
    private List<int> pending = new();
    private List<int> working = new();

    internal int PendingCount => pending.Count;

    internal void Reset(int cellCount)
    {
        queuedFlags = new bool[cellCount];
        pending.Clear();
        working.Clear();
        // 首次整块绑定只扩容一次；重绑优先沿用上一轮最大的列表缓冲。
        if (working.Capacity > pending.Capacity) (pending, working) = (working, pending);
        if (pending.Capacity < cellCount) pending.Capacity = cellCount;
    }

    internal void Clear()
    {
        queuedFlags = null;
        pending.Clear();
        working.Clear();
    }

    internal void Mark(int index)
    {
        if (queuedFlags == null || (uint)index >= (uint)queuedFlags.Length || queuedFlags[index]) return;
        queuedFlags[index] = true;
        pending.Add(index);
    }

    internal void MarkAll()
    {
        if (queuedFlags == null) return;
        for (int index = 0; index < queuedFlags.Length; index++) Mark(index);
    }

    internal List<int> BeginDrain()
    {
        List<int> batch = pending;
        pending = working;
        working = batch;
        for (int i = 0; i < batch.Count; i++) queuedFlags[batch[i]] = false;
        return batch;
    }

    internal void CompleteDrain(int processedCount)
    {
        // 异常中断时，未成功刷新的当前格及剩余格继续留在下次提交中。
        for (int i = processedCount; i < working.Count; i++) Mark(working[i]);
        working.Clear();
    }

    #endregion
}
