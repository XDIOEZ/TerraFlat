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

    private readonly Dictionary<RuntimeWorldAddress, List<ItemData>> parkedRuntimeAiByAddress = new(); // 区块快照。
    private readonly Dictionary<int, RuntimeWorldAddress> parkedRuntimeAiAddressByGuid = new(); // GUID 反查。
    private readonly Dictionary<string, int> parkedRuntimeAiSpeciesCounts = new(StringComparer.Ordinal); // 物种居民数。
    private readonly Dictionary<RuntimeWorldAddress, int> parkedRuntimeAiItemCursors = new(); // 区块内轮转位置。
    private readonly List<RuntimeWorldAddress> parkedRuntimeAiAddresses = new(); // 全局轮转地址。
    private readonly Dictionary<RuntimeWorldAddress, int> parkedRuntimeAiAddressIndices = new(); // 常数时间移除索引。
    private readonly HashSet<RuntimeWorldAddress> parkedRuntimeAiQueryAddresses = new(); // 循环世界范围查询去重。

    public int ParkedRuntimeAiCount => parkedRuntimeAiAddressByGuid.Count;

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
        AddParkedRuntimeAi(address, snapshot);
        try
        {
            UpsertRuntimeAiRecord(address, snapshot);
            ItemMgr.Instance.DespawnItem(item, saveData: false);
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
            ItemTransform savedTransform = runtimeData.transform;
            restored = ItemMgr.Instance.InstantiateItem(
                runtimeData, savedTransform.position, savedTransform.rotation, savedTransform.scale);
            restored.Load();
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
    }

    #endregion

    #region 区块差量

    private void UpsertRuntimeAiRecord(RuntimeWorldAddress address, ItemData snapshot)
    {
        string planetName = SceneManager.GetActiveScene().name;
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
        string key = BuildChunkKey(SceneManager.GetActiveScene().name, ToChunkName(address));
        if (!chunkDeltas.TryGetValue(key, out ChunkSaveRecord record) || record?.ChangedItems == null)
            return;
        record.ChangedItems.RemoveAll(data => data != null && data.Guid == guid);
        if (!record.HasChanges)
            chunkDeltas.Remove(key);
    }

    #endregion
}
