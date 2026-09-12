using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.UI;

public partial class Mod_Stamina : Module, IItemModuleDependencyBinder
{
    private readonly List<IStaminaCapacityModifier> capacityModifiers = new(); // 独立容量来源。
    /// <summary>缓存影响体力容量的模块，体力模块不依赖缺盐等具体玩法。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        capacityModifiers.Clear();
        foreach (Module module in modules.Mods.Values)
            if (module is IStaminaCapacityModifier modifier) capacityModifiers.Add(modifier);
    }
    /// <summary>容量变化后限制当前值，并通知现有 HUD。</summary>
    public void RefreshCapacity() => CurrentValue = Data.CurrentStamina;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    [System.Serializable]
    [MemoryPackable]
    public partial class StaminaData
    {
        public float CurrentStamina;
        public float MaxStamina;
    }
    // 是否为满精力 - 实时判断
    public bool IsStaminaFull => Mathf.Approximately(Data.CurrentStamina, MaxValue);

    public StaminaData Data;

    public UltEvent OnChangeStamina = new UltEvent();

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData_MemoryPackable)value;
    }

    public Ex_ModData_MemoryPackable modData;

    public GameObject Prefab_UI;//TODO 这个是UI的预制体

    public Slider slider;
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Stamina;
        }
    }
    public override void Load()
    {
        modData.ReadData(ref Data);

        // 实例化体力 UI 预制体并获取 Slider 组件
        if (Prefab_UI != null)
        {
            BasePanel staminaPanel = UIManager.Instance.CreatePanelFromGameObject(Prefab_UI);
            if (staminaPanel != null)
            {
                // 耐力条是常驻信息 HUD：不阻断玩法输入，并固定在其它面板下方。
                staminaPanel.SetGameplayInputBlocking(false);
                staminaPanel.transform.SetAsFirstSibling();
                slider = staminaPanel.GetComponentInChildren<Slider>();
            }
        }

        // 兜底：如果预制体上没找到，就在当前物体的子节点中找 Slider
        if (slider == null)
        {
            slider = GetComponentInChildren<Slider>();
        }

        // 用当前体力值刷新一次 UI
        UpdateSlider();
    }

    public override void Save()
    {
        modData.WriteData(Data);
    }

    /// <summary>增加或消耗基础体力值，并统一应用当前难度倍率。</summary>
    public void AddStamina(float value)
    {
        CurrentValue += ResolveStaminaDelta(value); // 会自动触发事件和更新Slider
    }

    /// <summary>尝试一次性消耗基础体力；不足时不扣除，供攻击等离散动作使用。</summary>
    public bool TryConsumeStamina(float amount)
    {
        if (amount <= 0f)
        {
            return true;
        }

        float delta = ResolveStaminaDelta(-amount);
        float effectiveCost = -delta;
        if (CurrentValue + 0.0001f < effectiveCost)
        {
            return false;
        }

        CurrentValue += delta;
        return true;
    }

    /// <summary>把基础体力变化换算为难度规则下的实际变化。</summary>
    private float ResolveStaminaDelta(float value)
    {
        float multiplier = GameDifficultyService.ResolveStaminaDeltaMultiplier(item, value);
        return value * multiplier;
    }


    public override void ModUpdate(float deltaTime)
    {
        UpdateSlider();
    }

    private void UpdateSlider()
    {
        if (slider != null && MaxValue > 0)
        {
            slider.value = CurrentValue / MaxValue;
        }
    }


    // 当前体力值 - 带事件触发
    public float CurrentValue
    {
        get => Data.CurrentStamina;
        set
        {
            Data.CurrentStamina = Mathf.Clamp(value, 0, MaxValue);
            OnChangeStamina.Invoke(); // 值改变时触发事件
            UpdateSlider(); // 更新Slider显示
        }
    }

    // 最大体力值 - 只读属性，不受事件影响
    public float MaxValue
    {
        get
        {
            float multiplier = 1f;
            foreach (IStaminaCapacityModifier modifier in capacityModifiers)
                multiplier *= modifier.StaminaCapacityMultiplier;
            return Data.MaxStamina * multiplier;
        }
        // 移除了 setter，使其成为只读属性
    }
}
