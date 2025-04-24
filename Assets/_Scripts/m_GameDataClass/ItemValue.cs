using MemoryPack;
using System.Collections;
using System.Collections.Generic;
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
    public float CurrentValue;
    [Tooltip("最大值")]
    public float MaxValue;
    [Tooltip("相关速度")]
    public float DefaultSpeed;
}