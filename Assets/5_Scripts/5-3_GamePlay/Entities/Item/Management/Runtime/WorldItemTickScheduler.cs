using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class WorldItemTickScheduler
{
    #region 每个世界独立的模拟窗口与调度时钟
    private sealed class Group
    {
        public readonly ItemTickScheduler Scheduler = new();
        public readonly List<Transform> Players = new();
        public readonly List<Item> Items = new();
    }
    private readonly Dictionary<string, Group> groups = new(StringComparer.Ordinal);
    private readonly Dictionary<Item, (string Key, ulong Revision)> owners = new();
    private readonly List<Group> dispatch = new();
    private readonly List<Item> removed = new();
    private readonly List<(Transform Player, string WorldKey)> playerWorlds = new();
    public int RegisteredCount => owners.Count;
    internal int GetRegisteredCountForWorld(string key) =>
        key != null && groups.TryGetValue(key, out Group group) ? group.Items.Count : 0;
    public int EveryFrameCount { get { int total = 0; foreach (Group group in groups.Values) total += group.Scheduler.EveryFrameCount; return total; } }
    public int FastCount { get { int total = 0; foreach (Group group in groups.Values) total += group.Scheduler.FastCount; return total; } }
    public int NormalCount { get { int total = 0; foreach (Group group in groups.Values) total += group.Scheduler.NormalCount; return total; } }
    public int SlowCount { get { int total = 0; foreach (Group group in groups.Values) total += group.Scheduler.SlowCount; return total; } }

    public void Register(Item item)
    {
        if (item == null) return;
        if (!GameplayWorldRegistry.TryGetItemWorldOwnership(item, out var world)) { Remove(item); return; }
        string key = world.WorldKey;
        ulong revision = world.Revision;
        if (owners.TryGetValue(item, out var previous))
        {
            if (previous.Key == key)
            {
                owners[item] = (key, revision);
                groups[key].Scheduler.Register(item);
                return;
            }
            groups[previous.Key].Scheduler.Remove(item); groups[previous.Key].Items.Remove(item);
        }
        if (!groups.TryGetValue(key, out Group group)) groups.Add(key, group = new Group());
        owners[item] = (key, revision); group.Items.Add(item); group.Scheduler.Register(item);
    }
    public void Remove(Item item)
    {
        if (ReferenceEquals(item, null) || !owners.Remove(item, out var owner)) return;
        groups[owner.Key].Scheduler.Remove(item); groups[owner.Key].Items.Remove(item);
    }
    public void NotifyChanged(Item item)
    { if (item == null) return; Register(item); if (owners.TryGetValue(item, out var owner)) groups[owner.Key].Scheduler.NotifyChanged(item); }
    public void NotifyMoved(Item item)
    {
        if (item == null) return;
        // 逻辑世界变更即使没有切换场景也要重新进入所属调度组。
        if (!owners.TryGetValue(item, out var owner) || owner.Revision != GameplayWorldRegistry.GetItemWorldRevision(item))
        {
            Register(item);
            if (!owners.TryGetValue(item, out owner)) return;
        }
        groups[owner.Key].Scheduler.NotifyMoved(item);
    }
    public void Pause(IReadOnlyList<Item> items)
    { foreach (Group group in groups.Values) group.Scheduler.Pause(group.Items); }
    public void Rebuild(IReadOnlyList<Item> items)
    {
        var members = new HashSet<Item>();
        for (int i = 0; i < items.Count; i++) if (items[i] != null) { members.Add(items[i]); Register(items[i]); }
        removed.Clear(); foreach (Item item in owners.Keys) if (!members.Contains(item)) removed.Add(item);
        foreach (Item item in removed) Remove(item); removed.Clear();
    }

    // 同坐标的其它维度玩家不扩大本世界的模拟范围，也不借用本机地表拓扑。
    public void Update(float deltaTime, Action<Item> beforeTick, IReadOnlyList<Transform> players, WorldTopologyDomain fallback)
    {
        playerWorlds.Clear();
        for (int i = 0; i < players.Count; i++)
        {
            Transform player = players[i];
            if (player == null) continue;
            Item item = player.GetComponent<Item>();
            string world = item != null ? GameplayWorldRegistry.GetItemWorldKey(item) :
                GameplayWorldRegistry.TryResolveUnambiguousWorldKeyForScene(player.gameObject.scene, out string key) ? key : null;
            if (!string.IsNullOrEmpty(world)) playerWorlds.Add((player, world));
        }
        dispatch.Clear(); dispatch.AddRange(groups.Values);
        foreach (Group group in dispatch)
        {
            if (group.Items.Count == 0) continue;
            string key = owners[group.Items[0]].Key;
            group.Players.Clear();
            for (int i = 0; i < playerWorlds.Count; i++)
                if (playerWorlds[i].WorldKey == key) group.Players.Add(playerWorlds[i].Player);
            if (GameplayWorldRegistry.TryGet(key, out _))
            {
                using (GameplayWorldRegistry.UseWorld(key))
                    group.Scheduler.Update(deltaTime, beforeTick, group.Players, WorldTopologyRuntime.GetActiveDomain());
            }
            else if (SpaceMgr.IsSpaceScene(key))
            {
                using (GameplayWorldRegistry.UseSpaceScene(key)) group.Scheduler.Update(deltaTime, beforeTick, group.Players, fallback);
            }
            else group.Scheduler.Update(deltaTime, beforeTick, group.Players, fallback);
        }
        dispatch.Clear();
    }
    #endregion
}
