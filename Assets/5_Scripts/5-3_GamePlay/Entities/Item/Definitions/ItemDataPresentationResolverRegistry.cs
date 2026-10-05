using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 为带有状态的 ItemData 注册通用 Sprite 解析器。
/// 解析入口按 ModuleData.ID 分派；展示端只请求物品当前 Sprite，不直接依赖液体容器、作物或其他玩法模块。
/// </summary>
public delegate bool ItemDataPresentationResolver(ItemData itemData, out Sprite sprite);

public static class ItemDataPresentationResolverRegistry
{
    #region 注册表

    private static readonly Dictionary<string, ItemDataPresentationResolver> Resolvers = // 模块稳定 ID 对应它的 ItemData 表现解析器。
        new Dictionary<string, ItemDataPresentationResolver>(StringComparer.Ordinal);

    /// <summary>为模块 ID 注册唯一的 ItemData 表现解析器；重复注册会替换该模块的旧入口。</summary>
    public static void Register(string moduleId, ItemDataPresentationResolver resolver)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            throw new ArgumentException("表现解析器必须声明模块 ID。", nameof(moduleId));
        if (resolver == null)
            throw new ArgumentNullException(nameof(resolver));

        Resolvers[moduleId.Trim()] = resolver;
    }

    /// <summary>按 ItemData 中的模块 ID 查找一个已注册的状态 Sprite。</summary>
    public static bool TryResolve(ItemData itemData, out Sprite sprite)
    {
        sprite = null;
        if (itemData?.ModuleDataDic == null)
            return false;

        foreach (ModuleData moduleData in itemData.ModuleDataDic.Values)
        {
            if (moduleData == null || string.IsNullOrWhiteSpace(moduleData.ID) ||
                !Resolvers.TryGetValue(moduleData.ID, out ItemDataPresentationResolver resolver))
                continue;

            if (resolver(itemData, out sprite) && sprite != null)
                return true;
        }

        return false;
    }

    /// <summary>每次进入运行时重置注册表，再由各模块在场景加载前登记自己的解析器。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForRuntimeSession() => Resolvers.Clear();

    #endregion
}
