using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FlatWorld.Diagnostics;
using UnityEngine;
using Sirenix.OdinInspector;

/// <summary>保存服务的生命周期、公开保存入口与世界采集协调。</summary>
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

    #region 已有单例查询
    /// <summary>诊断读取现有保存服务，不为查询额外创建单例对象。</summary>
    public static SaveDataMgr GetExistingInstance() => instance;
    #endregion

    #region 当前世界资料查询
    public PlanetData GetCurrentPlanetData()
        => TryGetActivePlanetData(out PlanetData planetData) ? planetData : null;
    #endregion

    #region 保存入口与世界采集协调
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
    #endregion
}
