using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 从 StreamingAssets 加载内建文字库；同步入口用于编辑器/测试，协程入口兼容 Android 和 WebGL。
/// </summary>
public static class TextLibraryCatalogLoader
{
    public const int SupportedSchemaVersion = 1;
    public const string RelativeTextLibraryRoot = "GameConfig/TextLibraries";
    public const string ConfigFileName = "text-library.json";
    public const string RelativeConfigPath = RelativeTextLibraryRoot + "/" + ConfigFileName;

    private static readonly JsonSerializerSettings StrictJsonSettings = new JsonSerializerSettings
    {
        MissingMemberHandling = MissingMemberHandling.Error
    };

    public static string BuiltInConfigPath =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeConfigPath);

    /// <summary>同步加载内建文字库。</summary>
    public static TextLibraryService LoadBuiltIn()
    {
        return Deserialize(StreamingAssetsTextLoader.ReadAllText(BuiltInConfigPath));
    }

    /// <summary>跨平台协程加载内建文字库。</summary>
    public static IEnumerator LoadBuiltInAsync(
        Action<TextLibraryService> onCompleted,
        Action<Exception> onFailed)
    {
        string json = null;
        Exception readError = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(
            BuiltInConfigPath,
            text => json = text,
            exception => readError = exception);

        if (readError != null)
        {
            onFailed?.Invoke(readError);
            yield break;
        }

        try
        {
            onCompleted?.Invoke(Deserialize(json));
        }
        catch (Exception exception)
        {
            onFailed?.Invoke(exception);
        }
    }

    /// <summary>反序列化并严格校验文字库配置。</summary>
    public static TextLibraryService Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("文字库 JSON 为空");

        TextLibraryCatalogDto catalog = JsonConvert.DeserializeObject<TextLibraryCatalogDto>(
            json,
            StrictJsonSettings);
        Validate(catalog);
        return BuildService(catalog);
    }

    /// <summary>校验 schema、分类、生成器引用和空内容。</summary>
    public static void Validate(TextLibraryCatalogDto catalog)
    {
        if (catalog == null)
            throw new InvalidDataException("文字库 JSON 根对象为空");
        if (catalog.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException(
                $"不支持的文字库 schemaVersion：{catalog.SchemaVersion}");
        if (catalog.Libraries == null || catalog.Libraries.Count == 0)
            throw new InvalidDataException("文字库至少需要一个 libraries 分类");

        HashSet<string> libraryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, List<string>> pair in catalog.Libraries)
        {
            string libraryId = NormalizeId(pair.Key, "文字分类");
            if (!libraryIds.Add(libraryId))
                throw new InvalidDataException($"文字库包含重复分类 ID：{pair.Key}");

            if (pair.Value == null || CountValidEntries(pair.Value) == 0)
                throw new InvalidDataException($"文字分类 {libraryId} 没有有效条目");
        }

        HashSet<string> generatorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, TextLibraryGeneratorDto> pair in
                 catalog.Generators ?? new Dictionary<string, TextLibraryGeneratorDto>())
        {
            string generatorId = NormalizeId(pair.Key, "文字生成器");
            if (!generatorIds.Add(generatorId))
                throw new InvalidDataException($"文字库包含重复生成器 ID：{generatorId}");

            TextLibraryGeneratorDto generator = pair.Value;
            if (generator == null || generator.Parts == null || generator.Parts.Count == 0)
                throw new InvalidDataException($"文字生成器 {generatorId} 缺少 parts");

            for (int index = 0; index < generator.Parts.Count; index++)
            {
                string partId = NormalizeId(generator.Parts[index],
                    $"文字生成器 {generatorId} 的 parts");
                if (!libraryIds.Contains(partId))
                {
                    throw new InvalidDataException(
                        $"文字生成器 {generatorId} 引用了不存在的分类：{partId}");
                }
            }
        }
    }

    private static TextLibraryService BuildService(TextLibraryCatalogDto catalog)
    {
        Dictionary<string, string[]> libraries =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, List<string>> pair in catalog.Libraries)
        {
            string libraryId = pair.Key.Trim();
            List<string> entries = new List<string>(pair.Value.Count);
            HashSet<string> deduplicated = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rawValue in pair.Value)
            {
                string value = rawValue?.Trim();
                if (!string.IsNullOrEmpty(value) && deduplicated.Add(value))
                    entries.Add(value);
            }

            libraries.Add(libraryId, entries.ToArray());
        }

        Dictionary<string, TextLibraryGeneratorDefinition> generators =
            new Dictionary<string, TextLibraryGeneratorDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, TextLibraryGeneratorDto> pair in
                 catalog.Generators ?? new Dictionary<string, TextLibraryGeneratorDto>())
        {
            List<string> parts = new List<string>(pair.Value.Parts.Count);
            for (int index = 0; index < pair.Value.Parts.Count; index++)
                parts.Add(pair.Value.Parts[index].Trim());

            generators.Add(
                pair.Key.Trim(),
                new TextLibraryGeneratorDefinition(parts.ToArray(), pair.Value.Separator));
        }

        return TextLibraryService.Create(libraries, generators);
    }

    private static int CountValidEntries(List<string> entries)
    {
        int count = 0;
        HashSet<string> deduplicated = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in entries)
        {
            string normalized = value?.Trim();
            if (!string.IsNullOrEmpty(normalized) && deduplicated.Add(normalized))
                count++;
        }

        return count;
    }

    private static string NormalizeId(string value, string fieldName)
    {
        string normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            throw new InvalidDataException($"{fieldName} ID 不能为空");
        return normalized;
    }
}
