using Force.DeepCloner;
using NavMeshPlus.Components;
using NPOI.SS.Formula.Functions;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public class Map : Item
{
    #region 属性和字段
    [Header("地图配置")]
    [SerializeField]
    public Data_TileMap Data = new Data_TileMap();

    [Header("Tilemap 组件")]
    [SerializeField]
    public Tilemap tileMap;

    public UltEvent OnMapGenerated_Start = new UltEvent();
    public Chunk chunk;

    public GameObject ParentObject;

    // 协程引用管理，避免协程叠加
    public Coroutine loadTileMapCoroutine;
    public Coroutine backTilePenaltyCoroutine;

    [SerializeReference]
    public RandomMapGenerator mapGenerator;//TODO 我在这里添加了一个类 以后就直接从这里配置了 修改一下这个脚本以适配我的需求

    private void Awake()
    {
        InitMapGenerator();
    }

    /// <summary>
    /// 初始化随机地图生成器：
    /// - 确保有实例（可在 Inspector 中直接配置字段）
    /// - 绑定当前 Map / Item 引用
    /// - 订阅 OnMapGenerated_Start 事件
    /// </summary>
    private void InitMapGenerator()
    {
        // 保留在 Inspector 中已经配置好的引用
        if (mapGenerator == null)
        {
            mapGenerator = new RandomMapGenerator();
        }

        // 让生成器知道当前地图和物品是谁
        mapGenerator.map = this;
        mapGenerator.Init(this);
    }

    // 强制类型转换属性（保持与基类 Item 的兼容）
    public override ItemData itemData { get => Data; set => Data = value as Data_TileMap; }
    #endregion

    #region 基类方法实现
    public override void Act()
    {
        if (Data.TileLoaded == false
            && SaveDataMgr.Instance.SaveData.PlanetData_Dict.TryGetValue(SceneManager.GetActiveScene().name, out var planetData))
        {
            OnMapGenerated_Start.Invoke();
        }
    }
    #endregion

    #region 保存和加载
    [Button("从数据加载地图")]
    public override void Load()
    {
        chunk = GetComponentInParent<Chunk>();
        chunk.Map = this;

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
    #endregion

    #region TileMap加载方法
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

            TileBase tile = GameRes.Instance.GetTileBase(topTile.ID);
            if (tile == null)
            {
                Debug.LogError($"无法加载 Tile: {topTile.ID}");
                continue;
            }

            Vector3Int position3D = new Vector3Int(position2D.x, position2D.y, 0);

            tileMap.SetTile(position3D, tile);
        }

        // 直接调用权重烘焙，不延迟
        BackTilePenalty_Async();
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
        const int batchSize = 500;
        int processedCount = 0;

        foreach (var kvp in Data.TileData)
        {
            Vector2Int position2D = kvp.Key;
            List<TileData> tileDataList = kvp.Value;

            // 获取最顶层 TileData（倒数第一个）
            TileData topTile = tileDataList[^1];

            TileBase tile = GameRes.Instance.GetTileBase(topTile.ID);
            if (tile == null)
            {
                Debug.LogError($"无法加载 Tile: {topTile.ID}");
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
        BackTilePenalty_Sync();

        Debug.Log($"✅ 完成加载 {Data.TileData.Count} 个Tile到Tilemap");

        // 清理协程引用
        loadTileMapCoroutine = null;
    }
    #endregion

    #region 寻路权重烘焙方法
    [Button("异步烘焙地块寻路权重")]
    public void BackTilePenalty_Async()
    {
        // 检查自身是否处于激活状态
        if (!gameObject.activeInHierarchy || !enabled)
        {
            Debug.Log("地图未激活，跳过权重烘焙");
            return;
        }

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

        // 处理所有节点数据 这个是根据地块数据进行烘焙的
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

            AstarGameManager.Instance?.ModifyNodePenalty_Optimized(alignedWorldPos, topTile.Penalty, topTile.IsWalkable);
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

        // 创建节点处理列表（包含坐标、权重与可通行性）
        List<(Vector3 worldPos, uint penalty, bool isWalkable)> nodesToProcess = new List<(Vector3, uint, bool)>();

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

            nodesToProcess.Add((alignedWorldPos, topTile.Penalty, topTile.IsWalkable));
        }

        // 分批处理节点，避免长时间阻塞主线程
        const int batchSize = 125;
        for (int i = 0; i < nodesToProcess.Count; i += batchSize)
        {
            int endIndex = Mathf.Min(i + batchSize, nodesToProcess.Count);

            // 处理当前批次
            for (int j = i; j < endIndex; j++)
            {
                var (worldPos, penalty, isWalkable) = nodesToProcess[j];
                AstarGameManager.Instance?.ModifyNodePenalty_Optimized(worldPos, penalty, isWalkable);
            }

            // 每处理一批就等待一帧，让出控制权给其他任务
            yield return null;
        }

        //        Debug.Log($"✅ 完成烘焙 {nodesToProcess.Count} 个地块的寻路权重");

        // 清理协程引用
        backTilePenaltyCoroutine = null;
    }
    #endregion

    #region 地块数据烘焙
    /// <summary>
    /// 烘焙单个地块的寻路权重
    /// </summary>
    /// <param name="position2D">地块的2D坐标</param>
    public void BackTilePenalty_Cell(Vector2 position2D)
    {
        var tile = GetTile(new Vector2Int(x: (int)position2D.x, y: (int)position2D.y));
        if (tile == null) return;

        uint penalty = tile.Penalty;
        AstarGameManager.Instance?.ModifyNodePenalty_Optimized(position2D, penalty, tile.IsWalkable);
    }
    /// <summary>
    /// 烘焙指定位置为中心的 3×3 地块寻路权重
    /// </summary>
    public void BackTilePenalty_Cell_3x3(Vector2 centerPosition2D)
    {
        Vector2Int center = new Vector2Int(
            Mathf.FloorToInt(centerPosition2D.x),
            Mathf.FloorToInt(centerPosition2D.y)
        );

        for (int offsetX = -1; offsetX <= 1; offsetX++)
        {
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                Vector2Int tilePos = new Vector2Int(
                    center.x + offsetX,
                    center.y + offsetY
                );

                var tile = GetTile(tilePos);
                if (tile == null)
                    continue;

                // 卸载建筑或恢复区域时，默认把地块恢复为可通行
                tile.IsWalkable = true;

                uint penalty = tile.Penalty;

                AstarGameManager.Instance?.ModifyNodePenalty_Optimized(
                    new Vector2(tilePos.x, tilePos.y),
                    penalty,
                    tile.IsWalkable
                );
            }
        }
    }



    /// <summary>
    /// 烘焙单个地块的寻路权重（强制设为不可通行）
    /// </summary>
    /// <param name="position2D">地块的2D坐标</param>
    public void BackTilePenalty_Cell_NotMove(Vector2 position2D)
    {
        // 先更新 TileData：将该格子的最顶层 Tile 标记为不可通行
        Vector2Int gridPos = new Vector2Int(
            Mathf.FloorToInt(position2D.x),
            Mathf.FloorToInt(position2D.y)
        );

        var tile = GetTile(gridPos);
        if (tile != null)
        {
            tile.IsWalkable = false;
        }

        // 再更新寻路节点：Penalty=0 + Walkable=false
        AstarGameManager.Instance?.ModifyNodePenalty_Optimized(position2D, 0, false);
    }

    /// <summary>
    /// 烘焙指定区域（Bounds）内所有地块的寻路权重
    /// </summary>
    /// <param name="bounds">要烘焙的区域</param>
    public void BackTilePenalty_Bounds(Bounds bounds, bool useTilepenalty = false)
    {
        Debug.Log($"[BackTilePenalty_Bounds] 开始烘焙Bounds区域，中心: {bounds.center}, 大小: {bounds.size}");

        // 检查必要组件
        if (Data == null)
        {
            Debug.LogError("[BackTilePenalty_Bounds] Data为空，无法执行烘焙");
            return;
        }

        if (Data.TileData == null)
        {
            Debug.LogError("[BackTilePenalty_Bounds] Data.TileData为空，无法执行烘焙");
            return;
        }

        if (tileMap == null)
        {
            Debug.LogError("[BackTilePenalty_Bounds] tileMap为空，无法执行烘焙");
            return;
        }

        if (AstarGameManager.Instance == null)
        {
            Debug.LogError("[BackTilePenalty_Bounds] AstarGameManager.Instance为空，无法执行烘焙");
            return;
        }

        // 获取GridGraph以获得节点尺寸信息
        var gridGraph = AstarGameManager.Instance?.Pathfinder?.data?.gridGraph;
        float nodeSize = gridGraph != null ? gridGraph.nodeSize : 1f;
        Debug.Log($"[BackTilePenalty_Bounds] 节点尺寸: {nodeSize}");

        // 计算Bounds覆盖的整数坐标范围，带有0.5的右上角偏移
        Vector2Int min = new Vector2Int(
            Mathf.FloorToInt(bounds.min.x),
            Mathf.FloorToInt(bounds.min.y)
        );
        Vector2Int max = new Vector2Int(
            Mathf.FloorToInt(bounds.max.x),
            Mathf.FloorToInt(bounds.max.y)
        );

        // 添加0.5的右上角偏移，确保与建筑放置时的网格对齐方式一致
        max.x += 1; // 右上角偏移0.5相当于增加一个单位
        max.y += 1; // 右上角偏移0.5相当于增加一个单位

        Debug.Log($"[BackTilePenalty_Bounds] 计算出的坐标范围(带0.5偏移): min({min.x}, {min.y}) - max({max.x}, {max.y})");

        int processedTiles = 0;
        int skippedTiles = 0;

        // 遍历Bounds内的所有地块
        for (int x = min.x; x <= max.x; x++)
        {
            for (int y = min.y; y <= max.y; y++)
            {
                Vector2Int position2D = new Vector2Int(x, y);

                // 只有当地图数据中存在该位置的地块时才处理
                if (Data.TileData.ContainsKey(position2D))
                {
                    // 获取最顶层 TileData（倒数第一个）
                    TileData topTile = Data.TileData[position2D][^1];
                    Vector3Int position3D = new Vector3Int(position2D.x, position2D.y, 0);

                    // 使用更精确的世界坐标计算方法，解决偏移问题
                    Vector3 cellCenterWorld = tileMap.CellToWorld(position3D) + tileMap.cellSize / 2f;

                    // 进一步校正坐标以匹配A*网格节点中心
                    float alignedX = Mathf.Floor(cellCenterWorld.x / nodeSize) * nodeSize + nodeSize * 0.5f;
                    float alignedY = Mathf.Floor(cellCenterWorld.y / nodeSize) * nodeSize + nodeSize * 0.5f;
                    Vector3 alignedWorldPos = new Vector3(alignedX, alignedY, cellCenterWorld.z);
                    if (useTilepenalty == false)
                        // 区域强制不可通行
                        AstarGameManager.Instance?.ModifyNodePenalty_Optimized(alignedWorldPos, 0, false);
                    else
                        // 使用地块自身的可通行性与权重
                        AstarGameManager.Instance?.ModifyNodePenalty_Optimized(alignedWorldPos, topTile.Penalty, topTile.IsWalkable);
                    processedTiles++;
                }
                else
                {
                    skippedTiles++;
                }
            }
        }

        Debug.Log($"[BackTilePenalty_Bounds] 烘焙完成: 处理了{processedTiles}个地块，跳过了{skippedTiles}个不存在的地块");
    }

    /// <summary>
    /// 协程：异步烘焙指定区域（Bounds）内所有地块的寻路权重
    /// </summary>
    /// <param name="bounds">要烘焙的区域</param>
    /// <returns></returns>
    private IEnumerator BackTilePenalty_BoundsCoroutine(Bounds bounds)
    {
        // 获取GridGraph以获得节点尺寸信息
        var gridGraph = AstarGameManager.Instance?.Pathfinder?.data?.gridGraph;
        float nodeSize = gridGraph != null ? gridGraph.nodeSize : 1f;

        // 计算Bounds覆盖的整数坐标范围，带有0.5的右上角偏移
        Vector2Int min = new Vector2Int(
            Mathf.FloorToInt(bounds.min.x),
            Mathf.FloorToInt(bounds.min.y)
        );
        Vector2Int max = new Vector2Int(
            Mathf.FloorToInt(bounds.max.x),
            Mathf.FloorToInt(bounds.max.y)
        );

        // 添加0.5的右上角偏移，确保与建筑放置时的网格对齐方式一致
        max.x += 1; // 右上角偏移0.5相当于增加一个单位
        max.y += 1; // 右上角偏移0.5相当于增加一个单位

        // 计算总共需要处理的地块数量
        int totalTiles = (max.x - min.x + 1) * (max.y - min.y + 1);

        // 分批处理地块，避免长时间阻塞主线程
        const int batchSize = 100;
        int processedCount = 0;

        // 遍历Bounds内的所有地块
        for (int x = min.x; x <= max.x; x++)
        {
            for (int y = min.y; y <= max.y; y++)
            {
                Vector2Int position2D = new Vector2Int(x, y);

                // 只有当地图数据中存在该位置的地块时才处理
                if (Data.TileData.ContainsKey(position2D))
                {
                    // 获取最顶层 TileData（倒数第一个）
                    TileData topTile = Data.TileData[position2D][^1];
                    Vector3Int position3D = new Vector3Int(position2D.x, position2D.y, 0);

                    // 使用更精确的世界坐标计算方法，解决偏移问题
                    Vector3 cellCenterWorld = tileMap.CellToWorld(position3D) + tileMap.cellSize / 2f;

                    // 进一步校正坐标以匹配A*网格节点中心
                    float alignedX = Mathf.Floor(cellCenterWorld.x / nodeSize) * nodeSize + nodeSize * 0.5f;
                    float alignedY = Mathf.Floor(cellCenterWorld.y / nodeSize) * nodeSize + nodeSize * 0.5f;
                    Vector3 alignedWorldPos = new Vector3(alignedX, alignedY, cellCenterWorld.z);

                    AstarGameManager.Instance?.ModifyNodePenalty_Optimized(alignedWorldPos, topTile.Penalty, topTile.IsWalkable);
                }

                processedCount++;

                // 每处理一批就等待一帧，让出控制权给其他任务
                if (processedCount % batchSize == 0)
                {
                    yield return null;
                }
            }
        }

        Debug.Log($"✅ 完成烘焙 Bounds 区域内 {processedCount} 个地块的寻路权重");
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

    public void ADDTileData(Vector2Int position, TileData tileData)
    {
        tileData.position = (Vector3Int)position;

        // 如果该位置没有初始化 List，就创建一个
        if (!Data.TileData.ContainsKey(position))
        {
            Data.TileData[position] = new List<TileData>();
        }

        Data.TileData[position].Add(tileData);
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
        TileBase tile = GameRes.Instance.GetTileBase(topTile.ID);

        if (tile == null)
        {
            Debug.LogError($"无法加载 TileBase：{topTile.ID}，更新失败。");
            return;
        }

        tileMap.SetTile(position3D, tile);
        //Debug.Log($"已更新 TileBase 于位置 {position}，使用资源：{topTile.Name_TileBase}");
    }
    #endregion
}