using MemoryPack;
using Sirenix.OdinInspector;
using UltEvents;
using UnityEngine;
using FlatWorld.Networking;

public partial class Mod_Temperature : Module, IEnvironmentAdjustable
{
    public const float NormalSurfaceTemperature = 36.5f; // 玩家默认体表温度，也是重生后的恢复目标

    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.25f;

#region 嵌套类型

    [System.Serializable]
    [MemoryPackable]
    public partial class TemperatureData
    {
        [LabelText("当前体表温度"), SuffixLabel("℃", true), PropertyTooltip("角色当前最外层温度。")]
        public float CurrentTemperature = NormalSurfaceTemperature; // 当前体表温度(℃)
        [HideInInspector]
        public float AmbientTemperature = 20f; // 当前环境温度(℃)
        [HideInInspector]
        public float Insulation = 0f; // 保留 MemoryPack 字段槽位，不再参与温度计算。

        [LabelText("安全体表温度下限"), SuffixLabel("℃", true), PropertyTooltip("体表温度低于该值时消耗缓冲，耗尽后获得低温冻伤。")]
        public float SafeTemperatureMin = 5f; // 危险低温的体表温度下限(℃)
        [LabelText("安全体表温度上限"), SuffixLabel("℃", true), PropertyTooltip("体表温度高于该值时消耗缓冲，耗尽后获得热射病。")]
        public float SafeTemperatureMax = 50f; // 危险高温的体表温度上限(℃)

        [LabelText("危险温度缓冲时长"), MinValue(0f), SuffixLabel("秒", true)]
        public float DangerTemperatureBufferSeconds = 60f; // 冷热共用的最大缓冲时间。
        [LabelText("安全温度缓冲恢复速度"), MinValue(0f), SuffixLabel("秒/秒", true)]
        public float TemperatureBufferRecoveryPerSecond = 1f; // 安全时每秒补回的缓冲时间。
        [LabelText("危险温度剩余缓冲"), ReadOnly, SuffixLabel("秒", true)]
        public float RemainingTemperatureBufferSeconds = 60f; // 剩余缓冲随角色保存，不能靠重载补满。

        [MemoryPackIgnore]
        public float RuntimeAmbientOffset = 0f; // 天气暴露、火源等运行时环境修正

        [MemoryPackIgnore]
        public float RuntimeChangeSpeedMultiplier = 1f; // 运行时体温变化速度倍率

        [MemoryPackIgnore]
        public float RuntimeCoolingSpeedMultiplier = 1f; // 仅在体温下降时生效的运行时倍率

        /// <summary>制作独立存档快照，只替换基础体温，不修改仍在运行的有效体温。</summary>
        public TemperatureData CreateSaveSnapshot(float naturalTemperature)
        {
            var snapshot = (TemperatureData)MemberwiseClone();
            snapshot.CurrentTemperature = naturalTemperature;
            return snapshot;
        }
    }

#endregion

#region 字段

    public const string ModuleId = "体温模块";
    // 模板读取早于 Awake，能力 ID 必须在装配前确定。
    public override string CanonicalModuleId => ModuleId;

    public Ex_ModData_MemoryPackable modData; // 模块存档容器
    [LabelText("体温数据"), InlineProperty]
    public TemperatureData Data = new TemperatureData(); // 体温运行时数据

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData_MemoryPackable)value;
    }

    public UltEvent<float> OnTemperatureChanged = new UltEvent<float>(); // 体温变化事件

    private bool _isInWater; // 当前是否处于真实水体中
    private int _lastWaterExitFrame = -1; // 最近一次退出真实水体的帧
    private bool _lastWaterExitWasActive; // 最近一次退出前是否确实处于水中
    private bool _hasWaterEntryCoolingTarget; // 是否存在尚未完成的入水降温目标
    private float _waterEntryCoolingTargetTemperature; // 本次入水降温的目标体温
    private float _waterEntryCoolingSpeed; // 本次入水降温速度(℃/s)
    private float _waterEntryReferenceTemperature; // 连续水域首次入水的基础体温，不重复扣除水格降温
    private float _waterEntryTemperatureDrop; // 最近一次有效浸没对应的降温档位
    private float _waterCoolingProtection; // 装备等来源累计提供的入水降温保护，1 表示完全免疫

#endregion

#region 生命周期

    /// <summary>恢复体温数据并重置不应跨生命周期保留的水体状态。</summary>
    protected override void OnLoad()
    {
        ResetTemporaryWarming();
        float configuredSafeTemperatureMin = Data.SafeTemperatureMin;
        float configuredSafeTemperatureMax = Data.SafeTemperatureMax;
        float configuredBufferSeconds = Data.DangerTemperatureBufferSeconds;
        float configuredBufferRecovery = Data.TemperatureBufferRecoveryPerSecond;
        Data.RemainingTemperatureBufferSeconds = configuredBufferSeconds;
        modData.ReadData(ref Data);
        Data.SafeTemperatureMin = configuredSafeTemperatureMin;
        Data.SafeTemperatureMax = configuredSafeTemperatureMax; // 安全范围属于当前玩法配置，不由角色存档覆盖。
        Data.DangerTemperatureBufferSeconds = configuredBufferSeconds;
        Data.TemperatureBufferRecoveryPerSecond = configuredBufferRecovery;
        TemperatureMgr.Instance.NormalizeData(Data);
        ResetWaterExposureState();

        InitializeTemperatureSafety();
        item.OnInit_Env += AdjustByEnvironment;
    }

    protected override void OnSave()
    {
        modData?.WriteData(Data.CreateSaveSnapshot(NaturalTemperature));
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority)
            return;

        // 只对已经加载的脚下地块结算；不把未就绪区块当成全局温度。
        float ambient;
        if (ItemEnvironmentSources.TryGet(item, out ItemEnvironmentSample localEnvironment))
            ambient = (float)localEnvironment.TemperatureCelsius;
        else if (!TemperatureMgr.Instance.TryGetAmbientTemperature(item.transform.position, out ambient))
            return;

        Data.AmbientTemperature = ambient;
        ProcessWaterEntryCooling(deltaTime);
        TemperatureMgr.Instance.ProcessTemperature(
            Data,
            deltaTime,
            SetNaturalTemperature,
            NaturalTemperature);
        UpdateTemperatureSafety(deltaTime);
    }

    protected override void OnUnload()
    {
        if (item != null)
        {
            item.OnInit_Env -= AdjustByEnvironment;
        }
        ResetWaterExposureState();
        ResetTemperatureSafety();
        ResetTemporaryWarming();
        base.OnUnload();
    }

    private void OnDestroy() => Unload();

#endregion

#region 环境接口

    public void AdjustByEnvironment(EnvironmentLayers layers, Vector2Int localPos)
    {
        if (layers == null || !layers.Contains(localPos.x, localPos.y))
        {
            return;
        }

        // 初始化只写本角色，后续由当前位置的权威温度场持续更新。
        Data.AmbientTemperature = layers.TemperatureCelsius[localPos.x, localPos.y] +
                                  TemperatureMgr.Instance.AmbientFieldOffset;
    }

#endregion

#region 公共方法

    public void AddTemperature(float value)
    {
        SetNaturalTemperature(NaturalTemperature + value);
    }

    public void SetTemperature(float value)
    {
        SetNaturalTemperature(value);
    }

    public void SetAmbientTemperature(float value)
    {
        Data.AmbientTemperature = value;
    }

    /// <summary>重生时恢复正常基础体温，并清除上一条生命遗留的水体降温与冷热伤计时。</summary>
    public void RestoreOnRespawn()
    {
        ResetWaterExposureState();
        ClearTemperatureConditionBuffs();
        Data.RemainingTemperatureBufferSeconds = Data.DangerTemperatureBufferSeconds;
        ClearTemperatureSafetyRetreat();
        SetNaturalTemperature(NormalSurfaceTemperature);
    }

    /// <summary>以连续水域入水体温为基准，只在有效浸没档位变化时更新平滑目标。</summary>
    public void SetWaterExposure(
        bool inWater,
        float temperatureDrop,
        float minimumTemperature,
        float transitionSeconds)
    {
        if (!inWater)
        {
            _lastWaterExitWasActive = _isInWater;
            _lastWaterExitFrame = Time.frameCount;
            _isInWater = false;
            return;
        }

        bool continuedAcrossWaterTiles =
            _isInWater || (_lastWaterExitWasActive && _lastWaterExitFrame == Time.frameCount);
        _isInWater = true;
        _lastWaterExitWasActive = false;
        if (!GameNetwork.HasStateAuthority)
            return;

        if (!continuedAcrossWaterTiles)
            _waterEntryReferenceTemperature = NaturalTemperature;
        else if (Mathf.Approximately(_waterEntryTemperatureDrop, temperatureDrop))
            return;

        _waterEntryTemperatureDrop = temperatureDrop;
        BeginWaterEntryCooling(temperatureDrop, minimumTemperature, transitionSeconds);
    }

    public void MultiplyRuntimeCoolingSpeed(float multiplier)
    {
        if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0f)
        {
            Debug.LogWarning($"[Mod_Temperature] 忽略无效降温倍率：{multiplier}", this);
            return;
        }

        Data.RuntimeCoolingSpeedMultiplier = Mathf.Clamp(
            Data.RuntimeCoolingSpeedMultiplier * multiplier,
            0.01f,
            100f);
    }

    /// <summary>叠加入水降温保护；0.8 表示只保留 20% 的入水降温速度，1 及以上表示完全阻止入水降温。</summary>
    public void AddWaterCoolingProtection(float protection)
    {
        if (float.IsNaN(protection) || float.IsInfinity(protection) || protection < 0f)
        {
            Debug.LogWarning($"[Mod_Temperature] 忽略无效入水降温保护：{protection}", this);
            return;
        }

        _waterCoolingProtection += protection;
    }

    /// <summary>移除先前叠加的入水降温保护。</summary>
    public void RemoveWaterCoolingProtection(float protection)
    {
        if (float.IsNaN(protection) || float.IsInfinity(protection) || protection < 0f)
        {
            Debug.LogWarning($"[Mod_Temperature] 忽略无效入水降温保护移除值：{protection}", this);
            return;
        }

        _waterCoolingProtection = Mathf.Max(0f, _waterCoolingProtection - protection);
    }

    public bool IsComfortable()
    {
        return IsTemperatureSafe(Data.CurrentTemperature);
    }

#endregion

#region 私有方法

    /// <summary>建立入水降温目标，在指定时间内逐步完成降温而不是瞬间跳变。</summary>
    private void BeginWaterEntryCooling(
        float temperatureDrop,
        float minimumTemperature,
        float transitionSeconds)
    {
        float resolvedDrop = Mathf.Max(0f, temperatureDrop);
        float resolvedMinimum = Mathf.Max(0f, minimumTemperature);
        if (resolvedDrop <= 0f || NaturalTemperature <= resolvedMinimum)
        {
            ClearWaterEntryCooling();
            return;
        }

        _waterEntryCoolingTargetTemperature = WaterEnvironmentRules.ResolveCoolingTarget(
            _waterEntryReferenceTemperature, resolvedDrop, resolvedMinimum);
        float coolingDistance = NaturalTemperature - _waterEntryCoolingTargetTemperature;
        if (coolingDistance <= 0f)
        {
            ClearWaterEntryCooling();
            return;
        }

        float resolvedTransitionSeconds = Mathf.Max(0.1f, transitionSeconds);
        _waterEntryCoolingSpeed = coolingDistance / resolvedTransitionSeconds;
        _hasWaterEntryCoolingTarget = true;
    }

    /// <summary>仅在真实浸水期间把体温平滑推进到本次入水目标，环境自身的温度变化仍独立结算。</summary>
    private void ProcessWaterEntryCooling(float deltaTime)
    {
        if (!_hasWaterEntryCoolingTarget)
            return;

        if (!_isInWater)
        {
            if (Time.frameCount > _lastWaterExitFrame)
                ClearWaterEntryCooling();
            return;
        }

        if (NaturalTemperature <= _waterEntryCoolingTargetTemperature)
        {
            ClearWaterEntryCooling();
            return;
        }

        float waterCoolingSpeedMultiplier = Mathf.Clamp01(1f - _waterCoolingProtection);
        if (waterCoolingSpeedMultiplier <= 0f)
            return;

        float nextTemperature = ThermalRuntime.AdvanceTowards(
            NaturalTemperature,
            _waterEntryCoolingTargetTemperature,
            _waterEntryCoolingSpeed,
            deltaTime,
            waterCoolingSpeedMultiplier);
        SetNaturalTemperature(nextTemperature);

        if (NaturalTemperature <= _waterEntryCoolingTargetTemperature)
            ClearWaterEntryCooling();
    }

    /// <summary>清除当前入水降温插值状态。</summary>
    private void ClearWaterEntryCooling()
    {
        _hasWaterEntryCoolingTarget = false;
        _waterEntryCoolingTargetTemperature = 0f;
        _waterEntryCoolingSpeed = 0f;
    }

    /// <summary>清除仅属于当前运行实例的入水判定状态。</summary>
    private void ResetWaterExposureState()
    {
        _isInWater = false;
        _lastWaterExitFrame = -1;
        _lastWaterExitWasActive = false;
        _waterEntryReferenceTemperature = 0f;
        _waterEntryTemperatureDrop = 0f;
        ClearWaterEntryCooling();
    }

    private void SetTemperatureInternal(float value)
    {
        float oldValue = Data.CurrentTemperature;
        Data.CurrentTemperature = value;

        if (Mathf.Approximately(oldValue, Data.CurrentTemperature))
        {
            return;
        }

        OnAction.Invoke(Data.CurrentTemperature);
        OnTemperatureChanged.Invoke(Data.CurrentTemperature);
    }

#endregion
}
