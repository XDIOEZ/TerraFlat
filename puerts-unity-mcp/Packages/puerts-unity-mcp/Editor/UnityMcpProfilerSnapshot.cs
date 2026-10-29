using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace PuertsUnityMcp.Editor
{
    public static class UnityMcpProfilerSnapshot
    {
        #region 手动导出入口

        // 只读取已有帧和 Console，不切换录制目标或改变游戏暂停状态。
        public static string ExportSelectedFrame(int frameIndex, string outputRoot, int summaryNeighborFrameCount = 60,
            Func<string, string, int, string[]> exportAdditionalFiles = null,
            int detailSelectionNeighborFrameCount = 300, int maximumAdditionalFrames = 3)
        {
            return UnityMcpEditorEndpoint.ExportProfilerSnapshot(frameIndex, outputRoot, summaryNeighborFrameCount,
                exportAdditionalFiles, detailSelectionNeighborFrameCount, maximumAdditionalFrames);
        }

        #endregion
    }

    internal sealed partial class UnityMcpEditorEndpoint
    {
        #region Profiler 离线快照

        internal static string ExportProfilerSnapshot(int frameIndex, string outputRoot, int summaryNeighborFrameCount,
            Func<string, string, int, string[]> exportAdditionalFiles,
            int detailSelectionNeighborFrameCount, int maximumAdditionalFrames)
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying)
                throw new InvalidOperationException("Unity 正在编译或切换运行状态，请稍后再导出。");
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
            string analysisGuide = File.ReadAllText(Path.Combine(ResolvePackageRoot(), "ProfilerAnalysisGuide.md"));
            byte[] screenshotPng = null;
            UnityMcpEditorMainWindowScreenshot.ScreenshotInfo screenshotInfo;
            try { screenshotPng = UnityMcpEditorMainWindowScreenshot.Capture(out screenshotInfo); }
            catch (Exception exception)
            {
                screenshotInfo = new UnityMcpEditorMainWindowScreenshot.ScreenshotInfo
                    { status = "failed", error = exception.GetBaseException().Message };
                warnings.Add("Unity 主窗口截图失败：" + screenshotInfo.error);
            }
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

            var threads = CaptureProfilerSnapshotTrees(frameIndex, warnings);
            if (threads.Length == 0)
                throw new InvalidOperationException("选中帧调用树已不可用，请重新选择仍在 Profiler 缓冲中的帧。");
            summaryNeighborFrameCount = Math.Max(0, summaryNeighborFrameCount);
            detailSelectionNeighborFrameCount = Math.Max(0, detailSelectionNeighborFrameCount);
            maximumAdditionalFrames = Math.Max(0, maximumAdditionalFrames);
            CalculateClippedNeighborFrameRange(frameIndex, summaryNeighborFrameCount, first, last,
                out int summaryFirst, out int summaryLast);
            CalculateClippedNeighborFrameRange(frameIndex, detailSelectionNeighborFrameCount, first, last,
                out int selectionFirst, out int selectionLast);
            var context = new List<ProfilerSnapshotFrame>();
            var candidates = new List<ProfilerSnapshotFrame>();
            int scanFirst = maximumAdditionalFrames > 0 ? Math.Min(summaryFirst, selectionFirst) : summaryFirst;
            int scanLast = maximumAdditionalFrames > 0 ? Math.Max(summaryLast, selectionLast) : summaryLast;
            // 摘要与慢帧筛选共用一次耗时读取，筛选范围内其余帧不写入报告。
            for (int index = scanFirst; index <= scanLast; index++)
            {
                if (index == frameIndex) continue;
                var frame = new ProfilerSnapshotFrame { displayFrame = index + 1 };
                try
                {
                    frame.timeMs = ReadFrameTimeMs(index);
                    frame.available = frame.timeMs > 0d && !double.IsInfinity(frame.timeMs);
                    frame.status = frame.available ? "captured" : "frame-time-unavailable";
                }
                catch (Exception exception)
                {
                    frame.status = "read-failed";
                    frame.error = exception.Message;
                    warnings.Add("附近帧 " + frame.displayFrame + " 耗时读取失败：" + frame.error);
                }
                if (index >= summaryFirst && index <= summaryLast) context.Add(frame);
                if (maximumAdditionalFrames > 0 && index >= selectionFirst && index <= selectionLast && frame.available)
                    candidates.Add(frame);
            }
            var additionalFrames = CaptureSlowestCandidateFrameTrees(candidates, maximumAdditionalFrames, warnings);
            var snapshot = new ProfilerSnapshotReport
            {
                snapshotId = snapshotId,
                capturedAtLocal = capturedAt.ToString("o", CultureInfo.InvariantCulture),
                capturedAtUtc = capturedAt.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                projectPath = UnityMcpPaths.ProjectRoot,
                unityVersion = Application.unityVersion,
                selectedFrameIndex = frameIndex,
                selectedDisplayFrame = frameIndex + 1,
                screenshot = screenshotInfo,
                editorPlayingAtExport = EditorApplication.isPlaying,
                editorPausedAtExport = EditorApplication.isPaused,
                deepProfilingAtExport = ProfilerDriver.deepProfiling,
                profilerTargetAtExport = BuildProfilerTargetListResult(),
                selectedFrameAnalysis = analysis,
                nearbyFrames = context.ToArray(),
                callTrees = threads,
                additionalFrameDetails = additionalFrames,
                additionalFramePolicy = new ProfilerSnapshotAdditionalFramePolicy
                {
                    summaryNeighborFrameCount = summaryNeighborFrameCount,
                    selectionNeighborFrameCount = detailSelectionNeighborFrameCount,
                    maximumAdditionalFrames = maximumAdditionalFrames,
                    firstSearchDisplayFrame = selectionFirst + 1,
                    lastSearchDisplayFrame = selectionLast + 1
                },
                performanceDebugAtExport = CapturePerformanceDebugAtExport(),
                warnings = warnings.ToArray()
            };
            string root = Path.GetFullPath(outputRoot);
            string guidePath = Path.Combine(root, Path.GetFileName(snapshot.instructionsFile));
            if (!File.Exists(guidePath) || File.ReadAllText(guidePath) != analysisGuide)
                AtomicFile.WriteAllText(guidePath, analysisGuide);
            string pending = Path.Combine(root, ".pending-" + snapshotId);
            string destination = Path.Combine(root, snapshotId);
            Directory.CreateDirectory(pending);
            // 项目附加现场也先写临时目录，全部成功后才发布同一份报告。
            snapshot.additionalFiles = exportAdditionalFiles?.Invoke(pending, snapshotId, frameIndex + 1)
                ?? Array.Empty<string>();
            AtomicFile.WriteJson(Path.Combine(pending, "profiler-analysis.json"), snapshot);
            AtomicFile.WriteJson(Path.Combine(pending, "console-logs.json"), console);
            if (screenshotPng != null) File.WriteAllBytes(Path.Combine(pending, screenshotInfo.file), screenshotPng);
            // 共享说明放在根目录，报告保留 JSON、主窗口截图和项目附加现场。
            Directory.Move(pending, destination);
            return destination;
        }

        // 邻帧不足时只裁剪当前一侧，奇数配置多取一帧前文，不向另一侧补齐。
        private static void CalculateClippedNeighborFrameRange(int frameIndex, int neighborFrameCount, int first, int last,
            out int rangeFirst, out int rangeLast)
        {
            rangeFirst = (int)Math.Max(first, (long)frameIndex - neighborFrameCount / 2 - neighborFrameCount % 2);
            rangeLast = (int)Math.Min(last, (long)frameIndex + neighborFrameCount / 2);
        }

        // 选中帧和附带慢帧共用聚合规则，保留完整线程树而不截断节点。
        private static ProfilerSnapshotThread[] CaptureProfilerSnapshotTrees(int frameIndex, List<string> warnings)
        {
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

            // 远端目标继续录制时可能滚动缓冲，不能把只读到部分线程的树标成完整数据。
            if (threads.Count > 0 && (frameIndex < ProfilerDriver.firstFrameIndex || frameIndex > ProfilerDriver.lastFrameIndex))
                throw new InvalidOperationException("读取调用树期间帧已离开 Profiler 缓冲。");
            return threads.ToArray();
        }

        #endregion

        #region 最慢邻帧与导出时累计统计

        private static ProfilerSnapshotFrameDetail[] CaptureSlowestCandidateFrameTrees(List<ProfilerSnapshotFrame> candidates,
            int maximumAdditionalFrames, List<string> warnings)
        {
            candidates.Sort((left, right) =>
            {
                int order = right.timeMs.CompareTo(left.timeMs);
                return order != 0 ? order : left.displayFrame.CompareTo(right.displayFrame);
            });
            int count = Math.Min(maximumAdditionalFrames, candidates.Count);
            var details = new List<ProfilerSnapshotFrameDetail>(count);
            for (int index = 0; index < count; index++)
            {
                ProfilerSnapshotFrame frame = candidates[index];
                var detail = new ProfilerSnapshotFrameDetail
                {
                    displayFrame = frame.displayFrame, timeMs = frame.timeMs,
                    callTrees = Array.Empty<ProfilerSnapshotThread>()
                };
                try
                {
                    detail.callTrees = CaptureProfilerSnapshotTrees(frame.displayFrame - 1, warnings);
                    detail.available = detail.callTrees.Length > 0;
                    detail.status = detail.available ? "captured" : "frame-no-longer-available";
                }
                catch (Exception exception)
                {
                    detail.status = "read-failed";
                    detail.error = exception.Message;
                    detail.callTrees = Array.Empty<ProfilerSnapshotThread>();
                }
                if (!detail.available)
                    warnings.Add("附近慢帧 " + detail.displayFrame + " 无完整调用树：" + detail.status + " " + detail.error);
                details.Add(detail);
            }
            return details.ToArray();
        }

        // 包保持独立，通过只读反射获取项目快照，不改开关、不清空或消费慢日志。
        private static ProfilerSnapshotDebugState CapturePerformanceDebugAtExport()
        {
            var state = new ProfilerSnapshotDebugState
            {
                capturedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                status = "registry-not-loaded", points = Array.Empty<ProfilerSnapshotDebugPoint>()
            };
            try
            {
                Type registry = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    registry = assembly.GetType("FlatWorld.Diagnostics.PerformanceDebugRegistry", false);
                    if (registry != null) break;
                }
                if (registry == null) return state;
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                state.statisticsEnabled = (bool)registry.GetProperty("StatisticsEnabled", flags).GetValue(null);
                state.slowSummaryEnabled = (bool)registry.GetProperty("SlowSummaryEnabled", flags).GetValue(null);
                state.slowThresholdMilliseconds = (double)registry.GetProperty("SlowThresholdMilliseconds", flags).GetValue(null);
                state.hasPendingSlowEvents = (bool)registry.GetProperty("HasPendingSlowEvents", flags).GetValue(null);
                var snapshots = (Array)registry.GetMethod("CaptureSnapshot", flags).Invoke(null, null);
                var points = new List<ProfilerSnapshotDebugPoint>(snapshots.Length);
                foreach (object snapshot in snapshots)
                {
                    Type type = snapshot.GetType();
                    points.Add(new ProfilerSnapshotDebugPoint
                    {
                        name = (string)type.GetField("Name").GetValue(snapshot),
                        calls = (long)type.GetField("Calls").GetValue(snapshot),
                        work = (long)type.GetField("Work").GetValue(snapshot),
                        gauge = (long)type.GetField("Gauge").GetValue(snapshot),
                        peakGauge = (long)type.GetField("PeakGauge").GetValue(snapshot),
                        hasGauge = (bool)type.GetField("HasGauge").GetValue(snapshot),
                        totalMilliseconds = (double)type.GetField("TotalMilliseconds").GetValue(snapshot),
                        maximumMilliseconds = (double)type.GetField("MaximumMilliseconds").GetValue(snapshot)
                    });
                }
                state.points = points.ToArray();
                state.available = true;
                state.status = state.statisticsEnabled ? "statistics-enabled" : "statistics-disabled";
            }
            catch (Exception exception)
            {
                state.status = "capture-failed";
                state.error = (exception.InnerException ?? exception).Message;
            }
            return state;
        }

        [Serializable]
        private sealed class ProfilerSnapshotAdditionalFramePolicy
        {
            public int summaryNeighborFrameCount, selectionNeighborFrameCount, maximumAdditionalFrames;
            public int firstSearchDisplayFrame, lastSearchDisplayFrame;
            public string selection = "Independent search window, descending timeMs then ascending displayFrame; no duration threshold; selectedDisplayFrame excluded";
            public string omission = "Neighbor counts exclude the selected frame, split evenly before/after (odd extra before); buffer edges are clipped without filling. Search-only frames are not exported; read failures remain explicit entries.";
        }

        [Serializable]
        private sealed class ProfilerSnapshotFrameDetail
        {
            public int displayFrame;
            public double timeMs;
            public bool available;
            public string status, error;
            public ProfilerSnapshotThread[] callTrees;
        }

        [Serializable]
        private sealed class ProfilerSnapshotDebugState
        {
            public bool available, statisticsEnabled, slowSummaryEnabled, hasPendingSlowEvents;
            public double slowThresholdMilliseconds;
            public string capturedAtUtc, status, error;
            public string source = "Editor process project registry; not remote Player data";
            public string timeScope = "Cumulative completed scopes since last Clear/session reset, during enabled periods; not a recorded Profiler frame";
            public string disabledMeaning = "Disabled or unavailable is not zero cost. Retained counts may describe earlier enabled periods. Pending slow events are not drained.";
            public ProfilerSnapshotDebugPoint[] points;
        }

        [Serializable]
        private sealed class ProfilerSnapshotDebugPoint
        {
            public string name;
            public long calls, work, gauge, peakGauge;
            public bool hasGauge;
            public double totalMilliseconds, maximumMilliseconds;
        }

        #endregion

        #region 导出数据契约

        [Serializable]
        private sealed class ProfilerSnapshotReport
        {
            public int schemaVersion = 5;
            public string snapshotId, capturedAtLocal, capturedAtUtc, projectPath, unityVersion;
            public string consoleFile = "console-logs.json";
            public string instructionsFile = "../00-READ-ME-FIRST.md";
            public string[] additionalFiles;
            public UnityMcpEditorMainWindowScreenshot.ScreenshotInfo screenshot;
            public int selectedFrameIndex, selectedDisplayFrame;
            public bool editorPlayingAtExport, editorPausedAtExport, deepProfilingAtExport;
            public EditorProfilerTargetListResult profilerTargetAtExport;
            public EditorProfilerAnalysis selectedFrameAnalysis;
            public ProfilerSnapshotFrame[] nearbyFrames;
            public ProfilerSnapshotThread[] callTrees;
            public ProfilerSnapshotFrameDetail[] additionalFrameDetails;
            public ProfilerSnapshotAdditionalFramePolicy additionalFramePolicy;
            public ProfilerSnapshotDebugState performanceDebugAtExport;
            public string[] warnings;
            public string[] readingGuide =
            {
                "selectedDisplayFrame and all displayFrame values are one-based; selectedFrameIndex is zero-based.",
                "selectedFrameAnalysis reuses editor.profiler.analyze; top lists are limited to 100 rows, callTrees are not truncated.",
                "callTrees merge repeated calls with the same parent and name within each thread; parentId=-1 means a root. Node ids are local to each thread.",
                "totalMs includes children; selfMs excludes direct children. Never add parent and child inclusive time, or thread totals, to estimate frame wall time.",
                "gcBytesWithChildren includes descendants; do not sum allocations across ancestors. GC bytes require captured GC.Alloc metadata.",
                "callTrees always belong only to selectedDisplayFrame. additionalFrameDetails contain the configured number of slowest valid neighbors from an independent search window, without a duration threshold; selectedDisplayFrame is excluded.",
                "nearbyFrames contain only the configured summary neighbors, excluding selectedDisplayFrame. Search-only frame times are not exported. Missing or omitted frames have no inferred cause. CPU trees are not GPU timing or a full event timeline.",
                "performanceDebugAtExport is a read-only cumulative snapshot of this Editor process at export time, not selected-frame or remote Player statistics. Disabled statistics are not evidence of zero cost; counts may remain from an earlier enabled period.",
                "Console logs are an export-time snapshot, not proven logs from the selected frame. Cleared logs cannot be recovered.",
                "screenshot is the Unity Editor main window at export time, not the recorded Profiler frame. If capture fails, status and error explain the missing image.",
                "Target, deep profiling and editor state describe export time; loaded historical captures may have different recording settings. Editor/wait markers are not proof of gameplay cost."
            };
        }

        [Serializable]
        private sealed class ProfilerSnapshotFrame
        {
            public int displayFrame;
            public bool available;
            public double timeMs;
            public string status, error;
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
