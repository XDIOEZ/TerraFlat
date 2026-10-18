using System.Collections.Generic;
using UnityEngine;

/// <summary>目标声明所属网格、命中格和显示层级，选择器只负责同格分区。</summary>
public interface IGridInteractionTarget : IWorldInteractionTarget
{
    bool TryGetInteractionGridHit(Vector2 pointer, WorldTopologyDomain topology, out GridInteractionHit hit);
}

public readonly struct GridInteractionHit
{
    #region 同格命中信息
    public readonly string GridId;
    public readonly Vector2Int Cell;
    public readonly int Layer;
    public readonly float Height01;

    public GridInteractionHit(string gridId, Vector2Int cell, int layer, float height01)
    { GridId = gridId; Cell = cell; Layer = layer; Height01 = height01; }

    public bool IsSameCell(GridInteractionHit other) => GridId == other.GridId && Cell == other.Cell;
    #endregion
}

/// <summary>每格只保留光标所在等分带的目标，从上到下对应显示底层到高层。</summary>
internal sealed class GridInteractionSelector
{
    #region 同格目标等分
    private readonly struct Entry
    {
        public readonly IGridInteractionTarget Target;
        public readonly GridInteractionHit Hit;
        public Entry(IGridInteractionTarget target, GridInteractionHit hit) { Target = target; Hit = hit; }
    }

    private sealed class EntryComparer : IComparer<Entry>
    {
        public static readonly EntryComparer Instance = new();
        public int Compare(Entry first, Entry second)
        {
            int order = string.CompareOrdinal(first.Hit.GridId, second.Hit.GridId);
            if (order == 0) order = first.Hit.Cell.x.CompareTo(second.Hit.Cell.x);
            if (order == 0) order = first.Hit.Cell.y.CompareTo(second.Hit.Cell.y);
            if (order == 0) order = first.Hit.Layer.CompareTo(second.Hit.Layer);
            return order != 0 ? order : first.Target.TargetGuid.CompareTo(second.Target.TargetGuid);
        }
    }

    private readonly List<Entry> entries = new();
    private readonly HashSet<IInteractable> seen = new();

    // 候选已由发送器校验交互资格和距离，复用缓冲区但不保留跨帧目标。
    public void FilterPointerTargets(List<IInteractable> candidates, Vector2 pointer, WorldTopologyDomain topology)
    {
        try
        {
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                IInteractable candidate = candidates[i];
                if (!seen.Add(candidate)) { candidates.RemoveAt(i); continue; }
                if (candidate is not IGridInteractionTarget target) continue;
                if (!target.TryGetInteractionGridHit(pointer, topology, out GridInteractionHit hit))
                { candidates.RemoveAt(i); continue; }
                entries.Add(new Entry(target, hit));
            }

            entries.Sort(EntryComparer.Instance);
            for (int start = 0; start < entries.Count;)
            {
                int end = start + 1;
                while (end < entries.Count && entries[start].Hit.IsSameCell(entries[end].Hit)) end++;
                int count = end - start;
                int band = Mathf.Clamp(Mathf.FloorToInt((1f - entries[start].Hit.Height01) * count), 0, count - 1);
                for (int i = start; i < end; i++)
                    if (i != start + band) candidates.Remove(entries[i].Target);
                start = end;
            }
        }
        finally { entries.Clear(); seen.Clear(); }
    }
    #endregion
}
