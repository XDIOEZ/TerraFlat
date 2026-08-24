using MemoryPack;
using UnityEngine;

[MemoryPackUnion(1, typeof(Ex_ModData))]
[MemoryPackUnion(2, typeof(Inventory_ModuleData))]
[MemoryPackUnion(3, typeof(Ex_ModData_MemoryPackable))]
[MemoryPackUnion(4, typeof(ModData_FoodData))]
[MemoryPackUnion(5, typeof(CollectableModuleData))]
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

/// <summary>
/// 通用采集模块的权威库存状态。
/// 自然资源由确定性生成得到初始库存，之后的库存随 ItemData 进入区块生态差量；
/// IsInitialized 用于区分“尚未完成自然初始化”和“库存确实为 0”。
/// </summary>
[System.Serializable]
[MemoryPackable]
public partial class CollectableModuleData : ModuleData
{
    /// <summary>当前可采集库存。</summary>
    public int CurrentStock;

    /// <summary>是否已经完成自然初始库存写入。</summary>
    public bool IsInitialized;
}
