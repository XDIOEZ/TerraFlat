using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 记录一个区块里哪些格子已经被建筑或物品占用。
    /// 它只记“这个格子属于哪个物品”，不负责画碰撞框，也不负责显示地图。
    /// </summary>
    public sealed class ChunkOccupancyData
    {
        // 真正能修改的字典藏在类里面。外面只能看，不能偷偷改掉数据而不通知其他系统。
        private readonly Dictionary<Int2, int> owners = new Dictionary<Int2, int>();
        private readonly ReadOnlyDictionary<Int2, int> readOnlyOwners;

        public ChunkOccupancyData() =>
            readOnlyOwners = new ReadOnlyDictionary<Int2, int>(owners);

        /// <summary>查看每个已占用格子属于哪个物品。</summary>
        public IReadOnlyDictionary<Int2, int> Owners => readOnlyOwners;
        /// <summary>数据每改一次就加 1，别的系统可以靠它判断自己保存的旧结果是否需要更新。</summary>
        public long Revision { get; private set; }
        /// <summary>有格子被占用或空出来时发出通知。</summary>
        public event Action<ChunkOccupancyChanged> Changed;

        /// <summary>看看区块里的某个格子是不是已经有人用了。</summary>
        public bool IsOccupied(Int2 localCell) => owners.ContainsKey(localCell);
        /// <summary>尝试找出某个格子现在属于哪个物品。</summary>
        public bool TryGetOwner(Int2 localCell, out int itemGuid) =>
            owners.TryGetValue(localCell, out itemGuid);

        /// <summary>
        /// 尝试一次占下一组格子。
        /// 只要其中一个格子已经属于别的物品，就一个都不占；已经属于自己的格子不会重复处理。
        /// </summary>
        public bool TryOccupy(int itemGuid, IReadOnlyList<Int2> localCells)
        {
            if (itemGuid <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemGuid));
            if (localCells == null)
                throw new ArgumentNullException(nameof(localCells));
            if (localCells.Count == 0)
                return false;
            // 先把所有格子检查一遍，避免最后出现“只成功占了一半”的尴尬情况。
            for (int i = 0; i < localCells.Count; i++)
            {
                if (owners.TryGetValue(localCells[i], out int owner) && owner != itemGuid)
                    return false;
            }

            // 全部确认没冲突后才正式写入，并只记录这次真正新占到的格子。
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

        /// <summary>让某个物品交还它在这个区块里占用的所有格子，并返回交还了多少格。</summary>
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

        /// <summary>清空全部占用记录；本来就是空的就什么也不做。</summary>
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
            // 实际没有格子变化，就不发通知，免得其他系统白忙一场。
            if (cells.Count == 0)
                return;
            Revision++;
            Changed?.Invoke(new ChunkOccupancyChanged(
                itemGuid, cells.ToArray(), occupied, Revision));
        }
    }

    /// <summary>一次“哪些格子被占用或释放了”的通知内容。</summary>
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

        /// <summary>这次变化由哪个物品引起；全部清空时是 0。</summary>
        public int ItemGuid { get; }
        /// <summary>这次真正发生变化的格子。</summary>
        public IReadOnlyList<Int2> Cells { get; }
        /// <summary>true 是占用，false 是释放。</summary>
        public bool Occupied { get; }
        /// <summary>改完以后，这份占用数据是第几个版本。</summary>
        public long Revision { get; }
    }
}
