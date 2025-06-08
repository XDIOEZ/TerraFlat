using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TileEffectReceiver : MonoBehaviour
{
    public Vector2Int lastGridPos;
    public Item item;
    public Map Cache_map;

    public float raycastDistance = 5f; // 向下检测的距离
    public LayerMask tileLayerMask;   // 指定哪些图层可以被检测

    private void Start()
    {
        //TODO 获取当前对象脚下的Tilemap

        // 缓存 Map 对象
        if (Cache_map == null)
        {
            foreach (var item in RunTimeItemManager.Instance.GetItemsByNameID("MapCore"))
            {
                if (item != null)
                {
                    Cache_map = item.GetComponent<Map>();
                    break;
                }
            }
        }


   

        // 初始化当前格子位置
        lastGridPos = GetCurrentGridPos();

        // 缓存 Item 组件
        if (item == null)
        {
            item = GetComponentInParent<Item>();
        }
    }
    /*
    void TryDetectTileByRaycastZ_2D()
    {
        Vector2 origin = (Vector2)transform.position + Vector2.up * 1f; // 起点稍微上移
        Vector2 direction = Vector2.down; // Unity 2D 中“Z轴负方向”可理解为“向下看”

        RaycastHit2D hit = Physics2D.Raycast(origin, direction, raycastDistance, tileLayerMask);

        if (hit.collider != null)
        {
            GameObject hitObj = hit.collider.gameObject;

            // 尝试获取 Map
            Map map = hitObj.GetComponent<Map>();
            if (map != null)
            {
                Cache_map = map;
                Debug.Log("通过Z轴射线找到 Map：" + map.name);
                return;
            }

            // 尝试获取 Item
            Item itemOnTile = hitObj.GetComponent<Item>();
            if (itemOnTile != null)
            {
                Debug.Log("找到 Item：" + itemOnTile.name);
                return;
            }

            Debug.Log("命中物体但未包含目标组件：" + hitObj.name);
        }
        else
        {
            Debug.LogWarning("2D射线未命中任何 Tile/Map！");
        }
    }*/




    private void FixedUpdate()
    {
        // 检测位置是否发生1单位变化
        Vector2Int currentGridPos = GetCurrentGridPos();

        if (currentGridPos != lastGridPos)
        {
            // ⚠️ 触发离开旧格子的逻辑
            TriggerTileExit(lastGridPos);

            // 更新当前位置
            lastGridPos = currentGridPos;

            // ✅ 触发进入新格子的逻辑
            TriggerTileEnter(currentGridPos);
        }
    }


    Vector2Int GetCurrentGridPos()
    {
        Vector3Int cellPos = Cache_map.tileMap.WorldToCell(transform.position);
        return new Vector2Int(cellPos.x, cellPos.y);
    }

    void TriggerTileEnter(Vector2Int gridPos)
    {
        TileData tileData = Cache_map.GetTile(gridPos);
        if (tileData == null)
        {
            return;
        }
        GameObject prefab = GameRes.Instance.GetPrefab(tileData.Name_ItemName);
        if (prefab == null)
        {
            Debug.LogError($"找不到或无法实例化Prefab: {tileData.Name_ItemName}");
            return;
        }
   
        Item tileItem = prefab.GetComponent<Item>();


        if (tileItem == null)
        {
            Debug.LogError($"Prefab 上缺少 Item 组件: {tileData.Name_ItemName}");
            return;
        }


        if (tileItem is IBlockTile tileBlock)
        {
            if (tileBlock.Data == null)
            {
                Debug.LogError("tileBlock.Data 为 null");
                return;
            }

            tileBlock.Tile_Enter(item, tileBlock.Data.tileData);
        }
        else
        {
            Debug.LogWarning($"tileItem 并不实现 IBlockTile 接口: {tileData.Name_ItemName}");
        }
    }


    void TriggerTileExit(Vector2Int gridPos)
    {

        TileData tileData = Cache_map.GetTile(gridPos);

        if (tileData != null)
        {
            Item tileItem = GameRes.Instance.GetPrefab(tileData.Name_ItemName).GetComponent<Item>();
            if (tileItem is IBlockTile tileBlock)
            {
                tileBlock.Tile_Exit(item,tileBlock.Data.tileData); // 👈 你需要添加这个接口方法
            }
        }
    }

}
