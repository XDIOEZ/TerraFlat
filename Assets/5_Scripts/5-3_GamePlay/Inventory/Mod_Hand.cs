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

        // Inventory_Hand 是所有独立容器（合成、熔炉、箱子等）反查本地快捷栏的
        // 通用入口；必须保留所属玩家，不能只依赖各窗口临时设置 DefaultTarget_Inventory。
        HandInventory.item = item;
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
