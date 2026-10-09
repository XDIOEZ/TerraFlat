using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class BuildingMaterialSalvage
{
    #region 可持久化的材料回收结果
    // 回收规则共用正式配方，未加载星球先得到数据再交给所属世界保存。
    public static bool TryCreateRandomMaterialSalvage(string summonerId, out List<ItemData> materials,
        out string reason, float recoveryChance = DefaultRecoveryChance)
    {
        materials = new List<ItemData>(); reason = null;
        if (!IsBuildingSummoner(summonerId)) { reason = summonerId + " 不是建筑召唤器"; return false; }
        if (!TryResolveSalvageRecipe(summonerId, out RuntimeRecipe recipe, out reason)) return false;
        var amounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (RuntimeRecipeIngredient ingredient in recipe.inputs.RowItems_List)
        {
            if (ingredient == null || ingredient.amount <= 0) continue;
            amounts.TryGetValue(ingredient.ItemName, out int old);
            amounts[ingredient.ItemName] = checked(old + ingredient.amount);
        }
        foreach (var amount in amounts)
        {
            int recovered = 0;
            for (int i = 0; i < amount.Value; i++) if (UnityEngine.Random.value < Mathf.Clamp01(recoveryChance)) recovered++;
            if (recovered <= 0) continue;
            ItemData data = GameRes.ExistingInstance.CreateItemData(amount.Key);
            if (data == null) throw new InvalidOperationException("回收材料定义缺失：" + amount.Key);
            data.Stack.Amount = recovered; data.Stack.CanBePickedUp = true; data.inHand = false;
            materials.Add(data);
        }
        return true;
    }
    #endregion
}
