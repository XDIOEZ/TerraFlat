using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System;
using UnityEngine;

[Serializable]
[MemoryPackable]
public partial class InventoryModuleData : ModuleData
{
    [ShowInInspector]
    [ReadOnly]
    public Dictionary<string, Inventory_Data> Data = new Dictionary<string, Inventory_Data>();
    public Vector3 PanleRectPosition = Vector3.zero;//TODO 我在这里添加了一个Vector3变量，用于保存面板的位置
    public string InventoryInitName = "";
    public bool BasePanelIsOpen = true;
}