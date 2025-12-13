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
        
        Debug.Log($"[区块加载器] ✅ 初始化完成 - 加载距离: {LoadChunkDistance}, 失活距离: {UnActiveDistance}, 销毁距离: {DestroyChunkDistance}");
    }

    public override void Save()
    {
        ModData.WriteData(distanceConfig);
        Debug.Log("[区块加载器] 💾 配置已保存");
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
            Debug.Log($"[区块加载器] 📍 玩家跨入新区块: {lastChunkPos}");
        }
    }

    /// <summary>
    /// 更新周围区块的加载状态
    /// </summary>
    private void UpdateChunks(Vector2 chunkPos)
    {
        // 安全检查
        if (!IsComponentValid())
        {
            Debug.LogWarning("[区块加载器] ⚠️ 组件无效，跳过区块更新");
            return;
        }

        // 销毁过远的失活区块
        ChunkMgr.Instance.DestroyChunk_In_Distance(gameObject, Distance: DestroyChunkDistance);
        Debug.Log($"[区块加载器] 🗑️ 清理距离 > {DestroyChunkDistance} 的区块");

        // 将较远的区块设置为非激活状态
        ChunkMgr.Instance.SwitchActiveChunks_TO_UnActive(gameObject, Distance: UnActiveDistance);
        Debug.Log($"[区块加载器] 😴 将距离 > {UnActiveDistance} 的区块设为非激活");

        // 异步更新寻路网格和加载区块
        AstarGameManager.Instance.UpdateMeshAsync(chunkPos, LoadChunkDistance, OnMeshUpdateComplete);
        Debug.Log($"[区块加载器] 🔄 开始异步更新寻路网格，加载范围: {LoadChunkDistance}");
    }

    /// <summary>
    /// 寻路网格更新完成后的回调
    /// </summary>
    private void OnMeshUpdateComplete()
    {
        // 多层防护的安全检查
        if (!IsComponentValid())
        {
            Debug.LogWarning("[区块加载器] ⚠️ 寻路网格更新完成时，组件已无效");
            return;
        }

        if (_Data != null && !_Data.isRunning)
        {
            Debug.LogWarning("[区块加载器] ⚠️ 模块已停止运行");
            return;
        }

        // 获取自动生成地图配置
        bool shouldAutoGenerate = GetAutoGenerateMapSetting();

        // 根据配置加载区块
        int loadDistance = shouldAutoGenerate ? LoadChunkDistance : 1;
        ChunkMgr.Instance.LoadChunkCloseToPlayer(gameObject, Distance: loadDistance);
        
        Debug.Log($"[区块加载器] ✅ 异步区块加载完成 (自动生成: {shouldAutoGenerate}, 距离: {loadDistance})");
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 检查组件和游戏对象是否有效
    /// </summary>
    private bool IsComponentValid()
    {
        return this != null && gameObject != null;
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