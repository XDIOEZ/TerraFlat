using System;
using UnityEngine;
using Sirenix.OdinInspector;
using System.Collections;
using Force.DeepCloner;
using UltEvents;
using System.Collections.Generic;

public class RandomMapGenerator : MonoBehaviour
{
    #region ���ò���
    [Header("��ͼ����")]
    [Required]
    public Map map;

    [Header("��������")]
    [Range(0.001f, 0.05f)]
    public float noiseScale = 0.01f;

    [Header("������ֵ")]
    [Range(0f, 1f)]
    public float waterThreshold = 0.3f;
    [Range(0f, 1f)]
    public float mountainThreshold = 0.7f;

    [Header("����ѡ��")]
    [Tooltip("ÿ֡���ɵ����ؿ��� (0=ȫ����������)")]
    public int tilesPerFrame = 100;

    [Header("�߽�����")]
    [Tooltip("ȷ�������ڵ�ͼ�޷�����")]
    public bool seamlessBorders = true;

    [Header("�߽���ɷ�Χ")]
    [Range(1, 20)]
    public int transitionRange = 5;

    [Header("����¼�")]
    [Tooltip("��ͼ������ɺ󴥷�")]
    public UltEvent onMapGenerated = new UltEvent();
    #endregion

    #region �ڲ�����
    // ���ɵ�ͼ������
    private string Seed => SaveAndLoad.Instance.SaveData.MapSeed;
    private int seed;
    #endregion

    #region Unity��������
    void Start()
    {
        if (map.Data.TileData.Count <= 0)
           GenerateRandomMap();
    }




#if UNITY_EDITOR
    // �ڱ༭���п��ӻ���ͼ�߽�
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

        // ���Ƶ�ͼλ�ñ�ǩ
        UnityEditor.Handles.Label(center, $"Map: {startPos}\nSize: {size}", new GUIStyle
        {
            normal = { textColor = Color.green },
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter
        });
    }
#endif
    #endregion

    #region ��ͼ�������߼�
    [Button("���������ͼ")]
    public void GenerateRandomMap()
    {
        if (map == null)
        {
            Debug.LogError("��ͼ����δ���ã�");
            return;
        }

        // ��ʼ���������
        seed = string.IsNullOrEmpty(Seed) ? DateTime.Now.GetHashCode() : Seed.GetHashCode();
        UnityEngine.Random.InitState(seed);

        // ������е�ͼ
        ClearMap();

        // ��ȡ��ͼ��Χ�ʹ�С
        Vector2Int startPos = map.Data.position;
        Vector2Int size = map.Data.size;

        if (tilesPerFrame > 0)
        {
            // ��֡����
            StartCoroutine(GenerateMapCoroutine(startPos, size));
        }
        else
        {
            // ��������
            GenerateAllTiles(startPos, size);
            OnGenerationComplete();
        }

        //�������
        GenerateMapEdges();
    }
    #endregion

    public void GenerateMapEdges()
    {
        // �����ĸ�����
        Vector2Int[] directions = new Vector2Int[]
        {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
        };

        // Ϊÿ���������ɱ߽�
        foreach (Vector2Int direction in directions)
        {
            Item edgeItem = RunTimeItemManager.Instance.InstantiateItem("MapEdge");
            if (edgeItem is WorldEdge worldEdge)
            {
                worldEdge.SetupMapEdge(direction);

                // ������Ϊ�Ӷ��󣬱��ֶ���
                // worldEdge.transform.SetParent(transform);
            }
            else
            {
                Debug.LogError("ʵ�����Ķ�����WorldEdge����!");
            }
        }

        Debug.Log("��ͼ�߽��������");
    }

    #region Э������
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

                // ÿ֡����һ�������ĵؿ�
                if (processed % tilesPerFrame == 0)
                {
                    yield return null; // �ȴ���һ֡
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

    #region �ؿ������߼�
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
            Debug.LogError($"�޷���ȡ�ݵ�Tile: {itemName}");
            return;
        }

        TileData_Grass tileData = blockTile.TileData.DeepClone() as TileData_Grass;
        if (tileData == null)
        {
            Debug.LogError($"TileData���ǲݵ�����: {itemName}");
            return;
        }

        // ����������ֶ� (0.3 - 0.8)
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
            Debug.LogError($"�޷���ȡˮ��Tile: {itemName}");
            return;
        }

        TileData_Water tileData = blockTile.TileData.DeepClone() as TileData_Water;
        if (tileData == null)
        {
            Debug.LogError($"TileData����ˮ������: {itemName}");
            return;
        }

        // ���������� (0.5 - 3.0)
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
            Debug.LogError($"�޷���ȡɽ��Tile: {itemName}");
            return;
        }

        TileData tileData = blockTile.TileData.DeepClone();
        tileData.Name_TileBase = "TileBase_Mountain";
        tileData.Name_ItemName = itemName;
        tileData.position = new Vector3Int(position.x, position.y, 0);

        // ��������Ĳ��ʱ�� (5-15��)
        tileData.DemolitionTime = Mathf.Lerp(5f, 15f, (noiseValue - mountainThreshold) / (1 - mountainThreshold));

        map.ADDTile(position, tileData);
    }
    #endregion

    #region ��������
    private float CalculateSeamlessNoise(Vector2Int position)
    {
        // ʹ��ȫ�������������
        float globalX = position.x * noiseScale;
        float globalY = position.y * noiseScale;

        // ��Ӷ���������㴴�����Ȼ�ĵ���
        float baseNoise = Mathf.PerlinNoise(globalX, globalY);
        float detailNoise = Mathf.PerlinNoise(globalX * 2.5f + 1000, globalY * 2.5f + 1000) * 0.2f;
        float ridgeNoise = Mathf.PerlinNoise(globalX * 0.5f + 2000, globalY * 0.5f + 2000) * 0.3f;

        // �������
        float combinedNoise = baseNoise + detailNoise - ridgeNoise;

        // ��׼����0-1��Χ
        return Mathf.Clamp01(combinedNoise);
    }
    #endregion
    #region ���߷���
    private void ClearMap()
    {
        // ���Tilemap��ʾ
        if (map.tileMap != null)
        {
            map.tileMap.ClearAllTiles(); 
        }

        // �������
        if (map.Data != null && map.Data.TileData != null)
        {
            map.Data.TileData.Clear();
        }

        Debug.Log("��ͼ�����");
    }

    private void OnGenerationComplete()
    {
        // ȷ�����и��Ķ�ͬ����Tilemap
        map.tileMap.RefreshAllTiles();
        Debug.Log($"��ͼ�������! λ��: {map.Data.position}, ��С: {map.Data.size}, ����: {seed}");

        // ��������¼�
        onMapGenerated?.Invoke();
    }
    #endregion
}