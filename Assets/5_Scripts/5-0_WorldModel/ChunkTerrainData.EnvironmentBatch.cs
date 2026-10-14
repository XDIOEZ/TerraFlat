using System;
using System.Buffers;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>环境层的一格写入值，索引为 y * Width + x。</summary>
    public readonly struct EnvironmentCellUpdate
    {
        #region 写入值
        public readonly int CellIndex;
        public readonly float Value;
        public EnvironmentCellUpdate(int cellIndex, float value) { CellIndex = cellIndex; Value = value; }
        #endregion
    }

    /// <summary>同步变化回调的无分配索引迭代器，不能保存到回调之外。</summary>
    public readonly struct TerrainChangedCellIndices
    {
        #region 变化格迭代
        private readonly Int2 localCell;
        private readonly ReadOnlyMemory<int> indices;
        private readonly int width;
        public TerrainChangedCellIndices(Int2 localCell, ReadOnlyMemory<int> indices, int width)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            this.localCell = localCell; this.indices = indices; this.width = width;
        }
        public Enumerator GetEnumerator() => new(localCell.Y * width + localCell.X, indices);

        public struct Enumerator
        {
            private readonly int singleIndex;
            private readonly ReadOnlyMemory<int> indices;
            private int position;
            public Enumerator(int singleIndex, ReadOnlyMemory<int> indices)
            { this.singleIndex = singleIndex; this.indices = indices; position = -1; }
            public int Current => indices.IsEmpty ? singleIndex : indices.Span[position];
            public bool MoveNext() => ++position < (indices.IsEmpty ? 1 : indices.Length);
        }
        #endregion
    }

    public sealed partial class ChunkTerrainData
    {
        #region 环境层批量写回
        private sealed class EnvironmentBatchState
        {
            public readonly int[] DirtyIndices;
            public int Count;
            public long Revision;
            public EnvironmentBatchState(int count) { DirtyIndices = new int[count]; }
        }
        private readonly Dictionary<string, EnvironmentBatchState> environmentBatches = new(StringComparer.Ordinal);
        private readonly List<string> pendingEnvironmentLayers = new();
        private bool publishingEnvironmentBatches;

        /// <summary>只读视图仅限同步读取，不能跨回调、帧或地形释放保存。</summary>
        public bool TryGetEnvironmentLayer(string layerId, out ReadOnlyMemory<float> values)
        {
            ThrowIfDisposed();
            if (_environmentLayers.TryGetValue(layerId, out float[] layer))
            {
                values = layer.AsMemory(0, CellCount);
                return true;
            }
            values = default;
            return false;
        }

        /// <summary>先校验整批唯一递增索引，再写回同一环境层，每批只递增一次版本。</summary>
        public int SetEnvironmentBatch(string layerId, ReadOnlySpan<EnvironmentCellUpdate> updates,
            bool deferNotification = false)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(layerId))
                throw new ArgumentException("Environment layer id is required.", nameof(layerId));
            EnsureEnvironmentWriteAllowed(layerId);
            int previousIndex = -1;
            for (int i = 0; i < updates.Length; i++)
            {
                EnvironmentCellUpdate update = updates[i];
                if ((uint)update.CellIndex >= (uint)CellCount || update.CellIndex <= previousIndex)
                    throw new ArgumentException("环境批次索引必须在区块范围内且严格递增。", nameof(updates));
                if (float.IsNaN(update.Value) || float.IsInfinity(update.Value))
                    throw new ArgumentException("环境批次不能写入非有限数值。", nameof(updates));
                previousIndex = update.CellIndex;
            }
            if (updates.Length == 0) return 0;
            if (!_environmentLayers.TryGetValue(layerId, out float[] values))
            {
                values = ArrayPool<float>.Shared.Rent(CellCount);
                Array.Clear(values, 0, CellCount);
                _environmentLayers.Add(layerId, values);
            }
            if (!environmentBatches.TryGetValue(layerId, out EnvironmentBatchState state))
            {
                state = new EnvironmentBatchState(CellCount);
                environmentBatches.Add(layerId, state);
            }
            for (int i = 0; i < updates.Length; i++)
            {
                EnvironmentCellUpdate update = updates[i];
                if (values[update.CellIndex].Equals(update.Value)) continue;
                values[update.CellIndex] = update.Value;
                state.DirtyIndices[state.Count++] = update.CellIndex;
            }
            int count = state.Count;
            if (count > 0)
            {
                state.Revision = ++_revision;
                if (string.Equals(layerId, TerrainSupportLayer.TileIdLayer, StringComparison.Ordinal))
                    _surfaceLiquidRevision++;
                pendingEnvironmentLayers.Add(layerId);
            }
            if (!deferNotification) PublishEnvironmentBatches();
            return count;
        }

        /// <summary>全部相关层写回后逐层通知，回调异常保留未发批次供重试。</summary>
        public void PublishEnvironmentBatches()
        {
            ThrowIfDisposed();
            if (publishingEnvironmentBatches)
                throw new InvalidOperationException("环境批次回调期间不能嵌套发布或批写。");
            publishingEnvironmentBatches = true;
            try
            {
                while (pendingEnvironmentLayers.Count > 0)
                {
                    string layerId = pendingEnvironmentLayers[0];
                    EnvironmentBatchState state = environmentBatches[layerId];
                    Changed?.Invoke(new ChunkTerrainChanged(state.DirtyIndices.AsMemory(0, state.Count),
                        state.Revision, layerId));
                    state.Count = 0;
                    pendingEnvironmentLayers.RemoveAt(0);
                }
            }
            finally
            {
                publishingEnvironmentBatches = false;
            }
        }

        private void EnsureEnvironmentWriteAllowed(string layerId)
        {
            if (publishingEnvironmentBatches ||
                environmentBatches.TryGetValue(layerId, out EnvironmentBatchState state) && state.Count > 0)
                throw new InvalidOperationException("同层环境批次尚未发布，或观察者正在处理批次。");
        }

        private void ClearEnvironmentBatches()
        {
            pendingEnvironmentLayers.Clear();
            environmentBatches.Clear();
        }
        #endregion
    }
}
