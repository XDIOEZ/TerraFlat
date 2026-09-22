using System.Collections.Generic;
using UnityEngine;

/// <summary>在库存事务预检前生成产物的派生状态；规则只修改新产物，不消费输入。</summary>
public interface ICraftingOutputRule
{
    bool Prepare(Inventory input, CraftingRecipeMatch match, IReadOnlyList<ItemData> outputs, out string error);
}

/// <summary>制作产物状态扩展点；食品等业务自行注册，通用制作服务不依赖具体玩法。</summary>
public static class CraftingOutputRules
{
    private static readonly SortedDictionary<string, ICraftingOutputRule> Rules = new(); // 稳定执行顺序。
    /// <summary>同一身份重复初始化时替换规则。</summary>
    public static void Register(string id, ICraftingOutputRule rule) => Rules[id] = rule;
    /// <summary>预览与提交使用同一状态推导，失败时输入保持原样。</summary>
    public static bool Prepare(Inventory input, CraftingRecipeMatch match, IReadOnlyList<ItemData> outputs, out string error)
    {
        error = null;
        foreach (ICraftingOutputRule rule in Rules.Values)
            if (!rule.Prepare(input, match, outputs, out error)) return false;
        return true;
    }
}

/// <summary>
/// 保存制作材料赋予的实例耐久品质。倍率跟随 ItemData 持久化，并始终以当前物品定义作为基础值，
/// 避免读档或内容更新后重复叠乘；可受伤建筑的生命值也使用同一倍率。
/// </summary>
public static class CraftedDurabilityQuality
{
    public const float DefaultMultiplier = 1f;

    /// <summary>倍率必须为大于零的有限数。</summary>
    public static bool IsValidMultiplier(float multiplier)
    {
        return !float.IsNaN(multiplier) && !float.IsInfinity(multiplier) && multiplier > 0f;
    }

    /// <summary>写入新制作产物，并保留当前耐久百分比。</summary>
    public static void ApplyToCraftedOutput(ItemData itemData, float multiplier)
    {
        if (itemData == null || !IsValidMultiplier(multiplier))
            return;

        float ratio = GetDurabilityRatio(itemData);
        itemData.CraftedDurabilityMultiplier = multiplier;
        itemData.MaxDurability = Mathf.Max(0f, itemData.MaxDurability) * multiplier;
        if (itemData.MaxDurability > 0f)
            itemData.Durability = itemData.MaxDurability * ratio;
    }

    /// <summary>读档时从持久化实例恢复品质，基础耐久仍由当前定义提供。</summary>
    public static void RestorePersistedMultiplier(ItemData currentData, ItemData persistedData)
    {
        if (currentData == null || persistedData == null)
            return;

        float multiplier = NormalizeMultiplier(persistedData.CraftedDurabilityMultiplier);
        currentData.CraftedDurabilityMultiplier = multiplier;
        currentData.MaxDurability = Mathf.Max(0f, currentData.MaxDurability) * multiplier;
    }

    /// <summary>便携建筑在手持载体与落地本体之间迁移制作品质。</summary>
    public static void CopyInstanceQuality(ItemData source, ItemData target)
    {
        if (source == null || target == null)
            return;

        float sourceMultiplier = NormalizeMultiplier(source.CraftedDurabilityMultiplier);
        float targetMultiplier = NormalizeMultiplier(target.CraftedDurabilityMultiplier);
        if (Mathf.Approximately(sourceMultiplier, DefaultMultiplier) &&
            Mathf.Approximately(targetMultiplier, DefaultMultiplier))
            return;

        float ratio = GetDurabilityRatio(source);
        target.CraftedDurabilityMultiplier = sourceMultiplier;
        target.MaxDurability = ResolveDefinitionBaseDurability(target) * sourceMultiplier;
        if (target.MaxDurability > 0f)
            target.Durability = target.MaxDurability * ratio;
    }

    /// <summary>模块加载完成后把同一品质倍率应用到实际生命值；重复 Load 不会叠乘。</summary>
    public static void ApplyRuntimeHealth(Item item)
    {
        if (item?.itemData == null || GameRes.Instance == null)
            return;

        float multiplier = NormalizeMultiplier(item.itemData.CraftedDurabilityMultiplier);
        item.itemData.CraftedDurabilityMultiplier = multiplier;
        if (Mathf.Approximately(multiplier, DefaultMultiplier) ||
            !GameRes.Instance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            definition?.Health?.HasHp != true)
            return;

        DamageReceiver receiver = item.itemMods?.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (receiver == null)
            return;

        float ratio = receiver.MaxHp > 0f ? Mathf.Clamp01(receiver.Hp / receiver.MaxHp) : 1f;
        float targetMaxHp = Mathf.Max(0.01f, definition.Health.MaxHp) * multiplier;
        receiver.MaxHp = targetMaxHp;
        receiver.Hp = targetMaxHp * ratio;
    }

    private static float NormalizeMultiplier(float multiplier)
    {
        return IsValidMultiplier(multiplier) ? multiplier : DefaultMultiplier;
    }

    private static float GetDurabilityRatio(ItemData itemData)
    {
        return itemData?.MaxDurability > 0f
            ? Mathf.Clamp01(itemData.Durability / itemData.MaxDurability)
            : 1f;
    }

    private static float ResolveDefinitionBaseDurability(ItemData itemData)
    {
        if (itemData != null &&
            GameRes.Instance != null &&
            !string.IsNullOrWhiteSpace(itemData.IDName) &&
            GameRes.Instance.TryGetItemDefinition(itemData.IDName, out RuntimeItemDefinition definition))
        {
            return Mathf.Max(0f, definition.CreateItemData()?.MaxDurability ?? 0f);
        }

        float multiplier = NormalizeMultiplier(itemData?.CraftedDurabilityMultiplier ?? DefaultMultiplier);
        return Mathf.Max(0f, (itemData?.MaxDurability ?? 0f) / multiplier);
    }
}
