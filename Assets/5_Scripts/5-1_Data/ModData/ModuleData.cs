using MemoryPack;
using UnityEngine;

[MemoryPackUnion(1, typeof(Ex_ModData))]
[MemoryPackUnion(2, typeof(Inventory_ModuleData))]
[MemoryPackUnion(3, typeof(Ex_ModData_MemoryPackable))]
[MemoryPackUnion(4, typeof(ModData_FoodData))]
[MemoryPackUnion(5, typeof(BerryBushModuleData))]
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
/// 浆果丛的权威运行时状态。
/// 自然浆果丛由确定性生成得到初始库存，之后的当前库存与生产计时必须随 ItemData 一起进入区块生态差量；
/// IsInitialized 用于区分“尚未完成自然初始化”的新实例和“库存确实为 0”的已持久化实例。
/// </summary>
[System.Serializable]
[MemoryPackable]
public partial class BerryBushModuleData : ModuleData
{
    /// <summary>当前可采摘浆果数量。</summary>
    public int CurrentBerryCount;

    /// <summary>距离下一批浆果成熟的累计秒数。</summary>
    public float ProductionTimer;

    /// <summary>是否已经写入过自然初始库存或运行时状态。</summary>
    public bool IsInitialized;
}
