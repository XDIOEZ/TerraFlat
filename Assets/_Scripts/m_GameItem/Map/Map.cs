using Force.DeepCloner;
using NavMeshPlus.Components;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public class Map : Item, ISave_Load
{
    #region 属性和字段
    [Header("地图配置")]
    [SerializeField]
    public Data_TileMap Data = new Data_TileMap();

    [Header("Tilemap 组件")]
    [SerializeField]
    public Tilemap tileMap;

    public UltEvent OnMapGenerated_Start;

    public NavMeshModifierTilemap navigationModiferTileMap;

    public GameObject ParentObject;

    // 协程引用管理，避免协程叠加
    private Coroutine loadTileMapCoroutine;
    private Coroutine backTilePenaltyCoroutine;

    // 强制类型转换属性（保持与基类 Item 的兼容）
    public override ItemData itemData { get => Data; set => Data = value as Data_TileMap; }
    #endregion

    #region 基类方法实现
    public override void Act()
    {
        if (Data.TileLoaded == false 
            && SaveDataMgr.Instance.SaveData.PlanetData_Dict.TryGetValue(SceneManager.GetActiveScene().name, out var planetData))
        {
            if(planetData.AutoGenerateMap == true)
            OnMapGenerated_Start.Invoke();
        }
    }
    #endregion

    #region 保存和加载
    [Button("从数据加载地图")]
    public override void Load()
    {
        // 检查TileData的数量是否等于ChunkSize*ChunkSize的数量
        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        int expectedTileCount = (int)(chunkSize.x * chunkSize.y);
        
        // 如果TileData为空或数量不等于期望值，表示TileData还在生成中
        if (Data == null || Data.TileData == null || Data.TileData.Count != expectedTileCount)
        {
            Debug.Log($"TileData还在生成中，当前数量: {Data?.TileData?.Count ?? 0}，期望数量: {expectedTileCount}");
            return;
        }
        
        // TileData已生成完成，开始加载
        LoadTileData_To_TileMap_Ansync();
    }

    //不需要保存数据 因为游戏中的所有对地图的行为 直接影响背后数据
    [Button("保存地图到数据")]
    public override void Save()
    {
        // 只有 tileMapData 为空或其 TileData 为空时才初始化数据
        if (Data == null || Data.TileData == null || Data.TileData.Count == 0)
        {
            SaveTileMap_TO_TileData();
        }
        base.Save();
    }

    public void LoadTileData_To_TileMap_Sync()
    {
        if (Data.TileData == null || Data.TileData.Count == 0)
        {
            Debug.LogWarning("TileData is empty. Nothing to load.");
            return;
        }

        foreach (var kvp in Data.TileData)
        {
            Vector2Int position2D = kvp.Key;
            List<TileData> tileDataList = kvp.Value;

            // 获取最顶层 TileData（倒数第一个）
            TileData topTile = tileDataList[^1];

            TileBase tile = GameRes.Instance.GetTileBase(topTile.Name_TileBase);
            if (tile == null)
            {
                Debug.LogError($"无法加载 Tile: {topTile.Name_TileBase}");
                continue;
            }

            Vector3Int position3D = new Vector3Int(position2D.x, position2D.y, 0);

            tileMap.SetTile(position3D, tile);
        }
        
        // 直接调用权重烘焙，不延迟
        BackTilePenalty();
    }

    public void LoadTileData_To_TileMap_Ansync()
    {
        // 如果已有协程在运行，先停止它
        if (loadTileMapCoroutine != null)
        {
            StopCoroutine(loadTileMapCoroutine);
        }
        
        // 启动新的协程
        loadTileMapCoroutine = StartCoroutine(LoadTileData_To_TileMapCoroutine());
    }

    /// <summary>
    /// 使用协程优化的加载Tile数据到Tilemap的方法
    /// </summary>
    private IEnumerator LoadTileData_To_TileMapCoroutine()
    {
        if (Data.TileData == null || Data.TileData.Count == 0)
        {
            Debug.LogWarning("TileData is empty. Nothing to load.");
            loadTileMapCoroutine = null;
            yield break;
        }

        // 分批处理Tile数据，避免长时间阻塞主线程
        const int batchSize = 50;
        int processedCount = 0;
        
        foreach (var kvp in Data.TileData)
        {
            Vector2Int position2D = kvp.Key;
            List<TileData> tileDataList = kvp.Value;

            // 获取最顶层 TileData（倒数第一个）
            TileData topTile = tileDataList[^1];

            TileBase tile = GameRes.Instance.GetTileBase(topTile.Name_TileBase);
            if (tile == null)
            {
                Debug.LogError($"无法加载 Tile: {topTile.Name_TileBase}");
                continue;
            }

            Vector3Int position3D = new Vector3Int(position2D.x, position2D.y, 0);

            tileMap.SetTile(position3D, tile);
            
            processedCount++;
            
            // 每处理一批就等待一帧，让出控制权给其他任务
            if (processedCount % batchSize == 0)
            {
                yield return null;
            }
        }
        
        // 等待一帧确保所有Tile设置完成
        yield return null;
        
        // 直接调用权重烘焙，不延迟
        BackTilePenalty();
        
        Debug.Log($"✅ 完成加载 {Data.TileData.Count} 个Tile到Tilemap");
        
        // 清理协程引用
        loadTileMapCoroutine = null;
    }

    [Button("烘焙地块寻路权重")]
    public void BackTilePenalty()
    {
        // 如果已有协程在运行，先停止它
        if (backTilePenaltyCoroutine != null)
        {
            StopCoroutine(backTilePenaltyCoroutine);
        }
        
        // 启动新的协程
        backTilePenaltyCoroutine = StartCoroutine(BackTilePenaltyCoroutine());
    }

 public void BackTilePenalty_Sync()
{
    // 获取GridGraph以获得节点尺寸信息
    var gridGraph = AstarGameManager.Instance?.Pathfinder?.data?.gridGraph;
    float nodeSize = gridGraph != null ? gridGraph.nodeSize : 1f;

    // 处理所有节点数据
    foreach (var kvp in Data.TileData)
    {
        Vector2Int position2D = kvp.Key;
        List<TileData> tileDataList = kvp.Value;

        // 获取最顶层 TileData（倒数第一个）
        TileData topTile = tileDataList[^1];

        Vector3Int position3D = new Vector3Int(position2D.x, position2D.y, 0);

        // 使用更精确的世界坐标计算方法，解决偏移问题
        Vector3 cellCenterWorld = tileMap.CellToWorld(position3D) + tileMap.cellSize / 2f;
        
        // 进一步校正坐标以匹配A*网格节点中心
        float alignedX = Mathf.Floor(cellCenterWorld.x / nodeSize) * nodeSize + nodeSize * 0.5f;
        float alignedY = Mathf.Floor(cellCenterWorld.y / nodeSize) * nodeSize + nodeSize * 0.5f;
        Vector3 alignedWorldPos = new Vector3(alignedX, alignedY, cellCenterWorld.z);

        AstarGameManager.Instance?.ModifyNodePenalty_Optimized(alignedWorldPos, topTile.Penalty);
    }

    Debug.Log($"✅ 完成同步烘焙 {Data.TileData.Count} 个地块的寻路权重");
}
    /// <summary>
    /// 使用协程优化的烘焙地块寻路权重方法
    /// </summary>
    private IEnumerator BackTilePenaltyCoroutine()
    {
        // 获取GridGraph以获得节点尺寸信息
        var gridGraph = AstarGameManager.Instance?.Pathfinder?.data?.gridGraph;
        float nodeSize = gridGraph != null ? gridGraph.nodeSize : 1f;

        // 创建节点处理列表
        List<(Vector3 worldPos, uint penalty)> nodesToProcess = new List<(Vector3, uint)>();

        // 收集所有需要处理的节点数据
        foreach (var kvp in Data.TileData)
        {
            Vector2Int position2D = kvp.Key;
            List<TileData> tileDataList = kvp.Value;

            // 获取最顶层 TileData（倒数第一个）
            TileData topTile = tileDataList[^1];

            Vector3Int position3D = new Vector3Int(position2D.x, position2D.y, 0);

            // 使用更精确的世界坐标计算方法，解决偏移问题
            Vector3 cellCenterWorld = tileMap.CellToWorld(position3D) + tileMap.cellSize / 2f;
            
            // 进一步校正坐标以匹配A*网格节点中心
            float alignedX = Mathf.Floor(cellCenterWorld.x / nodeSize) * nodeSize + nodeSize * 0.5f;
            float alignedY = Mathf.Floor(cellCenterWorld.y / nodeSize) * nodeSize + nodeSize * 0.5f;
            Vector3 alignedWorldPos = new Vector3(alignedX, alignedY, cellCenterWorld.z);

            nodesToProcess.Add((alignedWorldPos, topTile.Penalty));
        }

        // 分批处理节点，避免长时间阻塞主线程
        const int batchSize = 50;
        for (int i = 0; i < nodesToProcess.Count; i += batchSize)
        {
            int endIndex = Mathf.Min(i + batchSize, nodesToProcess.Count);
            
            // 处理当前批次
            for (int j = i; j < endIndex; j++)
            {
                var (worldPos, penalty) = nodesToProcess[j];
                AstarGameManager.Instance?.ModifyNodePenalty_Optimized(worldPos, penalty);
            }

            // 每处理一批就等待一帧，让出控制权给其他任务
            yield return null;
        }

        Debug.Log($"✅ 完成烘焙 {nodesToProcess.Count} 个地块的寻路权重");
        
        // 清理协程引用
        backTilePenaltyCoroutine = null;
    }
    #endregion

    #region 数据初始化
    public void SaveTileMap_TO_TileData()
    {
        if (tileMap == null)
        {
            Debug.LogError("Tilemap 组件为空，无法保存数据！");
            return;
        }

        BoundsInt bounds = tileMap.cellBounds;

        // 临时 TileData 字典
        Dictionary<Vector2Int, List<TileData>> tempTileData = new();

        // 遍历 Tilemap 上的所有 Tile
        foreach (Vector3Int pos3D in bounds.allPositionsWithin)
        {
            TileBase tilebase = tileMap.GetTile(pos3D);

            if (tilebase == null) continue;

            Vector2Int pos2D = new Vector2Int(pos3D.x, pos3D.y);

            string Name_ItemName = ConvertTileBaseNameToItemName(tilebase.name); // 使用转换方法

            TileData tileData;
            tileData = GameRes.Instance.GetPrefab(Name_ItemName).
                GetComponent<IBlockTile>().TileData.DeepClone();


            // 如果该坐标已有列表，添加，否则新建
            if (!tempTileData.ContainsKey(pos2D))
                tempTileData[pos2D] = new List<TileData>();

            tempTileData[pos2D].Add(tileData);
        }

        Data.TileData = tempTileData;

        Debug.Log("多层 TileData 已保存到 Data_TileMap 中" + tempTileData.Count);
    }
    #endregion

    #region 工具方法
    /// <summary>
    /// 将TileBase名称转换为对应的ItemName
    /// 规则：TileBase_XXX -> TileItem_XXX
    /// </summary>
    /// <param name="tileBaseName">TileBase的名称</param>
    /// <returns>对应的ItemName</returns>
    private string ConvertTileBaseNameToItemName(string tileBaseName)
    {
        if (string.IsNullOrEmpty(tileBaseName))
        {
            Debug.LogWarning("TileBase名称为空，无法转换为ItemName");
            return "";
        }

        // 检查是否以"TileBase_"开头
        if (tileBaseName.StartsWith("TileBase_"))
        {
            // 提取后缀部分（如：Grass, Water, Mountain）
            string suffix = tileBaseName.Substring("TileBase_".Length);

            // 组合成新的ItemName
            string itemName = "TileItem_" + suffix;

            Debug.Log($"TileBase名称转换：{tileBaseName} -> {itemName}");
            return itemName;
        }
        else
        {
            Debug.LogWarning($"TileBase名称 '{tileBaseName}' 不符合预期格式（应以'TileBase_'开头）");
            return "";
        }
    }
    #endregion

    #region Tile操作方法
    public void ADDTile(Vector2Int position, TileData tileData)
    {
        tileData.position = (Vector3Int)position;

        // 如果该位置没有初始化 List，就创建一个
        if (!Data.TileData.ContainsKey(position))
        {
            Data.TileData[position] = new List<TileData>();
        }

        Data.TileData[position].Add(tileData);

        UpdateTileBaseAtPosition(position);
    }

    [Button("获取 TileData")]
    public TileData GetTile(Vector2Int position, int? index = null)
    {
        if (!Data.TileData.TryGetValue(position, out var list) || list.Count == 0)
        {
            //  Debug.LogWarning($"位置 {position} 上没有任何 TileData。");
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
    
    // 重载方法：只获取最上层的 TileData
    public TileData GetTopTile(Vector2Int position)
    {
        return GetTile(position);
    }

    // 重载方法：获取指定索引的 TileData
    public TileData GetTileAt(Vector2Int position, int index)
    {
        return GetTile(position, index);
    }

    // 重载方法：获取所有 TileData
    public List<TileData> GetAllTiles(Vector2Int position)
    {
        if (Data?.TileData == null)
        {
            return new List<TileData>();
        }
        
        if (Data.TileData.TryGetValue(position, out var list))
        {
            return new List<TileData>(list);
        }
        
        return new List<TileData>();
    }

    public int GetTileArea(Vector2Int position)
    {
        if (navigationModiferTileMap == null)
        {
            Debug.LogWarning("navigationModiferTileMap is null");
            return -1;
        }

        var modifierMap = navigationModiferTileMap.GetModifierMap();
        if (modifierMap == null)
        {
            Debug.LogWarning("modifierMap is null");
            return -1;
        }

        var tile = tileMap.GetTile((Vector3Int)position);
        if (tile == null)
        {
           // Debug.LogWarning($"No tile found at position {position}");
            return -1;
        }

        if (!modifierMap.TryGetValue(tile, out var navModifier))
        {
            Debug.LogWarning($"No navigation modifier found for tile at {position}");
            return -1;
        }

        return navModifier.area;
    }

    public void DELTile(Vector2Int position, int? index = null)
    {
        if (!Data.TileData.ContainsKey(position) || Data.TileData[position].Count == 0)
        {
            Debug.LogWarning($"位置 {position} 上没有 TileData 可删除。");
            return;
        }

        List<TileData> list = Data.TileData[position];

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
            Data.TileData.Remove(position);
        }

        UpdateTileBaseAtPosition(position);
    }

    public void UPDTile(Vector2Int position, int index, TileData tileData)
    {
        tileData.position = (Vector3Int)position;
        Data.TileData[position][index] = tileData;
        UpdateTileBaseAtPosition(position);
    }

    public void UpdateTileBaseAtPosition(Vector2Int position)
    {
        Vector3Int position3D = new Vector3Int(position.x, position.y, 0);

        if (!Data.TileData.ContainsKey(position) || Data.TileData[position].Count == 0)
        {
            tileMap.SetTile(position3D, null); // 清除该 Tile
            Debug.Log($"清除了位置 {position} 上的 TileBase（无数据）");
            return;
        }

        // 获取该位置最顶层的 TileData（最后一个）
        TileData topTile = Data.TileData[position][^1];
        TileBase tile = GameRes.Instance.GetTileBase(topTile.Name_TileBase);

        if (tile == null)
        {
            Debug.LogError($"无法加载 TileBase：{topTile.Name_TileBase}，更新失败。");
            return;
        }

        tileMap.SetTile(position3D, tile);
        //Debug.Log($"已更新 TileBase 于位置 {position}，使用资源：{topTile.Name_TileBase}");
    }

    #endregion
}