using System.Collections.Generic;
using System.Linq;
using UltEvents;
using UnityEngine;

/// <summary>
/// Tile效果接收器模块，用于处理物品与地图Tile的交互
/// </summary>
public class TileEffectReceiver : Module
{
    #region 公共变量
    [Header("位置信息")]
    public Vector2Int lastGridPos;
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
        InitializeMap();
        InitializeItem();
        UpdateMapReference();
        lastGridPos = GetCurrentGridPos();
        // 初始化当前TileData缓存
        currentTileData = Cache_map?.GetTile(lastGridPos);
        enabled = true;

        OnTileEnter(lastGridPos);
    }

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.TileEffect;
        }
    }

    private void Update()
    {
        UpdateMapReference();
        
        Vector2Int currentGridPos = GetCurrentGridPos();
        if (currentGridPos != lastGridPos)
        {
            OnTileExit(lastGridPos);

            ChunkMgr.Instance.GetChunkByItemPosition(currentGridPos, out Chunk chunk);
            if (chunk == null)
            {
                // 踏上空白地图，不触发事件
                return;
            }
            Cache_map = chunk.Map;

            lastGridPos = currentGridPos;
            OnTileEnter(currentGridPos);
        }

        OnTileUpdate(currentGridPos);
    }
    #endregion

    #region 模块接口实现
    public override void Load()
    {
        ModSaveData.ReadData(ref lastGridPos);
    }

    public override void Save()
    {
        OnTileExit(lastGridPos);
        ModSaveData.WriteData(lastGridPos);
    }
    
    public override void ModUpdate(float deltaTime)
    {
        // 暂时没有需要每帧更新的逻辑
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
        if (Cache_map == null)
        {
            ChunkMgr.Instance.GetChunkByItemPosition(transform.position, out Chunk chunk);
            Cache_map = chunk?.Map;
        }

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
    /// 初始化物品引用
    /// </summary>
    private void InitializeItem()
    {
        if (item == null)
        {
            item = GetComponentInParent<Item>();
        }
    }
    
    /// <summary>
    /// 更新地图引用
    /// </summary>
    private void UpdateMapReference()
    {
        if (Cache_map == null || !Cache_map.gameObject.activeInHierarchy)
        {
            ChunkMgr.Instance.GetChunkByItemPosition(transform.position, out Chunk chunk);
            Cache_map = chunk?.Map;
        }
    }
    #endregion

    #region Tile事件处理
    private void OnTileEnter(Vector2Int gridPos)
    {
        if (item == null) return;
        if (TryGetTileBlock(gridPos, out var tileData, out var tileBlock))
        {
            if (tileBlock == null)
            {
                Debug.LogError("TileEffectReceiver: TileBlock为空！");
                return;
            }
            // 更新当前TileData缓存
            currentTileData = tileData;
            tileBlock.Tile_Enter(item, tileData);
            OnTileEnterEvent.Invoke(tileData);
        }
    }

    private void OnTileExit(Vector2Int gridPos)
    {
        if (item == null) return;
        if (TryGetTileBlock(gridPos, out var tileData, out var tileBlock))
        {
            if(tileBlock == null)
            {
                Debug.LogError("TileEffectReceiver: TileBlock为空！");
                return;
            }
            tileBlock.Tile_Exit(item, tileData);
            OnTileExitEvent.Invoke(tileData);
        }
    }

    private void OnTileUpdate(Vector2Int gridPos)
    {
        if (Cache_map == null || item == null) return;
        if (TryGetTileBlock(gridPos, out var tileData, out var tileBlock))
        {
            if (tileBlock == null)
            {
                Debug.LogError("TileEffectReceiver: TileBlock为空！");
                return;
            }
            // 更新当前TileData缓存
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
        {
            // 清除null值缓存（可能是加载失败的预制体）
            var invalidKeys = prefabCache.Where(kv => kv.Value == null)
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
        {
            prefabCache.Clear();
            Debug.Log("TileEffectReceiver: 已手动清空所有缓存");
        }
    }

    /// <summary>
    /// 移除特定预制体的缓存
    /// </summary>
    public static void RemoveFromCache(string itemName)
    {
        lock (cacheLock)
        {
            if (prefabCache.ContainsKey(itemName))
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
        Vector3Int cell = Cache_map.tileMap.WorldToCell(transform.position);
        return new Vector2Int(cell.x, cell.y);
    }

    /// <summary>
    /// 尝试获取指定位置的Tile块
    /// </summary>
    private bool TryGetTileBlock(Vector2Int pos, out TileData tileData, out IBlockTile tileBlock)
    {
        tileData = Cache_map?.GetTile(pos);
        if (tileData == null)
        {
            tileBlock = null;
            return false;
        }

        // 从缓存获取
        lock (cacheLock)
        {
            if (prefabCache.TryGetValue(tileData.Name_ItemName, out tileBlock))
            {
                return tileBlock != null;
            }
        }

        // 缓存未命中，加载预制体
        var prefab = GameRes.Instance?.GetPrefab(tileData.Name_ItemName);
        if (prefab == null)
        {
            Debug.LogError($"找不到 Prefab: {tileData.Name_ItemName}");
            tileBlock = null;
            return false;
        }

        var itemComp = prefab.GetComponent<Item>();
        if (itemComp is IBlockTile block)
        {
            tileBlock = block;
            // 存入缓存
            lock (cacheLock)
            {
                prefabCache[tileData.Name_ItemName] = tileBlock;
            }
            return true;
        }
        else
        {
            Debug.LogWarning($"Prefab 未实现 IBlockTile: {tileData.Name_ItemName}");
            tileBlock = null;
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