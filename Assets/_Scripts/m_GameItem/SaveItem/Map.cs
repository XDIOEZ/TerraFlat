using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Map : Item,ISave_Load
{
    [Header("��ͼ����")]
    [SerializeField]
    public TileMapData tileMapData;

    [Header("Tilemap ���")]
    [SerializeField]
    public Tilemap tileMap;

    [Header("ʵ����������߽紫�͵�")]
    public List<WorldEdge> worldEdges;

    // ǿ������ת�����ԣ���������� Item �ļ��ݣ�
    public override ItemData Item_Data { get => tileMapData; set => tileMapData = value as TileMapData; }
    public UltEvent onSave { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public UltEvent onLoad { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

    public override void Act()
    {
        throw new System.NotImplementedException();
    }
    [Button("�����ݼ��ص�ͼ")]
    public void Load()
    {
        print("���ص�ǰ������Tilemap");
        PullDataToTilemap();

       /* if (worldEdges.Count > 0)
        {*/
            foreach (var edgeData in tileMapData.WorldEdgeDatas)
            {
                GameObject edgeObj = GameRes.Instance.InstantiatePrefab(edgeData.Name);
                edgeObj.GetComponent<WorldEdge>().Data = edgeData;
                edgeObj.GetComponent<ISave_Load>().Load();
                worldEdges.Add(edgeObj.GetComponent<WorldEdge>());
                edgeObj.transform.parent = transform;
            }
       /* }*/
        
    }
    [Button("�����ͼ������")]
    public void Save()
    {
        if (worldEdges.Count > 0)
        {
            foreach (var edge in worldEdges)
            {
                edge.GetComponent<ISave_Load>().Save();
                edge.gameObject.SetActive(false);
                tileMapData.WorldEdgeDatas.Add(edge.Data);
                Destroy(edge.gameObject);
            }
        }
        else
        {
            WorldEdge[] AllWorldEdges = GetComponentsInChildren<WorldEdge>();
            foreach (var edge in AllWorldEdges)
            {
                edge.GetComponent<ISave_Load>().Save();
                tileMapData.WorldEdgeDatas.Add(edge.Data);
                edge.gameObject.SetActive(false);
                SaveAndLoad.Instance.GameObject_False.Add(edge.gameObject);
               // Destroy(edge.gameObject);
            }
        }




            print("���浱ǰ������tilemap");
        SaveTilemapData();
    }
    // --- Odin ��ť���� ---

    public void PullDataToTilemap()
    {
        foreach (var kvp in tileMapData.Data)
        {
            string tileName = kvp.Key;
            List<Vector2Int> positions = kvp.Value;

            // ͨ�����ƻ�ȡ TileBase ����
            TileBase tile = GameRes.Instance.GetTileBase(tileName);
            if (tile == null)
            {
                Debug.LogError($"�޷����� Tile: {tileName}");
                continue;
            }

            // �����������겢���� Tile
            foreach (Vector2Int pos2D in positions)
            {
                Vector3Int pos3D = new Vector3Int(pos2D.x, pos2D.y, 0);
                tileMap.SetTile(pos3D, tile);
            }
        }
    }




    public void SaveTilemapData()
    {
        // ������ʱ�ֵ�洢 <Tile����, �����б�>
        var tempData = new Dictionary<string, List<Vector2Int>>();

        // ��ȡ Tilemap �İ�Χ�з�Χ
        BoundsInt bounds = tileMap.cellBounds;

        // �������п��ܵ����꣨Vector3Int��
        foreach (Vector3Int pos3D in bounds.allPositionsWithin)
        {
            TileBase currentTile = tileMap.GetTile(pos3D);
            if (currentTile == null) continue;

            // ת��Ϊ Vector2Int������ Z �ᣩ
            Vector2Int pos2D = new Vector2Int(pos3D.x, pos3D.y);
            string tileName = currentTile.name;

            // ��������ӵ���Ӧ���Ƶ��б���
            if (!tempData.ContainsKey(tileName))
            {
                tempData[tileName] = new List<Vector2Int>();
            }
            tempData[tileName].Add(pos2D);
        }

        // ֱ�Ӹ�ֵ�����е� tileMapData�����ⴴ���¶���
        tileMapData.Data = tempData;
    }
}