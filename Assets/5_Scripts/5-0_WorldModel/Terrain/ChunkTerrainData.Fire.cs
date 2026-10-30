using System;
using System.Buffers;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>火场的唯一格状态，表面燃料累计燃烧量和火势一起冻结保存。</summary>
    [Serializable]
    public struct WorldFireCellState
    {
        #region 火格状态
        public int CellIndex;
        public float Intensity;
        public float SurfaceFuelBurnSeconds;
        #endregion
    }

    public sealed partial class ChunkTerrainData
    {
        #region 稀疏火格与批量变化
        private WorldFireCellState[] fireCellStates;
        private int[] fireCellSlots;
        private readonly List<int> activeFireCells = new();
        public IReadOnlyList<int> ActiveFireCellIndices => activeFireCells;

        public WorldFireCellState GetFireState(int x, int y)
        {
            ThrowIfDisposed();
            int index = GetIndex(x, y);
            WorldFireCellState state = fireCellStates == null ? default : fireCellStates[index];
            state.CellIndex = index;
            return state;
        }

        public WorldFireCellState[] CopyFireStates()
        {
            ThrowIfDisposed();
            var result = new WorldFireCellState[activeFireCells.Count];
            for (int i = 0; i < result.Length; i++) result[i] = fireCellStates[activeFireCells[i]];
            return result;
        }

        // 批次只借用脏索引到同步通知结束，回调期间新写入使用独立租用缓冲。
        public int SetFireStatesBatch(ReadOnlySpan<WorldFireCellState> updates)
        {
            ThrowIfDisposed();
            if (updates.IsEmpty) return 0;
            foreach (WorldFireCellState state in updates)
                if ((uint)state.CellIndex >= (uint)CellCount || !float.IsFinite(state.Intensity) ||
                    state.Intensity < 0f || state.Intensity > 1f || !float.IsFinite(state.SurfaceFuelBurnSeconds) || state.SurfaceFuelBurnSeconds < 0f)
                    throw new ArgumentException("火格状态必须在本块范围内且为有效火势和燃烧进度。", nameof(updates));
            int[] dirty = ArrayPool<int>.Shared.Rent(updates.Length);
            int count = 0;
            bool presentationChanged = false;
            try
            {
                foreach (WorldFireCellState value in updates)
                {
                    int index = value.CellIndex;
                    if (fireCellStates == null)
                    {
                        if (value.Intensity <= 0f) continue;
                        fireCellStates = new WorldFireCellState[CellCount];
                        fireCellSlots = new int[CellCount];
                        _fire = ArrayPool<byte>.Shared.Rent(CellCount);
                        Array.Clear(_fire, 0, CellCount);
                    }
                    WorldFireCellState previous = fireCellStates[index];
                    WorldFireCellState state = value.Intensity > 0f ? value : new WorldFireCellState { CellIndex = index };
                    if (previous.Intensity == state.Intensity && previous.SurfaceFuelBurnSeconds == state.SurfaceFuelBurnSeconds) continue;
                    fireCellStates[index] = state;
                    byte presented = state.Intensity > 0f ? (byte)Math.Max(1, (int)Math.Round(state.Intensity * 255f)) : (byte)0;
                    presentationChanged |= presented != _fire[index];
                    _fire[index] = presented;
                    if (state.Intensity > 0f && fireCellSlots[index] == 0)
                    {
                        activeFireCells.Add(index);
                        fireCellSlots[index] = activeFireCells.Count;
                    }
                    else if (state.Intensity <= 0f && fireCellSlots[index] > 0)
                    {
                        int slot = fireCellSlots[index] - 1;
                        int tail = activeFireCells[activeFireCells.Count - 1];
                        activeFireCells[slot] = tail;
                        fireCellSlots[tail] = slot + 1;
                        activeFireCells.RemoveAt(activeFireCells.Count - 1);
                        fireCellSlots[index] = 0;
                    }
                    dirty[count++] = index;
                }
                if (count > 0) Changed?.Invoke(new ChunkTerrainChanged(dirty.AsMemory(0, count), ++_revision,
                    TerrainChangeKind.Fire, presentationChanged));
                return count;
            }
            finally { ArrayPool<int>.Shared.Return(dirty); }
        }

        public void RestoreFireStates(ReadOnlySpan<WorldFireCellState> states)
        {
            WorldFireCellState[] removed = ArrayPool<WorldFireCellState>.Shared.Rent(activeFireCells.Count);
            int count = activeFireCells.Count;
            for (int i = 0; i < count; i++) removed[i] = new WorldFireCellState { CellIndex = activeFireCells[i] };
            try
            {
                SetFireStatesBatch(removed.AsSpan(0, count));
                SetFireStatesBatch(states);
            }
            finally { ArrayPool<WorldFireCellState>.Shared.Return(removed); }
        }

        private void ReleaseFireCellStorage()
        {
            fireCellStates = null;
            fireCellSlots = null;
            activeFireCells.Clear();
        }
        #endregion
    }
}
