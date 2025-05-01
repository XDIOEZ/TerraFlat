using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
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
    [SerializeField]
    private float currentValue;
    [Tooltip("���ֵ")]
    public float MaxValue;

    [MemoryPackIgnore]
    // ��ֵ�仯ʱ�������¼������ݱ仯ֵ��
    public UltEvent<float> OnCurrentValueChanged = new UltEvent<float>();

    [MemoryPackIgnore]
    public float CurrentValue
    {
        get => currentValue;
        set
        {
            if (Mathf.Approximately(currentValue, value)) return; // ���⸡�����µ��ظ�����
            currentValue = value;
            OnCurrentValueChanged.Invoke(currentValue); // �����¼�
        }
    }
}
