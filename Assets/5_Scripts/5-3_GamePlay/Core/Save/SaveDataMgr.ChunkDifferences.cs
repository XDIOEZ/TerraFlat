using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>区块差量仓库、旧地图基线与区块快照数据。</summary>
public partial class SaveDataMgr
{
    #region 区块差量索引
    private readonly Dictionary<string, ChunkBaseline> chunkBaselines = new();
    private readonly Dictionary<string, ChunkSaveRecord> chunkDeltas = new();
    private readonly Dictionary<string, long> chunkDifferenceRevisions = new();
    private long nextChunkDifferenceRevision;

    private void MarkChunkDifferenceChanged(string key) => chunkDifferenceRevisions[key] = ++nextChunkDifferenceRevision;

    private static string BuildChunkKey(string planetName, string chunkName)
    {
        return $"{planetName}\u001f{chunkName}";
    }

    private static string ToChunkName(RuntimeWorldAddress address)
    {
        return new Vector2Int(address.ChunkOrigin.X, address.ChunkOrigin.Y).ToString();
    }
    #endregion

    #region 旧地图物品采集
    /// <summary>
    /// 通过区块父对象获取地图保存数据
    /// </summary>
    /// <param name="MapParent">区块对象</param>
    /// <returns>地图保存数据</returns>
    public MapSave GetMapSave_By_Parent(Chunk MapParent)
    {
        if (MapParent == null)
        {
            Debug.LogError("❌ MapParent为null");
            return null;
        }

        return new MapSave
        {
            Name = MapParent.name,
            MapPosition = new Vector2Int((int)MapParent.transform.position.x, (int)MapParent.transform.position.y),
            items = GetActiveSceneAllItemData(MapParent)
        };
    }

    /// <summary>
    /// 获取指定区块中所有物品的数据
    /// </summary>
    /// <param name="MapParent">区块对象</param>
    /// <returns>物品数据字典</returns>
    public Dictionary<string, HashSet<ItemData>> GetActiveSceneAllItemData(Chunk MapParent)
    {
        if (MapParent?.RunTimeItems == null)
        {
            Debug.LogWarning("⚠️ MapParent或RunTimeItems为null");
            return new Dictionary<string, HashSet<ItemData>>();
        }

        return CollectItemDataByType(MapParent.RunTimeItems.Values);
    }

    /// <summary>
    /// 获取当前逻辑世界的地图保存数据（静态版本）
    /// </summary>
    /// <returns>地图保存数据</returns>
    public static MapSave GetCurrentMapStatic()
    {
        return new MapSave
        {
            Name = GameplayWorldRegistry.ExecutionWorldKey,
            items = GetActiveSceneAllItemData_Static()
        };
    }

    /// <summary>
    /// 获取当前逻辑世界中所有活动物品的数据（静态版本）
    /// </summary>
    /// <returns>物品数据字典</returns>
    public static Dictionary<string, HashSet<ItemData>> GetActiveSceneAllItemData_Static()
    {
        var itemDataDict = new Dictionary<string, HashSet<ItemData>>();
        string worldKey = GameplayWorldRegistry.ExecutionWorldKey;
        if (string.IsNullOrEmpty(worldKey)) return itemDataDict;
        foreach (Item item in FindObjectsOfType<Item>(includeInactive: false))
        {
            if (item == null || GameplayWorldRegistry.GetItemWorldKey(item) != worldKey) continue;

            try
            {
                item.ModuleSave();
            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ 保存物品失败: {item.name}", item);
                Debug.LogException(ex);
            }
            if (!item.gameObject.activeInHierarchy)
                continue;

            ItemData itemData = item.itemData;
            if (itemData == null)
                continue;

            if (!itemDataDict.TryGetValue(itemData.IDName, out HashSet<ItemData> set))
            {
                set = new HashSet<ItemData>();
                itemDataDict[itemData.IDName] = set;
            }

            set.Add(itemData);
        }

        return itemDataDict;
    }

    /// <summary>
    /// 通用的物品数据收集方法
    /// </summary>
    private Dictionary<string, HashSet<ItemData>> CollectItemDataByType(IEnumerable<Item> items)
    {
        Dictionary<string, HashSet<ItemData>> itemDataDict = new Dictionary<string, HashSet<ItemData>>();

        // 先保存所有物品
        foreach (Item item in items)
        {
            if (item == null) continue;

            try
            {
                item.ModuleSave();
            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ 保存物品失败: {item.name}", item);
                Debug.LogException(ex);
            }
        }

        // 再收集物品数据
        foreach (Item item in items)
        {
            if (item == null) continue;

            ItemData itemData = item.itemData;
            if (itemData == null) continue;

            if (!itemDataDict.TryGetValue(itemData.IDName, out HashSet<ItemData> set))
            {
                set = new HashSet<ItemData>();
                itemDataDict[itemData.IDName] = set;
            }

            set.Add(itemData);
        }

        return itemDataDict;
    }
    #endregion

    #region 区块基线与差量
    /// <summary>
    /// Called after deterministic terrain/resource generation and before saved changes are applied.
    /// </summary>
    public bool TryFinalizeProceduralChunk(Chunk chunk, out string failureReason)
    {
        failureReason = null;
        if (chunk?.MapSave == null || chunk.Map?.Data == null)
        {
            failureReason = "区块、MapSave 或地图数据为空。";
            return false;
        }

        string planetName = GameplayWorldRegistry.ExecutionWorldKey;
        if (string.IsNullOrEmpty(planetName))
        {
            failureReason = "区块缺少逻辑世界身份。";
            return false;
        }
        try
        {
            string key = BuildChunkKey(planetName, chunk.MapSave.Name);
            chunkBaselines[key] = CaptureChunkBaseline(chunk);

            if (chunkDeltas.TryGetValue(key, out ChunkSaveRecord delta))
                ApplyChunkDelta(chunk, delta);
            return true;
        }
        catch (Exception exception)
        {
            chunkBaselines.Remove(BuildChunkKey(planetName, chunk.MapSave.Name));
            failureReason = exception.Message;
            Debug.LogError($"[SaveDataMgr] 程序化区块最终化失败：{chunk.name}\n{exception}", chunk);
            return false;
        }
    }

    public void DiscardProceduralChunkBaseline(Chunk chunk)
    {
        if (chunk?.MapSave == null)
            return;

        string planetName = GameplayWorldRegistry.ExecutionWorldKey;
        if (string.IsNullOrEmpty(planetName)) return;
        chunkBaselines.Remove(BuildChunkKey(planetName, chunk.MapSave.Name));
    }

    /// <summary>
    /// 基于确定性基线构建当前格式的区块差量。
    /// </summary>
    public bool TrySaveChunkDifferences(Chunk chunk)
    {
        if (chunk?.MapSave == null || chunk.Map?.Data == null)
            return false;

        string planetName = GameplayWorldRegistry.ExecutionWorldKey;
        if (string.IsNullOrEmpty(planetName)) return false;
        string key = BuildChunkKey(planetName, chunk.MapSave.Name);
        MarkChunkDifferenceChanged(key);
        if (!chunkBaselines.TryGetValue(key, out ChunkBaseline baseline))
            return false;

        ChunkSaveRecord delta = new ChunkSaveRecord
        {
            PlanetName = planetName,
            ChunkName = chunk.MapSave.Name,
            ChunkPosition = chunk.MapSave.MapPosition,
            SunlightIntensity = chunk.MapSave.SunlightIntensity
        };

        HashSet<int> currentGuids = new HashSet<int>();
        foreach (Item item in chunk.RunTimeItems.Values)
        {
            if (item == null || item is Map || item.itemData == null ||
                RuntimeAiEntityUtility.IsAiEntity(item))
                continue;

            try
            {
                item.Save();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveDataMgr] 保存区块物品失败: {item.name}", item);
                Debug.LogException(ex);
            }

            ItemData itemData = item.itemData;
            currentGuids.Add(itemData.Guid);
            ulong currentHash = ComputeItemHash(itemData);
            if (!baseline.ItemHashes.TryGetValue(itemData.Guid, out ulong baselineHash) || currentHash != baselineHash)
            {
                delta.ChangedItems.Add(CloneItemData(itemData));
            }
        }

        foreach (int generatedGuid in baseline.ItemHashes.Keys)
        {
            if (!currentGuids.Contains(generatedGuid))
                delta.RemovedItemGuids.Add(generatedGuid);
        }

        delta.ChangedItems.Sort((a, b) => (a?.Guid ?? 0).CompareTo(b?.Guid ?? 0));
        delta.RemovedItemGuids.Sort();
        CollectTileDifferences(chunk.Map.Data, baseline, delta.TileDeltas);
        CollectGrassDifferences(chunk.Map.Data, baseline, delta.GrassDeltas);

        // 当前格式只保存差量，避免把生成基线重新塞回 MapSave。
        chunk.MapSave.items ??= new Dictionary<string, HashSet<ItemData>>();
        chunk.MapSave.items.Clear();

        if (delta.HasChanges)
            chunkDeltas[key] = delta;
        else
            chunkDeltas.Remove(key);

        return true;
    }

    /// <summary>
    /// 自动保存专用的分帧差异扫描；物品、删除 GUID、Tile 和草地逐段处理，完成后一次性提交差量。
    /// </summary>
    public IEnumerator TrySaveChunkDifferencesCoroutine(
        Chunk chunk,
        Action<bool, Exception> onCompleted,
        AutoSaveWorkBudget budget = null)
    {
        budget ??= new AutoSaveWorkBudget();
        if (chunk?.MapSave == null || chunk.Map?.Data == null)
        {
            onCompleted?.Invoke(false, null);
            yield break;
        }

        string planetName = GameplayWorldRegistry.ExecutionWorldKey;
        if (string.IsNullOrEmpty(planetName))
        {
            onCompleted?.Invoke(false, null);
            yield break;
        }
        string key = BuildChunkKey(planetName, chunk.MapSave.Name);
        MarkChunkDifferenceChanged(key);
        if (!chunkBaselines.TryGetValue(key, out ChunkBaseline baseline))
        {
            onCompleted?.Invoke(false, null);
            yield break;
        }

        ChunkSaveRecord delta = new ChunkSaveRecord
        {
            PlanetName = planetName,
            ChunkName = chunk.MapSave.Name,
            ChunkPosition = chunk.MapSave.MapPosition,
            SunlightIntensity = chunk.MapSave.SunlightIntensity
        };

        HashSet<int> currentGuids = new HashSet<int>();
        List<Item> items = new List<Item>(chunk.RunTimeItems.Values);
        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            if (item == null || item is Map || item.itemData == null ||
                RuntimeAiEntityUtility.IsAiEntity(item))
            {
                continue;
            }

            ItemData itemData = null;
            Exception itemFailure = null;
            ulong currentHash = 0UL;
            ItemData changedItem = null;
            try
            {
                item.Save();
                itemData = item.itemData;
                if (itemData == null)
                    throw new InvalidDataException($"区块物品 {item.name} 的 ItemData 为空");
                currentHash = ComputeItemHash(itemData);
                if (!baseline.ItemHashes.TryGetValue(itemData.Guid, out ulong baselineHash) ||
                    currentHash != baselineHash)
                {
                    changedItem = CloneItemData(itemData);
                }
            }
            catch (Exception exception)
            {
                itemFailure = exception;
                Debug.LogError($"[SaveDataMgr] 分帧保存区块物品失败: {item.name}", item);
                Debug.LogException(exception);
            }

            if (itemFailure != null)
            {
                onCompleted?.Invoke(false, itemFailure);
                yield break;
            }

            currentGuids.Add(itemData.Guid);
            if (changedItem != null)
                delta.ChangedItems.Add(changedItem);

            if (budget.ShouldYield())
                yield return budget.WaitForNextSlice();
        }

        List<int> baselineGuids = new List<int>(baseline.ItemHashes.Keys);
        for (int i = 0; i < baselineGuids.Count; i++)
        {
            int generatedGuid = baselineGuids[i];
            if (!currentGuids.Contains(generatedGuid))
                delta.RemovedItemGuids.Add(generatedGuid);

            if (budget.ShouldYield())
                yield return budget.WaitForNextSlice();
        }

        Data_TileMap mapData = chunk.Map.Data;
        var tileBuffer = new List<TileData>(4);
        int width = mapData.Width;
        int height = mapData.Height;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                mapData.CopyStackLocalTo(x, y, tileBuffer);
                ulong currentHash = ComputeTileHash(tileBuffer);
                bool hasBaselineCell = x < baseline.TileWidth && y < baseline.TileHeight;
                if (!hasBaselineCell || currentHash != baseline.TileHashes[x, y])
                {
                    delta.TileDeltas.Add(new TileCellSaveDelta
                    {
                        LocalPosition = new Vector2Int(x, y),
                        Tiles = CloneTileList(tileBuffer)
                    });
                }

                if (budget.ShouldYield())
                    yield return budget.WaitForNextSlice();
            }
        }

        mapData.EnsureGrassLayerStorage(mapData.Width, mapData.Height);
        GrassLayerData grassLayer = mapData.GrassLayer;
        for (int x = 0; x < grassLayer.Width; x++)
        {
            for (int y = 0; y < grassLayer.Height; y++)
            {
                GrassCellState currentState = grassLayer.Get(x, y);
                bool hasBaselineCell = x < baseline.GrassWidth && y < baseline.GrassHeight;
                int baselineIndex = y * baseline.GrassWidth + x;
                GrassCellState baselineState = hasBaselineCell &&
                    baseline.GrassStates != null &&
                    (uint)baselineIndex < (uint)baseline.GrassStates.Length
                        ? (GrassCellState)baseline.GrassStates[baselineIndex]
                        : GrassCellState.Uninitialized;

                if (currentState != baselineState)
                {
                    delta.GrassDeltas.Add(new GrassCellSaveDelta
                    {
                        LocalPosition = new Vector2Int(x, y),
                        State = currentState
                    });
                }

                if (budget.ShouldYield())
                    yield return budget.WaitForNextSlice();
            }
        }

        delta.ChangedItems.Sort((a, b) => (a?.Guid ?? 0).CompareTo(b?.Guid ?? 0));
        delta.RemovedItemGuids.Sort();

        // 只在完整差异扫描完成后提交，跨帧期间不会留下半成品差量。
        chunk.MapSave.items ??= new Dictionary<string, HashSet<ItemData>>();
        chunk.MapSave.items.Clear();
        if (delta.HasChanges)
            chunkDeltas[key] = delta;
        else
            chunkDeltas.Remove(key);

        onCompleted?.Invoke(true, null);
    }

    public bool TryGetChunkDelta(string planetName, string chunkName, out ChunkSaveRecord delta)
    {
        return chunkDeltas.TryGetValue(BuildChunkKey(planetName, chunkName), out delta);
    }

    #region 冷地表差量热查询
    private readonly Dictionary<(string Planet, Vector2Int Origin), string> chunkDeltaLookupKeys = new();

    // 只复用查询键，差量仍每次读取当前档案，扩展探索时有界释放旧键。
    public bool TryGetChunkDelta(string planetName, Vector2Int chunkOrigin, out ChunkSaveRecord delta)
        => chunkDeltas.TryGetValue(GetCachedChunkDifferenceKey(planetName, chunkOrigin), out delta);

    private string GetCachedChunkDifferenceKey(string planetName, Vector2Int chunkOrigin)
    {
        var lookup = (planetName, chunkOrigin);
        if (!chunkDeltaLookupKeys.TryGetValue(lookup, out string key))
        {
            if (chunkDeltaLookupKeys.Count >= 256) chunkDeltaLookupKeys.Clear();
            chunkDeltaLookupKeys.Add(lookup, key = BuildChunkKey(planetName, chunkOrigin.ToString()));
        }
        return key;
    }
    #endregion

    private ChunkBaseline CaptureChunkBaseline(Chunk chunk)
    {
        ChunkBaseline baseline = new ChunkBaseline();

        foreach (Item item in chunk.RunTimeItems.Values)
        {
            if (item == null || item is Map || item.itemData == null ||
                RuntimeAiEntityUtility.IsAiEntity(item))
                continue;

            try
            {
                item.Save();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveDataMgr] 捕获区块基线失败: {item.name}", item);
                Debug.LogException(ex);
                throw;
            }

            baseline.ItemHashes[item.itemData.Guid] = ComputeItemHash(item.itemData);
        }

        Data_TileMap mapData = chunk.Map.Data;
        baseline.TileWidth = mapData.Width;
        baseline.TileHeight = mapData.Height;
        baseline.TileHashes = new ulong[baseline.TileWidth, baseline.TileHeight];
        var tileBuffer = new List<TileData>(4);
        for (int x = 0; x < baseline.TileWidth; x++)
        {
            for (int y = 0; y < baseline.TileHeight; y++)
            {
                mapData.CopyStackLocalTo(x, y, tileBuffer);
                baseline.TileHashes[x, y] = ComputeTileHash(tileBuffer);
            }
        }

        mapData.EnsureGrassLayerStorage(mapData.Width, mapData.Height);
        baseline.GrassWidth = mapData.GrassLayer.Width;
        baseline.GrassHeight = mapData.GrassLayer.Height;
        baseline.GrassStates = mapData.GrassLayer.CopyCells();

        return baseline;
    }

    private static void CollectTileDifferences(
        Data_TileMap mapData,
        ChunkBaseline baseline,
        List<TileCellSaveDelta> output)
    {
        int width = mapData.Width;
        int height = mapData.Height;
        var tileBuffer = new List<TileData>(4);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                mapData.CopyStackLocalTo(x, y, tileBuffer);
                ulong currentHash = ComputeTileHash(tileBuffer);
                bool hasBaselineCell = x < baseline.TileWidth && y < baseline.TileHeight;
                if (hasBaselineCell && currentHash == baseline.TileHashes[x, y])
                    continue;

                output.Add(new TileCellSaveDelta
                {
                    LocalPosition = new Vector2Int(x, y),
                    Tiles = CloneTileList(tileBuffer)
                });
            }
        }
    }

    private static void CollectGrassDifferences(
        Data_TileMap mapData,
        ChunkBaseline baseline,
        List<GrassCellSaveDelta> output)
    {
        mapData.EnsureGrassLayerStorage(mapData.Width, mapData.Height);
        GrassLayerData grassLayer = mapData.GrassLayer;

        for (int x = 0; x < grassLayer.Width; x++)
        {
            for (int y = 0; y < grassLayer.Height; y++)
            {
                GrassCellState currentState = grassLayer.Get(x, y);
                bool hasBaselineCell = x < baseline.GrassWidth && y < baseline.GrassHeight;
                int baselineIndex = y * baseline.GrassWidth + x;
                GrassCellState baselineState = hasBaselineCell &&
                    baseline.GrassStates != null &&
                    (uint)baselineIndex < (uint)baseline.GrassStates.Length
                        ? (GrassCellState)baseline.GrassStates[baselineIndex]
                        : GrassCellState.Uninitialized;

                if (currentState == baselineState)
                    continue;

                output.Add(new GrassCellSaveDelta
                {
                    LocalPosition = new Vector2Int(x, y),
                    State = currentState
                });
            }
        }
    }

    private void ApplyChunkDelta(Chunk chunk, ChunkSaveRecord delta)
    {
        Data_TileMap mapData = chunk.Map?.Data;
        if (mapData == null)
            return;

        for (int i = 0; i < (delta.TileDeltas?.Count ?? 0); i++)
        {
            TileCellSaveDelta cell = delta.TileDeltas[i];
            int x = cell.LocalPosition.x;
            int y = cell.LocalPosition.y;
            if ((uint)x >= (uint)mapData.Width || (uint)y >= (uint)mapData.Height)
                continue;

            mapData.ReplaceStackLocal(x, y, CloneTileList(cell.Tiles));
            Vector2Int worldPosition = mapData.position + cell.LocalPosition;
            chunk.Map.UpdateTileBaseAtPosition(worldPosition);
            chunk.Map.MarkPenaltyDirty(worldPosition);
        }

        mapData.EnsureGrassLayerStorage(mapData.Width, mapData.Height);
        for (int i = 0; i < (delta.GrassDeltas?.Count ?? 0); i++)
        {
            GrassCellSaveDelta cell = delta.GrassDeltas[i];
            int x = cell.LocalPosition.x;
            int y = cell.LocalPosition.y;
            if (!mapData.GrassLayer.Set(x, y, cell.State))
                continue;

            Vector2Int worldPosition = mapData.position + cell.LocalPosition;
            chunk.Map.GetComponent<GrassDetailLayer>()?.RefreshCell(chunk.Map, worldPosition);
        }

        for (int i = 0; i < (delta.RemovedItemGuids?.Count ?? 0); i++)
        {
            RemoveRuntimeItem(chunk, delta.RemovedItemGuids[i]);
        }

        for (int i = 0; i < (delta.ChangedItems?.Count ?? 0); i++)
        {
            ItemData savedData = delta.ChangedItems[i];
            if (savedData == null || string.IsNullOrEmpty(savedData.IDName))
                continue;
            if (RuntimeAiEntityUtility.IsAiData(savedData))
                continue;

            RemoveRuntimeItem(chunk, savedData.Guid);

            try
            {
                ItemData runtimeData = CloneAndRebaseItemData(savedData);
                Item item = chunk.InstantiateItemInChunk(
                    runtimeData,
                    runtimeData.transform.position,
                    runtimeData.transform.rotation,
                    runtimeData.transform.scale);
                item?.Load();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveDataMgr] 应用区块物品差异失败: {savedData.IDName}, Guid={savedData.Guid}", chunk);
                Debug.LogException(ex);
            }
        }
    }

    private static void RemoveRuntimeItem(Chunk chunk, int guid)
    {
        if (!chunk.RunTimeItems.TryGetValue(guid, out Item item) || item == null)
        {
            chunk.RunTimeItems.Remove(guid);
            return;
        }

        chunk.RemoveItem(item);
        if (ItemMgr.Instance != null)
            ItemMgr.Instance.DespawnItem(item, saveData: false);
        else
            Destroy(item.gameObject);
    }

    private static ulong ComputeItemHash(ItemData itemData)
    {
        return ComputeHash(MemoryPackSerializer.Serialize<ItemData>(itemData));
    }

    private static ulong ComputeTileHash(List<TileData> tiles)
    {
        return ComputeHash(MemoryPackSerializer.Serialize(tiles ?? new List<TileData>()));
    }

    private static ulong ComputeHash(byte[] bytes)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= prime;
        }
        return hash;
    }

    private static List<TileData> CloneTileList(List<TileData> tiles)
    {
        if (tiles == null || tiles.Count == 0)
            return new List<TileData>();

        byte[] bytes = MemoryPackSerializer.Serialize(tiles);
        return MemoryPackSerializer.Deserialize<List<TileData>>(bytes) ?? new List<TileData>();
    }

    private sealed class ChunkBaseline
    {
        public readonly Dictionary<int, ulong> ItemHashes = new();
        public int TileWidth;
        public int TileHeight;
        public ulong[,] TileHashes = new ulong[0, 0];
        public int GrassWidth;
        public int GrassHeight;
        public byte[] GrassStates = Array.Empty<byte>();
    }
    #endregion
}

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class ChunkSaveRecord
{
    [MemoryPack.MemoryPackOrder(8)] public string PlanetName;
    [MemoryPack.MemoryPackOrder(2)] public string ChunkName;
    [MemoryPack.MemoryPackIgnore] public Vector2Int ChunkPosition;
    [MemoryPack.MemoryPackInclude, MemoryPack.MemoryPackOrder(3)]
    private ModuleGridPositionSaveSnapshot SavedChunkPosition
    {
        get => new() { X = ChunkPosition.x, Y = ChunkPosition.y };
        set { if (value != null) ChunkPosition = new(value.X, value.Y); }
    }
    [MemoryPack.MemoryPackOrder(12)] public float SunlightIntensity;
    [MemoryPackIgnore] public List<ItemData> ChangedItems = new();
    [MemoryPackInclude, MemoryPackOrder(1)]
    private List<ItemInstanceSnapshot> SavedChangedItems
    {
        get => ChangedItems?.ConvertAll(item => item == null ? null : ItemSnapshotSerialization.Capture(item));
        set => ChangedItems = value?.ConvertAll(item => item?.CreateColdData()) ?? new();
    }
    [MemoryPack.MemoryPackOrder(9)] public List<int> RemovedItemGuids = new();
    [MemoryPack.MemoryPackOrder(14)] public List<TileCellSaveDelta> TileDeltas = new();
    [MemoryPack.MemoryPackOrder(5)] public List<GrassCellSaveDelta> GrassDeltas = new();
    // 当前 WorldModel 的完整核心地形差量，包含地表铺设和阻挡墙。
    [MemoryPack.MemoryPackOrder(10)] public List<RuntimeTileCellSaveDelta> RuntimeTileDeltas = new();
    [MemoryPack.MemoryPackOrder(13)] public List<SupportCellSaveData> SupportCells = new(); // 独立支撑面
    [MemoryPack.MemoryPackOrder(0)] public List<AgricultureCellSaveData> AgricultureCells = new(); // 独立农业状态
    [MemoryPack.MemoryPackOrder(4)] public List<ContaminationCellSaveData> ContaminationCells = new(); // 独立污染状态
    [MemoryPack.MemoryPackOrder(7)] public List<LiquidCellSaveData> LiquidCells = new(); // 只保存玩家改变的液体格，与 Ground 差量独立。
    [MemoryPack.MemoryPackOrder(6)] public List<GroundLayerCellSaveData> GroundLayerCells = new(); // 追加伪 Z 轴差量，旧记录缺失时按未采挖处理。
    [MemoryPack.MemoryPackOrder(11)] public List<SnowCellSaveData> SnowCells = new(); // 独立保存天气雪与玩家编辑雪，不替换底层地形。
    [MemoryPack.MemoryPackOrder(15)] public List<FireCellSaveData> FireCells = new(); // 活火与表面燃烧进度的稀疏差量。

    [MemoryPackIgnore]
    public bool HasChanges =>
        ((ChangedItems?.Count ?? 0) > 0 ||
         (RemovedItemGuids?.Count ?? 0) > 0 ||
         (TileDeltas?.Count ?? 0) > 0 ||
         (GrassDeltas?.Count ?? 0) > 0 ||
         (RuntimeTileDeltas?.Count ?? 0) > 0 ||
         (AgricultureCells?.Count ?? 0) > 0 ||
         (SupportCells?.Count ?? 0) > 0 ||
         (ContaminationCells?.Count ?? 0) > 0 ||
         (LiquidCells?.Count ?? 0) > 0 ||
         (GroundLayerCells?.Count ?? 0) > 0 ||
         (SnowCells?.Count ?? 0) > 0 ||
         (FireCells?.Count ?? 0) > 0);
}

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class TileCellSaveDelta
{
    [MemoryPack.MemoryPackIgnore] public Vector2Int LocalPosition;
    [MemoryPack.MemoryPackInclude, MemoryPack.MemoryPackOrder(0)]
    private ModuleGridPositionSaveSnapshot SavedLocalPosition
    {
        get => new() { X = LocalPosition.x, Y = LocalPosition.y };
        set { if (value != null) LocalPosition = new(value.X, value.Y); }
    }
    [MemoryPackIgnore] public List<TileData> Tiles = new();
    [MemoryPackInclude, MemoryPackOrder(1)]
    private List<TileSaveSnapshot> SavedTiles
    {
        get => Tiles?.ConvertAll(TileSaveSnapshot.Capture);
        set => Tiles = value?.ConvertAll(tile => tile?.Restore()) ?? new();
    }
}

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class GrassCellSaveDelta
{
    [MemoryPack.MemoryPackIgnore] public Vector2Int LocalPosition;
    [MemoryPack.MemoryPackInclude, MemoryPack.MemoryPackOrder(0)]
    private ModuleGridPositionSaveSnapshot SavedLocalPosition
    {
        get => new() { X = LocalPosition.x, Y = LocalPosition.y };
        set { if (value != null) LocalPosition = new(value.X, value.Y); }
    }
    [MemoryPack.MemoryPackOrder(1)] public GrassCellState State;
}

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class RuntimeTileCellSaveDelta
{
    [MemoryPack.MemoryPackIgnore] public Vector2Int LocalPosition;
    [MemoryPack.MemoryPackInclude, MemoryPack.MemoryPackOrder(6)]
    private ModuleGridPositionSaveSnapshot SavedLocalPosition
    {
        get => new() { X = LocalPosition.x, Y = LocalPosition.y };
        set { if (value != null) LocalPosition = new(value.X, value.Y); }
    }
    [MemoryPack.MemoryPackOrder(5)] public int GroundTileId;
    [MemoryPack.MemoryPackOrder(1)] public int BackTileId;
    [MemoryPack.MemoryPackOrder(3)] public int BlockingTileId;
    [MemoryPack.MemoryPackOrder(2)] public int BiomeId;
    [MemoryPack.MemoryPackOrder(7)] public short NavigationCost;
    [MemoryPack.MemoryPackOrder(4)] public TerrainCellFlags Flags;
    // 0 表示该格建筑满耐久或当前没有阻挡建筑。
    [MemoryPack.MemoryPackOrder(0)] public float AccumulatedDamage;

    /// <summary>捕获核心地形，避免不同修改入口只保存自己关心的墙或水标记。</summary>
    public static RuntimeTileCellSaveDelta Capture(Vector2Int localPosition,
        TerrainCell cell, float accumulatedDamage)
    {
        return new RuntimeTileCellSaveDelta
        {
            LocalPosition = localPosition,
            GroundTileId = cell.GroundTileId,
            BackTileId = cell.BackTileId,
            BlockingTileId = cell.BlockingTileId,
            BiomeId = cell.BiomeId,
            NavigationCost = cell.NavigationCost,
            Flags = cell.Flags,
            AccumulatedDamage = Mathf.Max(0f, accumulatedDamage)
        };
    }

    /// <summary>按当前版本原样还原地形；不推测、迁移或补齐旧格式字段。</summary>
    public TerrainCell ToTerrainCell() => new TerrainCell(
        GroundTileId, BackTileId, BlockingTileId, BiomeId, NavigationCost, Flags);
}
