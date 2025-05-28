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
    public UltEvent onSave { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public UltEvent onLoad { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

    public override void Act()
    {
        throw new System.NotImplementedException();
    }
    [Button("从数据加载地图")]
    public void Load()
    {
     //   print("加载当前场景的Tilemap");
        PullDataToTilemap();
/*
        if (tileMapData.WorldEdgeDatas == null || tileMapData.WorldEdgeDatas.Count == 0)
        {
            Debug.LogWarning("[Map.Load] WorldEdgeDatas 为空，未生成任何边界");
            return;
        }

        worldEdges_GameObject.Clear(); // 清空旧的引用列表

        if (tileMapData.WorldEdgeDatas.Count > 0)
        foreach (var edgeData in tileMapData.WorldEdgeDatas)
        {
            GameObject edgeObj = GameRes.Instance.InstantiatePrefab(edgeData.Name);
            var worldEdge = edgeObj.GetComponent<WorldEdge>();
            worldEdge.Data = edgeData;
            edgeObj.GetComponent<ISave_Load>().Load();
            worldEdges_GameObject.Add(worldEdge);
            edgeObj.transform.parent = transform;
        }*/
    }

    [Button("保存地图到数据")]
    public void Save()
    {
      //  Debug.Log($"[Map.Save] 开始保存地图数据");

        // 🔥 清空旧数据，防止重复添加
       /* tileMapData.WorldEdgeDatas.Clear();*/
      //  Debug.Log($"[Map.Save] 清空旧的 WorldEdgeDatas");

 /*       if (worldEdges_GameObject.Count > 0)
        {
          //  Debug.Log($"[Map.Save] 使用 worldEdges 列表，共 {worldEdges_GameObject.Count} 个");

            foreach (var edge in worldEdges_GameObject)
            {
                var edgeName = edge.name;
             //   Debug.Log($"[Map.Save] 正在保存边界对象: {edgeName}");

                edge.GetComponent<ISave_Load>().Save();
                edge.gameObject.SetActive(false);
                tileMapData.WorldEdgeDatas.Add(edge.Data);

              //  Debug.Log($"[Map.Save] 保存并销毁边界对象: {edgeName}");
                Destroy(edge.gameObject);
            }
        }
        else
        {
            //Debug.LogWarning("[Map.Save] worldEdges 列表为空，从子物体中查找 WorldEdge");

            WorldEdge[] AllWorldEdges = GetComponentsInChildren<WorldEdge>();
         //   Debug.Log($"[Map.Save] 找到 {AllWorldEdges.Length} 个子物体中的 WorldEdge");

            foreach (var edge in AllWorldEdges)
            {
                var edgeName = edge.name;
               // Debug.Log($"[Map.Save] 正在保存子物体边界对象: {edgeName}");

                edge.GetComponent<ISave_Load>().Save();
                tileMapData.WorldEdgeDatas.Add(edge.Data);
                edge.gameObject.SetActive(false);

               // Debug.Log($"[Map.Save] 边界对象 {edgeName} 已禁用并添加到 GameObject_False 列表");
                SaveAndLoad.Instance.GameObject_False.Add(edge.gameObject);
            }
        }*/

       // Debug.Log($"[Map.Save] 当前 WorldEdgeDatas 数量: {tileMapData.WorldEdgeDatas.Count}");

       // Debug.Log("保存当前场景的 tilemap 数据");
        SaveTilemapData();

     //   Debug.Log("[Map.Save] 地图保存完成");
    }


    // --- Odin 按钮区域 ---

    public void PullDataToTilemap()
    {
        foreach (var kvp in tileMapData.Data)
        {
            string tileName = kvp.Key;
            List<Vector2Int> positions = kvp.Value;

            // 通过名称获取 TileBase 对象
            TileBase tile = GameRes.Instance.GetTileBase(tileName);
            if (tile == null)
            {
                Debug.LogError($"无法加载 Tile: {tileName}");
                continue;
            }

            // 遍历所有坐标并设置 Tile
            foreach (Vector2Int pos2D in positions)
            {
                Vector3Int pos3D = new Vector3Int(pos2D.x, pos2D.y, 0);
                tileMap.SetTile(pos3D, tile);
            }
        }
    }




    public void SaveTilemapData()
    {
        // 创建临时字典存储 <Tile名称, 坐标列表>
        var tempData = new Dictionary<string, List<Vector2Int>>();
        if(tileMap == null)
            tileMap= GetComponentInChildren<Tilemap>();
        // 获取 Tilemap 的包围盒范围
        BoundsInt bounds = tileMap.cellBounds;

        // 遍历所有可能的坐标（Vector3Int）
        foreach (Vector3Int pos3D in bounds.allPositionsWithin)
        {
            TileBase currentTile = tileMap.GetTile(pos3D);
            if (currentTile == null) continue;

            // 转换为 Vector2Int（忽略 Z 轴）
            Vector2Int pos2D = new Vector2Int(pos3D.x, pos3D.y);
            string tileName = currentTile.name;

            // 将坐标添加到对应名称的列表中
            if (!tempData.ContainsKey(tileName))
            {
                tempData[tileName] = new List<Vector2Int>();
            }
            tempData[tileName].Add(pos2D);
        }

        // 直接赋值到现有的 tileMapData（避免创建新对象）
        tileMapData.Data = tempData;
    }
}