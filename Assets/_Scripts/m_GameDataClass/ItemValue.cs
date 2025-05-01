using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class ItemValue
{
    [Tooltip("参数名称")]
    public string ValueName;
    [Tooltip("最小值")]
    public float MinValue;
    [Tooltip("当前值")]
    [SerializeField]
    private float currentValue;
    [Tooltip("最大值")]
    public float MaxValue;

    [MemoryPackIgnore]
    // 数值变化时触发的事件（传递变化值）
    public UltEvent<float> OnCurrentValueChanged = new UltEvent<float>();

    [MemoryPackIgnore]
    public float CurrentValue
    {
        get => currentValue;
        set
        {
            if (Mathf.Approximately(currentValue, value)) return; // 避免浮点误差导致的重复触发
            currentValue = value;
            OnCurrentValueChanged.Invoke(currentValue); // 触发事件
        }
    }
}
