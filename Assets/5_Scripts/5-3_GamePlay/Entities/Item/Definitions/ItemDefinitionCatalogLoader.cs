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

        string[] addresses = dtos
            .Where(dto => !dto.Abstract && !string.IsNullOrWhiteSpace(dto.Visual?.SpriteAddress))
            .Select(dto => dto.Visual.SpriteAddress.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var handles = new Dictionary<string, AsyncOperationHandle<Sprite>>(StringComparer.OrdinalIgnoreCase);
        foreach (string address in addresses)
            handles[address] = Addressables.LoadAssetAsync<Sprite>(address);

        while (handles.Values.Any(handle => !handle.IsDone))
        {
            int done = handles.Values.Count(handle => handle.IsDone);
            progress?.Invoke(addresses.Length == 0 ? 0.9f : 0.2f + 0.7f * done / addresses.Length);
            yield return null;
        }

        var sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, AsyncOperationHandle<Sprite>> pair in handles)
        {
            if (pair.Value.Status != AsyncOperationStatus.Succeeded || pair.Value.Result == null)
            {
                failed?.Invoke(new InvalidDataException($"找不到物品 Sprite Addressable：{pair.Key}"));
                yield break;
            }
            sprites[pair.Key] = pair.Value.Result;
        }

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
            .Select(id => resolved[id].ToObject<ItemDefinitionDto>() ??
                          throw new InvalidDataException($"无法解析物品定义：{id}"))
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

    private static RuntimeItemDefinition BuildRuntimeDefinition(
        GameRes gameRes,
        ItemDefinitionDto dto,
        IReadOnlyDictionary<string, Sprite> preloadedSprites = null)
    {
        string id = dto.Id?.Trim();
        string shellId = dto.ShellPrefab?.Trim();
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("物品定义 ID 为空");
        if (string.IsNullOrWhiteSpace(shellId))
            throw new InvalidDataException($"物品 {id} 缺少 shellPrefab");

        GameObject shell = gameRes.GetPrefab(shellId, false);
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

        Sprite sprite = ResolveSprite(dto.Visual?.SpriteAddress, id, preloadedSprites);
        return new RuntimeItemDefinition(
            id,
            shellId,
            shell,
            template,
            dto.Visual,
            sprite,
            moduleParameters,
            modulePrefabIds,
            dto.LabelKey,
            dto.DescriptionKey);
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
            "isRunning",
            "RuntimeOwnerItemData",
            "RuntimeOwnerInventoryData",
            "RuntimeOwnerSlot",
            "RuntimeOwnerSlotIndex");
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

        AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(key);
        Sprite sprite = handle.WaitForCompletion();
        if (handle.Status != AsyncOperationStatus.Succeeded || sprite == null)
            throw new InvalidDataException($"物品 {itemId} 找不到 Sprite Addressable：{address}");
        return sprite;
    }
}
