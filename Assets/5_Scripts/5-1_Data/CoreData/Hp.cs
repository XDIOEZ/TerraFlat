using MemoryPack;
using System.Collections.Generic;
using System;
using System.Linq;
using NaughtyAttributes;
using UltEvents;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class Hp
{
    [SerializeField]
    private float value;
    public float maxValue;
    // 添加弱点字符串列表
    public List<string> Weaknesses;
    [MemoryPackIgnore]
    public UltEvent<float> OnValueChanged = new UltEvent<float>();

    public float Value
    {
        get => value;
        set
        {
            if (Mathf.Approximately(this.value, value)) return;
            this.value = value;
            OnValueChanged.Invoke(this.value); // ✅ 传递当前值
        }
    }

    /*public Buffs buff;*/

    [MemoryPackConstructor]
    public Hp(float value)
    {
        this.Value = value;
        maxValue = value;
        Weaknesses = new List<string>(); // 初始化弱点列表
    }

    /// <summary>
    /// 检测是否存在指定的弱点
    /// </summary>
    /// <param name="weakness">需要检测的弱点</param>
    /// <returns>如果存在返回 true，否则返回 false</returns>
    public bool Check_Weakness(string weakness)
    {
        // 输入验证：如果 weakness 为空或无效，直接返回 false
        if (string.IsNullOrWhiteSpace(weakness))
        {
            return false;
        }

        // 检查 Weaknesses 列表中是否存在指定的弱点（忽略大小写）
        return Weaknesses.Contains(weakness, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检测是否存在任意一个指定的弱点
    /// </summary>
    /// <param name="weaknessList">需要检测的弱点列表</param>
    /// <returns>如果至少存在一个返回 true，否则返回 false</returns>
    public bool Check_Weakness(List<string> weaknessList)
    {
        if (weaknessList == null || weaknessList.Count == 0)
        {
            return false;
        }

        foreach (var weakness in weaknessList)
        {
            if (!string.IsNullOrWhiteSpace(weakness) &&
                Weaknesses.Contains(weakness, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

