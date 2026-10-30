using MemoryPack;

/// <summary>石臼独立存档：动态槽位分别保存剩余原料与即时产物，每格容量为 100。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class MortarState
{
    [MemoryPackIgnore] public Inventory_Data Bowl; // 原料和产物同库分槽，关闭面板不清空。
    [MemoryPackInclude, MemoryPackOrder(0)] private StandaloneInventorySaveSnapshot BowlSnapshot
    { get => StandaloneInventorySaveSnapshot.Capture(Bowl); set => Bowl = value?.Restore(); }
    [MemoryPackOrder(1)] public string ProcessingRecipeId; // 当前累计手动加工进度所属配方。
    [MemoryPackOrder(2)] public int ProcessingStep; // 已完成的有效手动加工次数，达到配方要求后才结算。
}
