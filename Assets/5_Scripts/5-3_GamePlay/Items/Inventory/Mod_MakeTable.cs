using System.Collections.Generic;
using UnityEngine;

/// <summary>工作台内容配置；世界运行逻辑位于 WorkbenchLogic。</summary>
public class Mod_MakeTable : Mod_MachineAuthoring
{
    #region 内容参数
    public Ex_ModData_MemoryPackable ModSaveData = new();
    public override ModuleData _Data { get => ModSaveData; set => ModSaveData = (Ex_ModData_MemoryPackable)value; }
    [SerializeReference] public List<string> RawData = new();
    public Inventory inputInventory;
    public Inventory outputInventory;
    public GameObject InventoryPanel_Prefab;
    public int workbenchLevel = 1;
    public int baseClickCount = 6;
    public int clickReductionPerLevel = 1;
    public int minClickCount = 1;
    #endregion
}
