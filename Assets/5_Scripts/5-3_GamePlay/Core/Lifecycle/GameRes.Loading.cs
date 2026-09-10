using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlatWorld.Gameplay.Quests;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>资源会话边界：单次加载、主菜单重载、失败清理与销毁，统一管理全部本体句柄。</summary>
public partial class GameRes
{
    #region 会话状态

    /// <summary>资源会话的权威状态。</summary>
    [Sirenix.OdinInspector.ShowInInspector] public ResourceLoadState LoadState { get; private set; }
    /// <summary>最后一次失败的阶段与原因。</summary>
    [Sirenix.OdinInspector.ShowInInspector] public string LastLoadError { get; private set; }
    /// <summary>当前或最后执行的阶段 ID。</summary>
    [Sirenix.OdinInspector.ShowInInspector] public string CurrentLoadStage => loadPipeline?.CurrentStage;
    private Coroutine loadCoroutine;
    private ResourceLoadPipeline loadPipeline;
    private ResourceAssetScope resourceAssets = new();
    internal ResourceAssetScope ResourceAssets => resourceAssets;

    /// <summary>所有入口共用一次请求；加载中忽略重复请求，有世界或存活实体时拒绝更换资源。</summary>
    public bool TryReloadResources()
    {
        if (LoadState == ResourceLoadState.Loading || LoadState == ResourceLoadState.Disposed) return false;
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[GameRes] 资源重载只在运行中的主菜单执行；编辑器内容检查请使用目录校验菜单。");
            return false;
        }
        GameManager manager = FindFirstObjectByType<GameManager>();
        ItemMgr items = ItemMgr.GetInstance();
        if ((manager != null && (manager.IsInGameWorld || manager.IsWorldEntryInProgress)) ||
            (items != null && items.WorldRunTimeItems.Values.Any(item => item != null)))
        {
            Debug.LogWarning("[GameRes] 请退出世界并返回主菜单后再重载资源。");
            return false;
        }

        LoadState = ResourceLoadState.Loading;
        LastLoadError = null;
        resourceLoadFailed = false;
        showLoadingGUI = true;
        loadingProgress = 0;
        loadingText = "准备资源加载会话";
        Coroutine started = StartCoroutine(RunResourceSession());
        loadCoroutine = LoadState == ResourceLoadState.Loading ? started : null;
        return true;
    }

    [Sirenix.OdinInspector.Button("重载所有资源（主菜单）")]
    public void HotReloadAllResources() => TryReloadResources();

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
        resourceLoadFailed = true;
        loadCoroutine = null;
        LastLoadError = $"阶段 {stage} 失败：{exception.Message}";
        loadingText = $"资源加载失败（{stage}），修正配置后可在主菜单按 F5 重试";
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
        Clear(() => ModRuntimeManager.Instance?.UnloadForResourceReload());
        Clear(ClearAllDictionaries);
        Clear(PlayerCreationTemplateCatalogService.Reset);
        Clear(TimeSystemConfigService.Reset);
        Clear(() => resourceAssets.Dispose());
        LoadedCount = 0;
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
        TileBlockDict.Clear();
        BuffDefinitions.Clear();
        ContaminationDefinitions.Clear();
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

    /// <summary>只加载通用 Prefab；Item 冗余变体和 Actor 专用外壳在位置阶段排除。</summary>
    private IEnumerator LoadPrefabCatalog(List<ItemDefinitionDto> definitions)
    {
        List<IResourceLocation> prefabs = null, actors = null;
        yield return ResolveLocations(new[] { "ItemPrefab", "Prefab" }, typeof(GameObject), true, value => prefabs = value);
        yield return ResolveLocations(new[] { ActorDefinitionCatalogLoader.ShellAddressableLabel }, typeof(GameObject), false, value => actors = value);
        HashSet<string> excluded = ItemDefinitionCatalogLoader.GetRedundantBuiltInPrefabPaths(definitions);
        var actorIds = new HashSet<string>(actors.Select(location => location.InternalId), StringComparer.Ordinal);
        var selected = prefabs.Where(location => !excluded.Contains(location.PrimaryKey.Replace('\\', '/')) &&
            !actorIds.Contains(location.InternalId)).ToList();
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
        Debug.Log($"[GameRes] Prefab 加载计划：加载 {selected.Count}，跳过 JSON 专用 Prefab {prefabs.Count - selected.Count}");
    }

    #endregion
}
