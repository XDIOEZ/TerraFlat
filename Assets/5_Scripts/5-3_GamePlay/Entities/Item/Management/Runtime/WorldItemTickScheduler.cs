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
    private readonly Dictionary<Item, (string Key, int Scene)> owners = new();
    private readonly List<Group> dispatch = new();
    private readonly List<Item> removed = new();
    public int RegisteredCount => owners.Count;
    public int EveryFrameCount { get { int total = 0; foreach (Group group in groups.Values) total += group.Scheduler.EveryFrameCount; return total; } }
    public int FastCount { get { int total = 0; foreach (Group group in groups.Values) total += group.Scheduler.FastCount; return total; } }
    public int NormalCount { get { int total = 0; foreach (Group group in groups.Values) total += group.Scheduler.NormalCount; return total; } }
    public int SlowCount { get { int total = 0; foreach (Group group in groups.Values) total += group.Scheduler.SlowCount; return total; } }

    public void Register(Item item)
    {
        if (item == null) return;
        var scene = item.gameObject.scene;
        string key = scene.name;
        if (owners.TryGetValue(item, out var previous))
        {
            if (previous.Key == key)
            {
                owners[item] = (key, scene.handle);
                groups[key].Scheduler.Register(item);
                return;
            }
            groups[previous.Key].Scheduler.Remove(item); groups[previous.Key].Items.Remove(item);
        }
        if (!groups.TryGetValue(key, out Group group)) groups.Add(key, group = new Group());
        owners[item] = (key, scene.handle); group.Items.Add(item); group.Scheduler.Register(item);
    }
    public void Remove(Item item)
    {
        if (ReferenceEquals(item, null) || !owners.Remove(item, out var owner)) return;
        groups[owner.Key].Scheduler.Remove(item); groups[owner.Key].Items.Remove(item);
    }
    public void NotifyChanged(Item item)
    { if (item == null) return; Register(item); groups[owners[item].Key].Scheduler.NotifyChanged(item); }
    public void NotifyMoved(Item item)
    {
        if (item == null) return;
        // 场景句柄不变时复用登记的世界名，移动热路径不再从原生层复制场景字符串。
        if (!owners.TryGetValue(item, out var owner) || owner.Scene != item.gameObject.scene.handle)
        {
            Register(item);
            owner = owners[item];
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
        dispatch.Clear(); dispatch.AddRange(groups.Values);
        foreach (Group group in dispatch)
        {
            if (group.Items.Count == 0) continue;
            string key = owners[group.Items[0]].Key;
            group.Players.Clear();
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null && players[i].gameObject.scene.name == key) group.Players.Add(players[i]);
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
