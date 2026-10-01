using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>库存网络地址只标识玩家自身库存或机器库存，不允许客户端上传任意物品状态。</summary>
public struct MachineInventoryAddress
{
    public int MachineId;
    public string PlayerInventory;
    public int Index;
}

/// <summary>统一拖放入口的薄网络桥；服务器仍调用原有 Inventory 事务，不复制搬运算法。</summary>
public static class MachineInventoryCommands
{
    #region 归属与意图
    public static event Func<Inventory, int, Inventory, int, string, int, bool> RemoteTransferRequested;

    private static readonly Dictionary<string, Func<Player, MachineEntity, Inventory>> privateInventories =
        new(StringComparer.Ordinal);

    private sealed class PrivateInventoryLease : IDisposable
    {
        private readonly string key;
        private readonly Func<Player, MachineEntity, Inventory> resolver;
        public PrivateInventoryLease(string key, Func<Player, MachineEntity, Inventory> resolver)
        { this.key = key; this.resolver = resolver; }
        public void Dispose()
        {
            if (privateInventories.TryGetValue(key, out var current) && ReferenceEquals(current, resolver))
                privateInventories.Remove(key);
        }
    }

    /// <summary>私有库存由角色和机器共同定位，MOD 卸载时释放注册租约。</summary>
    public static IDisposable RegisterPrivateInventory(string key, Func<Player, MachineEntity, Inventory> resolver)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key[0] == '@' || resolver == null)
            throw new ArgumentException("私有库存注册参数无效。");
        privateInventories.Add(key, resolver);
        return new PrivateInventoryLease(key, resolver);
    }

    public static bool IsPrivateInventoryAddress(MachineInventoryAddress address)
        => address.MachineId != 0 && address.Index == 0 &&
           !string.IsNullOrEmpty(address.PlayerInventory) &&
           privateInventories.ContainsKey(address.PlayerInventory);

    public static Inventory ResolvePrivate(Player actor, MachineEntity entity, string key)
    {
        if (actor == null || entity == null || string.IsNullOrEmpty(key) ||
            !privateInventories.TryGetValue(key, out var resolver)) return null;
        Inventory inventory = resolver(actor, entity);
        return ReferenceEquals(inventory?.MachineOwner, entity) ? inventory : null;
    }

    public static bool TryRoute(Inventory source, int sourceIndex, Inventory target, int targetIndex,
        string operation, int amount, out bool result)
    {
        result = false;
        if (GameNetwork.HasStateAuthority || source?.MachineOwner == null && target?.MachineOwner == null) return false;
        result = RemoteTransferRequested?.Invoke(source, sourceIndex, target, targetIndex, operation, amount) == true;
        return true;
    }

    public static void Reset()
    {
        RemoteTransferRequested = null;
        privateInventories.Clear();
    }

    public static IReadOnlyList<Inventory> GetMachineInventories(MachineEntity entity)
    {
        if (entity?.Logic != null) return entity.Logic.Inventories;
        return entity?.Processor == null ? Array.Empty<Inventory>() : new[] { entity.Processor.Input, entity.Processor.Output };
    }

    public static bool TryAddress(Player actor, Inventory inventory, out MachineInventoryAddress address)
    {
        address = default;
        if (actor == null || inventory == null) return false;
        if (inventory.MachineOwner != null)
        {
            foreach (var registration in privateInventories)
                if (ReferenceEquals(ResolvePrivate(actor, inventory.MachineOwner, registration.Key), inventory))
                {
                    address = new MachineInventoryAddress
                    { MachineId = inventory.MachineOwner.Id, PlayerInventory = registration.Key };
                    return true;
                }
            var machineInventories = GetMachineInventories(inventory.MachineOwner);
            for (int i = 0; i < machineInventories.Count; i++)
                if (ReferenceEquals(machineInventories[i], inventory))
                { address = new MachineInventoryAddress { MachineId = inventory.MachineOwner.Id, Index = i }; return true; }
            return false;
        }
        foreach (var pair in PlayerInventories(actor))
            if (ReferenceEquals(pair.Inventory, inventory)) { address = pair.Address; return true; }
        return false;
    }

    public static Inventory Resolve(Player actor, MachineInventoryAddress address)
    {
        if (actor == null || address.Index < 0) return null;
        if (address.MachineId != 0)
        {
            var entity = MachineWorld.GetById(address.MachineId);
            if (GameNetwork.HasStateAuthority) MachineWorld.WakeForInteraction(entity);
            if (!string.IsNullOrEmpty(address.PlayerInventory))
                return IsPrivateInventoryAddress(address)
                    ? ResolvePrivate(actor, entity, address.PlayerInventory) : null;
            var values = GetMachineInventories(entity);
            return (uint)address.Index < (uint)values.Count ? values[address.Index] : null;
        }
        foreach (var pair in PlayerInventories(actor))
            if (pair.Address.PlayerInventory == address.PlayerInventory && pair.Address.Index == address.Index) return pair.Inventory;
        return null;
    }

    private static IEnumerable<(MachineInventoryAddress Address, Inventory Inventory)> PlayerInventories(Player actor)
    {
        var hand = actor.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (hand != null) yield return (new MachineInventoryAddress { PlayerInventory = "@hand" }, hand);
        var hotbar = actor.itemMods?.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.RuntimeInventory;
        if (hotbar != null) yield return (new MachineInventoryAddress { PlayerInventory = "@hotbar" }, hotbar);
        if (actor.itemMods?.Mods == null) yield break;
        foreach (Module module in actor.itemMods.Mods.Values)
        {
            if (module is Mod_Inventory storage)
            {
                for (int i = 0; i < storage.InventoryInstances.Count; i++)
                    yield return (new MachineInventoryAddress { PlayerInventory = module._Data.ID, Index = i }, storage.InventoryInstances[i]);
            }
            else if (module is Mod_HandCraftTable table)
            {
                yield return (new MachineInventoryAddress { PlayerInventory = "@craft", Index = 0 }, table.inputInventory);
                yield return (new MachineInventoryAddress { PlayerInventory = "@craft", Index = 1 }, table.outputInventory);
            }
        }
    }
    #endregion

    #region 私有存档的网络裁剪
    /// <summary>公共角色快照只移除已注册的私有库存键，其他系统的特殊数据照常同步。</summary>
    internal static string PublicSpecialData(string raw)
    {
        if (privateInventories.Count == 0 || string.IsNullOrWhiteSpace(raw)) return raw;
        JObject root = ItemSpecialDataJsonStore.ReadRoot(raw);
        if (root[MachinePersistence.Namespace] is not JObject document) return raw;
        bool changed = false;
        foreach (string key in privateInventories.Keys) changed |= document.Remove(key);
        return changed ? root.ToString(Formatting.None) : raw;
    }

    /// <summary>收到公共角色快照时保留本机已有私有值，并拒绝客户端借快照写入私有键。</summary>
    internal static string MergePrivateSpecialData(string trusted, string incoming)
    {
        if (privateInventories.Count == 0) return incoming;
        string publicIncoming = PublicSpecialData(incoming);
        JObject saved = ItemSpecialDataJsonStore.ReadRoot(trusted);
        if (saved[MachinePersistence.Namespace] is not JObject source) return publicIncoming;
        JObject root = ItemSpecialDataJsonStore.ReadRoot(publicIncoming);
        JObject target = root[MachinePersistence.Namespace] as JObject;
        foreach (string key in privateInventories.Keys)
        {
            if (source[key] == null) continue;
            target ??= new JObject { ["version"] = MachinePersistence.Version };
            target[key] = source[key].DeepClone();
        }
        if (target == null) return publicIncoming;
        root[MachinePersistence.Namespace] = target;
        return root.ToString(Formatting.None);
    }
    #endregion

    #region 库存整理命令
    private sealed class LayoutRequest
    {
        public int InventoryIndex;
        public string PrivateKey;
        public bool Organize;
        public InventorySortMode Mode;
        public List<PriorityItem> Priority = new();
    }

    private sealed class PriorityItem
    {
        public int Slot;
        public int Guid;
        public string Id;
    }

    /// <summary>机器面板只发送整理意图，搜索结果用物品身份传递，不在客户端重排权威库存。</summary>
    public static bool TryRequestLayout(Inventory inventory, InventorySortMode? mode,
        Predicate<ItemData> priorityFilter, out bool accepted)
    {
        accepted = false;
        MachineEntity entity = inventory?.MachineOwner;
        if (entity == null) return false;
        var inventories = GetMachineInventories(entity);
        Player actor = null;
        int index = -1;
        for (int i = 0; i < inventories.Count; i++)
            if (ReferenceEquals(inventories[i], inventory)) { index = i; break; }
        string privateKey = null;
        if (index < 0)
        {
            actor = ItemMgr.Instance?.User_Player;
            if (!TryAddress(actor, inventory, out var address) || !IsPrivateInventoryAddress(address))
                return true;
            index = 0;
            privateKey = address.PlayerInventory;
        }
        var request = new LayoutRequest
        { InventoryIndex = index, PrivateKey = privateKey, Organize = !mode.HasValue, Mode = mode.GetValueOrDefault() };
        if (priorityFilter != null)
            for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
            {
                ItemData item = inventory.Data.itemSlots[i].itemData;
                if (item != null && priorityFilter(item))
                    request.Priority.Add(new PriorityItem { Slot = i, Guid = item.Guid, Id = item.IDName });
            }
        string argument = JsonConvert.SerializeObject(request);
        if (argument.Length > 2048) return true;
        accepted = MachineWorld.RequestOperation(entity,
            privateKey == null ? "inventory.layout" : "inventory.private-layout", argument, actor);
        return true;
    }

    /// <summary>服务端复用原库存排序事务，验证搜索身份，不接收客户端构造的物品数据。</summary>
    public static bool ExecuteLayout(MachineEntity entity, string argument)
    {
        if (!GameNetwork.HasStateAuthority || !MachineWorld.Contains(entity) ||
            string.IsNullOrWhiteSpace(argument) || argument.Length > 2048) return false;
        LayoutRequest request;
        try { request = JsonConvert.DeserializeObject<LayoutRequest>(argument); }
        catch (JsonException) { return false; }
        var inventories = GetMachineInventories(entity);
        if (request == null || (uint)request.InventoryIndex >= (uint)inventories.Count ||
            !string.IsNullOrEmpty(request.PrivateKey)) return false;
        return ExecuteLayoutRequest(inventories[request.InventoryIndex], request, entity);
    }

    /// <summary>私有库存的整理仍由服务端验证角色与箱体，并只回传该角色的结果。</summary>
    public static bool ExecutePrivateLayout(MachineEntity entity, string argument, Player actor)
    {
        if (!GameNetwork.HasStateAuthority || !MachineWorld.Contains(entity) || actor == null ||
            string.IsNullOrWhiteSpace(argument) || argument.Length > 2048) return false;
        LayoutRequest request;
        try { request = JsonConvert.DeserializeObject<LayoutRequest>(argument); }
        catch (JsonException) { return false; }
        if (request == null || request.InventoryIndex != 0 ||
            string.IsNullOrEmpty(request.PrivateKey) || request.PrivateKey.Length > 128) return false;
        Inventory inventory = ResolvePrivate(actor, entity, request.PrivateKey);
        return inventory != null && ExecuteLayoutRequest(inventory, request, null);
    }

    public static string ReadPrivateLayoutKey(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument) || argument.Length > 2048) return null;
        try { return JsonConvert.DeserializeObject<LayoutRequest>(argument)?.PrivateKey; }
        catch (JsonException) { return null; }
    }

    private static bool ExecuteLayoutRequest(Inventory inventory, LayoutRequest request, MachineEntity publicEntity)
    {
        if (!Enum.IsDefined(typeof(InventorySortMode), request.Mode) ||
            (request.Priority?.Count ?? 0) > 256) return false;
        if (MachineInventory.IsBeingDragged(inventory)) return false;
        var priority = new HashSet<ItemData>();
        foreach (PriorityItem item in request.Priority ?? new List<PriorityItem>())
        {
            if (item == null || item.Id == null || item.Id.Length > 128) return false;
            for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
            {
                ItemData candidate = inventory.Data.itemSlots[i].itemData;
                if (candidate != null && candidate.IDName == item.Id &&
                    (item.Guid != 0 ? candidate.Guid == item.Guid : i == item.Slot && candidate.Guid == 0))
                    priority.Add(candidate);
            }
        }
        Predicate<ItemData> priorityFilter = null;
        if (priority.Count > 0)
            priorityFilter = priority.Contains;
        bool changed = request.Organize
            ? inventory.Data.Organize(priorityFilter)
            : inventory.Data.Sort(request.Mode, priorityFilter);
        if (changed)
        {
            inventory.RefreshUI();
            if (publicEntity != null) MachineWorld.StateChanged(publicEntity);
        }
        return true;
    }
    #endregion
}
