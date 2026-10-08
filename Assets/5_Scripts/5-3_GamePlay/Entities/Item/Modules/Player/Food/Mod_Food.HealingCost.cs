using UnityEngine;

public partial class Mod_Food : IHealingCostPolicy
{
    #region 营养治疗成本

    private const float HealingProteinCostPerHp = 1f;

    public float LimitHealingAmount(float requestedAmount, Item healer)
    {
        if (!IsRuntimeLoaded || !GameDifficultyService.IsPlayer(item)) return requestedAmount;
        Nutrition nutrition = Data?.nutrition;
        float protein = nutrition != null && float.IsFinite(nutrition.Protein)
            ? Mathf.Max(0f, nutrition.Protein) : 0f;
        return Mathf.Min(requestedAmount, protein / HealingProteinCostPerHp);
    }

    public void PayHealingCost(float actualAmount, Item healer)
    {
        if (!IsRuntimeLoaded || !GameDifficultyService.IsPlayer(item) || actualAmount <= 0f) return;
        Nutrition nutrition = Data?.nutrition;
        if (nutrition == null) return;
        // 玩家各类治疗共用营养成本，部位耐久恢复继续使用自己的独立接口。
        nutrition.Protein = Mathf.Max(0f, nutrition.Protein - actualAmount * HealingProteinCostPerHp);
        NotifyStateChanged();
    }

    #endregion
}
