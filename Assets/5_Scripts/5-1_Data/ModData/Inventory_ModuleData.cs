using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System;
using FastCloner.Code;
using Newtonsoft.Json;
using UnityEngine;

[Serializable]
[MemoryPackable]
public partial class Inventory_ModuleData : ModuleData
{
    [ShowInInspector]
    [ReadOnly]
    public Dictionary<string, Inventory_Data> Data = new Dictionary<string, Inventory_Data>();

    [field: NonSerialized, MemoryPackIgnore, JsonIgnore, FastClonerIgnore]
    public event Action<Inventory_ModuleData> InventoryMapStructureChanged;

    /// <summary>库存键或库存引用改变后通知依赖嵌套槽的运行时观察者。</summary>
    public void NotifyInventoryMapStructureChanged() => InventoryMapStructureChanged?.Invoke(this);

    public Vector3 PanleRectPosition = Vector3.zero;
    public string InventoryInitName = "";
    public bool BasePanelIsOpen = true;

    // 玩家背包的实例容量由模块保存，空值使用当前默认值。
    [MemoryPackIgnore, JsonIgnore] public float? MaxCarryWeight;
    [MemoryPackIgnore, JsonIgnore] public float? MaxCarryVolume;
}
