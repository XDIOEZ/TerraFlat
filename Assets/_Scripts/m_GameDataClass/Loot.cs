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

        // 如果没有找到，创建一个新的Loot对象并初始化
        Loot newLoot = new Loot
        {
            lootsName = lootName,
            lootList = new List<LootData>()
        };

        // 将新创建的Loot对象添加到lootList中
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
