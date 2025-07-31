using MemoryPack;
using UnityEngine;

[MemoryPackUnion(1, typeof(Ex_ModData))]
[MemoryPackUnion(2, typeof(InventoryModuleData))]
[MemoryPackUnion(3, typeof(Ex_ModData_MemoryPackable))]
[System.Serializable]
[MemoryPackable]
public abstract partial class ModuleData
{
    public string Name;
    [Tooltip("是否正在运行")]
    public bool isRunning = true;
}

