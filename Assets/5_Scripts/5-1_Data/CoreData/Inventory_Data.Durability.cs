public partial class Inventory_Data
{
    #region 耐久库存事务
    /// <summary>磨损只认原数据引用，不用同 GUID 替身代扣，也不为每次命中分配查询委托。</summary>
    public ItemSlot FindExactItemSlot(ItemData expected)
    {
        if (expected == null || itemSlots == null) return null;
        for (int i = 0; i < itemSlots.Count; i++)
            if (ReferenceEquals(itemSlots[i]?.itemData, expected)) return itemSlots[i];
        return null;
    }

    /// <summary>只修改仍在本库存的原实例，一次发布磨损、减件和清槽后的最终状态。</summary>
    public bool TryConsumeDurability(ItemData expected, float wear, out bool brokeUnit)
    {
        brokeUnit = false;
        if (itemSlots == null || !ItemDurabilityRules.CanConsume(expected, wear)) return false;
        ItemSlot slot = FindExactItemSlot(expected);
        if (slot == null) return false;
        EnsureRuntimeEvents();
        Event_OnBeforeDataChanged.Invoke(slot);
        if (!ReferenceEquals(slot.itemData, expected) ||
            !ItemDurabilityRules.TryConsume(expected, wear, out brokeUnit)) return false;
        if (expected.Stack.Amount <= 0f) slot.itemData = null;
        slot.RefreshUI();
        Event_RefreshUI.Invoke(slot.Index);
        NotifyItemDataChanged(slot);
        return true;
    }
    #endregion
}
