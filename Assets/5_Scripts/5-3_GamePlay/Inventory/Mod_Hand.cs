using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Hand : Module
{
    #region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }
    #endregion


    #region 模组参数

    [SerializeReference]
    public List<string> RawData = new List<string>();

    public override void Load()
    {
        ModSaveData.ReadData(ref RawData);
        HandInventory.InitData();
        HandInventory.SwitchUI();
    }

    public override void Save()
    {
        ModSaveData.WriteData(RawData);
    }
    #endregion

    public Inventory_Hand HandInventory;
}
