using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System;
using UnityEngine;

[Serializable]
[MemoryPackable]
public partial class Inventory_ModuleData : ModuleData
{
    [ShowInInspector]
    [ReadOnly]
    public Dictionary<string, Inventory_Data> Data = new Dictionary<string, Inventory_Data>();

    public Vector3 PanleRectPosition = Vector3.zero;
    public string InventoryInitName = "";
    public bool BasePanelIsOpen = true;
}