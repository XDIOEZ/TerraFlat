using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// 生物生成与生态预算管理器。
/// 负责跨时刻调度、种群上限、环境校验、死亡补位和远距离回收。
/// </summary>
[RequireComponent(typeof(MonsterManager))]
public partial class MonsterSpawnerManager : SingletonAutoMono<MonsterSpawnerManager>
{
    #region 配置

    [Header("生成配置")]
    [SerializeField, Required]
    private List<SpawnerConfig> _spawnerConfigs = new();

    [Header("全局生态限制")]
    [SerializeField, Min(1)]
    private int _globalAliveLimit = 40;

    [SerializeField, Min(0.1f)]
    private float _spawnRetryInterval = 0.5f;

    [SerializeField, Min(0.5f)]
    private float _populationMaintenanceInterval = 2f;

    [SerializeField, Min(0.5f)]
    private float _recycleCheckInterval = 2f;

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
    private readonly List<SpawnerConfig> _jsonRuntimeConfigs = new();
    private readonly List<Vector3> _playerPositions = new(4);
    private readonly List<Item> _itemSnapshot = new(64);
    private readonly List<MonsterManager.Registration> _monsterSnapshot = new(64);
    private readonly Dictionary<string, int> _overflowSpeciesCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<SpawnerConfig, int> _overflowGroupCounts = new();
    private List<SpawnerConfig> _serializedSpawnerConfigs;
    private float _nextPopulationMaintenanceTime;
    private float _nextRecycleCheckTime;

    #endregion

    #region Unity 生命周期

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this)
            return;

        _serializedSpawnerConfigs = _spawnerConfigs;
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
        if (DimensionManager.Instance.ActiveDefinition?.EnableMonsterSpawning == false)
        {
            ClearTrackedPopulation();
            enabled = false;
            return;
        }

        if (!PrepareSpawnerConfigs())
        {
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
            enabled = false;
            return;
        }

        BindSaveData(SaveDataMgr.Instance?.SaveData);
        _itemManager.CleanupNullItems();
        _monsterManager.Configure(_spawnerConfigs, _itemManager.WorldRunTimeItems.Values);
        _nextPopulationMaintenanceTime = Time.unscaledTime;
        _nextRecycleCheckTime = Time.unscaledTime + _recycleCheckInterval;
        enabled = true;
    }

    private void OnGameWorldExit()
    {
        CaptureSaveData(SaveDataMgr.Instance?.SaveData);
        // 世界退出时对象会随场景/区块一起销毁，不再唤醒本管理器主动休眠的实体。
        ClearTrackedPopulation(restoreChunkDormantItems: false);
        ReleaseJsonRuntimeConfigs();
        _nextSpawnRetryTime.Clear();
        _nextRecoveryCheckTime.Clear();
        enabled = false;
        _itemManager = null;
        _chunkManager = null;
        _navigationManager = null;
        _lightLayerManager = null;
        _dayTimeSystem = null;
    }

    protected override void OnDestroy()
    {
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

        // OnDestroy 可能发生在 Unity 正在卸载场景时，禁止对即将销毁的对象调用 SetActive。
        ClearTrackedPopulation(restoreChunkDormantItems: false);
        ReleaseJsonRuntimeConfigs();

        base.OnDestroy();
    }

    private bool PrepareSpawnerConfigs()
    {
        _monsterManager?.ResetWorld();
        ReleaseJsonRuntimeConfigs();

        if (SpawnerConfigCatalogService.IsLoaded)
        {
            _jsonRuntimeConfigs.AddRange(SpawnerConfigCatalogService.CreateRuntimeConfigs());
            _spawnerConfigs = _jsonRuntimeConfigs;
        }
        else
        {
            _spawnerConfigs = _serializedSpawnerConfigs ?? new List<SpawnerConfig>();
        }

        if (_spawnerConfigs == null || _spawnerConfigs.Count == 0)
        {
            Debug.LogError("[MonsterSpawnerManager] JSON 与兼容回退均未提供有效 SpawnerConfig。", this);
            return false;
        }

        return true;
    }

    private void ReleaseJsonRuntimeConfigs()
    {
        for (int index = 0; index < _jsonRuntimeConfigs.Count; index++)
        {
            SpawnerConfig config = _jsonRuntimeConfigs[index];
            if (config != null)
                Destroy(config);
        }

        _jsonRuntimeConfigs.Clear();
    }

    private void Update()
    {
        if (_gameManager == null || !_gameManager.IsGameplayReady)
            return;

        RefreshChunkDormancy();

        if (!GameNetwork.HasStateAuthority)
            return;

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
            ProcessPendingSpawns(config, state, sceneName, currentDay);
        }

        MaintainTrackedPopulation();
        RecycleDistantPopulation();
    }

    #endregion

    #region 存档

    public void CaptureSaveData(GameSaveData saveData)
    {
        if (saveData == null)
            return;

        saveData.MonsterSpawnerData ??= new MonsterSpawnerSaveData();
        saveData.MonsterSpawnerData.ConfigStates = _runtimeStates ?? new Dictionary<string, SpawnerProgressSaveData>();
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
            state.LastCheckedDay = currentDay;
            state.TriggeredWindowIndices.Clear();
            return;
        }

        if (currentTotalTime < state.LastProcessedTotalTime)
            state.LastProcessedTotalTime = currentTotalTime;

        float previousTotalTime = state.LastProcessedTotalTime;
        state.LastProcessedTotalTime = currentTotalTime;
        state.LastCheckedDay = currentDay;
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
        int startDay = Mathf.Max(0, Mathf.FloorToInt(previousTotalTime / dayLength));
        int endDay = Mathf.Max(startDay, Mathf.FloorToInt(currentTotalTime / dayLength));

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

                if (UnityEngine.Random.value > config.SpawnChance)
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
        int targetPopulation = Mathf.Min(
            GameDifficultyService.ScaleCount(
                config.RecoveryTargetPopulation,
                GameDifficultyService.Current.World.SpawnPopulationMultiplier),
            GetEffectiveGroupLimit(config));
        if (targetPopulation <= 0)
            return;

        string key = GetConfigKey(config);
        float now = Time.unscaledTime;
        if (_nextRecoveryCheckTime.TryGetValue(key, out float nextCheck) && now < nextCheck)
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
        if (config.IgnorePopulationLimits)
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

    private void ProcessPendingSpawns(
        SpawnerConfig config,
        SpawnerProgressSaveData state,
        string sceneName,
        int currentDay)
    {
        if (state.PendingSpawnCount <= 0 && state.PendingReplacementCount <= 0)
            return;

        if (config.RequireGlobalDarkness && !IsGlobalDark(sceneName))
            return;

        string key = GetConfigKey(config);
        float now = Time.unscaledTime;
        if (_nextSpawnRetryTime.TryGetValue(key, out float nextRetry) && now < nextRetry)
            return;

        _nextSpawnRetryTime[key] = now + Mathf.Max(0.05f, _spawnRetryInterval);
        if (!TrySpawnOne(config, state))
            return;

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
    }

    private bool TrySpawnOne(SpawnerConfig config, SpawnerProgressSaveData state)
    {
        int globalLimit = GameDifficultyService.ScaleCount(
            _globalAliveLimit,
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

        float totalWeight = 0f;
        for (int i = 0; i < config.SpawnEntries.Count; i++)
        {
            SpawnerConfig.SpawnEntry entry = config.SpawnEntries[i];
            if (CanSpawnEntry(config, entry, state))
                totalWeight += entry.Probability;
        }

        if (totalWeight <= 0f)
            return null;

        float targetWeight = UnityEngine.Random.value * totalWeight;
        float cumulative = 0f;
        SpawnerConfig.SpawnEntry lastAvailable = null;
        for (int i = 0; i < config.SpawnEntries.Count; i++)
        {
            SpawnerConfig.SpawnEntry entry = config.SpawnEntries[i];
            if (!CanSpawnEntry(config, entry, state))
                continue;

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

         int speciesLimit = GameDifficultyService.ScaleCount(
             entry.SpeciesAliveLimit,
             GameDifficultyService.Current.World.SpawnPopulationMultiplier);
         return config.IgnorePopulationLimits ||
             entry.SpeciesAliveLimit <= 0 ||
             CountSpeciesAlive(entry.PrefabName) < speciesLimit;
    }

    private bool TryGetValidSpawnPosition(SpawnerConfig config, out Vector3 spawnPosition)
    {
        spawnPosition = default;
        if (_playerPositions.Count == 0)
            return false;

        int retries = Mathf.Max(1, config.SpawnSearchRetryCount);
        for (int i = 0; i < retries; i++)
        {
            Vector3 anchor = _playerPositions[UnityEngine.Random.Range(0, _playerPositions.Count)];
            float randomAngle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float randomDistance = UnityEngine.Random.Range(
                Mathf.Max(0f, config.MinSpawnDistance),
                Mathf.Max(config.MinSpawnDistance, config.MaxSpawnDistance));

            Vector3 candidate = anchor + new Vector3(
                Mathf.Cos(randomAngle) * randomDistance,
                Mathf.Sin(randomAngle) * randomDistance,
                0f);
            candidate.x = Mathf.Floor(candidate.x) + 0.5f;
            candidate.y = Mathf.Floor(candidate.y) + 0.5f;

            if (IsNearAnyPlayer(candidate, Mathf.Max(config.MinSpawnDistance, config.PlayerVisibilityExclusionDistance)) ||
                !IsWithinPlayerPopulationLimit(config, candidate) ||
                !IsRuntimeTerrainReady(candidate) ||
                !IsWalkableSpawnPosition(candidate) ||
                !IsBiomeAllowed(config, candidate) ||
                !IsLightAllowed(config, candidate))
            {
                continue;
            }

            spawnPosition = candidate;
            return true;
        }

        return false;
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

    private bool TrySpawnMonster(
        SpawnerConfig.SpawnEntry entry,
        Vector3 spawnPosition)
    {
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
        if (config.IgnorePopulationLimits)
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

            int nearbyCount = _monsterManager.CountGroupWithinRadius(config, playerPosition, radiusSqr);
            if (nearbyCount >= limit)
                return false;
        }

        return true;
    }

    private int GetEffectiveGroupLimit(SpawnerConfig config)
    {
        if (config.UnboundedDailyGrowth || config.IgnorePopulationLimits)
            return int.MaxValue;

        return GameDifficultyService.ScaleCount(
            config.GroupAliveLimit,
            GameDifficultyService.Current.World.SpawnPopulationMultiplier,
            1);
    }

    private int CountGroupAlive(SpawnerConfig config)
    {
        return _monsterManager?.GetGroupCount(config) ?? 0;
    }

    private int CountSpeciesAlive(string speciesId)
    {
        return _monsterManager?.GetSpeciesCount(speciesId) ?? 0;
    }

    /// <summary>只统计仍受全局数量预算约束的生态实体。</summary>
    private int CountPopulationLimitedAlive()
    {
        return _monsterManager?.PopulationLimitedCount ?? 0;
    }

    #endregion

    #region 生物生命周期与回收

    private void ClearTrackedPopulation(bool restoreChunkDormantItems = true)
    {
        if (restoreChunkDormantItems)
            RestoreChunkDormantItems();

        _farAwaySince.Clear();
        _chunkDormantItems.Clear();
        _itemSnapshot.Clear();
        _monsterSnapshot.Clear();
        _overflowSpeciesCounts.Clear();
        _overflowGroupCounts.Clear();
        _monsterManager?.ResetWorld();
    }

    /// <summary>管理器退出世界或重建索引前释放自己施加的休眠，避免留下永久隐藏实体。</summary>
    private void RestoreChunkDormantItems()
    {
        foreach (Item item in _chunkDormantItems)
        {
            if (item != null && !item.DestructionHandled && !item.gameObject.activeSelf)
                item.gameObject.SetActive(true);
        }

        _chunkDormantItems.Clear();
    }

    private void OnMonsterRegistered(Item item, SpawnerConfig config)
    {
        if (enabled)
            RefreshTrackedItemChunkDormancy(item);
    }

    private void OnMonsterUnregistered(Item item, SpawnerConfig config)
    {
        if (ReferenceEquals(item, null))
            return;

        _farAwaySince.Remove(item);
        _chunkDormantItems.Remove(item);
    }

    /// <summary>让自然生物随新版区块画面休眠，并在画面重新绑定后恢复。</summary>
    private void RefreshChunkDormancy()
    {
        if (_monsterManager == null || _monsterManager.Count == 0 || _chunkManager == null)
            return;

        _monsterManager.CopyRegistrations(_monsterSnapshot);
        for (int i = 0; i < _monsterSnapshot.Count; i++)
            RefreshTrackedItemChunkDormancy(_monsterSnapshot[i].Item);
    }

    /// <summary>只唤醒由本管理器主动休眠的实体，避免干扰死亡与对象池状态。</summary>
    private void RefreshTrackedItemChunkDormancy(Item item)
    {
        if (item == null || item.DestructionHandled || _chunkManager == null)
            return;

        bool presentationReady = _chunkManager.IsRuntimeEntityPresentationReady(
            item.transform.position);
        if (!presentationReady)
        {
            if (item.gameObject.activeSelf)
            {
                _chunkDormantItems.Add(item);
                item.gameObject.SetActive(false);
            }

            return;
        }

        if (_chunkDormantItems.Remove(item) && !item.gameObject.activeSelf)
            item.gameObject.SetActive(true);
    }

    private void OnMonsterDeathStarted(Item item, SpawnerConfig config)
    {
        QueueDeathReplacement(config);
    }

    private void MaintainTrackedPopulation()
    {
        if (Time.unscaledTime < _nextPopulationMaintenanceTime)
            return;

        _nextPopulationMaintenanceTime = Time.unscaledTime + Mathf.Max(0.5f, _populationMaintenanceInterval);
        _monsterManager.PruneInvalidRegistrations();
        _monsterManager.CopyRegistrations(_monsterSnapshot);
        CollectPopulationOverflow(_itemSnapshot);
        for (int i = 0; i < _itemSnapshot.Count; i++)
        {
            Item item = _itemSnapshot[i];
            if (item != null && _itemManager != null)
                _itemManager.DespawnItem(item, saveData: false);
        }
    }

    private void CollectPopulationOverflow(List<Item> overflow)
    {
        overflow.Clear();
        _overflowSpeciesCounts.Clear();
        _overflowGroupCounts.Clear();
        int totalCount = 0;

        for (int registrationIndex = 0; registrationIndex < _monsterSnapshot.Count; registrationIndex++)
        {
            MonsterManager.Registration registration = _monsterSnapshot[registrationIndex];
            Item item = registration.Item;
            SpawnerConfig config = registration.Config;
            if (item == null || item.itemData == null || config == null)
                continue;

            string speciesId = registration.SpeciesId;
            _overflowSpeciesCounts.TryGetValue(speciesId, out int speciesCount);
            _overflowGroupCounts.TryGetValue(config, out int groupCount);
            speciesCount++;
            groupCount++;
            if (!config.UnboundedDailyGrowth && !config.IgnorePopulationLimits)
                totalCount++;
            _overflowSpeciesCounts[speciesId] = speciesCount;
            _overflowGroupCounts[config] = groupCount;

            int speciesLimit = 0;
            if (config.SpawnEntries != null)
            {
                for (int i = 0; i < config.SpawnEntries.Count; i++)
                {
                    SpawnerConfig.SpawnEntry entry = config.SpawnEntries[i];
                    if (entry != null && entry.PrefabName == speciesId)
                    {
                        speciesLimit = entry.SpeciesAliveLimit;
                        break;
                    }
                }
            }

            bool exceedsLimit =
                !config.IgnorePopulationLimits &&
                ((!config.UnboundedDailyGrowth && totalCount > Mathf.Max(1, _globalAliveLimit)) ||
                 groupCount > GetEffectiveGroupLimit(config) ||
                 (speciesLimit > 0 && speciesCount > speciesLimit) ||
                 ExceedsNearbyPlayerLimit(item, config, overflow));
            if (exceedsLimit && !_monsterManager.IsEcologyRecycleProtected(item))
            {
                overflow.Add(item);
            }
        }
    }

    private bool ExceedsNearbyPlayerLimit(Item item, SpawnerConfig config, List<Item> pendingOverflow)
    {
        int limit = Mathf.Max(0, config.PerPlayerAliveLimit);
        if (limit <= 0 || _playerPositions.Count == 0)
            return false;

        float radius = Mathf.Max(1f, config.PlayerPopulationRadius);
        float radiusSqr = radius * radius;
        for (int playerIndex = 0; playerIndex < _playerPositions.Count; playerIndex++)
        {
            Vector3 playerPosition = _playerPositions[playerIndex];
            if (WorldTopologyRuntime.SqrDistance(item.transform.position, playerPosition) > radiusSqr)
                continue;

            int nearbyCount = 0;
            for (int registrationIndex = 0;
                 registrationIndex < _monsterSnapshot.Count;
                 registrationIndex++)
            {
                MonsterManager.Registration registration = _monsterSnapshot[registrationIndex];
                Item candidate = registration.Item;
                if (candidate == null ||
                    registration.Config != config ||
                    pendingOverflow.Contains(candidate))
                {
                    continue;
                }

                if (WorldTopologyRuntime.SqrDistance(candidate.transform.position, playerPosition) <= radiusSqr)
                    nearbyCount++;
            }

            if (nearbyCount > limit)
                return true;
        }

        return false;
    }

    private void RecycleDistantPopulation()
    {
        if (Time.unscaledTime < _nextRecycleCheckTime || _playerPositions.Count == 0)
            return;

        _nextRecycleCheckTime = Time.unscaledTime + Mathf.Max(0.5f, _recycleCheckInterval);
        _itemSnapshot.Clear();
        _monsterManager.CopyRegistrations(_monsterSnapshot);
        float now = Time.unscaledTime;

        for (int registrationIndex = 0; registrationIndex < _monsterSnapshot.Count; registrationIndex++)
        {
            MonsterManager.Registration registration = _monsterSnapshot[registrationIndex];
            Item item = registration.Item;
            SpawnerConfig config = registration.Config;
            if (item == null || config == null || config.RecycleDistance <= 0f)
                continue;
            if (_monsterManager.IsEcologyRecycleProtected(item))
            {
                _farAwaySince.Remove(item);
                continue;
            }

            if (!IsNearAnyPlayer(item.transform.position, config.RecycleDistance))
            {
                if (!_farAwaySince.TryGetValue(item, out float farSince))
                {
                    _farAwaySince[item] = now;
                }
                else if (now - farSince >= Mathf.Max(0f, config.RecycleGraceSeconds))
                {
                    _itemSnapshot.Add(item);
                }
            }
            else
            {
                _farAwaySince.Remove(item);
            }
        }

        for (int i = 0; i < _itemSnapshot.Count; i++)
        {
            Item item = _itemSnapshot[i];
            if (item != null && _itemManager != null)
                _itemManager.DespawnItem(item, saveData: false);
        }
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

        state.TriggeredWindowIndices ??= new List<int>();
        state.PendingSpawnCount = Mathf.Max(0, state.PendingSpawnCount);
        state.PendingReplacementCount = Mathf.Max(0, state.PendingReplacementCount);
        return state;
    }

    private static string GetConfigKey(SpawnerConfig config)
    {
        return string.IsNullOrWhiteSpace(config.PersistentId)
            ? config.name
            : config.PersistentId.Trim();
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
