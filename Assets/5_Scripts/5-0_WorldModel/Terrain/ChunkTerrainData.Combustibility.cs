using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>点燃概率按每个固定步判定，燃料量以游戏秒计。</summary>
    public readonly struct WorldCombustibilityValue
    {
        #region 可燃性数值
        public readonly float IgnitionProbability;
        public readonly float FuelSeconds;
        public WorldCombustibilityValue(float ignitionProbability, float fuelSeconds)
        { IgnitionProbability = ignitionProbability; FuelSeconds = fuelSeconds; }
        #endregion
    }

    public struct WorldCombustibilityCellState
    {
        #region 可燃性持久差量
        public int CellIndex;
        public bool OverridesBase;
        public float IgnitionProbability, FuelSeconds, ConsumedFuelSeconds;
        #endregion
    }

    public sealed partial class ChunkTerrainData
    {
        #region 可燃性基础缓存与来源合成
        private WorldCombustibilityValue[] combustibilityBase;
        private bool[] combustibilityBaseReady;
        private int[] combustibilityBaseIdentities;
        private readonly Dictionary<int, WorldCombustibilityValue> combustibilitySources = new();
        private readonly Dictionary<int, WorldCombustibilityCellState> combustibilityChanges = new();

        public bool HasCachedCombustibilityBase(int cellIndex)
            => combustibilityBaseReady != null && combustibilityBaseReady[cellIndex];

        public void CacheCombustibilityBase(int cellIndex, WorldCombustibilityValue value, int identity = 0)
        {
            if (combustibilityBase == null)
            {
                combustibilityBase = new WorldCombustibilityValue[CellCount];
                combustibilityBaseReady = new bool[CellCount];
                combustibilityBaseIdentities = new int[CellCount];
            }
            bool consumptionChanged = false;
            if (combustibilityBaseIdentities[cellIndex] != identity && combustibilityBase[cellIndex].FuelSeconds > 0f &&
                combustibilityChanges.TryGetValue(cellIndex, out var change) && !change.OverridesBase)
            {
                combustibilitySources.TryGetValue(cellIndex, out var source);
                float consumed = Math.Min(change.ConsumedFuelSeconds, source.FuelSeconds);
                consumptionChanged = consumed != change.ConsumedFuelSeconds;
                change.ConsumedFuelSeconds = consumed;
                combustibilityChanges[cellIndex] = change;
            }
            combustibilityBase[cellIndex] = value;
            combustibilityBaseIdentities[cellIndex] = identity;
            combustibilityBaseReady[cellIndex] = true;
            if (consumptionChanged) PublishCombustibilityChange(cellIndex);
        }

        public void InvalidateAllCombustibilityBaseCaches()
        { if (combustibilityBaseReady != null) Array.Clear(combustibilityBaseReady, 0, CellCount); }

        public void InvalidateCombustibilityBase(int cellIndex)
        { if (combustibilityBaseReady != null) combustibilityBaseReady[cellIndex] = false; }

        public WorldCombustibilityValue GetCombustibility(int x, int y)
        {
            int index = GetIndex(x, y);
            combustibilityChanges.TryGetValue(index, out var change);
            if (change.OverridesBase)
                return new(change.IgnitionProbability, Math.Max(0f, change.FuelSeconds - change.ConsumedFuelSeconds));
            WorldCombustibilityValue baseline = combustibilityBase == null ? default : combustibilityBase[index];
            combustibilitySources.TryGetValue(index, out var source);
            return new(Math.Max(baseline.IgnitionProbability, source.IgnitionProbability),
                Math.Max(0f, baseline.FuelSeconds + source.FuelSeconds - change.ConsumedFuelSeconds));
        }

        public void SetCombustibility(int x, int y, WorldCombustibilityValue value)
        {
            int index = GetIndex(x, y);
            combustibilityChanges[index] = new WorldCombustibilityCellState
            {
                CellIndex = index, OverridesBase = true,
                IgnitionProbability = Math.Clamp(value.IgnitionProbability, 0f, 1f), FuelSeconds = Math.Max(0f, value.FuelSeconds)
            };
            PublishCombustibilityChange(index);
        }

        public void SetCombustibilitySourceContribution(int cellIndex, WorldCombustibilityValue value, bool preserveFuelConsumption = false)
        {
            combustibilitySources.TryGetValue(cellIndex, out var previous);
            if (previous.IgnitionProbability == value.IgnitionProbability && previous.FuelSeconds == value.FuelSeconds) return;
            combustibilitySources[cellIndex] = value;
            if (!preserveFuelConsumption && value.FuelSeconds < previous.FuelSeconds && combustibilityChanges.TryGetValue(cellIndex, out var change) && !change.OverridesBase)
            {
                WorldCombustibilityValue baseline = combustibilityBase == null ? default : combustibilityBase[cellIndex];
                change.ConsumedFuelSeconds = Math.Min(change.ConsumedFuelSeconds, baseline.FuelSeconds + value.FuelSeconds);
                if (change.ConsumedFuelSeconds == 0f) combustibilityChanges.Remove(cellIndex);
                else combustibilityChanges[cellIndex] = change;
            }
            PublishCombustibilityChange(cellIndex);
        }

        // 火场统一提交批次通知，逐格扣料不额外触发事件。
        public void ConsumeCombustibilityFuel(int cellIndex, float seconds)
        {
            if (seconds <= 0f) return;
            combustibilityChanges.TryGetValue(cellIndex, out var change);
            change.CellIndex = cellIndex;
            change.ConsumedFuelSeconds += seconds;
            combustibilityChanges[cellIndex] = change;
        }

        private void PublishCombustibilityChange(int index)
            => Changed?.Invoke(new ChunkTerrainChanged(new Int2(index % Width, index / Width), TerrainChangeKind.Combustibility, ++_revision));

        public WorldCombustibilityCellState[] CopyCombustibilityChanges()
        {
            var result = new WorldCombustibilityCellState[combustibilityChanges.Count];
            int i = 0;
            foreach (var change in combustibilityChanges.Values) result[i++] = change;
            Array.Sort(result, (left, right) => left.CellIndex.CompareTo(right.CellIndex));
            return result;
        }

        public void RestoreCombustibilityChanges(ReadOnlySpan<WorldCombustibilityCellState> states)
        {
            combustibilityChanges.Clear();
            foreach (var state in states) combustibilityChanges[state.CellIndex] = state;
        }

        private void ReleaseCombustibilityStorage()
        {
            combustibilityBase = null; combustibilityBaseReady = null; combustibilityBaseIdentities = null;
            combustibilitySources.Clear(); combustibilityChanges.Clear();
        }
        #endregion
    }
}
