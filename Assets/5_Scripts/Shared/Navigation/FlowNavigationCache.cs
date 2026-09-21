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
        private readonly Dictionary<int2, bool> dirty = new();
        private readonly List<Goal> goals = new();
        private readonly List<int2> coordinates = new();
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
            MarkChunk(chunk, true);
            int2 local = FlowNavigationMath.LocalCell(FlowNavigationMath.LocalIndex(cell, domain));
            for (int side = 0; side < 4; side++)
            {
                if ((side == 0 && local.x != 15) || (side == 1 && local.x != 0) ||
                    (side == 2 && local.y != 15) || (side == 3 && local.y != 0)) continue;
                MarkChunk(FlowNavigationMath.ChunkOf(cell + FlowNavigationMath.SideOffset(side), domain), false);
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
            bool changed = !nativeChunks.IsCreated;
            builds.Clear();
            foreach (var entry in dirty) changed |= RefreshChunk(entry.Key, entry.Value);
            if (builds.Count > 0) { BuildFields(); ExitFieldBuilds += builds.Count; }
            dirty.Clear();

            builds.Clear();
            for (int slot = 0; slot < goals.Count; slot++)
            {
                Goal goal = goals[slot];
                if (!goal.Active || !goal.TargetDirty) continue;
                int2 coordinate = FlowNavigationMath.ChunkOf(goal.Cell, domain);
                if (chunks.TryGetValue(coordinate, out CachedChunk chunk))
                    builds.Add(new FieldBuild(chunk.Cells, FlowNavigationMath.LocalIndex(goal.Cell, domain), goal.Field));
                else goal.Field.Clear();
            }
            if (builds.Count > 0) { BuildFields(); TargetFieldBuilds += builds.Count; }
            if (changed) PublishChunks();
            PublishGoals(changed);
            goalsChanged = false;
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
        /// <summary>合并同块的内容/连接脏标记。</summary>
        private void MarkChunk(int2 coordinate, bool content)
        {
            dirty.TryGetValue(coordinate, out bool previous);
            dirty[coordinate] = previous || content;
        }

        /// <summary>重建世界身份和首次块清单，不消费旧路径管理器的变更队列。</summary>
        private void ResetWorld()
        {
            readers.Complete(); readers = default; DisposeNative();
            chunks.Clear(); dirty.Clear(); goals.Clear();
            domain = source.Domain;
            if (domain.IsWrapped && (math.any(domain.Span <= 0) || math.any(domain.Span % 16 != 0)))
                throw new InvalidOperationException("16×16 分层导航要求循环世界跨度由完整 Chunk 组成。");
            Epoch = ++nextEpoch; resetPending = false; goalsChanged = true;
            coordinates.Clear(); source.CollectChunks(coordinates);
            foreach (int2 coordinate in coordinates) MarkChunk(coordinate, true);
        }

        /// <summary>冻结受影响块的权重，并只为变化后的出口种子生成缺失图。</summary>
        private bool RefreshChunk(int2 coordinate, bool contentDirty)
        {
            bool exists = chunks.TryGetValue(coordinate, out CachedChunk chunk);
            if (!exists) chunk = new CachedChunk(coordinate);
            bool contentChanged = !exists;
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
                    contentChanged |= chunk.Cells[cell] != cost || chunk.Water[cell] != water ||
                                      !chunk.LiquidDepth[cell].Equals(liquidDepth);
                    chunk.Cells[cell] = cost;
                    chunk.Water[cell] = water;
                    chunk.LiquidDepth[cell] = liquidDepth;
                }
                if (registered == 0)
                {
                    if (!exists) return false;
                    chunks.Remove(coordinate); MarkTargetChunkChanged(coordinate); return true;
                }
            }
            List<FlowPortal> portals = DiscoverPortals(chunk);
            bool portalsChanged = !SamePortals(chunk.Portals, portals);
            if (!contentChanged && !portalsChanged) return false;
            chunks[coordinate] = chunk;
            if (contentChanged) { ChunkContentBuilds++; MarkTargetChunkChanged(coordinate); }
            if (portalsChanged) PortalConnectionBuilds++;
            chunk.Portals = portals;
            var retained = new HashSet<int>();
            foreach (FlowPortal portal in portals)
            {
                if (!retained.Add(portal.Anchor)) continue;
                bool cached = chunk.Fields.TryGetValue(portal.Anchor, out LocalField field);
                if (!cached) { field = new LocalField(); chunk.Fields.Add(portal.Anchor, field); }
                if (contentChanged || !cached) builds.Add(new FieldBuild(chunk.Cells, portal.Anchor, field));
            }
            var obsolete = new List<int>();
            foreach (int seed in chunk.Fields.Keys) if (!retained.Contains(seed)) obsolete.Add(seed);
            foreach (int seed in obsolete) chunk.Fields.Remove(seed);
            return true;
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
            using var cells = new NativeArray<int>(count * 256, Allocator.TempJob);
            using var requests = new NativeArray<FlowIntegrationRequest>(count, Allocator.TempJob);
            using var costs = new NativeArray<int>(count * 256, Allocator.TempJob);
            using var directions = new NativeArray<byte>(count * 256, Allocator.TempJob);
            using var nodes = new NativeArray<int>(count * 256, Allocator.TempJob);
            using var positions = new NativeArray<int>(count * 256, Allocator.TempJob);
            // using 变量本身只读；别名只用于填充同一份输入，所有权仍由上方作用域负责。
            NativeArray<int> inputCells = cells;
            NativeArray<FlowIntegrationRequest> inputRequests = requests;
            for (int i = 0; i < count; i++)
            {
                FieldBuild build = builds[i];
                inputRequests[i] = new FlowIntegrationRequest { CellStart = i * 256, Seed = build.Seed };
                for (int cell = 0; cell < 256; cell++) inputCells[i * 256 + cell] = build.Cells[cell];
            }
            new FlowIntegrationJob { Cells = cells, Requests = requests, Costs = costs, Directions = directions,
                HeapNodes = nodes, HeapPositions = positions }.Schedule(count, 1).Complete();
            for (int i = 0; i < count; i++)
                for (int cell = 0; cell < 256; cell++)
                { builds[i].Field.Costs[cell] = costs[i * 256 + cell]; builds[i].Field.Directions[cell] = directions[i * 256 + cell]; }
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
    }
}
