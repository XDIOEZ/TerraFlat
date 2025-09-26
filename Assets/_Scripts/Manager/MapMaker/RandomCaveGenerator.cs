using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Sirenix.OdinInspector;
using Force.DeepCloner;

/// <summary>
/// �������������
/// - ���ڷ�������ȵ���������
/// - ֧�ֲַ�ṹ�Ͳ�ͬ���͵ķ���
/// - ֧����Դ���ɺ�װ�������
/// - ֧�ֱ߽紦������/��������
/// </summary>
public class RandomCaveGenerator : MonoBehaviour
{
    #region ���ò���
    [Header("������")]
    [Required] public Map map; // ��ͼ�������
    [Tooltip("����ѡ���ֶ�ָ��Grid�����δָ�����Զ��ӵ�ǰ����/�Ӷ����ȡ")]
    public Grid mapGrid;
    [Tooltip("����ѡ���ֶ�ָ��Tilemap�����δָ�����Զ��ӵ�ǰ������Ӷ����ȡ")]
    public Tilemap targetTilemap;

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
    public string wallTileName = "CaveWall";
    [Tooltip("������ƬԤ��������")]
    public string floorTileName = "CaveFloor";

    [Header("���/��������")]
    [Tooltip("���Ԥ��������")]
    public string entrancePrefabName = "CaveEntrance";
    [Tooltip("����Ԥ��������")]
    public string exitPrefabName = "CaveExit";

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
        new LootEntry { LootPrefabName = "Stalagmite", DropChance = 0.2f, MinAmount = 1, MaxAmount = 1 },
        new LootEntry { LootPrefabName = "Crystal", DropChance = 0.1f, MinAmount = 1, MaxAmount = 1 }
    };

    [Header("����ѡ��")]
    [Tooltip("ÿ֡���ɵ������Ƭ�� (0=��������)")]
    public int tilesPerFrame = 10;

    [Header("�߽�����")]
    public bool generateEdges = true;
    #endregion

    #region �ڲ�����
    private int Seed => SaveDataMgr.Instance.SaveData.Seed;
    private Vector2Int currentCaveSize; // ��ǰ�󶴳ߴ�

    public static System.Random rng; // ϵͳ�����ʵ��

    private Dictionary<string, TileData> tileDataCache = new(); // TileData ����
    private List<CaveRoom> generatedRooms = new List<CaveRoom>(); // �����ɵķ����б�
    #endregion

    #region ���ݽṹ
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

    #region Unity ��������
    public void Awake()
    {
        rng = new System.Random(Seed);

        map.OnMapGenerated_Start += GenerateRandomCave;

        // 1. �Զ���ȡGrid��������ȼ����ֶ�ָ�� > ��ǰ���� > �Ӷ���
        if (mapGrid == null)
        {
            mapGrid = GetComponent<Grid>();
            if (mapGrid == null)
            {
                mapGrid = GetComponentInChildren<Grid>(includeInactive: false);
            }
        }

        // 2. �Զ���ȡ��ǰ������Ӷ���Tilemap
        if (targetTilemap == null)
        {
            Tilemap[] childTilemaps = GetComponentsInChildren<Tilemap>(includeInactive: false);
            if (childTilemaps != null && childTilemaps.Length > 0)
            {
                targetTilemap = childTilemaps[0];
                Debug.Log($"[RandomCaveGenerator] �Զ���ȡ�Ӷ���Tilemap��{targetTilemap.name}");
            }
            else
            {
                Debug.LogError($"[RandomCaveGenerator] ��ǰ������δ�ҵ��κ�Tilemap�Ӷ���");
            }
        }
    }
    #endregion

    #region ���߼�
    [Button("���������")]
    public void GenerateRandomCave()
    {
        if (map == null)
        {
            Debug.LogError("[RandomCaveGenerator] ��ͼ����δ���ã�");
            return;
        }

        ClearMap();

        map.Data.position = new Vector2Int(
            Mathf.RoundToInt(transform.parent.position.x),
            Mathf.RoundToInt(transform.parent.position.y)
        );

        // ������ɿ󶴳ߴ�
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

    #region ����������
    private IEnumerator GenerateCaveCoroutine()
    {
        GenerateAllCaveTiles();
        yield return null;
        OnGenerationComplete();
    }

    private void GenerateAllCaveTiles()
    {
        // ��ʼ����ͼ����
        map.Data.EnvironmentData = new EnvironmentFactors[currentCaveSize.x, currentCaveSize.y];
        
        // ���ɷ���
        GenerateRooms();
        
        // ���ӷ���
        ConnectRooms();
        
        // ��������
        GenerateCorridors();
        
        // ����ǽ�ں͵���
        GenerateWallsAndFloors();
        
        // ������Դ��װ����
        GenerateResourcesAndDecorations();
        
        // ������ںͳ���
        GenerateEntranceAndExit();
        
        // ���ɱ߽�
        if (generateEdges)
        {
            //GenerateCaveEdges();
        }
    }
    #endregion

    #region ���������߼�
    private void GenerateRooms()
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
                        PlaceTile(worldPos, floorTileName);
                    }
                }
            }
        }
        
        Debug.Log($"[RandomCaveGenerator] ������ {generatedRooms.Count} ������");
    }

    private void ConnectRooms()
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
                PlaceTile(worldPos, floorTileName);
            }
        }
        
        for (int y = Mathf.Min(center1.y, center2.y); y <= Mathf.Max(center1.y, center2.y); y++)
        {
            // �������ȿ�����ɶ����Ƭ
            for (int wx = 0; wx < corridorWidth; wx++)
            {
                Vector2Int worldPos = new Vector2Int(center2.x + wx - corridorWidth/2, y) + map.Data.position;
                PlaceTile(worldPos, floorTileName);
            }
        }
    }

    private void GenerateCorridors()
    {
        // �����Ѿ������ӷ���ʱ������
        // ���������Ӷ�������������߼�
    }

    private void GenerateWallsAndFloors()
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
                    PlaceTile(worldPos, wallTileName);
                }
            }
        }
    }
    #endregion

    #region ��Դ��װ��������
    private void GenerateResourcesAndDecorations()
    {
        // �ڵذ���������ɿ�ʯ��װ����
        for (int x = 0; x < currentCaveSize.x; x++)
        {
            for (int y = 0; y < currentCaveSize.y; y++)
            {
                Vector2Int localPos = new Vector2Int(x, y);
                Vector2Int worldPos = localPos + map.Data.position;
                
                // ֻ�ڵذ���������Դ
                if (map.GetTile(worldPos)?.Name_ItemName == floorTileName)
                {
                    // ���ɿ�ʯ
                    SpawnLootItems(oreLoots, worldPos);
                    
                    // ����װ����
                    SpawnLootItems(decorationLoots, worldPos);
                }
            }
        }
    }
    #endregion

    #region ��ڳ�������
    private void GenerateEntranceAndExit()
    {
        if (generatedRooms.Count == 0) return;
        
        // ��ڷ��ڵ�һ�����������
        Vector2Int entrancePos = generatedRooms[0].center + map.Data.position;
        SpawnEntranceOrExit(entrancePrefabName, entrancePos);
        
        // ���ڷ������һ�����������
        Vector2Int exitPos = generatedRooms[generatedRooms.Count - 1].center + map.Data.position;
        SpawnEntranceOrExit(exitPrefabName, exitPos);
    }
    #endregion

    #region �߽�����
    private void GenerateCaveEdges()
    {
        // ����߽�ǽ�ڵ����ź�λ�ò���
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
                
                // �����߽�ǽ�ڵĴ�С��λ��
                if (d == Vector2Int.up || d == Vector2Int.down)
                {
                    // ˮƽǽ��
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
                    // ��ֱǽ�� (��Ҫ��ת90��)
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
                Debug.LogError("[RandomCaveGenerator] �߽�������ʹ���ӦΪWorldEdge");
            }
        }
        Debug.Log("[RandomCaveGenerator] �󶴱߽��������");
    }
    #endregion

    #region ���߷���
    private void PlaceTile(Vector2Int position, string tileName)
    {
        // ����TileData�����ظ�����
        if (!tileDataCache.ContainsKey(tileName))
        {
            var prefab = GameRes.Instance.GetPrefab(tileName);
            if (prefab == null)
            {
                Debug.LogError($"�޷���ȡԤ����: {tileName}");
                return;
            }

            var blockTile = prefab.GetComponent<IBlockTile>();
            if (blockTile == null)
            {
                Debug.LogError($"Ԥ���� {tileName} ȱ�� IBlockTile ���");
                return;
            }

            tileDataCache[tileName] = blockTile.TileData;
        }

        // ��¡�����Tile����ͼ
        var tile = tileDataCache[tileName].DeepClone();
        tile.position = new Vector3Int(position.x, position.y, 0);
        map.ADDTile(position, tile);
    }

    private void SpawnEntranceOrExit(string prefabName, Vector2Int position)
    {
        Vector2 spawnPos = new Vector2(position.x + 0.5f, position.y + 0.5f);
        
        // ʵ������ڻ����Ԥ����
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
        
        // ʵ������Դ��Ʒ
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
            // ���ݵ�����ʾ����Ƿ�����
            if (rng.NextDouble() < loot.DropChance)
            {
                // �����������
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
        Debug.Log("[RandomCaveGenerator] �󶴵�ͼ�����");
    }

    private void OnGenerationComplete()
    {
        map.tileMap?.RefreshAllTiles();
        map.Data.TileLoaded = true;
        map.BackTilePenalty_Async();
        Debug.Log($"[RandomCaveGenerator] ��������ɣ��ߴ�: {currentCaveSize.x}x{currentCaveSize.y}");
    }
    #endregion
}