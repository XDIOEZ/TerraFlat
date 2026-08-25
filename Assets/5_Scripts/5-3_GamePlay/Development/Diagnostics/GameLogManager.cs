using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>自动收集 Unity 运行日志并按游戏会话写入本地文件。</summary>
[DefaultExecutionOrder(-32000)]
[DisallowMultipleComponent]
public sealed class GameLogManager : MonoBehaviour
{
    #region 日志配置

    private const string LogFolderName = "GameLogs";
    private const int MaxRetainedLogFiles = 30;
    private const long MaxLogFileBytes = 16L * 1024L * 1024L;
    private const int MaxEntryCharacters = 64 * 1024;
    private const int MaxRuntimeLogEntries = 256;
    private const int MaxRuntimeLogCharacters = 128 * 1024;
    private const int MaxRuntimeEntryCharacters = 12 * 1024;
    private const int MaxRuntimeSnapshotCharacters = 64 * 1024;
    private const float FlushIntervalSeconds = 1f;

    #endregion

    #region 静态状态

    private static readonly object WriteLock = new object();
    private static readonly Encoding LogEncoding = new UTF8Encoding(false);
    private static readonly Queue<RuntimeLogEntry> RuntimeEntries = new Queue<RuntimeLogEntry>();

    private static GameLogManager instance;
    private static StreamWriter writer;
    private static string logDirectoryPath = string.Empty;
    private static string currentLogFilePath = string.Empty;
    private static string sessionFilePrefix = string.Empty;
    private static volatile string activeSceneName = "<未加载>";
    private static string lastWriteError = string.Empty;
    private static long writtenBytes;
    private static int partIndex;
    private static volatile int lastFrame;
    private static bool subscribed;
    private static int runtimeLogCharacters;
    private static int runtimeLogVersion;
    private static int runtimeWarningCount;
    private static int runtimeErrorCount;

    #endregion

    #region 实例状态

    private float nextFlushTime;
    private bool ownsSession;

    #endregion

    #region 公共状态

    public static string LogDirectoryPath => logDirectoryPath;
    public static string CurrentLogFilePath => currentLogFilePath;
    public static string LastWriteError => lastWriteError;
    public static int RuntimeLogVersion => Volatile.Read(ref runtimeLogVersion);

    /// <summary>运行时内存日志最多保留的条数，也是复制条数设置的有效上限。</summary>
    public static int RuntimeLogCapacity => MaxRuntimeLogEntries;

    public static bool IsCapturing
    {
        get
        {
            lock (WriteLock)
            {
                return writer != null;
            }
        }
    }

    /// <summary>返回当前有界内存日志的数量摘要，供运行时调试面板显示。</summary>
    public static void GetRuntimeLogCounts(out int total, out int warnings, out int errors)
    {
        lock (WriteLock)
        {
            total = RuntimeEntries.Count;
            warnings = runtimeWarningCount;
            errors = runtimeErrorCount;
        }
    }

    /// <summary>生成最近运行日志的文本快照，并把输出严格限制在指定字符数内。</summary>
    public static string GetRuntimeLogSnapshot(int maxCharacters, out bool truncated)
    {
        int boundedCharacters = Math.Max(1, Math.Min(maxCharacters, MaxRuntimeSnapshotCharacters));
        lock (WriteLock)
        {
            StringBuilder builder = new StringBuilder(Math.Min(runtimeLogCharacters + 256, boundedCharacters));
            AppendRuntimeSnapshotHeaderNoLock(builder);
            builder.AppendLine();

            foreach (RuntimeLogEntry entry in RuntimeEntries)
                builder.Append(entry.Text);

            if (builder.Length <= boundedCharacters)
            {
                truncated = false;
                return builder.ToString();
            }

            const string marker = "<较早日志已省略，仅保留最近内容>\n";
            if (marker.Length >= boundedCharacters)
            {
                truncated = true;
                return marker.Substring(0, boundedCharacters);
            }

            int tailLength = Math.Max(0, boundedCharacters - marker.Length);
            truncated = true;
            return marker + builder.ToString(builder.Length - tailLength, tailLength);
        }
    }

    /// <summary>按去重日志槽位数生成最近内容的复制快照，保留完整日志记录而不从单条中间截断。</summary>
    public static string GetRuntimeLogSnapshotByEntryCount(
        int maxEntries,
        out int copiedEntries,
        out int collapsedEntries,
        out bool truncated)
    {
        int boundedEntries = Math.Max(1, Math.Min(maxEntries, MaxRuntimeLogEntries));
        lock (WriteLock)
        {
            List<RuntimeLogCopyGroup> groups = BuildRuntimeLogCopyGroupsNoLock();
            copiedEntries = Math.Min(boundedEntries, groups.Count);
            int skippedGroups = groups.Count - copiedEntries;
            truncated = skippedGroups > 0;
            collapsedEntries = 0;

            StringBuilder builder = new StringBuilder(Math.Min(runtimeLogCharacters + 256, MaxRuntimeLogCharacters));
            AppendRuntimeSnapshotHeaderNoLock(builder);
            builder.Append("# CopiedSlots: ").Append(copiedEntries).AppendLine();
            if (truncated)
            {
                builder.Append("<较早的 ").Append(skippedGroups)
                    .AppendLine(" 类日志已省略，仅复制最近内容>");
            }

            builder.AppendLine();
            for (int i = skippedGroups; i < groups.Count; i++)
            {
                RuntimeLogCopyGroup group = groups[i];
                if (group.RepeatCount > 1)
                {
                    collapsedEntries += group.RepeatCount - 1;
                    builder.Append("[重复 ×").Append(group.RepeatCount).AppendLine("]");
                }

                builder.Append(group.LatestEntry.Text);
            }

            return builder.ToString();
        }
    }

    /// <summary>按最后出现顺序合并相同级别、正文和堆栈的日志，供复制快照按槽位截取。</summary>
    private static List<RuntimeLogCopyGroup> BuildRuntimeLogCopyGroupsNoLock()
    {
        List<RuntimeLogCopyGroup> groups = new List<RuntimeLogCopyGroup>();
        int entryIndex = 0;
        foreach (RuntimeLogEntry entry in RuntimeEntries)
        {
            RuntimeLogCopyGroup matchedGroup = null;
            for (int i = 0; i < groups.Count; i++)
            {
                if (!groups[i].LatestEntry.HasSameContent(entry))
                    continue;

                matchedGroup = groups[i];
                break;
            }

            if (matchedGroup == null)
            {
                groups.Add(new RuntimeLogCopyGroup(entry, entryIndex));
            }
            else
            {
                matchedGroup.LatestEntry = entry;
                matchedGroup.LatestIndex = entryIndex;
                matchedGroup.RepeatCount++;
            }

            entryIndex++;
        }

        groups.Sort((left, right) => left.LatestIndex.CompareTo(right.LatestIndex));
        return groups;
    }

    /// <summary>写入运行时日志快照的统一会话头。</summary>
    private static void AppendRuntimeSnapshotHeaderNoLock(StringBuilder builder)
    {
        builder.Append("# FlatWorld Runtime Log\n");
        builder.Append("# File: ").AppendLine(
            string.IsNullOrWhiteSpace(currentLogFilePath) ? "<尚未创建>" : currentLogFilePath);
        builder.Append("# Entries: ").Append(RuntimeEntries.Count)
            .Append("  Warnings: ").Append(runtimeWarningCount)
            .Append("  Errors: ").Append(runtimeErrorCount)
            .AppendLine();
    }

    /// <summary>清空运行时面板使用的内存日志；磁盘会话日志保持不变。</summary>
    public static void ClearRuntimeLogBuffer()
    {
        lock (WriteLock)
        {
            RuntimeEntries.Clear();
            runtimeLogCharacters = 0;
            runtimeWarningCount = 0;
            runtimeErrorCount = 0;
            Interlocked.Increment(ref runtimeLogVersion);
        }
    }

    #endregion

    #region 自动启动

    /// <summary>在最早的托管运行阶段重置状态并订阅日志，保证场景与资源加载错误不会丢失。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Unsubscribe();

        lock (WriteLock)
        {
            CloseWriterNoLock();
            instance = null;
            logDirectoryPath = string.Empty;
            currentLogFilePath = string.Empty;
            sessionFilePrefix = string.Empty;
            activeSceneName = "<未加载>";
            lastWriteError = string.Empty;
            writtenBytes = 0;
            partIndex = 0;
            lastFrame = 0;
            RuntimeEntries.Clear();
            runtimeLogCharacters = 0;
            runtimeLogVersion = 0;
            runtimeWarningCount = 0;
            runtimeErrorCount = 0;
        }

        Subscribe();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        GameLogManager existing = FindObjectOfType<GameLogManager>();
        if (existing != null)
        {
            instance = existing;
            return;
        }

        GameObject root = new GameObject("[Game Log Manager]");
        root.AddComponent<GameLogManager>();
    }

    #endregion

    #region 生命周期

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        StartSession();
    }

    private void Update()
    {
        lastFrame = Time.frameCount;
        if (Time.unscaledTime < nextFlushTime)
            return;

        nextFlushTime = Time.unscaledTime + FlushIntervalSeconds;
        Flush();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            Flush();
    }

    private void OnApplicationQuit()
    {
        StopSession();
    }

    private void OnDestroy()
    {
        if (instance != this)
            return;

        StopSession();
        instance = null;
    }

    #endregion

    #region 工作日志接口

    public static void Log(
        string system,
        string action,
        string details,
        Object context = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        Debug.Log(BuildWorkMessage(system, action, details, callerMember, callerFile, callerLine), context);
    }

    public static void LogWarning(
        string system,
        string action,
        string details,
        Object context = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        Debug.LogWarning(BuildWorkMessage(system, action, details, callerMember, callerFile, callerLine), context);
    }

    public static void LogError(
        string system,
        string action,
        string details,
        Object context = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        Debug.LogError(BuildWorkMessage(system, action, details, callerMember, callerFile, callerLine), context);
    }

    public static void LogException(
        string system,
        string action,
        Exception exception,
        Object context = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        string details = exception == null ? "<空异常>" : exception.ToString();
        Debug.LogError(BuildWorkMessage(system, action, details, callerMember, callerFile, callerLine), context);
    }

    public static void Flush()
    {
        lock (WriteLock)
        {
            if (writer == null)
                return;

            try
            {
                writer.Flush();
            }
            catch (Exception exception)
            {
                FailWriterNoLock(exception);
            }
        }
    }

    #endregion

    #region 会话管理

    /// <summary>建立磁盘日志会话，并把文件创建前捕获到的日志补写进会话文件。</summary>
    private void StartSession()
    {
        if (ownsSession)
            return;

        try
        {
            logDirectoryPath = Path.Combine(Application.persistentDataPath, LogFolderName);
            Directory.CreateDirectory(logDirectoryPath);
            CleanupOldLogFiles();

            string sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
            sessionFilePrefix = $"GameSession_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{sessionId}";
            activeSceneName = FormatSceneName(SceneManager.GetActiveScene());

            lock (WriteLock)
            {
                partIndex = 0;
                OpenPartNoLock();
                WriteRawNoLock(BuildSessionHeader());
                WriteBufferedRuntimeEntriesNoLock();
                writer.Flush();
            }

            Subscribe();
            ownsSession = true;
            nextFlushTime = Time.unscaledTime + FlushIntervalSeconds;
            Debug.Log($"[GameLogManager] 游戏日志已开始收集：{currentLogFilePath}");
        }
        catch (Exception exception)
        {
            lastWriteError = exception.ToString();
            lock (WriteLock)
            {
                CloseWriterNoLock();
            }

            Debug.LogError($"[GameLogManager] 无法创建游戏日志：{exception.Message}");
        }
    }

    private void StopSession()
    {
        if (!ownsSession)
            return;

        ownsSession = false;
        Unsubscribe();

        lock (WriteLock)
        {
            if (writer == null)
                return;

            try
            {
                WriteRawNoLock($"# SessionEnded: {DateTimeOffset.Now:O}\n");
                writer.Flush();
            }
            catch (Exception exception)
            {
                lastWriteError = exception.ToString();
            }
            finally
            {
                CloseWriterNoLock();
            }
        }
    }

    private static void Subscribe()
    {
        if (subscribed)
            return;

        Application.logMessageReceivedThreaded += HandleUnityLog;
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        subscribed = true;
    }

    private static void Unsubscribe()
    {
        if (!subscribed)
            return;

        Application.logMessageReceivedThreaded -= HandleUnityLog;
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        subscribed = false;
    }

    #endregion

    #region 日志写入

    /// <summary>从任意 Unity 日志线程写入有界内存缓冲，并在磁盘会话可用时同步落盘。</summary>
    private static void HandleUnityLog(string condition, string stackTrace, LogType type)
    {
        string entry = BuildLogEntry(condition, stackTrace, type);

        lock (WriteLock)
        {
            CaptureRuntimeEntryNoLock(entry, type, condition, stackTrace);
            if (writer == null)
                return;

            try
            {
                int entryBytes = LogEncoding.GetByteCount(entry);
                if (writtenBytes > 0 && writtenBytes + entryBytes > MaxLogFileBytes)
                    RotateFileNoLock();

                writer.Write(entry);
                writtenBytes += entryBytes;

                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    writer.Flush();
            }
            catch (Exception exception)
            {
                FailWriterNoLock(exception);
            }
        }
    }

    /// <summary>把日志加入有界内存队列，达到条数或字符上限时优先移除最早内容。</summary>
    private static void CaptureRuntimeEntryNoLock(
        string entry,
        LogType type,
        string condition,
        string stackTrace)
    {
        string limitedEntry = entry.Length <= MaxRuntimeEntryCharacters
            ? entry
            : entry.Substring(0, MaxRuntimeEntryCharacters) + "\n<单条日志已截断>\n\n";
        RuntimeLogEntry runtimeEntry = new RuntimeLogEntry(limitedEntry, type, condition, stackTrace);

        while (RuntimeEntries.Count > 0 &&
               (RuntimeEntries.Count >= MaxRuntimeLogEntries ||
                runtimeLogCharacters + runtimeEntry.CharacterCount > MaxRuntimeLogCharacters))
        {
            RemoveOldestRuntimeEntryNoLock();
        }

        RuntimeEntries.Enqueue(runtimeEntry);
        runtimeLogCharacters += runtimeEntry.CharacterCount;
        if (IsErrorType(type))
            runtimeErrorCount++;
        else if (type == LogType.Warning)
            runtimeWarningCount++;
        Interlocked.Increment(ref runtimeLogVersion);
    }

    /// <summary>移除最早一条内存日志并同步维护字符数与级别统计。</summary>
    private static void RemoveOldestRuntimeEntryNoLock()
    {
        RuntimeLogEntry removed = RuntimeEntries.Dequeue();
        runtimeLogCharacters = Math.Max(0, runtimeLogCharacters - removed.CharacterCount);
        if (IsErrorType(removed.Type))
            runtimeErrorCount = Math.Max(0, runtimeErrorCount - 1);
        else if (removed.Type == LogType.Warning)
            runtimeWarningCount = Math.Max(0, runtimeWarningCount - 1);
    }

    /// <summary>把日志文件创建前捕获到的有界日志补写到当前会话。</summary>
    private static void WriteBufferedRuntimeEntriesNoLock()
    {
        if (RuntimeEntries.Count == 0)
            return;

        WriteRawNoLock("# BufferedBeforeLogFileOpened\n\n");
        foreach (RuntimeLogEntry entry in RuntimeEntries)
            WriteRawNoLock(entry.Text);
    }

    /// <summary>判断日志级别是否属于错误类别。</summary>
    private static bool IsErrorType(LogType type)
    {
        return type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
    }

    private static void HandleActiveSceneChanged(Scene previous, Scene next)
    {
        activeSceneName = FormatSceneName(next);
    }

    private static void OpenPartNoLock()
    {
        string suffix = partIndex == 0 ? string.Empty : $"_part{partIndex:D2}";
        currentLogFilePath = Path.Combine(logDirectoryPath, sessionFilePrefix + suffix + ".log");

        FileStream stream = new FileStream(
            currentLogFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.ReadWrite,
            4096,
            FileOptions.SequentialScan);

        writer = new StreamWriter(stream, LogEncoding, 4096)
        {
            NewLine = "\n"
        };
        writtenBytes = 0;
    }

    private static void RotateFileNoLock()
    {
        writer.Flush();
        writer.Dispose();
        writer = null;

        partIndex++;
        OpenPartNoLock();
        WriteRawNoLock($"# ContinuedSession: {DateTimeOffset.Now:O}\n# Part: {partIndex}\n\n");
    }

    private static void WriteRawNoLock(string text)
    {
        writer.Write(text);
        writtenBytes += LogEncoding.GetByteCount(text);
    }

    private static void FailWriterNoLock(Exception exception)
    {
        lastWriteError = exception.ToString();
        CloseWriterNoLock();
    }

    private static void CloseWriterNoLock()
    {
        if (writer == null)
            return;

        try
        {
            writer.Dispose();
        }
        catch (Exception exception)
        {
            lastWriteError = exception.ToString();
        }
        finally
        {
            writer = null;
        }
    }

    #endregion

    #region 内容格式

    private static string BuildSessionHeader()
    {
        StringBuilder builder = new StringBuilder(512);
        builder.AppendLine("# FlatWorld Game Session Log");
        builder.Append("# Started: ").AppendLine(DateTimeOffset.Now.ToString("O"));
        builder.Append("# Product: ").AppendLine(Application.productName);
        builder.Append("# Version: ").AppendLine(Application.version);
        builder.Append("# BuildGuid: ").AppendLine(Application.buildGUID);
        builder.Append("# Unity: ").AppendLine(Application.unityVersion);
        builder.Append("# Platform: ").AppendLine(Application.platform.ToString());
        builder.Append("# OperatingSystem: ").AppendLine(SystemInfo.operatingSystem);
        builder.Append("# DeviceModel: ").AppendLine(SystemInfo.deviceModel);
        builder.Append("# GraphicsDevice: ").AppendLine(SystemInfo.graphicsDeviceName);
        builder.Append("# SystemMemoryMB: ").AppendLine(SystemInfo.systemMemorySize.ToString());
        builder.Append("# InitialScene: ").AppendLine(activeSceneName);
        builder.Append("# LogDirectory: ").AppendLine(logDirectoryPath);
        builder.AppendLine();
        return builder.ToString();
    }

    private static string BuildLogEntry(string condition, string stackTrace, LogType type)
    {
        StringBuilder builder = new StringBuilder(512);
        builder.Append('[').Append(DateTimeOffset.Now.ToString("O")).Append("] ");
        builder.Append('[').Append(type).Append("] ");
        builder.Append("[Thread=").Append(Thread.CurrentThread.ManagedThreadId).Append("] ");
        builder.Append("[Frame=").Append(lastFrame).Append("] ");
        builder.Append("[Scene=").Append(activeSceneName).AppendLine("]");
        builder.Append("  Message: ").AppendLine(FormatMultiline(condition));

        if (!string.IsNullOrEmpty(stackTrace))
            builder.Append("  StackTrace: ").AppendLine(FormatMultiline(stackTrace));

        builder.AppendLine();
        return builder.ToString();
    }

    private static string BuildWorkMessage(
        string system,
        string action,
        string details,
        string callerMember,
        string callerFile,
        int callerLine)
    {
        string fileName = GetCallerFileName(callerFile);
        return $"[WORK][{FormatTag(system)}][{FormatTag(action)}] {details ?? string.Empty} | Caller={fileName}:{callerLine} {callerMember}";
    }

    private static string GetCallerFileName(string callerFile)
    {
        if (string.IsNullOrEmpty(callerFile))
            return "<未知文件>";

        int slashIndex = Math.Max(callerFile.LastIndexOf('/'), callerFile.LastIndexOf('\\'));
        return slashIndex >= 0 ? callerFile.Substring(slashIndex + 1) : callerFile;
    }

    private static string FormatTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "未分类";

        return value.Replace("\r", " ").Replace("\n", " ").Replace("]", ")");
    }

    private static string FormatMultiline(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        string limited = value.Length <= MaxEntryCharacters
            ? value
            : value.Substring(0, MaxEntryCharacters) + "\n<内容已截断>";

        return limited
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\n", "\n    ");
    }

    private static string FormatSceneName(Scene scene)
    {
        return scene.IsValid() ? $"{scene.name}#{scene.buildIndex}" : "<未加载>";
    }

    #endregion

    #region 文件清理

    private static void CleanupOldLogFiles()
    {
        try
        {
            FileInfo[] files = new DirectoryInfo(logDirectoryPath).GetFiles("*.log", SearchOption.TopDirectoryOnly);
            Array.Sort(files, (left, right) => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc));

            int keepOldFileCount = MaxRetainedLogFiles - 1;
            for (int i = keepOldFileCount; i < files.Length; i++)
                files[i].Delete();
        }
        catch (Exception exception)
        {
            lastWriteError = exception.ToString();
        }
    }

    #endregion

    #region 内存日志结构

    /// <summary>有界运行时日志的一条不可变记录。</summary>
    private readonly struct RuntimeLogEntry
    {
        /// <summary>创建一条已完成格式化的运行时日志记录。</summary>
        public RuntimeLogEntry(string text, LogType type, string condition, string stackTrace)
        {
            Text = text ?? string.Empty;
            Type = type;
            ConditionLength = condition?.Length ?? 0;
            StackTraceLength = stackTrace?.Length ?? 0;
            ConditionHash = ComputeContentHash(condition);
            StackTraceHash = ComputeContentHash(stackTrace);
            CharacterCount = Text.Length;
        }

        /// <summary>已格式化的日志正文。</summary>
        public string Text { get; }

        /// <summary>Unity 日志级别。</summary>
        public LogType Type { get; }

        /// <summary>原始正文长度，与稳定散列共同识别重复内容且不重复持有大字符串。</summary>
        public int ConditionLength { get; }

        /// <summary>原始堆栈长度。</summary>
        public int StackTraceLength { get; }

        /// <summary>原始正文的稳定 64 位散列。</summary>
        public ulong ConditionHash { get; }

        /// <summary>原始调用堆栈的稳定 64 位散列。</summary>
        public ulong StackTraceHash { get; }

        /// <summary>正文字符数。</summary>
        public int CharacterCount { get; }

        /// <summary>判断两条日志是否属于可堆叠的同一内容。</summary>
        public bool HasSameContent(RuntimeLogEntry other)
        {
            return Type == other.Type &&
                   ConditionLength == other.ConditionLength &&
                   StackTraceLength == other.StackTraceLength &&
                   ConditionHash == other.ConditionHash &&
                   StackTraceHash == other.StackTraceHash;
        }

        /// <summary>逐字符计算稳定散列，避免为去重长期保存完整正文和堆栈副本。</summary>
        private static ulong ComputeContentHash(string value)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;
            if (string.IsNullOrEmpty(value))
                return hash;

            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= prime;
            }

            return hash;
        }
    }

    /// <summary>复制快照中的一个去重槽位，保存最后一次出现内容与累计次数。</summary>
    private sealed class RuntimeLogCopyGroup
    {
        public RuntimeLogCopyGroup(RuntimeLogEntry entry, int latestIndex)
        {
            LatestEntry = entry;
            LatestIndex = latestIndex;
            RepeatCount = 1;
        }

        public RuntimeLogEntry LatestEntry { get; set; }
        public int LatestIndex { get; set; }
        public int RepeatCount { get; set; }
    }

    #endregion
}
