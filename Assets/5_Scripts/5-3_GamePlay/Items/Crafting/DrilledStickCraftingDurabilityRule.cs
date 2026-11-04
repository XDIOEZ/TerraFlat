using System.Collections.Generic;
using UnityEngine;

/// <summary>钻孔木棍照常参与扣料，工具成品在正式制作时随机损失当前耐久。</summary>
public sealed class DrilledStickCraftingDurabilityRule : ICraftingOutputRule
{
    #region 钻孔木棍制作损耗
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register() => CraftingOutputRules.Register("zz.materials.drilled-stick", new DrilledStickCraftingDurabilityRule());

    public bool Prepare(Inventory input, CraftingRecipeMatch match, IReadOnlyList<ItemData> outputs,
        out string error, bool isCommit = false)
    {
        error = null;
        if (!isCommit || match == null || input?.Data?.itemSlots == null) return true;
        bool consumedDrilledStick = false;
        foreach (CraftingConsumption consumption in match.Consumptions)
        {
            if (consumption.Amount <= 0f) continue;
            if ((uint)consumption.SlotIndex >= (uint)input.Data.itemSlots.Count)
            { error = "钻孔木棍扣料计划已经失效"; return false; }
            if (input.Data.itemSlots[consumption.SlotIndex]?.itemData?.IDName == "DrilledStick")
                consumedDrilledStick = true;
        }
        if (!consumedDrilledStick) return true;

        // 在耐久品质规则之后减固定点数，不按材料根数叠加，也不影响最大耐久。
        foreach (ItemData output in outputs)
        {
            if (output?.MaxDurability <= 0f ||
                !(output?.Tags?.Contains("Tool") == true || output?.Tags?.Contains("HandDrill") == true)) continue;
            output.Durability = Mathf.Max(0f, output.Durability - Random.Range(1, 11));
            Mod_HandDrill.SaveRuntimeDurabilityFromItemData(output);
        }
        return true;
    }
    #endregion
}
