using MemoryPack;
using UnityEngine;

[MemoryPackUnion(1, typeof(Ex_ModData))]
[MemoryPackUnion(2, typeof(InventoryModuleData))]
[MemoryPackUnion(3, typeof(Ex_ModData_MemoryPackable))]
[System.Serializable]
[MemoryPackable]
public abstract partial class ModuleData
{
    [Tooltip("模块独立名称")]
    public string Name;
    [Tooltip("模块实例化名称")]
    public string ID;
    [Tooltip("是否正在运行")]
    public bool isRunning = true;
    public ModuleType Type;

    public void DataUpdate()
    {

    }
    
    public override string ToString()
    {
        return $"模块数据:(Name: {Name}, ID: {ID}, Type: {Type}, isRunning: {isRunning})";
    }
}

public enum ModuleType
{
    None,
    Equipment,
}