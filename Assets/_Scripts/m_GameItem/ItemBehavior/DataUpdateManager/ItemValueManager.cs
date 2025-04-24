using NaughtyAttributes;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class ItemValuesManager : MonoBehaviour
{
    IItemValues _itemValues;

    public bool IsWorking = false;

    [ShowNonSerializedField]
    public Dictionary<string, ValueChangeSpeedDic> Value_DICT = new Dictionary<string, ValueChangeSpeedDic>();

    public void SetItemValues_AndStart(IItemValues itemValues_)
    {
        IsWorking = true;

        _itemValues = itemValues_;

        foreach (var ItemValue_List in itemValues_.ItemValues.ItemValue_List)
        {
            Value_DICT[ItemValue_List.ValueName] = new ValueChangeSpeedDic(ItemValue_List);
        }
        // 启动默认速度
        foreach (var value in Value_DICT.Values)
        {
            value.StartChangeValue(value.itemValue.DefaultSpeed,"默认速度");
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


}
[System.Serializable]
public class ValueChangeSpeedDic
{
    public ItemValue itemValue;

    [ShowNonSerializedField] // 速度增加字典（恢复）
    public Dictionary<string, float> Value_Change_DICT = new Dictionary<string, float>();
    // 当前总恢复速率
    public float totalRecoveryRate = 0f;

    // 当前总消耗速率
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

    // 数值变化时触发的事件（传递变化值）
    public UltEvent<float> OnValueChanged = new UltEvent<float>();

    public UltEvent OnValueSpeedChanged = new UltEvent();

    /// <summary>
    /// 生命运行函数
    /// </summary>
    public void FixedUpdate_Act()
    {
        CurrentValue += totalSpeed * Time.fixedDeltaTime;
    }
    //构造函数
    public ValueChangeSpeedDic(ItemValue itemValue_)
    {
        itemValue = itemValue_;
    }

    [Button("添加作用速度")]
    public void StartChangeValue(float consumptionRate, string sourceName)
    {
        Value_Change_DICT[sourceName] = consumptionRate;
        OnValueSpeedChanged.Invoke();
        UpdateTotalValue();
    }

    [Button("停止速度作用")]
    public void StopChangeValue(string sourceName)
    {
        Value_Change_DICT.Remove(sourceName);
        OnValueSpeedChanged.Invoke();
        UpdateTotalValue();
    }

    [Button("更新总作用速度")]
    public void UpdateTotalValue()
    {
        foreach (var value in Value_Change_DICT.Values)
        {
            totalSpeed += value;
        }
    }
}
