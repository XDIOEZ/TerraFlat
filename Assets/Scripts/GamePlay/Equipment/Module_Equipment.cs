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

    public Mod_Inventory inventory;

    #endregion

    #region 生命周期

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Grow;
        }
    }

    public override void Load()
    {
        inventory = item.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Equipment);
        inventory.inventory.Data.OnDataChanged += UpdateEquipment;
        UpdateEquipment();
        ModSaveData.ReadData(ref equipmentInstances);
        Init();
    }

    void UpdateEquipment()
    {
        
    }

    public  void Init()
    {
        foreach (var item in equipmentInstances)
        {
            item.Equip();
        }
    }

    public void UnEquipAll()
    {
        foreach (var item in equipmentInstances)
        {
            item.UnEquip();
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
