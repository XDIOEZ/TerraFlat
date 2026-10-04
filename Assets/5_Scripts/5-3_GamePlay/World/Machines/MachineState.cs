using System;
using System.Collections.Generic;
using MemoryPack;

/// <summary>加工模块的唯一持久状态；库存与进度随物品保存，UI 不保存第二份数据。</summary>
[Serializable, MemoryPackable]
public partial class RecipeProcessingState
{
    #region 加工状态
    public Inventory_Data Input = new(new List<ItemSlot> { new(0) }, "输入");
    public Inventory_Data Output = new(new List<ItemSlot> { new(0) }, "输出");
    public string RecipeId = "";
    public float Progress;
    #endregion
}

/// <summary>已放置机械节点的状态；RotationQuarterTurns 为逆时针九十度步数，只在世界本体写入，背包朝向使用临时字段。</summary>
[Serializable, MemoryPackable]
public partial class MachineState
{
    #region 节点状态
    public int RotationQuarterTurns;
    public int ConveyorMode; // 0 自动连接，1~12 固定直线或拐角的输入输出朝向。
    public bool Engaged = true;
    public int RatioIndex = 1;
    public float ManualSeconds;
    public float Hp = -1f; // -1 表示按物品定义初始化建造耐久。
    public float ElectricalStoredJoules; // 电池当前储能；非电池保持 0。
    public RecipeProcessingState Processing = new();
    public bool Generated; // 世界基线设施拆除后由机器存档保留删除标记。
    #endregion
}

/// <summary>整网冷存档，按世界隔离并保存完整 ItemData；不依赖 ChunkView 或节点是否可见。</summary>
[Serializable, MemoryPackable]
public partial class MachineArchive
{
    public int Version = 1;
    public Dictionary<string, List<ItemData>> Worlds = new(StringComparer.Ordinal);
    public Dictionary<string, HashSet<int>> RemovedGenerated = new(StringComparer.Ordinal);
}

public partial class GameSaveData
{
    /// <summary>独立存档封装载荷，不改变已有核心 GameSaveData 的 MemoryPack 布局。</summary>
    [MemoryPackIgnore] public MachineArchive Mechanical = new();
}
