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
        public readonly int Level;
        public readonly int TypeIndex;
        public readonly string LiquidId;
        public readonly bool Blocked;
        public readonly bool Active;
        public LiquidFlowCell(float groundHeight, int level, int typeIndex, string liquidId, bool blocked, bool active)
        { GroundHeight = groundHeight; Level = LiquidCellValue.NormalizeLevel(level); TypeIndex = typeIndex; LiquidId = liquidId; Blocked = blocked; Active = active; }
        #endregion
    }

    /// <summary>
    /// 无 Unity 依赖的四邻格实验求解器。先冻结候选边，再按整数份分配来源和目标容量。
    /// 复用缓冲；不持有世界权威数据、不调用渲染或存档，每次交换都同量扣源和增加目标。
    /// </summary>
    public sealed class LiquidFlowSolver
    {
        #region 复用缓冲
        private readonly List<Transfer> transfers = new();
        private int[] outgoing = Array.Empty<int>();
        private int[] incoming = Array.Empty<int>();
        private int[] delta = Array.Empty<int>();
        private int[] selectedSource = Array.Empty<int>();
        public int[] ResultLevel { get; private set; } = Array.Empty<int>();
        public int[] ResultType { get; private set; } = Array.Empty<int>();
        public float[] FlowX { get; private set; } = Array.Empty<float>();
        public float[] FlowY { get; private set; } = Array.Empty<float>();
        public int TransferCount { get; private set; }

        private readonly struct Transfer
        {
            public readonly int From, To, X, Y;
            public readonly int Amount;
            public readonly double Head;
            public Transfer(int from, int to, int x, int y, int amount, double head)
            { From = from; To = to; X = x; Y = y; Amount = amount; Head = head; }
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
            transfers.Sort(CompareTransfers);
            ApplyTransfers(cells, settings);
            for (int i = 0; i < cells.Count; i++)
            {
                ResultLevel[i] = cells[i].Level + delta[i];
                ResultType[i] = ResultLevel[i] == 0 ? 0 : cells[i].TypeIndex;
                if (ResultType[i] == 0 && ResultLevel[i] > 0 && selectedSource[i] >= 0)
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
                    (a.Level > 0 && b.Level > 0 && a.TypeIndex != b.TypeIndex)) continue;
                double head = (double)a.GroundHeight + LiquidCellValue.ToNormalizedDepth(a.Level) -
                    b.GroundHeight - LiquidCellValue.ToNormalizedDepth(b.Level);
                if (Math.Abs(head) <= 1d / LiquidCellValue.MaximumLevel + settings.SurfaceEpsilon) continue;
                int from = head > 0d ? i : j, to = head > 0d ? j : i;
                if (cells[from].Level <= 0 || cells[to].Level >= LiquidCellValue.MaximumLevel) continue;
                // 有足够液面差时至少转移一整份，小速率不会被每 Tick 取整永久卡住。
                int amount = Math.Max(1, (int)Math.Floor(Math.Abs(head) * LiquidCellValue.MaximumLevel * .5d * settings.FlowSpeed / 4d));
                amount = Math.Min(amount, Math.Min(cells[from].Level, LiquidCellValue.MaximumLevel - cells[to].Level));
                int sign = head > 0d ? 1 : -1;
                int dx = direction == 0 ? -sign : direction == 1 ? sign : 0;
                int dy = direction == 2 ? -sign : direction == 3 ? sign : 0;
                transfers.Add(new Transfer(from, to, dx, dy, amount, Math.Abs(head)));
                if (cells[to].Level == 0 && (selectedSource[to] < 0 ||
                    string.CompareOrdinal(cells[from].LiquidId, cells[selectedSource[to]].LiquidId) < 0))
                    selectedSource[to] = from;
            }
        }

        private static int CompareTransfers(Transfer left, Transfer right)
        {
            int head = right.Head.CompareTo(left.Head);
            int source = left.From.CompareTo(right.From);
            return head != 0 ? head : source != 0 ? source : left.To.CompareTo(right.To);
        }

        /// <summary>使用同一个最终转移量扣源增目标，流向来自真实交换量，单位为格子液深/秒。</summary>
        private void ApplyTransfers(IReadOnlyList<LiquidFlowCell> cells, LiquidFlowSettings settings)
        {
            for (int i = 0; i < transfers.Count; i++)
            {
                Transfer edge = transfers[i];
                if (!AcceptTransfer(cells, edge)) continue;
                int source = cells[edge.From].Level - outgoing[edge.From];
                int capacity = LiquidCellValue.MaximumLevel - cells[edge.To].Level - incoming[edge.To];
                double head = (double)cells[edge.From].GroundHeight - cells[edge.To].GroundHeight +
                    (cells[edge.From].Level + delta[edge.From] - cells[edge.To].Level - delta[edge.To]) / (double)LiquidCellValue.MaximumLevel;
                if (head <= 1d / LiquidCellValue.MaximumLevel + settings.SurfaceEpsilon) continue;
                int balance = (int)Math.Floor((head * LiquidCellValue.MaximumLevel + 1d - settings.SurfaceEpsilon) * .5d);
                int amount = Math.Min(edge.Amount, Math.Min(balance, Math.Min(source, capacity)));
                if (amount <= 0) continue;
                outgoing[edge.From] += amount;
                incoming[edge.To] += amount;
                delta[edge.From] -= amount;
                delta[edge.To] += amount;
                float flux = LiquidCellValue.ToNormalizedDepth(amount) * settings.TickRate;
                FlowX[edge.From] += edge.X * flux; FlowX[edge.To] += edge.X * flux;
                FlowY[edge.From] += edge.Y * flux; FlowY[edge.To] += edge.Y * flux;
                TransferCount++;
            }
        }

        /// <summary>一个原本为空的格子，同一 Tick 只能接收一种液体。</summary>
        private bool AcceptTransfer(IReadOnlyList<LiquidFlowCell> cells, Transfer edge) =>
            cells[edge.To].Level > 0 || selectedSource[edge.To] >= 0 &&
            cells[selectedSource[edge.To]].TypeIndex == cells[edge.From].TypeIndex;

        /// <summary>只在容量增长时分配，稳定范围内的 Tick 复用所有数组。</summary>
        private void EnsureCapacity(int count)
        {
            if (ResultLevel.Length >= count) return;
            int capacity = Math.Max(count, Math.Max(256, ResultLevel.Length * 2));
            outgoing = new int[capacity]; incoming = new int[capacity]; delta = new int[capacity];
            selectedSource = new int[capacity];
            ResultLevel = new int[capacity]; ResultType = new int[capacity];
            FlowX = new float[capacity]; FlowY = new float[capacity];
        }
        #endregion
    }
}
