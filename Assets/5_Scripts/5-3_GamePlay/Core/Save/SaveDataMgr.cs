// ===== AI 函数导航（按函数名搜索，不依赖行号） =====
// 网络快照：CreateCompressedNetworkSnapshot() / ApplyCompressedNetworkSnapshot()
// 保存入口：Save_And_WriteToDisk() / Save_And_WriteToDiskInBackground() / Save_And_WriteToDiskInBackgroundCoroutine()
// 磁盘读写：SaveToDisk() / LoadSaveByDisk() / WriteSaveAtomically() / InvalidatePendingBackgroundSave()
// 存档重命名：TryRenameSave() / TryRenamePlayerDisplayName()
// 区块差量：TryFinalizeProceduralChunk() / TrySaveChunkDifferences() / TrySaveChunkDifferencesCoroutine() / ApplyChunkDelta()
// 地形差量：RestoreRuntimeTerrainForChunk() / RecordRuntimeTerrainChange() / CaptureRuntimeTerrainStates()
// 建筑存档：RecordRuntimeBuildingChange() / CaptureRuntimeBuildingStates() / RestoreRuntimeBuildingsForChunk()
// AI 实体存档：CaptureRuntimeAiEntityStates() / RestoreRuntimeAiEntitiesForChunk()
// 编码解码：BuildCompactSavePayload() / BuildCoreCompactSavePayload() / DeserializeSavePayload() / DeserializeCoreSavePayload()
// ====================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using FlatWorld.Diagnostics;
using UnityEngine;
using MemoryPack;
using Sirenix.OdinInspector;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// 游戏存档与加载系统，负责管理游戏数据的保存和加载功能
/// </summary>
public partial class SaveDataMgr : SingletonAutoMono<SaveDataMgr>
{
    #region 保存性能诊断
    private static readonly PerformanceDebugPoint SaveCapturePoint = new("FlatWorld.Save.Capture.Sync");
    private static readonly PerformanceDebugPoint SaveCaptureSlicePoint = new("FlatWorld.Save.Capture.Slice");
    private static readonly PerformanceDebugPoint SavePayloadSlicePoint = new("FlatWorld.Save.Encode.Slice");
    private static readonly PerformanceDebugPoint SaveEncodePoint = new("FlatWorld.Save.Encode.Sync");
    private static readonly PerformanceDebugPoint SaveBackgroundEncodePoint = new("FlatWorld.Save.Encode.BackgroundEnvelope");
    private static readonly PerformanceDebugPoint SaveCoreCapturePoint = new("FlatWorld.Save.Capture.Core");
    private static readonly PerformanceDebugPoint SaveCoreEncodePoint = new("FlatWorld.Save.Encode.Core");
    private static readonly PerformanceDebugPoint SaveChunkEncodePoint = new("FlatWorld.Save.Encode.Chunks");
    private static readonly PerformanceDebugPoint SaveEcologyMetadataPoint = new("FlatWorld.Save.Encode.EcologyMetadata");
    private static readonly PerformanceDebugPoint SaveEcologyItemPoint = new("FlatWorld.Save.Encode.EcologyItem");
    private static readonly PerformanceDebugPoint SaveEcologyFinalizePoint = new("FlatWorld.Save.Capture.EcologyFinalize");
    private static readonly PerformanceDebugPoint SavePlayerCapturePoint = new("FlatWorld.Save.Capture.PlayerAndClock");
    private static readonly PerformanceDebugPoint SaveMachinesCapturePoint = new("FlatWorld.Save.Capture.Machines");
    private static readonly PerformanceDebugPoint SaveSpaceCapturePoint = new("FlatWorld.Save.Capture.Space");
    private static readonly PerformanceDebugPoint SaveDropsCapturePoint = new("FlatWorld.Save.Capture.DroppedItems");
    private static readonly PerformanceDebugPoint SaveWritePoint = new("FlatWorld.Save.Write.Atomic");
    private static readonly PerformanceDebugPoint SaveWriteBytesPoint = new("FlatWorld.Save.Write.BytesAndFlush");
    private static readonly PerformanceDebugPoint SaveSnapshotCompressPoint = new("FlatWorld.Save.NetworkSnapshot.GZip");

    // 协程只计每次同步推进，不能把 yield 等待时间算成 CPU 采集耗时。
    private static bool MoveSaveSlice(IEnumerator routine, PerformanceDebugPoint point)
    {
        using var sample = point.Measure(1);
        return routine.MoveNext();
    }
    #endregion

    private const int CompactSaveVersion = 34; // 地表快照新增天然冰融化进度，仅接受当前结构。
    private const int ModdedSaveVersion = 15;
    private const string TemporarySaveSuffix = ".tmp";
    private const string BackupSaveSuffix = ".bak";
    private const string LastExitTimeSuffix = ".lastplayed";
    // 实例状态采用当前布局，先检查封装头，再解析嵌套 MemoryPack 数据。
    private static readonly byte[] ModdedSaveMagic = { (byte)'F', (byte)'W', (byte)'D', (byte)'6' };
    private static readonly object SaveFileLock = new object();
    private static readonly object SaveRevisionLock = new object();
    private static readonly Dictionary<string, long> LatestSaveRevisions =
        new(StringComparer.OrdinalIgnoreCase);
    private static long nextSaveRevision;
    private string pendingBackgroundSavePath;
    private long pendingBackgroundSaveRevision;

    private readonly Dictionary<string, ChunkBaseline> chunkBaselines = new();
    private readonly Dictionary<string, ChunkSaveRecord> chunkDeltas = new();
    private readonly Dictionary<string, RuntimeChunkBaseline> runtimeChunkBaselines = new();
    private readonly HashSet<string> runtimeTerrainDirtyChunks = new(StringComparer.Ordinal);
    private readonly HashSet<string> runtimeBuildingDirtyChunks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeWorldAddress> runtimeBuildingDirtyAddresses = new(StringComparer.Ordinal);
    private sealed class WorldRestoreState
    {
        public readonly HashSet<string> AiChunks = new(StringComparer.Ordinal), BuildingChunks = new(StringComparer.Ordinal);
        public WorldRuntime AiWorld, BuildingWorld;
        public long AiEpoch = long.MinValue, BuildingEpoch = long.MinValue;
    }
    private readonly Dictionary<string, WorldRestoreState> worldRestoreStates = new(StringComparer.Ordinal);
    private WorldRestoreState RestoreState
    {
        get
        {
            string key = GameplayWorldRegistry.ExecutionWorldKey;
            if (!worldRestoreStates.TryGetValue(key, out WorldRestoreState value)) worldRestoreStates.Add(key, value = new WorldRestoreState());
            return value;
        }
    }
    private HashSet<string> restoredRuntimeAiChunks => RestoreState.AiChunks;
    private HashSet<string> restoredRuntimeBuildingChunks => RestoreState.BuildingChunks;
    private WorldRuntime restoredRuntimeAiWorld { get => RestoreState.AiWorld; set => RestoreState.AiWorld = value; }
    private WorldRuntime restoredRuntimeBuildingWorld { get => RestoreState.BuildingWorld; set => RestoreState.BuildingWorld = value; }
    private long restoredRuntimeAiEpoch { get => RestoreState.AiEpoch; set => RestoreState.AiEpoch = value; }
    private long restoredRuntimeBuildingEpoch { get => RestoreState.BuildingEpoch; set => RestoreState.BuildingEpoch = value; }

    #region 存档配置
    [Tooltip("玩家的存档路径")]
    public string UserSavePath = ""; // 初始化为空，将在Awake中设置

    [SerializeField, HideInInspector]
    private GameSaveData currentSaveData;

    [ShowInInspector]
    public GameSaveData SaveData
    {
        get => currentSaveData;
        set
        {
            if (ReferenceEquals(currentSaveData, value)) return;
            GameSaveData previous = currentSaveData;
            currentSaveData = value;
            // 换根档时先发布最终规则，再允许后续世界和角色加载读取配置。
            try
            {
                if (ReferenceEquals(instance, this)) GameWorldRuleSession.Bind(value);
            }
            catch
            {
                currentSaveData = previous;
                throw;
            }
        }
    }

    [Tooltip("当前控制的角色 ID；字段名保留以兼容现有调用")]
    public string CurrentContrrolPlayerName;

    /// <summary>为新角色创建与显示名无关的持久身份键。</summary>
    public static string CreatePlayerProfileId() => "player:" + Guid.NewGuid().ToString("N");
    
    #region 逻辑世界数据查询
    public PlanetData Active_PlanetData => GetCurrentPlanetData();

    public bool TryGetActivePlanetData(out PlanetData planetData)
    {
        planetData = SaveData?.CurrentPlanetData;
        return planetData != null;
    }
    #endregion

    protected override void Awake()
    {
        base.Awake();
        if (ReferenceEquals(instance, this)) GameWorldRuleSession.Bind(currentSaveData);
        InitializeUserSavePath();
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(instance, this)) GameWorldRuleSession.Bind(null);
        base.OnDestroy();
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
        using IDisposable publicPlayerCapture = ItemSnapshotSerialization.BeginPublicPlayerState(
            MachineInventoryCommands.PublicSpecialData);
        byte[] rawData = BuildCompactSavePayload(SaveData);
        using MemoryStream output = new MemoryStream();
        using var compressionSample = SaveSnapshotCompressPoint.Measure(rawData.LongLength);
        using (GZipStream gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
            gzip.Write(rawData, 0, rawData.Length);
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
    /// 自动保存专用入口：主线程按帧预算完成 Unity 对象快照，最后构建独立字节数组，随后仅把它交给后台原子写盘。
    /// 不触碰输入锁、时间缩放或实体启停，且较新的手动/退出保存会使旧自动保存失效。
    /// </summary>
    public Task<bool> Save_And_WriteToDiskInBackground()
    {
        if (SaveData == null)
        {
            Debug.LogError("❌ SaveData为null，无法自动保存");
            return Task.FromResult(false);
        }

        try
        {
            // Unity 对象和 ModRuntime 只能在主线程读取；后台任务只接收不可变字节数组和文件路径。
            PrepareLoadedChunksForSave();
            return QueueCurrentSaveWriteInBackground();
        }
        catch (Exception exception)
        {
            Debug.LogError($"❌ 自动保存快照创建失败: {exception.Message}");
            Debug.LogException(exception);
            return Task.FromException<bool>(exception);
        }
    }

    /// <summary>
    /// 自动保存专用协程：先通过既有入口采集生态自然物，再把旧区块快照分散到多帧。
    /// 完成快照后通过回调交付后台写盘任务，调用方只能轮询任务，不能同步等待。
    /// </summary>
    public IEnumerator Save_And_WriteToDiskInBackgroundCoroutine(Action<Task<bool>> onWriteQueued,
        Action captureCoreState, AutoSaveWorkBudget budget = null, Func<bool> canContinue = null)
    {
        if (SaveData == null)
        {
            Debug.LogError("❌ SaveData为null，无法自动保存");
            onWriteQueued?.Invoke(Task.FromResult(false));
            yield break;
        }

        budget ??= new AutoSaveWorkBudget();
        GameSaveData capturedSave = SaveData;
        AutoSaveWorldScope worldScope = CaptureAutoSaveWorldScope();
        if (!worldScope.IsCurrent || (canContinue != null && !canContinue()))
        {
            onWriteQueued?.Invoke(Task.FromResult(false));
            yield break;
        }
        string fullPath = null;
        long saveRevision = 0;
        Exception setupFailure = null;
        try
        {
            EnsureDirectoryExists(UserSavePath);
            fullPath = GetSaveFilePath(UserSavePath, capturedSave.saveName);
            // 长采集开始前登记版本，后来的手动或退出保存始终可以取代它。
            saveRevision = ReserveSaveRevision(fullPath);
            pendingBackgroundSavePath = fullPath;
            pendingBackgroundSaveRevision = saveRevision;
        }
        catch (Exception exception) { setupFailure = exception; }
        if (setupFailure != null)
        {
            onWriteQueued?.Invoke(Task.FromException<bool>(setupFailure));
            yield break;
        }

        ecologySnapshotReaders++;
        try
        {
            bool CanCapture() => worldScope.IsCurrent &&
                (canContinue == null || canContinue()) &&
                ReferenceEquals(SaveData, capturedSave) && IsSaveRevisionCurrent(fullPath, saveRevision);
            Exception captureFailure = null;
            IEnumerator captureRoutine = ForwardAutoSaveRoutine(
                PrepareLoadedChunksForAutoSaveCoroutine(exception => captureFailure = exception, budget),
                exception => captureFailure = exception);
            try
            {
                while (CanCapture() && MoveSaveSlice(captureRoutine, SaveCaptureSlicePoint))
                {
                    if (!CanCapture())
                        break;
                    yield return captureRoutine.Current;
                }
            }
            finally { (captureRoutine as IDisposable)?.Dispose(); }

            if (!CanCapture())
            {
                onWriteQueued?.Invoke(Task.FromResult(false));
                yield break;
            }

            if (captureFailure != null)
            {
                onWriteQueued?.Invoke(Task.FromException<bool>(captureFailure));
                yield break;
            }

            bool writeQueued = false;
            IEnumerator payloadRoutine = ForwardAutoSaveRoutine(
                QueueCapturedSaveWriteCoroutine(capturedSave, fullPath, saveRevision, budget, captureCoreState, task =>
                {
                    writeQueued = true;
                    onWriteQueued?.Invoke(task);
                }),
                exception => captureFailure = exception);
            try
            {
                while (CanCapture() && MoveSaveSlice(payloadRoutine, SavePayloadSlicePoint))
                {
                    if (!CanCapture())
                        break;
                    yield return payloadRoutine.Current;
                }
            }
            finally { (payloadRoutine as IDisposable)?.Dispose(); }
            if (!writeQueued && !CanCapture())
            {
                onWriteQueued?.Invoke(Task.FromResult(false));
                yield break;
            }
            if (captureFailure != null)
                onWriteQueued?.Invoke(Task.FromException<bool>(captureFailure));
        }
        finally
        {
            ecologySnapshotReaders--;
        }
    }

    /// <summary>在主线程构建完整字节快照，并返回不访问 Unity API 的后台文件写入任务。</summary>
    private Task<bool> QueueCurrentSaveWriteInBackground()
    {
        try
        {
            EnsureDirectoryExists(UserSavePath);

            string fullPath = GetSaveFilePath(UserSavePath, SaveData.saveName);
            byte[] dataBytes = BuildCompactSavePayload(SaveData);
            long saveRevision = ReserveSaveRevision(fullPath);
            pendingBackgroundSavePath = fullPath;
            pendingBackgroundSaveRevision = saveRevision;

            return Task.Run(() =>
                WriteSaveAtomically(fullPath, dataBytes, saveRevision, discardIfSuperseded: true));
        }
        catch (Exception exception)
        {
            Debug.LogError($"❌ 自动保存快照创建失败: {exception.Message}");
            Debug.LogException(exception);
            return Task.FromException<bool>(exception);
        }
    }

    /// <summary>
    /// 保存当前游戏状态，并记录玩家退出该存档的现实时间。
    /// </summary>
    public bool Save_And_WriteToDiskAndRecordExitTime()
    {
        return SaveCurrentToDisk(recordExitTime: true);
    }

    private bool SaveCurrentToDisk(bool recordExitTime)
    {
        if (SaveData == null)
        {
            Debug.LogError("❌ SaveData为null，无法保存");
            return false;
        }

        PrepareLoadedChunksForSave();
        if (!SaveToDisk(SaveData, UserSavePath, SaveData.saveName))
            return false;

        if (recordExitTime)
        {
            string fullPath = GetSaveFilePath(UserSavePath, SaveData.saveName);
            RecordLastExitTimeUtc(fullPath, DateTime.UtcNow);
        }
        return true;
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
            long saveRevision = ReserveSaveRevision(fullPath);
            if (!WriteSaveAtomically(fullPath, dataBytes, saveRevision, discardIfSuperseded: false))
                return false;

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

    #region 存档与角色改名

    /// <summary>只修改角色显示名，字典键、运行时玩家索引和控制角色 ID 保持稳定。</summary>
    public bool TryRenamePlayerDisplayName(string profileId, string requestedName)
        => TryRenamePlayerDisplayName(profileId, requestedName, out _);

    // 改名拒绝保留真实校验或写盘阶段，界面不再猜测名称和磁盘原因。
    public bool TryRenamePlayerDisplayName(string profileId, string requestedName, out string reason)
    {
        reason = null;
        string displayName = requestedName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        { reason = "名称不能为空"; return false; }
        if (displayName.Length > 48)
        { reason = "名称不能超过48个字符。"; return false; }
        if (SaveData?.PlayerData_Dict == null)
        { reason = "当前存档尚未载入，无法修改角色名称。"; return false; }
        if (string.IsNullOrWhiteSpace(profileId) ||
            !SaveData.PlayerData_Dict.TryGetValue(profileId, out Data_Player playerData) || playerData == null)
        { reason = "所选角色已不存在，请重新选择。"; return false; }
        if (string.Equals(playerData.Name_User, displayName, StringComparison.Ordinal))
            return true;
        foreach (KeyValuePair<string, Data_Player> profile in SaveData.PlayerData_Dict)
        {
            if (!string.Equals(profile.Key, profileId, StringComparison.Ordinal) &&
                string.Equals(profile.Value?.Name_User, displayName, StringComparison.OrdinalIgnoreCase))
            { reason = "当前存档中已有同名角色。"; return false; }
        }

        string previousName = playerData.Name_User;
        playerData.Name_User = displayName;
        if (SaveToDisk(SaveData, UserSavePath, SaveData.saveName))
            return true;

        playerData.Name_User = previousName;
        reason = "角色名称写盘失败，已保留原名称。";
        return false;
    }

    /// <summary>先写入新文件及内部名称，再移除旧文件；失败时保留旧档。</summary>
    public bool TryRenameSave(string oldName, string requestedName, out string renamedSaveName)
        => TryRenameSave(oldName, requestedName, out renamedSaveName, out _);

    public bool TryRenameSave(string oldName, string requestedName, out string renamedSaveName, out string reason)
    {
        renamedSaveName = oldName;
        reason = null;
        string newName = requestedName?.Trim();
        if (SaveData == null)
        { reason = "当前存档尚未载入，无法改名。"; return false; }
        if (string.IsNullOrWhiteSpace(oldName) ||
            !string.Equals(SaveData.saveName, oldName, StringComparison.Ordinal))
        { reason = "当前选择的存档已变化，请重新选择。"; return false; }
        if (string.IsNullOrWhiteSpace(newName))
        { reason = "名称不能为空"; return false; }
        if (newName.Length > 48)
        { reason = "名称不能超过48个字符。"; return false; }
        if (!string.Equals(newName, NormalizeSaveName(newName), StringComparison.Ordinal))
        { reason = "存档名称包含文件名不允许的字符，或以句点结尾。"; return false; }
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return true;

        string oldPath = GetSaveFilePath(UserSavePath, oldName);
        string newPath = GetSaveFilePath(UserSavePath, newName);
        if (!File.Exists(oldPath))
        { reason = "原存档文件已不存在，请刷新存档列表。"; return false; }
        if (File.Exists(newPath) || File.Exists(GetBackupSavePath(newPath)) || File.Exists(GetTemporarySavePath(newPath)))
        { reason = "这个存档名称已被存档或恢复文件占用。"; return false; }

        DateTime lastExitTimeUtc = GetLastExitTimeUtc(oldPath);
        if (!SaveToDisk(SaveData, UserSavePath, newName))
        {
            SaveData.saveName = oldName;
            reason = "新存档文件写入失败，已保留原名称。";
            return false;
        }

        try
        {
            if (lastExitTimeUtc != DateTime.MinValue)
                RecordLastExitTimeUtc(newPath, lastExitTimeUtc);
            // 先让旧路径的后台快照过期，再与原子写盘共用文件锁移除旧档。
            ReserveSaveRevision(oldPath);
            lock (SaveFileLock)
            {
                DeleteSaveFile(oldPath);
                if (File.Exists(oldPath))
                    throw new IOException("旧存档未能删除");
            }
            renamedSaveName = newName;
            return true;
        }
        catch (Exception exception)
        {
            reason = "旧存档或退出记录更新失败，未完成改名。";
            Debug.LogException(exception);
            DeleteSaveFile(newPath);
            SaveData.saveName = oldName;
            return false;
        }
    }

    #endregion

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
            value = NewWorldCreationRequest.CreateRandomWorldName();
        }

        const int maxLength = 48;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
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

    public PlanetData GetCurrentPlanetData()
        => TryGetActivePlanetData(out PlanetData planetData) ? planetData : null;
    
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
    /// 从磁盘加载存档
    /// </summary>
    /// <param name="loadSavePath">存档路径</param>
    public bool LoadSaveByDisk(string loadSavePath)
    {
        // 新选择载入失败时不能继续使用上一份存档和角色。
        SaveData = null;
        CurrentContrrolPlayerName = string.Empty;
        if (string.IsNullOrEmpty(loadSavePath))
        {
            Debug.LogError("❌ 存档路径为空");
            return false;
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
                    return true;
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
            if (ex is not SaveVersionIncompatibleException)
                Debug.LogException(ex);
        }
        return false;
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
    private static bool WriteSaveAtomically(
        string fullPath,
        byte[] dataBytes,
        long saveRevision,
        bool discardIfSuperseded)
    {
        using var writeSample = SaveWritePoint.Measure();
        writeSample.SetGauge(dataBytes?.LongLength ?? 0);
        string temporaryPath = GetTemporarySavePath(fullPath);
        string backupPath = GetBackupSavePath(fullPath);

        lock (SaveFileLock)
        {
            // 退出/手动保存已生成更新快照时，丢弃尚未开始写入的旧自动保存，
            // 防止后台任务在世界退出后把过期状态覆盖到磁盘。
            if (discardIfSuperseded && !IsSaveRevisionCurrent(fullPath, saveRevision))
                return false;

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
                    using var bytesSample = SaveWriteBytesPoint.Measure();
                    stream.Write(dataBytes, 0, dataBytes.Length);
                    bytesSample.AddWork(dataBytes.LongLength);
                    stream.Flush(true);
                }

                if (new FileInfo(temporaryPath).Length != dataBytes.LongLength)
                    throw new IOException("临时存档写入不完整");

                // 退出或更新保存可能发生在写临时文件期间，提交正式文件前再检查一次。
                if (discardIfSuperseded && !IsSaveRevisionCurrent(fullPath, saveRevision))
                    return false;

                if (!File.Exists(fullPath))
                {
                    File.Move(temporaryPath, fullPath);
                    return true;
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

                return true;
            }
            finally
            {
                DeleteFileIfExists(temporaryPath);
            }
        }
    }

    /// <summary>退出世界时使旧后台保存失效，不等待磁盘任务，也不影响接下来的正式退出保存。</summary>
    public void InvalidatePendingBackgroundSave()
    {
        string fullPath = pendingBackgroundSavePath;
        long revision = pendingBackgroundSaveRevision;
        pendingBackgroundSavePath = null;
        pendingBackgroundSaveRevision = 0;
        if (!string.IsNullOrEmpty(fullPath) && IsSaveRevisionCurrent(fullPath, revision))
            ReserveSaveRevision(fullPath);
    }

    /// <summary>
    /// 为同一存档文件登记最新快照版本；版本锁与文件锁分离，避免主线程等待后台磁盘写入。
    /// </summary>
    private static long ReserveSaveRevision(string fullPath)
    {
        string saveKey = Path.GetFullPath(fullPath);
        lock (SaveRevisionLock)
        {
            long revision = ++nextSaveRevision;
            LatestSaveRevisions[saveKey] = revision;
            return revision;
        }
    }

    /// <summary>判断后台自动保存的快照是否仍是该文件的最新版本。</summary>
    private static bool IsSaveRevisionCurrent(string fullPath, long saveRevision)
    {
        string saveKey = Path.GetFullPath(fullPath);
        lock (SaveRevisionLock)
        {
            return LatestSaveRevisions.TryGetValue(saveKey, out long latestRevision) &&
                   latestRevision == saveRevision;
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
    {
        var lookup = (planetName, chunkOrigin);
        if (!chunkDeltaLookupKeys.TryGetValue(lookup, out string key))
        {
            if (chunkDeltaLookupKeys.Count >= 256) chunkDeltaLookupKeys.Clear();
            chunkDeltaLookupKeys.Add(lookup, key = BuildChunkKey(planetName, chunkOrigin.ToString()));
        }
        return chunkDeltas.TryGetValue(key, out delta);
    }
    #endregion

    #region Runtime terrain persistence

    /// <summary>
    /// 新版 WorldModel 区块生成完成后恢复玩家修改的地表与阻挡地块。
    /// 先记录程序化生成的完整核心地形，再覆盖差量，确保平台下的水不会在重载后恢复。
    /// </summary>
    public void RestoreRuntimeTerrainForChunk(RuntimeWorldAddress address, ChunkRuntime chunk)
    {
        if (SaveData == null || chunk == null ||
            chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null ||
            chunk.Terrain.IsDisposed)
        {
            return;
        }

        string planetName = ResolveRuntimePlanetName();
        string chunkName = ToChunkName(address);
        string key = BuildChunkKey(planetName, chunkName);
        if (!TryEnsureRuntimeChunkBaseline(key, chunk, out RuntimeChunkBaseline baseline) ||
            baseline.PersistenceRestored)
            return;

        if (!chunkDeltas.TryGetValue(key, out ChunkSaveRecord delta) || delta == null)
        {
            baseline.PersistenceRestored = true;
            return;
        }

        ChunkTerrainData terrain = chunk.Terrain;
        for (int i = 0; i < (delta.RuntimeTileDeltas?.Count ?? 0); i++)
        {
            RuntimeTileCellSaveDelta cell = delta.RuntimeTileDeltas[i];
            int x = cell.LocalPosition.x;
            int y = cell.LocalPosition.y;
            if ((uint)x >= (uint)terrain.Width || (uint)y >= (uint)terrain.Height)
                continue;

            terrain.SetCell(x, y, cell.ToTerrainCell());

            // 同一格的建筑损伤与裸露泥炭采挖互斥；保留后者的部分进度以便读档续挖。
            float accumulatedDamage = cell.BlockingTileId != 0 ||
                GroundTileHarvestSystem.IsHarvestableGround(cell.GroundTileId)
                ? Mathf.Max(0f, cell.AccumulatedDamage) : 0f;
            WriteRuntimeBuildingDamage(terrain, x, y, accumulatedDamage);
        }
        RestoreAgricultureTerrain(chunk, delta);
        foreach (GrassCellSaveDelta grass in delta.GrassDeltas)
        {
            int x = grass.LocalPosition.x, y = grass.LocalPosition.y;
            if ((uint)x < (uint)terrain.Width && (uint)y < (uint)terrain.Height &&
                (grass.State == GrassCellState.Removed || grass.State == GrassCellState.Empty))
                terrain.SetGrass(x, y, 0);
        }
        RestoreSupportTerrain(chunk, delta);
        RestoreContaminationTerrain(chunk, delta);
        RestoreLiquidTerrain(chunk, delta);
        RestoreGroundLayerTerrain(chunk, delta);
        RestoreSnowTerrain(chunk, delta);
        baseline.PersistenceRestored = true;
    }

    /// <summary>放置、受损或拆除运行时格子建筑后立即更新内存差量，避免区块回收时丢失状态。</summary>
    public void RecordRuntimeTerrainChange(TileBuildingCell placedCell)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null ||
            !placedCell.UsesRuntimeTerrain)
        {
            return;
        }

        ChunkRuntime chunk = placedCell.RuntimeChunk;
        ChunkTerrainData terrain = chunk?.Terrain;
        if (chunk == null || chunk.DataStatus != ChunkDataStatus.Ready ||
            terrain == null || terrain.IsDisposed)
        {
            return;
        }

        string planetName = ResolveRuntimePlanetName();
        string chunkName = ToChunkName(chunk.Address);
        string key = BuildChunkKey(planetName, chunkName);
        if (!TryEnsureRuntimeChunkBaseline(key, chunk, out RuntimeChunkBaseline baseline))
            return;

        int x = placedCell.LocalPosition.x;
        int y = placedCell.LocalPosition.y;
        if ((uint)x >= (uint)terrain.Width || (uint)y >= (uint)terrain.Height)
            return;

        TerrainCell currentCell = terrain.GetCell(x, y);
        float accumulatedDamage = ReadRuntimeBuildingDamage(terrain, x, y);
        SetRuntimeTileDelta(key, planetName, chunk.Address, placedCell.LocalPosition,
            currentCell, accumulatedDamage, baseline.GetCell(x, y),
            baseline.GetAccumulatedDamage(x, y));
        runtimeTerrainDirtyChunks.Add(key);
        NotifyAuthoritativeTerrainCellChanged(chunk, placedCell.LocalPosition);
    }

    // 场景表现解绑会结清天气批处理，世界模型交接前再次保存实际地形变化。
    internal bool CaptureRuntimeTerrainAfterSceneViewUnbinding(string worldKey)
    {
        if (string.IsNullOrEmpty(worldKey) ||
            !string.Equals(worldKey, GameplayWorldRegistry.ExecutionWorldKey, StringComparison.Ordinal)) return false;
        try { return CaptureRuntimeTerrainStates(); }
        catch (Exception exception)
        {
            Debug.LogError($"[SaveDataMgr] 场景解绑后采集地形失败：{worldKey}，{exception}");
            return false;
        }
    }

    /// <summary>保存前复核本次变脏的运行时区块，避免只依赖单格通知留下半成品差量。</summary>
    private bool CaptureRuntimeTerrainStates()
    {
        if (!GameNetwork.HasStateAuthority || runtimeTerrainDirtyChunks.Count == 0) return true;
        string planetName = GameplayWorldRegistry.ExecutionWorldKey;
        if (string.IsNullOrWhiteSpace(planetName)) return false;
        string prefix = planetName + "\u001f";
        bool hasWorldChanges = false;
        foreach (string dirtyKey in runtimeTerrainDirtyChunks)
            if (dirtyKey.StartsWith(prefix, StringComparison.Ordinal)) { hasWorldChanges = true; break; }
        if (!hasWorldChanges) return true;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (SaveData == null || manager?.WorldRuntime == null || manager.WorldRuntime.WorldId != planetName)
            return false;

        bool succeeded = true;
        IReadOnlyDictionary<RuntimeWorldAddress, ChunkRuntime> chunks = manager.Chunks;
        foreach (KeyValuePair<RuntimeWorldAddress, ChunkRuntime> pair in chunks)
        {
            string key = BuildChunkKey(planetName, ToChunkName(pair.Key));
            if (!runtimeTerrainDirtyChunks.Contains(key)) continue;
            ChunkRuntime chunk = pair.Value;
            if (chunk == null || chunk.DataStatus != ChunkDataStatus.Ready ||
                chunk.Terrain == null || chunk.Terrain.IsDisposed)
            {
                succeeded = false;
                continue;
            }

            if (!TryEnsureRuntimeChunkBaseline(key, chunk, out RuntimeChunkBaseline baseline))
            {
                succeeded = false;
                continue;
            }

            // 采集前撤标记；采集期间新变化会重新标脏，失败则恢复标记。
            runtimeTerrainDirtyChunks.Remove(key);
            try
            {
                RefreshRuntimeChunkDelta(key, planetName, pair.Key, chunk, baseline);
                if (runtimeTerrainDirtyChunks.Contains(key)) succeeded = false;
            }
            catch (Exception exception)
            {
                runtimeTerrainDirtyChunks.Add(key);
                succeeded = false;
                Debug.LogError($"[SaveDataMgr] 采集世界地形失败：{key}，{exception}");
            }
        }
        return succeeded;
    }

    /// <summary>为当前运行时区块建立完整地形与互斥的建筑损伤、地表采挖进度基线。</summary>
    private bool TryEnsureRuntimeChunkBaseline(
        string key,
        ChunkRuntime chunk,
        out RuntimeChunkBaseline baseline)
    {
        baseline = null;
        if (chunk?.Terrain == null || chunk.Terrain.IsDisposed)
            return false;

        if (runtimeChunkBaselines.TryGetValue(key, out RuntimeChunkBaseline existing) &&
            existing.ChunkReference.IsAlive &&
            ReferenceEquals(existing.ChunkReference.Target, chunk))
        {
            baseline = existing;
            return true;
        }

        ChunkTerrainData terrain = chunk.Terrain;
        TerrainCell[] cells = new TerrainCell[terrain.CellCount];
        for (int x = 0; x < terrain.Width; x++)
        {
            for (int y = 0; y < terrain.Height; y++)
                cells[y * terrain.Width + x] = terrain.GetCell(x, y);
        }

        if (!terrain.TryCopyEnvironmentLayer(
                TileBuildingSystem.RuntimeDamageLayerId,
                out float[] accumulatedDamage))
        {
            accumulatedDamage = Array.Empty<float>();
        }

        baseline = new RuntimeChunkBaseline(
            chunk,
            terrain.Width,
            terrain.Height,
            cells,
            accumulatedDamage);
        runtimeChunkBaselines[key] = baseline;
        return true;
    }

    /// <summary>更新一格完整地形及其累计工作量的最小差量。</summary>
    private void SetRuntimeTileDelta(
        string key,
        string planetName,
        RuntimeWorldAddress address,
        Vector2Int localPosition,
        TerrainCell currentCell,
        float accumulatedDamage,
        TerrainCell baselineCell,
        float baselineAccumulatedDamage)
    {
        if (!chunkDeltas.TryGetValue(key, out ChunkSaveRecord delta) || delta == null)
            delta = CreateRuntimeChunkDelta(planetName, address);

        delta.RuntimeTileDeltas ??= new List<RuntimeTileCellSaveDelta>();
        delta.RuntimeTileDeltas.RemoveAll(cell => cell.LocalPosition == localPosition);
        if (!currentCell.Equals(baselineCell) ||
            !Mathf.Approximately(accumulatedDamage, baselineAccumulatedDamage))
        {
            delta.RuntimeTileDeltas.Add(RuntimeTileCellSaveDelta.Capture(
                localPosition, currentCell, accumulatedDamage));
            delta.RuntimeTileDeltas.Sort(CompareRuntimeTileDelta);
            chunkDeltas[key] = delta;
            return;
        }

        if (!delta.HasChanges)
            chunkDeltas.Remove(key);
    }

    /// <summary>保存前按完整区块状态重建运行时地形差量。</summary>
    private void RefreshRuntimeChunkDelta(
        string key,
        string planetName,
        RuntimeWorldAddress address,
        ChunkRuntime chunk,
        RuntimeChunkBaseline baseline)
    {
        ChunkTerrainData terrain = chunk.Terrain;
        chunkDeltas.TryGetValue(key, out ChunkSaveRecord delta);

        List<RuntimeTileCellSaveDelta> changes = new List<RuntimeTileCellSaveDelta>();
        for (int x = 0; x < terrain.Width; x++)
        {
            for (int y = 0; y < terrain.Height; y++)
            {
                TerrainCell currentCell = terrain.GetCell(x, y);
                float currentAccumulatedDamage = ReadRuntimeBuildingDamage(terrain, x, y);
                if (currentCell.Equals(baseline.GetCell(x, y)) &&
                    Mathf.Approximately(
                        currentAccumulatedDamage,
                        baseline.GetAccumulatedDamage(x, y)))
                {
                    continue;
                }

                changes.Add(RuntimeTileCellSaveDelta.Capture(
                    new Vector2Int(x, y), currentCell, currentAccumulatedDamage));
            }
        }

        if (changes.Count == 0)
        {
            delta?.RuntimeTileDeltas?.Clear();
            if (delta != null && !delta.HasChanges)
                chunkDeltas.Remove(key);
            return;
        }

        delta ??= CreateRuntimeChunkDelta(planetName, address);
        delta.RuntimeTileDeltas = changes;
        chunkDeltas[key] = delta;
    }

    /// <summary>读取一格建筑损伤或裸露资源采挖进度；两者在同一格互斥。</summary>
    private static float ReadRuntimeBuildingDamage(ChunkTerrainData terrain, int x, int y)
    {
        return terrain != null &&
               terrain.TryGetEnvironmentValue(
                   TileBuildingSystem.RuntimeDamageLayerId,
                   x,
                   y,
                   out float accumulatedDamage)
            ? Mathf.Max(0f, accumulatedDamage)
            : 0f;
    }

    /// <summary>恢复一格累计工作量，默认零值不额外创建整张环境层数组。</summary>
    private static void WriteRuntimeBuildingDamage(
        ChunkTerrainData terrain,
        int x,
        int y,
        float accumulatedDamage)
    {
        if (terrain == null)
            return;

        float safeDamage = Mathf.Max(0f, accumulatedDamage);
        bool hasLayer = terrain.TryGetEnvironmentValue(
            TileBuildingSystem.RuntimeDamageLayerId,
            x,
            y,
            out float currentDamage);
        if ((!hasLayer && Mathf.Approximately(safeDamage, 0f)) ||
            (hasLayer && Mathf.Approximately(currentDamage, safeDamage)))
        {
            return;
        }

        terrain.SetEnvironmentValue(
            TileBuildingSystem.RuntimeDamageLayerId,
            x,
            y,
            safeDamage);
    }

    private static ChunkSaveRecord CreateRuntimeChunkDelta(
        string planetName,
        RuntimeWorldAddress address)
    {
        return new ChunkSaveRecord
        {
            PlanetName = planetName,
            ChunkName = ToChunkName(address),
            ChunkPosition = new Vector2Int(address.ChunkOrigin.X, address.ChunkOrigin.Y)
        };
    }

    private static int CompareRuntimeTileDelta(
        RuntimeTileCellSaveDelta left,
        RuntimeTileCellSaveDelta right)
    {
        int x = left.LocalPosition.x.CompareTo(right.LocalPosition.x);
        return x != 0 ? x : left.LocalPosition.y.CompareTo(right.LocalPosition.y);
    }

    private static string ResolveRuntimePlanetName()
    {
        string worldKey = GameplayWorldRegistry.ExecutionWorldKey;
        return string.IsNullOrWhiteSpace(worldKey) ? "world" : worldKey;
    }

    #endregion

    public void ResetChunkDifferenceState()
    {
        chunkBaselines.Clear();
        chunkDeltas.Clear();
        runtimeChunkBaselines.Clear();
        runtimeTerrainDirtyChunks.Clear();
        runtimeBuildingDirtyChunks.Clear();
        runtimeBuildingDirtyAddresses.Clear();
        unloadedRuntimeBuildingGuids.Clear();
        buildingUnloadFailures.Clear();
        restoredRuntimeAiChunks.Clear();
        ClearParkedRuntimeAi();
        restoredRuntimeAiWorld = null;
        restoredRuntimeAiEpoch = long.MinValue;
        restoredRuntimeBuildingChunks.Clear();
        restoredRuntimeBuildingWorld = null;
        restoredRuntimeBuildingEpoch = long.MinValue;
    }

    private void PrepareLoadedChunksForSave()
    {
        using var captureSample = SaveCapturePoint.Measure();
        SaveCapturePoint.SetGauge(chunkDeltas.Count);
        WeatherMgr.FlushAllSurfaceWork();
        DroppedItemService.PrepareForSave();
        if (!CaptureRuntimeTerrainStates())
            throw new InvalidOperationException("当前世界的地形快照采集失败，无法安全保存。");
        // 新 WorldModel 的自然物挂在 ChunkView，不属于旧 Chunk.RunTimeItems；先捕获其状态。
        ChunkMgr.Instance?.CaptureRuntimeNaturalItemStates();
        if (ChunkMgr.Instance != null && ChunkMgr.Instance.Chunk_Dic_ByPos != null)
        {
            List<Chunk> chunks = new List<Chunk>(ChunkMgr.Instance.Chunk_Dic_ByPos.Values);
            for (int i = 0; i < chunks.Count; i++)
            {
                Chunk chunk = chunks[i];
                if (chunk != null)
                {
                    chunk.SaveChunk();
                    SaveCapturePoint.AddWork(1);
                }
            }
        }

        // 旧 Chunk 写入结束后再合并新版建筑和 AI，避免旧差量记录覆盖新版实体快照。
        CaptureRuntimeBuildingStates();
        CaptureRuntimeAiEntityStates();
    }

    /// <summary>自动保存将自然物、旧 Chunk、差异扫描和 AI 快照按单帧预算分散执行。</summary>
    private IEnumerator PrepareLoadedChunksForAutoSaveCoroutine(Action<Exception> onFailure, AutoSaveWorkBudget budget)
    {
        DroppedItemService.PrepareForSave();
        yield return budget.WaitForNextSlice();
        IEnumerator terrainRoutine = CaptureRuntimeTerrainStatesCoroutine(budget);
        while (terrainRoutine.MoveNext())
            yield return terrainRoutine.Current;
        yield return budget.WaitForNextSlice();
        if (ChunkMgr.Instance != null)
        {
            // 自然物也要分帧克隆，避免表现窗口内大量 Item 在同一帧序列化。
            Exception naturalFailure = null;
            IEnumerator naturalRoutine = ForwardAutoSaveRoutine(
                ChunkMgr.Instance.CaptureRuntimeNaturalItemStatesCoroutine(budget),
                exception => naturalFailure = exception);
            while (naturalRoutine.MoveNext())
                yield return naturalRoutine.Current;
            if (naturalFailure != null)
            {
                onFailure?.Invoke(naturalFailure);
                yield break;
            }
        }

        if (ChunkMgr.Instance != null && ChunkMgr.Instance.Chunk_Dic_ByPos != null)
        {
            List<Chunk> chunks = new List<Chunk>(ChunkMgr.Instance.Chunk_Dic_ByPos.Values);
            for (int i = 0; i < chunks.Count; i++)
            {
                Chunk chunk = chunks[i];
                if (chunk != null)
                {
                    Exception chunkSaveFailure = null;
                    IEnumerator chunkRoutine = ForwardAutoSaveRoutine(
                        chunk.SaveChunkCoroutine(exception => chunkSaveFailure = exception, budget),
                        exception =>
                        {
                            if (chunkSaveFailure == null)
                                chunkSaveFailure = exception;
                        });
                    while (chunkRoutine.MoveNext())
                        yield return chunkRoutine.Current;

                    if (chunkSaveFailure != null)
                    {
                        onFailure?.Invoke(chunkSaveFailure);
                        yield break;
                    }
                }

                // 即使当前 Chunk 很小，也让区块之间错开一帧，给输入与物理留出更新机会。
                yield return budget.WaitForNextSlice();
            }
        }

        // 旧 Chunk 写入结束后再合并新版建筑和 AI，避免旧差量记录覆盖新版实体快照。
        IEnumerator buildingRoutine = CaptureRuntimeBuildingStatesCoroutine(budget);
        while (buildingRoutine.MoveNext())
            yield return buildingRoutine.Current;
        yield return budget.WaitForNextSlice();
        Exception aiFailure = null;
        IEnumerator aiRoutine = ForwardAutoSaveRoutine(
            CaptureRuntimeAiEntityStatesCoroutine(budget),
            exception => aiFailure = exception);
        while (aiRoutine.MoveNext())
            yield return aiRoutine.Current;
        if (aiFailure != null)
            onFailure?.Invoke(aiFailure);
    }

    /// <summary>安全转发自动保存子协程，避免异常中断后让保存状态永久停留。</summary>
    private static IEnumerator ForwardAutoSaveRoutine(
        IEnumerator routine,
        Action<Exception> onFailure)
    {
        if (routine == null)
            yield break;

        try
        {
            while (true)
            {
                bool moved = false;
                Exception failure = null;
                try
                {
                    moved = routine.MoveNext();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                if (failure != null)
                {
                    onFailure?.Invoke(failure);
                    yield break;
                }

                if (!moved)
                    yield break;

                yield return routine.Current;
            }
        }
        finally { (routine as IDisposable)?.Dispose(); }
    }

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

        foreach (Item item in ItemMgr.Instance.WorldRunTimeItems.Values)
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

    #region Runtime AI persistence

    /// <summary>从 ItemMgr 的 WorldAddress 索引采集 AI，并写入当前区块差量。</summary>
    private bool CaptureRuntimeAiEntityStates(List<RuntimeAiSceneUnbindingCapture> unbindingCaptures = null)
    {
        ItemMgr itemManager = ItemMgr.Instance;
        if (itemManager == null || SaveData == null)
            return false;

        string planetName = GameplayWorldRegistry.ExecutionWorldKey;
        if (string.IsNullOrWhiteSpace(planetName)) return false;
        var addresses = new HashSet<RuntimeWorldAddress>();
        var capturedAddresses = new HashSet<RuntimeWorldAddress>();
        var runtimeItems = new List<Item>();
        var savedItems = new List<ItemData>();
        var addressCaptures = unbindingCaptures != null ? new List<RuntimeAiSceneUnbindingCapture>() : null;
        bool allSucceeded = true;
        itemManager.CopyRuntimeAiSaveAddresses(addresses);

        foreach (RuntimeWorldAddress address in addresses)
        {
            itemManager.CopyRuntimeAiItems(address, runtimeItems);
            savedItems.Clear();
            addressCaptures?.Clear();
            bool captureSucceeded = true;
            for (int i = 0; i < runtimeItems.Count; i++)
            {
                Item item = runtimeItems[i];
                try
                {
                    item.Save();
                    ItemData snapshot = CloneItemData(item.itemData);
                    if (snapshot?.transform == null)
                        throw new InvalidDataException($"生物 {item.name} 的快照缺少位置。");
                    savedItems.Add(snapshot);
                    addressCaptures?.Add(new RuntimeAiSceneUnbindingCapture(item, snapshot, address));
                }
                catch (Exception exception)
                {
                    captureSucceeded = false;
                    Debug.LogError($"[SaveDataMgr] 保存运行时 AI 失败：{item.name}", item);
                    Debug.LogException(exception);
                    break;
                }
            }

            if (!captureSucceeded)
            {
                allSucceeded = false;
                continue;
            }

            MergeRuntimeAiItems(planetName, address, savedItems);
            capturedAddresses.Add(address);
            if (addressCaptures != null) unbindingCaptures.AddRange(addressCaptures);
        }

        itemManager.AcknowledgeRuntimeAiSave(capturedAddresses);
        return allSucceeded;
    }

    /// <summary>自动保存专用的 AI 分帧快照；每帧限制 Item.Save 与数据克隆的累计耗时。</summary>
    private IEnumerator CaptureRuntimeAiEntityStatesCoroutine(AutoSaveWorkBudget budget)
    {
        ItemMgr itemManager = ItemMgr.Instance;
        if (itemManager == null || SaveData == null)
            yield break;

        string planetName = GameplayWorldRegistry.ExecutionWorldKey;
        var addresses = new HashSet<RuntimeWorldAddress>();
        bool copiedAddresses = true;
        try
        {
            itemManager.CopyRuntimeAiSaveAddresses(addresses);
        }
        catch (Exception exception)
        {
            copiedAddresses = false;
            Debug.LogError("[SaveDataMgr] 复制运行时 AI 地址失败");
            Debug.LogException(exception);
        }

        if (!copiedAddresses)
            yield break;

        List<RuntimeWorldAddress> addressSnapshot = new List<RuntimeWorldAddress>(addresses);
        var capturedAddresses = new HashSet<RuntimeWorldAddress>();
        var runtimeItems = new List<Item>();
        var savedItems = new List<ItemData>();

        for (int addressIndex = 0; addressIndex < addressSnapshot.Count; addressIndex++)
        {
            RuntimeWorldAddress address = addressSnapshot[addressIndex];
            bool copiedItems = true;
            try
            {
                itemManager.CopyRuntimeAiItems(address, runtimeItems);
            }
            catch (Exception exception)
            {
                copiedItems = false;
                Debug.LogError($"[SaveDataMgr] 复制运行时 AI 失败：{address}");
                Debug.LogException(exception);
            }

            if (!copiedItems)
                continue;

            savedItems.Clear();
            bool captureSucceeded = true;
            for (int itemIndex = 0; itemIndex < runtimeItems.Count; itemIndex++)
            {
                Item item = runtimeItems[itemIndex];
                Exception itemFailure = null;
                try
                {
                    item.Save();
                    savedItems.Add(CloneItemData(item.itemData));
                }
                catch (Exception exception)
                {
                    itemFailure = exception;
                    Debug.LogError($"[SaveDataMgr] 分帧保存运行时 AI 失败：{item.name}", item);
                    Debug.LogException(exception);
                }

                if (itemFailure != null)
                {
                    captureSucceeded = false;
                    break;
                }

                if (budget.ShouldYield())
                    yield return budget.WaitForNextSlice();
            }

            if (captureSucceeded)
            {
                MergeRuntimeAiItems(planetName, address, savedItems);
                capturedAddresses.Add(address);
            }

            if (budget.ShouldYield())
                yield return budget.WaitForNextSlice();
        }

        itemManager.AcknowledgeRuntimeAiSave(capturedAddresses);
    }

    /// <summary>在新版区块数据就绪时恢复该地址的 AI，不再等待旧 Chunk 实例化。</summary>
    public bool RestoreRuntimeAiEntitiesForChunk(RuntimeWorldAddress address)
    {
        if (!GameNetwork.HasStateAuthority) return true;
        if (SaveData == null || ItemMgr.Instance == null) return false;

        EnsureRuntimeAiRestoreScope();
        string planetName = GameplayWorldRegistry.ExecutionWorldKey;
        if (string.IsNullOrWhiteSpace(planetName)) return false;
        string chunkName = ToChunkName(address);
        string restoreKey = $"{BuildChunkKey(planetName, chunkName)}\u001f{address.DimensionId}";
        if (!restoredRuntimeAiChunks.Add(restoreKey))
            return true;

        var candidates = new List<ItemData>();
        string chunkKey = BuildChunkKey(planetName, chunkName);
        if (chunkDeltas.TryGetValue(chunkKey, out ChunkSaveRecord delta))
            CollectRuntimeAiData(delta?.ChangedItems, candidates);

        var restoredGuids = new HashSet<int>();
        bool succeeded = true;
        for (int i = 0; i < candidates.Count; i++)
        {
            ItemData savedData = candidates[i];
            if (savedData == null || !restoredGuids.Add(savedData.Guid))
                continue;

            Item existing = ItemMgr.Instance.GetItemByGuid(savedData.Guid);
            if (existing != null)
            {
                if (RuntimeAiEntityUtility.IsAiEntity(existing) &&
                    GameplayWorldRegistry.GetItemWorldKey(existing) == planetName)
                {
                    try
                    {
                        if (!existing.IsInitialized) { succeeded = false; continue; }
                        ItemWorldPlacement.AttachRuntimeAi(existing, existing.gameObject);
                        if (ParkedWorld.SceneDetachedLiveGuids.Contains(savedData.Guid))
                            RemoveParkedRuntimeAi(savedData.Guid);
                    }
                    catch (Exception exception) { succeeded = false; Debug.LogException(exception); }
                }
                else succeeded = false;
                continue;
            }

            // 运行中进入新区块时先登记休眠快照，由生态调度器在镜头外分帧恢复。
            if (!ParkedWorld.SceneDetachedLiveGuids.Contains(savedData.Guid) &&
                GameManager.Instance != null && GameManager.Instance.IsGameplayReady &&
                MonsterManager.IsRegisteredSpeciesId(savedData.IDName))
            {
                try { AddParkedRuntimeAi(address, CloneItemData(savedData)); }
                catch (Exception exception) { succeeded = false; Debug.LogException(exception); }
                continue;
            }

            Item restored = null;
            try
            {
                ItemData runtimeData = CloneAndRebaseItemData(savedData);
                ItemTransform transformData = runtimeData.transform ?? new ItemTransform();
                restored = ItemMgr.Instance.InstantiateItem(
                    runtimeData,
                    transformData.position,
                    transformData.rotation,
                    transformData.scale);
                if (restored != null)
                {
                    restored.Load();
                    if (ParkedWorld.SceneDetachedLiveGuids.Contains(savedData.Guid))
                        RemoveParkedRuntimeAi(savedData.Guid);
                }
                else succeeded = false;
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
                    $"[SaveDataMgr] 恢复运行时 AI 失败：{savedData.IDName}, Guid={savedData.Guid}");
                Debug.LogException(exception);
            }
        }
        if (!succeeded) restoredRuntimeAiChunks.Remove(restoreKey);
        return succeeded;
    }

    private void MergeRuntimeAiItems(
        string planetName,
        RuntimeWorldAddress address,
        List<ItemData> savedItems)
    {
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
        record.ChangedItems.RemoveAll(RuntimeAiEntityUtility.IsAiData);
        // 休眠实体已经卸载 GameObject，保存活动实体时仍须保留同一区块中的休眠快照。
        if (parkedRuntimeAiByAddress.TryGetValue(address, out List<ItemData> parkedItems))
            for (int i = 0; i < parkedItems.Count; i++)
                record.ChangedItems.Add(parkedItems[i]);
        for (int i = 0; i < savedItems.Count; i++)
            record.ChangedItems.Add(savedItems[i]);
        record.ChangedItems.Sort((left, right) => (left?.Guid ?? 0).CompareTo(right?.Guid ?? 0));

        if (record.HasChanges)
            chunkDeltas[key] = record;
        else
            chunkDeltas.Remove(key);
    }

    private static void CollectRuntimeAiData(IEnumerable<ItemData> source, List<ItemData> output)
    {
        if (source == null)
            return;
        foreach (ItemData data in source)
        {
            if (RuntimeAiEntityUtility.IsAiData(data))
                output.Add(data);
        }
    }

    private void EnsureRuntimeAiRestoreScope()
    {
        WorldRuntime world = ChunkMgr.ExistingInstance?.WorldRuntime;
        long epoch = world?.Epoch ?? long.MinValue;
        if (ReferenceEquals(restoredRuntimeAiWorld, world) && restoredRuntimeAiEpoch == epoch)
            return;

        restoredRuntimeAiChunks.Clear();
        ClearParkedRuntimeAi();
        restoredRuntimeAiWorld = world;
        restoredRuntimeAiEpoch = epoch;
    }

    private static string ToChunkName(RuntimeWorldAddress address)
    {
        return new Vector2Int(address.ChunkOrigin.X, address.ChunkOrigin.Y).ToString();
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

    private byte[] BuildCompactSavePayload(GameSaveData saveData)
    {
        using var encodeSample = SaveEncodePoint.Measure();
        ModRuntimeManager modRuntime = ModRuntimeManager.Instance;
        if (modRuntime == null || !modRuntime.IsReady)
            throw new InvalidOperationException("MOD 框架尚未完成加载，无法保存世界");

        CompactSaveEnvelope corePayload = BuildCoreCompactSavePayload(saveData);
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
        SaveEncodePoint.AddWork(payload.LongLength);
        SaveEncodePoint.SetGauge(payload.LongLength);
        return payload;
    }

    private CompactSaveEnvelope BuildCoreCompactSavePayload(GameSaveData saveData)
    {

        MonsterSpawnerManager.CaptureAll(saveData);

        // 先捕获所有移动作用域，再序列化核心数据，避免船体和设备库存落在不同时间点。
        byte[] machines, coreSaveData, droppedItems;
        using (SaveCoreCapturePoint.Measure())
        {
            using (SaveMachinesCapturePoint.Measure()) machines = MachineWorld.CaptureArchive(saveData);
            using (SaveSpaceCapturePoint.Measure()) FlatWorld.Spaceflight.SpaceSession.Capture(saveData);
            coreSaveData = SerializeCoreDataWithoutChunks(saveData);
            using (SaveDropsCapturePoint.Measure()) droppedItems = DroppedItemService.CaptureArchive(saveData);
            SaveCoreCapturePoint.AddWork((machines?.LongLength ?? 0) + (coreSaveData?.LongLength ?? 0) + (droppedItems?.LongLength ?? 0));
        }
        List<ChunkSaveRecord> records = new();

        foreach (KeyValuePair<string, ChunkSaveRecord> pair in chunkDeltas)
        {
            if (pair.Value == null || !pair.Value.HasChanges)
                continue;

            records.Add(pair.Value);
        }

        records.Sort(CompareChunkSaveRecords);
        List<byte[]> chunkRecords = new(records.Count);
        using (SaveChunkEncodePoint.Measure(records.Count))
        {
            foreach (ChunkSaveRecord record in records)
                chunkRecords.Add(MemoryPackSerializer.Serialize(record));
            SaveChunkEncodePoint.SetGauge(records.Count);
        }

        List<FrozenEcologyChunkSaveRecord> ecologyRecords = new();
        foreach (int work in FreezeEcologyChunks(saveData, ecologyRecords)) { }
        return BuildCompactSaveEnvelope(coreSaveData, chunkRecords, machines, droppedItems, ecologyRecords);
    }

    internal static byte[] SerializeCoreDataWithoutChunks(GameSaveData saveData)
    {
        using var coreSample = SaveCoreEncodePoint.Measure();
        List<(PlanetData planet, Dictionary<string, MapSave> maps, List<EcologyChunkSaveData> ecology)> backups = new();
        if (saveData.PlanetData_Dict != null)
        {
            foreach (PlanetData planet in saveData.PlanetData_Dict.Values)
            {
                if (planet == null)
                    continue;

                backups.Add((planet, planet.MapData_Dict, planet.Ecology?.Chunks));
                planet.MapData_Dict = new Dictionary<string, MapSave>();
                if (planet.Ecology != null)
                    planet.Ecology.Chunks = null;
            }
        }

        try
        {
            byte[] payload = MemoryPackSerializer.Serialize(saveData);
            SaveCoreEncodePoint.AddWork(payload.LongLength);
            SaveCoreEncodePoint.SetGauge(payload.LongLength);
            return payload;
        }
        finally
        {
            for (int i = 0; i < backups.Count; i++)
            {
                backups[i].planet.MapData_Dict = backups[i].maps;
                if (backups[i].planet.Ecology != null)
                    backups[i].planet.Ecology.Chunks = backups[i].ecology;
            }
        }
    }

    private GameSaveData DeserializeSavePayload(byte[] payload)
    {
        ModdedSaveEnvelope envelope = ReadSaveEnvelope(payload);
        ModSaveMetadata metadata = envelope.ModMetadata?.Length > 0
            ? MemoryPackSerializer.Deserialize<ModSaveMetadata>(envelope.ModMetadata) : null;
        var restoredChunkDeltas = new Dictionary<string, ChunkSaveRecord>();
        GameSaveData saveData = DeserializeCoreSavePayload(envelope.CoreSavePayload, restoredChunkDeltas);
        ValidateAndRestoreModMetadata(metadata);
        // 整份文件验证完才替换差量，失败候选不能清空或留下半份区块记录。
        ResetChunkDifferenceState();
        foreach (var entry in restoredChunkDeltas)
            chunkDeltas.Add(entry.Key, entry.Value);
        return saveData;
    }

    private static ModdedSaveEnvelope ReadSaveEnvelope(byte[] payload)
    {
        if (!HasSaveHeader(payload, ModdedSaveMagic))
        {
            throw new SaveVersionIncompatibleException(
                "存档封装与当前 Item 实例状态格式不兼容，请创建新世界。不会迁移、覆盖或删除该存档。");
        }
        ModdedSaveEnvelope envelope = MemoryPackSerializer.Deserialize<ModdedSaveEnvelope>(
            payload.AsSpan(ModdedSaveMagic.Length));
        if (envelope?.CoreSavePayload == null)
            throw new InvalidDataException("MOD 存档封装已损坏");
        EnsureCurrentSaveVersion("MOD 存档", envelope.Version, ModdedSaveVersion);
        return envelope;
    }

    private GameSaveData DeserializeCoreSavePayload(CompactSaveEnvelope envelope,
        Dictionary<string, ChunkSaveRecord> restoredChunkDeltas)
    {
        if (envelope == null || envelope.CoreSaveData == null)
            throw new InvalidDataException("差异存档封装已损坏");
        EnsureCurrentSaveVersion("差异存档", envelope.Version, CompactSaveVersion);

        GameSaveData saveData = MemoryPackSerializer.Deserialize<GameSaveData>(envelope.CoreSaveData);
        if (saveData == null)
            throw new InvalidDataException("差异存档的核心数据为空");

        if (!string.IsNullOrWhiteSpace(saveData.SpaceStateJson))
            _ = DeserializeSpaceState(saveData.SpaceStateJson);

        RestoreEcologyChunks(saveData, envelope.EcologyRecords);
        DroppedItemService.RestoreArchive(saveData, envelope.DroppedItems);
        MachineWorld.RestoreArchive(saveData, envelope.MechanicalNetworks);

        if (envelope.ChunkRecords == null)
            return saveData;

        for (int i = 0; i < envelope.ChunkRecords.Count; i++)
        {
            byte[] chunkPayload = envelope.ChunkRecords[i];
            if (chunkPayload == null || chunkPayload.Length == 0)
                throw new InvalidDataException("差异存档的区块快照为空");
            ChunkSaveRecord record = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(chunkPayload);
            if (record == null || string.IsNullOrEmpty(record.PlanetName) || string.IsNullOrEmpty(record.ChunkName))
                continue;

            restoredChunkDeltas[BuildChunkKey(record.PlanetName, record.ChunkName)] = record;
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

    #region 星区快照的严格格式边界
    // 先验证真实 JSON 版本，字段默认值不能把缺少版本的档案伪装成新格式。
    public static FlatWorld.Spaceflight.SpaceSessionState DeserializeSpaceState(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("太空存档内容为空。");
        Newtonsoft.Json.Linq.JObject document = Newtonsoft.Json.Linq.JObject.Parse(json);
        Newtonsoft.Json.Linq.JToken version = document[nameof(FlatWorld.Spaceflight.SpaceSessionState.Version)];
        if (version?.Type != Newtonsoft.Json.Linq.JTokenType.Integer)
            throw new InvalidDataException("太空存档缺少有效的格式版本。");
        EnsureCurrentSaveVersion("太空星区存档", version.ToObject<int>(), 2);
        var state = document.ToObject<FlatWorld.Spaceflight.SpaceSessionState>();
        if (state == null || state.Sectors == null || state.Sectors.Count == 0 || state.Ships == null ||
            state.Flights == null || state.Devices == null || state.Passengers == null || state.Navigation == null ||
            state.Docking == null || state.LooseItems == null || state.GroundAtmospheres == null ||
            !FlatWorld.Spaceflight.SpaceVector2.Finite(state.SimulationSeconds) || state.SimulationSeconds < 0d)
            throw new InvalidDataException("太空星区存档的权威状态不完整。");
        var sectors = new HashSet<FlatWorld.Spaceflight.SpaceSectorId>();
        foreach (FlatWorld.Spaceflight.UniverseState sector in state.Sectors)
            if (sector == null || !sectors.Add(sector.Sector) || sector.Bodies == null)
                throw new InvalidDataException("太空存档存在无效或重复的星区身份。");
        return state;
    }
    #endregion

    /// <summary>只读取当前存档格式；开发阶段不保留旧版本迁移或自动升级路径。</summary>
    private static void EnsureCurrentSaveVersion(string label, int savedVersion, int currentVersion)
    {
        if (savedVersion <= 0)
            throw new InvalidDataException($"{label}版本无效：{savedVersion}");

        if (savedVersion != currentVersion)
            throw new SaveVersionIncompatibleException(
                $"{label}版本不兼容：存档={savedVersion}，当前={currentVersion}。请创建新世界。");
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

    /// <summary>恢复运行时实体前先复制存档状态，再用当前物品定义刷新静态配置和模块组合。</summary>
    private static ItemData CloneAndRebaseItemData(ItemData itemData)
    {
        ItemData cloned = CloneItemData(itemData);
        GameRes gameRes = GameRes.Instance;
        return gameRes == null
            ? cloned
            : ItemDefinitionRuntime.RebasePersistedData(gameRes, cloned);
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

    /// <summary>新版区块的程序化阻挡层基线；用弱引用避免玩家探索过多区块后长期持有运行时对象。</summary>
    private sealed class RuntimeChunkBaseline
    {
        /// <summary>复制程序化区块基线，避免后续运行时修改污染比较源。</summary>
        public RuntimeChunkBaseline(
            ChunkRuntime chunk,
            int width,
            int height,
            TerrainCell[] cells,
            float[] accumulatedDamage)
        {
            ChunkReference = new WeakReference(chunk);
            Width = width;
            Height = height;
            Cells = cells;
            AccumulatedDamage = accumulatedDamage ?? Array.Empty<float>();
        }

        public WeakReference ChunkReference { get; }
        /// <summary>同一份区块数据的存档差量只恢复一次，防止缓存区块往返视野时覆盖运行中改动。</summary>
        public bool PersistenceRestored { get; set; }
        public int Width { get; }
        public int Height { get; }
        public TerrainCell[] Cells { get; }
        /// <summary>程序化基线中每格建筑已经累计的损伤；空数组表示全为零。</summary>
        public float[] AccumulatedDamage { get; }

        /// <summary>读取程序化基线中的完整格子属性。</summary>
        public TerrainCell GetCell(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException(nameof(x));
            return Cells[y * Width + x];
        }

        /// <summary>读取程序化基线中一格建筑的累计损伤。</summary>
        public float GetAccumulatedDamage(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                return 0f;
            int index = y * Width + x;
            return (uint)index < (uint)AccumulatedDamage.Length
                ? AccumulatedDamage[index]
                : 0f;
        }
    }

    #endregion
    
    #endregion
}

[MemoryPackable]
[Serializable]
public partial class ModdedSaveEnvelope
{
    public int Version;
    public CompactSaveEnvelope CoreSavePayload; // 单次遍历冻结字节，避免多层大数组封装复制。
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
    public List<byte[]> ChunkRecords = new(); // 区块在主线程独立冻结，后台封装不再回调物品模块。
    public byte[] DroppedItems;
    public byte[] MechanicalNetworks; // 追加独立版本载荷，旧版本缺失时为空。
    public List<FrozenEcologyChunkSaveRecord> EcologyRecords = new();
}

[MemoryPackable]
[Serializable]
public partial class ChunkSaveRecord
{
    public string PlanetName;
    public string ChunkName;
    public Vector2Int ChunkPosition;
    public float SunlightIntensity;
    public List<ItemData> ChangedItems = new();
    public List<int> RemovedItemGuids = new();
    public List<TileCellSaveDelta> TileDeltas = new();
    public List<GrassCellSaveDelta> GrassDeltas = new();
    // 当前 WorldModel 的完整核心地形差量，包含地表铺设和阻挡墙。
    public List<RuntimeTileCellSaveDelta> RuntimeTileDeltas = new();
    public List<SupportCellSaveData> SupportCells = new(); // 独立支撑面
    public List<AgricultureCellSaveData> AgricultureCells = new(); // 独立农业状态
    public List<ContaminationCellSaveData> ContaminationCells = new(); // 独立污染状态
    public List<LiquidCellSaveData> LiquidCells = new(); // 只保存玩家改变的液体格，与 Ground 差量独立。
    public List<GroundLayerCellSaveData> GroundLayerCells = new(); // 追加伪 Z 轴差量，旧记录缺失时按未采挖处理。
    public List<SnowCellSaveData> SnowCells = new(); // 独立保存天气雪与玩家编辑雪，不替换底层地形。

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
         (SnowCells?.Count ?? 0) > 0);
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

[MemoryPackable]
[Serializable]
public partial class RuntimeTileCellSaveDelta
{
    public Vector2Int LocalPosition;
    public int GroundTileId;
    public int BackTileId;
    public int BlockingTileId;
    public int BiomeId;
    public short NavigationCost;
    public TerrainCellFlags Flags;
    // 0 表示该格建筑满耐久或当前没有阻挡建筑。
    public float AccumulatedDamage;

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
