using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace FlatWorld.GameplayMCP
{
    internal static partial class GameplayTestPipelineCoordinator
    {
        #region 快速规则调度
        private static IEnumerator rulesFlow;
        private static string rulesRunId;
        private static Stopwatch rulesTimer;

        [InitializeOnLoadMethod]
        private static void RegisterRulesCleanup()
        {
            // 域重载或退出前显式展开 finally，避免编辑态 Lua VM 留在原生内存。
            AssemblyReloadEvents.beforeAssemblyReload -= InterruptRulesFlow;
            AssemblyReloadEvents.beforeAssemblyReload += InterruptRulesFlow;
            EditorApplication.quitting -= InterruptRulesFlow;
            EditorApplication.quitting += InterruptRulesFlow;
        }

        private static void StartRulesPhase(string runId)
        {
            PipelineReport pipeline = LoadCurrent();
            if (pipeline == null || pipeline.RunId != runId || pipeline.Status != "running" || pipeline.Phase != "queued") return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Finish(pipeline, "interrupted", "Unity 正在 PlayMode，不会改变当前游戏现场。");
                return;
            }
            try
            {
                GameplayTestSuite suite = GameplayTestSuite.Parse(File.ReadAllText(
                    GameplayTestSelection.GetAbsoluteSuitePath(pipeline.SuitePath), Encoding.UTF8));
                GameplayTestSelection.ApplySelectedCases(suite, pipeline.SuitePath);
                if (suite.ExecutionMode != "rules") throw new InvalidOperationException("快速入口只接受 rules 清单。");
                pipeline.Phase = "rules";
                pipeline.Rules.Status = "running";
                pipeline.EditMode.Status = pipeline.PlayMode.Status = "not_requested";
                Write(pipeline);
                rulesRunId = runId;
                rulesTimer = Stopwatch.StartNew();
                rulesFlow = GameplayTestLuaRunner.Advance(GameplayTestRulesSuiteRunner.Run(suite, pipeline.SuitePath, runId, result =>
                {
                    pipeline.Rules.Status = result.Status;
                    pipeline.Rules.Passed = result.PassedCaseCount;
                    pipeline.Rules.Failed = result.FailedCaseCount;
                    pipeline.Rules.Skipped = result.SkippedCaseCount;
                    pipeline.GameplayReportPath = result.ReportPath;
                    foreach (GameplayTestCaseReport entry in result.Cases)
                    {
                        if (entry.Status == "disabled") continue;
                        pipeline.Rules.Cases.Add(new CaseResult { Name = entry.Name,
                            Status = entry.Status == "passed" ? "Passed" : entry.Status == "failed" ? "Failed" : "Skipped",
                            Message = entry.Reason, DurationSeconds = entry.DurationSeconds });
                    }
                    Finish(pipeline, result.Status, result.Status == "passed" ?
                        "选中的 Lua 规则全部通过；本轮没有进入 PlayMode。" : "Lua 规则失败，请读取原始采样和断言堆栈。");
                }), failure => Finish(pipeline, "failed", "快速规则执行异常：" + failure));
                EditorApplication.update -= PumpRules;
                EditorApplication.update += PumpRules;
            }
            catch (Exception exception) { Finish(pipeline, "failed", "快速规则无法启动：" + exception); }
        }

        private static void PumpRules()
        {
            PipelineReport pipeline = null;
            try
            {
                pipeline = LoadCurrent();
                if (rulesFlow == null || pipeline == null || pipeline.RunId != rulesRunId || pipeline.Status != "running")
                { ReleaseRulesFlow(); return; }
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || rulesTimer.Elapsed.TotalSeconds > 120d)
                {
                    FinishInterruptedRules(pipeline, "快速规则因编译、资源更新、PlayMode 切换或超过 120 秒上限中断。");
                    return;
                }
                // 单次编辑器更新限制调度预算，参数组合多时仍保持窗口可以操作。
                var budget = Stopwatch.StartNew();
                while (budget.Elapsed.TotalMilliseconds < 4d)
                    if (!rulesFlow.MoveNext()) { ReleaseRulesFlow(); break; }
            }
            catch (Exception exception)
            {
                Exception failure = exception;
                try { ReleaseRulesFlow(); }
                catch (Exception cleanupFailure) { failure = new AggregateException(exception, cleanupFailure); }
                if (pipeline?.Status == "running") Finish(pipeline, "failed", "快速规则调度异常：" + failure);
                else UnityEngine.Debug.LogException(failure);
            }
        }

        private static void InterruptRulesFlow()
        {
            if (rulesFlow == null) return;
            PipelineReport pipeline = LoadCurrent();
            FinishInterruptedRules(pipeline, "快速规则因编辑器域重载或退出中断。");
        }

        private static void FinishInterruptedRules(PipelineReport pipeline, string reason)
        {
            try { ReleaseRulesFlow(); }
            catch (Exception exception) { reason += " 清理失败：" + exception; }
            if (pipeline?.Status == "running") Finish(pipeline, "interrupted", reason);
        }

        private static void ReleaseRulesFlow()
        {
            EditorApplication.update -= PumpRules;
            IEnumerator flow = rulesFlow;
            rulesFlow = null;
            rulesRunId = null;
            rulesTimer?.Stop();
            rulesTimer = null;
            (flow as IDisposable)?.Dispose();
        }

        [MenuItem("FlatWorld/测试/快速执行全部系统规则（不进入PlayMode）")]
        private static void MenuRunAllRules()
        {
            JObject reply = Start("GameplayTests/all-systems-rules.json");
            UnityEngine.Debug.Log("[GameplayTestRules] " + reply);
        }
        #endregion
    }
}
