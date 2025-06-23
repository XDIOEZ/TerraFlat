using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class ModuleData
{
    public string ModuleName;
    [Tooltip("是否正在运行")]
    public bool isRunning = false;
}
