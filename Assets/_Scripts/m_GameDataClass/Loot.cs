using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class Loot_List
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
    public Vector2Int lootAmountRange = new Vector2Int(1, 1);

    public int LootAmountMin { get => lootAmountRange.x; }
    public int LootAmountMax { get => lootAmountRange.y; }

    //随机输出一个lootAmount范围内的随机数
    public int GetRandomLootAmount()
    {
        return Random.Range(lootAmountRange.x, lootAmountRange.y+1);
    }
}
