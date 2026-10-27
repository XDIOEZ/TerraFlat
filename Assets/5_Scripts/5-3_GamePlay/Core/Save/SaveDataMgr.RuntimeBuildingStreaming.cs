using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class SaveDataMgr
{
    #region 建筑区块卸载与冷快照
    private readonly Dictionary<string, HashSet<int>> unloadedRuntimeBuildingGuids = new(StringComparer.Ordinal);
    private readonly HashSet<string> buildingUnloadFailures = new(StringComparer.Ordinal);

    // 冷区块的实体不在 ItemMgr 中，自动保存不能把它们误判成已拆除的建筑。
    private void RemoveReplacedRuntimeBuildingSnapshots(string key, List<ItemData> existing, List<ItemData> incoming)
    {
        if (!unloadedRuntimeBuildingGuids.TryGetValue(key, out HashSet<int> unloaded))
        {
            existing.RemoveAll(IsRuntimeBuildingData);
            return;
        }
        var replaced = new HashSet<int>();
        foreach (ItemData data in incoming) if (data != null) replaced.Add(data.Guid);
        existing.RemoveAll(data => IsRuntimeBuildingData(data) &&
            (!unloaded.Contains(data.Guid) || replaced.Contains(data.Guid)));
    }

    private void MarkRuntimeBuildingResident(string key, int guid)
    {
        if (unloadedRuntimeBuildingGuids.TryGetValue(key, out HashSet<int> unloaded) &&
            unloaded.Remove(guid) && unloaded.Count == 0)
            unloadedRuntimeBuildingGuids.Remove(key);
    }

    internal bool TryUnloadRuntimeBuildingsForChunk(RuntimeWorldAddress address)
    {
        if (!GameNetwork.HasStateAuthority) return true;
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (chunks == null) return true;
        ItemMgr manager = ItemMgr.GetInstance();
        if (SaveData == null || manager == null) return !chunks.HasRuntimeBuildings(address);

        string planet = ResolveRuntimePlanetName();
        string key = BuildChunkKey(planet, ToChunkName(address));
        var candidates = new List<Item>();
        var captured = new List<Item>();
        var snapshots = new List<ItemData>();
        chunks.CopyRuntimeBuildings(address, candidates);
        try
        {
            foreach (Item item in candidates)
            {
                if (!IsRuntimeBuildingItem(item, out _) || item.DestructionHandled || item.IsInPool ||
                    GameplayWorldRegistry.GetItemWorldKey(item) != planet ||
                    FlatWorld.Spaceflight.SpaceSession.Current?.Owns(item.itemData.Guid) == true)
                {
                    chunks.UnregisterRuntimeBuilding(item);
                    continue;
                }
                if (!chunks.ResolveWorldAddress(item.transform.position).Equals(address))
                {
                    chunks.RegisterRuntimeBuilding(item);
                    continue;
                }
                // 任意一件捕获失败都保留整个区块现场，不先卸载一半船体。
                item.Save();
                snapshots.Add(CloneItemData(item.itemData));
                captured.Add(item);
            }
            if (snapshots.Count > 0 || runtimeBuildingDirtyChunks.Contains(key))
                MergeRuntimeBuildingItems(planet, address, snapshots);
        }
        catch (Exception exception)
        {
            if (buildingUnloadFailures.Add(key))
                Debug.LogError($"[SaveDataMgr] 建筑区块快照失败，保留现场 {key}: {exception}");
            return false;
        }

        if (snapshots.Count > 0)
        {
            if (!unloadedRuntimeBuildingGuids.TryGetValue(key, out HashSet<int> unloaded))
                unloadedRuntimeBuildingGuids.Add(key, unloaded = new HashSet<int>());
            foreach (ItemData data in snapshots) unloaded.Add(data.Guid);
        }
        for (int i = 0; i < captured.Count; i++)
        {
            try { manager.DespawnItem(captured[i], saveData: false); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
        if (chunks.HasRuntimeBuildings(address)) return false;

        // 清除本次绑定的恢复门闩；数据仍被缓存时重入也必须重新生成建筑。
        restoredRuntimeBuildingChunks.Remove($"{key}\u001f{address.DimensionId}");
        runtimeBuildingDirtyChunks.Remove(key);
        runtimeBuildingDirtyAddresses.Remove(key);
        buildingUnloadFailures.Remove(key);
        return true;
    }
    #endregion
}
