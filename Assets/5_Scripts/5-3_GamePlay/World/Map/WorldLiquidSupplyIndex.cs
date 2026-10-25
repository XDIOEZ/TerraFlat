using System;
using System.Collections.Generic;

/// <summary>连通水域的真实供水格索引；按整数水位分桶，查询和单格更新不扫描整片水域。</summary>
public sealed class WorldLiquidSupplyIndex<T>
{
    #region 整数水位供水桶
    private sealed class Entry
    {
        public T Cell;
        public int Level;
        public Entry Previous, Next;
    }

    private readonly Dictionary<T, Entry> entries = new();
    private readonly Entry[] heads = new Entry[11];
    private readonly Entry[] tails = new Entry[11];
    public long TotalLevel { get; private set; }
    public int Count => entries.Count;

    public void SetLevel(T cell, int level)
    {
        if ((uint)level > 10) throw new ArgumentOutOfRangeException(nameof(level));
        if (!entries.TryGetValue(cell, out Entry entry))
        {
            entry = new Entry { Cell = cell };
            entries.Add(cell, entry);
        }
        if (entry.Level == level) return;
        TotalLevel += level - entry.Level;
        Unlink(entry);
        entry.Level = level;
        if (level > 0) Append(entry);
    }

    public bool TryGetHighest(out T cell, out int level)
    {
        for (int i = 10; i > 0; i--)
        {
            Entry entry = heads[i];
            if (entry == null) continue;
            cell = entry.Cell;
            level = i;
            return true;
        }
        cell = default;
        level = 0;
        return false;
    }

    private void Unlink(Entry entry)
    {
        if (entry.Level == 0) return;
        if (entry.Previous != null) entry.Previous.Next = entry.Next;
        else heads[entry.Level] = entry.Next;
        if (entry.Next != null) entry.Next.Previous = entry.Previous;
        else tails[entry.Level] = entry.Previous;
        entry.Previous = entry.Next = null;
    }

    private void Append(Entry entry)
    {
        // 同水位按入桶顺序供水，扣量后自然轮到其它格，避免始终挖空泵口附近。
        entry.Previous = tails[entry.Level];
        if (entry.Previous != null) entry.Previous.Next = entry;
        else heads[entry.Level] = entry;
        tails[entry.Level] = entry;
    }
    #endregion
}
