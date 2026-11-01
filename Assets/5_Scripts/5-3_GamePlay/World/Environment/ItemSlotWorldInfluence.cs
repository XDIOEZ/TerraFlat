using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>物品定义声明的单个世界环境来源。</summary>
public readonly struct ItemWorldLayerInfluence
{
    public const string TemperatureLayerId = "temperature.celsius";

    public ItemWorldLayerInfluence(string layerId, float value, float radius, float strength, bool cooling)
    {
        LayerId = layerId;
        Value = value;
        Radius = radius;
        Strength = strength;
        Cooling = cooling;
    }

    public string LayerId { get; }
    public float Value { get; }
    public float Radius { get; }
    public float Strength { get; }
    public bool Cooling { get; }

}

/// <summary>物品内容对外提供世界层影响，槽位宿主只转交物品数据。</summary>
public interface IItemWorldLayerInfluenceProvider
{
    void CollectWorldLayerInfluences(ItemData itemData, List<ItemWorldLayerInfluence> result);
}

/// <summary>真实槽位宿主提供稳定身份、世界锚点和槽内物品快照。</summary>
public interface IItemSlotHost
{
    string ItemSlotHostId { get; }
    int ItemSlotWorldInfluencePriority { get; }
    MachineEntity ItemSlotMachineOwner { get; }
    bool TryGetItemSlotWorldAnchor(out string worldKey, out Vector2 position);
    void CollectItemSlotAnchorMovementItems(List<Item> result);
    void CollectItemSlotBindings(List<ItemSlotWorldBinding> result);
}

public readonly struct ItemSlotWorldBinding
{
    public ItemSlotWorldBinding(string slotId, ItemData itemData)
    {
        SlotId = slotId;
        ItemData = itemData;
        ItemSnapshot = null;
        ItemId = itemData?.IDName;
        ItemGuid = itemData?.Guid ?? 0;
    }

    public ItemSlotWorldBinding(string slotId, ItemInstanceSnapshot itemSnapshot)
    {
        SlotId = slotId;
        ItemData = null;
        ItemSnapshot = itemSnapshot;
        ItemId = itemSnapshot?.DefinitionId;
        ItemGuid = itemSnapshot?.Guid ?? 0;
    }

    public ItemSlotWorldBinding(string slotId, string itemId, int itemGuid)
    {
        SlotId = slotId;
        ItemData = null;
        ItemSnapshot = null;
        ItemId = itemId;
        ItemGuid = itemGuid;
    }

    public string SlotId { get; }
    public ItemData ItemData { get; }
    public ItemInstanceSnapshot ItemSnapshot { get; }
    public string ItemId { get; }
    public int ItemGuid { get; }
}

/// <summary>环境层可以独立接收或撤销物品来源，不依赖具体槽位或物品类型。</summary>
public interface IWorldLayerInfluenceSink
{
    bool CanHandle(string layerId);
    bool SetSource(object owner, string worldKey, Vector2 position, ItemWorldLayerInfluence influence);
    void RemoveSource(object owner, string worldKey);
}

public static class WorldLayerInfluenceSinkRegistry
{
    #region 环境层接收器

    private sealed class TemperatureSink : IWorldLayerInfluenceSink
    {
        public bool CanHandle(string layerId)
            => string.Equals(layerId, ItemWorldLayerInfluence.TemperatureLayerId, StringComparison.Ordinal);

        public bool SetSource(object owner, string worldKey, Vector2 position, ItemWorldLayerInfluence influence)
        {
            if (string.Equals(worldKey, GameplayWorldRegistry.ExecutionWorldKey, StringComparison.Ordinal))
                return TemperatureMgr.ExistingInstance?.SetLocalTemperatureSource(owner, position,
                    influence.Radius, influence.Value, influence.Cooling, influence.Strength) == true;
            if (!GameplayWorldRegistry.TryGet(worldKey, out _)) return false;
            using (GameplayWorldRegistry.UseWorld(worldKey))
                return TemperatureMgr.ExistingInstance?.SetLocalTemperatureSource(owner, position,
                    influence.Radius, influence.Value, influence.Cooling, influence.Strength) == true;
        }

        public void RemoveSource(object owner, string worldKey)
        {
            if (string.Equals(worldKey, GameplayWorldRegistry.ExecutionWorldKey, StringComparison.Ordinal))
            {
                TemperatureMgr.ExistingInstance?.RemoveLocalTemperatureSource(owner);
                return;
            }
            if (!GameplayWorldRegistry.TryGet(worldKey, out _)) return;
            using (GameplayWorldRegistry.UseWorld(worldKey))
                TemperatureMgr.ExistingInstance?.RemoveLocalTemperatureSource(owner);
        }
    }

    private sealed class ContaminationSink : IWorldLayerInfluenceSink
    {
        public bool CanHandle(string layerId)
            => ContaminationSystem.TryGetDefinition(layerId, out _);

        public bool SetSource(object owner, string worldKey, Vector2 position, ItemWorldLayerInfluence influence)
            => ContaminationSystem.SetDynamicSource(owner, worldKey, position, influence.LayerId,
                influence.Value * influence.Strength, influence.Radius);

        public void RemoveSource(object owner, string worldKey)
            => ContaminationSystem.RemoveDynamicSource(owner);
    }

    private static readonly List<IWorldLayerInfluenceSink> builtInSinks = new()
    {
        new TemperatureSink(),
        new ContaminationSink()
    };
    private static readonly List<IWorldLayerInfluenceSink> extensionSinks = new();

    public static void Register(IWorldLayerInfluenceSink sink)
    {
        if (sink == null || extensionSinks.Contains(sink)) return;
        extensionSinks.Add(sink);
        ItemSlotWorldInfluenceSystem.RefreshRegisteredHosts();
    }

    public static void Unregister(IWorldLayerInfluenceSink sink)
    {
        if (sink != null && extensionSinks.Remove(sink))
            ItemSlotWorldInfluenceSystem.RefreshRegisteredHosts();
    }

    internal static IWorldLayerInfluenceSink Resolve(string layerId)
    {
        for (int index = extensionSinks.Count - 1; index >= 0; index--)
            if (extensionSinks[index].CanHandle(layerId)) return extensionSinks[index];
        for (int index = builtInSinks.Count - 1; index >= 0; index--)
            if (builtInSinks[index].CanHandle(layerId)) return builtInSinks[index];
        return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        extensionSinks.Clear();
    }

    #endregion
}

/// <summary>统一登记槽内容来源并在移动、取出或卸载时撤销环境影响。</summary>
public static class ItemSlotWorldInfluenceSystem
{
    #region 槽宿主与物品来源

    private readonly struct ItemIdentity : IEquatable<ItemIdentity>
    {
        private readonly int guid;
        private readonly ItemData reference;
        private readonly string snapshotKey;

        public ItemIdentity(ItemData itemData)
        {
            guid = itemData?.Guid ?? 0;
            reference = guid == 0 ? itemData : null;
            snapshotKey = null;
        }

        public ItemIdentity(int itemGuid, string fallbackKey)
        {
            guid = itemGuid;
            reference = null;
            snapshotKey = itemGuid == 0 ? fallbackKey : null;
        }

        public bool Equals(ItemIdentity other)
            => guid != 0 || other.guid != 0
                ? guid != 0 && guid == other.guid
                : reference != null || other.reference != null
                    ? ReferenceEquals(reference, other.reference)
                    : string.Equals(snapshotKey, other.snapshotKey, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ItemIdentity other && Equals(other);
        public override int GetHashCode()
            => guid != 0 ? guid : reference != null
                ? RuntimeHelpers.GetHashCode(reference)
                : StringComparer.Ordinal.GetHashCode(snapshotKey ?? string.Empty);
    }

    private sealed class SlotClaim
    {
        public ItemIdentity Item;
        public string HostId;
        public string SlotId;
        public string SourceKey;
        public string WorldKey;
        public Vector2 Position;
        public int Priority;
        public int Depth;
        public ItemWorldLayerInfluence[] Influences;
    }

    private sealed class HostRecord
    {
        public string HostId;
        public string WorldKey;
        public Vector2 Position;
        public int Priority;
        public IItemSlotHost Host;
        public MachineEntity MachineOwner;
        public bool IsMachineSnapshot;
        public List<Item> AnchorMovementItems = new();
        public List<ItemSlotWorldBinding> Bindings;
        public List<SlotClaim> Claims;
        public HashSet<ItemData> NestedItemData = new();
        public HashSet<Inventory_ModuleData> NestedInventoryModules = new();
        public HashSet<Inventory_Data> NestedInventoryData = new();
    }

    private sealed class AppliedInfluence
    {
        public string SourceKey;
        public object Owner;
        public string WorldKey;
        public Vector2 Position;
        public ItemWorldLayerInfluence Definition;
        public IWorldLayerInfluenceSink Sink;
        public bool IsApplied;
    }

    private static readonly Dictionary<string, HostRecord> hosts = new(StringComparer.Ordinal);
    private static readonly Dictionary<ItemIdentity, Dictionary<string, SlotClaim>> claimsByItem = new();
    private static readonly Dictionary<ItemIdentity, List<AppliedInfluence>> appliedByItem = new();
    private static readonly Dictionary<Item, HashSet<string>> hostsByAnchorMovementItem = new();
    private static readonly Dictionary<MachineEntity, HashSet<string>> hostsByMachine = new();
    private static readonly Dictionary<ItemData, HashSet<string>> hostsByNestedItemData = new();
    private static readonly Dictionary<Inventory_ModuleData, HashSet<string>> hostsByNestedInventoryModule = new();
    private static readonly Dictionary<Inventory_Data, HashSet<string>> hostsByNestedInventoryData = new();
    private static readonly List<string> movementHostScratch = new();
    private static readonly List<string> machineHostScratch = new();
    private static readonly List<string> worldHostScratch = new();
    private static readonly List<Item> movementItemScratch = new();
    private static readonly List<ItemSlotWorldBinding> machineBindingScratch = new();
    private static bool movementSubscribed;
    private static bool despawnSubscribed;
    private static bool machineSubscribed;
    private static bool worldReadySubscribed;

    public static string CreateMachineHostId(string worldKey, int machineId, string inventoryId)
        => $"machine:{worldKey}/{machineId}/{inventoryId}";

    private static string CreateMachineSnapshotHostId(string worldKey, int machineId)
        => $"machine-snapshot:{worldKey}/{machineId}";

    private static void EnsureEventSubscriptions()
    {
        if (!movementSubscribed)
        {
            ItemMgr.RuntimeItemMoved += OnRuntimeItemMoved;
            movementSubscribed = true;
        }
        if (!despawnSubscribed)
        {
            ItemMgr.RuntimeItemDespawning += OnRuntimeItemDespawning;
            despawnSubscribed = true;
        }
        if (!machineSubscribed)
        {
            MachineWorld.NodeStateChanged += OnMachineNodeStateChanged;
            MachineWorld.NodeRemoved += OnMachineNodeRemoved;
            machineSubscribed = true;
        }
        if (!worldReadySubscribed)
        {
            GameplayWorldRegistry.RuntimeWorldReady += OnRuntimeWorldReady;
            worldReadySubscribed = true;
        }
    }

    public static void RefreshHost(IItemSlotHost host)
    {
        if (host == null) return;
        EnsureEventSubscriptions();
        string hostId = host.ItemSlotHostId;
        if (string.IsNullOrWhiteSpace(hostId) ||
            !host.TryGetItemSlotWorldAnchor(out string worldKey, out Vector2 position) ||
            string.IsNullOrWhiteSpace(worldKey))
        {
            RemoveHost(host);
            return;
        }

        if (hosts.TryGetValue(hostId, out HostRecord current) && current.Host != null &&
            !ReferenceEquals(current.Host, host))
            return;

        var bindings = new List<ItemSlotWorldBinding>();
        host.CollectItemSlotBindings(bindings);
        ReplaceHost(hostId, worldKey, position, host.ItemSlotWorldInfluencePriority, bindings,
            host, host.ItemSlotMachineOwner);
    }

    public static void RefreshHostAnchor(IItemSlotHost host)
    {
        if (host == null) return;
        EnsureEventSubscriptions();
        string hostId = host.ItemSlotHostId;
        if (string.IsNullOrWhiteSpace(hostId) ||
            !host.TryGetItemSlotWorldAnchor(out string worldKey, out Vector2 position) ||
            string.IsNullOrWhiteSpace(worldKey))
        {
            RemoveHost(host);
            return;
        }

        if (hosts.TryGetValue(hostId, out HostRecord previous))
        {
            if (!ReferenceEquals(previous.Host, host)) return;
            if (string.Equals(previous.WorldKey, worldKey, StringComparison.Ordinal) &&
                previous.Position == position && HasSameMovementItems(host, previous.AnchorMovementItems))
                return;
            ReplaceHost(hostId, worldKey, position, host.ItemSlotWorldInfluencePriority,
                previous.Bindings, host, host.ItemSlotMachineOwner);
            return;
        }
        RefreshHost(host);
    }

    public static void ReplaceHost(string hostId, string worldKey, Vector2 position, int priority,
        IReadOnlyList<ItemSlotWorldBinding> bindings)
        => ReplaceHost(hostId, worldKey, position, priority, bindings, null, null);

    private static void ReplaceHost(string hostId, string worldKey, Vector2 position, int priority,
        IReadOnlyList<ItemSlotWorldBinding> bindings, IItemSlotHost host, MachineEntity machineOwner)
    {
        if (string.IsNullOrWhiteSpace(hostId)) return;
        EnsureEventSubscriptions();
        var affectedItems = new HashSet<ItemIdentity>();
        if (hosts.TryGetValue(hostId, out HostRecord previous))
        {
            RemoveMovementBindings(previous);
            RemoveMachineBinding(previous);
            RemoveNestedBindings(previous);
            foreach (SlotClaim claim in previous.Claims)
            {
                affectedItems.Add(claim.Item);
                RemoveClaim(claim);
            }
        }

        var record = new HostRecord
        {
            HostId = hostId,
            WorldKey = worldKey,
            Position = position,
            Priority = priority,
            Host = host,
            MachineOwner = machineOwner ?? host?.ItemSlotMachineOwner,
            IsMachineSnapshot = host == null && machineOwner != null,
            Bindings = bindings == null ? new List<ItemSlotWorldBinding>() : new List<ItemSlotWorldBinding>(bindings),
            Claims = new List<SlotClaim>()
        };
        if (host != null) host.CollectItemSlotAnchorMovementItems(record.AnchorMovementItems);

        GameRes resources = GameRes.ExistingInstance;
        if (resources != null)
        {
            var slotIds = new HashSet<string>(StringComparer.Ordinal);
            var visitedItems = new HashSet<ItemData>();
            var visitedInventories = new HashSet<Inventory_Data>();
            foreach (ItemSlotWorldBinding binding in record.Bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.SlotId) || !slotIds.Add(binding.SlotId))
                    continue;

                CollectItemSlotBindingInfluences(record, binding, resources, visitedItems,
                    visitedInventories, affectedItems, depth: 0, isSnapshotTree: binding.ItemSnapshot != null);
            }
        }

        hosts[hostId] = record;
        AddMovementBindings(record);
        AddMachineBinding(record);
        foreach (ItemIdentity item in affectedItems)
            ReconcileItem(item);
    }

    private static void CollectItemSlotBindingInfluences(HostRecord record, ItemSlotWorldBinding binding,
        GameRes resources, HashSet<ItemData> visitedItems, HashSet<Inventory_Data> visitedInventories,
        HashSet<ItemIdentity> affectedItems, int depth, bool isSnapshotTree)
    {
        ItemData itemData = binding.ItemData;
        if (binding.ItemSnapshot != null)
        {
            itemData = binding.ItemSnapshot.CreateColdData();
            isSnapshotTree = true;
        }

        string itemId = itemData?.IDName ?? binding.ItemId;
        int itemGuid = itemData?.Guid ?? binding.ItemGuid;
        if (!string.IsNullOrWhiteSpace(itemId) &&
            resources.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition))
        {
            var influences = new List<ItemWorldLayerInfluence>();
            if (itemData != null) definition.CollectWorldLayerInfluences(itemData, influences);
            else definition.CollectWorldLayerInfluences(influences);
            if (influences.Count > 0)
            {
                string sourceKey = record.HostId + "\u001f" + binding.SlotId;
                var claim = new SlotClaim
                {
                    Item = itemData != null && !isSnapshotTree
                        ? new ItemIdentity(itemData)
                        : new ItemIdentity(itemGuid, sourceKey),
                    HostId = record.HostId,
                    SlotId = binding.SlotId,
                    SourceKey = sourceKey,
                    WorldKey = record.WorldKey,
                    Position = record.Position,
                    Priority = record.Priority,
                    Depth = depth,
                    Influences = influences.ToArray()
                };
                record.Claims.Add(claim);
                affectedItems.Add(claim.Item);
                AddClaim(claim);
            }
        }

        if (itemData == null || !visitedItems.Add(itemData))
            return;
        if (!isSnapshotTree)
            AddNestedItemDataBinding(record, itemData);

        ModuleDataCollection modules = itemData.ModuleDataDic;
        if (modules == null) return;
        foreach (ModuleData moduleData in modules.Values)
        {
            if (moduleData is not Inventory_ModuleData inventoryModule)
                continue;
            if (!isSnapshotTree)
                AddNestedInventoryModuleBinding(record, inventoryModule);
            if (inventoryModule.Data == null) continue;

            var inventoryKeys = new List<string>(inventoryModule.Data.Keys);
            inventoryKeys.Sort(StringComparer.Ordinal);
            string moduleKey = string.IsNullOrWhiteSpace(moduleData.StableName)
                ? moduleData.ModuleId
                : moduleData.StableName;
            foreach (string inventoryKey in inventoryKeys)
            {
                Inventory_Data inventoryData = inventoryModule.Data[inventoryKey];
                if (inventoryData == null || !visitedInventories.Add(inventoryData))
                    continue;
                if (!isSnapshotTree)
                    AddNestedInventoryDataBinding(record, inventoryData);

                for (int index = 0; index < (inventoryData.itemSlots?.Count ?? 0); index++)
                {
                    ItemData nestedItem = inventoryData.itemSlots[index]?.itemData;
                    if (nestedItem == null) continue;
                    string nestedSlotId = binding.SlotId + "/" + moduleKey + "/" + inventoryKey + "/" +
                        index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    CollectItemSlotBindingInfluences(record,
                        new ItemSlotWorldBinding(nestedSlotId, nestedItem), resources, visitedItems,
                        visitedInventories, affectedItems, depth + 1, isSnapshotTree);
                }
            }
        }
    }

    public static void RemoveHost(IItemSlotHost host)
    {
        string hostId = host?.ItemSlotHostId;
        if (!string.IsNullOrWhiteSpace(hostId) && hosts.TryGetValue(hostId, out HostRecord record) &&
            ReferenceEquals(record.Host, host))
            RemoveHost(hostId);
    }

    public static void RemoveHost(string hostId)
    {
        if (string.IsNullOrWhiteSpace(hostId) || !hosts.Remove(hostId, out HostRecord record)) return;
        RemoveMovementBindings(record);
        RemoveMachineBinding(record);
        var affectedItems = new HashSet<ItemIdentity>();
        foreach (SlotClaim claim in record.Claims)
        {
            affectedItems.Add(claim.Item);
            RemoveClaim(claim);
        }
        RemoveNestedBindings(record);
        foreach (ItemIdentity item in affectedItems)
            ReconcileItem(item);
    }

    public static void RefreshRegisteredHosts()
    {
        EnsureEventSubscriptions();
        var records = new List<HostRecord>(hosts.Values);
        foreach (HostRecord record in records)
            RefreshHostRecord(record.HostId);
    }

    private static void OnRuntimeWorldReady(string worldKey)
    {
        worldHostScratch.Clear();
        foreach (HostRecord record in hosts.Values)
            if (string.Equals(record.WorldKey, worldKey, StringComparison.Ordinal))
                worldHostScratch.Add(record.HostId);
        foreach (string hostId in worldHostScratch)
        {
            try { RefreshHostRecord(hostId); }
            catch (Exception error) { Debug.LogException(error); }
        }
        worldHostScratch.Clear();
    }

    public static void RefreshMachineSnapshot(MachineEntity machine)
    {
        if (machine == null || string.IsNullOrWhiteSpace(machine.ScopeKey)) return;
        EnsureEventSubscriptions();
        string hostId = CreateMachineSnapshotHostId(machine.ScopeKey, machine.Id);
        if (machine.Logic != null || machine.Processor != null)
        {
            RemoveHost(hostId);
            return;
        }

        machineBindingScratch.Clear();
        MachineLogicRegistry.CollectColdItemSlotBindings(machine, machineBindingScratch);
        if (machineBindingScratch.Count == 0)
        {
            RemoveHost(hostId);
            return;
        }
        ReplaceHost(hostId, machine.ScopeKey, machine.Position, int.MaxValue,
            machineBindingScratch, null, machine);
    }

    public static void RemoveMachineSnapshot(string worldKey, int machineId)
    {
        if (!string.IsNullOrWhiteSpace(worldKey))
            RemoveHost(CreateMachineSnapshotHostId(worldKey, machineId));
    }

    private static void OnRuntimeItemMoved(Item item)
    {
        if (item == null || !hostsByAnchorMovementItem.TryGetValue(item, out HashSet<string> ids)) return;
        movementHostScratch.Clear();
        movementHostScratch.AddRange(ids);
        foreach (string id in movementHostScratch)
            if (hosts.TryGetValue(id, out HostRecord record) && record.Host != null)
                RefreshHostAnchor(record.Host);
    }

    private static void OnRuntimeItemDespawning(Item item)
    {
        if (item == null || !hostsByAnchorMovementItem.TryGetValue(item, out HashSet<string> ids)) return;
        movementHostScratch.Clear();
        movementHostScratch.AddRange(ids);
        foreach (string id in movementHostScratch)
            RemoveHost(id);
    }

    private static void OnMachineNodeStateChanged(MachineEntity machine)
    {
        if (machine == null || !hostsByMachine.TryGetValue(machine, out HashSet<string> ids)) return;
        machineHostScratch.Clear();
        machineHostScratch.AddRange(ids);
        foreach (string id in machineHostScratch)
        {
            if (!hosts.TryGetValue(id, out HostRecord record)) continue;
            if (record.Host != null) RefreshHostAnchor(record.Host);
            else if (record.IsMachineSnapshot) RefreshMachineSnapshot(machine);
        }
    }

    private static void OnMachineNodeRemoved(int machineId)
    {
        string worldKey = MachineWorld.WorldKey;
        RemoveMachineSnapshot(worldKey, machineId);
        machineHostScratch.Clear();
        foreach (HostRecord record in hosts.Values)
            if (record.MachineOwner?.Id == machineId &&
                string.Equals(record.MachineOwner.ScopeKey, worldKey, StringComparison.Ordinal))
                machineHostScratch.Add(record.HostId);
        foreach (string id in machineHostScratch)
            RemoveHost(id);
    }

    private static void AddNestedItemDataBinding(HostRecord record, ItemData itemData)
    {
        if (!record.NestedItemData.Add(itemData)) return;
        if (!hostsByNestedItemData.TryGetValue(itemData, out HashSet<string> ids))
        {
            hostsByNestedItemData.Add(itemData, ids = new HashSet<string>(StringComparer.Ordinal));
            itemData.ModuleStructureChanged += OnNestedItemModuleStructureChanged;
        }
        ids.Add(record.HostId);
    }

    private static void AddNestedInventoryDataBinding(HostRecord record, Inventory_Data inventoryData)
    {
        _ = inventoryData.ContentStructureRevision;
        if (!record.NestedInventoryData.Add(inventoryData)) return;
        if (!hostsByNestedInventoryData.TryGetValue(inventoryData, out HashSet<string> ids))
        {
            hostsByNestedInventoryData.Add(inventoryData, ids = new HashSet<string>(StringComparer.Ordinal));
            inventoryData.RuntimeStructureChanged += OnNestedInventoryStructureChanged;
        }
        ids.Add(record.HostId);
    }

    private static void AddNestedInventoryModuleBinding(HostRecord record, Inventory_ModuleData inventoryModule)
    {
        if (!record.NestedInventoryModules.Add(inventoryModule)) return;
        if (!hostsByNestedInventoryModule.TryGetValue(inventoryModule, out HashSet<string> ids))
        {
            hostsByNestedInventoryModule.Add(inventoryModule, ids = new HashSet<string>(StringComparer.Ordinal));
            inventoryModule.InventoryMapStructureChanged += OnNestedInventoryMapStructureChanged;
        }
        ids.Add(record.HostId);
    }

    private static void OnNestedItemModuleStructureChanged(ItemData itemData)
    {
        if (itemData == null || !hostsByNestedItemData.TryGetValue(itemData, out HashSet<string> ids)) return;
        var hostIds = new List<string>(ids);
        foreach (string id in hostIds)
            RefreshHostRecord(id);
    }

    private static void OnNestedInventoryStructureChanged(Inventory_Data inventoryData)
    {
        if (inventoryData == null || !hostsByNestedInventoryData.TryGetValue(inventoryData, out HashSet<string> ids)) return;
        var hostIds = new List<string>(ids);
        foreach (string id in hostIds)
            RefreshHostRecord(id);
    }

    private static void OnNestedInventoryMapStructureChanged(Inventory_ModuleData inventoryModule)
    {
        if (inventoryModule == null || !hostsByNestedInventoryModule.TryGetValue(inventoryModule, out HashSet<string> ids)) return;
        var hostIds = new List<string>(ids);
        foreach (string id in hostIds)
            RefreshHostRecord(id);
    }

    private static void RefreshHostRecord(string hostId)
    {
        if (!hosts.TryGetValue(hostId, out HostRecord record)) return;
        if (record.Host != null) RefreshHost(record.Host);
        else if (record.IsMachineSnapshot && record.MachineOwner != null)
            RefreshMachineSnapshot(record.MachineOwner);
        else ReplaceHost(record.HostId, record.WorldKey, record.Position, record.Priority,
            record.Bindings, null, record.MachineOwner);
    }

    private static void RemoveNestedBindings(HostRecord record)
    {
        foreach (ItemData itemData in record.NestedItemData)
            if (hostsByNestedItemData.TryGetValue(itemData, out HashSet<string> ids))
            {
                ids.Remove(record.HostId);
                if (ids.Count == 0)
                {
                    itemData.ModuleStructureChanged -= OnNestedItemModuleStructureChanged;
                    hostsByNestedItemData.Remove(itemData);
                }
            }
        record.NestedItemData.Clear();

        foreach (Inventory_ModuleData inventoryModule in record.NestedInventoryModules)
            if (hostsByNestedInventoryModule.TryGetValue(inventoryModule, out HashSet<string> ids))
            {
                ids.Remove(record.HostId);
                if (ids.Count == 0)
                {
                    inventoryModule.InventoryMapStructureChanged -= OnNestedInventoryMapStructureChanged;
                    hostsByNestedInventoryModule.Remove(inventoryModule);
                }
            }
        record.NestedInventoryModules.Clear();

        foreach (Inventory_Data inventoryData in record.NestedInventoryData)
            if (hostsByNestedInventoryData.TryGetValue(inventoryData, out HashSet<string> ids))
            {
                ids.Remove(record.HostId);
                if (ids.Count == 0)
                {
                    inventoryData.RuntimeStructureChanged -= OnNestedInventoryStructureChanged;
                    hostsByNestedInventoryData.Remove(inventoryData);
                }
            }
        record.NestedInventoryData.Clear();
    }

    private static void AddMovementBindings(HostRecord record)
    {
        foreach (Item item in record.AnchorMovementItems)
        {
            if (item == null) continue;
            if (!hostsByAnchorMovementItem.TryGetValue(item, out HashSet<string> ids))
                hostsByAnchorMovementItem.Add(item, ids = new HashSet<string>(StringComparer.Ordinal));
            ids.Add(record.HostId);
        }
    }

    private static bool HasSameMovementItems(IItemSlotHost host, List<Item> previous)
    {
        movementItemScratch.Clear();
        host.CollectItemSlotAnchorMovementItems(movementItemScratch);
        if (previous.Count != movementItemScratch.Count) return false;
        for (int index = 0; index < previous.Count; index++)
            if (!ReferenceEquals(previous[index], movementItemScratch[index])) return false;
        return true;
    }

    private static void RemoveMovementBindings(HostRecord record)
    {
        foreach (Item item in record.AnchorMovementItems)
            if (item != null && hostsByAnchorMovementItem.TryGetValue(item, out HashSet<string> ids))
            {
                ids.Remove(record.HostId);
                if (ids.Count == 0) hostsByAnchorMovementItem.Remove(item);
            }
    }

    private static void AddMachineBinding(HostRecord record)
    {
        if (record.MachineOwner == null) return;
        if (!hostsByMachine.TryGetValue(record.MachineOwner, out HashSet<string> ids))
            hostsByMachine.Add(record.MachineOwner, ids = new HashSet<string>(StringComparer.Ordinal));
        ids.Add(record.HostId);
    }

    private static void RemoveMachineBinding(HostRecord record)
    {
        if (record.MachineOwner == null || !hostsByMachine.TryGetValue(record.MachineOwner, out HashSet<string> ids)) return;
        ids.Remove(record.HostId);
        if (ids.Count == 0) hostsByMachine.Remove(record.MachineOwner);
    }

    private static void AddClaim(SlotClaim claim)
    {
        if (!claimsByItem.TryGetValue(claim.Item, out Dictionary<string, SlotClaim> claims))
            claimsByItem.Add(claim.Item, claims = new Dictionary<string, SlotClaim>(StringComparer.Ordinal));
        claims[claim.SourceKey] = claim;
    }

    private static void RemoveClaim(SlotClaim claim)
    {
        if (!claimsByItem.TryGetValue(claim.Item, out Dictionary<string, SlotClaim> claims)) return;
        claims.Remove(claim.SourceKey);
        if (claims.Count == 0) claimsByItem.Remove(claim.Item);
    }

    private static void ReconcileItem(ItemIdentity item)
    {
        SlotClaim winner = ResolveWinningClaim(item);
        if (winner == null)
        {
            if (appliedByItem.Remove(item, out List<AppliedInfluence> previous))
                foreach (AppliedInfluence influence in previous) RemoveInfluence(influence);
            return;
        }

        appliedByItem.TryGetValue(item, out List<AppliedInfluence> active);
        active ??= new List<AppliedInfluence>();
        int count = Mathf.Max(active.Count, winner.Influences.Length);
        for (int index = 0; index < count; index++)
        {
            AppliedInfluence previous = index < active.Count ? active[index] : null;
            bool hasNext = index < winner.Influences.Length;
            string sourceKey = hasNext ? winner.SourceKey + "\u001f" + index : null;
            ItemWorldLayerInfluence nextDefinition = hasNext ? winner.Influences[index] : default;
            bool sameSource = previous != null && hasNext &&
                string.Equals(previous.SourceKey, sourceKey, StringComparison.Ordinal) &&
                string.Equals(previous.Definition.LayerId, nextDefinition.LayerId, StringComparison.Ordinal);

            if (!sameSource && previous != null)
            {
                RemoveInfluence(previous);
                active[index] = null;
                previous = null;
            }

            if (!hasNext)
                continue;

            if (previous == null)
            {
                previous = new AppliedInfluence
                {
                    SourceKey = sourceKey,
                    Owner = new SourceToken(sourceKey),
                    WorldKey = winner.WorldKey,
                    Position = winner.Position,
                    Definition = nextDefinition,
                    Sink = WorldLayerInfluenceSinkRegistry.Resolve(nextDefinition.LayerId)
                };
                if (index < active.Count) active[index] = previous;
                else active.Add(previous);
            }
            else
            {
                IWorldLayerInfluenceSink nextSink = WorldLayerInfluenceSinkRegistry.Resolve(nextDefinition.LayerId);
                if (previous.IsApplied &&
                    (!string.Equals(previous.WorldKey, winner.WorldKey, StringComparison.Ordinal) ||
                     !ReferenceEquals(previous.Sink, nextSink)))
                    RemoveInfluence(previous);
                previous.Sink = nextSink;
            }

            previous.WorldKey = winner.WorldKey;
            previous.Position = winner.Position;
            previous.Definition = nextDefinition;
            previous.Sink ??= WorldLayerInfluenceSinkRegistry.Resolve(nextDefinition.LayerId);
            previous.IsApplied = previous.Sink?.SetSource(
                previous.Owner, previous.WorldKey, previous.Position, previous.Definition) == true;
            if (!previous.IsApplied && previous.Sink != null)
                previous.Sink.RemoveSource(previous.Owner, previous.WorldKey);
        }

        if (active.Count > winner.Influences.Length)
            active.RemoveRange(winner.Influences.Length, active.Count - winner.Influences.Length);
        if (active.Count == 0) appliedByItem.Remove(item);
        else appliedByItem[item] = active;
    }

    private static SlotClaim ResolveWinningClaim(ItemIdentity item)
    {
        if (!claimsByItem.TryGetValue(item, out Dictionary<string, SlotClaim> claims)) return null;
        SlotClaim winner = null;
        foreach (SlotClaim candidate in claims.Values)
        {
            if (winner == null || candidate.Priority < winner.Priority ||
                candidate.Priority == winner.Priority &&
                (candidate.Depth < winner.Depth ||
                 candidate.Depth == winner.Depth && string.CompareOrdinal(candidate.SourceKey, winner.SourceKey) < 0))
                winner = candidate;
        }
        return winner;
    }

    private static void RemoveInfluence(AppliedInfluence influence)
    {
        if (!influence.IsApplied) return;
        influence.Sink?.RemoveSource(influence.Owner, influence.WorldKey);
        influence.IsApplied = false;
    }

    private sealed class SourceToken
    {
        public readonly string Id;
        public SourceToken(string id) => Id = id;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        if (movementSubscribed) ItemMgr.RuntimeItemMoved -= OnRuntimeItemMoved;
        if (despawnSubscribed) ItemMgr.RuntimeItemDespawning -= OnRuntimeItemDespawning;
        if (machineSubscribed)
        {
            MachineWorld.NodeStateChanged -= OnMachineNodeStateChanged;
            MachineWorld.NodeRemoved -= OnMachineNodeRemoved;
        }
        if (worldReadySubscribed) GameplayWorldRegistry.RuntimeWorldReady -= OnRuntimeWorldReady;
        movementSubscribed = despawnSubscribed = machineSubscribed = worldReadySubscribed = false;
        foreach (ItemData itemData in hostsByNestedItemData.Keys)
            itemData.ModuleStructureChanged -= OnNestedItemModuleStructureChanged;
        foreach (Inventory_Data inventoryData in hostsByNestedInventoryData.Keys)
            inventoryData.RuntimeStructureChanged -= OnNestedInventoryStructureChanged;
        foreach (Inventory_ModuleData inventoryModule in hostsByNestedInventoryModule.Keys)
            inventoryModule.InventoryMapStructureChanged -= OnNestedInventoryMapStructureChanged;
        foreach (List<AppliedInfluence> influences in appliedByItem.Values)
            foreach (AppliedInfluence influence in influences) RemoveInfluence(influence);
        hosts.Clear();
        claimsByItem.Clear();
        appliedByItem.Clear();
        hostsByAnchorMovementItem.Clear();
        hostsByMachine.Clear();
        hostsByNestedItemData.Clear();
        hostsByNestedInventoryModule.Clear();
        hostsByNestedInventoryData.Clear();
        movementHostScratch.Clear();
        machineHostScratch.Clear();
        worldHostScratch.Clear();
        movementItemScratch.Clear();
        machineBindingScratch.Clear();
    }

    #endregion
}
