using System;

namespace FlatWorld.WorldModel
{
    /// <summary>一次液体写入；局部格索引按行递增，等级沿用权威层的 0～10 整数。</summary>
    public readonly struct LiquidCellUpdate
    {
        #region 写入值
        public readonly int CellIndex; // y * Width + x。
        public readonly int TypeIndex; // 当前资源会话编号。
        public readonly int Level; // 新的权威液体等级。
        public LiquidCellUpdate(int cellIndex, int typeIndex, int level)
        { CellIndex = cellIndex; TypeIndex = typeIndex; Level = level; }
        #endregion
    }

    /// <summary>整个区块的一次液体通知；索引缓冲仅在同步回调期间有效，观察者不得保存借用的内存。</summary>
    public readonly struct ChunkLiquidBatchChanged
    {
        #region 借用的变化集
        public readonly ChunkTerrainData Terrain;
        public readonly ReadOnlyMemory<int> CellIndices;
        public readonly long Revision;
        public ChunkLiquidBatchChanged(ChunkTerrainData terrain, ReadOnlyMemory<int> indices, long revision)
        { Terrain = terrain; CellIndices = indices; Revision = revision; }
        #endregion
    }

    public sealed partial class ChunkTerrainData
    {
        #region 液体批量写回
        private int[] liquidDirtyIndices; // 首次批写时分配，之后复用。
        private int liquidDirtyCount;
        private bool publishingLiquidBatch;
        /// <summary>批量写回专用通知；不会为每一格重复触发 Changed。</summary>
        public event Action<ChunkLiquidBatchChanged> LiquidBatchChanged;

        /// <summary>先完整校验再写回；索引必须唯一且递增。延迟通知用于跨 Chunk 全部写完后统一发布。</summary>
        public int SetLiquidBatch(ReadOnlySpan<LiquidCellUpdate> updates, bool deferNotification = false)
        {
            ThrowIfDisposed();
            if (liquidDirtyCount != 0 || publishingLiquidBatch)
                throw new InvalidOperationException("前一批液体变化尚未发布，或观察者正在处理批次。");
            int previousIndex = -1;
            for (int i = 0; i < updates.Length; i++)
            {
                LiquidCellUpdate update = updates[i];
                if ((uint)update.CellIndex >= (uint)CellCount || update.CellIndex <= previousIndex)
                    throw new ArgumentException("液体批次索引必须在区块范围内且严格递增。", nameof(updates));
                liquid.Normalize(update.TypeIndex, update.Level);
                previousIndex = update.CellIndex;
            }
            if (updates.Length == 0) return 0;
            liquidDirtyIndices ??= new int[CellCount];
            for (int i = 0; i < updates.Length; i++)
            {
                LiquidCellUpdate update = updates[i];
                LiquidCellValue next = liquid.Normalize(update.TypeIndex, update.Level);
                LiquidCellValue previous = liquid.Read(update.CellIndex);
                if (previous.Equals(next)) continue;
                if (!liquidBaselines.ContainsKey(update.CellIndex))
                    liquidBaselines.Add(update.CellIndex, previous);
                liquid.Write(update.CellIndex, next);
                liquidDirtyIndices[liquidDirtyCount++] = update.CellIndex;
            }
            int count = liquidDirtyCount;
            if (count > 0)
            {
                _revision++;
                _surfaceLiquidRevision++;
                liquidRevision++;
            }
            if (!deferNotification) PublishLiquidBatch();
            return count;
        }

        /// <summary>所有相关区块已完成写入后调用一次；回调中禁止嵌套批写同一区块。</summary>
        public void PublishLiquidBatch()
        {
            ThrowIfDisposed();
            if (liquidDirtyCount == 0) return;
            publishingLiquidBatch = true;
            try
            {
                LiquidBatchChanged?.Invoke(new ChunkLiquidBatchChanged(this,
                    liquidDirtyIndices.AsMemory(0, liquidDirtyCount), _revision));
            }
            finally
            {
                liquidDirtyCount = 0;
                publishingLiquidBatch = false;
            }
        }
        #endregion
    }
}
