using MemoryPack;

[System.Serializable]
[MemoryPackable]
public partial class GameValue_float
{
    public float BaseValue = 1;                      // ����ֵ
    public float BaseAdditive = 0;               // ���������װ����Buff�ȣ�
    public float AdditiveModifier = 0;           // �ٷֱȼ�������ܡ������ӳɵȣ�
    public float MultiplicativeModifier = 1;     // ��������������������״̬���ʣ�
    public float FinalAdditive = 0;              // ���ռ��������ʱ�������ӵ���ֵ��

    // ������ֵ���㹫ʽ
    public float Value
    {
        get
        {
            return (BaseValue + BaseAdditive) * (1 + AdditiveModifier) * MultiplicativeModifier + FinalAdditive;
        }
    }
}
