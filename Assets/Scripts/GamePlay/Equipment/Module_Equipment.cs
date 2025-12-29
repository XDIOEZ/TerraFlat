using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Module_Equipment : Module
{
    #region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }
    #endregion
    #region 模组参数
    [SerializeReference]
    public List<EquipmentInstance> equipmentInstances = new List<EquipmentInstance>();

    public Mod_Inventory Equipment_inventory;

    #endregion

    #region 生命周期

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Equipment_Module;
        }
    }

    public override void Load()
    {
        if (item == null)
        {
            Debug.LogError($"Module_Equipment.Load: 挂载在 {name} 上的 Module_Equipment 的 item 为空");
            return;
        }

        Equipment_inventory = (Mod_Inventory)item.FindInventoryModuleByName(ModText.Equipment);
        if (Equipment_inventory == null)
        {
            Debug.LogError($"Module_Equipment.Load: 在物品 {item.name} 上未找到 Mod_Inventory 模块(ID={ModText.Equipment})");
            return;
        }

        if (Equipment_inventory.inventory == null)
        {
            Debug.LogError($"Module_Equipment.Load: Mod_Inventory.inventory 为空 (物品 {item.name})");
            return;
        }

        if (Equipment_inventory.inventory.Data == null)
        {
            Debug.LogError($"Module_Equipment.Load: Mod_Inventory.inventory.Data 为空 (物品 {item.name})");
            return;
        }

        Equipment_inventory.inventory.Data.OnDataChanged += UpdateEquipment;
        UpdateEquipment();

        if (ModSaveData == null)
        {
            Debug.LogError($"Module_Equipment.Load: ModSaveData 为空，无法读取装备数据 (物品 {item.name})");
            return;
        }

        ModSaveData.ReadData(ref equipmentInstances);
        Init();
    }

    void UpdateEquipment()
    {

    }

    public void Init()
    {
        if (item == null)
        {
            Debug.LogError($"Module_Equipment.Init: item 为空，无法初始化装备实例 (模块 {name})");
            return;
        }
        if (item.itemData.Stack.CanBePickedUp != true)
            EquipAll();
    }

    public void EquipAll()
    {
        if (item == null)
        {
            Debug.LogError($"Module_Equipment.EquipAll: item 为空，无法装备 (模块 {name})");
            return;
        }

        foreach (var equipment in equipmentInstances)
        {
            equipment.Equip(item);
        }
    }

    public void UnEquipAll()
    {
        if (item == null)
        {
            Debug.LogError($"Module_Equipment.UnEquipAll: item 为空，无法卸下装备 (模块 {name})");
            return;
        }

        foreach (var equipment in equipmentInstances)
        {
            equipment.UnEquip(item);
        }
    }
    public override void ModUpdate(float deltaTime)
    {
        foreach (var item in equipmentInstances)
        {
            item.Update();
        }
    }
    public override void Save()
    {
        ModSaveData.WriteData(equipmentInstances);

        UnEquipAll();
    }
    public override void Act()
    {
        base.Act();
    }
    #endregion

}
