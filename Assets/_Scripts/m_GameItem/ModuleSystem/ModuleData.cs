using MemoryPack;
using UnityEngine;

[MemoryPackUnion(1, typeof(Mod_PlantGrow_Data))]
[MemoryPackUnion(2, typeof(Ex_ModData))]
[MemoryPackUnion(3, typeof(InventoryModuleData))]
[MemoryPackUnion(4, typeof(Ex_ModData_MemoryPack))]
[System.Serializable]
[MemoryPackable]
public abstract partial class ModuleData
{
    public string Name;
    [Tooltip("是否正在运行")]
    public bool isRunning = false;
}

