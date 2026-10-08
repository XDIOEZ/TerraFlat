using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace FlatWorld.Navigation
{
    /// <summary>
    /// 按世界持有的 16×16 分层流场缓存。出口取双方边缘连续缺口的代表格，数量由真实缺口决定。
    /// 局部图只因本块内容或出口锚点变化重建；共享目标在同连通分量内移动不触发出口图搜索。
    /// 路径经过缺口代表格，保留权重/可达性，但不承诺与完整逐格搜索具有相同的最短路长度。
    /// </summary>
    public sealed partial class FlowNavigationCache : IDisposable
    {
        #region 所有权与共享缓存
        private static uint nextEpoch;
        private readonly IFlowGridSource source;
        private readonly int maximumGoals;
        private readonly Dictionary<int2, CachedChunk> chunks = new();
        private readonly Dictionary<int2, ChunkDirtyKind> dirty = new();
        private readonly List<Goal> goals = new();
        private readonly List<int2> coordinates = new();
        private readonly List<ChunkPublication> changedChunks = new();
        private readonly List<FieldBuild> builds = new();
        private JobHandle readers;
        private bool resetPending, goalsChanged, disposed;
        private WorldTopologyDomain domain;
        public uint Epoch { get; private set; }
        public int CachedChunkCount => chunks.Count;
        public int CachedPortalCount => nativePortals.IsCreated ? nativePortals.Length : 0;
        public int PendingChunkCount => dirty.Count;
        // 累计真实计算次数，不由单位计数推算。
        public long ChunkContentBuilds { get; private set; }
        public long PortalConnectionBuilds { get; private set; }
        public long ExitFieldBuilds { get; private set; }
        public long TargetFieldBuilds { get; private set; }
        public long HighLevelRouteBuilds { get; private set; }
        // 区分表层原位发布与整表重排，便于核对增量路径是否实际生效。
        public long FullTablePublications { get; private set; }
        public long PatchedTablePublications { get; private set; }
        public long SurfaceOnlyPublications { get; private set; }

        /// <summary>绑定权威来源，目标数量上限限制意外的逐 AI 目标注册。</summary>
        public FlowNavigationCache(IFlowGridSource source, int maximumGoals = 32)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.maximumGoals = math.max(1, maximumGoals);
            ResetWorld();
        }

        /// <summary>登记快照读取依赖，写入/扩容/销毁前统一等待。</summary>
        public void RegisterReader(JobHandle handle) => readers = JobHandle.CombineDependencies(readers, handle);

        /// <summary>查询句柄是否仍属于当前世界和活跃目标槽。</summary>
        public bool IsValid(FlowGoalHandle handle) => !disposed && !resetPending && handle.Epoch == Epoch &&
            (uint)handle.Slot < (uint)goals.Count && goals[handle.Slot].Active && goals[handle.Slot].Generation == handle.Generation;

        /// <summary>观察者只借用已发布的流场，不创建目标或触发搜索；异步读取仍须登记 RegisterReader。</summary>
        public bool TryReadPublished(FlowGoalHandle handle, out FlowNavigationSnapshot snapshot)
        {
            snapshot = default;
            if (!IsValid(handle) || dirty.Count != 0 || goalsChanged || !nativeGoals.IsCreated ||
                (uint)handle.Slot >= (uint)nativeGoals.Length)
                return false;

            FlowGoalData goal = nativeGoals[handle.Slot];
            if (goal.Generation != handle.Generation || goal.Epoch != handle.Epoch || goal.Chunk < 0)
                return false;

            snapshot = Snapshot();
            return true;
        }

        /// <summary>创建供整组 AI 共享的目标，禁止为每只 AI 调用此入口。</summary>
        public FlowGoalHandle CreateGoal(float2 position)
        {
            if (resetPending) ResetWorld();
            int slot = goals.FindIndex(value => !value.Active);
            if (slot < 0)
            {
                if (goals.Count >= maximumGoals) throw new InvalidOperationException("共享导航目标数量已达上限；应按玩家/编队共享目标，不能逐 AI 注册。");
                slot = goals.Count; goals.Add(new Goal());
            }
            Goal goal = goals[slot];
            goal.Active = true; goal.Generation++; goal.TargetDirty = goal.RouteDirty = true;
            var handle = new FlowGoalHandle { Slot = slot, Generation = goal.Generation, Epoch = Epoch };
            UpdateGoal(handle, position);
            return handle;
        }

        /// <summary>按共享目标更新位置；跨格才重算局部图，同格移动只更新终点坐标。</summary>
        public void UpdateGoal(FlowGoalHandle handle, float2 position)
        {
            if (!IsValid(handle)) throw new InvalidOperationException("共享导航目标句柄已失效。");
            if (!math.all(math.isfinite(position))) throw new ArgumentOutOfRangeException(nameof(position));
            Goal goal = goals[handle.Slot];
            position = domain.Normalize(position);
            if (!goal.TargetDirty && position.Equals(goal.Position)) return;
            int2 cell = domain.Normalize((int2)math.floor(position));
            goal.TargetDirty |= !cell.Equals(goal.Cell);
            goal.Cell = cell; goal.Position = position; goalsChanged = true;
        }

        /// <summary>释放一个共享目标，槽位复用前递增代际。</summary>
        public void RemoveGoal(FlowGoalHandle handle)
        {
            if (!IsValid(handle)) return;
            Goal goal = goals[handle.Slot]; goal.Active = false; goal.RouteDirty = true;
            goalsChanged = true;
        }

        /// <summary>只标记受影响块；边缘变化同时使对岸连接待刷新。</summary>
        public void MarkCellDirty(int2 cell)
        {
            cell = domain.Normalize(cell);
            int2 chunk = FlowNavigationMath.ChunkOf(cell, domain);
            MarkChunk(chunk, ChunkDirtyKind.Content);
            int2 local = FlowNavigationMath.LocalCell(FlowNavigationMath.LocalIndex(cell, domain));
            for (int side = 0; side < 4; side++)
            {
                if ((side == 0 && local.x != 15) || (side == 1 && local.x != 0) ||
                    (side == 2 && local.y != 15) || (side == 3 && local.y != 0)) continue;
                MarkChunk(FlowNavigationMath.ChunkOf(cell + FlowNavigationMath.SideOffset(side), domain), ChunkDirtyKind.Connections);
            }
        }

        /// <summary>世界清空只锁存重置，等待读取依赖完成后再释放数据。</summary>
        public void RequestReset() { resetPending = true; }

        /// <summary>完成必要的共享计算并发布一致快照；无变化时直接复用。</summary>
        public FlowNavigationSnapshot Read()
        {
            if (disposed) throw new ObjectDisposedException(nameof(FlowNavigationCache));
            if (resetPending) ResetWorld();
            if (dirty.Count == 0 && !goalsChanged && nativeChunks.IsCreated) return Snapshot();
            readers.Complete(); readers = default;
            bool graphChanged = !nativeChunks.IsCreated;
            changedChunks.Clear();
            builds.Clear();
            foreach (var entry in dirty)
            {
                ChunkRefreshResult result = RefreshChunk(entry.Key,
                    (entry.Value & ChunkDirtyKind.Content) != 0,
                    (entry.Value & ChunkDirtyKind.Connections) != 0);
                if (result == ChunkRefreshResult.None) continue;
                changedChunks.Add(new ChunkPublication(entry.Key, result));
                graphChanged |= result == ChunkRefreshResult.Graph;
            }
            int exitBuildCount = builds.Count;
            dirty.Clear();

            for (int slot = 0; slot < goals.Count; slot++)
            {
                Goal goal = goals[slot];
                if (!goal.Active || !goal.TargetDirty) continue;
                int2 coordinate = FlowNavigationMath.ChunkOf(goal.Cell, domain);
                if (chunks.TryGetValue(coordinate, out CachedChunk chunk))
                    builds.Add(new FieldBuild(chunk.Cells, FlowNavigationMath.LocalIndex(goal.Cell, domain), goal.Field));
                else goal.Field.Clear();
            }
            if (builds.Count > 0)
            {
                BuildFields();
                ExitFieldBuilds += exitBuildCount;
                TargetFieldBuilds += builds.Count - exitBuildCount;
            }
            if (changedChunks.Count > 0 || !nativeChunks.IsCreated) PublishChunks(changedChunks, graphChanged);
            if (graphChanged || goalsChanged) PublishGoals(graphChanged);
            goalsChanged = false;
            CaptureSnapshot();
            return Snapshot();
        }

        /// <summary>释放全部 Native 所有权，调用方不能继续使用以前取得的视图。</summary>
        public void Dispose()
        {
            if (disposed) return;
            readers.Complete(); DisposeNative(); chunks.Clear(); goals.Clear(); dirty.Clear(); disposed = true;
        }
        #endregion

        #region 脏块与出口生成
        /// <summary>把格内容和邻块连接标记分别保留，水面更新可跳过出口扫描。</summary>
        private enum ChunkDirtyKind : byte { Connections = 1, Content = 2 }

        /// <summary>合并同块的内容与边缘连接脏标记。</summary>
        private void MarkChunk(int2 coordinate, ChunkDirtyKind kind)
        {
            dirty.TryGetValue(coordinate, out ChunkDirtyKind previous);
            dirty[coordinate] = previous | kind;
        }

        /// <summary>重建世界身份和首次块清单，不消费旧路径管理器的变更队列。</summary>
        private void ResetWorld()
        {
            readers.Complete(); readers = default; DisposeNative();
            chunks.Clear(); dirty.Clear(); goals.Clear(); changedChunks.Clear();
            domain = source.Domain;
            if (domain.IsWrapped && (math.any(domain.Span <= 0) || math.any(domain.Span % 16 != 0)))
                throw new InvalidOperationException("16×16 分层导航要求循环世界跨度由完整 Chunk 组成。");
            Epoch = ++nextEpoch; resetPending = false; goalsChanged = true;
            coordinates.Clear(); source.CollectChunks(coordinates);
            foreach (int2 coordinate in coordinates) MarkChunk(coordinate, ChunkDirtyKind.Content);
        }

        /// <summary>区分表层数据变化与代价/出口图变化，避免液体表现刷新触发路线重建。</summary>
        private enum ChunkRefreshResult : byte { None, Surface, Graph }

        /// <summary>记录需要发布的块及其失效层级。</summary>
        private readonly struct ChunkPublication
        {
            internal readonly int2 Coordinate;
            internal readonly ChunkRefreshResult Result;
            internal ChunkPublication(int2 coordinate, ChunkRefreshResult result)
            { Coordinate = coordinate; Result = result; }
        }

        /// <summary>冻结受影响块的权重，并只为代价或出口变化生成缺失图。</summary>
        private ChunkRefreshResult RefreshChunk(int2 coordinate, bool contentDirty, bool connectionsDirty)
        {
            bool exists = chunks.TryGetValue(coordinate, out CachedChunk chunk);
            if (!exists) chunk = new CachedChunk(coordinate);
            bool costChanged = !exists;
            bool surfaceChanged = !exists;
            int registered = 0;
            int2 origin = FlowNavigationMath.Origin(coordinate, domain);
            if (contentDirty || !exists)
            {
                for (int cell = 0; cell < 256; cell++)
                {
                    bool loaded = source.TryGetCell(origin + FlowNavigationMath.LocalCell(cell), out FlowNavigationCellData data);
                    if (loaded) registered++;
                    int cost = loaded && data.Penalty > 0 ? FlowNavigationMath.TerrainCost(data.Penalty) : -1;
                    byte water = loaded && data.Penalty > 0 ? data.Water : (byte)0;
                    float liquidDepth = water != 0 ? math.saturate(data.LiquidDepth) : 0f;
                    float2 waterCurrent = water != 0 ? data.WaterCurrent : float2.zero;
                    costChanged |= chunk.Cells[cell] != cost;
                    surfaceChanged |= chunk.Water[cell] != water ||
                                      !chunk.LiquidDepth[cell].Equals(liquidDepth) ||
                                      !chunk.WaterCurrent[cell].Equals(waterCurrent);
                    chunk.Cells[cell] = cost;
                    chunk.Water[cell] = water;
                    chunk.LiquidDepth[cell] = liquidDepth;
                    chunk.WaterCurrent[cell] = waterCurrent;
                }
                if (registered == 0)
                {
                    if (!exists) return ChunkRefreshResult.None;
                    chunks.Remove(coordinate); MarkTargetChunkChanged(coordinate); return ChunkRefreshResult.Graph;
                }
            }
            List<FlowPortal> portals = costChanged || connectionsDirty
                ? DiscoverPortals(chunk) : chunk.Portals;
            bool portalsChanged = !ReferenceEquals(portals, chunk.Portals) && !SamePortals(chunk.Portals, portals);
            if (!costChanged && !surfaceChanged && !portalsChanged) return ChunkRefreshResult.None;
            if (!costChanged && !portalsChanged)
            {
                // 水面数据已写入原有块缓存，出口与局部图无需分配临时集合或重新筛选。
                ChunkContentBuilds++;
                return ChunkRefreshResult.Surface;
            }
            chunks[coordinate] = chunk;
            if (costChanged || surfaceChanged) ChunkContentBuilds++;
            if (costChanged) MarkTargetChunkChanged(coordinate);
            if (portalsChanged) PortalConnectionBuilds++;
            chunk.Portals = portals;
            var retained = new HashSet<int>();
            foreach (FlowPortal portal in portals)
            {
                if (!retained.Add(portal.Anchor)) continue;
                bool cached = chunk.Fields.TryGetValue(portal.Anchor, out LocalField field);
                if (!cached) { field = new LocalField(); chunk.Fields.Add(portal.Anchor, field); }
                if (costChanged || !cached) builds.Add(new FieldBuild(chunk.Cells, portal.Anchor, field));
            }
            var obsolete = new List<int>();
            foreach (int seed in chunk.Fields.Keys) if (!retained.Contains(seed)) obsolete.Add(seed);
            foreach (int seed in obsolete) chunk.Fields.Remove(seed);
            return costChanged || portalsChanged ? ChunkRefreshResult.Graph : ChunkRefreshResult.Surface;
        }

        /// <summary>从双方均可通行的连续边缘格生成缺口，一侧可以有多个出口。</summary>
        private List<FlowPortal> DiscoverPortals(CachedChunk chunk)
        {
            var result = new List<FlowPortal>();
            int2 origin = FlowNavigationMath.Origin(chunk.Coordinate, domain);
            for (int side = 0; side < 4; side++)
            {
                int first = -1;
                for (int offset = 0; offset <= 16; offset++)
                {
                    int cell = offset < 16 ? FlowNavigationMath.EdgeCell(side, offset) : 0;
                    bool open = offset < 16 && chunk.Cells[cell] >= 0 &&
                        source.TryGetCell(domain.Normalize(origin + FlowNavigationMath.LocalCell(cell) + FlowNavigationMath.SideOffset(side)), out FlowNavigationCellData neighbour) &&
                        neighbour.Penalty > 0;
                    if (open) { if (first < 0) first = offset; continue; }
                    if (first < 0) continue;
                    int last = offset - 1;
                    result.Add(new FlowPortal { Side = side, First = first, Last = last,
                        Anchor = FlowNavigationMath.EdgeCell(side, (first + last) / 2), Pair = -1 });
                    first = -1;
                }
            }
            return result;
        }

        /// <summary>比较出口的实际缺口和锚点，而不是只比较出口数量。</summary>
        private static bool SamePortals(List<FlowPortal> a, List<FlowPortal> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].Side != b[i].Side || a[i].First != b[i].First || a[i].Last != b[i].Last) return false;
            return true;
        }

        /// <summary>只有目标块的内容变化才使其目标积分图失效。</summary>
        private void MarkTargetChunkChanged(int2 coordinate)
        {
            foreach (Goal goal in goals)
                if (goal.Active && FlowNavigationMath.ChunkOf(goal.Cell, domain).Equals(coordinate)) goal.TargetDirty = true;
        }
        #endregion

        #region 局部图构建
        /// <summary>把本次所有缺失的 256 格图一起调度，仅在发布边界同步一次。</summary>
        private void BuildFields()
        {
            int count = builds.Count;
            int cellCount = checked(count * 256);
            EnsureWorkspace(ref integrationCells, cellCount);
            EnsureWorkspace(ref integrationRequests, count);
            EnsureWorkspace(ref integrationCosts, cellCount);
            EnsureWorkspace(ref integrationDirections, cellCount);
            EnsureWorkspace(ref integrationNodes, cellCount);
            EnsureWorkspace(ref integrationPositions, cellCount);
            NativeArray<int> cells = integrationCells.GetSubArray(0, cellCount);
            NativeArray<FlowIntegrationRequest> requests = integrationRequests.GetSubArray(0, count);
            NativeArray<int> costs = integrationCosts.GetSubArray(0, cellCount);
            NativeArray<byte> directions = integrationDirections.GetSubArray(0, cellCount);
            for (int i = 0; i < count; i++)
            {
                FieldBuild build = builds[i];
                requests[i] = new FlowIntegrationRequest { CellStart = i * 256, Seed = build.Seed };
                NativeArray<int>.Copy(build.Cells, 0, cells, i * 256, 256);
            }
            new FlowIntegrationJob { Cells = cells, Requests = requests, Costs = costs, Directions = directions,
                HeapNodes = integrationNodes.GetSubArray(0, cellCount),
                HeapPositions = integrationPositions.GetSubArray(0, cellCount) }.Schedule(count, 1).Complete();
            for (int i = 0; i < count; i++)
            {
                NativeArray<int>.Copy(costs, i * 256, builds[i].Field.Costs, 0, 256);
                NativeArray<byte>.Copy(directions, i * 256, builds[i].Field.Directions, 0, 256);
            }
        }

        /// <summary>一张可复用的本地反向积分图。</summary>
        private sealed class LocalField
        {
            internal readonly int[] Costs = new int[256];
            internal readonly byte[] Directions = new byte[256];
            /// <summary>初始化为不可达。</summary>
            internal LocalField() { Clear(); }
            /// <summary>清除失效的目标图。</summary>
            internal void Clear() { Array.Fill(Costs, FlowNavigationMath.Infinity); Array.Clear(Directions, 0, Directions.Length); }
        }

        /// <summary>一个加载块持有基础权重及按出口种子共享的局部图。</summary>
        private sealed class CachedChunk
        {
            internal readonly int2 Coordinate;
            internal readonly int[] Cells = new int[256];
            internal readonly byte[] Water = new byte[256];
            internal readonly float[] LiquidDepth = new float[256];
            internal readonly float2[] WaterCurrent = new float2[256];
            internal readonly Dictionary<int, LocalField> Fields = new();
            internal List<FlowPortal> Portals = new();
            /// <summary>记录块坐标，权重将在读取源快照时填充。</summary>
            internal CachedChunk(int2 coordinate) { Coordinate = coordinate; Array.Fill(Cells, -1); }
        }

        /// <summary>每个共享目标独立持有局部图和路线失效信息。</summary>
        private sealed class Goal
        {
            internal bool Active, TargetDirty, RouteDirty;
            internal uint Generation;
            internal float2 Position;
            internal int2 Cell, RoutedChunk;
            internal ulong RootMask;
            internal readonly LocalField Field = new();
        }

        /// <summary>一次实际需要构建的局部图。</summary>
        private readonly struct FieldBuild
        {
            internal readonly int[] Cells;
            internal readonly int Seed;
            internal readonly LocalField Field;
            /// <summary>绑定只读块输入与输出缓存。</summary>
            internal FieldBuild(int[] cells, int seed, LocalField field) { Cells = cells; Seed = seed; Field = field; }
        }
        #endregion

        #region 持久计算工作区
        private NativeArray<int> integrationCells, integrationCosts, integrationNodes, integrationPositions;
        private NativeArray<byte> integrationDirections;
        private NativeArray<FlowIntegrationRequest> integrationRequests;
        private NativeArray<int> routeSlotsWorkspace, routeNodesWorkspace, routePositionsWorkspace;

        /// <summary>同步构建结束后按容量复用输入和堆工作区，不按每次目标更新分配 Native 内存。</summary>
        private static void EnsureWorkspace<T>(ref NativeArray<T> array, int count) where T : unmanaged
        {
            if (array.IsCreated && array.Length >= count) return;
            if (array.IsCreated) array.Dispose();
            array = new NativeArray<T>(math.ceilpow2(math.max(1, count)), Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        /// <summary>工作区不借给外部读取者，世界重置或销毁时一起释放。</summary>
        private void DisposeWorkspace()
        {
            if (integrationCells.IsCreated) integrationCells.Dispose();
            if (integrationCosts.IsCreated) integrationCosts.Dispose();
            if (integrationNodes.IsCreated) integrationNodes.Dispose();
            if (integrationPositions.IsCreated) integrationPositions.Dispose();
            if (integrationDirections.IsCreated) integrationDirections.Dispose();
            if (integrationRequests.IsCreated) integrationRequests.Dispose();
            if (routeSlotsWorkspace.IsCreated) routeSlotsWorkspace.Dispose();
            if (routeNodesWorkspace.IsCreated) routeNodesWorkspace.Dispose();
            if (routePositionsWorkspace.IsCreated) routePositionsWorkspace.Dispose();
        }
        #endregion
    }
}
