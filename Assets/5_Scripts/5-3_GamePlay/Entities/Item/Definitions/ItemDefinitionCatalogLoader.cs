using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
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

    /// <summary>已由新模块 Prefab 或 JSON 外壳取代，不再进入通用 Prefab 注册表的旧资源。</summary>
    private static readonly string[] ObsoleteRuntimePrefabPaths =
    {
        "Assets/2_Prefabs/Gameplay/Items/Inventory/Module_Furnace.prefab",
        "Assets/2_Prefabs/Gameplay/Modules/AI/Module_MoverAI.prefab",
        "Assets/2_Prefabs/Gameplay/Modules/Combat/AttackTrigger.prefab",
        "Assets/2_Prefabs/Gameplay/Modules/Common/Module_FocusPoint.prefab",
        "Assets/2_Prefabs/Gameplay/Modules/Managers/TileReciver.prefab",
        "Assets/2_Prefabs/Gameplay/Modules/Movement/Module_Move.prefab",
        "Assets/2_Prefabs/Gameplay/Modules/Movement/Mover.prefab",
        "Assets/2_Prefabs/Gameplay/Modules/Variants/Module_SmeltingVariant.prefab",
        "Assets/2_Prefabs/World/Buildings/Wall_Stone.prefab"
    };
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
        if (dtos.Any(dto => !string.IsNullOrWhiteSpace(dto.LootTableId)) && gameRes.LootTables.Count == 0)
        {
            foreach (RuntimeLootTable lootTable in LootTableCatalogLoader.LoadBuiltIn())
                gameRes.RegisterLootTable(lootTable);
        }
        ValidateLootTableItemIds(gameRes, dtos);
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

    /// <summary>异步读取 Manifest 与分包，一次解析结果同时供 Prefab 计划和定义构建使用。</summary>
    public static IEnumerator LoadBuiltInDefinitionsAsync(
        Action<List<ItemDefinitionDto>> completed, Action<Exception> failed, Action<float> progress = null)
    {
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

        completed(dtos);
    }

    /// <summary>从解析后的定义构建运行时目录；所有资源请求交由 GameRes 会话持有。</summary>
    public static IEnumerator LoadBuiltInAsync(
        GameRes gameRes, Action<int> completed, Action<Exception> failed,
        Action<float> progress = null, List<ItemDefinitionDto> preparedDefinitions = null)
    {
        if (gameRes == null) throw new ArgumentNullException(nameof(gameRes));
        List<ItemDefinitionDto> dtos = preparedDefinitions;
        if (dtos == null)
        {
            Exception error = null;
            yield return LoadBuiltInDefinitionsAsync(value => dtos = value, exception => error = exception, progress);
            if (error != null) { failed?.Invoke(error); yield break; }
        }
        ValidateLootTableItemIds(gameRes, dtos);

        // shellPrefab 引用通用 Prefab 目录；只有 shellAddress 才声明独立外壳资源。
        // sourcePrefab 仅是编辑器迁移来源，不参与运行时资源加载。
        var shellAddresses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto dto in dtos.Where(dto => !dto.Abstract && !string.IsNullOrWhiteSpace(dto.ShellAddress)))
        {
            string shellId = dto.ShellPrefab.Trim();
            string address = dto.ShellAddress.Trim();
            if (shellAddresses.TryGetValue(shellId, out string existing) && existing != address)
                throw new InvalidDataException($"物品外壳 {shellId} 同时声明不同地址：{existing} / {address}");
            shellAddresses[shellId] = address;
        }
        foreach (KeyValuePair<string, string> pair in shellAddresses)
        {
            var handle = gameRes.ResourceAssets.Load<GameObject>(pair.Value);
            yield return handle;
            GameObject shell = ResourceAssetScope.Require(handle, $"Item Shell {pair.Key} -> {pair.Value}");
            if (shell.GetComponent<Item>()?.itemData == null)
                throw new InvalidDataException($"物品外壳不是有效 Item：{pair.Key} -> {pair.Value}");
            gameRes.RegisterPrefabAlias(pair.Key, shell);
        }

        var sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        var materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        var controllers = new Dictionary<string, RuntimeAnimatorController>(StringComparer.OrdinalIgnoreCase);
        yield return LoadVisualAssets(gameRes, dtos, sprites, dto => dto.Visual?.SpriteAddress, progress);
        yield return LoadVisualAssets(gameRes, dtos, materials, dto => dto.Visual?.MaterialAddress, progress);
        yield return LoadVisualAssets(gameRes, dtos, controllers, dto => dto.Visual?.AnimatorControllerAddress, progress);

        var definitions = new List<RuntimeItemDefinition>(dtos.Count);
        try
        {
            foreach (ItemDefinitionDto dto in dtos)
            {
                if (!dto.Abstract)
                    definitions.Add(BuildRuntimeDefinition(
                        gameRes,
                        dto,
                        sprites,
                        controllers,
                        preloadedMaterials: materials));
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
        Debug.Log($"[ItemDefinitionCatalog] 已异步加载 " +
                  $"{definitions.Count} 个物品：{BuiltInManifestPath}");
        completed?.Invoke(definitions.Count);
    }

    /// <summary>批量加载某类视觉资源；地址去重、句柄所有权和失败语义由资源会话统一负责。</summary>
    private static IEnumerator LoadVisualAssets<T>(GameRes gameRes, IEnumerable<ItemDefinitionDto> definitions,
        IDictionary<string, T> output, Func<ItemDefinitionDto, string> select, Action<float> progress)
        where T : UnityEngine.Object
    {
        string[] addresses = definitions.Where(dto => !dto.Abstract).Select(select)
            .Where(address => !string.IsNullOrWhiteSpace(address)).Select(address => address.Trim())
            .Distinct(StringComparer.Ordinal).ToArray();
        // 每批最多 16 个请求，兼顾移动端内存峰值与本地/远程资源吞吐。
        const int batchSize = 16;
        for (int start = 0; start < addresses.Length; start += batchSize)
        {
            int count = Math.Min(batchSize, addresses.Length - start);
            var batch = new List<AsyncOperationHandle<T>>(count);
            for (int i = 0; i < count; i++)
            {
                string address = addresses[start + i];
#if UNITY_EDITOR
                // 先检查 Sprite 子资源名称，避免 Fast Mode 将错误名称送入包内部后才抛空引用。
                if (typeof(T) == typeof(Sprite) && address.StartsWith("Assets/", StringComparison.Ordinal) &&
                    !TryLoadEditorSprite(address, out _, out string error))
                    throw new InvalidDataException(error);
#endif
                batch.Add(gameRes.ResourceAssets.Load<T>(address));
            }
            while (batch.Any(handle => !handle.IsDone))
            {
                progress?.Invoke(0.2f + 0.6f * (start + batch.Sum(handle => handle.PercentComplete)) / addresses.Length);
                yield return null;
            }
            for (int i = 0; i < count; i++)
                output.Add(addresses[start + i], ResourceAssetScope.Require(batch[i], $"{typeof(T).Name} -> {addresses[start + i]}"));
            progress?.Invoke(0.2f + 0.6f * (start + count) / addresses.Length);
        }
    }

    /// <summary>
    /// 返回已经完整迁入 JSON、且不是任何运行时外壳/模块 Prefab 的旧资源路径。
    /// GameRes 用它在 Addressables 定位阶段直接跳过冗余变体，避免先加载再覆盖。
    /// </summary>
    public static HashSet<string> GetRedundantBuiltInPrefabPaths()
    {
        return GetRedundantBuiltInPrefabPaths(LoadBuiltInDefinitions());
    }

    /// <summary>按已解析的 Manifest 生成排除清单，所有平台使用同一份定义。</summary>
    public static HashSet<string> GetRedundantBuiltInPrefabPaths(IEnumerable<ItemDefinitionDto> definitions)
    {
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
        redundant.UnionWith(ObsoleteRuntimePrefabPaths);
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

    /// <summary>在目录注册前统一校验 Item/Actor 的表引用与模块掉落 ID，禁止通用外壳名进入生成链路。</summary>
    internal static void ValidateLootTableItemIds(
        GameRes gameRes,
        IReadOnlyCollection<ItemDefinitionDto> definitions)
    {
        var concreteItemIds = new HashSet<string>(
            definitions
                .Where(definition => definition != null && !definition.Abstract)
                .Select(definition => definition.Id?.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
        concreteItemIds.UnionWith(gameRes.ItemDefinitions.Keys);

        foreach (ItemDefinitionDto definition in definitions)
        {
            if (definition?.Modules == null)
                continue;
            foreach (KeyValuePair<string, ItemModuleDefinitionDto> module in definition.Modules)
            {
                if (module.Value?.Parameters == null)
                    continue;

                // 受伤动作等嵌套 LootEntry 同样必须在加载时校验，不能等概率命中才发现错误。
                foreach (JProperty property in module.Value.Parameters.Descendants().OfType<JProperty>())
                {
                    if (!string.Equals(property.Name, nameof(LootEntry.LootPrefabName), StringComparison.OrdinalIgnoreCase))
                        continue;
                    string lootItemId = property.Value.Type == JTokenType.String
                        ? property.Value.Value<string>()?.Trim()
                        : null;
                    if (string.IsNullOrWhiteSpace(lootItemId) || !concreteItemIds.Contains(lootItemId))
                        throw new InvalidDataException(
                            $"物品 {definition.Id} 模块 {module.Key}/{property.Path} 引用了不存在或抽象的 ItemDefinition：{lootItemId}");
                }
            }
        }

        foreach (string tableId in definitions
                     .Select(definition => definition?.LootTableId?.Trim())
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!gameRes.TryGetLootTable(tableId, out RuntimeLootTable table))
                throw new InvalidDataException($"物品定义引用了不存在的战利品表：{tableId}");

            foreach (RuntimeLootTableEntry entry in table.Entries)
            {
                if (!concreteItemIds.Contains(entry.ItemId))
                {
                    throw new InvalidDataException(
                        $"战利品表 {table.Id} 引用了不存在或抽象的 ItemDefinition：{entry.ItemId}");
                }
            }
        }
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
            // 外观和玩法可继承，显示名称与翻译键属于当前定义的身份，必须独立声明或生成。
            result.Remove("gameName");
            result.Remove("labelKey");
            result.Remove("descriptionKey");
        }

        RemoveReplacedModuleBodies(result, source);
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

    /// <summary>子定义切换模块 Prefab 时丢弃父模块参数，避免把旧组件字段合并进新组件。</summary>
    private static void RemoveReplacedModuleBodies(JObject inherited, JObject source)
    {
        if (inherited?["modules"] is not JObject inheritedModules ||
            source?["modules"] is not JObject sourceModules)
        {
            return;
        }

        foreach (JProperty childModule in sourceModules.Properties())
        {
            if (childModule.Value is not JObject childBody ||
                inheritedModules[childModule.Name] is not JObject inheritedBody)
            {
                continue;
            }

            string inheritedPrefab = inheritedBody.Value<string>("prefab")?.Trim();
            string childPrefab = childBody.Value<string>("prefab")?.Trim();
            if (!string.IsNullOrWhiteSpace(childPrefab) &&
                !string.Equals(inheritedPrefab, childPrefab, StringComparison.OrdinalIgnoreCase))
            {
                inheritedModules.Remove(childModule.Name);
            }
        }
    }

    #endregion

    internal static RuntimeItemDefinition BuildRuntimeDefinition(
        GameRes gameRes,
        ItemDefinitionDto dto,
        IReadOnlyDictionary<string, Sprite> preloadedSprites = null,
        IReadOnlyDictionary<string, RuntimeAnimatorController> preloadedControllers = null,
        bool isActor = false,
        IReadOnlyDictionary<string, GameObject> preloadedShells = null,
        IReadOnlyDictionary<string, Material> preloadedMaterials = null)
    {
        string id = dto.Id?.Trim();
        string shellId = dto.ShellPrefab?.Trim();
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("物品定义 ID 为空");
        if (string.IsNullOrWhiteSpace(shellId))
            throw new InvalidDataException($"物品 {id} 缺少 shellPrefab");

        RuntimeLootTable lootTable = null;
        if (!string.IsNullOrWhiteSpace(dto.LootTableId) &&
            !gameRes.TryGetLootTable(dto.LootTableId, out lootTable))
        {
            throw new InvalidDataException($"物品 {id} 引用了不存在的战利品表：{dto.LootTableId}");
        }

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
        if (string.IsNullOrWhiteSpace(dto.GameName))
            throw new InvalidDataException($"物品 {id} 缺少默认显示名 gameName，不能使用 ID 或外壳名替代。");
        template.GameName = dto.GameName.Trim();
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
        bool lootTableBound = false;
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

            JObject parameters = moduleDto.Parameters == null
                ? null
                : (JObject)moduleDto.Parameters.DeepClone();
            if (lootTable != null &&
                string.Equals(moduleId, "Module_DamageReciver", StringComparison.OrdinalIgnoreCase))
            {
                if (lootTableBound)
                    throw new InvalidDataException($"物品 {id} 包含多个死亡掉落接收器，无法绑定战利品表 {lootTable.Id}");
                parameters = BindLootTableParameters(parameters, lootTable, id);
                lootTableBound = true;
            }

            ModuleJsonConfigurator.Validate(prototype, id, moduleName, moduleData.ID, parameters?.ToString(Formatting.None));
            moduleParameters.Add(moduleName, parameters?.ToString(Formatting.None));
            modulePrefabIds.Add(moduleName, moduleId);
        }

        AddAutomaticHealthModule(
            gameRes,
            shell,
            dto.Health,
            lootTable,
            id,
            template,
            moduleParameters,
            modulePrefabIds);
        lootTableBound |= lootTable != null && dto.Health?.HasHp == true;
        if (lootTable != null && !lootTableBound)
            throw new InvalidDataException($"物品 {id} 引用了战利品表 {lootTable.Id}，但没有 DamageReceiver");

        // Actor 由 AnimatorController 驱动 SpriteRenderer，永远不再解析 Sprite 子资源地址。
        Sprite sprite = isActor
            ? null
            : ResolveSprite(gameRes, dto.Visual?.SpriteAddress, id, preloadedSprites);
        Material material = ResolveMaterial(gameRes, dto.Visual?.MaterialAddress, id, preloadedMaterials) ??
                            ResolveShellRendererMaterial(shell, dto.Visual?.RendererPath);
        RuntimeAnimatorController animatorController = ResolveAnimatorController(
            gameRes, dto.Visual?.AnimatorControllerAddress,
            id,
            preloadedControllers);
        return new RuntimeItemDefinition(
            id,
            shellId,
            shell,
            template,
            dto.Visual,
            dto.Health,
            lootTable?.Id,
            sprite,
            moduleParameters,
            modulePrefabIds,
            dto.LabelKey,
            dto.DescriptionKey,
            animatorController,
            isActor,
            material);
    }

    private static void AddAutomaticHealthModule(
        GameRes gameRes,
        GameObject shell,
        ItemHealthDefinitionDto health,
        RuntimeLootTable lootTable,
        string itemId,
        ItemData template,
        Dictionary<string, string> moduleParameters,
        Dictionary<string, string> modulePrefabIds)
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
        if (lootTable != null)
            ((JObject)parameters["Data"])["LootTable"] = lootTable.CreateDamageReceiverEntries();

        if (health.ModuleLocalPosition.HasValue)
        {
            parameters["$transform"] = new JObject
            {
                ["localPosition"] = CreateVector3Token(health.ModuleLocalPosition.Value)
            };
        }

        if (health.Collider != null)
        {
            ItemColliderDefinitionDto collider = health.Collider;
            var colliderJson = new JObject();
            if (!string.IsNullOrWhiteSpace(collider.Type)) colliderJson["type"] = collider.Type;
            if (collider.Enabled.HasValue) colliderJson["enabled"] = collider.Enabled.Value;
            if (collider.IsTrigger.HasValue) colliderJson["isTrigger"] = collider.IsTrigger.Value;
            if (collider.Offset.HasValue) colliderJson["offset"] = CreateVector2Token(collider.Offset.Value);
            if (collider.Size.HasValue) colliderJson["size"] = CreateVector2Token(collider.Size.Value);
            if (collider.EdgeRadius.HasValue) colliderJson["edgeRadius"] = collider.EdgeRadius.Value;
            if (collider.Radius.HasValue) colliderJson["radius"] = collider.Radius.Value;
            if (collider.Direction.HasValue) colliderJson["direction"] = collider.Direction.Value;
            if (collider.Points != null) colliderJson["points"] = CreateVector2ArrayToken(collider.Points);
            parameters["$collider2D"] = colliderJson;
        }
        moduleParameters.Add(moduleName, parameters.ToString(Formatting.None));
    }

    /// <summary>把表引用展开到显式 DamageReceiver 参数，并拒绝两套掉落来源并存。</summary>
    private static JObject BindLootTableParameters(
        JObject parameters,
        RuntimeLootTable lootTable,
        string itemId)
    {
        parameters ??= new JObject();
        JObject data;
        if (parameters["Data"] == null)
        {
            data = new JObject();
            parameters["Data"] = data;
        }
        else if (parameters["Data"] is JObject configuredData)
        {
            data = configuredData;
        }
        else
        {
            throw new InvalidDataException($"物品 {itemId} 的 DamageReceiver.Data 必须是对象");
        }

        if (data.Property("LootTable", StringComparison.OrdinalIgnoreCase) != null)
        {
            throw new InvalidDataException(
                $"物品 {itemId} 同时声明 lootTableId 和内联 LootTable；请只保留 ID 引用");
        }

        data["LootTable"] = lootTable.CreateDamageReceiverEntries();
        return parameters;
    }

    /// <summary>按物品 JSON 契约显式序列化 Vector2，避免 Json.NET 遍历 Unity 计算属性。</summary>
    private static JObject CreateVector2Token(Vector2 value)
    {
        return new JObject
        {
            ["x"] = value.x,
            ["y"] = value.y
        };
    }

    /// <summary>按物品 JSON 契约显式序列化 Vector3，避免 normalized 等计算属性形成自引用。</summary>
    private static JObject CreateVector3Token(Vector3 value)
    {
        return new JObject
        {
            ["x"] = value.x,
            ["y"] = value.y,
            ["z"] = value.z
        };
    }

    /// <summary>逐点构建碰撞轮廓数组，统一复用 Vector2 的稳定字段契约。</summary>
    private static JArray CreateVector2ArrayToken(IEnumerable<Vector2> values)
    {
        var result = new JArray();
        foreach (Vector2 value in values)
            result.Add(CreateVector2Token(value));
        return result;
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
        => FindModulePrototype(shell, gameRes.GetPrefab(moduleId, false), moduleId);

    /// <summary>构建、运行时校验和编辑器预检共用模块定位规则，支持外壳内嵌模块的持久化身份。</summary>
    public static Module FindModulePrototype(GameObject shell, GameObject modulePrefab, string moduleId)
    {
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

        return shell == null ? null : shell.GetComponentsInChildren<Module>(true)
            .FirstOrDefault(candidate => candidate != null && candidate.MatchesPersistedId(moduleId));
    }

    private static Sprite ResolveSprite(
        GameRes gameRes, string address,
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


        AsyncOperationHandle<Sprite> handle = gameRes.ResourceAssets.Load<Sprite>(key);
        Sprite sprite = handle.WaitForCompletion();
        if (handle.Status != AsyncOperationStatus.Succeeded || sprite == null)
            throw new InvalidDataException($"物品 {itemId} 找不到 Sprite Addressable：{address}");
        return sprite;
    }

    /// <summary>解析物品世界渲染器使用的共享材质。</summary>
    private static Material ResolveMaterial(
        GameRes gameRes, string address,
        string itemId,
        IReadOnlyDictionary<string, Material> preloadedMaterials)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        string key = address.Trim();
        if (preloadedMaterials != null)
        {
            if (preloadedMaterials.TryGetValue(key, out Material preloaded) && preloaded != null)
                return preloaded;
            throw new InvalidDataException($"物品 {itemId} 找不到预加载材质：{key}");
        }


        AsyncOperationHandle<Material> handle = gameRes.ResourceAssets.Load<Material>(key);
        Material material = handle.WaitForCompletion();
        if (handle.Status != AsyncOperationStatus.Succeeded || material == null)
            throw new InvalidDataException($"物品 {itemId} 找不到材质 Addressable：{address}");
        return material;
    }

    /// <summary>记录共享外壳的默认材质，确保对象池切换定义时不会串用上一个物品的材质。</summary>
    private static Material ResolveShellRendererMaterial(GameObject shell, string rendererPath)
    {
        if (shell == null)
            return null;

        SpriteRenderer renderer = null;
        if (!string.IsNullOrWhiteSpace(rendererPath))
        {
            Transform target = shell.transform.Find(rendererPath);
            renderer = target != null ? target.GetComponent<SpriteRenderer>() : null;
        }

        renderer ??= shell.GetComponentsInChildren<SpriteRenderer>(true)
            .FirstOrDefault(candidate => candidate.sprite != null);
        renderer ??= shell.GetComponentInChildren<SpriteRenderer>(true);
        return renderer != null ? renderer.sharedMaterial : null;
    }

    /// <summary>解析动画控制器；Actor 与普通物品共用同一套视觉应用管线。</summary>
    private static RuntimeAnimatorController ResolveAnimatorController(
        GameRes gameRes, string address,
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
            gameRes.ResourceAssets.Load<RuntimeAnimatorController>(key);
        RuntimeAnimatorController controller = handle.WaitForCompletion();
        if (handle.Status != AsyncOperationStatus.Succeeded || controller == null)
            throw new InvalidDataException($"物品 {itemId} 找不到动画控制器 Addressable：{address}");
        return controller;
    }
}
