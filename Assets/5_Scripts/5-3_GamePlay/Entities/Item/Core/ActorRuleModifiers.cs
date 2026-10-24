using UnityEngine;

/// <summary>根据角色身份读取已发布的业务参数，不识别外部参数设置者。</summary>
public static class ActorRuleModifiers
{
    #region 角色数值修正

    public static float ResolveEnvironmentalDamageMultiplier(Item receiver)
        => ActorIdentity.IsPlayer(receiver)
            ? Mathf.Max(0f, GameRuleConfiguration.Current.PlayerSurvival.EnvironmentalDamageMultiplier)
            : 1f;

    public static float ResolveHealingMultiplier(Item receiver)
        => ActorIdentity.IsPlayer(receiver)
            ? Mathf.Max(0f, GameRuleConfiguration.Current.PlayerSurvival.HealingMultiplier)
            : 1f;

    public static float ResolveStaminaDeltaMultiplier(Item owner, float delta)
    {
        if (!ActorIdentity.IsPlayer(owner))
            return 1f;

        var survival = GameRuleConfiguration.Current.PlayerSurvival;
        // 消耗与恢复使用各自的最终参数，零变化沿用恢复方向。
        return Mathf.Max(0f, delta < 0f
            ? survival.StaminaConsumptionMultiplier : survival.StaminaRecoveryMultiplier);
    }

    #endregion
}
