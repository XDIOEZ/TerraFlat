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
        private NativeList<FlowChunkHeader> nativeChunks;
        private NativeList<int> nativeCells;
        private NativeList<byte> nativeWater;
        private NativeList<float> nativeLiquidDepth;
        private NativeList<float2> nativeWaterCurrent;
        private NativeList<FlowPortal> nativePortals;
        private NativeList<int> nativeExitCosts;
        private NativeList<byte> nativeExitDirections;
        private NativeList<FlowGoalData> nativeGoals;
        private NativeList<int> nativeTargetCosts;
        private NativeList<byte> nativeTargetDirections;
        private NativeList<int> nativeRoutes;
        private NativeList<int> nativeSelectedExits;
        private readonly List<int> routeSlots = new();
        private FlowNavigationSnapshot publishedSnapshot;

        /// <summary>结构未变时只覆盖脏块；结构变化时复用 Native 容量重排紧凑表。</summary>
        private void PublishChunks(List<ChunkPublication> changed, bool graphChanged)
        {
            if (CanPatchChunks(changed))
            {
                foreach (ChunkPublication change in changed)
                {
                    nativeChunkLookup.TryGetValue(change.Coordinate, out int index);
                    CachedChunk chunk = chunks[change.Coordinate];
                    if (change.Result == ChunkRefreshResult.Surface)
                        PublishSurface(index, chunk);
                    else
                        PublishChunk(index, chunk, nativeChunks[index].PortalStart);
                }
                if (graphChanged) PairPortals();
                PatchedTablePublications++;
                if (!graphChanged) SurfaceOnlyPublications++;
                return;
            }

            FullTablePublications++;
            coordinates.Clear(); coordinates.AddRange(chunks.Keys);
            coordinates.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            int portalCount = 0;
            foreach (int2 coordinate in coordinates) portalCount += chunks[coordinate].Portals.Count;
            EnsureChunkStorage(coordinates.Count, portalCount);
            int nextPortal = 0;
            for (int index = 0; index < coordinates.Count; index++)
            {
                CachedChunk chunk = chunks[coordinates[index]];
                nativeChunkLookup.Add(chunk.Coordinate, index);
                PublishChunk(index, chunk, nextPortal);
                nextPortal += chunk.Portals.Count;
            }
            PairPortals();
        }

        /// <summary>块集合与每块出口数量相同时，原有紧凑索引可以原位覆盖。</summary>
        private bool CanPatchChunks(List<ChunkPublication> changed)
        {
            if (!nativeChunks.IsCreated || nativeChunks.Length != chunks.Count) return false;
            foreach (ChunkPublication change in changed)
            {
                if (!chunks.TryGetValue(change.Coordinate, out CachedChunk chunk) ||
                    !nativeChunkLookup.TryGetValue(change.Coordinate, out int index) ||
                    nativeChunks[index].PortalCount != chunk.Portals.Count)
                    return false;
            }
            return true;
        }

        /// <summary>按几何容量扩展持久容器；流送增减只改变有效长度。</summary>
        private void EnsureChunkStorage(int chunkCount, int portalCount)
        {
            int cellCount = checked(chunkCount * 256);
            int exitCellCount = checked(portalCount * 256);
            if (!nativeChunkLookup.IsCreated)
                nativeChunkLookup = new NativeParallelHashMap<int2, int>(math.max(1, chunkCount), Allocator.Persistent);
            else if (nativeChunkLookup.Capacity < chunkCount)
                nativeChunkLookup.Capacity = math.max(chunkCount, nativeChunkLookup.Capacity * 2);
            nativeChunkLookup.Clear();

            if (!nativeChunks.IsCreated) nativeChunks = new NativeList<FlowChunkHeader>(math.max(1, chunkCount), Allocator.Persistent);
            if (!nativeCells.IsCreated) nativeCells = new NativeList<int>(math.max(1, cellCount), Allocator.Persistent);
            if (!nativeWater.IsCreated) nativeWater = new NativeList<byte>(math.max(1, cellCount), Allocator.Persistent);
            if (!nativeLiquidDepth.IsCreated) nativeLiquidDepth = new NativeList<float>(math.max(1, cellCount), Allocator.Persistent);
            if (!nativeWaterCurrent.IsCreated) nativeWaterCurrent = new NativeList<float2>(math.max(1, cellCount), Allocator.Persistent);
            if (!nativePortals.IsCreated) nativePortals = new NativeList<FlowPortal>(math.max(1, portalCount), Allocator.Persistent);
            if (!nativeExitCosts.IsCreated) nativeExitCosts = new NativeList<int>(math.max(1, exitCellCount), Allocator.Persistent);
            if (!nativeExitDirections.IsCreated) nativeExitDirections = new NativeList<byte>(math.max(1, exitCellCount), Allocator.Persistent);
            nativeChunks.ResizeUninitialized(chunkCount);
            nativeCells.ResizeUninitialized(cellCount);
            nativeWater.ResizeUninitialized(cellCount);
            nativeLiquidDepth.ResizeUninitialized(cellCount);
            nativeWaterCurrent.ResizeUninitialized(cellCount);
            nativePortals.ResizeUninitialized(portalCount);
            nativeExitCosts.ResizeUninitialized(exitCellCount);
            nativeExitDirections.ResizeUninitialized(exitCellCount);
        }

        /// <summary>把一个块的权重、表层水数据和出口局部图发布到固定区间。</summary>
        private void PublishChunk(int index, CachedChunk chunk, int portalStart)
        {
            nativeChunks[index] = new FlowChunkHeader { Coordinate = chunk.Coordinate, PortalStart = portalStart, PortalCount = chunk.Portals.Count };
            int cellStart = index * 256;
            NativeArray<int>.Copy(chunk.Cells, 0, nativeCells.AsArray(), cellStart, 256);
            PublishSurface(index, chunk);
            for (int portalIndex = 0; portalIndex < chunk.Portals.Count; portalIndex++)
            {
                FlowPortal portal = chunk.Portals[portalIndex]; portal.Chunk = index;
                int target = portalStart + portalIndex;
                nativePortals[target] = portal;
                LocalField field = chunk.Fields[portal.Anchor];
                int exitStart = target * 256;
                NativeArray<int>.Copy(field.Costs, 0, nativeExitCosts.AsArray(), exitStart, 256);
                NativeArray<byte>.Copy(field.Directions, 0, nativeExitDirections.AsArray(), exitStart, 256);
            }
        }

        /// <summary>仅覆写同块的有效水面数据，保留通行权重和出口图。</summary>
        private void PublishSurface(int index, CachedChunk chunk)
        {
            int cellStart = index * 256;
            NativeArray<byte>.Copy(chunk.Water, 0, nativeWater.AsArray(), cellStart, 256);
            NativeArray<float>.Copy(chunk.LiquidDepth, 0, nativeLiquidDepth.AsArray(), cellStart, 256);
            NativeArray<float2>.Copy(chunk.WaterCurrent, 0, nativeWaterCurrent.AsArray(), cellStart, 256);
        }

        /// <summary>重配出口时清除旧配对，再按当前邻块的缺口区间连接。</summary>
        private void PairPortals()
        {
            for (int index = 0; index < nativePortals.Length; index++)
            {
                FlowPortal portal = nativePortals[index];
                portal.Pair = -1;
                nativePortals[index] = portal;
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

        /// <summary>目标表按形状复用容量；图变化时重新计算路线，表层水变化不进入此流程。</summary>
        private void PublishGoals(bool graphChanged)
        {
            int goalCellCount = checked(goals.Count * 256);
            int routeLength = checked(goals.Count * nativePortals.Length);
            int selectedLength = checked(goals.Count * nativeChunks.Length * 256);
            bool resized = !nativeGoals.IsCreated || nativeGoals.Length != goals.Count ||
                           nativeRoutes.Length != routeLength || nativeSelectedExits.Length != selectedLength;
            if (!nativeGoals.IsCreated)
            {
                nativeGoals = new NativeList<FlowGoalData>(math.max(1, goals.Count), Allocator.Persistent);
                nativeTargetCosts = new NativeList<int>(math.max(1, goalCellCount), Allocator.Persistent);
                nativeTargetDirections = new NativeList<byte>(math.max(1, goalCellCount), Allocator.Persistent);
                nativeRoutes = new NativeList<int>(math.max(1, routeLength), Allocator.Persistent);
                nativeSelectedExits = new NativeList<int>(math.max(1, selectedLength), Allocator.Persistent);
            }
            if (resized)
            {
                nativeGoals.ResizeUninitialized(goals.Count);
                nativeTargetCosts.ResizeUninitialized(goalCellCount);
                nativeTargetDirections.ResizeUninitialized(goalCellCount);
                nativeRoutes.ResizeUninitialized(routeLength);
                nativeSelectedExits.ResizeUninitialized(selectedLength);
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
                {
                    int targetStart = slot * 256;
                    NativeArray<int>.Copy(goal.Field.Costs, 0, nativeTargetCosts.AsArray(), targetStart, 256);
                    NativeArray<byte>.Copy(goal.Field.Directions, 0, nativeTargetDirections.AsArray(), targetStart, 256);
                }

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
                if (graphChanged || resized || goal.RouteDirty || !goal.RoutedChunk.Equals(coordinate) || goal.RootMask != mask) routeSlots.Add(slot);
                goal.RootMask = mask; goal.RoutedChunk = coordinate;
                goal.TargetDirty = goal.RouteDirty = false;
            }
            if (routeSlots.Count == 0) return;
            EnsureWorkspace(ref routeSlotsWorkspace, routeSlots.Count);
            EnsureWorkspace(ref routeNodesWorkspace, nativeRoutes.Length);
            EnsureWorkspace(ref routePositionsWorkspace, nativeRoutes.Length);
            NativeArray<int> slots = routeSlotsWorkspace.GetSubArray(0, routeSlots.Count);
            for (int i = 0; i < routeSlots.Count; i++) slots[i] = routeSlots[i];
            JobHandle handle = new FlowPortalRouteJob { GoalSlots = slots, Goals = nativeGoals.AsArray(), Chunks = nativeChunks.AsArray(),
                Portals = nativePortals.AsArray(), Cells = nativeCells.AsArray(), ExitCosts = nativeExitCosts.AsArray(), TargetCosts = nativeTargetCosts.AsArray(),
                Routes = nativeRoutes.AsArray(), HeapNodes = routeNodesWorkspace.GetSubArray(0, nativeRoutes.Length),
                HeapPositions = routePositionsWorkspace.GetSubArray(0, nativeRoutes.Length) }.Schedule(slots.Length, 1);
            if (nativeChunks.Length > 0)
                handle = new FlowSelectExitJob { GoalSlots = slots, Chunks = nativeChunks.AsArray(), Portals = nativePortals.AsArray(),
                    Cells = nativeCells.AsArray(), ExitCosts = nativeExitCosts.AsArray(), Routes = nativeRoutes.AsArray(), SelectedExits = nativeSelectedExits.AsArray() }
                    .Schedule(slots.Length * nativeChunks.Length, 1, handle);
            handle.Complete(); HighLevelRouteBuilds += routeSlots.Count;
        }

        /// <summary>只在发布边界更新 NativeList 视图，逐帧读取不重复创建安全别名。</summary>
        private void CaptureSnapshot() => publishedSnapshot = new FlowNavigationSnapshot
        {
            Domain = domain, Epoch = Epoch, ChunkLookup = nativeChunkLookup, Chunks = nativeChunks.AsArray(),
            Cells = nativeCells.AsArray(), Water = nativeWater.AsArray(), LiquidDepth = nativeLiquidDepth.AsArray(),
            WaterCurrent = nativeWaterCurrent.AsArray(),
            Portals = nativePortals.AsArray(), ExitDirections = nativeExitDirections.AsArray(), Goals = nativeGoals.AsArray(),
            TargetCosts = nativeTargetCosts.AsArray(), TargetDirections = nativeTargetDirections.AsArray(), SelectedExits = nativeSelectedExits.AsArray()
        };

        /// <summary>借出最近一次发布的视图；读取 Job 仍须登记依赖。</summary>
        private FlowNavigationSnapshot Snapshot() => publishedSnapshot;

        /// <summary>释放块表、出口表及共享出口图。</summary>
        private void DisposeChunks()
        {
            if (nativeChunkLookup.IsCreated) nativeChunkLookup.Dispose();
            if (nativeChunks.IsCreated) nativeChunks.Dispose();
            if (nativeCells.IsCreated) nativeCells.Dispose();
            if (nativeWater.IsCreated) nativeWater.Dispose();
            if (nativeLiquidDepth.IsCreated) nativeLiquidDepth.Dispose();
            if (nativeWaterCurrent.IsCreated) nativeWaterCurrent.Dispose();
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
        private void DisposeNative() { publishedSnapshot = default; DisposeGoals(); DisposeChunks(); DisposeWorkspace(); }
        #endregion
    }
}
