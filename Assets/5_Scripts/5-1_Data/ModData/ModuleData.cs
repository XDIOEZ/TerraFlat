using MemoryPack;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Serialization;

[MemoryPackUnion(1, typeof(Ex_ModData))]
[MemoryPackUnion(2, typeof(Inventory_ModuleData))]
[MemoryPackUnion(3, typeof(Ex_ModData_MemoryPackable))]
[MemoryPackUnion(4, typeof(ModData_FoodData))]
[MemoryPackUnion(5, typeof(CollectableModuleData))]
[System.Serializable]
[MemoryPackable]
public abstract partial class ModuleData
{
    [FormerlySerializedAs("Name")]
    [Tooltip("模块在当前 Item 内的稳定实例名；同时也是 ModuleDataDic 的唯一键。")]
    public string StableName;

    [FormerlySerializedAs("ID")]
    [Tooltip("模块能力 ID；同一能力允许由不同 Prefab 实现。")]
    public string ModuleId;

    [FormerlySerializedAs("isRunning")]
    [Tooltip("模块是否启用；由 Module 框架统一控制运行态与 Tick。")]
    public bool Enabled = true;
    public ModuleType Type;

    // 旧代码别名仅转发到唯一字段，不再形成第二套模块身份。
    [MemoryPackIgnore, JsonIgnore]
    public string Name { get => StableName; set => StableName = value; }

    [MemoryPackIgnore, JsonIgnore]
    public string ID { get => ModuleId; set => ModuleId = value; }

    [MemoryPackIgnore, JsonIgnore]
    public bool isRunning { get => Enabled; set => Enabled = value; }

    /// <summary>
    /// 模块数据更新入口，deltaTime 由外部调度层传入。
    /// </summary>
    public virtual void DataUpdate(float deltaTime)
    {

    }
    
    public override string ToString()
    {
        return $"模块数据:(StableName: {StableName}, ModuleId: {ModuleId}, Type: {Type}, Enabled: {Enabled})";
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
