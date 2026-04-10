using MemoryPack;
using UnityEngine;

[MemoryPackUnion(1, typeof(Ex_ModData))]
[MemoryPackUnion(2, typeof(Inventory_ModuleData))]
[MemoryPackUnion(3, typeof(Ex_ModData_MemoryPackable))]
[MemoryPackUnion(4, typeof(ModData_FoodData))]
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

    [MemoryPackIgnore]
    [Tooltip("运行时所属物品数据（由外部调度注入）")]
    public ItemData RuntimeOwnerItemData;

    [MemoryPackIgnore]
    [Tooltip("运行时所属容器数据（由外部调度注入）")]
    public Inventory_Data RuntimeOwnerInventoryData;

    [MemoryPackIgnore]
    [Tooltip("运行时所属槽位（由外部调度注入）")]
    public ItemSlot RuntimeOwnerSlot;

    [MemoryPackIgnore]
    [Tooltip("运行时所属槽位索引（由外部调度注入）")]
    public int RuntimeOwnerSlotIndex = -1;

    /// <summary>
    /// 模块数据更新入口，deltaTime 由外部调度层传入。
    /// </summary>
    public virtual void DataUpdate(float deltaTime)
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