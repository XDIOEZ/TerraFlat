
using MemoryPack;
using NaughtyAttributes;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class ChangeSpeeds
{
    [ShowInInspector] // �ٶ��ֵ䣨�ָ���
    public Dictionary<string, SpeedDuration> SpeedDurations_DICT = new Dictionary<string, SpeedDuration>();
 
    [Tooltip("��Ӷ�Ӧ�ٶ�(��λ/��)")]
    public void ADDChangeValue(float Value, string sourceName, float duration = -1f)
    {
        // -1 ��ʾ���޳���
        SpeedDurations_DICT.Add(sourceName, new SpeedDuration(sourceName, Value, duration));
    }
 
    [Tooltip("���ö�Ӧ�ٶ�ֵ�ĳ���ʱ��(��λ/��)")]
    public void SetDuration(string sourceName, float newDuration)
    {
        if (SpeedDurations_DICT.TryGetValue(sourceName, out var data))
        {
            data.Duration = newDuration;
        }
    }

    // ����Ƿ��Ѿ����ڸ� SpeedName
    public bool HasChangeSpeed(string speedName)
    {
        return SpeedDurations_DICT.ContainsKey(speedName);
    }

    [Tooltip("ֹͣ��Ӧ�ٶȵı仯")]
    public void StopChangeValue(string sourceName)
    {
        SpeedDurations_DICT.Remove(sourceName);
    }

    [Tooltip("���ص�ǰ�ٶ��ܺ�,ͬʱ���ٳ���ʱ��")]
    public float GetCurrentSpeedSum(float deltaTime)
    {
        float sum = 0f;
        List<string> keysToRemove = new List<string>();

        foreach (var kvp in SpeedDurations_DICT)
        {
            var data = kvp.Value;

            if (data.Duration != -1f) // -1 ��ʾ������Ч�����ݼ�
            {
                data.Duration -= deltaTime;
                if (data.Duration < 0)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            sum += data.Speed;
        }

        // ������ö�����޸��ֵ䣬ͳһ����
        foreach (var key in keysToRemove)
        {
            SpeedDurations_DICT.Remove(key);
        }

        return sum;
    }

}

[System.Serializable]
[MemoryPackable]
public partial class SpeedDuration
{
    public string name;         // ����
    public float Speed;        // ��ǰ�ٶ�
    public float Duration;     // ʣ�����ʱ�䣨�룩

    public SpeedDuration(string name, float speed, float duration)
    {
        Speed = speed;
        Duration = duration;
    }
}