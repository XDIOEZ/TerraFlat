using System;
using System.IO;
using System.Reflection;
using FlatWorld.Automation;
using FlatWorld.Diagnostics;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;

namespace FlatWorld.GameplayMCP
{
    [InitializeOnLoad]
    [McpForUnityTool("gameplay_profiler", Description =
        "Profiler capture and full performance evidence export. status reads job/result and recording state; start begins a non-blocking 1-120 second capture (default 10), then automatically exports the slowest retained frame; keep using gameplay while recording. stop finishes early and exports; cancel restores switches without exporting. export reads existing latest/selected/slowest buffered frame or a 1-based displayFrame. Does not open windows, pause, change target/deep profiling, clear history or reset statistics. Reports include profiler, Console, local world and Unity main-window screenshot when available; these current local attachments do not reconstruct historical/remote frames. Poll status until state=completed/failed/cancelled; success of start only means recording began.",
        Group = "core", Discoverable = false)]
    public static class GameplayProfilerTool
    {
        #region 协议与采集状态

        public sealed class Parameters : GameplayMcpOutputParameters
        {
            [ToolParameter("status, start, stop, cancel or export", Required = false, DefaultValue = "status")]
            public string action { get; set; }
            [ToolParameter("start: real seconds before automatic export, 1-120", Required = false, DefaultValue = "10")]
            public double seconds { get; set; }
            [ToolParameter("start: temporarily enable project performance statistics without clearing them", Required = false, DefaultValue = "true")]
            public bool enableStatistics { get; set; }
            [ToolParameter("export: latest, selected or slowest retained frame", Required = false, DefaultValue = "latest")]
            public string frame { get; set; }
            [ToolParameter("export: explicit 1-based Profiler display frame, overrides frame", Required = false)]
            public int? displayFrame { get; set; }
            [ToolParameter("start/export: nearby-frame radius, 0-300", Required = false, DefaultValue = "30")]
            public int contextFrameRadius { get; set; }
        }

        private const string ResultKey = "FlatWorld.GameplayMCP.Profiler.Result";
        private static readonly PropertyInfo ConnectedProfiler = typeof(ProfilerDriver).GetProperty("connectedProfiler",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        private static Capture active;
        private static JObject result;

        private sealed class Capture
        {
            internal string Id, StartedUtc, Target;
            internal double Started, Seconds;
            internal int FirstFrame, Radius;
            internal bool RecordingBefore, StatisticsBefore, StatisticsExpected, DeepProfiling, ProfileEditor;
            internal JArray StatisticsBeforeSnapshot;
        }

        static GameplayProfilerTool()
        {
            GameplayTestProfilerBridge.Backend = new LuaProfilerBackend();
            string saved = SessionState.GetString(ResultKey, "");
            if (!string.IsNullOrEmpty(saved))
            {
                try { result = JObject.Parse(saved); }
                catch (JsonException) { SessionState.EraseString(ResultKey); }
            }
            AssemblyReloadEvents.beforeAssemblyReload += () => Interrupt("domain_reload: 脚本重载中断采集。");
            EditorApplication.quitting += () => Interrupt("editor_quitting: 编辑器关闭中断采集。");
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode)
                    Interrupt("play_mode_changed: 运行状态改变，采集已中断。");
            };
        }

        public static object HandleCommand(JObject parameters)
            => GameplayMcpOutput.Invoke("gameplay_profiler", parameters, args =>
            {
                try { return ExecuteCommand(args); }
                catch (Exception exception) { return new ErrorResponse("profiler_request_failed: " + exception.GetBaseException().Message); }
            });

        private static object ExecuteCommand(JObject args)
        {
            string action = args?["action"]?.ToString()?.Trim().ToLowerInvariant() ?? "status";
            switch (action)
            {
                case "status": return new SuccessResponse("Profiler capture status.", Status());
                case "start": return Start(args);
                case "stop":
                    if (active == null) return new ErrorResponse("profiler_not_capturing: 当前没有 MCP 采集任务。");
                    Finish();
                    return ResultResponse();
                case "cancel":
                    if (active == null) return new ErrorResponse("profiler_not_capturing: 当前没有 MCP 采集任务。");
                    Interrupt("cancelled_by_client: 已取消采集，未导出报告。");
                    return new SuccessResponse("Profiler capture cancelled.", Status());
                case "export": return Export(args);
                default: return new ErrorResponse("invalid_action: Use status/start/stop/cancel/export.");
            }
        }

        private static string Target => ConnectedProfiler?.GetValue(null)?.ToString() ?? "unavailable";

        private static JObject Status()
        {
            JObject status = active != null ? CaptureStatus(active, "recording") :
                result != null ? (JObject)result.DeepClone() : new JObject { ["state"] = "idle" };
            status["recording"] = ProfilerDriver.enabled;
            status["statisticsEnabled"] = PerformanceDebugRegistry.StatisticsEnabled;
            status["playing"] = EditorApplication.isPlaying;
            status["paused"] = EditorApplication.isPaused;
            status["firstDisplayFrame"] = ProfilerDriver.firstFrameIndex < 0 ? 0 : ProfilerDriver.firstFrameIndex + 1;
            status["lastDisplayFrame"] = ProfilerDriver.lastFrameIndex < 0 ? 0 : ProfilerDriver.lastFrameIndex + 1;
            status["targetId"] = Target;
            status["deepProfiling"] = ProfilerDriver.deepProfiling;
            status["profileEditor"] = ProfilerDriver.profileEditor;
            return status;
        }

        private static JObject CaptureStatus(Capture job, string state) => new()
        {
            ["jobId"] = job.Id, ["state"] = state, ["seconds"] = job.Seconds,
            ["elapsedSeconds"] = Math.Max(0d, EditorApplication.timeSinceStartup - job.Started),
            ["startedAtUtc"] = job.StartedUtc
        };

        private static object ResultResponse() => result?["state"]?.ToString() == "failed"
            ? new ErrorResponse(result["error"]?.ToString(), Status())
            : new SuccessResponse("Profiler evidence exported.", Status());

        private sealed class LuaProfilerBackend : IGameplayTestProfilerBackend
        {
            public JObject StartCapture(double seconds)
            {
                object response = Start(new JObject { ["seconds"] = seconds, ["contextFrameRadius"] = 30 });
                if (response is ErrorResponse error) throw new InvalidOperationException(error.Error);
                return Status();
            }

            public JObject StopCapture(string jobId, string marker)
            {
                if (active == null || active.Id != jobId)
                    throw new InvalidOperationException("性能采集已提前结束或不属于本 Lua：" +
                        (result?["error"]?.ToString() ?? "请在采集时限内执行 profiler_stop。"));
                Capture job = active;
                Finish(marker);
                if (result?["state"]?.ToString() != "completed")
                    throw new InvalidOperationException("性能采集导出失败：" + result?["error"]);

                JObject sample = Status();
                string directory = sample["reportPath"]?.Value<string>();
                string analysisPath = Path.Combine(directory, "profiler-analysis.json");
                string capturePath = Path.Combine(directory, "capture-session.json");
                JObject analysis = JObject.Parse(File.ReadAllText(analysisPath));
                int nodes = 0, markerCalls = 0;
                foreach (JToken thread in analysis["callTrees"] as JArray ?? new JArray())
                foreach (JToken node in thread["nodes"] as JArray ?? new JArray())
                {
                    nodes++;
                    if (!string.IsNullOrEmpty(marker) && node["name"]?.Value<string>() == marker)
                        markerCalls += node["calls"]?.Value<int>() ?? 0;
                }
                if (nodes == 0 || !File.Exists(capturePath) || (!string.IsNullOrEmpty(marker) && markerCalls == 0))
                    throw new InvalidOperationException("性能 JSON 未包含所选帧的完整调用树或指定标记。");
                sample["analysisPath"] = analysisPath;
                sample["captureSessionPath"] = capturePath;
                sample["selectedTreeNodeCount"] = nodes;
                sample["selectedMarkerCalls"] = markerCalls;
                sample["callTreeScope"] = "selected-frame-only";
                var screenshot = analysis["screenshot"];
                bool screenshotCaptured = screenshot?["status"]?.Value<string>() == "captured" &&
                    File.Exists(Path.Combine(directory, "screen.png"));
                sample["screenshotCaptured"] = screenshotCaptured;
                sample["screenshotStatus"] = screenshot?["status"]?.Value<string>();
                sample["screenshotError"] = screenshot?["error"]?.Value<string>();
                sample["screenshotWidth"] = screenshotCaptured ? screenshot["width"]?.Value<int>() ?? 0 : 0;
                sample["screenshotHeight"] = screenshotCaptured ? screenshot["height"]?.Value<int>() ?? 0 : 0;
                sample["settingsRestored"] = ProfilerDriver.enabled == job.RecordingBefore &&
                    PerformanceDebugRegistry.StatisticsEnabled == job.StatisticsBefore &&
                    ProfilerDriver.deepProfiling == job.DeepProfiling && ProfilerDriver.profileEditor == job.ProfileEditor && Target == job.Target;
                return sample;
            }

            public void CancelCapture(string jobId)
            {
                if (active != null && active.Id == jobId)
                    Interrupt("lua_case_ended: Lua 用例结束，已取消本用例的性能采集。");
            }
        }

        #endregion

        #region 非阻塞录制与状态恢复

        private static object Start(JObject args)
        {
            if (active != null) return new ErrorResponse("profiler_busy: 请先查询、结束或取消当前采集。");
            EnsureEditorReady();
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                return new ErrorResponse("profiler_requires_running_game: 请先进入未暂停的 PlayMode，再开始采集。");
            double seconds = args?["seconds"]?.Value<double>() ?? 10d;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 1d || seconds > 120d)
                return new ErrorResponse("invalid_seconds: 采集时长必须在 1～120 秒之间。");
            int radius = ReadRadius(args);
            var job = new Capture
            {
                Id = Guid.NewGuid().ToString("N"), StartedUtc = DateTime.UtcNow.ToString("o"),
                Started = EditorApplication.timeSinceStartup, Seconds = seconds, Radius = radius,
                FirstFrame = Math.Max(0, ProfilerDriver.lastFrameIndex + 1), Target = Target,
                RecordingBefore = ProfilerDriver.enabled, DeepProfiling = ProfilerDriver.deepProfiling,
                ProfileEditor = ProfilerDriver.profileEditor,
                StatisticsBefore = PerformanceDebugRegistry.StatisticsEnabled,
                StatisticsBeforeSnapshot = JArray.FromObject(PerformanceDebugRegistry.CaptureSnapshot())
            };
            job.StatisticsExpected = job.StatisticsBefore || (args?["enableStatistics"]?.Value<bool>() ?? true);
            active = job;
            try
            {
                if (job.StatisticsExpected != job.StatisticsBefore)
                    PerformanceDebugRegistry.SetStatisticsEnabled(true);
                ProfilerDriver.enabled = true;
                EditorApplication.update += Update;
                return new SuccessResponse("Capture started; continue gameplay, then poll status for the exported report.", Status());
            }
            catch
            {
                Interrupt("profiler_start_failed: 开始采集失败。");
                throw;
            }
        }

        // 仅任务存活时挂接 Editor 更新，到时自动导出，不阻塞后续玩法调用。
        private static void Update()
        {
            if (active == null) return;
            try
            {
                string interruption = CheckCapture(active);
                if (interruption != null) { Interrupt(interruption); return; }
                if (EditorApplication.timeSinceStartup - active.Started >= active.Seconds) Finish();
            }
            catch (Exception exception) { Interrupt("profiler_capture_failed: " + exception.GetBaseException().Message); }
        }

        private static string CheckCapture(Capture job)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.isCompiling ||
                EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying)
                return "profiler_interrupted: 运行被暂停、退出或编辑器开始编译。";
            if (!ProfilerDriver.enabled || Target != job.Target || ProfilerDriver.deepProfiling != job.DeepProfiling ||
                ProfilerDriver.profileEditor != job.ProfileEditor || PerformanceDebugRegistry.StatisticsEnabled != job.StatisticsExpected)
                return "profiler_settings_changed: 录制目标或采集设置被外部改变。";
            if (ProfilerDriver.lastFrameIndex < job.FirstFrame - 1)
                return "profiler_buffer_reset: Profiler 历史已被清空或替换。";
            return null;
        }

        private static void Restore(Capture job)
        {
            EditorApplication.update -= Update;
            active = null;
            try { ProfilerDriver.enabled = job.RecordingBefore; }
            finally
            {
                PerformanceDebugRegistry.SetStatisticsEnabled(job.StatisticsBefore);
            }
        }

        private static void Interrupt(string error)
        {
            if (active == null) return;
            Capture job = active;
            result = CaptureStatus(job, "cancelled");
            result["error"] = error;
            try { Restore(job); }
            finally { SaveResult(); }
        }

        private static void Finish(string requiredMarker = null)
        {
            Capture job = active;
            result = CaptureStatus(job, "exporting");
            try
            {
                string interruption = CheckCapture(job);
                if (interruption != null) throw new InvalidOperationException(interruption);
                // 先停止新数据进入，保证选帧和完整导出使用同一批保留数据。
                ProfilerDriver.enabled = false;
                int first = Math.Max(job.FirstFrame, ProfilerDriver.firstFrameIndex);
                int last = ProfilerDriver.lastFrameIndex;
                JObject summary = Summarize(first, last, out int selected);
                if (!string.IsNullOrEmpty(requiredMarker)) selected = FindNewestFrameContainingMarker(first, last, requiredMarker);
                result["summary"] = summary;
                result["selectionMode"] = string.IsNullOrEmpty(requiredMarker) ? "slowest-retained-frame" : "newest-frame-containing-marker";
                result["requestedMarker"] = requiredMarker;
                result["callTreeScope"] = "selected-frame-only";
                result["selectedDisplayFrame"] = selected + 1;
                result["evictedFrameCount"] = Math.Max(0, first - job.FirstFrame);
                var metadata = (JObject)result.DeepClone();
                metadata["schemaVersion"] = 1;
                metadata["state"] = "completed";
                metadata["endedAtUtc"] = DateTime.UtcNow.ToString("o");
                metadata["requestedFirstDisplayFrame"] = job.FirstFrame + 1;
                metadata["targetId"] = job.Target;
                metadata["statisticsEnabledAtStart"] = job.StatisticsBefore;
                metadata["statisticsBefore"] = job.StatisticsBeforeSnapshot;
                metadata["statisticsScope"] = "Registry snapshots are cumulative; capture does not clear counters. Frame summary covers only retained valid frames, not the entire duration if evictedFrameCount > 0.";
                string directory = ProfilerSnapshotToolbar.ExportFrame(selected, job.Radius,
                    (pending, snapshotId, displayFrame) =>
                    {
                        metadata["snapshotId"] = snapshotId;
                        metadata["selectedDisplayFrame"] = displayFrame;
                        File.WriteAllText(Path.Combine(pending, "capture-session.json"), metadata.ToString(Formatting.Indented));
                        return new[] { "capture-session.json" };
                    });
                SetReport(directory, selected);
            }
            catch (Exception exception)
            {
                result["state"] = "failed";
                result["error"] = exception.GetBaseException().Message;
            }
            finally
            {
                try { Restore(job); }
                finally { SaveResult(); }
            }
        }

        private static void SaveResult() => SessionState.SetString(ResultKey, result.ToString(Formatting.None));

        #endregion

        #region 已有帧选择与证据导出

        private static object Export(JObject args)
        {
            if (active != null) return new ErrorResponse("profiler_busy: 采集中请使用 stop 完成并导出，或等待自动完成。");
            EnsureEditorReady();
            int radius = ReadRadius(args);
            int selected;
            if (args?["displayFrame"] != null && args["displayFrame"].Type != JTokenType.Null)
            {
                int display = args["displayFrame"].Value<int>();
                if (display < 1) return new ErrorResponse("invalid_frame: displayFrame 为从 1 开始的帧号。");
                selected = display - 1;
            }
            else
            {
                string frame = args?["frame"]?.ToString()?.Trim().ToLowerInvariant() ?? "latest";
                switch (frame)
                {
                    case "latest": selected = ProfilerDriver.lastFrameIndex; break;
                    case "slowest": Summarize(ProfilerDriver.firstFrameIndex, ProfilerDriver.lastFrameIndex, out selected); break;
                    case "selected":
                        var window = EditorWindow.focusedWindow as ProfilerWindow;
                        var windows = UnityEngine.Resources.FindObjectsOfTypeAll<ProfilerWindow>();
                        if (window == null && windows.Length > 0) window = windows[0];
                        long index = window != null ? window.selectedFrameIndex : -1;
                        if (index < 0 || index > int.MaxValue) return new ErrorResponse("profiler_no_selection: 请先选中 Profiler 帧，或使用 latest/slowest。");
                        selected = (int)index;
                        break;
                    default: return new ErrorResponse("invalid_frame: Use latest/selected/slowest or displayFrame.");
                }
            }
            string directory = ProfilerSnapshotToolbar.ExportFrame(selected, radius);
            result = new JObject();
            SetReport(directory, selected);
            SaveResult();
            return ResultResponse();
        }

        private static void SetReport(string directory, int selected)
        {
            result["state"] = "completed";
            result["selectedDisplayFrame"] = selected + 1;
            result["reportPath"] = directory;
            result["instructionsFile"] = Path.Combine(ProfilerSnapshotToolbar.ReportsRoot, "00-READ-ME-FIRST.md");
            var files = new JArray();
            foreach (string file in Directory.GetFiles(directory)) files.Add(Path.GetFileName(file));
            result["files"] = files;
        }

        private static JObject Summarize(int first, int last, out int selected)
        {
            selected = -1;
            int count = 0;
            double total = 0d, maximum = 0d;
            for (int index = Math.Max(0, first); index <= last; index++)
            {
                using var raw = ProfilerDriver.GetRawFrameDataView(index, 0);
                if (!raw.valid) continue;
                double ms = raw.frameTimeMs;
                if (double.IsNaN(ms) || double.IsInfinity(ms) || ms <= 0d) continue;
                count++;
                total += ms;
                if (selected < 0 || ms > maximum) { maximum = ms; selected = index; }
            }
            if (selected < 0) throw new InvalidOperationException("profiler_no_frames: 本次没有有效的新帧，旧录制不会被当成本次测试数据。");
            return new JObject
            {
                ["scope"] = "retained-profiler-buffer", ["validFrameCount"] = count,
                ["unavailableFrameCount"] = Math.Max(0, last - Math.Max(0, first) + 1 - count),
                ["firstDisplayFrame"] = first + 1, ["lastDisplayFrame"] = last + 1,
                ["meanFrameMs"] = total / count, ["maxFrameMs"] = maximum
            };
        }

        // 指定标记时从新到旧查保留帧，导出该标记实际出现的最近一帧。
        private static int FindNewestFrameContainingMarker(int first, int last, string marker)
        {
            for (int frame = last; frame >= first; frame--)
            for (int thread = 0; ; thread++)
            {
                using var raw = ProfilerDriver.GetRawFrameDataView(frame, thread);
                if (!raw.valid) break;
                int markerId = raw.GetMarkerId(marker);
                if (markerId < 0) continue;
                for (int index = 0; index < raw.sampleCount; index++)
                    if (raw.GetSampleMarkerId(index) == markerId) return frame;
            }
            throw new InvalidOperationException("profiler_marker_not_recorded: 本次保留帧没有标记 " + marker);
        }

        private static int ReadRadius(JObject args)
        {
            int radius = args?["contextFrameRadius"]?.Value<int>() ?? 30;
            if (radius < 0 || radius > 300) throw new ArgumentOutOfRangeException("contextFrameRadius", "半径必须在 0～300 之间。");
            return radius;
        }

        private static void EnsureEditorReady()
        {
            if (EditorApplication.isCompiling ||
                EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying)
                throw new InvalidOperationException("editor_busy: Unity 正在编译或切换运行状态。");
        }

        #endregion
    }
}
