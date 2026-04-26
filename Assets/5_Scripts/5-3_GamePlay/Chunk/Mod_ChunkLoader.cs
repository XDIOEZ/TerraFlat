using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// 区块加载器模块
/// 根据玩家位置动态加载、卸载和管理区块
/// </summary>
public class Mod_ChunkLoader : Module
{
    #region 数据结构
    /// <summary>
    /// 区块加载距离配置
    /// </summary>
    [System.Serializable]
    public struct ChunkDistanceConfig
    {
        [Tooltip("区块失活距离（超过此距离的区块将被设为非激活）")]
        public int UnActiveDistance;

        [Tooltip("区块销毁距离（超过此距离的区块将被销毁）")]
        public int DestroyChunkDistance;

        [Tooltip("区块加载距离（此距离内的区块将被加载）")]
        public int LoadChunkDistance;

        public ChunkDistanceConfig(int unActive = 2, int destroy = 3, int load = 1)
        {
            UnActiveDistance = unActive;
            DestroyChunkDistance = destroy;
            LoadChunkDistance = load;
        }
    }
    #endregion

    #region 序列化字段
    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }

    [Header("区块加载距离设置")]
    [SerializeField]
    private ChunkDistanceConfig distanceConfig = new ChunkDistanceConfig(2, 3, 1);

    [Header("动态视距同步")]
    [Tooltip("是否跟随相机视口自动调整加载范围")]
    [SerializeField] private bool syncWithCamera = true;
    [Tooltip("在视口范围外额外加载的Chunk圈数以防止穿帮")]
    [SerializeField] private int chunkBuffer = 1;
    [Tooltip("自动视距允许的最大Chunk圈数，防止相机缩放过大时瞬间加载过多区块")]
    [SerializeField, Min(1)] private int maxAutoLoadDistance = 6;

    [Header("性能节流")]
    [Tooltip("区块更新最小间隔（秒），防止高速移动时连续触发重计算")]
    [SerializeField, Min(0.01f)] private float chunkUpdateMinInterval = 0.08f;
    #endregion

    #region 运行时字段
    [Header("区块加载器运行时字段")]
    [ShowInInspector]
    private Vector2 lastChunkPos;

    /// <summary>
    /// 是否需要更新区块
    /// </summary>
    private bool needsChunkUpdate = false;
    private float _lastChunkUpdateTime = -999f;
    
    // 动态视距引用
    private Camera _boundCamera;
    private Mod_Cam _cameraFollowManager;
    private bool _isNavRefreshRunning;
    private bool _hasQueuedNavRefresh;
    private Vector2 _queuedNavRefreshCenter;
    #endregion

    #region 属性访问器
    private int UnActiveDistance
    {
        get => distanceConfig.UnActiveDistance;
        set => distanceConfig.UnActiveDistance = value;
    }

    private int DestroyChunkDistance
    {
        get => distanceConfig.DestroyChunkDistance;
        set => distanceConfig.DestroyChunkDistance = value;
    }

    private int LoadChunkDistance
    {
        get => distanceConfig.LoadChunkDistance;
        set => distanceConfig.LoadChunkDistance = value;
    }
    #endregion

    #region 生命周期方法

    private void OnEnable()
    {
        GameManager.Event_PlayerEnterWorld += OnPlayerEnterWorld;
    }

    private void OnDisable()
    {
        GameManager.Event_PlayerEnterWorld -= OnPlayerEnterWorld;
    }

    private void OnPlayerEnterWorld(Player player)
    {
        var owner = GetComponentInParent<Player>();
        if (player != owner) return;

        RefreshChunksAroundPlayer();
    }

    private void OnValidate()
    {
        if (_Data != null)
            _Data.ID = ModText.ChunkLoader;
    }

    public override void Load()
    {
        // 从存档读取配置
        ModData.ReadData(ref distanceConfig);

        // 初始化上一次区块位置
        lastChunkPos = Chunk.GetChunkPosition(transform.position);

        // 尝试获取相机管理器引用
        _cameraFollowManager = item.GetComponentInChildren<Mod_Cam>();
    }

    public override void Save()
    {
        ModData.WriteData(distanceConfig);
    }

    public override void ModUpdate(float deltaTime)
    {
        // 动态同步视距
        AutoAdjustDistance();

        // 检测位置是否跨区块
        DetectChunkChange();

        // 延迟执行区块更新（避免频繁调用）
        if (needsChunkUpdate)
        {
            if (Time.unscaledTime - _lastChunkUpdateTime < chunkUpdateMinInterval)
            {
                return;
            }

            needsChunkUpdate = false;
            _lastChunkUpdateTime = Time.unscaledTime;
            UpdateChunks(lastChunkPos);
        }
    }

    #endregion

    #region 外部接口

    [Button("刷新周围区块")]
    public void RefreshChunksAroundPlayer()
    {
        lastChunkPos = Chunk.GetChunkPosition(transform.position);
        needsChunkUpdate = false;

        if (ChunkMgr.Instance == null)
        {
            Debug.LogError("[区块加载器] ChunkMgr 未初始化，无法刷新区块", this);
            return;
        }

        UpdateChunks(lastChunkPos);
    }

    /// <summary>
    /// 相机视野变化后立即刷新区块
    /// </summary>
    public void RefreshChunksForCameraView()
    {
        AutoAdjustDistance();
        RefreshChunksAroundPlayer();
    }

    #endregion

    #region 动态视距逻辑

    /// <summary>
    /// 根据相机视野自动调整加载距离
    /// </summary>
    private void AutoAdjustDistance()
    {
        if (!syncWithCamera) return;

        // 1. 获取有效相机引用
        if (_boundCamera == null)
        {
            if (_cameraFollowManager == null)
                _cameraFollowManager = item.GetComponentInChildren<Mod_Cam>();

            if (_cameraFollowManager != null)
                _boundCamera = _cameraFollowManager.ControllerCamera;

            // 备用方案：主相机
            if (_boundCamera == null)
                _boundCamera = Camera.main;
        }

        // 相机可能被销毁或未初始化，或非正交相机
        if (_boundCamera == null || !_boundCamera.orthographic) return;

        // 2. 计算视野半径 (OrthographicSize是半高)
        float camSize = _boundCamera.orthographicSize;
        float height = camSize * 2f;
        float width = height * _boundCamera.aspect;
        float radius = Mathf.Max(width, height) / 2f;

        // 3. 计算所需Chunk距离
        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        // 取较小边以确保覆盖最坏情况，或取ChunkSize
        float minChunkDim = Mathf.Min(chunkSize.x, chunkSize.y);
        
        // 防止除零
        if (minChunkDim <= 0.1f) return;

        // 向上取整并增加缓冲区
        int neededDist = Mathf.CeilToInt(radius / minChunkDim) + chunkBuffer;
        neededDist = Mathf.Max(1, neededDist);
        neededDist = Mathf.Min(maxAutoLoadDistance, neededDist);

        // 4. 应用变更 (仅当需要改变时)
        if (neededDist != LoadChunkDistance)
        {
            int diff = neededDist - LoadChunkDistance;

            // 使用统一的方法进行调整
            AdjustLoadDistance(diff);
        }
    }

    #endregion

    #region 区块检测与更新

    /// <summary>
    /// 检测是否跨区块
    /// </summary>
    private void DetectChunkChange()
    {
        Vector2 currentChunkPos = Chunk.GetChunkPosition(transform.position);

        if (currentChunkPos != lastChunkPos)
        {
            lastChunkPos = currentChunkPos;
            needsChunkUpdate = true;
        }
    }

    /// <summary>
    /// 更新周围区块的加载状态
    /// </summary>
    private void UpdateChunks(Vector2 chunkPos)
    {
        // 先清空上一轮尚未处理完的加载请求，避免高速移动时旧位置的 Chunk 迟到加载并残留
        ChunkMgr.Instance.ResetChunkLoadQueue();

        // 销毁过远的失活区块
        ChunkMgr.Instance.DestroyChunk_In_Distance(gameObject, Distance: DestroyChunkDistance);

        // 将较远的区块设置为非激活状态
        ChunkMgr.Instance.SwitchActiveChunks_TO_UnActive(gameObject, Distance: UnActiveDistance);


        // 先完成区块加载，再刷新寻路范围，避免“区块已出现但网格/权重仍是旧状态”
        ChunkMgr.Instance.LoadChunkCloseToPlayer(gameObject, Distance: LoadChunkDistance, onAllChunksLoaded: () =>
        {
            // 调整Apath寻路网格覆盖范围（不需要扫描，只需调整范围）
            if (AstarGameManager.Instance == null)
            {
                Debug.LogError("[区块加载器] AstarGameManager 未初始化，无法刷新寻路网格", this);
                return;
            }

            RequestNavMeshRefresh(chunkPos);
        });

    }

    private void RequestNavMeshRefresh(Vector2 center)
    {
        if (_isNavRefreshRunning)
        {
            _hasQueuedNavRefresh = true;
            _queuedNavRefreshCenter = center;
            return;
        }

        _isNavRefreshRunning = true;
        AstarGameManager.Instance.RefreshNavMeshAsync(center: center, radius: LoadChunkDistance, onComplete: OnMeshUpdateComplete);
    }

    /// <summary>
    /// 寻路网格更新完成后的回调 , 开始加载区块更新权重
    /// </summary>
    private void OnMeshUpdateComplete()
    {
        _isNavRefreshRunning = false;

        // 寻路网格范围调整/扫描完成后，再根据玩家位置重烘焙附近区块的权重
        ChunkMgr.Instance.RefreshChunkPenaltyCloseToPlayer(gameObject, LoadChunkDistance);

        if (_hasQueuedNavRefresh)
        {
            _hasQueuedNavRefresh = false;
            RequestNavMeshRefresh(_queuedNavRefreshCenter);
        }
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 统一调整所有加载距离参数
    /// </summary>
    /// <param name="adjustment">调整值（正数增加范围，负数减少范围）</param>
    [Button("调整加载距离")]
    public void AdjustLoadDistance(int adjustment)
    {
        distanceConfig.UnActiveDistance = Mathf.Max(1, distanceConfig.UnActiveDistance + adjustment);
        distanceConfig.DestroyChunkDistance = Mathf.Max(1, distanceConfig.DestroyChunkDistance + adjustment);
        distanceConfig.LoadChunkDistance = Mathf.Max(1, distanceConfig.LoadChunkDistance + adjustment);

        // 调整完毕后立即更新区块
        needsChunkUpdate = true;
    }


    /// <summary>
    /// 获取是否自动生成地图的设置
    /// </summary>
    private bool GetAutoGenerateMapSetting()
    {
        // 默认启用自动生成
        const bool defaultAutoGenerate = true;

        // 多层防护null检查
        if (SaveDataMgr.Instance == null || SaveDataMgr.Instance.SaveData == null)
        {
            Debug.LogWarning("[区块加载器] ⚠️ SaveDataMgr 未初始化，使用默认自动生成设置");
            return defaultAutoGenerate;
        }

        PlanetData currentPlanetData = SaveDataMgr.Instance.SaveData.CurrentPlanetData;
        if (currentPlanetData == null)
        {
            Debug.LogWarning("[区块加载器] ⚠️ CurrentPlanetData 为空，使用默认自动生成设置");
            return defaultAutoGenerate;
        }

        return currentPlanetData.AutoGenerateMap;
    }

    #endregion
}
