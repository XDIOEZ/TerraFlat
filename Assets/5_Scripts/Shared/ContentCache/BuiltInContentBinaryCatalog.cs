using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MemoryPack;
using UnityEngine;

/// <summary>构建期收集纯配置载荷，Player 从同一资源目录恢复，不再读取原始 JSON。</summary>
public static class BuiltInContentBinaryCatalog
{
    #region 二进制目录与生命周期

    public const string ResourcePath = "CompiledContent/BuiltInConfigurations";
    private const int ArchiveVersion = 1;
    private static Dictionary<string, BuiltInContentBinaryEntry> entries;
    private static Dictionary<string, BuiltInContentBinaryEntry> recording;
    private static string streamingAssetsRoot;
    private static int sourceReloads;
    private static int readCount;
    public static int ReadCount => Volatile.Read(ref readCount);
    public static int EntryCount => entries?.Count ?? 0;
    public static double InitializationMilliseconds { get; private set; }
    public static bool IsRecording => recording != null;
    public static string[] RecordingKeys => recording?.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
    public static bool IsActive => entries != null && Volatile.Read(ref sourceReloads) == 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void Initialize()
    {
        streamingAssetsRoot = Application.streamingAssetsPath.Replace('\\', '/').TrimEnd('/') + "/";
        entries = null;
        sourceReloads = 0;
        readCount = 0;
        InitializationMilliseconds = 0;
#if !UNITY_EDITOR
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-flatworld-source-configurations") >= 0)
        {
            Debug.Log("[构建期二进制目录] 启动验收使用源配置路径。");
            return;
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null) throw new InvalidDataException("缺少构建期二进制配置目录，请重新构建游戏。");
        try { LoadArchive(asset.bytes); }
        finally { Resources.UnloadAsset(asset); }
        InitializationMilliseconds = timer.Elapsed.TotalMilliseconds;
        Debug.Log($"[构建期二进制目录] 已接入 {EntryCount} 个配置载荷，{InitializationMilliseconds:F1} ms。");
#endif
    }

    public static void LoadArchive(byte[] bytes)
    {
        BuiltInContentBinaryArchive archive = MemoryPackSerializer.Deserialize<BuiltInContentBinaryArchive>(bytes);
        if (archive?.Version != ArchiveVersion || archive.Entries == null)
            throw new InvalidDataException("构建期二进制配置目录版本不匹配。");
        var candidate = new Dictionary<string, BuiltInContentBinaryEntry>(archive.Entries.Length, StringComparer.Ordinal);
        foreach (BuiltInContentBinaryEntry entry in archive.Entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Key) || entry.Payload == null || !candidate.TryAdd(entry.Key, entry))
                throw new InvalidDataException("构建期二进制配置目录包含重复或空载荷。");
        }
        entries = candidate;
    }

    /// <summary>隔离验证使用临时目录，退出时恢复原目录及诊断计数。</summary>
    public static IDisposable UseArchive(byte[] bytes)
    {
        var lease = new BinaryCatalogLease(entries, ReadCount);
        LoadArchive(bytes);
        return lease;
    }

    private sealed class BinaryCatalogLease : IDisposable
    {
        private Dictionary<string, BuiltInContentBinaryEntry> previous;
        private readonly int previousReads;
        private bool disposed;
        public BinaryCatalogLease(Dictionary<string, BuiltInContentBinaryEntry> previous, int previousReads)
        { this.previous = previous; this.previousReads = previousReads; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            entries = previous;
            previous = null;
            Interlocked.Exchange(ref readCount, previousReads);
        }
    }

    public static bool TryRead<T>(string key, int version, out T value)
    {
        value = default;
        if (!IsActive) return false;
        if (!entries.TryGetValue(key, out BuiltInContentBinaryEntry entry)) return false;
        if (entry.Version != version || entry.Type != typeof(T).FullName)
            throw new InvalidDataException("构建期二进制配置契约不匹配：" + key);
        value = MemoryPackSerializer.Deserialize<T>(entry.Payload);
        Interlocked.Increment(ref readCount);
        return true;
    }

    /// <summary>F5 候选读取源配置，租约覆盖整个异步事务且取消时释放。</summary>
    public static IDisposable UseSourceConfigurations()
    {
        Interlocked.Increment(ref sourceReloads);
        return new SourceConfigurationLease();
    }

    private sealed class SourceConfigurationLease : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Interlocked.Decrement(ref sourceReloads);
        }
    }

    #endregion

    #region 通用文件与资源入口

    public static string GetStreamingAssetKey(string path)
    {
        string normalized = path.Replace('\\', '/');
        string root = streamingAssetsRoot ?? Application.streamingAssetsPath.Replace('\\', '/').TrimEnd('/') + "/";
        return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? "file:" + normalized.Substring(root.Length) : null;
    }

    public static T ReadFile<T>(string path, Func<string, T> parse, int version = 1)
    {
        string key = GetStreamingAssetKey(path);
        if (key != null && TryRead(key, version, out T cached)) return cached;
        if (key != null && IsActive) throw new InvalidDataException("构建期二进制目录缺少配置：" + key);
        T value = parse(StreamingAssetsTextLoader.ReadAllText(path));
        if (key != null) Record(key, version, value);
        return value;
    }

    public static IEnumerator ReadFileAsync<T>(string path, Func<string, T> parse,
        Action<T> completed, Action<Exception> failed, int version = 1)
    {
        string key = GetStreamingAssetKey(path);
        if (key != null && IsActive)
            return StreamingAssetsTextLoader.BeginPureDataAsync(() =>
            {
                if (!TryRead(key, version, out T value))
                    throw new InvalidDataException("构建期二进制目录缺少配置：" + key);
                return value;
            }, completed, failed);
        return StreamingAssetsTextLoader.ReadAndParsePrivateDataAsync(path, text =>
        {
            T value = parse(text);
            if (key != null) Record(key, version, value);
            return value;
        }, completed, failed);
    }

    public static T ReadResource<T>(string resourcePath, Func<string, T> parse, int version = 1)
    {
        string key = "resource:" + resourcePath;
        if (TryRead(key, version, out T cached)) return cached;
        if (IsActive) throw new InvalidDataException("构建期二进制目录缺少资源配置：" + resourcePath);
        TextAsset asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null) { Record(key, version, default(T)); return default; }
        T value = parse(asset.text);
        Record(key, version, value);
        return value;
    }

    public static string[] ReadResourceDirectoryPaths(string resourceRoot)
    {
        string key = "resource-list:" + resourceRoot;
        if (TryRead(key, 1, out string[] cached)) return cached;
        if (IsActive) throw new InvalidDataException("构建期二进制目录缺少资源配置清单：" + resourceRoot);
        string[] paths = Resources.LoadAll<TextAsset>(resourceRoot).Select(asset =>
        {
#if UNITY_EDITOR
            string path = UnityEditor.AssetDatabase.GetAssetPath(asset);
            int marker = path.LastIndexOf("/Resources/", StringComparison.Ordinal);
            if (marker >= 0) return Path.ChangeExtension(path.Substring(marker + "/Resources/".Length), null);
#endif
            return resourceRoot + "/" + asset.name;
        }).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Record(key, 1, paths);
        return paths;
    }

    #endregion

    #region 构建期收集

    public static void BeginRecording()
    {
        if (recording != null) throw new InvalidOperationException("二进制目录烘焙已在进行。");
        streamingAssetsRoot = Application.streamingAssetsPath.Replace('\\', '/').TrimEnd('/') + "/";
        recording = new Dictionary<string, BuiltInContentBinaryEntry>(StringComparer.Ordinal);
    }

    public static void Record<T>(string key, int version, T value)
    {
        Dictionary<string, BuiltInContentBinaryEntry> target = recording;
        if (target == null) return;
        var entry = new BuiltInContentBinaryEntry
        { Key = key, Version = version, Type = typeof(T).FullName, Payload = MemoryPackSerializer.Serialize(value) };
        lock (target)
        {
            if (target.TryGetValue(key, out BuiltInContentBinaryEntry previous) &&
                (previous.Version != version || previous.Type != entry.Type || !previous.Payload.AsSpan().SequenceEqual(entry.Payload)))
                throw new InvalidDataException("同一烘焙配置键产生不同载荷：" + key);
            target[key] = entry;
        }
    }

    public static byte[] FinishRecording()
    {
        Dictionary<string, BuiltInContentBinaryEntry> completed = recording
            ?? throw new InvalidOperationException("二进制目录尚未开始烘焙。");
        recording = null;
        return MemoryPackSerializer.Serialize(new BuiltInContentBinaryArchive
        { Version = ArchiveVersion, Entries = completed.Values.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray() });
    }

    public static void CancelRecording() => recording = null;

    #endregion
}

#region 构建期配置载荷

/// <summary>领域自己声明无参同步烘焙入口，构建器不维护业务系统清单。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class BuiltInContentBakeAttribute : Attribute { }

[MemoryPackable]
public sealed partial class BuiltInContentBinaryArchive
{
    public int Version;
    public BuiltInContentBinaryEntry[] Entries;
}

[MemoryPackable]
public sealed partial class BuiltInContentBinaryEntry
{
    public string Key;
    public int Version;
    public string Type;
    public byte[] Payload;
}

#endregion
