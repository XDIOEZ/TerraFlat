using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class ItemValue
{
    [Tooltip("��������")]
    public string ValueName;
    [Tooltip("��Сֵ")]
    public float MinValue;
    [Tooltip("��ǰֵ")]
    public float CurrentValue;
    [Tooltip("���ֵ")]
    public float MaxValue;
    [Tooltip("����ٶ�")]
    public float DefaultSpeed;
}