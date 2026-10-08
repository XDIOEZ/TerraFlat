using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>按更新档分桶调度，以注册版本和运行代际保护回调中的增删与对象池复用。</summary>
internal sealed class ItemTickScheduler
{
    #region 调度状态

    private const int BucketCount = 8;
    private const float FastSlice = 0.05f / BucketCount;
    private const float NormalSlice = 0.1f / BucketCount;
    private const float SlowSlice = 0.25f / BucketCount;
    private const float DormantPhysicsSlice = 0.25f / BucketCount;

    internal readonly struct ScheduledEntry
    {
        public readonly Item Item;
        public readonly uint Generation;
        public readonly ulong Token;

        public ScheduledEntry(Item item, ulong token)
        {
            Item = item;
            Generation = item.RuntimeGeneration;
            Token = token;
        }
    }

    private sealed class Location
    {
        public ScheduledEntry Entry;
        public ItemTickTier Tier;
        public int Bucket;
        public int Slot;
    }

    private readonly List<ScheduledEntry> everyFrameItems = new(64);
    private readonly List<ScheduledEntry>[] fastBuckets = CreateBuckets();
    private readonly List<ScheduledEntry>[] normalBuckets = CreateBuckets();
    private readonly List<ScheduledEntry>[] slowBuckets = CreateBuckets();
    private readonly List<ScheduledEntry>[] dormantPhysicsBuckets = CreateBuckets();
    private readonly HashSet<Item> dirtyItems = new(ItemReferenceComparer.Instance);
    private readonly Dictionary<Item, Location> locations = new(ItemReferenceComparer.Instance);
    private readonly List<ScheduledEntry> tickSnapshot = new(256);
    private readonly List<Item> maintenanceSnapshot = new(256);
    private readonly HashSet<Item> rebuildMembers = new(ItemReferenceComparer.Instance);
    private readonly ItemSimulationRangeIndex simulationRanges = new();
    private readonly List<ItemSimulationRangeIndex.Transition> rangeTransitions = new(32);
    private ulong registrationSequence;
    private ulong currentUpdateToken;
    private bool updating;

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

    #endregion

    #region Tick 桶登记

    public void NotifyChanged(Item item)
    {
        if (item != null && locations.ContainsKey(item))
            dirtyItems.Add(item);
    }

    public void NotifyMoved(Item item) => simulationRanges.NotifyMoved(item);

    /// <summary>换代和休眠边界重置时钟，活跃档间切换保留已累计的模拟时间。</summary>
    public void Register(Item item)
    {
        if (item == null)
        {
            Remove(item);
            return;
        }

        ItemTickTier tier = item.GetTickTier();
        int bucket = tier == ItemTickTier.EveryFrame ? -1 :
            tier == ItemTickTier.Dormant && !item.TryGetComponent<Rigidbody2D>(out _)
                ? -1 : (item.GetInstanceID() & int.MaxValue) % BucketCount;
        bool alreadyRegistered = locations.TryGetValue(item, out Location existing);
        bool newGeneration = !alreadyRegistered || existing.Entry.Generation != item.RuntimeGeneration;
        bool dormantBoundary = alreadyRegistered &&
            (existing.Tier == ItemTickTier.Dormant) != (tier == ItemTickTier.Dormant);
        if (!newGeneration && existing.Tier == tier && existing.Bucket == bucket)
        {
            simulationRanges.Register(existing.Entry, tier == ItemTickTier.EveryFrame);
            return;
        }

        if (registrationSequence == ulong.MaxValue)
            throw new InvalidOperationException("Item Tick 注册版本已耗尽。");
        ulong token = ++registrationSequence;
        if (alreadyRegistered)
            Remove(item);

        Location location = existing ?? new Location();
        location.Entry = new ScheduledEntry(item, token);
        location.Tier = tier;
        location.Bucket = bucket;
        location.Slot = -1;
        List<ScheduledEntry> target = GetBucket(location);
        if (target != null)
        {
            location.Slot = target.Count;
            target.Add(location.Entry);
        }
        locations.Add(item, location);
        simulationRanges.Register(location.Entry, tier == ItemTickTier.EveryFrame);

        if (newGeneration || dormantBoundary)
            item.ResetScheduledTickClock(tier == ItemTickTier.EveryFrame || tier == ItemTickTier.Dormant
                ? -1f : Time.time);
    }

    public void Remove(Item item)
    {
        if (ReferenceEquals(item, null))
            return;

        if (locations.Remove(item, out Location location))
        {
            List<ScheduledEntry> bucket = GetBucket(location);
            if (bucket != null)
            {
                int last = bucket.Count - 1;
                if (location.Slot != last)
                {
                    ScheduledEntry moved = bucket[last];
                    bucket[location.Slot] = moved;
                    locations[moved.Item].Slot = location.Slot;
                }
                bucket.RemoveAt(last);
            }
        }
        dirtyItems.Remove(item);
        simulationRanges.Remove(item);
    }

    private List<ScheduledEntry> GetBucket(Location location)
    {
        switch (location.Tier)
        {
            case ItemTickTier.EveryFrame: return everyFrameItems;
            case ItemTickTier.Fast: return fastBuckets[location.Bucket];
            case ItemTickTier.Normal: return normalBuckets[location.Bucket];
            case ItemTickTier.Slow: return slowBuckets[location.Bucket];
            case ItemTickTier.Dormant when location.Bucket >= 0:
                return dormantPhysicsBuckets[location.Bucket];
            default: return null;
        }
    }

    #endregion

    #region 帧边界与重建

    public void Update(float deltaTime, Action<Item> beforeEveryFrameTick,
        IReadOnlyList<Transform> players, WorldTopologyDomain topology)
    {
        if (updating)
            throw new InvalidOperationException("Item Tick 调度不允许重入。");

        updating = true;
        try
        {
            FlushDirty();
            simulationRanges.BeginFrame(players, topology);
            // 本轮调度中新增或重新注册的版本统一从下一帧生效，不能进入后续低频桶。
            currentUpdateToken = registrationSequence;
            ApplySimulationRangeTransitions();
            float currentTime = Time.time;
            simulationRanges.CollectEveryFrame(tickSnapshot, currentTime);
            for (int i = 0; i < tickSnapshot.Count; i++)
            {
                ScheduledEntry entry = tickSnapshot[i];
                try
                {
                    if (!CanTick(entry)) continue;
                    Item item = entry.Item;
                    if (!item.isActiveAndEnabled)
                    {
                        item.ResetScheduledTickClock(-1f);
                        continue;
                    }

                    if (!item.ShouldUseSimulationRange())
                    {
                        item.SetSimulationRangePaused(false);
                        if (!CanTick(entry)) continue;
                        beforeEveryFrameTick?.Invoke(item);
                        if (CanTick(entry) && item.isActiveAndEnabled)
                        {
                            item.TickUnthrottledAt(currentTime, deltaTime);
                            if (CanTick(entry))
                            {
                                simulationRanges.AcknowledgeTier(entry, SimulationRangeTier.Near);
                                simulationRanges.NotifyMoved(item);
                            }
                        }
                        continue;
                    }

                    SimulationRangeTier tier = ResolveTier(item);
                    if (!CanTick(entry)) continue;
                    item.SetSimulationRangePaused(tier == SimulationRangeTier.Stopped);
                    if (!CanTick(entry)) continue;
                    if (tier == SimulationRangeTier.Stopped)
                    {
                        item.ResetScheduledTickClock(-1f);
                        continue;
                    }

                    float interval = SimulationRangePreferences.TickInterval(tier);
                    if (!item.IsEveryFrameTickDue(currentTime, interval))
                    {
                        simulationRanges.AcknowledgeTier(entry, tier);
                        continue;
                    }
                    beforeEveryFrameTick?.Invoke(item);
                    if (CanTick(entry) && item.isActiveAndEnabled)
                    {
                        item.TickEveryFrameAt(currentTime, interval);
                        if (CanTick(entry))
                        {
                            simulationRanges.AcknowledgeTier(entry, tier);
                            simulationRanges.NotifyMoved(item);
                        }
                    }
                }
                finally
                {
                    simulationRanges.CompleteEveryFrame(entry);
                }
            }

            ProcessTier(fastBuckets, ref fastTimer, ref fastCursor, FastSlice, deltaTime);
            ProcessTier(normalBuckets, ref normalTimer, ref normalCursor, NormalSlice, deltaTime);
            ProcessTier(slowBuckets, ref slowTimer, ref slowCursor, SlowSlice, deltaTime);
            ProcessDormantPhysics(deltaTime);
        }
        finally
        {
            simulationRanges.EndFrame();
            tickSnapshot.Clear();
            updating = false;
        }
    }

    /// <summary>加载页期间暂停调度并重置时间基准，恢复后不补算暂停期间的 Tick。</summary>
    public void Pause(IReadOnlyList<Item> runtimeItems)
    {
        fastTimer = normalTimer = slowTimer = dormantPhysicsTimer = 0f;
        fastCursor = normalCursor = slowCursor = dormantPhysicsCursor = -1;
        simulationRanges.Pause();
        for (int i = 0; i < runtimeItems.Count; i++)
        {
            Item item = runtimeItems[i];
            if (item != null)
                item.ResetScheduledTickClock(-1f);
        }
    }

    /// <summary>按当前注册集合差量对齐，维护清理不会清零未改变实体的时钟。</summary>
    public void Rebuild(IReadOnlyList<Item> runtimeItems)
    {
        rebuildMembers.Clear();
        for (int i = 0; i < runtimeItems.Count; i++)
            if (runtimeItems[i] != null)
                rebuildMembers.Add(runtimeItems[i]);

        maintenanceSnapshot.Clear();
        foreach (Item item in locations.Keys)
            if (!rebuildMembers.Contains(item))
                maintenanceSnapshot.Add(item);
        for (int i = 0; i < maintenanceSnapshot.Count; i++)
            Remove(maintenanceSnapshot[i]);
        maintenanceSnapshot.Clear();

        for (int i = 0; i < runtimeItems.Count; i++)
            if (runtimeItems[i] != null)
                Register(runtimeItems[i]);
        rebuildMembers.Clear();
    }

    private void FlushDirty()
    {
        if (dirtyItems.Count == 0) return;
        maintenanceSnapshot.Clear();
        foreach (Item item in dirtyItems)
            maintenanceSnapshot.Add(item);
        dirtyItems.Clear();
        for (int i = 0; i < maintenanceSnapshot.Count; i++)
        {
            Item item = maintenanceSnapshot[i];
            if (item == null)
                Remove(item);
            else if (locations.ContainsKey(item))
                Register(item);
        }
        maintenanceSnapshot.Clear();
    }

    private bool CanTick(ScheduledEntry entry)
    {
        Item item = entry.Item;
        return entry.Token <= currentUpdateToken && item != null &&
               item.IsInitialized && !item.DestructionHandled && !item.IsInPool &&
               item.RuntimeGeneration == entry.Generation &&
               locations.TryGetValue(item, out Location current) && current.Entry.Token == entry.Token;
    }

    private void CopyTickSnapshot(List<ScheduledEntry> source)
    {
        tickSnapshot.Clear();
        tickSnapshot.AddRange(source);
    }

    private void ApplySimulationRangeTransitions()
    {
        simulationRanges.CopyTransitions(rangeTransitions);
        int cursor = 0;
        try
        {
            for (; cursor < rangeTransitions.Count; cursor++)
            {
                ItemSimulationRangeIndex.Transition transition = rangeTransitions[cursor];
                ScheduledEntry entry = transition.Entry;
                if (!CanTick(entry)) continue;
                SimulationRangeTier tier = ResolveTier(entry.Item);
                if (!CanTick(entry)) continue;
                entry.Item.SetSimulationRangePaused(tier == SimulationRangeTier.Stopped);
                if (CanTick(entry) && (transition.ResetClock || tier == SimulationRangeTier.Stopped))
                    entry.Item.ResetScheduledTickClock(-1f);
            }
        }
        finally
        {
            for (; cursor < rangeTransitions.Count; cursor++)
                simulationRanges.DeferTransition(rangeTransitions[cursor]);
            rangeTransitions.Clear();
        }
    }

    #endregion

    #region 分桶执行

    private static List<ScheduledEntry>[] CreateBuckets()
    {
        List<ScheduledEntry>[] buckets = new List<ScheduledEntry>[BucketCount];
        for (int i = 0; i < buckets.Length; i++)
            buckets[i] = new List<ScheduledEntry>(16);
        return buckets;
    }

    private static int CountItems(List<ScheduledEntry>[] buckets)
    {
        int count = 0;
        for (int i = 0; i < buckets.Length; i++)
            count += buckets[i].Count;
        return count;
    }

    private void ProcessTier(List<ScheduledEntry>[] buckets, ref float timer, ref int cursor,
        float slice, float deltaTime)
    {
        timer += deltaTime;
        int elapsedSlices = Mathf.FloorToInt(timer / slice);
        if (elapsedSlices <= 0) return;

        int slicesToProcess = Mathf.Min(elapsedSlices, buckets.Length);
        float currentTime = Time.time;
        for (int sliceIndex = 0; sliceIndex < slicesToProcess; sliceIndex++)
        {
            cursor = (cursor + 1) % buckets.Length;
            CopyTickSnapshot(buckets[cursor]);
            for (int i = 0; i < tickSnapshot.Count; i++)
            {
                ScheduledEntry entry = tickSnapshot[i];
                if (!CanTick(entry)) continue;
                Item item = entry.Item;
                if (!item.isActiveAndEnabled)
                {
                    item.ResetScheduledTickClock(-1f);
                    continue;
                }

                SimulationRangeTier tier = ResolveTier(item);
                if (!CanTick(entry)) continue;
                item.SetSimulationRangePaused(tier == SimulationRangeTier.Stopped);
                if (!CanTick(entry) || !item.isActiveAndEnabled) continue;
                if (tier == SimulationRangeTier.Stopped)
                {
                    item.ResetScheduledTickClock(-1f);
                    continue;
                }
                if (item.IsScheduledTickDue(currentTime, SimulationRangePreferences.TickInterval(tier)))
                {
                    item.TickScheduled(currentTime);
                    if (CanTick(entry)) simulationRanges.NotifyMoved(item);
                }
            }
        }
        timer = elapsedSlices >= buckets.Length ? 0f : timer - slicesToProcess * slice;
    }

    private SimulationRangeTier ResolveTier(Item item) => simulationRanges.ResolveTier(item);

    /// <summary>没有玩法 Tick 但仍带刚体的实体分桶检查，远处也要停止物理模拟。</summary>
    private void ProcessDormantPhysics(float deltaTime)
    {
        dormantPhysicsTimer += deltaTime;
        int elapsedSlices = Mathf.FloorToInt(dormantPhysicsTimer / DormantPhysicsSlice);
        if (elapsedSlices <= 0) return;

        int slicesToProcess = Mathf.Min(elapsedSlices, dormantPhysicsBuckets.Length);
        for (int sliceIndex = 0; sliceIndex < slicesToProcess; sliceIndex++)
        {
            dormantPhysicsCursor = (dormantPhysicsCursor + 1) % dormantPhysicsBuckets.Length;
            CopyTickSnapshot(dormantPhysicsBuckets[dormantPhysicsCursor]);
            for (int i = 0; i < tickSnapshot.Count; i++)
            {
                ScheduledEntry entry = tickSnapshot[i];
                if (!CanTick(entry) || !entry.Item.isActiveAndEnabled) continue;
                SimulationRangeTier tier = ResolveTier(entry.Item);
                if (CanTick(entry))
                    entry.Item.SetSimulationRangePaused(tier == SimulationRangeTier.Stopped);
            }
        }
        dormantPhysicsTimer = elapsedSlices >= dormantPhysicsBuckets.Length
            ? 0f : dormantPhysicsTimer - slicesToProcess * DormantPhysicsSlice;
    }

    #endregion
}
