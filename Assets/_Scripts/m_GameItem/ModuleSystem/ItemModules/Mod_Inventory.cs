using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Inventory : Module,IInventory
{
    public InventoryModuleData inventoryModuleData = new InventoryModuleData();
    public override ModuleData _Data { get => inventoryModuleData; set => inventoryModuleData = (InventoryModuleData)value; }
    public Inventory _Inventory { get => inventory; set => inventory = value; }
    [Tooltip("容器，用于存放物品")]
    public Inventory inventory;
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Bag;
        }
    }

    public override void Load()
    {

        if (_Inventory == null)
        {
            _Inventory = GetComponent<Inventory>();
        }
        if (inventoryModuleData.Data.Count == 0)
        {
            inventoryModuleData.Data[_Data.Name] = inventory.Data;
        }
        else
        {
            inventory.Data = inventoryModuleData.Data[_Data.Name];
        }

        if(Item_Data.ModuleDataDic.ContainsKey(_Data.Name))
        _Data = Item_Data.ModuleDataDic[_Data.Name];

        inventory.Belong_Item = item;

        if (item.itemMods.GetMod_ByID(ModText.Hand))
        {
            inventory.DefaultTarget_Inventory =
                          item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>()._Inventory;
        }
        else
        {
            inventory.DefaultTarget_Inventory = Inventory_Hand.PlayerHand;
        //   Debug.Log("Mod_Inventory: " + item.name + " 没有找到Mod_Hand");
        }

        _Inventory.Init();
    }


    [Button]
    public override void Save()
    {
        Item_Data.ModuleDataDic[_Data.Name] = inventoryModuleData;
    }

}

public interface IInventory
{
    Inventory _Inventory { get; set; }
}