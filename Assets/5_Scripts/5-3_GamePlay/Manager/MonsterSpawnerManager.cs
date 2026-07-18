using Sirenix.OdinInspector;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 怪物自然生成管理器
/// 负责在特定时间点（如白天12点）在玩家视野外生成怪物
/// </summary>
public class MonsterSpawnerManager : SingletonAutoMono<MonsterSpawnerManager>
{
    #region 字段

    [Header("生成配置")]
    [SerializeField, Required]
    private List<SpawnerConfig> _spawnerConfigs = new List<SpawnerConfig>(); // 怪物生成配置资产，支持多个配置（按优先级/索引配置）

    // 当前活动配置（兼容原先单个配置行为，使用第一个配置）
    private SpawnerConfig ActiveSpawnerConfig => (_spawnerConfigs != null && _spawnerConfigs.Count > 0) ? _spawnerConfigs[0] : null;

    [Header("调试")]
    [SerializeField, ReadOnly]
    private float _lastCheckTime = -1f; // 上次检查的时间，用于防止重复触发

    [SerializeField, ReadOnly]
    private bool _hasSpawnedToday = false; // 今天是否已经生成过

    [SerializeField, ReadOnly]
    private int _lastCheckedDay = -1; // 上次检查的游戏天数

    private int _lastSpawnDay = -999; // 上次成功生成的游戏日（用于间隔控制）

    // DayTimeSystem 会在进入游戏世界时由 GameManager 实例化，启动界面中尚不存在。
    private DayTimeSystem _dayTimeSystem;

    #endregion

    #region 生命周期

    private void Start()
    {
        // 初始状态禁用 Update，等玩家进入游戏世界后由事件激活
        enabled = false;

        if (ActiveSpawnerConfig == null)
        {
            Debug.LogError("[MonsterSpawnerManager] SpawnerConfig 未配置，请在检查器中指定至少一个配置资产");
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
        // RunWorld 会先实例化 TimeSystem，再触发 Event_GameWorldEnter。
        _dayTimeSystem = DayTimeSystem.Instance;
        if (_dayTimeSystem == null)
        {
            enabled = false;
            return;
        }

        enabled = true;
        Debug.Log("[MonsterSpawnerManager] 已初始化，监听游戏时间系统");
    }

    private void OnGameWorldExit()
    {
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
        CheckAndTriggerSpawn();
    }

    #endregion

    #region 核心逻辑

    /// <summary>
    /// 获取当前游戏天数
    /// </summary>
    private int GetCurrentDay(TimeData timeData)
    {
        return timeData.GetCurrentDay();
    }

    /// <summary>
    /// 检查并触发生成逻辑
    /// </summary>
    private void CheckAndTriggerSpawn()
    {
        if (ItemMgr.Instance == null || _dayTimeSystem == null)
            return;

        // 获取当前场景的时间数据
        string currentScene = ItemMgr.Instance.PlayerInSceneName;
        if (!_dayTimeSystem.WorldTimeDict.TryGetValue(currentScene, out TimeData timeData))
            return;

        // 获取当前时间（在 0-DayLength 范围内）
        float currentTimeInDay = timeData.CurrentTime % timeData.DayLength;

        // 获取当前游戏天数
        int currentDay = GetCurrentDay(timeData);

        // 如果是新的一天，重置标记
        if (currentDay != _lastCheckedDay)
        {
            _hasSpawnedToday = false;
            _lastCheckedDay = currentDay;
        }

        // 检查间隔天数（确保两次成功生成间隔足够）
        if (ActiveSpawnerConfig.DaysBetweenSpawns > 1 && (currentDay - _lastSpawnDay) < ActiveSpawnerConfig.DaysBetweenSpawns)
        {
            return; // 未到间隔天数
        }

        // 检查是否到达生成时间点
        float timeDiff = Mathf.Abs(currentTimeInDay - ActiveSpawnerConfig.SpawnTriggerTime);
        
        // 检查是否在生成时间窗口内，且今天还没生成过
        if (timeDiff <= ActiveSpawnerConfig.SpawnTimeTolerance && !_hasSpawnedToday)
        {
            // 防止在同一帧多次触发
            if (Mathf.Abs(currentTimeInDay - _lastCheckTime) > ActiveSpawnerConfig.SpawnTimeTolerance)
            {
                // 总体概率判定
                if (UnityEngine.Random.value <= ActiveSpawnerConfig.SpawnChance)
                {
                    TriggerSpawn();
                    _lastSpawnDay = currentDay;
                }
                else
                {
                    Debug.LogFormat("[MonsterSpawnerManager] {0} 未触发生成（概率未命中）", ActiveSpawnerConfig.name);
                }

                _hasSpawnedToday = true;
                _lastCheckTime = currentTimeInDay;
            }
        }
    }

    /// <summary>
    /// 触发怪物生成
    /// </summary>
    private void TriggerSpawn()
    {
        if (ActiveSpawnerConfig == null)
        {
            throw new System.NullReferenceException("[MonsterSpawnerManager] SpawnerConfig 为空，无法执行生成，请至少配置一个 SpawnerConfig");
        }

        int count = Mathf.Max(1, ActiveSpawnerConfig.SpawnCount);

        for (int i = 0; i < count; i++)
        {
            // 为每次生成选择类型
            float rv = UnityEngine.Random.value;
            string spawnType = ActiveSpawnerConfig.DetermineSpawnType(rv);

            if (string.IsNullOrEmpty(spawnType))
            {
                Debug.LogFormat("[MonsterSpawnerManager] 第{0}次生成：随机值 {1:F2}，未触发生成", i + 1, rv);
                continue;
            }

            // 获取有效的生成位置
            Vector3 spawnPosition = GetValidSpawnPosition();
            if (spawnPosition == Vector3.zero)
            {
                Debug.LogWarning("[MonsterSpawnerManager] 无法找到有效的生成位置");
                continue;
            }

            Debug.LogFormat("[MonsterSpawnerManager] 触发怪物生成：类型={0}, 随机值={1:F2}", spawnType, rv);
            SpawnMonster(spawnType, spawnPosition);
        }
    }

    #endregion

    #region 生成逻辑

    /// <summary>
    /// 获取玩家周围已加载区块内的有效生成位置
    /// 确保生成的怪物在已加载的 Chunk 内
    /// </summary>
    /// <returns>有效的生成世界坐标，失败返回 Vector3.zero</returns>
    private Vector3 GetValidSpawnPosition()
    {
        Player player = ItemMgr.Instance.User_Player;
        if (player == null)
        {
            Debug.LogError("[MonsterSpawnerManager] 玩家实例未找到");
            return Vector3.zero;
        }

        Vector3 playerPos = player.transform.position;

        // 多次尝试找到有效位置
        for (int i = 0; i < ActiveSpawnerConfig.SpawnSearchRetryCount; i++)
        {
            // 在指定距离范围内随机选择方向和距离
            float randomAngle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float randomDistance = UnityEngine.Random.Range(
                ActiveSpawnerConfig.MinSpawnDistance,
                ActiveSpawnerConfig.MaxSpawnDistance
            );

            Vector3 spawnPos = playerPos + new Vector3(
                Mathf.Cos(randomAngle) * randomDistance,
                Mathf.Sin(randomAngle) * randomDistance,
                0f
            );

            // 检查位置是否在已加载的区块内
            if (IsPositionInLoadedChunk(spawnPos))
            {
                return spawnPos;
            }
        }

        Debug.LogWarning("[MonsterSpawnerManager] 经过多次尝试仍未找到有效生成位置");
        return Vector3.zero;
    }

    /// <summary>
    /// 检查位置是否在已加载的区块内
    /// </summary>
    private bool IsPositionInLoadedChunk(Vector3 worldPos)
    {
        if (ChunkMgr.Instance == null)
            return false;

        // 获取世界坐标对应的 Chunk 位置
        Vector2Int chunkPos = Chunk.GetChunkPosition(worldPos);

        // 检查该 Chunk 是否已激活加载
        bool isLoaded = ChunkMgr.Instance.TryGetActiveChunkByPos(chunkPos, out Chunk chunk);

        return isLoaded && chunk != null;
    }

    /// <summary>
    /// 生成怪物实例
    /// </summary>
    private void SpawnMonster(string spawnType, Vector3 spawnPosition)
    {
        string currentScene = ItemMgr.Instance.PlayerInSceneName;

        try
        {
            Item spawnedItem = ItemMgr.Instance.InstantiateItem(
                spawnType,
                spawnPosition,
                Quaternion.identity,
                Vector3.one
            );

            spawnedItem.Load();

            if (spawnedItem != null)
            {
                Debug.LogFormat("[MonsterSpawnerManager] ✓ 成功生成并加载怪物：类型={0}, 位置={1}, 场景={2}", spawnType, spawnPosition.ToString("F1"), currentScene);
            }
            else
            {
                Debug.LogWarningFormat("[MonsterSpawnerManager] 生成怪物失败：类型={0}, InstantiateItem 返回 null", spawnType);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogErrorFormat("[MonsterSpawnerManager] 生成 {0} 失败: {1}", spawnType, ex.Message);
        }
    }

    #endregion

    #region 调试方法

    /// <summary>
    /// 手动触发一次生成（调试用）
    /// </summary>
    [Button("手动触发怪物生成")]
    public void DebugTriggerSpawn()
    {
        Debug.Log("[MonsterSpawnerManager] 手动触发生成");
        TriggerSpawn();
    }

    /// <summary>
    /// 重置今日生成状态（调试用）
    /// </summary>
    [Button("重置今日生成状态")]
    public void DebugResetDailyState()
    {
        _hasSpawnedToday = false;
        _lastCheckedDay = -1;
        Debug.Log("[MonsterSpawnerManager] 已重置今日生成状态");
    }

    #endregion
}
