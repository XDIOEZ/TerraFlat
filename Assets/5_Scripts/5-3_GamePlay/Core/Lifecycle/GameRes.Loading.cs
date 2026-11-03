using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlatWorld.Gameplay.Quests;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>资源会话边界：具体资源按会话清理，Addressables 初始化按 GameRes 生命周期持有。</summary>
public partial class GameRes
{
    #region 会话状态

    private enum ConfigurationSourcePolicy
    {
        BuiltInBinary,
        SourceFiles
    }

    /// <summary>资源会话的权威状态。</summary>
    [Sirenix.OdinInspector.ShowInInspector] public ResourceLoadState LoadState { get; private set; }
    /// <summary>主菜单交互所需的最小资源是否已经就绪；完整世界资源仍可能在后台加载。</summary>
    [Sirenix.OdinInspector.ShowInInspector] public bool IsStartupReady { get; private set; }
    /// <summary>启动阶段所需资源已加载，供早于资源目录启动的运行时工具延后绑定依赖 Prefab。</summary>
    public static event Action<GameRes> StartupResourcesReady;
    /// <summary>完整资源会话的当前总进度，供世界加载页在抢跑进入时复用。</summary>
    public float LoadProgress => loadingProgress;
    /// <summary>完整资源会话的当前阶段文案。</summary>
    public string LoadStatusText => GetResourceLoadStatusText();
    /// <summary>仅当菜单后台的世界资源请求仍在排队或执行时显示加载提示。</summary>
    public bool IsBackgroundWorldResourcePreloadInProgress =>
        LoadState == ResourceLoadState.Loading && IsStartupReady &&
        (worldResourceRequestQueued || (worldResourceSessionActive && loadCoroutine != null)) &&
        ShouldKeepCurrentResourceLoadingInBackground;
    private int cacheBuildStartRevision;
    private bool resourceSessionStarted;
    private bool completingInitialResourceLoad;
    private bool worldResourceSessionActive;
    private bool worldResourceRequestQueued;
    private bool startupResourceSessionRunning;
    /// <summary>最近一次成功提交的配置来源，跨世界资源会话保持。</summary>
    private ConfigurationSourcePolicy configurationSourcePolicy = ConfigurationSourcePolicy.BuiltInBinary;
    private long startupResourceSessionStartedTimestamp;
    private long worldResourceSessionStartedTimestamp;
    /// <summary>完整资源会话的验收目标；菜单提前开放不计作加载完成。</summary>
    public const double InitialResourceLoadTargetMilliseconds = 10000d;
    /// <summary>菜单必需资源从本轮启动请求到菜单可交互的完整耗时。</summary>
    public double LastStartupLoadMilliseconds { get; private set; }
    /// <summary>本轮世界资源预加载从启动到完整 Ready 的完整耗时。</summary>
    public double LastResourceLoadMilliseconds { get; private set; }
    private string cachedHintSource, cachedHintLocale, cachedHintStatus;
    private const string FirstCacheBuildHint = "首次加载可能较慢，请稍候";
    private bool ShowCacheBuildHint => LoadState == ResourceLoadState.Loading && !resourceLoadFailed &&
                                       BinaryContentCache.BuildRevision > cacheBuildStartRevision;

    /// <summary>首次构建缓存时保留阶段和进度，再补充玩家能看懂的等待提示。</summary>
    private string GetResourceLoadStatusText()
    {
        string source = loadingText ?? string.Empty;
        if (!ShowCacheBuildHint) return source;
        string locale = FlatWorld.Localization.FlatWorldLocalizationService.CurrentLocaleCode;
        if (cachedHintStatus == null || cachedHintSource != source || cachedHintLocale != locale)
        {
            cachedHintSource = source;
            cachedHintLocale = locale;
            cachedHintStatus = FlatWorld.Localization.FlatWorldLocalizationService.GetUiText(source) + "\n" +
                               FlatWorld.Localization.FlatWorldLocalizationService.GetUiText(FirstCacheBuildHint);
        }
        return cachedHintStatus;
    }
    /// <summary>最后一次失败的阶段与原因。</summary>
    [Sirenix.OdinInspector.ShowInInspector] public string LastLoadError { get; private set; }
    /// <summary>当前或最后执行的阶段 ID。</summary>
    [Sirenix.OdinInspector.ShowInInspector] public string CurrentLoadStage => loadPipeline?.CurrentStage;
    private Coroutine loadCoroutine;
    private Coroutine inGameReloadCoroutine;
    private ResourceLoadPipeline loadPipeline;
    private IDisposable resourceSessionSourceConfigurationLease;
    private ResourceAssetScope resourceAssets = new();
    /// <summary>菜单持续持有自己的根资源；世界目录与 F5 候选从不接管此 Scope。</summary>
    private ResourceAssetScope startupRootResourceAssets;
    /// <summary>Addressables 初始化属于 GameRes 生命周期，不能随单次资源会话释放。</summary>
    private AsyncOperationHandle<IResourceLocator> addressablesInitializationHandle;
    internal ResourceAssetScope ResourceAssets => resourceAssets;
    /// <summary>启动阶段已经持有的 Prefab 位置，完整 Prefab 阶段不再重复申请句柄。</summary>
    private HashSet<string> startupPrefabLocationIds = new(StringComparer.Ordinal);
    private Dictionary<string, GameObject> startupUiPrefabSnapshot = new(StringComparer.Ordinal);
    private PlayerCreationTemplateCatalogConfig startupPlayerCreationConfig;
    private TimeSystemConfigCatalog startupTimeSystemConfig;
    private AtmosphericGreenhouseConfiguration startupGreenhouseConfiguration;
    private WaterCurrentPushCatalog startupWaterCurrentCatalog;
    private bool backgroundLoadingPolicyActive;
    private bool startupMenuPrerequisitesReady;
    private ThreadPriority previousLoadingPriority;
    private int previousUploadTimeSlice;

    internal static bool ShouldKeepStartupResourceLoadingInBackground(bool startupReady, bool enteringWorld,
        bool inPlaceReload = false)
        => startupReady && !enteringWorld && !inPlaceReload;

    // 主菜单后台预热每帧最多推进 1ms，真正入世时恢复原有加载预算。
    internal static double? ResolveResourceWorkBudget(bool initialLoad, bool inPlaceReload, bool testCache,
        bool menuBackground = false)
        => testCache ? 8d : menuBackground ? 1d : initialLoad && !inPlaceReload ? 32d : null;

    private bool ShouldKeepCurrentResourceLoadingInBackground
    {
        get
        {
            GameManager manager = GameManager.ExistingInstance;
            return manager != null && ShouldKeepStartupResourceLoadingInBackground(IsStartupReady,
                manager.IsWorldEntryInProgress || manager.IsInGameWorld, preparingInPlaceReload);
        }
    }

    internal static bool ShouldPreloadWorldResourcesFromMenu(bool idleMenuResources, bool inWorld,
        bool enteringWorld, bool reloading)
        => idleMenuResources && !inWorld && !enteringWorld && !reloading;

    /// <summary>菜单空闲时只请求一次世界资源，共用入世时原有的完整加载会话。</summary>
    private void TryPreloadWorldResourcesFromMenu()
    {
        GameManager manager = GameManager.ExistingInstance;
        if (manager != null && ShouldPreloadWorldResourcesFromMenu(IsIdleStartupMenu,
                manager.IsInGameWorld, manager.IsWorldEntryInProgress, resourceReloadInProgress))
            RequestWorldResources();
    }

    /// <summary>定义构建和请求发出使用统一帧预算，不能让同帧的多个阶段各自重新计时。</summary>
    internal bool ShouldYieldResourceWork() => loadPipeline?.ShouldYield() == true;
    internal bool IsCompletingInitialResourceLoad => completingInitialResourceLoad && !preparingInPlaceReload;

    // 加载页接管交互后恢复正常接入和上传预算，回到菜单再启用后台限制。
    private void RefreshResourceLoadingPriorityForCurrentInteraction()
    {
        if (LoadState != ResourceLoadState.Loading || preparingInPlaceReload) return;
        if (ShouldKeepCurrentResourceLoadingInBackground) BeginMenuBackgroundLoading();
        else RestoreBackgroundLoadingPolicy();
    }

    /// <summary>菜单开放期间收紧 Player 的资源接入和 GPU 上传时间，结束会话后恢复原设置。</summary>
    private void BeginMenuBackgroundLoading()
    {
        if (backgroundLoadingPolicyActive) return;
        previousLoadingPriority = Application.backgroundLoadingPriority;
        previousUploadTimeSlice = QualitySettings.asyncUploadTimeSlice;
        backgroundLoadingPolicyActive = true;
        Application.backgroundLoadingPriority = ThreadPriority.Low;
        QualitySettings.asyncUploadTimeSlice = 1;
    }

    private void RestoreBackgroundLoadingPolicy()
    {
        if (!backgroundLoadingPolicyActive) return;
        Application.backgroundLoadingPriority = previousLoadingPriority;
        QualitySettings.asyncUploadTimeSlice = previousUploadTimeSlice;
        backgroundLoadingPolicyActive = false;
    }

    /// <summary>
    /// 主菜单显示后仍可能立即被访问的运行时 UI。
    /// 主菜单、存档页和新建世界页本身由 WorldManager 直接引用，不需要经过 GameRes。
    /// </summary>
    private static readonly string[] StartupPrefabKeys =
    {
        RuntimeUIPrefabKeys.SliderControl,
        RuntimeUIPrefabKeys.TabButton,
        RuntimeUIPrefabKeys.MainMenuSettings,
        RuntimeUIPrefabKeys.MainMenuExitConfirmation,
        RuntimeUIPrefabKeys.NetworkMode,
        RuntimeUIPrefabKeys.ModManager,
        RuntimeUIPrefabKeys.InputBindingRow,
        RuntimeUIPrefabKeys.WorldLoading,
        RuntimeUIPrefabKeys.MobileControls,
        RuntimeUIPrefabKeys.MobileControlLayoutEditor
    };

    /// <summary>
    /// Alt+R/调试入口统一使用这里：主菜单直接重载；单机世界内准备独立候选目录并原位发布。
    /// 活跃实例继续持有旧资源，不保存、不退出、不从磁盘恢复世界。
    /// </summary>
    public bool RequestResourceReload() => RequestResourceReload(useSourceConfigurations: false);

    private bool RequestResourceReload(bool useSourceConfigurations)
    {
        if ((LoadState == ResourceLoadState.Loading && !IsIdleStartupMenu && !CanInterruptMenuWorldResourcePreload) ||
            LoadState == ResourceLoadState.Disposed ||
            resourceReloadInProgress)
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
            return TryReloadResources(useSourceConfigurations);

        if (manager.IsWorldEntryInProgress)
        {
            Debug.LogWarning("[GameRes] 正在进入世界，暂不能执行 Alt+R 资源重载。");
            return false;
        }

        // 联机状态需要由服务器统一协调资源版本；本地单边热重载会直接造成内容目录不一致。
        if (GameNetwork.IsOnline)
        {
            Debug.LogWarning("[GameRes] 联机世界不允许本地 Alt+R 热重载资源，请先结束联机会话。");
            return false;
        }

        return StartInPlaceResourceReload(manager, useSourceConfigurations);
    }

    /// <summary>底层资源会话入口；调用前必须保证世界运行态与活跃 Item 已经清空。</summary>
    public bool TryReloadResources() => TryReloadResources(useSourceConfigurations: false);

    private bool TryReloadResources(bool useSourceConfigurations)
    {
        if ((LoadState == ResourceLoadState.Loading && !IsIdleStartupMenu && !CanInterruptMenuWorldResourcePreload) ||
            LoadState == ResourceLoadState.Disposed || resourceReloadInProgress) return false;
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
            Debug.LogWarning("[GameRes] 底层资源会话仍有世界运行态引用；游戏内请通过 Alt+R/RequestResourceReload 执行安全重载。");
            return false;
        }

        if (LoadState == ResourceLoadState.Loading && CanInterruptMenuWorldResourcePreload)
        {
            CancelWorldResourceLoadForMenuReturn();
            ReleaseWorldResourcesAndRestoreStartup();
            if (LoadState != ResourceLoadState.Loading || !IsIdleStartupMenu)
                return false;
        }

        LoadState = ResourceLoadState.Loading;
        LastResourceLoadMilliseconds = 0;
        LastStartupLoadMilliseconds = 0;
        IsStartupReady = false;
        worldResourceSessionActive = false;
        worldResourceRequestQueued = false;
        startupResourceSessionRunning = true;
        LastLoadError = null;
        resourceLoadFailed = false;
        completingInitialResourceLoad = !resourceSessionStarted;
        showLoadingGUI = true;
        loadingProgress = 0;
        loadingText = "准备资源加载会话";
        Coroutine started = StartCoroutine(RunResourceSession(startupOnly: true,
            requestedSourcePolicy: useSourceConfigurations
                ? ConfigurationSourcePolicy.SourceFiles
                : ConfigurationSourcePolicy.BuiltInBinary));
        loadCoroutine = LoadState == ResourceLoadState.Loading ? started : null;
        return true;
    }

    /// <summary>在菜单必要资源就绪后，幂等启动完整世界资源计划。</summary>
    public bool RequestWorldResources()
    {
        if (LoadState == ResourceLoadState.Ready) return true;
        if (LoadState != ResourceLoadState.Loading || !IsStartupReady || startupRootResourceAssets == null)
            return false;
        if (worldResourceSessionActive || worldResourceRequestQueued) return true;
        worldResourceSessionStartedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        if (loadCoroutine != null || startupResourceSessionRunning)
        {
            worldResourceRequestQueued = true;
            return true;
        }

        StartWorldResourceSession();
        return true;
    }

    /// <summary>世界对象与存档清理后释放世界目录，恢复菜单根资源和必要配置。</summary>
    public void ReleaseWorldResourcesAndRestoreStartup()
    {
        bool cancelQueuedOnly = worldResourceRequestQueued && !worldResourceSessionActive;
        worldResourceRequestQueued = false;
        if (inGameReloadCoroutine != null) StopCoroutine(inGameReloadCoroutine);
        CancelInPlaceResourceReloadSafely();
        if (cancelQueuedOnly && loadCoroutine != null)
        {
            worldResourceSessionStartedTimestamp = 0;
            return;
        }
        bool hasWorldScope = resourceAssets != null && resourceAssets != startupRootResourceAssets;
        if (!worldResourceSessionActive && !hasWorldScope)
        {
            worldResourceSessionStartedTimestamp = 0;
            return;
        }
        if (worldResourceSessionActive && loadCoroutine != null)
        {
            StopCoroutine(loadCoroutine);
            loadCoroutine = null;
            DisposeResourceSessionSourceConfigurationLease();
            loadPipeline?.Dispose();
            loadPipeline = null;
            GameplayTestResourceCache.Complete(false);
        }

        Exception cleanupError = null;
        try { ClearWorldResourceSession(); }
        catch (Exception exception) { cleanupError = exception; }
        worldResourceSessionActive = false;
        if (startupRootResourceAssets == null)
        {
            LoadState = ResourceLoadState.Failed;
            IsStartupReady = false;
            LastLoadError = "世界资源已释放，但主菜单资源根目录不存在。";
            resourceLoadFailed = true;
            showLoadingGUI = true;
            return;
        }

        resourceAssets = startupRootResourceAssets;
        if (cleanupError != null)
        {
            LoadState = ResourceLoadState.Failed;
            IsStartupReady = true;
            LastLoadError = $"世界资源已释放，但清理未完全成功：{cleanupError.Message}";
            resourceLoadFailed = true;
            showLoadingGUI = true;
            Debug.LogException(cleanupError, this);
            return;
        }

        LoadState = ResourceLoadState.Loading;
        IsStartupReady = true;
        LastLoadError = null;
        resourceLoadFailed = false;
        loadCoroutine = null;
        loadPipeline = null;
        loadingProgress = 1f;
        loadingText = "启动必要资源已就绪";
        showLoadingGUI = false;
        RefreshResourceLoadingPriorityForCurrentInteraction();
    }

    /// <summary>返回菜单时先撤销排队或正在执行的世界加载，实体清理完成后再释放 Scope。</summary>
    public void CancelWorldResourceLoadForMenuReturn()
    {
        bool canceledQueuedRequest = worldResourceRequestQueued && !worldResourceSessionActive;
        worldResourceRequestQueued = false;
        if (inGameReloadCoroutine != null) StopCoroutine(inGameReloadCoroutine);
        CancelInPlaceResourceReloadSafely();
        if (canceledQueuedRequest)
            worldResourceSessionStartedTimestamp = 0;
        if (!worldResourceSessionActive || loadCoroutine == null)
            return;

        StopCoroutine(loadCoroutine);
        loadCoroutine = null;
        DisposeResourceSessionSourceConfigurationLease();
        loadPipeline?.Dispose();
        loadPipeline = null;
        GameplayTestResourceCache.Complete(false);
    }

    private bool IsIdleStartupMenu => LoadState == ResourceLoadState.Loading && IsStartupReady &&
                                      loadCoroutine == null && !startupResourceSessionRunning &&
                                      !worldResourceSessionActive;

    private bool CanInterruptMenuWorldResourcePreload
    {
        get
        {
            GameManager manager = GameManager.ExistingInstance;
            return worldResourceSessionActive && loadCoroutine != null && manager != null && !manager.IsInGameWorld &&
                   !manager.IsWorldEntryInProgress;
        }
    }

    private void DisposeResourceSessionSourceConfigurationLease()
    {
        IDisposable lease = resourceSessionSourceConfigurationLease;
        resourceSessionSourceConfigurationLease = null;
        lease?.Dispose();
    }

    private void CancelInPlaceResourceReloadSafely()
    {
        try { cancelInPlaceReload?.Invoke(); }
        catch (Exception exception) { Debug.LogException(exception, this); }
        finally { ReleaseInPlaceSourceConfigurationLease(); }
    }

    private void StartWorldResourceSession()
    {
        if (worldResourceSessionActive || startupRootResourceAssets == null) return;
        worldResourceRequestQueued = false;
        worldResourceSessionActive = true;
        LastResourceLoadMilliseconds = 0;
        LastLoadError = null;
        resourceLoadFailed = false;
        LoadState = ResourceLoadState.Loading;
        IsStartupReady = true;
        showLoadingGUI = false;
        loadingProgress = 0f;
        loadingText = "准备世界资源";
        if (worldResourceSessionStartedTimestamp == 0)
            worldResourceSessionStartedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        RefreshResourceLoadingPriorityForCurrentInteraction();
        ConfigurationSourcePolicy sourcePolicy = configurationSourcePolicy;
        Coroutine started = StartCoroutine(RunResourceSession(startupOnly: false,
            requestedSourcePolicy: sourcePolicy));
        loadCoroutine = LoadState == ResourceLoadState.Loading ? started : null;
    }

    [Sirenix.OdinInspector.Button("重载所有资源")]
    public void HotReloadAllResources() => RequestResourceReload();

    /// <summary>源配置租约只由显式 Alt+R 请求传入，启动与入世默认读取构建包。</summary>
    private IEnumerator RunResourceSession(bool startupOnly, ConfigurationSourcePolicy requestedSourcePolicy)
    {
        bool startWorldAfterStartup = false;
        try
        {
            if (requestedSourcePolicy == ConfigurationSourcePolicy.SourceFiles)
                resourceSessionSourceConfigurationLease = BuiltInContentBinaryCatalog.UseSourceConfigurations();
            yield return RunResourceSessionCore(startupOnly, requestedSourcePolicy);

            startWorldAfterStartup = startupOnly && IsStartupReady && LoadState == ResourceLoadState.Loading &&
                                     worldResourceRequestQueued;
            if (startWorldAfterStartup && worldResourceSessionStartedTimestamp == 0)
                worldResourceSessionStartedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        finally
        {
            DisposeResourceSessionSourceConfigurationLease();
            GameplayTestResourceCache.Complete(false);
            if (startupOnly) startupResourceSessionRunning = false;
        }

        if (startWorldAfterStartup)
            StartWorldResourceSession();
    }

    private IEnumerator RunResourceSessionCore(bool startupOnly, ConfigurationSourcePolicy requestedSourcePolicy)
    {
        long sessionStartedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        Exception prepareError = null;
        try
        {
            cacheBuildStartRevision = resourceSessionStarted ? BinaryContentCache.BuildRevision : 0;
            if (startupOnly)
            {
                startupResourceSessionStartedTimestamp = sessionStartedTimestamp;
                completingInitialResourceLoad = !resourceSessionStarted;
                resourceSessionStarted = true;
            }
            GameplayTestResourceCache.Begin();
            ItemMgr.GetInstance()?.ClearResourcePools();
            if (startupOnly)
            {
                ClearResourceSession();
                startupRootResourceAssets = new ResourceAssetScope();
                resourceAssets = startupRootResourceAssets;
            }
            else
            {
                if (startupRootResourceAssets == null || !IsStartupReady)
                    throw new InvalidOperationException("完整世界资源加载要求启动菜单 Scope 已就绪。");
                resourceAssets = new ResourceAssetScope();
            }
            loadPipeline = CreateResourceLoadPlan(startupOnly: startupOnly);
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
        PersistentRuntimeObjectHierarchy.OrganizeUnregisteredPersistentRoots();
        if (startupOnly)
        {
            if (!IsStartupReady)
            {
                FailResourceSession("startup-ready", new InvalidDataException("菜单必要资源计划结束时尚未发布启动就绪状态。"));
                yield break;
            }
            configurationSourcePolicy = requestedSourcePolicy;
            GameplayTestResourceCache.Complete(true);
            RestoreBackgroundLoadingPolicy();
            LoadState = ResourceLoadState.Loading;
            IsStartupReady = true;
            loadCoroutine = null;
            loadingProgress = 1f;
            showLoadingGUI = false;
            ResourceLoadPipeline completedStartupPlan = loadPipeline;
            loadPipeline = null;
            Debug.Log($"[GameRes] 菜单必要资源就绪：{LastStartupLoadMilliseconds:F0} ms；世界资源将在菜单后台预加载。", this);
            if (completedStartupPlan != null)
                Debug.Log($"[GameRes] 菜单阶段推进 {completedStartupPlan.ExecutionMilliseconds:F1} ms，" +
                          $"帧间/异步等待 {completedStartupPlan.BetweenAdvancesMilliseconds:F1} ms，" +
                          $"最长调度间隔 {completedStartupPlan.LongestBetweenAdvancesMilliseconds:F1} ms。", this);
            yield break;
        }

        LoadState = ResourceLoadState.Ready;
        GameplayTestResourceCache.Complete(true);
        RestoreBackgroundLoadingPolicy();
        IsStartupReady = true;
        loadCoroutine = null;
        loadingProgress = 1;
        showLoadingGUI = false;
        LastResourceLoadMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - worldResourceSessionStartedTimestamp) *
                                       1000d / System.Diagnostics.Stopwatch.Frequency;
        bool initialLoadOverBudget = completingInitialResourceLoad &&
                                     LastResourceLoadMilliseconds > InitialResourceLoadTargetMilliseconds;
        completingInitialResourceLoad = false;
        Debug.Log($"<color=#7EDC8A><b>[GameRes] 全部资产加载完成</b></color>，总耗时：" +
                  $"<color=#FFD166><b>{LastResourceLoadMilliseconds / 1000d:F2} 秒（{LastResourceLoadMilliseconds:F0} ms）</b></color>。", this);
        if (initialLoadOverBudget)
            Debug.LogWarning($"[GameRes] 首次世界资源完整加载超出 10 秒目标：{LastResourceLoadMilliseconds:F0} ms；请按阶段计时继续定位。", this);
        // 只拆分诊断口径，完整计时仍从会话准备前开始，不能扣除首帧或异步等待。
        Debug.Log($"[GameRes] 加载耗时拆分：推进 {loadPipeline.ExecutionMilliseconds:F1} ms，" +
                  $"帧间/异步等待 {loadPipeline.BetweenAdvancesMilliseconds:F1} ms，" +
                  $"最长调度间隔 {loadPipeline.LongestBetweenAdvancesMilliseconds:F1} ms，" +
                  $"推进 {loadPipeline.ExecutionSlices} 次；等待包含渲染与编辑器工作，不代表纯读盘耗时。", this);
        Debug.Log($"[GameRes] 资源目录就绪：{ItemDefinitions.Count} 个物品/Actor，{TileBlockDict.Count} 个 TileBlock，" +
                  $"{recipeById.Count} 个配方，持有 {resourceAssets.HandleCount} 个资源请求。");
        Debug.Log($"[启动二进制缓存] 其他 JSON 配置：命中 {JsonConfigurationCache.Hits}，" +
                  $"新建 {JsonConfigurationCache.Builds}，写入失败 {JsonConfigurationCache.WriteFailures}。");
        Debug.Log($"[构建期二进制目录] 本轮读取 {BuiltInContentBinaryCatalog.ReadCount} 个配置载荷，" +
                  $"启动前接入 {BuiltInContentBinaryCatalog.InitializationMilliseconds:F1} ms。");
        Debug.Log($"[Sprite 批量目录] {FlatWorldSpriteAtlasDiagnostics.RuntimeSummary}。");
        loadPipeline = null;
    }

    /// <summary>保留原始异常与阶段，清理异常作为附加原因记录，不覆盖首个失败。</summary>
    private void FailResourceSession(string stage, Exception exception)
    {
        DisposeResourceSessionSourceConfigurationLease();
        bool retainStartupMenu = worldResourceSessionActive && startupRootResourceAssets != null;
        GameplayTestResourceCache.Complete(false);
        try
        {
            if (retainStartupMenu) ClearWorldResourceSession();
            else ClearResourceSession();
        }
        catch (Exception cleanupError) { exception = new AggregateException(exception, cleanupError); }
        LoadState = ResourceLoadState.Failed;
        completingInitialResourceLoad = false;
        worldResourceSessionActive = false;
        worldResourceRequestQueued = false;
        startupResourceSessionRunning = false;
        IsStartupReady = retainStartupMenu && startupRootResourceAssets != null;
        resourceLoadFailed = true;
        loadCoroutine = null;
        LastLoadError = $"阶段 {stage} 失败：{exception.Message}";
        resourceLoadingFailureStage = stage;
        loadingText = $"资源加载失败（{stage}），修正配置后可按 Alt+R 重试";
        showLoadingGUI = true;
        Debug.LogError($"[GameRes] {LastLoadError}\n{exception}", this);
    }

    /// <summary>销毁前取消嵌套流程；重复场景实例不能释放正式实例的目录。</summary>
    private void DisposeResourceSession()
    {
        if (instance != this) return;
        GameplayTestResourceCache.Complete(false);
        if (inGameReloadCoroutine != null) StopCoroutine(inGameReloadCoroutine);
        CancelInPlaceResourceReloadSafely();
        if (reloadWorldOwner != null)
            reloadWorldOwner.BackToHelloScene_Event_End -= ReleaseRetiredWorldResources;
        LoadState = ResourceLoadState.Disposed;
        if (loadCoroutine != null) StopCoroutine(loadCoroutine);
        loadCoroutine = null;
        DisposeResourceSessionSourceConfigurationLease();
        loadPipeline?.Dispose();
        try { ClearResourceSession(); }
        catch (Exception exception) { Debug.LogException(exception, this); }
        finally { ReleaseAddressablesInitialization(); }
    }

    /// <summary>依赖方先卸载，目录再清空，最后释放底层资源；每项清理都独立执行。</summary>
    private void ClearResourceSession()
    {
        RestoreBackgroundLoadingPolicy();
        var errors = new List<Exception>();
        void Clear(Action action) { try { action(); } catch (Exception exception) { errors.Add(exception); } }
        // 先让使用者释放 BRG 注册/材质，再销毁共享 Mesh，最后卸载源 Sprite/Tile/MOD。
        Clear(SharedSpriteMeshCache.Clear);
        Clear(() => WorldRenderingManager.ClearExistingVisualEffectPool());
        Clear(() => ModRuntimeManager.Instance?.UnloadForResourceReload());
        Clear(ReleaseRetiredResourceSessions);
        Clear(ClearAllDictionaries);
        Clear(() => worldAuthoringResources.Clear());
        Clear(PlayerCreationTemplateCatalogService.Reset);
        Clear(TimeSystemConfigService.Reset);
        Clear(AtmosphericGreenhouseConfigurationService.Reset);
        Clear(WaterCurrentPushConfigService.Reset);
        Clear(MachineCatalog.Clear);
        Clear(EquipmentDurabilityRules.Clear);
        var scopes = new HashSet<ResourceAssetScope>();
        if (resourceAssets != null) scopes.Add(resourceAssets);
        if (startupRootResourceAssets != null) scopes.Add(startupRootResourceAssets);
        foreach (ResourceAssetScope scope in scopes) Clear(scope.Dispose);
        resourceAssets = null;
        startupRootResourceAssets = null;
        LoadedCount = 0;
        IsStartupReady = false;
        startupMenuPrerequisitesReady = false;
        startupPrefabLocationIds.Clear();
        startupUiPrefabSnapshot.Clear();
        startupPlayerCreationConfig = null;
        startupTimeSystemConfig = null;
        startupGreenhouseConfiguration = null;
        startupWaterCurrentCatalog = null;
        worldResourceSessionActive = false;
        worldResourceRequestQueued = false;
        worldResourceSessionStartedTimestamp = 0;
        if (errors.Count > 0) throw new AggregateException("资源会话清理失败。", errors);
    }

    /// <summary>只清世界目录和世界 Scope；菜单根 Scope 与启动必要配置保持存活。</summary>
    private void ClearWorldResourceSession()
    {
        RestoreBackgroundLoadingPolicy();
        var errors = new List<Exception>();
        void Clear(Action action) { try { action(); } catch (Exception exception) { errors.Add(exception); } }
        ResourceAssetScope releasedWorldScope = resourceAssets != null && resourceAssets != startupRootResourceAssets
            ? resourceAssets : null;
        Clear(() => ItemMgr.GetInstance()?.ClearResourcePools());
        Clear(SharedSpriteMeshCache.Clear);
        Clear(() => WorldRenderingManager.ClearExistingVisualEffectPool());
        Clear(() => ModRuntimeManager.Instance?.UnloadForResourceReload());
        Clear(ReleaseRetiredResourceSessions);
        Clear(ClearAllDictionaries);
        Clear(() => worldAuthoringResources.Clear());
        Clear(PlayerCreationTemplateCatalogService.Reset);
        Clear(TimeSystemConfigService.Reset);
        Clear(AtmosphericGreenhouseConfigurationService.Reset);
        Clear(WaterCurrentPushConfigService.Reset);
        Clear(MachineCatalog.Clear);
        Clear(EquipmentDurabilityRules.Clear);
        if (releasedWorldScope != null)
        {
            Clear(releasedWorldScope.Dispose);
            int remainingCatalogs = releasedWorldScope.LoadedSpritePurposeCatalogCount;
            int remainingBindings = releasedWorldScope.BoundSpriteAddressCount;
            if (remainingCatalogs != 0 || remainingBindings != 0)
                Debug.LogWarning($"[GameRes] 世界 Scope 释放后仍有图集引用：用途目录={remainingCatalogs}，Sprite 地址={remainingBindings}。", this);
            else
                Debug.Log("[GameRes] 世界用途图集与 Sprite 地址已随 world Scope 释放。", this);
        }
        resourceAssets = startupRootResourceAssets;
        LoadedCount = 0;
        IsStartupReady = false;
        startupMenuPrerequisitesReady = false;
        worldResourceSessionActive = false;
        worldResourceRequestQueued = false;
        try { RestoreStartupMenuSnapshot(); }
        catch (Exception exception) { errors.Add(exception); }
        worldResourceSessionStartedTimestamp = 0;
        if (errors.Count > 0) throw new AggregateException("世界资源会话清理失败。", errors);
    }

    private void RestoreStartupMenuSnapshot()
    {
        if (startupRootResourceAssets == null || startupPlayerCreationConfig == null || startupTimeSystemConfig == null ||
            startupGreenhouseConfiguration == null || startupWaterCurrentCatalog == null ||
            startupUiPrefabSnapshot.Count != StartupPrefabKeys.Length)
            throw new InvalidDataException("主菜单必要资源快照不完整，无法恢复菜单阶段。");

        PlayerCreationTemplateCatalogService.ReplaceBuiltIn(startupPlayerCreationConfig);
        TimeSystemConfigService.ReplaceCatalog(startupTimeSystemConfig);
        AtmosphericGreenhouseConfigurationService.ReplaceConfiguration(startupGreenhouseConfiguration);
        WaterCurrentPushConfigService.ReplaceCatalog(startupWaterCurrentCatalog);
        foreach (string key in StartupPrefabKeys)
        {
            if (!startupUiPrefabSnapshot.TryGetValue(key, out GameObject prefab) || prefab == null)
                throw new InvalidDataException($"菜单必要 Prefab 快照缺失：{key}");
            RegisterPrefabAlias(key, prefab);
        }
        LoadedCount = StartupPrefabKeys.Length;
    }

    /// <summary>丢弃当前目录及其派生索引；不会在清理时创建新的管理器。</summary>
    private void ClearAllDictionaries()
    {
        AllPrefabs.Clear();
        ItemDefinitions.Clear();
        ItemDefinitionRevision++;
        itemDefinitionAliases.Clear();
        ActorDefinitions.Clear();
        LootTables.Clear();
        ActorDefinitionCatalogLoader.ResetRuntimeCatalog();
        SpawnerConfigCatalogService.Reset();
        NaturalGenerationRuleCatalogService.Reset();
        RiverGenerationConfigService.Reset();
        recipeDict.Clear();
        recipeCatalog.Clear();
        tileBaseDict.Clear();
        ClearTileDefinitions();
        BuffDefinitions.Clear();
        ContaminationDefinitions.Clear();
        LiquidDefinitions.Clear();
        LiquidTypes = null;
        FluidCatalog.Reset();
        AtmosphereCatalog.Reset();
        FlatWorld.Spaceflight.SpaceCatalog.Reset();
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
        if (!addressablesInitializationHandle.IsValid())
            addressablesInitializationHandle = Addressables.InitializeAsync(false);

        yield return addressablesInitializationHandle;
        try
        {
            ResourceAssetScope.Require(addressablesInitializationHandle, "Addressables 初始化目录");
        }
        catch
        {
            ReleaseAddressablesInitialization();
            throw;
        }
    }

    /// <summary>GameRes 销毁时释放跨资源会话持有的 Addressables 初始化句柄。</summary>
    private void ReleaseAddressablesInitialization()
    {
        if (!addressablesInitializationHandle.IsValid())
            return;

        try { Addressables.Release(addressablesInitializationHandle); }
        finally { addressablesInitializationHandle = default; }
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
            {
                string editorHint = string.Empty;
#if UNITY_EDITOR
                // 空标签也可能来自编辑器残留目录，报出播放配置以区分资源缺失与会话失效。
                editorHint = $"；播放配置：optionsEnabled={UnityEditor.EditorSettings.enterPlayModeOptionsEnabled}, " +
                    $"options={UnityEditor.EditorSettings.enterPlayModeOptions}。" +
                    "若静态目录存在对应标签，请结束当前播放并启用脚本域/场景重载后重新播放；" +
                    "Alt+R 只重载游戏资源会话，不能重建失效的编辑器资源组引用。";
#endif
                throw new InvalidDataException($"必需标签 {string.Join(", ", labels)} 未解析到 {type.Name}；" +
                    $"当前目录：{string.Join(", ", Addressables.ResourceLocators.Select(locator => locator.LocatorId))}{editorHint}");
            }
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
        var loaded = new List<T>(locations.Count);
        yield return LoadResourceLocations(locations, loaded, $"标签 {label}");
        foreach (T asset in loaded)
        {
            if (asset == null) throw new InvalidDataException($"标签 {label} 加载出空资源。");
            string id = key(asset);
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException($"{label} 资源 {asset.name} 缺少稳定 ID。");
            if (!target.TryAdd(id, asset)) throw new InvalidDataException($"{label} ID 重复：{id} ({target[id].name} / {asset.name})");
            LoadedCount++;
            if (ShouldYieldResourceWork()) yield return null;
        }
    }

    /// <summary>
    /// 只预载主菜单能够立刻访问的少量运行时 UI；其余玩法 Prefab 继续留给完整资源阶段。
    /// Addressables 会同时加载这些 Prefab 的字体、Sprite 等依赖，因此显示主菜单后不会出现缺失外观。
    /// </summary>
    private IEnumerator LoadStartupUiPrefabs()
    {
        if (startupUiPrefabSnapshot.Count == StartupPrefabKeys.Length)
        {
            RestoreStartupUiPrefabsFromSnapshot();
            yield break;
        }

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

        var locationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (IResourceLocation location in selected)
            locationIds.Add(GetLocationIdentity(location));

        var loadedNames = new HashSet<string>(StringComparer.Ordinal);
        var prefabSnapshot = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        yield return ResourceLoadQueue.Run(selected.Count,
            index =>
            {
                IResourceLocation location = selected[index];
                Debug.Log($"[GameRes] 启动 UI 加载：{StartupPrefabKeys[index]} ({location.PrimaryKey})");
                return resourceAssets.Load<GameObject>(location);
            }, handle => handle.IsDone,
            (index, handle) =>
            {
                GameObject prefab = ResourceAssetScope.Require(handle, $"启动必要 UI Prefab {StartupPrefabKeys[index]}");
                RegisterPrefabAlias(prefab.name, prefab);
                prefabSnapshot.Add(StartupPrefabKeys[index], prefab);
                loadedNames.Add(prefab.name);
                LoadedCount++;
            }, ShouldYieldResourceWork,
            completed => loadPipeline.Report(0.9f * completed / selected.Count));

        foreach (string prefabKey in StartupPrefabKeys)
        {
            if (!loadedNames.Contains(prefabKey))
                throw new InvalidDataException($"启动必要 UI Prefab 未成功加载：{prefabKey}");
        }
        startupPrefabLocationIds = locationIds;
        startupUiPrefabSnapshot = prefabSnapshot;
    }

    private void RestoreStartupUiPrefabsFromSnapshot()
    {
        foreach (string prefabKey in StartupPrefabKeys)
        {
            if (!startupUiPrefabSnapshot.TryGetValue(prefabKey, out GameObject prefab) || prefab == null)
                throw new InvalidDataException($"启动 UI Prefab 快照缺失：{prefabKey}");
            RegisterPrefabAlias(prefabKey, prefab);
        }
        loadPipeline?.Report(1f);
    }

    /// <summary>发布主菜单可交互状态；完整世界资源在菜单空闲时自动后台预加载。</summary>
    private void PublishStartupReady()
    {
        IsStartupReady = true;
        if (preparingInPlaceReload) return;
        if (startupResourceSessionStartedTimestamp != 0)
            LastStartupLoadMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - startupResourceSessionStartedTimestamp) *
                                          1000d / System.Diagnostics.Stopwatch.Frequency;
        RefreshResourceLoadingPriorityForCurrentInteraction();
        showLoadingGUI = false;
        Debug.Log("[GameRes] 启动必要资源已就绪，主菜单开放；即将后台预加载世界资源。");
        StartupResourcesReady?.Invoke(this);
        if (startupRootResourceAssets != null &&
            (startupRootResourceAssets.LoadedSpritePurposeCatalogCount != 0 ||
             startupRootResourceAssets.BoundSpriteAddressCount != 0))
            Debug.LogWarning("[GameRes] 菜单就绪回调后仍有用途图集目录或 Sprite 地址绑定；请检查启动 Scope 的图集依赖。", this);
    }

    /// <summary>菜单必要配置、UI 与音库全部完成后再发布菜单就绪。</summary>
    private IEnumerator PublishStartupReadyStage()
    {
        if (preparingInPlaceReload) PublishStartupReady();
        else
        {
            startupMenuPrerequisitesReady = true;
            TryPublishStartupReadyAfterAudioPreload();
        }
        yield return null;
    }

    private IEnumerator CompleteStartupAudioResourcePreload()
    {
        yield return FlatWorld.Audio.AudioService.PreloadAsync();
        TryPublishStartupReadyAfterAudioPreload();
    }

    private void TryPublishStartupReadyAfterAudioPreload()
    {
        if (!startupMenuPrerequisitesReady || IsStartupReady || LoadState != ResourceLoadState.Loading ||
            !FlatWorld.Audio.AudioService.TryCompleteStartupResourcePreload()) return;
        startupMenuPrerequisitesReady = false;
        PublishStartupReady();
    }

    // 帧间通知与协程内阶段共享失败语义，监听器异常也必须撤销仍在推进的资源目录。
    private void AdvanceStartupAudioReadiness()
    {
        try { TryPublishStartupReadyAfterAudioPreload(); }
        catch (Exception exception)
        {
            if (loadCoroutine != null) StopCoroutine(loadCoroutine);
            loadPipeline?.Dispose();
            FailResourceSession("audio", exception);
        }
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
        var loaded = new List<GameObject>(selected.Count);
        yield return LoadResourceLocations(selected, loaded, "运行时 Prefab");
        var moduleAliases = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        // 先登记全部主名称，再处理模块别名，避免顺序决定最终命中资源。
        foreach (GameObject prefab in loaded)
        {
            if (prefab == null) throw new InvalidDataException("Prefab 计划包含空资源。");
            RegisterPrefabAlias(prefab.name, prefab);
            LoadedCount++;
            if (ShouldYieldResourceWork()) yield return null;
        }
        foreach (GameObject prefab in loaded)
        {
            CollectPrefabAliases(prefab, moduleAliases);
            if (ShouldYieldResourceWork()) yield return null;
        }
        RegisterUniqueModuleAliases(moduleAliases);
        EnsureIndustrialRuntimeTemplates();
        Debug.Log($"[GameRes] Prefab 加载计划：后台加载 {selected.Count}，启动阶段已预载 {startupPrefabLocationIds.Count} 个 UI Prefab。");
    }

    /// <summary>位置资源最多四个在途请求，完成即补槽，发出与收尾共用主线程预算。</summary>
    private IEnumerator LoadResourceLocations<T>(IList<IResourceLocation> locations, List<T> loaded, string context)
        where T : UnityEngine.Object
    {
        var results = new T[locations.Count];
        yield return ResourceLoadQueue.Run(locations.Count,
            index => resourceAssets.Load<T>(locations[index]), handle => handle.IsDone,
            (index, handle) => results[index] = ResourceAssetScope.Require(handle, context),
            ShouldYieldResourceWork,
            completed => loadPipeline.Report(0.8f * completed / locations.Count));
        foreach (T result in results)
        {
            loaded.Add(result);
            if (ShouldYieldResourceWork()) yield return null;
        }
    }

    #endregion
}
