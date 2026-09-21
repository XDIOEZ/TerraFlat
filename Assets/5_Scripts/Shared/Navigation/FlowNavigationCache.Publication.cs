using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace FlatWorld.Navigation
{
    public sealed partial class FlowNavigationCache
    {
        #region 发布数据所有权
        private NativeParallelHashMap<int2, int> nativeChunkLookup;
        private NativeArray<FlowChunkHeader> nativeChunks;
        private NativeArray<int> nativeCells;
        private NativeArray<byte> nativeWater;
        private NativeArray<float> nativeLiquidDepth;
        private NativeArray<FlowPortal> nativePortals;
        private NativeArray<int> nativeExitCosts;
        private NativeArray<byte> nativeExitDirections;
        private NativeArray<FlowGoalData> nativeGoals;
        private NativeArray<int> nativeTargetCosts;
        private NativeArray<byte> nativeTargetDirections;
        private NativeArray<int> nativeRoutes;
        private NativeArray<int> nativeSelectedExits;
        private readonly List<int> routeSlots = new();

        /// <summary>只在块数据变化时重新排列 Native 表，直接复制未变局部图而不重新搜索。</summary>
        private void PublishChunks()
        {
            DisposeChunks();
            coordinates.Clear(); coordinates.AddRange(chunks.Keys);
            coordinates.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            int portalCount = 0;
            foreach (int2 coordinate in coordinates) portalCount += chunks[coordinate].Portals.Count;
            nativeChunkLookup = new NativeParallelHashMap<int2, int>(math.max(1, coordinates.Count), Allocator.Persistent);
            nativeChunks = new NativeArray<FlowChunkHeader>(coordinates.Count, Allocator.Persistent);
            nativeCells = new NativeArray<int>(coordinates.Count * 256, Allocator.Persistent);
            nativeWater = new NativeArray<byte>(coordinates.Count * 256, Allocator.Persistent);
            nativeLiquidDepth = new NativeArray<float>(coordinates.Count * 256, Allocator.Persistent);
            nativePortals = new NativeArray<FlowPortal>(portalCount, Allocator.Persistent);
            nativeExitCosts = new NativeArray<int>(portalCount * 256, Allocator.Persistent);
            nativeExitDirections = new NativeArray<byte>(portalCount * 256, Allocator.Persistent);
            int nextPortal = 0;
            for (int index = 0; index < coordinates.Count; index++)
            {
                CachedChunk chunk = chunks[coordinates[index]];
                nativeChunkLookup.Add(chunk.Coordinate, index);
                nativeChunks[index] = new FlowChunkHeader { Coordinate = chunk.Coordinate, PortalStart = nextPortal, PortalCount = chunk.Portals.Count };
                for (int cell = 0; cell < 256; cell++)
                {
                    int target = index * 256 + cell;
                    nativeCells[target] = chunk.Cells[cell];
                    nativeWater[target] = chunk.Water[cell];
                    nativeLiquidDepth[target] = chunk.LiquidDepth[cell];
                }
                foreach (FlowPortal item in chunk.Portals)
                {
                    FlowPortal portal = item; portal.Chunk = index;
                    nativePortals[nextPortal] = portal;
                    LocalField field = chunk.Fields[portal.Anchor];
                    for (int cell = 0; cell < 256; cell++)
                    { nativeExitCosts[nextPortal * 256 + cell] = field.Costs[cell]; nativeExitDirections[nextPortal * 256 + cell] = field.Directions[cell]; }
                    nextPortal++;
                }
            }
            for (int index = 0; index < nativePortals.Length; index++)
            {
                FlowPortal portal = nativePortals[index];
                int2 edgeCell = FlowNavigationMath.Origin(nativeChunks[portal.Chunk].Coordinate, domain) + FlowNavigationMath.LocalCell(portal.Anchor);
                int2 oppositeCell = domain.Normalize(edgeCell + FlowNavigationMath.SideOffset(portal.Side));
                if (!nativeChunkLookup.TryGetValue(FlowNavigationMath.ChunkOf(oppositeCell, domain), out int other)) continue;
                FlowChunkHeader neighbour = nativeChunks[other];
                for (int pair = neighbour.PortalStart; pair < neighbour.PortalStart + neighbour.PortalCount; pair++)
                {
                    FlowPortal candidate = nativePortals[pair];
                    if (candidate.Side != (portal.Side ^ 1) || candidate.First != portal.First || candidate.Last != portal.Last) continue;
                    portal.Pair = pair; break;
                }
                nativePortals[index] = portal;
            }
        }

        /// <summary>只更新变化目标的局部图；出口路由仅在 Chunk/连通分量/图版本变化时重算。</summary>
        private void PublishGoals(bool graphChanged)
        {
            bool resized = graphChanged || !nativeGoals.IsCreated || nativeGoals.Length != goals.Count;
            if (resized)
            {
                DisposeGoals();
                nativeGoals = new NativeArray<FlowGoalData>(goals.Count, Allocator.Persistent);
                nativeTargetCosts = new NativeArray<int>(goals.Count * 256, Allocator.Persistent);
                nativeTargetDirections = new NativeArray<byte>(goals.Count * 256, Allocator.Persistent);
                nativeRoutes = new NativeArray<int>(checked(goals.Count * nativePortals.Length), Allocator.Persistent);
                nativeSelectedExits = new NativeArray<int>(checked(goals.Count * nativeChunks.Length * 256), Allocator.Persistent);
                for (int i = 0; i < nativeSelectedExits.Length; i++) nativeSelectedExits[i] = -1;
            }
            routeSlots.Clear();
            for (int slot = 0; slot < goals.Count; slot++)
            {
                Goal goal = goals[slot];
                int2 coordinate = FlowNavigationMath.ChunkOf(goal.Cell, domain);
                int chunkIndex = -1;
                if (goal.Active && nativeChunkLookup.TryGetValue(coordinate, out int found)) chunkIndex = found;
                nativeGoals[slot] = new FlowGoalData { Chunk = chunkIndex, Cell = FlowNavigationMath.LocalIndex(goal.Cell, domain),
                    Position = goal.Position, Generation = goal.Generation, Epoch = Epoch };
                if (goal.TargetDirty || resized)
                    for (int cell = 0; cell < 256; cell++)
                    { nativeTargetCosts[slot * 256 + cell] = goal.Field.Costs[cell]; nativeTargetDirections[slot * 256 + cell] = goal.Field.Directions[cell]; }

                ulong mask = 0;
                if (chunkIndex >= 0)
                {
                    FlowChunkHeader chunk = nativeChunks[chunkIndex];
                    for (int p = chunk.PortalStart; p < chunk.PortalStart + chunk.PortalCount; p++)
                    {
                        FlowPortal portal = nativePortals[p];
                        if (goal.Field.Costs[portal.Anchor] >= FlowNavigationMath.Infinity) continue;
                        int offset = portal.Side < 2 ? portal.Anchor / 16 : portal.Anchor % 16;
                        mask |= 1UL << (portal.Side * 16 + offset);
                    }
                }
                if (resized || goal.RouteDirty || !goal.RoutedChunk.Equals(coordinate) || goal.RootMask != mask) routeSlots.Add(slot);
                goal.RootMask = mask; goal.RoutedChunk = coordinate;
                goal.TargetDirty = goal.RouteDirty = false;
            }
            if (routeSlots.Count == 0) return;
            using var slots = new NativeArray<int>(routeSlots.ToArray(), Allocator.TempJob);
            using var nodes = new NativeArray<int>(nativeRoutes.Length, Allocator.TempJob);
            using var positions = new NativeArray<int>(nativeRoutes.Length, Allocator.TempJob);
            JobHandle handle = new FlowPortalRouteJob { GoalSlots = slots, Goals = nativeGoals, Chunks = nativeChunks,
                Portals = nativePortals, Cells = nativeCells, ExitCosts = nativeExitCosts, TargetCosts = nativeTargetCosts,
                Routes = nativeRoutes, HeapNodes = nodes, HeapPositions = positions }.Schedule(slots.Length, 1);
            if (nativeChunks.Length > 0)
                handle = new FlowSelectExitJob { GoalSlots = slots, Chunks = nativeChunks, Portals = nativePortals,
                    Cells = nativeCells, ExitCosts = nativeExitCosts, Routes = nativeRoutes, SelectedExits = nativeSelectedExits }
                    .Schedule(slots.Length * nativeChunks.Length, 1, handle);
            handle.Complete(); HighLevelRouteBuilds += routeSlots.Count;
        }

        /// <summary>借出当前不可变视图；容器所有权仍留在本缓存。</summary>
        private FlowNavigationSnapshot Snapshot() => new FlowNavigationSnapshot
        {
            Domain = domain, Epoch = Epoch, ChunkLookup = nativeChunkLookup, Chunks = nativeChunks,
            Cells = nativeCells, Water = nativeWater, LiquidDepth = nativeLiquidDepth,
            Portals = nativePortals, ExitDirections = nativeExitDirections, Goals = nativeGoals,
            TargetCosts = nativeTargetCosts, TargetDirections = nativeTargetDirections, SelectedExits = nativeSelectedExits
        };

        /// <summary>释放块表、出口表及共享出口图。</summary>
        private void DisposeChunks()
        {
            if (nativeChunkLookup.IsCreated) nativeChunkLookup.Dispose();
            if (nativeChunks.IsCreated) nativeChunks.Dispose();
            if (nativeCells.IsCreated) nativeCells.Dispose();
            if (nativeWater.IsCreated) nativeWater.Dispose();
            if (nativeLiquidDepth.IsCreated) nativeLiquidDepth.Dispose();
            if (nativePortals.IsCreated) nativePortals.Dispose();
            if (nativeExitCosts.IsCreated) nativeExitCosts.Dispose();
            if (nativeExitDirections.IsCreated) nativeExitDirections.Dispose();
        }

        /// <summary>释放目标表和已组合的出口选择表。</summary>
        private void DisposeGoals()
        {
            if (nativeGoals.IsCreated) nativeGoals.Dispose();
            if (nativeTargetCosts.IsCreated) nativeTargetCosts.Dispose();
            if (nativeTargetDirections.IsCreated) nativeTargetDirections.Dispose();
            if (nativeRoutes.IsCreated) nativeRoutes.Dispose();
            if (nativeSelectedExits.IsCreated) nativeSelectedExits.Dispose();
        }

        /// <summary>在所有读取任务完成后释放整份发布数据。</summary>
        private void DisposeNative() { DisposeGoals(); DisposeChunks(); }
        #endregion
    }
}
