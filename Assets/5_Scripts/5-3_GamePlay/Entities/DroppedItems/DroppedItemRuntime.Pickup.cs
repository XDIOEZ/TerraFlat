using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using UnityEngine;

internal sealed partial class DroppedItemRuntime
{
    private readonly Dictionary<ItemPicker, HashSet<int>> pickupAttempts = new();
    private readonly HashSet<int> nearby = new();
    private readonly HashSet<int> pickupReservations = new();
    private readonly List<int> pickupScratch = new();
    private readonly List<ItemPicker> pickerScratch = new();
    private float pickupClock;

    public void ForgetPicker(ItemPicker picker) => pickupAttempts.Remove(picker);

    /// <summary>只查询拾取器附近空间桶；沿用其真实 Collider 的范围，不为掉落物创建 Collider。</summary>
    private void TickPickup(float deltaTime, IEnumerable<ItemPicker> pickers)
    {
        pickupClock += deltaTime;
        if (pickupClock < 0.1f) return;
        pickupClock = 0f;
        pickerScratch.Clear(); pickerScratch.AddRange(pickers);
        foreach (ItemPicker picker in pickerScratch)
        {
            if (picker == null || !picker.TryGetDroppedPickupBounds(out Bounds bounds)) continue;
            if (!pickupAttempts.TryGetValue(picker, out HashSet<int> attempted))
                pickupAttempts.Add(picker, attempted = new HashSet<int>());
            if (!picker.CanPickUp) { attempted.Clear(); continue; }
            nearby.Clear();
            Vector2Int min = SpatialCell(bounds.min), max = SpatialCell(bounds.max);
            for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    Vector2 point = domain.Normalize(new Unity.Mathematics.float2((x + 0.5f) * SpatialCellSize, (y + 0.5f) * SpatialCellSize));
                    if (!spatial.TryGetValue(SpatialCell(point), out HashSet<int> bucket)) continue;
                    foreach (int id in bucket)
                    {
                        DroppedBody body = simulation.Get(id);
                        if (body.Pickable != 0 && picker.CanReachDroppedPoint(domain.NearestImagePosition((Vector2)bounds.center, body.Position)))
                            nearby.Add(id);
                    }
                }
            attempted.RemoveWhere(id => !nearby.Contains(id));
            pickupScratch.Clear(); pickupScratch.AddRange(nearby);
            pickupScratch.Sort();
            foreach (int id in pickupScratch)
                if (attempted.Add(id)) TryPickup(picker, id);
        }
    }

    /// <summary>用独立候选载荷执行现有库存事务；失败时绝不把被预检改写的数量写回实体。</summary>
    private void TryPickup(ItemPicker picker, int id)
    {
        if (!simulation.Contains(id) || !pickupReservations.Add(id)) return;
        try
        {
            DroppedBody body = simulation.Get(id);
            if (body.Pickable == 0 || body.Amount <= 0f) return;
            ItemData candidate = FastCloner.FastCloner.DeepClone(payloads[id]);
            candidate.Stack.Amount = body.Amount; candidate.Stack.CanBePickedUp = true;
            if (!picker.TryAcceptDroppedPickup(candidate)) return;
            float remaining = candidate.Stack.CanBePickedUp ? candidate.Stack.Amount : 0f;
            remaining = Mathf.Clamp(remaining, 0f, body.Amount);
            if (remaining >= body.Amount) return;
            DroppedItemVisual visual = visuals[id];
            body.Amount = remaining;
            if (remaining <= 0f) Remove(id); else simulation.Set(body);
            // 数量提交先于表现；声音或短期动画失败不能复活已入包的物品。
            try { picker.PlayDroppedPickupFeedback(visual, body); }
            catch (Exception exception) { Debug.LogWarning($"[DroppedItems] 拾取已提交，表现失败：{exception.Message}"); }
        }
        finally { pickupReservations.Remove(id); }
    }
}
