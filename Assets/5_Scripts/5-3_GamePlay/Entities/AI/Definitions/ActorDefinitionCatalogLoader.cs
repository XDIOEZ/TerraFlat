using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 加载本体 Actor JSON 目录，并把数据配置绑定到保留行为组件的 AI 外壳 Prefab。
/// 外壳、Sprite 与动画控制器均使用稳定 Addressables 地址，移动资源文件不会破坏引用。
/// </summary>
public static class ActorDefinitionCatalogLoader
{
    public const int SupportedSchemaVersion = 1;
    public const string RelativeActorRoot = "GameConfig/Actors";
    public const string ManifestFileName = "actor-manifest.json";
    public const string RelativeManifestPath = RelativeActorRoot + "/" + ManifestFileName;

    private static readonly Dictionary<string, JObject> ResolvedSources =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Sprite> LoadedSprites =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, RuntimeAnimatorController> LoadedControllers =
        new(StringComparer.OrdinalIgnoreCase);

    public static string BuiltInActorRoot =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeActorRoot);

    public static string BuiltInManifestPath =>
        StreamingAssetsTextLoader.CombinePath(BuiltInActorRoot, ManifestFileName);

    #region 目录读取

    /// <summary>同步读取 Actor 定义，供编辑器迁移、静态测试和诊断使用。</summary>
    public static List<ItemDefinitionDto> LoadBuiltInDefinitions()
    {
        string manifestJson = File.ReadAllText(BuiltInManifestPath);
        ActorDefinitionManifestDto manifest = DeserializeManifest(manifestJson);
        ValidateManifest(manifest);

        var catalogs = new List<string>();
        foreach (ActorDefinitionPackageDto package in manifest.Packages.Where(entry => entry.Enabled))
        {
            string packagePath = ItemDefinitionCatalogLoader.ResolvePackagePath(BuiltInActorRoot, package.Path);
            catalogs.Add(ConvertActorCatalogToItemCatalog(File.ReadAllText(packagePath), package.Id));
        }

        List<JObject> resolved = ItemDefinitionCatalogLoader.ResolveDefinitionObjects(catalogs);
        CacheResolvedSources(resolved);
        return ConvertResolvedDefinitions(resolved);
    }

    /// <summary>异步加载全部本体 Actor 资源并原子注册。</summary>
    public static IEnumerator LoadBuiltInAsync(
        GameRes gameRes,
        Action<int> completed,
        Action<Exception> failed,
        Action<float> progress = null)
    {
        if (gameRes == null)
        {
            failed?.Invoke(new ArgumentNullException(nameof(gameRes)));
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
            failed?.Invoke(readError);
            yield break;
        }

        ActorDefinitionManifestDto manifest;
        try
        {
            manifest = DeserializeManifest(manifestJson);
            ValidateManifest(manifest);
        }
        catch (Exception exception)
        {
            failed?.Invoke(exception);
            yield break;
        }

        ActorDefinitionPackageDto[] packages = manifest.Packages.Where(entry => entry.Enabled).ToArray();
        var catalogs = new List<string>(packages.Length);
        for (int index = 0; index < packages.Length; index++)
        {
            ActorDefinitionPackageDto package = packages[index];
            string packagePath;
            try
            {
                packagePath = ItemDefinitionCatalogLoader.ResolvePackagePath(BuiltInActorRoot, package.Path);
            }
            catch (Exception exception)
            {
                failed?.Invoke(exception);
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
                failed?.Invoke(new IOException($"Actor 分包读取失败：{package.Id} ({packagePath})", readError));
                yield break;
            }

            try
            {
                catalogs.Add(ConvertActorCatalogToItemCatalog(packageJson, package.Id));
            }
            catch (Exception exception)
            {
                failed?.Invoke(exception);
                yield break;
            }

            progress?.Invoke(packages.Length == 0 ? 0.15f : 0.15f * (index + 1) / packages.Length);
        }

        List<ItemDefinitionDto> definitions;
        try
        {
            List<JObject> resolved = ItemDefinitionCatalogLoader.ResolveDefinitionObjects(catalogs);
            CacheResolvedSources(resolved);
            definitions = ConvertResolvedDefinitions(resolved);
        }
        catch (Exception exception)
        {
            failed?.Invoke(exception);
            yield break;
        }

        ItemDefinitionDto[] concrete = definitions.Where(definition => !definition.Abstract).ToArray();
        var shellAddresses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var actorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in concrete)
        {
            string id = definition.Id?.Trim();
            string shellId = definition.ShellPrefab?.Trim();
            string address = definition.ShellAddress?.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(shellId) ||
                string.IsNullOrWhiteSpace(address))
            {
                failed?.Invoke(new InvalidDataException(
                    $"Actor {id ?? "<empty>"} 必须声明 id、shellPrefab 与 shellAddress"));
                yield break;
            }

            if (!actorIds.Add(id))
            {
                failed?.Invoke(new InvalidDataException($"Actor ID 冲突：{id}"));
                yield break;
            }
            if (gameRes.ItemDefinitions.ContainsKey(id))
            {
                failed?.Invoke(new InvalidDataException($"Actor ID 与 ItemDefinition 冲突：{id}"));
                yield break;
            }

            if (string.IsNullOrWhiteSpace(definition.Visual?.AnimatorControllerAddress))
            {
                failed?.Invoke(new InvalidDataException(
                    $"Actor {id} 必须声明 visual.animatorControllerAddress；Actor 不再绑定 Sprite 子资源"));
                yield break;
            }

            if (shellAddresses.TryGetValue(shellId, out string existing) &&
                !string.Equals(existing, address, StringComparison.OrdinalIgnoreCase))
            {
                failed?.Invoke(new InvalidDataException(
                    $"Actor 外壳 {shellId} 同时绑定了不同地址：{existing} / {address}"));
                yield break;
            }
            shellAddresses[shellId] = address;
        }

        var shellHandles = shellAddresses.ToDictionary(
            pair => pair.Key,
            pair => Addressables.LoadAssetAsync<GameObject>(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        string[] controllerAddresses = concrete
            .Select(definition => definition.Visual?.AnimatorControllerAddress?.Trim())
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var controllerHandles = controllerAddresses.ToDictionary(
            address => address,
            Addressables.LoadAssetAsync<RuntimeAnimatorController>,
            StringComparer.OrdinalIgnoreCase);

        while (shellHandles.Values.Any(handle => !handle.IsDone) ||
               controllerHandles.Values.Any(handle => !handle.IsDone))
        {
            int total = shellHandles.Count + controllerHandles.Count;
            int done = shellHandles.Values.Count(handle => handle.IsDone) +
                       controllerHandles.Values.Count(handle => handle.IsDone);
            progress?.Invoke(total == 0 ? 0.85f : 0.15f + 0.7f * done / total);
            yield return null;
        }

        try
        {
            var loadedShells = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, AsyncOperationHandle<GameObject>> pair in shellHandles)
            {
                if (pair.Value.Status != AsyncOperationStatus.Succeeded || pair.Value.Result == null)
                    throw new InvalidDataException(
                        $"Actor 外壳 Addressable 无效：{pair.Key} -> {shellAddresses[pair.Key]}");
                ValidateActorShell(pair.Value.Result, pair.Key);
                loadedShells[pair.Key] = pair.Value.Result;
            }

            LoadedSprites.Clear();
            LoadedControllers.Clear();
            foreach (KeyValuePair<string, AsyncOperationHandle<RuntimeAnimatorController>> pair in controllerHandles)
            {
                if (pair.Value.Status != AsyncOperationStatus.Succeeded || pair.Value.Result == null)
                    throw new InvalidDataException($"Actor 动画控制器 Addressable 无效：{pair.Key}");
                LoadedControllers[pair.Key] = pair.Value.Result;
            }

            var runtimeDefinitions = new List<RuntimeItemDefinition>(concrete.Length);
            foreach (ItemDefinitionDto definition in concrete)
            {
                runtimeDefinitions.Add(ItemDefinitionCatalogLoader.BuildRuntimeDefinition(
                    gameRes,
                    definition,
                    LoadedSprites,
                    LoadedControllers,
                    isActor: true,
                    preloadedShells: loadedShells));
            }

            RegisterBuiltInDefinitionsAtomically(gameRes, loadedShells, runtimeDefinitions);

            progress?.Invoke(1f);
            Debug.Log($"[ActorDefinitionCatalog] 已加载 {runtimeDefinitions.Count} 个 JSON Actor");
            completed?.Invoke(runtimeDefinitions.Count);
        }
        catch (Exception exception)
        {
            failed?.Invoke(exception);
        }
    }

    #endregion

    #region MOD 与运行时扩展

    /// <summary>读取已完全解析的本体 Actor，MOD 可在其上继续合并差异。</summary>
    public static bool TryGetResolvedSource(string actorId, out JObject source)
    {
        source = null;
        if (string.IsNullOrWhiteSpace(actorId) ||
            !ResolvedSources.TryGetValue(actorId.Trim(), out JObject resolved))
        {
            return false;
        }

        source = (JObject)resolved.DeepClone();
        return true;
    }

    public static bool TryGetLoadedSprite(string address, out Sprite sprite)
    {
        return LoadedSprites.TryGetValue(address ?? string.Empty, out sprite);
    }

    public static bool TryGetLoadedController(
        string address,
        out RuntimeAnimatorController controller)
    {
        return LoadedControllers.TryGetValue(address ?? string.Empty, out controller);
    }

    /// <summary>将已解析的 MOD Actor 构建为运行时定义；调用方负责先解析 Bundle 视觉资源。</summary>
    public static RuntimeItemDefinition BuildExternalDefinition(
        GameRes gameRes,
        ItemDefinitionDto definition,
        IReadOnlyDictionary<string, Sprite> sprites,
        IReadOnlyDictionary<string, RuntimeAnimatorController> controllers)
    {
        GameObject shell = gameRes?.GetPrefab(definition?.ShellPrefab, false);
        ValidateActorShell(shell, definition?.ShellPrefab);
        return ItemDefinitionCatalogLoader.BuildRuntimeDefinition(
            gameRes,
            definition,
            sprites,
            controllers,
            isActor: true,
            preloadedShells: new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase)
            {
                [definition.ShellPrefab.Trim()] = shell
            });
    }

    public static void ResetRuntimeCatalog()
    {
        ResolvedSources.Clear();
        LoadedSprites.Clear();
        LoadedControllers.Clear();
    }

    #endregion

    #region 校验与转换

    /// <summary>任一 Actor 注册失败时撤销本批次，避免保留不可重试的半加载目录。</summary>
    private static void RegisterBuiltInDefinitionsAtomically(
        GameRes gameRes,
        IReadOnlyDictionary<string, GameObject> loadedShells,
        IReadOnlyList<RuntimeItemDefinition> runtimeDefinitions)
    {
        var registeredIds = new List<string>(runtimeDefinitions.Count);
        var registeredShellAliases = new List<KeyValuePair<string, GameObject>>(loadedShells.Count);
        try
        {
            foreach (RuntimeItemDefinition definition in runtimeDefinitions)
            {
                gameRes.RegisterActorDefinition(definition);
                registeredIds.Add(definition.Id);
            }
            foreach (KeyValuePair<string, GameObject> pair in loadedShells)
            {
                gameRes.RegisterPrefabAlias(pair.Key, pair.Value);
                registeredShellAliases.Add(pair);
            }
        }
        catch
        {
            for (int index = registeredShellAliases.Count - 1; index >= 0; index--)
            {
                KeyValuePair<string, GameObject> pair = registeredShellAliases[index];
                gameRes.UnregisterPrefabAlias(pair.Key, pair.Value);
            }
            for (int index = registeredIds.Count - 1; index >= 0; index--)
                gameRes.UnregisterExternalActorDefinition(registeredIds[index]);
            throw;
        }
    }

    private static ActorDefinitionManifestDto DeserializeManifest(string json)
    {
        ActorDefinitionManifestDto manifest = JsonConvert.DeserializeObject<ActorDefinitionManifestDto>(
            json,
            new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
        return manifest ?? throw new InvalidDataException("Actor Manifest 为空");
    }

    private static void ValidateManifest(ActorDefinitionManifestDto manifest)
    {
        if (manifest.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException(
                $"不支持 Actor Manifest schemaVersion={manifest.SchemaVersion}");
        if (manifest.Packages == null || manifest.Packages.Count == 0)
            throw new InvalidDataException("Actor Manifest 未声明 packages");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ActorDefinitionPackageDto package in manifest.Packages)
        {
            if (string.IsNullOrWhiteSpace(package.Id) || string.IsNullOrWhiteSpace(package.Path))
                throw new InvalidDataException("Actor Manifest 包含空 id 或 path");
            if (!ids.Add(package.Id.Trim()))
                throw new InvalidDataException($"Actor Manifest 分包 ID 冲突：{package.Id}");
        }
    }

    private static string ConvertActorCatalogToItemCatalog(string json, string sourceName)
    {
        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Actor 分包 JSON 无效：{sourceName}", exception);
        }

        int schemaVersion = root.Value<int?>("schemaVersion") ?? 0;
        if (schemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException(
                $"Actor 分包 {sourceName} schemaVersion={schemaVersion} 不受支持");
        if (root["actors"] is not JArray actors)
            throw new InvalidDataException($"Actor 分包 {sourceName} 缺少 actors 数组");

        return new JObject
        {
            ["schemaVersion"] = SupportedSchemaVersion,
            ["items"] = actors.DeepClone()
        }.ToString(Formatting.None);
    }

    private static List<ItemDefinitionDto> ConvertResolvedDefinitions(IEnumerable<JObject> sources)
    {
        return sources.Select(source => source.ToObject<ItemDefinitionDto>() ??
                                        throw new InvalidDataException(
                                            $"无法解析 Actor：{source.Value<string>("id")}"))
            .ToList();
    }

    private static void CacheResolvedSources(IEnumerable<JObject> definitions)
    {
        ResolvedSources.Clear();
        foreach (JObject definition in definitions)
        {
            string id = definition.Value<string>("id")?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                continue;
            ResolvedSources[id] = (JObject)definition.DeepClone();
        }
    }

    private static void ValidateActorShell(GameObject shell, string shellId)
    {
        if (shell == null)
            throw new InvalidDataException($"Actor 外壳不存在：{shellId ?? "<empty>"}");
        if (shell.GetComponent<Item>()?.itemData == null)
            throw new InvalidDataException($"Actor 外壳缺少有效 Item：{shellId}");
        bool hasActor = shell.GetComponentsInChildren<MonoBehaviour>(true)
            .Any(component => component is IAIActor);
        if (!hasActor)
            throw new InvalidDataException($"Actor 外壳缺少 IAIActor 行为：{shellId}");
    }

    #endregion
}

/// <summary>Actor 本体配置清单。</summary>
[Serializable]
public sealed class ActorDefinitionManifestDto
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion = 1;

    [JsonProperty("packages")]
    public List<ActorDefinitionPackageDto> Packages = new();
}

/// <summary>Actor 定义分包入口。</summary>
[Serializable]
public sealed class ActorDefinitionPackageDto
{
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("path")]
    public string Path;

    [JsonProperty("enabled")]
    public bool Enabled = true;
}
