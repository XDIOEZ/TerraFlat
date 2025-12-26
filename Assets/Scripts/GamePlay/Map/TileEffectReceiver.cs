using System.Collections.Generic;
using System.Linq;
using UltEvents;
using UnityEngine;

/// <summary>
/// Tile效果接收器模块，用于处理物品与地图Tile的交互
/// 负责检测物品所在的Tile变化，并触发相应的进入、退出和更新事件
/// </summary>
public class TileEffectReceiver : Module
{
    #region 公共变量
    [Header("位置信息")]
    public Vector2Int lastGridPos;
    [Header("当前所处地图缓存")]
    public Map Cache_map;
    
    [Header("Tile事件")]
    public UltEvent<TileData> OnTileEnterEvent = new UltEvent<TileData>();
    public UltEvent<TileData> OnTileExitEvent = new UltEvent<TileData>();
    
    [Tooltip("当前踩着的TileData缓存，供其他模块引用")]
    public TileData currentTileData;
    #endregion

    #region 静态缓存相关
    
    // 所有实例共享的预制体缓存
    private static readonly Dictionary<string, IBlockTile> prefabCache = new Dictionary<string, IBlockTile>();
    // 缓存操作的线程安全锁
    private static readonly object cacheLock = new object();
    // 缓存清理时间戳
    private static float lastCleanupTime;
    private const float cleanupInterval = 300f; // 5分钟清理一次过期缓存
    #endregion

    #region 模块数据
    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data 
    { 
        get => ModSaveData; 
        set => ModSaveData = (Ex_ModData_MemoryPackable)value; 
    }
    #endregion

    #region 生命周期
    private void Start()
    {
        lastGridPos = GetCurrentGridPos();
        // 初始化当前TileData缓存
        currentTileData = Cache_map?.GetTile(lastGridPos);
        enabled = true;

        OnTileEnter(lastGridPos);
    }

    private void OnValidate()
    {
        // 设置模块ID
        _Data.ID = ModText.TileEffectReceiver;
    }

   public override void ModUpdate(float deltaTime)
    {
        // 定期清理缓存
        CleanupCacheIfNeeded();
        
        UpdateMapReference();
        
        Vector2Int currentGridPos = GetCurrentGridPos();
        if (currentGridPos != lastGridPos)
        {            
            // 先退出旧的Tile
            OnTileExit(lastGridPos);

            // 获取新位置所在的Chunk
            Chunk chunk;
            ChunkMgr.Instance.GetChunkBy_ItemPosition(currentGridPos, out chunk);
            
            if (chunk == null)
            {                // 踏上空白地图，不触发事件
                return;
            }
            
            Cache_map = chunk.Map;
            lastGridPos = currentGridPos;
            
            // 进入新的Tile
            OnTileEnter(currentGridPos);
        }

        // 每帧更新当前Tile状态
        OnTileUpdate(currentGridPos);
    }
    #endregion

    #region 模块接口实现
    public override void Load()
    {
        ModSaveData.ReadData(ref lastGridPos);
        InitializeMap();
    }

    public override void Save()
    {
        ModSaveData.WriteData(lastGridPos);
    }

    private void OnDestroy()
    {
        // 确保在销毁时退出当前Tile
        OnTileExit(lastGridPos);
    }
    
    public override void Act()
    {
        base.Act();
    }
    #endregion

    #region 初始化方法
    /// <summary>
    /// 初始化地图引用
    /// </summary>
    private void InitializeMap()
    {
        if (Cache_map != null) return;
        
        // 从当前位置获取Chunk和Map引用
        Chunk chunk;
        ChunkMgr.Instance.GetChunkBy_ItemPosition(transform.position, out chunk);
        
        if (chunk == null)
        {            Debug.LogWarning($"TileEffectReceiver: 未找到有效的 Chunk 组件！{(item != null ? $"对象: {item.itemData.IDName}" : "")}");
            return;
        }
        
        Cache_map = chunk.Map;
        // 如果仍未找到Map，尝试在场景中查找
        if (Cache_map == null)
        {
            Cache_map = FindFirstObjectByType<Map>();
        }

        if (Cache_map == null)
        {
            Debug.LogError("TileEffectReceiver: 未找到有效的 Map 组件！");
            enabled = false;
        }
    }
    
    /// <summary>
    /// 更新地图引用
    /// 当当前Map为空或未激活时重新获取Map引用
    /// </summary>
    private void UpdateMapReference()
    {
        if(ChunkMgr.Instance == null) return;
        
        if (Cache_map == null || !Cache_map.gameObject.activeInHierarchy)
        {
            Chunk chunk;
            ChunkMgr.Instance.GetChunkBy_ItemPosition(transform.position, out chunk);
            Cache_map = chunk?.Map;
        }
    }
    #endregion

    #region Tile事件处理
    /// <summary>
    /// 处理进入Tile的逻辑
    /// </summary>
    private void OnTileEnter(Vector2Int gridPos)
    {
        if (item == null) return;
        
        if (TryGetTileBlock(gridPos, out TileData tileData, out IBlockTile tileBlock))
        {            // 更新当前TileData缓存
            currentTileData = tileData;
            tileBlock.Tile_Enter(item, tileData);
            OnTileEnterEvent.Invoke(tileData);
        }
    }

    /// <summary>
    /// 处理离开Tile的逻辑
    /// </summary>
    private void OnTileExit(Vector2Int gridPos)
    {
        if (item == null) return;
        
        if (TryGetTileBlock(gridPos, out TileData tileData, out IBlockTile tileBlock))
        {            tileBlock.Tile_Exit(item, tileData);
            OnTileExitEvent.Invoke(tileData);
        }
    }

    /// <summary>
    /// 处理Tile更新的逻辑
    /// </summary>
    private void OnTileUpdate(Vector2Int gridPos)
    {
        if (Cache_map == null || item == null) return;
        
        if (TryGetTileBlock(gridPos, out TileData tileData, out IBlockTile tileBlock))
        {            // 更新当前TileData缓存
            currentTileData = tileData;
            tileBlock.Tile_Update(item, tileData);
        }
    }
    #endregion

    #region 缓存管理方法
    /// <summary>
    /// 定期清理无效缓存
    /// </summary>
    private static void CleanupCacheIfNeeded()
    {
        if (Time.time - lastCleanupTime < cleanupInterval) return;

        lock (cacheLock)
        {            // 清除null值缓存（可能是加载失败的预制体）
            var invalidKeys = prefabCache
                .Where(kv => kv.Value == null)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in invalidKeys)
            {
                prefabCache.Remove(key);
            }

            lastCleanupTime = Time.time;
            Debug.Log($"TileEffectReceiver: 清理了 {invalidKeys.Count} 个无效缓存");
        }
    }

    /// <summary>
    /// 手动清理所有缓存（如场景切换时调用）
    /// </summary>
    public static void ClearCache()
    {
        lock (cacheLock)
        {            prefabCache.Clear();
            Debug.Log("TileEffectReceiver: 已手动清空所有缓存");
        }
    }

    /// <summary>
    /// 移除特定预制体的缓存
    /// </summary>
    public static void RemoveFromCache(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return;
        
        lock (cacheLock)
        {            if (prefabCache.ContainsKey(itemName))
            {
                prefabCache.Remove(itemName);
            }
        }
    }
    #endregion

    #region 辅助方法
    /// <summary>
    /// 获取当前网格坐标
    /// </summary>
    private Vector2Int GetCurrentGridPos()
    {
        // 若地图为空，则返回上一次的坐标
        if (Cache_map == null) return lastGridPos;
        
        Vector3Int cell = Cache_map.tileMap.WorldToCell(transform.position);
        return new Vector2Int(cell.x, cell.y);
    }

    /// <summary>
    /// 尝试获取指定位置的Tile块和Tile数据
    /// </summary>
    private bool TryGetTileBlock(Vector2Int pos, out TileData tileData, out IBlockTile tileBlock)
    {
        tileData = null;
        tileBlock = null;
        
        // 获取Tile数据
        tileData = Cache_map?.GetTile(pos);
        if (tileData == null) return false;

        // 从缓存获取IBlockTile
        lock (cacheLock)
        {            if (prefabCache.TryGetValue(tileData.Name, out tileBlock))
            {
                return tileBlock != null;
            }
        }

        // 缓存未命中，加载预制体
        var prefab = GameRes.Instance?.GetPrefab(tileData.Name);
        if (prefab == null)
        {            Debug.LogError($"TileEffectReceiver: 找不到 Prefab: {tileData.Name}");
            return false;
        }

        var itemComp = prefab.GetComponent<Item>();
        if (itemComp is IBlockTile block)
        {            tileBlock = block;
            // 存入缓存
            lock (cacheLock)
            {                prefabCache[tileData.Name] = tileBlock;
            }
            return true;
        }
        else
        {            Debug.LogWarning($"TileEffectReceiver: Prefab 未实现 IBlockTile 接口: {tileData.Name}");
            return false;
        }
    }

    /// <summary>
    /// 获取当前Tile数据
    /// </summary>
    public TileData GetCurrentTileData()
    {
        var pos = GetCurrentGridPos();
        return Cache_map?.GetTile(pos);
    }
    #endregion
}