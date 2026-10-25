using System;
using UnityEngine;

/// <summary>玩家丢弃与容器出料共用生成成功后才扣量的世界掉落事务。</summary>
public static class InventoryWorldDrop
{
    #region 世界掉落事务

    public static bool TryDrop(ItemSlot slot, int count, Item actor, Vector2 start, Vector2 destination,
        float duration, float bezierOffset = 0f, float arcHeight = 1f, Inventory_Data inventory = null)
    {
        ItemData original = slot?.itemData;
        if (actor == null || original?.Stack == null || !float.IsFinite(original.Stack.Amount) ||
            count <= 0 || original.Stack.Amount < count ||
            !float.IsFinite(duration) || duration < 0f || !float.IsFinite(start.x) || !float.IsFinite(start.y) ||
            !float.IsFinite(destination.x) || !float.IsFinite(destination.y) ||
            inventory != null && (inventory.itemSlots == null || !inventory.itemSlots.Contains(slot))) return false;

        var scene = actor.gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded) return false;
        bool space = SpaceMgr.IsSpaceScene(scene.name);
        if (!space && (!GameplayWorldRegistry.TryGet(scene.name, out var world) || world.Scene != scene)) return false;
        IDisposable scope = null;
        DroppedItemHandle handle = default;
        float before = original.Stack.Amount;
        try
        {
            scope = space ? GameplayWorldRegistry.UseSpaceScene(scene.name) : GameplayWorldRegistry.UseWorld(scene.name);
            ItemData dropped = FastCloner.FastCloner.DeepClone(original);
            dropped.Stack.Amount = count;
            dropped.Stack.CanBePickedUp = false;
            dropped.inHand = false;
            handle = DroppedItemService.Spawn(dropped, start, destination, duration,
                bezierOffset: bezierOffset, arcHeight: arcHeight,
                spawnReason: DroppedItemSpawnReason.PlayerDiscard, sourceItem: actor);
            if (!ReferenceEquals(slot.itemData, original) || original.Stack.Amount != before)
            { DroppedItemService.Remove(handle); return false; }
            if (inventory != null)
            {
                if (!inventory.TryConsumeFromSlot(slot, count, out _))
                { DroppedItemService.Remove(handle); return false; }
            }
            else
            {
                slot.Amount -= count;
                ItemDurabilityRules.AdvanceAfterRemoval(original);
                if (slot.Amount <= 0) slot.ClearData();
            }
            return true;
        }
        catch (Exception exception)
        {
            // 扣量后的监听报错不撤掉已交付物；生成或预检失败仍撤销候选。
            bool consumed = original.Stack.Amount == before - count &&
                (ReferenceEquals(slot.itemData, original) || slot.itemData == null);
            if (!consumed) DroppedItemService.Remove(handle);
            Debug.LogError($"掉落物生成失败：{original.IDName}，{exception.Message}", actor);
            return consumed;
        }
        finally { scope?.Dispose(); }
    }

    #endregion
}
