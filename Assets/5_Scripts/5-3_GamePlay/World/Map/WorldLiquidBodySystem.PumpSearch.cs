using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public enum WorldLiquidPumpSourceSelection
{
    Surplus,
    Searching,
    LocalRemainder,
    Unavailable
}

public sealed partial class WorldLiquidBodySystem
{
    #region 水域共享的取水搜索缓存
    private const int PumpSearchBoundaryStepsPerFrame = 128;
    private readonly Dictionary<RuntimeWorldAddress, PumpChunkTopologySnapshot> pumpChunkSnapshots = new();
    private readonly HashSet<SharedPumpWaterSearch> sharedPumpSearches = new();
    private readonly HashSet<SharedPumpWaterSearch> activeSharedPumpSearches = new();
    private readonly Dictionary<RuntimeWorldAddress, List<PumpBoundarySearchWaiter>> pumpBoundaryWaiters = new();
    private readonly List<SharedPumpWaterSearch> pumpSearchWork = new();
    private int nextPumpSearchWork;
    public int SharedPumpSearchGroupCount => sharedPumpSearches.Count;
    public int PumpSearchCachedChunkCount => pumpChunkSnapshots.Count;
    public long PumpSearchTopologyBuildCount { get; private set; }
    public long PumpSearchBoundaryStepCount { get; private set; }

    private sealed class PumpChunkTopologySnapshot
    {
        public RuntimeWorldAddress Address;
        public int Width, Height;
        public ulong TopologyHash;
        public int[] ComponentIndices;
        public readonly List<PumpWetComponent> Components = new();
        public PumpWetComponent ComponentAt(int index)
        {
            int id = ComponentIndices == null ? 0 : ComponentIndices[index];
            return id < 0 ? null : Components[id];
        }
    }

    private sealed class PumpWetComponent
    {
        public PumpChunkTopologySnapshot Chunk;
        public string LiquidId;
        public int CellCount;
        public SharedPumpWaterSearch Search;
        public readonly List<Vector2Int> Borders = new();
        public readonly Queue<int> CandidateIndices = new();
        public readonly HashSet<int> QueuedCandidateIndices = new();
        public void AddCandidate(int index)
        {
            if (QueuedCandidateIndices.Add(index)) CandidateIndices.Enqueue(index);
        }
    }

    private sealed class PumpComponentSearchCursor
    {
        public PumpWetComponent Component;
        public int BorderIndex;
    }

    private sealed class SharedPumpWaterSearch
    {
        public SharedPumpWaterSearch Parent;
        public bool Invalidated, Failed;
        public int WaitingBorders;
        public double LastRequestTime;
        public readonly HashSet<PumpWetComponent> Members = new();
        public readonly Queue<PumpComponentSearchCursor> Frontier = new();
        public readonly Queue<PumpBoundarySearchWaiter> DeferredUnknownBorders = new();
        public readonly Queue<PumpWetComponent> CandidateComponents = new();
        public readonly HashSet<PumpWetComponent> QueuedCandidateComponents = new();
    }

    private readonly struct PumpBoundarySearchWaiter
    {
        public readonly PumpWetComponent Source;
        public readonly Vector2Int Neighbour;
        public PumpBoundarySearchWaiter(PumpWetComponent source, Vector2Int neighbour)
        { Source = source; Neighbour = neighbour; }
    }

    private static SharedPumpWaterSearch ResolveSharedPumpSearch(SharedPumpWaterSearch search)
    {
        while (search?.Parent != null) search = search.Parent;
        return search;
    }

    private static void QueueSharedPumpCandidateComponent(SharedPumpWaterSearch search, PumpWetComponent component)
    {
        if (component.CandidateIndices.Count > 0 && search.QueuedCandidateComponents.Add(component))
            search.CandidateComponents.Enqueue(component);
    }

    private SharedPumpWaterSearch JoinSharedPumpSearch(SharedPumpWaterSearch search, PumpWetComponent component)
    {
        search = ResolveSharedPumpSearch(search);
        SharedPumpWaterSearch existing = ResolveSharedPumpSearch(component.Search);
        if (existing != null && !existing.Invalidated && !ReferenceEquals(existing, search))
        {
            if (existing.Members.Count > search.Members.Count)
            { SharedPumpWaterSearch swap = search; search = existing; existing = swap; }
            existing.Parent = search;
            search.LastRequestTime = Math.Max(search.LastRequestTime, existing.LastRequestTime);
            search.WaitingBorders += existing.WaitingBorders;
            search.Failed |= existing.Failed;
            foreach (PumpWetComponent member in existing.Members)
            {
                member.Search = search;
                search.Members.Add(member);
                QueueSharedPumpCandidateComponent(search, member);
            }
            while (existing.Frontier.Count > 0) search.Frontier.Enqueue(existing.Frontier.Dequeue());
            while (existing.DeferredUnknownBorders.Count > 0)
                search.DeferredUnknownBorders.Enqueue(existing.DeferredUnknownBorders.Dequeue());
            sharedPumpSearches.Remove(existing);
            activeSharedPumpSearches.Remove(existing);
        }
        component.Search = search;
        if (search.Members.Add(component) && component.Borders.Count > 0)
            search.Frontier.Enqueue(new PumpComponentSearchCursor { Component = component });
        QueueSharedPumpCandidateComponent(search, component);
        if (search.Frontier.Count > 0) activeSharedPumpSearches.Add(search);
        return search;
    }

    private void InvalidateSharedPumpSearchChunk(RuntimeWorldAddress address)
    {
        if (!pumpChunkSnapshots.Remove(address, out PumpChunkTopologySnapshot snapshot)) return;
        foreach (PumpWetComponent component in snapshot.Components)
        {
            SharedPumpWaterSearch search = ResolveSharedPumpSearch(component.Search);
            if (search == null) continue;
            search.Invalidated = true;
            sharedPumpSearches.Remove(search);
            activeSharedPumpSearches.Remove(search);
            search.Members.Clear(); search.Frontier.Clear(); search.DeferredUnknownBorders.Clear();
            search.CandidateComponents.Clear(); search.QueuedCandidateComponents.Clear();
        }
    }

    public void InvalidatePumpSearchAtCell(Vector2Int cell) => InvalidateSharedPumpSearchChunk(
        owner.ResolveWorldAddress((Vector2)WorldTopologyRuntime.NormalizeCell(cell) + Vector2.one * .5f, dimension));

    private void NotifySharedPumpSearchLiquidChanged(ChunkState state, int index)
    {
        if (!pumpChunkSnapshots.TryGetValue(state.Chunk.Address, out PumpChunkTopologySnapshot snapshot)) return;
        PumpWetComponent component = snapshot.ComponentAt(index);
        int level = state.Terrain.GetLiquidLevel(index % snapshot.Width, index / snapshot.Width);
        string id = state.Terrain.GetLiquidId(index % snapshot.Width, index / snapshot.Width);
        if ((component == null) != (level == 0) || component != null && level > 0 &&
            !string.Equals(component.LiquidId, id, StringComparison.OrdinalIgnoreCase))
        {
            InvalidateSharedPumpSearchChunk(snapshot.Address);
            return;
        }
        if (level <= 1 || component == null) return;
        component.AddCandidate(index);
        SharedPumpWaterSearch search = ResolveSharedPumpSearch(component.Search);
        if (search != null && !search.Invalidated) QueueSharedPumpCandidateComponent(search, component);
    }

    private PumpChunkTopologySnapshot PrepareSharedPumpChunkSnapshot(ChunkState state)
    {
        ChunkTerrainData terrain = state.Terrain;
        int count = terrain.CellCount, width = terrain.Width, height = terrain.Height;
        int[] wetTypes = new int[count];
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < count; i++)
        {
            int x = i % width, y = i / width;
            if (terrain.GetLiquidLevel(x, y) > 0 && IsOpenStaticCell(state, x, y))
                wetTypes[i] = terrain.GetLiquidTypeIndex(x, y);
            hash = unchecked((hash ^ (uint)wetTypes[i]) * 1099511628211UL);
        }
        if (pumpChunkSnapshots.TryGetValue(state.Chunk.Address, out PumpChunkTopologySnapshot old))
        {
            if (old.TopologyHash == hash && old.Width == width && old.Height == height)
            {
                for (int i = 0; i < count; i++)
                    if (terrain.GetLiquidLevel(i % width, i / width) > 1) old.ComponentAt(i)?.AddCandidate(i);
                foreach (PumpWetComponent component in old.Components)
                {
                    SharedPumpWaterSearch search = ResolveSharedPumpSearch(component.Search);
                    if (search != null && !search.Invalidated) QueueSharedPumpCandidateComponent(search, component);
                }
                return old;
            }
            InvalidateSharedPumpSearchChunk(state.Chunk.Address);
        }
        var snapshot = new PumpChunkTopologySnapshot
        { Address = state.Chunk.Address, Width = width, Height = height, TopologyHash = hash,
            ComponentIndices = new int[count] };
        Array.Fill(snapshot.ComponentIndices, -1);
        int[] frontier = new int[count];
        for (int i = 0; i < count; i++)
        {
            if (wetTypes[i] == 0 || snapshot.ComponentIndices[i] >= 0) continue;
            int type = wetTypes[i], id = snapshot.Components.Count, head = 0, tail = 0;
            var component = new PumpWetComponent { Chunk = snapshot, LiquidId = terrain.LiquidTypes.GetId(type) };
            snapshot.Components.Add(component);
            snapshot.ComponentIndices[i] = id;
            frontier[tail++] = i;
            while (head < tail)
            {
                int index = frontier[head++], x = index % width, y = index / width;
                component.CellCount++;
                if (terrain.GetLiquidLevel(x, y) > 1) component.AddCandidate(index);
                foreach (Vector2Int direction in Directions)
                {
                    int nx = x + direction.x, ny = y + direction.y;
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    { component.Borders.Add(WorldCell(state, x, y) + direction); continue; }
                    int next = ny * width + nx;
                    if (wetTypes[next] != type || snapshot.ComponentIndices[next] >= 0) continue;
                    snapshot.ComponentIndices[next] = id;
                    frontier[tail++] = next;
                }
            }
        }
        // 整块同种水面不保留逐格归属数组，远端只缓存轻量连通摘要。
        if (snapshot.Components.Count == 1 && snapshot.Components[0].CellCount == count)
            snapshot.ComponentIndices = null;
        pumpChunkSnapshots.Add(snapshot.Address, snapshot);
        PumpSearchTopologyBuildCount++;
        return snapshot;
    }

    private bool TryGetSharedPumpComponent(Vector2Int cell, out PumpWetComponent component)
    {
        component = null;
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        if (WorldLiquidFlowObstacles.IsBlocked(cell)) return true;
        RuntimeWorldAddress address = owner.ResolveWorldAddress((Vector2)cell + Vector2.one * .5f, dimension);
        if (!pumpChunkSnapshots.TryGetValue(address, out PumpChunkTopologySnapshot snapshot))
        {
            if (!TryGetState(cell, out ChunkState state, out _, out _)) return false;
            snapshot = PrepareSharedPumpChunkSnapshot(state);
        }
        int x = cell.x - address.ChunkOrigin.X, y = cell.y - address.ChunkOrigin.Y;
        if ((uint)x < (uint)snapshot.Width && (uint)y < (uint)snapshot.Height)
            component = snapshot.ComponentAt(y * snapshot.Width + x);
        return true;
    }

    private WorldLiquidPumpSourceSelection SelectSharedPumpCandidate(SharedPumpWaterSearch search,
        out RuntimeTerrainTileSample source)
    {
        source = default;
        while (search.CandidateComponents.Count > 0)
        {
            PumpWetComponent component = search.CandidateComponents.Peek();
            while (component.CandidateIndices.Count > 0)
            {
                int index = component.CandidateIndices.Peek();
                Vector2Int cell = new(component.Chunk.Address.ChunkOrigin.X + index % component.Chunk.Width,
                    component.Chunk.Address.ChunkOrigin.Y + index / component.Chunk.Width);
                if (!TryGetState(cell, out ChunkState state, out int x, out int y))
                { TryQueueSharedPumpChunkData(component.Chunk.Address); return WorldLiquidPumpSourceSelection.Searching; }
                if (state.Terrain.GetLiquidLevel(x, y) > 1 && IsOpenStaticCell(state, x, y) &&
                    string.Equals(state.Terrain.GetLiquidId(x, y), component.LiquidId, StringComparison.OrdinalIgnoreCase))
                {
                    source = new RuntimeTerrainTileSample(state.Chunk.Address, state.Terrain, cell,
                        new Vector2Int(x, y), state.Terrain.GetCell(x, y), 0);
                    return WorldLiquidPumpSourceSelection.Surplus;
                }
                component.CandidateIndices.Dequeue();
                component.QueuedCandidateIndices.Remove(index);
            }
            search.CandidateComponents.Dequeue();
            search.QueuedCandidateComponents.Remove(component);
        }
        return search.Frontier.Count == 0 && search.DeferredUnknownBorders.Count == 0 && search.WaitingBorders == 0 && !search.Failed
            ? WorldLiquidPumpSourceSelection.LocalRemainder : WorldLiquidPumpSourceSelection.Searching;
    }

    /// <summary>水域共享搜索先取大于一级的真实格；搜索未完成时禁止提前抽空入口。</summary>
    public WorldLiquidPumpSourceSelection SelectConnectedPumpSource(Vector2 position, out RuntimeTerrainTileSample source)
    {
        source = default;
        if (disposed || world.Epoch != epoch || !GameNetwork.HasStateAuthority)
            return WorldLiquidPumpSourceSelection.Unavailable;
        Vector2Int inlet = WorldTopologyRuntime.NormalizeCell(Vector2Int.FloorToInt(position));
        if (!TryGetState(inlet, out ChunkState inletState, out int ix, out int iy))
        {
            TryQueueSharedPumpChunkData(owner.ResolveWorldAddress((Vector2)inlet + Vector2.one * .5f, dimension));
            return WorldLiquidPumpSourceSelection.Searching;
        }
        if (!IsOpenStaticCell(inletState, ix, iy)) return WorldLiquidPumpSourceSelection.Unavailable;
        bool localWet = inletState.Terrain.GetLiquidLevel(ix, iy) > 0, waiting = false;
        LiquidCellValue inletBaseline = inletState.Terrain.GetLiquidBaseline(ix, iy);
        string intendedLiquid = localWet ? inletState.Terrain.GetLiquidId(ix, iy) :
            inletBaseline.LiquidLevel > 0 ? inletState.Terrain.LiquidTypes.GetId(inletBaseline.LiquidTypeIndex) : null;
        int choices = localWet ? 1 : Directions.Length;
        for (int i = 0; i < choices; i++)
        {
            Vector2Int seed = localWet ? inlet : WorldTopologyRuntime.NormalizeCell(inlet + Directions[i]);
            if (!TryGetSharedPumpComponent(seed, out PumpWetComponent component))
            {
                TryQueueSharedPumpChunkData(owner.ResolveWorldAddress((Vector2)seed + Vector2.one * .5f, dimension));
                waiting = true;
                continue;
            }
            if (component == null) continue;
            if (intendedLiquid != null && !string.Equals(intendedLiquid, component.LiquidId, StringComparison.OrdinalIgnoreCase)) continue;
            SharedPumpWaterSearch search = ResolveSharedPumpSearch(component.Search);
            if (search == null || search.Invalidated)
            {
                search = new SharedPumpWaterSearch();
                sharedPumpSearches.Add(search);
                search = JoinSharedPumpSearch(search, component);
            }
            search.LastRequestTime = Time.unscaledTimeAsDouble;
            WorldLiquidPumpSourceSelection selected = SelectSharedPumpCandidate(search, out source);
            if (search.Frontier.Count > 0 || search.CandidateComponents.Count == 0 && search.DeferredUnknownBorders.Count > 0)
                activeSharedPumpSearches.Add(search);
            if (selected == WorldLiquidPumpSourceSelection.Surplus) return selected;
            waiting |= selected == WorldLiquidPumpSourceSelection.Searching;
        }
        if (waiting) return WorldLiquidPumpSourceSelection.Searching;
        if (!localWet) return WorldLiquidPumpSourceSelection.Unavailable;
        source = new RuntimeTerrainTileSample(inletState.Chunk.Address, inletState.Terrain, inlet,
            new Vector2Int(ix, iy), inletState.Terrain.GetCell(ix, iy), 0);
        return WorldLiquidPumpSourceSelection.LocalRemainder;
    }

    private void AdvanceSharedPumpSearches()
    {
        pumpSearchWork.Clear(); pumpSearchWork.AddRange(activeSharedPumpSearches);
        int count = pumpSearchWork.Count, budget = PumpSearchBoundaryStepsPerFrame;
        for (int n = 0; n < count && budget > 0; n++)
        {
            SharedPumpWaterSearch search = ResolveSharedPumpSearch(pumpSearchWork[(nextPumpSearchWork + n) % count]);
            if (search.Invalidated ||
                Time.unscaledTimeAsDouble - search.LastRequestTime > 5d)
            { activeSharedPumpSearches.Remove(search); continue; }
            int fairBudget = Math.Max(1, budget / Math.Max(1, count - n));
            while (search.Frontier.Count > 0 && fairBudget-- > 0 && budget-- > 0)
            {
                PumpComponentSearchCursor cursor = search.Frontier.Dequeue();
                Vector2Int next = WorldTopologyRuntime.NormalizeCell(cursor.Component.Borders[cursor.BorderIndex]);
                PumpSearchBoundaryStepCount++;
                if (TryGetSharedPumpComponent(next, out PumpWetComponent component))
                {
                    if (component != null && string.Equals(cursor.Component.LiquidId, component.LiquidId, StringComparison.OrdinalIgnoreCase))
                        search = JoinSharedPumpSearch(search, component);
                    cursor.BorderIndex++;
                }
                else if (search.CandidateComponents.Count > 0)
                {
                    search.DeferredUnknownBorders.Enqueue(new PumpBoundarySearchWaiter(cursor.Component, next));
                    cursor.BorderIndex++;
                }
                else if (TryQueueSharedPumpChunkData(owner.ResolveWorldAddress((Vector2)next + Vector2.one * .5f, dimension)))
                {
                    RuntimeWorldAddress address = owner.ResolveWorldAddress((Vector2)next + Vector2.one * .5f, dimension);
                    if (!pumpBoundaryWaiters.TryGetValue(address, out var waiters))
                        pumpBoundaryWaiters.Add(address, waiters = new List<PumpBoundarySearchWaiter>());
                    waiters.Add(new PumpBoundarySearchWaiter(cursor.Component, next));
                    search.WaitingBorders++;
                    cursor.BorderIndex++;
                }
                if (cursor.BorderIndex < cursor.Component.Borders.Count) search.Frontier.Enqueue(cursor);
            }
            while (search.CandidateComponents.Count == 0 && search.DeferredUnknownBorders.Count > 0 && fairBudget-- > 0 && budget-- > 0)
            {
                PumpBoundarySearchWaiter waiter = search.DeferredUnknownBorders.Dequeue();
                Vector2Int next = WorldTopologyRuntime.NormalizeCell(waiter.Neighbour);
                PumpSearchBoundaryStepCount++;
                if (TryGetSharedPumpComponent(next, out PumpWetComponent component))
                {
                    if (component != null && string.Equals(waiter.Source.LiquidId, component.LiquidId, StringComparison.OrdinalIgnoreCase))
                        search = JoinSharedPumpSearch(search, component);
                }
                else
                {
                    RuntimeWorldAddress address = owner.ResolveWorldAddress((Vector2)next + Vector2.one * .5f, dimension);
                    if (!TryQueueSharedPumpChunkData(address)) { search.DeferredUnknownBorders.Enqueue(waiter); break; }
                    if (!pumpBoundaryWaiters.TryGetValue(address, out var waiters))
                        pumpBoundaryWaiters.Add(address, waiters = new List<PumpBoundarySearchWaiter>());
                    waiters.Add(waiter);
                    search.WaitingBorders++;
                }
            }
            if (search.Frontier.Count == 0 && (search.CandidateComponents.Count > 0 || search.DeferredUnknownBorders.Count == 0))
                activeSharedPumpSearches.Remove(search);
        }
        if (count > 0) nextPumpSearchWork = (nextPumpSearchWork + 1) % count;
    }

    private void NotifySharedPumpChunkDataReady(RuntimeWorldAddress address, bool restored)
    {
        if (restored && observed.TryGetValue(address, out ChunkState state)) PrepareSharedPumpChunkSnapshot(state);
        if (!pumpBoundaryWaiters.Remove(address, out var waiters)) return;
        foreach (PumpBoundarySearchWaiter waiter in waiters)
        {
            SharedPumpWaterSearch search = ResolveSharedPumpSearch(waiter.Source.Search);
            if (search == null || search.Invalidated) continue;
            search.WaitingBorders = Math.Max(0, search.WaitingBorders - 1);
            if (!restored) { search.Failed = true; continue; }
            if (TryGetSharedPumpComponent(waiter.Neighbour, out PumpWetComponent component) && component != null &&
                string.Equals(waiter.Source.LiquidId, component.LiquidId, StringComparison.OrdinalIgnoreCase))
                JoinSharedPumpSearch(search, component);
        }
    }

    private bool SharedPumpCandidateNeedsChunkData(RuntimeWorldAddress address)
    {
        if (!pumpChunkSnapshots.TryGetValue(address, out var snapshot)) return false;
        foreach (PumpWetComponent component in snapshot.Components)
        {
            SharedPumpWaterSearch search = ResolveSharedPumpSearch(component.Search);
            if (search != null && !search.Invalidated && component.CandidateIndices.Count > 0 &&
                Time.unscaledTimeAsDouble - search.LastRequestTime <= 5d) return true;
        }
        return false;
    }

    private void ClearSharedPumpSearches()
    {
        pumpChunkSnapshots.Clear(); sharedPumpSearches.Clear(); activeSharedPumpSearches.Clear();
        pumpBoundaryWaiters.Clear(); pumpSearchWork.Clear();
    }
    #endregion
}
