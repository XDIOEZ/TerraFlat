using UnityEngine;

/// <summary>独立的闪避来源只报告自己的概率，命中系统负责逐项乘算。</summary>
public interface ICreatureDodgeChanceSource
{
    float DodgeChance { get; }
}

/// <summary>生物逻辑命中：命中等级和体型等级各占10%每级，其他闪避分别乘算。</summary>
public static class CreatureHitChanceRules
{
    public const int DefaultAccuracyLevel = 7;
    public const int DefaultSizeLevel = 5;
    public const float MaximumExtraDodgeChance = 0.95f;

    public static float AccuracyFactor(int level) => Mathf.Clamp(level, 0, 10) * 0.1f;
    public static float SizeFactor(int level) => Mathf.Clamp(level, 0, 10) * 0.1f;

    public static float Calculate(int accuracyLevel, int sizeLevel, float weaponMultiplier = 1f, float stateMultiplier = 1f)
    {
        return Mathf.Clamp01(AccuracyFactor(accuracyLevel) * SizeFactor(sizeLevel) *
                             Mathf.Max(0f, weaponMultiplier) * Mathf.Clamp01(stateMultiplier));
    }

    public static float ApplyDodge(float hitChance, float dodgeChance)
    {
        // 单项额外闪避不能达到100%，多个独立来源依次相乘，不相加。
        if (float.IsNaN(dodgeChance) || float.IsInfinity(dodgeChance))
            dodgeChance = 0f;
        return Mathf.Clamp01(hitChance) * (1f - Mathf.Clamp(dodgeChance, 0f, MaximumExtraDodgeChance));
    }
}
