using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using Unity.Profiling;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// 默认关闭的液体实验运行器。外部液体/墙体变化唤醒局部区域，独立 5Hz Tick，单帧最多一步。
/// 仅活动 Chunk 持有模拟租约；高度、流向和活动集合不进存档。世界切换、停用和失败都释放租约。
/// </summary>
public sealed class WorldLiquidFlowExperiment : IDisposable
{
    #region 状态与诊断
    private static readonly ProfilerMarker TickMarker = new("FlatWorld.ExperimentalLiquidFlow.Tick");
    private static readonly Vector2Int[] Directions = { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };
    private static readonly Comparison<State> StateComparison = CompareState; // 避免每 Tick 创建排序委托。
    private readonly ChunkMgr owner;
    private readonly LiquidFlowSettings settings;
    private readonly WorldRuntime world;
    private readonly long epoch;
    private readonly string dimension;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Dictionary<RuntimeWorldAddress, State> observed = new();
    private readonly List<State> active = new();
    private readonly List<State> tickStates = new();
    private readonly HashSet<RuntimeWorldAddress> regions = new();
    private readonly HashSet<RuntimeWorldAddress> ready = new();
    private readonly HashSet<RuntimeWorldAddress> queued = new();
    private readonly HashSet<RuntimeWorldAddress> loading = new();
    private readonly HashSet<RuntimeWorldAddress> failed = new();
    private readonly List<RuntimeWorldAddress> addresses = new();
    private readonly List<LiquidFlowCell> cells = new();
    private readonly List<Location> locations = new();
    private readonly Dictionary<Vector2Int, int> nodeIndices = new();
    private readonly LiquidFlowSolver solver = new();
    private readonly IDisposable committedSubscription;
    private readonly IDisposable evictedSubscription;
    private int[] neighbours = Array.Empty<int>();
    private float elapsed;
    private bool disposed;
    private bool applying;
    public ulong TickCount { get; private set; }
    public int LastTransferCount => solver.TransferCount;
    public int ActiveChunkCount => active.Count;
    public int PendingChunkCount => loading.Count + queued.Count;
    public int ActiveCellCount { get { int count = 0; foreach (State state in active) count += state.ActiveCount; return count; } }
    /// <summary>只通知临时流向变化；表现层应合并至帧末，不读取或修改存档。</summary>
    public static event Action<ChunkTerrainData> FlowChanged;

    private readonly struct Location
    {
        public readonly State State;
        public readonly int Index;
        public readonly Vector2Int WorldCell;
        public Location(State state, int index, Vector2Int cell) { State = state; Index = index; WorldCell = cell; }
    }

    /// <summary>一个已观察区块的缓存；活动格稀疏迭代，密集时扫描 256 格。</summary>
    private sealed class State
    {
        public readonly WorldLiquidFlowExperiment Host;
        public readonly ChunkRuntime Chunk;
        public readonly ChunkTerrainData Terrain;
        public readonly float[] GroundHeightCache;
        public readonly bool[] ActiveCells, Woken;
        public readonly byte[] QuietTicks;
        public readonly Vector2[] Flow;
        public readonly List<int> ActiveIndices = new();
        public readonly LiquidCellUpdate[] Updates;
        public readonly int[] DirtyIndices;
        public int ActiveCount, UpdateCount;
        public bool FlowDirty;
        public ChunkLease Lease;

        public State(WorldLiquidFlowExperiment host, ChunkRuntime chunk)
        {
            Host = host; Chunk = chunk; Terrain = chunk.Terrain;
            int count = Terrain.CellCount;
            GroundHeightCache = new float[count]; ActiveCells = new bool[count]; Woken = new bool[count];
            QuietTicks = new byte[count]; Flow = new Vector2[count];
            Updates = new LiquidCellUpdate[count]; DirtyIndices = new int[count];
            for (int i = 0; i < count; i++)
            {
                // 复用冻结规则生成并留在区块内的 height；无高度层的洞穴采用平底，绝不按液深反算。
                Terrain.TryGetSurfaceElevation(i % Terrain.Width, i / Terrain.Width, out float height);
                if (!float.IsFinite(height)) throw new InvalidOperationException("液体模拟遇到非法生成高度。");
                GroundHeightCache[i] = height;
            }
            Terrain.Changed += OnChanged;
            Terrain.LiquidBatchChanged += OnLiquidBatch;
        }

        public void OnChanged(ChunkTerrainChanged change)
        {
            if (Host.applying || change.Kind != TerrainChangeKind.Liquid && change.Kind != TerrainChangeKind.Cell &&
                change.Kind != TerrainChangeKind.TileStack) return;
            Host.Wake(new Vector2Int(Chunk.Address.ChunkOrigin.X + change.LocalCell.X,
                Chunk.Address.ChunkOrigin.Y + change.LocalCell.Y));
        }

        public void OnLiquidBatch(ChunkLiquidBatchChanged change)
        {
            if (Host.applying) return;
            SaveDataMgr.Instance?.RecordLiquidBatch(Chunk.Address, Terrain, change.CellIndices.Span);
            foreach (int index in change.CellIndices.Span)
                Host.Wake(new Vector2Int(Chunk.Address.ChunkOrigin.X + index % Terrain.Width,
                    Chunk.Address.ChunkOrigin.Y + index / Terrain.Width));
        }

        public void Dispose()
        {
            Terrain.Changed -= OnChanged;
            Terrain.LiquidBatchChanged -= OnLiquidBatch;
            Lease?.Dispose(); Lease = null;
        }
    }
    #endregion

    #region 生命周期与唤醒
    public WorldLiquidFlowExperiment(ChunkMgr owner, LiquidFlowSettings settings, string dimension)
    {
        settings.Validate();
        this.owner = owner; this.settings = settings.Snapshot(); this.dimension = dimension;
        world = owner.WorldRuntime; epoch = world.Epoch;
        committedSubscription = world.Events.Subscribe<ChunkCommitted>(OnCommitted);
        evictedSubscription = world.Events.Subscribe<ChunkEvicted>(OnEvicted);
        WorldLiquidFlowObstacles.Changed += Wake;
        foreach (var pair in owner.Chunks)
            if (pair.Key.DimensionId == dimension && pair.Value.DataStatus == ChunkDataStatus.Ready) ready.Add(pair.Key);
    }

    /// <summary>只在真实修改或开发者显式操作时扩展模拟范围，模拟传播本身不能无限扩大范围。</summary>
    public void Wake(Vector2Int worldCell)
    {
        if (disposed || !GameNetwork.HasStateAuthority) return;
        worldCell = WorldTopologyRuntime.NormalizeCell(worldCell);
        RuntimeWorldAddress center = owner.ResolveWorldAddress(worldCell, dimension);
        int width = owner.ActiveGenerationProfile.Width, height = owner.ActiveGenerationProfile.Height;
        for (int y = -settings.RegionRadiusChunks; y <= settings.RegionRadiusChunks; y++)
        for (int x = -settings.RegionRadiusChunks; x <= settings.RegionRadiusChunks; x++)
            regions.Add(owner.ResolveWorldAddress(new Vector2(center.ChunkOrigin.X + x * width,
                center.ChunkOrigin.Y + y * height), dimension));
        failed.Clear();
        WakeCross(worldCell);
    }

    /// <summary>主线程只读取已准备好的区块；差量和建筑恢复必须先于订阅与激活。</summary>
    public void Observe(ChunkRuntime chunk)
    {
        if (disposed || chunk?.Terrain == null || chunk.Terrain.IsDisposed || chunk.Address.DimensionId != dimension) return;
        if (observed.TryGetValue(chunk.Address, out State old))
        {
            if (ReferenceEquals(old.Terrain, chunk.Terrain)) return;
            RemoveState(old);
        }
        var state = new State(this, chunk);
        observed.Add(chunk.Address, state);
        for (int i = 0; i < state.Terrain.CellCount; i++)
            if (state.Terrain.IsLiquidChanged(i % state.Terrain.Width, i / state.Terrain.Width))
                Wake(WorldCell(state, i));
    }

    private void OnCommitted(ChunkCommitted message) { if (message.Address.DimensionId == dimension) ready.Add(message.Address); }
    private void OnEvicted(ChunkEvicted message)
    {
        ready.Remove(message.Address);
        if (observed.TryGetValue(message.Address, out State state)) RemoveState(state);
    }

    /// <summary>关闭实验时保留已写入的液深，只释放运行缓存；不逆向修改玩家世界。</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { cancellation.Cancel(); }
        catch (Exception exception) { Debug.LogException(exception); }
        WorldLiquidFlowObstacles.Changed -= Wake;
        committedSubscription.Dispose(); evictedSubscription.Dispose();
        foreach (State state in observed.Values)
        {
            ClearFlow(state);
            state.Dispose();
            if (state.FlowDirty)
            {
                // MOD 表现观察者异常不能中断其余区块的租约释放。
                try { FlowChanged?.Invoke(state.Terrain); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
        observed.Clear(); active.Clear(); ready.Clear(); queued.Clear(); regions.Clear();
        cancellation.Dispose();
    }
    #endregion

    #region 独立 Tick 与数据窗口
    /// <summary>没有活动格就不累计时钟；掉帧时丢弃历史积欠，单帧最多计算一个 Tick。</summary>
    public void Advance(float seconds)
    {
        if (disposed || world.Epoch != epoch || !GameNetwork.HasStateAuthority) return;
        PrepareReadyChunks();
        StartQueuedLoads();
        if (active.Count == 0) { elapsed = 0f; return; }
        if (!float.IsFinite(seconds) || seconds <= 0f) return;
        elapsed += seconds;
        if (elapsed < 1f / settings.TickRate) return;
        elapsed = 0f;
        using (TickMarker.Auto()) Tick();
    }

    /// <summary>提交结果后统一恢复差量；预取的数据也不能绕过存档覆盖阶段。</summary>
    private void PrepareReadyChunks()
    {
        addresses.Clear(); addresses.AddRange(ready); ready.Clear(); addresses.Sort();
        foreach (RuntimeWorldAddress address in addresses)
        {
            if (observed.ContainsKey(address) || !owner.TryGetChunkRuntime(address, out ChunkRuntime chunk) ||
                chunk.DataStatus != ChunkDataStatus.Ready) continue;
            SaveDataMgr.Instance?.RestoreRuntimeTerrainForChunk(address, chunk);
            SaveDataMgr.Instance?.RestoreRuntimeBuildingsForChunk(address);
            Observe(chunk);
        }
    }

    /// <summary>有上限地请求范围内缺失邻区；请求期间持有租约，不能直接写不存在的格子。</summary>
    private void StartQueuedLoads()
    {
        addresses.Clear(); addresses.AddRange(queued); addresses.Sort();
        foreach (RuntimeWorldAddress address in addresses)
        {
            if (loading.Count >= settings.MaxPendingLoads || active.Count + loading.Count >= settings.MaxActiveChunks) break;
            queued.Remove(address);
            if (observed.TryGetValue(address, out State state))
            {
                for (int i = 0; i < state.Terrain.CellCount; i++) WakeCross(WorldCell(state, i));
                continue;
            }
            if (!failed.Contains(address) && !loading.Contains(address)) LoadNeighbour(address);
        }
    }

    private async void LoadNeighbour(RuntimeWorldAddress address)
    {
        loading.Add(address);
        ChunkLease lease = owner.AcquireChunkLease(address, ChunkLeaseKind.Simulation);
        try
        {
            ChunkRuntime chunk = await owner.RequestLiquidFlowChunkAsync(address, cancellation.Token);
            if (disposed || world.Epoch != epoch) return;
            SaveDataMgr.Instance?.RestoreRuntimeTerrainForChunk(address, chunk);
            SaveDataMgr.Instance?.RestoreRuntimeBuildingsForChunk(address);
            loading.Remove(address);
            Observe(chunk);
            // 就绪后同时唤醒共享边两侧，恢复此前因为数据缺失而休眠的来源格。
            for (int i = 0; i < chunk.Terrain.CellCount; i++)
                WakeCross(new Vector2Int(address.ChunkOrigin.X + i % chunk.Terrain.Width,
                    address.ChunkOrigin.Y + i / chunk.Terrain.Width));
        }
        catch (OperationCanceledException) { if (!disposed) queued.Add(address); }
        catch (Exception exception)
        {
            failed.Add(address);
            Debug.LogError($"[LiquidFlow] 邻区准备失败 {address}：{exception.Message}");
        }
        finally { loading.Remove(address); lease.Dispose(); }
    }

    private void WakeCross(Vector2Int cell)
    {
        WakeCell(cell);
        for (int i = 0; i < 4; i++) WakeCell(cell + Directions[i]);
    }

    private void WakeCell(Vector2Int cell)
    {
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        RuntimeWorldAddress address = owner.ResolveWorldAddress(cell, dimension);
        if (!regions.Contains(address)) return;
        if (!observed.TryGetValue(address, out State state)) { QueueLoad(address); return; }
        if (!EnsureLease(state)) { queued.Add(address); return; }
        int index = (cell.y - address.ChunkOrigin.Y) * state.Terrain.Width + cell.x - address.ChunkOrigin.X;
        if ((uint)index >= (uint)state.Terrain.CellCount) return;
        if (!state.ActiveCells[index]) { state.ActiveCells[index] = true; state.ActiveCount++; state.ActiveIndices.Add(index); }
        state.QuietTicks[index] = 0;
        state.Woken[index] = true;
    }

    private bool EnsureLease(State state)
    {
        if (state.Lease != null) return true;
        if (active.Count + loading.Count >= settings.MaxActiveChunks) return false;
        state.Lease = owner.AcquireChunkLease(state.Chunk.Address, ChunkLeaseKind.Simulation);
        state.UpdateCount = 0;
        active.Add(state);
        return true;
    }

    private void QueueLoad(RuntimeWorldAddress address)
    {
        if (!loading.Contains(address) && !failed.Contains(address)) queued.Add(address);
    }
    #endregion

    #region 冻结输入与统一写回
    /// <summary>固定区块/格子顺序；先构造所有快照，再求解、写回、保存内存差量、最后发布通知。</summary>
    private void Tick()
    {
        tickStates.Clear(); tickStates.AddRange(active); tickStates.Sort(StateComparison);
        cells.Clear(); locations.Clear(); nodeIndices.Clear();
        foreach (State state in tickStates)
        {
            Array.Clear(state.Woken, 0, state.Woken.Length);
            ClearFlow(state);
            if (state.ActiveCount >= state.Terrain.CellCount * settings.DenseThreshold)
            {
                for (int i = 0; i < state.Terrain.CellCount; i++) CaptureCross(WorldCell(state, i));
            }
            else
            {
                state.ActiveIndices.Sort();
                for (int i = 0; i < state.ActiveIndices.Count; i++) CaptureCross(WorldCell(state, state.ActiveIndices[i]));
            }
        }
        if (neighbours.Length < cells.Count * 4) neighbours = new int[Math.Max(1024, cells.Count * 8)];
        for (int i = 0; i < locations.Count; i++)
        for (int direction = 0; direction < 4; direction++)
        {
            Vector2Int neighbour = WorldTopologyRuntime.NormalizeCell(locations[i].WorldCell + Directions[direction]);
            neighbours[i * 4 + direction] = nodeIndices.TryGetValue(neighbour, out int index) ? index : -1;
        }
        solver.Calculate(cells, neighbours.AsSpan(0, cells.Count * 4), settings);
        WriteResults();
        AgeActiveCells();
        TickCount++;
    }

    private static int CompareState(State left, State right) => left.Chunk.Address.CompareTo(right.Chunk.Address);

    private void CaptureCross(Vector2Int cell)
    {
        CaptureCell(cell);
        for (int i = 0; i < 4; i++) CaptureCell(cell + Directions[i]);
    }

    private void CaptureCell(Vector2Int cell)
    {
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        if (nodeIndices.ContainsKey(cell)) return;
        RuntimeWorldAddress address = owner.ResolveWorldAddress(cell, dimension);
        if (!regions.Contains(address)) return;
        if (!observed.TryGetValue(address, out State state)) { QueueLoad(address); return; }
        if (!EnsureLease(state)) return;
        int x = cell.x - address.ChunkOrigin.X, y = cell.y - address.ChunkOrigin.Y;
        if ((uint)x >= (uint)state.Terrain.Width || (uint)y >= (uint)state.Terrain.Height) return;
        int index = y * state.Terrain.Width + x;
        TerrainCell terrainCell = state.Terrain.GetCell(x, y);
        bool blocked = terrainCell.BlockingTileId != 0 || (terrainCell.Flags & TerrainCellFlags.Blocking) != 0 ||
            WorldLiquidFlowObstacles.IsBlocked(cell);
        nodeIndices.Add(cell, cells.Count);
        locations.Add(new Location(state, index, cell));
        cells.Add(new LiquidFlowCell(state.GroundHeightCache[index], state.Terrain.GetLiquidDepth(x, y),
            state.Terrain.GetLiquidTypeIndex(x, y), state.Terrain.GetLiquidId(x, y), blocked, state.ActiveCells[index]));
    }

    private void WriteResults()
    {
        foreach (State state in active) state.UpdateCount = 0;
        for (int i = 0; i < locations.Count; i++)
        {
            Location location = locations[i]; State state = location.State;
            Vector2 flow = new(solver.FlowX[i], solver.FlowY[i]);
            if (state.Flow[location.Index] != flow) { state.Flow[location.Index] = flow; state.FlowDirty = true; }
            if (solver.ResultDepth[i] == cells[i].Depth && solver.ResultType[i] == cells[i].TypeIndex) continue;
            state.Updates[state.UpdateCount++] = new LiquidCellUpdate(location.Index, solver.ResultType[i], solver.ResultDepth[i]);
            WakeCross(location.WorldCell);
        }
        applying = true;
        try
        {
            // 所有格子的输入已经冻结，排序仅用于批次契约，不影响流量求解。
            foreach (State state in active)
            {
                if (state.UpdateCount == 0) continue;
                Array.Sort(state.Updates, 0, state.UpdateCount, UpdateComparer.Instance);
                state.Terrain.SetLiquidBatch(state.Updates.AsSpan(0, state.UpdateCount), true);
                for (int i = 0; i < state.UpdateCount; i++) state.DirtyIndices[i] = state.Updates[i].CellIndex;
            }
            foreach (State state in active)
                if (state.UpdateCount > 0)
                    SaveDataMgr.Instance?.RecordLiquidBatch(state.Chunk.Address, state.Terrain,
                        state.DirtyIndices.AsSpan(0, state.UpdateCount));
        }
        finally
        {
            try
            {
                PublishResults();
            }
            finally { applying = false; }
        }
    }

    /// <summary>一个观察者失败也要结束其它 Chunk 的批次，不能留下永久阻止后续写入的待发布状态。</summary>
    private void PublishResults()
    {
        Exception firstFailure = null;
        foreach (State state in active)
        {
            try
            {
                state.Terrain.PublishLiquidBatch();
                if (state.FlowDirty) FlowChanged?.Invoke(state.Terrain);
            }
            catch (Exception exception) { firstFailure ??= exception; }
            finally { state.FlowDirty = false; }
        }
        if (firstFailure != null) throw new InvalidOperationException("液体批次观察者处理失败。", firstFailure);
    }

    private sealed class UpdateComparer : IComparer<LiquidCellUpdate>
    {
        public static readonly UpdateComparer Instance = new();
        public int Compare(LiquidCellUpdate x, LiquidCellUpdate y) => x.CellIndex.CompareTo(y.CellIndex);
    }

    private void AgeActiveCells()
    {
        for (int s = active.Count - 1; s >= 0; s--)
        {
            State state = active[s];
            for (int i = state.ActiveIndices.Count - 1; i >= 0; i--)
            {
                int index = state.ActiveIndices[i];
                if (state.Woken[index] || ++state.QuietTicks[index] < settings.SleepTicks) continue;
                state.ActiveCells[index] = false; state.ActiveCount--; state.ActiveIndices.RemoveAt(i);
            }
            if (state.ActiveCount != 0) continue;
            ClearFlow(state);
            if (state.FlowDirty) FlowChanged?.Invoke(state.Terrain);
            state.FlowDirty = false;
            state.Lease?.Dispose(); state.Lease = null; active.RemoveAt(s);
        }
        if (active.Count == 0 && loading.Count == 0 && queued.Count == 0) regions.Clear();
    }

    private static Vector2Int WorldCell(State state, int index) => new(
        state.Chunk.Address.ChunkOrigin.X + index % state.Terrain.Width,
        state.Chunk.Address.ChunkOrigin.Y + index / state.Terrain.Width);

    private static void ClearFlow(State state)
    {
        for (int i = 0; i < state.Flow.Length; i++)
            if (state.Flow[i] != Vector2.zero) { state.Flow[i] = Vector2.zero; state.FlowDirty = true; }
    }

    private void RemoveState(State state)
    {
        state.Dispose(); active.Remove(state); observed.Remove(state.Chunk.Address);
    }

    /// <summary>读取上一个 Tick 的真实转移流向；休眠、关闭或未模拟格返回零，天然水文数据保持独立。</summary>
    public bool TryGetFlow(Vector2Int cell, out Vector2 flow)
    {
        flow = Vector2.zero;
        if (disposed) return false;
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        RuntimeWorldAddress address = owner.ResolveWorldAddress(cell, dimension);
        if (!observed.TryGetValue(address, out State state)) return false;
        int x = cell.x - address.ChunkOrigin.X, y = cell.y - address.ChunkOrigin.Y;
        if ((uint)x >= (uint)state.Terrain.Width || (uint)y >= (uint)state.Terrain.Height) return false;
        flow = state.Flow[y * state.Terrain.Width + x];
        return flow.sqrMagnitude > 0f;
    }
    #endregion
}
