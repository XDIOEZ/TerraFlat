using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>实验液体参数；默认 5Hz、每帧最多一步。范围与区块上限限制海洋传播，不改变存档格式。</summary>
    [Serializable]
    public sealed class LiquidFlowSettings
    {
        #region 可配置规则
        public float TickRate = 5f; // 独立液体时钟，最高 10Hz。
        public float FlowSpeed = 0.5f; // 每 Tick 液面差转移倍率。
        public float SurfaceEpsilon = 0.0005f; // 液面接近平衡时停止。
        public float MinimumTransfer = 0.00001f; // 小流量不参与交换，不能删除残余液体。
        public int SleepTicks = 3; // 连续无变化后休眠。
        public float DenseThreshold = 0.5f; // 活动格占比达到此值时扫描全块。
        public int RegionRadiusChunks = 2; // 外部修改点周围最多两圈区块。
        public int MaxActiveChunks = 25; // 包括等待数据的额外租约。
        public int MaxPendingLoads = 2; // 限制同时请求邻区数据。

        /// <summary>运行器使用配置副本，Inspector/MOD 修改须重新启用后生效。</summary>
        public LiquidFlowSettings Snapshot() { Validate(); return (LiquidFlowSettings)MemberwiseClone(); }

        /// <summary>在接入前拒绝非法配置，避免 NaN 或无限范围进入热路径。</summary>
        public void Validate()
        {
            if (!float.IsFinite(TickRate) || TickRate < 1f || TickRate > 10f ||
                !float.IsFinite(FlowSpeed) || FlowSpeed <= 0f || FlowSpeed > 1f ||
                !float.IsFinite(SurfaceEpsilon) || SurfaceEpsilon <= 0f || SurfaceEpsilon >= 1f ||
                !float.IsFinite(MinimumTransfer) || MinimumTransfer <= 0f || MinimumTransfer >= 1f ||
                !float.IsFinite(DenseThreshold) || DenseThreshold <= 0f || DenseThreshold > 1f ||
                SleepTicks < 1 || SleepTicks > 255 || RegionRadiusChunks < 1 || RegionRadiusChunks > 8 ||
                MaxActiveChunks < 1 || MaxActiveChunks > 256 || MaxPendingLoads < 1 || MaxPendingLoads > MaxActiveChunks)
                throw new ArgumentOutOfRangeException(nameof(LiquidFlowSettings), "液体实验参数超出有限模拟范围。");
        }
        #endregion
    }

    /// <summary>Tick 开始时冻结的一格数据；稳定液体名只用于空格被不同液体竞争时的确定性裁决。</summary>
    public readonly struct LiquidFlowCell
    {
        #region 输入快照
        public readonly float GroundHeight;
        public readonly float Depth;
        public readonly int TypeIndex;
        public readonly string LiquidId;
        public readonly bool Blocked;
        public readonly bool Active;
        public LiquidFlowCell(float groundHeight, float depth, int typeIndex, string liquidId, bool blocked, bool active)
        { GroundHeight = groundHeight; Depth = depth; TypeIndex = typeIndex; LiquidId = liquidId; Blocked = blocked; Active = active; }
        #endregion
    }

    /// <summary>
    /// 无 Unity 依赖的四邻格实验求解器。先冻结候选边，再同时限制来源总流出和目标总流入，最后生成写回值。
    /// 复用缓冲；不持有世界权威数据、不调用渲染或存档。浮点舍入允许机器精度误差，绝不按阈值删除残液。
    /// </summary>
    public sealed class LiquidFlowSolver
    {
        #region 复用缓冲
        private readonly List<Transfer> transfers = new();
        private double[] outgoing = Array.Empty<double>();
        private double[] incoming = Array.Empty<double>();
        private double[] delta = Array.Empty<double>();
        private int[] selectedSource = Array.Empty<int>();
        public float[] ResultDepth { get; private set; } = Array.Empty<float>();
        public int[] ResultType { get; private set; } = Array.Empty<int>();
        public float[] FlowX { get; private set; } = Array.Empty<float>();
        public float[] FlowY { get; private set; } = Array.Empty<float>();
        public int TransferCount { get; private set; }

        private readonly struct Transfer
        {
            public readonly int From, To, X, Y;
            public readonly double Amount;
            public Transfer(int from, int to, int x, int y, double amount)
            { From = from; To = to; X = x; Y = y; Amount = amount; }
        }
        #endregion

        #region 两阶段求解
        /// <summary>邻接表按每格左、右、下、上排列，未知/范围外邻格为 -1；只读取冻结输入。</summary>
        public void Calculate(IReadOnlyList<LiquidFlowCell> cells, ReadOnlySpan<int> neighbours, LiquidFlowSettings settings)
        {
            if (cells == null || neighbours.Length != cells.Count * 4) throw new ArgumentException("液体邻接表与格数不一致。");
            EnsureCapacity(cells.Count);
            transfers.Clear();
            TransferCount = 0;
            Array.Clear(outgoing, 0, cells.Count);
            Array.Clear(incoming, 0, cells.Count);
            Array.Clear(delta, 0, cells.Count);
            Array.Clear(FlowX, 0, cells.Count);
            Array.Clear(FlowY, 0, cells.Count);
            for (int i = 0; i < cells.Count; i++) selectedSource[i] = -1;
            BuildTransfers(cells, neighbours, settings);
            AccumulateCapacity(cells);
            ApplyTransfers(cells, settings);
            for (int i = 0; i < cells.Count; i++)
            {
                ResultDepth[i] = (float)Math.Clamp(cells[i].Depth + delta[i], 0d, 1d);
                ResultType[i] = ResultDepth[i] == 0f ? 0 : cells[i].TypeIndex;
                if (ResultType[i] == 0 && ResultDepth[i] > 0f && selectedSource[i] >= 0)
                    ResultType[i] = cells[selectedSource[i]].TypeIndex;
            }
        }

        /// <summary>每条相邻边只计算一次；空格争用按稳定 LiquidId 裁决，避免遍历顺序改变液体身份。</summary>
        private void BuildTransfers(IReadOnlyList<LiquidFlowCell> cells, ReadOnlySpan<int> neighbours, LiquidFlowSettings settings)
        {
            for (int i = 0; i < cells.Count; i++)
            for (int direction = 0; direction < 4; direction++)
            {
                int j = neighbours[i * 4 + direction];
                if (j <= i || j >= cells.Count) continue;
                LiquidFlowCell a = cells[i], b = cells[j];
                if ((!a.Active && !b.Active) || a.Blocked || b.Blocked ||
                    (a.Depth > 0f && b.Depth > 0f && a.TypeIndex != b.TypeIndex)) continue;
                double head = (double)a.GroundHeight + a.Depth - b.GroundHeight - b.Depth;
                if (Math.Abs(head) <= settings.SurfaceEpsilon) continue;
                int from = head > 0d ? i : j, to = head > 0d ? j : i;
                if (cells[from].Depth <= 0f || cells[to].Depth >= 1f) continue;
                // 四个方向共享松弛预算；避免棋盘高低液面在相邻 Tick 之间整体交换而永不休眠。
                double amount = Math.Min(Math.Abs(head) * 0.5d * settings.FlowSpeed / 4d,
                    Math.Min(cells[from].Depth, 1d - cells[to].Depth));
                if (amount < settings.MinimumTransfer) continue;
                int sign = head > 0d ? 1 : -1;
                int dx = direction == 0 ? -sign : direction == 1 ? sign : 0;
                int dy = direction == 2 ? -sign : direction == 3 ? sign : 0;
                transfers.Add(new Transfer(from, to, dx, dy, amount));
                if (cells[to].Depth == 0f && (selectedSource[to] < 0 ||
                    string.CompareOrdinal(cells[from].LiquidId, cells[selectedSource[to]].LiquidId) < 0))
                    selectedSource[to] = from;
            }
        }

        /// <summary>累计整格需求；不能只逐边限量，否则四个邻格会同时透支来源或灌满同一空格。</summary>
        private void AccumulateCapacity(IReadOnlyList<LiquidFlowCell> cells)
        {
            for (int i = 0; i < transfers.Count; i++)
            {
                Transfer edge = transfers[i];
                if (!AcceptTransfer(cells, edge)) continue;
                outgoing[edge.From] += edge.Amount;
                incoming[edge.To] += edge.Amount;
            }
        }

        /// <summary>使用同一个最终转移量扣源增目标，流向来自真实交换量，单位为格子液深/秒。</summary>
        private void ApplyTransfers(IReadOnlyList<LiquidFlowCell> cells, LiquidFlowSettings settings)
        {
            for (int i = 0; i < transfers.Count; i++)
            {
                Transfer edge = transfers[i];
                if (!AcceptTransfer(cells, edge)) continue;
                double scale = Math.Min(1d, Math.Min(cells[edge.From].Depth / outgoing[edge.From],
                    (1d - cells[edge.To].Depth) / incoming[edge.To]));
                double amount = edge.Amount * scale;
                if (amount < settings.MinimumTransfer) continue;
                delta[edge.From] -= amount;
                delta[edge.To] += amount;
                float flux = (float)(amount * settings.TickRate);
                FlowX[edge.From] += edge.X * flux; FlowX[edge.To] += edge.X * flux;
                FlowY[edge.From] += edge.Y * flux; FlowY[edge.To] += edge.Y * flux;
                TransferCount++;
            }
        }

        /// <summary>一个原本为空的格子，同一 Tick 只能接收一种液体。</summary>
        private bool AcceptTransfer(IReadOnlyList<LiquidFlowCell> cells, Transfer edge) =>
            cells[edge.To].Depth > 0f || selectedSource[edge.To] >= 0 &&
            cells[selectedSource[edge.To]].TypeIndex == cells[edge.From].TypeIndex;

        /// <summary>只在容量增长时分配，稳定范围内的 Tick 复用所有数组。</summary>
        private void EnsureCapacity(int count)
        {
            if (ResultDepth.Length >= count) return;
            int capacity = Math.Max(count, Math.Max(256, ResultDepth.Length * 2));
            outgoing = new double[capacity]; incoming = new double[capacity]; delta = new double[capacity];
            selectedSource = new int[capacity];
            ResultDepth = new float[capacity]; ResultType = new int[capacity];
            FlowX = new float[capacity]; FlowY = new float[capacity];
        }
        #endregion
    }
}
