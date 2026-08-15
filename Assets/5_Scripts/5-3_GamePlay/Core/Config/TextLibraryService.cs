using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 已解析文字库的高频查询服务。
/// 配置加载完成后只保留规范化字符串数组和索引字典，随机查询为 O(1)，不在查询阶段反序列化或扫描文件。
/// </summary>
public sealed class TextLibraryService : ITextLibraryService
{
    private readonly Dictionary<string, string[]> libraries;
    private readonly Dictionary<string, TextLibraryGeneratorDefinition> generators;
    private readonly object randomLock = new object();
    private readonly Random random;
    private readonly IReadOnlyList<string> libraryIds;

    /// <summary>配置加载是否成功。</summary>
    public bool IsReady { get; }

    /// <summary>可用文字分类 ID 快照。</summary>
    public IReadOnlyList<string> LibraryIds => libraryIds;

    /// <summary>当前缓存的文字条目总数。</summary>
    public int EntryCount { get; }

    /// <summary>配置缺失或加载失败时使用的空服务。</summary>
    public static TextLibraryService Empty { get; } = CreateEmpty();

    internal TextLibraryService(
        Dictionary<string, string[]> libraries,
        Dictionary<string, TextLibraryGeneratorDefinition> generators,
        bool isReady,
        int? randomSeed = null)
    {
        this.libraries = libraries ??
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        this.generators = generators ??
            new Dictionary<string, TextLibraryGeneratorDefinition>(StringComparer.OrdinalIgnoreCase);
        libraryIds = new List<string>(this.libraries.Keys).AsReadOnly();
        IsReady = isReady;
        random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();

        int entryCount = 0;
        foreach (string[] values in this.libraries.Values)
            entryCount += values?.Length ?? 0;
        EntryCount = entryCount;
    }

    /// <summary>随机读取一条分类内容。</summary>
    public bool TryGetRandom(string libraryId, out string value)
    {
        value = null;
        if (!IsReady || string.IsNullOrWhiteSpace(libraryId) ||
            !libraries.TryGetValue(libraryId.Trim(), out string[] values) ||
            values == null || values.Length == 0)
        {
            return false;
        }

        int index;
        lock (randomLock)
            index = random.Next(values.Length);

        value = values[index];
        return !string.IsNullOrEmpty(value);
    }

    /// <summary>按照生成器部件定义组合随机文字。</summary>
    public bool TryGenerate(string generatorId, out string value)
    {
        value = null;
        if (!IsReady || string.IsNullOrWhiteSpace(generatorId) ||
            !generators.TryGetValue(generatorId.Trim(), out TextLibraryGeneratorDefinition generator) ||
            generator.Parts.Length == 0)
        {
            return false;
        }

        if (generator.Parts.Length == 1 && string.IsNullOrEmpty(generator.Separator))
            return TryGetRandom(generator.Parts[0], out value);

        StringBuilder builder = new StringBuilder();
        for (int index = 0; index < generator.Parts.Length; index++)
        {
            if (!TryGetRandom(generator.Parts[index], out string part))
                return false;

            if (index > 0)
                builder.Append(generator.Separator);
            builder.Append(part);
        }

        value = builder.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>生成失败时返回兜底值。</summary>
    public string GenerateOrDefault(string generatorId, string fallback)
    {
        return TryGenerate(generatorId, out string value) ? value : fallback;
    }

    internal static TextLibraryService Create(
        Dictionary<string, string[]> libraries,
        Dictionary<string, TextLibraryGeneratorDefinition> generators)
    {
        return new TextLibraryService(libraries, generators, true);
    }

    private static TextLibraryService CreateEmpty()
    {
        return new TextLibraryService(
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, TextLibraryGeneratorDefinition>(StringComparer.OrdinalIgnoreCase),
            false,
            0);
    }
}

/// <summary>运行时已规范化的生成器定义，避免每次生成都解析 JSON 字符串。</summary>
internal sealed class TextLibraryGeneratorDefinition
{
    public readonly string[] Parts;
    public readonly string Separator;

    public TextLibraryGeneratorDefinition(string[] parts, string separator)
    {
        Parts = parts ?? Array.Empty<string>();
        Separator = separator ?? string.Empty;
    }
}
