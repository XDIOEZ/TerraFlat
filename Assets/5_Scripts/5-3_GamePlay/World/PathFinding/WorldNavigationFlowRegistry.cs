using System.Collections.Generic;
using FlatWorld.Navigation;
using UnityEngine;

#region 导航观察契约

/// <summary>导航后端提供真实玩家目标；观察者只能借用，不得为显示创建另一份寻路目标。</summary>
public interface IWorldNavigationFlowSource
{
    /// <summary>取得本后端当前玩家的共享流场及目标句柄；不存在有效目标时返回 false。</summary>
    bool TryGetPlayerFlow(Player player, out FlowNavigationCache cache, out FlowGoalHandle goal);
}

/// <summary>
/// 按后端生命周期注册的导航观察入口，供 GM 等表现读取怪物实际使用的玩家流场。
/// 只登记少量世界后端，不扫描怪物或场景；MOD 后端可实现同一契约接入。
/// </summary>
public static class WorldNavigationFlowRegistry
{
    private static readonly List<IWorldNavigationFlowSource> Sources = new(); // 已运行的世界导航来源。

    /// <summary>后端准备好真实玩家目标后注册。</summary>
    public static void Register(IWorldNavigationFlowSource source)
    {
        if (source != null && !Sources.Contains(source))
            Sources.Add(source);
    }

    /// <summary>后端释放目标和 Native 数据前注销。</summary>
    public static void Unregister(IWorldNavigationFlowSource source) => Sources.Remove(source);

    /// <summary>按本地玩家身份取得实际目标，不创建网格、加载区块或修改导航。</summary>
    public static bool TryGetPlayerFlow(Player player, out FlowNavigationCache cache, out FlowGoalHandle goal)
    {
        cache = null;
        goal = default;
        if (player == null || !player.gameObject.activeInHierarchy)
            return false;

        for (int i = 0; i < Sources.Count; i++)
            if (Sources[i].TryGetPlayerFlow(player, out cache, out goal))
                return true;

        cache = null;
        goal = default;
        return false;
    }

    /// <summary>关闭域重载时也不保留上一次 Play 的导航来源。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Sources.Clear();
}

#endregion
