using MemoryPack;
using Sirenix.OdinInspector;
using UltEvents;
using UnityEngine;
using FlatWorld.Networking;

public partial class Mod_Temperature : Module, IEnvironmentAdjustable
{
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.25f;

#region 嵌套类型

    [System.Serializable]
    [MemoryPackable]
    public partial class TemperatureData
    {
        [LabelText("当前体温"), SuffixLabel("℃", true), PropertyTooltip("角色当前体温。")]
        public float CurrentTemperature = 36.5f; // 当前体温(℃)
        [HideInInspector]
        public float AmbientTemperature = 20f; // 当前环境温度(℃)
        [LabelText("变化速度"), SuffixLabel("℃/s", true), PropertyTooltip("体温向环境温度逼近的速度。")]
        public float ChangeSpeed = 0.5f; // 体温趋近环境的速度(℃/s)
        [LabelText("保温系数"), SuffixLabel("℃", true), PropertyTooltip("正数偏保暖，负数偏散热。")]
        public float Insulation = 0f; // 保温系数(℃，正数偏保暖，负数偏散热)

        [LabelText("冷伤起点"), SuffixLabel("℃", true), PropertyTooltip("体温低于该值后开始受到冷伤害。")]
        public float ColdDamageStart = 34f; // 低于该体温开始受冷伤(℃)
        [LabelText("热伤起点"), SuffixLabel("℃", true), PropertyTooltip("体温高于该值后开始受到热伤害。")]
        public float HotDamageStart = 40f; // 高于该体温开始受热伤(℃)
        [LabelText("冷伤每秒"), PropertyTooltip("低温状态下每秒造成的伤害值。")]
        public float ColdDamagePerSecond = 0.6f; // 低温每秒伤害
        [LabelText("热伤每秒"), PropertyTooltip("高温状态下每秒造成的伤害值。")]
        public float HotDamagePerSecond = 1f; // 高温每秒伤害

        [MemoryPackIgnore]
        public float RuntimeAmbientOffset = 0f; // 天气暴露、火源等运行时环境修正

        [MemoryPackIgnore]
        public float RuntimeChangeSpeedMultiplier = 1f; // 运行时体温变化速度倍率

        [MemoryPackIgnore]
        public float RuntimeCoolingSpeedMultiplier = 1f; // 仅在体温下降时生效的运行时倍率
    }

#endregion

#region 字段

    public const string ModuleId = "体温模块";

    public Ex_ModData_MemoryPackable modData; // 模块存档容器
    [LabelText("体温数据"), InlineProperty]
    public TemperatureData Data = new TemperatureData(); // 体温运行时数据

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData_MemoryPackable)value;
    }

    public UltEvent<float> OnTemperatureChanged = new UltEvent<float>(); // 体温变化事件

    private DamageReceiver _damageReceiver; // 血量模块引用
    private float _damageTickTimer; // 温度伤害计时器
    private bool _isInWater; // 当前是否处于真实水体中
    private int _lastWaterExitFrame = -1; // 最近一次退出真实水体的帧
    private bool _lastWaterExitWasActive; // 最近一次退出前是否确实处于水中
    private bool _hasWaterEntryCoolingTarget; // 是否存在尚未完成的入水降温目标
    private float _waterEntryCoolingTargetTemperature; // 本次入水降温的目标体温
    private float _waterEntryCoolingSpeed; // 本次入水降温速度(℃/s)
    private float _waterCoolingProtection; // 装备等来源累计提供的入水降温保护，1 表示完全免疫

#endregion

#region 生命周期

    public override void Awake()
    {
        if (_Data.ID == string.Empty)
        {
            _Data.ID = ModText.Temperature;
        }
    }

    /// <summary>恢复体温数据并重置不应跨生命周期保留的水体状态。</summary>
    public override void Load()
    {
        modData.ReadData(ref Data);
        TemperatureMgr.Instance.NormalizeData(Data);
        _damageTickTimer = 0f;
        ResetWaterExposureState();

        _damageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        item.OnInit_Env += AdjustByEnvironment;
    }

    public override void Save()
    {
        if (TemperatureMgr.Instance != null)
            Data.AmbientTemperature = TemperatureMgr.Instance.GetGlobalAmbientTemperature();

        modData?.WriteData(Data);
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority)
            return;

        Data.AmbientTemperature = TemperatureMgr.Instance.GetGlobalAmbientTemperature();
        ProcessWaterEntryCooling(deltaTime);
        TemperatureMgr.Instance.ProcessTemperature(Data, _damageReceiver, deltaTime, SetTemperatureInternal, ref _damageTickTimer);
    }

    private void OnDestroy()
    {
        if (item != null)
        {
            item.OnInit_Env -= AdjustByEnvironment;
        }
    }

#endregion

#region 环境接口

    public void AdjustByEnvironment(EnvironmentLayers layers, Vector2Int localPos)
    {
        if (layers == null || !layers.Contains(localPos.x, localPos.y))
        {
            return;
        }

        float ambientTemperature = layers.TemperatureCelsius[localPos.x, localPos.y];
        TemperatureMgr.Instance.SetGlobalAmbientTemperature(ambientTemperature);
    Data.AmbientTemperature = TemperatureMgr.Instance.GetGlobalAmbientTemperature();
    Debug.Log($"[Mod_Temperature] 环境初始化完成，基础温度={ambientTemperature:F1}℃，有效环境温度={Data.AmbientTemperature:F1}℃，当前体温={Data.CurrentTemperature:F1}℃");
    }

#endregion

#region 公共方法

    public void AddTemperature(float value)
    {
        SetTemperatureInternal(Data.CurrentTemperature + value);
    }

    public void SetTemperature(float value)
    {
        SetTemperatureInternal(value);
    }

    public void SetAmbientTemperature(float value)
    {
        TemperatureMgr.Instance.SetGlobalAmbientTemperature(value);
        Data.AmbientTemperature = TemperatureMgr.Instance.GetGlobalAmbientTemperature();
    }

    /// <summary>同步真实入水状态，并在首次进入连续水域时启动一次带下限的平滑降温。</summary>
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

        if (_isInWater)
            return;

        bool continuedAcrossWaterTiles =
            _lastWaterExitWasActive && _lastWaterExitFrame == Time.frameCount;
        _isInWater = true;
        _lastWaterExitWasActive = false;
        if (continuedAcrossWaterTiles || !GameNetwork.HasStateAuthority)
            return;

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
        return Data.CurrentTemperature >= Data.ColdDamageStart && Data.CurrentTemperature <= Data.HotDamageStart;
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
        if (resolvedDrop <= 0f || Data.CurrentTemperature <= resolvedMinimum)
        {
            ClearWaterEntryCooling();
            return;
        }

        _waterEntryCoolingTargetTemperature = Mathf.Max(
            resolvedMinimum,
            Data.CurrentTemperature - resolvedDrop);
        float coolingDistance = Data.CurrentTemperature - _waterEntryCoolingTargetTemperature;
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

        if (Data.CurrentTemperature <= _waterEntryCoolingTargetTemperature)
        {
            ClearWaterEntryCooling();
            return;
        }

        float waterCoolingSpeedMultiplier = Mathf.Clamp01(1f - _waterCoolingProtection);
        if (waterCoolingSpeedMultiplier <= 0f)
            return;

        float nextTemperature = Mathf.MoveTowards(
            Data.CurrentTemperature,
            _waterEntryCoolingTargetTemperature,
            _waterEntryCoolingSpeed * waterCoolingSpeedMultiplier * Mathf.Max(0f, deltaTime));
        SetTemperatureInternal(nextTemperature);

        if (Data.CurrentTemperature <= _waterEntryCoolingTargetTemperature)
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
