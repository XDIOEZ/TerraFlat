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
    [SerializeField] private string testSuitePath = "GameplayTests/mechanical-suite.json";
    [SerializeField] private string seed = "flatworld-showroom-001";
    [SerializeField, Min(1f)] private float resourceTimeoutSeconds = 240f;
    [SerializeField, Min(1f)] private float worldTimeoutSeconds = 120f;
    [SerializeField, Min(1f)] private float totalTimeoutSeconds = 1200f;
    [SerializeField, Min(1f)] private float screenshotIntervalSeconds = 10f;
    [SerializeField, Min(100)] private int maximumLogEntries = 10000;

    public string ReportPath => report?.ReportPath;
    public string Status => report?.Status ?? "not_started";
    #endregion

    #region 运行状态
    private static GameplayTestScene activeRunner;
    private readonly object reportLock = new();
    private readonly object logQueueLock = new();
    private readonly Queue<CapturedLog> pendingLogs = new();
    private readonly Dictionary<GameplayTestCaseReport, OmittedLogs> pendingCaseOmissions = new();
    private readonly System.Diagnostics.Stopwatch clock = new();
    private GameplayTestReport report;
    private GameplayTestShowroom showroom;
    private GameplayTestLuaRunner luaRunner;
    private GameplayTestSuite suite;
    private volatile GameplayTestCaseReport activeCase;
    private GameManager manager;
    private SaveDataMgr saveManager;
    private GameplayTestSaveScope saveScope;
    private Player player;
    private string testPlayerProfile;
    private Vector3 playerStart;
    private string isolatedSaveDirectory;
    private string worldEntryFailure;
    private volatile string currentStage = "startup";
    private bool finished;
    private bool flowCompleted;
    private bool interruptionRequested;
    private bool worldExited;
    private bool subscribed;
    private volatile bool logCaptureClosed;
    private bool failedStep;
    private int capturedLogCount;
    private int pendingOmittedCount;
    private int pendingOmittedWarnings;
    private bool periodicScreenshotsEnabled;
    private bool screenshotCapturing;
    private double nextScreenshotAt;

    private sealed class CapturedLog
    {
        public GameplayTestCaseReport TestCase;
        public GameplayTestLogRecord Record;
    }

    private sealed class OmittedLogs
    {
        public int Count;
        public int Warnings;
    }
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
        runId = GameplayTestSelection.ResolveRunId(runId);
        string root = GameplayTestSelection.RootDirectory;
        isolatedSaveDirectory = Path.Combine(root, "Saves", runId);
        report = new GameplayTestReport
        {
            RunId = runId, Scene = gameObject.scene.path, TestSuitePath = testSuitePath,
            StartedUtc = DateTime.UtcNow.ToString("O"), SaveDirectory = isolatedSaveDirectory
        };
        GameplayTestArtifacts.Initialize(report);
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
        if (GameManager.Instance?.IsGameplayReady == true && ItemMgr.Instance?.User_Player != null)
            player = ItemMgr.Instance.User_Player;
        lock (reportLock) DrainCapturedLogs();
        if (periodicScreenshotsEnabled && !screenshotCapturing && !worldExited &&
            activeCase?.Status == "running" && Time.realtimeSinceStartupAsDouble >= nextScreenshotAt &&
            TryGetScreenshotPresentation(out _))
        {
            nextScreenshotAt = Time.realtimeSinceStartupAsDouble + Mathf.Max(1f, screenshotIntervalSeconds);
            StartCoroutine(CaptureAutomaticScreenshot(activeCase, "定时截图"));
        }
        if (worldExited || clock.Elapsed.TotalSeconds > totalTimeoutSeconds)
        {
            if (!interruptionRequested)
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
            StopAllCoroutines();
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
        GameplayTestMechanicalPlant.Reset();
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
            if (Time.realtimeSinceStartup >= deadline)
                throw new TimeoutException("等待游戏资源加载超时。当前状态：" +
                    (resources == null ? "GameRes 尚未创建" : resources.LoadState + " / " + resources.LoadStatusText));
            yield return null;
        }
        Record("resources", "完整资源就绪", true, GameRes.ExistingInstance.LoadStatusText);

        Stage("load_suite");
        testSuitePath = GameplayTestSelection.ResolveSuitePath(testSuitePath);
        report.TestSuitePath = testSuitePath;
        string suiteJson = null;
        yield return LoadText(testSuitePath, text => suiteJson = text);
        suite = GameplayTestSuite.Parse(suiteJson);
        if (suite.ExecutionMode != "gameplay")
            throw new InvalidOperationException("快速规则清单应通过 Editor 测试中心执行，不能进入真实世界测试场景。");
        GameplayTestSelection.ApplySelectedCases(suite, testSuitePath);
        foreach (GameplayTestCase testCase in suite.Cases)
            report.Cases.Add(new GameplayTestCaseReport
            {
                Id = testCase.Id, Name = testCase.Name, LuaPath = testCase.LuaPath,
                Status = testCase.Enabled ? "pending" : "disabled",
                Reason = testCase.Enabled ? null : "用例未启用。",
                ReportPath = GameplayTestArtifacts.CaseReportPath(report, testCase.Id)
            });
        Record("load_suite", "读取 Lua 测试清单", true, $"已登记 {report.Cases.Count} 个用例。");

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
        testPlayerProfile = player.ProfileName;
        playerStart = player.transform.position;
        report.PlayerStart = new GameplayTestPosition(playerStart);
        Record("create_world", "世界和默认玩家就绪", true, planet.Name);

        Stage("showroom");
        if (suite.BuildShowroom)
            yield return showroom.Build(planet, Record);
        else
            yield return showroom.PrepareMinimal(player.transform.position, Record);
        CopyShowroomReport();
        Record("showroom", suite.BuildShowroom ? "陈列厅构建完成" : "最小测试地形就绪",
            showroom.IsTerrainReady && (!suite.BuildShowroom || showroom.IsComplete),
            suite.BuildShowroom
                ? $"已放置 {showroom.PlacedCount}/{showroom.ExpectedCount} 种物品。"
                : "已跳过全物品陈列，只保留真实世界和玩家。");

        yield return RunCases();
        flowCompleted = true;
    }

    private IEnumerator LoadText(string relativePath, Action<string> loaded)
    {
        string path = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + relativePath.Replace('\\', '/');
        using (UnityWebRequest requestText = UnityWebRequest.Get(
            path.Contains("://") ? path : new Uri(Path.GetFullPath(path)).AbsoluteUri))
        {
            requestText.timeout = 30;
            yield return requestText.SendWebRequest();
            if (requestText.result != UnityWebRequest.Result.Success)
                throw new IOException($"测试文件读取失败：{path}，{requestText.error}");
            loaded(requestText.downloadHandler.text);
        }
    }

    private IEnumerator RunGuarded(IEnumerator routine)
    {
        try
        {
            yield return AdvanceGuarded(routine);
        }
        finally { Finish(interruptionRequested); }
    }

    private IEnumerator AdvanceGuarded(IEnumerator routine)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(routine);
        try
        {
            while (stack.Count > 0 && !finished && !interruptionRequested)
            {
                object next = null;
                bool moved = false;
                Exception failure = null;
                try
                {
                    moved = stack.Peek().MoveNext();
                    if (moved) next = stack.Peek().Current;
                    else (stack.Pop() as IDisposable)?.Dispose();
                }
                catch (Exception exception) { failure = exception; }
                if (failure != null)
                {
                    Record("exception", "流程发生异常", false, failure.ToString());
                    Debug.LogException(failure, this);
                    break;
                }
                if (!moved) continue;
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
        }
    }

    private bool Check(string name)
    {
        switch (name)
        {
            case "world_ready": return manager != null && manager.IsInGameWorld && manager.IsGameplayReady;
            case "player_ready": return RebindTestPlayer();
            case "terrain_ready": return showroom?.IsTerrainReady == true;
            case "showroom_complete": return showroom?.IsComplete == true;
            case "player_moved": return player != null && Vector3.Distance(playerStart, player.transform.position) > 0.1f;
            default:
                if (GameplayTestGm.TryCheck(player, name, out bool passed)) return passed;
                throw new ArgumentException("未知测试检查：" + name);
        }
    }

    // 场景迁移可以重建角色，只重绑同档案且属于本轮隔离存档的正式本地玩家。
    private bool RebindTestPlayer()
    {
        Player current = ItemMgr.GetInstance()?.User_Player;
        if (current == null || !current.IsLocalProfile || current.ProfileName != testPlayerProfile) return false;
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(current);
        if (player != current)
        {
            int previousInstance = player == null ? 0 : player.GetInstanceID();
            player = current;
            Record("player_rebind", "迁移后重新绑定隔离测试玩家", true,
                $"档案 {testPlayerProfile}；实例 {previousInstance} → {current.GetInstanceID()}。");
        }
        return true;
    }

    private void OnWorldEntryProgress(WorldEntryProgressInfo progress)
    {
        if (progress.State == WorldEntryProgressState.Failed) worldEntryFailure = progress.Status;
    }

    private void OnWorldExit()
    {
        if (GameplayTestSpaceScenario.IsMigrationExpected) return;
        worldExited = true;
        if (finished) return;
        // 退出事件当帧就标记中断，避免协程先结束而把退出误记为普通失败。
        interruptionRequested = true;
        Record("abort", "测试中断", false, "测试期间退出了世界。");
    }

    private void OnReturnedToMenu()
    {
        // 退出通知早于存档写盘，必须等返回菜单后才能恢复正式存档目录。
        Destroy(gameObject);
    }
    #endregion

    #region 多用例调度
    private IEnumerator RunCases()
    {
        bool stopped = false;
        foreach (GameplayTestCaseReport testCase in report.Cases)
        {
            if (finished || interruptionRequested) yield break;
            lock (reportLock)
            {
                DrainCapturedLogs();
                RefreshCaseCounts();
                stopped |= !suite.ContinueAfterFailure && report.FailedCaseCount > 0;
            }
            if (testCase.Status == "disabled") continue;
            if (stopped || !Check("world_ready") || !Check("player_ready"))
            {
                testCase.Status = "skipped";
                testCase.Reason = stopped ? "前面的用例失败，清单已配置失败即停。" : "世界或玩家不再就绪。";
                testCase.FinishedUtc = DateTime.UtcNow.ToString("O");
                failedStep = true;
                continue;
            }
            yield return RunCase(testCase);
            stopped |= testCase.Status != "passed" && !suite.ContinueAfterFailure;
        }
    }

    private IEnumerator RunCase(GameplayTestCaseReport testCase)
    {
        lock (reportLock)
        {
            lock (logQueueLock) activeCase = testCase;
            testCase.Status = "running";
            testCase.StartedUtc = DateTime.UtcNow.ToString("O");
            testCase.StartedElapsedSeconds = clock.Elapsed.TotalSeconds;
            playerStart = player.transform.position;
            testCase.PlayerStart = new GameplayTestPosition(playerStart);
        }
        nextScreenshotAt = Time.realtimeSinceStartupAsDouble + Mathf.Max(1f, screenshotIntervalSeconds);
        periodicScreenshotsEnabled = true;
        bool succeeded = false;
        bool finalScreenshotRequested = false;
        try
        {
            // 每个用例自己捕获异常，读取失败或 Lua 报错不会截断整个测试清单。
            yield return AdvanceGuarded(ExecuteCase(testCase, () =>
            {
                finalScreenshotRequested = true;
                return CaptureFinalScreenshotBeforeRuntimeCleanup(testCase);
            }));
            succeeded = luaRunner?.Succeeded == true;
            if (!finalScreenshotRequested)
                yield return CaptureFinalScreenshotBeforeRuntimeCleanup(testCase);
        }
        finally
        {
            periodicScreenshotsEnabled = false;
            ReleaseLuaRunner();
            CompleteCase(testCase, succeeded, interruptionRequested);
        }
    }

    private IEnumerator ExecuteCase(GameplayTestCaseReport testCase, Func<IEnumerator> beforeCleanup)
    {
        Stage("case." + testCase.Id + ".load_lua");
        string script = null;
        yield return LoadText(testCase.LuaPath, text => script = text);
        Record("load_lua", "读取测试 Lua", true, testCase.LuaPath);
        Stage("case." + testCase.Id + ".lua");
        luaRunner = new GameplayTestLuaRunner(player, Check, Record);
        yield return luaRunner.Run(script, "@" + testCase.LuaPath, beforeCleanup);
    }

    #region 定时截图
    private IEnumerator CaptureFinalScreenshotBeforeRuntimeCleanup(GameplayTestCaseReport testCase)
    {
        periodicScreenshotsEnabled = false;
        // 结束图必须保留尚未清理的设备，已有定时图也不能跳过最后现场。
        while (screenshotCapturing && !interruptionRequested) yield return null;
        if (interruptionRequested) yield break;
        int previousCount = testCase.Screenshots.Count;
        yield return CaptureAutomaticScreenshot(testCase, "用例结束补拍");
        if (testCase.Screenshots.Count <= previousCount ||
            !string.IsNullOrEmpty(testCase.Screenshots[testCase.Screenshots.Count - 1].Error) ||
            !File.Exists(testCase.Screenshots[testCase.Screenshots.Count - 1].Path))
            Record("screenshot_coverage", "清理前保存用例结束现场", false,
                "结束截图失败，请查看本用例 screenshots 中的采集错误。");
    }

    private IEnumerator CaptureAutomaticScreenshot(GameplayTestCaseReport testCase, string label)
    {
        screenshotCapturing = true;
        int firstNewScreenshot = testCase.Screenshots.Count;
        try
        {
            yield return GameplayTestLuaRunner.Advance(CaptureScreenshotForCase(testCase, label), error =>
            {
                if (testCase.Screenshots.Count > firstNewScreenshot)
                    testCase.Screenshots[testCase.Screenshots.Count - 1].Error ??= error.ToString();
                else
                    Record("auto_screenshot", label, false, error.ToString());
            });
        }
        finally { screenshotCapturing = false; }
    }

    private IEnumerator CaptureScreenshotForCase(GameplayTestCaseReport testCase, string label)
    {
        GameplayTestScreenshotRecord screenshot;
        lock (reportLock)
        {
            if (activeCase != testCase) yield break;
            int index = testCase.Screenshots.Count + 1;
            screenshot = new GameplayTestScreenshotRecord
            {
                Index = index, Label = label,
                Path = GameplayTestArtifacts.CaseScreenshotPath(report, testCase.Id, index),
                Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                Utc = DateTime.UtcNow.ToString("O"), ElapsedSeconds = clock.Elapsed.TotalSeconds,
                PlayerPosition = ItemMgr.Instance?.User_Player == null ? null :
                    new GameplayTestPosition(ItemMgr.Instance.User_Player.transform.position)
            };
            // 先记录请求，捕获中断时报告仍能说明哪个现场没有保存成功。
            testCase.Screenshots.Add(screenshot);
        }
        // 相机已创建不代表主角已经入镜，以连续渲染帧的真实可见状态作为取图前提。
        double deadline = Time.realtimeSinceStartupAsDouble + 8d;
        int readyFrames = 0;
        string presentation = string.Empty;
        while (readyFrames < 2)
        {
            yield return null;
            readyFrames = TryGetScreenshotPresentation(out presentation) ? readyFrames + 1 : 0;
            if (Time.realtimeSinceStartupAsDouble < deadline || readyFrames >= 2) continue;
            screenshot.Error = "截图现场未就绪：" + presentation;
            yield break;
        }
        lock (reportLock)
        {
            screenshot.Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            screenshot.Utc = DateTime.UtcNow.ToString("O");
            screenshot.ElapsedSeconds = clock.Elapsed.TotalSeconds;
            screenshot.PlayerPosition = new GameplayTestPosition(ItemMgr.Instance.User_Player.transform.position);
        }
        yield return GameplayTestArtifacts.CaptureScreenshot(this, screenshot.Path,
            error => screenshot.Error = error);
        if (string.IsNullOrEmpty(screenshot.Error))
            Record("auto_screenshot", label, true, screenshot.Path);
    }
    #endregion

    private bool TryGetScreenshotPresentation(out string reason)
    {
        Player current = ItemMgr.Instance?.User_Player;
        if (manager?.IsGameplayReady != true || current == null)
        {
            reason = "玩法世界或本地玩家尚未就绪。";
            return false;
        }
        Camera camera = current.itemMods?.GetMod_ByID<Mod_Cam>(ModText.Camera)?.ControllerCamera;
        SpriteRenderer sprite = current.Sprite;
        if (camera == null || !camera.isActiveAndEnabled || sprite == null ||
            !sprite.enabled || !sprite.gameObject.activeInHierarchy || sprite.sprite == null || sprite.color.a <= 0.01f)
        {
            reason = "玩家相机或主角贴图尚未显示。";
            return false;
        }
        Vector3 viewport = camera.WorldToViewportPoint(sprite.bounds.center);
        bool visible = (camera.cullingMask & (1 << sprite.gameObject.layer)) != 0 &&
                       viewport.z >= camera.nearClipPlane && viewport.z <= camera.farClipPlane &&
                       viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
        reason = $"主角视口 ({viewport.x:0.###}, {viewport.y:0.###}, {viewport.z:0.###})，相机 {camera.GetInstanceID()}。";
        return visible;
    }

    private void CompleteCase(GameplayTestCaseReport testCase, bool succeeded, bool interrupted)
    {
        lock (reportLock)
        {
            if (finished || testCase.Status != "running") return;
            DrainCapturedLogs(clearCurrentCase: true);
            testCase.FinishedUtc = DateTime.UtcNow.ToString("O");
            testCase.DurationSeconds = clock.Elapsed.TotalSeconds - testCase.StartedElapsedSeconds;
            testCase.PlayerEnd = player == null ? null : new GameplayTestPosition(player.transform.position);
            testCase.Status = interrupted ? "interrupted" : succeeded && testCase.FailedStepCount == 0 &&
                testCase.ErrorCount == 0 ? "passed" : "failed";
            if (testCase.Status != "passed")
            {
                failedStep = true;
                testCase.Reason = interrupted ? "测试流程被中断。" : "请查看用例步骤与错误日志。";
                foreach (GameplayTestStepRecord step in testCase.Steps)
                    if (!step.Success) { testCase.Reason = step.Label + "：" + step.Message; break; }
            }
            if (!WriteCaseReport(testCase)) testCase.Status = "failed";
            RefreshCaseCounts();
        }
        Stage("between_cases");
    }

    private void ReleaseLuaRunner()
    {
        try { luaRunner?.Dispose(); }
        catch (Exception exception) { Record("cleanup", "玩家控制清理失败", false, exception.ToString()); }
        luaRunner = null;
    }

    private bool WriteCaseReport(GameplayTestCaseReport testCase)
    {
        try
        {
            GameplayTestReport.WriteJson(testCase.ReportPath, testCase);
            return true;
        }
        catch (Exception exception)
        {
            failedStep = true;
            testCase.ReportWriteError = exception.ToString();
            Debug.LogError($"[GameplayTest] 用例报告写入失败：{testCase.ReportPath}\n{exception}", this);
            return false;
        }
    }

    private void RefreshCaseCounts()
    {
        report.PassedCaseCount = report.FailedCaseCount = report.SkippedCaseCount = report.InterruptedCaseCount = 0;
        foreach (GameplayTestCaseReport testCase in report.Cases)
        {
            if (testCase.Status == "passed") report.PassedCaseCount++;
            if (testCase.Status == "failed") report.FailedCaseCount++;
            if (testCase.Status == "skipped") report.SkippedCaseCount++;
            if (testCase.Status == "interrupted") report.InterruptedCaseCount++;
        }
    }
    #endregion

    #region 步骤与线程日志
    private void Stage(string stage)
    {
        if (finished) return;
        currentStage = stage;
        report.CurrentStage = stage;
        WriteReport();
    }

    private void Record(string command, string label, bool success, string message)
    {
        lock (reportLock)
        {
            if (logCaptureClosed) return;
            failedStep |= !success;
            var step = new GameplayTestStepRecord
            {
                Index = report.Steps.Count + 1, Stage = currentStage, Command = command,
                CaseId = activeCase?.Id,
                Label = label, Success = success, Message = message,
                ElapsedSeconds = clock.Elapsed.TotalSeconds,
                PlayerPosition = player == null ? null : new GameplayTestPosition(player.transform.position)
            };
            report.Steps.Add(step);
            if (activeCase != null)
            {
                activeCase.Steps.Add(step);
                if (!success) activeCase.FailedStepCount++;
            }
        }
    }

    private void CaptureLog(string message, string stackTrace, LogType type)
    {
        // 短锁内冻结归属并入队，后台日志不等待 JSON 写盘，也不访问 Unity 对象。
        lock (logQueueLock)
        {
            if (logCaptureClosed) return;
            GameplayTestCaseReport testCase = activeCase;
            bool isError = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            if (!isError && capturedLogCount >= maximumLogEntries)
            {
                pendingOmittedCount++;
                if (type == LogType.Warning) pendingOmittedWarnings++;
                if (testCase != null)
                {
                    if (!pendingCaseOmissions.TryGetValue(testCase, out OmittedLogs counts))
                        pendingCaseOmissions.Add(testCase, counts = new OmittedLogs());
                    counts.Count++;
                    if (type == LogType.Warning) counts.Warnings++;
                }
                return;
            }
            capturedLogCount++;
            pendingLogs.Enqueue(new CapturedLog
            {
                TestCase = testCase,
                Record = new GameplayTestLogRecord
                {
                    Utc = DateTime.UtcNow.ToString("O"), ElapsedSeconds = clock.Elapsed.TotalSeconds,
                    Stage = currentStage, CaseId = testCase?.Id,
                    Type = type.ToString(), Message = message, StackTrace = stackTrace
                }
            });
        }
    }

    private void DrainCapturedLogs(bool clearCurrentCase = false, bool closeCapture = false)
    {
        CapturedLog[] logs;
        lock (logQueueLock)
        {
            if (clearCurrentCase) activeCase = null;
            if (closeCapture) logCaptureClosed = true;
            logs = pendingLogs.ToArray();
            pendingLogs.Clear();
            report.OmittedLogCount += pendingOmittedCount;
            report.WarningCount += pendingOmittedWarnings;
            pendingOmittedCount = pendingOmittedWarnings = 0;
            foreach (KeyValuePair<GameplayTestCaseReport, OmittedLogs> entry in pendingCaseOmissions)
            {
                entry.Key.OmittedLogCount += entry.Value.Count;
                entry.Key.WarningCount += entry.Value.Warnings;
            }
            pendingCaseOmissions.Clear();
        }
        foreach (CapturedLog captured in logs)
        {
            GameplayTestLogRecord log = captured.Record;
            GameplayTestCaseReport testCase = captured.TestCase;
            bool isError = log.Type == "Error" || log.Type == "Exception" || log.Type == "Assert";
            if (isError) report.ErrorCount++;
            if (log.Type == "Warning") report.WarningCount++;
            report.Logs.Add(log);
            if (testCase == null) continue;
            if (isError) testCase.ErrorCount++;
            if (log.Type == "Warning") testCase.WarningCount++;
            testCase.Logs.Add(log);
            if (isError && testCase.Status == "passed")
            {
                testCase.Status = "failed";
                failedStep = true;
            }
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
        ReleaseLuaRunner();
        lock (reportLock)
        {
            DrainCapturedLogs(clearCurrentCase: true, closeCapture: true);
            foreach (GameplayTestCaseReport testCase in report.Cases)
            {
                foreach (GameplayTestScreenshotRecord screenshot in testCase.Screenshots)
                    if (!File.Exists(screenshot.Path) && string.IsNullOrEmpty(screenshot.Error))
                        screenshot.Error = "自动截图被中断，现场图片未保存。";
                if (testCase.Status == "running")
                {
                    testCase.Status = interrupted ? "interrupted" : "failed";
                    testCase.FinishedUtc = DateTime.UtcNow.ToString("O");
                    testCase.DurationSeconds = clock.Elapsed.TotalSeconds - testCase.StartedElapsedSeconds;
                    testCase.PlayerEnd = player == null ? null : new GameplayTestPosition(player.transform.position);
                    testCase.Reason = "测试流程提前结束。";
                }
                if (testCase.Status == "pending")
                {
                    testCase.Status = "skipped";
                    testCase.Reason = "测试流程提前结束，本用例未执行。";
                    testCase.FinishedUtc = DateTime.UtcNow.ToString("O");
                }
                if (!WriteCaseReport(testCase)) testCase.Status = "failed";
            }
            RefreshCaseCounts();
            CopyShowroomReport();
            report.PlayerEnd = player == null ? null : new GameplayTestPosition(player.transform.position);
            report.DurationSeconds = clock.Elapsed.TotalSeconds;
            report.FinishedUtc = DateTime.UtcNow.ToString("O");
            report.Status = interrupted ? "interrupted" : !flowCompleted || failedStep || report.ErrorCount > 0 ? "failed" : "passed";
        }
        UnsubscribeLogs();
        saveScope?.Dispose();
        if (WriteReport())
        {
            try
            {
                GameplayTestArtifacts.WriteSummary(report);
                report.PackagePath = GameplayTestArtifacts.CreatePackage(report.RunDirectory);
            }
            catch (Exception exception)
            {
                report.ArtifactError = exception.ToString();
                report.PackagePath = null;
                report.Status = "failed";
                WriteReport();
                try { GameplayTestArtifacts.WriteSummary(report); }
                catch (Exception summaryError) { Debug.LogError("[GameplayTest] 阅读报告写入失败：" + summaryError, this); }
                Debug.LogError("[GameplayTest] 结果包生成失败：" + exception, this);
            }
            GameplayTestSelection.WriteLastGameplayReport(report.ReportPath);
            Debug.Log($"[GameplayTest] 测试结束：{report.Status}，报告：{report.ReportPath}", this);
        }
        else
            Debug.LogError("[GameplayTest] 测试已经结束，但最终 JSON 未能保存，请查看报告写入错误。", this);
    }

    private bool WriteReport()
    {
        try
        {
            lock (reportLock)
            {
                DrainCapturedLogs();
                RefreshCaseCounts();
                report.Write();
            }
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
