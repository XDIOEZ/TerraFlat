using System;
using UnityEngine;

/// <summary>徒手采集按有效交互次数结算，作物、独立采集物和原生实体共用同一规则。</summary>
public static class ManualHarvestWork
{
    #region 徒手采集工作量
    public const int DefaultSteps = 5;
    public const float StaminaCost = 1f;
    public const string StaminaSourceId = "flatworld.harvest.manual";

    // 每次有效采摘先支付体力，次数与产量各自独立。
    public static bool TryPayStamina(Item actor) => actor?.itemMods?.GetMod_ByID<Mod_Stamina>(ModText.Stamina)
        ?.TryConsumeStamina(StaminaSourceId, StaminaCost) ?? true;

    public static float ShakeAngle(float elapsed)
    {
        const float duration = .24f;
        if (elapsed < 0f || elapsed >= duration) return 0f;
        return Mathf.Sin(elapsed * Mathf.PI * 6f / duration) * 7f * (1f - elapsed / duration);
    }

    public static bool Advance(ref int progress, int requiredSteps)
    {
        int steps = Math.Max(1, requiredSteps);
        progress = Math.Clamp(progress, 0, steps - 1) + 1;
        if (progress < steps) return false;
        progress = 0;
        return true;
    }
    #endregion
}
