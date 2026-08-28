using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.Tilemaps;
using UnityEngine;
using UnityEngine.InputSystem;
using System.IO;
using System.Linq;
using FlatWorld.Gameplay.Quests;

public partial class GameRes : SingletonAutoMono<GameRes>
{
    #region 字段

    /// <summary>只返回当前已存在的资源管理器，不会因查询而自动创建实例。</summary>
    public static GameRes ExistingInstance => instance;

    public int LoadedCount = 0;
    
    [Header("资源标签列表")]
    public List<string> ADBLabels = new List<string>();

    [Header("所有预制体字典")]
    [ShowInInspector]
    public Dictionary<string, GameObject> AllPrefabs = new Dictionary<string, GameObject>();

    [Header("JSON 物品定义字典")]
    [ShowInInspector]
    public Dictionary<string, RuntimeItemDefinition> ItemDefinitions =
        new Dictionary<string, RuntimeItemDefinition>(System.StringComparer.OrdinalIgnoreCase);

    [Header("JSON Actor 定义字典")]
    [ShowInInspector]
    public Dictionary<string, RuntimeItemDefinition> ActorDefinitions =
        new Dictionary<string, RuntimeItemDefinition>(System.StringComparer.OrdinalIgnoreCase);

    [Header("JSON 战利品表字典")]
    [ShowInInspector]
    public Dictionary<string, RuntimeLootTable> LootTables =
        new Dictionary<string, RuntimeLootTable>(System.StringComparer.OrdinalIgnoreCase);

    [Header("配方字典")]
    [ShowInInspector]
    public Dictionary<string, RuntimeRecipe> recipeDict = new Dictionary<string, RuntimeRecipe>();

    [ShowInInspector]
    public IReadOnlyDictionary<string, RuntimeRecipe> recipeById => recipeCatalog.RecipesById;

    /// <summary>运行时配方的权威索引；注册时构建类型候选顺序。</summary>
    private readonly CraftingRecipeCatalog recipeCatalog = new CraftingRecipeCatalog();

    [Header("TileBase字典")]
    [ShowInInspector]
    public Dictionary<string, TileBase> tileBaseDict = new Dictionary<string, TileBase>();

    [Header("Tile地块逻辑SO字典")]
    [ShowInInspector]
    public Dictionary<string, Tile_Block> TileBlockDict = new Dictionary<string, Tile_Block>();

    [Header("Buff数据字典")]
    [ShowInInspector]
    public Dictionary<string, BuffDefinition> BuffDefinitions =
        new Dictionary<string, BuffDefinition>(System.StringComparer.OrdinalIgnoreCase);

    [System.NonSerialized]
    private TextLibraryService textLibraryService = TextLibraryService.Empty;

    /// <summary>文字库访问接口；调用方不需要依赖具体 JSON 加载实现。</summary>
    public ITextLibraryService TextLibraries => textLibraryService;

    [Header("初始库存字典")]
    [ShowInInspector]
    public Dictionary<string, Inventoryinit> InventoryInitDict = new Dictionary<string, Inventoryinit>();

    [Header("技能字典")]
    [ShowInInspector]
    public Dictionary<string, BaseSkill> SkillDict = new Dictionary<string, BaseSkill>();

    public bool isLoadFinish = false;
    
    // 进度条相关字段
    private bool showLoadingGUI = false;
    private string loadingText = "";
    private float loadingProgress = 0f;
    private int totalAssetsToLoad = 0;
    private int loadedAssetsCount = 0;
    private bool resourceLoadFailed = false;

    #endregion

    #region Unity 生命周期

    protected override void Awake()
    {
        base.Awake();

        // 返回主菜单时场景会再次带入 WorldManager Prefab；重复实例已由基类安排销毁，
        // 不能再启动一条会清空正式资源目录、随后又因对象销毁而中断的加载协程。
        if (instance != this)
            return;

        // 初始化时显示加载界面
        showLoadingGUI = true;
        loadingText = "正在加载资源...";
        InitializeResourceLoadingPresentation();
        RefreshResourceLoadingPresentation();
        StartCoroutine(LoadResourcesWithProgress());
    }

    /// <summary>刷新资源加载界面，并保留 F5 热重载入口。</summary>
    public void Update()
    {
        RefreshResourceLoadingPresentation();

        // 资源热重载是主菜单阶段也需生效的全局开发快捷键。
        if (Keyboard.current?.f5Key.wasPressedThisFrame == true)
        {
            Debug.Log("F5键被按下，开始热更新资源...");
            showLoadingGUI = true;
            StartCoroutine(LoadResourcesWithProgress());
        }
    }

    #endregion

    #region 协程加载资源（带进度）
    
    private System.Collections.IEnumerator LoadResourcesWithProgress()
    {
        isLoadFinish = false;
        resourceLoadFailed = false;
        loadingProgress = 0f;

        // 记录上次加载的资源数量
        int previousLoadedCount = LoadedCount;
        
        // 清空现有字典并重置计数器
        ClearAllDictionaries();
        LoadedCount = 0;
        loadedAssetsCount = 0;
        PlayerCreationTemplateCatalogService.ClearExternal();
        loadingText = "加载 JSON 玩家创建配置";
        PlayerCreationTemplateCatalogConfig playerCreationCatalog = null;
        System.Exception playerCreationConfigError = null;
        yield return StartCoroutine(PlayerCreationTemplateJsonLoader.LoadBuiltInAsync(
            catalog => playerCreationCatalog = catalog,
            exception => playerCreationConfigError = exception));
        if (playerCreationConfigError != null)
        {
            MarkResourceLoadingFailed($"玩家创建配置加载失败：{playerCreationConfigError.Message}", playerCreationConfigError);
            yield break;
        }

        PlayerCreationTemplateCatalogService.ReplaceBuiltIn(playerCreationCatalog);

        loadingText = "加载 JSON 时间系统配置";
        TimeSystemConfigCatalog loadedTimeSystemConfig = null;
        System.Exception timeSystemConfigError = null;
        yield return StartCoroutine(TimeSystemConfigLoader.LoadBuiltInAsync(
            catalog => loadedTimeSystemConfig = catalog,
            exception => timeSystemConfigError = exception));
        if (timeSystemConfigError != null)
        {
            MarkResourceLoadingFailed($"时间系统配置加载失败：{timeSystemConfigError.Message}", timeSystemConfigError);
            yield break;
        }

        TimeSystemConfigService.ReplaceCatalog(loadedTimeSystemConfig);
        GameManager.Instance?.ApplyDefaultTimeSystemProfile();

        loadingText = "加载 JSON 战利品表";
        IReadOnlyList<RuntimeLootTable> loadedLootTables = null;
        System.Exception lootTableError = null;
        yield return StartCoroutine(LootTableCatalogLoader.LoadBuiltInAsync(
            tables => loadedLootTables = tables,
            exception => lootTableError = exception));
        if (lootTableError != null)
        {
            MarkResourceLoadingFailed($"战利品库加载失败：{lootTableError.Message}", lootTableError);
            yield break;
        }

        foreach (RuntimeLootTable lootTable in loadedLootTables)
            RegisterLootTable(lootTable);

        // 默认标签
        if (ADBLabels.Count == 0)
        {
            ADBLabels.Add("ItemPrefab");
            ADBLabels.Add("Prefab");
            ADBLabels.Add("TileBase");
            ADBLabels.Add("TileBlock");
            ADBLabels.Add("InventoryInit");
            ADBLabels.Add("Skill");
        }

        // 估算总资源数量（用于进度条）
        totalAssetsToLoad = EstimateTotalAssets();
        loadingProgress = 0f;

        HashSet<string> redundantItemPrefabPaths;
        try
        {
            redundantItemPrefabPaths = ItemDefinitionCatalogLoader.GetRedundantBuiltInPrefabPaths();
        }
        catch (System.Exception exception)
        {
            MarkResourceLoadingFailed($"物品定义预检失败：{exception.Message}", exception);
            yield break;
        }

        // 先解析 Addressables 位置，再过滤 JSON 目录独占的 Prefab。
        yield return StartCoroutine(SyncLoadPrefabsWithProgress(
            new List<string> { "ItemPrefab", "Prefab" },
            redundantItemPrefabPaths,
            "加载运行时预制体"));
        if (resourceLoadFailed)
            yield break;

        loadingText = "加载 JSON 物品定义";
        int loadedItemDefinitionCount = 0;
        System.Exception itemDefinitionError = null;
        yield return StartCoroutine(ItemDefinitionCatalogLoader.LoadBuiltInAsync(
            this,
            count => loadedItemDefinitionCount = count,
            exception => itemDefinitionError = exception,
            progress => loadingProgress = ClampIntermediateProgress(progress)));
        if (itemDefinitionError != null)
        {
            MarkResourceLoadingFailed($"物品定义加载失败：{itemDefinitionError.Message}", itemDefinitionError);
            yield break;
        }
        loadedAssetsCount += loadedItemDefinitionCount;
        loadingProgress = GetAssetLoadingProgress();

        loadingText = "加载 JSON Actor 定义";
        int loadedActorDefinitionCount = 0;
        System.Exception actorDefinitionError = null;
        yield return StartCoroutine(ActorDefinitionCatalogLoader.LoadBuiltInAsync(
            this,
            count => loadedActorDefinitionCount = count,
            exception => actorDefinitionError = exception,
            progress => loadingProgress = ClampIntermediateProgress(progress)));
        if (actorDefinitionError != null)
        {
            MarkResourceLoadingFailed($"Actor 定义加载失败：{actorDefinitionError.Message}", actorDefinitionError);
            yield break;
        }
        loadedAssetsCount += loadedActorDefinitionCount;
        loadingProgress = GetAssetLoadingProgress();

        loadingText = "加载 JSON 动物技能";
        AnimalSkillCatalog loadedAnimalSkillCatalog = null;
        System.Exception animalSkillError = null;
        yield return StartCoroutine(AnimalSkillCatalogLoader.LoadBuiltInAsync(
            catalog => loadedAnimalSkillCatalog = catalog,
            exception => animalSkillError = exception));
        if (animalSkillError != null)
        {
            MarkResourceLoadingFailed($"动物技能配置加载失败：{animalSkillError.Message}", animalSkillError);
            yield break;
        }

        AnimalSkillCatalogService.Replace(loadedAnimalSkillCatalog);

        loadingText = "加载 JSON 生物生成配置";
        SpawnerConfigCatalog loadedSpawnerConfig = null;
        System.Exception spawnerConfigError = null;
        yield return StartCoroutine(SpawnerConfigCatalogLoader.LoadBuiltInAsync(
            catalog => loadedSpawnerConfig = catalog,
            exception => spawnerConfigError = exception));
        if (spawnerConfigError != null)
        {
            MarkResourceLoadingFailed($"生物生成配置加载失败：{spawnerConfigError.Message}", spawnerConfigError);
            yield break;
        }

        SpawnerConfigCatalogService.ReplaceCatalog(loadedSpawnerConfig);
            
        loadingText = "加载JSON配方";
        int loadedRecipeCount = 0;
        System.Exception recipeLoadError = null;
        yield return StartCoroutine(RecipeCatalogLoader.LoadBuiltInAsync(
            this,
            count => loadedRecipeCount = count,
            exception => recipeLoadError = exception));
        if (recipeLoadError != null)
        {
            MarkResourceLoadingFailed($"配方加载失败：{recipeLoadError.Message}", recipeLoadError);
            yield break;
        }
        loadedAssetsCount += loadedRecipeCount;
        loadingProgress = GetAssetLoadingProgress();

        loadingText = "加载JSON Buff";
        int loadedBuffCount = 0;
        System.Exception buffLoadError = null;
        yield return StartCoroutine(BuffCatalogLoader.LoadBuiltInAsync(
            this,
            count => loadedBuffCount = count,
            exception => buffLoadError = exception));
        if (buffLoadError != null)
        {
            MarkResourceLoadingFailed($"Buff 加载失败：{buffLoadError.Message}", buffLoadError);
            yield break;
        }
        loadedAssetsCount += loadedBuffCount;
        loadingProgress = GetAssetLoadingProgress();

        loadingText = "加载 JSON 任务";
        int loadedQuestCount = 0;
        System.Exception questLoadError = null;
        yield return StartCoroutine(QuestCatalogLoader.LoadBuiltInAsync(
            count => loadedQuestCount = count,
            exception => questLoadError = exception));
        if (questLoadError != null)
        {
            MarkResourceLoadingFailed($"任务加载失败：{questLoadError.Message}", questLoadError);
            yield break;
        }
        loadedAssetsCount += loadedQuestCount;
        loadingProgress = GetAssetLoadingProgress();

        loadingText = "加载 JSON 文字库";
        TextLibraryService loadedTextLibrary = null;
        System.Exception textLibraryError = null;
        yield return StartCoroutine(TextLibraryCatalogLoader.LoadBuiltInAsync(
            service => loadedTextLibrary = service,
            exception => textLibraryError = exception));
        if (textLibraryError != null)
        {
            // 文字库属于可选扩展配置；加载失败不应阻断已有游戏内容，调用方会使用旧的数字名兜底。
            textLibraryService = TextLibraryService.Empty;
            Debug.LogError($"文字库加载失败，将使用默认名称兜底：{textLibraryError.Message}");
            Debug.LogException(textLibraryError);
        }
        else
        {
            textLibraryService = loadedTextLibrary ?? TextLibraryService.Empty;
            Debug.Log($"[TextLibrary] 已加载 {textLibraryService.EntryCount} 条文字，" +
                $"{textLibraryService.LibraryIds.Count} 个分类");
        }
        loadingProgress = GetAssetLoadingProgress();
            
        yield return StartCoroutine(SyncLoadAssetsWithProgress<TileBase>(
            new List<string> { "TileBase" },
            tileBaseDict,
            null,
            "加载TileBase"));
        if (resourceLoadFailed)
            yield break;

        // 新增：加载 Tile_Block 资源（地块逻辑 ScriptableObject）
        yield return StartCoroutine(SyncLoadAssetsWithProgress<Tile_Block>(
            new List<string> { "TileBlock" },
            TileBlockDict,
            null,
            "加载TileBlock SO"));
        if (resourceLoadFailed)
            yield break;
            
        // 新增：加载InventoryInit资源
        yield return StartCoroutine(SyncLoadAssetsWithProgress<Inventoryinit>(
            new List<string> { "InventoryInit" },
            InventoryInitDict,
            null,
            "加载初始库存"));
        if (resourceLoadFailed)
            yield break;

        // 新增：加载Skill资源
        yield return StartCoroutine(SyncLoadAssetsWithProgress<BaseSkill>(
            new List<string> { "Skill" },
            SkillDict,
            null,
            "加载技能"));
        if (resourceLoadFailed)
            yield break;

        // 内建资源完成后再加载 MOD，确保 MOD 可以引用游戏本体内容。
        ModRuntimeManager modRuntime = ModRuntimeManager.Ensure(gameObject);
        yield return StartCoroutine(modRuntime.LoadEnabledMods(this, ReportModLoadingProgress));
        if (!modRuntime.IsReady)
        {
            ClearAllDictionaries();
            MarkResourceLoadingFailed($"MOD 加载失败：{modRuntime.FailureReason}");
            yield break;
        }

        loadingProgress = 1f;
        isLoadFinish = true;
        showLoadingGUI = false; // 隐藏加载界面
        
        // 计算本次加载的资源数量
        int currentLoadedCount = LoadedCount;
        int difference = currentLoadedCount - previousLoadedCount;
        
        string differenceText = difference > 0 ? $"(比上次多加载 {difference} 个)" : 
                               difference < 0 ? $"(比上次少加载 {Mathf.Abs(difference)} 个)" : 
                               "(与上次加载数量相同)";
        
        Debug.Log($"所有资源同步加载完成！共加载 {currentLoadedCount} 个资源 {differenceText}");
    }

    // 估算总资源数量
    private int EstimateTotalAssets()
    {
        // 这里可以根据经验数据估算，或者先进行一次异步预加载来获取数量
        // 简单估算：假设每种类型大约有100个资源
        return ADBLabels.Count * 100;
    }

    private void ReportModLoadingProgress(string text, float progress)
    {
        loadingText = text;
        loadingProgress = ClampIntermediateProgress(progress);
    }

    /// <summary>记录资源加载失败，避免错误状态伪装成 100% 完成。</summary>
    private void MarkResourceLoadingFailed(string message, System.Exception exception = null)
    {
        resourceLoadFailed = true;
        isLoadFinish = false;
        showLoadingGUI = true;
        loadingProgress = 0f;
        loadingText = $"{message}（请查看日志）";
        Debug.LogError($"[GameRes] {message}");
        if (exception != null)
            Debug.LogException(exception);
    }

    /// <summary>预留 1% 给最终注册，避免中途提前显示 100%。</summary>
    private float ClampIntermediateProgress(float progress)
    {
        return Mathf.Clamp(progress, 0f, 0.99f);
    }

    /// <summary>按已处理数量计算中间进度。</summary>
    private float GetAssetLoadingProgress()
    {
        if (totalAssetsToLoad <= 0)
            return 0f;

        return ClampIntermediateProgress((float)loadedAssetsCount / totalAssetsToLoad);
    }

    // 带进度的同步加载
    private System.Collections.IEnumerator SyncLoadAssetsWithProgress<T>(
        List<string> labels,
        IDictionary<string, T> dict,
        System.Action<T> onLoadedAsset,
        string progressText)
    {
        if (labels == null || labels.Count == 0) yield break;

        var handle = Addressables.LoadAssetsAsync<T>(
            labels,
            null,
            Addressables.MergeMode.Union);

        // 更新进度文本
        loadingText = progressText;
        
        // 等待加载完成，同时更新进度
        while (!handle.IsDone)
        {
            loadingProgress = GetAssetLoadingProgress();
            yield return null;
        }

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            MarkResourceLoadingFailed(
                $"加载 {typeof(T).Name} 失败：{handle.OperationException?.Message ?? "Addressables 操作未成功"}",
                handle.OperationException);
            yield break;
        }

        IList<T> assets = handle.Result;

        foreach (var asset in assets)
        {
            if (asset == null) continue;

            // 回调（直接传入 asset，就不会有类型转换问题）
            onLoadedAsset?.Invoke(asset);

            string key = asset switch
            {
                GameObject go => go.name,
                TileBase tile => tile.name,
                Inventoryinit inventoryInit => inventoryInit.name,
                BaseSkill skill => skill.name,
                Tile_Block tileBlock => tileBlock.name,
                _ => asset.ToString()
            };

            dict[key] = asset;
            LoadedCount++;
            loadedAssetsCount++;
            
            // 每加载一定数量的资源就更新一次进度
            if (loadedAssetsCount % 10 == 0)
            {
                loadingProgress = GetAssetLoadingProgress();
                yield return null; // 让出控制权，避免卡顿
            }
        }

        // 更新进度
        loadingProgress = GetAssetLoadingProgress();
    }

    private System.Collections.IEnumerator SyncLoadPrefabsWithProgress(
        List<string> labels,
        HashSet<string> excludedPaths,
        string progressText)
    {
        if (labels == null || labels.Count == 0)
            yield break;

        AsyncOperationHandle<IList<IResourceLocation>> locationsHandle =
            Addressables.LoadResourceLocationsAsync(labels, Addressables.MergeMode.Union, typeof(GameObject));
        while (!locationsHandle.IsDone)
        {
            loadingText = progressText;
            yield return null;
        }
        if (locationsHandle.Status != AsyncOperationStatus.Succeeded)
        {
            MarkResourceLoadingFailed(
                $"无法解析 Prefab Addressables 位置：{locationsHandle.OperationException?.Message ?? "Addressables 操作未成功"}",
                locationsHandle.OperationException);
            yield break;
        }

        // Actor 外壳只能由 ActorDefinition 目录加载；通用 Prefab 管线提前加载会产生重复实例与别名冲突。
        AsyncOperationHandle<IList<IResourceLocation>> actorShellLocationsHandle =
            Addressables.LoadResourceLocationsAsync(
                ActorDefinitionCatalogLoader.ShellAddressableLabel,
                typeof(GameObject));
        while (!actorShellLocationsHandle.IsDone)
        {
            loadingText = progressText;
            yield return null;
        }
        if (actorShellLocationsHandle.Status != AsyncOperationStatus.Succeeded)
        {
            MarkResourceLoadingFailed(
                $"无法解析 ActorShell Addressables 位置：{actorShellLocationsHandle.OperationException?.Message ?? "Addressables 操作未成功"}",
                actorShellLocationsHandle.OperationException);
            Addressables.Release(locationsHandle);
            yield break;
        }

        excludedPaths ??= new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var actorShellInternalIds = new HashSet<string>(
            actorShellLocationsHandle.Result
                .Where(location => location != null)
                .Select(location => location.InternalId),
            System.StringComparer.OrdinalIgnoreCase);
        List<IResourceLocation> locations = locationsHandle.Result
            .Where(location => location != null)
            .GroupBy(location => location.PrimaryKey, System.StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(location => !excludedPaths.Contains((location.PrimaryKey ?? string.Empty).Replace('\\', '/')))
            .Where(location => !actorShellInternalIds.Contains(location.InternalId))
            .ToList();
        int skippedCount = locationsHandle.Result.Count - locations.Count;

        AsyncOperationHandle<IList<GameObject>> assetsHandle =
            Addressables.LoadAssetsAsync<GameObject>(locations, null, true);
        while (!assetsHandle.IsDone)
        {
            loadingText = $"{progressText}（已跳过 {skippedCount} 个 JSON 专用 Prefab）";
            loadingProgress = GetAssetLoadingProgress();
            yield return null;
        }
        if (assetsHandle.Status != AsyncOperationStatus.Succeeded)
        {
            MarkResourceLoadingFailed(
                $"加载运行时 Prefab 失败：{assetsHandle.OperationException?.Message ?? "Addressables 操作未成功"}",
                assetsHandle.OperationException);
            yield break;
        }

        foreach (GameObject prefab in assetsHandle.Result)
        {
            if (prefab == null)
                continue;
            RegisterPrefabAlias(prefab.name, prefab);
            HandlePrefab(prefab);
            LoadedCount++;
            loadedAssetsCount++;
            if (loadedAssetsCount % 10 == 0)
                yield return null;
        }

        Debug.Log($"[GameRes] Prefab 加载计划：加载 {locations.Count}，跳过 JSON 专用 Prefab {skippedCount}");
        loadingProgress = GetAssetLoadingProgress();
        Addressables.Release(actorShellLocationsHandle);
        Addressables.Release(locationsHandle);
    }

    #endregion

    #region 同步加载资源（原有方法保持不变）
    
public void LoadResourcesSync()
{
    showLoadingGUI = true;
    StopAllCoroutines();
    StartCoroutine(LoadResourcesWithProgress());
}

// 清空所有字典
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
    AnimalSkillCatalogService.Reset();
    QuestCatalog.Reset();
    textLibraryService = TextLibraryService.Empty;
    InventoryInitDict.Clear();
    SkillDict.Clear();
}

[Button("热更新所有资源")]
public void HotReloadAllResources()
{
    Debug.Log("开始热更新所有资源...");
    LoadResourcesSync();
    Debug.Log("已开始重新加载本体资源与 MOD；完成前不可进入世界。");
}

    // 通用同步加载，并填充到字典
    private void SyncLoadAssetsByLabels<T>(
        List<string> labels,
        IDictionary<string, T> dict,
        System.Action<T> onLoadedAsset = null)
    {
        if (labels == null || labels.Count == 0) return;

        var handle = Addressables.LoadAssetsAsync<T>(
            labels,
            null,
            Addressables.MergeMode.Union);

        // 阻塞等待
        IList<T> assets = handle.WaitForCompletion();
        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"同步加载 {typeof(T).Name} 失败");
            return;
        }

        foreach (var asset in assets)
        {
            if (asset == null) continue;

            // 回调（直接传入 asset，就不会有类型转换问题）
            onLoadedAsset?.Invoke(asset);

            string key = asset switch
            {
                GameObject go => go.name,
                TileBase tile => tile.name,
                Inventoryinit inventoryInit => inventoryInit.name,
                BaseSkill skill => skill.name,
                Tile_Block tileBlock => string.IsNullOrEmpty(tileBlock.tileItemName) ? tileBlock.name : tileBlock.tileItemName,
                _ => asset.ToString()
            };

            dict[key] = asset;
            LoadedCount++;
        }

      //  Debug.Log($"同步加载 {typeof(T).Name} 完成，数量：{assets.Count}");
    }

    // 专门处理 Prefab 的额外逻辑：把 Item ID 和独立模块 ID 也加入字典
    private void HandlePrefab(GameObject prefab)
    {
        var item = prefab.GetComponent<Item>();
        if (item != null)
        {
            RegisterPrefabAlias(item.itemData?.IDName, prefab);
            return;
        }

        foreach (Module module in prefab.GetComponentsInChildren<Module>(true))
        {
            if (module == null)
                continue;

            RegisterPrefabAlias(module.CanonicalModuleId, prefab);
            RegisterPrefabAlias(module._Data?.ID, prefab);
        }
    }

    internal void RegisterPrefabAlias(string key, GameObject prefab)
    {
        if (prefab == null || string.IsNullOrWhiteSpace(key))
            return;

        string normalizedKey = key.Trim();
        if (AllPrefabs.TryGetValue(normalizedKey, out GameObject existingPrefab) &&
            existingPrefab != null && existingPrefab != prefab)
        {
            Item existingItem = existingPrefab.GetComponent<Item>();
            Item incomingItem = prefab.GetComponent<Item>();

            // JSON shellPrefab 必须稳定指向真实 Item；普通 Prefab 或模块别名不得覆盖同名外壳。
            if (existingItem != null && incomingItem == null)
                return;
        }

        AllPrefabs[normalizedKey] = prefab;
    }

    /// <summary>批量注册失败时，仅移除仍指向本次外壳的内部别名。</summary>
    internal void UnregisterPrefabAlias(string key, GameObject prefab)
    {
        if (prefab == null || string.IsNullOrWhiteSpace(key))
            return;

        string normalizedKey = key.Trim();
        if (AllPrefabs.TryGetValue(normalizedKey, out GameObject registered) && registered == prefab)
            AllPrefabs.Remove(normalizedKey);
    }

    #endregion

    #region 外部接口

    public GameObject InstantiatePrefab(string prefab, Vector3? position = null, Quaternion? rotation = null, Vector3? scale = null, Transform parent = null)
    {
        if (AllPrefabs.TryGetValue(prefab, out var go))
        {
            var obj = Instantiate(go, parent);

            // 设置位置、旋转和缩放
            obj.transform.position = position ?? Vector3.zero;
            obj.transform.rotation = rotation ?? Quaternion.identity;

            // 修复缩放为0的问题 - 如果未指定缩放或缩放为零向量，使用Vector3.one
            obj.transform.localScale = scale ?? Vector3.one;

            // 额外检查：确保缩放的每个分量都不为0
            if (obj.transform.localScale.x == 0) obj.transform.localScale = new Vector3(1, obj.transform.localScale.y, obj.transform.localScale.z);
            if (obj.transform.localScale.y == 0) obj.transform.localScale = new Vector3(obj.transform.localScale.x, 1, obj.transform.localScale.z);
            if (obj.transform.localScale.z == 0) obj.transform.localScale = new Vector3(obj.transform.localScale.x, obj.transform.localScale.y, 1);

            if (ModRuntimeManager.Instance != null && ModRuntimeManager.Instance.IsRuntimeTemplate(go))
                obj.SetActive(true);

            if (ItemDefinitions.TryGetValue(prefab, out RuntimeItemDefinition definition) &&
                obj.TryGetComponent(out Item item))
            {
                ItemDefinitionRuntime.ConfigureInstance(this, definition, item, definition.CreateItemData());
            }

            return obj;
        }
        Debug.LogError($"预制件不存在:{prefab}");
        return null;
    }
    
   void OnTriggerEnter2D()
    {
        
    }
    public GameObject GetPrefab(string prefabName, bool logError = true)
    {
        if (AllPrefabs.TryGetValue(prefabName, out var go))
        {
            return go;
        }
        else
        {
            // 输出错误日志，包含关键信息便于调试
            if (logError)
                Debug.LogError($"找不到名为 [{prefabName}] 的预制体！请检查AllPrefabs字典中是否正确注册了该预制体", this);
            return null;
        }
    }

    public TileBase GetTileBase(string tileBaseName)
    {
        tileBaseDict.TryGetValue(tileBaseName, out var tile);
        return tile;
    }
    
    /// <summary>
    /// 获取 Tile_Block 逻辑 ScriptableObject（通过 tileItemName 或资源名）
    /// </summary>
    public Tile_Block GetTileBlock(string key)
    {
        TileBlockDict.TryGetValue(key, out var block);
        return block;
    }
    
    public BuffDefinition GetBuffDefinition(string buffId)
    {
        if (string.IsNullOrWhiteSpace(buffId))
            return null;

        BuffDefinitions.TryGetValue(buffId.Trim(), out BuffDefinition definition);
        return definition;
    }

    public void RegisterItemDefinition(RuntimeItemDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || definition.ShellPrefab == null)
            throw new InvalidDataException("注册的 ItemDefinition、ID 或外壳为空");
        if (ItemDefinitions.ContainsKey(definition.Id))
            throw new InvalidDataException($"ItemDefinition ID 冲突：{definition.Id}");

        ItemDefinitions.Add(definition.Id, definition);
        // 兼容仍以 AllPrefabs.ContainsKey 判断物品是否存在的配方与玩法代码。
        AllPrefabs[definition.Id] = definition.ShellPrefab;
        LoadedCount++;
    }

    /// <summary>按稳定 ID 注册战利品表；重复 ID 视为配置错误。</summary>
    public void RegisterLootTable(RuntimeLootTable lootTable)
    {
        if (lootTable == null || string.IsNullOrWhiteSpace(lootTable.Id))
            throw new InvalidDataException("注册的战利品表或 ID 为空");
        if (LootTables.ContainsKey(lootTable.Id))
            throw new InvalidDataException($"战利品表 ID 冲突：{lootTable.Id}");

        LootTables.Add(lootTable.Id, lootTable);
    }

    /// <summary>按稳定 ID 查询战利品表。</summary>
    public bool TryGetLootTable(string lootTableId, out RuntimeLootTable lootTable)
    {
        if (string.IsNullOrWhiteSpace(lootTableId))
        {
            lootTable = null;
            return false;
        }

        return LootTables.TryGetValue(lootTableId.Trim(), out lootTable);
    }

    /// <summary>注册 JSON Actor；同时进入通用 ItemDefinition 管线以复用对象池与存档。</summary>
    public void RegisterActorDefinition(RuntimeItemDefinition definition)
    {
        if (definition == null || !definition.IsActor)
            throw new InvalidDataException("注册的 ActorDefinition 为空或未标记为 Actor");
        if (ActorDefinitions.ContainsKey(definition.Id))
            throw new InvalidDataException($"ActorDefinition ID 冲突：{definition.Id}");
        if (AllPrefabs.TryGetValue(definition.Id, out GameObject existingPrefab) &&
            existingPrefab != null && existingPrefab != definition.ShellPrefab)
        {
            throw new InvalidDataException($"ActorDefinition ID 与已有 Prefab 别名冲突：{definition.Id}");
        }

        // 两张表必须保持原子一致，避免其中一步失败后留下半注册 Actor。
        bool itemRegistered = false;
        try
        {
            RegisterItemDefinition(definition);
            itemRegistered = true;
            ActorDefinitions.Add(definition.Id, definition);
        }
        catch
        {
            ActorDefinitions.Remove(definition.Id);
            if (itemRegistered)
            {
                ItemDefinitions.Remove(definition.Id);
                if (AllPrefabs.TryGetValue(definition.Id, out GameObject prefab) &&
                    prefab == definition.ShellPrefab)
                {
                    AllPrefabs.Remove(definition.Id);
                }
                LoadedCount = Mathf.Max(0, LoadedCount - 1);
            }
            throw;
        }
    }

    public bool TryGetActorDefinition(string actorId, out RuntimeItemDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            definition = null;
            return false;
        }

        return ActorDefinitions.TryGetValue(actorId.Trim(), out definition);
    }

    /// <summary>仅供 MOD 卸载回滚其运行时 Actor 定义。</summary>
    public bool UnregisterExternalActorDefinition(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId) ||
            !ActorDefinitions.Remove(actorId.Trim(), out RuntimeItemDefinition definition))
        {
            return false;
        }

        ItemDefinitions.Remove(actorId.Trim());
        if (AllPrefabs.TryGetValue(actorId.Trim(), out GameObject prefab) &&
            prefab == definition.ShellPrefab)
        {
            AllPrefabs.Remove(actorId.Trim());
        }
        LoadedCount = Mathf.Max(0, LoadedCount - 1);
        return true;
    }

    public bool TryGetItemDefinition(string itemId, out RuntimeItemDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            definition = null;
            return false;
        }
        return ItemDefinitions.TryGetValue(itemId, out definition);
    }

    /// <summary>
    /// 获取 JSON 物品定义中的界面显示信息。
    /// </summary>
    public bool TryGetItemPresentation(string itemId, out string displayName, out Sprite sprite)
    {
        displayName = string.Empty;
        sprite = null;
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        string requestedId = itemId.Trim();
        if (!TryGetItemDefinition(requestedId, out RuntimeItemDefinition definition))
        {
            Debug.LogError($"物品 {requestedId} 没有 JSON 定义");
            return false;
        }

        displayName = definition.DisplayName;
        sprite = definition.Sprite;
        if (sprite == null)
        {
            Debug.LogError($"物品 {requestedId} 的 JSON 定义缺少 visual.spriteAddress");
            return false;
        }

        return true;
    }

    /// <summary>按物品 ID 创建数据；JSON 目录是唯一权威来源。</summary>
    public ItemData CreateItemData(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            throw new System.ArgumentException("物品 ID 不能为空", nameof(itemId));

        string requestedId = itemId.Trim();
        if (!TryGetItemDefinition(requestedId, out RuntimeItemDefinition definition))
            throw new InvalidDataException($"物品 {requestedId} 没有 JSON 定义");

        ItemData data = definition.CreateItemData();
        data.IDName = requestedId;
        data.Guid = System.Guid.NewGuid().GetHashCode();
        return data;
    }

    public IReadOnlyList<string> GetAllItemIds()
    {
        return ItemDefinitions.Keys.OrderBy(id => id, System.StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void ApplyItemModuleConfiguration(string itemId, string moduleName, Module module, ModuleData data)
    {
        if (!TryGetItemDefinition(itemId, out RuntimeItemDefinition definition) ||
            !definition.TryGetModuleParameters(moduleName, out string json))
        {
            return;
        }

        ModuleJsonConfigurator.Apply(module, itemId, moduleName, data?.ID, json);
    }

    public void RegisterBuff(BuffDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            throw new InvalidDataException("注册的 Buff JSON 定义或 ID 为空");

        string id = definition.Id.Trim();
        if (BuffDefinitions.ContainsKey(id))
            throw new InvalidDataException($"Buff ID 冲突：{id}");

        BuffDefinitions[id] = definition;
        LoadedCount++;
    }
    
    public RuntimeRecipe GetRecipe(string recipeName)
    {
        if (recipeCatalog.TryGet(recipeName, out RuntimeRecipe recipe))
            return recipe;

        recipeDict.TryGetValue(recipeName, out recipe);
        return recipe;
    }

    /// <summary>返回按匹配优先级预排序的指定类型配方。</summary>
    public IReadOnlyList<RuntimeRecipe> GetRecipes(RecipeType recipeType)
    {
        return recipeCatalog.GetByType(recipeType);
    }

    /// <summary>
    /// 注册校验后的运行时配方，同时建立 ID 与旧输入签名索引。
    /// </summary>
    public void RegisterRecipe(RuntimeRecipe recipe, bool replaceExistingSignature)
    {
        if (recipe == null || string.IsNullOrWhiteSpace(recipe.Id))
            throw new System.IO.InvalidDataException("注册的运行时配方或配方 ID 为空");
        if (recipeById.ContainsKey(recipe.Id))
            throw new System.IO.InvalidDataException($"配方 ID 冲突：{recipe.Id}");

        string inputKey = recipe.inputs?.ToString();
        if (string.IsNullOrWhiteSpace(inputKey))
            throw new System.IO.InvalidDataException($"配方 {recipe.Id} 无法生成输入签名");
        if (recipeDict.ContainsKey(inputKey) && !replaceExistingSignature)
            throw new System.IO.InvalidDataException($"配方输入签名冲突：{recipe.Id} -> {inputKey}");
        if (recipeDict.TryGetValue(inputKey, out RuntimeRecipe existing))
            Debug.LogWarning($"[RecipeCatalog] 配方 {recipe.Id} 覆盖相同输入签名的配方 {existing.Id}：{inputKey}");

        recipeCatalog.Register(recipe);
        recipeDict[inputKey] = recipe;
        LoadedCount++;
    }
    
    // 新增：获取InventoryInit资源
    public Inventoryinit InventoryInitGet(string inventoryInitName, out Inventoryinit inventoryInit)
    {
        InventoryInitDict.TryGetValue(inventoryInitName, out inventoryInit);
        return inventoryInit;
    }
    
    // 新增：获取BaseSkill资源
    public BaseSkill GetSkill(string skillName)
    {
        SkillDict.TryGetValue(skillName, out var skill);
        return skill;
    }

    public VFX GetVFX(string vfxName)
    {
        AllPrefabs.TryGetValue(vfxName, out var vfx);
        if (vfx == null)
        {
            Debug.LogError($"VisualEffectMManager: 特效对象为空");
            return null;
        }
        VFX vfxComponent = vfx.GetComponent<VFX>();
        if (vfxComponent == null)
        {
            Debug.LogError($"VisualEffectMManager: 特效对象 {vfxName} 没有VFX组件");
            return null;
        }
        return vfxComponent;
    }

    public void InstantiateVFX(string vfxName, Vector3 position, Quaternion rotation)
    {
        VFX vfx = GetVFX(vfxName);
        if (vfx == null)
        {
            Debug.LogError($"VisualEffectMManager: 特效对象 {vfxName} 为空");
            return;
        }
        GameObject vfxObj = Instantiate(vfx.gameObject, position, rotation);
        vfxObj.transform.SetParent(transform);
    }

    #endregion

}
