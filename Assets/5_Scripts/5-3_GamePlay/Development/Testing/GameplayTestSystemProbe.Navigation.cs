using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 独立导航网格
    private static void RegisterNavigationOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("navigation/weighted_path", NavigationPath);
        operations.Add("navigation/blockers", NavigationBlockers);
        operations.Add("navigation/grid_changes", NavigationChanges);
        operations.Add("navigation/corner", NavigationCorner);
        operations.Add("navigation/canonical_batch", NavigationCanonicalBatch);
        operations.Add("navigation/chunk_block_pool", NavigationChunkBlockPool);
        operations.Add("navigation/liquid_cell_snapshot", NavigationLiquidCellSnapshot);
        operations.Add("navigation/machine_obstacle_dirty", NavigationMachineObstacleDirty);
        operations.Add("navigation/avoidance_topology", NavigationAvoidanceTopology);
        operations.Add("navigation/topology_bounds_cache", NavigationTopologyBoundsCache);
    }

    /// <summary>缓存必须和正式世界边界算法一致，动态配置或星球身份变化立即失效。</summary>
    private static JObject NavigationTopologyBoundsCache(JObject args)
    {
        var cache = new WorldTopologyRuntime.ActiveBoundsCache();
        var planet = new PlanetData
        {
            TopologyMode = WorldTopologyMode.Wrapped,
            Radius = 9,
            ChunkSize = new Vector2Int(4, 4)
        };

        static bool MatchesBaseline(WorldTopologyRuntime.ActiveBoundsCache cache, PlanetData planet)
        {
            bool expectedValid = WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds expected);
            bool actualValid = cache.TryGet(planet, out WorldTopologyBounds actual);
            return expectedValid == actualValid && (!actualValid ||
                expected.Min == actual.Min && expected.MaxExclusive == actual.MaxExclusive &&
                expected.Span == actual.Span && expected.ChunkSize == actual.ChunkSize &&
                expected.Mode == actual.Mode);
        }

        bool initial = MatchesBaseline(cache, planet) && cache.RebuildCount == 1;
        bool reused = true;
        for (int i = 0; i < 512; i++)
            reused &= MatchesBaseline(cache, planet);
        reused &= cache.RebuildCount == 1;

        planet.Radius = 23;
        bool radiusRefresh = MatchesBaseline(cache, planet) && cache.RebuildCount == 2;
        planet.ChunkSize = new Vector2Int(6, 4);
        bool chunkRefresh = MatchesBaseline(cache, planet) && cache.RebuildCount == 3;
        planet.ChunkSize = new Vector2Int(0, 4);
        bool invalidCached = MatchesBaseline(cache, planet) && MatchesBaseline(cache, planet) &&
            cache.RebuildCount == 4;
        planet.ChunkSize = new Vector2Int(6, 4);
        bool recovered = MatchesBaseline(cache, planet) && cache.RebuildCount == 5;
        planet.TopologyMode = WorldTopologyMode.Infinite;
        bool infinite = MatchesBaseline(cache, planet) && cache.RebuildCount == 6;
        planet.TopologyMode = WorldTopologyMode.Wrapped;
        bool wrapped = MatchesBaseline(cache, planet) && cache.RebuildCount == 7;

        var other = new PlanetData
        {
            TopologyMode = WorldTopologyMode.Wrapped,
            Radius = planet.Radius,
            ChunkSize = planet.ChunkSize
        };
        bool differentPlanet = MatchesBaseline(cache, other) && cache.RebuildCount == 8;
        bool missingPlanet = MatchesBaseline(cache, null) && MatchesBaseline(cache, null) &&
            cache.RebuildCount == 9;
        bool returned = MatchesBaseline(cache, planet) && cache.RebuildCount == 10;
        cache.Clear();
        bool reset = cache.RebuildCount == 0 && MatchesBaseline(cache, planet) &&
            cache.RebuildCount == 1;

        return new JObject
        {
            ["initial"] = initial, ["reused"] = reused, ["radiusRefresh"] = radiusRefresh,
            ["chunkRefresh"] = chunkRefresh, ["invalidCached"] = invalidCached,
            ["recovered"] = recovered, ["infinite"] = infinite, ["wrapped"] = wrapped,
            ["differentPlanet"] = differentPlanet, ["missingPlanet"] = missingPlanet,
            ["returned"] = returned, ["reset"] = reset
        };
    }

    /// <summary>用正式局部避让桶入口核验无限地图和环绕接缝的格归属。</summary>
    private static JObject NavigationAvoidanceTopology(JObject args)
    {
        var wrapped = new WorldTopologyDomain(new int2(-16, -16), new int2(32, 32), true);
        var unwrapped = default(WorldTopologyDomain);

        static JArray Bucket(Vector2 position, in WorldTopologyDomain topology)
        {
            Vector2Int bucket = WorldNavigationAgent.GetLocalAvoidanceBucket(position, topology);
            return new JArray(bucket.x, bucket.y);
        }

        var neighbours = new JArray();
        Vector2 center = new(15.5f, 0.5f);
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
            neighbours.Add(Bucket(center + new Vector2(x * 2f, y * 2f), wrapped));

        float2 seamDelta = wrapped.ShortestDelta(new float2(15.5f, 0.5f), new float2(-15.5f, 0.5f));
        return new JObject
        {
            ["unwrappedNegative"] = Bucket(new Vector2(-2.01f, -1.99f), unwrapped),
            ["wrappedSeam"] = Bucket(center, wrapped),
            ["equivalentImage"] = Bucket(new Vector2(79.5f, 0.5f), wrapped),
            ["wrappedCrossing"] = Bucket(new Vector2(17.5f, 0.5f), wrapped),
            ["unwrappedCrossing"] = Bucket(new Vector2(17.5f, 0.5f), unwrapped),
            ["nonSquareWrapped"] = Bucket(new Vector2(12.25f, 14.25f),
                new WorldTopologyDomain(new int2(-12, -6), new int2(24, 20), true)),
            ["neighbours"] = neighbours,
            ["seamDelta"] = new JArray(seamDelta.x, seamDelta.y)
        };
    }

    private static WorldNavigationGrid NavigationFixture()
    {
        var grid = new WorldNavigationGrid();
        // 只构造本用例的稀疏网格，路径搜索仍调用正式导航算法。
        for (int y = 10; y <= 12; y++)
        for (int x = 10; x <= 12; x++) grid.SetCell(new Vector2Int(x, y), 1, true);
        return grid;
    }

    private static JObject NavigationPath(JObject args)
    {
        var grid = NavigationFixture();
        uint penalty = checked((uint)Number(args, "penalty", 1000d));
        var center = new Vector2Int(11, 11);
        grid.SetCell(center, penalty, !Flag(args, "blocked", false));
        var start = new Vector2Int(10, 11);
        var goal = new Vector2Int(12, 11);
        var path = new List<Vector2Int>();
        bool found = grid.TryFindPath(start, goal, path, checked((int)Number(args, "budget", 64d)));
        bool valid = found;
        int totalCost = 0;
        bool includesCenter = path.Contains(center);
        for (int i = 1; i < path.Count; i++)
        {
            // 正式路径会平滑成多格线段，按正式线段遍历接口读取实际代价。
            bool traversable = grid.TryCalculateLineTraversalCost(path[i - 1], path[i], out int cost);
            valid &= traversable; totalCost += cost;
            Vector2Int delta = path[i] - path[i - 1], offset = center - path[i - 1];
            int projection = offset.x * delta.x + offset.y * delta.y;
            includesCenter |= offset.x * delta.y == offset.y * delta.x &&
                projection >= 0 && projection <= delta.sqrMagnitude;
        }
        return new JObject { ["found"] = found, ["length"] = path.Count, ["valid"] = valid,
            ["includesCostlyCell"] = includesCenter, ["totalCost"] = totalCost,
            ["centerWalkable"] = grid.IsWalkable(center),
            ["endsAtTarget"] = found && path[path.Count - 1] == WorldNavigationGrid.NormalizeCell(goal),
            ["lineOfSight"] = grid.HasLineOfSight(start, goal) };
    }

    private static JObject NavigationBlockers(JObject args)
    {
        var grid = NavigationFixture();
        var center = new Vector2Int(11, 11);
        bool initially = grid.IsWalkable(center);
        grid.RegisterBlocker(1, new[] { center });
        grid.RegisterBlocker(2, new[] { center });
        bool both = grid.IsWalkable(center);
        grid.UnregisterBlocker(1);
        bool one = grid.IsWalkable(center);
        grid.UnregisterBlocker(2);
        bool released = grid.IsWalkable(center);
        grid.RegisterBlocker(1, new[] { center });
        grid.RegisterBlocker(1, new[] { new Vector2Int(12, 11) });
        return new JObject { ["initiallyWalkable"] = initially, ["bothWalkable"] = both,
            ["oneWalkable"] = one, ["releasedWalkable"] = released,
            ["oldFootprintReleased"] = grid.IsWalkable(center), ["newFootprintBlocked"] = !grid.IsWalkable(new Vector2Int(12, 11)) };
    }

    private static JObject NavigationChanges(JObject args)
    {
        var grid = new WorldNavigationGrid();
        int initial = grid.Revision;
        grid.BeginBatchUpdate();
        grid.SetCell(new Vector2Int(10, 10), 1, true);
        grid.SetCell(new Vector2Int(11, 10), 1, true);
        int during = grid.Revision;
        grid.EndBatchUpdate();
        int after = grid.Revision;
        int beforeRepeat = grid.Revision;
        grid.SetCell(new Vector2Int(10, 10), 1, true);
        var changed = new List<Vector2Int>();
        grid.ConsumeChanges(changed, out bool reset);
        int repeated = grid.Revision;
        int removed = grid.RemoveRegion(new RectInt(10, 10, 2, 1));
        grid.SetCell(new Vector2Int(10, 10), 1, true);
        grid.Clear();
        grid.ConsumeChanges(changed, out bool cleared);
        return new JObject { ["initialRevision"] = initial, ["duringRevision"] = during, ["afterRevision"] = after,
            ["beforeRepeat"] = beforeRepeat, ["afterRepeat"] = repeated,
            ["removed"] = removed, ["remaining"] = grid.CellCount, ["clearInvalidatesAll"] = cleared };
    }

    private static JObject NavigationCorner(JObject args)
    {
        var grid = NavigationFixture();
        var from = new Vector2Int(10, 10);
        var to = new Vector2Int(11, 11);
        bool open = grid.CanTraverse(from, to, out int openCost);
        grid.SetCell(new Vector2Int(11, 10), 0, false);
        bool closed = grid.CanTraverse(from, to, out _);
        grid.SetCell(new Vector2Int(11, 10), 1, true);
        grid.SetCell(new Vector2Int(10, 11), 0, false);
        bool otherClosed = grid.CanTraverse(from, to, out _);
        return new JObject { ["open"] = open, ["openCost"] = openCost, ["closed"] = closed, ["otherClosed"] = otherClosed,
            ["farTraversal"] = grid.CanTraverse(from, new Vector2Int(12, 12), out _),
            ["sameTraversal"] = grid.CanTraverse(from, from, out _) };
    }

    /// <summary>对比常规世界格接口与已归一化批量接口的结果、变更通知和环绕位置。</summary>
    private static JObject NavigationCanonicalBatch(JObject args)
    {
        var regular = new WorldNavigationGrid();
        var canonical = new WorldNavigationGrid();
        var regularChanged = new List<Vector2Int>();
        var canonicalChanged = new List<Vector2Int>();
        regular.CellChanged += cell => regularChanged.Add(cell);
        canonical.CellChanged += cell => canonicalChanged.Add(cell);
        Vector2Int[] cells = { new(2, 3), new(3, 3), new(4, 3), new(-7, 9) };
        uint[] costs = { 1, 40, 0, 8 };
        bool[] walkable = { true, true, false, true };
        bool[] water = { false, true, false, true };
        float[] depths = { 0f, 0.5f, 0f, 0.9f };

        regular.BeginBatchUpdate();
        canonical.BeginBatchUpdate();
        for (int i = 0; i < cells.Length; i++)
        {
            regular.SetCell(cells[i], costs[i], walkable[i], water[i], depths[i]);
            canonical.SetCanonicalCell(WorldNavigationGrid.NormalizeCell(cells[i]),
                costs[i], walkable[i], water[i], depths[i]);
        }
        regular.EndBatchUpdate();
        canonical.EndBatchUpdate();
        regular.SetCell(cells[1], 55, true, true, 0.4f);
        canonical.SetCanonicalCell(WorldNavigationGrid.NormalizeCell(cells[1]), 55, true, true, 0.4f);
        regular.SetCell(cells[1], 55, true, true, 0.4f);
        canonical.SetCanonicalCell(WorldNavigationGrid.NormalizeCell(cells[1]), 55, true, true, 0.4f);
        regular.RegisterBlocker(301, new[] { cells[0] });
        canonical.RegisterBlocker(301, new[] { cells[0] });
        regular.RemoveCell(cells[2]);
        canonical.RemoveCanonicalCell(WorldNavigationGrid.NormalizeCell(cells[2]));

        bool sameCells = regular.CellCount == canonical.CellCount;
        foreach (Vector2Int position in cells)
        {
            bool left = regular.TryGetCell(position, out WorldNavigationCell a);
            bool right = canonical.TryGetCanonicalCell(WorldNavigationGrid.NormalizeCell(position),
                out WorldNavigationCell b);
            sameCells &= left == right && (!left ||
                a.Penalty == b.Penalty && a.Walkable == b.Walkable && a.Water == b.Water &&
                Mathf.Approximately(a.LiquidDepth, b.LiquidDepth));
        }
        var regularPositions = new List<Vector2Int>();
        var canonicalPositions = new List<Vector2Int>();
        regular.CopyCellPositions(regularPositions);
        canonical.CopyCellPositions(canonicalPositions);
        sameCells &= new HashSet<Vector2Int>(regularPositions).SetEquals(canonicalPositions);
        bool sameNotifications = regularChanged.Count == canonicalChanged.Count;
        for (int i = 0; i < regularChanged.Count && sameNotifications; i++)
            sameNotifications = regularChanged[i] == canonicalChanged[i];
        bool sameRevisions = regular.Revision == canonical.Revision &&
            regular.PathInvalidationRevision == canonical.PathInvalidationRevision &&
            regular.PathCostRevision == canonical.PathCostRevision;
        bool walkabilityEquivalent = true;
        foreach (Vector2Int position in cells)
            walkabilityEquivalent &= BuildingOccupancyRegistry.GetEffectiveWalkable(position, true) ==
                BuildingOccupancyRegistry.GetEffectiveWalkableNormalized(
                    WorldNavigationGrid.NormalizeCell(position), true);
        var domain = new WorldTopologyDomain(new int2(-16, -16), new int2(32, 32), true);
        bool wrappedEquivalent = true;
        foreach (Vector2Int position in new[]
        {
            new Vector2Int(-17, -16), new Vector2Int(-16, -16), new Vector2Int(15, 15),
            new Vector2Int(16, 16), new Vector2Int(48, -49)
        })
        {
            int2 expected = domain.Normalize(new int2(position.x, position.y));
            Vector2Int actual = WorldNavigationManager.NormalizeRuntimeNavigationCell(
                domain, position.x, position.y);
            wrappedEquivalent &= actual.x == expected.x && actual.y == expected.y;
        }
        Vector2Int unwrapped = WorldNavigationManager.NormalizeRuntimeNavigationCell(
            default, -100, 420);
        return new JObject
        {
            ["cells"] = sameCells,
            ["notifications"] = sameNotifications,
            ["revisions"] = sameRevisions,
            ["walkability"] = walkabilityEquivalent,
            ["wrapped"] = wrappedEquivalent,
            ["unwrapped"] = unwrapped == new Vector2Int(-100, 420),
            ["liquidCost"] = regular.TryGetCell(cells[1], out WorldNavigationCell waterCell) &&
                waterCell.Penalty == 55 && waterCell.Water &&
                Mathf.Approximately(waterCell.LiquidDepth, 0.4f)
        };
    }

    // 对照原版多次水位查询与新的同格复用，覆盖平台、抽干和不可走水格。
    private static JObject NavigationLiquidCellSnapshot(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(4, 1);
        for (int x = 0; x < 4; x++)
            buffer.SetCell(x, 0, new TerrainCell(17, 0, 0, 1, 12, TerrainCellFlags.Walkable));
        int liquidIndex = buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        buffer.SetLiquid(0, 0, liquidIndex, 6);
        buffer.SetLiquid(1, 0, liquidIndex, 6);
        buffer.SetLiquid(3, 0, liquidIndex, 6);
        using ChunkTerrainData terrain = buffer.Seal();
        TerrainSupportLayer.Set(terrain, 1, 0, 90, 2);

        bool Equivalent(int x, bool walkable)
        {
            bool baselineWater = WorldLiquidSystem.GetSurfaceDepth(terrain, x, 0) > 0f;
            float baselineDepth = baselineWater ? terrain.GetLiquidDepth(x, 0) : 0f;
            uint baselineCost = 0, nextCost = 0;
            bool baselineThrows = false, nextThrows = false;
            if (walkable)
                try { baselineCost = WorldLiquidSystem.GetNavigationCost(terrain, x, 0, 12); }
                catch (InvalidOperationException) { baselineThrows = true; }
            try
            {
                nextCost = WorldNavigationManager.ResolveRuntimeLiquidNavigation(terrain, x, 0,
                    12, walkable, out bool nextWater, out float nextDepth);
                return !baselineThrows && baselineCost == nextCost && baselineWater == nextWater &&
                    Mathf.Approximately(baselineDepth, nextDepth);
            }
            catch (InvalidOperationException) { nextThrows = true; }
            return baselineThrows && nextThrows;
        }

        bool wet = WorldLiquidSystem.GetSurfaceLevel(terrain, 0, 0) == 6 && Equivalent(0, true);
        bool covered = WorldLiquidSystem.GetSurfaceLevel(terrain, 1, 0) == 0 && Equivalent(1, true);
        bool dry = WorldLiquidSystem.GetSurfaceLevel(terrain, 2, 0) == 0 && Equivalent(2, true);
        bool blockedWet = Equivalent(3, false) &&
            WorldNavigationManager.ResolveRuntimeLiquidNavigation(terrain, 3, 0, 12, false,
                out bool water, out float depth) == 0 && water && Mathf.Approximately(depth, .6f);
        TerrainSupportLayer.Set(terrain, 1, 0, 0, 2);
        bool uncovered = WorldLiquidSystem.GetSurfaceLevel(terrain, 1, 0) == 6 && Equivalent(1, true);
        terrain.SetLiquid(0, 0, 0, 0);
        bool drained = WorldLiquidSystem.GetSurfaceLevel(terrain, 0, 0) == 0 && Equivalent(0, true);
        TerrainSupportLayer.Set(terrain, 1, 0, 90, 2);

        // 只用无水及平台覆盖格测读格成本，避免测试资源目录和异常分配影响基准。
        const int iterations = 65536;
        long baselineSum = 0, optimizedSum = 0;
        for (int i = 0; i < 256; i++)
        {
            int x = i % 2 + 1;
            _ = WorldLiquidSystem.GetNavigationCost(terrain, x, 0, 12);
            _ = WorldNavigationManager.ResolveRuntimeLiquidNavigation(terrain, x, 0, 12, true, out _, out _);
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            int x = i % 2 + 1;
            bool hasWater = WorldLiquidSystem.GetSurfaceDepth(terrain, x, 0) > 0f;
            uint cost = WorldLiquidSystem.GetNavigationCost(terrain, x, 0, 12);
            float liquidDepth = hasWater ? terrain.GetLiquidDepth(x, 0) : 0f;
            baselineSum += cost + (hasWater ? 1 : 0) + (int)(liquidDepth * 10f);
        }
        timer.Stop();
        double baselineMilliseconds = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        for (int i = 0; i < iterations; i++)
        {
            int x = i % 2 + 1;
            uint cost = WorldNavigationManager.ResolveRuntimeLiquidNavigation(terrain, x, 0,
                12, true, out bool hasWater, out float liquidDepth);
            optimizedSum += cost + (hasWater ? 1 : 0) + (int)(liquidDepth * 10f);
        }
        timer.Stop();
        return new JObject
        {
            ["wet"] = wet, ["covered"] = covered, ["dry"] = dry,
            ["blockedWet"] = blockedWet, ["uncovered"] = uncovered, ["drained"] = drained,
            ["iterations"] = iterations, ["sameOutputs"] = baselineSum == optimizedSum,
            ["baselineMilliseconds"] = baselineMilliseconds,
            ["optimizedMilliseconds"] = timer.Elapsed.TotalMilliseconds
        };
    }

    // 反复装卸不同区块，确认导航块复用不产生稳定帧 GC 且不残留旧地形。
    private static JObject NavigationChunkBlockPool(JObject args)
    {
        var grid = new WorldNavigationGrid();
        var changed = new List<Vector2Int>(512);
        const int size = 16;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            grid.SetCanonicalCell(new Vector2Int(x, y), 3, true, x == 0, x == 0 ? .6f : 0f);
        bool originalWater = grid.TryGetCanonicalCell(Vector2Int.zero, out WorldNavigationCell water) &&
            water.Walkable && water.Water && Mathf.Approximately(water.LiquidDepth, .6f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            grid.RemoveCanonicalCell(new Vector2Int(x, y));
        grid.ConsumeChanges(changed, out _);
        int createdAtWarmup = grid.CreatedNavigationBlockCount;

        const int rounds = 64;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < rounds; round++)
        {
            int originX = (round + 1) * size;
            grid.BeginBatchUpdate();
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                grid.SetCanonicalCell(new Vector2Int(originX + x, y), 7, true);
            grid.EndBatchUpdate();
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                grid.RemoveCanonicalCell(new Vector2Int(originX + x, y));
            grid.ConsumeChanges(changed, out _);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        int createdAfterRounds = grid.CreatedNavigationBlockCount;
        bool emptyAfterUnload = grid.CellCount == 0 &&
            !grid.TryGetCanonicalCell(new Vector2Int(rounds * size, 0), out _);

        var old = new Vector2Int(0, 0);
        var fresh = new Vector2Int(-16, -16);
        grid.SetCanonicalCell(old, 2, true, true, .4f);
        grid.RegisterBlocker(9001, new[] { old });
        bool initiallyBlocked = grid.TryGetCanonicalCell(old, out WorldNavigationCell blocked) && !blocked.Walkable;
        grid.RemoveCanonicalCell(old);
        grid.SetCanonicalCell(fresh, 12, true);
        bool reusedClean = !grid.TryGetCanonicalCell(old, out _) &&
            grid.TryGetCanonicalCell(fresh, out WorldNavigationCell freshCell) && freshCell.Walkable &&
            freshCell.Penalty == 12 && !freshCell.Water && freshCell.LiquidDepth == 0f;
        grid.SetCanonicalCell(old, 2, true);
        bool blockerPreserved = !grid.IsWalkable(old);
        grid.UnregisterBlocker(9001);
        bool blockerReleased = grid.IsWalkable(old);
        grid.Clear();
        bool clearReset = grid.CellCount == 0 && !grid.TryGetCanonicalCell(fresh, out _);

        // 超过池上限的并行块必须有界保留，换世界则完全丢弃缓存。
        var bounded = new WorldNavigationGrid();
        for (int i = 0; i < 72; i++)
            bounded.SetCanonicalCell(new Vector2Int(i * size, 0), 1, true);
        for (int i = 0; i < 72; i++)
            bounded.RemoveCanonicalCell(new Vector2Int(i * size, 0));
        bool poolBounded = bounded.PooledNavigationBlockCount == 64 && bounded.CellCount == 0;
        bounded.Clear();
        bool worldResetReleasesPool = bounded.PooledNavigationBlockCount == 0;
        bounded.SetCanonicalCell(new Vector2Int(-32, 0), 1, true);
        bool resetRequiresFreshBlock = bounded.CreatedNavigationBlockCount == 73;
        return new JObject
        {
            ["rounds"] = rounds, ["allocatedBytes"] = allocated,
            ["createdAtWarmup"] = createdAtWarmup, ["createdAfterRounds"] = createdAfterRounds,
            ["originalWater"] = originalWater, ["emptyAfterUnload"] = emptyAfterUnload,
            ["initiallyBlocked"] = initiallyBlocked, ["reusedClean"] = reusedClean,
            ["blockerPreserved"] = blockerPreserved, ["blockerReleased"] = blockerReleased,
            ["clearReset"] = clearReset, ["poolBounded"] = poolBounded,
            ["worldResetReleasesPool"] = worldResetReleasesPool,
            ["resetRequiresFreshBlock"] = resetRequiresFreshBlock
        };
    }

    /// <summary>核对增量碰撞索引只删除事件锚点的失效机器，不误清相邻锚点。</summary>
    private static JObject NavigationMachineObstacleDirty(JObject args)
    {
        Vector2Int first = new(4, 5), second = new(5, 5);
        var anchors = new Dictionary<int, Vector2Int>
        {
            [10] = first, [11] = first, [12] = second
        };
        var live = new HashSet<int> { 10, 11 };
        var stale = new List<int>();
        ChunkCollisionRenderer.CollectStaleMachineIds(anchors, first, live, stale);
        bool unchanged = stale.Count == 0;
        live.Remove(11);
        ChunkCollisionRenderer.CollectStaleMachineIds(anchors, first, live, stale);
        bool removedOnlyOne = stale.Count == 1 && stale[0] == 11;
        anchors.Remove(11);
        anchors[10] = second;
        ChunkCollisionRenderer.CollectStaleMachineIds(anchors, first, live, stale);
        bool movedNotRemoved = stale.Count == 0;
        live.Clear();
        live.Add(10);
        ChunkCollisionRenderer.CollectStaleMachineIds(anchors, second, live, stale);
        bool neighborRemoved = stale.Count == 1 && stale[0] == 12;
        live.Clear();
        ChunkCollisionRenderer.CollectStaleMachineIds(anchors, second, live, stale);
        bool allRemoved = stale.Count == 2 && stale.Contains(10) && stale.Contains(12);
        return new JObject
        {
            ["unchanged"] = unchanged,
            ["removedOnlyOne"] = removedOnlyOne,
            ["movedNotRemoved"] = movedNotRemoved,
            ["neighborRemoved"] = neighborRemoved,
            ["allRemoved"] = allRemoved
        };
    }
    #endregion
}
