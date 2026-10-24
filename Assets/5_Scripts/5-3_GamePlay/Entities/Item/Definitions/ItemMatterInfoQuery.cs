using UnityEngine;

/// <summary>物质状态的只读数据接口；无需打开窗口或推进环境模拟。</summary>
public static class ItemMatterInfoQuery
{
    #region 含水量查询

    /// <summary>直接查询一件物品，供 UI 之外的系统复用。</summary>
    public static bool TryGetMoisture(ItemData item, out float fraction)
    {
        fraction = 0f;
        GameRes resources = GameRes.ExistingInstance;
        return item != null && !string.IsNullOrWhiteSpace(item.IDName) && resources != null &&
            resources.TryGetItemDefinition(item.IDName, out RuntimeItemDefinition definition) &&
            TryGetMoisture(item, definition, out fraction);
    }

    /// <summary>已有物品定义时不重复查目录；结果为 0～1 的含水比例。</summary>
    public static bool TryGetMoisture(ItemData item, RuntimeItemDefinition definition, out float fraction)
    {
        fraction = 0f;
        return item != null && TryGetMoisture(item.MatterState, definition?.Matter, out fraction);
    }

    /// <summary>未开始环境模拟的物品读取定义初值，不为了展示而初始化实例状态。</summary>
    public static bool TryGetMoisture(ItemMatterState state, RuntimeItemMatterDefinition matter, out float fraction)
    {
        fraction = 0f;
        if (!HasMoistureBehavior(matter)) return false;
        float value = state?.Initialized == true ? state.Moisture : matter.InitialMoisture;
        if (!float.IsFinite(value)) return false;
        fraction = Mathf.Clamp01(value);
        return true;
    }

    private static bool HasMoistureBehavior(RuntimeItemMatterDefinition matter)
    {
        if (matter == null) return false;
        if (matter.InitialMoisture > 0f || matter.EvaporationRatePerSecond > 0f ||
            matter.WaterAbsorptionRatePerSecond > 0f) return true;
        // 只有燃烧特质的物品不显示无意义的 0% 含水量。
        for (int i = 0; i < matter.Transitions.Count; i++)
        {
            RuntimeItemMatterTransition transition = matter.Transitions[i];
            if (transition.MinMoisture.HasValue || transition.MaxMoisture.HasValue) return true;
        }
        return false;
    }

    #endregion
}
