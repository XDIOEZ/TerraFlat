using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Module_Equipment_Store : Module
{
    #region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }
    #endregion


    #region 模组参数

    [SerializeReference]
    public List<EquipmentInstance> equipmentInstances = new List<EquipmentInstance>();

    public override void Load()
    {
        ModSaveData.ReadData(ref equipmentInstances);
    }

    public override void Save()
    {
        ModSaveData.WriteData(equipmentInstances);
    }
    #endregion

    public override void Awake()
    {
        _Data.ID = ModText.Equipment_Store;
    }

    public List<EquipmentInstance> GetAllEquipmentInstances()
    {
        return equipmentInstances;
    }

}