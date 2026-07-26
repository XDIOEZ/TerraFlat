using System;
using UnityEngine;

/// <summary>
/// 血液流逝的统一周期行为：真实伤害绕过护甲，同时直接损失角色水分。
/// </summary>
[Serializable]
public class BuffAction_BloodLossTick : BuffAction
{
    [Min(0f)]
    [Tooltip("每次周期造成的真实伤害")]
    public float damagePerTick = 1f;

    [Min(0f)]
    [Tooltip("每次周期额外损失的水分")]
    public float waterLossPerTick = 2f;

    public override void Apply(BuffRunTime data)
    {
        Item receiver = data?.buff_Receiver;
        if (receiver == null)
            return;

        if (damagePerTick > 0f)
        {
            DamageReceiver damageReceiver =
                receiver.itemMods.GetMod_ByID(ModText.Hp) as DamageReceiver;
            damageReceiver?.ForceHurt(damagePerTick);
        }

        if (waterLossPerTick <= 0f)
            return;

        Mod_Food food = receiver.itemMods.GetMod_ByID(ModText.Food) as Mod_Food;
        Nutrition nutrition = food?.Data?.nutrition;
        if (nutrition == null)
            return;

        float oldWater = nutrition.Water;
        nutrition.Water = Mathf.Max(0f, oldWater - waterLossPerTick);
        if (!Mathf.Approximately(oldWater, nutrition.Water))
            food.DataUpdate?.Invoke();
    }
}
