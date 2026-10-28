using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using FlatWorld.Localization;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>从 StreamingAssets 加载本体 ItemDefinition，并绑定少量外壳 Prefab。</summary>
public static class ItemDefinitionCatalogLoader
{
    public const int SupportedSchemaVersion = 1;
    public const string RelativeItemRoot = "GameConfig/Items";
    public const string ManifestFileName = "item-manifest.json";

    #region 地表植被排序契约
    // 地表植被由固定的地形 BRG 绘制，只接受其真实世界排序键。
    internal static void ValidateGroundCoverWorldSortingConfiguration(string itemId, ItemVisualDefinitionDto visual)
    {
        int terrainLayer = SortingLayer.NameToID(WorldRenderingConfigCatalog.Default.sorting.terrainSortingLayer);
        WorldSortingManager.ResolveItemVisualSortingKey(visual, terrainLayer, 0, out int layer, out int order);
        if (layer != terrainLayer || order != 0)
            throw new InvalidDataException($"地表植被 {itemId} 必须使用地形 Sorting Layer 和 Order 0。");
    }
    #endregion

    /// <summary>角色与 AI 仍内嵌的模块随宿主加载，不再单独进入通用 Prefab 注册表。</summary>
    private static readonly string[] EmbeddedModulePrefabPathsExcludedFromGenericCatalog =
    {
        "Assets/2_Prefabs/Gameplay/Modules/AI/Module_MoverAI.prefab",
        "Assets/2_Prefabs/Gameplay/Modules/Common/Module_FocusPoint.prefab",
        "Assets/2_Prefabs/Gameplay/Modules/Managers/TileReciver.prefab",
        "Assets/2_Prefabs/Gameplay/Modules/Movement/Module_Move.prefab",
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
        BinaryContentCache cache = CreateBinaryCache();
        if (cache.TryRead(out List<ItemDefinitionDto> cached)) return cached;
        List<ItemDefinitionDto> definitions = ResolveLoadedPackages(ReadBuiltInPackages(cache));
        cache.Store(definitions);
        return definitions;
    }

    private static BinaryContentCache CreateBinaryCache()
        => BinaryContentCache.ForBuiltIn<List<ItemDefinitionDto>>("items", BuiltInItemRoot, BuiltInManifestPath, 1);

    /// <summary>返回合并后的原始定义，供迁移工具保留 parent 与手工覆盖字段。</summary>
    public static JObject LoadBuiltInSourceCatalog()
    {
        return CreateCombinedCatalog(ReadBuiltInPackages().Select(package => package.Root));
    }

    /// <summary>异步读取 Manifest 与分包，一次解析结果同时供 Prefab 计划和定义构建使用。</summary>
    public static IEnumerator LoadBuiltInDefinitionsAsync(
        Action<List<ItemDefinitionDto>> completed, Action<Exception> failed, Action<float> progress = null)
    {
        BinaryContentCache cache = CreateBinaryCache();
        List<ItemDefinitionDto> cached = null;
        yield return cache.TryReadAsync<List<ItemDefinitionDto>>(value => cached = value);
        if (cached != null)
        {
            cache.Report("物品", cached.Count);
            progress?.Invoke(0.2f);
            completed(cached);
            yield break;
        }
        string manifestJson = null;
        Exception readError = null;
        yield return cache.ReadTextAsync(
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

        // 本地分包一次后台读取、合并与继承解析，避免每个文件反复跨帧派发线程任务。
        if (!StreamingAssetsTextLoader.RequiresWebRequest(BuiltInItemRoot))
        {
            var packagePaths = new string[enabledPackages.Length];
            try
            {
                for (int i = 0; i < enabledPackages.Length; i++)
                    packagePaths[i] = ResolvePackagePath(BuiltInItemRoot, enabledPackages[i].Path);
            }
            catch (Exception exception) { failed?.Invoke(exception); yield break; }

            List<ItemDefinitionDto> localDefinitions = null;
            readError = null;
            yield return StreamingAssetsTextLoader.RunPureDataAsync(() =>
            {
                var packages = new List<LoadedItemPackage>(enabledPackages.Length);
                for (int i = 0; i < enabledPackages.Length; i++)
                {
                    string packageJson;
                    try { packageJson = cache.ReadText(packagePaths[i]); }
                    catch (Exception exception)
                    {
                        throw new IOException($"物品分包 {enabledPackages[i].Id} 读取失败：{packagePaths[i]}", exception);
                    }
                    packages.Add(new LoadedItemPackage(enabledPackages[i], packageJson));
                }
                return ResolveLoadedPackages(packages);
            }, value => localDefinitions = value, exception => readError = exception);
            if (readError != null) { failed?.Invoke(readError); yield break; }

            progress?.Invoke(0.2f);
            yield return cache.StoreAsync(localDefinitions);
            cache.Report("物品", localDefinitions.Count);
            completed(localDefinitions);
            yield break;
        }

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
            yield return cache.ReadTextAsync(
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

            LoadedItemPackage loadedPackage = null;
            yield return StreamingAssetsTextLoader.RunPureDataAsync(
                () => new LoadedItemPackage(package, packageJson),
                value => loadedPackage = value, exception => readError = exception);
            if (readError != null) { failed?.Invoke(readError); yield break; }
            loadedPackages.Add(loadedPackage);

            progress?.Invoke(0.2f * (i + 1) / enabledPackages.Length);
        }

        List<ItemDefinitionDto> dtos = null;
        // 继承合并只访问本次读取的私有 JSON，不在工作线程访问 Unity 或正式目录。
        yield return StreamingAssetsTextLoader.RunPureDataAsync(
            () => ResolveLoadedPackages(loadedPackages),
            value => dtos = value, exception => readError = exception);
        if (readError != null) { failed?.Invoke(readError); yield break; }

        yield return cache.StoreAsync(dtos);
        cache.Report("物品", dtos.Count);
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
        // 独立外壳也使用有界并发，避免少数慢请求把后续外壳全部串行阻塞。
        var shells = shellAddresses.ToArray();
        var loadedShells = new GameObject[shells.Length];
        var sectionTimer = System.Diagnostics.Stopwatch.StartNew();
        yield return ResourceLoadQueue.Run(shells.Length,
            index => gameRes.ResourceAssets.Load<GameObject>(shells[index].Value),
            handle => handle.IsDone,
            (index, handle) => loadedShells[index] = ResourceAssetScope.Require(
                handle, $"Item Shell {shells[index].Key} -> {shells[index].Value}"),
            gameRes.ShouldYieldResourceWork);
        for (int i = 0; i < shells.Length; i++)
        {
            GameObject shell = loadedShells[i];
            if (shell.GetComponent<Item>()?.itemData == null)
                throw new InvalidDataException($"物品外壳不是有效 Item：{shells[i].Key} -> {shells[i].Value}");
            gameRes.RegisterPrefabAlias(shells[i].Key, shell);
            if (gameRes.ShouldYieldResourceWork()) yield return null;
        }
        long shellMilliseconds = sectionTimer.ElapsedMilliseconds;

        var sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        var materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        var controllers = new Dictionary<string, RuntimeAnimatorController>(StringComparer.OrdinalIgnoreCase);
        sectionTimer.Restart();
        yield return LoadVisualAssets(gameRes, dtos, sprites, SelectVisualSpriteAddresses, progress);
        long spriteMilliseconds = sectionTimer.ElapsedMilliseconds;
        sectionTimer.Restart();
        yield return LoadVisualAssets(gameRes, dtos, materials, dto => SelectOptionalAddress(dto.Visual?.MaterialAddress), progress);
        long materialMilliseconds = sectionTimer.ElapsedMilliseconds;
        sectionTimer.Restart();
        yield return LoadVisualAssets(gameRes, dtos, controllers, dto => SelectOptionalAddress(dto.Visual?.AnimatorControllerAddress), progress);
        long controllerMilliseconds = sectionTimer.ElapsedMilliseconds;

        sectionTimer.Restart();
        var prototypeCache = new ItemModulePrototypeCache();
        var definitions = new List<RuntimeItemDefinition>(dtos.Count);
        double compilationWorkMilliseconds = 0;
        double slowestDefinitionMilliseconds = 0;
        string slowestDefinitionId = null;
        foreach (ItemDefinitionDto dto in dtos)
        {
            long definitionStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                if (!dto.Abstract)
                    definitions.Add(BuildRuntimeDefinition(
                        gameRes,
                        dto,
                        sprites,
                        controllers,
                        preloadedMaterials: materials,
                        prototypeCache: prototypeCache));
            }
            catch (Exception exception) { failed?.Invoke(exception); yield break; }
            double definitionMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - definitionStarted) *
                                            1000d / System.Diagnostics.Stopwatch.Frequency;
            compilationWorkMilliseconds += definitionMilliseconds;
            if (definitionMilliseconds > slowestDefinitionMilliseconds)
            {
                slowestDefinitionMilliseconds = definitionMilliseconds;
                slowestDefinitionId = dto.Id;
            }
            if (gameRes.ShouldYieldResourceWork()) yield return null;
        }
        long compilationMilliseconds = sectionTimer.ElapsedMilliseconds;
        sectionTimer.Restart();
        foreach (RuntimeItemDefinition definition in definitions)
        {
            try { gameRes.RegisterItemDefinition(definition); }
            catch (Exception exception) { failed?.Invoke(exception); yield break; }
            if (gameRes.ShouldYieldResourceWork()) yield return null;
        }

        long registrationMilliseconds = sectionTimer.ElapsedMilliseconds;
        progress?.Invoke(1f);
        Debug.Log($"[ItemDefinitionCatalog] 已异步加载 " +
                  $"{definitions.Count} 个物品：{BuiltInManifestPath}；" +
                  $"外壳 {shells.Length} 个/{shellMilliseconds} ms，Sprite {sprites.Count} 个/{spriteMilliseconds} ms，" +
                  $"材质 {materials.Count} 个/{materialMilliseconds} ms，控制器 {controllers.Count} 个/{controllerMilliseconds} ms，" +
                  $"定义编译 {compilationMilliseconds} ms（实际工作 {compilationWorkMilliseconds:F1} ms，" +
                  $"最慢 {slowestDefinitionId}/{slowestDefinitionMilliseconds:F1} ms），注册 {registrationMilliseconds} ms");
        completed?.Invoke(definitions.Count);
    }

    /// <summary>批量加载某类视觉资源；地址去重、句柄所有权和失败语义由资源会话统一负责。</summary>
    internal static IEnumerator LoadVisualAssets<T>(GameRes gameRes, IEnumerable<ItemDefinitionDto> definitions,
        IDictionary<string, T> output, Func<ItemDefinitionDto, IEnumerable<string>> select, Action<float> progress,
        IDictionary<string, Exception> failures = null)
        where T : UnityEngine.Object
    {
        string[] addresses = definitions.Where(dto => !dto.Abstract)
            .SelectMany(dto => select(dto) ?? Enumerable.Empty<string>())
            .Where(address => !string.IsNullOrWhiteSpace(address)).Select(address => address.Trim())
            .Distinct(StringComparer.Ordinal).ToArray();
#if UNITY_EDITOR
        var preloadedTextures = new Dictionary<string, AssetDatabaseLoadOperation>(StringComparer.OrdinalIgnoreCase);
        // 单次加载中的同一图集只解析一次子 Sprite 表，避免重复扫描 AssetDatabase。
        var editorSprites = typeof(T) == typeof(Sprite)
            ? new Dictionary<string, EditorSpriteAssetSet>(StringComparer.OrdinalIgnoreCase)
            : null;
#endif
        var results = new T[addresses.Length];
        yield return ResourceLoadQueue.Run<VisualAssetRequest<T>>(addresses.Length,
            index =>
            {
                string address = addresses[index];
                var request = new VisualAssetRequest<T> { Address = address };
                try
                {
#if UNITY_EDITOR
                    if (typeof(T) == typeof(Sprite) && address.StartsWith("Assets/", StringComparison.Ordinal))
                        request.TextureRequest = BeginEditorSpriteTexture(address, preloadedTextures);
                    else
#endif
                    {
                        request.Handle = gameRes.ResourceAssets.Load<T>(address);
                        request.AssetRequestStarted = true;
                    }
                }
                catch (Exception exception) when (failures != null) { request.Error = exception; }
                return request;
            }, request =>
            {
                if (request.Error != null) return true;
                try
                {
                    if (request.AssetRequestStarted) return request.Handle.IsDone;
#if UNITY_EDITOR
                    // 主纹理完成后接续该 Sprite，不等待其他槽位；错误子资源仍在 Fast Mode 前拒绝。
                    AssetDatabaseLoadOperation texture = request.TextureRequest;
                    if (texture != null && !texture.isDone) return false;
                    if (texture != null && texture.LoadedObject is not Texture2D)
                        throw new InvalidDataException($"Sprite 主纹理异步加载失败：{request.Address}");
                    if (!TryLoadEditorSprite(request.Address, editorSprites, out _, out string error))
                        throw new InvalidDataException(error);
#endif
                    request.Handle = gameRes.ResourceAssets.Load<T>(request.Address);
                    request.AssetRequestStarted = true;
                    return request.Handle.IsDone;
                }
                catch (Exception exception) when (failures != null)
                {
                    request.Error = exception;
                    return true;
                }
            },
            (index, request) =>
            {
                try
                {
                    if (request.Error != null) throw request.Error;
                    results[index] = ResourceAssetScope.Require(request.Handle, $"{typeof(T).Name} -> {addresses[index]}");
                }
                // Actor 热更新按地址保留失败原因，由所属定义决定拒绝并保留旧版。
                catch (Exception exception) when (failures != null) { failures[addresses[index]] = exception; }
            },
            gameRes.ShouldYieldResourceWork,
            completed => progress?.Invoke(0.2f + 0.6f * completed / addresses.Length));
        for (int i = 0; i < addresses.Length; i++)
        {
            if (failures?.ContainsKey(addresses[i]) == true) continue;
            output.Add(addresses[i], results[i]);
            if (gameRes.ShouldYieldResourceWork()) yield return null;
        }
    }

    private sealed class VisualAssetRequest<T> where T : UnityEngine.Object
    {
        public string Address;
        public AsyncOperationHandle<T> Handle;
        public bool AssetRequestStarted;
        public Exception Error;
#if UNITY_EDITOR
        public AssetDatabaseLoadOperation TextureRequest;
#endif
    }

    /// <summary>枚举一个物品定义需要预载的主 Sprite 与额外状态 Sprite。</summary>
    internal static IEnumerable<string> SelectVisualSpriteAddresses(ItemDefinitionDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto?.Visual?.SpriteAddress))
            yield return dto.Visual.SpriteAddress;

        if (dto?.Visual?.SpriteStates == null)
            yield break;

        foreach (string address in dto.Visual.SpriteStates.Values)
        {
            if (!string.IsNullOrWhiteSpace(address))
                yield return address;
        }
    }

    /// <summary>把可空的单地址适配为统一的多地址加载入口。</summary>
    private static IEnumerable<string> SelectOptionalAddress(string address)
    {
        if (!string.IsNullOrWhiteSpace(address))
            yield return address;
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
        redundant.UnionWith(EmbeddedModulePrefabPathsExcludedFromGenericCatalog);
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

    private static List<LoadedItemPackage> ReadBuiltInPackages(BinaryContentCache cache = null)
    {
        ItemDefinitionManifestDto manifest = DeserializeManifest(
            cache == null ? StreamingAssetsTextLoader.ReadAllText(BuiltInManifestPath) : cache.ReadText(BuiltInManifestPath));
        ValidateManifest(manifest);

        var packages = new List<LoadedItemPackage>();
        foreach (ItemDefinitionPackageDto package in manifest.Packages.Where(package => package.Enabled))
        {
            string packagePath = ResolvePackagePath(BuiltInItemRoot, package.Path);
            packages.Add(new LoadedItemPackage(
                package,
                cache == null ? StreamingAssetsTextLoader.ReadAllText(packagePath) : cache.ReadText(packagePath)));
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
            ValidateLootTableItemIds(gameRes, definition, concreteItemIds);
    }

    /// <summary>逐个校验定义的掉落引用，供世界内 F5 隔离单个无效 Actor 使用。</summary>
    internal static void ValidateLootTableItemIds(
        GameRes gameRes, ItemDefinitionDto definition, ISet<string> concreteItemIds)
    {
        if (definition == null) return;
        if (definition.Modules != null)
        {
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

        if (definition.Processing != null)
        {
            foreach (KeyValuePair<string, ItemProcessingDefinitionDto> pair in definition.Processing)
            {
                ItemProcessingDefinitionDto processing = pair.Value;
                if (processing?.Outputs == null)
                    continue;
                foreach (ItemProcessingOutputDto output in processing.Outputs)
                {
                    string outputId = output?.ItemId?.Trim();
                    if (string.IsNullOrWhiteSpace(outputId) || !concreteItemIds.Contains(outputId))
                    {
                        throw new InvalidDataException(
                            $"物品 {definition.Id} 的加工能力 {pair.Key} 引用了不存在或抽象的 ItemDefinition：{outputId}");
                    }
                }
            }
        }

        string tableId = definition.LootTableId?.Trim();
        if (!string.IsNullOrWhiteSpace(tableId))
        {
            if (!gameRes.TryGetLootTable(tableId, out RuntimeLootTable table))
                throw new InvalidDataException($"物品 {definition.Id} 引用了不存在的战利品表：{tableId}");

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

    private static readonly Unity.Profiling.ProfilerMarker EditorTextureRequestMarker =
        new("FlatWorld.Resources.EditorSprite.TextureRequest");
    private const long ImportedTextureMainLocalId = 2800000L;

    private sealed class EditorSpriteAssetSet
    {
        public UnityEngine.Object MainAsset;
        public Sprite MainSprite;
        public Dictionary<string, Sprite> Representations;
    }

    /// <summary>同图集只发出一次主纹理异步请求，由资源队列跟进完成和子 Sprite 接续。</summary>
    private static AssetDatabaseLoadOperation BeginEditorSpriteTexture(string address,
        IDictionary<string, AssetDatabaseLoadOperation> preloadedTextures)
    {
        int bracket = address.LastIndexOf('[');
        string path = bracket > 0 && address.EndsWith("]", StringComparison.Ordinal)
            ? address.Substring(0, bracket) : address;
        path = path.Replace('\\', '/');
        if (preloadedTextures.TryGetValue(path, out AssetDatabaseLoadOperation operation)) return operation;
        if (AssetImporter.GetAtPath(path) is TextureImporter)
        {
            using (EditorTextureRequestMarker.Auto())
                operation = AssetDatabase.LoadObjectAsync(path, ImportedTextureMainLocalId);
        }
        preloadedTextures.Add(path, operation);
        return operation;
    }

    /// <summary>
    /// 编辑器 Fast Mode 直接解析 Sprite，避免 Addressables 1.22.3 的 AssetDatabaseProvider 子资源空引用。
    /// </summary>
    public static bool TryLoadEditorSprite(string address, out Sprite sprite, out string error)
        => TryLoadEditorSprite(address, null, out sprite, out error);

    private static bool TryLoadEditorSprite(string address,
        IDictionary<string, EditorSpriteAssetSet> loadedAssets, out Sprite sprite, out string error)
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

        if (loadedAssets == null || !loadedAssets.TryGetValue(assetPath, out EditorSpriteAssetSet assetSet))
        {
            assetSet = new EditorSpriteAssetSet
            {
                MainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath),
                MainSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath)
            };
            loadedAssets?.Add(assetPath, assetSet);
        }

        if (assetSet.MainAsset == null)
        {
            error = $"Sprite Addressable '{key}' 的主资源无法加载：{assetPath}。";
            return false;
        }

        if (string.IsNullOrEmpty(subObjectName))
        {
            sprite = assetSet.MainSprite;
        }
        else
        {
            if (assetSet.MainSprite != null &&
                string.Equals(assetSet.MainSprite.name, subObjectName, StringComparison.Ordinal))
                sprite = assetSet.MainSprite;

            if (sprite == null)
            {
                if (loadedAssets == null)
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
                else
                {
                    if (assetSet.Representations == null)
                    {
                        assetSet.Representations = new Dictionary<string, Sprite>(StringComparer.Ordinal);
                        foreach (UnityEngine.Object representation in
                                 AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
                        {
                            if (representation is Sprite candidate)
                                assetSet.Representations.TryAdd(candidate.name, candidate);
                        }
                    }
                    assetSet.Representations.TryGetValue(subObjectName, out sprite);
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
            result.Remove("formerIds");
            // processing 描述具体物品受到加工后的结果；模板 parent 只复用静态配置，不能把加工产物串给子物品。
            result.Remove("processing");
        }

        RemoveReplacedModuleBodies(result, source);
        result.Merge(source, new JsonMergeSettings
        {
            MergeArrayHandling = MergeArrayHandling.Replace,
            MergeNullValueHandling = MergeNullValueHandling.Merge
        });
        RemoveInheritedModules(result, source, id);
        result["id"] = id;
        result["abstract"] = source.Value<bool?>("abstract") ?? false;
        result.Remove("parent");
        result.Remove("removeModules");

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

    /// <summary>子定义显式删除父模板物种专属模块，避免外壳与 JSON 再实例化旧机制。</summary>
    private static void RemoveInheritedModules(JObject result, JObject source, string id)
    {
        JToken removalToken = source["removeModules"];
        if (removalToken == null)
            return;
        if (removalToken is not JArray removals || result["modules"] is not JObject modules)
            throw new InvalidDataException($"物品 {id} removeModules 必须是有效模块名数组。");

        foreach (JToken token in removals)
        {
            if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
                throw new InvalidDataException($"物品 {id} removeModules 包含无效模块名。");
            string moduleName = token.Value<string>().Trim();
            if (!modules.Remove(moduleName))
                throw new InvalidDataException($"物品 {id} removeModules 未找到父模块：{moduleName}");
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
        IReadOnlyDictionary<string, Material> preloadedMaterials = null,
        ItemModulePrototypeCache prototypeCache = null)
    {
        string id = dto.Id?.Trim();
        string shellId = dto.ShellPrefab?.Trim();
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("物品定义 ID 为空");
        if (string.IsNullOrWhiteSpace(shellId))
            throw new InvalidDataException($"物品 {id} 缺少 shellPrefab");
        dto.Visual?.LiquidSurface?.Validate(id);

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
        template.MatterState = new ItemMatterState(); // 连续物质状态属于实例，不继承外壳或静态定义。
        PopulateTemplateData(dto.ItemData, template, id);
        template.IDName = id;
        template.Guid = 0;
        if (string.IsNullOrWhiteSpace(dto.GameName))
            throw new InvalidDataException($"物品 {id} 缺少默认显示名 gameName，不能使用 ID 或外壳名替代。");
        template.GameName = dto.GameName.Trim();
        if (dto.Description != null) template.Description = dto.Description;
        if (dto.Durability.HasValue) template.Durability = dto.Durability.Value;
        if (dto.MaxDurability.HasValue) template.MaxDurability = dto.MaxDurability.Value;
        if (dto.Tags != null)
        {
            template.Tags = new List<string>(dto.Tags.Count);
            for (int tagIndex = 0; tagIndex < dto.Tags.Count; tagIndex++)
            {
                string tagId = dto.Tags[tagIndex]?.Trim();
                if (!ItemTagLocalizationCatalog.IsStableId(tagId))
                {
                    throw new InvalidDataException(
                        $"物品 {id} 的 Tag 必须使用稳定 ASCII ID，不能直接保存本地化文本：{dto.Tags[tagIndex]}");
                }

                template.Tags.Add(tagId);
            }
        }

        template.Stack ??= new ItemStack();
        if (!isActor)
        {
            if (!dto.Weight.HasValue || dto.Weight.Value <= 0f)
                throw new InvalidDataException($"物品 {id} 缺少有效重量 weight");
            if (!dto.Volume.HasValue || dto.Volume.Value <= 0f)
                throw new InvalidDataException($"物品 {id} 缺少有效体积 volume");
            if (!dto.Stackable.HasValue)
                throw new InvalidDataException($"物品 {id} 缺少明确的堆叠属性 stackable");
        }
        if (dto.Amount.HasValue) template.Stack.Amount = dto.Amount.Value;
        if (dto.Volume.HasValue) template.Stack.Volume = dto.Volume.Value;
        if (dto.Weight.HasValue) template.Stack.Weight = dto.Weight.Value;
        if (dto.Stackable.HasValue) template.Stack.Stackable = dto.Stackable.Value;
        if (dto.CanBePickedUp.HasValue) template.Stack.CanBePickedUp = dto.CanBePickedUp.Value;
        if (dto.RequiredGroundSupport < 0)
            throw new InvalidDataException($"物品 {id} 的 requiredGroundSupport 不能为负数");

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

            bool resourceExtension = string.Equals(dto.EntityRuntime?.Trim(), "resource", StringComparison.OrdinalIgnoreCase) &&
                ResourceEntityCapabilityRegistry.IsRegistered(moduleId);
            Module prototype = resourceExtension ? null :
                ResolveModulePrototype(gameRes, shell, moduleId, moduleDto.Id, prototypeCache);
            ModuleData moduleData;
            if (resourceExtension)
            {
                // 纯 ECS 扩展使用本体已支持存档的通用状态，无需制作 Module GameObject。
                moduleData = new Ex_ModData();
            }
            else
            {
                if (prototype?._Data == null)
                    throw new InvalidDataException($"物品 {id} 找不到模块 Prefab/外壳模块：{moduleId}");
                // 以 object 作为泛型实参，避免深拷贝按 ModuleData 静态类型退化为错误的 Ex_ModData。
                moduleData = FastCloner.FastCloner.DeepClone<object>(prototype._Data) as ModuleData;
                if (moduleData == null || moduleData.GetType() != prototype._Data.GetType())
                    throw new InvalidDataException(
                        $"物品 {id} 的模块 {moduleName} 数据类型复制失败：期望 {prototype._Data.GetType().Name}，实际 {moduleData?.GetType().Name ?? "null"}");
            }
            PopulateModuleData(moduleDto.Data, moduleData, id, moduleName);
            moduleData.StableName = moduleName;
            moduleData.ModuleId = string.IsNullOrWhiteSpace(moduleDto.Id)
                ? (!string.IsNullOrWhiteSpace(prototype?._Data?.ModuleId) ? prototype._Data.ModuleId : moduleId)
                : moduleDto.Id.Trim();
            if (moduleDto.Enabled.HasValue)
                moduleData.Enabled = moduleDto.Enabled.Value;
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

            moduleParameters.Add(moduleName, parameters?.ToString(Formatting.None));
            modulePrefabIds.Add(moduleName, moduleId);
        }

        if (!isActor) EquipmentDurabilityRules.ApplyDefaults(dto, template);

        AddAutomaticHealthModule(
            gameRes,
            shell,
            dto.Health,
            lootTable,
            id,
            template,
            moduleParameters,
            modulePrefabIds,
            prototypeCache);
        lootTableBound |= lootTable != null && dto.Health?.HasHp == true;
        if (lootTable != null && !lootTableBound)
            throw new InvalidDataException($"物品 {id} 引用了战利品表 {lootTable.Id}，但没有 Mod_DamageReceiver");

        // Actor 与普通物品共用正式 Sprite 解析，单图控制器在首次启用前就能取得首帧。
        Sprite sprite = ResolveSprite(gameRes, dto.Visual?.SpriteAddress, id, preloadedSprites);
        if (isActor && sprite == null)
            throw new InvalidDataException($"Actor {id} 缺少初始 Sprite，请配置 visual.spriteAddress。");
        if (dto.Visual?.LiquidSurface != null && (sprite == null || sprite.texture == null || !sprite.texture.isReadable))
            throw new InvalidDataException($"物品 {id} 配置了 visual.liquidSurface，但基础 Sprite 没有启用 Read/Write。");
        Dictionary<string, Sprite> stateSprites = isActor
            ? new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase)
            : ResolveVisualStateSprites(gameRes, dto.Visual?.SpriteStates, id, preloadedSprites);
        Material material = ResolveMaterial(gameRes, dto.Visual?.MaterialAddress, id, preloadedMaterials) ??
                            ResolveShellRendererMaterial(shell, dto.Visual?.RendererPath);
        RuntimeAnimatorController animatorController = ResolveAnimatorController(
            gameRes, dto.Visual?.AnimatorControllerAddress,
            id,
            preloadedControllers);
        if (dto.GroundCover && (isActor || sprite == null || template.Stack?.CanBePickedUp != true))
            throw new InvalidDataException($"地表植被 {id} 必须提供 Sprite，并定义为可拾取的普通物品。");
        if (dto.GroundCover) ValidateGroundCoverWorldSortingConfiguration(id, dto.Visual);
        return new RuntimeItemDefinition(
            id,
            shellId,
            shell,
            template,
            dto.Visual,
            dto.Health,
            lootTable?.Id,
            dto.WaterEntryTransformItemId,
            sprite,
            moduleParameters,
            modulePrefabIds,
            dto.LabelKey,
            dto.DescriptionKey,
            animatorController,
            isActor,
            material,
            stateSprites,
            dto.GroundCover,
            ResolveWorldGridOccupancy(dto.WorldGridOccupancy, id),
            dto.RequiredGroundSupport,
            dto.Ecs,
            dto.EntityRuntime,
            ResolveWorldDropBehavior(dto.WorldDropBehavior, id),
            ResolveProcessingDefinitions(dto.Processing, id),
            ResolveProcessingCapabilityLevels(dto.ProcessingCapabilities, id),
            dto.FormerIds,
            ItemMatterReactionCompiler.CompileMatter(dto.Matter, dto.Tags, id),
            ItemMatterReactionCompiler.CompileReactions(dto.Reactions, id),
            gameRes,
            prototypeCache,
            dto.Physics);
    }

    private static Dictionary<string, int> ResolveProcessingCapabilityLevels(
        Dictionary<string, ItemProcessingCapabilityDto> source,
        string itemId)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
            return result;

        foreach (KeyValuePair<string, ItemProcessingCapabilityDto> pair in source)
        {
            string capability = pair.Key?.Trim();
            int level = pair.Value?.Level ?? 0;
            if (string.IsNullOrWhiteSpace(capability) || level < 1)
                throw new InvalidDataException($"物品 {itemId} 包含无效加工发出能力");
            if (!result.TryAdd(capability, level))
                throw new InvalidDataException($"物品 {itemId} 重复声明加工发出能力：{capability}");
        }
        return result;
    }

    /// <summary>把物品内聚的加工响应编译为稳定 RuntimeRecipe，具体设备只负责提供能力和工作量。</summary>
    private static Dictionary<string, RuntimeItemProcessingDefinition> ResolveProcessingDefinitions(
        Dictionary<string, ItemProcessingDefinitionDto> source,
        string itemId)
    {
        var result = new Dictionary<string, RuntimeItemProcessingDefinition>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
            return result;

        foreach (KeyValuePair<string, ItemProcessingDefinitionDto> pair in source)
        {
            string capability = pair.Key?.Trim();
            ItemProcessingDefinitionDto definition = pair.Value;
            if (string.IsNullOrWhiteSpace(capability) || definition == null)
                throw new InvalidDataException($"物品 {itemId} 包含空加工能力定义");
            if (definition.InputAmount < 1 || float.IsNaN(definition.Work) || float.IsInfinity(definition.Work) ||
                definition.Work <= 0f || definition.Outputs == null || definition.Outputs.Count == 0)
            {
                throw new InvalidDataException($"物品 {itemId} 的加工能力 {capability} 参数无效");
            }
            bool hasMinLevel = definition.MinLevel.HasValue;
            bool hasMaxLevel = definition.MaxLevel.HasValue;
            if (hasMinLevel != hasMaxLevel ||
                hasMinLevel && (definition.MinLevel.Value < 1 || definition.MaxLevel.Value < definition.MinLevel.Value))
            {
                throw new InvalidDataException(
                    $"物品 {itemId} 的加工能力 {capability} 必须声明有效的 minLevel/maxLevel 闭区间");
            }

            var outputs = new List<RuntimeRecipeResult>(definition.Outputs.Count);
            foreach (ItemProcessingOutputDto output in definition.Outputs)
            {
                string outputId = output?.ItemId?.Trim();
                float durabilityMultiplier = output?.DurabilityMultiplier ?? CraftedDurabilityQuality.DefaultMultiplier;
                if (output == null || string.IsNullOrWhiteSpace(outputId) || output.Amount < 1 ||
                    float.IsNaN(durabilityMultiplier) || float.IsInfinity(durabilityMultiplier) || durabilityMultiplier <= 0f)
                {
                    throw new InvalidDataException($"物品 {itemId} 的加工能力 {capability} 包含无效产物");
                }

                outputs.Add(new RuntimeRecipeResult
                {
                    ItemName = outputId,
                    amount = output.Amount,
                    durabilityMultiplier = durabilityMultiplier
                });
            }

            if (!result.TryAdd(capability, new RuntimeItemProcessingDefinition(
                    itemId, capability, definition.InputAmount, definition.Work, outputs,
                    definition.MinLevel, definition.MaxLevel)))
            {
                throw new InvalidDataException($"物品 {itemId} 重复声明加工能力：{capability}");
            }
        }

        return result;
    }

    private static WorldDropBehavior ResolveWorldDropBehavior(string value, string itemId)
    {
        if (string.IsNullOrWhiteSpace(value))
            return WorldDropBehavior.Passive;

        return value.Trim().ToLowerInvariant() switch
        {
            "passive" => WorldDropBehavior.Passive,
            "interactive" => WorldDropBehavior.Interactive,
            _ => throw new InvalidDataException(
                $"物品 {itemId} 的 worldDropBehavior 只能是 passive 或 interactive：{value}")
        };
    }

    /// <summary>校验并转换配置中的整数占格，禁止空格、重复格或无效条目进入运行时定义。</summary>
    private static GridCellOffset[] ResolveWorldGridOccupancy(WorldGridOccupancyData occupancy, string itemId)
    {
        if (occupancy == null)
            return Array.Empty<GridCellOffset>();

        if (occupancy.Cells == null || occupancy.Cells.Count == 0)
            throw new InvalidDataException($"物品 {itemId} 声明了 worldGridOccupancy，但 cells 为空。");

        var offsets = new GridCellOffset[occupancy.Cells.Count];
        var uniqueOffsets = new HashSet<GridCellOffset>();
        for (int i = 0; i < occupancy.Cells.Count; i++)
        {
            WorldGridOccupancyCellData cell = occupancy.Cells[i];
            if (cell == null)
                throw new InvalidDataException($"物品 {itemId} 的 worldGridOccupancy.cells[{i}] 为空。");

            GridCellOffset offset = new(cell.X, cell.Y);
            if (!uniqueOffsets.Add(offset))
                throw new InvalidDataException(
                    $"物品 {itemId} 的 worldGridOccupancy 重复声明格偏移 ({cell.X}, {cell.Y})。");
            offsets[i] = offset;
        }

        return offsets;
    }

    /// <summary>解析并固化额外视觉状态 Sprite，运行时模块只按状态名读取，不再重复发资源请求。</summary>
    private static Dictionary<string, Sprite> ResolveVisualStateSprites(
        GameRes gameRes,
        IReadOnlyDictionary<string, string> stateAddresses,
        string itemId,
        IReadOnlyDictionary<string, Sprite> preloadedSprites)
    {
        var result = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        if (stateAddresses == null)
            return result;

        foreach (KeyValuePair<string, string> pair in stateAddresses)
        {
            string stateName = pair.Key?.Trim();
            if (string.IsNullOrWhiteSpace(stateName))
                throw new InvalidDataException($"物品 {itemId} 的 visual.spriteStates 包含空状态名。");
            if (string.IsNullOrWhiteSpace(pair.Value))
                throw new InvalidDataException($"物品 {itemId} 的视觉状态 {stateName} 缺少 Sprite 地址。");
            if (!result.TryAdd(stateName, ResolveSprite(gameRes, pair.Value, itemId, preloadedSprites)))
                throw new InvalidDataException($"物品 {itemId} 的视觉状态重复：{stateName}");
        }

        return result;
    }

    private static void AddAutomaticHealthModule(
        GameRes gameRes,
        GameObject shell,
        ItemHealthDefinitionDto health,
        RuntimeLootTable lootTable,
        string itemId,
        ItemData template,
        Dictionary<string, string> moduleParameters,
        Dictionary<string, string> modulePrefabIds,
        ItemModulePrototypeCache prototypeCache)
    {
        if (health == null || !health.HasHp) return;

        const string moduleName = "生命值系统模块";
        const string prefabId = "Module_DamageReciver";
        if (template.ModuleDataDic.ContainsKey(moduleName) || modulePrefabIds.Values.Any(value => string.Equals(value, prefabId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"物品 {itemId} 同时声明 health 和显式 {prefabId} 模块；请只保留 health 声明");

        Module prototype = ResolveModulePrototype(gameRes, shell, prefabId, prototypeCache: prototypeCache);
        if (prototype?._Data == null)
            throw new InvalidDataException($"物品 {itemId} 的 health 找不到模块 Prefab：{prefabId}");

        ModuleData moduleData = FastCloner.FastCloner.DeepClone<object>(prototype._Data) as ModuleData;
        if (moduleData == null || moduleData.GetType() != prototype._Data.GetType())
            throw new InvalidDataException($"物品 {itemId} 的 health 模块数据复制失败");
        moduleData.StableName = moduleName;
        moduleData.ModuleId = !string.IsNullOrWhiteSpace(prototype._Data.ModuleId) ? prototype._Data.ModuleId : moduleName;
        moduleData.Enabled = true;
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
                ["CombatBalanceVersion"] = health.CombatBalanceVersion,
                ["DefenseValues"] = new JObject
                {
                    ["Physical"] = defense.Physical
                }
            },
            ["weakTool"] = (int)health.WeakTool
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

    /// <summary>把表引用展开到显式 Mod_DamageReceiver 参数，并拒绝两套掉落来源并存。</summary>
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
            throw new InvalidDataException($"物品 {itemId} 的 Mod_DamageReceiver.Data 必须是对象");
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
            using var reader = data.CreateReader();
            JsonSerializer.Create(TemplateSettings).Populate(reader, template);
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
            using var reader = data.CreateReader();
            JsonSerializer.Create(TemplateSettings).Populate(reader, moduleData);
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

    private static Module ResolveModulePrototype(GameRes gameRes, GameObject shell, string prefabId,
        string moduleId = null, ItemModulePrototypeCache prototypeCache = null)
        => FindModulePrototype(shell, gameRes.GetPrefab(prefabId, false),
            string.IsNullOrWhiteSpace(moduleId) ? prefabId : moduleId.Trim(), prototypeCache);

    /// <summary>构建、运行时校验和编辑器预检共用模块定位规则，支持外壳内嵌模块的持久化身份。</summary>
    public static Module FindModulePrototype(GameObject shell, GameObject modulePrefab, string moduleId,
        ItemModulePrototypeCache prototypeCache = null)
    {
        if (modulePrefab != null)
        {
            Module[] candidates;
            bool containsItem;
            if (prototypeCache != null)
                prototypeCache.GetPrefabComponents(modulePrefab, out candidates, out containsItem);
            else
            {
                containsItem = modulePrefab.GetComponentInChildren<Item>(true) != null;
                candidates = modulePrefab.GetComponentsInChildren<Module>(true);
            }
            if (containsItem)
                throw new InvalidDataException($"模块 Prefab {modulePrefab.name} 不能包含 Item，物品必须保留独立所有权。");
            Module matched = FindUniquePrototype(candidates, moduleId, modulePrefab.name);
            if (matched != null)
                return matched;
            if (candidates.Length == 1)
                return candidates[0];
            if (candidates.Length > 1)
                throw new InvalidDataException($"模块 Prefab {modulePrefab.name} 包含多个 Module，找不到唯一能力 {moduleId}。");
        }

        if (shell == null) return null;
        Module[] shellModules = prototypeCache != null
            ? prototypeCache.GetOwnedShellComponents(shell)
            : Array.FindAll(shell.GetComponentsInChildren<Module>(true),
                module => ItemModuleAssemblyCompiler.IsOwnedByRoot(module, shell.transform));
        return FindUniquePrototype(shellModules, moduleId, shell.name);
    }

    /// <summary>多模块宿主必须唯一命中，禁止目录顺序决定具体实现。</summary>
    private static Module FindUniquePrototype(Module[] candidates, string moduleId, string hostName)
    {
        Module matched = null;
        for (int index = 0; index < candidates.Length; index++)
        {
            Module candidate = candidates[index];
            if (candidate == null || !candidate.MatchesPersistedId(moduleId))
                continue;
            if (matched != null)
                throw new InvalidDataException($"外壳/模块 Prefab {hostName} 的能力 {moduleId} 存在多个实现，请使用确定的 Prefab 与稳定模块名。");
            matched = candidate;
        }
        return matched;
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
