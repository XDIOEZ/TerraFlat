using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace PuertsUnityMcp.Editor
{
    public static class UnityMcpProfilerSnapshot
    {
        #region 手动导出入口

        // 只读取已有帧和 Console，不切换录制目标或改变游戏暂停状态。
        public static string ExportSelectedFrame(int frameIndex, string outputRoot, int contextFrameRadius = 30)
        {
            return UnityMcpEditorEndpoint.ExportProfilerSnapshot(frameIndex, outputRoot, contextFrameRadius);
        }

        #endregion
    }

    internal sealed partial class UnityMcpEditorEndpoint
    {
        #region Profiler 离线快照

        internal static string ExportProfilerSnapshot(int frameIndex, string outputRoot, int contextFrameRadius)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying)
                throw new InvalidOperationException("Unity 正在编译、导入或切换运行状态，请稍后再导出。");
            int first = ProfilerDriver.firstFrameIndex;
            int last = ProfilerDriver.lastFrameIndex;
            if (first < 0 || frameIndex < first || frameIndex > last)
                throw new InvalidOperationException("当前选中的 Profiler 帧没有数据，请先录制并选中要分析的帧。");
            if (string.IsNullOrWhiteSpace(outputRoot))
                throw new ArgumentException("必须指定性能报告目录。", nameof(outputRoot));

            var capturedAt = DateTimeOffset.Now;
            var snapshotId = capturedAt.ToString("yyyy-MM-dd_HH-mm-ss_fff", CultureInfo.InvariantCulture)
                + "_frame-" + (frameIndex + 1) + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var warnings = new List<string>();
            var errors = new List<string>();
            // 先取日志，避免导出过程产生的日志混入用户的现场。
            var console = UnityMcpConsoleSnapshot.Read(snapshotId, capturedAt, frameIndex + 1);
            var analysis = AnalyzeEditorProfilerFrames(new EditorProfilerRequest
            {
                firstFrame = frameIndex + 1,
                lastFrame = frameIndex + 1,
                maxMarkers = 100,
                hitchThresholdMs = DefaultProfilerHitchThresholdMs
            }, warnings, errors);
            if (!analysis.success || analysis.frameCount != 1)
                throw new InvalidOperationException("选中帧读取失败：" + string.Join("; ", errors));

            var threads = new List<ProfilerSnapshotThread>();
            for (int threadIndex = 0; ; threadIndex++)
            {
                using (var raw = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex))
                {
                    if (!raw.valid) break;
                    var samples = BuildSampleRecords(raw, warnings);
                    AccumulateProfilerSampleChildren(samples);
                    var nodes = new List<ProfilerSnapshotNode>();
                    var nodeIds = new int[samples.Count];
                    var byParentAndName = new Dictionary<(int, string), int>();
                    for (int sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
                    {
                        var sample = samples[sampleIndex];
                        int parentId = sample.parentIndex < 0 ? -1 : nodeIds[sample.parentIndex];
                        var key = (parentId, sample.name);
                        if (!byParentAndName.TryGetValue(key, out int nodeId))
                        {
                            nodeId = nodes.Count;
                            nodes.Add(new ProfilerSnapshotNode { id = nodeId, parentId = parentId, name = sample.name });
                            byParentAndName.Add(key, nodeId);
                        }
                        nodeIds[sampleIndex] = nodeId;
                        var node = nodes[nodeId];
                        node.calls++;
                        node.totalMs += sample.ms;
                        node.selfMs += sample.selfMs;
                        node.maxCallMs = Math.Max(node.maxCallMs, sample.ms);
                        node.gcBytesWithChildren += sample.gcBytesWithChildren;
                        node.gcAllocationsWithChildren += sample.gcAllocationCountWithChildren;
                    }
                    threads.Add(new ProfilerSnapshotThread
                    {
                        threadIndex = threadIndex,
                        name = BuildProfilerThreadName(raw.threadGroupName, raw.threadName),
                        rawSampleCount = raw.sampleCount,
                        nodes = nodes.ToArray()
                    });
                }
            }

            int radius = Mathf.Clamp(contextFrameRadius, 0, 300);
            var context = new List<ProfilerSnapshotFrame>();
            for (int index = Math.Max(first, frameIndex - radius); index <= Math.Min(last, frameIndex + radius); index++)
            {
                double timeMs = ReadFrameTimeMs(index);
                context.Add(new ProfilerSnapshotFrame { displayFrame = index + 1, available = timeMs > 0d, timeMs = timeMs });
            }
            var snapshot = new ProfilerSnapshotReport
            {
                snapshotId = snapshotId,
                capturedAtLocal = capturedAt.ToString("o", CultureInfo.InvariantCulture),
                capturedAtUtc = capturedAt.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                projectPath = UnityMcpPaths.ProjectRoot,
                unityVersion = Application.unityVersion,
                selectedFrameIndex = frameIndex,
                selectedDisplayFrame = frameIndex + 1,
                editorPlayingAtExport = EditorApplication.isPlaying,
                editorPausedAtExport = EditorApplication.isPaused,
                deepProfilingAtExport = ProfilerDriver.deepProfiling,
                profilerTargetAtExport = BuildProfilerTargetListResult(),
                selectedFrameAnalysis = analysis,
                nearbyFrames = context.ToArray(),
                callTrees = threads.ToArray(),
                warnings = warnings.ToArray()
            };
            string root = Path.GetFullPath(outputRoot);
            string pending = Path.Combine(root, ".pending-" + snapshotId);
            string destination = Path.Combine(root, snapshotId);
            Directory.CreateDirectory(pending);
            AtomicFile.WriteJson(Path.Combine(pending, "profiler-analysis.json"), snapshot);
            AtomicFile.WriteJson(Path.Combine(pending, "console-logs.json"), console);
            // 两份文件都落盘后才公布正式目录，失败的 pending 目录不作为新报告。
            Directory.Move(pending, destination);
            return destination;
        }

        [Serializable]
        private sealed class ProfilerSnapshotReport
        {
            public int schemaVersion = 1;
            public string snapshotId, capturedAtLocal, capturedAtUtc, projectPath, unityVersion;
            public string consoleFile = "console-logs.json";
            public int selectedFrameIndex, selectedDisplayFrame;
            public bool editorPlayingAtExport, editorPausedAtExport, deepProfilingAtExport;
            public EditorProfilerTargetListResult profilerTargetAtExport;
            public EditorProfilerAnalysis selectedFrameAnalysis;
            public ProfilerSnapshotFrame[] nearbyFrames;
            public ProfilerSnapshotThread[] callTrees;
            public string[] warnings;
            public string[] readingGuide =
            {
                "selectedDisplayFrame and all displayFrame values are one-based; selectedFrameIndex is zero-based.",
                "selectedFrameAnalysis reuses editor.profiler.analyze; top lists are limited to 100 rows, callTrees are not truncated.",
                "callTrees merge repeated calls with the same parent and name within each thread; parentId=-1 means a root. Node ids are local to each thread.",
                "totalMs includes children; selfMs excludes direct children. Never add parent and child inclusive time, or thread totals, to estimate frame wall time.",
                "gcBytesWithChildren includes descendants; do not sum allocations across ancestors. GC bytes require captured GC.Alloc metadata.",
                "nearbyFrames contain frame time only, not detailed analysis. callTrees contain CPU markers, not GPU timing or a full event timeline.",
                "Console logs are an export-time snapshot, not proven logs from the selected frame. Cleared logs cannot be recovered.",
                "Target, deep profiling and editor state describe export time; loaded historical captures may have different recording settings. Editor/wait markers are not proof of gameplay cost."
            };
        }

        [Serializable]
        private sealed class ProfilerSnapshotFrame
        {
            public int displayFrame;
            public bool available;
            public double timeMs;
        }

        [Serializable]
        private sealed class ProfilerSnapshotThread
        {
            public int threadIndex, rawSampleCount;
            public string name;
            public ProfilerSnapshotNode[] nodes;
        }

        [Serializable]
        private sealed class ProfilerSnapshotNode
        {
            public int id, parentId, calls, gcAllocationsWithChildren;
            public string name;
            public double totalMs, selfMs, maxCallMs;
            public long gcBytesWithChildren;
        }

        #endregion
    }
}
