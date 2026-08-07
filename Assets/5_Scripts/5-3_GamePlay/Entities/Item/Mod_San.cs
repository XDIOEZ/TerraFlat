using MemoryPack;
using UltEvents;
using UnityEngine;

public partial class Mod_San : Module
{
#region 嵌套类型

    [System.Serializable]
    [MemoryPackable]
    public partial class SanData
    {
        public float CurrentSan = 100f; // 当前理智值
        public float MaxSan = 100f; // 理智上限
    }

#endregion

#region 字段

    public const string ModuleId = "理智模块";

    public Ex_ModData_MemoryPackable modData; // 模块存档容器
    public SanData Data = new SanData(); // 理智运行时数据

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData_MemoryPackable)value;
    }

    public UltEvent<float> OnSanChanged = new UltEvent<float>(); // 理智变化事件

    private bool _hasTriggeredDeath; // 防止重复触发死亡
    private Mod_PlayerTraits _playerTraits; // 玩家特质模块
    private DamageReceiver _damageReceiver; // 生命模块

#endregion

#region 属性

    public float CurrentValue
    {
        get => Data.CurrentSan;
        set
        {
            float clamped = Mathf.Clamp(value, 0f, MaxValue);
            if (Mathf.Approximately(Data.CurrentSan, clamped))
            {
                return;
            }

            Data.CurrentSan = clamped;
            OnAction.Invoke(Data.CurrentSan);
            OnSanChanged.Invoke(Data.CurrentSan);

            if (Data.CurrentSan <= 0f)
            {
                TryTriggerDeath();
                return;
            }

            _hasTriggeredDeath = false;
        }
    }

    public float MaxValue
    {
        get => Data.MaxSan;
        set
        {
            Data.MaxSan = Mathf.Max(0f, value);
            Data.CurrentSan = Mathf.Clamp(Data.CurrentSan, 0f, Data.MaxSan);
        }
    }

#endregion

#region 生命周期

    public override void Awake()
    {
        if (_Data.ID == string.Empty)
        {
            _Data.ID = ModText.San;
        }
    }

    public override void Load()
    {
        modData.ReadData(ref Data);
        NormalizeData();
        ResolveDependencies();

        if (CurrentValue <= 0f)
        {
            TryTriggerDeath();
        }
    }

    public override void Save()
    {
        modData.WriteData(Data);
    }

#endregion

#region 公共方法

    public void AddSan(float value)
    {
        CurrentValue += value;
    }

    public void ReduceSan(float value)
    {
        CurrentValue -= value;
    }

    public void SetSan(float value)
    {
        CurrentValue = value;
    }

#endregion

#region 私有方法

    private void NormalizeData()
    {
        Data.MaxSan = Mathf.Max(0f, Data.MaxSan);
        Data.CurrentSan = Mathf.Clamp(Data.CurrentSan, 0f, Data.MaxSan);
    }

    private void ResolveDependencies()
    {
        _playerTraits = item.itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
        _damageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
    }

    private void TryTriggerDeath()
    {
        if (_hasTriggeredDeath)
        {
            return;
        }

        _hasTriggeredDeath = true;

        if (_playerTraits != null)
        {
            Debug.Log($"[Mod_San] 理智归零，触发玩家死亡: {item.itemData.GameName}");
            _playerTraits.Death();
            return;
        }

        if (_damageReceiver != null)
        {
            Debug.Log($"[Mod_San] 理智归零，触发血量归零死亡: {item.itemData.GameName}");
            _damageReceiver.ForceHurt(_damageReceiver.Hp + _damageReceiver.MaxHp + 99999f);
            return;
        }

        throw new MissingComponentException($"[Mod_San] 目标 {item.itemData.GameName} 缺少 Mod_PlayerTraits 与 DamageReceiver，无法执行理智归零死亡。");
    }

#endregion
}