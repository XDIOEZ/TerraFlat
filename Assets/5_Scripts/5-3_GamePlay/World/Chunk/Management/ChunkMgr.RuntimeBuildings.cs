using System.Collections.Generic;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

// 建筑按锚点归属区块，回收只访问本区块，不扫描全世界或依赖 Transform 父级。
internal sealed class RuntimeBuildingChunkRegistry
{
    #region 区块归属索引
    private readonly Dictionary<RuntimeWorldAddress, HashSet<Item>> itemsByChunk = new();
    private readonly Dictionary<Item, RuntimeWorldAddress> owners = new(ItemReferenceComparer.Instance);

    internal void Track(Item item, RuntimeWorldAddress address)
    {
        if (item == null || item.DestructionHandled || item.IsInPool) return;
        if (owners.TryGetValue(item, out RuntimeWorldAddress previous) && previous.Equals(address)) return;
        Remove(item);
        if (!itemsByChunk.TryGetValue(address, out HashSet<Item> items))
            itemsByChunk.Add(address, items = new HashSet<Item>(ItemReferenceComparer.Instance));
        items.Add(item);
        owners.Add(item, address);
        item.OnItemDestroy += Remove;
    }

    internal void Remove(Item item)
    {
        if (ReferenceEquals(item, null) || !owners.TryGetValue(item, out RuntimeWorldAddress address)) return;
        owners.Remove(item);
        item.OnItemDestroy -= Remove;
        if (itemsByChunk.TryGetValue(address, out HashSet<Item> items) && items.Remove(item) && items.Count == 0)
            itemsByChunk.Remove(address);
    }

    internal void CopyTo(RuntimeWorldAddress address, List<Item> output)
    {
        output.Clear();
        if (itemsByChunk.TryGetValue(address, out HashSet<Item> items)) output.AddRange(items);
    }

    internal bool HasItems(RuntimeWorldAddress address)
        => itemsByChunk.TryGetValue(address, out HashSet<Item> items) && items.Count > 0;

    internal void Clear()
    {
        foreach (Item item in owners.Keys)
            if (!ReferenceEquals(item, null)) item.OnItemDestroy -= Remove;
        owners.Clear();
        itemsByChunk.Clear();
    }
    #endregion
}

public partial class ChunkMgr
{
    #region 普通建筑流送归属
    private readonly RuntimeBuildingChunkRegistry runtimeBuildings = new();

    internal void RegisterRuntimeBuilding(Item item)
    {
        if (RuntimeWorldResidentEntityUtility.IsPersistentWorldEntity(item))
        { runtimeBuildings.Remove(item); return; }
        if (!IsWorldModelRuntimeActive || item == null || item.DestructionHandled || item.IsInPool ||
            item.InHand || item.PersistenceOwner != null || item.gameObject.scene != GameplayWorldRegistry.CurrentScene)
            return;
        runtimeBuildings.Track(item, ResolveWorldAddress(item.transform.position));
    }

    internal void UnregisterRuntimeBuilding(Item item) => runtimeBuildings.Remove(item);
    internal void CopyRuntimeBuildings(RuntimeWorldAddress address, List<Item> output) => runtimeBuildings.CopyTo(address, output);
    internal bool HasRuntimeBuildings(RuntimeWorldAddress address) => runtimeBuildings.HasItems(address);
    #endregion
}
