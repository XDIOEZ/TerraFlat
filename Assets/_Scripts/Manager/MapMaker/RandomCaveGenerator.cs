using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Sirenix.OdinInspector;
using Force.DeepCloner;

/// <summary>
/// 随机矿洞生成器：
/// - 基于房间和走廊的连接生成
/// - 支持分层结构和不同类型的房间
/// - 支持资源生成和装饰物放置
/// - 支持边界处理和入口/出口生成
/// </summary>
public class RandomCaveGenerator : MonoBehaviour
{
    #region 配置参数
    [Header("矿洞配置")]
    [Required] public Map map; // 地图管理对象
    [Tooltip("（可选）手动指定Grid组件，未指定则自动从当前对象/子对象获取")]
    public Grid mapGrid;
    [Tooltip("（可选）手动指定Tilemap组件，未指定则自动从当前对象的子对象获取")]
    public Tilemap targetTilemap;

    [Header("矿洞尺寸")]
    [Tooltip("矿洞的最小宽度和高度")]
    public Vector2Int minCaveSize = new Vector2Int(40, 40);
    [Tooltip("矿洞的最大宽度和高度")]
    public Vector2Int maxCaveSize = new Vector2Int(80, 80);
    
    [Header("房间配置")]
    [Tooltip("房间的最小尺寸")]
    public Vector2Int minRoomSize = new Vector2Int(3, 3);
    [Tooltip("房间的最大尺寸")]
    public Vector2Int maxRoomSize = new Vector2Int(8, 8);
    [Tooltip("生成的房间数量")]
    public int roomCount = 15;
    [Tooltip("房间之间的最小间距")]
    public int roomSpacing = 2;

    [Header("走廊配置")]
    [Tooltip("走廊的最小宽度")]
    public int minCorridorWidth = 1;
    [Tooltip("走廊的最大宽度")]
    public int maxCorridorWidth = 3;

    [Header("瓦片配置")]
    [Tooltip("墙壁瓦片预制体名称")]
    public string wallTileName = "CaveWall";
    [Tooltip("地面瓦片预制体名称")]
    public string floorTileName = "CaveFloor";

    [Header("入口/出口配置")]
    [Tooltip("入口预制体名称")]
    public string entrancePrefabName = "CaveEntrance";
    [Tooltip("出口预制体名称")]
    public string exitPrefabName = "CaveExit";

    [Header("资源生成")]
    [Tooltip("矿石战利品列表")]
    public List<LootEntry> oreLoots = new List<LootEntry>
    {
        new LootEntry { LootPrefabName = "Mine_Copper", DropChance = 0.003f, MinAmount = 1, MaxAmount = 3 },
        new LootEntry { LootPrefabName = "Mine_Iron", DropChance = 0.005f, MinAmount = 1, MaxAmount = 5 },
        new LootEntry { LootPrefabName = "Mine_Stone", DropChance = 0.01f, MinAmount = 1, MaxAmount = 2 }
    };
    
    [Tooltip("装饰物战利品列表")]
    public List<LootEntry> decorationLoots = new List<LootEntry>
    {
        new LootEntry { LootPrefabName = "Twine", DropChance = 0.02f, MinAmount = 1, MaxAmount = 1 },
        new LootEntry { LootPrefabName = "Stalagmite", DropChance = 0.2f, MinAmount = 1, MaxAmount = 1 },
        new LootEntry { LootPrefabName = "Crystal", DropChance = 0.1f, MinAmount = 1, MaxAmount = 1 }
    };

    [Header("性能选项")]
    [Tooltip("每帧生成的最大瓦片数 (0=立即生成)")]
    public int tilesPerFrame = 10;

    [Header("边界连接")]
    public bool generateEdges = true;
    #endregion

    #region 内部变量
    private int Seed => SaveDataMgr.Instance.SaveData.Seed;
    private Vector2Int currentCaveSize; // 当前矿洞尺寸

    public static System.Random rng; // 系统级随机实例

    private Dictionary<string, TileData> tileDataCache = new(); // TileData 缓存
    private List<CaveRoom> generatedRooms = new List<CaveRoom>(); // 已生成的房间列表
    #endregion

    #region 数据结构
    [Serializable]
    public class CaveRoom
    {
        public RectInt bounds;
        public Vector2Int center;
        public bool isConnected = false;

        public CaveRoom(RectInt bounds)
        {
            this.bounds = bounds;
            this.center = new Vector2Int(
                bounds.x + bounds.width / 2,
                bounds.y + bounds.height / 2
            );
        }
    }

    [Serializable]
    public class LootEntry
    {
        public string LootPrefabName;
        public float DropChance;
        public int MinAmount;
        public int MaxAmount;
    }
    #endregion

    #region Unity 生命周期
    public void Awake()
    {
        rng = new System.Random(Seed);

        map.OnMapGenerated_Start += GenerateRandomCave;

        // 1. 自动获取Grid组件（优先级：手动指定 > 当前对象 > 子对象）
        if (mapGrid == null)
        {
            mapGrid = GetComponent<Grid>();
            if (mapGrid == null)
            {
                mapGrid = GetComponentInChildren<Grid>(includeInactive: false);
            }
        }

        // 2. 自动获取当前对象的子对象Tilemap
        if (targetTilemap == null)
        {
            Tilemap[] childTilemaps = GetComponentsInChildren<Tilemap>(includeInactive: false);
            if (childTilemaps != null && childTilemaps.Length > 0)
            {
                targetTilemap = childTilemaps[0];
                Debug.Log($"[RandomCaveGenerator] 自动获取子对象Tilemap：{targetTilemap.name}");
            }
            else
            {
                Debug.LogError($"[RandomCaveGenerator] 当前对象下未找到任何Tilemap子对象！");
            }
        }
    }
    #endregion

    #region 主逻辑
    [Button("生成随机矿洞")]
    public void GenerateRandomCave()
    {
        if (map == null)
        {
            Debug.LogError("[RandomCaveGenerator] 地图引用未设置！");
            return;
        }

        ClearMap();

        map.Data.position = new Vector2Int(
            Mathf.RoundToInt(transform.parent.position.x),
            Mathf.RoundToInt(transform.parent.position.y)
        );

        // 随机生成矿洞尺寸
        currentCaveSize = new Vector2Int(
            rng.Next(minCaveSize.x, maxCaveSize.x + 1),
            rng.Next(minCaveSize.y, maxCaveSize.y + 1)
        );

        if (tilesPerFrame > 0)
            ChunkMgr.Instance.StartCoroutine(GenerateCaveCoroutine());
        else
        {
            GenerateAllCaveTiles();
            OnGenerationComplete();
        }
    }
    #endregion

    #region 矿洞生成流程
    private IEnumerator GenerateCaveCoroutine()
    {
        GenerateAllCaveTiles();
        yield return null;
        OnGenerationComplete();
    }

    private void GenerateAllCaveTiles()
    {
        // 初始化地图数据
        map.Data.EnvironmentData = new EnvironmentFactors[currentCaveSize.x, currentCaveSize.y];
        
        // 生成房间
        GenerateRooms();
        
        // 连接房间
        ConnectRooms();
        
        // 生成走廊
        GenerateCorridors();
        
        // 生成墙壁和地面
        GenerateWallsAndFloors();
        
        // 生成资源和装饰物
        GenerateResourcesAndDecorations();
        
        // 生成入口和出口
        GenerateEntranceAndExit();
        
        // 生成边界
        if (generateEdges)
        {
            //GenerateCaveEdges();
        }
    }
    #endregion

    #region 房间生成逻辑
    private void GenerateRooms()
    {
        generatedRooms.Clear();
        
        for (int i = 0; i < roomCount; i++)
        {
            // 随机生成房间尺寸
            int width = rng.Next(minRoomSize.x, maxRoomSize.x + 1);
            int height = rng.Next(minRoomSize.y, maxRoomSize.y + 1);
            
            // 随机生成房间位置
            int x = rng.Next(roomSpacing, currentCaveSize.x - width - roomSpacing);
            int y = rng.Next(roomSpacing, currentCaveSize.y - height - roomSpacing);
            
            RectInt newRoom = new RectInt(x, y, width, height);
            
            // 检查是否与其他房间重叠
            bool overlaps = false;
            foreach (var room in generatedRooms)
            {
                if (room.bounds.Overlaps(newRoom))
                {
                    overlaps = true;
                    break;
                }
            }
            
            if (!overlaps)
            {
                CaveRoom caveRoom = new CaveRoom(newRoom);
                generatedRooms.Add(caveRoom);
                
                // 在地图上标记房间区域为地板
                for (int rx = newRoom.x; rx < newRoom.x + newRoom.width; rx++)
                {
                    for (int ry = newRoom.y; ry < newRoom.y + newRoom.height; ry++)
                    {
                        Vector2Int worldPos = new Vector2Int(rx, ry) + map.Data.position;
                        PlaceTile(worldPos, floorTileName);
                    }
                }
            }
        }
        
        Debug.Log($"[RandomCaveGenerator] 生成了 {generatedRooms.Count} 个房间");
    }

    private void ConnectRooms()
    {
        if (generatedRooms.Count < 2) return;
        
        // 使用最小生成树算法连接房间
        List<int> connectedRooms = new List<int> { 0 };
        generatedRooms[0].isConnected = true;
        
        while (connectedRooms.Count < generatedRooms.Count)
        {
            int closestRoomIndex = -1;
            int closestConnectedIndex = -1;
            float closestDistance = float.MaxValue;
            
            // 寻找最近的未连接房间
            for (int i = 0; i < generatedRooms.Count; i++)
            {
                if (generatedRooms[i].isConnected) continue;
                
                for (int j = 0; j < connectedRooms.Count; j++)
                {
                    int connectedIndex = connectedRooms[j];
                    float distance = Vector2Int.Distance(
                        generatedRooms[i].center, 
                        generatedRooms[connectedIndex].center
                    );
                    
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestRoomIndex = i;
                        closestConnectedIndex = connectedIndex;
                    }
                }
            }
            
            if (closestRoomIndex != -1)
            {
                // 连接两个房间
                ConnectTwoRooms(closestRoomIndex, closestConnectedIndex);
                generatedRooms[closestRoomIndex].isConnected = true;
                connectedRooms.Add(closestRoomIndex);
            }
            else
            {
                break;
            }
        }
    }

    private void ConnectTwoRooms(int roomIndex1, int roomIndex2)
    {
        Vector2Int center1 = generatedRooms[roomIndex1].center;
        Vector2Int center2 = generatedRooms[roomIndex2].center;
        
        // 随机生成走廊宽度
        int corridorWidth = rng.Next(minCorridorWidth, maxCorridorWidth + 1);
        
        // 创建L型走廊连接两个房间中心
        // 先水平移动，再垂直移动
        for (int x = Mathf.Min(center1.x, center2.x); x <= Mathf.Max(center1.x, center2.x); x++)
        {
            // 根据走廊宽度生成多个瓦片
            for (int wy = 0; wy < corridorWidth; wy++)
            {
                Vector2Int worldPos = new Vector2Int(x, center1.y + wy - corridorWidth/2) + map.Data.position;
                PlaceTile(worldPos, floorTileName);
            }
        }
        
        for (int y = Mathf.Min(center1.y, center2.y); y <= Mathf.Max(center1.y, center2.y); y++)
        {
            // 根据走廊宽度生成多个瓦片
            for (int wx = 0; wx < corridorWidth; wx++)
            {
                Vector2Int worldPos = new Vector2Int(center2.x + wx - corridorWidth/2, y) + map.Data.position;
                PlaceTile(worldPos, floorTileName);
            }
        }
    }

    private void GenerateCorridors()
    {
        // 走廊已经在连接房间时生成了
        // 这里可以添加额外的走廊生成逻辑
    }

    private void GenerateWallsAndFloors()
    {
        // 遍历整个矿洞区域
        for (int x = 0; x < currentCaveSize.x; x++)
        {
            for (int y = 0; y < currentCaveSize.y; y++)
            {
                Vector2Int localPos = new Vector2Int(x, y);
                Vector2Int worldPos = localPos + map.Data.position;
                
                // 如果当前位置没有地板，则放置墙壁
                if (map.GetTile(worldPos) == null)
                {
                    PlaceTile(worldPos, wallTileName);
                }
            }
        }
    }
    #endregion

    #region 资源和装饰物生成
    private void GenerateResourcesAndDecorations()
    {
        // 在地板上随机生成矿石和装饰物
        for (int x = 0; x < currentCaveSize.x; x++)
        {
            for (int y = 0; y < currentCaveSize.y; y++)
            {
                Vector2Int localPos = new Vector2Int(x, y);
                Vector2Int worldPos = localPos + map.Data.position;
                
                // 只在地板上生成资源
                if (map.GetTile(worldPos)?.Name_ItemName == floorTileName)
                {
                    // 生成矿石
                    SpawnLootItems(oreLoots, worldPos);
                    
                    // 生成装饰物
                    SpawnLootItems(decorationLoots, worldPos);
                }
            }
        }
    }
    #endregion

    #region 入口出口生成
    private void GenerateEntranceAndExit()
    {
        if (generatedRooms.Count == 0) return;
        
        // 入口放在第一个房间的中心
        Vector2Int entrancePos = generatedRooms[0].center + map.Data.position;
        SpawnEntranceOrExit(entrancePrefabName, entrancePos);
        
        // 出口放在最后一个房间的中心
        Vector2Int exitPos = generatedRooms[generatedRooms.Count - 1].center + map.Data.position;
        SpawnEntranceOrExit(exitPrefabName, exitPos);
    }
    #endregion

    #region 边界生成
    private void GenerateCaveEdges()
    {
        // 计算边界墙壁的缩放和位置参数
        float horizontalScale = currentCaveSize.x / 2f;
        float verticalScale = currentCaveSize.y / 2f;
        
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        Vector2Int centerPos = map.Data.position + new Vector2Int(currentCaveSize.x / 2, currentCaveSize.y / 2);
        
        foreach (var d in dirs)
        {
            Item edge = ItemMgr.Instance.InstantiateItem("CaveEdge", default, default, default, map.ParentObject);
            if (edge is WorldEdge we)
            {
                we.SetupMapEdge(d, map.Data.position);
                
                // 调整边界墙壁的大小和位置
                if (d == Vector2Int.up || d == Vector2Int.down)
                {
                    // 水平墙壁
                    we.transform.localScale = new Vector3(we.transform.localScale.x, horizontalScale, we.transform.localScale.z);
                    we.transform.localEulerAngles = new Vector3(we.transform.localEulerAngles.x, we.transform.localEulerAngles.y, 0);
                    we.transform.position = new Vector3(
                        centerPos.x, 
                        d == Vector2Int.up ? centerPos.y + verticalScale : centerPos.y - verticalScale, 
                        we.transform.position.z
                    );
                }
                else if (d == Vector2Int.left || d == Vector2Int.right)
                {
                    // 垂直墙壁 (需要旋转90度)
                    we.transform.localScale = new Vector3(we.transform.localScale.x, verticalScale, we.transform.localScale.z);
                    we.transform.localEulerAngles = new Vector3(we.transform.localEulerAngles.x, we.transform.localEulerAngles.y, 90);
                    we.transform.position = new Vector3(
                        d == Vector2Int.right ? centerPos.x + horizontalScale : centerPos.x - horizontalScale,
                        centerPos.y,
                        we.transform.position.z
                    );
                }
            }
            else
            {
                Debug.LogError("[RandomCaveGenerator] 边界对象类型错误，应为WorldEdge");
            }
        }
        Debug.Log("[RandomCaveGenerator] 矿洞边界生成完成");
    }
    #endregion

    #region 工具方法
    private void PlaceTile(Vector2Int position, string tileName)
    {
        // 缓存TileData避免重复加载
        if (!tileDataCache.ContainsKey(tileName))
        {
            var prefab = GameRes.Instance.GetPrefab(tileName);
            if (prefab == null)
            {
                Debug.LogError($"无法获取预制体: {tileName}");
                return;
            }

            var blockTile = prefab.GetComponent<IBlockTile>();
            if (blockTile == null)
            {
                Debug.LogError($"预制体 {tileName} 缺少 IBlockTile 组件");
                return;
            }

            tileDataCache[tileName] = blockTile.TileData;
        }

        // 克隆并添加Tile到地图
        var tile = tileDataCache[tileName].DeepClone();
        tile.position = new Vector3Int(position.x, position.y, 0);
        map.ADDTile(position, tile);
    }

    private void SpawnEntranceOrExit(string prefabName, Vector2Int position)
    {
        Vector2 spawnPos = new Vector2(position.x + 0.5f, position.y + 0.5f);
        
        // 实例化入口或出口预制体
        ItemMgr.Instance.InstantiateItem(
            prefabName,
            spawnPos,
            default,
            default,
            map.ParentObject
        ).Load();
    }

    private void SpawnItem(string itemName, Vector2Int position)
    {
        Vector2 spawnPos = new Vector2(position.x + 0.5f, position.y + 0.5f);
        
        // 实例化资源物品
        ItemMgr.Instance.InstantiateItem(
            itemName,
            spawnPos,
            default,
            default,
            map.ParentObject
        ).Load();
    }
    
    private void SpawnLootItems(List<LootEntry> lootEntries, Vector2Int position)
    {
        foreach (var loot in lootEntries)
        {
            // 根据掉落概率决定是否生成
            if (rng.NextDouble() < loot.DropChance)
            {
                // 随机生成数量
                int amount = rng.Next(loot.MinAmount, loot.MaxAmount + 1);
                
                for (int i = 0; i < amount; i++)
                {
                    SpawnItem(loot.LootPrefabName, position);
                }
            }
        }
    }

    private void ClearMap()
    {
        map.tileMap?.ClearAllTiles();
        map.Data.TileData?.Clear();
        Debug.Log("[RandomCaveGenerator] 矿洞地图已清除");
    }

    private void OnGenerationComplete()
    {
        map.tileMap?.RefreshAllTiles();
        map.Data.TileLoaded = true;
        map.BackTilePenalty_Async();
        Debug.Log($"[RandomCaveGenerator] 矿洞生成完成，尺寸: {currentCaveSize.x}x{currentCaveSize.y}");
    }
    #endregion
}