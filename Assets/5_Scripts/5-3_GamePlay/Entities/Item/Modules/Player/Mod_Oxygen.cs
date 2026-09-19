using System;
using MemoryPack;
using UltEvents;
using UnityEngine;

/// <summary>
/// 玩家氧气状态模块。
/// 水深、漂浮、下沉、减速与何时开始缺氧由 TileEffectReceiver 的通用水中生存状态统一判定；
/// 本模块只保存玩家氧气、提供水中消耗参数并向 HUD 广播变化。
/// </summary>
public partial class Mod_Oxygen : Module
{
    public const string ModuleId = "氧气模块";

    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    #region 数据

    [System.Serializable]
    [MemoryPackable]
    public partial class OxygenData
    {
        public float CurrentOxygen = 100f; // 当前氧气值。
        public float MaxOxygen = 100f; // 最大氧气值。
    }

    public Ex_ModData_MemoryPackable modData; // 模块存档容器。
    public OxygenData Data = new(); // 氧气持久化数据。

    [Header("水中生存")]
    [Min(0f)] public float staminaConsumePerSecond = 5f; // 漂浮时每秒基础体力消耗。
    [Min(0f)] public float oxygenConsumePerSecond = 10f; // 体力耗尽后每秒氧气消耗。
    [Min(0f)] public float oxygenRecoverPerSecond = 25f; // 可呼吸时每秒氧气恢复。
    [Min(0f)] public float drowningDamagePerTick = 10f; // 缺氧时每次固定伤害。

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
    public bool IsInWater => isInWater;
    public bool IsBreathBlocked => isBreathBlocked;
    public bool IsOxygenDepleting => isInWater && isBreathBlocked;
    public bool IsDrowning => isInWater && isBreathBlocked && CurrentValue <= 0f;

    #endregion

    #region 运行时状态

    private bool isInWater; // 当前是否真实处于水体。
    private bool isBreathBlocked; // 当前有效淹没高度是否超过安全呼吸线。

    #endregion

    #region 生命周期

    public override void Awake()
    {
        base.Awake();
        if (_Data != null)
            _Data.ID = ModuleId;
    }

    public override void Load()
    {
        modData.ReadData(ref Data);
        NormalizeData();
        ResetWaterExposureState();
    }

    public override void Save()
    {
        modData.WriteData(Data);
    }

    public override void Unload()
    {
        ResetWaterExposureState();
        base.Unload();
    }

    #endregion

    #region 水体状态

    /// <summary>同步真实入水状态；邻接水格只提供边缘交互时不会进入本状态。</summary>
    public void SetWaterExposure(bool inWater)
    {
        isInWater = inWater;
        if (!inWater)
            SetBreathBlocked(false);
    }

    /// <summary>同步当前是否已经被水淹过安全呼吸线。</summary>
    public void SetBreathBlocked(bool blocked)
    {
        bool nextBlocked = isInWater && blocked;
        if (isBreathBlocked == nextBlocked)
            return;

        isBreathBlocked = nextBlocked;
        BreathBlockedChanged?.Invoke(isBreathBlocked);
    }

    private void ResetWaterExposureState()
    {
        bool wasBreathBlocked = isBreathBlocked;
        isInWater = false;
        isBreathBlocked = false;
        if (wasBreathBlocked)
            BreathBlockedChanged?.Invoke(false);
    }

    #endregion

    #region 生存结算

    /// <summary>统一修改氧气值；正数恢复、负数消耗，并广播 HUD。</summary>
    public void AddOxygen(float value) => SetCurrentOxygen(CurrentValue + value);

    private void SetCurrentOxygen(float value)
    {
        float maxOxygen = Mathf.Max(0f, MaxValue);
        float nextValue = Mathf.Clamp(value, 0f, maxOxygen);
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
        Data.MaxOxygen = Mathf.Max(0f, Data.MaxOxygen);
        Data.CurrentOxygen = Mathf.Clamp(Data.CurrentOxygen, 0f, Data.MaxOxygen);
    }

    #endregion
}
