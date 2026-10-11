using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>专用测试入口复用正式建世界流程，测试存档和报告统一写入隔离目录。</summary>
[DefaultExecutionOrder(-31999)]
[DisallowMultipleComponent]
public sealed class GameplayTestScene : MonoBehaviour
{
    #region 场景配置
    [SerializeField] private string luaPath = "GameplayTests/enter_showroom.lua";
    [SerializeField] private string seed = "flatworld-showroom-001";
    [SerializeField, Min(1f)] private float resourceTimeoutSeconds = 120f;
    [SerializeField, Min(1f)] private float worldTimeoutSeconds = 120f;
    [SerializeField, Min(1f)] private float totalTimeoutSeconds = 600f;
    [SerializeField, Min(100)] private int maximumLogEntries = 10000;

    public string ReportPath => report?.ReportPath;
    public string Status => report?.Status ?? "not_started";
    #endregion

    #region 运行状态
    private static GameplayTestScene activeRunner;
    private readonly object reportLock = new();
    private readonly System.Diagnostics.Stopwatch clock = new();
    private GameplayTestReport report;
    private GameplayTestShowroom showroom;
    private GameplayTestLuaRunner luaRunner;
    private GameManager manager;
    private SaveDataMgr saveManager;
    private GameplayTestSaveScope saveScope;
    private Player player;
    private Vector3 playerStart;
    private string isolatedSaveDirectory;
    private string worldEntryFailure;
    private volatile string currentStage = "startup";
    private bool finished;
    private bool flowCompleted;
    private bool interruptionRequested;
    private bool worldExited;
    private bool subscribed;
    private bool logCaptureClosed;
    private bool failedStep;
    #endregion

    #region 场景生命周期
    private void Awake()
    {
        if (activeRunner != null && activeRunner != this)
        {
            enabled = false;
            Destroy(gameObject);
            return;
        }
        activeRunner = this;
        DontDestroyOnLoad(gameObject);
        clock.Start();
        string runId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
#if UNITY_EDITOR
        string root = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "FlatWorldGameplayTests");
#else
        string root = Path.Combine(Application.persistentDataPath, "GameplayTests");
#endif
        isolatedSaveDirectory = Path.Combine(root, "Saves", runId);
        report = new GameplayTestReport
        {
            RunId = runId, Scene = gameObject.scene.path, LuaPath = luaPath,
            StartedUtc = DateTime.UtcNow.ToString("O"), SaveDirectory = isolatedSaveDirectory,
            ReportPath = Path.Combine(root, "Reports", runId + ".json")
        };
        Application.logMessageReceivedThreaded += CaptureLog;
        subscribed = true;
    }

    private void Start()
    {
        if (report != null) StartCoroutine(RunGuarded(Run()));
    }

    private void Update()
    {
        showroom?.UpdatePresentation();
        if (report == null || finished) return;
        if (worldExited || clock.Elapsed.TotalSeconds > totalTimeoutSeconds)
        {
            Record("abort", "测试中断", false, worldExited ? "测试期间退出了世界。" : "测试达到总时间上限。");
            interruptionRequested = true;
            StopAllCoroutines();
            Finish(true);
        }
    }

    private void OnDisable()
    {
        if (report == null) return;
        if (!finished)
        {
            Record("abort", "测试中断", false, "测试组件停用或 Play Mode 已停止。");
            interruptionRequested = true;
            StopAllCoroutines();
            Finish(true);
        }
    }

    private void OnApplicationQuit()
    {
        if (report != null && !finished)
        {
            Record("abort", "测试中断", false, "应用关闭或 Play Mode 已停止。");
            interruptionRequested = true;
            Finish(true);
        }
    }

    private void OnDestroy()
    {
        if (activeRunner == this) activeRunner = null;
        UnsubscribeLogs();
        if (manager != null)
        {
            manager.WorldEntryProgressChanged -= OnWorldEntryProgress;
            manager.Event_GameWorldExit -= OnWorldExit;
            manager.BackToHelloScene_Event_End -= OnReturnedToMenu;
        }
        luaRunner?.Dispose();
        showroom?.Dispose();
        saveScope?.Dispose();
    }
    #endregion

    #region 启动与世界流程
    private IEnumerator Run()
    {
        Stage("resources");
        float deadline = Time.realtimeSinceStartup + resourceTimeoutSeconds;
        while (GameRes.ExistingInstance == null || !GameRes.ExistingInstance.isLoadFinish)
        {
            GameRes resources = GameRes.ExistingInstance;
            if (resources != null && resources.LoadState == ResourceLoadState.Failed)
                throw new InvalidOperationException(resources.LastLoadError);
            if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException("等待游戏资源加载超时。");
            yield return null;
        }
        Record("resources", "完整资源就绪", true, GameRes.ExistingInstance.LoadStatusText);

        manager = GameManager.Instance;
        if (manager.IsInGameWorld || manager.IsWorldEntryInProgress)
            throw new InvalidOperationException("测试场景需要独立启动，当前已有正在运行或进入中的世界。");
        manager.WorldEntryProgressChanged += OnWorldEntryProgress;
        manager.Event_GameWorldExit += OnWorldExit;
        manager.BackToHelloScene_Event_End += OnReturnedToMenu;
        saveManager = SaveDataMgr.Instance;
        Directory.CreateDirectory(isolatedSaveDirectory);
        saveScope = new GameplayTestSaveScope(saveManager, manager, isolatedSaveDirectory);

        Stage("create_world");
        PlanetData planet = FastCloner.FastCloner.DeepClone(manager.ReadyPlanetData);
        planet.Name = "测试陈列厅";
        planet.TopologyMode = WorldTopologyMode.Infinite;
        showroom = new GameplayTestShowroom();
        showroom.Configure(planet);
        var request = new NewWorldCreationRequest("陈列厅_" + report.RunId, "测试玩家", seed,
            planet, manager.ReadyTimeData);
        if (!manager.CreateNewWorld(request)) throw new InvalidOperationException("正式建世界入口拒绝了测试请求。");
        saveScope.KeepUntilMenu();
        deadline = Time.realtimeSinceStartup + worldTimeoutSeconds;
        while (!manager.IsInGameWorld || !manager.IsGameplayReady || ItemMgr.Instance.User_Player == null)
        {
            if (!string.IsNullOrEmpty(worldEntryFailure)) throw new InvalidOperationException(worldEntryFailure);
            if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException("等待测试世界和玩家进入超时。");
            yield return null;
        }
        player = ItemMgr.Instance.User_Player;
        playerStart = player.transform.position;
        report.PlayerStart = new GameplayTestPosition(playerStart);
        Record("create_world", "世界和默认玩家就绪", true, planet.Name);

        Stage("showroom");
        yield return showroom.Build(planet, Record);
        CopyShowroomReport();
        Record("showroom", "陈列厅构建完成", showroom.IsComplete && showroom.IsTerrainReady,
            $"已放置 {showroom.PlacedCount}/{showroom.ExpectedCount} 种物品。");

        Stage("load_lua");
        string script;
        string path = Path.Combine(Application.streamingAssetsPath, luaPath);
        using (UnityWebRequest requestLua = UnityWebRequest.Get(
            path.Contains("://") ? path : new Uri(Path.GetFullPath(path)).AbsoluteUri))
        {
            requestLua.timeout = 30;
            yield return requestLua.SendWebRequest();
            if (requestLua.result != UnityWebRequest.Result.Success)
                throw new IOException($"测试 Lua 读取失败：{path}，{requestLua.error}");
            script = requestLua.downloadHandler.text;
        }
        Record("load_lua", "读取测试 Lua", true, luaPath);
        Stage("lua");
        luaRunner = new GameplayTestLuaRunner(player, Check, Record);
        yield return luaRunner.Run(script, "@" + luaPath);
        if (!luaRunner.Succeeded) failedStep = true;
        flowCompleted = true;
    }

    private IEnumerator RunGuarded(IEnumerator routine)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(routine);
        try
        {
            while (stack.Count > 0)
            {
                object next = null;
                bool moved = false;
                Exception failure = null;
                try
                {
                    moved = stack.Peek().MoveNext();
                    if (moved) next = stack.Peek().Current;
                }
                catch (Exception exception) { failure = exception; }
                if (failure != null)
                {
                    Record("exception", "流程发生异常", false, failure.ToString());
                    Debug.LogException(failure, this);
                    break;
                }
                if (!moved)
                {
                    (stack.Pop() as IDisposable)?.Dispose();
                    continue;
                }
                if (next is IEnumerator nested) stack.Push(nested);
                else yield return next;
            }
        }
        finally
        {
            while (stack.Count > 0)
            {
                try { (stack.Pop() as IDisposable)?.Dispose(); }
                catch (Exception exception) { Record("cleanup", "协程清理失败", false, exception.ToString()); }
            }
            Finish(interruptionRequested);
        }
    }

    private bool Check(string name)
    {
        switch (name)
        {
            case "world_ready": return manager != null && manager.IsInGameWorld && manager.IsGameplayReady;
            case "player_ready": return player != null && ItemMgr.Instance.User_Player == player;
            case "terrain_ready": return showroom?.IsTerrainReady == true;
            case "showroom_complete": return showroom?.IsComplete == true;
            case "player_moved": return player != null && Vector3.Distance(playerStart, player.transform.position) > 0.1f;
            default: throw new ArgumentException("未知测试检查：" + name);
        }
    }

    private void OnWorldEntryProgress(WorldEntryProgressInfo progress)
    {
        if (progress.State == WorldEntryProgressState.Failed) worldEntryFailure = progress.Status;
    }

    private void OnWorldExit() => worldExited = true;

    private void OnReturnedToMenu()
    {
        // 退出通知早于存档写盘，必须等返回菜单后才能恢复正式存档目录。
        Destroy(gameObject);
    }
    #endregion

    #region 步骤与线程日志
    private void Stage(string stage)
    {
        currentStage = stage;
        report.CurrentStage = stage;
        WriteReport();
    }

    private void Record(string command, string label, bool success, string message)
    {
        failedStep |= !success;
        lock (reportLock)
            report.Steps.Add(new GameplayTestStepRecord
            {
                Index = report.Steps.Count + 1, Stage = currentStage, Command = command,
                Label = label, Success = success, Message = message,
                ElapsedSeconds = clock.Elapsed.TotalSeconds,
                PlayerPosition = player == null ? null : new GameplayTestPosition(player.transform.position)
            });
    }

    private void CaptureLog(string message, string stackTrace, LogType type)
    {
        // 日志回调可能来自后台线程，只收集普通数据，写文件放回主线程。
        lock (reportLock)
        {
            if (logCaptureClosed) return;
            bool isError = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            if (isError) report.ErrorCount++;
            if (type == LogType.Warning) report.WarningCount++;
            if (!isError && report.Logs.Count >= maximumLogEntries)
            {
                report.OmittedLogCount++;
                return;
            }
            report.Logs.Add(new GameplayTestLogRecord
            {
                Utc = DateTime.UtcNow.ToString("O"), ElapsedSeconds = clock.Elapsed.TotalSeconds,
                Stage = currentStage, Type = type.ToString(), Message = message, StackTrace = stackTrace
            });
        }
    }

    private void CopyShowroomReport()
    {
        if (showroom == null) return;
        report.ExpectedItemCount = showroom.ExpectedCount;
        report.PlacedItemCount = showroom.PlacedCount;
        report.Exhibits = new List<GameplayTestExhibit>(showroom.Exhibits);
    }

    private void Finish(bool interrupted)
    {
        if (finished || report == null) return;
        finished = true;
        try { luaRunner?.Dispose(); }
        catch (Exception exception) { Record("cleanup", "玩家控制清理失败", false, exception.ToString()); }
        luaRunner = null;
        lock (reportLock)
        {
            logCaptureClosed = true;
            CopyShowroomReport();
            report.PlayerEnd = player == null ? null : new GameplayTestPosition(player.transform.position);
            report.DurationSeconds = clock.Elapsed.TotalSeconds;
            report.FinishedUtc = DateTime.UtcNow.ToString("O");
            report.Status = interrupted ? "interrupted" : !flowCompleted || failedStep || report.ErrorCount > 0 ? "failed" : "passed";
        }
        UnsubscribeLogs();
        saveScope?.Dispose();
        if (WriteReport())
            Debug.Log($"[GameplayTest] 测试结束：{report.Status}，报告：{report.ReportPath}", this);
        else
            Debug.LogError("[GameplayTest] 测试已经结束，但最终 JSON 未能保存，请查看报告写入错误。", this);
    }

    private bool WriteReport()
    {
        try
        {
            lock (reportLock) report.Write();
            return true;
        }
        catch (Exception exception)
        {
            failedStep = true;
            report.ReportWriteError = exception.ToString();
            if (finished) report.Status = "failed";
            Debug.LogError($"[GameplayTest] 报告写入失败：{report.ReportPath}\n{exception}", this);
            return false;
        }
    }

    private void UnsubscribeLogs()
    {
        if (!subscribed) return;
        Application.logMessageReceivedThreaded -= CaptureLog;
        subscribed = false;
    }
    #endregion
}

/// <summary>保存隔离独立于测试组件寿命，删除组件后仍等退出写盘完毕才恢复目录。</summary>
internal sealed class GameplayTestSaveScope : IDisposable
{
    #region 存档隔离租约
    private readonly SaveDataMgr saves;
    private readonly GameManager manager;
    private readonly string originalDirectory;
    private readonly string testDirectory;
    private bool keepUntilMenu;
    private bool released;

    public GameplayTestSaveScope(SaveDataMgr saves, GameManager manager, string directory)
    {
        this.saves = saves;
        this.manager = manager;
        originalDirectory = saves.UserSavePath;
        testDirectory = directory;
        saves.UserSavePath = directory;
        manager.BackToHelloScene_Event_End += OnReturnedToMenu;
    }

    public void KeepUntilMenu() => keepUntilMenu = true;

    private void OnReturnedToMenu()
    {
        keepUntilMenu = false;
        Dispose();
    }

    public void Dispose()
    {
        if (released || keepUntilMenu && Application.isPlaying && manager != null) return;
        released = true;
        if (saves != null && saves.UserSavePath == testDirectory) saves.UserSavePath = originalDirectory;
        if (manager != null) manager.BackToHelloScene_Event_End -= OnReturnedToMenu;
    }
    #endregion
}
