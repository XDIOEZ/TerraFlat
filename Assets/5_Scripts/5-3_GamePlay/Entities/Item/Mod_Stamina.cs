using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.UI;

public partial class Mod_Stamina : Module
{
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

    public void AddStamina(float value)
    {
        float multiplier = GameDifficultyService.ResolveStaminaDeltaMultiplier(item, value);
        CurrentValue += value * multiplier; // 会自动触发事件和更新Slider
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
        get => Data.MaxStamina;
        // 移除了 setter，使其成为只读属性
    }
}