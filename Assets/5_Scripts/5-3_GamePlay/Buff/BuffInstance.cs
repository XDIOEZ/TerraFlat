using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class BuffManagerSaveData
{
    public List<BuffInstance> Buffs = new();
}

[MemoryPackable]
[System.Serializable]
public partial class BuffInstance
{
    private const int MaxTicksPerUpdate = 64;

    public string DefinitionId;
    public float RemainingDurationSeconds;
    public float TickElapsedSeconds;

    [MemoryPackIgnore]
    public BuffDefinition Definition { get; private set; }

    [MemoryPackIgnore]
    public Item Receiver { get; private set; }

    [MemoryPackIgnore]
    private bool started;

    [MemoryPackIgnore]
    private bool stopped;

    [MemoryPackIgnore]
    private bool startPending;

    [MemoryPackIgnore]
    public float RemainingDuration =>
        Definition == null
            ? 0f
            : Definition.IsPermanent
                ? float.PositiveInfinity
                : Mathf.Max(0f, RemainingDurationSeconds);

    [MemoryPackIgnore]
    public bool IsExpired =>
        Definition == null ||
        (!Definition.IsPermanent && RemainingDurationSeconds <= 0f);

    public bool Initialize(BuffDefinition definition, Item receiver)
    {
        if (definition == null || receiver == null)
            return false;

        Definition = definition;
        DefinitionId = definition.Id;
        RemainingDurationSeconds = definition.DurationSeconds ?? 0f;
        TickElapsedSeconds = 0f;
        Receiver = receiver;
        started = false;
        stopped = false;
        startPending = false;
        return true;
    }

    public bool Restore(Item receiver)
    {
        if (string.IsNullOrWhiteSpace(DefinitionId) || receiver == null)
            return false;

        Definition = GameRes.Instance?.GetBuffDefinition(DefinitionId);
        if (Definition == null)
        {
            Debug.LogWarning($"[BuffInstance] 找不到 Buff 定义：{DefinitionId}");
            return false;
        }

        Receiver = receiver;
        RemainingDurationSeconds = Definition.IsPermanent
            ? 0f
            : Mathf.Max(0f, RemainingDurationSeconds);
        TickElapsedSeconds = Mathf.Max(0f, TickElapsedSeconds);
        started = false;
        stopped = false;
        startPending = true;
        return true;
    }

    /// <summary>
    /// 推进 Buff。返回 true 表示已到期，应由 BuffManager 统一移除。
    /// </summary>
    public bool Tick(float deltaTime)
    {
        if (Definition == null || float.IsNaN(deltaTime) || deltaTime <= 0f)
            return IsExpired;

        EnsureStarted();
        if (IsExpired)
            return true;

        float activeDelta = Definition.IsPermanent
            ? deltaTime
            : Mathf.Min(deltaTime, RemainingDurationSeconds);

        if (!Definition.IsPermanent)
            RemainingDurationSeconds -= activeDelta;

        TickElapsedSeconds += activeDelta;
        ExecuteTicks();
        return IsExpired;
    }

    public void Start()
    {
        if (started || stopped || Definition == null)
            return;

        startPending = false;
        started = true;
        BuffEffectDispatcher.Execute(Definition.StartEffects, this);
    }

    public void Stop()
    {
        EnsureStarted();
        if (!started || stopped)
            return;

        stopped = true;
        BuffEffectDispatcher.Execute(Definition.StopEffects, this);
    }

    public bool ExtendDuration(float seconds)
    {
        if (Definition == null ||
            Definition.IsPermanent ||
            float.IsNaN(seconds) ||
            float.IsInfinity(seconds) ||
            seconds <= 0f)
        {
            return false;
        }

        RemainingDurationSeconds += seconds;
        stopped = false;
        return true;
    }

    public bool RefreshDuration()
    {
        if (Definition?.DurationSeconds is not float baseDuration)
            return false;

        RemainingDurationSeconds = Mathf.Max(RemainingDurationSeconds, baseDuration);
        stopped = false;
        return true;
    }

    /// <summary>
    /// 设置限时 Buff 的剩余持续时间。用于受控的运行时调试入口；
    /// 永久 Buff 仍由其 JSON 定义控制，不能在实例层面改写为限时 Buff。
    /// </summary>
    public bool TrySetRemainingDuration(float seconds)
    {
        if (Definition == null ||
            Definition.IsPermanent ||
            float.IsNaN(seconds) ||
            float.IsInfinity(seconds) ||
            seconds < 0f)
        {
            return false;
        }

        RemainingDurationSeconds = seconds;
        stopped = false;
        return true;
    }

    private void EnsureStarted()
    {
        if (startPending)
            Start();
    }

    private void ExecuteTicks()
    {
        if (!started || stopped || Definition.TickEffects.Count == 0)
            return;

        float interval = Definition.TickIntervalSeconds;
        if (interval <= 0f)
            return;

        int tickCount = 0;
        while (TickElapsedSeconds + Mathf.Epsilon >= interval &&
               tickCount < MaxTicksPerUpdate)
        {
            TickElapsedSeconds -= interval;
            BuffEffectDispatcher.Execute(Definition.TickEffects, this);
            tickCount++;
        }

        if (tickCount == MaxTicksPerUpdate && TickElapsedSeconds >= interval)
            TickElapsedSeconds %= interval;
    }
}
