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
        public float CurrentStamina;
        public GameValue_float MaxStamina;
    }
    // �Ƿ�Ϊ������ - ʵʱ�ж�
    public bool IsStaminaFull => Mathf.Approximately(Data.CurrentStamina, MaxValue);

    public StaminaData Data;

    public UltEvent OnChangeStamina = new UltEvent();

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData_MemoryPackable)value;
    }

    public Ex_ModData_MemoryPackable modData;

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
        UpdateSlider();
    
        if (slider == null)    slider = GetComponentInChildren<Slider>();
    }

    public override void Save()
    {
        modData.WriteData(Data);
    }

    public void AddStamina(float value)
    {
        if (value <= 0) return; // ��ֵ������Ҳ������չ������۳�������

        CurrentValue += value; // ���Զ������¼��͸���Slider
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


    // ��ǰ����ֵ - ���¼�����
    public float CurrentValue
    {
        get => Data.CurrentStamina;
        set
        {
            Data.CurrentStamina = Mathf.Clamp(value, 0, MaxValue);
            OnChangeStamina.Invoke(); // ֵ�ı�ʱ�����¼�
            UpdateSlider(); // ����Slider��ʾ
        }
    }

    // �������ֵ - ֻ�����ԣ������¼�Ӱ��
    public float MaxValue
    {
        get => Data.MaxStamina.Value;
        // �Ƴ��� setter��ʹ���Ϊֻ������
    }
}