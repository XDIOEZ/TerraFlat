using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>建筑/MOD 显式声明挡液体的格子；不读取 Collider，不把平台或普通导航占地自动视为水坝。</summary>
public static class WorldLiquidFlowObstacles
{
    #region 显式阻挡登记
    private static readonly Dictionary<object, HashSet<Vector2Int>> owners = new();
    private static readonly Dictionary<Vector2Int, int> counts = new();
    public static event Action<Vector2Int> Changed;

    /// <summary>安装/关闸时登记；同一 owner 再登记会替换旧范围。</summary>
    public static void Register(object owner, IEnumerable<Vector2Int> cells)
    {
        if (owner == null || cells == null) throw new ArgumentNullException();
        var next = new HashSet<Vector2Int>();
        foreach (Vector2Int cell in cells) next.Add(WorldTopologyRuntime.NormalizeCell(cell));
        Unregister(owner);
        owners.Add(owner, next);
        foreach (Vector2Int cell in next)
        {
            counts.TryGetValue(cell, out int count);
            counts[cell] = count + 1;
            Changed?.Invoke(cell);
        }
    }

    /// <summary>拆除/开闸/Unload 时归还登记；重叠阻挡按引用计数保留。</summary>
    public static void Unregister(object owner)
    {
        if (owner == null || !owners.TryGetValue(owner, out HashSet<Vector2Int> cells)) return;
        owners.Remove(owner);
        foreach (Vector2Int cell in cells)
        {
            if (counts[cell] <= 1) counts.Remove(cell); else counts[cell]--;
            Changed?.Invoke(cell);
        }
    }

    /// <summary>只查询已登记的规范世界格。</summary>
    public static bool IsBlocked(Vector2Int cell) => counts.ContainsKey(WorldTopologyRuntime.NormalizeCell(cell));

    /// <summary>换世界时清理临时登记，建筑恢复后重新注册。</summary>
    public static void ClearWorld() { owners.Clear(); counts.Clear(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() { ClearWorld(); Changed = null; }
    #endregion
}
