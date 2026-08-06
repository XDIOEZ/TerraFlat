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
    private bool _adminManualLoadDistanceOverride;
    private Vector2 _queuedNavRefreshCenter;
    private int _queuedNavRefreshLoadDistance; // 排队请求的加载距离
    private int _pendingRefreshLoadDistance; // 当前刷新使用的加载距离
    private Vector2Int _pendingRefreshCenterChunk; // 当前刷新使用的区块中心坐标
    private Coroutine _delayedNavRefreshCoroutine;
    private bool _externalStreamingManaged;
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

    public int CurrentLoadChunkDistance => LoadChunkDistance;
    #endregion

    #region 生命周期方法

    private void OnEnable() => GameManager.Event_PlayerEnterWorld += OnPlayerEnterWorld;
    private void OnDisable() => GameManager.Event_PlayerEnterWorld -= OnPlayerEnterWorld;

    private void OnPlayerEnterWorld(Player player)
    {
        if (player != GetComponentInParent<Player>())
            return;
        if (_externalStreamingManaged)
            return;
        RefreshChunksAroundPlayer();
    }

    private void OnValidate()
    {
        if (_Data != null)
            _Data.ID = ModText.ChunkLoader;
    }

    public override void Load()
    {
        ModData.ReadData(ref distanceConfig);
        lastChunkPos = Chunk.GetChunkPosition(transform.position);
        _cameraFollowManager = item.GetComponentInChildren<Mod_Cam>();
    }

    public override void Save() => ModData.WriteData(distanceConfig);

    public override void ModUpdate(float deltaTime)
    {
        if (_externalStreamingManaged)
            return;

        AutoAdjustDistance();
        DetectChunkChange();

        if (needsChunkUpdate && Time.unscaledTime - _lastChunkUpdateTime >= chunkUpdateMinInterval)
        {
            needsChunkUpdate = false;
            _lastChunkUpdateTime = Time.unscaledTime;
            UpdateChunks(lastChunkPos);
        }
    }

    #endregion

    #region 外部接口

    /// <summary>
    /// 联机模式由 NetworkChunkStreamingCoordinator 按所有玩家位置联合管理区块。
    /// </summary>
    public void SetExternalStreamingManaged(bool managed)
    {
        _externalStreamingManaged = managed;
        needsChunkUpdate = false;
    }

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

    /// <summary>Immediately refreshes the canonical window after a local world wrap.</summary>
    public void RefreshAfterWorldWrap()
    {
        if (_externalStreamingManaged)
            return;

        RefreshChunksAroundPlayer();
    }

    /// <summary>
    /// 相机视野变化后立即刷新区块
    /// </summary>
    public void RefreshChunksForCameraView()
    {
        AutoAdjustDistance();
        RefreshChunksAroundPlayer();
    }

    public int IncreaseLoadDistanceForAdmin(int amount = 1)
    {
        _adminManualLoadDistanceOverride = true;
        AdjustLoadDistance(Mathf.Max(1, amount));
        RefreshChunksAroundPlayer();
        return LoadChunkDistance;
    }

    #endregion

    #region 动态视距逻辑

    private void AutoAdjustDistance()
    {
        if (!syncWithCamera) return;
        if (_adminManualLoadDistanceOverride) return;

        // 获取有效相机引用（仅用于 aspect 和回退）
        _boundCamera ??= _cameraFollowManager?.ControllerCamera ?? Camera.main;
        if (_boundCamera == null || !_boundCamera.orthographic) return;

        // 关键修复：使用 Mod_Cam 的目标 lens size，避免 Cinemachine 同步延迟导致读取到旧的 orthographicSize
        float camSize = _cameraFollowManager?.CurrentOrthographicSize ?? _boundCamera.orthographicSize;
        if (camSize <= 0f) return;

        // 计算视野半径（取半宽/半高中的较大者）
        float radius = Mathf.Max(camSize * _boundCamera.aspect, camSize);

        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        float minChunkDim = Mathf.Min(chunkSize.x, chunkSize.y);
        if (minChunkDim <= 0.1f) return;

        int neededDist = Mathf.Clamp(Mathf.CeilToInt(radius / minChunkDim) + chunkBuffer, 1, maxAutoLoadDistance);
        if (neededDist != LoadChunkDistance)
            AdjustLoadDistance(neededDist - LoadChunkDistance);
    }

    #endregion

    #region 区块检测与更新

    private void DetectChunkChange()
    {
        Vector2 currentChunkPos = Chunk.GetChunkPosition(transform.position);
        if (currentChunkPos != lastChunkPos)
        {
            lastChunkPos = currentChunkPos;
            needsChunkUpdate = true;
        }
    }

    private void UpdateChunks(Vector2 chunkPos)
    {
        if (ChunkMgr.Instance == null) return;

        // 保存当前的加载距离，避免回调触发时 LoadChunkDistance 已被修改
        int currentLoadDistance = LoadChunkDistance;
        // 将 Vector2 center 转为 Vector2Int 区块坐标，确保权重烘焙使用固定中心
        Vector2Int centerChunkPos = new Vector2Int(Mathf.RoundToInt(chunkPos.x), Mathf.RoundToInt(chunkPos.y));

        if (WorldNavigationManager.Instance?.EnableDebugLogs == true) Debug.Log($"[WorldNav][ChunkLoader] UpdateChunks | chunkPos={chunkPos} centerChunkPos={centerChunkPos} LoadDistance={currentLoadDistance} UnActiveDistance={UnActiveDistance} DestroyDistance={DestroyChunkDistance}");

        // 停止上一轮保底协程
        if (_delayedNavRefreshCoroutine != null)
        {
            StopCoroutine(_delayedNavRefreshCoroutine);
            _delayedNavRefreshCoroutine = null;
        }

        ChunkMgr.Instance.ResetChunkLoadQueue();
        ChunkMgr.Instance.DestroyChunk_In_Distance(gameObject, DestroyChunkDistance);
        ChunkMgr.Instance.SwitchActiveChunks_TO_UnActive(gameObject, UnActiveDistance);
        ChunkMgr.Instance.LoadChunkCloseToPlayer(gameObject, currentLoadDistance, () =>
        {
            if (WorldNavigationManager.Instance == null)
            {
                Debug.LogError("[区块加载器] WorldNavigationManager 未初始化", this);
                return;
            }
            if (WorldNavigationManager.Instance.EnableDebugLogs) Debug.Log($"[WorldNav][ChunkLoader] 所有区块加载完成，刷新导航 | center={chunkPos} centerChunkPos={centerChunkPos} currentLoadDistance={currentLoadDistance}");
            _pendingRefreshLoadDistance = currentLoadDistance;
            _pendingRefreshCenterChunk = centerChunkPos;
            RequestNavMeshRefresh(chunkPos);
        });

        // 保底：即使回调因竞态未触发，也确保权重烘焙管线启动
        if (!_isNavRefreshRunning)
        {
            _delayedNavRefreshCoroutine = StartCoroutine(DelayedNavMeshRefreshCoroutine(chunkPos, currentLoadDistance, centerChunkPos));
        }
    }

    /// <summary>
    /// 保底协程：等待区块加载就绪后触发NavMesh刷新和权重烘焙。
    /// 如果回调已触发NavMesh刷新，此协程会被 _isNavRefreshRunning 标志跳过。
    /// </summary>
    private System.Collections.IEnumerator DelayedNavMeshRefreshCoroutine(Vector2 center, int loadDistance, Vector2Int centerChunkPos)
    {
        // 等待所有待加载区块处理完毕，避免在区块未激活时就触发烘焙
        if (ChunkMgr.Instance != null)
        {
            int waitFrames = 0;
            while (ChunkMgr.Instance.HasPendingChunkLoads && waitFrames < 120)
            {
                waitFrames++;
                yield return null;
            }
        }

        // 额外等待 2 帧确保区块激活状态同步
        yield return null;
        yield return null;

        _delayedNavRefreshCoroutine = null;

        // 如果回调链已触发NavMesh刷新，则跳过
        if (_isNavRefreshRunning)
        {
            if (WorldNavigationManager.Instance?.EnableDebugLogs == true) Debug.Log($"[WorldNav][ChunkLoader] 导航已在刷新，跳过保底 | center={center}");
            yield break;
        }

        if (WorldNavigationManager.Instance == null)
        {
            Debug.LogError("[区块加载器] WorldNavigationManager 未初始化", this);
            yield break;
        }

        if (WorldNavigationManager.Instance?.EnableDebugLogs == true) Debug.Log($"[WorldNav][ChunkLoader] 保底刷新 | center={center} loadDistance={loadDistance} centerChunkPos={centerChunkPos}");
        _pendingRefreshLoadDistance = loadDistance;
        _pendingRefreshCenterChunk = centerChunkPos;
        RequestNavMeshRefresh(center);
    }

    private void RequestNavMeshRefresh(Vector2 center)
    {
        if (WorldNavigationManager.Instance?.EnableDebugLogs == true) Debug.Log($"[WorldNav][ChunkLoader] RequestNavigationRefresh | center={center} running={_isNavRefreshRunning} LoadDistance={LoadChunkDistance}");

        if (_isNavRefreshRunning)
        {
            _hasQueuedNavRefresh = true;
            _queuedNavRefreshCenter = center;
            _queuedNavRefreshLoadDistance = LoadChunkDistance; // 保存排队请求的距离
            if (WorldNavigationManager.Instance?.EnableDebugLogs == true) Debug.Log($"[WorldNav][ChunkLoader] 刷新运行中，合并后续请求 | queuedCenter={center} queuedLoadDistance={_queuedNavRefreshLoadDistance}");
            return;
        }

        _isNavRefreshRunning = true;
        _pendingRefreshLoadDistance = LoadChunkDistance; // 保存当前刷新使用的距离

        WorldNavigationManager navigation = WorldNavigationManager.Instance;
        if (navigation == null)
        {
            _isNavRefreshRunning = false;
            return;
        }

        if (navigation.EnableDebugLogs) Debug.Log($"[WorldNav][ChunkLoader] 注册已加载区块 | pendingLoadDistance={_pendingRefreshLoadDistance}");
        navigation.RefreshLoadedRegion(center, _pendingRefreshLoadDistance, OnMeshUpdateComplete);
    }

    private void OnMeshUpdateComplete()
    {
        _isNavRefreshRunning = false;
        int completedLoadDistance = _pendingRefreshLoadDistance;
        Vector2Int completedCenterChunk = _pendingRefreshCenterChunk;
        if (WorldNavigationManager.Instance?.EnableDebugLogs == true) Debug.Log($"[WorldNav][ChunkLoader] 导航刷新完成 | queued={_hasQueuedNavRefresh} completedLoadDistance={completedLoadDistance} completedCenterChunk={completedCenterChunk}");

        if (_hasQueuedNavRefresh)
        {
            _hasQueuedNavRefresh = false;
            _pendingRefreshLoadDistance = _queuedNavRefreshLoadDistance;
            // 排队请求的中心坐标需要重新计算（排队请求来自 RefreshChunksAroundPlayer，使用当时的 lastChunkPos）
            _pendingRefreshCenterChunk = new Vector2Int(Mathf.RoundToInt(_queuedNavRefreshCenter.x), Mathf.RoundToInt(_queuedNavRefreshCenter.y));
            RequestNavMeshRefresh(_queuedNavRefreshCenter);
            return;
        }

        if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
            Debug.Log($"[WorldNav] 增量导航更新完成 | centerChunk={completedCenterChunk} LoadDistance={completedLoadDistance}");
    }

    #endregion

    #region 工具方法

    [Button("调整加载距离")]
    public void AdjustLoadDistance(int adjustment)
    {
        distanceConfig.UnActiveDistance = Mathf.Max(1, distanceConfig.UnActiveDistance + adjustment);
        distanceConfig.DestroyChunkDistance = Mathf.Max(1, distanceConfig.DestroyChunkDistance + adjustment);
        distanceConfig.LoadChunkDistance = Mathf.Max(1, distanceConfig.LoadChunkDistance + adjustment);
        needsChunkUpdate = true;
    }

    private bool GetAutoGenerateMapSetting()
    {
        const bool defaultAutoGenerate = true;
        if (SaveDataMgr.Instance?.SaveData?.CurrentPlanetData == null)
        {
            Debug.LogWarning("[区块加载器] SaveDataMgr 未初始化，使用默认设置");
            return defaultAutoGenerate;
        }
        return SaveDataMgr.Instance.SaveData.CurrentPlanetData.AutoGenerateMap;
    }

    #endregion
}
