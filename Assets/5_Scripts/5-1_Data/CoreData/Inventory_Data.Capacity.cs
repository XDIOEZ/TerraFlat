using FastCloner.Code;
using MemoryPack;
using Newtonsoft.Json;

/// <summary>库存的自动扩容策略：每次占用末尾格后补一个容量为 100 的空槽，不预分配固定上限。</summary>
public partial class Inventory_Data
{
    #region 动态槽位容量

    /// <summary>新建普通槽的容量；无限背包只扩展格数，不改变单格堆叠规则。</summary>
    public const float DefaultSlotVolume = 100f;

    /// <summary>运行时容量策略；由所属玩家的独立状态恢复，不加入 MemoryPack 库存布局。</summary>
    [MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    public bool HasUnlimitedSlots { get; private set; }

    /// <summary>启用或关闭自动扩容；关闭时保留现有槽位与物品。</summary>
    public void SetUnlimitedSlots(bool enabled)
    {
        HasUnlimitedSlots = enabled;
        EnsureSpareSlot();
    }

    /// <summary>无限库存末尾始终预留一个空槽，已有空槽不重复扩容。</summary>
    public void EnsureSpareSlot()
    {
        if (!HasUnlimitedSlots ||
            (itemSlots.Count > 0 && itemSlots[itemSlots.Count - 1].itemData == null))
            return;

        itemSlots.Add(new ItemSlot(itemSlots.Count) { SlotMaxVolume = DefaultSlotVolume });
    }

    /// <summary>数据事务提交后先维护容量，再通知订阅者；拾取、拖放、整理共用同一边界。</summary>
    private void NotifyItemDataChanged(ItemSlot slot)
    {
        EnsureSpareSlot();
        Event_OnDataChanged.Invoke(slot);
    }

    #endregion
}
