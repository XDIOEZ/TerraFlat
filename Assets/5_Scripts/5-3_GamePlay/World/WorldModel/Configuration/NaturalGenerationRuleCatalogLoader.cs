using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

/// <summary>按清单读取自然物规则分包；加载完整且校验通过后才发布目录。</summary>
public static class NaturalGenerationRuleCatalogLoader
{
    #region 清单与路径

    public const int SupportedSchemaVersion = 1;
    public const string RelativeRoot = "GameConfig/WorldGeneration/NaturalItems";
    public const string ManifestFileName = "natural-item-manifest.json";
    private const int MaximumConfigCharacters = 256 * 1024;

    private static readonly JsonSerializerSettings StrictJsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        MissingMemberHandling = MissingMemberHandling.Error,
        DateParseHandling = DateParseHandling.None
    };

    private static string BuiltInRoot =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeRoot);

    private static string ManifestPath =>
        StreamingAssetsTextLoader.CombinePath(BuiltInRoot, ManifestFileName);

    /// <summary>检查清单、分包标识与路径；文件必须显式列入清单。</summary>
    private static NaturalGenerationManifest DeserializeManifest(string json)
    {
        NaturalGenerationManifest manifest = Deserialize<NaturalGenerationManifest>(json, ManifestFileName);
        if (manifest.SchemaVersion != SupportedSchemaVersion || manifest.Packages == null ||
            manifest.Packages.Count == 0)
            throw new InvalidDataException("自然物规则清单版本无效或 packages 为空。");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (NaturalGenerationPackageReference package in manifest.Packages)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.Id) ||
                string.IsNullOrWhiteSpace(package.Path))
                throw new InvalidDataException("自然物规则清单含空分包 ID 或路径。");
            StreamingAssetsTextLoader.CombinePath(BuiltInRoot, package.Path);
            if (!package.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                !ids.Add(package.Id) || !paths.Add(package.Path))
                throw new InvalidDataException($"自然物规则分包 ID/路径重复或路径不是 JSON：{package.Id} / {package.Path}");
        }
        return manifest;
    }

    /// <summary>严格解析 JSON；未知或缺失字段会指出具体文件。</summary>
    private static T Deserialize<T>(string json, string source)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumConfigCharacters)
            throw new InvalidDataException($"自然物规则 JSON 为空或超过大小限制：{source}");
        try
        {
            return JsonConvert.DeserializeObject<T>(json, StrictJsonSettings)
                ?? throw new InvalidDataException($"自然物规则 JSON 根对象为空：{source}");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"自然物规则 JSON 无效：{source}；{exception.Message}", exception);
        }
    }

    #endregion

    #region 文件加载

    /// <summary>编辑器预览的同步读取入口；游戏资源加载使用异步入口。</summary>
    public static NaturalGenerationRuleCatalog LoadBuiltIn()
    {
        NaturalGenerationManifest manifest = DeserializeManifest(
            StreamingAssetsTextLoader.ReadAllText(ManifestPath));
        var catalog = new NaturalGenerationRuleCatalog();
        foreach (NaturalGenerationPackageReference package in manifest.Packages)
        {
            if (!package.Enabled) continue;
            string json = StreamingAssetsTextLoader.ReadAllText(
                StreamingAssetsTextLoader.CombinePath(BuiltInRoot, package.Path));
            catalog.Register(Deserialize<NaturalGenerationRulePackage>(json, package.Path), package.Path);
        }
        EnsureNonEmpty(catalog);
        return catalog;
    }

    /// <summary>兼容 APK 内 StreamingAssets；任何分包出错都阻止资源目录发布。</summary>
    public static IEnumerator LoadBuiltInAsync(
        Action<NaturalGenerationRuleCatalog> onCompleted,
        Action<Exception> onFailed)
    {
        string json = null;
        Exception readError = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(
            ManifestPath, value => json = value, error => readError = error);
        if (readError != null) { onFailed?.Invoke(readError); yield break; }

        NaturalGenerationManifest manifest;
        try { manifest = DeserializeManifest(json); }
        catch (Exception error) { onFailed?.Invoke(error); yield break; }

        var catalog = new NaturalGenerationRuleCatalog();
        foreach (NaturalGenerationPackageReference package in manifest.Packages)
        {
            if (!package.Enabled) continue;
            json = null;
            readError = null;
            yield return StreamingAssetsTextLoader.ReadAllTextAsync(
                StreamingAssetsTextLoader.CombinePath(BuiltInRoot, package.Path),
                value => json = value, error => readError = error);
            if (readError != null) { onFailed?.Invoke(readError); yield break; }
            try
            {
                catalog.Register(Deserialize<NaturalGenerationRulePackage>(json, package.Path), package.Path);
            }
            catch (Exception error) { onFailed?.Invoke(error); yield break; }
        }

        try { EnsureNonEmpty(catalog); onCompleted?.Invoke(catalog); }
        catch (Exception error) { onFailed?.Invoke(error); }
    }

    private static void EnsureNonEmpty(NaturalGenerationRuleCatalog catalog)
    {
        if (catalog.EcologyRules.Count == 0 && catalog.CaveResourceRules.Count == 0)
            throw new InvalidDataException("自然物规则清单没有启用任何规则。");
    }

    #endregion
}

/// <summary>显式选择可读取的自然物 JSON 分包。</summary>
#pragma warning disable CS0649 // 以下字段由 JSON 反序列化赋值。
[JsonObject(ItemRequired = Required.Always)]
internal sealed class NaturalGenerationManifest
{
    public int SchemaVersion;
    public List<NaturalGenerationPackageReference> Packages;
}

/// <summary>单个自然物分包的标识、相对路径和开关。</summary>
[JsonObject(ItemRequired = Required.Always)]
internal sealed class NaturalGenerationPackageReference
{
    public string Id;
    public string Path;
    public bool Enabled;
}
#pragma warning restore CS0649
