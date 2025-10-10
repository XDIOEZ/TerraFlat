using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LootItemMaker : MonoBehaviour
{
    public Loot_List loots;

    public void Start()
    {
        loots = GetComponentInParent<ILoot>().Loots;
        GetComponentInParent<IHealth>().OnDeath += LootItemMaker_Death;
    }

    public void LootItemMaker_Death()
    {
        loots.GetLoot("DeathLoot").lootList.ForEach(x => Spawn(x));
    }

    public void Spawn(LootData lootData)
    {
       Item item = ItemMgr.Instance.InstantiateItem(lootData.lootName);
        item.transform.position = transform.position;
       item.itemData.Stack.Amount = lootData.LootAmountMin;
    }
}
