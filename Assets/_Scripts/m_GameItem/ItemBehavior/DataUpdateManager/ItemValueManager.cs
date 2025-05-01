using NaughtyAttributes;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class ItemValuesManager : MonoBehaviour
{/*
    public bool IsWorking = false;
    [ShowNonSerializedField]
    public Dictionary<string, Value_ChangeSpeed_Dic> Value_DICT = new Dictionary<string, Value_ChangeSpeed_Dic>();
 
    public void Start_ValueManager(IItemValues itemValues_)
    {
        IsWorking = true;

        Value_DICT.Clear();

        // �����ֵ�
        itemValues_.ItemValues.Build_ItemValueDict();

        // ʹ�ù����õ��ֵ���г�ʼ��
        foreach (var kvp in itemValues_.ItemValues.ItemValue_Dict)
        {
            var valueName = kvp.Key;
            var itemValue = kvp.Value;

            if (!string.IsNullOrEmpty(valueName) && itemValue != null)
            {
                Value_DICT[valueName] = new Value_ChangeSpeed_Dic(itemValue);
            }
        }

        // ����Ĭ���ٶ�
        foreach (var value in Value_DICT.Values)
        {
            value.StartChangeValue(value.itemValue.DefaultSpeed, "Ĭ���ٶ�");
        }
    }


    public void FixedUpdate()
    {
        if (!IsWorking)
        {
            return;
        }
        foreach (var value in Value_DICT.Values)
        {
            value.FixedUpdate_Act();
        }
    }
*/

}
/*
[System.Serializable]
public class Value_ChangeSpeed_Dic
{
    public ItemValue itemValue;

    [ShowNonSerializedField] // �ٶ������ֵ䣨�ָ���
    public Dictionary<string, float> Value_Change_DICT = new Dictionary<string, float>();
    // ��ǰ�ָܻ�����
    public float totalRecoveryRate = 0f;

    // ��ǰ����������
    public float totalSpeed = 0f;

    public float CurrentValue
    {
        get => itemValue.CurrentValue;
        set
        {
          
            itemValue.CurrentValue = value;
            OnValueChanged.Invoke(value);
            
        }
    }

    // ��ֵ�仯ʱ�������¼������ݱ仯ֵ��
    public UltEvent<float> OnValueChanged = new UltEvent<float>();

    public UltEvent OnValueSpeedChanged = new UltEvent();

    /// <summary>
    /// �������к���
    /// </summary>
    public void FixedUpdate_Act()
    {
        CurrentValue += totalSpeed * Time.fixedDeltaTime;
    }
    //���캯��
    public Value_ChangeSpeed_Dic(ItemValue itemValue_)
    {
        itemValue = itemValue_;
    }

    [Button("��������ٶ�")]
    public void StartChangeValue(float consumptionRate, string sourceName)
    {
        Value_Change_DICT[sourceName] = consumptionRate;
        OnValueSpeedChanged.Invoke();
        UpdateTotalValue();
    }

    [Button("ֹͣ�ٶ�����")]
    public void StopChangeValue(string sourceName)
    {
        Value_Change_DICT.Remove(sourceName);
        OnValueSpeedChanged.Invoke();
        UpdateTotalValue();
    }

    [Button("�����������ٶ�")]
    public void UpdateTotalValue()
    {
        foreach (var value in Value_Change_DICT.Values)
        {
            totalSpeed += value;
        }
    }
}
*/