// RandomCaveGeneratorConfig.cs
using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "RandomCaveConfig", menuName = "Map/Random Cave Generator Config", order = 2)]
public class RandomCaveGeneratorConfig : CaveGeneratorBase
{
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
    public string wallTileName = "TileItem_StoneWall";
    [Tooltip("地面瓦片预制体名称")]
    public string floorTileName = "TileItem_Stone";

    [Header("入口/出口配置")]
    [Tooltip("入口预制体名称")]
    public string entrancePrefabName = "Door";
    [Tooltip("出口预制体名称")]
    public string exitPrefabName = "Cave";

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
    };
    
    [Tooltip("宝藏战利品列表")]
    public List<LootEntry> treasureLoots = new List<LootEntry>
    {
        new LootEntry { LootPrefabName = "GoldCoin", DropChance = 0.5f, MinAmount = 1, MaxAmount = 5 },
        new LootEntry { LootPrefabName = "SilverCoin", DropChance = 0.3f, MinAmount = 1, MaxAmount = 3 },
    };

    [Tooltip("箱子被生成时,注入其中的宝藏配置,随机选择一个初始化配置")]
    public List<Inventoryinit> inventoryinits = new();

    // 内部变量
    private Vector2Int currentCaveSize;
    private List<CaveRoom> generatedRooms = new List<CaveRoom>();
    
    // 用于跟踪已生成物品的位置
    private HashSet<Vector2Int> occupiedPositions;

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

    // 在编辑器中验证时调用
    private void OnValidate()
    {
        // 验证矿石战利品条目
        if (oreLoots != null)
        {
            foreach (var lootEntry in oreLoots)
            {
                if (lootEntry != null)
                {
                    lootEntry.OnValidate();
                }
            }
        }
        
        // 验证装饰物战利品条目
        if (decorationLoots != null)
        {
            foreach (var lootEntry in decorationLoots)
            {
                if (lootEntry != null)
                {
                    lootEntry.OnValidate();
                }
            }
        }
        
        // 验证宝藏战利品条目
        if (treasureLoots != null)
        {
            foreach (var lootEntry in treasureLoots)
            {
                if (lootEntry != null)
                {
                    lootEntry.OnValidate();
                }
            }
        }
        
        // 验证尺寸配置
        if (minCaveSize.x < 10) minCaveSize.x = 10;
        if (minCaveSize.y < 10) minCaveSize.y = 10;
        if (maxCaveSize.x < minCaveSize.x) maxCaveSize.x = minCaveSize.x;
        if (maxCaveSize.y < minCaveSize.y) maxCaveSize.y = minCaveSize.y;
        
        // 验证房间配置
        if (minRoomSize.x < 1) minRoomSize.x = 1;
        if (minRoomSize.y < 1) minRoomSize.y = 1;
        if (maxRoomSize.x < minRoomSize.x) maxRoomSize.x = minRoomSize.x;
        if (maxRoomSize.y < minRoomSize.y) maxRoomSize.y = minRoomSize.y;
        if (roomCount < 1) roomCount = 1;
        if (roomSpacing < 0) roomSpacing = 0;
        
        // 验证走廊配置
        if (minCorridorWidth < 1) minCorridorWidth = 1;
        if (maxCorridorWidth < minCorridorWidth) maxCorridorWidth = minCorridorWidth;
        
        // 验证资源生成概率（确保不超过100%）
        if (oreLoots != null)
        {
            foreach (var loot in oreLoots)
            {
                if (loot.DropChance > 1f) loot.DropChance = 1f;
                if (loot.DropChance < 0f) loot.DropChance = 0f;
                if (loot.MinAmount < 0) loot.MinAmount = 0;
                if (loot.MaxAmount < loot.MinAmount) loot.MaxAmount = loot.MinAmount;
            }
        }
        
        if (decorationLoots != null)
        {
            foreach (var loot in decorationLoots)
            {
                if (loot.DropChance > 1f) loot.DropChance = 1f;
                if (loot.DropChance < 0f) loot.DropChance = 0f;
                if (loot.MinAmount < 0) loot.MinAmount = 0;
                if (loot.MaxAmount < loot.MinAmount) loot.MaxAmount = loot.MinAmount;
            }
        }
        
        if (treasureLoots != null)
        {
            foreach (var loot in treasureLoots)
            {
                if (loot.DropChance > 1f) loot.DropChance = 1f;
                if (loot.DropChance < 0f) loot.DropChance = 0f;
                if (loot.MinAmount < 0) loot.MinAmount = 0;
                if (loot.MaxAmount < loot.MinAmount) loot.MaxAmount = loot.MinAmount;
            }
        }
    }

    public override void GenerateCave(Map map, System.Random rng)
    {
        if (map == null)
        {
            Debug.LogError("[RandomCaveGeneratorConfig] 地图引用未设置！");
            return;
        }

        ClearMap(map);
        
        // 初始化已占用位置集合
        occupiedPositions = new HashSet<Vector2Int>();

        // 随机生成矿洞尺寸
        currentCaveSize = new Vector2Int(
            rng.Next(minCaveSize.x, maxCaveSize.x + 1),
            rng.Next(minCaveSize.y, maxCaveSize.y + 1)
        );

        // 初始化地图数据
        map.Data.EnvironmentData = new EnvironmentFactors[currentCaveSize.x, currentCaveSize.y];
        
        // 缓存字典
        Dictionary<string, TileData> tileDataCache = new Dictionary<string, TileData>();
        
        // 生成房间
        GenerateRooms(map, tileDataCache, rng);
        
        // 连接房间
        ConnectRooms(map, tileDataCache, rng);
        
        // 生成墙壁和地面
        GenerateWallsAndFloors(map, tileDataCache);
        
        // 生成资源和装饰物
        GenerateResourcesAndDecorations(map, rng);
        
        // 生成入口和出口
        GenerateEntranceAndExit(map, rng);
        
        Debug.Log($"[RandomCaveGeneratorConfig] 矿洞生成完成，尺寸: {currentCaveSize.x}x{currentCaveSize.y}");
    }

    private void GenerateRooms(Map map, Dictionary<string, TileData> tileDataCache, System.Random rng)
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
                        PlaceTile(map, tileDataCache, worldPos, floorTileName);
                    }
                }
            }
        }
        
        Debug.Log($"[RandomCaveGeneratorConfig] 生成了 {generatedRooms.Count} 个房间");
    }

    private void ConnectRooms(Map map, Dictionary<string, TileData> tileDataCache, System.Random rng)
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
                ConnectTwoRooms(map, tileDataCache, rng, closestRoomIndex, closestConnectedIndex);
                generatedRooms[closestRoomIndex].isConnected = true;
                connectedRooms.Add(closestRoomIndex);
            }
            else
            {
                break;
            }
        }
    }

    private void ConnectTwoRooms(Map map, Dictionary<string, TileData> tileDataCache, System.Random rng, int roomIndex1, int roomIndex2)
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
                PlaceTile(map, tileDataCache, worldPos, floorTileName);
            }
        }
        
        for (int y = Mathf.Min(center1.y, center2.y); y <= Mathf.Max(center1.y, center2.y); y++)
        {
            // 根据走廊宽度生成多个瓦片
            for (int wx = 0; wx < corridorWidth; wx++)
            {
                Vector2Int worldPos = new Vector2Int(center2.x + wx - corridorWidth/2, y) + map.Data.position;
                PlaceTile(map, tileDataCache, worldPos, floorTileName);
            }
        }
    }

    private void GenerateWallsAndFloors(Map map, Dictionary<string, TileData> tileDataCache)
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
                    PlaceTile(map, tileDataCache, worldPos, wallTileName);
                }
            }
        }
    }

private void GenerateResourcesAndDecorations(Map map, System.Random rng)
{
    // 创建一个位置到物品的映射，确保每个位置只生成一个物品
    Dictionary<Vector2Int, string> positionToItem = new Dictionary<Vector2Int, string>();
    List<Vector2Int> treasureChestPositions = new List<Vector2Int>(); // 专门记录宝箱位置
    
    // 收集所有可以生成物品的地板位置
    List<Vector2Int> floorPositions = new List<Vector2Int>();
    for (int x = 0; x < currentCaveSize.x; x++)
    {
        for (int y = 0; y < currentCaveSize.y; y++)
        {
            Vector2Int localPos = new Vector2Int(x, y);
            Vector2Int worldPos = localPos + map.Data.position;
            
            // 只在地板上生成资源
            if (map.GetTile(worldPos)?.Name_ItemName == floorTileName)
            {
                floorPositions.Add(worldPos);
            }
        }
    }
    
    // 打乱位置列表以随机选择
    ShuffleList(floorPositions, rng);
    
    // 为每个位置生成物品
    foreach (Vector2Int pos in floorPositions)
    {
        // 检查此位置是否已被占用
        if (occupiedPositions.Contains(pos))
            continue;
            
        // 先尝试生成宝藏箱（概率较低但优先级高）
        string treasureItem = GetRandomLootItem(treasureLoots, rng);
        if (!string.IsNullOrEmpty(treasureItem))
        {
            positionToItem[pos] = treasureItem;
            occupiedPositions.Add(pos);
            treasureChestPositions.Add(pos); // 记录宝箱位置
            continue;
        }
            
        // 再尝试生成矿石
        string oreItem = GetRandomLootItem(oreLoots, rng);
        if (!string.IsNullOrEmpty(oreItem))
        {
            positionToItem[pos] = oreItem;
            occupiedPositions.Add(pos);
            continue; // 生成了矿石就不再生成装饰物
        }
        
        // 如果没有生成矿石，尝试生成装饰物
        string decorationItem = GetRandomLootItem(decorationLoots, rng);
        if (!string.IsNullOrEmpty(decorationItem))
        {
            positionToItem[pos] = decorationItem;
            occupiedPositions.Add(pos);
        }
    }
    
    // 实际生成普通物品
    foreach (var kvp in positionToItem)
    {
        // 跳过宝箱位置，宝箱单独处理
        if (treasureChestPositions.Contains(kvp.Key))
            continue;
            
        SpawnItem(kvp.Value, kvp.Key, map.ParentObject).Load();
    }
    
    // 实际生成宝箱并初始化其内容
    foreach (Vector2Int treasurePos in treasureChestPositions)
    {
        string treasureItemName = positionToItem[treasurePos];
        Item spawnedTreasure = SpawnItem(treasureItemName, treasurePos, map.ParentObject);
            spawnedTreasure.Load();
            // 初始化宝箱内容
            if (spawnedTreasure != null)
        {
            InitializeTreasureChest(spawnedTreasure, rng);
              }
    }
    
    Debug.Log($"[RandomCaveGeneratorConfig] 总共生成了 {positionToItem.Count} 个物品，其中宝箱 {treasureChestPositions.Count} 个");
}
    
    
    // 辅助方法：初始化宝藏箱
    private void InitializeTreasureChest(Item chestItem, System.Random rng)
    {
        Mod_Inventory inventoryModule = (Mod_Inventory)chestItem.itemMods.GetMod_ByID(ModText.Bag);
        Mod_Building BuildingModule = (Mod_Building)chestItem.itemMods.GetMod_ByID(ModText.Building);
        // 随机选择一个初始化配置
        BuildingModule.SetAsInstalled();
        int randomIndex = rng.Next(0, inventoryinits.Count);
            Inventoryinit selectedInit = inventoryinits[randomIndex];
        inventoryModule.inventory.TryInitializeItems(selectedInit);
        // 留空方法供后续填充
        InitializeTreasureChestContent(chestItem, rng);
    }
    
    // 空白方法供后续填充具体实现
    private void InitializeTreasureChestContent(Item chestItem, System.Random rng)
    {
        // TODO: 在这里实现具体的宝藏箱内容初始化逻辑
        // 需要获取箱子上的Mod_Inventory模块并应用随机配置
        Debug.Log($"[RandomCaveGeneratorConfig] 检测到宝藏箱 {chestItem.name}，需要初始化内容");
    }

    // 辅助方法：从战利品列表中根据概率随机获取一个物品名称
    private string GetRandomLootItem(List<LootEntry> lootEntries, System.Random rng)
    {
        foreach (var loot in lootEntries)
        {
            // 根据掉落概率决定是否生成
            if (rng.NextDouble() < loot.DropChance)
            {
                return loot.LootPrefabName;
            }
        }
        return null;
    }
    
    // 辅助方法：打乱列表
    private void ShuffleList<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    private void GenerateEntranceAndExit(Map map, System.Random rng)
    {
        if (generatedRooms.Count == 0) return;
        
        // 入口放在第一个房间的中心
        Vector2Int entrancePos = generatedRooms[0].center + map.Data.position;
        // 确保入口位置不被其他物品占用
        occupiedPositions.Add(entrancePos);
        SpawnItem(entrancePrefabName, entrancePos, map.ParentObject);
        
        // 出口放在最后一个房间的中心
        Vector2Int exitPos = generatedRooms[generatedRooms.Count - 1].center + map.Data.position;
        // 确保出口位置不被其他物品占用
        occupiedPositions.Add(exitPos);
        SpawnItem(exitPrefabName, exitPos, map.ParentObject);
    }

    // 重写SpawnItem方法以返回生成的物品实例
    protected override Item SpawnItem(string itemName, Vector2Int position, GameObject parentObject)
    {
        Vector2 spawnPos = new Vector2(position.x + 0.5f, position.y + 0.5f);
        
        // 实例化资源物品
        return ItemMgr.Instance.InstantiateItem(
            itemName,
            spawnPos,
            default,
            default,
            parentObject
        );
    }
}