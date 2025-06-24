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
            return (BaseValue + BaseAdditive) * (1 + AdditiveModifier) * MultiplicativeModifier + FinalAdditive;
        }
    }
}
