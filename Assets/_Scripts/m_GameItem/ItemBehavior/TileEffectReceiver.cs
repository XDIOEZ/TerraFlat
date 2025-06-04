using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TileEffectReceiver : MonoBehaviour
{
    public Vector2Int lastGridPos;
    public Item item;
    public Map Cache_map;

    private void Start()
    {
        // 缓存 Map 对象
        if (Cache_map == null)
        {
            Cache_map = (Map)RunTimeItemManager.Instance.GetItemsByNameID("MapCore")[0];
        }

        // 初始化当前格子位置
        lastGridPos = GetCurrentGridPos();

        // 缓存 Item 组件
        if (item == null)
        {
            item = GetComponentInParent<Item>();
        }
    }

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
