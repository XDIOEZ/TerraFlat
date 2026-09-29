using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using Newtonsoft.Json;

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

    public static bool TryRoute(Inventory source, int sourceIndex, Inventory target, int targetIndex,
        string operation, int amount, out bool result)
    {
        result = false;
        if (GameNetwork.HasStateAuthority || source?.MachineOwner == null && target?.MachineOwner == null) return false;
        result = RemoteTransferRequested?.Invoke(source, sourceIndex, target, targetIndex, operation, amount) == true;
        return true;
    }

    public static void Reset() => RemoteTransferRequested = null;

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
        var hotbar = actor.itemMods?.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar)?.RuntimeInventory;
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

    #region 库存整理命令
    private sealed class LayoutRequest
    {
        public int InventoryIndex;
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
        int index = -1;
        for (int i = 0; i < inventories.Count; i++)
            if (ReferenceEquals(inventories[i], inventory)) { index = i; break; }
        if (index < 0) return true;
        var request = new LayoutRequest { InventoryIndex = index, Organize = !mode.HasValue, Mode = mode.GetValueOrDefault() };
        if (priorityFilter != null)
            for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
            {
                ItemData item = inventory.Data.itemSlots[i].itemData;
                if (item != null && priorityFilter(item))
                    request.Priority.Add(new PriorityItem { Slot = i, Guid = item.Guid, Id = item.IDName });
            }
        string argument = JsonConvert.SerializeObject(request);
        if (argument.Length > 2048) return true;
        accepted = MachineWorld.RequestOperation(entity, "inventory.layout", argument, null);
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
            !Enum.IsDefined(typeof(InventorySortMode), request.Mode) || (request.Priority?.Count ?? 0) > 256) return false;
        Inventory inventory = inventories[request.InventoryIndex];
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
        bool changed = request.Organize ? inventory.Data.Organize() : inventory.Data.Sort(request.Mode, priority.Contains);
        if (changed)
        {
            inventory.RefreshUI();
            MachineWorld.StateChanged(entity);
        }
        return true;
    }
    #endregion
}
