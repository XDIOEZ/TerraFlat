using MemoryPack;
using System.Collections.Generic;
using System;
using System.Linq;

[System.Serializable]
[MemoryPackable]
public partial class Hp
{
    public float value;
    public float maxValue;

    // ��������ַ����б�
    public List<string> Weaknesses;

    [MemoryPackConstructor]
    public Hp(float value)
    {
        this.value = value;
        maxValue = value;
        Weaknesses = new List<string>(); // ��ʼ�������б�
    }

    /// <summary>
    /// ����Ƿ����ָ��������
    /// </summary>
    /// <param name="weakness">��Ҫ��������</param>
    /// <returns>������ڷ��� true�����򷵻� false</returns>
    public bool Check_Weakness(string weakness)
    {
        // ������֤����� weakness Ϊ�ջ���Ч��ֱ�ӷ��� false
        if (string.IsNullOrWhiteSpace(weakness))
        {
            return false;
        }

        // ��� Weaknesses �б����Ƿ����ָ�������㣨���Դ�Сд��
        return Weaknesses.Contains(weakness, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ����Ƿ��������һ��ָ��������
    /// </summary>
    /// <param name="weaknessList">��Ҫ���������б�</param>
    /// <returns>������ٴ���һ������ true�����򷵻� false</returns>
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
