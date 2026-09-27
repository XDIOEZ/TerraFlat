using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// 区块流送只读诊断入口：按需快照或 1～20 秒有界采样，仅结束时输出一条日志和一份 JSON。
    /// 不解暂停、不申请控制、不改变视距/并发/提交预算，不把主线程等待误报为 GPU 或 CPU 利用率。
    /// </summary>
    [InitializeOnLoad]
    internal static class GameplayChunkStreamingDiagnostics
    {
        #region 快照与暂停标记

        private static bool sampling;

        static GameplayChunkStreamingDiagnostics()
        {
            EditorApplication.pauseStateChanged += HandlePauseChanged;
            HandlePauseChanged(EditorApplication.isPaused ? PauseState.Paused : PauseState.Unpaused);
        }

        private static void HandlePauseChanged(PauseState state) =>
            ChunkMgr.ExistingInstance?.WorldRuntime?.StreamingDiagnostics.SetSuspended(state == PauseState.Paused);

        internal static JObject Capture(ChunkMgr manager)
        {
            // Unity 已销毁对象不等于 C# null，避免诊断本身触发旧 Player/管理器引用异常。
            if (manager == null) manager = null;
            ChunkStreamingDiagnostics diagnostics = manager?.WorldRuntime?.StreamingDiagnostics;
            diagnostics?.SetSuspended(EditorApplication.isPaused);
            return new JObject
            {
                ["schema"] = "chunk-streaming-diagnostics/1",
                ["playing"] = EditorApplication.isPlaying,
                ["paused"] = EditorApplication.isPaused,
                ["frame"] = Time.frameCount,
                ["applicationFocused"] = Application.isFocused,
                ["runInBackground"] = Application.runInBackground,
                ["targetFrameRate"] = Application.targetFrameRate,
                ["vSyncCount"] = QualitySettings.vSyncCount,
                ["worldEpoch"] = manager?.WorldRuntime?.Epoch ?? 0,
                ["driver"] = manager != null ? JObject.FromObject(manager.CaptureStreamingDriverDiagnostics()) : null,
                ["pending"] = manager?.RuntimeChunks != null ? JObject.FromObject(manager.RuntimeChunks.CapturePendingStreamingDiagnostics()) : null,
                ["timings"] = diagnostics != null ? JObject.FromObject(diagnostics.Capture()) : null,
                ["generationQueued"] = manager?.RuntimeChunks?.QueuedGenerationCount ?? 0,
                ["generationActive"] = manager?.RuntimeChunks?.ActiveGenerationCount ?? 0,
                ["pendingCommits"] = manager?.RuntimeChunks?.PendingCommitCount ?? 0,
                ["pendingPresentations"] = manager?.PendingRuntimeChunkPresentationCount ?? 0,
                ["gpuTiming"] = "not_measured",
                ["timingNotes"] = "Stopwatch 墙钟毫秒，不是 CPU 占用率。queue_wait/latency 是等待；work/同步阶段是执行耗时。父子阶段不能相加。recent 为最近128次，total 为会话累计。暂停跨越的请求不计入队列/生成耗时汇总，原始时间线用 PauseAffected 标出；新埋点不补造此前历史。"
            };
        }

        #endregion

        #region 有界采样

        internal static async Task<object> Sample(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
                return new ErrorResponse("Sample duration must be finite.");
            ChunkMgr manager = ChunkMgr.ExistingInstance;
            if (!EditorApplication.isPlaying || manager == null || manager.WorldRuntime == null)
                return new ErrorResponse("请先进入真实游戏世界；不会自动启动或切换存档。");
            if (EditorApplication.isPaused)
                return new ErrorResponse("现场已暂停：只能读取 status，采样不会自行解除暂停。");
            if (sampling) return new ErrorResponse("已有区块采样正在执行。");

            sampling = true;
            try
            {
                using var window = new SampleWindow(manager, Mathf.Clamp(seconds, 1f, 20f));
                await window.Completion.Task;
                JObject report = window.BuildReport();
                Export(report);
                Debug.Log(BuildSummary(report));
                return new SuccessResponse("区块分阶段采样完成；未改动游戏加载参数。", report);
            }
            finally { sampling = false; }
        }

        /// <summary>只在 Editor 更新观察帧号与队列；完整 JSON 仅在采样前后各创建一次。</summary>
        private sealed class SampleWindow : IDisposable
        {
            private readonly ChunkMgr manager;
            private readonly WorldRuntime world;
            private readonly float duration;
            private readonly double start = EditorApplication.timeSinceStartup;
            private readonly JObject before;
            private readonly List<double> observedFrames = new List<double>(1024);
            private readonly List<string> issues = new List<string>();
            private readonly int pauseRevision;
            private int previousFrame = Time.frameCount;
            private int maxCommits, maxGeneration, maxPresentation, errors, warnings;
            private string stopReason = "duration_complete";
            public readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>();

            public SampleWindow(ChunkMgr manager, float duration)
            {
                this.manager = manager;
                this.duration = duration;
                world = manager.WorldRuntime;
                before = Capture(manager);
                pauseRevision = world.StreamingDiagnostics.PauseRevision;
                maxCommits = manager.RuntimeChunks.PendingCommitCount;
                maxGeneration = manager.RuntimeChunks.QueuedGenerationCount;
                maxPresentation = manager.PendingRuntimeChunkPresentationCount;
                EditorApplication.update += Observe;
                AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                Application.logMessageReceived += OnLog;
            }

            private void Observe()
            {
                if (!EditorApplication.isPlaying || manager == null || !ReferenceEquals(world, manager.WorldRuntime))
                    stopReason = "world_changed";
                else if (EditorApplication.isPaused || world.StreamingDiagnostics.PauseRevision != pauseRevision)
                    stopReason = "pause_interrupted";
                else if (EditorApplication.isCompiling)
                    stopReason = "compilation_interrupted";
                else
                {
                    maxCommits = Math.Max(maxCommits, manager.RuntimeChunks.PendingCommitCount);
                    maxGeneration = Math.Max(maxGeneration, manager.RuntimeChunks.QueuedGenerationCount);
                    maxPresentation = Math.Max(maxPresentation, manager.PendingRuntimeChunkPresentationCount);
                    if (previousFrame != Time.frameCount)
                    {
                        previousFrame = Time.frameCount;
                        if (observedFrames.Count < 4096) observedFrames.Add(Time.unscaledDeltaTime * 1000d);
                    }
                    if (EditorApplication.timeSinceStartup - start < duration) return;
                }
                Completion.TrySetResult(true);
            }

            private void BeforeReload()
            {
                stopReason = "domain_reload_interrupted";
                Completion.TrySetResult(true);
            }

            private void OnLog(string message, string trace, LogType type)
            {
                bool error = type == LogType.Error || type == LogType.Assert || type == LogType.Exception;
                if (error) errors++;
                if (type == LogType.Warning) warnings++;
                if ((error || type == LogType.Warning) && issues.Count < 8) issues.Add(message);
            }

            public JObject BuildReport()
            {
                JObject after = Capture(manager);
                double seconds = Math.Max(0.001d, EditorApplication.timeSinceStartup - start);
                int frames = Math.Max(0, Time.frameCount - (before["frame"]?.Value<int>() ?? Time.frameCount));
                bool sameSession = (string)before["timings"]?["sessionId"] == (string)after["timings"]?["sessionId"] &&
                    (long?)before["worldEpoch"] == (long?)after["worldEpoch"];
                var deltaCounters = new JObject();
                var intervalTimings = new JArray();
                if (sameSession)
                {
                    foreach (JProperty counter in ((JObject)after["timings"]["counters"]).Properties())
                        deltaCounters[counter.Name] = counter.Value.Value<long>() -
                            (before["timings"]?["counters"]?[counter.Name]?.Value<long>() ?? 0);
                    var previous = ((JArray)before["timings"]["metrics"]).ToDictionary(m => (string)m["stage"]);
                    foreach (JToken metric in (JArray)after["timings"]["metrics"])
                    {
                        previous.TryGetValue((string)metric["stage"], out JToken old);
                        long count = metric["totalCount"].Value<long>() - (old?["totalCount"]?.Value<long>() ?? 0);
                        if (count <= 0) continue;
                        double total = metric["totalMs"].Value<double>() - (old?["totalMs"]?.Value<double>() ?? 0d);
                        intervalTimings.Add(new JObject { ["stage"] = metric["stage"], ["count"] = count,
                            ["totalMs"] = total, ["averageMs"] = total / count });
                    }
                }
                observedFrames.Sort();
                var report = new JObject
                {
                    ["schema"] = "chunk-streaming-sample/1", ["stopReason"] = stopReason,
                    ["validUnpausedInterval"] = sameSession && stopReason == "duration_complete" && frames > 0,
                    ["elapsedSeconds"] = seconds, ["framesAdvanced"] = frames,
                    ["averageFps"] = frames / seconds, ["observedFrameSamples"] = observedFrames.Count,
                    ["observedFrameP95Ms"] = Percentile(0.95), ["observedFrameMaxMs"] = Percentile(1),
                    ["frameNotes"] = "Editor update 可能跳过部分渲染帧；观测分位数不冒充完整 PlayerLoop/GPU 计时。",
                    ["maxPendingCommits"] = maxCommits, ["maxGenerationQueued"] = maxGeneration,
                    ["maxPendingPresentations"] = maxPresentation,
                    ["counterDeltas"] = deltaCounters, ["intervalTimings"] = intervalTimings,
                    ["intervalTimingNotes"] = "intervalTimings 按本段完成的阶段统计，该阶段可能在采样前已开始；totalMs 不等于本时间段 CPU 忙碌时间。分位数在 before/after.timings 中，覆盖各自最近128次，并非严格采样窗口分位数。",
                    ["errors"] = errors, ["warnings"] = warnings, ["issues"] = new JArray(issues),
                    ["before"] = before, ["after"] = after
                };
                report["evidence"] = BuildEvidence(report);
                return report;
            }

            private double Percentile(double fraction) => observedFrames.Count == 0 ? 0d :
                observedFrames[Math.Max(0, (int)Math.Ceiling(observedFrames.Count * fraction) - 1)];

            public void Dispose()
            {
                EditorApplication.update -= Observe;
                AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
                Application.logMessageReceived -= OnLog;
            }
        }

        #endregion

        #region 报告与菜单

        /// <summary>仅报告已发生的堵塞证据，不用单个快照推断 CPU 满载或 GPU 慢。</summary>
        private static JArray BuildEvidence(JObject report)
        {
            var evidence = new JArray();
            if (report["validUnpausedInterval"]?.Value<bool>() != true)
            {
                evidence.Add("采样被暂停/换世界/编译中断，或没有实际游戏帧推进，不能用本段墙钟等待判定性能瓶颈。");
                return evidence;
            }
            JObject driver = (JObject)report["after"]?["driver"];
            JObject counters = (JObject)report["counterDeltas"];
            if (driver?["hostBoundToManager"]?.Value<bool>() != true)
                evidence.Add("WorldRuntimeHost 未绑定当前 ChunkMgr：先排查提交驱动失联，不能先归因 CPU 算力。");
            long advanceDelta = (driver?["advanceCount"]?.Value<long>() ?? 0) -
                (report["before"]?["driver"]?["advanceCount"]?.Value<long>() ?? 0);
            if (report["framesAdvanced"].Value<int>() > 1 && advanceDelta == 0 && report["maxPendingCommits"].Value<int>() > 0)
                evidence.Add("画面帧在推进，但区块 Advance 一次也没执行，且存在待提交结果。");
            if ((counters?["commit.count_limit_hits"]?.Value<long>() ?? 0) > 0)
                evidence.Add("提交实际触及每帧数量上限且队列仍非空；结合 commit.process 平均耗时判断是否预算过紧。");
            long discarded = (counters?["commit.stale"]?.Value<long>() ?? 0) +
                (counters?["commit.cancelled"]?.Value<long>() ?? 0);
            if (discarded > 0) evidence.Add($"{discarded} 个提交名额用于清理过期/取消结果，没有产生新区块。");
            if ((counters?["view.start_time_limit_hits"]?.Value<long>() ?? 0) > 0 ||
                (counters?["view.step_time_limit_hits"]?.Value<long>() ?? 0) > 0)
                evidence.Add("主线程表现队列实际触及时长预算；检查 renderer.* 与 restore.*，不是直接证明 GPU 画不过来。");
            if (report["after"]?["pending"]?["completedValid"]?.Value<int>() > 0)
                evidence.Add("仍有有效生成结果等待主线程提交；pending.completedStale/Cancelled/Failed 已分开统计。");
            if (evidence.Count == 0) evidence.Add("本段没有命中上述堵塞条件；查看分阶段耗时或在移动缺块时重新采样，不能据此宣称加载正常。");
            return evidence;
        }

        private static string BuildSummary(JObject report)
        {
            var text = new StringBuilder("[ChunkStreamingDebug] ");
            text.AppendLine((string)report["stopReason"] ?? "当前快照");
            if (report["intervalTimings"] is JArray stages)
                foreach (JToken stage in stages)
                    text.AppendLine($"{stage["stage"]}: {stage["count"]}次，平均{stage["averageMs"].Value<double>():F2}ms");
            if (report["evidence"] is JArray evidence)
                foreach (JToken entry in evidence) text.AppendLine(entry.ToString());
            text.Append("完整报告：").Append((string)report["reportPath"]);
            return text.ToString();
        }

        private static void Export(JObject report)
        {
            try
            {
                string folder = "Library/FlatWorldGameplayMCP/ChunkDiagnostics";
                Directory.CreateDirectory(folder);
                string path = $"{folder}/chunk-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json";
                report["reportPath"] = path;
                File.WriteAllText(path, report.ToString(Formatting.Indented));
            }
            catch (Exception exception) { report["exportError"] = exception.Message; }
        }

        [MenuItem("FlatWorld/调试/区块加载/输出当前诊断（支持暂停）")]
        private static void DumpMenu()
        {
            JObject report = JObject.FromObject(GameplayChunkRenderDebugTool.ReadStatus());
            Export(report);
            Debug.Log("[ChunkStreamingDebug] " + report.ToString(Formatting.Indented));
        }

        [MenuItem("FlatWorld/调试/区块加载/采样10秒并输出报告")]
        private static async void SampleMenu()
        {
            try
            {
                object result = await Sample(10f);
                JObject response = JObject.FromObject(result);
                if (response["success"]?.Value<bool>() != true)
                    Debug.Log("[ChunkStreamingDebug] " + response.ToString(Formatting.None));
            }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        #endregion
    }
}
