using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Treasure_Inventory : Mod_Inventory
{
    public List<Inventoryinit> inventoryInitList;

    public override void Load()
    {
        //TODO ��inventoryInitList�����ѡһ��
        string inventoryName = inventoryInitList[Random.Range(0, inventoryInitList.Count)].name;
        Data.InventoryInitName = inventoryName;
        base.Load();
    }
} 
