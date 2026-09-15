using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace FlatWorld.Navigation
{
    #region 搜索输入与通用堆
    /// <summary>一张 256 格积分图的输入，输出区间由调度者独占分配。</summary>
    internal struct FlowIntegrationRequest
    {
        // 独占输入区间与本地图的种子格。
        public int CellStart;
        public int Seed;
    }

    /// <summary>有索引的减键堆，每个节点最多占一个槽位；复用调用方 scratch，不产生托管分配。</summary>
    internal struct FlowMinHeap
    {
        private NativeArray<int> nodes, positions, costs;
        private int start, count;

        /// <summary>绑定一个独占连续工作区，所有位置由调用方预置为 -1。</summary>
        internal FlowMinHeap(NativeArray<int> nodes, NativeArray<int> positions, NativeArray<int> costs, int start)
        {
            this.nodes = nodes; this.positions = positions; this.costs = costs;
            this.start = start; count = 0;
        }

        /// <summary>加入新节点或在代价降低后上浮。</summary>
        internal void Push(int node)
        {
            int index = positions[start + node];
            if (index == -2) return;
            if (index < 0) { index = count++; nodes[start + index] = node; positions[start + node] = index; }
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!Less(node, nodes[start + parent])) break;
                Swap(index, parent); index = parent;
            }
        }

        /// <summary>取出最低代价节点，同价以节点索引稳定排序。</summary>
        internal bool Pop(out int node)
        {
            node = -1;
            if (count == 0) return false;
            node = nodes[start];
            count--;
            if (count > 0)
            {
                nodes[start] = nodes[start + count]; positions[start + nodes[start]] = 0;
                int index = 0;
                while (index * 2 + 1 < count)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < count && Less(nodes[start + child + 1], nodes[start + child])) child++;
                    if (!Less(nodes[start + child], nodes[start + index])) break;
                    Swap(index, child); index = child;
                }
            }
            positions[start + node] = -2;
            return true;
        }

        /// <summary>比较已存储的当前代价。</summary>
        private bool Less(int a, int b) => costs[start + a] < costs[start + b] ||
            (costs[start + a] == costs[start + b] && a < b);

        /// <summary>交换堆槽并同步节点位置。</summary>
        private void Swap(int a, int b)
        {
            int first = nodes[start + a], second = nodes[start + b];
            nodes[start + a] = second; nodes[start + b] = first;
            positions[start + first] = b; positions[start + second] = a;
        }
    }
    #endregion

    #region 分层共享计算
    /// <summary>按图并行的反向 Dijkstra；复用旧网格的八邻接、目标格权重与禁止切角规则。</summary>
    [BurstCompile]
    internal struct FlowIntegrationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> Cells;
        [ReadOnly] public NativeArray<FlowIntegrationRequest> Requests;
        // 每个任务只写 index*256 开始的独占区间。
        [NativeDisableParallelForRestriction] public NativeArray<int> Costs;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Directions;
        [NativeDisableParallelForRestriction] public NativeArray<int> HeapNodes;
        [NativeDisableParallelForRestriction] public NativeArray<int> HeapPositions;

        /// <summary>从一个出口锚点或目标格积分整个 Chunk。</summary>
        public void Execute(int index)
        {
            FlowIntegrationRequest request = Requests[index];
            int start = index * 256;
            for (int cell = 0; cell < 256; cell++)
            { Costs[start + cell] = FlowNavigationMath.Infinity; Directions[start + cell] = 0; HeapPositions[start + cell] = -1; }
            if (request.Seed < 0 || Cells[request.CellStart + request.Seed] < 0) return;
            var heap = new FlowMinHeap(HeapNodes, HeapPositions, Costs, start);
            Costs[start + request.Seed] = 0; heap.Push(request.Seed);
            while (heap.Pop(out int current))
            {
                int2 point = FlowNavigationMath.LocalCell(current);
                for (byte direction = 1; direction <= 8; direction++)
                {
                    int2 offset = FlowNavigationMath.Direction(direction);
                    int2 neighbour = point + offset;
                    if (math.any(neighbour < 0) || math.any(neighbour >= 16)) continue;
                    int next = neighbour.x + neighbour.y * 16;
                    if (Cells[request.CellStart + next] < 0) continue;
                    bool diagonal = direction >= 5;
                    if (diagonal && (Cells[request.CellStart + point.x + offset.x + point.y * 16] < 0 ||
                        Cells[request.CellStart + point.x + (point.y + offset.y) * 16] < 0)) continue;
                    int cost = Costs[start + current] + (diagonal ? 14 : 10) + Cells[request.CellStart + current];
                    if (cost >= Costs[start + next]) continue;
                    Costs[start + next] = cost;
                    Directions[start + next] = direction <= 4 ? (byte)(direction % 2 == 0 ? direction - 1 : direction + 1) : (byte)(13 - direction);
                    heap.Push(next);
                }
            }
        }
    }

    /// <summary>每个共享目标运行一次出口图搜索，节点数由 Chunk 出口数决定，与 AI 数量无关。</summary>
    [BurstCompile]
    internal struct FlowPortalRouteJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> GoalSlots;
        [ReadOnly] public NativeArray<FlowGoalData> Goals;
        [ReadOnly] public NativeArray<FlowChunkHeader> Chunks;
        [ReadOnly] public NativeArray<FlowPortal> Portals;
        [ReadOnly] public NativeArray<int> Cells;
        [ReadOnly] public NativeArray<int> ExitCosts;
        [ReadOnly] public NativeArray<int> TargetCosts;
        [NativeDisableParallelForRestriction] public NativeArray<int> Routes;
        [NativeDisableParallelForRestriction] public NativeArray<int> HeapNodes;
        [NativeDisableParallelForRestriction] public NativeArray<int> HeapPositions;

        /// <summary>以目标所在连通分量的入口为终点，计算各出口通往目标 Chunk 的剩余代价。</summary>
        public void Execute(int index)
        {
            int goalSlot = GoalSlots[index];
            FlowGoalData goal = Goals[goalSlot];
            int start = goalSlot * Portals.Length;
            var heap = new FlowMinHeap(HeapNodes, HeapPositions, Routes, start);
            for (int portal = 0; portal < Portals.Length; portal++)
            {
                Routes[start + portal] = FlowNavigationMath.Infinity; HeapPositions[start + portal] = -1;
                FlowPortal entry = Portals[portal];
                if (goal.Chunk >= 0 && entry.Chunk == goal.Chunk && TargetCosts[goalSlot * 256 + entry.Anchor] < FlowNavigationMath.Infinity)
                { Routes[start + portal] = 0; heap.Push(portal); }
            }
            while (heap.Pop(out int current))
            {
                FlowPortal entry = Portals[current];
                FlowChunkHeader chunk = Chunks[entry.Chunk];
                for (int p = chunk.PortalStart; p < chunk.PortalStart + chunk.PortalCount; p++)
                    Relax(p, Routes[start + current], ExitCosts[current * 256 + Portals[p].Anchor], start, ref heap);
                if (entry.Pair >= 0)
                    Relax(entry.Pair, Routes[start + current], 10 + Cells[entry.Chunk * 256 + entry.Anchor], start, ref heap);
            }
        }

        /// <summary>更新反向边的起点；不可达距离不参与加法。</summary>
        private void Relax(int node, int remaining, int edge, int start, ref FlowMinHeap heap)
        {
            if (edge >= FlowNavigationMath.Infinity) return;
            int cost = math.min(FlowNavigationMath.Infinity, remaining + edge);
            if (cost >= Routes[start + node]) return;
            Routes[start + node] = cost; heap.Push(node);
        }
    }

    /// <summary>把少量共享目标的出口路线编译成逐格出口索引，AI 热路径只有 O(1) 表读取。</summary>
    [BurstCompile]
    internal struct FlowSelectExitJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> GoalSlots;
        [ReadOnly] public NativeArray<FlowChunkHeader> Chunks;
        [ReadOnly] public NativeArray<FlowPortal> Portals;
        [ReadOnly] public NativeArray<int> Cells;
        [ReadOnly] public NativeArray<int> ExitCosts;
        [ReadOnly] public NativeArray<int> Routes;
        [NativeDisableParallelForRestriction] public NativeArray<int> SelectedExits;

        /// <summary>选择当前格应离开的出口，剩余代价从对岸计算，避免在出口锚点来回折返。</summary>
        public void Execute(int index)
        {
            int chunkIndex = index % Chunks.Length;
            int goal = GoalSlots[index / Chunks.Length];
            FlowChunkHeader chunk = Chunks[chunkIndex];
            for (int cell = 0; cell < 256; cell++)
            {
                int best = FlowNavigationMath.Infinity, selected = -1;
                for (int p = chunk.PortalStart; p < chunk.PortalStart + chunk.PortalCount; p++)
                {
                    int pair = Portals[p].Pair;
                    if (pair < 0) continue;
                    int local = ExitCosts[p * 256 + cell], remaining = Routes[goal * Portals.Length + pair];
                    if (local >= FlowNavigationMath.Infinity || remaining >= FlowNavigationMath.Infinity) continue;
                    FlowPortal other = Portals[pair];
                    int cost = local + 10 + Cells[other.Chunk * 256 + other.Anchor] + remaining;
                    if (cost >= best) continue;
                    best = cost; selected = p;
                }
                SelectedExits[(goal * Chunks.Length + chunkIndex) * 256 + cell] = selected;
            }
        }
    }
    #endregion
}
