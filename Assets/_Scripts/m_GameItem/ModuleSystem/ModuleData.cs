using MemoryPack;
using UnityEngine;

[MemoryPackUnion(1, typeof(Mod_PlantGrow_Data))]
[MemoryPackUnion(2, typeof(StaminaData))]
[System.Serializable]
[MemoryPackable]
public abstract  partial class ModuleData
{
    public string ModuleName;
    [Tooltip("�Ƿ���������")]
    public bool isRunning = false;
}
