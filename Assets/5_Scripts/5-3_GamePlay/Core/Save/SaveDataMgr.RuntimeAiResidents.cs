using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// 运行时 AI 的休眠快照仓库。活体远离玩家时把完整 ItemData 留在原区块差量中，
/// 只卸载昂贵的 GameObject；再次靠近时以同一 GUID 和模块状态恢复。
/// </summary>
public partial class SaveDataMgr
{
    #region 休眠索引

    private sealed class ParkedWorldState
    {
        public readonly Dictionary<RuntimeWorldAddress, List<ItemData>> ByAddress = new();
        public readonly Dictionary<int, RuntimeWorldAddress> ByGuid = new();
        public readonly Dictionary<string, int> Species = new(StringComparer.Ordinal);
        public readonly Dictionary<RuntimeWorldAddress, int> Cursors = new();
        public readonly List<RuntimeWorldAddress> Addresses = new();
        public readonly Dictionary<RuntimeWorldAddress, int> Indices = new();
        public readonly HashSet<RuntimeWorldAddress> QueryAddresses = new();
        public readonly HashSet<int> SceneDetachedLiveGuids = new();
    }
    private readonly Dictionary<string, ParkedWorldState> parkedWorldStates = new(StringComparer.Ordinal);
    private ParkedWorldState ParkedWorld
    {
        get
        {
            string key = GameplayWorldRegistry.ExecutionWorldKey;
            if (!parkedWorldStates.TryGetValue(key, out ParkedWorldState value)) parkedWorldStates.Add(key, value = new ParkedWorldState());
            return value;
        }
    }
    private Dictionary<RuntimeWorldAddress, List<ItemData>> parkedRuntimeAiByAddress => ParkedWorld.ByAddress;
    private Dictionary<int, RuntimeWorldAddress> parkedRuntimeAiAddressByGuid => ParkedWorld.ByGuid;
    private Dictionary<string, int> parkedRuntimeAiSpeciesCounts => ParkedWorld.Species;
    private Dictionary<RuntimeWorldAddress, int> parkedRuntimeAiItemCursors => ParkedWorld.Cursors;
    private List<RuntimeWorldAddress> parkedRuntimeAiAddresses => ParkedWorld.Addresses;
    private Dictionary<RuntimeWorldAddress, int> parkedRuntimeAiAddressIndices => ParkedWorld.Indices;
    private HashSet<RuntimeWorldAddress> parkedRuntimeAiQueryAddresses => ParkedWorld.QueryAddresses;

    public int ParkedRuntimeAiCount => parkedRuntimeAiAddressByGuid.Count;

    /// <summary>只读现有世界的休眠索引，监控不能初始化仓库或触发居民恢复。</summary>
    public int GetExistingParkedRuntimeAiCount(string world, string speciesId = null)
    {
        if (string.IsNullOrWhiteSpace(world) || !parkedWorldStates.TryGetValue(world, out ParkedWorldState state))
            return 0;
        return speciesId == null ? state.ByGuid.Count : state.Species.TryGetValue(speciesId, out int count) ? count : 0;
    }

    // 休眠居民仍占用身份，新出生必须拒绝复用该 GUID。
    public bool ContainsParkedRuntimeAi(int guid)
    {
        EnsureRuntimeAiRestoreScope();
        return parkedRuntimeAiAddressByGuid.ContainsKey(guid);
    }

    /// <summary>查询尚未装载表现的物种居民数，供本地区域密度查询快速跳过空物种。</summary>
    public int GetParkedRuntimeAiSpeciesCount(string speciesId)
    {
        EnsureRuntimeAiRestoreScope();
        return !string.IsNullOrWhiteSpace(speciesId) &&
               parkedRuntimeAiSpeciesCounts.TryGetValue(speciesId, out int count) ? count : 0;
    }

    /// <summary>只查询目标周围的区块快照，供当地生态密度判断使用。</summary>
    public int CountParkedRuntimeAiWithinRadius(SpawnerConfig config, Vector3 center, float radiusSqr)
    {
        return config?.SpawnEntries == null ? 0 : CountParkedRuntimeAiWithinRadius(center, radiusSqr, config, null);
    }

    /// <summary>查询目标周围某物种的休眠居民，防止同一区域重复出生。</summary>
    public int CountParkedRuntimeAiSpeciesWithinRadius(string speciesId, Vector3 center, float radiusSqr)
    {
        return GetParkedRuntimeAiSpeciesCount(speciesId) == 0
            ? 0 : CountParkedRuntimeAiWithinRadius(center, radiusSqr, null, speciesId);
    }

    private int CountParkedRuntimeAiWithinRadius(
        Vector3 center, float radiusSqr, SpawnerConfig config, string speciesId)
    {
        EnsureRuntimeAiRestoreScope();
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager == null || parkedRuntimeAiByAddress.Count == 0 || radiusSqr < 0f)
            return 0;

        var profile = chunkManager.ActiveGenerationProfile;
        if (profile == null)
            return 0;
        float stepX = Mathf.Max(1, profile.Width);
        float stepY = Mathf.Max(1, profile.Height);
        float radius = Mathf.Sqrt(radiusSqr);
        int stepsX = Mathf.CeilToInt(radius / stepX) + 1;
        int stepsY = Mathf.CeilToInt(radius / stepY) + 1;
        int count = 0;
        long gridCells = (2L * stepsX + 1L) * (2L * stepsY + 1L);
        if (gridCells > parkedRuntimeAiAddresses.Count + 2L * ParkedRuntimeAiCount)
        {
            // 休眠地址较稀疏时遍历已有地址，比探测大量空区块更省工作量。
            for (int i = 0; i < parkedRuntimeAiAddresses.Count; i++)
                count += CountMatchingParkedRuntimeAi(
                    parkedRuntimeAiByAddress[parkedRuntimeAiAddresses[i]], center, radiusSqr, config, speciesId);
            return count;
        }

        parkedRuntimeAiQueryAddresses.Clear();
        for (int y = -stepsY; y <= stepsY; y++)
        {
            for (int x = -stepsX; x <= stepsX; x++)
            {
                RuntimeWorldAddress address = chunkManager.ResolveWorldAddress(
                    new Vector2(center.x + x * stepX, center.y + y * stepY));
                if (!parkedRuntimeAiQueryAddresses.Add(address) ||
                    !parkedRuntimeAiByAddress.TryGetValue(address, out List<ItemData> items))
                    continue;

                count += CountMatchingParkedRuntimeAi(items, center, radiusSqr, config, speciesId);
            }
        }
        parkedRuntimeAiQueryAddresses.Clear();
        return count;
    }

    /// <summary>同一区块只检查真正存储的居民，并按循环世界距离筛选。</summary>
    private static int CountMatchingParkedRuntimeAi(
        List<ItemData> items, Vector3 center, float radiusSqr, SpawnerConfig config, string speciesId)
    {
        int count = 0;
        for (int i = 0; i < items.Count; i++)
        {
            ItemData item = items[i];
            if (item?.transform != null &&
                MatchesParkedRuntimeAi(item, config, speciesId) &&
                WorldTopologyRuntime.SqrDistance(item.transform.position, center) <= radiusSqr)
                count++;
        }
        return count;
    }

    /// <summary>按冻结的生成目录匹配组或物种，不创建运行时 Item。</summary>
    private static bool MatchesParkedRuntimeAi(ItemData item, SpawnerConfig config, string speciesId)
    {
        if (speciesId != null)
            return string.Equals(item.IDName, speciesId, StringComparison.Ordinal);
        for (int i = 0; i < config.SpawnEntries.Count; i++)
            if (string.Equals(item.IDName, config.SpawnEntries[i]?.PrefabName, StringComparison.Ordinal))
                return true;
        return false;
    }

    // 跨帧自动保存快照期间禁止生态实体改变装载状态。
    private int ecologySnapshotReaders;
    public bool IsEcologySnapshotInProgress => ecologySnapshotReaders > 0;

    private readonly struct RuntimeAiSceneUnbindingCapture
    {
        public readonly Item Item;
        public readonly ItemData Snapshot;
        public readonly RuntimeWorldAddress Address;
        public RuntimeAiSceneUnbindingCapture(Item item, ItemData snapshot, RuntimeWorldAddress address)
        { Item = item; Snapshot = snapshot; Address = address; }
    }

    // 场景解绑前先完整捕获世界居民，再休眠实例并允许同一世界重新恢复。
    internal bool PrepareRuntimeWorldForSceneUnbinding(string worldKey)
    {
        if (!GameNetwork.HasStateAuthority) return true;
        if (string.IsNullOrEmpty(worldKey) ||
            !string.Equals(worldKey, GameplayWorldRegistry.ExecutionWorldKey, StringComparison.Ordinal)) return false;
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        ItemMgr manager = ItemMgr.GetExistingInstance();
        if (chunks?.WorldRuntime == null)
        {
            if (!CaptureRuntimeTerrainStates()) return false;
            if (manager == null) return GameplayWorldRegistry.GetWorldItemCount(worldKey) == 0;
            foreach (Item item in manager.WorldRunTimeItems.Values)
                if (item != null && GameplayWorldRegistry.GetItemWorldKey(item) == worldKey && manager.IsRuntimeAiEntity(item))
                    return false;
            return true;
        }
        if (SaveData == null || IsEcologySnapshotInProgress || chunks.WorldRuntime.WorldId != worldKey) return false;
        var attempted = new List<RuntimeAiSceneUnbindingCapture>();
        try
        {
            if (!CaptureRuntimeTerrainStates()) return false;
            if (manager == null) return GameplayWorldRegistry.GetWorldItemCount(worldKey) == 0;
            EnsureRuntimeAiRestoreScope();
            // 修复刚接管世界时仍未进入地址桶的已登记生物。
            var runtimeItems = new List<Item>(manager.WorldRunTimeItems.Values);
            foreach (Item item in runtimeItems)
                if (item != null && GameplayWorldRegistry.GetItemWorldKey(item) == worldKey && manager.IsRuntimeAiEntity(item))
                    manager.NotifyRuntimeItemMoved(item);
            var captures = new List<RuntimeAiSceneUnbindingCapture>();
            if (!CaptureRuntimeAiEntityStates(captures)) return false;
            foreach (RuntimeAiSceneUnbindingCapture capture in captures)
            {
                Item item = capture.Item;
                if (item == null || item.itemData == null || item.itemData.Guid != capture.Snapshot.Guid ||
                    GameplayWorldRegistry.GetItemWorldKey(item) != worldKey ||
                    !ReferenceEquals(manager.GetItemByGuid(capture.Snapshot.Guid), item)) return false;
            }
            foreach (RuntimeAiSceneUnbindingCapture capture in captures)
            {
                Item item = capture.Item;
                if (item == null || item.itemData == null || item.itemData.Guid != capture.Snapshot.Guid ||
                    GameplayWorldRegistry.GetItemWorldKey(item) != worldKey ||
                    !ReferenceEquals(manager.GetItemByGuid(capture.Snapshot.Guid), item))
                {
                    RestoreSceneUnbindingAiAfterFailure(manager, attempted);
                    return false;
                }
                attempted.Add(capture);
                if (!TryParkRuntimeAiSnapshot(item, capture.Snapshot, capture.Address))
                {
                    RestoreSceneUnbindingAiAfterFailure(manager, attempted);
                    return false;
                }
                ParkedWorld.SceneDetachedLiveGuids.Add(capture.Snapshot.Guid);
            }
            restoredRuntimeAiChunks.Clear();
            return true;
        }
        catch (Exception exception)
        {
            RestoreSceneUnbindingAiAfterFailure(manager, attempted);
            Debug.LogError($"[SaveDataMgr] 世界居民解绑失败，保留场景：{worldKey}，{exception}");
            return false;
        }
    }

    // 中途失败时立即恢复已休眠居民；恢复失败的快照仍留在世界仓库供后续重试。
    private void RestoreSceneUnbindingAiAfterFailure(ItemMgr manager, List<RuntimeAiSceneUnbindingCapture> attempted)
    {
        if (attempted.Count == 0) return;
        restoredRuntimeAiChunks.Clear();
        foreach (RuntimeAiSceneUnbindingCapture capture in attempted)
        {
            int guid = capture.Snapshot.Guid;
            if (!parkedRuntimeAiAddressByGuid.ContainsKey(guid)) continue;
            Item existing = manager.GetItemByGuid(guid);
            if (existing != null)
            {
                if (GameplayWorldRegistry.GetItemWorldKey(existing) == GameplayWorldRegistry.ExecutionWorldKey &&
                    manager.IsRuntimeAiEntity(existing)) RemoveParkedRuntimeAi(guid);
                continue;
            }
            if (TryWakeParkedRuntimeAi(capture.Snapshot)) continue;
            Item restored = null;
            try
            {
                ItemData data = CloneAndRebaseItemData(capture.Snapshot);
                ItemTransform pose = data.transform;
                restored = manager.InstantiateItem(data, pose.position, pose.rotation, pose.scale);
                if (restored == null) continue;
                restored.Load();
                RemoveParkedRuntimeAi(guid);
            }
            catch (Exception exception)
            {
                if (restored != null && !restored.DestructionHandled)
                {
                    try { manager.DespawnItem(restored, saveData: false); }
                    catch (Exception cleanup) { Debug.LogException(cleanup); }
                }
                Debug.LogError($"[SaveDataMgr] 解绑回滚暂未恢复生物：{capture.Snapshot.IDName}, Guid={guid}，{exception}");
            }
        }
    }

    /// <summary>捕获活体状态并从运行时卸载；写入区块差量后才能销毁实体。</summary>
    public bool TryParkRuntimeAi(Item item)
    {
        if (IsEcologySnapshotInProgress || !GameNetwork.HasStateAuthority || item == null || item.DestructionHandled ||
            item.itemData == null || ItemMgr.Instance == null || ChunkMgr.ExistingInstance == null)
            return false;

        EnsureRuntimeAiRestoreScope();
        item.Save();
        ItemData snapshot = CloneItemData(item.itemData);
        if (snapshot?.transform == null)
            throw new InvalidOperationException($"生物 {item.name} 的休眠快照缺少位置。");
        RuntimeWorldAddress address = ChunkMgr.ExistingInstance.ResolveWorldAddress(item.transform.position);
        return TryParkRuntimeAiSnapshot(item, snapshot, address);
    }

    private bool TryParkRuntimeAiSnapshot(Item item, ItemData snapshot, RuntimeWorldAddress address)
    {
        AddParkedRuntimeAi(address, snapshot);
        try
        {
            UpsertRuntimeAiRecord(address, snapshot);
            ItemMgr.Instance.DespawnItem(item, saveData: false);
            if (ReferenceEquals(ItemMgr.Instance.GetItemByGuid(snapshot.Guid), item))
            {
                RemoveParkedRuntimeAi(snapshot.Guid);
                return false;
            }
            return true;
        }
        catch
        {
            // 注销若已发生，即使后续对象池清理抛错，也必须保留快照以免丢失居民。
            if (ReferenceEquals(ItemMgr.Instance.GetItemByGuid(snapshot.Guid), item))
            {
                RemoveParkedRuntimeAi(snapshot.Guid);
                RemoveRuntimeAiRecord(address, snapshot.Guid);
            }
            throw;
        }
    }

    /// <summary>按区块轮转休眠居民，候选读取不枚举全部动物或整张地图。</summary>
    public bool TryGetNextParkedRuntimeAi(ref int addressCursor, out ItemData snapshot)
    {
        snapshot = null;
        EnsureRuntimeAiRestoreScope();
        if (parkedRuntimeAiAddresses.Count == 0)
            return false;

        if (addressCursor >= parkedRuntimeAiAddresses.Count)
            addressCursor = 0;
        RuntimeWorldAddress address = parkedRuntimeAiAddresses[addressCursor++];
        List<ItemData> items = parkedRuntimeAiByAddress[address];
        parkedRuntimeAiItemCursors.TryGetValue(address, out int itemCursor);
        if (itemCursor >= items.Count)
            itemCursor = 0;
        snapshot = items[itemCursor];
        parkedRuntimeAiItemCursors[address] = itemCursor + 1;
        return true;
    }

    /// <summary>优先读取刚完成区块表现的居民，避免全世界休眠区块延迟本地唤醒。</summary>
    public bool TryGetNextParkedRuntimeAi(RuntimeWorldAddress address, out ItemData snapshot)
    {
        snapshot = null;
        EnsureRuntimeAiRestoreScope();
        if (!parkedRuntimeAiByAddress.TryGetValue(address, out List<ItemData> items))
            return false;
        parkedRuntimeAiItemCursors.TryGetValue(address, out int cursor);
        if (cursor >= items.Count)
            cursor = 0;
        snapshot = items[cursor];
        parkedRuntimeAiItemCursors[address] = cursor + 1;
        return true;
    }

    /// <summary>恢复休眠居民，成功后移除休眠索引和旧区块记录。</summary>
    public bool TryWakeParkedRuntimeAi(ItemData snapshot)
    {
        if (IsEcologySnapshotInProgress || snapshot == null ||
            !parkedRuntimeAiAddressByGuid.TryGetValue(snapshot.Guid, out RuntimeWorldAddress address))
            return false;

        Item restored = null;
        try
        {
            ItemData runtimeData = CloneAndRebaseItemData(snapshot);
            MonsterSpawnerManager spawner = MonsterSpawnerManager.ExistingInstance;
            if (spawner == null || !spawner.TryRestoreCreature(runtimeData, out restored)) return false;
            RemoveRuntimeAiRecord(address, snapshot.Guid);
            RemoveParkedRuntimeAi(snapshot.Guid);
            return true;
        }
        catch (Exception exception)
        {
            if (restored != null && !restored.DestructionHandled)
                ItemMgr.Instance.DespawnItem(restored, saveData: false);
            Debug.LogError($"[SaveDataMgr] 恢复休眠生物失败：{snapshot.IDName}, Guid={snapshot.Guid}");
            Debug.LogException(exception);
            return false;
        }
    }

    private void AddParkedRuntimeAi(RuntimeWorldAddress address, ItemData snapshot)
    {
        if (parkedRuntimeAiAddressByGuid.ContainsKey(snapshot.Guid))
            RemoveParkedRuntimeAi(snapshot.Guid);
        if (!parkedRuntimeAiByAddress.TryGetValue(address, out List<ItemData> items))
        {
            items = new List<ItemData>(4);
            parkedRuntimeAiByAddress.Add(address, items);
            parkedRuntimeAiAddressIndices.Add(address, parkedRuntimeAiAddresses.Count);
            parkedRuntimeAiAddresses.Add(address);
        }
        items.Add(snapshot);
        parkedRuntimeAiAddressByGuid.Add(snapshot.Guid, address);
        parkedRuntimeAiSpeciesCounts.TryGetValue(snapshot.IDName, out int speciesCount);
        parkedRuntimeAiSpeciesCounts[snapshot.IDName] = speciesCount + 1;
    }

    private void RemoveParkedRuntimeAi(int guid)
    {
        ParkedWorld.SceneDetachedLiveGuids.Remove(guid);
        if (!parkedRuntimeAiAddressByGuid.TryGetValue(guid, out RuntimeWorldAddress address))
            return;
        parkedRuntimeAiAddressByGuid.Remove(guid);
        List<ItemData> items = parkedRuntimeAiByAddress[address];
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Guid != guid)
                continue;
            string speciesId = items[i].IDName;
            int speciesCount = parkedRuntimeAiSpeciesCounts[speciesId] - 1;
            if (speciesCount == 0)
                parkedRuntimeAiSpeciesCounts.Remove(speciesId);
            else
                parkedRuntimeAiSpeciesCounts[speciesId] = speciesCount;
            items.RemoveAt(i);
            break;
        }
        if (items.Count > 0)
            return;
        parkedRuntimeAiByAddress.Remove(address);
        parkedRuntimeAiItemCursors.Remove(address);
        int addressIndex = parkedRuntimeAiAddressIndices[address];
        int lastAddressIndex = parkedRuntimeAiAddresses.Count - 1;
        RuntimeWorldAddress lastAddress = parkedRuntimeAiAddresses[lastAddressIndex];
        parkedRuntimeAiAddresses[addressIndex] = lastAddress;
        parkedRuntimeAiAddresses.RemoveAt(lastAddressIndex);
        parkedRuntimeAiAddressIndices[lastAddress] = addressIndex;
        parkedRuntimeAiAddressIndices.Remove(address);
    }

    private void ClearParkedRuntimeAi()
    {
        parkedRuntimeAiByAddress.Clear();
        parkedRuntimeAiAddressByGuid.Clear();
        parkedRuntimeAiSpeciesCounts.Clear();
        parkedRuntimeAiItemCursors.Clear();
        parkedRuntimeAiAddresses.Clear();
        parkedRuntimeAiAddressIndices.Clear();
        parkedRuntimeAiQueryAddresses.Clear();
        ParkedWorld.SceneDetachedLiveGuids.Clear();
    }

    #endregion

    #region 区块差量

    private void UpsertRuntimeAiRecord(RuntimeWorldAddress address, ItemData snapshot)
    {
        string planetName = GameplayWorldRegistry.ExecutionWorldKey;
        string chunkName = ToChunkName(address);
        string key = BuildChunkKey(planetName, chunkName);
        if (!chunkDeltas.TryGetValue(key, out ChunkSaveRecord record) || record == null)
        {
            record = new ChunkSaveRecord
            {
                PlanetName = planetName,
                ChunkName = chunkName,
                ChunkPosition = new Vector2Int(address.ChunkOrigin.X, address.ChunkOrigin.Y)
            };
        }
        record.ChangedItems ??= new List<ItemData>();
        record.ChangedItems.RemoveAll(data => data != null && data.Guid == snapshot.Guid);
        record.ChangedItems.Add(snapshot);
        chunkDeltas[key] = record;
    }

    private void RemoveRuntimeAiRecord(RuntimeWorldAddress address, int guid)
    {
        string key = BuildChunkKey(GameplayWorldRegistry.ExecutionWorldKey, ToChunkName(address));
        if (!chunkDeltas.TryGetValue(key, out ChunkSaveRecord record) || record?.ChangedItems == null)
            return;
        record.ChangedItems.RemoveAll(data => data != null && data.Guid == guid);
        if (!record.HasChanges)
            chunkDeltas.Remove(key);
    }

    #endregion
}
