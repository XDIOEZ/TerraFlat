using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using Sirenix.OdinInspector;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>
/// 生物生态调度器。调度间隔、每轮检查量和两种后端的装载预算由全局 JSON 决定；
/// 每次调度至多创建一只生物。时间计划只产生待出生需求，
/// 已出生个体由存档和休眠仓库保留身份，远距卸载表现，靠近时恢复同一 GUID 与模块状态。
/// </summary>
[RequireComponent(typeof(MonsterManager))]
public partial class MonsterSpawnerManager : SingletonAutoMono<MonsterSpawnerManager>
{
    #region 配置

    private List<SpawnerConfig> _spawnerConfigs = new();
    // 世界内只读取启动时校验过的全局设置，不在游玩帧解析 JSON。
    private SpawnerRuntimeSettings _settings;

    #endregion

    #region 运行时状态

    private GameManager _gameManager;
    private ItemMgr _itemManager;
    private ChunkMgr _chunkManager;
    private MonsterManager _monsterManager;
    private WorldNavigationManager _navigationManager;
    private LightLayerMgr _lightLayerManager;
    private DayTimeSystem _dayTimeSystem;
    private Dictionary<string, SpawnerProgressSaveData> _runtimeStates = new();
    private readonly Dictionary<Item, float> _farAwaySince = new();
    private readonly HashSet<Item> _chunkDormantItems = new();
    private readonly Dictionary<string, float> _nextSpawnRetryTime = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _nextRecoveryCheckTime = new(StringComparer.Ordinal);
    private readonly Dictionary<SpawnerConfig, int> _remainingSearchAttempts = new();
    private readonly Dictionary<int, float> _nextResidentWakeAttempt = new();
    private readonly List<SpawnerConfig> _jsonRuntimeConfigs = new();
    private readonly List<Vector3> _playerPositions = new(4);
    private readonly List<Item> _itemSnapshot = new(64);
    private readonly List<MonsterManager.Registration> _monsterSnapshot = new(64);
    private float _nextEcologyTickTime;
    private float _nextCreatureBirthTime;
    private readonly RuntimeTreeHabitatIndex _treeHabitatIndex = new();
    private readonly Dictionary<SpawnerConfig, HabitatCandidates> _treeCapacityCache = new();
    private readonly Dictionary<SpawnerConfig, string> _configKeys = new();
    private readonly Dictionary<SpawnerConfig.SpawnEntry, SpawnerConfig> _entryOwners = new();
    private readonly HashSet<SpawnerConfig> _entityConfigs = new();
    private readonly List<int> _nearbyGroupCounts = new(4);
    private readonly List<SpawnerConfig.SpawnEntry> _availableEntries = new(8);
    private bool _hasLimitedEntities;
    private readonly List<Item> _presentationItems = new(32);
    private int _monsterSnapshotVersion = -1;
    private int _spawnConfigCursor;
    private int _activeResidentCursor;
    private int _parkedAddressCursor;

    #endregion

    #region Unity 生命周期

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this)
            return;

        _monsterManager = GetComponent<MonsterManager>();
    }

    private void Start()
    {
        enabled = false;

        _gameManager = GameManager.Instance;
        if (_gameManager != null)
        {
            _gameManager.Event_GameWorldEnter += OnGameWorldEnter;
            _gameManager.Event_GameWorldExit += OnGameWorldExit;
        }

        if (_monsterManager != null)
        {
            _monsterManager.MonsterRegistered += OnMonsterRegistered;
            _monsterManager.MonsterUnregistered += OnMonsterUnregistered;
            _monsterManager.MonsterDeathStarted += OnMonsterDeathStarted;
        }
    }

    private void OnGameWorldEnter()
    {
        enabled = false;
        UnsubscribePresentationEvents();
        if (DimensionManager.Instance.ActiveDefinition?.EnableMonsterSpawning == false)
        {
            ClearTrackedPopulation();
            AiRuntimeBackendService.ClearRoutes();
            enabled = false;
            return;
        }

        if (!PrepareSpawnerConfigs())
        {
            enabled = false;
            return;
        }

        try
        {
            AiRuntimeBackendService.ConfigureRoutes(_spawnerConfigs);
            CacheConfigurationQueries();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[MonsterSpawnerManager] AI 后端路由配置无效：{exception.Message}", this);
            AiRuntimeBackendService.ClearRoutes();
            ReleaseJsonRuntimeConfigs();
            enabled = false;
            return;
        }

        _itemManager = ItemMgr.Instance;
        _chunkManager = ChunkMgr.Instance;
        _navigationManager = WorldNavigationManager.Instance;
        _lightLayerManager = LightLayerMgr.Instance;
        _dayTimeSystem = DayTimeSystem.Instance;
        if (_monsterManager == null ||
            _itemManager == null ||
            _chunkManager == null ||
            _navigationManager == null ||
            _lightLayerManager == null ||
            _dayTimeSystem == null)
        {
            Debug.LogError("[MonsterSpawnerManager] 怪物生态依赖的运行时管理器未就绪。", this);
            ClearTrackedPopulation();
            AiRuntimeBackendService.ClearRoutes();
            enabled = false;
            return;
        }

        BindSaveData(SaveDataMgr.Instance?.SaveData);
        _itemManager.CleanupNullItems();
        _monsterManager.Configure(_spawnerConfigs, _itemManager.WorldRunTimeItems.Values);
        _treeHabitatIndex.Bind(_itemManager.WorldRunTimeItems.Values);
        _treeCapacityCache.Clear();
        if (AiRuntimeBackendService.HasEntitiesRoutes)
        {
            if (!AiRuntimeBackendService.UseEntities)
            {
                Debug.LogWarning(
                    "[MonsterSpawnerManager] 当前关闭 AIECS；显式 Entities 物种暂停生成，不会回退 GameObject。",
                    this);
            }
            else
            {
                IAiEcologyBackend backend = AiRuntimeBackendService.Ecology;
                if (backend == null || !backend.PrepareWorld(_spawnerConfigs))
                {
                    Debug.LogError(
                        "[MonsterSpawnerManager] AIECS 物种后端未就绪；这些物种不会回退 GameObject，其他 GameObject 生物继续运行。",
                        this);
                }
            }
        }
        _nextEcologyTickTime = Time.unscaledTime;
        _nextCreatureBirthTime = Time.unscaledTime;
        _nextEventSearchTime = Time.unscaledTime;
        _chunkManager.RuntimeEntityPresentationChanged += OnChunkPresentationChanged;
        _itemManager.RuntimeAiAddressChanged += OnRuntimeAiAddressChanged;
        enabled = true;
    }

    private void OnGameWorldExit()
    {
        enabled = false;
        UnsubscribePresentationEvents();
        _treeHabitatIndex.Dispose();
        _treeCapacityCache.Clear();
        CaptureSaveData(SaveDataMgr.Instance?.SaveData);
        // 世界退出时对象会随场景/区块一起销毁，不再唤醒本管理器主动休眠的实体。
        ClearTrackedPopulation(restoreChunkDormantItems: false);
        AiRuntimeBackendService.ClearRoutes();
        ReleaseJsonRuntimeConfigs();
        _nextSpawnRetryTime.Clear();
        _nextRecoveryCheckTime.Clear();
        _remainingSearchAttempts.Clear();
        _nextResidentWakeAttempt.Clear();
        enabled = false;
        _itemManager = null;
        _chunkManager = null;
        _navigationManager = null;
        _lightLayerManager = null;
        _dayTimeSystem = null;
    }

    protected override void OnDestroy()
    {
        enabled = false;
        UnsubscribePresentationEvents();
        _treeHabitatIndex.Dispose();
        bool ownsSingleton = ReferenceEquals(instance, this);
        if (_gameManager != null)
        {
            _gameManager.Event_GameWorldEnter -= OnGameWorldEnter;
            _gameManager.Event_GameWorldExit -= OnGameWorldExit;
        }

        if (_monsterManager != null)
        {
            _monsterManager.MonsterRegistered -= OnMonsterRegistered;
            _monsterManager.MonsterUnregistered -= OnMonsterUnregistered;
            _monsterManager.MonsterDeathStarted -= OnMonsterDeathStarted;
        }

        // 场景切换时可能短暂创建重复 WorldManager；副本销毁不得清空正式单例的混合后端路由。
        if (ownsSingleton)
        {
            // OnDestroy 可能发生在 Unity 正在卸载场景时，禁止对即将销毁的对象调用 SetActive。
            ClearTrackedPopulation(restoreChunkDormantItems: false);
            AiRuntimeBackendService.ClearRoutes();
            ReleaseJsonRuntimeConfigs();
        }

        base.OnDestroy();
    }

    private bool PrepareSpawnerConfigs()
    {
        _monsterManager?.ResetWorld();
        ReleaseJsonRuntimeConfigs();

        if (!SpawnerConfigCatalogService.IsLoaded)
        {
            Debug.LogError("[MonsterSpawnerManager] 生物生成 JSON 目录尚未加载。", this);
            return false;
        }

        _jsonRuntimeConfigs.AddRange(SpawnerConfigCatalogService.CreateRuntimeConfigs());
        _spawnerConfigs = _jsonRuntimeConfigs;
        _settings = SpawnerConfigCatalogService.Catalog.Settings;
        AiRuntimeBackendService.UseEntities = _settings.UseAiecsBackend;

        if (_spawnerConfigs.Count == 0)
        {
            Debug.LogError("[MonsterSpawnerManager] 生物生成 JSON 目录没有有效配置。", this);
            return false;
        }

        return true;
    }

    private void ReleaseJsonRuntimeConfigs()
    {
        _configKeys.Clear();
        _entryOwners.Clear();
        _entityConfigs.Clear();
        _hasLimitedEntities = false;
        for (int index = 0; index < _jsonRuntimeConfigs.Count; index++)
        {
            SpawnerConfig config = _jsonRuntimeConfigs[index];
            if (config != null)
                Destroy(config);
        }

        _jsonRuntimeConfigs.Clear();
    }

    private void TickSpawner()
    {
        if (_gameManager == null || !_gameManager.IsGameplayReady)
            return;

        float now = Time.unscaledTime;
        if (now < _nextEcologyTickTime)
            return;
        _nextEcologyTickTime = now + Mathf.Max(0.1f, _settings.EcologyTickInterval);

        bool presentationChanged = ProcessPresentationChanges(Mathf.Max(1, _settings.ResidentChecksPerTick));
        if (!GameNetwork.HasStateAuthority)
        {
            RefreshClientChunkDormancy();
            return;
        }

        if (!TryGetCurrentTimeData(out string sceneName, out TimeData timeData))
            return;

        RefreshPlayerPositions(sceneName);
        if (_playerPositions.Count == 0)
            return;

        int currentDay = timeData.GetCurrentDay();
        for (int i = 0; i < _spawnerConfigs.Count; i++)
        {
            SpawnerConfig config = _spawnerConfigs[i];
            if (config == null)
                continue;

            SpawnerProgressSaveData state = GetOrCreateState(config);
            RecoverEcologyBudget(config, state, currentDay);
            ClampPendingSpawns(config, state);

            if (config.ScheduleMode == SpawnerScheduleMode.DayMilestoneGrowth)
                ProcessMilestoneGrowth(config, state, sceneName, currentDay);
            else
                ProcessTimedWindows(config, state, sceneName, timeData, currentDay);

            QueuePopulationRecovery(config, state);
        }

        if (!presentationChanged && SaveDataMgr.Instance?.IsEcologySnapshotInProgress != true &&
            !MaintainResidentStreaming())
            ProcessDueSpawnWork(sceneName, currentDay);
    }

    #endregion

    #region 存档

    public void CaptureSaveData(GameSaveData saveData)
    {
        if (saveData == null)
            return;

        saveData.MonsterSpawnerData ??= new MonsterSpawnerSaveData();
        saveData.MonsterSpawnerData.ConfigStates = _runtimeStates ?? new Dictionary<string, SpawnerProgressSaveData>();
        // ECS 停用时保留原有冷快照，不让未启动的后端覆盖历史居民。
        if (AiRuntimeBackendService.UseEntities && AiRuntimeBackendService.HasEntitiesRoutes)
            AiRuntimeBackendService.Ecology?.CaptureResidents(saveData.MonsterSpawnerData);
    }

    private void BindSaveData(GameSaveData saveData)
    {
        if (saveData == null)
        {
            _runtimeStates = new Dictionary<string, SpawnerProgressSaveData>();
            return;
        }

        saveData.MonsterSpawnerData ??= new MonsterSpawnerSaveData();
        saveData.MonsterSpawnerData.ConfigStates ??= new Dictionary<string, SpawnerProgressSaveData>();
        _runtimeStates = saveData.MonsterSpawnerData.ConfigStates;
    }

    #endregion

    #region 时间调度

    private bool TryGetCurrentTimeData(out string sceneName, out TimeData timeData)
    {
        sceneName = null;
        timeData = null;

        if (_itemManager == null || _dayTimeSystem == null)
        {
            return false;
        }

        sceneName = _itemManager.PlayerInSceneName;
        return !string.IsNullOrEmpty(sceneName) &&
               _dayTimeSystem.TryGetResolvedTimeData(sceneName, out _, out timeData);
    }

    private void ProcessTimedWindows(
        SpawnerConfig config,
        SpawnerProgressSaveData state,
        string sceneName,
        TimeData timeData,
        int currentDay)
    {
        float currentTotalTime = timeData.GetTotalGameTime();
        if (state.LastProcessedTotalTime < 0f)
        {
            state.LastProcessedTotalTime = currentTotalTime;
            return;
        }

        if (currentTotalTime < state.LastProcessedTotalTime)
            state.LastProcessedTotalTime = currentTotalTime;

        float previousTotalTime = state.LastProcessedTotalTime;
        state.LastProcessedTotalTime = currentTotalTime;
        if (currentTotalTime <= previousTotalTime)
            return;

        float frequencyMultiplier = GameDifficultyService.Current.World.SpawnFrequencyMultiplier;
        if (frequencyMultiplier <= 0f)
            return;

        int spawnsPerDay = GameDifficultyService.ScaleCount(
            config.SpawnsPerDay,
            frequencyMultiplier,
            1);
        float dayLength = Mathf.Max(1f, timeData.DayLength);
        float interval = dayLength / spawnsPerDay;
        float firstTriggerTime = Mathf.Repeat(config.SpawnTriggerTime, dayLength);
        // 跨天快进只处理当前日窗口，不能把离线期间的出生事件一次性补进当前视野。
        int endDay = Mathf.Max(0, Mathf.FloorToInt(currentTotalTime / dayLength));
        int startDay = endDay;

        for (int day = startDay; day <= endDay; day++)
        {
            if (config.DaysBetweenSpawns > 1 &&
                day - state.LastSpawnDay < config.DaysBetweenSpawns)
            {
                continue;
            }

            for (int windowIndex = 0; windowIndex < spawnsPerDay; windowIndex++)
            {
                float triggerTimeInDay = Mathf.Repeat(firstTriggerTime + interval * windowIndex, dayLength);
                float triggerTotalTime = day * dayLength + triggerTimeInDay;
                if (triggerTotalTime <= previousTotalTime || triggerTotalTime > currentTotalTime)
                    continue;

                if (config.RequireGlobalDarkness &&
                    !IsScheduledTimeDark(sceneName, timeData, triggerTimeInDay))
                {
                    continue;
                }

                if (config.SpawnChance <= 0f || UnityEngine.Random.value >= config.SpawnChance)
                    continue;

                int queueRoom = Mathf.Max(
                    0,
                    GetEffectiveGroupLimit(config) -
                    CountGroupAlive(config) -
                    state.PendingSpawnCount -
                    state.PendingReplacementCount);
                int scheduledCount = GameDifficultyService.ScaleCount(
                    config.SpawnCount,
                    frequencyMultiplier,
                    1);
                state.PendingSpawnCount += Mathf.Min(queueRoom, scheduledCount);
                state.LastSpawnDay = day;
            }
        }
    }

    private void ProcessMilestoneGrowth(
        SpawnerConfig config,
        SpawnerProgressSaveData state,
        string sceneName,
        int currentDay)
    {
        int dayNumber = Mathf.Max(1, currentDay + 1);
        int growthInterval = Mathf.Max(1, config.GrowthIntervalDays);
        if (config.UnboundedDailyGrowth)
        {
            QueueUnboundedDailyGrowth(config, state, dayNumber);
            return;
        }

        int lifetimeLimit = GameDifficultyService.ScaleCount(
            config.MaxLifetimeSpawnCount,
            GameDifficultyService.Current.World.SpawnPopulationMultiplier,
            1);
        int limit = Mathf.Min(
            Mathf.Clamp(lifetimeLimit, 1, 64),
            GetEffectiveGroupLimit(config));
        int targetScheduledCount = Mathf.Min(
            limit,
            GameDifficultyService.ScaleCount(
                dayNumber / growthInterval,
                GameDifficultyService.Current.World.SpawnFrequencyMultiplier));
        int alreadyScheduled = CountGroupAlive(config) +
                               Mathf.Max(0, state.PendingSpawnCount) +
                               Mathf.Max(0, state.PendingReplacementCount);

        if (targetScheduledCount > alreadyScheduled)
            state.PendingSpawnCount += targetScheduledCount - alreadyScheduled;

        state.ProcessedGrowthMilestones =
            Mathf.Max(state.ProcessedGrowthMilestones, targetScheduledCount);
        state.PendingSpawnCount = Mathf.Clamp(
            state.PendingSpawnCount,
            0,
            Mathf.Max(0, targetScheduledCount - CountGroupAlive(config)));
    }

    /// <summary>为无上限逐日模式排入当晚对应数量。</summary>
    private void QueueUnboundedDailyGrowth(
        SpawnerConfig config,
        SpawnerProgressSaveData state,
        int dayNumber)
    {
        int lastProcessedDay = Mathf.Max(0, state.ProcessedGrowthMilestones);
        if (dayNumber <= lastProcessedDay)
            return;

        long firstMissingDay = lastProcessedDay + 1L;
        long missingDayCount = dayNumber - lastProcessedDay;
        long scheduledCount = (firstMissingDay + dayNumber) * missingDayCount / 2L;
        state.PendingSpawnCount = (int)Math.Min(
            int.MaxValue,
            (long)Mathf.Max(0, state.PendingSpawnCount) + scheduledCount);
        state.ProcessedGrowthMilestones = dayNumber;
    }

    private bool IsScheduledTimeDark(string sceneName, TimeData timeData, float timeInDay)
    {
        if (timeData?.LightParams == null)
            return false;

        float dayLength = Mathf.Max(1f, timeData.DayLength);
        if (!_dayTimeSystem.SceneLightingRateDict.TryGetValue(sceneName, out float lightingRate))
            lightingRate = 1f;
        float lighting = timeData.LightParams.Evaluate(Mathf.Repeat(timeInDay, dayLength) / dayLength) * lightingRate;
        return lighting <= LightLayerMgr.CompletelyDarkValue + 0.0001f;
    }

    #endregion

    #region 生态预算与补位

    private static void RecoverEcologyBudget(SpawnerConfig config, SpawnerProgressSaveData state, int currentDay)
    {
        if (config.UnboundedDailyGrowth)
        {
            state.AvailableBudget = int.MaxValue;
            state.LastBudgetRecoveryDay = currentDay;
            return;
        }

        float populationMultiplier = GameDifficultyService.Current.World.SpawnPopulationMultiplier;
        int maxBudget = GameDifficultyService.ScaleCount(
            config.MaxEcologyBudget,
            populationMultiplier,
            1);
        if (state.AvailableBudget < 0)
            state.AvailableBudget = maxBudget;
        else
            state.AvailableBudget = Mathf.Min(state.AvailableBudget, maxBudget);

        if (state.LastBudgetRecoveryDay < 0 || currentDay < state.LastBudgetRecoveryDay)
        {
            state.LastBudgetRecoveryDay = currentDay;
            return;
        }

        int daysPassed = currentDay - state.LastBudgetRecoveryDay;
        if (daysPassed <= 0)
            return;

        int recoveryPerDay = GameDifficultyService.ScaleCount(
            config.DailyBudgetRecovery,
            populationMultiplier);
        long restored = (long)daysPassed * recoveryPerDay;
        state.AvailableBudget = Mathf.Clamp(
            state.AvailableBudget + (int)Mathf.Min(int.MaxValue, restored),
            0,
            maxBudget);
        state.LastBudgetRecoveryDay = currentDay;
    }

    private void QueuePopulationRecovery(SpawnerConfig config, SpawnerProgressSaveData state)
    {
        if (config.RecoveryTargetPopulation <= 0)
            return;
        string key = GetConfigKey(config);
        float now = Time.unscaledTime;
        if (_nextRecoveryCheckTime.TryGetValue(key, out float nextCheck) && now < nextCheck)
            return;

        int targetPopulation = Mathf.Min(
            GameDifficultyService.ScaleCount(
                config.RecoveryTargetPopulation,
                GameDifficultyService.Current.World.SpawnPopulationMultiplier),
            GetEffectiveGroupLimit(config));
        if (targetPopulation <= 0)
            return;

        _nextRecoveryCheckTime[key] = now + Mathf.Max(0.5f, config.RecoveryCheckInterval);
        int aliveCount = CountGroupAlive(config);
        int pendingCount = Mathf.Max(0, state.PendingSpawnCount) + Mathf.Max(0, state.PendingReplacementCount);
        int missingCount = targetPopulation - aliveCount - pendingCount;
        if (missingCount > 0)
            state.PendingReplacementCount += missingCount;
    }

    private void ClampPendingSpawns(SpawnerConfig config, SpawnerProgressSaveData state)
    {
        if (state.PendingSpawnCount <= 0 && state.PendingReplacementCount <= 0)
            return;
        if (config.IgnorePopulationLimits || config.UnboundedDailyGrowth)
        {
            state.PendingSpawnCount = Mathf.Max(0, state.PendingSpawnCount);
            state.PendingReplacementCount = Mathf.Max(0, state.PendingReplacementCount);
            return;
        }

        int availableRoom = Mathf.Max(0, GetEffectiveGroupLimit(config) - CountGroupAlive(config));
        state.PendingSpawnCount = Mathf.Clamp(state.PendingSpawnCount, 0, availableRoom);
        availableRoom -= state.PendingSpawnCount;
        state.PendingReplacementCount = Mathf.Clamp(state.PendingReplacementCount, 0, availableRoom);
    }

    private void QueueDeathReplacement(SpawnerConfig config)
    {
        if (!enabled || config == null || config.RecoveryTargetPopulation <= 0)
            return;

        SpawnerProgressSaveData state = GetOrCreateState(config);
        int target = Mathf.Min(
            GameDifficultyService.ScaleCount(
                config.RecoveryTargetPopulation,
                GameDifficultyService.Current.World.SpawnPopulationMultiplier),
            GetEffectiveGroupLimit(config));
        int aliveAfterDeath = Mathf.Max(0, CountGroupAlive(config) - 1);
        int pending = Mathf.Max(0, state.PendingSpawnCount) + Mathf.Max(0, state.PendingReplacementCount);
        if (aliveAfterDeath + pending < target)
            state.PendingReplacementCount++;
    }

    #endregion

    #region 生成执行

    /// <summary>每轮只处理一个配置的一次候选搜索或创建；配置轮转防止长期饥饿。</summary>
    private void ProcessDueSpawnWork(string sceneName, int currentDay)
    {
        int count = _spawnerConfigs.Count;
        if (count == 0) return;
        int cursor = _spawnConfigCursor % count;
        for (int visited = 0; visited < count; visited++)
        {
            SpawnerConfig config = _spawnerConfigs[cursor];
            cursor = (cursor + 1) % count;
            if (config == null) continue;
            if (ProcessPendingSpawns(config, GetOrCreateState(config), sceneName, currentDay))
                break;
        }
        _spawnConfigCursor = cursor;
    }

    private bool ProcessPendingSpawns(
        SpawnerConfig config,
        SpawnerProgressSaveData state,
        string sceneName,
        int currentDay)
    {
        if (state.PendingSpawnCount <= 0 && state.PendingReplacementCount <= 0)
            return false;

        string key = GetConfigKey(config);
        float now = Time.unscaledTime;
        if (_nextSpawnRetryTime.TryGetValue(key, out float nextRetry) && now < nextRetry)
            return false;
        if (now < _nextCreatureBirthTime)
            return false;

        if (!HasAvailableBackendEntry(config))
            return false;

        if (config.RequireGlobalDarkness && !IsGlobalDark(sceneName))
            return false;

        if (_monsterManager.Count >= Mathf.Max(1, _settings.MaxLoadedGameObjectActors) &&
            !UsesEntityPopulation(config))
            return false;

        if (!TrySpawnOne(config, state))
        {
            int remaining = _remainingSearchAttempts.TryGetValue(config, out int value)
                ? value - 1 : Mathf.Max(1, config.SpawnSearchRetryCount) - 1;
            if (remaining <= 0)
            {
                _remainingSearchAttempts.Remove(config);
                _nextSpawnRetryTime[key] = now + Mathf.Max(0.05f, _settings.SpawnRetryInterval);
            }
            else
            {
                _remainingSearchAttempts[config] = remaining;
            }
            return true;
        }

        _remainingSearchAttempts.Remove(config);
        _nextSpawnRetryTime[key] = now + Mathf.Max(_settings.EcologyTickInterval, config.AsyncSpawnInterval);

        if (state.PendingSpawnCount > 0)
        {
            state.PendingSpawnCount--;
            state.LifetimeSpawnCount++;
        }
        else
        {
            state.PendingReplacementCount = Mathf.Max(0, state.PendingReplacementCount - 1);
        }

        state.LastSpawnDay = Mathf.Max(state.LastSpawnDay, currentDay);
        return true;
    }

    private bool TrySpawnOne(SpawnerConfig config, SpawnerProgressSaveData state)
    {
        int globalLimit = GameDifficultyService.ScaleCount(
            _settings.GlobalAliveLimit,
            GameDifficultyService.Current.World.SpawnPopulationMultiplier,
            1);
        if (!config.UnboundedDailyGrowth &&
            !config.IgnorePopulationLimits &&
            (CountPopulationLimitedAlive() >= globalLimit ||
             CountGroupAlive(config) >= GetEffectiveGroupLimit(config)))
        {
            return false;
        }

        SpawnerConfig.SpawnEntry entry = DetermineAvailableEntry(config, state);
        if (entry == null)
            return false;

        if (!TryGetValidSpawnPosition(config, out Vector3 spawnPosition))
            return false;

        if (!IsWithinLocalSpeciesLimit(config, entry, spawnPosition))
            return false;

        // 即使资源初始化失败，昂贵的创建尝试也不能在同一短时间窗内反复执行。
        _nextCreatureBirthTime = Time.unscaledTime + Mathf.Max(0.1f, _settings.EcologyTickInterval);
        if (!TrySpawnMonster(entry, spawnPosition))
            return false;

        if (!config.UnboundedDailyGrowth)
            state.AvailableBudget = Mathf.Max(0, state.AvailableBudget - Mathf.Max(1, entry.EcologyCost));
        return true;
    }

    private SpawnerConfig.SpawnEntry DetermineAvailableEntry(
        SpawnerConfig config,
        SpawnerProgressSaveData state)
    {
        if (config.SpawnEntries == null || config.SpawnEntries.Count == 0)
            return null;

        _availableEntries.Clear();
        float totalWeight = 0f;
        for (int i = 0; i < config.SpawnEntries.Count; i++)
        {
            SpawnerConfig.SpawnEntry entry = config.SpawnEntries[i];
            if (CanSpawnEntry(config, entry, state))
            {
                _availableEntries.Add(entry);
                totalWeight += entry.Probability;
            }
        }

        if (totalWeight <= 0f)
            return null;

        float targetWeight = UnityEngine.Random.value * totalWeight;
        float cumulative = 0f;
        SpawnerConfig.SpawnEntry lastAvailable = null;
        for (int i = 0; i < _availableEntries.Count; i++)
        {
            SpawnerConfig.SpawnEntry entry = _availableEntries[i];
            lastAvailable = entry;
            cumulative += entry.Probability;
            if (targetWeight < cumulative)
                return entry;
        }

        return lastAvailable;
    }

    /// <summary>检查生成条目是否满足当前生成器配置、生态预算与物种上限。</summary>
    private bool CanSpawnEntry(
        SpawnerConfig config,
        SpawnerConfig.SpawnEntry entry,
        SpawnerProgressSaveData state)
    {
        if (entry == null ||
            string.IsNullOrWhiteSpace(entry.PrefabName) ||
            entry.Probability <= 0f ||
            (!config.UnboundedDailyGrowth && state.AvailableBudget < Mathf.Max(1, entry.EcologyCost)))
        {
            return false;
        }

        if (AiRuntimeBackendService.UsesEntities(entry) &&
            (!AiRuntimeBackendService.UseEntities ||
             AiRuntimeBackendService.Ecology == null ||
             !AiRuntimeBackendService.Ecology.SupportsSpecies(entry.PrefabName)))
        {
            return false;
        }

        if (!AiRuntimeBackendService.UsesEntities(entry) &&
            _monsterManager.Count >= Mathf.Max(1, _settings.MaxLoadedGameObjectActors))
            return false;
        if (AiRuntimeBackendService.UsesEntities(entry) &&
            AiRuntimeBackendService.Ecology.ResidentCount >= Mathf.Max(1, _settings.MaxLoadedEntityActors))
            return false;

         int speciesLimit = GameDifficultyService.ScaleCount(
             entry.SpeciesAliveLimit,
             GameDifficultyService.Current.World.SpawnPopulationMultiplier);
         return config.IgnorePopulationLimits || config.UnboundedDailyGrowth ||
             entry.SpeciesAliveLimit <= 0 ||
             CountSpeciesAlive(entry.PrefabName) < speciesLimit;
    }

    private bool TryGetValidSpawnPosition(SpawnerConfig config, out Vector3 spawnPosition)
    {
        spawnPosition = default;
        if (_playerPositions.Count == 0)
            return false;

        PrepareNearbyGroupCounts(config);
        Vector3 anchor = _playerPositions[UnityEngine.Random.Range(0, _playerPositions.Count)];
        float randomAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float randomDistance = UnityEngine.Random.Range(
            Mathf.Max(0f, config.MinSpawnDistance),
            Mathf.Max(config.MinSpawnDistance, config.MaxSpawnDistance));
        Vector3 candidate = anchor + new Vector3(
            Mathf.Cos(randomAngle) * randomDistance,
            Mathf.Sin(randomAngle) * randomDistance,
            0f);
        if (config.TreeHabitat.Enabled && !TryGetTreeHabitatPosition(config, out candidate))
            return false;
        candidate = WorldTopologyRuntime.NormalizePosition(candidate);
        candidate.x = Mathf.Floor(candidate.x) + 0.5f;
        candidate.y = Mathf.Floor(candidate.y) + 0.5f;

        if (IsNearAnyPlayer(candidate, Mathf.Max(config.MinSpawnDistance, config.PlayerVisibilityExclusionDistance)) ||
            IsVisibleByAnyActiveCamera(candidate, 0.15f) ||
            !IsWithinPlayerPopulationLimit(config, candidate) ||
            !_chunkManager.IsRuntimeEntityPresentationReady(candidate) ||
            !IsRuntimeTerrainReady(candidate) ||
            !(config.WaterOnly ? AquaticHabitat.CanSwimAt(candidate) : IsWalkableSpawnPosition(candidate)) ||
            !IsBiomeAllowed(config, candidate) ||
            !IsGroundTileAllowed(config, candidate) ||
            !IsLightAllowed(config, candidate))
            return false;

        spawnPosition = candidate;
        return true;
    }

    private bool IsWalkableSpawnPosition(Vector3 worldPos)
    {
        if (_navigationManager == null || !_navigationManager.IsNavigationReady)
            return false;

        return _navigationManager.TryGetCell(worldPos, out _, out bool walkable) && walkable;
    }

    /// <summary>只有新版权威地形已提交的格子才允许生成实体。</summary>
    private bool IsRuntimeTerrainReady(Vector3 worldPos)
    {
        return _chunkManager != null && _chunkManager.TryGetRuntimeTerrainTile(worldPos, out _);
    }

    private bool IsBiomeAllowed(SpawnerConfig config, Vector3 worldPos)
    {
        if (config.AllowedBiomeNames == null || config.AllowedBiomeNames.Count == 0)
            return true;

        Vector2Int worldCell = new(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
        if (_chunkManager != null && _chunkManager.TryGetRuntimeBiomeName(
                worldCell + new Vector2(0.5f, 0.5f), out string runtimeBiomeName))
        {
            return IsAllowedBiomeName(
                config.AllowedBiomeNames, runtimeBiomeName, runtimeBiomeName);
        }

        return false;
    }

    /// <summary>按权威地表 Tile 限制生态出生地，避免只看群系时刷在石块斑块上。</summary>
    private bool IsGroundTileAllowed(SpawnerConfig config, Vector3 worldPos)
    {
        if (config.AllowedGroundTileIds == null || config.AllowedGroundTileIds.Count == 0)
            return true;

        return _chunkManager.TryGetRuntimeTerrainTile(worldPos, out RuntimeTerrainTileSample sample) &&
               config.AllowedGroundTileIds.Contains(sample.Cell.GroundTileId);
    }

    /// <summary>兼容旧 BiomeData 的显示名与资源名。</summary>
    private static bool IsAllowedBiomeName(IReadOnlyList<string> allowedBiomeNames,
        string displayName, string assetName)
    {
        for (int i = 0; i < allowedBiomeNames.Count; i++)
        {
            string allowedName = allowedBiomeNames[i];
            if (string.Equals(allowedName, displayName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(allowedName, assetName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsLightAllowed(SpawnerConfig config, Vector3 worldPos)
    {
        bool needsLightCheck = config.RequireCompletelyDarkTile || config.MaxAllowedTileLight < 0.9999f;
        if (!needsLightCheck)
            return true;

        if (_lightLayerManager == null ||
            !_lightLayerManager.TryGetLightLevel(worldPos, out float lightLevel))
        {
            return false;
        }

        float maxAllowedLight = config.RequireCompletelyDarkTile
            ? LightLayerMgr.CompletelyDarkValue
            : Mathf.Clamp01(config.MaxAllowedTileLight);
        return lightLevel <= maxAllowedLight + 0.0001f;
    }

    private bool CreateMonster(
        SpawnerConfig.SpawnEntry entry,
        Vector3 spawnPosition)
    {
        if (AiRuntimeBackendService.UsesEntities(entry))
        {
            SpawnerConfig owner = FindConfigForEntry(entry);
            return AiRuntimeBackendService.UseEntities &&
                   owner != null && AiRuntimeBackendService.Ecology != null &&
                   AiRuntimeBackendService.Ecology.TrySpawn(owner, entry, spawnPosition);
        }

        Item spawnedItem = null;
        try
        {
            spawnedItem = _itemManager.InstantiateItem(
                entry.PrefabName,
                spawnPosition,
                Quaternion.identity,
                Vector3.one);

            if (spawnedItem == null)
                return false;

            spawnedItem.Load();
            ApplySpawnInitialization(spawnedItem, entry);
            return true;
        }
        catch (Exception ex)
        {
            if (spawnedItem != null && !spawnedItem.DestructionHandled && _itemManager != null)
                _itemManager.DespawnItem(spawnedItem, saveData: false);

            Debug.LogError($"[MonsterSpawnerManager] 生成 {entry?.PrefabName} 失败: {ex}");
            return false;
        }
    }

    private static void ApplySpawnInitialization(
        Item spawnedItem,
        SpawnerConfig.SpawnEntry entry)
    {
        SpawnerConfig.SpawnerNutritionInitialization nutritionConfig = entry?.Initialization?.Nutrition;
        if (spawnedItem == null || nutritionConfig == null || !nutritionConfig.Enabled)
            return;

        Mod_Food food = spawnedItem.itemMods?.GetMod_ByID<Mod_Food>(ModText.Food);
        if (food?.Data?.nutrition == null)
        {
            throw new InvalidOperationException(
                $"物种 {entry.PrefabName} 配置了营养出生初始化，但实例缺少 Food 模块。");
        }

        Nutrition nutrition = food.Data.nutrition;
        float minRate = Mathf.Clamp01(Mathf.Min(
            nutritionConfig.MinFoodRate,
            nutritionConfig.MaxFoodRate));
        float maxRate = Mathf.Clamp01(Mathf.Max(
            nutritionConfig.MinFoodRate,
            nutritionConfig.MaxFoodRate));
        float rate = GetDeterministicFoodRate(spawnedItem, minRate, maxRate);
        nutrition.Carbohydrates = nutrition.Max_Carbohydrates * rate;
        nutrition.Fat = nutrition.Max_Fat * rate;
        food.NotifyStateChanged();
    }

    private static float GetDeterministicFoodRate(Item item, float minRate, float maxRate)
    {
        if (Mathf.Approximately(minRate, maxRate))
            return minRate;

        int seed = item?.itemData != null && item.itemData.Guid != 0
            ? item.itemData.Guid
            : item != null ? item.GetInstanceID() : 1;
        uint hash = unchecked((uint)seed * 2654435761u);
        float normalized = (hash & 0xFFFFu) / 65535f;
        return Mathf.Lerp(minRate, maxRate, normalized);
    }

    private bool IsGlobalDark(string sceneName)
    {
        return _dayTimeSystem != null &&
               _dayTimeSystem.GetLighting(sceneName) <= LightLayerMgr.CompletelyDarkValue + 0.0001f;
    }

    #endregion

    #region 玩家与种群统计

    private void RefreshPlayerPositions(string sceneName)
    {
        _playerPositions.Clear();
        if (_itemManager == null)
            return;

        foreach (Player player in _itemManager.Player_DIC.Values)
        {
            if (player == null ||
                (player.Data != null &&
                 !string.IsNullOrWhiteSpace(player.Data.CurrentSceneName) &&
                 player.Data.CurrentSceneName != sceneName))
            {
                continue;
            }

            AddPlayerPosition(player.transform.position);
        }

        if (_playerPositions.Count == 0 && _itemManager.UserPlayerTransform != null)
            AddPlayerPosition(_itemManager.UserPlayerTransform.position);
    }

    private void AddPlayerPosition(Vector3 position)
    {
        for (int i = 0; i < _playerPositions.Count; i++)
        {
            if (WorldTopologyRuntime.SqrDistance(_playerPositions[i], position) <= 0.01f)
                return;
        }

        _playerPositions.Add(position);
    }

    private bool IsNearAnyPlayer(Vector3 position, float distance)
    {
        float distanceSqr = Mathf.Max(0f, distance) * Mathf.Max(0f, distance);
        for (int i = 0; i < _playerPositions.Count; i++)
        {
            if (WorldTopologyRuntime.SqrDistance(_playerPositions[i], position) < distanceSqr)
                return true;
        }

        return false;
    }

    private bool IsWithinPlayerPopulationLimit(SpawnerConfig config, Vector3 candidate)
    {
        if (config.IgnorePopulationLimits || config.UnboundedDailyGrowth)
            return true;

        int limit = GameDifficultyService.ScaleCount(
            config.PerPlayerAliveLimit,
            GameDifficultyService.Current.World.SpawnPopulationMultiplier);
        if (limit <= 0)
            return true;

        float radius = Mathf.Max(1f, config.PlayerPopulationRadius);
        float radiusSqr = radius * radius;
        for (int i = 0; i < _playerPositions.Count; i++)
        {
            Vector3 playerPosition = _playerPositions[i];
            if (WorldTopologyRuntime.SqrDistance(candidate, playerPosition) > radiusSqr)
                continue;

            int nearbyCount = _nearbyGroupCounts[i];
            if (nearbyCount >= limit)
                return false;
        }

        return true;
    }

    /// <summary>物种上限同时考虑候选区域的休眠居民，不阻止其它区域建立独立种群。</summary>
    private bool IsWithinLocalSpeciesLimit(
        SpawnerConfig config, SpawnerConfig.SpawnEntry entry, Vector3 candidate)
    {
        if (config.IgnorePopulationLimits || config.UnboundedDailyGrowth || entry.SpeciesAliveLimit <= 0)
            return true;
        int limit = GameDifficultyService.ScaleCount(
            entry.SpeciesAliveLimit, GameDifficultyService.Current.World.SpawnPopulationMultiplier);
        int loaded = CountSpeciesAlive(entry.PrefabName);
        int nearbyParked = SaveDataMgr.Instance?.CountParkedRuntimeAiSpeciesWithinRadius(
            entry.PrefabName, candidate,
            Mathf.Max(1f, config.PlayerPopulationRadius) * Mathf.Max(1f, config.PlayerPopulationRadius)) ?? 0;
        return loaded + nearbyParked < limit;
    }

    private int GetEffectiveGroupLimit(SpawnerConfig config)
    {
        if (config.UnboundedDailyGrowth || config.IgnorePopulationLimits)
            return int.MaxValue;

        int populationLimit = GameDifficultyService.ScaleCount(
            config.GroupAliveLimit,
            GameDifficultyService.Current.World.SpawnPopulationMultiplier,
            1);
        if (config.TreeHabitat.Enabled)
            populationLimit = Mathf.Min(populationLimit, RuntimeTreeHabitatIndex.ResolveCapacity(
                CountEligibleHabitatTrees(config), config.TreeHabitat.TreesPerActor));
        return populationLimit;
    }

    /// <summary>当前玩家范围和群系共同筛选树，不把其它维度或远处森林计入容量。</summary>
    private bool IsEligibleHabitatTree(SpawnerConfig config, Vector2 position)
    {
        return IsNearAnyPlayer(position, config.MaxSpawnDistance) && IsBiomeAllowed(config, position);
    }

    private int CountEligibleHabitatTrees(SpawnerConfig config)
    {
        return GetEligibleHabitatTrees(config).Count;
    }

    /// <summary>在树索引中均匀抽取候选，再沿用正式地形、可见性、群系和数量门槛。</summary>
    private bool TryGetTreeHabitatPosition(SpawnerConfig config, out Vector3 candidate)
    {
        candidate = default;
        List<Vector2> positions = GetEligibleHabitatTrees(config);
        if (positions.Count == 0) return false;
        Vector2 tree = positions[UnityEngine.Random.Range(0, positions.Count)];
        if (!IsEligibleHabitatTree(config, tree)) return false;
        candidate = WorldTopologyRuntime.NormalizePosition(tree + UnityEngine.Random.insideUnitCircle * config.TreeHabitat.SpawnRadius);
        return true;
    }

    private int CountGroupAlive(SpawnerConfig config)
    {
        int count = _monsterManager?.GetResidentGroupCount(config) ?? 0;
        if (UsesEntityPopulation(config))
            count += AiRuntimeBackendService.Ecology?.GetGroupCount(config) ?? 0;
        return count;
    }

    private int CountSpeciesAlive(string speciesId)
    {
        int count = _monsterManager?.GetResidentSpeciesCount(speciesId) ?? 0;
        if (AiRuntimeBackendService.UseEntities && AiRuntimeBackendService.UsesEntities(speciesId))
            count += AiRuntimeBackendService.Ecology?.GetSpeciesCount(speciesId) ?? 0;
        return count;
    }

    /// <summary>只统计仍受全局数量预算约束的生态实体。</summary>
    private int CountPopulationLimitedAlive()
    {
        int count = _monsterManager?.ResidentPopulationLimitedCount ?? 0;
        if (AiRuntimeBackendService.UseEntities && _hasLimitedEntities)
            count += AiRuntimeBackendService.Ecology?.PopulationLimitedCount ?? 0;
        return count;
    }

    #endregion

    #region 生物生命周期与回收

    private void ClearTrackedPopulation(bool restoreChunkDormantItems = true)
    {
        if (AiRuntimeBackendService.HasEntitiesRoutes)
            AiRuntimeBackendService.Ecology?.ResetWorld();

        if (restoreChunkDormantItems)
            RestoreChunkDormantItems();

        _farAwaySince.Clear();
        _chunkDormantItems.Clear();
        _itemSnapshot.Clear();
        _monsterSnapshot.Clear();
        _monsterSnapshotVersion = -1;
        _spawnConfigCursor = 0;
        _activeResidentCursor = 0;
        _parkedAddressCursor = 0;
        _playerPositions.Clear();
        _nearbyGroupCounts.Clear();
        _availableEntries.Clear();
        _presentationItems.Clear();
        Array.Clear(_eventCameras, 0, _eventCameras.Length);
        _remainingSearchAttempts.Clear();
        _nextResidentWakeAttempt.Clear();
        _monsterManager?.ResetWorld();
    }

    /// <summary>管理器退出世界或重建索引前释放自己施加的休眠，避免留下永久隐藏实体。</summary>
    private void RestoreChunkDormantItems()
    {
        _itemSnapshot.Clear();
        _itemSnapshot.AddRange(_chunkDormantItems);
        _chunkDormantItems.Clear();
        for (int i = 0; i < _itemSnapshot.Count; i++)
        {
            Item item = _itemSnapshot[i];
            if (item != null && !item.DestructionHandled && !item.gameObject.activeSelf)
            {
                item.gameObject.SetActive(true);
            }
        }

        _itemSnapshot.Clear();
    }

    private void OnMonsterRegistered(Item item, SpawnerConfig config)
    {
        OnRuntimeAiAddressChanged(item);
    }

    private void OnMonsterUnregistered(Item item, SpawnerConfig config)
    {
        if (ReferenceEquals(item, null))
            return;

        _farAwaySince.Remove(item);
        _chunkDormantItems.Remove(item);
    }

    /// <summary>只唤醒由本管理器主动休眠的实体，避免干扰死亡与对象池状态。</summary>
    private bool RefreshTrackedItemChunkDormancy(Item item)
    {
        if (item == null || item.DestructionHandled || _chunkManager == null)
            return false;

        bool presentationReady = _chunkManager.IsRuntimeEntityPresentationReady(
            item.transform.position);
        if (!presentationReady)
        {
            if (item.gameObject.activeSelf &&
                !IsVisibleByAnyActiveCamera(item.transform.position, 0.15f))
            {
                _chunkDormantItems.Add(item);
                item.gameObject.SetActive(false);
                return true;
            }

            return false;
        }

        if (item.gameObject.activeSelf)
            _chunkDormantItems.Remove(item);
        if (_chunkDormantItems.Contains(item) &&
            !IsVisibleByAnyActiveCamera(item.transform.position, 0.15f) &&
            !item.gameObject.activeSelf)
        {
            _chunkDormantItems.Remove(item);
            item.gameObject.SetActive(true);
            return true;
        }
        return false;
    }

    private void OnMonsterDeathStarted(Item item, SpawnerConfig config)
    {
        QueueDeathReplacement(config);
    }

    /// <summary>分批休眠远处活体并恢复邻近居民；一次调度最多执行一个实体生命周期操作。</summary>
    private bool MaintainResidentStreaming()
    {
        SaveDataMgr saveManager = SaveDataMgr.Instance;
        if (saveManager == null || saveManager.SaveData == null || _monsterManager == null)
            return false;

        RefreshMonsterSnapshot();
        int checks = Mathf.Min(Mathf.Max(1, _settings.ResidentChecksPerTick), _monsterSnapshot.Count);
        float now = Time.unscaledTime;
        for (int i = 0; i < checks; i++)
        {
            if (_activeResidentCursor >= _monsterSnapshot.Count)
                _activeResidentCursor = 0;
            MonsterManager.Registration registration = _monsterSnapshot[_activeResidentCursor++];
            Item item = registration.Item;
            SpawnerConfig config = registration.Config;
            if (item == null || item.DestructionHandled || config == null)
                continue;

            // 区块事件处理常规显隐，这里补偿相机移动后仍待处理的可见实体。
            if (RefreshTrackedItemChunkDormancy(item))
                return true;
            if (config.RecycleDistance <= 0f || _monsterManager.IsEcologyRecycleProtected(item) ||
                IsNearAnyPlayer(item.transform.position, config.RecycleDistance) ||
                IsVisibleByAnyActiveCamera(item.transform.position, 0.15f))
            {
                _farAwaySince.Remove(item);
                continue;
            }

            if (!_farAwaySince.TryGetValue(item, out float farSince))
            {
                _farAwaySince[item] = now;
                continue;
            }
            if (now - farSince < Mathf.Max(0f, config.RecycleGraceSeconds))
                continue;

            try
            {
                if (saveManager.TryParkRuntimeAi(item))
                    return true;
            }
            catch (Exception exception)
            {
                _farAwaySince[item] = now;
                Debug.LogError($"[MonsterSpawnerManager] 休眠生物失败：{item.name}", item);
                Debug.LogException(exception);
            }
        }

        int parkedChecks = Mathf.Max(1, _settings.ResidentChecksPerTick);
        int priorityChecks = Mathf.Min(parkedChecks, _wakePriorityAddresses.Count);
        for (int i = 0; i < priorityChecks; i++)
        {
            RuntimeWorldAddress address = _wakePriorityAddresses.Dequeue();
            _queuedWakePriorityAddresses.Remove(address);
            if (!saveManager.TryGetNextParkedRuntimeAi(address, out ItemData prioritized))
                continue;
            if (!TryWakeResidentSnapshot(saveManager, prioritized, now))
                continue;
            if (!_nextResidentWakeAttempt.ContainsKey(prioritized.Guid))
                QueueWakePriorityAddress(address);
            return true;
        }

        for (int i = priorityChecks; i < parkedChecks; i++)
        {
            if (!saveManager.TryGetNextParkedRuntimeAi(ref _parkedAddressCursor, out ItemData snapshot))
                break;
            if (TryWakeResidentSnapshot(saveManager, snapshot, now))
                return true;
        }

        return false;
    }

    /// <summary>只有玩家附近、区块就绪且所有活动镜头之外，才恢复同一居民的完整状态。</summary>
    private bool TryWakeResidentSnapshot(SaveDataMgr saveManager, ItemData snapshot, float now)
    {
        if (snapshot?.transform == null ||
            !_monsterManager.TryGetConfigForSpecies(snapshot.IDName, out SpawnerConfig config) ||
            config.RecycleDistance <= 0f)
            return false;

        Vector3 position = snapshot.transform.position;
        float wakeDistance = Mathf.Max(config.MaxSpawnDistance, config.RecycleDistance * 0.8f);
        if (!IsNearAnyPlayer(position, wakeDistance) ||
            _monsterManager.Count >= Mathf.Max(1, _settings.MaxLoadedGameObjectActors) ||
            !_chunkManager.IsRuntimeEntityPresentationReady(position) ||
            IsVisibleByAnyActiveCamera(position, 0.15f) ||
            (_nextResidentWakeAttempt.TryGetValue(snapshot.Guid, out float nextAttempt) && now < nextAttempt))
            return false;

        if (saveManager.TryWakeParkedRuntimeAi(snapshot))
            _nextResidentWakeAttempt.Remove(snapshot.Guid);
        else
            _nextResidentWakeAttempt[snapshot.Guid] = now + 5f;
        return true;
    }

    /// <summary>非权威客户端只维护本地表现显隐，不操作生态出生和持久化。</summary>
    private void RefreshClientChunkDormancy()
    {
        RefreshMonsterSnapshot();
        int checks = Mathf.Min(Mathf.Max(1, _settings.ResidentChecksPerTick), _monsterSnapshot.Count);
        for (int i = 0; i < checks; i++)
        {
            if (_activeResidentCursor >= _monsterSnapshot.Count)
                _activeResidentCursor = 0;
            if (RefreshTrackedItemChunkDormancy(_monsterSnapshot[_activeResidentCursor++].Item))
                return;
        }
    }

    #endregion

    #region AI 后端切换

    /// <summary>查找生成条目的所属配置；条目对象来自已冻结的当前生态目录。</summary>
    private SpawnerConfig FindConfigForEntry(SpawnerConfig.SpawnEntry entry)
    {
        return entry != null && _entryOwners.TryGetValue(entry, out SpawnerConfig config) ? config : null;
    }

    /// <summary>至少存在一个当前后端可实际生成的条目；ECS 失败不会把该物种静默回退为 GameObject。</summary>
    private static bool HasAvailableBackendEntry(SpawnerConfig config)
    {
        if (config?.SpawnEntries == null)
            return false;

        for (int i = 0; i < config.SpawnEntries.Count; i++)
        {
            SpawnerConfig.SpawnEntry entry = config.SpawnEntries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.PrefabName))
                continue;
            if (!AiRuntimeBackendService.UsesEntities(entry))
                return true;
            if (AiRuntimeBackendService.UseEntities &&
                AiRuntimeBackendService.Ecology?.SupportsSpecies(entry.PrefabName) == true)
                return true;
        }
        return false;
    }

    #endregion

    #region 状态与调试

    private SpawnerProgressSaveData GetOrCreateState(SpawnerConfig config)
    {
        _runtimeStates ??= new Dictionary<string, SpawnerProgressSaveData>();
        string key = GetConfigKey(config);
        if (!_runtimeStates.TryGetValue(key, out SpawnerProgressSaveData state) || state == null)
        {
            state = new SpawnerProgressSaveData();
            _runtimeStates[key] = state;
        }

        state.PendingSpawnCount = Mathf.Max(0, state.PendingSpawnCount);
        state.PendingReplacementCount = Mathf.Max(0, state.PendingReplacementCount);
        return state;
    }

    private string GetConfigKey(SpawnerConfig config)
    {
        if (_configKeys.TryGetValue(config, out string cached))
            return cached;
        string key = string.IsNullOrWhiteSpace(config.PersistentId)
            ? config.name
            : config.PersistentId.Trim();
        _configKeys.Add(config, key);
        return key;
    }

    [Button("调试：触发首个配置")]
    public void DebugTriggerSpawn()
    {
        if (_spawnerConfigs == null || _spawnerConfigs.Count == 0 || _spawnerConfigs[0] == null)
            return;

        SpawnerConfig config = _spawnerConfigs[0];
        SpawnerProgressSaveData state = GetOrCreateState(config);
        state.PendingSpawnCount += Mathf.Max(1, config.SpawnCount);
    }

    [Button("调试：幽灵待生成数量+1")]
    public void DebugQueueOneGrowthSpawn()
    {
        SpawnerConfig config = _spawnerConfigs?.Find(
            value => value != null && value.ScheduleMode == SpawnerScheduleMode.DayMilestoneGrowth);
        if (config == null)
            return;

        SpawnerProgressSaveData state = GetOrCreateState(config);
        state.PendingReplacementCount++;
    }

    [Button("调试：立即生成幽灵")]
    public void DebugSpawnGhostImmediate()
    {
        SpawnerConfig config = _spawnerConfigs?.Find(
            value => value != null && value.ScheduleMode == SpawnerScheduleMode.DayMilestoneGrowth);
        if (config == null)
        {
            Debug.LogWarning("[MonsterSpawnerManager] 未找到幽灵生成配置。", this);
            return;
        }

        SpawnerProgressSaveData state = GetOrCreateState(config);
        if (!TrySpawnOne(config, state))
        {
            Debug.LogWarning("[MonsterSpawnerManager] 幽灵生成失败，请确认玩家附近存在已加载、可行走的完全黑暗格。", this);
        }
    }

    #endregion
}
