using MemoryPack;

/// <summary>石臼独立存档：动态槽位分别保存剩余原料与即时产物，每格容量为 100。</summary>
[MemoryPackable]
public partial class MortarState
{
    public Inventory_Data Bowl; // 原料和产物同库分槽，关闭面板不清空。
    public float HeatingSeconds; // 坩埚模式的累计加热时间；石臼模式保持为 0。
}
