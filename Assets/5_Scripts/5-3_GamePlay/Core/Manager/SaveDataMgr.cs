using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.SceneManagement;
using MemoryPack;
using Sirenix.OdinInspector;

/// <summary>
/// 游戏存档与加载系统，负责管理游戏数据的保存和加载功能
/// </summary>
public class SaveDataMgr : SingletonAutoMono<SaveDataMgr>
{
    private const int CompactSaveVersion = 4;
    private const int ModdedSaveVersion = 3;
    private const string TemporarySaveSuffix = ".tmp";
    private const string BackupSaveSuffix = ".bak";
    private const string LastExitTimeSuffix = ".lastplayed";
    private static readonly byte[] CompactSaveMagic = { (byte)'F', (byte)'W', (byte)'D', (byte)'2' };
    private static readonly byte[] ModdedSaveMagic = { (byte)'F', (byte)'W', (byte)'D', (byte)'3' };
    private static readonly object SaveFileLock = new object();

    private readonly Dictionary<string, ChunkBaseline> chunkBaselines = new();
    private readonly Dictionary<string, ChunkSaveRecord> chunkDeltas = new();

    #region 存档配置
    [Tooltip("玩家的存档路径")]
    public string UserSavePath = ""; // 初始化为空，将在Awake中设置

    [Tooltip("当前使用的存档数据")]
    public GameSaveData SaveData;

    [Tooltip("当前控制的玩家名称")]
    public string CurrentContrrolPlayerName;
    
    /// <summary>
    /// 获取当前活跃星球数据（快捷属性）
    /// </summary>
    public PlanetData Active_PlanetData
    {
        get => GetActivePlanetData();
    }

    /// <summary>
    /// 获取当前活跃星球数据的内部方法
    /// </summary>
    private PlanetData GetActivePlanetData()
    {
        if (TryGetActivePlanetData(out PlanetData planetData))
            return planetData;

        if (SaveData?.PlanetData_Dict == null)
        {
            Debug.LogWarning("⚠️ SaveData或PlanetData_Dict为null");
            return null;
        }

        string activeSceneName = SceneManager.GetActiveScene().name;
        Debug.LogWarning($"⚠️ 未找到场景 {activeSceneName} 的星球数据");
        return null;
    }

    /// <summary>
    /// Quiet topology lookup for hot paths such as movement, navigation and
    /// distance checks. Missing active-world data is a normal infinite-world
    /// fallback here and must not emit one warning per queried cell.
    /// </summary>
    public bool TryGetActivePlanetData(out PlanetData planetData)
    {
        planetData = null;
        if (SaveData?.PlanetData_Dict == null)
            return false;

        string activeSceneName = SceneManager.GetActiveScene().name;
        return SaveData.PlanetData_Dict.TryGetValue(activeSceneName, out planetData) &&
               planetData != null;
    }

    protected override void Awake()
    {
        base.Awake();
        InitializeUserSavePath();
    }

    /// <summary>
    /// 初始化用户存档路径
    /// </summary>
    private void InitializeUserSavePath()
    {
        UserSavePath = GetDefaultSavePath();
        
        if (!Directory.Exists(UserSavePath))
        {
            Directory.CreateDirectory(UserSavePath);
        }
    }

    #endregion

    #region 保存功能

    /// <summary>
    /// 为联机客户端创建完整世界快照。快照包含存档种子、星球参数和地图区块数据，
    /// 并使用 GZip 压缩以降低首次加入时的传输体积。
    /// </summary>
    public byte[] CreateCompressedNetworkSnapshot()
    {
        if (SaveData == null)
            throw new InvalidOperationException("SaveData为null，无法创建联机世界快照");

        PrepareLoadedChunksForSave();
        byte[] rawData = BuildCompactSavePayload(SaveData);
        using MemoryStream output = new MemoryStream();
        using (GZipStream gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
        {
            gzip.Write(rawData, 0, rawData.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    /// 应用主机发送的完整世界快照。客户端只替换内存数据，不写入本地磁盘。
    /// </summary>
    public void ApplyCompressedNetworkSnapshot(byte[] compressedData)
    {
        if (compressedData == null || compressedData.Length == 0)
            throw new ArgumentException("联机世界快照为空", nameof(compressedData));

        if (compressedData.Length < 2 || compressedData[0] != 0x1F || compressedData[1] != 0x8B)
            throw new InvalidDataException("联机世界快照缺少 GZip 文件头，请确认两端脚本版本一致");

        using MemoryStream input = new MemoryStream(compressedData, false);
        using GZipStream gzip = new GZipStream(input, CompressionMode.Decompress);
        using MemoryStream output = new MemoryStream();
        gzip.CopyTo(output);

        GameSaveData snapshot = DeserializeSavePayload(output.ToArray());
        if (snapshot == null)
            throw new InvalidDataException("联机世界快照反序列化失败");

        SaveData = snapshot;
    }

    /// <summary>
    /// 保存当前游戏状态到磁盘
    /// </summary>
    [Button("保存存档到磁盘上")]
    public void Save_And_WriteToDisk()
    {
        SaveCurrentToDisk(recordExitTime: false);
    }

    /// <summary>
    /// 保存当前游戏状态，并记录玩家退出该存档的现实时间。
    /// </summary>
    public void Save_And_WriteToDiskAndRecordExitTime()
    {
        SaveCurrentToDisk(recordExitTime: true);
    }

    private void SaveCurrentToDisk(bool recordExitTime)
    {
        if (SaveData == null)
        {
            Debug.LogError("❌ SaveData为null，无法保存");
            return;
        }

        PrepareLoadedChunksForSave();
        if (!SaveToDisk(SaveData, UserSavePath, SaveData.saveName))
            return;

        if (recordExitTime)
        {
            string fullPath = GetSaveFilePath(UserSavePath, SaveData.saveName);
            RecordLastExitTimeUtc(fullPath, DateTime.UtcNow);
        }
    }

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
    #endregion
    
    #region 磁盘操作
    /// <summary>
    /// 将游戏存档数据保存到磁盘
    /// </summary>
    /// <param name="saveData">存档数据</param>
    /// <param name="savePath">保存路径</param>
    /// <param name="saveName">保存名称</param>
    public bool SaveToDisk(GameSaveData saveData, string savePath, string saveName)
    {
        if (saveData == null)
        {
            Debug.LogError("❌ saveData为null，无法保存");
            return false;
        }

        if (string.IsNullOrWhiteSpace(saveName))
        {
            Debug.LogError("❌ 存档名称为空，无法保存");
            return false;
        }

        try
        {
            saveData.saveName = saveName;

            // 确保保存路径存在
            EnsureDirectoryExists(savePath);

            string fullPath = GetSaveFilePath(savePath, saveName);
            byte[] dataBytes = BuildCompactSavePayload(saveData);
            WriteSaveAtomically(fullPath, dataBytes);

            Debug.Log($"✅ 存档成功！路径: {fullPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ 保存存档失败: {ex.Message}");
            Debug.LogException(ex);
            return false;
        }
    }

    /// <summary>
    /// 为新世界分配一个安全且未被占用的存档名，并立即创建首个存档文件。
    /// 新建世界不会覆盖任何已有世界；重名时自动追加编号。
    /// </summary>
    public bool TryCreateNewSave(GameSaveData saveData, string requestedName, out string createdSaveName)
    {
        createdSaveName = GetAvailableSaveName(requestedName);
        return SaveToDisk(saveData, UserSavePath, createdSaveName);
    }

    private string GetAvailableSaveName(string requestedName)
    {
        string normalizedName = NormalizeSaveName(requestedName);
        string candidate = normalizedName;
        int suffix = 2;

        while (File.Exists(GetSaveFilePath(UserSavePath, candidate)))
        {
            candidate = $"{normalizedName} ({suffix})";
            suffix++;
        }

        return candidate;
    }

    private static string NormalizeSaveName(string requestedName)
    {
        string value = requestedName?.Trim() ?? string.Empty;
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidCharacter.ToString(), "_");
        }

        value = value.Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(value) || value == "." || value == "..")
        {
            value = NewWorldCreationRequest.CreateRandomNumericName();
        }

        const int maxLength = 48;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    /// <summary>
    /// 获取当前活动场景的地图保存数据（静态版本）
    /// </summary>
    /// <returns>地图保存数据</returns>
    public static MapSave GetCurrentMapStatic()
    {
        return new MapSave
        {
            Name = SceneManager.GetActiveScene().name,
            items = GetActiveSceneAllItemData_Static()
        };
    }

    public PlanetData GetCurrentPlanetData()
    {
        string activeSceneName = SceneManager.GetActiveScene().name;
        if (SaveData.PlanetData_Dict.TryGetValue(activeSceneName, out PlanetData planetData))
        {
            return planetData;
        }
        return null;
    }
    
    /// <summary>
    /// 获取当前活动场景中所有物品的数据（静态版本）
    /// </summary>
    /// <returns>物品数据字典</returns>
    public static Dictionary<string, HashSet<ItemData>> GetActiveSceneAllItemData_Static()
    {
        Item[] allItems = FindObjectsOfType<Item>(includeInactive: false);
        
        // 先处理所有物品保存
        SaveAllItems(allItems);
        
        // 再收集所有活动物品数据
        return CollectActiveItemData(allItems);
    }

    /// <summary>
    /// 保存所有物品数据
    /// </summary>
    private static void SaveAllItems(Item[] items)
    {
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
    }

    /// <summary>
    /// 收集活跃物品数据
    /// </summary>
    private static Dictionary<string, HashSet<ItemData>> CollectActiveItemData(Item[] items)
    {
        Dictionary<string, HashSet<ItemData>> itemDataDict = new Dictionary<string, HashSet<ItemData>>();

        foreach (Item item in items)
        {
            if (item == null || item.transform == null || item.gameObject == null)
                continue;

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
    /// 从磁盘加载存档
    /// </summary>
    /// <param name="loadSavePath">存档路径</param>
    public void LoadSaveByDisk(string loadSavePath)
    {
        if (string.IsNullOrEmpty(loadSavePath))
        {
            Debug.LogError("❌ 存档路径为空");
            return;
        }

        try
        {
            string fullPath = NormalizeSavePath(loadSavePath);
            string backupPath = GetBackupSavePath(fullPath);
            string temporaryPath = GetTemporarySavePath(fullPath);
            string[] candidatePaths = { fullPath, backupPath, temporaryPath };
            Exception lastException = null;

            for (int i = 0; i < candidatePaths.Length; i++)
            {
                string candidatePath = candidatePaths[i];
                if (!File.Exists(candidatePath))
                    continue;

                try
                {
                    Debug.Log($"📖 开始加载存档：{candidatePath}");
                    GameSaveData loadedSaveData = DeserializeSavePayload(File.ReadAllBytes(candidatePath));
                    if (loadedSaveData == null)
                        throw new InvalidDataException("存档反序列化结果为空");

                    SaveData = loadedSaveData;
                    if (i == 0)
                        Debug.Log("✅ 存档加载成功");
                    else
                        Debug.LogWarning($"⚠️ 正式存档不可用，已从{(i == 1 ? "备份" : "临时文件")}恢复：{candidatePath}");
                    return;
                }
                catch (Exception ex)
                {
                    if (ex is SaveVersionIncompatibleException)
                        throw;

                    lastException = ex;
                    Debug.LogWarning($"⚠️ 存档文件无效，尝试恢复文件：{candidatePath}\n{ex.Message}");
                }
            }

            if (lastException != null)
                throw new InvalidDataException("正式存档、备份及临时存档均无法加载", lastException);

            Debug.LogWarning($"⚠️ 存档文件不存在：{fullPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ 加载存档失败: {ex.Message}");
            Debug.LogException(ex);
        }
    }

    /// <summary>
    /// 删除指定存档
    /// </summary>
    /// <param name="savePath">存档路径</param>
    /// <param name="saveName">存档名称</param>
    public void DeleteSave(string savePath, string saveName)
    {
        string fullPath = GetSaveFilePath(savePath, saveName);
        DeleteSaveFile(fullPath);
    }
    
    /// <summary>
    /// 删除指定存档文件（已废弃，使用DeleteSave）
    /// </summary>
    /// <param name="fullPath">完整的存档文件路径</param>
    [System.Obsolete("使用 DeleteSave(path, name) 代替", false)]
    public void DeletSave(string fullPath)
    {
        DeleteSaveFile(fullPath);
    }

    /// <summary>
    /// 删除存档文件的内部方法
    /// </summary>
    private void DeleteSaveFile(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            Debug.LogError("❌ 存档路径为空");
            return;
        }

        try
        {
            string backupPath = GetBackupSavePath(fullPath);
            string temporaryPath = GetTemporarySavePath(fullPath);
            bool foundSaveFile = File.Exists(fullPath) || File.Exists(backupPath) || File.Exists(temporaryPath);

            DeleteFileIfExists(fullPath);
            DeleteFileIfExists(backupPath);
            DeleteFileIfExists(temporaryPath);
            DeleteFileIfExists(GetLastExitTimePath(fullPath));

            if (foundSaveFile)
            {
                Debug.Log($"✅ 存档已删除：{fullPath}");
            }
            else
            {
                Debug.LogWarning($"⚠️ 未找到要删除的存档文件：{fullPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ 删除存档失败：{fullPath}");
            Debug.LogException(ex);
        }
    }
    
    /// <summary>
    /// 获取默认存档路径
    /// </summary>
    /// <returns>默认存档路径</returns>
    public static string GetDefaultSavePath()
    {
        return Path.Combine(Application.persistentDataPath, "Saves", "LocalSaveData") + Path.DirectorySeparatorChar;
    }
    
    /// <summary>
    /// 获取存档文件完整路径
    /// </summary>
    /// <param name="saveName">存档名称</param>
    /// <returns>完整路径</returns>
    public string GetFullSavePath(string saveName)
    {
        return GetSaveFilePath(UserSavePath, saveName);
    }

    /// <summary>
    /// 构建存档文件路径
    /// </summary>
    private string GetSaveFilePath(string basePath, string saveName)
    {
        return Path.Combine(basePath, saveName + ".bytes");
    }

    private static string GetTemporarySavePath(string fullPath)
    {
        return fullPath + TemporarySaveSuffix;
    }

    private static string GetBackupSavePath(string fullPath)
    {
        return fullPath + BackupSaveSuffix;
    }

    private static string GetLastExitTimePath(string fullPath)
    {
        return fullPath + LastExitTimeSuffix;
    }

    /// <summary>
    /// 获取存档最后退出时间。旧存档没有元数据时，使用存档文件修改时间兼容。
    /// </summary>
    public static DateTime GetLastExitTimeUtc(string fullSavePath)
    {
        if (string.IsNullOrWhiteSpace(fullSavePath))
            return DateTime.MinValue;

        try
        {
            string metadataPath = GetLastExitTimePath(fullSavePath);
            if (File.Exists(metadataPath))
            {
                string value = File.ReadAllText(metadataPath);
                if (DateTime.TryParse(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out DateTime timestamp))
                {
                    return timestamp.ToUniversalTime();
                }
            }

            return File.Exists(fullSavePath)
                ? File.GetLastWriteTimeUtc(fullSavePath)
                : DateTime.MinValue;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"读取存档最后退出时间失败，将该存档排到列表末尾：{fullSavePath}\n{ex.Message}");
            return DateTime.MinValue;
        }
    }

    private static void RecordLastExitTimeUtc(string fullSavePath, DateTime exitTimeUtc)
    {
        try
        {
            File.WriteAllText(
                GetLastExitTimePath(fullSavePath),
                exitTimeUtc.ToUniversalTime().ToString(
                    "O",
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"存档已保存，但记录最后退出时间失败：{fullSavePath}\n{ex.Message}");
        }
    }

    /// <summary>
    /// 规范化存档路径（添加.bytes扩展名）
    /// </summary>
    private string NormalizeSavePath(string path)
    {
        return path.EndsWith(".bytes") ? path : path + ".bytes";
    }

    /// <summary>
    /// 确保目录存在
    /// </summary>
    private void EnsureDirectoryExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("存档目录为空", nameof(path));

        Directory.CreateDirectory(path);
    }

    /// <summary>
    /// 先将完整存档写入同目录临时文件并强制落盘，再原子替换正式文件。
    /// 正式文件的上一版本会保留为.bak备份。
    /// </summary>
    private static void WriteSaveAtomically(string fullPath, byte[] dataBytes)
    {
        string temporaryPath = GetTemporarySavePath(fullPath);
        string backupPath = GetBackupSavePath(fullPath);

        lock (SaveFileLock)
        {
            DeleteFileIfExists(temporaryPath);

            try
            {
                using (FileStream stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(dataBytes, 0, dataBytes.Length);
                    stream.Flush(true);
                }

                if (new FileInfo(temporaryPath).Length != dataBytes.LongLength)
                    throw new IOException("临时存档写入不完整");

                if (!File.Exists(fullPath))
                {
                    File.Move(temporaryPath, fullPath);
                    return;
                }

                DeleteFileIfExists(backupPath);

                try
                {
                    File.Replace(temporaryPath, fullPath, backupPath);
                }
                catch (PlatformNotSupportedException)
                {
                    ReplaceSaveWithBackupFallback(temporaryPath, fullPath, backupPath);
                }
                catch (NotSupportedException)
                {
                    ReplaceSaveWithBackupFallback(temporaryPath, fullPath, backupPath);
                }
            }
            finally
            {
                DeleteFileIfExists(temporaryPath);
            }
        }
    }

    /// <summary>
    /// 不支持File.Replace的平台使用可恢复的两步替换：旧正式存档先转为备份，再提交新存档。
    /// </summary>
    private static void ReplaceSaveWithBackupFallback(string temporaryPath, string fullPath, string backupPath)
    {
        File.Move(fullPath, backupPath);

        try
        {
            File.Move(temporaryPath, fullPath);
        }
        catch
        {
            if (!File.Exists(fullPath) && File.Exists(backupPath))
                File.Copy(backupPath, fullPath);

            throw;
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
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

    #region Chunk difference persistence

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

        try
        {
            string planetName = SceneManager.GetActiveScene().name;
            string key = BuildChunkKey(planetName, chunk.MapSave.Name);
            chunkBaselines[key] = CaptureChunkBaseline(chunk);

            if (chunkDeltas.TryGetValue(key, out ChunkSaveRecord delta))
                ApplyChunkDelta(chunk, delta);
            return true;
        }
        catch (Exception exception)
        {
            string planetName = SceneManager.GetActiveScene().name;
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

        string planetName = SceneManager.GetActiveScene().name;
        chunkBaselines.Remove(BuildChunkKey(planetName, chunk.MapSave.Name));
    }

    /// <summary>
    /// Builds a delta against the deterministic baseline. Returns false for legacy/full-snapshot chunks.
    /// </summary>
    public bool TrySaveChunkDifferences(Chunk chunk)
    {
        if (chunk?.MapSave == null || chunk.Map?.Data == null)
            return false;

        string planetName = SceneManager.GetActiveScene().name;
        string key = BuildChunkKey(planetName, chunk.MapSave.Name);
        if (!chunkBaselines.TryGetValue(key, out ChunkBaseline baseline))
            return false;

        ChunkSaveRecord delta = new ChunkSaveRecord
        {
            PlanetName = planetName,
            ChunkName = chunk.MapSave.Name,
            ChunkPosition = chunk.MapSave.MapPosition,
            SunlightIntensity = chunk.MapSave.SunlightIntensity,
            IsDelta = true
        };

        HashSet<int> currentGuids = new HashSet<int>();
        foreach (Item item in chunk.RunTimeItems.Values)
        {
            if (item == null || item is Map || item.itemData == null)
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

        // Prevent the old full-snapshot path from retaining generated content in memory.
        chunk.MapSave.items ??= new Dictionary<string, HashSet<ItemData>>();
        chunk.MapSave.items.Clear();

        if (delta.HasChanges)
            chunkDeltas[key] = delta;
        else
            chunkDeltas.Remove(key);

        return true;
    }

    public bool TryGetChunkDelta(string planetName, string chunkName, out ChunkSaveRecord delta)
    {
        return chunkDeltas.TryGetValue(BuildChunkKey(planetName, chunkName), out delta);
    }

    public void ResetChunkDifferenceState()
    {
        chunkBaselines.Clear();
        chunkDeltas.Clear();
    }

    private void PrepareLoadedChunksForSave()
    {
        if (ChunkMgr.Instance == null || ChunkMgr.Instance.Chunk_Dic_ByPos == null)
            return;

        List<Chunk> chunks = new List<Chunk>(ChunkMgr.Instance.Chunk_Dic_ByPos.Values);
        for (int i = 0; i < chunks.Count; i++)
        {
            Chunk chunk = chunks[i];
            if (chunk != null)
                chunk.SaveChunk();
        }
    }

    private ChunkBaseline CaptureChunkBaseline(Chunk chunk)
    {
        ChunkBaseline baseline = new ChunkBaseline();

        foreach (Item item in chunk.RunTimeItems.Values)
        {
            if (item == null || item is Map || item.itemData == null)
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

            RemoveRuntimeItem(chunk, savedData.Guid);

            try
            {
                ItemData runtimeData = CloneItemData(savedData);
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

    private byte[] BuildCompactSavePayload(GameSaveData saveData)
    {
        ModRuntimeManager modRuntime = ModRuntimeManager.Instance;
        if (modRuntime == null || !modRuntime.IsReady)
            throw new InvalidOperationException("MOD 框架尚未完成加载，无法保存世界");

        byte[] corePayload = BuildCoreCompactSavePayload(saveData);
        ModdedSaveEnvelope moddedEnvelope = new ModdedSaveEnvelope
        {
            Version = ModdedSaveVersion,
            CoreSavePayload = corePayload,
            ModMetadata = MemoryPackSerializer.Serialize(modRuntime.CaptureSaveMetadata())
        };

        byte[] body = MemoryPackSerializer.Serialize(moddedEnvelope);
        byte[] payload = new byte[ModdedSaveMagic.Length + body.Length];
        Buffer.BlockCopy(ModdedSaveMagic, 0, payload, 0, ModdedSaveMagic.Length);
        Buffer.BlockCopy(body, 0, payload, ModdedSaveMagic.Length, body.Length);
        return payload;
    }

    private byte[] BuildCoreCompactSavePayload(GameSaveData saveData)
    {

        MonsterSpawnerManager spawnerManager = FindObjectOfType<MonsterSpawnerManager>();
        spawnerManager?.CaptureSaveData(saveData);

        CompactSaveEnvelope envelope = new CompactSaveEnvelope
        {
            Version = CompactSaveVersion,
            CoreSaveData = SerializeCoreDataWithoutChunks(saveData)
        };

        HashSet<string> addedKeys = new HashSet<string>();
        foreach (KeyValuePair<string, ChunkSaveRecord> pair in chunkDeltas)
        {
            if (pair.Value == null || !pair.Value.HasChanges)
                continue;

            envelope.ChunkRecords.Add(pair.Value);
            addedKeys.Add(pair.Key);
        }

        if (saveData.PlanetData_Dict != null)
        {
            foreach (KeyValuePair<string, PlanetData> planetPair in saveData.PlanetData_Dict)
            {
                Dictionary<string, MapSave> maps = planetPair.Value?.MapData_Dict;
                if (maps == null)
                    continue;

                foreach (KeyValuePair<string, MapSave> mapPair in maps)
                {
                    string key = BuildChunkKey(planetPair.Key, mapPair.Key);
                    if (addedKeys.Contains(key) || chunkBaselines.ContainsKey(key) || mapPair.Value == null)
                        continue;

                    envelope.ChunkRecords.Add(new ChunkSaveRecord
                    {
                        PlanetName = planetPair.Key,
                        ChunkName = mapPair.Key,
                        ChunkPosition = mapPair.Value.MapPosition,
                        SunlightIntensity = mapPair.Value.SunlightIntensity,
                        IsDelta = false,
                        FullSnapshot = mapPair.Value
                    });
                    addedKeys.Add(key);
                }
            }
        }

        envelope.ChunkRecords.Sort((a, b) =>
        {
            int planetCompare = string.CompareOrdinal(a?.PlanetName, b?.PlanetName);
            return planetCompare != 0 ? planetCompare : string.CompareOrdinal(a?.ChunkName, b?.ChunkName);
        });

        byte[] body = MemoryPackSerializer.Serialize(envelope);
        byte[] payload = new byte[CompactSaveMagic.Length + body.Length];
        Buffer.BlockCopy(CompactSaveMagic, 0, payload, 0, CompactSaveMagic.Length);
        Buffer.BlockCopy(body, 0, payload, CompactSaveMagic.Length, body.Length);
        return payload;
    }

    private static byte[] SerializeCoreDataWithoutChunks(GameSaveData saveData)
    {
        List<(PlanetData planet, Dictionary<string, MapSave> maps)> backups = new();
        if (saveData.PlanetData_Dict != null)
        {
            foreach (PlanetData planet in saveData.PlanetData_Dict.Values)
            {
                if (planet == null)
                    continue;

                backups.Add((planet, planet.MapData_Dict));
                planet.MapData_Dict = new Dictionary<string, MapSave>();
            }
        }

        try
        {
            return MemoryPackSerializer.Serialize(saveData);
        }
        finally
        {
            for (int i = 0; i < backups.Count; i++)
                backups[i].planet.MapData_Dict = backups[i].maps;
        }
    }

    private GameSaveData DeserializeSavePayload(byte[] payload)
    {
        ResetChunkDifferenceState();

        ModSaveMetadata metadata = null;
        byte[] corePayload = payload;
        if (HasSaveHeader(payload, ModdedSaveMagic))
        {
            byte[] body = new byte[payload.Length - ModdedSaveMagic.Length];
            Buffer.BlockCopy(payload, ModdedSaveMagic.Length, body, 0, body.Length);
            ModdedSaveEnvelope envelope = MemoryPackSerializer.Deserialize<ModdedSaveEnvelope>(body);
            if (envelope == null || envelope.CoreSavePayload == null)
                throw new InvalidDataException("MOD 存档封装已损坏");
            if (envelope.Version != ModdedSaveVersion)
            {
                throw new SaveVersionIncompatibleException(
                    $"MOD 存档版本不兼容：存档={envelope.Version}，当前={ModdedSaveVersion}。不会迁移、覆盖或删除该存档。");
            }

            corePayload = envelope.CoreSavePayload;
            if (envelope.ModMetadata != null && envelope.ModMetadata.Length > 0)
                metadata = MemoryPackSerializer.Deserialize<ModSaveMetadata>(envelope.ModMetadata);
        }

        GameSaveData saveData = DeserializeCoreSavePayload(corePayload);
        ValidateAndRestoreModMetadata(metadata);
        return saveData;
    }

    private GameSaveData DeserializeCoreSavePayload(byte[] payload)
    {

        if (!HasSaveHeader(payload, CompactSaveMagic))
        {
            throw new SaveVersionIncompatibleException(
                "无头旧二进制存档与当前地形栈格式不兼容。不会迁移、覆盖或删除该存档。");
        }

        byte[] body = new byte[payload.Length - CompactSaveMagic.Length];
        Buffer.BlockCopy(payload, CompactSaveMagic.Length, body, 0, body.Length);
        CompactSaveEnvelope envelope = MemoryPackSerializer.Deserialize<CompactSaveEnvelope>(body);
        if (envelope == null || envelope.CoreSaveData == null)
            throw new InvalidDataException("差异存档封装已损坏");
        if (envelope.Version != CompactSaveVersion)
        {
            throw new SaveVersionIncompatibleException(
                $"差异存档版本不兼容：存档={envelope.Version}，当前={CompactSaveVersion}。不会迁移、覆盖或删除该存档。");
        }

        GameSaveData saveData = MemoryPackSerializer.Deserialize<GameSaveData>(envelope.CoreSaveData);
        if (saveData == null)
            throw new InvalidDataException("差异存档的核心数据为空");

        saveData.PlanetData_Dict ??= new Dictionary<string, PlanetData>();
        if (envelope.ChunkRecords == null)
            return saveData;

        for (int i = 0; i < envelope.ChunkRecords.Count; i++)
        {
            ChunkSaveRecord record = envelope.ChunkRecords[i];
            if (record == null || string.IsNullOrEmpty(record.PlanetName) || string.IsNullOrEmpty(record.ChunkName))
                continue;

            if (!saveData.PlanetData_Dict.TryGetValue(record.PlanetName, out PlanetData planet) || planet == null)
                continue;

            planet.MapData_Dict ??= new Dictionary<string, MapSave>();
            if (record.IsDelta)
            {
                chunkDeltas[BuildChunkKey(record.PlanetName, record.ChunkName)] = record;
            }
            else if (record.FullSnapshot != null)
            {
                planet.MapData_Dict[record.ChunkName] = record.FullSnapshot;
            }
        }

        return saveData;
    }

    private static void ValidateAndRestoreModMetadata(ModSaveMetadata metadata)
    {
        ModRuntimeManager modRuntime = ModRuntimeManager.Instance;
        if (modRuntime == null || !modRuntime.IsReady)
            throw new InvalidOperationException("MOD 框架尚未完成加载，不能读取存档");

        if (!modRuntime.ValidateSaveMetadata(metadata, out string error))
            throw new InvalidDataException($"存档 MOD 环境不兼容：{error}");

        modRuntime.RestoreSaveMetadata(metadata);
    }

    private static bool HasSaveHeader(byte[] payload, byte[] magic)
    {
        if (payload == null || magic == null || payload.Length <= magic.Length)
            return false;

        for (int i = 0; i < magic.Length; i++)
        {
            if (payload[i] != magic[i])
                return false;
        }

        return true;
    }

    private static string BuildChunkKey(string planetName, string chunkName)
    {
        return $"{planetName}\u001f{chunkName}";
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

    private static ItemData CloneItemData(ItemData itemData)
    {
        if (itemData == null)
            return null;

        byte[] bytes = MemoryPackSerializer.Serialize<ItemData>(itemData);
        return MemoryPackSerializer.Deserialize<ItemData>(bytes);
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
    
    #endregion
}

[MemoryPackable]
[Serializable]
public partial class ModdedSaveEnvelope
{
    public int Version;
    public byte[] CoreSavePayload;
    public byte[] ModMetadata;
}

public sealed class SaveVersionIncompatibleException : IOException
{
    public SaveVersionIncompatibleException(string message) : base(message)
    {
    }
}

[MemoryPackable]
[Serializable]
public partial class CompactSaveEnvelope
{
    public int Version;
    public byte[] CoreSaveData;
    public List<ChunkSaveRecord> ChunkRecords = new();
}

[MemoryPackable]
[Serializable]
public partial class ChunkSaveRecord
{
    public string PlanetName;
    public string ChunkName;
    public Vector2Int ChunkPosition;
    public float SunlightIntensity;
    public bool IsDelta;
    public MapSave FullSnapshot;
    public List<ItemData> ChangedItems = new();
    public List<int> RemovedItemGuids = new();
    public List<TileCellSaveDelta> TileDeltas = new();
    public List<GrassCellSaveDelta> GrassDeltas = new();

    [MemoryPackIgnore]
    public bool HasChanges =>
        IsDelta &&
        ((ChangedItems?.Count ?? 0) > 0 ||
         (RemovedItemGuids?.Count ?? 0) > 0 ||
         (TileDeltas?.Count ?? 0) > 0 ||
         (GrassDeltas?.Count ?? 0) > 0);
}

[MemoryPackable]
[Serializable]
public partial class TileCellSaveDelta
{
    public Vector2Int LocalPosition;
    public List<TileData> Tiles = new();
}

[MemoryPackable]
[Serializable]
public partial class GrassCellSaveDelta
{
    public Vector2Int LocalPosition;
    public GrassCellState State;
}
