using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class BuildingMaterialSalvage
{
    #region 可持久化的材料回收结果
    /// <summary>磨损损毁按单件材料固定比例返料，独立于战斗破坏时的逐份随机回收。</summary>
    public static bool TryCreateProportionalMaterialSalvage(string summonerId, float fraction,
        out List<ItemData> materials, out string reason, ItemData buildingData = null)
    {
        materials = new List<ItemData>(); reason = null;
        if (!float.IsFinite(fraction) || fraction < 0f || fraction > 1f)
        { reason = "固定材料回收比例无效"; return false; }
        if (!IsBuildingSummoner(summonerId)) { reason = summonerId + " 不是建筑召唤器"; return false; }
        if (!TryGetMaterialAmounts(summonerId, buildingData, out var amounts, out reason)) return false;
        return TryCreateMaterialItems(GetProportionalMaterialAmounts(amounts, fraction), out materials, out reason);
    }

    public static Dictionary<string, int> GetProportionalMaterialAmounts(RuntimeRecipe recipe, float fraction)
    {
        if (!float.IsFinite(fraction) || fraction < 0f || fraction > 1f)
            throw new ArgumentOutOfRangeException(nameof(fraction));
        var totals = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (RuntimeRecipeIngredient ingredient in recipe.inputs.RowItems_List)
        {
            if (ingredient == null || ingredient.amount <= 0) continue;
            if (ingredient.matchMode != MatchMode.ExactItem || string.IsNullOrWhiteSpace(ingredient.ItemName))
                throw new InvalidOperationException("固定返料需要精确材料配方。");
            totals.TryGetValue(ingredient.ItemName, out float amount);
            totals[ingredient.ItemName] = amount + ingredient.amount;
        }
        return GetProportionalMaterialAmounts(totals, fraction);
    }

    // 回收先得到独立物品数据，再交给实际所属世界生成或保存。
    public static bool TryCreateRandomMaterialSalvage(string summonerId, out List<ItemData> materials,
        out string reason, float recoveryChance = DefaultRecoveryChance, ItemData buildingData = null)
    {
        materials = new List<ItemData>(); reason = null;
        if (!IsBuildingSummoner(summonerId)) { reason = summonerId + " 不是建筑召唤器"; return false; }
        if (!TryGetMaterialAmounts(summonerId, buildingData, out var amounts, out reason)) return false;
        if (!float.IsFinite(recoveryChance)) { reason = "材料回收概率无效"; return false; }
        return TryCreateMaterialItems(GetRandomMaterialAmounts(amounts, recoveryChance), out materials, out reason);
    }

    private static bool TryCreateMaterialItems(IReadOnlyDictionary<string, int> amounts,
        out List<ItemData> materials, out string reason)
    {
        materials = new List<ItemData>();
        reason = null;
        try
        {
            foreach (var amount in amounts)
            {
                if (amount.Value <= 0) continue;
                ItemData data = GameRes.ExistingInstance.CreateItemData(amount.Key);
                if (data?.Stack == null) throw new InvalidOperationException("回收材料定义缺失：" + amount.Key);
                data.Stack.Amount = amount.Value; data.Stack.CanBePickedUp = true; data.inHand = false;
                materials.Add(data);
            }
            return true;
        }
        catch (Exception exception)
        {
            materials.Clear();
            reason = exception.Message;
            return false;
        }
    }
    #endregion
}
