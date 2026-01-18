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
    #endregion

    #region 运行时字段
    [Header("区块加载器运行时字段")]
    [ShowInInspector]
    private Vector2 lastChunkPos;

    /// <summary>
    /// 是否需要更新区块
    /// </summary>
    private bool needsChunkUpdate = false;
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
    }

    public override void Save()
    {
        ModData.WriteData(distanceConfig);
    }

    public override void ModUpdate(float deltaTime)
    {
        // 检测位置是否跨区块
        DetectChunkChange();

        // 延迟执行区块更新（避免频繁调用）
        if (needsChunkUpdate)
        {
            needsChunkUpdate = false;
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
        // 销毁过远的失活区块
        ChunkMgr.Instance.DestroyChunk_In_Distance(gameObject, Distance: DestroyChunkDistance);

        // 将较远的区块设置为非激活状态
        ChunkMgr.Instance.SwitchActiveChunks_TO_UnActive(gameObject, Distance: UnActiveDistance);


        ChunkMgr.Instance.LoadChunkCloseToPlayer(gameObject, Distance: LoadChunkDistance);

        //调整Apath寻路网格覆盖范围(不需要扫描,只需要调整范围就可以了,网格的具体参数在加载区块后其会自动更新)
        if (AstarGameManager.Instance == null)
        {
            Debug.LogError("[区块加载器] AstarGameManager 未初始化，无法刷新寻路网格", this);
            return;
        }

        AstarGameManager.Instance.RefreshNavMeshAsync(center: chunkPos, radius: LoadChunkDistance, onComplete: OnMeshUpdateComplete);

    }

    /// <summary>
    /// 寻路网格更新完成后的回调 , 开始加载区块更新权重
    /// </summary>
    private void OnMeshUpdateComplete()
    {
        // 寻路网格范围调整/扫描完成后，再根据玩家位置重烘焙附近区块的权重
        ChunkMgr.Instance.RefreshChunkPenaltyCloseToPlayer(gameObject, LoadChunkDistance);
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

        UpdateChunks(lastChunkPos);
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