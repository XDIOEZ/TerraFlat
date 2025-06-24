using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class GameValue_float
{
    [Header("基础数值")]
    public float BaseValue = 1; // 基础值

    [Header("基础值加算项")]
    public float BaseAdditive = 0; // 基础加算（装备、Buff等）

    [Header("百分比加成")]
    public float AdditiveModifier = 0; // 百分比加成（技能、被动加成）

    [Header("乘算修正")]
    public float MultiplicativeModifier = 1; // 乘算修正（暴击、状态倍率）

    [Header("最终加算项")]
    public float FinalAdditive = 0; // 最终加算（结算时额外增加的数值）

    // 最终数值计算公式
    public float Value
    {
        get
        {
            return (BaseValue + BaseAdditive) * (1 + AdditiveModifier) * MultiplicativeModifier + FinalAdditive;
        }
    }
}
