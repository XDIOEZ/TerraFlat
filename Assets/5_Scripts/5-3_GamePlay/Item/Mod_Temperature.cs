using MemoryPack;
using UltEvents;
using UnityEngine;

public partial class Mod_Temperature : Module, IEnvironmentAdjustable
{
#region 嵌套类型

    [System.Serializable]
    [MemoryPackable]
    public partial class TemperatureData
    {
        public float CurrentTemperature = 36.5f; // 当前体温(℃)
        public float AmbientTemperature = 20f; // 当前环境温度(℃)
        public float ComfortableMin = 36f; // 舒适区最低体温(℃)
        public float ComfortableMax = 37.2f; // 舒适区最高体温(℃)
        public float ChangeSpeed = 0.5f; // 体温趋近环境的速度(℃/s)
        public float Insulation = 0f; // 保温系数(℃，正数偏保暖，负数偏散热)

        public float ColdDamageStart = 34f; // 低于该体温开始受冷伤(℃)
        public float HotDamageStart = 40f; // 高于该体温开始受热伤(℃)
        public float ColdDamagePerSecond = 0.6f; // 低温每秒伤害
        public float HotDamagePerSecond = 1f; // 高温每秒伤害

        public float CriticalLow = 30f; // 危险低温阈值(℃)
        public float CriticalHigh = 43f; // 危险高温阈值(℃)
    }

#endregion

#region 字段

    public const string ModuleId = "体温模块";

    public Ex_ModData_MemoryPackable modData; // 模块存档容器
    public TemperatureData Data = new TemperatureData(); // 体温运行时数据

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData_MemoryPackable)value;
    }

    public UltEvent<float> OnTemperatureChanged = new UltEvent<float>(); // 体温变化事件

    private DamageReceiver _damageReceiver; // 血量模块引用

#endregion

#region 生命周期

    public override void Awake()
    {
        if (_Data.ID == string.Empty)
        {
            _Data.ID = ModText.Temperature;
        }
    }

    public override void Load()
    {
        modData.ReadData(ref Data);
        NormalizeData();

        _damageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        item.OnInit_Env += AdjustByEnvironment;
    }

    public override void Save()
    {
        modData.WriteData(Data);
    }

    public override void ModUpdate(float deltaTime)
    {
        float targetTemperature = Data.AmbientTemperature + Data.Insulation;
        SetTemperatureInternal(Mathf.MoveTowards(Data.CurrentTemperature, targetTemperature, Data.ChangeSpeed * deltaTime));

        ApplyTemperatureDamage(deltaTime);
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

        Data.AmbientTemperature = layers.TemperatureCelsius[localPos.x, localPos.y];
        Debug.Log($"[Mod_Temperature] 环境初始化完成，环境温度={Data.AmbientTemperature:F1}℃，当前体温={Data.CurrentTemperature:F1}℃");
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
        Data.AmbientTemperature = value;
    }

    public bool IsComfortable()
    {
        return Data.CurrentTemperature >= Data.ComfortableMin && Data.CurrentTemperature <= Data.ComfortableMax;
    }

#endregion

#region 私有方法

    private void NormalizeData()
    {
        Data.ChangeSpeed = Mathf.Max(0f, Data.ChangeSpeed);

        Data.ColdDamagePerSecond = Mathf.Max(0f, Data.ColdDamagePerSecond);
        Data.HotDamagePerSecond = Mathf.Max(0f, Data.HotDamagePerSecond);

        Data.ComfortableMax = Mathf.Max(Data.ComfortableMin, Data.ComfortableMax);
        Data.HotDamageStart = Mathf.Max(Data.ColdDamageStart, Data.HotDamageStart);
        Data.CriticalHigh = Mathf.Max(Data.HotDamageStart, Data.CriticalHigh);
        Data.CriticalLow = Mathf.Min(Data.ColdDamageStart, Data.CriticalLow);
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

    private void ApplyTemperatureDamage(float deltaTime)
    {
        if (_damageReceiver == null)
        {
            return;
        }

        if (Data.CurrentTemperature < Data.ColdDamageStart)
        {
            float ratio = Mathf.Max(0f, Data.ColdDamageStart - Data.CurrentTemperature);
            _damageReceiver.ForceHurt(ratio * Data.ColdDamagePerSecond * deltaTime);
        }

        if (Data.CurrentTemperature > Data.HotDamageStart)
        {
            float ratio = Mathf.Max(0f, Data.CurrentTemperature - Data.HotDamageStart);
            _damageReceiver.ForceHurt(ratio * Data.HotDamagePerSecond * deltaTime);
        }
    }

#endregion
}