using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class ModuleData
{
    public string ModuleName;
    [Tooltip("�Ƿ���������")]
    public bool isRunning = false;
}
