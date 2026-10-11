using System;
using System.Collections.Generic;
using FlatWorld.Audio;
using UnityEngine;

public static partial class DroppedItemService
{
    #region 外部世界轻量拾取
    // 特殊世界复用已登记拾取器和正式库存事务，不需要为冷载荷装配物品模块。
    internal static bool TryPickupExternal(ItemData payload, Vector2 point, out float remaining)
    {
        remaining = payload?.Stack?.Amount ?? 0f;
        if (remaining <= 0f || payload.Stack.CanBePickedUp != true) return false;
        using var pickerLease = UnityEngine.Pool.ListPool<Mod_ItemPicker>.Get(out List<Mod_ItemPicker> scratch);
        scratch.AddRange(pickers);
        foreach (Mod_ItemPicker picker in scratch)
        {
            if (picker == null || !picker.CanPickUp || !picker.TryGetDroppedPickupBounds(out Bounds bounds) ||
                !bounds.Contains(new Vector3(point.x, point.y, bounds.center.z)) || !picker.CanReachDroppedPoint(point)) continue;
            ItemData candidate = FastCloner.FastCloner.DeepClone(payload);
            if (!picker.TryAcceptDroppedPickup(candidate)) continue;
            float after = candidate.Stack.CanBePickedUp ? Mathf.Clamp(candidate.Stack.Amount, 0f, remaining) : 0f;
            if (after >= remaining) continue;
            remaining = after;
            payload.Stack.Amount = remaining;
            // 数量先提交，拾取声音失败也不能把已入包的东西留在地上。
            try { AudioService.Instance.PlayAt(AudioEventIds.ItemPickup, point); }
            catch (Exception exception) { Debug.LogWarning($"[SpaceDrops] 拾取已提交，声音失败：{exception.Message}"); }
            return true;
        }
        return false;
    }
    #endregion
}
