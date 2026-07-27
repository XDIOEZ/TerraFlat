using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public enum ModLoadState
{
    NotStarted,
    Loading,
    Ready,
    Failed
}

public sealed class ModRuntimeManager : MonoBehaviour
{
    #region 常量与静态入口

    private const string ManifestFileName = "manifest.json";
    public const int SupportedApiVersion = 1;
    private const int MaximumPackageFileCount = 4096;
    private const long MaximumPackageBytes = 1024L * 1024L * 1024L;
    private const int MaximumJsonCharacters = 8 * 1024 * 1024;
    private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

    public static ModRuntimeManager Instance { get; private set; }

    public static ModRuntimeManager Ensure(GameObject host)
    {
        if (Instance != null)
            return Instance;

        ModRuntimeManager manager = host.GetComponent<ModRuntimeManager>();
        if (manager == null)
            manager = host.AddComponent<ModRuntimeManager>();

        return manager;
    }

    #endregion

    #region 运行时状态

    private readonly List<ModPackage> loadedPackages = new();
    private readonly Dictionary<string, ModPackage> packagesById = new(IdComparer);
    private readonly Dictionary<string, ModLuaRuntime> luaRuntimes = new(IdComparer);
    private readonly Dictionary<string, string> globalStates = new(IdComparer);
    private readonly List<AssetBundle> loadedBundles = new();
    private readonly List<UnityEngine.Object> clonedAssets = new();
    private readonly HashSet<GameObject> runtimeTemplates = new();
    private readonly List<PendingItemDefinition> pendingItemDefinitions = new();
    private readonly List<PendingPatchDocument> pendingPatchDocuments = new();
    private readonly Dictionary<string, ModDefinitionInfo> definitionInfos = new(IdComparer);
    private ModProfile activeProfile;
    private bool safeModeActive;
    private GameManager boundGameManager;
    private bool staticEventsBound;
    private bool worldMutationAllowed = true;

    public ModLoadState State { get; private set; } = ModLoadState.NotStarted;
    public string FailureReason { get; private set; }
    public string ModSetHash { get; private set; } = string.Empty;
    public string ModsRootPath => Path.Combine(Application.persistentDataPath, "Mods");
    public bool IsReady => State == ModLoadState.Ready;
    public bool IsSafeModeActive => safeModeActive;
    public IReadOnlyList<ModManifest> LoadedManifests => loadedPackages.Select(package => package.Manifest).ToList();
    public IReadOnlyCollection<ModDefinitionInfo> DefinitionInfos => definitionInfos.Values;

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ModManagerOverlay.Ensure(gameObject, this);
    }

    private void Update()
    {
        if (State == ModLoadState.Ready)
            BindGameEvents();

        foreach (ModPackage package in loadedPackages)
        {
            if (!luaRuntimes.TryGetValue(package.Manifest.Id, out ModLuaRuntime runtime))
                continue;

            try
            {
                runtime.Tick(Time.unscaledDeltaTime);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MOD:{package.Manifest.Id}] OnUpdate 执行失败：{ex.Message}");
            }
        }
    }

    private void OnDestroy()
    {
        UnloadAll();
        if (Instance == this)
            Instance = null;
    }

    #endregion

    #region 加载流程

    public IEnumerator LoadEnabledMods(GameRes gameRes, Action<string, float> reportProgress = null)
    {
        if (State == ModLoadState.Loading)
            yield break;

        State = ModLoadState.Loading;
        FailureReason = null;
        UnloadAll();
        Directory.CreateDirectory(ModsRootPath);

        IEnumerator routine = LoadEnabledModsCore(gameRes, reportProgress);
        while (true)
        {
            bool movedNext;
            object current = null;
            try
            {
                movedNext = routine.MoveNext();
                if (movedNext)
                    current = routine.Current;
            }
            catch (Exception ex)
            {
                FailureReason = ex.Message;
                State = ModLoadState.Failed;
                ModProfileStore.RecordLoadFailure(ex.ToString());
                Debug.LogError($"[ModRuntime] MOD 加载失败：{FailureReason}");
                Debug.LogException(ex);
                UnloadAll(keepFailureState: true);
                yield break;
            }

            if (!movedNext)
                yield break;
            yield return current;
        }
    }

    private IEnumerator LoadEnabledModsCore(GameRes gameRes, Action<string, float> reportProgress)
    {
        reportProgress?.Invoke("扫描 MOD 清单", 0f);
        List<ModPackage> packages = ScanPackages();
        activeProfile = ModProfileStore.LoadActiveProfile();
        safeModeActive = ModProfileStore.ConsumeSafeModeRequest();
        if (safeModeActive)
        {
            packages.Clear();
            Debug.LogWarning("[ModRuntime] 检测到上次加载失败，本次已使用安全模式跳过所有外部 MOD");
        }
        else
        {
            packages = packages.Where(package => activeProfile.IsEnabled(package.Manifest.Id)).ToList();
        }

        List<ModPackage> sortedPackages = ResolveLoadOrder(packages, activeProfile);

        for (int i = 0; i < sortedPackages.Count; i++)
        {
            ModPackage package = sortedPackages[i];
            reportProgress?.Invoke($"读取 MOD 内容：{package.Manifest.Name ?? package.Manifest.Id}", CalculateProgress(i, sortedPackages.Count, 0.05f, 0.45f));
            LoadPackageSources(gameRes, package);
            loadedPackages.Add(package);
            packagesById.Add(package.Manifest.Id, package);
            yield return null;
        }

        reportProgress?.Invoke("解析 Def 继承与 Patch", 0.55f);
        ProcessItemDefinitions(gameRes);
        yield return null;

        for (int i = 0; i < loadedPackages.Count; i++)
        {
            ModPackage package = loadedPackages[i];
            reportProgress?.Invoke($"初始化 MOD 脚本：{package.Manifest.Name ?? package.Manifest.Id}", CalculateProgress(i, loadedPackages.Count, 0.65f, 0.3f));
            InitializeLua(package);
            yield return null;
        }

        ModSetHash = ComputeModSetHash(loadedPackages);
        State = ModLoadState.Ready;
        BindGameEvents();
        DispatchEvent("content.ready", new
        {
            modCount = loadedPackages.Count,
            modSetHash = ModSetHash,
            safeMode = safeModeActive
        });
        reportProgress?.Invoke(loadedPackages.Count == 0 ? "未启用 MOD" : $"已加载 {loadedPackages.Count} 个 MOD", 1f);
        Debug.Log($"[ModRuntime] 加载完成，数量={loadedPackages.Count}，集合哈希={ModSetHash}");
    }

    private List<ModPackage> ScanPackages()
    {
        List<ModPackage> packages = new();
        string[] directories = Directory.GetDirectories(ModsRootPath, "*", SearchOption.TopDirectoryOnly);
        Array.Sort(directories, StringComparer.OrdinalIgnoreCase);

        foreach (string directory in directories)
        {
            if (File.Exists(Path.Combine(directory, ".disabled")))
                continue;

            string manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
                continue;

            ValidateNoReparsePoints(ModsRootPath, manifestPath);
            string manifestJson = ReadLimitedText(manifestPath, MaximumJsonCharacters);
            ModManifest manifest = JsonConvert.DeserializeObject<ModManifest>(manifestJson)
                ?? throw new InvalidDataException($"MOD 清单为空：{manifestPath}");

            ValidateManifest(manifest, directory);
            string contentHash = ComputePackageHash(directory, manifestJson);
            if (!string.IsNullOrWhiteSpace(manifest.ContentHash) &&
                !string.Equals(manifest.ContentHash.Trim(), contentHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"MOD {manifest.Id} 内容哈希不匹配，文件可能不完整或已被修改");
            }

            manifest.ContentHash = contentHash;
            packages.Add(new ModPackage(directory, manifest));
        }

        return packages;
    }

    private static List<ModPackage> ResolveLoadOrder(List<ModPackage> packages, ModProfile profile)
    {
        Dictionary<string, ModPackage> byId = new(IdComparer);
        foreach (ModPackage package in packages)
        {
            if (!byId.TryAdd(package.Manifest.Id, package))
                throw new InvalidDataException($"发现重复 MOD ID：{package.Manifest.Id}");
        }

        Dictionary<string, HashSet<string>> dependencies = new(IdComparer);
        Dictionary<string, HashSet<string>> dependents = new(IdComparer);
        foreach (ModPackage package in packages)
        {
            dependencies[package.Manifest.Id] = new HashSet<string>(IdComparer);
            dependents[package.Manifest.Id] = new HashSet<string>(IdComparer);
        }

        foreach (ModPackage package in packages)
        {
            ModManifest manifest = package.Manifest;
            foreach (string conflictId in manifest.Conflicts ?? Enumerable.Empty<string>())
            {
                if (byId.ContainsKey(conflictId))
                    throw new InvalidDataException($"MOD {manifest.Id} 与已启用 MOD {conflictId} 不兼容");
            }

            foreach (ModDependency dependency in manifest.Dependencies ?? Enumerable.Empty<ModDependency>())
            {
                if (!byId.TryGetValue(dependency.Id, out ModPackage dependencyPackage))
                {
                    if (!dependency.Optional)
                        throw new InvalidDataException($"MOD {manifest.Id} 缺少依赖：{dependency.Id}");
                    continue;
                }

                ValidateVersionRange(dependencyPackage.Manifest.Version, dependency.MinVersion, dependency.MaxVersion,
                    $"MOD {manifest.Id} 的依赖 {dependency.Id}");
                AddOrderEdge(dependency.Id, manifest.Id, dependencies, dependents);
            }

            foreach (string id in manifest.LoadAfter ?? Enumerable.Empty<string>())
            {
                if (byId.ContainsKey(id))
                    AddOrderEdge(id, manifest.Id, dependencies, dependents);
            }

            foreach (string id in manifest.LoadBefore ?? Enumerable.Empty<string>())
            {
                if (byId.ContainsKey(id))
                    AddOrderEdge(manifest.Id, id, dependencies, dependents);
            }
        }

        List<ModPackage> result = new(packages.Count);
        List<ModPackage> ready = packages
            .Where(package => dependencies[package.Manifest.Id].Count == 0)
            .OrderBy(package => package.Manifest.LoadOrder)
            .ThenBy(package => profile?.GetSoftLoadOrder(package.Manifest.Id) ?? int.MaxValue)
            .ThenBy(package => package.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        while (ready.Count > 0)
        {
            ModPackage package = ready[0];
            ready.RemoveAt(0);
            result.Add(package);

            foreach (string dependentId in dependents[package.Manifest.Id].ToArray())
            {
                dependencies[dependentId].Remove(package.Manifest.Id);
                if (dependencies[dependentId].Count == 0)
                {
                    ready.Add(byId[dependentId]);
                    ready = ready
                        .OrderBy(candidate => candidate.Manifest.LoadOrder)
                        .ThenBy(candidate => profile?.GetSoftLoadOrder(candidate.Manifest.Id) ?? int.MaxValue)
                        .ThenBy(candidate => candidate.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }
        }

        if (result.Count != packages.Count)
        {
            string cycle = string.Join(", ", dependencies.Where(pair => pair.Value.Count > 0).Select(pair => pair.Key));
            throw new InvalidDataException($"MOD 依赖或排序规则存在循环：{cycle}");
        }

        return result;
    }

    private void LoadPackageSources(GameRes gameRes, ModPackage package)
    {
        foreach (ModBundleDefinition bundleDefinition in package.Manifest.Bundles ?? Enumerable.Empty<ModBundleDefinition>())
        {
            if (!IsCurrentPlatform(bundleDefinition.Platform))
                continue;

            if (package.Bundles.ContainsKey(bundleDefinition.Id))
                throw new InvalidDataException($"MOD {package.Manifest.Id} 存在重复 Bundle ID：{bundleDefinition.Id}");

            string bundlePath = ModPathUtility.ResolvePackagePath(package.RootPath, bundleDefinition.Path, true);
            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
                throw new InvalidDataException($"MOD {package.Manifest.Id} 无法加载 AssetBundle：{bundleDefinition.Path}");

            package.Bundles.Add(bundleDefinition.Id, bundle);
            loadedBundles.Add(bundle);
        }

        foreach (string definitionFile in package.Manifest.DefinitionFiles ?? Enumerable.Empty<string>())
        {
            string fullPath = ModPathUtility.ResolvePackagePath(package.RootPath, definitionFile, true);
            string json = ReadLimitedText(fullPath, MaximumJsonCharacters);
            JObject document = JObject.Parse(json);
            foreach (JToken token in document["assets"] as JArray ?? new JArray())
            {
                ModAssetDefinition definition = token.ToObject<ModAssetDefinition>()
                    ?? throw new InvalidDataException($"MOD {package.Manifest.Id} 资源定义无效：{definitionFile}");
                RegisterBundleAsset(gameRes, package, definition);
            }

            int itemIndex = 0;
            foreach (JToken token in document["items"] as JArray ?? new JArray())
            {
                if (token is not JObject itemObject)
                    throw new InvalidDataException($"MOD {package.Manifest.Id} 物品 Def 无效：{definitionFile}#{itemIndex}");

                string id = itemObject.Value<string>("id");
                ValidateContentId(package.Manifest.Id, id);
                pendingItemDefinitions.Add(new PendingItemDefinition(package, definitionFile, itemIndex++, (JObject)itemObject.DeepClone()));
            }
        }

        foreach (string patchFile in package.Manifest.PatchFiles ?? Enumerable.Empty<string>())
        {
            string fullPath = ModPathUtility.ResolvePackagePath(package.RootPath, patchFile, true);
            ModPatchDocument document = JsonConvert.DeserializeObject<ModPatchDocument>(ReadLimitedText(fullPath, MaximumJsonCharacters))
                ?? throw new InvalidDataException($"MOD {package.Manifest.Id} Patch 文件为空：{patchFile}");
            pendingPatchDocuments.Add(new PendingPatchDocument(package, patchFile, document));
        }

        foreach (string localizationFile in package.Manifest.LocalizationFiles ?? Enumerable.Empty<string>())
        {
            string fullPath = ModPathUtility.ResolvePackagePath(package.RootPath, localizationFile, true);
            ModLocalizationDocument document = JsonConvert.DeserializeObject<ModLocalizationDocument>(ReadLimitedText(fullPath, MaximumJsonCharacters))
                ?? throw new InvalidDataException($"MOD {package.Manifest.Id} 本地化文件为空：{localizationFile}");
            ModLocalizationRegistry.Register(package.Manifest.Id, document);
        }

        if (!string.IsNullOrWhiteSpace(package.Manifest.SettingsFile))
        {
            string fullPath = ModPathUtility.ResolvePackagePath(package.RootPath, package.Manifest.SettingsFile, true);
            ModSettingsDocument document = JsonConvert.DeserializeObject<ModSettingsDocument>(ReadLimitedText(fullPath, MaximumJsonCharacters))
                ?? throw new InvalidDataException($"MOD {package.Manifest.Id} 设置文件为空：{package.Manifest.SettingsFile}");
            ModSettingsRegistry.Register(package.Manifest.Id, document);
        }
    }

    private void ProcessItemDefinitions(GameRes gameRes)
    {
        Dictionary<string, PendingItemDefinition> sources = new(IdComparer);
        foreach (PendingItemDefinition pending in pendingItemDefinitions)
        {
            string id = pending.Document.Value<string>("id");
            if (!sources.TryAdd(id, pending))
                throw new InvalidDataException($"重复物品 Def：{id}");
        }

        Dictionary<string, JObject> resolved = new(IdComparer);
        HashSet<string> resolving = new(IdComparer);
        foreach (string id in sources.Keys)
            ResolveItemDefinition(id, sources, resolved, resolving);

        foreach (PendingPatchDocument pending in pendingPatchDocuments)
        {
            int operationIndex = 0;
            foreach (ModPatchOperation operation in pending.Document.Patches ?? Enumerable.Empty<ModPatchOperation>())
            {
                ApplyItemPatch(gameRes, resolved, pending, operation, operationIndex++);
            }
        }

        foreach (KeyValuePair<string, JObject> pair in resolved)
        {
            ModItemDefinition definition = pair.Value.ToObject<ModItemDefinition>()
                ?? throw new InvalidDataException($"物品 Def 无法解析：{pair.Key}");
            if (definition.Abstract)
                continue;
            if (string.IsNullOrWhiteSpace(definition.BasePrefab))
                throw new InvalidDataException($"物品 Def {definition.Id} 缺少 basePrefab 或有效 parent");

            PendingItemDefinition source = sources.TryGetValue(pair.Key, out PendingItemDefinition declared)
                ? declared
                : null;
            ModPackage owner = source?.Package ?? ResolveLastPatchOwner(pair.Key);
            if (owner == null)
                throw new InvalidDataException($"无法确定物品 Def 来源：{pair.Key}");

            if (!string.IsNullOrWhiteSpace(definition.LabelKey))
                definition.GameName = ModLocalizationRegistry.Translate(definition.LabelKey, definition.GameName);
            if (!string.IsNullOrWhiteSpace(definition.DescriptionKey))
                definition.Description = ModLocalizationRegistry.Translate(definition.DescriptionKey, definition.Description);

            bool replacesExisting = gameRes.AllPrefabs.ContainsKey(definition.Id);
            RegisterJsonItem(gameRes, owner, definition, replacesExisting, source != null);

            ModDefinitionInfo info = GetOrCreateDefinitionInfo(definition.Id);
            info.Materialized = true;
            info.ReplacedBuiltInContent = replacesExisting;
        }
    }

    private JObject ResolveItemDefinition(
        string id,
        Dictionary<string, PendingItemDefinition> sources,
        Dictionary<string, JObject> resolved,
        HashSet<string> resolving)
    {
        if (resolved.TryGetValue(id, out JObject existing))
            return existing;
        if (!sources.TryGetValue(id, out PendingItemDefinition source))
            return null;
        if (!resolving.Add(id))
            throw new InvalidDataException($"物品 Def 继承存在循环：{id}");

        JObject result = new();
        string parentId = source.Document.Value<string>("parent");
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            JObject parent = ResolveItemDefinition(parentId, sources, resolved, resolving);
            if (parent != null)
                result = (JObject)parent.DeepClone();
            else
                result["basePrefab"] = parentId;
        }

        MergeDefinition(result, source.Document);
        result["id"] = id;
        result["abstract"] = source.Document.Value<bool?>("abstract") ?? false;
        result.Remove("parent");
        resolving.Remove(id);
        resolved.Add(id, result);

        definitionInfos[id] = new ModDefinitionInfo
        {
            Id = id,
            DeclaringModId = source.Package.Manifest.Id,
            SourceFile = source.File,
            SourceIndex = source.Index
        };
        return result;
    }

    private void ApplyItemPatch(
        GameRes gameRes,
        Dictionary<string, JObject> resolved,
        PendingPatchDocument pending,
        ModPatchOperation operation,
        int operationIndex)
    {
        if (operation == null || string.IsNullOrWhiteSpace(operation.Target))
            throw new InvalidDataException($"MOD {pending.Package.Manifest.Id} Patch 目标为空：{pending.File}#{operationIndex}");

        if (!resolved.TryGetValue(operation.Target, out JObject target))
        {
            GameObject builtInPrefab = gameRes.GetPrefab(operation.Target, false);
            if (builtInPrefab == null)
            {
                if (operation.Optional)
                    return;
                throw new InvalidDataException($"Patch 找不到目标：{operation.Target}（{pending.File}#{operationIndex}）");
            }

            target = new JObject
            {
                ["id"] = operation.Target,
                ["basePrefab"] = operation.Target
            };
            resolved.Add(operation.Target, target);
            definitionInfos[operation.Target] = new ModDefinitionInfo
            {
                Id = operation.Target,
                DeclaringModId = "core",
                SourceFile = "built-in",
                SourceIndex = -1
            };
        }

        ApplyPatchOperation(target, operation, pending.File, operationIndex);
        ModDefinitionInfo info = GetOrCreateDefinitionInfo(operation.Target);
        info.LastModifiedBy = pending.Package.Manifest.Id;
        info.Patches.Add($"{pending.Package.Manifest.Id}:{pending.File}#{operationIndex}");
    }

    private ModPackage ResolveLastPatchOwner(string targetId)
    {
        if (!definitionInfos.TryGetValue(targetId, out ModDefinitionInfo info) || string.IsNullOrWhiteSpace(info.LastModifiedBy))
            return null;
        return packagesById.TryGetValue(info.LastModifiedBy, out ModPackage package) ? package : null;
    }

    private ModDefinitionInfo GetOrCreateDefinitionInfo(string id)
    {
        if (!definitionInfos.TryGetValue(id, out ModDefinitionInfo info))
        {
            info = new ModDefinitionInfo { Id = id };
            definitionInfos.Add(id, info);
        }
        return info;
    }

    private static void MergeDefinition(JObject target, JObject source)
    {
        foreach (JProperty property in source.Properties())
        {
            if (property.Name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("parent", StringComparison.OrdinalIgnoreCase))
                continue;
            target[property.Name] = property.Value.DeepClone();
        }
    }

    private static void ApplyPatchOperation(JObject target, ModPatchOperation operation, string file, int index)
    {
        string[] segments = NormalizePatchPath(operation.Path);
        if (segments.Length == 0)
            throw new InvalidDataException($"Patch 路径为空：{file}#{index}");

        JObject parent = target;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (parent[segments[i]] is not JObject child)
            {
                child = new JObject();
                parent[segments[i]] = child;
            }
            parent = child;
        }

        string propertyName = segments[^1];
        JToken current = parent[propertyName];
        JToken expected = operation.Expect;
        if (expected != null && !JToken.DeepEquals(current, expected))
            throw new InvalidDataException($"Patch expect 失败：{operation.Target}/{operation.Path}（{file}#{index}）");

        switch (operation.Operation?.Trim().ToLowerInvariant())
        {
            case "set":
                parent[propertyName] = operation.Value?.DeepClone() ?? JValue.CreateNull();
                break;
            case "replace":
                if (current == null)
                    throw new InvalidDataException($"Patch replace 找不到字段：{operation.Target}/{operation.Path}");
                parent[propertyName] = operation.Value?.DeepClone() ?? JValue.CreateNull();
                break;
            case "merge":
                if (operation.Value is not JObject mergeValue)
                    throw new InvalidDataException($"Patch merge 的 value 必须是对象：{file}#{index}");
                JObject mergeTarget = current as JObject ?? new JObject();
                mergeTarget.Merge(mergeValue, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Union });
                parent[propertyName] = mergeTarget;
                break;
            case "add":
            {
                JArray array = current as JArray ?? new JArray();
                if (operation.Value is JArray values)
                {
                    foreach (JToken value in values)
                        array.Add(value.DeepClone());
                }
                else
                {
                    array.Add(operation.Value?.DeepClone() ?? JValue.CreateNull());
                }
                parent[propertyName] = array;
                break;
            }
            case "remove":
                if (current is JArray removeArray && operation.Value != null)
                {
                    foreach (JToken match in removeArray.Where(value => JToken.DeepEquals(value, operation.Value)).ToList())
                        match.Remove();
                }
                else
                {
                    parent.Remove(propertyName);
                }
                break;
            case "test":
            {
                JToken testValue = operation.Expect ?? operation.Value;
                if (!JToken.DeepEquals(current, testValue))
                    throw new InvalidDataException($"Patch test 失败：{operation.Target}/{operation.Path}（{file}#{index}）");
                break;
            }
            default:
                throw new InvalidDataException($"不支持的 Patch 操作：{operation.Operation}（{file}#{index}）");
        }
    }

    private static string[] NormalizePatchPath(string path)
    {
        return (path ?? string.Empty)
            .Trim()
            .Trim('/')
            .Split(new[] { '/', '.' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private void InitializeLua(ModPackage package)
    {
        ModLuaRuntime runtime = new(package.Manifest.Id, package.RootPath, new ModApi(this, package.Manifest.Id));
        try
        {
            if (!string.IsNullOrWhiteSpace(package.Manifest.EntryLua))
                runtime.LoadMain(package.Manifest.EntryLua);
            luaRuntimes.Add(package.Manifest.Id, runtime);
        }
        catch
        {
            runtime.Dispose();
            throw;
        }
    }

    #endregion

    #region 内容注册

    private void RegisterBundleAsset(GameRes gameRes, ModPackage package, ModAssetDefinition definition)
    {
        ValidateContentId(package.Manifest.Id, definition.Id);
        if (!package.Bundles.TryGetValue(definition.Bundle, out AssetBundle bundle))
            throw new InvalidDataException($"MOD {package.Manifest.Id} 找不到 Bundle：{definition.Bundle}");

        Type assetType = ResolveAssetType(definition.Type);
        UnityEngine.Object asset = bundle.LoadAsset(definition.Asset, assetType);
        if (asset == null)
            throw new InvalidDataException($"MOD {package.Manifest.Id} 找不到资源：{definition.Asset}");

        switch (definition.Type.Trim().ToLowerInvariant())
        {
            case "prefab":
                RegisterPrefab(gameRes, package.Manifest.Id, definition.Id, (GameObject)asset);
                break;
            case "recipe":
                RegisterRecipe(gameRes, definition.Id, CloneAsset((Recipe)asset));
                break;
            case "tile":
                RegisterUnique(gameRes.tileBaseDict, definition.Id, CloneAsset((TileBase)asset));
                break;
            case "tileblock":
                Tile_Block tileBlock = CloneAsset((Tile_Block)asset);
                tileBlock.tileItemName = definition.Id;
                RegisterUnique(gameRes.TileBlockDict, definition.Id, tileBlock);
                break;
            case "buff":
                Buff_Data buff = CloneAsset((Buff_Data)asset);
                buff.buff_ID = definition.Id;
                RegisterUnique(gameRes.BuffData_Dict, definition.Id, buff);
                break;
            case "inventory":
                RegisterUnique(gameRes.InventoryInitDict, definition.Id, CloneAsset((Inventoryinit)asset));
                break;
            case "skill":
                BaseSkill skill = CloneAsset((BaseSkill)asset);
                skill.skillName = definition.Id;
                RegisterUnique(gameRes.SkillDict, definition.Id, skill);
                break;
            default:
                throw new InvalidDataException($"MOD {package.Manifest.Id} 使用了不支持的资源类型：{definition.Type}");
        }
    }

    private void RegisterJsonItem(
        GameRes gameRes,
        ModPackage package,
        ModItemDefinition definition,
        bool replaceExisting,
        bool validateNamespace)
    {
        if (validateNamespace)
            ValidateContentId(package.Manifest.Id, definition.Id);
        GameObject basePrefab = gameRes.GetPrefab(definition.BasePrefab, false);
        if (basePrefab == null)
            throw new InvalidDataException($"MOD {package.Manifest.Id} 的物品 {definition.Id} 找不到基础预制体：{definition.BasePrefab}");

        GameObject template = Instantiate(basePrefab, transform);
        template.name = definition.Id;
        template.SetActive(false);
        clonedAssets.Add(template);
        runtimeTemplates.Add(template);

        Item item = template.GetComponent<Item>();
        if (item == null || item.itemData == null)
            throw new InvalidDataException($"MOD {package.Manifest.Id} 的基础预制体不是有效物品：{definition.BasePrefab}");

        item.itemData.IDName = definition.Id;
        item.itemData.Guid = 0;
        if (!string.IsNullOrWhiteSpace(definition.GameName))
            item.itemData.GameName = definition.GameName;
        if (definition.Description != null)
            item.itemData.Description = definition.Description;
        if (definition.Durability.HasValue)
            item.itemData.Durability = definition.Durability.Value;
        if (definition.MaxDurability.HasValue)
            item.itemData.MaxDurability = definition.MaxDurability.Value;
        if (definition.Tags != null)
            item.itemData.Tags = new List<string>(definition.Tags);

        item.itemData.Stack ??= new ItemStack();
        if (definition.Amount.HasValue)
            item.itemData.Stack.Amount = definition.Amount.Value;
        if (definition.Volume.HasValue)
            item.itemData.Stack.Volume = definition.Volume.Value;
        if (definition.CanBePickedUp.HasValue)
            item.itemData.Stack.CanBePickedUp = definition.CanBePickedUp.Value;

        foreach (string moduleId in definition.Modules ?? Enumerable.Empty<string>())
        {
            GameObject modulePrefab = gameRes.GetPrefab(moduleId, false);
            if (modulePrefab == null || modulePrefab.GetComponentInChildren<Module>(true) == null)
                throw new InvalidDataException($"MOD {package.Manifest.Id} 的物品 {definition.Id} 找不到模块：{moduleId}");

            GameObject moduleObject = Instantiate(modulePrefab, template.transform);
            moduleObject.name = moduleId;
            Module module = moduleObject.GetComponentInChildren<Module>(true);
            module._Data.ID = moduleId;
            module._Data.Name = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(definition.SpriteAsset))
        {
            if (!package.Bundles.TryGetValue(definition.SpriteBundle, out AssetBundle spriteBundle))
                throw new InvalidDataException($"MOD {package.Manifest.Id} 找不到 Sprite Bundle：{definition.SpriteBundle}");

            Sprite sprite = spriteBundle.LoadAsset<Sprite>(definition.SpriteAsset);
            SpriteRenderer renderer = template.GetComponentInChildren<SpriteRenderer>(true);
            if (sprite == null || renderer == null)
                throw new InvalidDataException($"MOD {package.Manifest.Id} 无法为物品 {definition.Id} 应用 Sprite：{definition.SpriteAsset}");
            renderer.sprite = sprite;
        }

        if (replaceExisting)
            gameRes.AllPrefabs[definition.Id] = template;
        else
            RegisterUnique(gameRes.AllPrefabs, definition.Id, template);
        gameRes.LoadedCount++;
    }

    private void RegisterPrefab(GameRes gameRes, string modId, string contentId, GameObject prefab)
    {
        Item item = prefab.GetComponent<Item>();
        if (item != null && item.itemData != null)
            item.itemData.IDName = contentId;
        else
        {
            foreach (Module module in prefab.GetComponentsInChildren<Module>(true))
            {
                module._Data.ID = contentId;
                if (string.IsNullOrWhiteSpace(module._Data.Name))
                    module._Data.Name = contentId;
            }
        }

        RegisterUnique(gameRes.AllPrefabs, contentId, prefab);
        Debug.Log($"[MOD:{modId}] 已注册 Prefab：{contentId}");
    }

    private void RegisterRecipe(GameRes gameRes, string contentId, Recipe recipe)
    {
        RegisterUnique(gameRes.recipeDict, contentId, recipe);
        string inputKey = recipe.inputs?.ToString();
        if (!string.IsNullOrWhiteSpace(inputKey))
            RegisterUnique(gameRes.recipeDict, inputKey, recipe);
    }

    private T CloneAsset<T>(T asset) where T : UnityEngine.Object
    {
        T clone = Instantiate(asset);
        clonedAssets.Add(clone);
        return clone;
    }

    private static void RegisterUnique<T>(IDictionary<string, T> dictionary, string id, T value)
    {
        if (dictionary.ContainsKey(id))
            throw new InvalidDataException($"MOD 内容 ID 与已有内容冲突：{id}");
        dictionary.Add(id, value);
    }

    #endregion

    #region Lua 与存档

    public string InvokeItemLua(string modId, string scriptPath, string functionName, Item item, string state, float deltaTime = 0f)
    {
        if (!luaRuntimes.TryGetValue(modId ?? string.Empty, out ModLuaRuntime runtime))
        {
            Debug.LogWarning($"[ModRuntime] 找不到 Lua MOD：{modId}");
            return state;
        }

        try
        {
            return runtime.InvokeModule(scriptPath, functionName, item, state, deltaTime);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MOD:{modId}] Lua {functionName} 执行失败：{ex.Message}", item);
            return state;
        }
    }

    public ModSaveMetadata CaptureSaveMetadata()
    {
        if (State != ModLoadState.Ready)
            throw new InvalidOperationException("MOD 框架尚未完成加载，无法保存 MOD 元数据");

        foreach (KeyValuePair<string, ModLuaRuntime> pair in luaRuntimes)
        {
            string current = GetGlobalState(pair.Key);
            globalStates[pair.Key] = pair.Value.CaptureGlobalState(current);
        }

        return new ModSaveMetadata
        {
            ModSetHash = ModSetHash,
            Mods = loadedPackages.Select((package, index) => new ModSaveRecord
            {
                Id = package.Manifest.Id,
                Version = package.Manifest.Version,
                ContentHash = package.Manifest.ContentHash,
                LoadIndex = index
            }).ToList(),
            GlobalStates = new Dictionary<string, string>(globalStates, IdComparer)
        };
    }

    public bool ValidateSaveMetadata(ModSaveMetadata metadata, out string error)
    {
        error = null;
        if (metadata?.Mods == null || metadata.Mods.Count == 0)
            return true;

        List<ModSaveRecord> expected = metadata.Mods.OrderBy(record => record.LoadIndex).ToList();
        if (expected.Count != loadedPackages.Count)
        {
            error = $"存档需要 {expected.Count} 个 MOD，当前加载了 {loadedPackages.Count} 个";
            return false;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            ModManifest current = loadedPackages[i].Manifest;
            ModSaveRecord saved = expected[i];
            if (!string.Equals(saved.Id, current.Id, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(saved.Version, current.Version, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(saved.ContentHash, current.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                error = $"存档 MOD 不匹配：位置 {i + 1} 需要 {saved.Id} {saved.Version}，当前为 {current.Id} {current.Version}";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(metadata.ModSetHash) &&
            !string.Equals(metadata.ModSetHash, ModSetHash, StringComparison.OrdinalIgnoreCase))
        {
            error = "存档 MOD 集合哈希与当前环境不一致";
            return false;
        }

        return true;
    }

    public void RestoreSaveMetadata(ModSaveMetadata metadata)
    {
        globalStates.Clear();
        if (metadata?.GlobalStates != null)
        {
            foreach (KeyValuePair<string, string> pair in metadata.GlobalStates)
                globalStates[pair.Key] = pair.Value;
        }

        foreach (KeyValuePair<string, ModLuaRuntime> pair in luaRuntimes)
            pair.Value.RestoreGlobalState(GetGlobalState(pair.Key));
    }

    internal string GetGlobalState(string modId)
    {
        return globalStates.TryGetValue(modId ?? string.Empty, out string state) ? state : string.Empty;
    }

    internal void SetGlobalState(string modId, string json)
    {
        EnsureWorldMutationAllowed("SetGlobalState");
        if (!packagesById.ContainsKey(modId ?? string.Empty))
            throw new InvalidOperationException($"未加载 MOD：{modId}");
        globalStates[modId] = json ?? string.Empty;
    }

    public bool IsRuntimeTemplate(GameObject prefab)
    {
        return prefab != null && runtimeTemplates.Contains(prefab);
    }

    public string GetNetworkSummary()
    {
        string mods = string.Join(",", loadedPackages.Select(package =>
            $"{package.Manifest.Id}@{package.Manifest.Version}#{ShortHash(package.Manifest.ContentHash)}"));
        return $"{mods}|settings#{ShortHash(ModSettingsRegistry.ComputeAuthorityHash())}";
    }

    public bool IsModLoaded(string modId)
    {
        return packagesById.ContainsKey(modId ?? string.Empty);
    }

    public string GetModVersion(string modId)
    {
        return packagesById.TryGetValue(modId ?? string.Empty, out ModPackage package)
            ? package.Manifest.Version
            : string.Empty;
    }

    internal string GetDefinitionInfoJson(string contentId)
    {
        return definitionInfos.TryGetValue(contentId ?? string.Empty, out ModDefinitionInfo info)
            ? JsonConvert.SerializeObject(info)
            : string.Empty;
    }

    internal void EmitModEvent(string modId, string eventName, string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(eventName) || eventName.Length > 96 ||
            eventName.Any(character => !char.IsLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
        {
            throw new InvalidDataException($"MOD 事件名无效：{eventName}");
        }

        if ((payloadJson?.Length ?? 0) > 256 * 1024)
            throw new InvalidDataException("MOD 事件负载超过 256KB 限制");

        string normalizedPayload = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : JToken.Parse(payloadJson).ToString(Formatting.None);
        DispatchSerializedEvent($"mod.{modId}.{eventName}", normalizedPayload);
    }

    public void SetWorldMutationAuthority(bool allowed)
    {
        worldMutationAllowed = allowed;
    }

    internal void EnsureWorldMutationAllowed(string operation)
    {
        if (!worldMutationAllowed)
            throw new InvalidOperationException($"联网客户端 MOD 不允许执行世界修改：{operation}");
    }

    public List<InstalledModInfo> DiscoverInstalledMods()
    {
        Directory.CreateDirectory(ModsRootPath);
        ModProfile profile = ModProfileStore.LoadActiveProfile();
        List<InstalledModInfo> result = new();
        string[] directories = Directory.GetDirectories(ModsRootPath, "*", SearchOption.TopDirectoryOnly);
        Array.Sort(directories, StringComparer.OrdinalIgnoreCase);

        foreach (string directory in directories)
        {
            InstalledModInfo info = new() { FolderPath = directory, FolderName = Path.GetFileName(directory) };
            try
            {
                string manifestPath = Path.Combine(directory, ManifestFileName);
                if (!File.Exists(manifestPath))
                    throw new FileNotFoundException("缺少 manifest.json");

                ModManifest manifest = JsonConvert.DeserializeObject<ModManifest>(ReadLimitedText(manifestPath, MaximumJsonCharacters))
                    ?? throw new InvalidDataException("manifest.json 为空");
                info.Id = manifest.Id;
                info.Name = manifest.Name;
                info.Version = manifest.Version;
                info.Enabled = !File.Exists(Path.Combine(directory, ".disabled")) && profile.IsEnabled(manifest.Id);
                info.Loaded = packagesById.ContainsKey(manifest.Id ?? string.Empty);
                info.Valid = true;
            }
            catch (Exception ex)
            {
                info.Valid = false;
                info.Error = ex.Message;
            }
            result.Add(info);
        }

        return result;
    }

    public static string CalculatePackageHash(string packageRoot)
    {
        string manifestPath = Path.Combine(packageRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("MOD 包缺少 manifest.json", manifestPath);
        return ComputePackageHash(packageRoot, ReadLimitedText(manifestPath, MaximumJsonCharacters));
    }

    #endregion

    #region 游戏事件桥接

    private void BindGameEvents()
    {
        if (!staticEventsBound)
        {
            GameManager.Event_PlayerEnterWorld += OnPlayerEnteredWorld;
            ItemMgr.RuntimeItemInstantiated += OnItemSpawned;
            ItemMgr.RuntimeItemDespawning += OnItemDespawning;
            SceneManager.sceneLoaded += OnSceneLoaded;
            staticEventsBound = true;
        }

        if (boundGameManager == GameManager.Instance)
            return;

        if (boundGameManager != null)
        {
            boundGameManager.Event_GameWorldEnter -= OnWorldEntered;
            boundGameManager.Event_GameWorldExit -= OnWorldExiting;
        }

        boundGameManager = GameManager.Instance;
        if (boundGameManager != null)
        {
            boundGameManager.Event_GameWorldEnter += OnWorldEntered;
            boundGameManager.Event_GameWorldExit += OnWorldExiting;
        }
    }

    private void UnbindGameEvents()
    {
        if (staticEventsBound)
        {
            GameManager.Event_PlayerEnterWorld -= OnPlayerEnteredWorld;
            ItemMgr.RuntimeItemInstantiated -= OnItemSpawned;
            ItemMgr.RuntimeItemDespawning -= OnItemDespawning;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            staticEventsBound = false;
        }

        if (boundGameManager != null)
        {
            boundGameManager.Event_GameWorldEnter -= OnWorldEntered;
            boundGameManager.Event_GameWorldExit -= OnWorldExiting;
            boundGameManager = null;
        }
    }

    private void OnWorldEntered()
    {
        DispatchEvent("world.entered", new { scene = SceneManager.GetActiveScene().name });
    }

    private void OnWorldExiting()
    {
        DispatchEvent("world.exiting", new { scene = SceneManager.GetActiveScene().name });
    }

    private void OnPlayerEnteredWorld(Player player)
    {
        DispatchEvent("player.entered", CreateItemEventPayload(player));
    }

    private void OnItemSpawned(Item item)
    {
        DispatchEvent("item.spawned", CreateItemEventPayload(item));
    }

    private void OnItemDespawning(Item item)
    {
        DispatchEvent("item.despawning", CreateItemEventPayload(item));
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        DispatchEvent("scene.loaded", new { name = scene.name, buildIndex = scene.buildIndex, mode = mode.ToString() });
    }

    private static object CreateItemEventPayload(Item item)
    {
        Vector3 position = item != null ? item.transform.position : Vector3.zero;
        return new
        {
            id = item?.itemData?.IDName ?? string.Empty,
            guid = item?.itemData?.Guid ?? 0,
            x = position.x,
            y = position.y,
            z = position.z
        };
    }

    private void DispatchEvent(string eventName, object payload)
    {
        string payloadJson = JsonConvert.SerializeObject(payload ?? new { });
        DispatchSerializedEvent(eventName, payloadJson);
    }

    private void DispatchSerializedEvent(string eventName, string payloadJson)
    {
        foreach (ModPackage package in loadedPackages)
        {
            if (!luaRuntimes.TryGetValue(package.Manifest.Id, out ModLuaRuntime runtime))
                continue;

            try
            {
                runtime.InvokeEvent(eventName, payloadJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MOD:{package.Manifest.Id}] 事件 {eventName} 执行失败：{ex.Message}");
            }
        }
    }

    #endregion

    #region 校验与工具

    private static void ValidateManifest(ModManifest manifest, string packageRoot)
    {
        if (manifest.ApiVersion != SupportedApiVersion)
            throw new InvalidDataException($"MOD {manifest.Id ?? "<unknown>"} API 版本不受支持：{manifest.ApiVersion}，当前支持 {SupportedApiVersion}");
        if (string.IsNullOrWhiteSpace(manifest.Id) || !IsValidModId(manifest.Id))
            throw new InvalidDataException($"MOD ID 无效：{manifest.Id ?? "<null>"}，仅允许小写字母、数字、点、下划线和短横线");
        if (string.IsNullOrWhiteSpace(manifest.Version) || !TryParseVersion(manifest.Version, out _))
            throw new InvalidDataException($"MOD {manifest.Id} 版本无效：{manifest.Version}");

        ValidateVersionRange(Application.version, manifest.MinGameVersion, manifest.MaxGameVersion, $"MOD {manifest.Id} 的游戏版本");

        foreach (ModDependency dependency in manifest.Dependencies ?? Enumerable.Empty<ModDependency>())
        {
            if (dependency == null || string.IsNullOrWhiteSpace(dependency.Id) || !IsValidModId(dependency.Id))
                throw new InvalidDataException($"MOD {manifest.Id} 包含无效依赖 ID");
        }

        foreach (string conflictId in manifest.Conflicts ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(conflictId) || !IsValidModId(conflictId))
                throw new InvalidDataException($"MOD {manifest.Id} 包含无效冲突 ID：{conflictId}");
        }

        foreach (string path in manifest.DefinitionFiles ?? Enumerable.Empty<string>())
            ModPathUtility.ResolvePackagePath(packageRoot, path, true);
        foreach (string path in manifest.PatchFiles ?? Enumerable.Empty<string>())
            ModPathUtility.ResolvePackagePath(packageRoot, path, true);
        foreach (string path in manifest.LocalizationFiles ?? Enumerable.Empty<string>())
            ModPathUtility.ResolvePackagePath(packageRoot, path, true);
        foreach (ModBundleDefinition bundle in manifest.Bundles ?? Enumerable.Empty<ModBundleDefinition>())
            ModPathUtility.ResolvePackagePath(packageRoot, bundle.Path, true);
        if (!string.IsNullOrWhiteSpace(manifest.SettingsFile))
            ModPathUtility.ResolvePackagePath(packageRoot, manifest.SettingsFile, true);
        if (!string.IsNullOrWhiteSpace(manifest.EntryLua))
            ModPathUtility.ResolvePackagePath(packageRoot, manifest.EntryLua, true);
    }

    private static void ValidateContentId(string modId, string contentId)
    {
        string prefix = modId + ":";
        if (string.IsNullOrWhiteSpace(contentId) ||
            !contentId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            contentId.Length == prefix.Length)
        {
            throw new InvalidDataException($"MOD 内容 ID 必须使用命名空间 {prefix}，实际为：{contentId}");
        }
    }

    private static bool IsValidModId(string id)
    {
        if (id.Length > 96)
            return false;

        foreach (char character in id)
        {
            if ((character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9') ||
                character == '.' || character == '_' || character == '-')
                continue;
            return false;
        }

        return true;
    }

    private static void ValidateVersionRange(string actualVersion, string minimumVersion, string maximumVersion, string context)
    {
        if (!TryParseVersion(actualVersion, out Version actual))
            return;

        if (!string.IsNullOrWhiteSpace(minimumVersion) &&
            TryParseVersion(minimumVersion, out Version minimum) && actual < minimum)
            throw new InvalidDataException($"{context}版本过低，需要 >= {minimumVersion}，当前为 {actualVersion}");

        if (!string.IsNullOrWhiteSpace(maximumVersion) &&
            TryParseVersion(maximumVersion, out Version maximum) && actual > maximum)
            throw new InvalidDataException($"{context}版本过高，需要 <= {maximumVersion}，当前为 {actualVersion}");
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string numeric = new string(value.Trim().TakeWhile(character => char.IsDigit(character) || character == '.').ToArray());
        int partCount = numeric.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).Length;
        if (partCount == 1)
            numeric += ".0";
        return Version.TryParse(numeric, out version);
    }

    private static void AddOrderEdge(
        string before,
        string after,
        Dictionary<string, HashSet<string>> dependencies,
        Dictionary<string, HashSet<string>> dependents)
    {
        if (IdComparer.Equals(before, after))
            throw new InvalidDataException($"MOD 排序规则自引用：{before}");
        if (dependencies[after].Add(before))
            dependents[before].Add(after);
    }

    private static Type ResolveAssetType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "prefab" => typeof(GameObject),
            "recipe" => typeof(Recipe),
            "tile" => typeof(TileBase),
            "tileblock" => typeof(Tile_Block),
            "buff" => typeof(Buff_Data),
            "inventory" => typeof(Inventoryinit),
            "skill" => typeof(BaseSkill),
            _ => throw new InvalidDataException($"不支持的 MOD 资源类型：{type}")
        };
    }

    private static bool IsCurrentPlatform(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform) || platform.Equals("Any", StringComparison.OrdinalIgnoreCase))
            return true;
        return platform.Equals(Application.platform.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static float CalculateProgress(int index, int count, float start, float length)
    {
        return count <= 0 ? start + length : start + length * index / count;
    }

    private static string ReadLimitedText(string path, int maximumCharacters)
    {
        FileInfo info = new(path);
        if (info.Length > maximumCharacters * 4L)
            throw new InvalidDataException($"JSON 文件过大：{path}");

        string text = File.ReadAllText(path, Encoding.UTF8);
        if (text.Length > maximumCharacters)
            throw new InvalidDataException($"JSON 文件超过字符限制：{path}");
        return text;
    }

    private static string ComputePackageHash(string packageRoot, string manifestJson)
    {
        List<string> files = EnumerateSafePackageFiles(packageRoot);

        JObject canonicalManifest = JObject.Parse(manifestJson);
        canonicalManifest["contentHash"] = string.Empty;

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashText(hash, ManifestFileName);
        AppendHashText(hash, canonicalManifest.ToString(Formatting.None));

        foreach (string file in files
                     .Where(file => !Path.GetFileName(file).Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(file => Path.GetRelativePath(packageRoot, file).Replace('\\', '/'), StringComparer.Ordinal))
        {
            ValidateNoReparsePoints(packageRoot, file);
            string relativePath = Path.GetRelativePath(packageRoot, file).Replace('\\', '/');
            AppendHashText(hash, relativePath);

            using FileStream stream = File.OpenRead(file);
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
        }

        return ToLowerHex(hash.GetHashAndReset());
    }

    private static List<string> EnumerateSafePackageFiles(string packageRoot)
    {
        List<string> files = new();
        Stack<string> pendingDirectories = new();
        pendingDirectories.Push(Path.GetFullPath(packageRoot));
        long totalBytes = 0L;

        while (pendingDirectories.Count > 0)
        {
            string directory = pendingDirectories.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"MOD 不允许使用符号链接或目录联接：{directory}");

            foreach (string childDirectory in Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"MOD 不允许使用符号链接或目录联接：{childDirectory}");
                pendingDirectories.Push(childDirectory);
            }

            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"MOD 不允许使用符号链接：{file}");

                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension is ".dll" or ".exe" or ".com" or ".scr" or ".msi" or
                    ".bat" or ".cmd" or ".ps1" or ".vbs" or ".js" or ".cs")
                {
                    throw new InvalidDataException($"MOD 包包含禁止的可执行文件：{file}");
                }

                files.Add(file);
                if (files.Count > MaximumPackageFileCount)
                    throw new InvalidDataException($"MOD 文件数量超过限制：{packageRoot}");

                totalBytes += new FileInfo(file).Length;
                if (totalBytes > MaximumPackageBytes)
                    throw new InvalidDataException($"MOD 包体积超过 1GB 限制：{packageRoot}");
            }
        }

        return files;
    }

    private static string ComputeModSetHash(IEnumerable<ModPackage> packages)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (ModPackage package in packages)
        {
            AppendHashText(hash, package.Manifest.Id);
            AppendHashText(hash, package.Manifest.Version);
            AppendHashText(hash, package.Manifest.ContentHash);
        }
        AppendHashText(hash, ModSettingsRegistry.ComputeAuthorityHash());
        return ToLowerHex(hash.GetHashAndReset());
    }

    private static string ShortHash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Substring(0, Math.Min(12, value.Length));
    }

    private static string ToLowerHex(byte[] bytes)
    {
        StringBuilder builder = new(bytes.Length * 2);
        for (int i = 0; i < bytes.Length; i++)
            builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private static void AppendHashText(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        byte[] length = BitConverter.GetBytes(bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void ValidateNoReparsePoints(string rootPath, string targetPath)
    {
        string root = Path.GetFullPath(rootPath);
        string target = Path.GetFullPath(targetPath);
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"MOD 路径越界：{targetPath}");

        string current = target;
        while (!string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"MOD 不允许使用符号链接或目录联接：{targetPath}");
            }
            current = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(current))
                break;
        }
    }

    private void UnloadAll(bool keepFailureState = false)
    {
        UnbindGameEvents();
        foreach (ModLuaRuntime runtime in luaRuntimes.Values)
            runtime.Dispose();
        luaRuntimes.Clear();

        foreach (UnityEngine.Object asset in clonedAssets)
        {
            if (asset != null)
                Destroy(asset);
        }
        clonedAssets.Clear();
        runtimeTemplates.Clear();

        for (int i = loadedBundles.Count - 1; i >= 0; i--)
        {
            if (loadedBundles[i] != null)
                loadedBundles[i].Unload(false);
        }
        loadedBundles.Clear();
        loadedPackages.Clear();
        packagesById.Clear();
        globalStates.Clear();
        pendingItemDefinitions.Clear();
        pendingPatchDocuments.Clear();
        definitionInfos.Clear();
        ModLocalizationRegistry.Clear();
        ModSettingsRegistry.Clear();
        activeProfile = null;
        safeModeActive = false;
        ModSetHash = string.Empty;

        if (!keepFailureState)
        {
            State = ModLoadState.NotStarted;
            FailureReason = null;
        }
    }

    #endregion

    private sealed class ModPackage
    {
        public ModPackage(string rootPath, ModManifest manifest)
        {
            RootPath = rootPath;
            Manifest = manifest;
        }

        public string RootPath { get; }
        public ModManifest Manifest { get; }
        public Dictionary<string, AssetBundle> Bundles { get; } = new(IdComparer);
    }

    private sealed class PendingItemDefinition
    {
        public PendingItemDefinition(ModPackage package, string file, int index, JObject document)
        {
            Package = package;
            File = file;
            Index = index;
            Document = document;
        }

        public ModPackage Package { get; }
        public string File { get; }
        public int Index { get; }
        public JObject Document { get; }
    }

    private sealed class PendingPatchDocument
    {
        public PendingPatchDocument(ModPackage package, string file, ModPatchDocument document)
        {
            Package = package;
            File = file;
            Document = document;
        }

        public ModPackage Package { get; }
        public string File { get; }
        public ModPatchDocument Document { get; }
    }
}

public sealed class ModDefinitionInfo
{
    public string Id;
    public string DeclaringModId;
    public string LastModifiedBy;
    public string SourceFile;
    public int SourceIndex;
    public bool Materialized;
    public bool ReplacedBuiltInContent;
    public List<string> Patches = new();
}

public sealed class InstalledModInfo
{
    public string Id;
    public string Name;
    public string Version;
    public string FolderName;
    public string FolderPath;
    public bool Enabled;
    public bool Loaded;
    public bool Valid;
    public string Error;
}

internal static class ModPathUtility
{
    public static string ResolvePackagePath(string packageRoot, string relativePath, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"MOD 路径必须是包内相对路径：{relativePath}");

        string root = Path.GetFullPath(packageRoot);
        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"MOD 路径越界：{relativePath}");
        if (mustExist && !File.Exists(fullPath))
            throw new FileNotFoundException($"MOD 文件不存在：{relativePath}", fullPath);
        return fullPath;
    }
}
