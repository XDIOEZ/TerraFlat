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
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>从 StreamingAssets 加载本体 ItemDefinition，并绑定少量外壳 Prefab。</summary>
public static class ItemDefinitionCatalogLoader
{
    public const int SupportedSchemaVersion = 1;
    public const string RelativeItemRoot = "GameConfig/Items";
    public const string ManifestFileName = "item-manifest.json";
    public const string RelativeManifestPath = RelativeItemRoot + "/" + ManifestFileName;

    public static string BuiltInItemRoot =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeItemRoot);

    public static string BuiltInManifestPath =>
        StreamingAssetsTextLoader.CombinePath(BuiltInItemRoot, ManifestFileName);

    private static readonly JsonSerializerSettings TemplateSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    #region Manifest 与分包加载

    public static int LoadBuiltIn(GameRes gameRes)
    {
        if (gameRes == null)
            throw new ArgumentNullException(nameof(gameRes));

        List<ItemDefinitionDto> dtos = LoadBuiltInDefinitions();
        var definitions = new List<RuntimeItemDefinition>(dtos.Count);

        // 先完整构建，避免失败时只注册了一半目录。
        foreach (ItemDefinitionDto dto in dtos)
        {
            if (!dto.Abstract)
                definitions.Add(BuildRuntimeDefinition(gameRes, dto));
        }

        foreach (RuntimeItemDefinition definition in definitions)
            gameRes.RegisterItemDefinition(definition);

        Debug.Log($"[ItemDefinitionCatalog] 已从 Manifest 加载 {definitions.Count} 个物品：{BuiltInManifestPath}");
        return definitions.Count;
    }

    /// <summary>同步读取本体 Manifest，并在所有启用分包合并后统一解析继承。</summary>
    public static List<ItemDefinitionDto> LoadBuiltInDefinitions()
    {
        return ResolveLoadedPackages(ReadBuiltInPackages());
    }

    /// <summary>返回合并后的原始定义，供迁移工具保留 parent 与手工覆盖字段。</summary>
    public static JObject LoadBuiltInSourceCatalog()
    {
        return CreateCombinedCatalog(ReadBuiltInPackages().Select(package => package.Root));
    }

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

        ItemDefinitionManifestDto manifest;
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

        ItemDefinitionPackageDto[] enabledPackages = manifest.Packages
            .Where(package => package.Enabled)
            .ToArray();
        var loadedPackages = new List<LoadedItemPackage>(enabledPackages.Length);
        for (int i = 0; i < enabledPackages.Length; i++)
        {
            ItemDefinitionPackageDto package = enabledPackages[i];
            string packagePath;
            try
            {
                packagePath = ResolvePackagePath(BuiltInItemRoot, package.Path);
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
                failed?.Invoke(new IOException(
                    $"物品分包 {package.Id} 读取失败：{packagePath}",
                    readError));
                yield break;
            }

            try
            {
                loadedPackages.Add(new LoadedItemPackage(package, packageJson));
            }
            catch (Exception exception)
            {
                failed?.Invoke(exception);
                yield break;
            }

            progress?.Invoke(0.2f * (i + 1) / enabledPackages.Length);
        }

        List<ItemDefinitionDto> dtos;
        try
        {
            dtos = ResolveLoadedPackages(loadedPackages);
        }
        catch (Exception exception)
        {
            failed?.Invoke(exception);
            yield break;
        }

        // JSON 外壳由定义目录显式预加载，避免通用 Prefab 别名的加载顺序决定 shellPrefab 结果。
        var shellAddresses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto dto in dtos)
        {
            if (dto.Abstract || string.IsNullOrWhiteSpace(dto.ShellPrefab) ||
                string.IsNullOrWhiteSpace(dto.SourcePrefab))
            {
                continue;
            }

            string shellId = dto.ShellPrefab.Trim();
            string sourcePath = dto.SourcePrefab.Trim().Replace('\\', '/');
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(sourcePath),
                    shellId,
                    StringComparison.OrdinalIgnoreCase))
            {
                shellAddresses.TryAdd(shellId, sourcePath);
            }
        }

#if UNITY_EDITOR
        // Fast Mode 下只读取已经导入的 Prefab。Play Mode 内强制重新导入会触发
        // PrefabImporter 不一致警告，也会让资源目录初始化产生额外抖动。
        foreach (KeyValuePair<string, string> pair in shellAddresses)
        {
            GameObject shell = AssetDatabase.LoadAssetAtPath<GameObject>(pair.Value);
            Item shellItem = shell != null ? shell.GetComponent<Item>() : null;
            if (shellItem?.itemData == null)
            {
                string componentTypes = shell == null
                    ? "<none>"
                    : string.Join(",", shell.GetComponents<Component>()
                        .Where(component => component != null)
                        .Select(component => component.GetType().Name));
                failed?.Invoke(new InvalidDataException(
                    $"内置物品外壳 Prefab 无效：{pair.Key} → {pair.Value}；" +
                    $"result={shell?.name ?? "<null>"}，" +
                    $"components={componentTypes}，item={shellItem?.GetType().Name ?? "<null>"}，" +
                    $"itemData={(shellItem?.itemData == null ? "null" : shellItem.itemData.IDName)}"));
                yield break;
            }
            gameRes.RegisterPrefabAlias(pair.Key, shell);
        }
#else
        var shellHandles = new Dictionary<string, AsyncOperationHandle<GameObject>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> pair in shellAddresses)
            shellHandles[pair.Key] = Addressables.LoadAssetAsync<GameObject>(pair.Value);

        while (shellHandles.Values.Any(handle => !handle.IsDone))
            yield return null;

        foreach (KeyValuePair<string, AsyncOperationHandle<GameObject>> pair in shellHandles)
        {
            GameObject shell = pair.Value.Result;
            Item shellItem = shell != null ? shell.GetComponent<Item>() : null;
            if (pair.Value.Status != AsyncOperationStatus.Succeeded || shellItem?.itemData == null)
            {
                string componentTypes = shell == null
                    ? "<none>"
                    : string.Join(",", shell.GetComponents<Component>()
                        .Where(component => component != null)
                        .Select(component => component.GetType().Name));
                failed?.Invoke(new InvalidDataException(
                    $"物品外壳 Addressable 无效：{pair.Key} → {shellAddresses[pair.Key]}；" +
                    $"status={pair.Value.Status}，result={shell?.name ?? "<null>"}，" +
                    $"components={componentTypes}，item={shellItem?.GetType().Name ?? "<null>"}，" +
                    $"itemData={(shellItem?.itemData == null ? "null" : shellItem.itemData.IDName)}"));
                yield break;
            }
            gameRes.RegisterPrefabAlias(pair.Key, shell);
        }
#endif

        string[] addresses = dtos
            .Where(dto => !dto.Abstract && !string.IsNullOrWhiteSpace(dto.Visual?.SpriteAddress))
            .Select(dto => dto.Visual.SpriteAddress.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
#if UNITY_EDITOR
        // Fast Mode 下直接读取 AssetDatabase，绕过 Addressables 1.22.3 的子资源空引用缺陷。
        foreach (string address in addresses)
        {
            if (!TryLoadEditorSprite(address, out Sprite sprite, out string error))
            {
                failed?.Invoke(new InvalidDataException(error));
                yield break;
            }

            sprites[address] = sprite;
        }
#else
        var handles = new Dictionary<string, AsyncOperationHandle<Sprite>>(StringComparer.OrdinalIgnoreCase);
        foreach (string address in addresses)
            handles[address] = Addressables.LoadAssetAsync<Sprite>(address);

        while (handles.Values.Any(handle => !handle.IsDone))
        {
            int done = handles.Values.Count(handle => handle.IsDone);
            progress?.Invoke(addresses.Length == 0 ? 0.9f : 0.2f + 0.7f * done / addresses.Length);
            yield return null;
        }

        foreach (KeyValuePair<string, AsyncOperationHandle<Sprite>> pair in handles)
        {
            if (pair.Value.Status != AsyncOperationStatus.Succeeded || pair.Value.Result == null)
            {
                failed?.Invoke(new InvalidDataException($"找不到物品 Sprite Addressable：{pair.Key}"));
                yield break;
            }
            sprites[pair.Key] = pair.Value.Result;
        }
#endif

        var definitions = new List<RuntimeItemDefinition>(dtos.Count);
        try
        {
            foreach (ItemDefinitionDto dto in dtos)
            {
                if (!dto.Abstract)
                    definitions.Add(BuildRuntimeDefinition(gameRes, dto, sprites));
            }
            foreach (RuntimeItemDefinition definition in definitions)
                gameRes.RegisterItemDefinition(definition);
        }
        catch (Exception exception)
        {
            failed?.Invoke(exception);
            yield break;
        }

        progress?.Invoke(1f);
        Debug.Log($"[ItemDefinitionCatalog] 已从 {enabledPackages.Length} 个分包异步加载 " +
                  $"{definitions.Count} 个物品：{BuiltInManifestPath}");
        completed?.Invoke(definitions.Count);
    }

    /// <summary>
    /// 返回已经完整迁入 JSON、且不是任何运行时外壳/模块 Prefab 的旧资源路径。
    /// GameRes 用它在 Addressables 定位阶段直接跳过冗余变体，避免先加载再覆盖。
    /// </summary>
    public static HashSet<string> GetRedundantBuiltInPrefabPaths()
    {
        // APK/JAR 内的 StreamingAssets 不能同步读取；此时只是不做旧 Prefab 过滤，
        // 后续异步 Manifest 加载仍会建立权威定义。
        if (StreamingAssetsTextLoader.RequiresWebRequest(BuiltInManifestPath) ||
            !File.Exists(BuiltInManifestPath))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        List<ItemDefinitionDto> definitions = LoadBuiltInDefinitions();
        var requiredPrefabIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in definitions)
        {
            if (!string.IsNullOrWhiteSpace(definition.ShellPrefab))
                requiredPrefabIds.Add(definition.ShellPrefab.Trim());
            foreach (ItemModuleDefinitionDto module in definition.Modules?.Values ?? Enumerable.Empty<ItemModuleDefinitionDto>())
            {
                if (!string.IsNullOrWhiteSpace(module?.Prefab))
                    requiredPrefabIds.Add(module.Prefab.Trim());
            }
        }

        var redundant = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in definitions)
        {
            if (definition.Abstract || string.IsNullOrWhiteSpace(definition.SourcePrefab))
                continue;
            string path = definition.SourcePrefab.Trim().Replace('\\', '/');
            string prefabId = Path.GetFileNameWithoutExtension(path);
            if (!requiredPrefabIds.Contains(prefabId))
                redundant.Add(path);
        }
        return redundant;
    }

    public static ItemDefinitionManifestDto DeserializeManifest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("物品分包清单为空");
        return JsonConvert.DeserializeObject<ItemDefinitionManifestDto>(json);
    }

    public static void ValidateManifest(ItemDefinitionManifestDto manifest)
    {
        if (manifest == null)
            throw new InvalidDataException("物品分包清单根对象为空");
        if (manifest.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的物品清单 schemaVersion：{manifest.SchemaVersion}");
        if (manifest.Packages == null || manifest.Packages.Count == 0)
            throw new InvalidDataException("物品分包清单没有 packages");

        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasEnabledPackage = false;
        foreach (ItemDefinitionPackageDto package in manifest.Packages)
        {
            if (package == null)
                throw new InvalidDataException("物品分包清单包含空分包定义");
            if (string.IsNullOrWhiteSpace(package.Id))
                throw new InvalidDataException("物品分包清单包含空分包 ID");
            if (string.IsNullOrWhiteSpace(package.Path))
                throw new InvalidDataException($"物品分包 {package.Id} 缺少 path");
            if (!packageIds.Add(package.Id.Trim()))
                throw new InvalidDataException($"物品分包清单包含重复 ID：{package.Id}");
            if (!packagePaths.Add(package.Path.Trim().Replace('\\', '/')))
                throw new InvalidDataException($"物品分包清单包含重复路径：{package.Path}");
            hasEnabledPackage |= package.Enabled;
        }

        if (!hasEnabledPackage)
            throw new InvalidDataException("物品分包清单没有启用的 packages");
    }

    public static string ResolvePackagePath(string itemRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(itemRoot))
            throw new ArgumentException("物品配置根目录不能为空", nameof(itemRoot));

        // CombinePath 会统一拒绝绝对路径、空段、.、.. 与盘符/ADS 冒号。
        string combinedPath = StreamingAssetsTextLoader.CombinePath(itemRoot, relativePath);

        if (StreamingAssetsTextLoader.RequiresWebRequest(itemRoot))
            return combinedPath;

        string normalizedRoot = Path.GetFullPath(itemRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(combinedPath);
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"物品分包路径越出 Items 目录：{relativePath}");
        return fullPath;
    }

    private static List<LoadedItemPackage> ReadBuiltInPackages()
    {
        ItemDefinitionManifestDto manifest = DeserializeManifest(
            StreamingAssetsTextLoader.ReadAllText(BuiltInManifestPath));
        ValidateManifest(manifest);

        var packages = new List<LoadedItemPackage>();
        foreach (ItemDefinitionPackageDto package in manifest.Packages.Where(package => package.Enabled))
        {
            string packagePath = ResolvePackagePath(BuiltInItemRoot, package.Path);
            packages.Add(new LoadedItemPackage(
                package,
                StreamingAssetsTextLoader.ReadAllText(packagePath)));
        }
        return packages;
    }

    private static List<ItemDefinitionDto> ResolveLoadedPackages(
        IReadOnlyCollection<LoadedItemPackage> packages)
    {
        JObject combined = CreateCombinedCatalog(packages.Select(package => package.Root));
        List<ItemDefinitionDto> definitions = ResolveDefinitions(combined);
        var resolvedById = definitions.ToDictionary(
            definition => definition.Id,
            StringComparer.OrdinalIgnoreCase);

        foreach (LoadedItemPackage package in packages)
        {
            string expectedShell = package.Definition.ShellPrefab?.Trim();
            if (string.IsNullOrWhiteSpace(expectedShell))
                continue;

            foreach (JObject source in package.Items.OfType<JObject>())
            {
                string id = source.Value<string>("id")?.Trim();
                if (!resolvedById.TryGetValue(id ?? string.Empty, out ItemDefinitionDto resolved) ||
                    !string.Equals(resolved.ShellPrefab?.Trim(), expectedShell, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"物品 {id} 解析出的 shellPrefab 与分包 {package.Definition.Id} 不一致；" +
                        $"expected={expectedShell}, actual={resolved?.ShellPrefab}");
                }
            }
        }

        return definitions;
    }

    private static JObject CreateCombinedCatalog(IEnumerable<JObject> roots)
    {
        var items = new JArray();
        int rootCount = 0;
        foreach (JObject root in roots ?? throw new ArgumentNullException(nameof(roots)))
        {
            rootCount++;
            foreach (JToken item in (JArray)root["items"])
                items.Add(item.DeepClone());
        }

        if (rootCount == 0)
            throw new InvalidDataException("没有可加载的物品分包");
        return new JObject
        {
            ["schemaVersion"] = SupportedSchemaVersion,
            ["items"] = items
        };
    }

    private static JObject ParseCatalogRoot(string json, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException($"物品分包为空：{sourceName}");

        JObject root = JObject.Parse(json);
        int schemaVersion = root.Value<int?>("schemaVersion") ?? 0;
        if (schemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"物品分包 {sourceName} 的 schemaVersion 不受支持：{schemaVersion}");
        if (root["items"] is not JArray items)
            throw new InvalidDataException($"物品分包 {sourceName} 缺少 items 数组");
        foreach (JToken item in items)
        {
            if (item is not JObject source || string.IsNullOrWhiteSpace(source.Value<string>("id")))
                throw new InvalidDataException($"物品分包 {sourceName} 包含非对象或空 ID 定义");
        }
        return root;
    }

    private sealed class LoadedItemPackage
    {
        public ItemDefinitionPackageDto Definition { get; }
        public JObject Root { get; }
        public JArray Items => (JArray)Root["items"];

        public LoadedItemPackage(ItemDefinitionPackageDto definition, string json)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Root = ParseCatalogRoot(json, definition.Id);
        }
    }

    #endregion

#if UNITY_EDITOR
    #region 编辑器 Sprite 解析

    /// <summary>
    /// 编辑器 Fast Mode 直接解析 Sprite，避免 Addressables 1.22.3 的 AssetDatabaseProvider 子资源空引用。
    /// </summary>
    public static bool TryLoadEditorSprite(string address, out Sprite sprite, out string error)
    {
        sprite = null;
        error = null;

        string key = address?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            error = "Sprite Addressable 不能为空。";
            return false;
        }

        string assetKey = key;
        string subObjectName = null;
        int closeBracket = key.LastIndexOf(']');
        int openBracket = closeBracket > 0 ? key.LastIndexOf('[', closeBracket) : -1;
        if (openBracket > 0 && closeBracket == key.Length - 1)
        {
            assetKey = key.Substring(0, openBracket);
            subObjectName = key.Substring(openBracket + 1, closeBracket - openBracket - 1);
        }

        string assetPath = assetKey.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
            ? assetKey.Replace('\\', '/')
            : null;
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            error = $"Sprite Addressable '{key}' 不是可直接读取的 Assets 路径。";
            return false;
        }

        UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (mainAsset == null)
        {
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        }

        if (mainAsset == null)
        {
            error = $"Sprite Addressable '{key}' 的主资源无法加载：{assetPath}。";
            return false;
        }

        if (string.IsNullOrEmpty(subObjectName))
        {
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }
        else
        {
            Sprite mainSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (mainSprite != null && string.Equals(mainSprite.name, subObjectName, StringComparison.Ordinal))
                sprite = mainSprite;

            if (sprite == null)
            {
                foreach (UnityEngine.Object representation in
                         AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
                {
                    if (representation is Sprite candidate &&
                        string.Equals(candidate.name, subObjectName, StringComparison.Ordinal))
                    {
                        sprite = candidate;
                        break;
                    }
                }
            }
        }

        if (sprite == null)
        {
            error = $"Sprite Addressable '{key}' 找不到子资源：{assetPath}[{subObjectName}]。";
            return false;
        }

        return true;
    }

    #endregion
#endif

    #region 定义继承

    public static List<ItemDefinitionDto> ResolveDefinitions(string json)
    {
        return ResolveDefinitions(ParseCatalogRoot(json, "内存目录"));
    }

    /// <summary>先合并全部分包，再解析跨文件 parent 继承。</summary>
    public static List<ItemDefinitionDto> ResolveDefinitions(IEnumerable<string> catalogJsons)
    {
        if (catalogJsons == null)
            throw new ArgumentNullException(nameof(catalogJsons));

        var roots = new List<JObject>();
        int index = 0;
        foreach (string json in catalogJsons)
        {
            roots.Add(ParseCatalogRoot(json, $"内存分包[{index}]"));
            index++;
        }
        return ResolveDefinitions(CreateCombinedCatalog(roots));
    }

    private static List<ItemDefinitionDto> ResolveDefinitions(JObject root)
    {
        return ResolveDefinitionObjects(root)
            .Select(source => source.ToObject<ItemDefinitionDto>() ??
                              throw new InvalidDataException(
                                  $"无法解析物品定义：{source.Value<string>("id")}"))
            .ToList();
    }

    /// <summary>返回继承合并后的原始 JSON，Actor/MOD 可继续继承且不会反射 Unity 结构体属性。</summary>
    internal static List<JObject> ResolveDefinitionObjects(IEnumerable<string> catalogJsons)
    {
        if (catalogJsons == null)
            throw new ArgumentNullException(nameof(catalogJsons));

        var roots = new List<JObject>();
        int index = 0;
        foreach (string json in catalogJsons)
        {
            roots.Add(ParseCatalogRoot(json, $"内存分包[{index}]"));
            index++;
        }
        return ResolveDefinitionObjects(CreateCombinedCatalog(roots));
    }

    private static List<JObject> ResolveDefinitionObjects(JObject root)
    {
        JArray items = (JArray)root["items"];

        var sources = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        foreach (JToken token in items)
        {
            if (token is not JObject source)
                throw new InvalidDataException("items 中包含非对象定义");
            string id = source.Value<string>("id")?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("物品定义包含空 ID");
            if (!sources.TryAdd(id, (JObject)source.DeepClone()))
                throw new InvalidDataException($"重复物品 ID：{id}");
        }

        var resolved = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        var resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in sources.Keys)
            ResolveOne(id, sources, resolved, resolving);

        return sources.Keys
            .Select(id => (JObject)resolved[id].DeepClone())
            .ToList();
    }

    private static JObject ResolveOne(
        string id,
        Dictionary<string, JObject> sources,
        Dictionary<string, JObject> resolved,
        HashSet<string> resolving)
    {
        if (resolved.TryGetValue(id, out JObject cached))
            return cached;
        if (!resolving.Add(id))
            throw new InvalidDataException($"物品定义继承存在循环：{id}");

        JObject source = sources[id];
        JObject result = new();
        string parentId = source.Value<string>("parent")?.Trim();
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            if (!sources.ContainsKey(parentId))
                throw new InvalidDataException($"物品 {id} 找不到 parent：{parentId}");
            result = (JObject)ResolveOne(parentId, sources, resolved, resolving).DeepClone();
        }

        result.Merge(source, new JsonMergeSettings
        {
            MergeArrayHandling = MergeArrayHandling.Replace,
            MergeNullValueHandling = MergeNullValueHandling.Merge
        });
        result["id"] = id;
        result["abstract"] = source.Value<bool?>("abstract") ?? false;
        result.Remove("parent");

        resolving.Remove(id);
        resolved.Add(id, result);
        return result;
    }

    #endregion

    internal static RuntimeItemDefinition BuildRuntimeDefinition(
        GameRes gameRes,
        ItemDefinitionDto dto,
        IReadOnlyDictionary<string, Sprite> preloadedSprites = null,
        IReadOnlyDictionary<string, RuntimeAnimatorController> preloadedControllers = null,
        bool isActor = false,
        IReadOnlyDictionary<string, GameObject> preloadedShells = null)
    {
        string id = dto.Id?.Trim();
        string shellId = dto.ShellPrefab?.Trim();
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("物品定义 ID 为空");
        if (string.IsNullOrWhiteSpace(shellId))
            throw new InvalidDataException($"物品 {id} 缺少 shellPrefab");

        GameObject shell;
        if (preloadedShells != null)
        {
            if (!preloadedShells.TryGetValue(shellId, out shell) || shell == null)
                throw new InvalidDataException($"物品 {id} 找不到预加载外壳：{shellId}");
        }
        else
        {
            shell = gameRes.GetPrefab(shellId, false);
        }
        Item shellItem = shell != null ? shell.GetComponent<Item>() : null;
        if (shellItem?.itemData == null)
            throw new InvalidDataException($"物品 {id} 的外壳不是有效 Item：{shellId}");

        ItemData template = FastCloner.FastCloner.DeepClone(shellItem.itemData);
        PopulateTemplateData(dto.ItemData, template, id);
        template.IDName = id;
        template.Guid = 0;
        if (!string.IsNullOrWhiteSpace(dto.GameName)) template.GameName = dto.GameName;
        if (dto.Description != null) template.Description = dto.Description;
        if (dto.Durability.HasValue) template.Durability = dto.Durability.Value;
        if (dto.MaxDurability.HasValue) template.MaxDurability = dto.MaxDurability.Value;
        if (dto.Tags != null) template.Tags = new List<string>(dto.Tags);

        template.Stack ??= new ItemStack();
        if (dto.Amount.HasValue) template.Stack.Amount = dto.Amount.Value;
        if (dto.Volume.HasValue) template.Stack.Volume = dto.Volume.Value;
        if (dto.CanBePickedUp.HasValue) template.Stack.CanBePickedUp = dto.CanBePickedUp.Value;

        // JSON 是模块初始状态的唯一来源，不继承外壳 Prefab 上的 ModuleData。
        template.ModuleDataDic = new Dictionary<string, ModuleData>(StringComparer.Ordinal);
        var moduleParameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var modulePrefabIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ItemModuleDefinitionDto> pair in dto.Modules ?? new())
        {
            string moduleName = pair.Key?.Trim();
            ItemModuleDefinitionDto moduleDto = pair.Value;
            string moduleId = moduleDto?.Prefab?.Trim();
            if (string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(moduleId))
                throw new InvalidDataException($"物品 {id} 包含空模块名或 prefab");

            Module prototype = ResolveModulePrototype(gameRes, shell, moduleId);
            if (prototype?._Data == null)
                throw new InvalidDataException($"物品 {id} 找不到模块 Prefab/外壳模块：{moduleId}");

            // 以 object 作为泛型实参，避免深拷贝按 ModuleData 静态类型退化为错误的 Ex_ModData。
            ModuleData moduleData = FastCloner.FastCloner.DeepClone<object>(prototype._Data) as ModuleData;
            if (moduleData == null || moduleData.GetType() != prototype._Data.GetType())
                throw new InvalidDataException(
                    $"物品 {id} 的模块 {moduleName} 数据类型复制失败：期望 {prototype._Data.GetType().Name}，实际 {moduleData?.GetType().Name ?? "null"}");
            PopulateModuleData(moduleDto.Data, moduleData, id, moduleName);
            moduleData.Name = moduleName;
            moduleData.ID = string.IsNullOrWhiteSpace(moduleDto.Id)
                ? (!string.IsNullOrWhiteSpace(prototype._Data.ID) ? prototype._Data.ID : moduleId)
                : moduleDto.Id.Trim();
            if (moduleDto.Enabled.HasValue)
                moduleData.isRunning = moduleDto.Enabled.Value;
            template.ModuleDataDic.Add(moduleName, moduleData);
            moduleParameters.Add(moduleName, moduleDto.Parameters?.ToString(Formatting.None));
            modulePrefabIds.Add(moduleName, moduleId);
        }

        AddAutomaticHealthModule(gameRes, shell, dto.Health, id, template, moduleParameters, modulePrefabIds);

        // Actor 由 AnimatorController 驱动 SpriteRenderer，永远不再解析 Sprite 子资源地址。
        Sprite sprite = isActor
            ? null
            : ResolveSprite(dto.Visual?.SpriteAddress, id, preloadedSprites);
        RuntimeAnimatorController animatorController = ResolveAnimatorController(
            dto.Visual?.AnimatorControllerAddress,
            id,
            preloadedControllers);
        return new RuntimeItemDefinition(
            id,
            shellId,
            shell,
            template,
            dto.Visual,
            dto.Health,
            sprite,
            moduleParameters,
            modulePrefabIds,
            dto.LabelKey,
            dto.DescriptionKey,
            animatorController,
            isActor);
    }

    private static void AddAutomaticHealthModule(GameRes gameRes, GameObject shell, ItemHealthDefinitionDto health, string itemId, ItemData template, Dictionary<string, string> moduleParameters, Dictionary<string, string> modulePrefabIds)
    {
        if (health == null || !health.HasHp) return;

        const string moduleName = "生命值系统模块";
        const string prefabId = "Module_DamageReciver";
        if (template.ModuleDataDic.ContainsKey(moduleName) || modulePrefabIds.Values.Any(value => string.Equals(value, prefabId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"物品 {itemId} 同时声明 health 和显式 {prefabId} 模块；请只保留 health 声明");

        Module prototype = ResolveModulePrototype(gameRes, shell, prefabId);
        if (prototype?._Data == null)
            throw new InvalidDataException($"物品 {itemId} 的 health 找不到模块 Prefab：{prefabId}");

        ModuleData moduleData = FastCloner.FastCloner.DeepClone<object>(prototype._Data) as ModuleData;
        if (moduleData == null || moduleData.GetType() != prototype._Data.GetType())
            throw new InvalidDataException($"物品 {itemId} 的 health 模块数据复制失败");
        moduleData.Name = moduleName;
        moduleData.ID = !string.IsNullOrWhiteSpace(prototype._Data.ID) ? prototype._Data.ID : moduleName;
        moduleData.isRunning = true;
        template.ModuleDataDic.Add(moduleName, moduleData);
        modulePrefabIds.Add(moduleName, prefabId);

        float maxHp = Mathf.Max(0.01f, health.MaxHp);
        float hp = Mathf.Clamp(health.Hp, 0f, maxHp);
        ItemDefenseDefinitionDto defense = health.Defense ?? new ItemDefenseDefinitionDto();
        var parameters = new JObject
        {
            ["Data"] = new JObject
            {
                ["Hp"] = hp,
                ["MaxHp"] = maxHp,
                ["DefenseValues"] = new JObject
                {
                    ["Cutting"] = defense.Cutting,
                    ["Piercing"] = defense.Piercing,
                    ["Chopping"] = defense.Chopping,
                    ["Blunt"] = defense.Blunt
                }
            }
        };
        if (health.ModuleLocalPosition.HasValue)
        {
            parameters["$transform"] = new JObject
            {
                ["localPosition"] = JToken.FromObject(health.ModuleLocalPosition.Value)
            };
        }

        if (health.Collider != null)
        {
            ItemColliderDefinitionDto collider = health.Collider;
            var colliderJson = new JObject();
            if (!string.IsNullOrWhiteSpace(collider.Type)) colliderJson["type"] = collider.Type;
            if (collider.Enabled.HasValue) colliderJson["enabled"] = collider.Enabled.Value;
            if (collider.IsTrigger.HasValue) colliderJson["isTrigger"] = collider.IsTrigger.Value;
            if (collider.Offset.HasValue) colliderJson["offset"] = JToken.FromObject(collider.Offset.Value);
            if (collider.Size.HasValue) colliderJson["size"] = JToken.FromObject(collider.Size.Value);
            if (collider.EdgeRadius.HasValue) colliderJson["edgeRadius"] = collider.EdgeRadius.Value;
            if (collider.Radius.HasValue) colliderJson["radius"] = collider.Radius.Value;
            if (collider.Direction.HasValue) colliderJson["direction"] = collider.Direction.Value;
            if (collider.Points != null) colliderJson["points"] = JToken.FromObject(collider.Points);
            parameters["$collider2D"] = colliderJson;
        }
        moduleParameters.Add(moduleName, parameters.ToString(Formatting.None));
    }

    private static void PopulateTemplateData(JObject data, ItemData template, string itemId)
    {
        if (data == null)
            return;

        RejectFields(data, itemId, "itemData", "IDName", "Guid", "ModuleDataDic");
        try
        {
            JsonConvert.PopulateObject(data.ToString(Formatting.None), template, TemplateSettings);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException($"物品 {itemId} 的 itemData 无法应用到 {template.GetType().Name}", exception);
        }
    }

    private static void PopulateModuleData(
        JObject data,
        ModuleData moduleData,
        string itemId,
        string moduleName)
    {
        if (data == null)
            return;

        RejectFields(
            data,
            itemId,
            $"modules.{moduleName}.data",
            "Name",
            "ID",
            "isRunning");
        try
        {
            JsonConvert.PopulateObject(data.ToString(Formatting.None), moduleData, TemplateSettings);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                $"物品 {itemId} 的模块数据 {moduleName} 无法应用到 {moduleData.GetType().Name}",
                exception);
        }
    }

    private static void RejectFields(JObject data, string itemId, string section, params string[] names)
    {
        foreach (string name in names)
        {
            if (data.Property(name, StringComparison.OrdinalIgnoreCase) != null)
                throw new InvalidDataException($"物品 {itemId} 的 {section} 不允许覆盖保留字段：{name}");
        }
    }

    private static Module ResolveModulePrototype(GameRes gameRes, GameObject shell, string moduleId)
    {
        GameObject modulePrefab = gameRes.GetPrefab(moduleId, false);
        if (modulePrefab != null)
        {
            // 一个旧模块 Prefab 可能同时包含 Item 和多个 Module，必须按持久化 ID 选中目标类型，不能取第一个组件。
            Module[] candidates = modulePrefab.GetComponentsInChildren<Module>(true);
            Module matched = candidates.FirstOrDefault(candidate =>
                candidate != null && candidate.MatchesPersistedId(moduleId));
            if (matched != null)
                return matched;
            if (candidates.Length == 1)
                return candidates[0];
        }

        return shell.GetComponentsInChildren<Module>(true)
            .FirstOrDefault(candidate => candidate != null && candidate.MatchesPersistedId(moduleId));
    }

    private static Sprite ResolveSprite(
        string address,
        string itemId,
        IReadOnlyDictionary<string, Sprite> preloadedSprites)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        string key = address.Trim();
        if (preloadedSprites != null)
        {
            if (preloadedSprites.TryGetValue(key, out Sprite preloaded) && preloaded != null)
                return preloaded;
            throw new InvalidDataException($"物品 {itemId} 找不到预加载 Sprite：{key}");
        }

#if UNITY_EDITOR
        if (key.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            if (TryLoadEditorSprite(key, out Sprite editorSprite, out string editorError))
                return editorSprite;

            throw new InvalidDataException($"物品 {itemId} 的 Sprite 无法解析：{editorError}");
        }
#endif
        AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(key);
        Sprite sprite = handle.WaitForCompletion();
        if (handle.Status != AsyncOperationStatus.Succeeded || sprite == null)
            throw new InvalidDataException($"物品 {itemId} 找不到 Sprite Addressable：{address}");
        return sprite;
    }

    /// <summary>解析动画控制器；Actor 与普通物品共用同一套视觉应用管线。</summary>
    private static RuntimeAnimatorController ResolveAnimatorController(
        string address,
        string itemId,
        IReadOnlyDictionary<string, RuntimeAnimatorController> preloadedControllers)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        string key = address.Trim();
        if (preloadedControllers != null)
        {
            if (preloadedControllers.TryGetValue(key, out RuntimeAnimatorController preloaded) &&
                preloaded != null)
            {
                return preloaded;
            }

            throw new InvalidDataException($"物品 {itemId} 找不到预加载动画控制器：{key}");
        }

        AsyncOperationHandle<RuntimeAnimatorController> handle =
            Addressables.LoadAssetAsync<RuntimeAnimatorController>(key);
        RuntimeAnimatorController controller = handle.WaitForCompletion();
        if (handle.Status != AsyncOperationStatus.Succeeded || controller == null)
            throw new InvalidDataException($"物品 {itemId} 找不到动画控制器 Addressable：{address}");
        return controller;
    }
}
