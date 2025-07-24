using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.UI;

public partial class Mod_Stamina : Module
{
    [System.Serializable]
    [MemoryPackable]
    public partial class StaminaData
    {
        public float Current_Stamina;
        public GameValue_float Max_Stamina;
    }

    public StaminaData staminaData;

    public UltEvent OnChangeStamina = new UltEvent();

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData_MemoryPack)value;
    }

    public Ex_ModData_MemoryPack modData;

    public Slider slider;

    public override void Load()
    {
        modData.ReadData(ref staminaData);
        UpdateSlider();
    
        if (slider == null)    slider = GetComponentInChildren<Slider>();
    }

    public override void Save()
    {
        modData.WriteData(staminaData);
    }

    void Update()
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
        get => staminaData.Current_Stamina;
        set
        {
            staminaData.Current_Stamina = Mathf.Clamp(value, 0, MaxValue);
            OnChangeStamina.Invoke(); // 值改变时触发事件
            UpdateSlider(); // 更新Slider显示
        }
    }

    // 最大体力值 - 只读属性，不受事件影响
    public float MaxValue
    {
        get => staminaData.Max_Stamina.Value;
        // 移除了 setter，使其成为只读属性
    }
}