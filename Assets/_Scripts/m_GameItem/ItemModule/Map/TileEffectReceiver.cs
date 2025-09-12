using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UltEvents;
using UnityEngine;

public class TileEffectReceiver : MonoBehaviour
{
    public Vector2Int lastGridPos;
    public Item item;
    public Map Cache_map;
    public UltEvent<TileData> OnTileEnterEvent = new UltEvent<TileData>();
    public UltEvent<TileData> OnTileExitEvent = new UltEvent<TileData>();


    // 改为静态字典，所有实例共享缓存
    private static readonly Dictionary<string, IBlockTile> prefabCache = new Dictionary<string, IBlockTile>();
    // 静态锁，确保多线程环境下的字典操作安全
    private static readonly object cacheLock = new object();
    // 缓存清理时间戳（用于定期清理）
    private static float lastCleanupTime;
    private const float cleanupInterval = 300f; // 5分钟清理一次过期缓存

    private void Start()
    {
        // 初始化Map引用
        if (Cache_map == null)
        {
            var mapItems = ItemMgr.Instance?.GetItemsByNameID("MapCore");
            if (mapItems != null && mapItems.Count > 0)
            {
                Cache_map = mapItems[0].GetComponent<Map>();
            }
        }

        if (Cache_map == null)
        {
            Cache_map = FindFirstObjectByType<Map>();
        }

        if (Cache_map == null)
        {
            Debug.LogError("TileEffectReceiver: 未找到有效的 Map 组件！");
            enabled = false;
            return;
        }

        // 初始化Item引用
        if (item == null)
        {
            item = GetComponentInParent<Item>();
        }

        lastGridPos = GetCurrentGridPos();
        enabled = true;
    }

    private void Update()
    {
        // 定期清理缓存（静态方法，全局生效一次）
        CleanupCacheIfNeeded();

        Vector2Int currentGridPos = GetCurrentGridPos();
        if (currentGridPos != lastGridPos)
        {
            OnTileExit(lastGridPos);
            lastGridPos = currentGridPos;
            OnTileEnter(currentGridPos);
        }
        OnTileUpdate(currentGridPos);
    }

    // 静态方法：定期清理无效缓存
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

    // 静态方法：手动清理缓存（如场景切换时调用）
    public static void ClearCache()
    {
        lock (cacheLock)
        {
            prefabCache.Clear();
            Debug.Log("TileEffectReceiver: 已手动清空所有缓存");
        }
    }

    // 静态方法：移除特定预制体的缓存
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

    private Vector2Int GetCurrentGridPos()
    {
        Vector3Int cell = Cache_map.tileMap.WorldToCell(transform.position);
        return new Vector2Int(cell.x, cell.y);
    }

    private void OnTileEnter(Vector2Int gridPos)
    {
        if (item == null) return;
        if (TryGetTileBlock(gridPos, out var tileData, out var tileBlock))
        {
            tileBlock.Tile_Enter(item, tileData);
            OnTileEnterEvent.Invoke(tileData);
        }
    }

    private void OnTileExit(Vector2Int gridPos)
    {
        if (item == null) return;
        if (TryGetTileBlock(gridPos, out var tileData, out var tileBlock))
        {
            tileBlock.Tile_Exit(item, tileData);
            OnTileExitEvent.Invoke(tileData);
        }
    }

    private void OnTileUpdate(Vector2Int gridPos)
    {
        if (Cache_map == null || item == null) return;
        if (TryGetTileBlock(gridPos, out var tileData, out var tileBlock))
        {
            tileBlock.Tile_Update(item, tileData);
        }
    }

    private bool TryGetTileBlock(Vector2Int pos, out TileData tileData, out IBlockTile tileBlock)
    {
        tileData = Cache_map?.GetTile(pos);
        if (tileData == null)
        {
            tileBlock = null;
            return false;
        }

        // 静态缓存访问加锁
        lock (cacheLock)
        {
            if (prefabCache.TryGetValue(tileData.Name_ItemName, out tileBlock))
            {
                return tileBlock != null;
            }
        }

        // 未命中缓存时加载预制体
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
            // 存入静态缓存
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

    public TileData GetCurrentTileData()
    {
        var pos = GetCurrentGridPos();
        return Cache_map?.GetTile(pos);
    }

    // 场景卸载时清理静态缓存（可选，根据需求决定）
    private void OnDestroy()
    {
        // 如果是最后一个实例，清理缓存
        // if (FindObjectsOfType<TileEffectReceiver>().Length <= 1)
        // {
        //     ClearCache();
        // }
    }
}
