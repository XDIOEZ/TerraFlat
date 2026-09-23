using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace FlatWorld.GameplayMCP
{
    /// <summary>正式刷怪链的只读计数审计与 1～20 秒采样；不生成、清理实体或修改配置。</summary>
    [McpForUnityTool("gameplay_spawner_debug", Description =
        "Read the real monster registry and ecology limits, sample CPU for 1-20 seconds, or inspect recorded Unity Profiler frames including descendant GC.Alloc metadata. Actions: status, sample, profile_frames. Read-only; does not mutate gameplay.", Group = "core")]
    public static class GameplaySpawnerDebugTool
    {
        #region 协议

        public sealed class Parameters
        {
            [ToolParameter("status, sample or profile_frames", Required = false, DefaultValue = "status")]
            public string action { get; set; }
            [ToolParameter("Real-time duration, 1-20 seconds", Required = false, DefaultValue = "10")]
            public float seconds { get; set; }
            [ToolParameter("Maximum recorded Profiler frames to read, 1-300", Required = false, DefaultValue = "300")]
            public int frames { get; set; }
        }

        private static bool sampling;

        public static async Task<object> HandleCommand(JObject parameters)
        {
            var manager = MonsterSpawnerManager.ExistingInstance;
            if (!EditorApplication.isPlaying || manager == null)
                return new ErrorResponse("Enter the real game world first.");
            string action = parameters?["action"]?.ToString() ?? "status";
            if (action == "profile_frames") return CaptureProfilerFrames(parameters?["frames"]?.Value<int>() ?? 300);
            if (action == "status")
                return new SuccessResponse("Read-only spawner audit.", manager.CaptureSpawnerDiagnostics());
            if (action != "sample") return new ErrorResponse("Use status, sample or profile_frames.");
            if (sampling) return new ErrorResponse("A spawner sample is already running.");
            if (!manager.enabled || GameManager.Instance == null || !GameManager.Instance.IsGameplayReady)
                return new ErrorResponse("The world and spawner must be ready before sampling.");
            float seconds = parameters?["seconds"]?.Value<float>() ?? 10f;
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return new ErrorResponse("Duration must be finite.");
            sampling = true;
            try { return await Sample(manager, Mathf.Clamp(seconds, 1f, 20f)); }
            finally { sampling = false; }
        }

        #endregion

        #region 有界采样

        private static async Task<object> Sample(MonsterSpawnerManager manager, float seconds)
        {
            object before = manager.CaptureSpawnerDiagnostics();
            int errors = 0, warnings = 0, firstFrame = Time.frameCount, previousFrame = firstFrame;
            int frameSamples = 0;
            long frameGcTotal = 0, frameGcMax = 0;
            var issues = new List<string>(8);
            var complete = new TaskCompletionSource<bool>();
            var frameGc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            double start = EditorApplication.timeSinceStartup;
            bool aborted = false;
            Application.LogCallback onLog = (message, trace, type) =>
            {
                bool error = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
                if (error) errors++;
                if (type == LogType.Warning) warnings++;
                if ((error || type == LogType.Warning) && issues.Count < 8) issues.Add(message);
            };
            EditorApplication.CallbackFunction observe = () =>
            {
                aborted = !EditorApplication.isPlaying || manager == null || !manager.enabled ||
                          GameManager.Instance == null || !GameManager.Instance.IsGameplayReady;
                if (aborted || EditorApplication.timeSinceStartup - start >= seconds)
                { complete.TrySetResult(true); return; }
                if (Time.frameCount == previousFrame) return;
                previousFrame = Time.frameCount;
                if (!frameGc.Valid || frameGc.Count == 0) return;
                frameSamples++;
                frameGcTotal += frameGc.LastValue;
                frameGcMax = Math.Max(frameGcMax, frameGc.LastValue);
            };
            manager.BeginPerformanceSample();
            Application.logMessageReceived += onLog;
            EditorApplication.update += observe;
            try
            {
                await complete.Task;
                object measured = manager == null ? null : manager.EndPerformanceSample();
                string root = Path.GetFullPath("Library/FlatWorldGameplayMCP/Saves") + Path.DirectorySeparatorChar;
                string savePath = SaveDataMgr.Instance?.UserSavePath;
                return new SuccessResponse("Bounded Editor Play Mode sample; not a standalone Player benchmark.", new
                {
                    aborted, elapsed = EditorApplication.timeSinceStartup - start,
                    frames = Time.frameCount - firstFrame, frameSamples,
                    wholeFrameMeanGcBytes = frameSamples == 0 ? 0 : (double)frameGcTotal / frameSamples,
                    wholeFrameMaxGcBytes = frameGcMax,
                    profilerEnabled = UnityEngine.Profiling.Profiler.enabled,
                    deepProfiling = UnityEditorInternal.ProfilerDriver.deepProfiling,
                    isolated = !string.IsNullOrEmpty(savePath) &&
                        (Path.GetFullPath(savePath) + Path.DirectorySeparatorChar).StartsWith(root, StringComparison.OrdinalIgnoreCase),
                    errors, warnings, issues, measured, before,
                    after = manager == null ? null : manager.CaptureSpawnerDiagnostics()
                });
            }
            finally
            {
                if (manager != null) manager.EndPerformanceSample();
                Application.logMessageReceived -= onLog;
                EditorApplication.update -= observe;
                frameGc.Dispose();
            }
        }

        #endregion

        #region Unity Profiler 原始证据

        private sealed class RecordedBucket
        {
            public int Samples, GcCalls;
            public long TotalBytes, MaxBytes;
            public double TotalMs, MaxMs;

            public void Add(double ms, long bytes, int calls)
            {
                Samples++; TotalMs += ms; MaxMs = Math.Max(MaxMs, ms);
                TotalBytes += bytes; MaxBytes = Math.Max(MaxBytes, bytes); GcCalls += calls;
            }

            public object Report() => new
            {
                samples = Samples, meanMs = Samples == 0 ? 0 : TotalMs / Samples, maxMs = MaxMs,
                gcCalls = GcCalls, totalGcBytes = TotalBytes,
                meanGcBytes = Samples == 0 ? 0 : (double)TotalBytes / Samples, maxGcBytes = MaxBytes
            };
        }

        /// <summary>只读取已完成帧；按命名标记的完整子树累计 GC.Alloc 元数据，不依赖 Mono GC 计数 API。</summary>
        private static object CaptureProfilerFrames(int requested)
        {
            if (!UnityEngine.Profiling.Profiler.enabled)
                return new ErrorResponse("Unity Profiler must already be recording. This tool does not change Profiler settings.");
            var steady = new RecordedBucket();
            var spawning = new RecordedBucket();
            var creation = new RecordedBucket();
            var dormancy = new RecordedBucket();
            int last = ProfilerDriver.lastFrameIndex;
            int first = Math.Max(ProfilerDriver.firstFrameIndex, last - Mathf.Clamp(requested, 1, 300) + 1);
            int validFrames = 0, framesWithGcMetadata = 0;
            long allMainThreadGc = 0;
            for (int frame = first; frame <= last; frame++)
            {
                using RawFrameDataView raw = ProfilerDriver.GetRawFrameDataView(frame, 0);
                if (!raw.valid) continue;
                validFrames++;
                int tickId = raw.GetMarkerId("FlatWorld.MonsterSpawner.Tick");
                int createId = raw.GetMarkerId("FlatWorld.MonsterSpawner.Create");
                int dormancyId = raw.GetMarkerId("FlatWorld.MonsterSpawner.Dormancy");
                int gcId = raw.GetMarkerId("GC.Alloc");
                bool hasGc = false;
                for (int sample = 0; sample < raw.sampleCount; sample++)
                {
                    int marker = raw.GetSampleMarkerId(sample);
                    if (marker == gcId && raw.GetSampleMetadataCount(sample) > 0)
                    {
                        hasGc = true;
                        allMainThreadGc += raw.GetSampleMetadataAsLong(sample, 0);
                    }
                    if (marker != tickId && marker != createId && marker != dormancyId) continue;
                    int end = Math.Min(raw.sampleCount, sample + 1 + raw.GetSampleChildrenCountRecursive(sample));
                    long bytes = 0;
                    int calls = 0;
                    bool hasCreation = false;
                    for (int child = sample + 1; child < end; child++)
                    {
                        int childMarker = raw.GetSampleMarkerId(child);
                        if (childMarker == createId) hasCreation = true;
                        if (childMarker != gcId || raw.GetSampleMetadataCount(child) == 0) continue;
                        bytes += raw.GetSampleMetadataAsLong(child, 0);
                        calls++;
                    }
                    RecordedBucket target = marker == createId ? creation : marker == dormancyId ? dormancy :
                        hasCreation ? spawning : steady;
                    target.Add(raw.GetSampleTimeMs(sample), bytes, calls);
                }
                if (hasGc) framesWithGcMetadata++;
            }
            return new SuccessResponse("Completed Unity Profiler main-thread frames; inclusive marker subtree GC metadata.", new
            {
                first, last, validFrames, framesWithGcMetadata,
                gcEvidenceAvailable = framesWithGcMetadata > 0, allMainThreadGc,
                deepProfiling = ProfilerDriver.deepProfiling,
                noCreationFrames = steady.Report(), creationFrames = spawning.Report(),
                creationOperations = creation.Report(), dormancyCallbacks = dormancy.Report()
            });
        }

        #endregion
    }
}
