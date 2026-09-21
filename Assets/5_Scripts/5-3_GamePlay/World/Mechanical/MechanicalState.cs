using System;
using System.Collections.Generic;
using MemoryPack;

/// <summary>加工模块的唯一持久状态；库存与进度随物品保存，UI 不保存第二份数据。</summary>
[Serializable, MemoryPackable]
public partial class MechanicalProcessingState
{
    #region 加工状态
    public Inventory_Data Input = new(new List<ItemSlot> { new(0) }, "输入");
    public Inventory_Data Output = new(new List<ItemSlot> { new(0) }, "输出");
    public string RecipeId = "";
    public float Progress;
    #endregion
}

/// <summary>已放置机械节点的状态；Vertical 只在世界本体写入，背包朝向永远使用临时字段。</summary>
[Serializable, MemoryPackable]
public partial class MechanicalNodeState
{
    #region 节点状态
    public bool Vertical;
    public bool Engaged = true;
    public int RatioIndex = 1;
    public float ManualSeconds;
    public bool WaterSupported;
    public MechanicalProcessingState Processing = new();
    #endregion
}

/// <summary>整网冷存档，按世界隔离并保存完整 ItemData；不依赖 ChunkView 或节点是否可见。</summary>
[Serializable, MemoryPackable]
public partial class MechanicalArchive
{
    public int Version = 1;
    public Dictionary<string, List<ItemData>> Worlds = new(StringComparer.Ordinal);
}

public partial class GameSaveData
{
    /// <summary>独立存档封装载荷，不改变已有核心 GameSaveData 的 MemoryPack 布局。</summary>
    [MemoryPackIgnore] public MechanicalArchive Mechanical = new();
}
