using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FlatWorld.WorldModel
{
    /// <summary>Chunk-local occupancy keyed by the existing ItemData.Guid.</summary>
    public sealed class ChunkOccupancyData
    {
        private readonly Dictionary<Int2, int> owners = new Dictionary<Int2, int>();
        private readonly ReadOnlyDictionary<Int2, int> readOnlyOwners;

        public ChunkOccupancyData() =>
            readOnlyOwners = new ReadOnlyDictionary<Int2, int>(owners);

        public IReadOnlyDictionary<Int2, int> Owners => readOnlyOwners;
        public long Revision { get; private set; }
        public event Action<ChunkOccupancyChanged> Changed;

        public bool IsOccupied(Int2 localCell) => owners.ContainsKey(localCell);
        public bool TryGetOwner(Int2 localCell, out int itemGuid) =>
            owners.TryGetValue(localCell, out itemGuid);

        public bool TryOccupy(int itemGuid, IReadOnlyList<Int2> localCells)
        {
            if (itemGuid <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemGuid));
            if (localCells == null)
                throw new ArgumentNullException(nameof(localCells));
            if (localCells.Count == 0)
                return false;
            for (int i = 0; i < localCells.Count; i++)
            {
                if (owners.TryGetValue(localCells[i], out int owner) && owner != itemGuid)
                    return false;
            }

            var changed = new List<Int2>(localCells.Count);
            for (int i = 0; i < localCells.Count; i++)
            {
                Int2 cell = localCells[i];
                if (owners.TryGetValue(cell, out int owner) && owner == itemGuid)
                    continue;
                owners[cell] = itemGuid;
                changed.Add(cell);
            }
            Publish(itemGuid, changed, true);
            return true;
        }

        public int Release(int itemGuid)
        {
            var cells = new List<Int2>();
            foreach (KeyValuePair<Int2, int> pair in owners)
            {
                if (pair.Value == itemGuid)
                    cells.Add(pair.Key);
            }
            for (int i = 0; i < cells.Count; i++)
                owners.Remove(cells[i]);
            Publish(itemGuid, cells, false);
            return cells.Count;
        }

        public void Clear()
        {
            if (owners.Count == 0)
                return;
            owners.Clear();
            Revision++;
            Changed?.Invoke(new ChunkOccupancyChanged(0, Array.Empty<Int2>(), false, Revision));
        }

        private void Publish(int itemGuid, List<Int2> cells, bool occupied)
        {
            if (cells.Count == 0)
                return;
            Revision++;
            Changed?.Invoke(new ChunkOccupancyChanged(
                itemGuid, cells.ToArray(), occupied, Revision));
        }
    }

    public readonly struct ChunkOccupancyChanged
    {
        public ChunkOccupancyChanged(int itemGuid, IReadOnlyList<Int2> cells,
            bool occupied, long revision)
        {
            ItemGuid = itemGuid;
            Cells = cells ?? Array.Empty<Int2>();
            Occupied = occupied;
            Revision = revision;
        }

        public int ItemGuid { get; }
        public IReadOnlyList<Int2> Cells { get; }
        public bool Occupied { get; }
        public long Revision { get; }
    }
}
