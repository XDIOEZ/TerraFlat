using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

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

    #region JSON 来源与二进制缓存

    private static BinaryContentCache CreateBinaryCache()
        => BinaryContentCache.ForBuiltIn<BuffBinaryCatalog>("buffs", BuiltInBuffRoot, BuiltInManifestPath, 1);

    private static BuffBinaryCatalog ReadLocalCatalog(BinaryContentCache cache)
    {
        BuffManifestDto manifest = DeserializeManifest(cache.ReadText(BuiltInManifestPath));
        ValidateManifest(manifest);
        var result = new BuffBinaryCatalog { Manifest = manifest };
        foreach (BuffPackageDto package in manifest.Packages)
            if (package.Enabled) result.Catalogs.Add(BuffDefinitionFactory.Deserialize(
                cache.ReadText(ResolvePackagePath(package.Path))));
        return result;
    }

    private static List<BuffDefinition> BuildDefinitions(BuffBinaryCatalog binary)
    {
        ValidateManifest(binary?.Manifest);
        int enabledCount = 0;
        foreach (BuffPackageDto package in binary.Manifest.Packages)
            if (package.Enabled) enabledCount++;
        if (binary.Catalogs == null || binary.Catalogs.Count != enabledCount)
            throw new InvalidDataException("Buff 二进制包与 JSON 清单的启用分包不一致");

        var definitions = new List<BuffDefinition>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (BuffPackageDto package in binary.Manifest.Packages)
        {
            if (!package.Enabled) continue;
            foreach (BuffDefinition definition in BuffDefinitionFactory.BuildCatalog(binary.Catalogs[index++]))
            {
                if (!ids.Add(definition.Id))
                    throw new InvalidDataException($"跨 Buff 分包存在重复 ID：{definition.Id}");
                definitions.Add(definition);
            }
        }
        return definitions;
    }

    #endregion

    /// <summary>同步入口供编辑器、测试和文件系统平台使用。</summary>
    public static int LoadBuiltIn(GameRes gameRes)
    {
        if (gameRes == null)
            throw new ArgumentNullException(nameof(gameRes));

        List<BuffDefinition> definitions = LoadBuiltInDefinitions();
        LoadIcons(gameRes, definitions);
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

        yield return LoadIconsAsync(gameRes, definitions);

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
        BinaryContentCache cache = CreateBinaryCache();
        bool hit = cache.TryRead(out BuffBinaryCatalog binary);
        if (!hit) binary = ReadLocalCatalog(cache);
        List<BuffDefinition> definitions = BuildDefinitions(binary);
        if (!hit) cache.Store(binary);
        cache.Report("Buff", definitions.Count);
        return definitions;
    }

    public static IEnumerator LoadBuiltInDefinitionsAsync(
        Action<List<BuffDefinition>> onCompleted,
        Action<Exception> onFailed)
    {
        BinaryContentCache cache = CreateBinaryCache();
        BuffBinaryCatalog binary = null;
        yield return cache.TryReadAsync<BuffBinaryCatalog>(value => binary = value);
        bool hit = binary != null;
        Exception readError = null;
        if (!hit)
        {
            string manifestJson = null;
            yield return cache.ReadTextAsync(BuiltInManifestPath,
                text => manifestJson = text, exception => readError = exception);
            if (readError != null) { onFailed?.Invoke(readError); yield break; }

            BuffManifestDto manifest;
            try
            {
                manifest = DeserializeManifest(manifestJson);
                ValidateManifest(manifest);
            }
            catch (Exception exception) { onFailed?.Invoke(exception); yield break; }
            binary = new BuffBinaryCatalog { Manifest = manifest };
            var enabled = new List<BuffPackageDto>();
            var paths = new List<string>();
            try
            {
                foreach (BuffPackageDto package in manifest.Packages)
                {
                    if (!package.Enabled) continue;
                    enabled.Add(package);
                    paths.Add(ResolvePackagePath(package.Path));
                }
            }
            catch (Exception exception) { onFailed?.Invoke(exception); yield break; }

            if (!StreamingAssetsTextLoader.RequiresWebRequest(BuiltInBuffRoot))
            {
                // 本地分包一次后台读盘并解析，游戏下次启动只恢复二进制 DTO。
                List<BuffCatalogDto> catalogs = null;
                yield return StreamingAssetsTextLoader.RunPureDataAsync(() =>
                {
                    var result = new List<BuffCatalogDto>(paths.Count);
                    for (int i = 0; i < paths.Count; i++)
                    {
                        try { result.Add(BuffDefinitionFactory.Deserialize(cache.ReadText(paths[i]))); }
                        catch (Exception error) { throw new InvalidDataException($"Buff 分包 {enabled[i].Id} JSON 无效", error); }
                    }
                    return result;
                }, value => catalogs = value, exception => readError = exception);
                if (readError != null) { onFailed?.Invoke(readError); yield break; }
                binary.Catalogs = catalogs;
            }
            else
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    string json = null;
                    yield return cache.ReadTextAsync(paths[i],
                        value => json = value, exception => readError = exception);
                    if (readError != null)
                    {
                        onFailed?.Invoke(new IOException($"Buff 分包 {enabled[i].Id} 读取失败：{paths[i]}", readError));
                        yield break;
                    }
                    BuffCatalogDto catalog = null;
                    yield return StreamingAssetsTextLoader.RunPureDataAsync(
                        () => BuffDefinitionFactory.Deserialize(json),
                        value => catalog = value, exception => readError = exception);
                    if (readError != null)
                    {
                        onFailed?.Invoke(new InvalidDataException($"Buff 分包 {enabled[i].Id} JSON 无效", readError));
                        yield break;
                    }
                    binary.Catalogs.Add(catalog);
                }
            }
        }

        List<BuffDefinition> definitions;
        try { definitions = BuildDefinitions(binary); }
        catch (Exception exception) { onFailed?.Invoke(exception); yield break; }
        if (!hit) yield return cache.StoreAsync(binary);
        cache.Report("Buff", definitions.Count);
        onCompleted?.Invoke(definitions);
    }

    public static BuffManifestDto DeserializeManifest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Buff 分包清单为空");

        return JsonConfigurationCache.Deserialize<BuffManifestDto>(json, StrictJsonSettings)
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

    #region 图标资源

    private static void LoadIcons(GameRes gameRes, IReadOnlyList<BuffDefinition> definitions)
    {
        var icons = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        foreach (BuffDefinition definition in definitions)
        {
            string address = definition.IconAddress;
            if (string.IsNullOrWhiteSpace(address))
                continue;
            if (!icons.TryGetValue(address, out Sprite icon))
            {
                if (TryBeginIconLoad(gameRes, address, out AsyncOperationHandle<Sprite> request))
                {
                    try
                    {
                        request.WaitForCompletion();
                        icon = ReadLoadedIcon(request, address);
                    }
                    catch (Exception exception)
                    {
                        WarnMissingIcon(address, exception.Message);
                    }
                }
                icons.Add(address, icon);
            }
            definition.Icon = icon;
        }
    }

    private static IEnumerator LoadIconsAsync(GameRes gameRes, IReadOnlyList<BuffDefinition> definitions)
    {
        var icons = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        var addresses = new List<string>();
        foreach (BuffDefinition definition in definitions)
        {
            string address = definition.IconAddress;
            if (!string.IsNullOrWhiteSpace(address) && icons.TryAdd(address, null)) addresses.Add(address);
        }

        // 可选图标共用有界队列，单张缺失仍保留名称首字回退。
        yield return ResourceLoadQueue.Run(addresses.Count,
            index => TryBeginIconLoad(gameRes, addresses[index], out AsyncOperationHandle<Sprite> request) ? request : default,
            request => !request.IsValid() || request.IsDone,
            (index, request) =>
            {
                if (request.IsValid()) icons[addresses[index]] = ReadLoadedIcon(request, addresses[index]);
            }, gameRes.ShouldYieldResourceWork);

        foreach (BuffDefinition definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.IconAddress)) continue;
            definition.Icon = icons[definition.IconAddress];
            if (gameRes.ShouldYieldResourceWork())
                yield return null;
        }
    }

    private static bool TryBeginIconLoad(GameRes gameRes, string address, out AsyncOperationHandle<Sprite> request)
    {
        request = default;
        try
        {
#if UNITY_EDITOR
            // 先沿用现有编辑器解析，避免错误子资源名称触发 Fast Mode 内部空引用。
            if (address.StartsWith("Assets/", StringComparison.Ordinal) &&
                !ItemDefinitionCatalogLoader.TryLoadEditorSprite(address, out _, out string error))
            {
                WarnMissingIcon(address, error);
                return false;
            }
#endif
            request = gameRes.ResourceAssets.Load<Sprite>(address);
            return true;
        }
        catch (Exception exception)
        {
            WarnMissingIcon(address, exception.Message);
            return false;
        }
    }

    private static Sprite ReadLoadedIcon(AsyncOperationHandle<Sprite> request, string address)
    {
        if (request.IsValid() && request.Status == AsyncOperationStatus.Succeeded && request.Result != null)
            return request.Result;
        WarnMissingIcon(address, request.IsValid() ? request.OperationException?.Message : "请求已经释放");
        return null;
    }

    private static void WarnMissingIcon(string address, string reason)
    {
        Debug.LogWarning($"[BuffCatalog] 图标加载失败，HUD 使用名称首字：{address}。{reason}");
    }

    #endregion
}

/// <summary>玩家可继续改 JSON；仅在首次打开或内容变更后重建 Buff 二进制包。</summary>
[MemoryPack.MemoryPackable]
public sealed partial class BuffBinaryCatalog
{
    public BuffManifestDto Manifest;
    public List<BuffCatalogDto> Catalogs = new();
}
