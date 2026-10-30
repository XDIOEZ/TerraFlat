using System;
using System.Collections.Generic;
using MemoryPack;
using UltEvents;
using UnityEngine;

/// <summary>
/// 氧气模块只维护氧气储备和缺氧伤害，外部能力主动调用供给接口。
/// </summary>
public partial class Mod_Oxygen : Module, IItemModuleDependencyBinder, IPlayerStateAuthorityModule
{
    public const string ModuleId = "氧气模块";

    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => .2f;

    #region 数据

    [System.Serializable]
    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class OxygenData
    {
        [MemoryPackOrder(0)] public float CurrentOxygen = 100f; // 当前氧气值。
        [MemoryPackOrder(1)] public float MaxOxygen = 100f; // 最大氧气值。
        [MemoryPackOrder(2)] public float HypoxiaSeconds; // 零氧后的伤害计时随实例保存，重载不能刷新喘息窗口。
    }

    public Ex_ModData_MemoryPackable modData; // 模块存档容器。
    public OxygenData Data = new(); // 氧气持久化数据。

    [Header("氧气生存")]
    [Min(0f)] public float oxygenConsumePerSecond = 10f; // 每秒基础氧气消耗。
    [Min(0f)] public float oxygenRecoverPerSecond = 25f; // 足量供给时的净恢复速度。
    [Min(0f)] public float hypoxiaDamagePerTick = 10f; // 缺氧时每次固定伤害。

    public UltEvent<float> OnOxygenChanged = new(); // 氧气变化通知，供 HUD 等表现层订阅。
    public event Action<float> OxygenValueChanged; // 运行时氧气变化事件，避免 HUD 轮询。
    public event Action<bool> BreathBlockedChanged; // 呼吸阻塞变化事件，驱动头顶氧气条显隐。

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData_MemoryPackable)value;
    }

    public float CurrentValue => Data?.CurrentOxygen ?? 0f;
    public float MaxValue => Data?.MaxOxygen ?? 0f;
    public float OxygenRatio => MaxValue > 0f ? Mathf.Clamp01(CurrentValue / MaxValue) : 0f;
    public bool IsBreathBlocked => isBreathBlocked;
    public bool IsOxygenDepleting => isBreathBlocked;
    public bool CanReceiveOxygen => FlatWorld.Networking.GameNetwork.HasStateAuthority &&
        IsRuntimeLoaded && item != null && breathingHealth != null && breathingHealth.Hp > 0f;

    #endregion

    #region 运行时状态

    private bool isBreathBlocked;
    private float suppliedOxygen;
    private Mod_DamageReceiver breathingHealth;
    private float hypoxiaClock;
    private readonly Dictionary<object, string> breathingBlockCauses = new();

    #endregion

    #region 生命周期

    public override void Awake()
    {
        base.Awake();
        if (_Data != null)
            _Data.ID = ModuleId;
    }

    protected override void OnLoad()
    {
        modData.ReadData(ref Data);
        NormalizeData();
        hypoxiaClock = float.IsFinite(Data.HypoxiaSeconds) ? Mathf.Clamp(Data.HypoxiaSeconds, 0f, 5f) : 0f;
        suppliedOxygen = 0f;
        breathingBlockCauses.Clear();
        PublishBreathBlocked(false);
        if (breathingHealth != null) breathingHealth.DeathStarted += ClearDeadBreathing;
    }

    protected override void OnSave()
    {
        Data.HypoxiaSeconds = hypoxiaClock;
        modData.WriteData(Data);
    }

    protected override void OnUnload()
    {
        if (breathingHealth != null) breathingHealth.DeathStarted -= ClearDeadBreathing;
        breathingHealth = null;
        hypoxiaClock = 0f;
        suppliedOxygen = 0f;
        breathingBlockCauses.Clear();
        PublishBreathBlocked(false);
        base.OnUnload();
    }

    #endregion

    #region 通用供氧接口
    public float GetOxygenSupplyAmount(float seconds)
        => float.IsFinite(seconds) && seconds > 0f ? (oxygenConsumePerSecond + oxygenRecoverPerSecond) * seconds : 0f;

    // 供给当场加入身体储备，不保存供氧来源或未来供给承诺。
    public float SupplyOxygen(float amount)
    {
        if (!CanReceiveOxygen || !float.IsFinite(amount) || amount <= 0f) return 0f;
        float before = CurrentValue;
        AddOxygen(amount);
        suppliedOxygen = Mathf.Min(float.MaxValue, suppliedOxygen + amount);
        return CurrentValue - before;
    }
    private void PublishBreathBlocked(bool blocked)
    {
        bool nextBlocked = blocked;
        if (isBreathBlocked == nextBlocked)
            return;

        isBreathBlocked = nextBlocked;
        BreathBlockedChanged?.Invoke(isBreathBlocked);
    }

    #endregion

    #region 缺氧原因来源

    /// <summary>环境供给方登记呼吸被阻断的原因，氧气模块只保留通用来源记录。</summary>
    public void SetBreathingBlockCause(object source, string causeId)
    {
        if (source == null) return;
        if (string.IsNullOrWhiteSpace(causeId)) { RemoveBreathingBlockCause(source); return; }
        breathingBlockCauses[source] = causeId;
    }

    public void RemoveBreathingBlockCause(object source)
    {
        if (source != null) breathingBlockCauses.Remove(source);
    }

    private string ResolveHypoxiaCauseId()
    {
        foreach (string causeId in breathingBlockCauses.Values)
            return causeId;
        return "suffocation";
    }

    #endregion

    #region 生存结算

    /// <summary>统一修改氧气值；正数恢复、负数消耗，并广播 HUD。</summary>
    public void AddOxygen(float value)
    {
        if (float.IsFinite(value)) SetCurrentOxygen(CurrentValue + value);
    }

    private void SetCurrentOxygen(float value)
        => CommitClampedOxygenValue(SurvivalNumericBatch.ClampOxygen(value, MaxValue));

    private void CommitClampedOxygenValue(float nextValue)
    {
        if (Mathf.Approximately(Data.CurrentOxygen, nextValue))
            return;

        Data.CurrentOxygen = nextValue;
        OnAction.Invoke(nextValue);
        OnOxygenChanged.Invoke(nextValue);
        OxygenValueChanged?.Invoke(nextValue);
    }

    private void NormalizeData()
    {
        Data ??= new OxygenData();
        Data.MaxOxygen = float.IsFinite(Data.MaxOxygen) ? Mathf.Max(0f, Data.MaxOxygen) : 100f;
        Data.CurrentOxygen = float.IsFinite(Data.CurrentOxygen) ? Mathf.Clamp(Data.CurrentOxygen, 0f, Data.MaxOxygen) : 0f;
    }

    #endregion

    #region 唯一呼吸结算
    public void BindModuleDependencies(ItemMods modules)
    {
        breathingHealth = modules.RequireSingleModById<Mod_DamageReceiver>(ModText.Hp);
    }
    private void ClearDeadBreathing(Mod_DamageReceiver _) { hypoxiaClock = 0f; suppliedOxygen = 0f; breathingBlockCauses.Clear(); PublishBreathBlocked(false); }
    public void ResetForNewLife()
    {
        hypoxiaClock = 0f;
        suppliedOxygen = 0f;
        breathingBlockCauses.Clear();
        PublishBreathBlocked(false);
        SetCurrentOxygen(MaxValue);
    }
    public override void ModUpdate(float deltaTime)
    {
        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority || item == null || !float.IsFinite(deltaTime) || deltaTime <= 0f) return;
        if (breathingHealth == null || breathingHealth.Hp <= 0f) { hypoxiaClock = 0f; return; }
        float before = CurrentValue;
        var consumption = new SurvivalNumericBatch.OxygenConsumption
        {
            Current = before, Maximum = MaxValue, ConsumePerSecond = oxygenConsumePerSecond, Seconds = deltaTime
        };
        SurvivalNumericBatch.Advance(ref consumption);
        if (consumption.HasFiniteChange) CommitClampedOxygenValue(consumption.Next);
        // 只有供氧不足以维持基础呼吸才显示缺氧状态，额外的恢复速度不属于呼吸需求。
        var breathing = new SurvivalNumericBatch.OxygenBreathing
        {
            Supplied = suppliedOxygen, ConsumePerSecond = oxygenConsumePerSecond, Seconds = deltaTime
        };
        SurvivalNumericBatch.Advance(ref breathing);
        PublishBreathBlocked(breathing.Blocked);
        suppliedOxygen = 0f;
        var hypoxia = new SurvivalNumericBatch.OxygenHypoxia
        {
            BeforeConsumption = before, Current = CurrentValue, ConsumePerSecond = oxygenConsumePerSecond,
            Seconds = deltaTime, DamageClock = hypoxiaClock
        };
        SurvivalNumericBatch.Advance(ref hypoxia);
        hypoxiaClock = hypoxia.DamageClock;
        // 所有缺氧情况共用同一伤害时钟，不由供给者自行扣血。
        while (hypoxiaClock >= 5f && breathingHealth != null && breathingHealth.Hp > 0f)
        {
            hypoxiaClock -= 5f;
            breathingHealth.ForceHurt(hypoxiaDamagePerTick, ResolveHypoxiaCauseId());
        }
    }
    #endregion
}
