using System;
using UnityEngine;
using Sirenix.OdinInspector;
using System.Collections;
using Force.DeepCloner;
using UltEvents;
using System.Collections.Generic;

public class RandomMapGenerator : MonoBehaviour
{
    #region 配置参数
    [Header("地图配置")]
    [Required]
    public Map map;

    [Header("噪声参数")]
    [Range(0.001f, 0.05f)]
    public float noiseScale = 0.01f;

    [Header("地形阈值")]
    [Range(0f, 1f)]
    public float waterThreshold = 0.3f;
    [Range(0f, 1f)]
    public float mountainThreshold = 0.7f;

    [Header("性能选项")]
    [Tooltip("每帧生成的最大地块数 (0=全部立即生成)")]
    public int tilesPerFrame = 100;

    [Header("边界连接")]
    [Tooltip("确保与相邻地图无缝连接")]
    public bool seamlessBorders = true;

    [Header("边界过渡范围")]
    [Range(1, 20)]
    public int transitionRange = 5;

    [Header("完成事件")]
    [Tooltip("地图生成完成后触发")]
    public UltEvent onMapGenerated = new UltEvent();
    #endregion

    #region 内部变量
    // 生成地图的种子
    private string Seed => SaveAndLoad.Instance.SaveData.MapSeed;
    private int seed;
    #endregion

    #region Unity生命周期
    void Start()
    {
        if (map.Data.TileData.Count <= 0)
           GenerateRandomMap();
    }




#if UNITY_EDITOR
    // 在编辑器中可视化地图边界
    private void OnDrawGizmos()
    {
        if (map == null || map.Data == null) return;

        Vector2Int startPos = map.Data.position;
        Vector2Int size = map.Data.size;
        Vector3 center = new Vector3(startPos.x + size.x / 2f, startPos.y + size.y / 2f, 0);
        Vector3 size3D = new Vector3(size.x, size.y, 0.1f);

        Gizmos.color = new Color(0, 1, 0, 0.3f);
        Gizmos.DrawCube(center, size3D);
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, size3D);

        // 绘制地图位置标签
        UnityEditor.Handles.Label(center, $"Map: {startPos}\nSize: {size}", new GUIStyle
        {
            normal = { textColor = Color.green },
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter
        });
    }
#endif
    #endregion

    #region 地图生成主逻辑
    [Button("生成随机地图")]
    public void GenerateRandomMap()
    {
        if (map == null)
        {
            Debug.LogError("地图引用未设置！");
            return;
        }

        // 初始化随机种子
        seed = string.IsNullOrEmpty(Seed) ? DateTime.Now.GetHashCode() : Seed.GetHashCode();
        UnityEngine.Random.InitState(seed);

        // 清除现有地图
        ClearMap();

        // 获取地图范围和大小
        Vector2Int startPos = map.Data.position;
        Vector2Int size = map.Data.size;

        if (tilesPerFrame > 0)
        {
            // 分帧生成
            StartCoroutine(GenerateMapCoroutine(startPos, size));
        }
        else
        {
            // 立即生成
            GenerateAllTiles(startPos, size);
            OnGenerationComplete();
        }

        //生成完毕
        GenerateMapEdges();
    }
    #endregion

    public void GenerateMapEdges()
    {
        // 定义四个方向
        Vector2Int[] directions = new Vector2Int[]
        {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
        };

        // 为每个方向生成边界
        foreach (Vector2Int direction in directions)
        {
            Item edgeItem = RunTimeItemManager.Instance.InstantiateItem("MapEdge");
            if (edgeItem is WorldEdge worldEdge)
            {
                worldEdge.SetupMapEdge(direction);

                // 不设置为子对象，保持独立
                // worldEdge.transform.SetParent(transform);
            }
            else
            {
                Debug.LogError("实例化的对象不是WorldEdge类型!");
            }
        }

        Debug.Log("地图边界生成完成");
    }

    #region 协程生成
    private IEnumerator GenerateMapCoroutine(Vector2Int startPos, Vector2Int size)
    {
        int totalTiles = size.x * size.y;
        int processed = 0;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Vector2Int pos = new Vector2Int(startPos.x + x, startPos.y + y);
                GenerateTileAtPosition(pos);
                processed++;

                // 每帧处理一定数量的地块
                if (processed % tilesPerFrame == 0)
                {
                    yield return null; // 等待下一帧
                }
            }
        }

        OnGenerationComplete();
    }

    private void GenerateAllTiles(Vector2Int startPos, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Vector2Int pos = new Vector2Int(startPos.x + x, startPos.y + y);
                GenerateTileAtPosition(pos);
            }
        }
    }
    #endregion

    #region 地块生成逻辑
    private void GenerateTileAtPosition(Vector2Int position)
    {
        float noiseValue = CalculateSeamlessNoise(position);

        if (noiseValue < waterThreshold)
        {
            GenerateWaterTile(position, noiseValue);
        }
        else if (noiseValue > mountainThreshold)
        {
            GenerateMountainTile(position, noiseValue);
        }
        else
        {
            GenerateGrassTile(position, noiseValue);
        }
    }

    private void GenerateGrassTile(Vector2Int position, float noiseValue)
    {
        string itemName = "TileItem_Grass";
        GameObject prefab = GameRes.Instance.GetPrefab(itemName);

        if (prefab == null || !prefab.TryGetComponent<IBlockTile>(out var blockTile))
        {
            Debug.LogError($"无法获取草地Tile: {itemName}");
            return;
        }

        TileData_Grass tileData = blockTile.TileData.DeepClone() as TileData_Grass;
        if (tileData == null)
        {
            Debug.LogError($"TileData不是草地类型: {itemName}");
            return;
        }

        // 设置随机肥沃度 (0.3 - 0.8)
        tileData.FertileValue.BaseValue = Mathf.Lerp(0.3f, 0.8f, noiseValue);
        tileData.Name_TileBase = "TileBase_Grass";
        tileData.Name_ItemName = itemName;
        tileData.position = new Vector3Int(position.x, position.y, 0);

        map.ADDTile(position, tileData);
    }

    private void GenerateWaterTile(Vector2Int position, float noiseValue)
    {
        string itemName = "TileItem_Water";
        GameObject prefab = GameRes.Instance.GetPrefab(itemName);

        if (prefab == null || !prefab.TryGetComponent<IBlockTile>(out var blockTile))
        {
            Debug.LogError($"无法获取水域Tile: {itemName}");
            return;
        }

        TileData_Water tileData = blockTile.TileData.DeepClone() as TileData_Water;
        if (tileData == null)
        {
            Debug.LogError($"TileData不是水域类型: {itemName}");
            return;
        }

        // 设置随机深度 (0.5 - 3.0)
        tileData.DeepValue.BaseValue = Mathf.Lerp(0.5f, 3.0f, 1 - (noiseValue / waterThreshold));
        tileData.Name_TileBase = "TileBase_Water";
        tileData.Name_ItemName = itemName;
        tileData.position = new Vector3Int(position.x, position.y, 0);

        map.ADDTile(position, tileData);
    }

    private void GenerateMountainTile(Vector2Int position, float noiseValue)
    {
        string itemName = "TileItem_Mountain";
        GameObject prefab = GameRes.Instance.GetPrefab(itemName);

        if (prefab == null || !prefab.TryGetComponent<IBlockTile>(out var blockTile))
        {
            Debug.LogError($"无法获取山地Tile: {itemName}");
            return;
        }

        TileData tileData = blockTile.TileData.DeepClone();
        tileData.Name_TileBase = "TileBase_Mountain";
        tileData.Name_ItemName = itemName;
        tileData.position = new Vector3Int(position.x, position.y, 0);

        // 设置随机的拆除时间 (5-15秒)
        tileData.DemolitionTime = Mathf.Lerp(5f, 15f, (noiseValue - mountainThreshold) / (1 - mountainThreshold));

        map.ADDTile(position, tileData);
    }
    #endregion

    #region 噪声计算
    private float CalculateSeamlessNoise(Vector2Int position)
    {
        // 使用全局坐标计算噪声
        float globalX = position.x * noiseScale;
        float globalY = position.y * noiseScale;

        // 添加额外的噪声层创造更自然的地形
        float baseNoise = Mathf.PerlinNoise(globalX, globalY);
        float detailNoise = Mathf.PerlinNoise(globalX * 2.5f + 1000, globalY * 2.5f + 1000) * 0.2f;
        float ridgeNoise = Mathf.PerlinNoise(globalX * 0.5f + 2000, globalY * 0.5f + 2000) * 0.3f;

        // 组合噪声
        float combinedNoise = baseNoise + detailNoise - ridgeNoise;

        // 标准化到0-1范围
        return Mathf.Clamp01(combinedNoise);
    }
    #endregion
    #region 工具方法
    private void ClearMap()
    {
        // 清除Tilemap显示
        if (map.tileMap != null)
        {
            map.tileMap.ClearAllTiles(); 
        }

        // 清除数据
        if (map.Data != null && map.Data.TileData != null)
        {
            map.Data.TileData.Clear();
        }

        Debug.Log("地图已清除");
    }

    private void OnGenerationComplete()
    {
        // 确保所有更改都同步到Tilemap
        map.tileMap.RefreshAllTiles();
        Debug.Log($"地图生成完成! 位置: {map.Data.position}, 大小: {map.Data.size}, 种子: {seed}");

        // 触发完成事件
        onMapGenerated?.Invoke();
    }
    #endregion
}