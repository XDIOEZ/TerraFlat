using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>当前已加载的连通静水水域快照；水量的唯一真源仍是各格的 LiquidLevel。</summary>
public readonly struct WorldLiquidBodySnapshot
{
    #region 水域统计
    public readonly string LiquidId;
    public readonly long TotalLevel;
    public readonly long Capacity;
    public readonly int CellCount;
    public readonly int LoadedChunkCount;
    public readonly bool HasUnloadedBoundary;
    public float AverageLevel => CellCount == 0 ? 0f : (float)TotalLevel / CellCount;

    public WorldLiquidBodySnapshot(string id, long total, int cells, int chunks, bool partial)
    {
        LiquidId = id;
        TotalLevel = total;
        CellCount = cells;
        Capacity = (long)cells * LiquidCellValue.MaximumLevel;
        LoadedChunkCount = chunks;
        HasUnloadedBoundary = partial;
    }
    #endregion
}

/// <summary>
/// 天然静水分块建图、跨块组合统计；仅被修改的水域唤醒局部守恒转移，稳定后不再执行求解。
/// 不申请额外区块租约，未加载部分不参与结算，也不把聚合值另存为第二份权威水量。
/// </summary>
public sealed class WorldLiquidBodySystem : IDisposable
{
    #region 图与增量状态
    private const int MinimumBodyCells = 16;
    private const int WorkCellsPerFrame = 128;
    private static readonly Vector2Int[] Directions =
        { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };

    private readonly ChunkMgr owner;
    private readonly WorldRuntime world;
    private readonly long epoch;
    private readonly string dimension;
    private readonly IDisposable evictedSubscription;
    private readonly Dictionary<RuntimeWorldAddress, ChunkState> observed = new();
    private readonly Queue<Vector2Int> pending = new();
    private readonly HashSet<Vector2Int> pendingSet = new();
    private long topologyVersion = 1;
    private bool applying;
    private bool disposed;

    private readonly struct BoundaryEdge
    {
        public readonly Vector2Int Neighbour;
        public BoundaryEdge(Vector2Int neighbour) { Neighbour = neighbour; }
    }

    private sealed class LocalComponent
    {
        public ChunkState State;
        public string LiquidId;
        public int TypeIndex;
        public int Cells;
        public long TotalLevel;
        public Body Body;
        public readonly List<BoundaryEdge> Borders = new();
    }

    private sealed class Body
    {
        public long Version;
        public long TotalLevel;
        public int Cells;
        public int ChunkCount;
        public bool HasUnloadedBoundary;
        public string LiquidId;
    }

    private sealed class ChunkState
    {
        public readonly WorldLiquidBodySystem Host;
        public readonly ChunkRuntime Chunk;
        public readonly ChunkTerrainData Terrain;
        public int[] ComponentIndices;
        public int[] Levels;
        public readonly List<LocalComponent> Components = new();
        public bool Dirty = true;

        public ChunkState(WorldLiquidBodySystem host, ChunkRuntime chunk)
        {
            Host = host;
            Chunk = chunk;
            Terrain = chunk.Terrain;
            Terrain.Changed += OnChanged;
            Terrain.LiquidBatchChanged += OnLiquidBatch;
        }

        private void OnChanged(ChunkTerrainChanged change)
        {
            bool topology = change.Kind == TerrainChangeKind.Cell ||
                            change.Kind == TerrainChangeKind.TileStack ||
                            change.AffectsEnvironmentLayer(TerrainSurfaceHeightLayer.ExcavatedLevelsLayerId) ||
                            change.AffectsEnvironmentLayer(TerrainSurfaceHeightLayer.CoverLevelsLayerId);
            if (!topology && change.Kind != TerrainChangeKind.Liquid) return;
            foreach (int index in change.GetCellIndices(Terrain.Width))
            {
                int x = index % Terrain.Width, y = index / Terrain.Width;
                if (topology) Host.OnTopologyChanged(this, x, y);
                else Host.OnLiquidChanged(this, index);
            }
        }

        private void OnLiquidBatch(ChunkLiquidBatchChanged change)
        {
            foreach (int index in change.CellIndices.Span)
                Host.OnLiquidChanged(this, index);
        }

        public void Dispose()
        {
            Terrain.Changed -= OnChanged;
            Terrain.LiquidBatchChanged -= OnLiquidBatch;
            Components.Clear();
            ComponentIndices = null;
            Levels = null;
        }
    }
    #endregion

    #region 生命周期与失效
    public WorldLiquidBodySystem(ChunkMgr owner, string dimension)
    {
        this.owner = owner;
        this.dimension = dimension;
        world = owner.WorldRuntime;
        epoch = world.Epoch;
        evictedSubscription = world.Events.Subscribe<ChunkEvicted>(OnEvicted);
        WorldLiquidFlowObstacles.Changed += OnObstacleChanged;
    }

    /// <summary>必须在存档地形和建筑恢复后观察；不能对尚未恢复的生成草稿建立水域。</summary>
    public void Observe(ChunkRuntime chunk)
    {
        if (disposed || chunk?.Terrain == null || chunk.Terrain.IsDisposed ||
            chunk.Address.DimensionId != dimension || chunk.DataStatus != ChunkDataStatus.Ready)
            return;
        if (observed.TryGetValue(chunk.Address, out ChunkState previous))
        {
            if (ReferenceEquals(previous.Terrain, chunk.Terrain)) return;
            previous.Dispose();
            observed.Remove(chunk.Address);
        }
        var state = new ChunkState(this, chunk);
        observed.Add(chunk.Address, state);
        topologyVersion++;
        // 恢复存档后的非平衡格也须唤醒；天然生成的未变湖泊则始终保持休眠。
        for (int i = 0; i < state.Terrain.CellCount; i++)
            if (state.Terrain.IsLiquidChanged(i % state.Terrain.Width, i / state.Terrain.Width))
                WakeCross(WorldCell(state, i % state.Terrain.Width, i / state.Terrain.Width));
        // 新加载的边界只与已观察邻块比较，不把远方水域一并加载进来。
        WakeJoiningBorders(state);
    }

    private void OnEvicted(ChunkEvicted message)
    {
        if (!observed.Remove(message.Address, out ChunkState state)) return;
        state.Dispose();
        topologyVersion++;
    }

    private void OnObstacleChanged(Vector2Int cell)
    {
        if (disposed || !ReferenceEquals(owner.WorldRuntime, world) ||
            world.WorldId != GameplayWorldRegistry.ExecutionWorldKey) return;
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        if (!TryGetState(cell, out ChunkState state, out int x, out int y)) return;
        Invalidate(state);
        WakeCross(cell);
    }

    private void OnTopologyChanged(ChunkState state, int x, int y)
    {
        Invalidate(state);
        WakeCross(WorldCell(state, x, y));
    }

    private void Invalidate(ChunkState state)
    {
        if (state.Dirty) return;
        state.Dirty = true;
        topologyVersion++;
    }

    private void OnLiquidChanged(ChunkState state, int index)
    {
        int x = index % state.Terrain.Width, y = index / state.Terrain.Width;
        if (state.Terrain.GetLiquidBaseline(x, y).LiquidLevel <= 0 &&
            TerrainSurfaceHeightLayer.GetExcavatedLevels(state.Terrain, x, y) == 0) return;
        int level = state.Terrain.GetLiquidLevel(x, y);
        int type = state.Terrain.GetLiquidTypeIndex(x, y);
        if (!state.Dirty && state.ComponentIndices != null)
        {
            int componentIndex = state.ComponentIndices[index];
            if ((componentIndex >= 0 && level > 0 && type != state.Components[componentIndex].TypeIndex) ||
                (componentIndex < 0 && (level == 0 || type ==
                    state.Terrain.GetLiquidBaseline(x, y).LiquidTypeIndex)))
                Invalidate(state);
            else if (componentIndex >= 0)
            {
                int difference = level - state.Levels[index];
                LocalComponent component = state.Components[componentIndex];
                component.TotalLevel += difference;
                if (component.Body != null && component.Body.Version == topologyVersion)
                    component.Body.TotalLevel += difference;
            }
            state.Levels[index] = level;
        }
        if (!applying) WakeCross(WorldCell(state, x, y));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        WorldLiquidFlowObstacles.Changed -= OnObstacleChanged;
        evictedSubscription.Dispose();
        foreach (ChunkState state in observed.Values) state.Dispose();
        observed.Clear();
        pending.Clear();
        pendingSet.Clear();
    }
    #endregion

    #region 静水建图与跨区块聚合
    private static Vector2Int WorldCell(ChunkState state, int x, int y) =>
        new(state.Chunk.Address.ChunkOrigin.X + x, state.Chunk.Address.ChunkOrigin.Y + y);

    private bool TryGetState(Vector2Int cell, out ChunkState state, out int x, out int y)
    {
        state = null;
        x = y = 0;
        if (disposed) return false;
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        RuntimeWorldAddress address = owner.ResolveWorldAddress((Vector2)cell + Vector2.one * .5f, dimension);
        if (!observed.TryGetValue(address, out state) || state.Terrain.IsDisposed) return false;
        x = cell.x - address.ChunkOrigin.X;
        y = cell.y - address.ChunkOrigin.Y;
        return (uint)x < (uint)state.Terrain.Width && (uint)y < (uint)state.Terrain.Height;
    }

    private bool TryGetComponent(Vector2Int cell, out LocalComponent component,
        out RuntimeTerrainTileSample sample)
    {
        component = null;
        sample = default;
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        if (!TryGetState(cell, out ChunkState state, out int x, out int y)) return false;
        EnsureTopology(state);
        int id = state.ComponentIndices[y * state.Terrain.Width + x];
        if (id < 0) return false;
        component = state.Components[id];
        sample = new RuntimeTerrainTileSample(state.Chunk.Address, state.Terrain, cell,
            new Vector2Int(x, y), state.Terrain.GetCell(x, y), 0);
        return true;
    }

    private static bool IsOpenStaticCell(ChunkState state, int x, int y)
    {
        ChunkTerrainData terrain = state.Terrain;
        TerrainCell cell = terrain.GetCell(x, y);
        if (cell.BlockingTileId != 0 || (cell.Flags & TerrainCellFlags.Blocking) != 0 ||
            (SurfaceBiomeKind)cell.BiomeId == SurfaceBiomeKind.Ocean ||
            WorldLiquidFlowObstacles.IsBlocked(WorldCell(state, x, y)))
            return false;
        terrain.TryGetEnvironmentValue("riverKind", x, y, out float riverKind);
        if (Mathf.RoundToInt(riverKind) != 0) return false;
        return true;
    }

    private static bool CanHoldChannelWater(ChunkState state, int x, int y) =>
        TerrainSurfaceHeightLayer.GetExcavatedLevels(state.Terrain, x, y) > 0 &&
        (!state.Terrain.TryGetEnvironmentValue(TerrainSurfaceHeightLayer.CoverLevelsLayerId,
            x, y, out float cover) || cover <= 0f);

    private static int EligibleType(ChunkState state, int x, int y)
    {
        if (!IsOpenStaticCell(state, x, y)) return 0;
        ChunkTerrainData terrain = state.Terrain;
        LiquidCellValue baseline = terrain.GetLiquidBaseline(x, y);
        if (baseline.LiquidLevel <= 0 && !CanHoldChannelWater(state, x, y)) return 0;
        int level = terrain.GetLiquidLevel(x, y);
        int type = level > 0 ? terrain.GetLiquidTypeIndex(x, y) : baseline.LiquidTypeIndex;
        if (baseline.LiquidLevel > 0 && level > 0 && type != baseline.LiquidTypeIndex)
            return 0;
        if (type <= 0) return 0; // 未注水的新渠格由附近已经识别的水域拓扑传播类型。
        string id = terrain.LiquidTypes.GetId(type);
        return GameRes.ExistingInstance != null &&
            GameRes.ExistingInstance.TryGetLiquidDefinition(id, out LiquidDefinition definition) &&
            definition.WorldWater?.WaterContact == true ? type : 0;
    }

    private void EnsureTopology(ChunkState state)
    {
        if (!state.Dirty) return;
        ChunkTerrainData terrain = state.Terrain;
        int width = terrain.Width, height = terrain.Height, count = terrain.CellCount;
        state.Components.Clear();
        state.ComponentIndices = new int[count];
        Array.Fill(state.ComponentIndices, -1);
        state.Levels = terrain.LiquidLevel.ToArray();
        int[] eligible = new int[count];
        for (int i = 0; i < count; i++)
            eligible[i] = EligibleType(state, i % width, i / width);
        int[] frontier = new int[count];
        // 水渠空格从相邻水源传播稳定类型，冲突时保持先遇到的类型，不混入第二种液体。
        int seedCount = 0;
        for (int i = 0; i < count; i++)
            if (eligible[i] > 0) frontier[seedCount++] = i;
        for (int i = 0; i < count; i++)
        {
            if (eligible[i] != 0) continue;
            int x = i % width, y = i / width;
            if (!CanHoldChannelWater(state, x, y) || !IsOpenStaticCell(state, x, y))
                continue;
            Vector2Int cell = WorldCell(state, x, y);
            foreach (Vector2Int direction in Directions)
            {
                int nx = x + direction.x, ny = y + direction.y;
                if ((uint)nx < (uint)width && (uint)ny < (uint)height) continue;
                if (!TryGetState(WorldTopologyRuntime.NormalizeCell(cell + direction),
                        out ChunkState adjacent, out nx, out ny))
                    continue;
                int type = EligibleType(adjacent, nx, ny);
                if (type == 0) continue;
                string liquidId = adjacent.Terrain.LiquidTypes.GetId(type);
                eligible[i] = terrain.LiquidTypes.GetIndex(liquidId);
                frontier[seedCount++] = i;
                break;
            }
        }
        int scan = 0, tailSeeds = seedCount;
        while (scan < tailSeeds)
        {
            int i = frontier[scan++], x = i % width, y = i / width;
            int type = eligible[i];
            foreach (Vector2Int direction in Directions)
            {
                int nx = x + direction.x, ny = y + direction.y;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                int neighbour = ny * width + nx;
                if (eligible[neighbour] > 0 ||
                    !CanHoldChannelWater(state, nx, ny) ||
                    !IsOpenStaticCell(state, nx, ny)) continue;
                int wetType = terrain.GetLiquidTypeIndex(nx, ny);
                if (wetType > 0 && wetType != type) continue;
                eligible[neighbour] = type;
                frontier[tailSeeds++] = neighbour;
            }
        }
        for (int i = 0; i < count; i++)
        {
            if (eligible[i] == 0 || state.ComponentIndices[i] >= 0) continue;
            int type = eligible[i], id = state.Components.Count;
            var component = new LocalComponent
            {
                State = state,
                TypeIndex = type,
                LiquidId = terrain.LiquidTypes.GetId(type)
            };
            state.Components.Add(component);
            int head = 0, tail = 0;
            frontier[tail++] = i;
            state.ComponentIndices[i] = id;
            while (head < tail)
            {
                int index = frontier[head++];
                int x = index % width, y = index / width;
                component.Cells++;
                component.TotalLevel += state.Levels[index];
                Vector2Int worldCell = WorldCell(state, x, y);
                for (int d = 0; d < Directions.Length; d++)
                {
                    int nx = x + Directions[d].x, ny = y + Directions[d].y;
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    {
                        component.Borders.Add(new BoundaryEdge(worldCell + Directions[d]));
                        continue;
                    }
                    int neighbour = ny * width + nx;
                    if (eligible[neighbour] != type || state.ComponentIndices[neighbour] >= 0) continue;
                    state.ComponentIndices[neighbour] = id;
                    frontier[tail++] = neighbour;
                }
            }
        }
        state.Dirty = false;
    }

    private Body ResolveBody(LocalComponent origin)
    {
        if (origin.Body != null && origin.Body.Version == topologyVersion) return origin.Body;
        var body = new Body { Version = topologyVersion, LiquidId = origin.LiquidId };
        var queue = new Queue<LocalComponent>();
        var chunks = new HashSet<ChunkState>();
        origin.Body = body;
        queue.Enqueue(origin);
        while (queue.Count != 0)
        {
            LocalComponent component = queue.Dequeue();
            body.Cells += component.Cells;
            body.TotalLevel += component.TotalLevel;
            chunks.Add(component.State);
            foreach (BoundaryEdge edge in component.Borders)
            {
                Vector2Int neighbour = WorldTopologyRuntime.NormalizeCell(edge.Neighbour);
                if (!TryGetState(neighbour, out _, out _, out _))
                {
                    body.HasUnloadedBoundary = true;
                    continue;
                }
                if (!TryGetComponent(neighbour, out LocalComponent other, out _) ||
                    !string.Equals(other.LiquidId, body.LiquidId, StringComparison.OrdinalIgnoreCase) ||
                    ReferenceEquals(other.Body, body))
                    continue;
                other.Body = body;
                queue.Enqueue(other);
            }
        }
        body.ChunkCount = chunks.Count;
        return body;
    }

    /// <summary>按需返回已加载水域的总量、容量和均值；跨未加载区块只标记不强制加载。</summary>
    public bool TryGetBody(Vector2 worldPosition, out WorldLiquidBodySnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetComponent(Vector2Int.FloorToInt(worldPosition), out LocalComponent component, out _))
            return false;
        Body body = ResolveBody(component);
        snapshot = new WorldLiquidBodySnapshot(body.LiquidId, body.TotalLevel, body.Cells,
            body.ChunkCount, body.HasUnloadedBoundary);
        return true;
    }
    #endregion

    #region 变化唤醒与局部守恒结算
    private void QueueCell(Vector2Int cell)
    {
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        if (pendingSet.Add(cell)) pending.Enqueue(cell);
    }

    private void WakeCross(Vector2Int cell)
    {
        if (disposed || !GameNetwork.HasStateAuthority ||
            owner.LiquidFlowExperiment != null) return;
        QueueCell(cell);
        foreach (Vector2Int direction in Directions) QueueCell(cell + direction);
    }

    private void WakeJoiningBorders(ChunkState state)
    {
        int width = state.Terrain.Width, height = state.Terrain.Height;
        for (int y = 0; y < height; y++)
        {
            WakeIfConnected(state, 0, y, Vector2Int.left);
            WakeIfConnected(state, width - 1, y, Vector2Int.right);
        }
        for (int x = 0; x < width; x++)
        {
            WakeIfConnected(state, x, 0, Vector2Int.down);
            WakeIfConnected(state, x, height - 1, Vector2Int.up);
        }
    }

    private void WakeIfConnected(ChunkState state, int x, int y, Vector2Int direction)
    {
        if (state.Terrain.GetLiquidBaseline(x, y).LiquidLevel <= 0 &&
            !CanHoldChannelWater(state, x, y)) return;
        Vector2Int from = WorldCell(state, x, y);
        if (!TryGetState(from + direction, out ChunkState other, out int nx, out int ny) ||
            (other.Terrain.GetLiquidBaseline(nx, ny).LiquidLevel <= 0 &&
             !CanHoldChannelWater(other, nx, ny)) ||
            (!state.Terrain.IsLiquidChanged(x, y) &&
             !other.Terrain.IsLiquidChanged(nx, ny) &&
             !CanHoldChannelWater(state, x, y) && !CanHoldChannelWater(other, nx, ny)) ||
            Math.Abs(state.Terrain.GetLiquidLevel(x, y) - other.Terrain.GetLiquidLevel(nx, ny)) < 2)
            return;
        WakeCross(from);
    }

    /// <summary>已提交取水优先唤醒整个静水连接组；非天然小水域仍由旧局部规则处理。</summary>
    public bool TryWake(RuntimeTerrainTileSample source)
    {
        if (!GameNetwork.HasStateAuthority || disposed || owner.LiquidFlowExperiment != null ||
            !TryGetComponent(source.WorldCell, out LocalComponent component, out RuntimeTerrainTileSample current) ||
            !ReferenceEquals(current.Terrain, source.Terrain) ||
            ResolveBody(component).Cells < MinimumBodyCells)
            return false;
        WakeCross(source.WorldCell);
        return true;
    }

    /// <summary>没有水域变更时零扫描；有限队列逐帧沿真实邻格传播，不会把整片湖泊瞬间均压。</summary>
    public void Advance()
    {
        if (disposed || world.Epoch != epoch) return;
        if (owner.LiquidFlowExperiment != null)
        {
            pending.Clear();
            pendingSet.Clear();
            return;
        }
        if (pending.Count == 0 || !GameNetwork.HasStateAuthority) return;
        applying = true;
        try
        {
            int processed = 0;
            while (pending.Count > 0 && processed++ < WorkCellsPerFrame)
            {
                Vector2Int position = pending.Dequeue();
                pendingSet.Remove(position);
                if (!TryGetComponent(position, out LocalComponent component, out RuntimeTerrainTileSample current) ||
                    ResolveBody(component).Cells < MinimumBodyCells)
                    continue;
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int next = WorldTopologyRuntime.NormalizeCell(position + direction);
                    if (!TryGetComponent(next, out LocalComponent neighbour, out RuntimeTerrainTileSample sample) ||
                        !string.Equals(component.LiquidId, neighbour.LiquidId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    int a = current.Terrain.GetLiquidLevel(current.LocalCell.x, current.LocalCell.y);
                    int b = sample.Terrain.GetLiquidLevel(sample.LocalCell.x, sample.LocalCell.y);
                    if (Math.Abs(a - b) < 2) continue;
                    RuntimeTerrainTileSample donor = a > b ? current : sample;
                    RuntimeTerrainTileSample receiver = a > b ? sample : current;
                    if (!WorldLiquidReplenishment.TryTransfer(donor, receiver, true)) continue;
                    WakeCross(position);
                    WakeCross(next);
                }
            }
        }
        finally { applying = false; }
    }
    #endregion
}
