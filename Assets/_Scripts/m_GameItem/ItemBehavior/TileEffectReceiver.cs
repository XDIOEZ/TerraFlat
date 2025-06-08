using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 优化版 TileEffectReceiver：
/// - 缓存 Map 和 Tilemap 引用
/// - 缓存 Prefab 对应的 IBlockTile 引用，按 Name_ItemName 而非格子位置缓存
/// - 持续动态获取 TileData，支持地图动态变化
/// - 完成 Tile_Exit、Tile_Update 方法调用
/// </summary>
public class TileEffectReceiver : MonoBehaviour
{
    public Vector2Int lastGridPos;
    public Item item;
    public Map Cache_map;

    [Header("Raycast Settings (unused now)")]
    public float raycastDistance = 5f;
    public LayerMask tileLayerMask;

    // 缓存 Prefab 名称 -> IBlockTile 实例
    private readonly Dictionary<string, IBlockTile> prefabCache = new Dictionary<string, IBlockTile>();

    private void Start()
    {
        // 缓存 Map 对象
        if (Cache_map == null)
        {
            foreach (var mapItem in RunTimeItemManager.Instance.GetItemsByNameID("MapCore"))
            {
                if (mapItem != null)
                {
                    Cache_map = mapItem.GetComponent<Map>();
                    break;
                }
            }
        }

        if (Cache_map == null)
        {
            Debug.LogError("未找到 MapCore 中的 Map 组件");
            enabled = false;
            return;
        }

        // 缓存 Item 组件
        if (item == null)
        {
            item = GetComponentInParent<Item>();
        }

        // 初始化网格位置
        lastGridPos = GetCurrentGridPos();
    }

    private void Update()
    {
        Vector2Int currentGridPos = GetCurrentGridPos();
        if (currentGridPos != lastGridPos)
        {
            OnTileExit(lastGridPos);
            lastGridPos = currentGridPos;
            OnTileEnter(currentGridPos);
        }
        OnTileUpdate(currentGridPos);
    }

    private Vector2Int GetCurrentGridPos()
    {
        Vector3Int cell = Cache_map.tileMap.WorldToCell(transform.position);
        return new Vector2Int(cell.x, cell.y);
    }

    private void OnTileEnter(Vector2Int gridPos)
    {
        if (TryGetTileBlock(gridPos, out var tileData, out var tileBlock))
        {
            tileBlock.Tile_Enter(item, tileData);
        }
    }

    private void OnTileExit(Vector2Int gridPos)
    {
        if (TryGetTileBlock(gridPos, out var tileData, out var tileBlock))
        {
            tileBlock.Tile_Exit(item, tileData);
        }
    }

    private void OnTileUpdate(Vector2Int gridPos)
    {
        if (TryGetTileBlock(gridPos, out var tileData, out var tileBlock))
        {
            tileBlock.Tile_Update(item, tileData);
        }
    }

    /// <summary>
    /// 根据位置获取 TileData，并从 prefabCache 中获取或缓存 IBlockTile
    /// </summary>
    private bool TryGetTileBlock(Vector2Int pos, out TileData tileData, out IBlockTile tileBlock)
    {
        tileData = Cache_map.GetTile(pos);
        if (tileData == null)
        {
            tileBlock = null;
            return false;
        }

        if (!prefabCache.TryGetValue(tileData.Name_ItemName, out tileBlock))
        {
            var prefab = GameRes.Instance.GetPrefab(tileData.Name_ItemName);
            if (prefab == null)
            {
                Debug.LogError($"找不到 Prefab: {tileData.Name_ItemName}");
                prefabCache[tileData.Name_ItemName] = null;
                return false;
            }

            var itemComp = prefab.GetComponent<Item>();
            if (itemComp is IBlockTile block)
            {
                tileBlock = block;
                prefabCache[tileData.Name_ItemName] = tileBlock;
            }
            else
            {
                Debug.LogWarning($"Prefab 未实现 IBlockTile: {tileData.Name_ItemName}");
                prefabCache[tileData.Name_ItemName] = null;
                tileBlock = null;
                return false;
            }
        }

        return tileBlock != null;
    }

    /// <summary>
    /// 获取当前格子的 TileData
    /// </summary>
    public TileData GetCurrentTileData()
    {
        var pos = GetCurrentGridPos();
        return Cache_map.GetTile(pos);
    }
}
