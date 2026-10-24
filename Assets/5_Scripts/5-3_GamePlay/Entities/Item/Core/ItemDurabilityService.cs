using System.Collections;
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
