using System;
using System.Collections.Generic;

/// <summary>
/// 由 JSON 构建的只读 Buff 定义。null 持续时间表示永久 Buff。
/// </summary>
[Serializable]
public sealed class BuffDefinition
{
    public string Id { get; internal set; }
    public string DisplayName { get; internal set; }
    public BuffCategory Category { get; internal set; } = BuffCategory.General;
    public string Description { get; internal set; }
    public float? DurationSeconds { get; internal set; }
    public float TickIntervalSeconds { get; internal set; }
    public BuffStackMode StackMode { get; internal set; }
    public float DrinkDurationExtensionSeconds { get; internal set; }

    private BuffEffectDefinition[] allEffects = Array.Empty<BuffEffectDefinition>();
    private BuffEffectDefinition[] startEffects = Array.Empty<BuffEffectDefinition>();
    private BuffEffectDefinition[] tickEffects = Array.Empty<BuffEffectDefinition>();
    private BuffEffectDefinition[] stopEffects = Array.Empty<BuffEffectDefinition>();

    public bool IsPermanent => !DurationSeconds.HasValue;
    public IReadOnlyList<BuffEffectDefinition> Effects => allEffects;
    public IReadOnlyList<BuffEffectDefinition> StartEffects => startEffects;
    public IReadOnlyList<BuffEffectDefinition> TickEffects => tickEffects;
    public IReadOnlyList<BuffEffectDefinition> StopEffects => stopEffects;

    internal void SetEffects(
        List<BuffEffectDefinition> all,
        List<BuffEffectDefinition> start,
        List<BuffEffectDefinition> tick,
        List<BuffEffectDefinition> stop)
    {
        allEffects = all?.ToArray() ?? Array.Empty<BuffEffectDefinition>();
        startEffects = start?.ToArray() ?? Array.Empty<BuffEffectDefinition>();
        tickEffects = tick?.ToArray() ?? Array.Empty<BuffEffectDefinition>();
        stopEffects = stop?.ToArray() ?? Array.Empty<BuffEffectDefinition>();
    }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Id) ? base.ToString() : Id;
    }
}

[Serializable]
public sealed class BuffEffectDefinition
{
    public string TypeId { get; internal set; }
    public BuffEffectPhase Phase { get; internal set; }
    public string TargetId { get; internal set; }
    public string RequiredTag { get; internal set; }
    public float Value { get; internal set; }

    [NonSerialized]
    private BuffEffectHandler cachedHandler;

    public bool IsHandlerCached => cachedHandler != null;

    internal bool TryCacheHandler(BuffEffectHandler handler)
    {
        cachedHandler = handler;
        return cachedHandler != null;
    }

    internal void Execute(BuffInstance runtime)
    {
        cachedHandler?.Invoke(this, runtime);
    }
}

public delegate void BuffEffectHandler(BuffEffectDefinition effect, BuffInstance runtime);

public enum BuffEffectPhase
{
    Start = 0,
    Tick = 1,
    Stop = 2
}

public enum BuffStackMode
{
    Ignore = 0,
    ExtendDuration = 1,
    RefreshDuration = 2
}

public enum BuffCategory
{
    General = 0,
    BloodLoss = 1
}
