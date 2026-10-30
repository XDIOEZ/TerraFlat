using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>建筑/MOD 显式声明挡液体的格子；不读取 Collider，不把平台或普通导航占地自动视为水坝。</summary>
public static class WorldLiquidFlowObstacles
{
    #region 显式阻挡登记
    private sealed class WorldState
    {
        public readonly Dictionary<object, HashSet<Vector2Int>> Owners = new();
        public readonly Dictionary<Vector2Int, int> Counts = new();
    }
    private static readonly Dictionary<string, WorldState> states = new(StringComparer.Ordinal);
    private static readonly Dictionary<object, string> ownerWorlds = new();
    private static WorldState State
    {
        get
        {
            string key = GameplayWorldRegistry.ExecutionWorldKey;
            if (!states.TryGetValue(key, out WorldState state)) states.Add(key, state = new WorldState());
            return state;
        }
    }
    private static Dictionary<object, HashSet<Vector2Int>> owners => State.Owners;
    private static Dictionary<Vector2Int, int> counts => State.Counts;
    public static event Action<Vector2Int> Changed;

    /// <summary>安装/关闸时登记；同一 owner 再登记会替换旧范围。</summary>
    public static void Register(object owner, IEnumerable<Vector2Int> cells)
    {
        if (owner == null || cells == null) throw new ArgumentNullException();
        var next = new HashSet<Vector2Int>();
        foreach (Vector2Int cell in cells) next.Add(WorldTopologyRuntime.NormalizeCell(cell));
        Unregister(owner);
        owners.Add(owner, next);
        ownerWorlds[owner] = GameplayWorldRegistry.ExecutionWorldKey;
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
        if (owner == null || !ownerWorlds.TryGetValue(owner, out string key)) return;
        using var scope = key != GameplayWorldRegistry.ExecutionWorldKey && GameplayWorldRegistry.TryGet(key, out _)
            ? GameplayWorldRegistry.UseWorld(key) : default;
        if (!states.TryGetValue(key, out WorldState state) || !state.Owners.Remove(owner, out HashSet<Vector2Int> cells)) return;
        ownerWorlds.Remove(owner);
        foreach (Vector2Int cell in cells)
        {
            if (state.Counts[cell] <= 1) state.Counts.Remove(cell); else state.Counts[cell]--;
            Changed?.Invoke(cell);
        }
    }

    /// <summary>只查询已登记的规范世界格。</summary>
    public static bool IsBlocked(Vector2Int cell) => counts.ContainsKey(WorldTopologyRuntime.NormalizeCell(cell));

    /// <summary>换世界时清理临时登记，建筑恢复后重新注册。</summary>
    public static void ClearWorld()
    {
        string key = GameplayWorldRegistry.ExecutionWorldKey;
        if (states.Remove(key, out WorldState state)) foreach (object owner in state.Owners.Keys) ownerWorlds.Remove(owner);
    }
    public static void ReleaseAll() { states.Clear(); ownerWorlds.Clear(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() { ReleaseAll(); Changed = null; }
    #endregion
}
