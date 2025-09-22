using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class GameValue_float
{
    [Header("������ֵ")]
    public float BaseValue = 1; // ����ֵ

    [Header("����ֵ������")]
    public float BaseAdditive = 0; // �������㣨װ����Buff�ȣ�

    [Header("�ٷֱȼӳ�")]
    public float AdditiveModifier = 0; // �ٷֱȼӳɣ����ܡ������ӳɣ�

    [Header("��������")]
    public float MultiplicativeModifier = 1; // ����������������״̬���ʣ�

    [Header("���ռ�����")]
    public float FinalAdditive = 0; // ���ռ��㣨����ʱ�������ӵ���ֵ��

    // ������ֵ���㹫ʽ
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
    
    // ���ؼӷ������ - ��float���
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
    
    // ���ؼӷ������ - ����һ��GameValue_float���
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
    
    // ���ؼ�������� - ��float���
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
    
    // ���ؼ�������� - ����һ��GameValue_float���
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
    
    // ���س˷������ - ��float���
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
    
    // ���س�������� - ��float���
    public static GameValue_float operator /(GameValue_float gv, float value)
    {
        if (value == 0)
        {
            Debug.LogError("GameValue_float �������");
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
    
    // ������������
    public static bool operator ==(GameValue_float gv1, GameValue_float gv2)
    {
        if (ReferenceEquals(gv1, null) && ReferenceEquals(gv2, null)) return true;
        if (ReferenceEquals(gv1, null) || ReferenceEquals(gv2, null)) return false;
        
        return Mathf.Approximately(gv1.Value, gv2.Value);
    }
    
    // ���ز��������
    public static bool operator !=(GameValue_float gv1, GameValue_float gv2)
    {
        return !(gv1 == gv2);
    }
    
    // ��дEquals����
    public override bool Equals(object obj)
    {
        if (obj is GameValue_float other)
        {
            return this == other;
        }
        return false;
    }
    
    // ��дGetHashCode����
    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }
    
    // ������ʽת��Ϊfloat
    public static implicit operator float(GameValue_float gv)
    {
        return gv.Value;
    }
    
    // ������ʽת��ΪGameValue_float
    public static implicit operator GameValue_float(float value)
    {
        return new GameValue_float(value);
    }
    
    // ��дToString����
    public override string ToString()
    {
        return Value.ToString();
    }
}