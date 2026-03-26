using System.Collections;
using System.Collections.Generic;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class EquipmentInstance_Bag : EquipmentInstance
{
    public Inventory_Data BagData;


    [Header("对象缓存")]
    [MemoryPackIgnore]
    Inventory BagInventory = new Inventory();

    [Button]
    public override void Equip(Item item = null)
    {
        BagInventory.Data = BagData;
        var controller = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        BagInventory.InitData();
        BagInventory.BindController(controller);
    }

    public override void Update()
    {

    }

    [Button]
    public override void UnEquip(Item item = null)
    {
        BagData = BagInventory.Data;
        BagInventory.UnbindController();
        if (BagInventory.basePanel != null)
            BagInventory.basePanel.Destroy();

    }

}
