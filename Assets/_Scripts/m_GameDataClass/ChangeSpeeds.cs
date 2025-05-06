
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
    [ShowInInspector] // 速度字典（恢复）
    public Dictionary<string, SpeedDuration> SpeedDurations_DICT = new Dictionary<string, SpeedDuration>();
 
    [Tooltip("添加对应速度(单位/秒)")]
    public void ADDChangeValue(float Value, string sourceName, float duration = -1f)
    {
        // -1 表示无限持续
        SpeedDurations_DICT.Add(sourceName, new SpeedDuration(sourceName, Value, duration));
    }
 
    [Tooltip("设置对应速度值的持续时间(单位/秒)")]
    public void SetDuration(string sourceName, float newDuration)
    {
        if (SpeedDurations_DICT.TryGetValue(sourceName, out var data))
        {
            data.Duration = newDuration;
        }
    }

    // 检查是否已经存在该 SpeedName
    public bool HasChangeSpeed(string speedName)
    {
        return SpeedDurations_DICT.ContainsKey(speedName);
    }

    [Tooltip("停止对应速度的变化")]
    public void StopChangeValue(string sourceName)
    {
        SpeedDurations_DICT.Remove(sourceName);
    }

    [Tooltip("返回当前速度总和,同时减少持续时间")]
    public float GetCurrentSpeedSum(float deltaTime)
    {
        float sum = 0f;
        List<string> keysToRemove = new List<string>();

        foreach (var kvp in SpeedDurations_DICT)
        {
            var data = kvp.Value;

            if (data.Duration != -1f) // -1 表示永久生效，不递减
            {
                data.Duration -= deltaTime;
                if (data.Duration < 0)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            sum += data.Speed;
        }

        // 避免在枚举中修改字典，统一处理
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
    public string name;         // 名称
    public float Speed;        // 当前速度
    public float Duration;     // 剩余持续时间（秒）

    public SpeedDuration(string name, float speed, float duration)
    {
        Speed = speed;
        Duration = duration;
    }
}