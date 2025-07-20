using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Inventory : Module,IInventory
{
    public InventoryModuleData inventoryModuleData = new InventoryModuleData();
    public override ModuleData Data { get => inventoryModuleData; set => inventoryModuleData = (InventoryModuleData)value; }
    public Inventory _Inventory { get => inventory; set => inventory = value; }
    [Tooltip("容器，用于存放物品")]
    public Inventory inventory;

    public override void Load()
    {
        if (_Inventory == null)
        {
            _Inventory = GetComponent<Inventory>();
        }
        if (inventoryModuleData.Data.Count == 0)
        {
            inventoryModuleData.Data[inventory.Data.Name] = inventory.Data;
        }
        else
        {
            inventory.Data = inventoryModuleData.Data[inventory.Data.Name];
        }

        inventory.Belong_Item = item;

        inventory.DefaultTarget_Inventory = item.Mods[Mod_Text.Hand].GetComponent<IInventory>()._Inventory;
    }

    public override void Save()
    {
        item.Item_Data.ModuleDataDic[Data.Name] = Data;
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

public interface IInventory
{
    Inventory _Inventory { get; set; }
}