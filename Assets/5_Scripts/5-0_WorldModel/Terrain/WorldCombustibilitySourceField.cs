using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>来源只在占地或参数变化时合成，火场读取已合成的格值。</summary>
    public sealed class WorldCombustibilitySourceField
    {
        #region 来源替换与撤销
        private sealed class Source
        {
            internal readonly HashSet<Int2> Cells = new();
            internal WorldCombustibilityValue Value;
        }
        private readonly Dictionary<object, Source> sources = new();
        private readonly Dictionary<Int2, Dictionary<object, WorldCombustibilityValue>> cells = new();
        public event Action<Int2, WorldCombustibilityValue> Changed;

        public WorldCombustibilityValue Get(Int2 cell)
        {
            float probability = 0f, fuel = 0f;
            if (cells.TryGetValue(cell, out var contributors))
                foreach (var value in contributors.Values)
                { probability = Math.Max(probability, value.IgnitionProbability); fuel += value.FuelSeconds; }
            return new(probability, fuel);
        }

        public void ReplaceSource(object owner, IReadOnlyCollection<Int2> occupiedCells, WorldCombustibilityValue value)
        {
            sources.TryGetValue(owner, out var previous);
            if (previous != null && previous.Value.IgnitionProbability == value.IgnitionProbability &&
                previous.Value.FuelSeconds == value.FuelSeconds && previous.Cells.SetEquals(occupiedCells)) return;
            var affected = new HashSet<Int2>();
            if (previous != null)
                foreach (Int2 cell in previous.Cells)
                { cells[cell].Remove(owner); affected.Add(cell); }
            var next = previous ?? new Source();
            next.Cells.Clear(); next.Value = value;
            foreach (Int2 cell in occupiedCells)
            {
                next.Cells.Add(cell); affected.Add(cell);
                if (!cells.TryGetValue(cell, out var contributors)) cells.Add(cell, contributors = new());
                contributors[owner] = value;
            }
            sources[owner] = next;
            Publish(affected);
        }

        public void RemoveSource(object owner)
        {
            if (!sources.Remove(owner, out var source)) return;
            foreach (Int2 cell in source.Cells) cells[cell].Remove(owner);
            Publish(source.Cells);
        }

        private void Publish(HashSet<Int2> affected)
        {
            foreach (Int2 cell in affected)
            {
                WorldCombustibilityValue value = Get(cell);
                if (cells[cell].Count == 0) cells.Remove(cell);
                Changed?.Invoke(cell, value);
            }
        }
        #endregion
    }
}
