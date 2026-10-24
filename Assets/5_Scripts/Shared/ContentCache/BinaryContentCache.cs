using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MemoryPack;
using UnityEngine;

/// <summary>缓存解析后的纯配置对象，JSON 内容或当前代码变化时自动重建。</summary>
public sealed class BinaryContentCache
{
    #region 缓存会话与来源

    private const int FileMagic = 0x31435746; // FWC1，只保存本机生成的可丢弃缓存。
    private const int HeaderLength = 40;
    private const int MaxFileLength = 128 * 1024 * 1024;
    private readonly string contract;
    private readonly string sourceRoot;
    private readonly string entryPath;
    private readonly bool remote;
    private readonly byte[] documentHash;
    private static int buildRevision;
    private bool buildStarted;
    private readonly Dictionary<string, BinaryContentSource> sources = new(StringComparer.Ordinal);
    private readonly Stopwatch timer = Stopwatch.StartNew();

    public string CachePath { get; }
    public bool IsHit { get; private set; }
    public string Reason { get; private set; } = "首次加载";
    public static int BuildRevision => Volatile.Read(ref buildRevision);

    /// <summary>只记录真实的缓存重建，加载画面据此提示首次加载较慢。</summary>
    public void BeginBuild()
    {
        if (buildStarted) return;
        buildStarted = true;
        Interlocked.Increment(ref buildRevision);
    }

    public BinaryContentCache(string cachePath, string contract, string sourceRoot, string entryPath)
    {
        CachePath = cachePath ?? throw new ArgumentNullException(nameof(cachePath));
        this.contract = contract ?? throw new ArgumentNullException(nameof(contract));
        remote = StreamingAssetsTextLoader.RequiresWebRequest(sourceRoot);
        this.sourceRoot = NormalizeRoot(sourceRoot, remote);
        this.entryPath = RelativePath(entryPath);
    }

    private BinaryContentCache(string cachePath, string contract, byte[] documentHash)
    {
        CachePath = cachePath;
        this.contract = contract;
        this.documentHash = documentHash;
        sourceRoot = string.Empty;
        entryPath = "@document";
        sources.Add(entryPath, new BinaryContentSource { Path = entryPath, Hash = documentHash });
    }

    public static BinaryContentCache ForDocument(string path, string contract, byte[] hash)
        => new(path, contract, hash);

    public static BinaryContentCache ForBuiltIn<T>(string id, string root, string manifest, int version)
    {
        // 代码重编译、平台或安装来源不同均重新解析，不能复用旧的字段布局与解析规则。
        string codeRevision = Application.buildGUID;
#if UNITY_EDITOR
        codeRevision = $"{typeof(T).Module.ModuleVersionId}|{typeof(BinaryContentCache).Module.ModuleVersionId}|" +
                       string.Join("|", typeof(T).GetGenericArguments().Select(type => type.Module.ModuleVersionId.ToString()));
#endif
        string signature = $"{version}|{typeof(T).AssemblyQualifiedName}|{codeRevision}|" +
                           $"{typeof(MemoryPackSerializer).Assembly.FullName}|" +
                           $"{Application.platform}|{IntPtr.Size}|{BitConverter.IsLittleEndian}";
        string scope = Hex(Hash(Encoding.UTF8.GetBytes(NormalizeRoot(root,
            StreamingAssetsTextLoader.RequiresWebRequest(root)) + "|" + Application.platform)));
        string path = Path.Combine(Application.persistentDataPath, "ContentCache", scope, id + ".fwbin");
        return new BinaryContentCache(path, signature, root, manifest);
    }

    public string ReadText(string path)
    {
        BeginBuild();
        byte[] bytes = File.ReadAllBytes(path);
        return RecordText(path, bytes);
    }

    public IEnumerator ReadTextAsync(string path, Action<string> completed, Action<Exception> failed)
    {
        BeginBuild();
        if (!remote)
        {
            yield return StreamingAssetsTextLoader.RunPureDataAsync(() => ReadText(path), completed, failed);
            yield break;
        }
        byte[] bytes = null;
        Exception error = null;
        yield return StreamingAssetsTextLoader.ReadAllBytesAsync(path, value => bytes = value, value => error = value);
        if (error != null) { failed?.Invoke(error); yield break; }
        yield return StreamingAssetsTextLoader.RunPureDataAsync(() => RecordText(path, bytes), completed, failed);
    }

    private string RecordText(string path, byte[] bytes)
    {
        string relative = RelativePath(path);
        using var reader = new StreamReader(new MemoryStream(bytes, false), Encoding.UTF8, true);
        string text = reader.ReadToEnd();
        sources[relative] = new BinaryContentSource { Path = relative, Hash = Hash(bytes) };
        return text;
    }

    public void Report(string label, int count)
    {
        UnityEngine.Debug.Log($"[启动二进制缓存] {label}：{(IsHit ? "命中" : Reason)}，" +
                              $"{count} 个定义，读取/解析 {timer.Elapsed.TotalMilliseconds:F1} ms");
    }

    #endregion

    #region 读取与内容校验

    public bool TryRead<T>(out T value) where T : class
    {
        value = null;
        IsHit = false;
        try
        {
            BinaryContentEnvelope envelope = ReadEnvelope();
            if (envelope == null || !ValidateLocalSources(envelope.Sources)) return false;
            value = Restore<T>(envelope);
            return value != null;
        }
        catch (Exception error) { Reason = "缓存不可用，重读 JSON：" + error.Message; return false; }
    }

    public IEnumerator TryReadAsync<T>(Action<T> completed) where T : class
    {
        if (!remote)
        {
            yield return StreamingAssetsTextLoader.RunPureDataAsync(
                () => TryRead<T>(out T value) ? value : null, completed,
                error => { Reason = "缓存不可用：" + error.Message; completed(null); });
            yield break;
        }

        BinaryContentEnvelope envelope = null;
        yield return StreamingAssetsTextLoader.RunPureDataAsync(() => ReadEnvelope(), value => envelope = value,
            error => Reason = "缓存不可用：" + error.Message);
        if (envelope == null) { completed(null); yield break; }
        bool valid = false;
        yield return ValidateSourcesAsync(envelope.Sources, value => valid = value);
        if (!valid) { completed(null); yield break; }
        yield return StreamingAssetsTextLoader.RunPureDataAsync(() => Restore<T>(envelope), completed,
            error => { Reason = "缓存不可用：" + error.Message; completed(null); });
    }

    private BinaryContentEnvelope ReadEnvelope()
    {
        if (!File.Exists(CachePath)) { Reason = "首次加载"; return null; }
        using var stream = File.OpenRead(CachePath);
        if (stream.Length < HeaderLength || stream.Length > MaxFileLength)
            throw new InvalidDataException("缓存长度无效");
        using var reader = new BinaryReader(stream);
        if (reader.ReadInt32() != FileMagic || reader.ReadInt32() != stream.Length - HeaderLength)
            throw new InvalidDataException("缓存文件头无效");
        byte[] expected = reader.ReadBytes(32);
        byte[] body = reader.ReadBytes((int)stream.Length - HeaderLength);
        if (!SameHash(expected, Hash(body))) throw new InvalidDataException("缓存校验失败");
        BinaryContentEnvelope envelope = MemoryPackSerializer.Deserialize<BinaryContentEnvelope>(body);
        if (envelope?.Contract != contract) { Reason = "代码或缓存版本更新，重读 JSON"; return null; }
        if (envelope.Payload == null || envelope.Sources == null || envelope.Sources.Length == 0 ||
            !envelope.Sources.Any(source => source?.Path == entryPath))
            throw new InvalidDataException("缓存缺少来源或载荷");
        foreach (BinaryContentSource source in envelope.Sources)
        {
            if (documentHash != null)
            {
                if (source?.Path != entryPath || source.Hash?.Length != 32 || envelope.Sources.Length != 1)
                    throw new InvalidDataException("缓存文档来源无效");
                continue;
            }
            if (source?.Hash?.Length != 32 || RelativePath(SourcePath(source.Path)) != source.Path)
                throw new InvalidDataException("缓存来源无效");
        }
        return envelope;
    }

    private T Restore<T>(BinaryContentEnvelope envelope) where T : class
    {
        T value = MemoryPackSerializer.Deserialize<T>(envelope.Payload);
        if (value == null) throw new InvalidDataException("缓存没有配置对象");
        IsHit = true;
        return value;
    }

    private bool ValidateLocalSources(IEnumerable<BinaryContentSource> inputs)
    {
        if (documentHash != null)
            return inputs.All(source => source.Path == entryPath && SameHash(source.Hash, documentHash));
        foreach (BinaryContentSource source in inputs)
        {
            using var stream = File.OpenRead(SourcePath(source.Path));
            using var hash = SHA256.Create();
            if (!SameHash(source.Hash, hash.ComputeHash(stream)))
            { Reason = "JSON 更新，重建缓存：" + source.Path; return false; }
        }
        return true;
    }

    private IEnumerator ValidateSourcesAsync(BinaryContentSource[] inputs, Action<bool> completed)
    {
        if (!remote)
        {
            yield return StreamingAssetsTextLoader.RunPureDataAsync(() => ValidateLocalSources(inputs), completed,
                error => { Reason = "来源不可用：" + error.Message; completed(false); });
            yield break;
        }
        foreach (BinaryContentSource source in inputs)
        {
            byte[] bytes = null;
            Exception error = null;
            yield return StreamingAssetsTextLoader.ReadAllBytesAsync(SourcePath(source.Path),
                value => bytes = value, value => error = value);
            if (error != null) { Reason = "来源不可用：" + error.Message; completed(false); yield break; }
            bool same = false;
            yield return StreamingAssetsTextLoader.RunPureDataAsync(() => SameHash(source.Hash, Hash(bytes)),
                value => same = value, value => error = value);
            if (error != null || !same)
            { Reason = "JSON 更新，重建缓存：" + source.Path; completed(false); yield break; }
        }
        completed(true);
    }

    #endregion

    #region 原子保存与失败回退

    public bool Store<T>(T value) where T : class
    {
        try
        {
            BinaryContentSource[] inputs = SnapshotSources();
            if (!ValidateLocalSources(inputs)) return false;
            Write(value, inputs);
            return true;
        }
        catch (Exception error) { Reason = "缓存写入失败，使用 JSON 结果：" + error.Message; return false; }
    }

    public IEnumerator StoreAsync<T>(T value) where T : class
    {
        BinaryContentSource[] inputs = null;
        try { inputs = SnapshotSources(); }
        catch (Exception error) { Reason = "缓存未写入：" + error.Message; }
        if (inputs == null) yield break;
        bool valid = false;
        yield return ValidateSourcesAsync(inputs, result => valid = result);
        if (!valid) yield break;
        yield return StreamingAssetsTextLoader.RunPureDataAsync(() => { Write(value, inputs); return true; }, _ => { },
            error => Reason = "缓存写入失败，使用 JSON 结果：" + error.Message);
    }

    private BinaryContentSource[] SnapshotSources()
    {
        if (!sources.ContainsKey(entryPath)) throw new InvalidDataException("未读取入口清单，不生成缓存");
        return sources.Values.OrderBy(source => source.Path, StringComparer.Ordinal).ToArray();
    }

    private void Write<T>(T value, BinaryContentSource[] inputs) where T : class
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        byte[] body = MemoryPackSerializer.Serialize(new BinaryContentEnvelope
        { Contract = contract, Sources = inputs, Payload = MemoryPackSerializer.Serialize(value) });
        if (body.Length > MaxFileLength - HeaderLength) throw new InvalidDataException("缓存超过大小限制");
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
        string temporary = CachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(FileMagic);
                writer.Write(body.Length);
                writer.Write(Hash(body));
                writer.Write(body);
                writer.Flush();
                stream.Flush(true);
            }
            if (File.Exists(CachePath)) File.Replace(temporary, CachePath, null);
            else File.Move(temporary, CachePath);
            Reason = "已从 JSON 构建二进制缓存";
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    #endregion

    #region 路径与摘要

    private static string NormalizeRoot(string root, bool remote)
        => (remote ? root : Path.GetFullPath(root)).Replace('\\', '/').TrimEnd('/') + "/";

    private string RelativePath(string path)
    {
        string normalized = remote ? path.Replace('\\', '/') : Path.GetFullPath(path).Replace('\\', '/');
        StringComparison comparison = !remote && Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!normalized.StartsWith(sourceRoot, comparison)) throw new InvalidDataException("缓存来源超出配置目录");
        string relative = normalized.Substring(sourceRoot.Length);
        StreamingAssetsTextLoader.CombinePath(sourceRoot, relative);
        return relative;
    }

    private string SourcePath(string relative) => StreamingAssetsTextLoader.CombinePath(sourceRoot, relative);
    private static byte[] Hash(byte[] bytes) { using var hash = SHA256.Create(); return hash.ComputeHash(bytes); }
    private static bool SameHash(byte[] left, byte[] right)
        => left != null && right != null && left.AsSpan().SequenceEqual(right);
    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty);

    #endregion
}

#region 二进制缓存封装

[MemoryPackable]
public sealed partial class BinaryContentEnvelope
{
    public string Contract;
    public BinaryContentSource[] Sources;
    public byte[] Payload;
}

[MemoryPackable]
public sealed partial class BinaryContentSource
{
    public string Path;
    public byte[] Hash;
}

#endregion
