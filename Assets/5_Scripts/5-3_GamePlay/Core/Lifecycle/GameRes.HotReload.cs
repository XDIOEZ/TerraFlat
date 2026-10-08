using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlatWorld.Gameplay.Quests;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 当前存档内的资源更新事务：跨帧准备候选目录，正式世界继续运行；校验成功后同帧切换。
/// 不写盘、不退出、不重建区块；发布后只重建本地 Player 运行时外壳与 UI，继续复用同一份玩家 Data。
/// 旧代资源保留到世界彻底退出，防止其它活跃实例持有失效引用。
/// </summary>
public partial class GameRes
{
    #region 状态与通知

    private bool preparingInPlaceReload;
    private bool resourceReloadInProgress;
    private float resourceReloadProgress;
    private Action cancelInPlaceReload;
    private readonly List<Action> retiredResourceReleases = new();
    /// <summary>当前世界暂留的上一代物品定义，世界退出时随旧资源一起移除。</summary>
    private readonly Dictionary<string, RuntimeItemDefinition> retiredWorldItemDefinitions =
        new(StringComparer.OrdinalIgnoreCase);
    private GameManager reloadWorldOwner;

    /// <summary>候选加载期间正式 LoadState 仍为 Ready，玩法查询可继续读取旧目录。</summary>
    public bool IsResourceReloadInProgress => resourceReloadInProgress;
    /// <summary>只统计成功发布的原位更新，便于运行时诊断连续 F5。</summary>
    public int ResourceReloadVersion { get; private set; }
    /// <summary>原位更新失败原因；失败不改变正式目录的 Ready 状态。</summary>
    public string LastResourceReloadError { get; private set; }
    /// <summary>部分 Actor 定义未更新时保留明确原因，成功发布不隐去内容错误。</summary>
    public string LastResourceReloadWarnings { get; private set; }
    /// <summary>成功发布后通知需要主动重建缓存的系统；不复用世界进入/退出事件。</summary>
    public event Action ResourcesReloaded;

    #endregion

    #region 候选会话

    private bool StartInPlaceResourceReload(GameManager manager)
    {
        if (resourceReloadInProgress || LoadState != ResourceLoadState.Ready || !manager.IsGameplayReady)
            return false;
        resourceReloadInProgress = true;
        resourceReloadProgress = 0f;
        LastResourceReloadError = null;
        LastResourceReloadWarnings = null;
        if (reloadWorldOwner != manager)
        {
            if (reloadWorldOwner != null)
                reloadWorldOwner.BackToHelloScene_Event_End -= ReleaseRetiredWorldResources;
            reloadWorldOwner = manager;
            manager.BackToHelloScene_Event_End += ReleaseRetiredWorldResources;
        }
        manager.BeginResourceReloadStatus();
        Coroutine started = StartCoroutine(ReloadResourcesInPlace(manager));
        inGameReloadCoroutine = resourceReloadInProgress ? started : null;
        return true;
    }

    /// <summary>既有加载计划只负责构建与校验，事务负责隔离、兼容性检查和资源所有权转移。</summary>
    private IEnumerator ReloadResourcesInPlace(GameManager manager)
    {
        var candidateAssets = new ResourceAssetScope();
        ResourceReloadContext context = null;
        ResourceLoadPipeline plan = null;
        ModRuntimeManager mods = ModRuntimeManager.Instance;
        GameSaveData world = SaveDataMgr.Instance?.SaveData;
        Dictionary<string, RuntimeItemDefinition> previousItems = ItemDefinitions;
        ActorDefinitionCatalogLoader.ReloadSnapshot actorSnapshot = null;
        MachineCatalog.ReloadSnapshot previousMechanical = null;
        Dictionary<string, RuntimeTileDefinition> previousTiles = TileBlockDict;
        LiquidTypeCatalog previousLiquidTypes = LiquidTypes;
        string[] previousLiquids = LiquidDefinitions.Keys.ToArray();
        FluidCatalog previousFluids = FluidCatalog.Default;
        ResourceAssetScope previousAssets = resourceAssets;
        bool committed = false;
        bool cleaned = false;
        bool terminalFeedbackReported = false;
        string[] retainedItemIds = Array.Empty<string>();
        string[] retainedMechanicalEntries = Array.Empty<string>();

        // 显式清理入口同时用于正常完成、世界切换、组件销毁和协程取消，且只能执行一次。
        cancelInPlaceReload = () =>
        {
            if (cleaned) return;
            cleaned = true;
            if (!committed)
            {
                try { context?.InCandidate(() => { plan?.Dispose(); mods?.ReleaseCandidateResources(); }); }
                catch (Exception exception) { Debug.LogException(exception, this); }
                try { candidateAssets.Dispose(); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
            mods?.FinishResourceReload();
            preparingInPlaceReload = false;
            resourceReloadInProgress = false;
            if (!terminalFeedbackReported && manager != null)
                manager.CancelResourceReloadStatus();
            inGameReloadCoroutine = null;
            cancelInPlaceReload = null;
        };

        Debug.Log("[GameRes] F5 原位更新开始：世界继续运行，候选目录加载期间保留当前资源。", this);
        try
        {
            Exception error = null;
            try
            {
                if (mods == null) throw new InvalidOperationException("当前资源会话缺少 MOD 管理器。");
                previousMechanical = MachineCatalog.CaptureReloadSnapshot();
                actorSnapshot = ActorDefinitionCatalogLoader.CaptureReloadSnapshot(this);
                context = CreateInPlaceReloadContext(candidateAssets, mods);
                plan = CreateResourceLoadPlan(actorSnapshot);
                // 发布前单独验证机械目录；不清除正在运行的机械网络和动力源。
                plan.Add("mechanical-reload", "校验机械配置", 1, () => RunAction(MachineCatalog.EnsureLoaded));
                context.Add(() => loadPipeline, value => loadPipeline = value, plan);
            }
            catch (Exception exception) { error = exception; }
            if (error == null)
            {
                yield return context.Run(plan.Run(), () => manager != null && manager.IsInGameWorld &&
                    ReferenceEquals(SaveDataMgr.Instance?.SaveData, world));
                error = plan.Failure;
                if (manager == null || !manager.IsInGameWorld || !ReferenceEquals(SaveDataMgr.Instance?.SaveData, world))
                    error = new OperationCanceledException("世界已退出或切换，候选更新已取消。");
            }

            if (error == null)
            {
                try
                {
                    // 全局脚本状态直到提交前才捕获，加载期间产生的进度不会被旧快照覆盖。
                    ModSaveMetadata modState = mods.CaptureSaveMetadata();
                    context.InCandidate(() =>
                    {
                        retainedItemIds = RetainRemovedItemDefinitions(previousItems);
                        retainedMechanicalEntries = MachineCatalog.RetainMissingEntries(previousMechanical);
                        ValidateInPlaceCompatibility(previousItems, previousTiles, previousLiquidTypes, previousLiquids);
                        foreach (string id in previousFluids.Definitions.Keys)
                            if (!FluidCatalog.Default.TryGet(id, out _))
                                throw new InvalidDataException($"运行中不能删除流体身份：{id}");
                        mods.PrepareReloadState(modState);
                    });
                    Action previousModRelease = mods.CaptureResourceRelease();
                    context.Commit();
                    mods.PublishContainerPortFactories();
                    committed = true;
                    UpdateRetiredWorldItemDefinitions(retainedItemIds);
                    LoadState = ResourceLoadState.Ready;
                    IsStartupReady = true;
                    preparingInPlaceReload = false;
                    mods.FinishResourceReload();
                    retiredResourceReleases.Add(previousModRelease);
                    retiredResourceReleases.Add(previousAssets.Dispose);
                    ResourceReloadVersion++;
                    LastResourceReloadWarnings = actorSnapshot.Issues.Count == 0
                        ? null : string.Join("\n", actorSnapshot.Issues);
                    LastLoadError = null;
                    loadingProgress = 1f;
                    loadingText = "资源已在当前世界更新";
                }
                catch (Exception exception) { error = exception; }
            }

            if (error != null)
            {
                LastResourceReloadError = $"阶段 {plan?.CurrentStage ?? "prepare"}：{error.Message}";
                if (manager != null)
                {
                    if (error is OperationCanceledException)
                        manager.CancelResourceReloadStatus();
                    else
                        manager.EndResourceReloadStatus(false);
                }
                terminalFeedbackReported = true;
                Debug.LogWarning($"[GameRes] F5 原位更新未发布，当前世界与旧资源保持运行。{LastResourceReloadError}", this);
                yield break;
            }

            PublishInPlaceReload(previousItems, mods);
            ItemMgr itemManager = ItemMgr.GetInstance();
            if (itemManager != null)
                yield return itemManager.ReloadLocalPlayerAfterResourceReload();

            if (retainedItemIds.Length > 0 || retainedMechanicalEntries.Length > 0)
                Debug.Log($"[GameRes] F5 已发布；当前世界暂时保留旧定义：物品 [{string.Join("、", retainedItemIds)}]，机械 [{string.Join("、", retainedMechanicalEntries)}]（退出世界后释放）。", this);
            if (LastResourceReloadWarnings != null)
                Debug.LogWarning($"[GameRes] F5 已发布有效资源，以下 Actor 定义未更新：\n{LastResourceReloadWarnings}", this);
            if (manager != null)
                manager.EndResourceReloadStatus(true);
            terminalFeedbackReported = true;
            Debug.Log($"[GameRes] F5 原位更新完成：版本={ResourceReloadVersion}，世界实例保持不变，本地玩家与运行时 UI 已按最新 Prefab 重建。", this);
        }
        finally { cancelInPlaceReload?.Invoke(); }
    }

    /// <summary>全部目录显式登记；新增静态目录应由其所有者提供同类隔离入口。</summary>
    private ResourceReloadContext CreateInPlaceReloadContext(ResourceAssetScope assets, ModRuntimeManager mods)
    {
        var context = new ResourceReloadContext();
        context.Add(() => preparingInPlaceReload, value => preparingInPlaceReload = value, true);
        context.Add(() => resourceAssets, value => resourceAssets = value, assets);
        context.Add(() => LoadedCount, value => LoadedCount = value, 0);
        context.Add(() => IsStartupReady, value => IsStartupReady = value, false);
        context.Add(() => LoadState, value => LoadState = value, ResourceLoadState.Loading);
        context.AddDictionary(() => AllPrefabs, value => AllPrefabs = value);
        context.AddDictionary(() => ItemDefinitions, value => ItemDefinitions = value);
        context.AddDictionary(() => itemDefinitionAliases, value => itemDefinitionAliases = value);
        context.AddDictionary(() => ActorDefinitions, value => ActorDefinitions = value);
        context.AddDictionary(() => LootTables, value => LootTables = value);
        context.AddDictionary(() => recipeDict, value => recipeDict = value);
        context.Add(() => recipeCatalog, value => recipeCatalog = value, new CraftingRecipeCatalog());
        context.AddDictionary(() => tileBaseDict, value => tileBaseDict = value);
        context.AddDictionary(() => TileBlockDict, value => TileBlockDict = value);
        context.AddDictionary(() => tileDefinitionsByNumber, value => tileDefinitionsByNumber = value);
        context.AddDictionary(() => BuffDefinitions, value => BuffDefinitions = value);
        context.AddDictionary(() => ContaminationDefinitions, value => ContaminationDefinitions = value);
        context.AddDictionary(() => LiquidDefinitions, value => LiquidDefinitions = value);
        context.Add(() => LiquidTypes, value => LiquidTypes = value, (LiquidTypeCatalog)null);
        context.Add(() => textLibraryService, value => textLibraryService = value, TextLibraryService.Empty);
        context.AddDictionary(() => InventoryInitDict, value => InventoryInitDict = value);
        context.AddDictionary(() => SkillDict, value => SkillDict = value);
        context.AddSet(() => startupPrefabLocationIds, value => startupPrefabLocationIds = value);
        ActorDefinitionCatalogLoader.ConfigureResourceReload(context);
        PlayerCreationTemplateCatalogService.ConfigureResourceReload(context);
        TimeSystemConfigService.ConfigureResourceReload(context);
        WaterCurrentPushConfigService.ConfigureResourceReload(context);
        SpawnerConfigCatalogService.ConfigureResourceReload(context);
        NaturalGenerationRuleCatalogService.ConfigureResourceReload(context);
        RiverGenerationConfigService.ConfigureResourceReload(context);
        AnimalSkillCatalogService.ConfigureResourceReload(context);
        FluidCatalog.ConfigureResourceReload(context);
        AtmosphereCatalog.ConfigureResourceReload(context);
        QuestCatalog.ConfigureResourceReload(context);
        MachineCatalog.ConfigureResourceReload(context);
        mods.ConfigureResourceReload(context);
        return context;
    }

    #endregion

    #region 发布与兼容

    /// <summary>新目录删除或重命名物品 ID 时，当前世界继续使用上一代定义直至退出。</summary>
    private string[] RetainRemovedItemDefinitions(IReadOnlyDictionary<string, RuntimeItemDefinition> previousItems)
    {
        var retainedIds = new List<string>();
        foreach (KeyValuePair<string, RuntimeItemDefinition> pair in previousItems)
        {
            if (TryGetItemDefinition(pair.Key, out _)) continue;
            ItemDefinitions.Add(pair.Key, pair.Value);
            retainedIds.Add(pair.Key);
        }
        return retainedIds.ToArray();
    }

    /// <summary>记录本次候选仍缺失的身份；被新目录重新定义的身份不再视为退役。</summary>
    private void UpdateRetiredWorldItemDefinitions(IEnumerable<string> retainedItemIds)
    {
        var retainedIds = new HashSet<string>(retainedItemIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (string id in retiredWorldItemDefinitions.Keys.Where(id => !retainedIds.Contains(id)).ToArray())
            retiredWorldItemDefinitions.Remove(id);
        foreach (string id in retainedIds)
            if (ItemDefinitions.TryGetValue(id, out RuntimeItemDefinition definition))
                retiredWorldItemDefinitions[id] = definition;
    }

    /// <summary>先从新会话目录移除临时旧身份，再释放承载其 Prefab、Sprite 与 MOD 资源的旧代。</summary>
    private void RemoveRetiredWorldItemDefinitions()
    {
        foreach (KeyValuePair<string, RuntimeItemDefinition> pair in retiredWorldItemDefinitions)
            if (ItemDefinitions.TryGetValue(pair.Key, out RuntimeItemDefinition current) &&
                ReferenceEquals(current, pair.Value))
                ItemDefinitions.Remove(pair.Key);
        retiredWorldItemDefinitions.Clear();
    }

    /// <summary>旧物品身份须已被暂留；地块稳定编号和液体编号表仍不能改变。</summary>
    private void ValidateInPlaceCompatibility(
        IReadOnlyDictionary<string, RuntimeItemDefinition> items,
        IReadOnlyDictionary<string, RuntimeTileDefinition> tiles,
        LiquidTypeCatalog liquids,
        string[] liquidIds)
    {
        foreach (string id in items.Keys)
            if (!TryGetItemDefinition(id, out _)) throw new InvalidDataException($"运行中不能删除物品定义：{id}");
        foreach (RuntimeTileDefinition tile in tiles.Values)
            if (!TileBlockDict.TryGetValue(tile.Id, out RuntimeTileDefinition current) || current.RuntimeTileId != tile.RuntimeTileId)
                throw new InvalidDataException($"运行中不能删除地块或改变稳定编号：{tile.Id}");
        if (liquidIds.Length != LiquidDefinitions.Count || liquidIds.Any(id => !LiquidDefinitions.ContainsKey(id)))
            throw new InvalidDataException("运行中不能增删液体身份；可以更新现有液体配置，编号表变更需在主菜单执行。");
        foreach (string id in liquidIds)
            if (liquids.GetIndex(id) != LiquidTypes.GetIndex(id))
                throw new InvalidDataException($"液体 {id} 的运行时编号发生变化。");
    }

    /// <summary>刷新可原位应用的实例配置，再分别通知订阅者；不调用 Item.Load 或世界进入事件。</summary>
    private void PublishInPlaceReload(IReadOnlyDictionary<string, RuntimeItemDefinition> previousItems, ModRuntimeManager mods)
    {
        try { ItemMgr.GetInstance()?.ClearResourcePools(); }
        catch (Exception exception) { Debug.LogException(exception, this); }
        Item[] items = ItemMgr.GetInstance()?.WorldRunTimeItems.Values.Where(item => item != null).ToArray() ?? Array.Empty<Item>();
        foreach (Item item in items)
        {
            // 旧活跃实例可能在稍后才回池，禁止其旧外壳/模块基线再次用于新目录的生成。
            item.PoolMarker.PoolingDisabled = true;
            if (!previousItems.TryGetValue(item.itemData?.IDName ?? string.Empty, out RuntimeItemDefinition previous) ||
                !TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition current)) continue;
            try { ItemDefinitionRuntime.RefreshLiveConfiguration(item, previous, current); }
            catch (Exception exception) { Debug.LogWarning($"[GameRes] {item.name} 的部分实例配置未刷新：{exception.Message}", item); }
        }
        mods.PublishContentReady();
        if (ResourcesReloaded == null) return;
        foreach (Action observer in ResourcesReloaded.GetInvocationList())
        {
            try { observer(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }

    /// <summary>只在正式退出完成、活跃实体清空后回收旧代；F5 本身从不进入这个流程。</summary>
    private void ReleaseRetiredWorldResources()
    {
        if (retiredResourceReleases.Count == 0) return;
        ItemMgr.GetInstance()?.ClearResourcePools();
        SharedSpriteMeshCache.Clear();
        ReleaseRetiredResourceSessions();
    }

    private void ReleaseRetiredResourceSessions()
    {
        MachineCatalog.ReleaseRetiredEntries();
        RemoveRetiredWorldItemDefinitions();
        foreach (Action release in retiredResourceReleases)
        {
            try { release(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
        retiredResourceReleases.Clear();
    }

    #endregion
}
