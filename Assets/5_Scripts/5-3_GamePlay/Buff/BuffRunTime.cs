using MemoryPack;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class BuffRunTime
{
    private const int MaxTicksPerUpdate = 64;

    public string buff_IDName;

    // 兼容旧存档：该值仍表示“朝到期点推进的时间”。
    // 延长 Buff 时允许它变为负数，无需增加破坏旧 MemoryPack 布局的新字段。
    public float buff_CurrentDuration;
    public float buff_CurrentStack = 1f;

    public int senderGuid;
    public int receiverGuid;

    [MemoryPackIgnore]
    public Buff_Data buff;

    [MemoryPackIgnore]
    public Item buff_Sender;

    [MemoryPackIgnore]
    public Item buff_Receiver;

    [MemoryPackIgnore]
    private float tickAccumulator;

    [MemoryPackIgnore]
    private bool hasStarted;

    [MemoryPackIgnore]
    private bool hasStopped;

    [MemoryPackIgnore]
    private bool restorePending;

    [MemoryPackIgnore]
    public float RemainingDuration =>
        buff == null
            ? 0f
            : buff.buff_StackType == BuffStackType.Keep
                ? float.PositiveInfinity
                : Mathf.Max(0f, buff.buff_Duration - buff_CurrentDuration);

    [MemoryPackIgnore]
    public bool IsExpired =>
        buff == null ||
        (buff.buff_StackType != BuffStackType.Keep &&
         buff_CurrentDuration >= Mathf.Max(0f, buff.buff_Duration));

    public bool SetBuffData(Item sender, Item receiver)
    {
        if (buff == null)
        {
            Buff_Data definition = GameRes.Instance?.GetBuffData(buff_IDName);
            if (definition == null)
            {
                Debug.LogWarning($"[BuffRunTime] 找不到 Buff 定义：{buff_IDName}");
                return false;
            }

            // Action 不再缓存接收者组件，因此定义可以作为只读资源安全复用。
            buff = definition;
        }

        if (sender != null)
        {
            buff_Sender = sender;
            if (sender.itemData != null)
                senderGuid = sender.itemData.Guid;
        }

        if (receiver != null)
        {
            buff_Receiver = receiver;
            if (receiver.itemData != null)
                receiverGuid = receiver.itemData.Guid;
        }

        // Tick 相位不写入旧版存档结构；加载后从一个完整间隔重新计时，
        // 避免根据已延长过的负 elapsed 重建时产生额外伤害。
        tickAccumulator = 0f;
        hasStarted = false;
        hasStopped = false;
        restorePending = false;
        return true;
    }

    /// <summary>
    /// 标记为读档恢复。真正的 Start 重放延迟到首次 Tick，确保其他模块已完成 Load。
    /// </summary>
    public void PrepareRestore()
    {
        hasStarted = false;
        hasStopped = false;
        restorePending = true;
    }

    /// <summary>
    /// 推进 Buff。返回 true 表示已到期，应由 BuffManager 统一移除。
    /// </summary>
    public bool Tick(float deltaTime)
    {
        if (buff == null || float.IsNaN(deltaTime) || deltaTime <= 0f)
            return IsExpired;

        EnsureRestored();
        if (IsExpired)
            return true;

        float activeDelta = deltaTime;
        if (buff.buff_StackType != BuffStackType.Keep)
        {
            float remainingBeforeTick = buff.buff_Duration - buff_CurrentDuration;
            activeDelta = Mathf.Min(deltaTime, Mathf.Max(0f, remainingBeforeTick));
        }

        buff_CurrentDuration += activeDelta;
        tickAccumulator += activeDelta;

        OnBuff_Update();
        return IsExpired;
    }

    public void OnBuff_Start()
    {
        if (hasStarted || hasStopped || buff == null)
            return;

        restorePending = false;
        hasStarted = true;
        buff.buff_Behavior_Start?.Apply(this);
    }

    public void OnBuff_Update()
    {
        if (!hasStarted || hasStopped || buff?.buff_Behavior_Update == null)
            return;

        float interval = buff.buff_Interval;
        if (interval <= 0f)
            return;

        int tickCount = 0;
        while (tickAccumulator + Mathf.Epsilon >= interval &&
               tickCount < MaxTicksPerUpdate)
        {
            tickAccumulator -= interval;
            buff.buff_Behavior_Update.Apply(this);
            tickCount++;
        }

        if (tickCount == MaxTicksPerUpdate &&
            tickAccumulator >= interval)
        {
            tickAccumulator %= interval;
        }
    }

    public void OnBuff_Stop()
    {
        EnsureRestored();
        if (!hasStarted || hasStopped)
            return;

        hasStopped = true;
        buff?.buff_Behavior_Stop?.Apply(this);
    }

    /// <summary>
    /// 增加剩余时间。返回实际增加的秒数。
    /// </summary>
    public float ExtendDuration(float seconds)
    {
        if (buff == null ||
            buff.buff_StackType == BuffStackType.Keep ||
            float.IsNaN(seconds) ||
            seconds <= 0f)
            return 0f;

        // 允许推进时间变为负数，确保刚获得 Buff 时也能完整增加 5/10/20 秒。
        // 周期计时独立累积，饮水只延长到期点，不改变下一次伤害的节拍。
        buff_CurrentDuration -= seconds;
        hasStopped = false;
        return seconds;
    }

    public void RefreshDuration()
    {
        // 刷新只补回基础时长；已通过饮水获得的额外时长（负 elapsed）不得被吞掉。
        buff_CurrentDuration = Mathf.Min(0f, buff_CurrentDuration);
        hasStopped = false;
    }

    public bool TryAddStack()
    {
        if (buff == null)
            return false;

        float maxStack = Mathf.Max(1, buff.buff_MaxStack);
        if (buff_CurrentStack >= maxStack)
        {
            RefreshDuration();
            return false;
        }

        buff_CurrentStack += 1f;
        return true;
    }

    private void EnsureRestored()
    {
        if (!restorePending || buff == null)
            return;

        restorePending = false;
        if (buff.buff_LoadBehavior == BuffLoadBehavior.ReapplyStart &&
            buff.buff_Behavior_Start != null)
        {
            OnBuff_Start();
            return;
        }

        // 对已写入模块存档的临时修正，不重复 Start，但允许未来 Stop 正确撤销。
        hasStarted = true;
    }

}
