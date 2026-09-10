using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 从 StreamingAssets 的污染清单加载本体污染指标，并注册到 GameRes。
/// MOD 继续复用 ContaminationDefinitionFactory，不需要复制第二套 schema。
/// </summary>
public static class ContaminationCatalogLoader
{
    public const string RelativeRoot = "GameConfig/Contamination";
    public const string ManifestFileName = "contamination-manifest.json";
    public const string RelativeManifestPath = RelativeRoot + "/" + ManifestFileName;

    private static readonly JsonSerializerSettings StrictJsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error
    };

    public static string BuiltInRoot =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeRoot);

    public static string BuiltInManifestPath =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeManifestPath);

    /// <summary>跨平台加载本体污染目录。</summary>
    public static IEnumerator LoadBuiltInAsync(GameRes gameRes, Action<int> onCompleted, Action<Exception> onFailed)
    {
        if (gameRes == null)
        {
            onFailed?.Invoke(new ArgumentNullException(nameof(gameRes)));
            yield break;
        }

        string manifestJson = null;
        Exception readError = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(
            BuiltInManifestPath,
            text => manifestJson = text,
            exception => readError = exception);
        if (readError != null)
        {
            onFailed?.Invoke(readError);
            yield break;
        }

        ContaminationManifestDto manifest;
        try
        {
            manifest = DeserializeManifest(manifestJson);
            ValidateManifest(manifest);
        }
        catch (Exception exception)
        {
            onFailed?.Invoke(exception);
            yield break;
        }

        var definitions = new List<ContaminationDefinition>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ContaminationPackageDto package in manifest.Packages)
        {
            if (!package.Enabled)
                continue;

            string packagePath = ResolvePackagePath(package.Path);
            string packageJson = null;
            readError = null;
            yield return StreamingAssetsTextLoader.ReadAllTextAsync(
                packagePath,
                text => packageJson = text,
                exception => readError = exception);
            if (readError != null)
            {
                onFailed?.Invoke(new IOException($"污染分包 {package.Id} 读取失败：{packagePath}", readError));
                yield break;
            }

            try
            {
                ContaminationCatalogDto catalog = ContaminationDefinitionFactory.DeserializeCatalog(packageJson);
                foreach (ContaminationDefinition definition in ContaminationDefinitionFactory.BuildCatalog(catalog))
                {
                    if (!ids.Add(definition.Id))
                        throw new InvalidDataException($"跨污染分包存在重复 ID：{definition.Id}");
                    definitions.Add(definition);
                }
            }
            catch (Exception exception)
            {
                onFailed?.Invoke(new InvalidDataException($"污染分包 {package.Id} JSON 无效", exception));
                yield break;
            }
        }

        try
        {
            foreach (ContaminationDefinition definition in definitions)
                gameRes.RegisterContaminationDefinition(definition);
            Debug.Log($"[ContaminationCatalog] 已加载 {definitions.Count} 个本体污染指标：{BuiltInManifestPath}");
            onCompleted?.Invoke(definitions.Count);
        }
        catch (Exception exception)
        {
            onFailed?.Invoke(exception);
        }
    }

    /// <summary>严格解析污染清单。</summary>
    public static ContaminationManifestDto DeserializeManifest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("污染清单为空");

        return JsonConvert.DeserializeObject<ContaminationManifestDto>(json, StrictJsonSettings)
            ?? throw new InvalidDataException("污染清单无法反序列化");
    }

    /// <summary>校验分包 ID、路径以及 schema。</summary>
    public static void ValidateManifest(ContaminationManifestDto manifest)
    {
        if (manifest == null)
            throw new InvalidDataException("污染清单根对象为空");
        if (manifest.SchemaVersion != ContaminationDefinitionFactory.SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的污染清单 schemaVersion：{manifest.SchemaVersion}");

        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ContaminationPackageDto package in manifest.Packages ?? new List<ContaminationPackageDto>())
        {
            if (package == null || string.IsNullOrWhiteSpace(package.Id) || string.IsNullOrWhiteSpace(package.Path))
                throw new InvalidDataException("污染清单包含空分包定义、ID 或 path");
            if (!packageIds.Add(package.Id.Trim()))
                throw new InvalidDataException($"污染清单包含重复分包 ID：{package.Id}");

            string normalizedPath = package.Path.Trim().Replace('\\', '/');
            if (!packagePaths.Add(normalizedPath))
                throw new InvalidDataException($"污染清单包含重复文件路径：{package.Path}");
            ResolvePackagePath(normalizedPath);
        }
    }

    /// <summary>解析污染分包路径，并沿用 StreamingAssets 的路径越界保护。</summary>
    public static string ResolvePackagePath(string relativePath) =>
        StreamingAssetsTextLoader.CombinePath(BuiltInRoot, relativePath);
}
