using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using FlatWorld.Diagnostics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

/// <summary>打包版常驻轻量帧时间缓存，按需将最近一段时间打包为单个诊断文件。</summary>
[DefaultExecutionOrder(32000)]
public sealed class RuntimePerformanceReportRecorder : MonoBehaviour
{
    #region 缓冲与生命周期

    public const int MinimumExportSeconds = 5;
    public const int MaximumExportSeconds = 120;
    private const int MaximumRecordedFrames = 32768;

    private static RuntimePerformanceReportRecorder instance;
    private readonly RecordedFrame[] frames = new RecordedFrame[MaximumRecordedFrames];
    private int nextFrameIndex, recordedFrameCount;
    private Task<string> pendingExport;

    public static bool IsExporting => instance != null && instance.pendingExport != null;
    public static string LastReportPath { get; private set; } = string.Empty;
    public static string LastError { get; private set; } = string.Empty;
    public static string ReportsDirectory => Path.Combine(Application.persistentDataPath, "PerformanceReports");

    public static double AvailableSeconds
    {
        get
        {
            if (instance == null || instance.recordedFrameCount == 0) return 0d;
            int oldest = (instance.nextFrameIndex - instance.recordedFrameCount + MaximumRecordedFrames) % MaximumRecordedFrames;
            RecordedFrame first = instance.frames[oldest];
            int newest = (instance.nextFrameIndex - 1 + MaximumRecordedFrames) % MaximumRecordedFrames;
            return Math.Min(MaximumExportSeconds, Math.Max(0d,
                instance.frames[newest].timeSeconds - first.timeSeconds + first.frameMilliseconds / 1000d));
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        instance = null;
        LastReportPath = LastError = string.Empty;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRecorder()
    {
        if (instance != null) return;
        var root = new GameObject(nameof(RuntimePerformanceReportRecorder));
        root.AddComponent<RuntimePerformanceReportRecorder>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        PersistentRuntimeObjectHierarchy.MoveToPersistentCategory(gameObject, PersistentRuntimeObjectCategory.Debug);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        float frameMilliseconds = Time.unscaledDeltaTime * 1000f;
        if (frameMilliseconds > 0f && !float.IsNaN(frameMilliseconds) && !float.IsInfinity(frameMilliseconds))
        {
            frames[nextFrameIndex] = new RecordedFrame
            {
                timeSeconds = Time.realtimeSinceStartupAsDouble,
                frameMilliseconds = frameMilliseconds
            };
            nextFrameIndex = (nextFrameIndex + 1) % MaximumRecordedFrames;
            if (recordedFrameCount < MaximumRecordedFrames) recordedFrameCount++;
        }
        PublishCompletedExport();
    }

    private struct RecordedFrame
    {
        internal double timeSeconds;
        internal float frameMilliseconds;
    }

    #endregion

    #region 最近时间范围与报告内容

    public static bool TryExportRecentSeconds(int seconds, out string error)
    {
        error = string.Empty;
        if (instance == null) { error = "性能帧缓存尚未就绪。"; return false; }
        instance.PublishCompletedExport();
        if (instance.pendingExport != null) { error = "上一份性能报告还在导出中。"; return false; }
        if (seconds < MinimumExportSeconds || seconds > MaximumExportSeconds)
        {
            error = $"导出范围必须为 {MinimumExportSeconds}～{MaximumExportSeconds} 秒。";
            return false;
        }

        try
        {
            FrameReport report = instance.BuildReport(seconds);
            if (report == null) { error = "还没有可用帧，请先运行游戏。"; return false; }
            string json = JsonUtility.ToJson(report, true);
            string logs = GameLogManager.GetRuntimeLogSnapshot(32768, out bool logsTruncated);
            string explanation = "FlatWorld 打包版性能报告\n" +
                "performance.json: 最近指定时间内的帧间隔、FPS 与设备内存；不是 Unity Profiler CPU 调用树。\n" +
                "runtime-logs.txt: 当前内存日志快照" + (logsTruncated ? "（较早日志已截断）" : "") + "。\n" +
                "performancePoints: 仅在启用性能次数统计时有数据，为开启以来的累计值，不属于选中秒数范围。\n" +
                "requestedSeconds 为用户范围；availableSeconds 和 completeWindow 标识实际缓存覆盖情况。\n";
            string name = $"performance-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.zip";
            string path = Path.Combine(ReportsDirectory, name);
            LastError = string.Empty;
            instance.pendingExport = Task.Run(() => WriteReportArchive(path, json, logs, explanation));
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetBaseException().Message;
            LastError = error;
            return false;
        }
    }

    private FrameReport BuildReport(int requestedSeconds)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        double cutoff = now - requestedSeconds;
        int firstIndex = (nextFrameIndex - recordedFrameCount + MaximumRecordedFrames) % MaximumRecordedFrames;
        var selected = new List<RecordedFrame>(Math.Min(recordedFrameCount, requestedSeconds * 240));
        for (int i = 0; i < recordedFrameCount; i++)
        {
            RecordedFrame frame = frames[(firstIndex + i) % MaximumRecordedFrames];
            if (frame.timeSeconds >= cutoff && frame.timeSeconds <= now) selected.Add(frame);
        }
        if (selected.Count == 0) return null;

        var report = new FrameReport
        {
            schemaVersion = 1,
            createdUtc = DateTime.UtcNow.ToString("o"),
            platform = Application.platform.ToString(),
            unityVersion = Application.unityVersion,
            gameVersion = Application.version,
            scene = SceneManager.GetActiveScene().name,
            requestedSeconds = requestedSeconds,
            availableSeconds = AvailableSeconds,
            completeWindow = AvailableSeconds >= requestedSeconds - 0.05d,
            frameCount = selected.Count,
            allocatedMemoryBytes = Profiler.GetTotalAllocatedMemoryLong(),
            reservedMemoryBytes = Profiler.GetTotalReservedMemoryLong(),
            monoHeapUsedBytes = Profiler.GetMonoUsedSizeLong(),
            performanceStatisticsEnabled = PerformanceDebugRegistry.StatisticsEnabled,
            frameTimeSource = "Time.unscaledDeltaTime (wall-clock frame interval, not CPU thread time)",
            performancePointScope = "session cumulative; not restricted to requestedSeconds",
            frames = new FrameEntry[selected.Count]
        };
        var orderedFrameTimes = new float[selected.Count];
        double firstTime = selected[0].timeSeconds;
        double totalMilliseconds = 0d;
        for (int i = 0; i < selected.Count; i++)
        {
            float milliseconds = selected[i].frameMilliseconds;
            report.frames[i] = new FrameEntry
            {
                secondsFromFirstFrame = (float)(selected[i].timeSeconds - firstTime),
                frameMilliseconds = milliseconds
            };
            orderedFrameTimes[i] = milliseconds;
            totalMilliseconds += milliseconds;
            if (milliseconds > 1000f / 30f) report.framesBelow30Fps++;
            if (milliseconds > 1000f / 60f) report.framesBelow60Fps++;
        }

        Array.Sort(orderedFrameTimes);
        report.coveredSeconds = Math.Min(requestedSeconds,
            selected[selected.Count - 1].timeSeconds - firstTime + selected[0].frameMilliseconds / 1000d);
        report.averageFrameMilliseconds = (float)(totalMilliseconds / selected.Count);
        report.averageFps = report.averageFrameMilliseconds > 0f ? 1000f / report.averageFrameMilliseconds : 0f;
        report.fastestFrameMilliseconds = orderedFrameTimes[0];
        report.p95FrameMilliseconds = orderedFrameTimes[(int)Math.Ceiling(selected.Count * 0.95d) - 1];
        report.p99FrameMilliseconds = orderedFrameTimes[(int)Math.Ceiling(selected.Count * 0.99d) - 1];
        report.slowestFrameMilliseconds = orderedFrameTimes[orderedFrameTimes.Length - 1];

        PerformanceDebugRegistry.Snapshot[] statistics = PerformanceDebugRegistry.CaptureSnapshot();
        var points = new List<PerformancePointEntry>();
        if (report.performanceStatisticsEnabled)
            foreach (PerformanceDebugRegistry.Snapshot statistic in statistics)
                if (statistic.Calls != 0 || statistic.Work != 0 || statistic.HasGauge)
                    points.Add(new PerformancePointEntry
                    {
                        name = statistic.Name,
                        calls = statistic.Calls,
                        work = statistic.Work,
                        totalMilliseconds = statistic.TotalMilliseconds,
                        maximumMilliseconds = statistic.MaximumMilliseconds,
                        hasGauge = statistic.HasGauge,
                        gauge = statistic.Gauge,
                        peakGauge = statistic.PeakGauge
                    });
        report.performancePoints = points.ToArray();
        return report;
    }

    [Serializable]
    private sealed class FrameReport
    {
        public int schemaVersion, requestedSeconds, frameCount, framesBelow30Fps, framesBelow60Fps;
        public string createdUtc, platform, unityVersion, gameVersion, scene, frameTimeSource, performancePointScope;
        public bool completeWindow, performanceStatisticsEnabled;
        public double availableSeconds, coveredSeconds;
        public float averageFps, averageFrameMilliseconds, fastestFrameMilliseconds,
            p95FrameMilliseconds, p99FrameMilliseconds, slowestFrameMilliseconds;
        public long allocatedMemoryBytes, reservedMemoryBytes, monoHeapUsedBytes;
        public FrameEntry[] frames;
        public PerformancePointEntry[] performancePoints;
    }

    [Serializable]
    private struct FrameEntry
    {
        public float secondsFromFirstFrame, frameMilliseconds;
    }

    [Serializable]
    private struct PerformancePointEntry
    {
        public string name;
        public long calls, work, gauge, peakGauge;
        public bool hasGauge;
        public double totalMilliseconds, maximumMilliseconds;
    }

    #endregion

    #region 后台压缩与主线程反馈

    private static string WriteReportArchive(string path, string json, string logs, string explanation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string pendingPath = path + ".pending";
        try
        {
            using (var stream = new FileStream(pendingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteTextEntry(archive, "performance.json", json);
                WriteTextEntry(archive, "runtime-logs.txt", logs);
                WriteTextEntry(archive, "README.txt", explanation);
            }
            File.Move(pendingPath, path);
            return path;
        }
        catch
        {
            try { if (File.Exists(pendingPath)) File.Delete(pendingPath); }
            catch (IOException) { }
            throw;
        }
    }

    private static void WriteTextEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(
            archive.CreateEntry(name, System.IO.Compression.CompressionLevel.Fastest).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private void PublishCompletedExport()
    {
        if (pendingExport == null || !pendingExport.IsCompleted) return;
        Task<string> completed = pendingExport;
        pendingExport = null;
        if (completed.IsFaulted || completed.IsCanceled)
        {
            LastError = completed.Exception?.GetBaseException().Message ?? "写入报告失败或已取消。";
            Debug.LogWarning("[性能报告] 导出失败：" + LastError);
            return;
        }

        LastReportPath = completed.Result;
        LastError = string.Empty;
        try { GUIUtility.systemCopyBuffer = LastReportPath; }
        catch (Exception exception) { Debug.LogWarning("[性能报告] 文件已导出，但复制路径失败：" + exception.Message); }
        Debug.Log("[性能报告] 已导出 ZIP，路径已尝试复制到剪贴板：" + LastReportPath);
    }

    #endregion
}
