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

        inventory.DefaultTarget_Inventory = item.Mods[ModText.Hand].GetComponent<IInventory>()._Inventory;

        /*     if(_Inventory.DefaultTarget_Inventory != null)
             _Inventory.OnItemClick(_Inventory.Data.Index);*/
        _Inventory.Init();
    }

    public override void Save()
    {
        item.Item_Data.ModuleDataDic[_Data.Name] = _Data;
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