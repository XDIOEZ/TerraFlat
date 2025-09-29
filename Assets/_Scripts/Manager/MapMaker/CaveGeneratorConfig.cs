// RandomCaveGeneratorConfig.cs
using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "RandomCaveConfig", menuName = "Map/Random Cave Generator Config", order = 2)]
public class RandomCaveGeneratorConfig : CaveGeneratorBase
{
    [Header("�󶴳ߴ�")]
    [Tooltip("�󶴵���С��Ⱥ͸߶�")]
    public Vector2Int minCaveSize = new Vector2Int(40, 40);
    [Tooltip("�󶴵�����Ⱥ͸߶�")]
    public Vector2Int maxCaveSize = new Vector2Int(80, 80);
    
    [Header("��������")]
    [Tooltip("�������С�ߴ�")]
    public Vector2Int minRoomSize = new Vector2Int(3, 3);
    [Tooltip("��������ߴ�")]
    public Vector2Int maxRoomSize = new Vector2Int(8, 8);
    [Tooltip("���ɵķ�������")]
    public int roomCount = 15;
    [Tooltip("����֮�����С���")]
    public int roomSpacing = 2;

    [Header("��������")]
    [Tooltip("���ȵ���С���")]
    public int minCorridorWidth = 1;
    [Tooltip("���ȵ������")]
    public int maxCorridorWidth = 3;

    [Header("��Ƭ����")]
    [Tooltip("ǽ����ƬԤ��������")]
    public string wallTileName = "TileItem_StoneWall";
    [Tooltip("������ƬԤ��������")]
    public string floorTileName = "TileItem_Stone";

    [Header("���/��������")]
    [Tooltip("���Ԥ��������")]
    public string entrancePrefabName = "Door";
    [Tooltip("����Ԥ��������")]
    public string exitPrefabName = "Cave";

    [Header("��Դ����")]
    [Tooltip("��ʯս��Ʒ�б�")]
    public List<LootEntry> oreLoots = new List<LootEntry>
    {
        new LootEntry { LootPrefabName = "Mine_Copper", DropChance = 0.003f, MinAmount = 1, MaxAmount = 3 },
        new LootEntry { LootPrefabName = "Mine_Iron", DropChance = 0.005f, MinAmount = 1, MaxAmount = 5 },
        new LootEntry { LootPrefabName = "Mine_Stone", DropChance = 0.01f, MinAmount = 1, MaxAmount = 2 }
    };
    
    [Tooltip("װ����ս��Ʒ�б�")]
    public List<LootEntry> decorationLoots = new List<LootEntry>
    {
        new LootEntry { LootPrefabName = "Twine", DropChance = 0.02f, MinAmount = 1, MaxAmount = 1 },
    };
    
    [Tooltip("����ս��Ʒ�б�")]
    public List<LootEntry> treasureLoots = new List<LootEntry>
    {
        new LootEntry { LootPrefabName = "GoldCoin", DropChance = 0.5f, MinAmount = 1, MaxAmount = 5 },
        new LootEntry { LootPrefabName = "SilverCoin", DropChance = 0.3f, MinAmount = 1, MaxAmount = 3 },
    };

    [Tooltip("���ӱ�����ʱ,ע�����еı�������,���ѡ��һ����ʼ������")]
    public List<Inventoryinit> inventoryinits = new();

    // �ڲ�����
    private Vector2Int currentCaveSize;
    private List<CaveRoom> generatedRooms = new List<CaveRoom>();
    
    // ���ڸ�����������Ʒ��λ��
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

    // �ڱ༭������֤ʱ����
    private void OnValidate()
    {
        // ��֤��ʯս��Ʒ��Ŀ
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
        
        // ��֤װ����ս��Ʒ��Ŀ
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
        
        // ��֤����ս��Ʒ��Ŀ
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
        
        // ��֤�ߴ�����
        if (minCaveSize.x < 10) minCaveSize.x = 10;
        if (minCaveSize.y < 10) minCaveSize.y = 10;
        if (maxCaveSize.x < minCaveSize.x) maxCaveSize.x = minCaveSize.x;
        if (maxCaveSize.y < minCaveSize.y) maxCaveSize.y = minCaveSize.y;
        
        // ��֤��������
        if (minRoomSize.x < 1) minRoomSize.x = 1;
        if (minRoomSize.y < 1) minRoomSize.y = 1;
        if (maxRoomSize.x < minRoomSize.x) maxRoomSize.x = minRoomSize.x;
        if (maxRoomSize.y < minRoomSize.y) maxRoomSize.y = minRoomSize.y;
        if (roomCount < 1) roomCount = 1;
        if (roomSpacing < 0) roomSpacing = 0;
        
        // ��֤��������
        if (minCorridorWidth < 1) minCorridorWidth = 1;
        if (maxCorridorWidth < minCorridorWidth) maxCorridorWidth = minCorridorWidth;
        
        // ��֤��Դ���ɸ��ʣ�ȷ��������100%��
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
            Debug.LogError("[RandomCaveGeneratorConfig] ��ͼ����δ���ã�");
            return;
        }

        ClearMap(map);
        
        // ��ʼ����ռ��λ�ü���
        occupiedPositions = new HashSet<Vector2Int>();

        // ������ɿ󶴳ߴ�
        currentCaveSize = new Vector2Int(
            rng.Next(minCaveSize.x, maxCaveSize.x + 1),
            rng.Next(minCaveSize.y, maxCaveSize.y + 1)
        );

        // ��ʼ����ͼ����
        map.Data.EnvironmentData = new EnvironmentFactors[currentCaveSize.x, currentCaveSize.y];
        
        // �����ֵ�
        Dictionary<string, TileData> tileDataCache = new Dictionary<string, TileData>();
        
        // ���ɷ���
        GenerateRooms(map, tileDataCache, rng);
        
        // ���ӷ���
        ConnectRooms(map, tileDataCache, rng);
        
        // ����ǽ�ں͵���
        GenerateWallsAndFloors(map, tileDataCache);
        
        // ������Դ��װ����
        GenerateResourcesAndDecorations(map, rng);
        
        // ������ںͳ���
        GenerateEntranceAndExit(map, rng);
        
        Debug.Log($"[RandomCaveGeneratorConfig] ��������ɣ��ߴ�: {currentCaveSize.x}x{currentCaveSize.y}");
    }

    private void GenerateRooms(Map map, Dictionary<string, TileData> tileDataCache, System.Random rng)
    {
        generatedRooms.Clear();
        
        for (int i = 0; i < roomCount; i++)
        {
            // ������ɷ���ߴ�
            int width = rng.Next(minRoomSize.x, maxRoomSize.x + 1);
            int height = rng.Next(minRoomSize.y, maxRoomSize.y + 1);
            
            // ������ɷ���λ��
            int x = rng.Next(roomSpacing, currentCaveSize.x - width - roomSpacing);
            int y = rng.Next(roomSpacing, currentCaveSize.y - height - roomSpacing);
            
            RectInt newRoom = new RectInt(x, y, width, height);
            
            // ����Ƿ������������ص�
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
                
                // �ڵ�ͼ�ϱ�Ƿ�������Ϊ�ذ�
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
        
        Debug.Log($"[RandomCaveGeneratorConfig] ������ {generatedRooms.Count} ������");
    }

    private void ConnectRooms(Map map, Dictionary<string, TileData> tileDataCache, System.Random rng)
    {
        if (generatedRooms.Count < 2) return;
        
        // ʹ����С�������㷨���ӷ���
        List<int> connectedRooms = new List<int> { 0 };
        generatedRooms[0].isConnected = true;
        
        while (connectedRooms.Count < generatedRooms.Count)
        {
            int closestRoomIndex = -1;
            int closestConnectedIndex = -1;
            float closestDistance = float.MaxValue;
            
            // Ѱ�������δ���ӷ���
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
                // ������������
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
        
        // ����������ȿ��
        int corridorWidth = rng.Next(minCorridorWidth, maxCorridorWidth + 1);
        
        // ����L����������������������
        // ��ˮƽ�ƶ����ٴ�ֱ�ƶ�
        for (int x = Mathf.Min(center1.x, center2.x); x <= Mathf.Max(center1.x, center2.x); x++)
        {
            // �������ȿ�����ɶ����Ƭ
            for (int wy = 0; wy < corridorWidth; wy++)
            {
                Vector2Int worldPos = new Vector2Int(x, center1.y + wy - corridorWidth/2) + map.Data.position;
                PlaceTile(map, tileDataCache, worldPos, floorTileName);
            }
        }
        
        for (int y = Mathf.Min(center1.y, center2.y); y <= Mathf.Max(center1.y, center2.y); y++)
        {
            // �������ȿ�����ɶ����Ƭ
            for (int wx = 0; wx < corridorWidth; wx++)
            {
                Vector2Int worldPos = new Vector2Int(center2.x + wx - corridorWidth/2, y) + map.Data.position;
                PlaceTile(map, tileDataCache, worldPos, floorTileName);
            }
        }
    }

    private void GenerateWallsAndFloors(Map map, Dictionary<string, TileData> tileDataCache)
    {
        // ��������������
        for (int x = 0; x < currentCaveSize.x; x++)
        {
            for (int y = 0; y < currentCaveSize.y; y++)
            {
                Vector2Int localPos = new Vector2Int(x, y);
                Vector2Int worldPos = localPos + map.Data.position;
                
                // �����ǰλ��û�еذ壬�����ǽ��
                if (map.GetTile(worldPos) == null)
                {
                    PlaceTile(map, tileDataCache, worldPos, wallTileName);
                }
            }
        }
    }

private void GenerateResourcesAndDecorations(Map map, System.Random rng)
{
    // ����һ��λ�õ���Ʒ��ӳ�䣬ȷ��ÿ��λ��ֻ����һ����Ʒ
    Dictionary<Vector2Int, string> positionToItem = new Dictionary<Vector2Int, string>();
    List<Vector2Int> treasureChestPositions = new List<Vector2Int>(); // ר�ż�¼����λ��
    
    // �ռ����п���������Ʒ�ĵذ�λ��
    List<Vector2Int> floorPositions = new List<Vector2Int>();
    for (int x = 0; x < currentCaveSize.x; x++)
    {
        for (int y = 0; y < currentCaveSize.y; y++)
        {
            Vector2Int localPos = new Vector2Int(x, y);
            Vector2Int worldPos = localPos + map.Data.position;
            
            // ֻ�ڵذ���������Դ
            if (map.GetTile(worldPos)?.Name_ItemName == floorTileName)
            {
                floorPositions.Add(worldPos);
            }
        }
    }
    
    // ����λ���б������ѡ��
    ShuffleList(floorPositions, rng);
    
    // Ϊÿ��λ��������Ʒ
    foreach (Vector2Int pos in floorPositions)
    {
        // ����λ���Ƿ��ѱ�ռ��
        if (occupiedPositions.Contains(pos))
            continue;
            
        // �ȳ������ɱ����䣨���ʽϵ͵����ȼ��ߣ�
        string treasureItem = GetRandomLootItem(treasureLoots, rng);
        if (!string.IsNullOrEmpty(treasureItem))
        {
            positionToItem[pos] = treasureItem;
            occupiedPositions.Add(pos);
            treasureChestPositions.Add(pos); // ��¼����λ��
            continue;
        }
            
        // �ٳ������ɿ�ʯ
        string oreItem = GetRandomLootItem(oreLoots, rng);
        if (!string.IsNullOrEmpty(oreItem))
        {
            positionToItem[pos] = oreItem;
            occupiedPositions.Add(pos);
            continue; // �����˿�ʯ�Ͳ�������װ����
        }
        
        // ���û�����ɿ�ʯ����������װ����
        string decorationItem = GetRandomLootItem(decorationLoots, rng);
        if (!string.IsNullOrEmpty(decorationItem))
        {
            positionToItem[pos] = decorationItem;
            occupiedPositions.Add(pos);
        }
    }
    
    // ʵ��������ͨ��Ʒ
    foreach (var kvp in positionToItem)
    {
        // ��������λ�ã����䵥������
        if (treasureChestPositions.Contains(kvp.Key))
            continue;
            
        SpawnItem(kvp.Value, kvp.Key, map.ParentObject).Load();
    }
    
    // ʵ�����ɱ��䲢��ʼ��������
    foreach (Vector2Int treasurePos in treasureChestPositions)
    {
        string treasureItemName = positionToItem[treasurePos];
        Item spawnedTreasure = SpawnItem(treasureItemName, treasurePos, map.ParentObject);
            spawnedTreasure.Load();
            // ��ʼ����������
            if (spawnedTreasure != null)
        {
            InitializeTreasureChest(spawnedTreasure, rng);
              }
    }
    
    Debug.Log($"[RandomCaveGeneratorConfig] �ܹ������� {positionToItem.Count} ����Ʒ�����б��� {treasureChestPositions.Count} ��");
}
    
    
    // ������������ʼ��������
    private void InitializeTreasureChest(Item chestItem, System.Random rng)
    {
        Mod_Inventory inventoryModule = (Mod_Inventory)chestItem.itemMods.GetMod_ByID(ModText.Bag);
        Mod_Building BuildingModule = (Mod_Building)chestItem.itemMods.GetMod_ByID(ModText.Building);
        // ���ѡ��һ����ʼ������
        BuildingModule.SetAsInstalled();
        int randomIndex = rng.Next(0, inventoryinits.Count);
            Inventoryinit selectedInit = inventoryinits[randomIndex];
        inventoryModule.inventory.TryInitializeItems(selectedInit);
        // ���շ������������
        InitializeTreasureChestContent(chestItem, rng);
    }
    
    // �հ׷���������������ʵ��
    private void InitializeTreasureChestContent(Item chestItem, System.Random rng)
    {
        // TODO: ������ʵ�־���ı��������ݳ�ʼ���߼�
        // ��Ҫ��ȡ�����ϵ�Mod_Inventoryģ�鲢Ӧ���������
        Debug.Log($"[RandomCaveGeneratorConfig] ��⵽������ {chestItem.name}����Ҫ��ʼ������");
    }

    // ������������ս��Ʒ�б��и��ݸ��������ȡһ����Ʒ����
    private string GetRandomLootItem(List<LootEntry> lootEntries, System.Random rng)
    {
        foreach (var loot in lootEntries)
        {
            // ���ݵ�����ʾ����Ƿ�����
            if (rng.NextDouble() < loot.DropChance)
            {
                return loot.LootPrefabName;
            }
        }
        return null;
    }
    
    // ���������������б�
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
        
        // ��ڷ��ڵ�һ�����������
        Vector2Int entrancePos = generatedRooms[0].center + map.Data.position;
        // ȷ�����λ�ò���������Ʒռ��
        occupiedPositions.Add(entrancePos);
        SpawnItem(entrancePrefabName, entrancePos, map.ParentObject);
        
        // ���ڷ������һ�����������
        Vector2Int exitPos = generatedRooms[generatedRooms.Count - 1].center + map.Data.position;
        // ȷ������λ�ò���������Ʒռ��
        occupiedPositions.Add(exitPos);
        SpawnItem(exitPrefabName, exitPos, map.ParentObject);
    }

    // ��дSpawnItem�����Է������ɵ���Ʒʵ��
    protected override Item SpawnItem(string itemName, Vector2Int position, GameObject parentObject)
    {
        Vector2 spawnPos = new Vector2(position.x + 0.5f, position.y + 0.5f);
        
        // ʵ������Դ��Ʒ
        return ItemMgr.Instance.InstantiateItem(
            itemName,
            spawnPos,
            default,
            default,
            parentObject
        );
    }
}