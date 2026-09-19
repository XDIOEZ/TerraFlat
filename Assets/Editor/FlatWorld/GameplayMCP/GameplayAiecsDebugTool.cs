using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FlatWorld.AIECS.Gameplay;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using Unity.Burst;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// 只读 ECS 压力观察：状态检查或 1～20 秒有界采样，不生成单位、不修改生命/位置/时间。
    /// 帧时来自真实 PlayerLoop，报告采样覆盖和 Editor 条件；生成仍须通过 GM 正式按钮。
    /// </summary>
    [McpForUnityTool("gameplay_aiecs_debug", Description =
        "Read live AIECS test counters or sample real frame times for 1-20 seconds. Actions: status, sample. Read-only; create/clear units through the GM UI. Reports Burst, actual alive/visible counts, tick progress, backlog, memory, errors and occupancy validation.", Group = "core")]
    public static class GameplayAiecsDebugTool
    {
        #region 协议与状态

        public sealed class Parameters
        {
            [ToolParameter("status or sample", Required = false, DefaultValue = "status")]
            public string action { get; set; }
            [ToolParameter("Bounded real-time sample duration, 1-20 seconds", Required = false, DefaultValue = "10")]
            public float seconds { get; set; }
        }

        private static bool sampling;

        public static async Task<object> HandleCommand(JObject parameters)
        {
            string action = parameters?["action"]?.ToString() ?? "status";
            if (action == "status") return new SuccessResponse("Read-only AIECS diagnostics.", Status());
            if (action != "sample") return new ErrorResponse("Use status or sample.");
            if (!EditorApplication.isPlaying || AiecsPlayground.Active == null || !AiecsPlayground.Active.HasActiveScenario)
                return new ErrorResponse("Open a ready world and start the AIECS test through the GM UI first.");
            if (sampling) return new ErrorResponse("An AIECS sample is already running.");
            float seconds = Mathf.Clamp(parameters?["seconds"]?.Value<float>() ?? 10f, 1f, 20f);
            if (float.IsNaN(seconds)) return new ErrorResponse("Sample duration must be finite.");
            sampling = true;
            try { return await Sample(seconds); }
            finally { sampling = false; }
        }

        private static object Status()
        {
            AiecsPlayground entry = AiecsPlayground.Active;
            return new
            {
                playing = EditorApplication.isPlaying, burst = BurstCompiler.Options.EnableBurstCompilation,
                isolated = IsIsolated(), frame = Time.frameCount, timeScale = Time.timeScale,
                screen = new[] { Screen.width, Screen.height }, safeArea = Screen.safeArea.ToString(),
                usedMemory = Profiler.GetTotalAllocatedMemoryLong(), reservedMemory = Profiler.GetTotalReservedMemoryLong(),
                ecs = entry == null ? null : entry.CaptureDebugSnapshot(true), drops = DroppedItemService.Count,
                shadows = entry == null ? null : new { enabled = entry.ShowFootShadows, visible = entry.VisibleShadowCount,
                    batches = entry.VisibleShadowBatchCount }
            };
        }

        /// <summary>只报告是否处于 MCP 隔离目录，不向工具输出玩家的磁盘路径。</summary>
        private static bool IsIsolated()
        {
            if (!EditorApplication.isPlaying) return false;
            string path = SaveDataMgr.Instance.UserSavePath;
            if (string.IsNullOrEmpty(path)) return false;
            string isolated = Path.GetFullPath("Library/FlatWorldGameplayMCP/Saves").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return (Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
                .StartsWith(isolated, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region 有界采样

        private static async Task<object> Sample(float seconds)
        {
            AiecsPlayground entry = AiecsPlayground.Active;
            var before = entry.CaptureDebugSnapshot(true);
            var frames = new List<double>(2048);
            var issues = new List<string>();
            int errors = 0, warnings = 0, previousFrame = Time.frameCount, firstFrame = previousFrame;
            int minimumAlive = entry.AliveUnitCount, maximumAlive = minimumAlive;
            int minimumVisible = entry.VisibleUnitCount, maximumVisible = minimumVisible;
            double maxBacklog = entry.SimulationBacklogSeconds;
            long memoryBefore = Profiler.GetTotalAllocatedMemoryLong();
            double start = EditorApplication.timeSinceStartup;
            var completion = new TaskCompletionSource<bool>();
            Application.LogCallback onLog = (message, trace, type) =>
            {
                bool error = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
                if (error) errors++;
                if (type == LogType.Warning) warnings++;
                if ((error || type == LogType.Warning) && issues.Count < 12) issues.Add(message);
            };
            EditorApplication.CallbackFunction sample = () =>
            {
                if (!EditorApplication.isPlaying || entry == null || !entry.HasActiveScenario ||
                    EditorApplication.timeSinceStartup - start >= seconds)
                { completion.TrySetResult(true); return; }
                if (Time.frameCount == previousFrame) return;
                previousFrame = Time.frameCount;
                frames.Add(Time.unscaledDeltaTime * 1000d);
                minimumAlive = Math.Min(minimumAlive, entry.AliveUnitCount);
                maximumAlive = Math.Max(maximumAlive, entry.AliveUnitCount);
                minimumVisible = Math.Min(minimumVisible, entry.VisibleUnitCount);
                maximumVisible = Math.Max(maximumVisible, entry.VisibleUnitCount);
                maxBacklog = Math.Max(maxBacklog, entry.SimulationBacklogSeconds);
            };
            Application.logMessageReceived += onLog;
            EditorApplication.update += sample;
            try
            {
                await completion.Task;
                double elapsed = EditorApplication.timeSinceStartup - start;
                var after = entry == null ? null : entry.CaptureDebugSnapshot(true);
                double total = 0; foreach (double value in frames) total += value;
                frames.Sort();
                return new SuccessResponse("Bounded Editor Play Mode sample completed; not a standalone build benchmark.", new
                {
                    elapsed, samples = frames.Count, renderedFrames = Time.frameCount - firstFrame,
                    sampleFps = total > 0 ? frames.Count * 1000d / total : 0,
                    p50Ms = Percentile(frames, 0.5), p95Ms = Percentile(frames, 0.95),
                    maxMs = Percentile(frames, 1), maxBacklog, minimumAlive, maximumAlive, minimumVisible, maximumVisible,
                    memoryBefore, memoryAfter = Profiler.GetTotalAllocatedMemoryLong(),
                    errors, warnings, issues, burst = BurstCompiler.Options.EnableBurstCompilation,
                    isolated = IsIsolated(), before, after,
                    shadows = entry == null ? null : new { enabled = entry.ShowFootShadows, visible = entry.VisibleShadowCount,
                        batches = entry.VisibleShadowBatchCount }
                });
            }
            finally
            {
                Application.logMessageReceived -= onLog;
                EditorApplication.update -= sample;
            }
        }

        private static double Percentile(List<double> values, double quantile) => values.Count == 0 ? 0 :
            values[Math.Min(values.Count - 1, (int)Math.Ceiling(values.Count * quantile) - 1)];

        #endregion
    }
}
