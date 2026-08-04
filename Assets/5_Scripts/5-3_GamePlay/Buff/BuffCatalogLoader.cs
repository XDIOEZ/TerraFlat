using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 从 StreamingAssets 的 Buff 清单加载多个业务分包，并统一注册到 GameRes。
/// </summary>
public static class BuffCatalogLoader
{
    public const string RelativeBuffRoot = "GameConfig/Buffs";
    public const string ManifestFileName = "buff-manifest.json";
    public const string RelativeManifestPath = RelativeBuffRoot + "/" + ManifestFileName;

    private static readonly JsonSerializerSettings StrictJsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error
    };

    public static string BuiltInBuffRoot =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeBuffRoot);

    public static string BuiltInManifestPath =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeManifestPath);

    /// <summary>同步入口供编辑器、测试和文件系统平台使用。</summary>
    public static int LoadBuiltIn(GameRes gameRes)
    {
        if (gameRes == null)
            throw new ArgumentNullException(nameof(gameRes));

        List<BuffDefinition> definitions = LoadBuiltInDefinitions();
        RegisterDefinitions(gameRes, definitions);
        Debug.Log($"[BuffCatalog] 已从 Buff 分包加载 {definitions.Count} 个 Buff：{BuiltInManifestPath}");
        return definitions.Count;
    }

    /// <summary>跨平台协程入口；Android/WebGL 使用 UnityWebRequest 读取包内文件。</summary>
    public static IEnumerator LoadBuiltInAsync(
        GameRes gameRes,
        Action<int> onCompleted,
        Action<Exception> onFailed)
    {
        if (gameRes == null)
        {
            onFailed?.Invoke(new ArgumentNullException(nameof(gameRes)));
            yield break;
        }

        List<BuffDefinition> definitions = null;
        Exception loadError = null;
        yield return LoadBuiltInDefinitionsAsync(
            result => definitions = result,
            exception => loadError = exception);

        if (loadError != null)
        {
            onFailed?.Invoke(loadError);
            yield break;
        }

        try
        {
            RegisterDefinitions(gameRes, definitions);
            Debug.Log($"[BuffCatalog] 已从 Buff 分包加载 {definitions.Count} 个 Buff：{BuiltInManifestPath}");
            onCompleted?.Invoke(definitions.Count);
        }
        catch (Exception exception)
        {
            onFailed?.Invoke(exception);
        }
    }

    public static List<BuffDefinition> LoadBuiltInDefinitions()
    {
        string manifestJson = StreamingAssetsTextLoader.ReadAllText(BuiltInManifestPath);
        BuffManifestDto manifest = DeserializeManifest(manifestJson);
        ValidateManifest(manifest);

        var definitions = new List<BuffDefinition>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BuffPackageDto package in manifest.Packages)
        {
            if (!package.Enabled)
                continue;

            string packagePath = ResolvePackagePath(package.Path);
            string packageJson = StreamingAssetsTextLoader.ReadAllText(packagePath);
            AppendPackage(package, packageJson, definitions, ids);
        }

        return definitions;
    }

    public static IEnumerator LoadBuiltInDefinitionsAsync(
        Action<List<BuffDefinition>> onCompleted,
        Action<Exception> onFailed)
    {
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

        BuffManifestDto manifest;
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

        var definitions = new List<BuffDefinition>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BuffPackageDto package in manifest.Packages)
        {
            if (!package.Enabled)
                continue;

            string packagePath;
            try
            {
                packagePath = ResolvePackagePath(package.Path);
            }
            catch (Exception exception)
            {
                onFailed?.Invoke(exception);
                yield break;
            }

            string packageJson = null;
            readError = null;
            yield return StreamingAssetsTextLoader.ReadAllTextAsync(
                packagePath,
                text => packageJson = text,
                exception => readError = exception);

            if (readError != null)
            {
                onFailed?.Invoke(new IOException(
                    $"Buff 分包 {package.Id} 读取失败：{packagePath}",
                    readError));
                yield break;
            }

            try
            {
                AppendPackage(package, packageJson, definitions, ids);
            }
            catch (Exception exception)
            {
                onFailed?.Invoke(exception);
                yield break;
            }
        }

        onCompleted?.Invoke(definitions);
    }

    public static BuffManifestDto DeserializeManifest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Buff 分包清单为空");

        return JsonConvert.DeserializeObject<BuffManifestDto>(json, StrictJsonSettings)
            ?? throw new InvalidDataException("Buff 分包清单无法反序列化");
    }

    public static void ValidateManifest(BuffManifestDto manifest)
    {
        if (manifest == null)
            throw new InvalidDataException("Buff 分包清单根对象为空");
        if (manifest.SchemaVersion != BuffDefinitionFactory.SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的 Buff 清单 schemaVersion：{manifest.SchemaVersion}");

        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BuffPackageDto package in manifest.Packages ?? new List<BuffPackageDto>())
        {
            if (package == null)
                throw new InvalidDataException("Buff 清单包含空分包定义");
            if (string.IsNullOrWhiteSpace(package.Id))
                throw new InvalidDataException("Buff 清单包含空分包 ID");
            if (string.IsNullOrWhiteSpace(package.Path))
                throw new InvalidDataException($"Buff 分包 {package.Id} 缺少 path");
            if (!packageIds.Add(package.Id.Trim()))
                throw new InvalidDataException($"Buff 清单包含重复分包 ID：{package.Id}");

            string normalizedPath = package.Path.Trim().Replace('\\', '/');
            if (!packagePaths.Add(normalizedPath))
                throw new InvalidDataException($"Buff 清单包含重复文件路径：{package.Path}");

            // Resolve during validation so traversal and absolute paths fail before any package is read.
            ResolvePackagePath(normalizedPath);
        }
    }

    public static string ResolvePackagePath(string relativePath)
        => StreamingAssetsTextLoader.CombinePath(BuiltInBuffRoot, relativePath);

    public static string Serialize(BuffCatalogDto catalog)
        => JsonConvert.SerializeObject(catalog, Formatting.Indented);

    public static string SerializeManifest(BuffManifestDto manifest)
        => JsonConvert.SerializeObject(manifest, Formatting.Indented);

    private static void AppendPackage(
        BuffPackageDto package,
        string json,
        List<BuffDefinition> definitions,
        HashSet<string> ids)
    {
        BuffCatalogDto catalog;
        try
        {
            catalog = BuffDefinitionFactory.Deserialize(json);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException($"Buff 分包 {package.Id} JSON 无效", exception);
        }

        List<BuffDefinition> packageDefinitions = BuffDefinitionFactory.BuildCatalog(catalog);
        foreach (BuffDefinition definition in packageDefinitions)
        {
            if (!ids.Add(definition.Id))
                throw new InvalidDataException($"跨 Buff 分包存在重复 ID：{definition.Id}");
            definitions.Add(definition);
        }
    }

    private static void RegisterDefinitions(GameRes gameRes, List<BuffDefinition> definitions)
    {
        if (definitions == null)
            throw new InvalidDataException("Buff 分包加载结果为空");

        foreach (BuffDefinition definition in definitions)
        {
            if (gameRes.GetBuffDefinition(definition.Id) != null)
                throw new InvalidDataException($"Buff ID 冲突：{definition.Id}");
        }

        foreach (BuffDefinition definition in definitions)
            gameRes.RegisterBuff(definition);
    }
}
