using System;
using System.Collections.Generic;
using FlatWorld.Diagnostics;
using FlatWorld.DroppedItems;
using UnityEngine;

internal sealed partial class DroppedItemRuntime
{
    #region 拾取性能诊断
    // 按真实掉落记录统计事务，不把堆叠数量当成实例数。
    private static readonly PerformanceDebugPoint PickupBatchDebug = new("FlatWorld.Pickup.DroppedBatch");
    private static readonly PerformanceDebugPoint PickupTransactionDebug = new("FlatWorld.Pickup.DroppedTransaction");
    private static readonly PerformanceDebugPoint PickupCloneDebug = new("FlatWorld.Pickup.PayloadClone");
    private static readonly PerformanceDebugPoint PickupFeedbackDebug = new("FlatWorld.Pickup.DroppedFeedback");
    private static readonly PerformanceDebugPoint PickupCommittedDebug = new("FlatWorld.Pickup.DroppedCommittedStacks");
    private static readonly PerformanceDebugPoint PickupRejectedDebug = new("FlatWorld.Pickup.DroppedRejectedStacks");
    #endregion

    private readonly Dictionary<Mod_ItemPicker, HashSet<int>> pickupAttempts = new();
    private readonly Dictionary<Mod_ItemPicker, ulong> pickupAttemptCapacityRevisions = new();
    private readonly HashSet<int> nearby = new();
    private readonly HashSet<int> pickupReservations = new();
    private readonly List<int> pickupScratch = new();
    private readonly List<Mod_ItemPicker> pickerScratch = new();
    private float pickupClock;

    public void ForgetPicker(Mod_ItemPicker picker)
    {
        pickupAttempts.Remove(picker);
        pickupAttemptCapacityRevisions.Remove(picker);
    }

    /// <summary>只查询拾取器附近空间桶；沿用其真实 Collider 的范围，不为掉落物创建 Collider。</summary>
    private void TickPickup(float deltaTime, IEnumerable<Mod_ItemPicker> pickers)
    {
        pickupClock += deltaTime;
        if (pickupClock < 0.1f) return;
        pickupClock = 0f;
        using var batchDebug = PickupBatchDebug.Measure();
        pickerScratch.Clear(); pickerScratch.AddRange(pickers);
        foreach (Mod_ItemPicker picker in pickerScratch)
        {
            if (picker == null || GameplayWorldRegistry.GetItemWorldKey(picker.item) != matterWorldKey ||
                presentation == null || picker.gameObject.scene != presentationScene ||
                !picker.TryGetDroppedPickupBounds(out Bounds bounds)) continue;
            if (!pickupAttempts.TryGetValue(picker, out HashSet<int> attempted))
                pickupAttempts.Add(picker, attempted = new HashSet<int>());
            ulong revision = picker.PickupCapacityRevision;
            if (!pickupAttemptCapacityRevisions.TryGetValue(picker, out ulong attemptedRevision) || attemptedRevision != revision)
            {
                attempted.Clear();
                pickupAttemptCapacityRevisions[picker] = revision;
            }
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
                        LightweightDroppedBody body = simulation.Get(id);
                        if (body.Pickable != 0 && picker.CanReachDroppedPoint(
                            domain.NearestImagePosition((Vector2)bounds.center, body.Position), body.IsOnBottom))
                            nearby.Add(id);
                    }
                }
            attempted.RemoveWhere(id => !nearby.Contains(id));
            pickupScratch.Clear(); pickupScratch.AddRange(nearby);
            pickupScratch.Sort();
            PickupBatchDebug.AddWork(pickupScratch.Count);
            foreach (int id in pickupScratch)
                if (attempted.Add(id)) TryPickup(picker, id);
        }
    }

    /// <summary>用独立候选载荷执行现有库存事务；失败时绝不把被预检改写的数量写回实体。</summary>
    private void TryPickup(Mod_ItemPicker picker, int id)
    {
        if (!simulation.Contains(id) || !pickupReservations.Add(id)) return;
        try
        {
            LightweightDroppedBody body = simulation.Get(id);
            if (body.Pickable == 0 || body.Amount <= 0f || !picker.CanReachDroppedPoint(
                domain.NearestImagePosition((Vector2)picker.item.transform.position, body.Position), body.IsOnBottom)) return;
            ItemData payload = payloads[id];
            using var transactionDebug = PickupTransactionDebug.Measure(1, payload.IDName);
            ItemData candidate;
            using (PickupCloneDebug.Measure(1, payload.IDName))
                candidate = FastCloner.FastCloner.DeepClone(payload);
            candidate.Stack.Amount = body.Amount; candidate.Stack.CanBePickedUp = true;
            if (!picker.TryAcceptDroppedPickup(candidate))
            {
                PickupRejectedDebug.AddWork(1);
                return;
            }
            float remaining = candidate.Stack.CanBePickedUp ? candidate.Stack.Amount : 0f;
            remaining = Mathf.Clamp(remaining, 0f, body.Amount);
            if (remaining >= body.Amount) return;
            DroppedItemVisual visual = visuals[id];
            body.Amount = remaining;
            if (remaining > 0f) payload.Durability = candidate.Durability;
            if (remaining <= 0f) Remove(id); else simulation.Set(body);
            PickupCommittedDebug.AddWork(1);
            // 数量提交先于表现；声音或短期动画失败不能复活已入包的物品。
            try
            {
                using var feedbackDebug = PickupFeedbackDebug.Measure(1, payload.IDName);
                picker.PlayDroppedPickupFeedback(visual, body);
            }
            catch (Exception exception) { Debug.LogWarning($"[DroppedItems] 拾取已提交，表现失败：{exception.Message}"); }
        }
        finally { pickupReservations.Remove(id); }
    }
}
