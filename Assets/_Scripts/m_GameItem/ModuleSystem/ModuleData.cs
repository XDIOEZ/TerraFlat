using MemoryPack;
using UnityEngine;

[MemoryPackUnion(1, typeof(Mod_PlantGrow_Data))]
[MemoryPackUnion(2, typeof(StaminaData))]
[System.Serializable]
[MemoryPackable]
public abstract  partial class ModuleData
{
    public string ModuleName;
    [Tooltip("是否正在运行")]
    public bool isRunning = false;
}
