using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

/// <summary>按逻辑世界和区域维护模拟资格，只有距离边界逐实体精算，中远档按原有时钟取出到期成员。</summary>
internal sealed class ItemSimulationRangeIndex
{
    #region 区域与成员状态

    private const int RegionSize = 16;
    internal const float RegionRadius = 11.31381f;
    private const float RegionShortcutMargin = 0.02f;
    private const float ClockTolerance = 0.0005f;

    private readonly struct RegionKey : IEquatable<RegionKey>
    {
        public readonly int WorldIdentity;
        public readonly int X;
        public readonly int Y;

        public RegionKey(int worldIdentity, float2 position)
        {
            WorldIdentity = worldIdentity;
            X = (int)math.floor(position.x / RegionSize);
            Y = (int)math.floor(position.y / RegionSize);
        }

        public bool Equals(RegionKey other) => WorldIdentity == other.WorldIdentity && X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is RegionKey other && Equals(other);
        public override int GetHashCode() => unchecked((WorldIdentity * 397 ^ X) * 397 ^ Y);
        public float2 Center => new((X + 0.5f) * RegionSize, (Y + 0.5f) * RegionSize);
    }

    private readonly struct PlayerPosition
    {
        public readonly Transform Transform;
        public readonly int WorldIdentity;
        public readonly float2 Position;

        public PlayerPosition(Transform player, WorldTopologyDomain topology)
        {
            Transform = player;
            Item playerItem = player.GetComponent<Item>();
            WorldIdentity = playerItem != null ? GameplayWorldRegistry.GetItemWorldIdentity(playerItem) :
                GameplayWorldRegistry.TryResolveUnambiguousWorldKeyForScene(player.gameObject.scene, out string worldKey)
                    ? GameplayWorldRegistry.GetOrCreateRuntimeWorldIdentity(worldKey) : 0;
            Vector3 position = player.position;
            Position = topology.Normalize(new float2(position.x, position.y));
        }

        public bool Equals(PlayerPosition other) => ReferenceEquals(Transform, other.Transform) &&
            WorldIdentity == other.WorldIdentity && math.all(Position == other.Position);
    }

    private sealed class Region
    {
        public readonly RegionKey Key;
        public readonly List<Member> Members = new(8);
        public int Slot;
        public ulong Version;
        public bool Boundary;
        public SimulationRangeTier Tier;

        public Region(RegionKey key, int slot) { Key = key; Slot = slot; }
    }

    private sealed class Member
    {
        public ItemTickScheduler.ScheduledEntry Entry;
        public Region Region;
        public int RegionSlot = -1;
        public int NearSlot = -1;
        public int HeapSlot = -1;
        public bool EveryFrame;
        public bool HasTier;
        public bool ForceDue;
        public SimulationRangeTier Tier;
        public float DueAt;
        // 一次 ResolveTier 之后的真实位置快照。区域版本由 BeginFrame 统一更新，
        // 只有同区域、同位置、同逻辑世界且距离资格没有改变，才能跳过重复寻址。
        public bool HasMembershipSnapshot;
        public bool WasRangeRestricted;
        // 距离和区域寻址只使用 XY；渲染层级的 Z 变化不应该使模拟身份缓存失效。
        public float2 LastPosition;
        public int LastWorldIdentity;
    }

    internal readonly struct Transition
    {
        public readonly ItemTickScheduler.ScheduledEntry Entry;
        public readonly bool ResetClock;

        public Transition(ItemTickScheduler.ScheduledEntry entry, bool resetClock)
        {
            Entry = entry;
            ResetClock = resetClock;
        }
    }

    private readonly Dictionary<Item, Member> members = new(ItemReferenceComparer.Instance);
    private readonly Dictionary<RegionKey, Region> regions = new();
    private readonly List<Region> occupiedRegions = new(32);
    private readonly List<Member> nearMembers = new(64);
    private readonly List<Member> dueHeap = new(64);
    private readonly List<ItemTickScheduler.ScheduledEntry> dueSnapshot = new(64);
    private readonly List<PlayerPosition> players = new(4);
    private readonly List<PlayerPosition> nextPlayers = new(4);
    private readonly List<Transition> pendingTransitions = new(32);
    private readonly Dictionary<Item, int> transitionSlots = new(ItemReferenceComparer.Instance);
    private WorldTopologyDomain topology;
    private ulong contextVersion;
    private bool contextReady;
    private bool contextInvalidated;
    private int nearRadius;
    private int middleRadius;
    private int farRadius;

    // 由隔离规则测试读取；正式发布包无需累计诊断数字。
    internal long MembershipRecalculations { get; private set; }
    internal long MembershipCacheHits { get; private set; }

    #endregion

    #region 登记与移动边界

    public void Register(ItemTickScheduler.ScheduledEntry entry, bool everyFrame)
    {
        if (members.TryGetValue(entry.Item, out Member member))
        {
            if (member.Entry.Generation != entry.Generation || member.Entry.Token != entry.Token)
            {
                Remove(entry.Item);
                member = null;
            }
        }
        if (member == null)
        {
            member = new Member { Entry = entry, EveryFrame = everyFrame };
            members.Add(entry.Item, member);
        }
        else if (member.EveryFrame != everyFrame)
        {
            RemoveActive(member);
            member.EveryFrame = everyFrame;
            member.HasTier = false;
        }
        RefreshMembership(member);
    }

    public void Remove(Item item)
    {
        if (ReferenceEquals(item, null) || !members.Remove(item, out Member member)) return;
        RemoveActive(member);
        RemoveFromRegion(member);
        if (!transitionSlots.Remove(item, out int slot)) return;
        int last = pendingTransitions.Count - 1;
        if (slot != last)
        {
            Transition moved = pendingTransitions[last];
            pendingTransitions[slot] = moved;
            transitionSlots[moved.Entry.Item] = slot;
        }
        pendingTransitions.RemoveAt(last);
    }

    public void NotifyMoved(Item item)
    {
        if (item != null && members.TryGetValue(item, out Member member) &&
            member.Entry.Generation == item.RuntimeGeneration)
            RefreshMembership(member);
    }

    private void RefreshMembership(Member member)
    {
        Item item = member.Entry.Item;
        int worldIdentity = GameplayWorldRegistry.GetItemWorldIdentity(item);
        if (worldIdentity == 0)
        {
            RemoveFromRegion(member);
            SetTier(member, SimulationRangeTier.Stopped);
            member.HasMembershipSnapshot = false;
            return;
        }
        bool restricted = item.ShouldUseSimulationRange();
        if (!restricted)
        {
            // Player / 手持物无距离限制，位置变化不会改变其模拟档。
            if (member.HasMembershipSnapshot && !member.WasRangeRestricted &&
                member.Region == null && member.HasTier && member.Tier == SimulationRangeTier.Near)
            {
                RestoreMissingActiveSlot(member);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MembershipCacheHits++;
#endif
                return;
            }
            RemoveFromRegion(member);
            SetTier(member, SimulationRangeTier.Near);
            member.HasMembershipSnapshot = true;
            member.WasRangeRestricted = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MembershipRecalculations++;
#endif
            return;
        }

        // 单轮 Tick 会先 ResolveTier 后 NotifyMoved；静止单位不再重复规范化坐标/查区域。
        // 玩家移动时 BeginFrame 已更新 Region.Version 和需要变化的成员档位。
        Vector3 position = item.transform.position;
        float2 positionXY = new(position.x, position.y);
        if (member.HasMembershipSnapshot && member.WasRangeRestricted &&
            member.Region != null && member.Region.Version == contextVersion &&
            member.HasTier && math.all(member.LastPosition == positionXY) && member.LastWorldIdentity == worldIdentity)
        {
            RestoreMissingActiveSlot(member);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MembershipCacheHits++;
#endif
            return;
        }

        RegionKey key = new(worldIdentity, topology.Normalize(positionXY));
        if (member.Region == null || !member.Region.Key.Equals(key))
        {
            RemoveFromRegion(member);
            if (!regions.TryGetValue(key, out Region region))
            {
                region = new Region(key, occupiedRegions.Count);
                regions.Add(key, region);
                occupiedRegions.Add(region);
            }
            member.Region = region;
            member.RegionSlot = region.Members.Count;
            region.Members.Add(member);
        }

        Region current = member.Region;
        RefreshRegion(current);
        SetTier(member, current.Boundary ? ResolveExact(position, worldIdentity) : current.Tier);
        member.HasMembershipSnapshot = true;
        member.WasRangeRestricted = true;
        member.LastPosition = positionXY;
        member.LastWorldIdentity = worldIdentity;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MembershipRecalculations++;
#endif
    }

    /// <summary>缓存命中时仍保留 SetTier 的原有兜底：异常收尾后补回脱离队列的到期成员。</summary>
    private void RestoreMissingActiveSlot(Member member)
    {
        if (member.EveryFrame && member.Tier != SimulationRangeTier.Stopped &&
            member.NearSlot < 0 && member.HeapSlot < 0)
            SetTier(member, member.Tier);
    }

    private void RemoveFromRegion(Member member)
    {
        Region region = member.Region;
        if (region == null) return;
        int last = region.Members.Count - 1;
        if (member.RegionSlot != last)
        {
            Member moved = region.Members[last];
            region.Members[member.RegionSlot] = moved;
            moved.RegionSlot = member.RegionSlot;
        }
        region.Members.RemoveAt(last);
        member.Region = null;
        member.RegionSlot = -1;
        if (region.Members.Count != 0) return;
        regions.Remove(region.Key);
        last = occupiedRegions.Count - 1;
        if (region.Slot != last)
        {
            Region moved = occupiedRegions[last];
            occupiedRegions[region.Slot] = moved;
            moved.Slot = region.Slot;
        }
        occupiedRegions.RemoveAt(last);
    }

    #endregion

    #region 区域资格与边界精算

    public void BeginFrame(IReadOnlyList<Transform> playerTransforms, WorldTopologyDomain currentTopology)
    {
        bool topologyChanged = topology.IsWrappedValue != currentTopology.IsWrappedValue ||
            math.any(topology.Min != currentTopology.Min) || math.any(topology.Span != currentTopology.Span);
        topology = currentTopology;
        nextPlayers.Clear();
        if (playerTransforms != null)
            for (int i = 0; i < playerTransforms.Count; i++)
                if (playerTransforms[i] != null)
                {
                    var player = new PlayerPosition(playerTransforms[i], topology);
                    if (player.WorldIdentity != 0) nextPlayers.Add(player);
                }

        bool changed = !contextReady || contextInvalidated || topologyChanged || players.Count != nextPlayers.Count;
        for (int i = 0; !changed && i < players.Count; i++)
            changed = !players[i].Equals(nextPlayers[i]);
        int near = SimulationRangePreferences.NearRadius;
        int middle = SimulationRangePreferences.MiddleRadius;
        int far = SimulationRangePreferences.FarRadius;
        changed |= near != nearRadius || middle != middleRadius || far != farRadius;
        if (!changed) return;

        players.Clear();
        players.AddRange(nextPlayers);
        nearRadius = near;
        middleRadius = middle;
        farRadius = far;
        contextReady = true;
        contextInvalidated = false;
        if (contextVersion == ulong.MaxValue)
            throw new InvalidOperationException("Item 模拟区域版本已耗尽。");
        contextVersion++;

        if (topologyChanged)
        {
            // 拓扑变化时重新归一化位置，局部镜像不会形成重复逻辑区域。
            regions.Clear();
            occupiedRegions.Clear();
            foreach (Member member in members.Values)
            {
                member.Region = null;
                member.RegionSlot = -1;
                if (member.Entry.Item != null) RefreshMembership(member);
            }
        }

        for (int i = 0; i < occupiedRegions.Count; i++)
        {
            Region region = occupiedRegions[i];
            bool oldBoundary = region.Boundary;
            SimulationRangeTier oldTier = region.Tier;
            RefreshRegion(region);
            if (!oldBoundary && !region.Boundary && oldTier == region.Tier) continue;
            for (int j = 0; j < region.Members.Count; j++)
            {
                Member member = region.Members[j];
                if (member.Entry.Item != null)
                    SetTier(member, region.Boundary ? ResolveExact(member.Entry.Item) : region.Tier);
            }
        }
    }

    private void RefreshRegion(Region region)
    {
        if (region.Version == contextVersion && contextReady) return;
        region.Version = contextVersion;
        if (!contextReady || players.Count == 0)
        {
            region.Boundary = false;
            region.Tier = SimulationRangeTier.Near;
            return;
        }

        float nearestCenter = float.PositiveInfinity;
        for (int i = 0; i < players.Count; i++)
            if (players[i].WorldIdentity == region.Key.WorldIdentity)
                nearestCenter = math.min(nearestCenter, topology.SqrDistance(region.Key.Center, players[i].Position));
        // 明显位于近档或最远档外的整块区域直接判定，边界沿用原始开平方精算。
        if (TryResolveUnambiguousRegionTier(nearestCenter, nearRadius, farRadius, out SimulationRangeTier settled))
        {
            region.Boundary = false;
            region.Tier = settled;
            return;
        }
        float distance = math.sqrt(nearestCenter);
        float lower = math.max(0f, distance - RegionRadius);
        float upper = distance + RegionRadius;
        SimulationRangeTier closest = ResolveDistance(lower * lower);
        SimulationRangeTier farthest = ResolveDistance(upper * upper);
        region.Boundary = closest != farthest;
        region.Tier = closest;
    }

    #region 区域距离快速判档

    /// <summary>仅远离阈值时跳过开平方；预留小幅浮点余量，靠近边界仍使用原判断。</summary>
    internal static bool TryResolveUnambiguousRegionTier(float squaredDistance, int near, int far,
        out SimulationRangeTier tier)
    {
        float certainlyNear = near - RegionRadius - RegionShortcutMargin;
        if (certainlyNear > 0f && squaredDistance >= 0f && squaredDistance < certainlyNear * certainlyNear)
        {
            tier = SimulationRangeTier.Near;
            return true;
        }

        float certainlyStopped = far + RegionRadius + RegionShortcutMargin;
        if (squaredDistance > certainlyStopped * certainlyStopped)
        {
            tier = SimulationRangeTier.Stopped;
            return true;
        }

        tier = default;
        return false;
    }

    #endregion

    public SimulationRangeTier ResolveTier(Item item)
    {
        if (!members.TryGetValue(item, out Member member)) return ResolveExact(item);
        RefreshMembership(member);
        return member.Tier;
    }

    private SimulationRangeTier ResolveExact(Item item)
    {
        int worldIdentity = GameplayWorldRegistry.GetItemWorldIdentity(item);
        if (worldIdentity == 0) return SimulationRangeTier.Stopped;
        if (!item.ShouldUseSimulationRange() || players.Count == 0) return SimulationRangeTier.Near;
        Vector3 position = item.transform.position;
        return ResolveExact(position, worldIdentity);
    }

    private SimulationRangeTier ResolveExact(Vector3 position, int worldIdentity)
    {
        if (players.Count == 0) return SimulationRangeTier.Near;
        float2 entityPosition = new(position.x, position.y);
        float distanceSquared = float.PositiveInfinity;
        for (int i = 0; i < players.Count; i++)
            if (players[i].WorldIdentity == worldIdentity)
                distanceSquared = math.min(distanceSquared, topology.SqrDistance(entityPosition, players[i].Position));
        return ResolveDistance(distanceSquared);
    }

    private SimulationRangeTier ResolveDistance(float squaredDistance)
    {
        if (squaredDistance <= nearRadius * nearRadius) return SimulationRangeTier.Near;
        if (squaredDistance <= middleRadius * middleRadius) return SimulationRangeTier.Middle;
        if (squaredDistance <= farRadius * farRadius) return SimulationRangeTier.Far;
        return SimulationRangeTier.Stopped;
    }

    private void SetTier(Member member, SimulationRangeTier tier)
    {
        bool changed = !member.HasTier || member.Tier != tier;
        if (!changed)
        {
            if (member.EveryFrame && member.NearSlot < 0 && member.HeapSlot < 0 && tier != SimulationRangeTier.Stopped)
                AddActive(member, false);
            return;
        }
        bool stoppedBoundary = member.HasTier &&
            (member.Tier == SimulationRangeTier.Stopped) != (tier == SimulationRangeTier.Stopped);
        RemoveActive(member);
        bool initial = !member.HasTier;
        member.Tier = tier;
        member.HasTier = true;
        member.ForceDue = true;
        AddActive(member, true);
        if (initial || stoppedBoundary) DeferTransition(new Transition(member.Entry, stoppedBoundary));
    }

    #endregion

    #region 到期成员与休眠边界

    public void CopyTransitions(List<Transition> snapshot)
    {
        snapshot.Clear();
        snapshot.AddRange(pendingTransitions);
        pendingTransitions.Clear();
        transitionSlots.Clear();
    }

    public void DeferTransition(Transition transition)
    {
        ItemTickScheduler.ScheduledEntry entry = transition.Entry;
        if (!members.TryGetValue(entry.Item, out Member member) || member.Entry.Token != entry.Token ||
            member.Entry.Generation != entry.Generation) return;
        if (transitionSlots.TryGetValue(entry.Item, out int slot))
            pendingTransitions[slot] = new Transition(entry,
                transition.ResetClock || pendingTransitions[slot].ResetClock);
        else
        {
            transitionSlots.Add(entry.Item, pendingTransitions.Count);
            pendingTransitions.Add(transition);
        }
    }

    public void CollectEveryFrame(List<ItemTickScheduler.ScheduledEntry> snapshot, float currentTime)
    {
        snapshot.Clear();
        dueSnapshot.Clear();
        for (int i = 0; i < nearMembers.Count; i++) snapshot.Add(nearMembers[i].Entry);
        while (dueHeap.Count != 0 && dueHeap[0].DueAt <= currentTime + ClockTolerance)
        {
            Member member = dueHeap[0];
            RemoveHeap(member);
            snapshot.Add(member.Entry);
            dueSnapshot.Add(member.Entry);
        }
    }

    public void CompleteEveryFrame(ItemTickScheduler.ScheduledEntry entry)
    {
        if (entry.Item == null || entry.Item.RuntimeGeneration != entry.Generation ||
            !members.TryGetValue(entry.Item, out Member member) || member.Entry.Token != entry.Token ||
            member.Entry.Generation != entry.Generation || !member.EveryFrame ||
            member.Tier == SimulationRangeTier.Near || member.Tier == SimulationRangeTier.Stopped) return;
        RemoveHeap(member);
        member.DueAt = member.ForceDue ? -1f : entry.Item.NextSimulationTickTime;
        AddHeap(member);
    }

    public void AcknowledgeTier(ItemTickScheduler.ScheduledEntry entry, SimulationRangeTier tier)
    {
        if (members.TryGetValue(entry.Item, out Member member) && member.Entry.Token == entry.Token &&
            member.Entry.Generation == entry.Generation && member.Tier == tier)
            member.ForceDue = false;
    }

    public void EndFrame()
    {
        // 某个模块抛异常时也归还尚未执行的到期成员，避免它们永久脱离调度。
        for (int i = 0; i < dueSnapshot.Count; i++)
            if (members.TryGetValue(dueSnapshot[i].Item, out Member member) && member.HeapSlot < 0)
                CompleteEveryFrame(dueSnapshot[i]);
        dueSnapshot.Clear();
    }

    public void Pause()
    {
        contextInvalidated = true;
        for (int i = 0; i < dueHeap.Count; i++) dueHeap[i].DueAt = -1f;
    }

    private void AddActive(Member member, bool immediatelyDue)
    {
        if (!member.EveryFrame || member.Tier == SimulationRangeTier.Stopped) return;
        if (member.Tier == SimulationRangeTier.Near)
        {
            member.NearSlot = nearMembers.Count;
            nearMembers.Add(member);
            return;
        }
        member.DueAt = immediatelyDue || member.ForceDue ? -1f : member.Entry.Item.NextSimulationTickTime;
        AddHeap(member);
    }

    private void RemoveActive(Member member)
    {
        if (member.NearSlot >= 0)
        {
            int last = nearMembers.Count - 1;
            if (member.NearSlot != last)
            {
                Member moved = nearMembers[last];
                nearMembers[member.NearSlot] = moved;
                moved.NearSlot = member.NearSlot;
            }
            nearMembers.RemoveAt(last);
            member.NearSlot = -1;
        }
        RemoveHeap(member);
    }

    #endregion

    #region 到期小根堆

    private void AddHeap(Member member)
    {
        if (member.HeapSlot >= 0) return;
        member.HeapSlot = dueHeap.Count;
        dueHeap.Add(member);
        SiftUp(member.HeapSlot);
    }

    private void RemoveHeap(Member member)
    {
        int slot = member.HeapSlot;
        if (slot < 0) return;
        int last = dueHeap.Count - 1;
        member.HeapSlot = -1;
        if (slot == last)
        {
            dueHeap.RemoveAt(last);
            return;
        }
        Member moved = dueHeap[last];
        dueHeap[slot] = moved;
        moved.HeapSlot = slot;
        dueHeap.RemoveAt(last);
        if (slot > 0 && moved.DueAt < dueHeap[(slot - 1) / 2].DueAt) SiftUp(slot);
        else SiftDown(slot);
    }

    private void SiftUp(int slot)
    {
        while (slot > 0)
        {
            int parent = (slot - 1) / 2;
            if (dueHeap[parent].DueAt <= dueHeap[slot].DueAt) break;
            SwapHeap(parent, slot);
            slot = parent;
        }
    }

    private void SiftDown(int slot)
    {
        while (slot * 2 + 1 < dueHeap.Count)
        {
            int child = slot * 2 + 1;
            if (child + 1 < dueHeap.Count && dueHeap[child + 1].DueAt < dueHeap[child].DueAt) child++;
            if (dueHeap[slot].DueAt <= dueHeap[child].DueAt) break;
            SwapHeap(slot, child);
            slot = child;
        }
    }

    private void SwapHeap(int left, int right)
    {
        Member member = dueHeap[left];
        dueHeap[left] = dueHeap[right];
        dueHeap[right] = member;
        dueHeap[left].HeapSlot = left;
        dueHeap[right].HeapSlot = right;
    }

    #endregion
}
