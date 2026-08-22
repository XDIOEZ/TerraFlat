using System;
using System.Collections.Generic;
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

    private readonly List<Item> everyFrameItems = new(64);
    private readonly List<Item>[] fastBuckets = CreateBuckets();
    private readonly List<Item>[] normalBuckets = CreateBuckets();
    private readonly List<Item>[] slowBuckets = CreateBuckets();
    private readonly HashSet<Item> dirtyItems = new();
    private readonly List<Item> snapshot = new(256);

    private float fastTimer;
    private float normalTimer;
    private float slowTimer;
    private int fastCursor = -1;
    private int normalCursor = -1;
    private int slowCursor = -1;

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
        dirtyItems.Remove(item);
    }

    public void Update(IReadOnlyList<Item> runtimeItems, float deltaTime, Action<Item> beforeEveryFrameTick)
    {
        FlushDirty(runtimeItems);

        snapshot.Clear();
        snapshot.AddRange(everyFrameItems);

        for (int i = 0; i < snapshot.Count; i++)
        {
            Item item = snapshot[i];
            if (item != null && item.isActiveAndEnabled)
            {
                beforeEveryFrameTick?.Invoke(item);
            }
        }

        for (int i = 0; i < snapshot.Count; i++)
        {
            Item item = snapshot[i];
            if (item != null && item.isActiveAndEnabled)
            {
                item.Tick(deltaTime);
            }
        }

        ProcessTier(fastBuckets, ref fastTimer, ref fastCursor, FastSlice, deltaTime);
        ProcessTier(normalBuckets, ref normalTimer, ref normalCursor, NormalSlice, deltaTime);
        ProcessTier(slowBuckets, ref slowTimer, ref slowCursor, SlowSlice, deltaTime);
    }

    /// <summary>加载页期间暂停调度并重置时间基准，恢复后不补算暂停期间的 Tick。</summary>
    public void Pause(IReadOnlyList<Item> runtimeItems)
    {
        fastTimer = 0f;
        normalTimer = 0f;
        slowTimer = 0f;
        fastCursor = -1;
        normalCursor = -1;
        slowCursor = -1;

        float currentTime = Time.time;
        for (int i = 0; i < runtimeItems.Count; i++)
        {
            Item item = runtimeItems[i];
            if (item != null)
                item.ResetScheduledTickClock(currentTime);
        }
    }

    public void Rebuild(IReadOnlyList<Item> runtimeItems)
    {
        everyFrameItems.Clear();
        ClearBuckets(fastBuckets);
        ClearBuckets(normalBuckets);
        ClearBuckets(slowBuckets);
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
        float deltaTime)
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
                    item.ResetScheduledTickClock(currentTime);
                    continue;
                }

                item.TickScheduled(currentTime);
            }
        }

        timer = elapsedSlices >= buckets.Length ? 0f : timer - slicesToProcess * slice;
    }

    private static void ClearBuckets(List<Item>[] buckets)
    {
        for (int i = 0; i < buckets.Length; i++)
        {
            buckets[i].Clear();
        }
    }
}
