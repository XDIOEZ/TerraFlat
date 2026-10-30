using System.Collections;
using System.Collections.Generic;
using FlatWorld.Networking;

/// <summary>耐久的运行时边界：库存拥有数量，物品拥有表现；以后模块仍调用同一入口。</summary>
public static class ItemDurabilityService
{
    #region 权威消耗与通知
    public static bool TryConsume(Item source, float wear, ItemData data = null, Item inventoryOwner = null)
    {
        if (!GameNetwork.HasStateAuthority) return false;
        data ??= source?.itemData;
        if (!ItemDurabilityRules.CanConsume(data, wear)) return false;
        Item liveSource = source != null && ReferenceEquals(source.itemData, data) ? source : null;
        uint generation = liveSource != null ? liveSource.RuntimeGeneration : 0;
        Item owner = inventoryOwner != null ? inventoryOwner : liveSource?.Owner;
        Mod_HotBar hotbar = owner?.itemMods?.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
        Inventory_Data inventory = hotbar?.RuntimeInventory?.Data;
        if (inventory?.FindExactItemSlot(data) == null)
        {
            inventory = owner != null && InventoryContextResolver.TryResolveContainingInventory(owner, data, out Inventory found)
                ? found.Data : null;
        }
        bool changed = inventory != null
            ? inventory.TryConsumeDurability(data, wear, out _)
            : ItemDurabilityRules.TryConsume(data, wear, out _);
        if (!changed) return false;
        if (IsCurrentSource(liveSource, data, generation))
        {
            liveSource.OnDurabilityModified?.Invoke(data.Durability);
            if (IsCurrentSource(liveSource, data, generation)) liveSource.OnUIRefresh?.Invoke();
            // 命中回调结束后再回收世界外壳，避免枚举伤害目标时清空当前模块。
            if (IsCurrentSource(liveSource, data, generation))
            {
                if (inventory == null && data.Stack.Amount <= 0f && liveSource.isActiveAndEnabled)
                    liveSource.StartCoroutine(DespawnEmptySource(liveSource, data, generation));
                else if (owner == null) ItemNetworkStateSerialization.NotifyRuntimeStateChanged(liveSource);
            }
        }
        hotbar?.NotifyOwnerNetworkStateChanged();
        if (owner != null && hotbar == null) ItemNetworkStateSerialization.NotifyRuntimeStateChanged(owner);
        return true;
    }

    /// <summary>含内部库存的工具损坏前先交付内容，失败时保留工具和内容。</summary>
    public static bool TryConsumePreservingContainedItems(Item source, float wear, params Inventory[] inventories)
    {
        ItemData data = source?.itemData;
        if (!ItemDurabilityRules.CanConsume(data, wear)) return false;
        if (data.Durability > wear) return TryConsume(source, wear);
        float originalAmount = data.Stack.Amount;
        var drops = new List<DroppedItemHandle>();
        var removed = new List<(Inventory_Data inventory, ItemSlot slot, ItemData data, float amount)>();
        using var scope = source.UseOwnedWorldForItemCallbacks();
        try
        {
            foreach (Inventory inventory in inventories)
            {
                if (inventory?.Data?.itemSlots == null) continue;
                foreach (ItemSlot slot in inventory.Data.itemSlots)
                {
                    ItemData content = slot?.itemData;
                    if (content?.Stack == null || content.Stack.Amount <= 0f) continue;
                    ItemData drop = ItemInstanceDataFactory.CloneRuntime(content);
                    drop.Stack.CanBePickedUp = true;
                    drop.inHand = false;
                    DroppedItemHandle handle = DroppedItemService.Spawn(drop,
                        source.Owner != null ? source.Owner.transform.position : source.transform.position, sourceItem: source);
                    if (!handle.IsValid) throw new System.InvalidOperationException("工具损毁时无法交付内部物品。");
                    drops.Add(handle);
                    removed.Add((inventory.Data, slot, content, content.Stack.Amount));
                }
            }
            foreach (var entry in removed)
                if (!ReferenceEquals(entry.slot.itemData, entry.data) ||
                    !entry.inventory.TrySetSlotItemAmount(entry.slot, entry.data, 0f))
                    throw new System.InvalidOperationException("工具内部物品已发生变化。");
            if (!TryConsume(source, wear)) throw new System.InvalidOperationException("工具耐久扣除失败。");
            return true;
        }
        catch (System.Exception exception)
        {
            if (data.Stack.Amount < originalAmount) { UnityEngine.Debug.LogException(exception); return true; }
            foreach (DroppedItemHandle handle in drops) DroppedItemService.Remove(handle);
            foreach (var entry in removed)
                if (entry.slot.itemData == null)
                    entry.inventory.TrySetSlotItemAmount(entry.slot, entry.data, entry.amount);
            UnityEngine.Debug.LogException(exception);
            return false;
        }
    }

    private static bool IsCurrentSource(Item source, ItemData data, uint generation)
        => source != null && !source.DestructionHandled && source.RuntimeGeneration == generation &&
            ReferenceEquals(source.itemData, data);

    private static IEnumerator DespawnEmptySource(Item source, ItemData data, uint generation)
    {
        yield return null;
        if (IsCurrentSource(source, data, generation) && data.Stack.Amount <= 0f)
            source.DestroySelf();
    }
    #endregion
}
