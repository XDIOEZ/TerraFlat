using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Timed world-creature spawning. Runtime progress is stored in GameSaveData so
/// reloading a save cannot replay an already processed spawn window or milestone.
/// </summary>
public class MonsterSpawnerManager : SingletonAutoMono<MonsterSpawnerManager>
{
    [Header("生成配置")]
    [SerializeField, Required]
    private List<SpawnerConfig> _spawnerConfigs = new();

    [Header("调试")]
    [SerializeField, ReadOnly]
    private int _activeAsyncSpawnJobs;

    private DayTimeSystem _dayTimeSystem;
    private Dictionary<string, SpawnerProgressSaveData> _runtimeStates = new();
    private readonly Dictionary<string, Coroutine> _asyncSpawnCoroutines = new();

    private void Start()
    {
        enabled = false;

        if (_spawnerConfigs == null || _spawnerConfigs.Count == 0)
        {
            Debug.LogError("[MonsterSpawnerManager] 至少需要一个 SpawnerConfig。", this);
            return;
        }

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
        enabled = true;
    }

    private void OnGameWorldExit()
    {
        CaptureSaveData(SaveDataMgr.Instance?.SaveData);
        StopAllCoroutines();
        _asyncSpawnCoroutines.Clear();
        _activeAsyncSpawnJobs = 0;
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

        base.OnDestroy();
    }

    private void Update()
    {
        if (!TryGetCurrentTimeData(out string sceneName, out TimeData timeData))
            return;

        int currentDay = timeData.GetCurrentDay();
        for (int i = 0; i < _spawnerConfigs.Count; i++)
        {
            SpawnerConfig config = _spawnerConfigs[i];
            if (config == null)
                continue;

            SpawnerProgressSaveData state = GetOrCreateState(config);
            if (config.ScheduleMode == SpawnerScheduleMode.DayMilestoneGrowth)
                ProcessMilestoneGrowth(config, state, sceneName, currentDay);
            else
                ProcessTimedWindows(config, state, sceneName, timeData, currentDay);
        }
    }

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

    private bool TryGetCurrentTimeData(out string sceneName, out TimeData timeData)
    {
        sceneName = null;
        timeData = null;

        if (ItemMgr.Instance == null ||
            ItemMgr.Instance.UserPlayerTransform == null ||
            _dayTimeSystem == null)
        {
            return false;
        }

        sceneName = ItemMgr.Instance.PlayerInSceneName;
        return !string.IsNullOrEmpty(sceneName) &&
               _dayTimeSystem.WorldTimeDict.TryGetValue(sceneName, out timeData);
    }

    private void ProcessTimedWindows(
        SpawnerConfig config,
        SpawnerProgressSaveData state,
        string sceneName,
        TimeData timeData,
        int currentDay)
    {
        if (state.LastCheckedDay != currentDay)
        {
            state.TriggeredWindowIndices.Clear();
            state.LastCheckedDay = currentDay;
        }

        if (config.RequireGlobalDarkness && !IsGlobalDark(sceneName))
            return;

        if (config.DaysBetweenSpawns > 1 &&
            currentDay - state.LastSpawnDay < config.DaysBetweenSpawns)
        {
            return;
        }

        int spawnsPerDay = Mathf.Max(1, config.SpawnsPerDay);
        float dayLength = Mathf.Max(1f, timeData.DayLength);
        float interval = dayLength / spawnsPerDay;
        float currentTimeInDay = Mathf.Repeat(timeData.CurrentTime, dayLength);
        float firstTriggerTime = Mathf.Repeat(config.SpawnTriggerTime, dayLength);

        for (int windowIndex = 0; windowIndex < spawnsPerDay; windowIndex++)
        {
            if (state.TriggeredWindowIndices.Contains(windowIndex))
                continue;

            float triggerTime = Mathf.Repeat(firstTriggerTime + interval * windowIndex, dayLength);
            if (Mathf.Abs(currentTimeInDay - triggerTime) > config.SpawnTimeTolerance)
                continue;

            if (Random.value <= config.SpawnChance)
            {
                int spawnedCount = TriggerSpawnImmediate(config, Mathf.Max(1, config.SpawnCount));
                if (spawnedCount > 0)
                    state.LastSpawnDay = currentDay;
            }

            state.TriggeredWindowIndices.Add(windowIndex);
            break;
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
        int limit = Mathf.Clamp(config.MaxLifetimeSpawnCount, 1, 64);
        int targetScheduledCount = Mathf.Min(limit, dayNumber / growthInterval);
        int alreadyScheduled = Mathf.Max(0, state.LifetimeSpawnCount) +
                               Mathf.Max(0, state.PendingSpawnCount);

        if (targetScheduledCount > alreadyScheduled)
            state.PendingSpawnCount += targetScheduledCount - alreadyScheduled;

        state.ProcessedGrowthMilestones =
            Mathf.Max(state.ProcessedGrowthMilestones, targetScheduledCount);
        state.PendingSpawnCount = Mathf.Clamp(
            state.PendingSpawnCount,
            0,
            Mathf.Max(0, limit - state.LifetimeSpawnCount));

        if (state.PendingSpawnCount <= 0 ||
            (config.RequireGlobalDarkness && !IsGlobalDark(sceneName)))
        {
            return;
        }

        string key = GetConfigKey(config);
        if (_asyncSpawnCoroutines.ContainsKey(key))
            return;

        _asyncSpawnCoroutines[key] = StartCoroutine(
            ProcessPendingSpawnsAsync(key, config, state, sceneName));
        _activeAsyncSpawnJobs = _asyncSpawnCoroutines.Count;
    }

    private IEnumerator ProcessPendingSpawnsAsync(
        string key,
        SpawnerConfig config,
        SpawnerProgressSaveData state,
        string sceneName)
    {
        while (enabled && state.PendingSpawnCount > 0)
        {
            if (config.RequireGlobalDarkness && !IsGlobalDark(sceneName))
                break;

            bool spawned = TrySpawnOne(config);
            if (spawned)
            {
                state.PendingSpawnCount--;
                state.LifetimeSpawnCount++;

                if (config.AsyncSpawnInterval <= 0f)
                    yield return null;
                else
                    yield return new WaitForSeconds(config.AsyncSpawnInterval);
            }
            else
            {
                // Loaded chunks or dark cells may not be ready yet. Keep the
                // pending count persisted and retry without blocking a frame.
                yield return new WaitForSeconds(0.5f);
            }
        }

        _asyncSpawnCoroutines.Remove(key);
        _activeAsyncSpawnJobs = _asyncSpawnCoroutines.Count;
    }

    private int TriggerSpawnImmediate(SpawnerConfig config, int count)
    {
        int spawnedCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (TrySpawnOne(config))
                spawnedCount++;
        }

        return spawnedCount;
    }

    private bool TrySpawnOne(SpawnerConfig config)
    {
        string spawnType = config.DetermineSpawnType(Random.value);
        if (string.IsNullOrWhiteSpace(spawnType))
            return false;

        if (!TryGetValidSpawnPosition(config, out Vector3 spawnPosition))
            return false;

        return SpawnMonster(spawnType, spawnPosition);
    }

    private bool TryGetValidSpawnPosition(SpawnerConfig config, out Vector3 spawnPosition)
    {
        spawnPosition = default;
        Transform playerTransform = ItemMgr.Instance?.UserPlayerTransform;
        if (playerTransform == null)
            return false;

        Vector3 playerPos = playerTransform.position;
        int retries = Mathf.Max(1, config.SpawnSearchRetryCount);
        for (int i = 0; i < retries; i++)
        {
            float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float randomDistance = Random.Range(
                Mathf.Max(0f, config.MinSpawnDistance),
                Mathf.Max(config.MinSpawnDistance, config.MaxSpawnDistance));

            Vector3 candidate = playerPos + new Vector3(
                Mathf.Cos(randomAngle) * randomDistance,
                Mathf.Sin(randomAngle) * randomDistance,
                0f);
            candidate.x = Mathf.Floor(candidate.x) + 0.5f;
            candidate.y = Mathf.Floor(candidate.y) + 0.5f;

            if (!IsPositionInLoadedChunk(candidate))
                continue;

            if (!IsWalkableSpawnPosition(candidate))
                continue;

            if (config.RequireCompletelyDarkTile &&
                (LightLayerMgr.Instance == null ||
                 !LightLayerMgr.Instance.IsCompletelyDark(candidate)))
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

    private static bool IsPositionInLoadedChunk(Vector3 worldPos)
    {
        if (ChunkMgr.Instance == null)
            return false;

        Vector2Int chunkPos = Chunk.GetChunkPosition(worldPos);
        return ChunkMgr.Instance.TryGetActiveChunkByPos(chunkPos, out Chunk chunk) &&
               chunk != null;
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

        TriggerSpawnImmediate(_spawnerConfigs[0], Mathf.Max(1, _spawnerConfigs[0].SpawnCount));
    }

    [Button("调试：幽灵待生成数量+1")]
    public void DebugQueueOneGrowthSpawn()
    {
        SpawnerConfig config = _spawnerConfigs?.Find(
            value => value != null && value.ScheduleMode == SpawnerScheduleMode.DayMilestoneGrowth);
        if (config == null)
            return;

        SpawnerProgressSaveData state = GetOrCreateState(config);
        int limit = Mathf.Clamp(config.MaxLifetimeSpawnCount, 1, 64);
        if (state.LifetimeSpawnCount + state.PendingSpawnCount < limit)
            state.PendingSpawnCount++;
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

        if (!TrySpawnOne(config))
        {
            Debug.LogWarning("[MonsterSpawnerManager] 幽灵生成失败，请确认玩家附近存在已加载、可行走的完全黑暗格。", this);
        }
    }
}
