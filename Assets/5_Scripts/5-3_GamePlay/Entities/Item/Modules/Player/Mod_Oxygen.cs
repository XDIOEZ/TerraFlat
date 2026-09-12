using FlatWorld.Networking;
using MemoryPack;
using UltEvents;
using UnityEngine;

/// <summary>
/// 玩家水中生存模块：在真实水体中持续消耗体力维持漂浮，体力耗尽后消耗氧气，
/// 氧气归零后通过 DamageReceiver 按每秒 10 点基础伤害持续结算溺水伤害。
/// 氧气是玩家独立持久化状态，不使用 Buff，水体只负责同步当前是否真实入水。
/// </summary>
public partial class Mod_Oxygen : Module, IItemModuleDependencyBinder
{
    public const string ModuleId = "氧气模块";

    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.1f;

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
    [Min(0f)] public float drowningDamagePerSecond = 10f; // 氧气归零后每秒基础溺水伤害。

    public UltEvent<float> OnOxygenChanged = new(); // 氧气变化通知，供 HUD 等表现层订阅。

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData_MemoryPackable)value;
    }

    public float CurrentValue => Data?.CurrentOxygen ?? 0f;
    public float MaxValue => Data?.MaxOxygen ?? 0f;
    public float OxygenRatio => MaxValue > 0f ? Mathf.Clamp01(CurrentValue / MaxValue) : 0f;
    public bool IsInWater => isInWater;
    public bool IsDrowning => isInWater && stamina != null && stamina.CurrentValue <= 0f && CurrentValue <= 0f;

    #endregion

    #region 运行时依赖

    private Mod_Stamina stamina; // 玩家体力权威模块。
    private DamageReceiver damageReceiver; // 玩家生命权威模块。
    private bool isInWater; // 当前是否处于真实水格。
    private int lastWaterExitFrame = -1; // 连续水格切换时避免把同帧退出当成真正上岸。
    private bool lastWaterExitWasActive; // 最近退出前是否确实处于水中。

    public void BindModuleDependencies(ItemMods modules)
    {
        stamina = modules.RequireSingleModById<Mod_Stamina>(ModText.Stamina);
        damageReceiver = modules.RequireSingleModById<DamageReceiver>(ModText.Hp);
    }

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

    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority || Data == null || damageReceiver == null || damageReceiver.Hp <= 0f)
            return;

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        if (safeDeltaTime <= 0f)
            return;

        if (!isInWater)
        {
            RecoverOxygen(safeDeltaTime);
            return;
        }

        ProcessWaterSurvival(safeDeltaTime);
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
        if (!inWater)
        {
            lastWaterExitWasActive = isInWater;
            lastWaterExitFrame = Time.frameCount;
            isInWater = false;
            return;
        }

        if (isInWater)
            return;

        bool continuedAcrossWaterTiles =
            lastWaterExitWasActive && lastWaterExitFrame == Time.frameCount;
        isInWater = true;
        lastWaterExitWasActive = false;

        // 同帧跨水格仍属于同一片连续水域，不需要额外重置任何氧气状态。
        if (continuedAcrossWaterTiles)
            return;
    }

    private void ResetWaterExposureState()
    {
        isInWater = false;
        lastWaterExitFrame = -1;
        lastWaterExitWasActive = false;
    }

    #endregion

    #region 生存结算

    /// <summary>先消耗体力维持漂浮；体力为零后才开始消耗氧气。</summary>
    private void ProcessWaterSurvival(float deltaTime)
    {
        if (stamina != null && stamina.CurrentValue > 0f)
        {
            stamina.AddStamina(-Mathf.Max(0f, staminaConsumePerSecond) * deltaTime);
            if (stamina.CurrentValue > 0f)
            {
                RecoverOxygen(deltaTime);
                return;
            }
        }

        ConsumeOxygen(deltaTime);
        if (CurrentValue > 0f)
            return;

        float damage = Mathf.Max(0f, drowningDamagePerSecond) * deltaTime;
        if (damage > 0f)
            damageReceiver.ForceHurt(damage);
    }

    private void ConsumeOxygen(float deltaTime)
    {
        SetCurrentOxygen(CurrentValue - Mathf.Max(0f, oxygenConsumePerSecond) * deltaTime);
    }

    private void RecoverOxygen(float deltaTime)
    {
        SetCurrentOxygen(CurrentValue + Mathf.Max(0f, oxygenRecoverPerSecond) * deltaTime);
    }

    private void SetCurrentOxygen(float value)
    {
        float maxOxygen = Mathf.Max(0f, MaxValue);
        float nextValue = Mathf.Clamp(value, 0f, maxOxygen);
        if (Mathf.Approximately(Data.CurrentOxygen, nextValue))
            return;

        Data.CurrentOxygen = nextValue;
        OnAction.Invoke(nextValue);
        OnOxygenChanged.Invoke(nextValue);
    }

    private void NormalizeData()
    {
        Data ??= new OxygenData();
        Data.MaxOxygen = Mathf.Max(0f, Data.MaxOxygen);
        Data.CurrentOxygen = Mathf.Clamp(Data.CurrentOxygen, 0f, Data.MaxOxygen);
    }

    #endregion
}
