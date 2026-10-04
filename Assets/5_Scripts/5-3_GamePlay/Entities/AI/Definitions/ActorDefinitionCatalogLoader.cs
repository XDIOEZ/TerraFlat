using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 加载本体 Actor JSON 目录，并把数据配置绑定到保留行为组件的 AI 外壳 Prefab。
/// 外壳与可选动画控制器使用稳定 Addressables 地址；无动画 Actor 保留外壳上的静态 Sprite。
/// </summary>
public static class ActorDefinitionCatalogLoader
{
    public const int SupportedSchemaVersion = 1;
    public const string ShellAddressableLabel = "ActorShell";
    public const string RelativeActorRoot = "GameConfig/Actors";
    public const string ManifestFileName = "actor-manifest.json";
    public const string RelativeManifestPath = RelativeActorRoot + "/" + ManifestFileName;

    private static Dictionary<string, JObject> ResolvedSources =
        new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, Sprite> LoadedSprites =
        new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, RuntimeAnimatorController> LoadedControllers =
        new(StringComparer.OrdinalIgnoreCase);

    #region 目录读取

    /// <summary>F5 候选构建时保留已发布的本体 Actor 目录和资源引用，单个定义无效时只撤销该定义的更新。</summary>
    public sealed class ReloadSnapshot
    {
        internal readonly Dictionary<string, RuntimeItemDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, JObject> Sources = new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, Sprite> Sprites = new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, RuntimeAnimatorController> Controllers = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> issues = new();
        public IReadOnlyList<string> Issues => issues;

        /// <summary>记录未能发布的具体定义及原因。</summary>
        internal void RecordIssue(string issue) => issues.Add(issue);
    }

    public static string BuiltInActorRoot =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeActorRoot);

    public static string BuiltInManifestPath =>
        StreamingAssetsTextLoader.CombinePath(BuiltInActorRoot, ManifestFileName);

    /// <summary>让候选 Actor 来源与资源缓存独立于仍在运行的正式目录。</summary>
    internal static void ConfigureResourceReload(ResourceReloadContext context)
    {
        context.AddDictionary(() => ResolvedSources, value => ResolvedSources = value);
        context.AddDictionary(() => LoadedSprites, value => LoadedSprites = value);
        context.AddDictionary(() => LoadedControllers, value => LoadedControllers = value);
    }

    /// <summary>只捕获本体来源对应的 Actor，MOD Actor 留给 MOD 阶段独立处理。</summary>
    public static ReloadSnapshot CaptureReloadSnapshot(GameRes resources)
    {
        var snapshot = new ReloadSnapshot();
        foreach (KeyValuePair<string, JObject> pair in ResolvedSources)
            snapshot.Sources.Add(pair.Key, (JObject)pair.Value.DeepClone());
        foreach (KeyValuePair<string, RuntimeItemDefinition> pair in resources.ActorDefinitions)
            if (snapshot.Sources.ContainsKey(pair.Key)) snapshot.Definitions.Add(pair.Key, pair.Value);
        foreach (KeyValuePair<string, Sprite> pair in LoadedSprites)
            snapshot.Sprites.Add(pair.Key, pair.Value);
        foreach (KeyValuePair<string, RuntimeAnimatorController> pair in LoadedControllers)
            snapshot.Controllers.Add(pair.Key, pair.Value);
        return snapshot;
    }

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
        Action<float> progress = null,
        ReloadSnapshot previous = null)
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

            string converted = null;
            yield return StreamingAssetsTextLoader.RunPureDataAsync(
                () => ConvertActorCatalogToItemCatalog(packageJson, package.Id),
                value => converted = value, exception => readError = exception);
            if (readError != null) { failed?.Invoke(readError); yield break; }
            catalogs.Add(converted);

            progress?.Invoke(packages.Length == 0 ? 0.15f : 0.15f * (index + 1) / packages.Length);
        }

        List<JObject> resolved = null;
        yield return StreamingAssetsTextLoader.RunPureDataAsync(
            () => ItemDefinitionCatalogLoader.ResolveDefinitionObjects(catalogs),
            value => resolved = value, exception => readError = exception);
        if (readError != null) { failed?.Invoke(readError); yield break; }
        List<ItemDefinitionDto> definitions;
        var rejected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            CacheResolvedSources(resolved);
            definitions = previous == null
                ? ConvertResolvedDefinitions(resolved)
                : ConvertResolvedDefinitionsForReload(resolved, previous, rejected);
            if (previous == null)
                ItemDefinitionCatalogLoader.ValidateLootTableItemIds(gameRes, definitions);
        }
        catch (Exception exception)
        {
            failed?.Invoke(exception);
            yield break;
        }

        ItemDefinitionDto[] concrete = definitions.Where(definition => !definition.Abstract).ToArray();
        var knownItemIds = new HashSet<string>(gameRes.ItemDefinitions.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in concrete)
            if (!string.IsNullOrWhiteSpace(definition.Id)) knownItemIds.Add(definition.Id.Trim());
        if (previous != null) knownItemIds.UnionWith(previous.Definitions.Keys);

        var candidates = new List<ItemDefinitionDto>(concrete.Length);
        var shellAddresses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var controllerAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in concrete)
        {
            string id = definition.Id?.Trim();
            if (string.IsNullOrWhiteSpace(id) || !actorIds.Add(id))
            {
                failed?.Invoke(new InvalidDataException($"Actor ID 为空或重复：{id ?? "<empty>"}"));
                yield break;
            }

            Exception validationError = null;
            try
            {
                string shellId = definition.ShellPrefab?.Trim();
                string address = definition.ShellAddress?.Trim();
                string controllerAddress = definition.Visual?.AnimatorControllerAddress?.Trim();
                if (string.IsNullOrWhiteSpace(shellId) || string.IsNullOrWhiteSpace(address))
                    throw new InvalidDataException($"Actor {id} 必须声明 shellPrefab 与 shellAddress");
                if (gameRes.ItemDefinitions.ContainsKey(id))
                    throw new InvalidDataException($"Actor ID 与 ItemDefinition 冲突：{id}");
                if (shellAddresses.TryGetValue(shellId, out string existing) &&
                    !string.Equals(existing, address, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Actor 外壳 {shellId} 同时绑定了不同地址：{existing} / {address}");
                if (previous != null)
                    ItemDefinitionCatalogLoader.ValidateLootTableItemIds(gameRes, definition, knownItemIds);

                candidates.Add(definition);
                shellAddresses[shellId] = address;
                if (!string.IsNullOrWhiteSpace(controllerAddress))
                    controllerAddresses.Add(controllerAddress);
            }
            catch (Exception exception)
            {
                validationError = exception;
            }
            if (validationError == null) continue;
            if (previous == null) { failed?.Invoke(validationError); yield break; }
            RejectActorUpdate(previous, id, validationError, rejected);
        }

        var shellHandles = new Dictionary<string, AsyncOperationHandle<GameObject>>(StringComparer.OrdinalIgnoreCase);
        var controllerHandles = new Dictionary<string, AsyncOperationHandle<RuntimeAnimatorController>>(StringComparer.OrdinalIgnoreCase);
        var shellErrors = new Dictionary<string, Exception>(StringComparer.OrdinalIgnoreCase);
        var controllerErrors = new Dictionary<string, Exception>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> pair in shellAddresses)
        {
            try { shellHandles.Add(pair.Key, gameRes.ResourceAssets.Load<GameObject>(pair.Value)); }
            catch (Exception exception) { shellErrors.Add(pair.Key, exception); }
            if (gameRes.ShouldYieldResourceWork()) yield return null;
            if (shellHandles.Count % 4 == 0)
            {
                while (shellHandles.Values.Any(handle => !handle.IsDone)) yield return null;
                yield return null;
            }
        }
        foreach (string address in controllerAddresses)
        {
            try { controllerHandles.Add(address, gameRes.ResourceAssets.Load<RuntimeAnimatorController>(address)); }
            catch (Exception exception) { controllerErrors.Add(address, exception); }
            if (gameRes.ShouldYieldResourceWork()) yield return null;
            if ((shellHandles.Count + controllerHandles.Count) % 4 == 0)
            {
                while (shellHandles.Values.Any(handle => !handle.IsDone) ||
                       controllerHandles.Values.Any(handle => !handle.IsDone)) yield return null;
                yield return null;
            }
        }

        while (shellHandles.Values.Any(handle => !handle.IsDone) ||
               controllerHandles.Values.Any(handle => !handle.IsDone))
        {
            int total = shellHandles.Count + controllerHandles.Count;
            int done = shellHandles.Values.Count(handle => handle.IsDone) +
                       controllerHandles.Values.Count(handle => handle.IsDone);
            progress?.Invoke(total == 0 ? 0.85f : 0.15f + 0.7f * done / total);
            yield return null;
        }

        IEnumerator BuildRuntimeActors()
        {
            var loadedShells = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, AsyncOperationHandle<GameObject>> pair in shellHandles)
            {
                try
                {
                    if (pair.Value.Status != AsyncOperationStatus.Succeeded || pair.Value.Result == null)
                        throw new InvalidDataException(
                            $"Actor 外壳 Addressable 无效：{pair.Key} -> {shellAddresses[pair.Key]}");
                    ValidateActorShell(pair.Value.Result, pair.Key);
                    loadedShells[pair.Key] = pair.Value.Result;
                }
                catch (Exception exception) { shellErrors[pair.Key] = exception; }
                if (gameRes.ShouldYieldResourceWork()) yield return null;
            }

            LoadedSprites.Clear();
            LoadedControllers.Clear();
            if (previous != null)
            {
                foreach (KeyValuePair<string, Sprite> pair in previous.Sprites)
                    LoadedSprites.Add(pair.Key, pair.Value);
                foreach (KeyValuePair<string, RuntimeAnimatorController> pair in previous.Controllers)
                    LoadedControllers.Add(pair.Key, pair.Value);
            }
            foreach (KeyValuePair<string, AsyncOperationHandle<RuntimeAnimatorController>> pair in controllerHandles)
            {
                if (pair.Value.Status != AsyncOperationStatus.Succeeded || pair.Value.Result == null)
                    controllerErrors[pair.Key] = new InvalidDataException($"Actor 动画控制器 Addressable 无效：{pair.Key}");
                else
                    LoadedControllers[pair.Key] = pair.Value.Result;
            }

            var selected = new Dictionary<string, RuntimeItemDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (ItemDefinitionDto definition in candidates)
            {
                string id = definition.Id.Trim();
                try
                {
                    string shellId = definition.ShellPrefab.Trim();
                    string controllerAddress = definition.Visual?.AnimatorControllerAddress?.Trim();
                    if (shellErrors.TryGetValue(shellId, out Exception shellError)) throw shellError;
                    if (!string.IsNullOrWhiteSpace(controllerAddress) &&
                        controllerErrors.TryGetValue(controllerAddress, out Exception controllerError))
                    {
                        throw controllerError;
                    }
                    selected.Add(id, ItemDefinitionCatalogLoader.BuildRuntimeDefinition(
                        gameRes, definition, LoadedSprites, LoadedControllers,
                        isActor: true, preloadedShells: loadedShells));
                }
                catch (Exception exception)
                {
                    if (previous == null) throw;
                    RejectActorUpdate(previous, id, exception, rejected);
                }
                if (gameRes.ShouldYieldResourceWork()) yield return null;
            }

            if (previous != null)
            {
                foreach (KeyValuePair<string, RuntimeItemDefinition> pair in previous.Definitions)
                {
                    if (selected.ContainsKey(pair.Key)) continue;
                    selected.Add(pair.Key, pair.Value);
                    if (!rejected.Contains(pair.Key))
                        RejectActorUpdate(previous, pair.Key,
                            new InvalidDataException("新目录缺少已有 Actor 定义"), rejected);
                }
                foreach (KeyValuePair<string, JObject> pair in previous.Sources)
                    if (!ResolvedSources.ContainsKey(pair.Key))
                        ResolvedSources.Add(pair.Key, (JObject)pair.Value.DeepClone());

                // 引用闭包按最终选中定义复核，防止健康 Actor 指向被跳过的新增 Actor。
                bool changed;
                do
                {
                    changed = false;
                    var publishedIds = new HashSet<string>(gameRes.ItemDefinitions.Keys, StringComparer.OrdinalIgnoreCase);
                    publishedIds.UnionWith(selected.Keys);
                    foreach (ItemDefinitionDto definition in candidates)
                    {
                        string id = definition.Id.Trim();
                        previous.Definitions.TryGetValue(id, out RuntimeItemDefinition old);
                        if (!selected.TryGetValue(id, out RuntimeItemDefinition current) ||
                            ReferenceEquals(current, old))
                            continue;
                        try
                        {
                            ItemDefinitionCatalogLoader.ValidateLootTableItemIds(
                                gameRes, definition, publishedIds);
                        }
                        catch (Exception exception)
                        {
                            selected.Remove(id);
                            if (old != null) selected.Add(id, old);
                            RejectActorUpdate(previous, id, exception, rejected);
                            changed = true;
                        }
                    }
                } while (changed);
            }

            RuntimeItemDefinition[] runtimeDefinitions = selected.Values.ToArray();
            RegisterBuiltInDefinitionsAtomically(gameRes,
                BuildShellAliases(runtimeDefinitions), runtimeDefinitions);

            progress?.Invoke(1f);
            Debug.Log($"[ActorDefinitionCatalog] 已加载 {runtimeDefinitions.Length} 个 JSON Actor");
            completed?.Invoke(runtimeDefinitions.Length);
        }

        // 手动转发本地迭代器异常，保留目录加载器的失败回调契约。
        IEnumerator build = BuildRuntimeActors();
        try
        {
            while (true)
            {
                bool moved = false;
                try { moved = build.MoveNext(); }
                catch (Exception exception) { failed?.Invoke(exception); }
                if (!moved) break;
                yield return build.Current;
            }
        }
        finally { (build as IDisposable)?.Dispose(); }
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

    /// <summary>拒绝单个无效 Actor 候选，并恢复其已发布的继承来源供 MOD 继续使用。</summary>
    private static void RejectActorUpdate(ReloadSnapshot previous, string id,
        Exception error, HashSet<string> rejected)
    {
        if (!rejected.Add(id)) return;
        if (previous.Sources.TryGetValue(id, out JObject oldSource))
            ResolvedSources[id] = (JObject)oldSource.DeepClone();
        else
            ResolvedSources.Remove(id);
        string disposition = previous.Definitions.ContainsKey(id) ? "保留旧版" :
            previous.Sources.ContainsKey(id) ? "保留旧来源" : "未发布新增定义";
        previous.RecordIssue($"Actor {id} {disposition}：{error.Message}");
    }

    /// <summary>外壳别名只登记最终选中的 Actor 版本，避免无效候选覆盖旧外壳。</summary>
    private static Dictionary<string, GameObject> BuildShellAliases(
        IEnumerable<RuntimeItemDefinition> definitions)
    {
        var aliases = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        foreach (RuntimeItemDefinition definition in definitions)
        {
            if (aliases.TryGetValue(definition.ShellPrefabId, out GameObject existing) &&
                existing != definition.ShellPrefab)
                throw new InvalidDataException($"Actor 外壳别名冲突：{definition.ShellPrefabId}");
            aliases[definition.ShellPrefabId] = definition.ShellPrefab;
        }
        return aliases;
    }

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

    /// <summary>F5 逐个转换 Actor；单个 DTO 格式错误不影响同目录其他定义。</summary>
    private static List<ItemDefinitionDto> ConvertResolvedDefinitionsForReload(
        IEnumerable<JObject> sources, ReloadSnapshot previous, HashSet<string> rejected)
    {
        var definitions = new List<ItemDefinitionDto>();
        foreach (JObject source in sources)
        {
            string id = source.Value<string>("id")?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("Actor 定义缺少稳定 ID，无法隔离候选。");
            try
            {
                definitions.Add(source.ToObject<ItemDefinitionDto>() ??
                    throw new InvalidDataException($"Actor {id} 无法转换为配置对象。"));
            }
            catch (Exception exception)
            {
                RejectActorUpdate(previous, id, exception, rejected);
            }
        }
        return definitions;
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
