using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace FlatWorld.GameplayMCP
{
    /// <summary>一个 Unity Editor 进程内串行执行 EditMode → PlayMode，失败阻断后续阶段。</summary>
    [InitializeOnLoad]
    internal static class GameplayTestPipelineCoordinator
    {
        #region 文件与生命周期
        private const string SessionRunKey = "FlatWorld.GameplayTestPipeline.ActiveRun";
        private const string PlayModeExitKey = "FlatWorld.GameplayTestPipeline.PlayModeExitUtc";
        private const string EditAssembly = "FlatWorld.Gameplay.EditModeTests";
        private const string PlayAssembly = "FlatWorld.Gameplay.PlayModeTests";
        private static TestRunnerApi api;
        private static Callbacks callbacks;
        private static double nextRequestPoll;

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
            public List<string> SelectedCaseIds = new();
            public string ReportPath;
            public string StartedUtc;
            public string FinishedUtc;
            public string Status = "running";
            public string Phase = "queued";
            public string Reason;
            public string GameplayReportPath;
            public PhaseResult EditMode = new();
            public PhaseResult PlayMode = new();
        }

        static GameplayTestPipelineCoordinator()
        {
            EditorApplication.update += PollLocalRequests;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            // PlayMode 域重载后补登记同一个监听器，避免丢失最终报告。
            EditorApplication.delayCall += () =>
            {
                if (!string.IsNullOrEmpty(SessionState.GetString(SessionRunKey, string.Empty)))
                {
                    EnsureCallbacks();
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
                response = Start(command.Value<string>("suitePath"), selectedIds);
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
            if (change != PlayModeStateChange.ExitingPlayMode) return;
            PipelineReport report = LoadCurrent();
            if (report?.Status == "running" && report.Phase == "playmode")
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
                return;
            }
            bool knownExit = DateTime.TryParse(exitText, out DateTime exitedAt);
            // 异常重编译可能丢掉退出事件；超时后仍要自动清掉僵死运行状态。
            DateTime.TryParse(report.StartedUtc, out DateTime startedAt);
            bool stale = !knownExit && startedAt != default &&
                         DateTime.UtcNow - startedAt.ToUniversalTime() > TimeSpan.FromMinutes(4);
            if (knownExit &&
                DateTime.UtcNow - exitedAt.ToUniversalTime() < TimeSpan.FromSeconds(12) || !knownExit && !stale)
                return;
            if (report.Status == "running")
                Finish(report, "interrupted", "PlayMode 已退出，但 Test Framework 未返回最终结果；请查看场景报告与 Unity Console。");
            SessionState.SetString(PlayModeExitKey, string.Empty);
        }

        private static void ResumeQueuedPhase()
        {
            PipelineReport report = LoadCurrent();
            if (report == null || report.Status != "running") return;
            if (report.Phase == "queued")
                RunPhase(report.RunId, TestMode.EditMode);
            else if (report.Phase == "playmode_queued")
                RunPhase(report.RunId, TestMode.PlayMode);
        }
        #endregion

        #region AI 和菜单入口
        internal static JObject Start(string suitePath, IReadOnlyCollection<string> selectedCaseIds = null)
        {
            PipelineReport active = LoadCurrent();
            if (active != null && active.Status == "running")
                return Reply(false, active, "已有一轮测试正在执行，请读取状态，不要重复启动。");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Reply(false, null, "当前 Unity 处于 PlayMode 或正在切换，不会打断你的游戏现场。");
            suitePath = string.IsNullOrWhiteSpace(suitePath) ? GameplayTestSelection.QuickSuitePath : suitePath;
            try
            {
                string path = GameplayTestSelection.GetAbsoluteSuitePath(suitePath);
                GameplayTestSuite suite = GameplayTestSuite.Parse(File.ReadAllText(path, Encoding.UTF8));
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
                    RunId = runId, SuitePath = suitePath, StartedUtc = DateTime.UtcNow.ToString("O"),
                    SelectedCaseIds = casesToRun
                };
                GameplayTestSelection.SelectSuite(suitePath, runId, selectedCaseIds);
                CurrentRunId = runId;
                Write(report);
                // 编译中先接单保存现场，待 Unity 稳定后由更新循环启动，无需 Agent 重发请求。
                if (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
                    EnsureCallbacks();
                // 让本次 MCP 请求先返回，测试在 Editor 后续更新中自行跑完。
                EditorApplication.delayCall += () => RunPhase(runId, TestMode.EditMode);
                return Reply(true, report, "已排队执行 EditMode 测试，全部通过后才进入 PlayMode。");
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

        [MenuItem("FlatWorld/测试/快速执行 EditMode 然后 PlayMode")]
        private static void MenuRun()
        {
            JObject response = Start(GameplayTestSelection.QuickSuitePath);
            Debug.Log("[GameplayTestPipeline] " + response.ToString(Formatting.None));
        }

        [MenuItem("FlatWorld/测试/查看最近报告")]
        private static void MenuReport()
            => Debug.Log("[GameplayTestPipeline] " + Status(false).ToString(Formatting.Indented));

        private static JObject Reply(bool ok, PipelineReport report, string message)
        {
            var result = new JObject
            {
                ["ok"] = ok,
                ["message"] = message,
                ["runId"] = report?.RunId,
                ["status"] = report?.Status,
                ["phase"] = report?.Phase,
                ["suitePath"] = report?.SuitePath,
                ["selectedCaseIds"] = report == null ? null : new JArray(report.SelectedCaseIds),
                ["reportPath"] = report?.ReportPath,
                ["gameplayReportPath"] = report?.GameplayReportPath,
                ["reason"] = report?.Reason,
                ["editMode"] = report == null ? null : JObject.FromObject(report.EditMode),
                ["playMode"] = report == null ? null : JObject.FromObject(report.PlayMode)
            };
            if (!ok || report?.Status == "failed")
                result["nextAction"] = "读取失败用例和堆栈 → 修复生产代码或清单 → 重新调用 run；不要原样无限重试。";
            return result;
        }
        #endregion

        #region 分阶段调度
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
            report.Reason = reason;
            report.FinishedUtc = DateTime.UtcNow.ToString("O");
            if (File.Exists(GameplayTestSelection.LastGameplayReportPath))
            {
                string gameplayPath = File.ReadAllText(GameplayTestSelection.LastGameplayReportPath, Encoding.UTF8).Trim();
                if (File.Exists(gameplayPath)) report.GameplayReportPath = gameplayPath;
            }
            Write(report);
            GameplayTestSelection.ClearSuite(report.RunId);
            SessionState.SetString(PlayModeExitKey, string.Empty);
            CurrentRunId = string.Empty;
            Debug.Log($"[GameplayTestPipeline] {status}：{reason}，报告：{report.ReportPath}");
        }

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
                    Finish(report, "passed", "EditMode 全部通过，PlayMode 全部通过。");
            }
        }
        #endregion
    }
}
