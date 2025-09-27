using MemoryPack;
using UnityEngine;

[MemoryPackUnion(1, typeof(Ex_ModData))]
[MemoryPackUnion(2, typeof(InventoryModuleData))]
[MemoryPackUnion(3, typeof(Ex_ModData_MemoryPackable))]
[System.Serializable]
[MemoryPackable]
public abstract partial class ModuleData
{
    [Tooltip("ģ���������")]
    public string Name;
    [Tooltip("ģ��ʵ��������")]
    public string ID;
    [Tooltip("�Ƿ���������")]
    public bool isRunning = true;
    public ModuleType Type;

    public void DataUpdate()
    {

    }
    
    public override string ToString()
    {
        return $"ģ������:(Name: {Name}, ID: {ID}, Type: {Type}, isRunning: {isRunning})";
    }
}

public enum ModuleType
{
    None,
    Equipment,
}