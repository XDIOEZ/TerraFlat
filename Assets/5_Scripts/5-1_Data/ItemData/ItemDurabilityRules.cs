using System;

/// <summary>基础耐久只描述当前一件；其余堆叠件始终完整，不依赖攻击或具体模块。</summary>
public static class ItemDurabilityRules
{
    #region 单件磨损与损毁
    public static bool IsEnabled(ItemData data) => data != null &&
        float.IsFinite(data.MaxDurability) && data.MaxDurability > 0f;

    public static bool CanConsume(ItemData data, float wear) => IsEnabled(data) &&
        float.IsFinite(wear) && wear > 0f && float.IsFinite(data.Durability) && data.Durability > 0f &&
        data.Stack != null && float.IsFinite(data.Stack.Amount) && data.Stack.Amount >= 1f;

    /// <summary>一次消耗最多损毁当前一件，多余磨损不穿透到下一件。</summary>
    public static bool TryConsume(ItemData data, float wear, out bool brokeUnit)
    {
        brokeUnit = false;
        if (!CanConsume(data, wear)) return false;
        float remaining = Math.Max(0f, Math.Min(data.Durability, data.MaxDurability) - wear);
        brokeUnit = remaining <= 0f;
        if (!brokeUnit) { data.Durability = remaining; return true; }
        float count = Math.Max(0f, data.Stack.Amount - 1f);
        data.Durability = count >= 1f ? data.MaxDurability : 0f;
        data.Stack.Amount = count;
        return true;
    }
    #endregion

    #region 堆叠磨损守恒
    public static bool HasWornUnit(ItemData data) => IsEnabled(data) &&
        data.Stack?.Amount > 0f && data.Durability < data.MaxDurability;

    /// <summary>一个槽只保存一件的磨损，两份受损件或不同耐久上限不能挤进同一槽。</summary>
    public static bool CanMerge(ItemData left, ItemData right, bool rightTopAlreadyTransferred = false)
    {
        if (left == null || right == null) return false;
        return left.MaxDurability == right.MaxDurability &&
            (rightTopAlreadyTransferred || !(HasWornUnit(left) && HasWornUnit(right)));
    }

    /// <summary>转移先带走当前件，合入完整堆时保留它的磨损。</summary>
    public static void CopyTopWear(ItemData source, ItemData destination)
    {
        if (HasWornUnit(source) && IsEnabled(destination))
            destination.Durability = Math.Min(destination.Durability, source.Durability);
    }

    /// <summary>当前件已经成功移出或消费后，剩余物品换成下一件满耐久。</summary>
    public static void AdvanceAfterRemoval(ItemData source)
    {
        if (IsEnabled(source) && source.Stack?.Amount > 0f)
            source.Durability = source.MaxDurability;
    }
    #endregion
}
