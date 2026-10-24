using System;

/// <summary>盾牌输入和窗口只负责纯状态，伤害、防御、移动与表现由各自能力消费。</summary>
public enum ShieldActionKind : byte { Idle, Pressing, Bash, Guard, Parry }

[Serializable]
public sealed class ShieldActionSettings
{
    #region 盾牌动作参数
    public float HoldThreshold = .2f;
    public float BashDuration = .3f;
    public float BashDefense = 5f;
    public float GuardDefense = 10f;
    public float GuardSpeedMultiplier = .5f;
    public float ParryDuration = .2f;
    public float ParryDefense = 10f;
    public float ParryCooldown = .6f;

    public ShieldActionSettings ValidatedCopy() => new()
    {
        HoldThreshold = Positive(HoldThreshold, .2f),
        BashDuration = Positive(BashDuration, .3f),
        BashDefense = NonNegative(BashDefense, 5f),
        GuardDefense = NonNegative(GuardDefense, 10f),
        GuardSpeedMultiplier = Math.Clamp(Positive(GuardSpeedMultiplier, .5f), .001f, 1f),
        ParryDuration = Positive(ParryDuration, .2f),
        ParryDefense = NonNegative(ParryDefense, 10f),
        ParryCooldown = NonNegative(ParryCooldown, .6f)
    };
    private static float Positive(float value, float fallback) => Finite(value) && value > 0f ? value : fallback;
    private static float NonNegative(float value, float fallback) => Finite(value) && value >= 0f ? value : fallback;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    #endregion
}

public sealed class ShieldActionClock
{
    #region 单击、长按与互斥防御窗口
    public ShieldActionKind Kind { get; private set; }
    public float Elapsed { get; private set; }
    public float ParryCooldownRemaining { get; private set; }
    public ShieldActionSettings Settings { get; private set; } = new();
    public bool IsActive => Kind != ShieldActionKind.Idle;
    public float Duration => Kind switch
    {
        ShieldActionKind.Pressing => Settings.HoldThreshold,
        ShieldActionKind.Bash => Settings.BashDuration,
        ShieldActionKind.Parry => Settings.ParryDuration,
        _ => 0f
    };
    public float Defense => Kind switch
    {
        ShieldActionKind.Bash => Settings.BashDefense,
        ShieldActionKind.Guard => Settings.GuardDefense,
        ShieldActionKind.Parry => Settings.ParryDefense,
        _ => 0f
    };
    public float SpeedMultiplier => Kind == ShieldActionKind.Guard ? Settings.GuardSpeedMultiplier : 1f;

    public bool TryPress(ShieldActionSettings settings)
    {
        if (Kind != ShieldActionKind.Idle || settings == null) return false;
        Settings = settings.ValidatedCopy();
        Transition(ShieldActionKind.Pressing);
        return true;
    }

    public bool Release()
    {
        if (Kind == ShieldActionKind.Pressing) { Transition(ShieldActionKind.Bash); return true; }
        if (Kind == ShieldActionKind.Guard) Cancel();
        return false;
    }

    public bool TryBash(ShieldActionSettings settings)
    {
        if (Kind != ShieldActionKind.Idle || settings == null) return false;
        Settings = settings.ValidatedCopy();
        Transition(ShieldActionKind.Bash);
        return true;
    }

    public bool TryParry(ShieldActionSettings settings)
    {
        if (Kind != ShieldActionKind.Idle || ParryCooldownRemaining > 0f || settings == null) return false;
        Settings = settings.ValidatedCopy();
        ParryCooldownRemaining = Math.Max(Settings.ParryDuration, Settings.ParryCooldown);
        Transition(ShieldActionKind.Parry);
        return true;
    }

    public void Advance(float deltaTime)
    {
        if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f) return;
        ParryCooldownRemaining = Math.Max(0f, ParryCooldownRemaining - deltaTime);
        if (Kind == ShieldActionKind.Idle) return;
        Elapsed += deltaTime;
        if (Kind == ShieldActionKind.Pressing && Elapsed >= Settings.HoldThreshold)
            Transition(ShieldActionKind.Guard);
        else if ((Kind == ShieldActionKind.Bash || Kind == ShieldActionKind.Parry) && Elapsed >= Duration)
            Cancel();
    }

    public void Cancel() => Transition(ShieldActionKind.Idle);
    private void Transition(ShieldActionKind kind) { Kind = kind; Elapsed = 0f; }
    #endregion
}
