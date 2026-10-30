using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// AI-Context: Item 实例、运行时注册和网络玩家副本的生命周期管理器；远程视觉物不得进入本地 Tick/存档索引。
// ===== AI 函数导航（ItemMgr 为 partial 类，按函数名及分部文件搜索） =====
// 本文件主循环：LoadAllRuntimeItems() / Update() / LateUpdate() / RefreshSimulationPlayers()
// 本文件调度维护：CleanupNullItems() / NotifyItemScheduleChanged()
// 生成与销毁 → ItemMgr.Spawning.cs：InstantiateItem() / InstantiateHeldItem() / InjectRuntimeItem() / DespawnItem()
// 玩家加载与保存 → ItemMgr.Players.cs：LoadPlayer() / CreatePlayer() / SavePlayer() / ReloadLocalPlayerAfterResourceReload()
// 网络玩家 → ItemMgr.Players.cs：LoadNetworkPlayer() / PromoteNetworkPlayerToLocal() / ReleaseNetworkPlayer()
// 运行时实体 → ItemMgr.RuntimeEntities.cs：TryGetRuntimeEntityAddress() / CopyRuntimeAiItems()
// 替换实例 → ItemMgr.Replacement.cs：ReplaceRuntimeItem()
// 其余分部：ItemMgr.Perception.cs（感知）/ ItemMgr.Matter.cs（物质）/ ItemMgr.RandomDrop.cs（掉落）
// =================================================================

public partial class ItemMgr : SingletonMono<ItemMgr>, IGameplayFixedStepSimulationSystem
{
    private static readonly ProfilerMarker MachineTickMarker = new("ItemMgr.Update.MachineWorld");
    private static readonly ProfilerMarker EntityTickMarker = new("ItemMgr.Update.WorldEntityRuntime");
    private static readonly ProfilerMarker PerceptionCompleteMarker = new("ItemMgr.Update.PerceptionComplete");
    private static readonly ProfilerMarker SchedulerTickMarker = new("ItemMgr.Update.TickScheduler");
    private static readonly ProfilerMarker WaterTickMarker = new("ItemMgr.Update.WorldItemWater");
    private static readonly ProfilerMarker MatterTickMarker = new("ItemMgr.Update.WorldItemMatter");
    private static readonly ProfilerMarker DroppedTickMarker = new("ItemMgr.Update.DroppedItems");
    private static readonly ProfilerMarker PerceptionScheduleMarker = new("ItemMgr.Update.PerceptionSchedule");
    private static readonly ProfilerMarker DroppedPresentMarker = new("ItemMgr.LateUpdate.DroppedPresent");
    private static readonly ProfilerMarker NaturalPresentMarker = new("ItemMgr.LateUpdate.NaturalPresent");

    /// <summary>
    /// AI-Context: 网络层通过这两个事件观察运行时 Item 生命周期；GamePlay 层不反向依赖 Mirror。
    /// 事件发生在注册完成后、销毁注销前，订阅方不得在回调内再次销毁同一 Item。
    /// </summary>
    public static event Action<Item> RuntimeItemInstantiated;
    public static event Action<Item> RuntimeItemDespawning;

    /// <summary>完整注册链的表现扩展点；包含生成、注入、加载与重绑，不改变网络生成事件语义。</summary>
    internal static event Action<Item> RuntimeItemRegistered;
    internal static event Action<Item> RuntimeItemUnregistered;
    internal static event Action<Item> RuntimeItemMoved;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetAdapterEvents()
    {
        RuntimeItemRegistered = null;
        RuntimeItemUnregistered = null;
        RuntimeItemMoved = null;
    }

    [Header("玩家创建配置")]
    [SerializeField] private string defaultPlayerCreationTemplateId;

    public string DefaultPlayerCreationTemplateId => defaultPlayerCreationTemplateId;

    #region Runtime Data

    [ShowInInspector]
    public IReadOnlyDictionary<int, Item> WorldRunTimeItems => _runtimeRegistry.ItemsByGuid;

    [ShowInInspector]
    public IReadOnlyDictionary<string, IReadOnlyList<Item>> RuntimeItemsGroup => _runtimeRegistry.Groups;

    private Transform _externalPlayerTransform;
    // 记录实际订阅者，销毁时不再查询可能已经卸载的 GameManager。
    private GameManager _lifecycleGameManager;

    #endregion

    #region 分级更新调度

    private readonly WorldItemTickScheduler _tickScheduler = new();
    private Action<Item> _refreshRuntimeItemIndexesCallback;
    private readonly List<Transform> _simulationPlayers = new(4);
    private bool _itemTickSuspended;

    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int EveryFrameItemCount => _tickScheduler.EveryFrameCount;
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int FastTickItemCount => _tickScheduler.FastCount;
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int NormalTickItemCount => _tickScheduler.NormalCount;
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int SlowTickItemCount => _tickScheduler.SlowCount;
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int DormantItemCount => Mathf.Max(
        0,
        RuntimeItems.Count - EveryFrameItemCount - FastTickItemCount - NormalTickItemCount - SlowTickItemCount);

    #endregion

    #region Properties

    public string PlayerInSceneName
    {
        get
        {
            string playerName = SaveDataMgr.Instance.CurrentContrrolPlayerName;
            if (Player_DIC.TryGetValue(playerName, out Player runtimePlayer) && runtimePlayer?.Data != null)
            {
                return runtimePlayer.Data.CurrentSceneName;
            }

            // 联机玩家尚未完成 Item 实例创建时，从同步后的玩家存档提供当前场景作为保底。
            if (SaveDataMgr.Instance.SaveData?.PlayerData_Dict != null &&
                SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(playerName, out Data_Player playerData) &&
                !string.IsNullOrWhiteSpace(playerData.CurrentSceneName))
            {
                return playerData.CurrentSceneName;
            }

            return GameplayWorldRegistry.ExecutionWorldKey;
        }
    }

    public Player User_Player
    {
        get
        {
            if (Player_DIC.TryGetValue(SaveDataMgr.Instance.CurrentContrrolPlayerName, out var player))
            {
                return player;
            }

            return null;
        }
    }

    /// <summary>
    /// 当前本地玩家的 Transform。单机与联机模式均优先返回核心 Player Item。
    /// </summary>
    public Transform UserPlayerTransform
    {
        get
        {
            string playerName = SaveDataMgr.Instance.CurrentContrrolPlayerName;
            if (Player_DIC.TryGetValue(playerName, out Player runtimePlayer) && runtimePlayer != null)
                return runtimePlayer.transform;

            return _externalPlayerTransform;
        }
    }

    public void RegisterExternalPlayerTransform(Transform playerTransform)
    {
        _externalPlayerTransform = playerTransform;
    }

    public void UnregisterExternalPlayerTransform(Transform playerTransform)
    {
        if (_externalPlayerTransform == playerTransform)
            _externalPlayerTransform = null;
    }

    #endregion

    #region Unity Lifecycle

    protected override void Awake()
    {
        base.Awake();
        WorldRenderingManager.EnsureForOwner(this);
        Item.RuntimeStructureChanged += OnPerceptionStructureChanged;
        Item.RuntimeStructureChanged += TrackWorldItemMatter;
        RuntimeItemRegistered += TrackWorldItemMatter;
        RuntimeItemUnregistered += ReleaseWorldItemMatter;
        // 不在 Awake 中自动加载场景物品，避免破坏游戏生命周期。手动或在合适的时机调用 LoadAllRuntimeItems()
    }

    private void OnEnable()
    {
        GameplayFixedStepSimulation.Register(this, GameplayFixedStepSimulationPhase.Gameplay);
        RenderPipelineManager.beginContextRendering -= PresentDroppedItemsAfterWorldProjection;
        RenderPipelineManager.beginContextRendering += PresentDroppedItemsAfterWorldProjection;
    }

    [Button("加载所有Runtime物品")]
    public void LoadAllRuntimeItems()
    {
        // 第一步：获取场景中所有的 Item（包括非激活状态）
        Item[] allItems = FindObjectsOfType<Item>(includeInactive: true);

        foreach (Item item in allItems)
        {
            if (item == null)
            {
                continue;
            }

            if (item.PoolMarker.InPool || item.IsPreparedCandidate)
            {
                continue;
            }

            RegisterRuntimeItem(item, item.name);
        }
    }

    public void Start()
    {
        _lifecycleGameManager = GameManager.Instance;
        _lifecycleGameManager.BackToHelloScene_Event_Start += CleanupNullItems;
    }

    protected override void OnDestroy()
    {
        RenderPipelineManager.beginContextRendering -= PresentDroppedItemsAfterWorldProjection;
        ClearPreparedItems();
        Item.RuntimeStructureChanged -= TrackWorldItemMatter;
        RuntimeItemRegistered -= TrackWorldItemMatter;
        RuntimeItemUnregistered -= ReleaseWorldItemMatter;
        ClearWorldItemMatter();
        MachineWorld.ReleaseWorld(capture: false);
        WorldEntityRuntime.ReleaseAll();
        FlatWorld.NaturalEntities.NaturalEntityEcsService.ReleaseAll();
        DroppedItemService.ReleaseAll(capture: false);
        Item.RuntimeStructureChanged -= OnPerceptionStructureChanged;
        CompletePerceptionBatch(false);
        DisposePerceptionJobData();

        if (_lifecycleGameManager != null)
        {
            _lifecycleGameManager.BackToHelloScene_Event_Start -= CleanupNullItems;
        }
        _lifecycleGameManager = null;

        base.OnDestroy();
    }

    private void OnDisable()
    {
        GameplayFixedStepSimulation.Unregister(this);
        RenderPipelineManager.beginContextRendering -= PresentDroppedItemsAfterWorldProjection;
        ClearWorldItemMatter();
        CompletePerceptionBatch(false);
    }

    private void Update()
    {
        if (!IsWorldItemRuntimeActive())
        {
            ClearWorldItemMatter();
            if (!_itemTickSuspended)
            {
                _tickScheduler.Pause(RuntimeItems);
                _itemTickSuspended = true;
            }

            // 退出世界后只结算并释放已提交的感知 Job，禁止旧场景实体继续 Tick。
            CompletePerceptionBatch(applyResults: false);
            return;
        }

        _itemTickSuspended = false;
        _tickScheduler.UpdateRenderFrame(Time.deltaTime);
    }

    public void SimulateFixedStep(float deltaTime)
    {
        if (!IsWorldItemRuntimeActive()) return;

        using (MachineTickMarker.Auto())
            MachineWorld.Tick(deltaTime);
        using (EntityTickMarker.Auto())
            WorldEntityRuntime.Tick(deltaTime);

        using (PerceptionCompleteMarker.Auto())
            CompletePerceptionBatch();

        if (RuntimeItems.Count == 0)
        {
            DroppedItemService.Tick(deltaTime);
            return;
        }

        RefreshSimulationPlayers();
        // 索引刷新委托只创建一次，避免每帧方法组转换分配。
        _refreshRuntimeItemIndexesCallback ??= RefreshRuntimeItemIndexes;
        using (SchedulerTickMarker.Auto())
            _tickScheduler.Update(deltaTime, GameplayFixedStepSimulation.CurrentTime, _refreshRuntimeItemIndexesCallback,
                _simulationPlayers, WorldTopologyRuntime.GetActiveDomain());
        using (WaterTickMarker.Auto())
            WorldItemWaterSystem.ProcessPendingSpawnChecks();
        using (MatterTickMarker.Auto())
            TickWorldItemMatter(deltaTime);
        using (DroppedTickMarker.Auto())
            DroppedItemService.Tick(deltaTime);
        // AI Tick 已提交本帧全部检测请求后立即调度，让 Job 在剩余帧时间里并行运行到下一帧结算。
        using (PerceptionScheduleMarker.Auto())
            SchedulePerceptionBatch();
    }

    private void LateUpdate()
    {
        if (!IsWorldItemRuntimeActive())
            return;

        PresentWorldItemMatter();
        using (NaturalPresentMarker.Auto())
            FlatWorld.NaturalEntities.NaturalEntityEcsService.Present();
    }

    // 等全部世界投影同步完再提交掉落物，避免读取上一帧船姿。
    private void PresentDroppedItemsAfterWorldProjection(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (!IsWorldItemRuntimeActive()) return;
        using (DroppedPresentMarker.Auto())
            DroppedItemService.Present();
    }

    /// <summary>仅在真实游戏世界中调度 Item，菜单与退出回收阶段不允许旧实体继续运行。</summary>
    private static bool IsWorldItemRuntimeActive()
    {
        GameManager gameManager = GameManager.Instance;
        return gameManager != null &&
               gameManager.IsInGameWorld &&
               gameManager.IsGameplayReady;
    }

    /// <summary>复用本帧活动玩家引用；按最近玩家位置选择实体距离档。</summary>
    private void RefreshSimulationPlayers()
    {
        _simulationPlayers.Clear();
        foreach (Player player in Player_DIC.Values)
        {
            if (player != null && player.gameObject.activeInHierarchy &&
                !_simulationPlayers.Contains(player.transform))
                _simulationPlayers.Add(player.transform);
        }

        if (_simulationPlayers.Count == 0 && UserPlayerTransform != null)
            _simulationPlayers.Add(UserPlayerTransform);
    }

    #endregion

    #region Public Maintenance API

    [Button("清理空引用")]
    public void CleanupNullItems()
    {
        _runtimeRegistry.CleanupNullItems();
        CleanupRuntimeAiIndex();
        RebuildSpatialIndex();
        _tickScheduler.Rebuild(RuntimeItems);
    }

    public void NotifyItemScheduleChanged(Item item)
    {
        // 独立夹具和未提交的冷物品不属于世界调度，注册时会统一建立调度状态。
        if (item != null && _runtimeRegistry.Contains(item))
            _tickScheduler.NotifyChanged(item);
    }

    #endregion
}
