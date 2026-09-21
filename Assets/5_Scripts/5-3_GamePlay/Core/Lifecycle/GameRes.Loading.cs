using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlatWorld.Gameplay.Quests;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>资源会话边界：单次加载、主菜单重载、失败清理与销毁，统一管理全部本体句柄。</summary>
public partial class GameRes
{
    #region 会话状态

    /// <summary>资源会话的权威状态。</summary>
    [Sirenix.OdinInspector.ShowInInspector] public ResourceLoadState LoadState { get; private set; }
    /// <summary>主菜单交互所需的最小资源是否已经就绪；完整世界资源仍可能在后台加载。</summary>
    [Sirenix.OdinInspector.ShowInInspector] public bool IsStartupReady { get; private set; }
    /// <summary>完整资源会话的当前总进度，供世界加载页在抢跑进入时复用。</summary>
    public float LoadProgress => loadingProgress;
    /// <summary>完整资源会话的当前阶段文案。</summary>
    public string LoadStatusText => loadingText ?? string.Empty;
    /// <summary>最后一次失败的阶段与原因。</summary>
    [Sirenix.OdinInspector.ShowInInspector] public string LastLoadError { get; private set; }
    /// <summary>当前或最后执行的阶段 ID。</summary>
    [Sirenix.OdinInspector.ShowInInspector] public string CurrentLoadStage => loadPipeline?.CurrentStage;
    private Coroutine loadCoroutine;
    private Coroutine inGameReloadCoroutine;
    private ResourceLoadPipeline loadPipeline;
    private ResourceAssetScope resourceAssets = new();
    internal ResourceAssetScope ResourceAssets => resourceAssets;
    /// <summary>启动阶段已经持有的 Prefab 位置，完整 Prefab 阶段不再重复申请句柄。</summary>
    private readonly HashSet<string> startupPrefabLocationIds = new(StringComparer.Ordinal);

    /// <summary>
    /// 主菜单显示后仍可能立即被访问的运行时 UI。
    /// 主菜单、存档页和新建世界页本身由 WorldManager 直接引用，不需要经过 GameRes。
    /// </summary>
    private static readonly string[] StartupPrefabKeys =
    {
        RuntimeUIPrefabKeys.MainMenuSettings,
        RuntimeUIPrefabKeys.MainMenuExitConfirmation,
        RuntimeUIPrefabKeys.InputBindingRow,
        RuntimeUIPrefabKeys.WorldLoading,
        RuntimeUIPrefabKeys.MobileControls,
        RuntimeUIPrefabKeys.MobileControlLayoutEditor
    };

    /// <summary>
    /// F5/调试入口统一使用这里：主菜单直接重载；单机世界内先保存并退出运行态，
    /// 再重载资源并自动回到同一存档，避免释放仍被活跃 Item 引用的 Addressables。
    /// </summary>
    public bool RequestResourceReload()
    {
        if (LoadState == ResourceLoadState.Loading ||
            LoadState == ResourceLoadState.Disposed ||
            inGameReloadCoroutine != null)
        {
            return false;
        }

        if (!Application.isPlaying)
        {
            Debug.LogWarning("[GameRes] 资源重载只在运行时执行；编辑器内容检查请使用目录校验菜单。");
            return false;
        }

        GameManager manager = FindFirstObjectByType<GameManager>();
        if (manager == null || !manager.IsInGameWorld)
            return TryReloadResources();

        if (manager.IsWorldEntryInProgress)
        {
            Debug.LogWarning("[GameRes] 正在进入世界，暂不能执行 F5 资源重载。");
            return false;
        }

        // 联机状态需要由服务器统一协调资源版本；本地单边热重载会直接造成内容目录不一致。
        if (GameNetwork.IsOnline)
        {
            Debug.LogWarning("[GameRes] 联机世界不允许本地 F5 热重载资源，请先结束联机会话。");
            return false;
        }

        SaveDataMgr saveDataMgr = SaveDataMgr.Instance;
        ItemMgr itemMgr = ItemMgr.GetInstance();
        string saveName = saveDataMgr?.SaveData?.saveName;
        string playerName = saveDataMgr?.CurrentContrrolPlayerName;
        Player player = itemMgr?.User_Player;
        if (saveDataMgr == null ||
            string.IsNullOrWhiteSpace(saveName) ||
            string.IsNullOrWhiteSpace(playerName) ||
            player == null)
        {
            Debug.LogWarning("[GameRes] 当前世界缺少可恢复的存档或本地玩家，已取消 F5 资源重载。");
            return false;
        }

        string savePath = saveDataMgr.GetFullSavePath(saveName);
        showLoadingGUI = true;
        loadingProgress = 0f;
        loadingText = "正在保存当前世界并准备更新资源...";
        inGameReloadCoroutine = StartCoroutine(
            ReloadResourcesAndResumeWorld(manager, player, playerName, saveName, savePath));
        return true;
    }

    /// <summary>底层资源会话入口；调用前必须保证世界运行态与活跃 Item 已经清空。</summary>
    public bool TryReloadResources()
    {
        if (LoadState == ResourceLoadState.Loading || LoadState == ResourceLoadState.Disposed) return false;
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[GameRes] 资源重载只在运行时执行；编辑器内容检查请使用目录校验菜单。");
            return false;
        }
        GameManager manager = FindFirstObjectByType<GameManager>();
        ItemMgr items = ItemMgr.GetInstance();
        if ((manager != null && (manager.IsInGameWorld || manager.IsWorldEntryInProgress)) ||
            (items != null && items.WorldRunTimeItems.Values.Any(item => item != null)))
        {
            Debug.LogWarning("[GameRes] 底层资源会话仍有世界运行态引用；游戏内请通过 F5/RequestResourceReload 执行安全重载。");
            return false;
        }

        LoadState = ResourceLoadState.Loading;
        IsStartupReady = false;
        LastLoadError = null;
        resourceLoadFailed = false;
        showLoadingGUI = true;
        loadingProgress = 0;
        loadingText = "准备资源加载会话";
        Coroutine started = StartCoroutine(RunResourceSession());
        loadCoroutine = LoadState == ResourceLoadState.Loading ? started : null;
        return true;
    }

    [Sirenix.OdinInspector.Button("重载所有资源")]
    public void HotReloadAllResources() => RequestResourceReload();

    /// <summary>单机世界内的安全热重载：完整保存/清场后重建内容目录，再加载同一存档。</summary>
    private IEnumerator ReloadResourcesAndResumeWorld(
        GameManager manager,
        Player player,
        string playerName,
        string saveName,
        string savePath)
    {
        Debug.Log($"[GameRes] F5 游戏内资源重载开始：存档={saveName}，玩家={playerName}");

        yield return manager.BackToHelloScene_Coroutine(player, saveCurrentGame: true);

        // 退出流程会将当前 SaveData 重置为空对象；确认运行态已经清空后才释放旧资源句柄。
        ItemMgr.GetInstance()?.CleanupNullItems();
        if (manager.IsInGameWorld ||
            (ItemMgr.GetInstance() != null && ItemMgr.GetInstance().WorldRunTimeItems.Values.Any(item => item != null)))
        {
            Debug.LogError("[GameRes] F5 资源重载中止：退出世界后仍存在运行态 Item，旧资源会话未释放。");
            showLoadingGUI = false;
            inGameReloadCoroutine = null;
            yield break;
        }

        loadingText = "正在更新游戏资源...";
        loadingProgress = 0f;
        showLoadingGUI = true;
        if (!TryReloadResources())
        {
            Debug.LogError("[GameRes] F5 资源重载中止：无法启动新的资源会话。");
            showLoadingGUI = false;
            inGameReloadCoroutine = null;
            yield break;
        }

        while (LoadState == ResourceLoadState.Loading)
            yield return null;

        if (LoadState != ResourceLoadState.Ready)
        {
            Debug.LogError($"[GameRes] F5 资源重载失败，已停留在主菜单：{LastLoadError}");
            inGameReloadCoroutine = null;
            yield break;
        }

        SaveDataMgr saveDataMgr = SaveDataMgr.Instance;
        if (saveDataMgr == null)
        {
            Debug.LogError("[GameRes] F5 资源重载完成，但 SaveDataMgr 不存在，无法恢复原存档。");
            inGameReloadCoroutine = null;
            yield break;
        }

        loadingText = "资源更新完成，正在恢复当前存档...";
        showLoadingGUI = true;
        saveDataMgr.LoadSaveByDisk(savePath);
        if (saveDataMgr.SaveData == null ||
            !string.Equals(saveDataMgr.SaveData.saveName, saveName, StringComparison.Ordinal) ||
            saveDataMgr.SaveData.PlayerData_Dict == null ||
            !saveDataMgr.SaveData.PlayerData_Dict.ContainsKey(playerName))
        {
            Debug.LogError($"[GameRes] F5 资源重载完成，但无法恢复存档 {saveName} 的玩家 {playerName}。");
            showLoadingGUI = false;
            inGameReloadCoroutine = null;
            yield break;
        }

        showLoadingGUI = false;
        inGameReloadCoroutine = null;
        Debug.Log($"[GameRes] F5 资源更新完成，正在重新进入存档：{saveName}");
        manager.ContinueGame(playerName);
    }

    /// <summary>先释放旧会话，再执行声明式计划；任何失败都清空目录并保持不可进入世界。</summary>
    private IEnumerator RunResourceSession()
    {
        Exception prepareError = null;
        try
        {
            ItemMgr.GetInstance()?.ClearResourcePools();
            ClearResourceSession();
            resourceAssets = new ResourceAssetScope();
            loadPipeline = CreateResourceLoadPlan();
        }
        catch (Exception exception) { prepareError = exception; }
        if (prepareError != null)
        {
            FailResourceSession("prepare", prepareError);
            yield break;
        }

        yield return loadPipeline.Run();
        if (loadPipeline.Failure != null)
        {
            FailResourceSession(loadPipeline.CurrentStage, loadPipeline.Failure);
            yield break;
        }
        LoadState = ResourceLoadState.Ready;
        IsStartupReady = true;
        loadCoroutine = null;
        loadingProgress = 1;
        showLoadingGUI = false;
        Debug.Log($"[GameRes] 资源目录就绪：{ItemDefinitions.Count} 个物品/Actor，{TileBlockDict.Count} 个 TileBlock，" +
                  $"{recipeById.Count} 个配方，持有 {resourceAssets.HandleCount} 个资源请求。");
    }

    /// <summary>保留原始异常与阶段，清理异常作为附加原因记录，不覆盖首个失败。</summary>
    private void FailResourceSession(string stage, Exception exception)
    {
        try { ClearResourceSession(); }
        catch (Exception cleanupError) { exception = new AggregateException(exception, cleanupError); }
        LoadState = ResourceLoadState.Failed;
        IsStartupReady = false;
        resourceLoadFailed = true;
        loadCoroutine = null;
        LastLoadError = $"阶段 {stage} 失败：{exception.Message}";
        loadingText = $"资源加载失败（{stage}），修正配置后可按 F5 重试";
        showLoadingGUI = true;
        Debug.LogError($"[GameRes] {LastLoadError}\n{exception}", this);
    }

    /// <summary>销毁前取消嵌套流程；重复场景实例不能释放正式实例的目录。</summary>
    private void DisposeResourceSession()
    {
        if (instance != this) return;
        LoadState = ResourceLoadState.Disposed;
        if (loadCoroutine != null) StopCoroutine(loadCoroutine);
        loadCoroutine = null;
        loadPipeline?.Dispose();
        try { ClearResourceSession(); }
        catch (Exception exception) { Debug.LogException(exception, this); }
    }

    /// <summary>依赖方先卸载，目录再清空，最后释放底层资源；每项清理都独立执行。</summary>
    private void ClearResourceSession()
    {
        var errors = new List<Exception>();
        void Clear(Action action) { try { action(); } catch (Exception exception) { errors.Add(exception); } }
        // 先让使用者释放 BRG 注册/材质，再销毁共享 Mesh，最后卸载源 Sprite/Tile/MOD。
        Clear(SharedSpriteMeshCache.Clear);
        Clear(() => ModRuntimeManager.Instance?.UnloadForResourceReload());
        Clear(ClearAllDictionaries);
        Clear(PlayerCreationTemplateCatalogService.Reset);
        Clear(TimeSystemConfigService.Reset);
        Clear(MechanicalCatalog.Clear);
        Clear(() => resourceAssets.Dispose());
        LoadedCount = 0;
        IsStartupReady = false;
        startupPrefabLocationIds.Clear();
        if (errors.Count > 0) throw new AggregateException("资源会话清理失败。", errors);
    }

    /// <summary>丢弃当前目录及其派生索引；不会在清理时创建新的管理器。</summary>
    private void ClearAllDictionaries()
    {
        AllPrefabs.Clear();
        ItemDefinitions.Clear();
        ActorDefinitions.Clear();
        LootTables.Clear();
        ActorDefinitionCatalogLoader.ResetRuntimeCatalog();
        SpawnerConfigCatalogService.Reset();
        recipeDict.Clear();
        recipeCatalog.Clear();
        tileBaseDict.Clear();
        ClearTileDefinitions();
        BuffDefinitions.Clear();
        ContaminationDefinitions.Clear();
        LiquidDefinitions.Clear();
        LiquidTypes = null;
        AnimalSkillCatalogService.Reset();
        QuestCatalog.Reset();
        textLibraryService = TextLibraryService.Empty;
        InventoryInitDict.Clear();
        SkillDict.Clear();
    }

    #endregion

    #region 通用资源阶段

    /// <summary>显式等待 Addressables 初始化，不把初始化异常误归因到首个物品。</summary>
    private IEnumerator InitializeAddressableCatalog()
    {
        var handle = resourceAssets.Own(Addressables.InitializeAsync(false));
        yield return handle;
        ResourceAssetScope.Require(handle, "Addressables 初始化目录");
    }

    /// <summary>位置查询使用短期句柄；结果去重依据实际资源与类型，别名不能掩盖资源冲突。</summary>
    private IEnumerator ResolveLocations(string[] labels, Type type, bool required, Action<List<IResourceLocation>> completed)
    {
        var handle = Addressables.LoadResourceLocationsAsync((IEnumerable)labels, Addressables.MergeMode.Union, type);
        try
        {
            yield return handle;
            IList<IResourceLocation> found = ResourceAssetScope.Require(handle, $"标签 {string.Join(", ", labels)} ({type.Name})");
            var locations = found.Where(location => location != null)
                .GroupBy(location => (location.InternalId, location.ProviderId, location.ResourceType))
                .Select(group => group.First()).OrderBy(location => location.PrimaryKey, StringComparer.Ordinal).ToList();
            if (required && locations.Count == 0)
                throw new InvalidDataException($"必需标签 {string.Join(", ", labels)} 未解析到 {type.Name}；" +
                    $"当前目录：{string.Join(", ", Addressables.ResourceLocators.Select(locator => locator.LocatorId))}");
            completed(locations);
        }
        finally { if (handle.IsValid()) Addressables.Release(handle); }
    }

    /// <summary>标签组统一按显式主键注册；重复 ID 直接报出冲突资源。</summary>
    private IEnumerator LoadAssetGroup<T>(string label, IDictionary<string, T> target, Func<T, string> key, bool required)
        where T : UnityEngine.Object
    {
        List<IResourceLocation> locations = null;
        yield return ResolveLocations(new[] { label }, typeof(T), required, value => locations = value);
        if (locations.Count == 0) yield break;
        var handle = resourceAssets.Own(Addressables.LoadAssetsAsync<T>(locations, null, true));
        while (!handle.IsDone) { loadPipeline.Report(handle.PercentComplete * 0.8f); yield return null; }
        foreach (T asset in ResourceAssetScope.Require(handle, $"标签 {label}"))
        {
            if (asset == null) throw new InvalidDataException($"标签 {label} 加载出空资源。");
            string id = key(asset);
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException($"{label} 资源 {asset.name} 缺少稳定 ID。");
            if (!target.TryAdd(id, asset)) throw new InvalidDataException($"{label} ID 重复：{id} ({target[id].name} / {asset.name})");
            LoadedCount++;
        }
    }

    /// <summary>
    /// 只预载主菜单能够立刻访问的少量运行时 UI；其余玩法 Prefab 继续留给完整资源阶段。
    /// Addressables 会同时加载这些 Prefab 的字体、Sprite 等依赖，因此显示主菜单后不会出现缺失外观。
    /// </summary>
    private IEnumerator LoadStartupUiPrefabs()
    {
        List<IResourceLocation> prefabLocations = null;
        yield return ResolveLocations(new[] { "Prefab" }, typeof(GameObject), true, value => prefabLocations = value);

        var requiredNames = new HashSet<string>(StartupPrefabKeys, StringComparer.Ordinal);
        var byPrefabName = new Dictionary<string, IResourceLocation>(StringComparer.Ordinal);
        foreach (IResourceLocation location in prefabLocations)
        {
            string prefabName = GetPrefabLocationName(location);
            if (string.IsNullOrWhiteSpace(prefabName) || !requiredNames.Contains(prefabName))
                continue;
            if (byPrefabName.TryGetValue(prefabName, out IResourceLocation existing) &&
                GetLocationIdentity(existing) != GetLocationIdentity(location))
            {
                throw new InvalidDataException($"启动 UI Prefab 名称冲突：{prefabName}");
            }
            byPrefabName[prefabName] = location;
        }

        var selected = new List<IResourceLocation>(StartupPrefabKeys.Length);
        foreach (string prefabKey in StartupPrefabKeys)
        {
            if (!byPrefabName.TryGetValue(prefabKey, out IResourceLocation location))
                throw new InvalidDataException($"启动必要 UI Prefab 未登记到 Addressables/Prefab：{prefabKey}");
            selected.Add(location);
        }

        startupPrefabLocationIds.Clear();
        foreach (IResourceLocation location in selected)
            startupPrefabLocationIds.Add(GetLocationIdentity(location));

        var handle = resourceAssets.Own(Addressables.LoadAssetsAsync<GameObject>(selected, null, true));
        while (!handle.IsDone)
        {
            loadPipeline.Report(handle.PercentComplete * 0.9f);
            yield return null;
        }

        IList<GameObject> loaded = ResourceAssetScope.Require(handle, "启动必要 UI Prefab");
        var loadedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameObject prefab in loaded)
        {
            if (prefab == null)
                throw new InvalidDataException("启动必要 UI Prefab 加载出空资源。");
            RegisterPrefabAlias(prefab.name, prefab);
            loadedNames.Add(prefab.name);
            LoadedCount++;
        }

        foreach (string prefabKey in StartupPrefabKeys)
        {
            if (!loadedNames.Contains(prefabKey))
                throw new InvalidDataException($"启动必要 UI Prefab 未成功加载：{prefabKey}");
        }
    }

    /// <summary>发布主菜单可交互状态；完整内容目录继续在同一资源会话中后台推进。</summary>
    private void PublishStartupReady()
    {
        IsStartupReady = true;
        showLoadingGUI = false;
        Debug.Log("[GameRes] 启动必要资源已就绪，主菜单开放；剩余游戏资源继续后台加载。");
    }

    /// <summary>开放主菜单后至少让出一帧，避免紧接着的后台目录工作阻塞首次可交互画面。</summary>
    private IEnumerator PublishStartupReadyStage()
    {
        PublishStartupReady();
        yield return null;
    }

    /// <summary>生成稳定的位置身份，用于避免启动预载与完整 Prefab 阶段重复持有同一请求。</summary>
    private static string GetLocationIdentity(IResourceLocation location)
    {
        if (location == null)
            return string.Empty;
        return $"{location.ProviderId}\n{location.InternalId}";
    }

    /// <summary>从当前以文件路径为主键的 Addressables 位置提取 Prefab 名称。</summary>
    private static string GetPrefabLocationName(IResourceLocation location)
    {
        string key = location?.PrimaryKey;
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        string normalized = key.Replace('\\', '/');
        int slashIndex = normalized.LastIndexOf('/');
        string fileName = slashIndex >= 0 ? normalized.Substring(slashIndex + 1) : normalized;
        return fileName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            ? fileName.Substring(0, fileName.Length - 7)
            : fileName;
    }

    /// <summary>只加载通用 Prefab；Item 冗余变体和 Actor 专用外壳在位置阶段排除。</summary>
    private IEnumerator LoadPrefabCatalog(List<ItemDefinitionDto> definitions)
    {
        List<IResourceLocation> prefabs = null, actors = null;
        yield return ResolveLocations(new[] { "ItemPrefab", "Prefab" }, typeof(GameObject), true, value => prefabs = value);
        yield return ResolveLocations(new[] { ActorDefinitionCatalogLoader.ShellAddressableLabel }, typeof(GameObject), false, value => actors = value);
        HashSet<string> excluded = ItemDefinitionCatalogLoader.GetRedundantBuiltInPrefabPaths(definitions);
        var actorIds = new HashSet<string>(actors.Select(location => location.InternalId), StringComparer.Ordinal);
        var selected = prefabs.Where(location => !excluded.Contains(location.PrimaryKey.Replace('\\', '/')) &&
            !actorIds.Contains(location.InternalId) &&
            !startupPrefabLocationIds.Contains(GetLocationIdentity(location))).ToList();
        if (selected.Count == 0) throw new InvalidDataException("Prefab 计划过滤后为空，缺少运行时模块与通用外壳。");
        var handle = resourceAssets.Own(Addressables.LoadAssetsAsync<GameObject>(selected, null, true));
        while (!handle.IsDone) { loadPipeline.Report(handle.PercentComplete * 0.8f); yield return null; }
        IList<GameObject> loaded = ResourceAssetScope.Require(handle, "运行时 Prefab");
        var moduleAliases = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        // 先登记全部主名称，再处理模块别名，避免顺序决定最终命中资源。
        foreach (GameObject prefab in loaded)
        {
            if (prefab == null) throw new InvalidDataException("Prefab 计划包含空资源。");
            RegisterPrefabAlias(prefab.name, prefab);
            LoadedCount++;
        }
        foreach (GameObject prefab in loaded) CollectPrefabAliases(prefab, moduleAliases);
        RegisterUniqueModuleAliases(moduleAliases);
        Debug.Log($"[GameRes] Prefab 加载计划：后台加载 {selected.Count}，启动阶段已预载 {startupPrefabLocationIds.Count} 个 UI Prefab。");
    }

    #endregion
}
