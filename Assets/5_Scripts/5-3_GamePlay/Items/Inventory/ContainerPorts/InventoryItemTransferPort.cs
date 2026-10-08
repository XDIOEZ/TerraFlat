using System;
using System.Collections.Generic;
using UnityEngine;
using FlatWorld.Networking;

/// <summary>库存端口只调用正式库存事务，限制槽位、堆叠身份与承载容量。</summary>
public sealed class InventoryItemTransferPort : ContainerPortBase, IItemTransferPort
{
    #region 库存适配
    private readonly Inventory inventory;
    private readonly Action changed;
    private readonly bool allowDraggedSource;
    private readonly InventoryContainerNotifications notifications = new();
    public InventoryItemTransferPort(Inventory inventory, Item owner, ContainerPortConfiguration configuration,
        Func<bool> valid = null, string ownerId = null, Vector2 position = default, int sceneHandle = 0, Action changed = null, bool allowDraggedSource = false)
        : base(ownerId ?? $"item:{owner?.itemData?.Guid}", configuration.Id, configuration, valid, owner, position, sceneHandle)
    { this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory)); this.changed = changed; this.allowDraggedSource = allowDraggedSource && configuration.Access == ContainerAccessKind.Manual; }
    public override object StorageIdentity => inventory.Data;
    public override long StateVersion
    {
        get
        {
            long version = inventory.Data.ContentStructureRevision;
            foreach (ItemSlot slot in inventory.Data.itemSlots)
                version = unchecked(version * 397 ^ (slot?.itemData?.Guid ?? 0) ^ (slot?.itemData?.Stack?.Amount.GetHashCode() ?? 0));
            return version;
        }
    }
    private bool AllowedSlot(int index)
    { if (Configuration.SlotIndices == null || Configuration.SlotIndices.Length == 0) return true; foreach (int allowed in Configuration.SlotIndices) if (index == allowed) return true; return false; }
    public ItemData PeekItem(string itemId = null, string tag = null)
    {
        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            ItemData data = inventory.Data.itemSlots[i]?.itemData;
            if (!AllowedSlot(i) || !allowDraggedSource && inventory.IsSlotBeingDragged(i) || data?.Stack == null || data.Stack.Amount < 1f ||
                !Configuration.AllowsItem(data) || itemId != null && data.IDName != itemId || tag != null && !ContainerPortConfiguration.HasTag(data, tag)) continue;
            return data;
        }
        return null;
    }
    public int GetReceivableItems(ItemData item, int requested)
    {
        if (!Configuration.AllowsItem(item) || requested <= 0 || inventory.Data.IsDepositBlocked) return 0;
        int capacity = Mathf.FloorToInt(inventory.Data.GetCapacityLimitedAmount(item, requested) + .0001f);
        if (capacity <= 0) return 0;
        float space = 0f;
        var from = new ItemSlot(0) { itemData = item };
        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            ItemSlot slot = inventory.Data.itemSlots[i];
            if (!AllowedSlot(i) || slot == null || inventory.IsSlotBeingDragged(i) || !inventory.CanAcceptQuickTransfer(from, slot)) continue;
            if (slot.itemData == null) space += item.Stack.Stackable ? slot.SlotMaxVolume : 1f;
            else if (slot.itemData.CanStackWith(item)) space += Mathf.Max(0f, slot.SlotMaxVolume - slot.itemData.Stack.Amount);
            if (space >= capacity) return capacity;
        }
        if (inventory.Data.HasUnlimitedSlots && (Configuration.SlotIndices == null || Configuration.SlotIndices.Length == 0) &&
            inventory.CanAcceptQuickTransfer(from, new ItemSlot(inventory.Data.itemSlots.Count))) return capacity;
        return Mathf.FloorToInt(Mathf.Min(capacity, space) + .0001f);
    }
    public bool ExtractItems(ItemData expected, int amount, out ItemData extracted)
    {
        extracted = null;
        if (!IsValid || !GameNetwork.HasStateAuthority) return false;
        int index = inventory.Data.itemSlots.FindIndex(slot => ReferenceEquals(slot?.itemData, expected));
        if (index < 0 || !AllowedSlot(index) || !allowDraggedSource && inventory.IsSlotBeingDragged(index) || !Configuration.AllowsItem(expected) || amount <= 0 || expected.Stack.Amount + .0001f < amount) return false;
        extracted = ItemInstanceDataFactory.CloneRuntime(expected);
        extracted.Stack.Amount = amount;
        if (expected.Stack.Amount > amount + .0001f) extracted.Guid = NewGuid(expected.Guid);
        return notifications.Mutate(inventory, () => inventory.Data.TryConsumeFromSlot(inventory.Data.itemSlots[index], amount, out _));
    }
    public bool InsertItems(ItemData item, int amount)
    {
        if (!IsValid || !GameNetwork.HasStateAuthority) return false;
        if (GetReceivableItems(item, amount) < amount) return false;
        return notifications.Mutate(inventory, () => InsertNative(item, amount));
    }
    private bool InsertNative(ItemData item, int amount)
    {
        inventory.Data.EnsureSpareSlot();
        ItemData copy = ItemInstanceDataFactory.CloneRuntime(item); copy.Stack.Amount = amount;
        var sourceSlot = new ItemSlot(0) { itemData = copy };
        var source = new Inventory_Data(new List<ItemSlot> { sourceSlot }, "transfer");
        for (int pass = 0; pass < 2 && sourceSlot.itemData != null; pass++)
        {
            for (int i = 0; i < inventory.Data.itemSlots.Count && sourceSlot.itemData != null; i++)
            {
                ItemSlot target = inventory.Data.itemSlots[i];
                if (!AllowedSlot(i) || target == null || inventory.IsSlotBeingDragged(i) ||
                    (pass == 0 ? target.itemData == null : target.itemData != null) || !inventory.CanAcceptQuickTransfer(sourceSlot, target)) continue;
                source.TransferItemQuantityTo(sourceSlot, inventory.Data, target, Mathf.FloorToInt(sourceSlot.itemData.Stack.Amount));
            }
        }
        return sourceSlot.itemData == null || sourceSlot.itemData.Stack.Amount <= .0001f;
    }
    private static int NewGuid(int previous) { int id; do id = Guid.NewGuid().GetHashCode(); while (id == 0 || id == previous); return id; }
    public override object CaptureState() => InventoryInstanceSnapshot.Capture(inventory.Data);
    public override void RestoreState(object snapshot)
    {
        notifications.Clear(); var saved = (InventoryInstanceSnapshot)snapshot;
        notifications.Mutate(inventory, () =>
        {
            saved.RestoreTo(inventory.Data);
            if (inventory.Data.itemSlots.Count > saved.SlotCount) inventory.Data.itemSlots.RemoveRange(saved.SlotCount, inventory.Data.itemSlots.Count - saved.SlotCount);
            inventory.Data.NotifySlotsChanged(); return true;
        }, discard: true);
        inventory.RefreshUI();
    }
    public override void PublishState()
    {
        try { notifications.Publish(); }
        finally { try { inventory.Save(); changed?.Invoke(); } finally { if (Owner != null) ItemNetworkStateSerialization.NotifyRuntimeStateChanged(Owner); } }
    }
    #endregion
}
