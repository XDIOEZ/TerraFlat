using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// 生物生成与生态预算管理器。
/// 负责跨时刻调度、种群上限、环境校验、死亡补位和远距离回收。
/// </summary>
public class MonsterSpawnerManager : SingletonAutoMono<MonsterSpawnerManager>
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

    [Header("调试")]
    [SerializeField, ReadOnly]
    private int _trackedPopulation;

    #endregion

    #region 运行时状态

    private DayTimeSystem _dayTimeSystem;
    private Dictionary<string, SpawnerProgressSaveData> _runtimeStates = new();
    private readonly Dictionary<string, SpawnerConfig> _configBySpecies = new(StringComparer.Ordinal);
    private readonly Dictionary<Item, SpawnerConfig> _trackedItems = new();
    private readonly Dictionary<DamageReceiver, Item> _deathReceivers = new();
    private readonly Dictionary<Item, DamageReceiver> _receiverByItem = new();
    private readonly Dictionary<Item, float> _farAwaySince = new();
    private readonly Dictionary<string, float> _nextSpawnRetryTime = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _nextRecoveryCheckTime = new(StringComparer.Ordinal);
    private readonly List<Vector3> _playerPositions = new(4);
    private readonly List<Item> _itemSnapshot = new(64);
    private float _nextPopulationMaintenanceTime;
    private float _nextRecycleCheckTime;

    #endregion

    #region Unity 生命周期

    private void Start()
    {
        enabled = false;

        if (_spawnerConfigs == null || _spawnerConfigs.Count == 0)
        {
            Debug.LogError("[MonsterSpawnerManager] 至少需要一个 SpawnerConfig。", this);
            return;
        }

        BuildSpeciesLookup();
        ItemMgr.RuntimeItemInstantiated += OnRuntimeItemInstantiated;
        ItemMgr.RuntimeItemDespawning += OnRuntimeItemDespawning;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.Event_GameWorldEnter += OnGameWorldEnter;
            GameManager.Instance.Event_GameWorldExit += OnGameWorldExit;
        }
    }

    private void OnGameWorldEnter()
    {
        _dayTimeSystem = DayTimeSystem.Instance;
        if (_dayTimeSystem == null)
        {
            enabled = false;
            return;
        }

        BindSaveData(SaveDataMgr.Instance?.SaveData);
        RebuildTrackedPopulation();
        _nextPopulationMaintenanceTime = Time.unscaledTime;
        _nextRecycleCheckTime = Time.unscaledTime + _recycleCheckInterval;
        enabled = true;
    }

    private void OnGameWorldExit()
    {
        CaptureSaveData(SaveDataMgr.Instance?.SaveData);
        ClearTrackedPopulation();
        _nextSpawnRetryTime.Clear();
        _nextRecoveryCheckTime.Clear();
        enabled = false;
        _dayTimeSystem = null;
    }

    protected override void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.Event_GameWorldEnter -= OnGameWorldEnter;
            GameManager.Instance.Event_GameWorldExit -= OnGameWorldExit;
        }

        ItemMgr.RuntimeItemInstantiated -= OnRuntimeItemInstantiated;
        ItemMgr.RuntimeItemDespawning -= OnRuntimeItemDespawning;
        ClearTrackedPopulation();

        base.OnDestroy();
    }

    private void Update()
    {
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

        if (ItemMgr.Instance == null || _dayTimeSystem == null)
        {
            return false;
        }

        sceneName = ItemMgr.Instance.PlayerInSceneName;
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
        if (_trackedItems.Count >= globalLimit ||
            CountGroupAlive(config) >= GetEffectiveGroupLimit(config))
        {
            return false;
        }

        SpawnerConfig.SpawnEntry entry = DetermineAvailableEntry(config, state);
        if (entry == null)
            return false;

        if (!TryGetValidSpawnPosition(config, out Vector3 spawnPosition))
            return false;

        if (!SpawnMonster(entry.PrefabName, spawnPosition))
            return false;

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
            if (CanSpawnEntry(entry, state))
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
            if (!CanSpawnEntry(entry, state))
                continue;

            lastAvailable = entry;
            cumulative += entry.Probability;
            if (targetWeight < cumulative)
                return entry;
        }

        return lastAvailable;
    }

    private bool CanSpawnEntry(SpawnerConfig.SpawnEntry entry, SpawnerProgressSaveData state)
    {
        if (entry == null ||
            string.IsNullOrWhiteSpace(entry.PrefabName) ||
            entry.Probability <= 0f ||
            state.AvailableBudget < Mathf.Max(1, entry.EcologyCost))
        {
            return false;
        }

         int speciesLimit = GameDifficultyService.ScaleCount(
             entry.SpeciesAliveLimit,
             GameDifficultyService.Current.World.SpawnPopulationMultiplier);
         return entry.SpeciesAliveLimit <= 0 ||
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
                !TryGetLoadedMap(candidate, out Map map) ||
                !IsWalkableSpawnPosition(candidate) ||
                !IsBiomeAllowed(config, map, candidate) ||
                !IsLightAllowed(config, candidate))
            {
                continue;
            }

            spawnPosition = candidate;
            return true;
        }

        return false;
    }

    private static bool IsWalkableSpawnPosition(Vector3 worldPos)
    {
        AstarGameManager astarManager = AstarGameManager.Instance;
        if (astarManager == null || !astarManager.IsGridGraphReady)
            return false;

        return astarManager.TryGetNodePenalty_GridGraphFast(worldPos, out _, out bool walkable) && walkable;
    }

    private static bool TryGetLoadedMap(Vector3 worldPos, out Map map)
    {
        map = null;
        if (ChunkMgr.Instance == null)
            return false;

        Vector2Int chunkPos = Chunk.GetChunkPosition(worldPos);
        if (!ChunkMgr.Instance.TryGetActiveChunkByPos(chunkPos, out Chunk chunk) ||
            chunk == null ||
            chunk.Map == null ||
            !chunk.Map.IsReadyForChunkLifecycle)
        {
            return false;
        }

        map = chunk.Map;
        return true;
    }

    private static bool IsBiomeAllowed(SpawnerConfig config, Map map, Vector3 worldPos)
    {
        if (config.AllowedBiomeNames == null || config.AllowedBiomeNames.Count == 0)
            return true;

        ChunkGenerator_Land landGenerator = map?.LandGenerator;
        Vector2Int worldCell = new(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
        if (landGenerator == null || !landGenerator.TryGetBiomeAtWorld(worldCell, out BiomeData biome))
            return false;

        for (int i = 0; i < config.AllowedBiomeNames.Count; i++)
        {
            string allowedName = config.AllowedBiomeNames[i];
            if (string.Equals(allowedName, biome.BiomeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(allowedName, biome.name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLightAllowed(SpawnerConfig config, Vector3 worldPos)
    {
        bool needsLightCheck = config.RequireCompletelyDarkTile || config.MaxAllowedTileLight < 0.9999f;
        if (!needsLightCheck)
            return true;

        if (LightLayerMgr.Instance == null ||
            !LightLayerMgr.Instance.TryGetLightLevel(worldPos, out float lightLevel))
        {
            return false;
        }

        float maxAllowedLight = config.RequireCompletelyDarkTile
            ? LightLayerMgr.CompletelyDarkValue
            : Mathf.Clamp01(config.MaxAllowedTileLight);
        return lightLevel <= maxAllowedLight + 0.0001f;
    }

    private static bool SpawnMonster(string spawnType, Vector3 spawnPosition)
    {
        try
        {
            Item spawnedItem = ItemMgr.Instance.InstantiateItem(
                spawnType,
                spawnPosition,
                Quaternion.identity,
                Vector3.one);

            if (spawnedItem == null)
                return false;

            spawnedItem.Load();
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MonsterSpawnerManager] 生成 {spawnType} 失败: {ex.Message}");
            return false;
        }
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
        ItemMgr itemMgr = ItemMgr.Instance;
        if (itemMgr == null)
            return;

        foreach (Player player in itemMgr.Player_DIC.Values)
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

        if (_playerPositions.Count == 0 && itemMgr.UserPlayerTransform != null)
            AddPlayerPosition(itemMgr.UserPlayerTransform.position);
    }

    private void AddPlayerPosition(Vector3 position)
    {
        for (int i = 0; i < _playerPositions.Count; i++)
        {
            if ((_playerPositions[i] - position).sqrMagnitude <= 0.01f)
                return;
        }

        _playerPositions.Add(position);
    }

    private bool IsNearAnyPlayer(Vector3 position, float distance)
    {
        float distanceSqr = Mathf.Max(0f, distance) * Mathf.Max(0f, distance);
        for (int i = 0; i < _playerPositions.Count; i++)
        {
            if ((_playerPositions[i] - position).sqrMagnitude < distanceSqr)
                return true;
        }

        return false;
    }

    private bool IsWithinPlayerPopulationLimit(SpawnerConfig config, Vector3 candidate)
    {
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
            if ((candidate - playerPosition).sqrMagnitude > radiusSqr)
                continue;

            int nearbyCount = 0;
            foreach (KeyValuePair<Item, SpawnerConfig> pair in _trackedItems)
            {
                Item item = pair.Key;
                if (item != null && pair.Value == config &&
                    (item.transform.position - playerPosition).sqrMagnitude <= radiusSqr)
                {
                    nearbyCount++;
                }
            }

            if (nearbyCount >= limit)
                return false;
        }

        return true;
    }

    private int GetEffectiveGroupLimit(SpawnerConfig config)
    {
        return GameDifficultyService.ScaleCount(
            config.GroupAliveLimit,
            GameDifficultyService.Current.World.SpawnPopulationMultiplier,
            1);
    }

    private int CountGroupAlive(SpawnerConfig config)
    {
        int count = 0;
        foreach (KeyValuePair<Item, SpawnerConfig> pair in _trackedItems)
        {
            if (pair.Key != null && pair.Value == config)
                count++;
        }

        return count;
    }

    private int CountSpeciesAlive(string speciesId)
    {
        int count = 0;
        foreach (Item item in _trackedItems.Keys)
        {
            if (item?.itemData != null && item.itemData.IDName == speciesId)
                count++;
        }

        return count;
    }

    #endregion

    #region 生物生命周期与回收

    private void BuildSpeciesLookup()
    {
        _configBySpecies.Clear();

        for (int i = 0; i < _spawnerConfigs.Count; i++)
        {
            SpawnerConfig config = _spawnerConfigs[i];
            if (config?.SpawnEntries == null)
                continue;

            for (int entryIndex = 0; entryIndex < config.SpawnEntries.Count; entryIndex++)
            {
                SpawnerConfig.SpawnEntry entry = config.SpawnEntries[entryIndex];
                if (entry == null || string.IsNullOrWhiteSpace(entry.PrefabName))
                    continue;

                if (_configBySpecies.ContainsKey(entry.PrefabName))
                {
                    Debug.LogError(
                        $"[MonsterSpawnerManager] 物种 {entry.PrefabName} 同时存在于多个生成配置，已忽略后续配置 {config.name}。",
                        config);
                    continue;
                }

                _configBySpecies[entry.PrefabName] = config;
            }
        }
    }

    private void RebuildTrackedPopulation()
    {
        ClearTrackedPopulation();
        ItemMgr itemMgr = ItemMgr.Instance;
        if (itemMgr == null)
            return;

        foreach (Item item in itemMgr.WorldRunTimeItems.Values)
            TrackItem(item);

        _trackedPopulation = _trackedItems.Count;
    }

    private void ClearTrackedPopulation()
    {
        foreach (DamageReceiver receiver in _deathReceivers.Keys)
        {
            if (receiver != null)
                receiver.DeathStarted -= OnTrackedItemDeathStarted;
        }

        _deathReceivers.Clear();
        _receiverByItem.Clear();
        _trackedItems.Clear();
        _farAwaySince.Clear();
        _trackedPopulation = 0;
    }

    private void OnRuntimeItemInstantiated(Item item)
    {
        if (enabled)
            TrackItem(item);
    }

    private void OnRuntimeItemDespawning(Item item)
    {
        UntrackItem(item);
    }

    private void TrackItem(Item item)
    {
        if (item?.itemData == null ||
            _trackedItems.ContainsKey(item) ||
            !_configBySpecies.TryGetValue(item.itemData.IDName, out SpawnerConfig config))
        {
            return;
        }

        _trackedItems[item] = config;
        DamageReceiver receiver = item.GetComponentInChildren<DamageReceiver>(true);
        if (receiver != null && !_deathReceivers.ContainsKey(receiver))
        {
            _deathReceivers[receiver] = item;
            _receiverByItem[item] = receiver;
            receiver.DeathStarted += OnTrackedItemDeathStarted;
        }

        _trackedPopulation = _trackedItems.Count;
    }

    private void UntrackItem(Item item)
    {
        if (ReferenceEquals(item, null))
            return;

        _trackedItems.Remove(item);
        _farAwaySince.Remove(item);
        if (_receiverByItem.TryGetValue(item, out DamageReceiver receiver))
        {
            _receiverByItem.Remove(item);
            _deathReceivers.Remove(receiver);
            if (receiver != null)
                receiver.DeathStarted -= OnTrackedItemDeathStarted;
        }

        _trackedPopulation = _trackedItems.Count;
    }

    private void OnTrackedItemDeathStarted(DamageReceiver receiver)
    {
        if (receiver == null ||
            !_deathReceivers.TryGetValue(receiver, out Item item) ||
            item == null ||
            !_trackedItems.TryGetValue(item, out SpawnerConfig config))
        {
            return;
        }

        QueueDeathReplacement(config);
    }

    private void MaintainTrackedPopulation()
    {
        if (Time.unscaledTime < _nextPopulationMaintenanceTime)
            return;

        _nextPopulationMaintenanceTime = Time.unscaledTime + Mathf.Max(0.5f, _populationMaintenanceInterval);
        _itemSnapshot.Clear();
        foreach (Item item in _trackedItems.Keys)
        {
            if (item == null || item.itemData == null)
                _itemSnapshot.Add(item);
        }

        for (int i = 0; i < _itemSnapshot.Count; i++)
            UntrackItem(_itemSnapshot[i]);

        CollectPopulationOverflow(_itemSnapshot);
        for (int i = 0; i < _itemSnapshot.Count; i++)
        {
            Item item = _itemSnapshot[i];
            if (item != null && ItemMgr.Instance != null)
                ItemMgr.Instance.DespawnItem(item, saveData: false);
        }
    }

    private void CollectPopulationOverflow(List<Item> overflow)
    {
        overflow.Clear();
        var speciesCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var groupCounts = new Dictionary<SpawnerConfig, int>();
        int totalCount = 0;

        foreach (KeyValuePair<Item, SpawnerConfig> pair in _trackedItems)
        {
            Item item = pair.Key;
            SpawnerConfig config = pair.Value;
            if (item == null || item.itemData == null || config == null)
                continue;

            string speciesId = item.itemData.IDName;
            speciesCounts.TryGetValue(speciesId, out int speciesCount);
            groupCounts.TryGetValue(config, out int groupCount);
            speciesCount++;
            groupCount++;
            totalCount++;
            speciesCounts[speciesId] = speciesCount;
            groupCounts[config] = groupCount;

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

            if (totalCount > Mathf.Max(1, _globalAliveLimit) ||
                groupCount > GetEffectiveGroupLimit(config) ||
                (speciesLimit > 0 && speciesCount > speciesLimit) ||
                ExceedsNearbyPlayerLimit(item, config, overflow))
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
            if ((item.transform.position - playerPosition).sqrMagnitude > radiusSqr)
                continue;

            int nearbyCount = 0;
            foreach (KeyValuePair<Item, SpawnerConfig> pair in _trackedItems)
            {
                Item candidate = pair.Key;
                if (candidate == null || pair.Value != config || pendingOverflow.Contains(candidate))
                    continue;

                if ((candidate.transform.position - playerPosition).sqrMagnitude <= radiusSqr)
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
        float now = Time.unscaledTime;

        foreach (KeyValuePair<Item, SpawnerConfig> pair in _trackedItems)
        {
            Item item = pair.Key;
            SpawnerConfig config = pair.Value;
            if (item == null || config == null || config.RecycleDistance <= 0f)
                continue;

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
            if (item != null && ItemMgr.Instance != null)
                ItemMgr.Instance.DespawnItem(item, saveData: false);
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

        if (state.DataVersion < 1)
        {
            state.DataVersion = 1;
            state.LastProcessedTotalTime = -1f;
            state.AvailableBudget = -1;
            state.LastBudgetRecoveryDay = -1;
            state.PendingReplacementCount = 0;
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
