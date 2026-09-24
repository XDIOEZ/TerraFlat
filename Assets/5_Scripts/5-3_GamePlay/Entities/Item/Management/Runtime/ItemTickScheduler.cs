using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 按 ItemTickTier 分桶执行 Item Tick，与实例注册、存档和世界归属解耦。
/// </summary>
internal sealed class ItemTickScheduler
{
    private const int BucketCount = 8;
    private const float FastSlice = 0.05f / BucketCount;
    private const float NormalSlice = 0.1f / BucketCount;
    private const float SlowSlice = 0.25f / BucketCount;
    private const float DormantPhysicsSlice = 0.25f / BucketCount;

    private readonly List<Item> everyFrameItems = new(64);
    private readonly List<Item>[] fastBuckets = CreateBuckets();
    private readonly List<Item>[] normalBuckets = CreateBuckets();
    private readonly List<Item>[] slowBuckets = CreateBuckets();
    private readonly List<Item>[] dormantPhysicsBuckets = CreateBuckets();
    private readonly HashSet<Item> dirtyItems = new();
    private readonly List<Item> snapshot = new(256);

    private float fastTimer;
    private float normalTimer;
    private float slowTimer;
    private float dormantPhysicsTimer;
    private int fastCursor = -1;
    private int normalCursor = -1;
    private int slowCursor = -1;
    private int dormantPhysicsCursor = -1;

    public int EveryFrameCount => everyFrameItems.Count;
    public int FastCount => CountItems(fastBuckets);
    public int NormalCount => CountItems(normalBuckets);
    public int SlowCount => CountItems(slowBuckets);

    public void NotifyChanged(Item item)
    {
        if (item != null)
        {
            dirtyItems.Add(item);
        }
    }

    public void Register(Item item)
    {
        Remove(item);
        if (item == null)
        {
            return;
        }

        switch (item.GetTickTier())
        {
            case ItemTickTier.EveryFrame:
                everyFrameItems.Add(item);
                item.ResetScheduledTickClock(-1f);
                break;
            case ItemTickTier.Fast:
                AddToBucket(fastBuckets, item);
                item.ResetScheduledTickClock(Time.time);
                break;
            case ItemTickTier.Normal:
                AddToBucket(normalBuckets, item);
                item.ResetScheduledTickClock(Time.time);
                break;
            case ItemTickTier.Slow:
                AddToBucket(slowBuckets, item);
                item.ResetScheduledTickClock(Time.time);
                break;
            case ItemTickTier.Dormant:
                if (item.GetComponent<Rigidbody2D>() != null)
                    AddToBucket(dormantPhysicsBuckets, item);
                break;
        }
    }

    public void Remove(Item item)
    {
        if (ReferenceEquals(item, null))
        {
            return;
        }

        everyFrameItems.Remove(item);
        RemoveFromBuckets(fastBuckets, item);
        RemoveFromBuckets(normalBuckets, item);
        RemoveFromBuckets(slowBuckets, item);
        RemoveFromBuckets(dormantPhysicsBuckets, item);
        dirtyItems.Remove(item);
    }

    public void Update(IReadOnlyList<Item> runtimeItems, float deltaTime, Action<Item> beforeEveryFrameTick,
        IReadOnlyList<Transform> players, WorldTopologyDomain topology)
    {
        FlushDirty(runtimeItems);

        snapshot.Clear();
        snapshot.AddRange(everyFrameItems);
        float currentTime = Time.time;

        for (int i = 0; i < snapshot.Count; i++)
        {
            Item item = snapshot[i];
            if (item == null)
                continue;
            if (!item.isActiveAndEnabled)
            {
                item.ResetScheduledTickClock(-1f);
                continue;
            }

            if (!item.ShouldUseSimulationRange())
            {
                item.SetSimulationRangePaused(false);
                beforeEveryFrameTick?.Invoke(item);
                item.Tick(deltaTime);
                continue;
            }

            SimulationRangeTier tier = ResolveTier(item, players, topology);
            item.SetSimulationRangePaused(tier == SimulationRangeTier.Stopped);
            if (tier == SimulationRangeTier.Stopped)
            {
                item.ResetScheduledTickClock(-1f);
                continue;
            }

            float interval = SimulationRangePreferences.TickInterval(tier);
            if (!item.IsEveryFrameTickDue(currentTime, interval))
                continue;

            beforeEveryFrameTick?.Invoke(item);
            item.TickEveryFrameAt(currentTime, interval);
        }

        ProcessTier(fastBuckets, ref fastTimer, ref fastCursor, FastSlice, deltaTime, players, topology);
        ProcessTier(normalBuckets, ref normalTimer, ref normalCursor, NormalSlice, deltaTime, players, topology);
        ProcessTier(slowBuckets, ref slowTimer, ref slowCursor, SlowSlice, deltaTime, players, topology);
        ProcessDormantPhysics(deltaTime, players, topology);
    }

    /// <summary>加载页期间暂停调度并重置时间基准，恢复后不补算暂停期间的 Tick。</summary>
    public void Pause(IReadOnlyList<Item> runtimeItems)
    {
        fastTimer = 0f;
        normalTimer = 0f;
        slowTimer = 0f;
        dormantPhysicsTimer = 0f;
        fastCursor = -1;
        normalCursor = -1;
        slowCursor = -1;
        dormantPhysicsCursor = -1;

        for (int i = 0; i < runtimeItems.Count; i++)
        {
            Item item = runtimeItems[i];
            if (item != null)
                item.ResetScheduledTickClock(-1f);
        }
    }

    public void Rebuild(IReadOnlyList<Item> runtimeItems)
    {
        everyFrameItems.Clear();
        ClearBuckets(fastBuckets);
        ClearBuckets(normalBuckets);
        ClearBuckets(slowBuckets);
        ClearBuckets(dormantPhysicsBuckets);
        dirtyItems.Clear();

        for (int i = 0; i < runtimeItems.Count; i++)
        {
            Item item = runtimeItems[i];
            if (item != null)
            {
                Register(item);
            }
        }
    }

    private void FlushDirty(IReadOnlyList<Item> runtimeItems)
    {
        if (dirtyItems.Count == 0)
        {
            return;
        }

        snapshot.Clear();
        foreach (Item item in dirtyItems)
        {
            snapshot.Add(item);
        }

        dirtyItems.Clear();
        for (int i = 0; i < snapshot.Count; i++)
        {
            Item item = snapshot[i];
            if (item != null && Contains(runtimeItems, item))
            {
                Register(item);
            }
        }
    }

    private static bool Contains(IReadOnlyList<Item> items, Item target)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == target)
            {
                return true;
            }
        }

        return false;
    }

    private static List<Item>[] CreateBuckets()
    {
        List<Item>[] buckets = new List<Item>[BucketCount];
        for (int i = 0; i < buckets.Length; i++)
        {
            buckets[i] = new List<Item>(16);
        }

        return buckets;
    }

    private static void AddToBucket(List<Item>[] buckets, Item item)
    {
        int bucketIndex = (item.GetInstanceID() & int.MaxValue) % buckets.Length;
        buckets[bucketIndex].Add(item);
    }

    private static void RemoveFromBuckets(List<Item>[] buckets, Item item)
    {
        for (int i = 0; i < buckets.Length; i++)
        {
            buckets[i].Remove(item);
        }
    }

    private static int CountItems(List<Item>[] buckets)
    {
        int count = 0;
        for (int i = 0; i < buckets.Length; i++)
        {
            count += buckets[i].Count;
        }

        return count;
    }

    private static void ProcessTier(
        List<Item>[] buckets,
        ref float timer,
        ref int cursor,
        float slice,
        float deltaTime,
        IReadOnlyList<Transform> players,
        WorldTopologyDomain topology)
    {
        timer += deltaTime;
        int elapsedSlices = Mathf.FloorToInt(timer / slice);
        if (elapsedSlices <= 0)
        {
            return;
        }

        int slicesToProcess = Mathf.Min(elapsedSlices, buckets.Length);
        float currentTime = Time.time;
        for (int sliceIndex = 0; sliceIndex < slicesToProcess; sliceIndex++)
        {
            cursor = (cursor + 1) % buckets.Length;
            List<Item> bucket = buckets[cursor];
            for (int i = 0; i < bucket.Count; i++)
            {
                Item item = bucket[i];
                if (item == null)
                {
                    continue;
                }

                if (!item.isActiveAndEnabled)
                {
                    item.ResetScheduledTickClock(-1f);
                    continue;
                }

                SimulationRangeTier tier = ResolveTier(item, players, topology);
                item.SetSimulationRangePaused(tier == SimulationRangeTier.Stopped);
                if (tier == SimulationRangeTier.Stopped)
                {
                    item.ResetScheduledTickClock(-1f);
                    continue;
                }
                if (!item.IsScheduledTickDue(currentTime, SimulationRangePreferences.TickInterval(tier)))
                    continue;

                item.TickScheduled(currentTime);
            }
        }

        timer = elapsedSlices >= buckets.Length ? 0f : timer - slicesToProcess * slice;
    }

    /// <summary>对同场景最近玩家求距离；世界尚无玩家时保留原有近距离行为。</summary>
    private static SimulationRangeTier ResolveTier(Item item, IReadOnlyList<Transform> players,
        WorldTopologyDomain topology)
    {
        if (!item.ShouldUseSimulationRange() || players == null || players.Count == 0)
            return SimulationRangeTier.Near;

        Vector3 position = item.transform.position;
        float2 entityPosition = new float2(position.x, position.y);
        float distanceSquared = float.PositiveInfinity;
        for (int i = 0; i < players.Count; i++)
        {
            Transform player = players[i];
            if (player == null || player.gameObject.scene != item.gameObject.scene)
                continue;
            Vector3 playerPosition = player.position;
            distanceSquared = Mathf.Min(distanceSquared, topology.SqrDistance(entityPosition,
                new float2(playerPosition.x, playerPosition.y)));
        }

        return SimulationRangePreferences.ResolveTier(distanceSquared);
    }

    /// <summary>没有玩法 Tick 但仍带刚体的实体分桶检查，远处也要停止物理模拟。</summary>
    private void ProcessDormantPhysics(float deltaTime, IReadOnlyList<Transform> players,
        WorldTopologyDomain topology)
    {
        dormantPhysicsTimer += deltaTime;
        int elapsedSlices = Mathf.FloorToInt(dormantPhysicsTimer / DormantPhysicsSlice);
        if (elapsedSlices <= 0) return;

        int slicesToProcess = Mathf.Min(elapsedSlices, dormantPhysicsBuckets.Length);
        for (int sliceIndex = 0; sliceIndex < slicesToProcess; sliceIndex++)
        {
            dormantPhysicsCursor = (dormantPhysicsCursor + 1) % dormantPhysicsBuckets.Length;
            List<Item> bucket = dormantPhysicsBuckets[dormantPhysicsCursor];
            for (int i = 0; i < bucket.Count; i++)
            {
                Item item = bucket[i];
                if (item == null || !item.isActiveAndEnabled) continue;
                SimulationRangeTier tier = ResolveTier(item, players, topology);
                item.SetSimulationRangePaused(tier == SimulationRangeTier.Stopped);
            }
        }

        dormantPhysicsTimer = elapsedSlices >= dormantPhysicsBuckets.Length
            ? 0f : dormantPhysicsTimer - slicesToProcess * DormantPhysicsSlice;
    }

    private static void ClearBuckets(List<Item>[] buckets)
    {
        for (int i = 0; i < buckets.Length; i++)
        {
            buckets[i].Clear();
        }
    }
}
