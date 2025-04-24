using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class List_Loot
{
    public List<Loot> lootList;

    public Loot GetLoot(string lootName)
    {
        foreach (Loot loot in lootList)
        {
            if (loot.lootsName == lootName)
            {
                return loot;
            }
        }

        // ���û���ҵ�������һ���µ�Loot���󲢳�ʼ��
        Loot newLoot = new Loot
        {
            lootsName = lootName,
            lootList = new List<LootData>()
        };

        // ���´�����Loot������ӵ�lootList��
        lootList.Add(newLoot);

        return newLoot;
    }

}

[System.Serializable]
[MemoryPackable]
public partial class Loot
{
    public string lootsName;
    public List<LootData> lootList;
}

[System.Serializable]
[MemoryPackable]
public partial class LootData
{
    public string lootName;
    public int lootAmount;
}
