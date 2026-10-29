using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.GameplayMCP
{
    /// <summary>一个 Unity Editor 内按清单执行 Lua 规则、仅 PlayMode 或 EditMode → PlayMode，统一保存报告。</summary>
    [InitializeOnLoad]
    internal static partial class GameplayTestPipelineCoordinator
    {
        #region 文件与生命周期
        private const string SessionRunKey = "FlatWorld.GameplayTestPipeline.ActiveRun";
        private const string PlayModeEnteredKey = "FlatWorld.GameplayTestPipeline.PlayModeEnteredRun";
        private const string PlayModeExitKey = "FlatWorld.GameplayTestPipeline.PlayModeExitUtc";
        private const string PlayModeLostKey = "FlatWorld.GameplayTestPipeline.PlayModeLostUtc";
        private const string EditAssembly = "FlatWorld.Gameplay.EditModeTests";
        private const string PlayAssembly = "FlatWorld.Gameplay.PlayModeTests";
        private static TestRunnerApi api;
        private static Callbacks callbacks;
        private static double nextRequestPoll;
        private static readonly Func<bool> IsUnityTestRunActive = (Func<bool>)Delegate.CreateDelegate(
            typeof(Func<bool>), typeof(TestRunnerApi).GetMethod("IsRunActive", BindingFlags.Static | BindingFlags.NonPublic));

        [Serializable]
        private sealed class CaseResult
        {
            public string Name;
            public string Status;
            public string Message;
            public string StackTrace;
            public double DurationSeconds;
        }

        [Serializable]
        private sealed class PhaseResult
        {
            public string Status = "pending";
            public int Passed;
            public int Failed;
            public int Skipped;
            public int Inconclusive;
            public List<CaseResult> Cases = new();
        }

        [Serializable]
        private sealed class PipelineReport
        {
            public string RunId;
            public string SuitePath;
            public string ExecutionMode = "gameplay";
            public bool PlayModeOnly;
            public List<string> SelectedCaseIds = new();
            public string ReportPath;
            public string RunDirectory;
            public string SummaryPath;
            public string PackagePath;
            public List<string> ScreenshotPaths = new();
            public string ArtifactError;
            public string StartedUtc;
            public string FinishedUtc;
            public string Status = "running";
            public string Phase = "queued";
            public string Reason;
            public string GameplayReportPath;
            public PhaseResult EditMode = new();
            public PhaseResult PlayMode = new();
            public PhaseResult Rules = new();
        }

        static GameplayTestPipelineCoordinator()
        {
            EditorApplication.update += PollLocalRequests;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneClosed += _ => ScheduleBootstrapSceneCleanupWhenIdle();
            // PlayMode 域重载后补登记同一个监听器，避免丢失最终报告。
            EditorApplication.delayCall += () =>
            {
                EnsureCallbacks();
                ScheduleBootstrapSceneCleanupWhenIdle();
                if (!string.IsNullOrEmpty(SessionState.GetString(SessionRunKey, string.Empty)))
                {
                    ResumeQueuedPhase();
                }
            };
        }

        private static string DirectoryPath => Path.Combine(GameplayTestSelection.RootDirectory, "Pipeline");
        private static string LatestPath => Path.Combine(DirectoryPath, "latest-run.txt");
        private static string InboxDirectory => Path.Combine(DirectoryPath, "inbox");
        private static string OutboxDirectory => Path.Combine(DirectoryPath, "outbox");

        private static string CurrentRunId
        {
            get => SessionState.GetString(SessionRunKey, string.Empty);
            set => SessionState.SetString(SessionRunKey, value ?? string.Empty);
        }

        private static void EnsureCallbacks()
        {
            if (api != null) return;
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            callbacks = new Callbacks();
            api.RegisterCallbacks(callbacks);
        }

        private static string ReportFile(string runId) => Path.Combine(DirectoryPath, runId + ".json");

        private static PipelineReport LoadCurrent()
        {
            string id = CurrentRunId;
            return string.IsNullOrEmpty(id) ? null : LoadFile(ReportFile(id));
        }

        private static PipelineReport LoadFile(string path)
            => File.Exists(path) ? JsonConvert.DeserializeObject<PipelineReport>(File.ReadAllText(path, Encoding.UTF8)) : null;

        private static void Write(PipelineReport report)
        {
            Directory.CreateDirectory(DirectoryPath);
            string path = ReportFile(report.RunId);
            report.ReportPath = path;
            string json = JsonConvert.SerializeObject(report, Formatting.Indented,
                new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            // CLI 轮询报告时 Windows 可能短暂锁住目标文件，重试而不破坏原子替换。
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                    else File.Move(temporaryPath, path);
                    break;
                }
                catch (IOException) when (attempt < 9)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
            File.WriteAllText(LatestPath, path, new UTF8Encoding(false));
        }

        /// <summary>本机 CLI 写一次文件就能启动并等待完整报告，省去高频 MCP 轮询。</summary>
        private static void PollLocalRequests()
        {
            if (EditorApplication.timeSinceStartup < nextRequestPoll) return;
            nextRequestPoll = EditorApplication.timeSinceStartup + 0.5d;
            CheckUnexpectedPlayModeExit();
            ResumeQueuedPhase();
            if (!Directory.Exists(InboxDirectory)) return;
            string[] requests = Directory.GetFiles(InboxDirectory, "*.json");
            if (requests.Length == 0) return;
            Array.Sort(requests, StringComparer.Ordinal);
            string requestPath = requests[0];
            string requestId = Path.GetFileNameWithoutExtension(requestPath);
            JObject response;
            try
            {
                if (requestId.Length != 32 || !Guid.TryParseExact(requestId, "N", out _))
                    throw new InvalidDataException("无效本地请求编号。");
                JObject command = JObject.Parse(File.ReadAllText(requestPath, Encoding.UTF8));
                string[] selectedIds = null;
                if (command["caseIds"] != null)
                {
                    if (command["caseIds"] is not JArray ids)
                        throw new InvalidDataException("caseIds 必须是用例 ID 数组。");
                    selectedIds = new string[ids.Count];
                    for (int i = 0; i < ids.Count; i++)
                    {
                        if (ids[i].Type != JTokenType.String)
                            throw new InvalidDataException("caseIds 中的用例 ID 必须是字符串。");
                        selectedIds[i] = ids[i].Value<string>();
                    }
                }
                if (command["playModeOnly"] != null && command["playModeOnly"].Type != JTokenType.Boolean)
                    throw new InvalidDataException("playModeOnly 必须是布尔值。");
                response = Start(command.Value<string>("suitePath"), selectedIds,
                    command.Value<bool?>("playModeOnly") == true);
            }
            catch (Exception exception)
            {
                response = new JObject { ["ok"] = false, ["message"] = exception.ToString() };
            }
            try
            {
                Directory.CreateDirectory(OutboxDirectory);
                string output = Path.Combine(OutboxDirectory, requestId + ".json");
                File.WriteAllText(output, response.ToString(Formatting.Indented), new UTF8Encoding(false));
            }
            finally
            {
                File.Delete(requestPath);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change is PlayModeStateChange.ExitingEditMode or PlayModeStateChange.EnteredEditMode)
                ScheduleBootstrapSceneCleanupWhenIdle();
            PipelineReport report = LoadCurrent();
            if (report?.Status != "running" || report.Phase != "playmode") return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.SetString(PlayModeEnteredKey, report.RunId);
                SessionState.SetString(PlayModeLostKey, string.Empty);
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
                SessionState.SetString(PlayModeExitKey, DateTime.UtcNow.ToString("O"));
        }

        private static void CheckUnexpectedPlayModeExit()
        {
            string exitText = SessionState.GetString(PlayModeExitKey, string.Empty);
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            PipelineReport report = LoadCurrent();
            if (report?.Status != "running" || report.Phase != "playmode")
            {
                if (!string.IsNullOrEmpty(exitText))
                    SessionState.SetString(PlayModeExitKey, string.Empty);
                SessionState.SetString(PlayModeEnteredKey, string.Empty);
                SessionState.SetString(PlayModeLostKey, string.Empty);
                return;
            }
            // 启动阶段可能因首次编译耗时数分钟，不得在真正 EnteredPlayMode 前判断超时。
            if (SessionState.GetString(PlayModeEnteredKey, string.Empty) != report.RunId) return;
            bool knownExit = DateTime.TryParse(exitText, out DateTime exitedAt);
            if (knownExit)
            {
                if (DateTime.UtcNow - exitedAt.ToUniversalTime() < TimeSpan.FromSeconds(12)) return;
            }
            else
            {
                // 域重载可能丢失 ExitingPlayMode，连续确认离开状态后再清理任务。
                string lostText = SessionState.GetString(PlayModeLostKey, string.Empty);
                if (!DateTime.TryParse(lostText, out DateTime lostAt))
                {
                    SessionState.SetString(PlayModeLostKey, DateTime.UtcNow.ToString("O"));
                    return;
                }
                if (DateTime.UtcNow - lostAt.ToUniversalTime() < TimeSpan.FromSeconds(12)) return;
            }
            if (report.Status == "running")
                Finish(report, "interrupted", "PlayMode 已退出，但 Test Framework 未返回最终结果；请查看场景报告与 Unity Console。");
        }

        private static void ResumeQueuedPhase()
        {
            PipelineReport report = LoadCurrent();
            if (report == null || report.Status != "running") return;
            if (report.Phase == "queued")
            {
                if (report.ExecutionMode == "rules") StartRulesPhase(report.RunId);
                else RunPhase(report.RunId, TestMode.EditMode);
            }
            else if (report.Phase == "rules" && rulesFlow == null)
                Finish(report, "interrupted", "编辑器域重载中断了快速规则；请重新执行选中的系统。");
            else if (report.Phase == "playmode_queued")
                RunPhase(report.RunId, TestMode.PlayMode);
        }
        #endregion

        #region AI 和菜单入口
        internal static JObject Start(string suitePath, IReadOnlyCollection<string> selectedCaseIds = null,
            bool playModeOnly = false)
        {
            PipelineReport active = LoadCurrent();
            if (active != null && active.Status == "running")
                return Reply(false, active, "已有一轮测试正在执行，请读取状态，不要重复启动。");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Reply(false, null, "当前 Unity 处于 PlayMode 或正在切换，不会打断你的游戏现场。");
            suitePath = string.IsNullOrWhiteSpace(suitePath)
                ? (playModeOnly ? "GameplayTests/all-systems-gameplay.json" : GameplayTestSelection.DefaultSuitePath)
                : suitePath;
            try
            {
                string path = GameplayTestSelection.GetAbsoluteSuitePath(suitePath);
                GameplayTestSuite suite = GameplayTestSuite.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (playModeOnly && suite.ExecutionMode != "gameplay")
                    throw new InvalidDataException("仅 PlayMode 模式只接受 gameplay 清单，不能运行 rules 清单。");
                if (selectedCaseIds != null)
                    GameplayTestSelection.FilterCases(suite, selectedCaseIds);
                var casesToRun = new List<string>();
                foreach (GameplayTestCase testCase in suite.Cases)
                {
                    if (!testCase.Enabled) continue;
                    casesToRun.Add(testCase.Id);
                    string script = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath,
                        testCase.LuaPath.Replace('/', Path.DirectorySeparatorChar)));
                    if (!File.Exists(script) || new FileInfo(script).Length == 0)
                        return Reply(false, null, "用例脚本缺失或为空：" + testCase.Id);
                }

                string runId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" +
                               Guid.NewGuid().ToString("N").Substring(0, 8);
                var report = new PipelineReport
                {
                    RunId = runId, SuitePath = suitePath, ExecutionMode = suite.ExecutionMode, PlayModeOnly = playModeOnly,
                    Phase = playModeOnly ? "playmode_queued" : "queued", StartedUtc = DateTime.UtcNow.ToString("O"),
                    SelectedCaseIds = casesToRun,
                    RunDirectory = GameplayTestArtifacts.GetRunDirectory(runId)
                };
                if (playModeOnly) report.EditMode.Status = "not_requested";
                report.SummaryPath = Path.Combine(report.RunDirectory, "report.md");
                report.PackagePath = report.RunDirectory;
                Directory.CreateDirectory(report.RunDirectory);
                GameplayTestSelection.SelectSuite(suitePath, runId, selectedCaseIds);
                CurrentRunId = runId;
                Write(report);
                // 编译中先接单保存现场，待 Unity 稳定后由更新循环启动，无需 Agent 重发请求。
                if (suite.ExecutionMode != "rules" && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
                    EnsureCallbacks();
                // 让本次 MCP 请求先返回，测试在 Editor 后续更新中自行跑完。
                EditorApplication.delayCall += ResumeQueuedPhase;
                return Reply(true, report, suite.ExecutionMode == "rules" ?
                    "已排队执行编辑态 Lua 规则，不启动世界或 PlayMode。" : playModeOnly ?
                    "已排队直接执行 PlayMode Lua 测试，不运行 EditMode 安全前置。" :
                    "已排队执行 EditMode 测试，全部通过后才进入 PlayMode。");
            }
            catch (Exception exception)
            {
                return Reply(false, null, "清单验证或启动失败：" + exception);
            }
        }

        internal static JObject Status(bool full)
        {
            PipelineReport report = LoadCurrent();
            if (report == null && File.Exists(LatestPath))
            {
                string path = File.ReadAllText(LatestPath, Encoding.UTF8).Trim();
                report = LoadFile(path);
            }
            if (report == null) return Reply(false, null, "尚无测试报告。");
            JObject response = Reply(true, report, null);
            if (full && !string.IsNullOrEmpty(report.GameplayReportPath) && File.Exists(report.GameplayReportPath))
                response["gameplay"] = JObject.Parse(File.ReadAllText(report.GameplayReportPath));
            return response;
        }

        [MenuItem("FlatWorld/测试/执行机械动力系统（EditMode → PlayMode）")]
        private static void MenuRun()
        {
            JObject response = Start("GameplayTests/mechanical-suite.json");
            Debug.Log("[GameplayTestPipeline] " + response.ToString(Formatting.None));
        }

        [MenuItem("FlatWorld/测试/查看最近报告")]
        private static void MenuReport()
            => Debug.Log("[GameplayTestPipeline] " + Status(false).ToString(Formatting.Indented));

        private static JObject Reply(bool ok, PipelineReport report, string message)
        {
            var serializer = Newtonsoft.Json.JsonSerializer.Create(new JsonSerializerSettings
                { ContractResolver = new CamelCasePropertyNamesContractResolver() });
            var result = new JObject
            {
                ["ok"] = ok,
                ["message"] = message,
                ["runId"] = report?.RunId,
                ["status"] = report?.Status,
                ["phase"] = report?.Phase,
                ["suitePath"] = report?.SuitePath,
                ["executionMode"] = report?.ExecutionMode,
                ["playModeOnly"] = report?.PlayModeOnly,
                ["rules"] = report == null ? null : JObject.FromObject(report.Rules, serializer),
                ["selectedCaseIds"] = report == null ? null : new JArray(report.SelectedCaseIds),
                ["reportPath"] = report?.ReportPath,
                ["gameplayReportPath"] = report?.GameplayReportPath,
                ["runDirectory"] = report?.RunDirectory,
                ["summaryPath"] = report?.SummaryPath,
                ["packagePath"] = report?.PackagePath,
                ["screenshotPaths"] = report == null ? null : new JArray(report.ScreenshotPaths),
                ["artifactError"] = report?.ArtifactError,
                ["reason"] = report?.Reason,
                ["editMode"] = report == null ? null : JObject.FromObject(report.EditMode, serializer),
                ["playMode"] = report == null ? null : JObject.FromObject(report.PlayMode, serializer)
            };
            if (!ok || report?.Status == "failed")
                result["nextAction"] = "读取失败用例和堆栈 → 修复生产代码或清单 → 重新调用 run；不要原样无限重试。";
            return result;
        }
        #endregion

        #region 分阶段调度
        private static bool EnsureRestorableEditorScene(out string error)
        {
            error = null;
            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            bool staleBootstrap = false;
            foreach (SceneSetup scene in setup)
                if (scene.isLoaded && IsTestRunnerBootstrapScene(scene.path))
                {
                    staleBootstrap = true;
                    break;
                }
            if (!staleBootstrap) return true;

            // Unity 测试器删除临时引导场景后，下一轮不能继续把它作为恢复目标。
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isDirty && !IsTestRunnerBootstrapScene(scene.path))
                {
                    error = "当前 Editor 场景有未保存修改，拒绝自动替换临时测试场景。";
                    return false;
                }
            }

            const string stableScene = "Assets/3_Scenes/Development/GameplayTestScene.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(stableScene) == null)
            {
                error = "缺少可用于恢复编辑器状态的正式测试场景：" + stableScene;
                return false;
            }
            EditorSceneManager.OpenScene(stableScene, OpenSceneMode.Single);
            Debug.Log("[GameplayTestPipeline] 离开上轮遗留的临时 InitTestScene，改用正式测试场景作为恢复入口。");
            return true;
        }

        internal static bool IsTestRunnerBootstrapScene(string path)
        {
            const string prefix = "Assets/InitTestScene";
            const string suffix = ".unity";
            return path != null && path.Length == prefix.Length + 36 + suffix.Length &&
                   path.StartsWith(prefix, StringComparison.Ordinal) &&
                   path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                   Guid.TryParseExact(path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length),
                       "D", out _);
        }

        private static void ScheduleBootstrapSceneCleanupWhenIdle()
        {
            EditorApplication.update -= CleanupBootstrapScenesWhenIdle;
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.update += CleanupBootstrapScenesWhenIdle;
        }

        private static void CleanupBootstrapScenesWhenIdle()
        {
            if (TryCleanupUnopenedTestRunnerBootstrapScenes(out _))
                EditorApplication.update -= CleanupBootstrapScenesWhenIdle;
        }

        internal static bool TryCleanupUnopenedTestRunnerBootstrapScenes(out int deletedCount)
        {
            deletedCount = 0;
            // 测试器完成恢复和删除前保留其引导场景；只在事件触发后的首次空闲时扫描。
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || IsUnityTestRunActive()) return false;
            var openScenePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (SceneSetup scene in EditorSceneManager.GetSceneManagerSetup()) openScenePaths.Add(scene.path);
            foreach (string file in Directory.EnumerateFiles(Application.dataPath, "InitTestScene*.unity"))
            {
                string path = "Assets/" + Path.GetFileName(file);
                if (!IsTestRunnerBootstrapScene(path) || openScenePaths.Contains(path)) continue;
                if (AssetDatabase.DeleteAsset(path)) deletedCount++;
                else Debug.LogWarning("[GameplayTestPipeline] 未能删除遗留的临时测试场景：" + path);
            }
            if (deletedCount > 0) Debug.Log($"[GameplayTestPipeline] 已清理 {deletedCount} 个遗留的临时测试场景及 meta。");
            return true;
        }

        private static void RunPhase(string runId, TestMode mode)
        {
            PipelineReport report = LoadCurrent();
            if (report == null || report.RunId != runId || report.Status != "running") return;
            // 两个入口可能分别来自 Editor.delayCall 和编译后恢复；阶段必须只消费一次。
            if (report.Phase != (mode == TestMode.EditMode ? "queued" : "playmode_queued")) return;
            // 资产刷新期间先等编译完成，不重新启动 Unity，也不浪费一次测试运行。
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Finish(report, "interrupted", "Unity 正在 PlayMode 或进入 PlayMode，不会打断当前游戏。");
                return;
            }
            try
            {
                if (mode == TestMode.PlayMode && !EnsureRestorableEditorScene(out string sceneError))
                {
                    Finish(report, "interrupted", sceneError);
                    return;
                }
                TryCleanupUnopenedTestRunnerBootstrapScenes(out _);
                EnsureCallbacks();
                report.Phase = mode == TestMode.EditMode ? "editmode" : "playmode";
                if (mode == TestMode.EditMode) report.EditMode.Status = "running";
                else report.PlayMode.Status = "running";
                Write(report);
                api.Execute(new ExecutionSettings(new Filter
                {
                    testMode = mode,
                    assemblyNames = new[] { mode == TestMode.EditMode ? EditAssembly : PlayAssembly }
                }));
            }
            catch (Exception exception)
            {
                Finish(report, "failed", "Test Framework 无法启动：" + exception);
            }
        }

        private static void Finish(PipelineReport report, string status, string reason)
        {
            report.Status = status;
            report.Phase = "finished";
            if (report.EditMode.Status == "running") report.EditMode.Status = "interrupted";
            if (report.PlayMode.Status == "running") report.PlayMode.Status = "interrupted";
            if (report.Rules.Status == "running") report.Rules.Status = "interrupted";
            report.Reason = reason;
            report.FinishedUtc = DateTime.UtcNow.ToString("O");
            FinalizeArtifacts(report);
            Write(report);
            GameplayTestSelection.ClearSuite(report.RunId);
            SessionState.SetString(PlayModeEnteredKey, string.Empty);
            SessionState.SetString(PlayModeExitKey, string.Empty);
            SessionState.SetString(PlayModeLostKey, string.Empty);
            CurrentRunId = string.Empty;
            ScheduleBootstrapSceneCleanupWhenIdle();
            Debug.Log($"[GameplayTestPipeline] {report.Status}：{report.Reason}，报告：{report.ReportPath}，测试包：{report.PackagePath}");
        }

        #region 本轮结果归集
        private static void FinalizeArtifacts(PipelineReport report)
        {
            report.RunDirectory = GameplayTestArtifacts.GetRunDirectory(report.RunId);
            report.SummaryPath = Path.Combine(report.RunDirectory, "report.md");
            report.PackagePath = report.RunDirectory;
            GameplayTestReport gameplay = null;
            try
            {
                // 只关联同一编号目录内且 JSON 编号匹配的报告，拒绝把上一轮报告挂到本轮。
                string expected = Path.Combine(report.RunDirectory, "report.json");
                if (File.Exists(expected))
                {
                    gameplay = JsonConvert.DeserializeObject<GameplayTestReport>(File.ReadAllText(expected, Encoding.UTF8));
                    if (gameplay == null || gameplay.RunId != report.RunId)
                        throw new InvalidDataException("玩法报告与本轮运行编号不匹配。");
                    report.GameplayReportPath = expected;
                }
                else
                {
                    gameplay = CreateUnstartedGameplayReport(report);
                    report.GameplayReportPath = gameplay.ReportPath;
                }
                CompleteInterruptedArtifacts(gameplay, report);
                if (report.ExecutionMode == "rules")
                {
                    // 中断也从已落盘的规则报告归集数量，不丢失已完成案例。
                    report.Rules.Status = report.Status;
                    report.Rules.Passed = gameplay.PassedCaseCount;
                    report.Rules.Failed = gameplay.FailedCaseCount;
                    report.Rules.Skipped = gameplay.SkippedCaseCount + gameplay.InterruptedCaseCount;
                    report.Rules.Cases.Clear();
                    foreach (GameplayTestCaseReport entry in gameplay.Cases)
                        if (entry.Status != "disabled")
                            report.Rules.Cases.Add(new CaseResult { Name = entry.Name,
                                Status = entry.Status == "passed" ? "Passed" : entry.Status == "failed" ? "Failed" : "Skipped",
                                Message = entry.Reason, DurationSeconds = entry.DurationSeconds });
                }
                gameplay.Write();
                // 对外按 Lua 用例和自动截图顺序返回本轮已落盘的现场图片。
                report.ScreenshotPaths.Clear();
                foreach (GameplayTestCaseReport testCase in gameplay.Cases)
                    foreach (GameplayTestScreenshotRecord screenshot in testCase.Screenshots)
                        if (!string.IsNullOrEmpty(screenshot.Path) && File.Exists(screenshot.Path))
                            report.ScreenshotPaths.Add(screenshot.Path);
                report.ArtifactError = gameplay.ArtifactError;
                GameplayTestArtifacts.WriteSummary(gameplay);
                string phaseSummary = BuildPipelineSummary(report);
                File.AppendAllText(report.SummaryPath, phaseSummary, new UTF8Encoding(false));
                WritePipelineArtifact(report);
                report.PackagePath = GameplayTestArtifacts.CreatePackage(report.RunDirectory);
            }
            catch (Exception exception)
            {
                report.ArtifactError = string.IsNullOrEmpty(report.ArtifactError)
                    ? exception.ToString() : report.ArtifactError + "\n" + exception;
                report.PackagePath = null;
                if (report.Status == "passed")
                {
                    report.Status = "failed";
                    report.Reason += " 测试产物未能完整保存，请检查 artifactError。";
                }
                try
                {
                    Directory.CreateDirectory(report.RunDirectory);
                    WritePipelineArtifact(report);
                    if (gameplay != null && gameplay.RunId == report.RunId)
                    {
                        gameplay.ArtifactError = report.ArtifactError;
                        gameplay.PackagePath = null;
                        gameplay.Write();
                        GameplayTestArtifacts.WriteSummary(gameplay);
                        File.AppendAllText(report.SummaryPath, BuildPipelineSummary(report), new UTF8Encoding(false));
                    }
                    else
                    {
                        report.SummaryPath = Path.Combine(report.RunDirectory, "pipeline-report.md");
                        File.WriteAllText(report.SummaryPath, BuildPipelineSummary(report), new UTF8Encoding(false));
                    }
                }
                catch (Exception writeFailure)
                {
                    report.ArtifactError += "\n失败报告也未能保存：" + writeFailure;
                }
                Debug.LogWarning("[GameplayTestPipeline] 本轮测试产物归集失败：" + exception);
            }
        }

        private static void WritePipelineArtifact(PipelineReport report)
            => File.WriteAllText(Path.Combine(report.RunDirectory, "pipeline.json"),
                JsonConvert.SerializeObject(report, Formatting.Indented,
                    new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }),
                new UTF8Encoding(false));

        private static void CompleteInterruptedArtifacts(GameplayTestReport gameplay, PipelineReport pipeline)
        {
            GameplayTestArtifacts.Initialize(gameplay);
            if (gameplay.Status == "running")
            {
                gameplay.Status = pipeline.Status == "failed" ? "failed" : "interrupted";
                gameplay.FinishedUtc = pipeline.FinishedUtc;
            }
            gameplay.PassedCaseCount = gameplay.FailedCaseCount = gameplay.SkippedCaseCount = gameplay.InterruptedCaseCount = 0;
            foreach (GameplayTestCaseReport testCase in gameplay.Cases)
            {
                if (testCase.Status == "running" || testCase.Status == "pending")
                {
                    testCase.Status = testCase.Status == "running" ? "interrupted" : "skipped";
                    testCase.Reason = pipeline.Reason;
                    testCase.FinishedUtc = pipeline.FinishedUtc;
                }
                testCase.ReportPath = GameplayTestArtifacts.CaseReportPath(gameplay, testCase.Id);
                foreach (GameplayTestScreenshotRecord screenshot in testCase.Screenshots)
                    if (!File.Exists(screenshot.Path) && string.IsNullOrEmpty(screenshot.Error))
                        screenshot.Error = "自动截图已开始，但报告收尾时未找到截图文件。";
                if (testCase.Status == "passed") gameplay.PassedCaseCount++;
                if (testCase.Status == "failed") gameplay.FailedCaseCount++;
                if (testCase.Status == "skipped") gameplay.SkippedCaseCount++;
                if (testCase.Status == "interrupted") gameplay.InterruptedCaseCount++;
                Directory.CreateDirectory(Path.GetDirectoryName(testCase.ReportPath));
                File.WriteAllText(testCase.ReportPath, JsonConvert.SerializeObject(testCase, Formatting.Indented,
                    new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }),
                    new UTF8Encoding(false));
            }
        }

        private static GameplayTestReport CreateUnstartedGameplayReport(PipelineReport pipeline)
        {
            var report = new GameplayTestReport
            {
                RunId = pipeline.RunId, TestSuitePath = pipeline.SuitePath,
                StartedUtc = pipeline.StartedUtc, FinishedUtc = pipeline.FinishedUtc,
                Status = pipeline.Status, CurrentStage = "not_started"
            };
            GameplayTestArtifacts.Initialize(report);
            GameplayTestSuite suite = null;
            try
            {
                suite = GameplayTestSuite.Parse(File.ReadAllText(
                    GameplayTestSelection.GetAbsoluteSuitePath(pipeline.SuitePath), Encoding.UTF8));
            }
            catch (Exception exception) { report.ArtifactError = "收尾时无法读取用例清单：" + exception; }
            foreach (string caseId in pipeline.SelectedCaseIds)
            {
                GameplayTestCase configured = suite?.Cases.Find(entry => entry.Id == caseId);
                var testCase = new GameplayTestCaseReport
                {
                    Id = caseId, Name = configured?.Name ?? caseId, LuaPath = configured?.LuaPath,
                    Status = "skipped", Reason = pipeline.Reason, FinishedUtc = pipeline.FinishedUtc,
                    ReportPath = GameplayTestArtifacts.CaseReportPath(report, caseId)
                };
                report.Cases.Add(testCase);
                report.SkippedCaseCount++;
            }
            return report;
        }

        private static string BuildPipelineSummary(PipelineReport report)
        {
            var text = new StringBuilder();
            text.AppendLine();
            text.AppendLine(report.ExecutionMode == "rules" ? "## 编辑态 Lua 规则流水线" : "## Test Framework 流水线");
            text.AppendLine();
            text.AppendLine("- 流水线结果：**" + report.Status + "**");
            text.AppendLine("- 说明：" + (report.Reason ?? string.Empty).Replace("\n", "<br>"));
            text.AppendLine("- [完整流水线结果](pipeline.json)");
            text.AppendLine();
            text.AppendLine("| 阶段 | 结果 | 通过 | 失败 | 跳过 | 不确定 |");
            text.AppendLine("| --- | --- | --- | --- | --- | --- |");
            if (report.ExecutionMode == "rules") AppendPhaseSummary(text, "Lua 规则", report.Rules);
            else
            {
                AppendPhaseSummary(text, "EditMode", report.EditMode);
                AppendPhaseSummary(text, "PlayMode", report.PlayMode);
            }
            text.AppendLine();
            AppendFailures(text, "EditMode", report.EditMode);
            AppendFailures(text, "PlayMode", report.PlayMode);
            AppendFailures(text, "Lua 规则", report.Rules);
            return text.ToString();
        }

        private static void AppendPhaseSummary(StringBuilder text, string name, PhaseResult phase)
            => text.AppendLine($"| {name} | {phase.Status} | {phase.Passed} | {phase.Failed} | {phase.Skipped} | {phase.Inconclusive} |");

        private static void AppendFailures(StringBuilder text, string name, PhaseResult phase)
        {
            foreach (CaseResult testCase in phase.Cases)
            {
                if (testCase.Status?.StartsWith("Passed", StringComparison.OrdinalIgnoreCase) == true) continue;
                text.AppendLine("- " + name + "：`" + testCase.Name + "`（" + testCase.Status + "）");
                text.AppendLine("  " + (testCase.Message ?? string.Empty).Replace("\n", "<br>"));
            }
        }
        #endregion

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result == null || result.HasChildren) return;
                PipelineReport report = LoadCurrent();
                if (report == null || report.Status != "running") return;
                PhaseResult phase = report.Phase == "editmode" ? report.EditMode :
                    report.Phase == "playmode" ? report.PlayMode : null;
                if (phase == null) return;
                var entry = new CaseResult
                {
                    Name = result.FullName, Status = result.ResultState,
                    Message = result.Message, StackTrace = result.StackTrace,
                    DurationSeconds = result.Duration
                };
                // 重复注册的 TestRunner 回调不应在报告中重复计入同一测试。
                int previous = phase.Cases.FindIndex(existing => existing.Name == entry.Name);
                if (previous < 0) phase.Cases.Add(entry);
                else phase.Cases[previous] = entry;
                Write(report);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                ScheduleBootstrapSceneCleanupWhenIdle();
                PipelineReport report = LoadCurrent();
                if (report == null || report.Status != "running" || result == null) return;
                bool edit = report.Phase == "editmode";
                if (!edit && report.Phase != "playmode") return;
                PhaseResult phase = edit ? report.EditMode : report.PlayMode;
                phase.Passed = result.PassCount;
                phase.Failed = result.FailCount;
                phase.Skipped = result.SkipCount;
                phase.Inconclusive = result.InconclusiveCount;
                bool allPassed = result.PassCount > 0 && result.FailCount == 0 &&
                                 result.SkipCount == 0 && result.InconclusiveCount == 0;
                phase.Status = allPassed ? "passed" : "failed";
                Write(report);
                if (!allPassed)
                {
                    Finish(report, "failed",
                        (edit ? "EditMode" : "PlayMode") +
                        $" 未全部通过：{phase.Failed} 失败、{phase.Skipped} 跳过、{phase.Inconclusive} 不确定。");
                    return;
                }
                if (edit)
                {
                    report.Phase = "playmode_queued";
                    Write(report);
                    string id = report.RunId;
                    EditorApplication.delayCall += () => RunPhase(id, TestMode.PlayMode);
                }
                else
                    Finish(report, "passed", report.PlayModeOnly ?
                        "PlayMode 全部通过；本轮未运行 EditMode 测试。" :
                        "EditMode 全部通过，PlayMode 全部通过。");
            }
        }
        #endregion
    }
}
