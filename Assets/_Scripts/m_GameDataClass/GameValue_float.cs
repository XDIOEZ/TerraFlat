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
            return ((BaseValue + BaseAdditive) * (1 + AdditiveModifier) * MultiplicativeModifier) + FinalAdditive;
        }
    }
    
    [MemoryPackConstructor]
    public GameValue_float(float BaseValue)
    {
        this.BaseValue = BaseValue;
    }

    public GameValue_float()
    {
    }
    
    // 重载加法运算符 - 与float相加
    public static GameValue_float operator +(GameValue_float gv, float value)
    {
        GameValue_float result = new GameValue_float
        {
            BaseValue = gv.BaseValue,
            BaseAdditive = gv.BaseAdditive,
            AdditiveModifier = gv.AdditiveModifier,
            MultiplicativeModifier = gv.MultiplicativeModifier,
            FinalAdditive = gv.FinalAdditive + value
        };
        return result;
    }
    
    // 重载加法运算符 - 与另一个GameValue_float相加
    public static GameValue_float operator +(GameValue_float gv1, GameValue_float gv2)
    {
        GameValue_float result = new GameValue_float
        {
            BaseValue = gv1.BaseValue + gv2.BaseValue,
            BaseAdditive = gv1.BaseAdditive + gv2.BaseAdditive,
            AdditiveModifier = gv1.AdditiveModifier + gv2.AdditiveModifier,
            MultiplicativeModifier = gv1.MultiplicativeModifier * gv2.MultiplicativeModifier,
            FinalAdditive = gv1.FinalAdditive + gv2.FinalAdditive
        };
        return result;
    }
    
    // 重载减法运算符 - 与float相减
    public static GameValue_float operator -(GameValue_float gv, float value)
    {
        GameValue_float result = new GameValue_float
        {
            BaseValue = gv.BaseValue,
            BaseAdditive = gv.BaseAdditive,
            AdditiveModifier = gv.AdditiveModifier,
            MultiplicativeModifier = gv.MultiplicativeModifier,
            FinalAdditive = gv.FinalAdditive - value
        };
        return result;
    }
    
    // 重载减法运算符 - 与另一个GameValue_float相减
    public static GameValue_float operator -(GameValue_float gv1, GameValue_float gv2)
    {
        GameValue_float result = new GameValue_float
        {
            BaseValue = gv1.BaseValue - gv2.BaseValue,
            BaseAdditive = gv1.BaseAdditive - gv2.BaseAdditive,
            AdditiveModifier = gv1.AdditiveModifier - gv2.AdditiveModifier,
            MultiplicativeModifier = gv1.MultiplicativeModifier / gv2.MultiplicativeModifier,
            FinalAdditive = gv1.FinalAdditive - gv2.FinalAdditive
        };
        return result;
    }
    
    // 重载乘法运算符 - 与float相乘
    public static GameValue_float operator *(GameValue_float gv, float value)
    {
        GameValue_float result = new GameValue_float
        {
            BaseValue = gv.BaseValue * value,
            BaseAdditive = gv.BaseAdditive * value,
            AdditiveModifier = gv.AdditiveModifier * value,
            MultiplicativeModifier = gv.MultiplicativeModifier * value,
            FinalAdditive = gv.FinalAdditive * value
        };
        return result;
    }
    
    // 重载除法运算符 - 与float相除
    public static GameValue_float operator /(GameValue_float gv, float value)
    {
        if (value == 0)
        {
            Debug.LogError("GameValue_float 除零错误");
            return gv;
        }
        
        GameValue_float result = new GameValue_float
        {
            BaseValue = gv.BaseValue / value,
            BaseAdditive = gv.BaseAdditive / value,
            AdditiveModifier = gv.AdditiveModifier / value,
            MultiplicativeModifier = gv.MultiplicativeModifier / value,
            FinalAdditive = gv.FinalAdditive / value
        };
        return result;
    }
    
    // 重载相等运算符
    public static bool operator ==(GameValue_float gv1, GameValue_float gv2)
    {
        if (ReferenceEquals(gv1, null) && ReferenceEquals(gv2, null)) return true;
        if (ReferenceEquals(gv1, null) || ReferenceEquals(gv2, null)) return false;
        
        return Mathf.Approximately(gv1.Value, gv2.Value);
    }
    
    // 重载不等运算符
    public static bool operator !=(GameValue_float gv1, GameValue_float gv2)
    {
        return !(gv1 == gv2);
    }
    
    // 重写Equals方法
    public override bool Equals(object obj)
    {
        if (obj is GameValue_float other)
        {
            return this == other;
        }
        return false;
    }
    
    // 重写GetHashCode方法
    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }
    
    // 重载隐式转换为float
    public static implicit operator float(GameValue_float gv)
    {
        return gv.Value;
    }
    
    // 重载隐式转换为GameValue_float
    public static implicit operator GameValue_float(float value)
    {
        return new GameValue_float(value);
    }
    
    // 重写ToString方法
    public override string ToString()
    {
        return Value.ToString();
    }
}