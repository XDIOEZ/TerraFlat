using MemoryPack;
using System.Collections.Generic;
using System;
using System.Linq;
using NaughtyAttributes;

[System.Serializable]
[MemoryPackable]
public partial class Hp
{
    public float value;
    public float maxValue;
    public float HpChangSpeed;
    // ��������ַ����б�
    public List<string> Weaknesses;

    /*public Buffs buff;*/

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
[System.Serializable]
[MemoryPackable]
public partial class Buffs
{
    public List<Buff> buffs;

    //���Ҷ�Ӧ���ֵ�buff
    public Buff FindBuff(string name)
    {
        if (buffs == null || buffs.Count == 0)
        {
            return null;
        }

        return buffs.Find(b => b.name == name);
    }

    //���buff
    public void AddBuff(Buff buff)
    {
        if (buffs == null)
        {
            buffs = new List<Buff>();
        }
    buffs.Add(buff);
    }

    //�Ƴ���Ӧ���ֵ�buff
    public void RemoveBuff(string name)
    {
        if (buffs == null || buffs.Count == 0)
        {
            return;
        }

        buffs.RemoveAll(b => b.name == name);
    }
    //�Ƴ�����buff
    public void RemoveAllBuff()
    {
        if (buffs == null || buffs.Count == 0)
        {
            return;
        }

        buffs.Clear();
    }
    //�޸Ķ�Ӧ���ֵ�buff
    public void ModifyBuff(string name, float value)
    {
        Buff buff = FindBuff(name);
        if (buff!= null)
        {
            buff.value = value;
        }
    }

    //�������buff��ֵ�ܺͺ�
    public float AllBuffData()
    {
        float allValue = 0;
        foreach (var buff in buffs)
        {
            allValue += buff.value;
        }
        return allValue;
    }

}

[System.Serializable]
[MemoryPackable]
public partial class Buff
{
    public string name;
    public float value;
}

