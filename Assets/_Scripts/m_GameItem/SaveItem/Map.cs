using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Map : Item,ISave_Load
{
    [Header("地图配置")]
    [SerializeField]
    public Data_TileMap tileMapData;

    [Header("Tilemap 组件")]
    [SerializeField]
    public Tilemap tileMap;

    // 强制类型转换属性（保持与基类 Item 的兼容）
    public override ItemData Item_Data { get => tileMapData; set => tileMapData = value as Data_TileMap; }
 
    public override void Act()
    {
        throw new System.NotImplementedException();
    }
    [Button("从数据加载地图")]
    public void Load()
    {
        LoadTileData();
        
    }
    [Button("保存地图到数据")]
    public void Save()
    {
        SaveTileData();
    }
    public void LoadTileData()
    {
        if (tileMapData.TileData == null || tileMapData.TileData.Count == 0)
        {
            Debug.LogWarning("TileData is empty. Nothing to load.");
            return;
        }

        foreach (var kvp in tileMapData.TileData)
        {
            Vector2Int position2D = kvp.Key;
            List<TileData> tileDataList = kvp.Value;

            if (tileDataList == null || tileDataList.Count == 0) continue;

            // 检测 tileDataList[0].Name_ItemName 是否为空
            if (string.IsNullOrEmpty(tileDataList[0].Name_ItemName))
            {
                string baseName = tileDataList[0].Name_tileBase;
                if (!string.IsNullOrEmpty(baseName) && baseName.StartsWith("TileBase_"))
                {
                    tileDataList[0].Name_ItemName = "Tile_" + baseName.Substring("TileBase_".Length);
                }
                else
                {
                    tileDataList[0].Name_ItemName = "Tile_Default"; // 兜底名字
                }
            }

            // 获取最顶层 TileData（倒数第一个）
            TileData topTile = tileDataList[^1];

            TileBase tile = GameRes.Instance.GetTileBase(topTile.Name_tileBase);
            if (tile == null)
            {
                Debug.LogError($"无法加载 Tile: {topTile.Name_tileBase}");
                continue;
            }

            Vector3Int position3D = new Vector3Int(position2D.x, position2D.y, 0);
            tileMap.SetTile(position3D, tile);
        }

        Debug.Log("多层 TileData 已加载到 Tilemap 中");
    }

    public void SaveTileData()
    {
        if (tileMap == null)
        {
            Debug.LogError("Tilemap 组件为空，无法保存数据！");
            return;
        }

        BoundsInt bounds = tileMap.cellBounds;
        Dictionary<Vector2Int, List<TileData>> tempTileData = new();

        foreach (Vector3Int pos3D in bounds.allPositionsWithin)
        {
            TileBase tile = tileMap.GetTile(pos3D);
            if (tile == null) continue;

            Vector2Int pos2D = new Vector2Int(pos3D.x, pos3D.y);

            TileData tileData = new TileData
            {
                Name_tileBase = tile.name,
                position = pos3D,
                workTime = 0f
            };

            // 如果该坐标已有列表，添加，否则新建
            if (!tempTileData.ContainsKey(pos2D))
                tempTileData[pos2D] = new List<TileData>();

            tempTileData[pos2D].Add(tileData);
        }

        tileMapData.TileData = tempTileData;

        Debug.Log("多层 TileData 已保存到 Data_TileMap 中");
    }

    public void ADDTile(Vector2Int position, TileData tileData)
    {
        tileData.position = (Vector3Int)position;

        // 如果该位置没有初始化 List，就创建一个
        if (!tileMapData.TileData.ContainsKey(position))
        {
            tileMapData.TileData[position] = new List<TileData>();
        }

        tileMapData.TileData[position].Add(tileData);

        UpdateTileBaseAtPosition(position);
    }

    [Button("获取 TileData")]
    public TileData GetTile(Vector2Int position, int? index = null)
    {
        if (!tileMapData.TileData.TryGetValue(position, out var list) || list.Count == 0)
        {
            Debug.LogWarning($"位置 {position} 上没有任何 TileData。");
            return null;
        }

        int i = index ?? (list.Count - 1); // 默认返回最上层（最后一个）

        if (i < 0 || i >= list.Count)
        {
            Debug.LogWarning($"位置 {position} 的索引 {i} 不合法。");
            return null;
        }

        return list[i];
    }



    public void DELTile(Vector2Int position, int? index = null)
    {
        if (!tileMapData.TileData.ContainsKey(position) || tileMapData.TileData[position].Count == 0)
        {
            Debug.LogWarning($"位置 {position} 上没有 TileData 可删除。");
            return;
        }

        List<TileData> list = tileMapData.TileData[position];

        int removeIndex = index ?? (list.Count - 1); // 若 index 为 null，就删除最后一个

        if (removeIndex < 0 || removeIndex >= list.Count)
        {
            Debug.LogWarning($"位置 {position} 的删除索引 {removeIndex} 非法。");
            return;
        }

        list.RemoveAt(removeIndex);

        // 如果该位置已经没有层了，可以考虑移除字典项（可选）
        if (list.Count == 0)
        {
            tileMapData.TileData.Remove(position);
        }

        UpdateTileBaseAtPosition(position);
    }


    public void UPDTile(Vector2Int position, int index, TileData tileData)
    {
        tileData.position = (Vector3Int)position;
        tileMapData.TileData[position][index] = tileData;
        UpdateTileBaseAtPosition(position);
    }
    public void UpdateTileBaseAtPosition(Vector2Int position)
    {
        Vector3Int position3D = new Vector3Int(position.x, position.y, 0);

        if (!tileMapData.TileData.ContainsKey(position) || tileMapData.TileData[position].Count == 0)
        {
            tileMap.SetTile(position3D, null); // 清除该 Tile
            Debug.Log($"清除了位置 {position} 上的 TileBase（无数据）");
            return;
        }

        // 获取该位置最顶层的 TileData（最后一个）
        TileData topTile = tileMapData.TileData[position][^1];
        TileBase tile = GameRes.Instance.GetTileBase(topTile.Name_tileBase);

        if (tile == null)
        {
            Debug.LogError($"无法加载 TileBase：{topTile.Name_tileBase}，更新失败。");
            return;
        }

        tileMap.SetTile(position3D, tile);
        Debug.Log($"已更新 TileBase 于位置 {position}，使用资源：{topTile.Name_tileBase}");
    }


}