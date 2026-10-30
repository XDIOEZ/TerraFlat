using MemoryPack;
using Newtonsoft.Json;
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
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class LootData
{
    [MemoryPackOrder(0)] public string lootName;
    [MemoryPackIgnore] public Vector2Int lootAmountRange = new Vector2Int(1, 1);
    [MemoryPackInclude, MemoryPackOrder(1)] private int MinAmount { get => lootAmountRange.x; set => lootAmountRange.x = value; }
    [MemoryPackInclude, MemoryPackOrder(2)] private int MaxAmount { get => lootAmountRange.y; set => lootAmountRange.y = value; }
    [MemoryPackIgnore]
    [JsonIgnore]
    public GameObject lootPrefab;

    [MemoryPackIgnore] public int LootAmountMin { get => lootAmountRange.x; }
    [MemoryPackIgnore] public int LootAmountMax { get => lootAmountRange.y; }

    //随机输出一个lootAmount范围内的随机数
    public int GetRandomLootAmount()
    {
        return Random.Range(lootAmountRange.x, lootAmountRange.y+1);
    }
}
//TODO 这是我的战利品基类 结合其重新修改之前的脚本
