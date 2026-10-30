using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>运行时建筑的记录、同步或分帧捕获与恢复。</summary>
public partial class SaveDataMgr
{
    #region 建筑差量索引
    private readonly HashSet<string> runtimeBuildingDirtyChunks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeWorldAddress> runtimeBuildingDirtyAddresses = new(StringComparer.Ordinal);
    #endregion

    #region Runtime building persistence

    /// <summary>
    /// 标记新版 WorldModel 中动态建筑所在区块需要重写。
    /// 通过 Mod_Building.Save 调用，覆盖安装、拆除、受损和拆除后召唤器掉落等路径。
    /// </summary>
    public void RecordRuntimeBuildingChange(Item buildingItem)
    {
        RecordRuntimeBuildingChangeAtPosition(
            buildingItem,
            buildingItem != null ? (Vector2)buildingItem.transform.position : default);
    }

    /// <summary>按指定世界位置标记动态建筑区块脏；物理载具跨区块时用它同时清理旧区块快照。</summary>
    public void RecordRuntimeBuildingChangeAtPosition(Item buildingItem, Vector2 worldPosition)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null ||
            !IsRuntimeBuildingChangeSource(buildingItem))
        {
            return;
        }

        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager == null || !chunkManager.IsWorldModelRuntimeActive)
            return;

        string planetName = ResolveRuntimePlanetName();
        RuntimeWorldAddress address = chunkManager.ResolveWorldAddress(worldPosition);
        string key = BuildChunkKey(planetName, ToChunkName(address));
        MarkChunkDifferenceChanged(key);
        if (!buildingItem.DestructionHandled)
        {
            chunkManager.RegisterRuntimeBuilding(buildingItem);
            MarkRuntimeBuildingResident(key, buildingItem.itemData.Guid);
        }
        runtimeBuildingDirtyChunks.Add(key);
        runtimeBuildingDirtyAddresses[key] = address;
        if (chunkDeltas.TryGetValue(key, out ChunkSaveRecord current))
            current.RemovedItemGuids?.Remove(buildingItem.itemData.Guid);
    }

    /// <summary>建筑交给其它世界所有者前立即撤掉地面记录，避免保存之前重载区块又生成同一实例。</summary>
    public void RemoveRuntimeBuildingRecord(Item buildingItem)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null || !IsRuntimeBuildingChangeSource(buildingItem)) return;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.IsWorldModelRuntimeActive) return;
        RecordRuntimeBuildingChange(buildingItem);
        string planetName = ResolveRuntimePlanetName();
        RuntimeWorldAddress address = manager.ResolveWorldAddress(buildingItem.transform.position);
        string key = BuildChunkKey(planetName, ToChunkName(address));
        if (!chunkDeltas.TryGetValue(key, out ChunkSaveRecord record) || record == null)
            chunkDeltas[key] = record = CreateRuntimeChunkDelta(planetName, address);
        int guid = buildingItem.itemData.Guid;
        MarkRuntimeBuildingResident(key, guid);
        record.ChangedItems?.RemoveAll(data => data?.Guid == guid);
        record.RemovedItemGuids ??= new List<int>();
        if (!record.RemovedItemGuids.Contains(guid)) record.RemovedItemGuids.Add(guid);
        record.RemovedItemGuids.Sort();
    }

    /// <summary>保存新版 WorldModel 中所有动态建筑，而不是只保存某一种建筑预制体。</summary>
    private void CaptureRuntimeBuildingStates()
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null ||
            ItemMgr.Instance == null || ChunkMgr.ExistingInstance == null ||
            !ChunkMgr.ExistingInstance.IsWorldModelRuntimeActive)
        {
            return;
        }

        string planetName = ResolveRuntimePlanetName();
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        var savedItemsByKey = new Dictionary<string, List<ItemData>>(StringComparer.Ordinal);
        var addressesByKey = new Dictionary<string, RuntimeWorldAddress>(StringComparer.Ordinal);
        var failedKeys = new HashSet<string>(StringComparer.Ordinal);
        using var itemList = UnityEngine.Pool.ListPool<Item>.Get(out var worldItems);
        GameplayWorldRegistry.CopyWorldItems(planetName, worldItems);

        foreach (Item item in worldItems)
        {
            if (!IsRuntimeBuildingItemInWorld(item, planetName))
                continue;

            RuntimeWorldAddress address = chunkManager.ResolveWorldAddress(item.transform.position);
            string key = BuildChunkKey(planetName, ToChunkName(address));
            addressesByKey[key] = address;

            try
            {
                item.Save();
                if (item.itemData == null)
                    throw new InvalidDataException($"建筑 {item.name} 的 ItemData 为空");

                if (!savedItemsByKey.TryGetValue(key, out List<ItemData> savedItems))
                {
                    savedItems = new List<ItemData>();
                    savedItemsByKey[key] = savedItems;
                }

                savedItems.Add(CloneItemData(item.itemData));
            }
            catch (Exception exception)
            {
                failedKeys.Add(key);
                Debug.LogError($"[SaveDataMgr] 保存运行时建筑失败：{item.name}", item);
                Debug.LogException(exception);
            }
        }

        var keysToCapture = new HashSet<string>(StringComparer.Ordinal);
        string worldPrefix = planetName + "\u001f";
        foreach (string dirtyKey in runtimeBuildingDirtyChunks)
            if (dirtyKey.StartsWith(worldPrefix, StringComparison.Ordinal)) keysToCapture.Add(dirtyKey);
        keysToCapture.UnionWith(savedItemsByKey.Keys);
        foreach (string key in keysToCapture)
        {
            if (failedKeys.Contains(key))
                continue;

            if (!addressesByKey.TryGetValue(key, out RuntimeWorldAddress address) &&
                !runtimeBuildingDirtyAddresses.TryGetValue(key, out address))
            {
                continue;
            }

            savedItemsByKey.TryGetValue(key, out List<ItemData> savedItems);
            MergeRuntimeBuildingItems(planetName, address, savedItems);
            runtimeBuildingDirtyChunks.Remove(key);
            runtimeBuildingDirtyAddresses.Remove(key);
        }
    }

    /// <summary>判断当前运行时对象是否是应该写入世界存档的建筑。</summary>
    private static bool IsRuntimeBuildingItem(Item item, out Mod_Building building)
    {
        building = null;
        if (RuntimeWorldResidentEntityUtility.IsPersistentWorldEntity(item)) return false;
        if (item?.PersistenceOwner != null || MachineWorld.OwnsWorldItem(item?.itemData)) return false;
        if (item == null || item is Player || item is Map || item.itemData == null)
            return false;

        building = item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
        if (building?.Data == null)
            return false;

        if (building.Data.Role == BuildingRole.PlacedBuilding)
        {
            return building.CurrentState is BuildingState.Installed or
                BuildingState.Damaged or BuildingState.Uninstalling;
        }

        // 背包中的召唤器由玩家数据保存；只有已经掉到世界里的召唤器才归入区块存档。
        return building.Data.Role == BuildingRole.Summoner &&
               !item.InHand && item.Owner == null && !item.DestructionHandled;
    }

    // 建筑快照只采集当前世界，不能把同坐标的其他世界实例写入当前区块。
    private static bool IsRuntimeBuildingItemInWorld(Item item, string worldKey)
        => item != null && GameplayWorldRegistry.GetItemWorldKey(item) == worldKey &&
           IsRuntimeBuildingItem(item, out _);

    /// <summary>判断建筑 SaveData 是否属于动态世界建筑，供差量替换和清理使用。</summary>
    private static bool IsRuntimeBuildingData(ItemData data)
    {
        if (RuntimeWorldResidentEntityUtility.IsPersistentWorldEntityData(data)) return false;
        try
        {
            return data != null && Mod_Building.TryReadBuildingData(
                data, out _, out Mod_Building.Building_Data buildingData) &&
                buildingData != null &&
                (buildingData.Role == BuildingRole.PlacedBuilding ||
                 (buildingData.Role == BuildingRole.Summoner && !data.inHand));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>判断建筑 SaveData 是否可以在世界加载时重新实例化。</summary>
    private static bool IsRestorableRuntimeBuildingData(ItemData data)
    {
        if (RuntimeWorldResidentEntityUtility.IsPersistentWorldEntityData(data)) return false;
        if (MachineWorld.OwnsWorldItem(data)) return true;
        try
        {
            if (data == null || !Mod_Building.TryReadBuildingData(
                    data, out _, out Mod_Building.Building_Data buildingData) ||
                buildingData == null)
            {
                return false;
            }

            return (buildingData.Role == BuildingRole.PlacedBuilding &&
                    buildingData.State is BuildingState.Installed or BuildingState.Damaged) ||
                   (buildingData.Role == BuildingRole.Summoner && !data.inHand);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>建筑被安装、拆除或状态保存时都要记录所在区块，确保空区块也能清掉旧建筑差量。</summary>
    private static bool IsRuntimeBuildingChangeSource(Item item)
    {
        if (item == null || item.PersistenceOwner != null || item.itemData == null || item is Player || item is Map)
            return false;

        Mod_Building building = item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
        if (building?.Data == null)
            return false;

        if (building.Data.Role == BuildingRole.PlacedBuilding)
        {
            return building.CurrentState is BuildingState.Installed or
                BuildingState.Damaged or BuildingState.Uninstalling;
        }

        return building.Data.Role == BuildingRole.Summoner &&
               !item.InHand && item.Owner == null && !item.DestructionHandled;
    }

    private void MergeRuntimeBuildingItems(
        string planetName,
        RuntimeWorldAddress address,
        List<ItemData> savedItems)
    {
        string chunkName = ToChunkName(address);
        string key = BuildChunkKey(planetName, chunkName);
        MarkChunkDifferenceChanged(key);
        savedItems ??= new List<ItemData>();

        if (!chunkDeltas.TryGetValue(key, out ChunkSaveRecord record) || record == null)
        {
            record = CreateRuntimeChunkDelta(planetName, address);
        }

        record.ChangedItems ??= new List<ItemData>();
        RemoveReplacedRuntimeBuildingSnapshots(key, record.ChangedItems, savedItems);
        record.RemovedItemGuids ??= new List<int>();
        for (int i = 0; i < savedItems.Count; i++)
        {
            ItemData savedItem = savedItems[i];
            if (savedItem == null)
                continue;

            record.RemovedItemGuids.Remove(savedItem.Guid);
            record.ChangedItems.Add(savedItem);
        }

        record.ChangedItems.Sort((left, right) => (left?.Guid ?? 0).CompareTo(right?.Guid ?? 0));
        record.RemovedItemGuids.Sort();
        if (record.HasChanges)
            chunkDeltas[key] = record;
        else
            chunkDeltas.Remove(key);
    }

    /// <summary>新版区块就绪时恢复该区块的全部动态建筑，包括建筑模块状态和耐久。</summary>
    public bool RestoreRuntimeBuildingsForChunk(RuntimeWorldAddress address)
    {
        if (!GameNetwork.HasStateAuthority) return true;
        if (SaveData == null || ItemMgr.Instance == null) return false;

        EnsureRuntimeBuildingRestoreScope();
        string planetName = ResolveRuntimePlanetName();
        string chunkName = ToChunkName(address);
        string restoreKey = $"{BuildChunkKey(planetName, chunkName)}\u001f{address.DimensionId}";
        if (!restoredRuntimeBuildingChunks.Add(restoreKey))
            return true;

        string chunkKey = BuildChunkKey(planetName, chunkName);
        var candidates = new List<ItemData>();
        var removedGuids = new HashSet<int>();
        if (chunkDeltas.TryGetValue(chunkKey, out ChunkSaveRecord delta))
        {
            if (delta?.RemovedItemGuids != null)
                removedGuids.UnionWith(delta.RemovedItemGuids);
            CollectRuntimeBuildingData(delta?.ChangedItems, candidates);
        }

        var restoredGuids = new HashSet<int>();
        bool succeeded = true;
        for (int i = 0; i < candidates.Count; i++)
        {
            ItemData savedData = candidates[i];
            if (savedData == null || removedGuids.Contains(savedData.Guid) ||
                !restoredGuids.Add(savedData.Guid))
            {
                continue;
            }

            Item existing = ItemMgr.Instance.GetItemByGuid(savedData.Guid);
            if (existing != null)
            {
                if (GameplayWorldRegistry.GetItemWorldKey(existing) != planetName ||
                    !existing.IsInitialized || !IsRuntimeBuildingItem(existing, out _)) succeeded = false;
                continue;
            }

            Item restored = null;
            try
            {
                ItemData runtimeData = CloneAndRebaseItemData(savedData);
                ItemTransform transformData = runtimeData.transform ?? new ItemTransform();
                if (MachineWorld.OwnsWorldItem(runtimeData))
                {
                    if (MachineWorld.RestoreMachine(runtimeData) == null)
                    {
                        succeeded = false;
                        continue;
                    }
                    // 机器快照接管成功后删除旧区块 Item 来源，避免拆除后旧差量再次恢复同一台设施。
                    delta?.ChangedItems?.RemoveAll(item => item?.Guid == runtimeData.Guid);
                    continue;
                }
                restored = ItemMgr.Instance.InstantiateItem(
                    runtimeData,
                    transformData.position,
                    transformData.rotation,
                    transformData.scale);
                if (restored == null) { succeeded = false; continue; }
                restored.Load();
                RecordRuntimeBuildingChange(restored);
            }
            catch (Exception exception)
            {
                succeeded = false;
                if (restored != null && !restored.DestructionHandled)
                {
                    try { ItemMgr.Instance.DespawnItem(restored, saveData: false); }
                    catch (Exception cleanup) { Debug.LogException(cleanup); }
                }
                Debug.LogError(
                    $"[SaveDataMgr] 恢复运行时建筑失败：{savedData.IDName}, Guid={savedData.Guid}");
                Debug.LogException(exception);
            }
        }
        if (!succeeded) restoredRuntimeBuildingChunks.Remove(restoreKey);
        return succeeded;
    }

    public void CollectArchivedRuntimeBuildings(string worldKey, Dictionary<int, ItemData> output)
    {
        // 冷区块的建筑仍参与气密，活动实例由调用方覆盖，已移交身份不再读回。
        foreach (ChunkSaveRecord record in chunkDeltas.Values)
        {
            if (record?.PlanetName != worldKey || record.ChangedItems == null) continue;
            string restoredKey = $"{BuildChunkKey(worldKey, record.ChunkName)}\u001f{WorldAddress.FromWorldKey(worldKey).DimensionId}";
            if (worldKey == GameplayWorldRegistry.ExecutionWorldKey && restoredRuntimeBuildingChunks.Contains(restoredKey)) continue;
            foreach (ItemData data in record.ChangedItems)
                if (data != null && record.RemovedItemGuids?.Contains(data.Guid) != true &&
                    !MachineWorld.OwnsWorldItem(data) && IsRestorableRuntimeBuildingData(data) &&
                    Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data building) &&
                    building.Role == BuildingRole.PlacedBuilding)
                    output[data.Guid] = data;
        }
    }

    private static void CollectRuntimeBuildingData(
        IEnumerable<ItemData> source,
        List<ItemData> output)
    {
        if (source == null)
            return;

        foreach (ItemData data in source)
        {
            if (IsRestorableRuntimeBuildingData(data))
                output.Add(data);
        }
    }

    private void EnsureRuntimeBuildingRestoreScope()
    {
        WorldRuntime world = ChunkMgr.ExistingInstance?.WorldRuntime;
        long epoch = world?.Epoch ?? long.MinValue;
        if (ReferenceEquals(restoredRuntimeBuildingWorld, world) &&
            restoredRuntimeBuildingEpoch == epoch)
        {
            return;
        }

        restoredRuntimeBuildingChunks.Clear();
        restoredRuntimeBuildingWorld = world;
        restoredRuntimeBuildingEpoch = epoch;
    }

    #endregion

    #region 建筑分帧采集
    private IEnumerator CaptureRuntimeBuildingStatesCoroutine(AutoSaveWorkBudget budget)
    {
        ItemMgr items = ItemMgr.GetInstance();
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (!GameNetwork.HasStateAuthority || SaveData == null || items == null ||
            chunks == null || !chunks.IsWorldModelRuntimeActive)
            yield break;

        string planetName = ResolveRuntimePlanetName();
        var addresses = new Dictionary<string, RuntimeWorldAddress>(StringComparer.Ordinal);
        string worldPrefix = planetName + "\u001f";
        foreach (var pair in runtimeBuildingDirtyAddresses)
            if (pair.Key.StartsWith(worldPrefix, StringComparison.Ordinal)) addresses.Add(pair.Key, pair.Value);
        var snapshot = new List<Item>();
        GameplayWorldRegistry.CopyWorldItems(planetName, snapshot);
        foreach (Item item in snapshot)
        {
            if (IsRuntimeBuildingItemInWorld(item, planetName))
            {
                RuntimeWorldAddress address = chunks.ResolveWorldAddress(item.transform.position);
                addresses[BuildChunkKey(planetName, ToChunkName(address))] = address;
            }
            if (budget.ShouldYield())
                yield return budget.WaitForNextSlice();
        }

        var savedItems = new List<ItemData>();
        foreach (var pair in addresses)
        {
            GameplayWorldRegistry.CopyWorldItems(planetName, snapshot);
            savedItems.Clear();
            bool succeeded = true;
            // 同区块建筑的捕获与合并不跨帧，避免拆除后把旧建筑快照重新写回来。
            foreach (Item item in snapshot)
            {
                if (!IsRuntimeBuildingItemInWorld(item, planetName)) continue;
                RuntimeWorldAddress address = chunks.ResolveWorldAddress(item.transform.position);
                if (!string.Equals(BuildChunkKey(planetName, ToChunkName(address)), pair.Key, StringComparison.Ordinal)) continue;
                try
                {
                    item.Save();
                    if (item.itemData == null) throw new InvalidDataException($"建筑 {item.name} 的 ItemData 为空");
                    savedItems.Add(CloneItemData(item.itemData));
                }
                catch (Exception exception)
                {
                    succeeded = false;
                    Debug.LogError($"[SaveDataMgr] 分帧保存运行时建筑失败：{item.name}", item);
                    Debug.LogException(exception);
                }
            }
            if (succeeded)
            {
                MergeRuntimeBuildingItems(planetName, pair.Value, savedItems);
                runtimeBuildingDirtyChunks.Remove(pair.Key);
                runtimeBuildingDirtyAddresses.Remove(pair.Key);
            }
            yield return budget.WaitForNextSlice();
        }
    }
    #endregion
}
