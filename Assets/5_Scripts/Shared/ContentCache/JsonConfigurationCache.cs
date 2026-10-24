using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MemoryPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>各配置保留自己的解析规则，只把解析后的纯数据对象交给统一二进制缓存。</summary>
public static class JsonConfigurationCache
{
    #region 环境与诊断

    private static string directory;
    private static string environment;
    private static int hits, builds, writeFailures;
    public static int Hits => Volatile.Read(ref hits);
    public static int Builds => Volatile.Read(ref builds);
    public static int WriteFailures => Volatile.Read(ref writeFailures);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    public static void Initialize()
    {
        // Unity 路径只在主线程捕获，后台解析和缓存读写只使用普通字符串。
        directory = Path.Combine(Application.persistentDataPath, "ContentCache", "JsonConfigurations");
        environment = $"{Application.platform}|{Application.buildGUID}|{IntPtr.Size}|{BitConverter.IsLittleEndian}";
        Interlocked.Exchange(ref hits, 0);
        Interlocked.Exchange(ref builds, 0);
        Interlocked.Exchange(ref writeFailures, 0);
        MemoryPackFormatterProvider.Register(new BinaryJsonObjectFormatter());
        MemoryPackFormatterProvider.Register(new BinaryJsonTokenFormatter());
    }

    #endregion

    #region 解析边界

    public static T Deserialize<T>(string json, JsonSerializerSettings settings = null,
        [CallerFilePath] string parser = "", [CallerLineNumber] int line = 0) where T : class
        => Parse(json, () => JsonConvert.DeserializeObject<T>(json, settings), parser, line);

    public static T FromUnityJson<T>(string json,
        [CallerFilePath] string parser = "", [CallerLineNumber] int line = 0) where T : class
        => Parse(json, () => JsonUtility.FromJson<T>(json), parser, line);

    public static T Parse<T>(string json, Func<T> parse,
        [CallerFilePath] string parser = "", [CallerLineNumber] int line = 0) where T : class
    {
        if (parse == null) throw new ArgumentNullException(nameof(parse));
        if (string.IsNullOrEmpty(directory) || json == null) return parse();
        return ParseBytes(Encoding.UTF8.GetBytes(json), parse, parser, line);
    }

    public static JObject ParseObject(string json,
        [CallerFilePath] string parser = "", [CallerLineNumber] int line = 0)
        => Parse(json, () => JObject.Parse(json), parser, line);

    public static T FromToken<T>(JToken token, JsonSerializer serializer = null,
        [CallerFilePath] string parser = "", [CallerLineNumber] int line = 0) where T : class
    {
        if (token == null) return null;
        T Read() => serializer == null ? token.ToObject<T>() : token.ToObject<T>(serializer);
        if (string.IsNullOrEmpty(directory)) return Read();
        byte[] bytes;
        try { bytes = MemoryPackSerializer.Serialize(token); }
        catch (Exception) { return Read(); }
        return ParseBytes(bytes, Read, parser, line);
    }

    private static T ParseBytes<T>(byte[] bytes, Func<T> parse, string parser, int line) where T : class
    {
        BinaryContentCache cache = null;
        try
        {
            byte[] hash = Digest(bytes);
            // 同一解析器可处理多个分包，以内容寻址避免分包互相覆盖或复用错误的解析设置。
            string key = typeof(T).AssemblyQualifiedName + "|" + parser + "|" + line;
            string path = Path.Combine(directory, Hex(Digest(Encoding.UTF8.GetBytes(key))) + "_" + Hex(hash) + ".fwbin");
            string revision = environment;
#if UNITY_EDITOR
            revision += $"|{typeof(T).Module.ModuleVersionId}|{typeof(JsonConfigurationCache).Module.ModuleVersionId}";
#endif
            string contract = $"json-config-v1|{key}|{revision}|{typeof(MemoryPackSerializer).Assembly.FullName}";
            cache = BinaryContentCache.ForDocument(path, contract, hash);
        }
        catch (Exception) { /* 缓存自身失败不改变原解析器的异常与回退语义。 */ }

        return ReadOrBuild(cache, parse);
    }

    public static T ReadOrBuild<T>(BinaryContentCache cache, Func<T> parse) where T : class
    {
        if (cache != null && cache.TryRead(out T cached))
        {
            Interlocked.Increment(ref hits);
            return cached;
        }
        cache?.BeginBuild();
        T value = parse();
        if (value != null && cache != null)
        {
            Interlocked.Increment(ref builds);
            if (!cache.Store(value)) Interlocked.Increment(ref writeFailures);
        }
        return value;
    }

    private static byte[] Digest(byte[] bytes) { using var hash = SHA256.Create(); return hash.ComputeHash(bytes); }
    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty);

    #endregion
}
