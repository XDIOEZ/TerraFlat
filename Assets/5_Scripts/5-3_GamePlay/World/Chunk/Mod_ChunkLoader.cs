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
    private bool _adminManualLoadDistanceOverride;
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
    public int CurrentUnActiveChunkDistance => UnActiveDistance;
    public int CurrentDestroyChunkDistance => DestroyChunkDistance;
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
        lastChunkPos = ResolveChunkOrigin(transform.position);
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
        lastChunkPos = ResolveChunkOrigin(transform.position);
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
        Vector2 currentChunkPos = ResolveChunkOrigin(transform.position);
        if (currentChunkPos != lastChunkPos)
        {
            lastChunkPos = currentChunkPos;
            needsChunkUpdate = true;
        }
    }

    private void UpdateChunks(Vector2 chunkPos)
    {
        if (ChunkMgr.Instance == null) return;

        ChunkMgr.Instance.RefreshRuntimeWindow(
            chunkPos,
            LoadChunkDistance,
            DestroyChunkDistance,
            includeLocalPresentation: true);
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

    private static Vector2 ResolveChunkOrigin(Vector2 worldPosition)
    {
        if (ChunkMgr.Instance == null)
            return worldPosition;
        Vector2Int origin = ChunkMgr.Instance.ResolveRuntimeChunkOrigin(worldPosition);
        return origin;
    }

    #endregion
}
