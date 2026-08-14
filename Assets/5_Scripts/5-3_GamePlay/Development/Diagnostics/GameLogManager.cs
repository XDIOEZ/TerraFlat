using System;
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
    private const float FlushIntervalSeconds = 1f;

    #endregion

    #region 静态状态

    private static readonly object WriteLock = new object();
    private static readonly Encoding LogEncoding = new UTF8Encoding(false);

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

    #endregion

    #region 实例状态

    private float nextFlushTime;
    private bool ownsSession;

    #endregion

    #region 公共状态

    public static string LogDirectoryPath => logDirectoryPath;
    public static string CurrentLogFilePath => currentLogFilePath;
    public static string LastWriteError => lastWriteError;

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

    #endregion

    #region 自动启动

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
        }
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

    private static void HandleUnityLog(string condition, string stackTrace, LogType type)
    {
        string entry = BuildLogEntry(condition, stackTrace, type);

        lock (WriteLock)
        {
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
}
