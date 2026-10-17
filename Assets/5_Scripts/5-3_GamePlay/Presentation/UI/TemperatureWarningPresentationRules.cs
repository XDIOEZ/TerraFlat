using UnityEngine;

/// <summary>体温条与屏幕边缘共用的只读预警强度。</summary>
public readonly struct TemperatureWarningPresentationSample
{
    #region 预警采样

    public float ColdWarning { get; }
    public float HeatWarning { get; }
    public float ColdDanger { get; }
    public float HeatDanger { get; }
    public float SignedWarning => HeatWarning - ColdWarning;
    public float SignedDanger => HeatDanger - ColdDanger;

    public TemperatureWarningPresentationSample(
        float coldWarning, float heatWarning, float coldDanger, float heatDanger)
    {
        ColdWarning = coldWarning;
        HeatWarning = heatWarning;
        ColdDanger = coldDanger;
        HeatDanger = heatDanger;
    }

    #endregion
}

/// <summary>只将最终体温与真实安全范围换算为表现，不修改体温、缓冲或伤害规则。</summary>
public static class TemperatureWarningPresentationRules
{
    #region 温度预警规则

    public const float WarningRangeCelsius = 3f;
    public const float DangerRangeCelsius = 10f;
    public const float DefaultTransitionSeconds = 1f;

    public static readonly Color ColdBarColor = new Color(0.24f, 0.61f, 0.97f, 1f);
    public static readonly Color HeatBarColor = new Color(1f, 0.30f, 0.20f, 1f);

    // 体温条提前变色，屏幕边缘只在真正越过安全线后从零渐强。
    public static TemperatureWarningPresentationSample Evaluate(
        float currentTemperature, float safeMinimum, float safeMaximum)
    {
        if (!float.IsFinite(currentTemperature) || !float.IsFinite(safeMinimum) ||
            !float.IsFinite(safeMaximum) || safeMaximum < safeMinimum)
            return default;

        float warningWidth = Mathf.Min(WarningRangeCelsius, (safeMaximum - safeMinimum) * 0.25f);
        float coldWarning = warningWidth > 0f
            ? SmoothUnit((safeMinimum + warningWidth - currentTemperature) / warningWidth)
            : currentTemperature < safeMinimum ? 1f : 0f;
        float heatWarning = warningWidth > 0f
            ? SmoothUnit((currentTemperature - (safeMaximum - warningWidth)) / warningWidth)
            : currentTemperature > safeMaximum ? 1f : 0f;

        return new TemperatureWarningPresentationSample(
            coldWarning,
            heatWarning,
            SmoothUnit((safeMinimum - currentTemperature) / DangerRangeCelsius),
            SmoothUnit((currentTemperature - safeMaximum) / DangerRangeCelsius));
    }

    private static float SmoothUnit(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * (3f - 2f * t);
    }

    #endregion

    #region 颜色与时间过渡

    // 冷热共用一个有符号强度，快速切换时也不会同时结冰和冒热浪。
    public static float AdvanceScreenSeverity(
        float currentSignedSeverity,
        float targetSignedSeverity,
        float unscaledDeltaTime,
        float transitionSeconds = DefaultTransitionSeconds)
    {
        float current = float.IsFinite(currentSignedSeverity) ? Mathf.Clamp(currentSignedSeverity, -1f, 1f) : 0f;
        float target = float.IsFinite(targetSignedSeverity) ? Mathf.Clamp(targetSignedSeverity, -1f, 1f) : 0f;
        if (!float.IsFinite(unscaledDeltaTime) || unscaledDeltaTime <= 0f)
            return current;
        if (!float.IsFinite(transitionSeconds) || transitionSeconds <= 0f)
            return target;

        return Mathf.MoveTowards(current, target, unscaledDeltaTime / transitionSeconds);
    }

    // 正常色沿用正式 HUD 的主题填充色，危险色只覆盖体温这一条。
    public static Color ResolveBarColor(float signedWarning, Color normalColor)
    {
        float warning = float.IsFinite(signedWarning) ? Mathf.Clamp(signedWarning, -1f, 1f) : 0f;
        Color dangerColor = warning < 0f ? ColdBarColor : HeatBarColor;
        dangerColor.a = normalColor.a;
        return Color.LerpUnclamped(normalColor, dangerColor, Mathf.Abs(warning));
    }

    #endregion
}
