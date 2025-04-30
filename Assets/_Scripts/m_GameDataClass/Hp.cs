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
    // 添加弱点字符串列表
    public List<string> Weaknesses;

    /*public Buffs buff;*/

    [MemoryPackConstructor]
    public Hp(float value)
    {
        this.value = value;
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
[System.Serializable]
[MemoryPackable]
public partial class Buffs
{
    public List<Buff> buffs;

    //查找对应名字的buff
    public Buff FindBuff(string name)
    {
        if (buffs == null || buffs.Count == 0)
        {
            return null;
        }

        return buffs.Find(b => b.name == name);
    }

    //添加buff
    public void AddBuff(Buff buff)
    {
        if (buffs == null)
        {
            buffs = new List<Buff>();
        }
    buffs.Add(buff);
    }

    //移除对应名字的buff
    public void RemoveBuff(string name)
    {
        if (buffs == null || buffs.Count == 0)
        {
            return;
        }

        buffs.RemoveAll(b => b.name == name);
    }
    //移除所有buff
    public void RemoveAllBuff()
    {
        if (buffs == null || buffs.Count == 0)
        {
            return;
        }

        buffs.Clear();
    }
    //修改对应名字的buff
    public void ModifyBuff(string name, float value)
    {
        Buff buff = FindBuff(name);
        if (buff!= null)
        {
            buff.value = value;
        }
    }

    //输出所有buff数值总和和
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

