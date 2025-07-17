using MemoryPack;
using UnityEngine;

[MemoryPackUnion(1, typeof(Mod_PlantGrow_Data))]
[MemoryPackUnion(2, typeof(StaminaData))]
[MemoryPackUnion(3, typeof(Ex_ModData))]
[System.Serializable]
[MemoryPackable]
public abstract  partial class ModuleData
{
    public string Name;
    [Tooltip("是否正在运行")]
    public bool isRunning = false;


}

