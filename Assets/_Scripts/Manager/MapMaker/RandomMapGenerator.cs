using System;
using UnityEngine;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using Force.DeepCloner;
using UltEvents;
using NUnit;

/// <summary>
/// �����ͼ����������������������Ⱥϵ��Biome��
/// ֧�ַ�֡���ɡ����ͼ�޷��νӣ��Լ�Ⱥϵ��Դ��������ɡ�
/// </summary>
public class RandomMapGenerator : MonoBehaviour
{
    #region ���ò���
    [Header("��ͼ����")]
    [Required]
    public Map map;  // ��ͼ������󣬰��� TileData �� Tilemap ����

    [Header("��������")]
    [Tooltip("��������ϵ����ԽС����Խ��Χ�ĸߵ����")]
    public float noiseScale = 0.01f;

    [Tooltip("½�غ��������½��ռ�ȣ�����ռ��")]
    public float landOceanRatio = 0.5f;

    [Tooltip("�¶�ƫ��")]
    public float Temp = 0.5f;
    [Tooltip("��ǰ����")]
    public float Longitude = 0.5f;
    [Tooltip("��ǰ����")]
    public float LongitudeRate = 0.5f;

    [Header("����Ⱥϵ�б�")]
    [Tooltip("��ͬ�¶�/ʪ�ȶ�Ӧ������Ⱥϵ����")]
    public List<BiomeData> biomes;

    [Header("����ѡ��")]
    [Tooltip("ÿ֡���ɵ����ؿ��� (0=ȫ����������)")]
    public int tilesPerFrame = 100;

    [Header("�߽�����")]
    [Tooltip("�Ƿ����������ڵ�ͼ������޷�Խ�")]
    public bool seamlessBorders = true;

    [Header("�߽���ɷ�Χ")]
    [Range(1, 20)]
    [Tooltip("�����޷�����ʱ�ı߽��Ͽ�ȣ���������")]
    public int transitionRange = 5;

    [Header("����¼�")]
    [Tooltip("��ͼ��ȫ���ɺ󴥷����¼������� Inspector �ҽ��Զ�����Ӧ")]
    public UltEvent onMapGenerated = new UltEvent();

    /// <summary>
    /// ��ͼ�ߴ磬��ȡ��ȫ�ִ浵
    /// </summary>
    public Vector2Int MapSize => SaveAndLoad.Instance.SaveData.MapSize;
    #endregion

    #region �ڲ�����
    /// <summary>
    /// ��ͼ���ӣ��ַ�����ʽ���Ӵ浵��ȡ
    /// </summary>
    private string Seed => SaveAndLoad.Instance.SaveData.MapSeed;

    /// <summary>
    /// ��������ֵ�����ڳ�ʼ�������
    /// </summary>
    private int seed;

    /// <summary>
    /// ϵͳ���ɸ������ʵ����������Դ����
    /// </summary>
    private System.Random rng;

    /// <summary>
    /// ���� TileData ģ�壬�����ظ����ؿ���
    /// </summary>
    private Dictionary<string, TileData> tileDataCache = new Dictionary<string, TileData>();
    #endregion

    #region Unity��������
    private void Start()
    {
        if (map.Data.TileData.Count <= 0)
            GenerateRandomMap();
    }
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (map == null || map.Data == null) return;

        Vector2Int startPos = map.Data.position;
        Vector2Int size = MapSize;
        Vector3 center = new Vector3(startPos.x + size.x / 2f, startPos.y + size.y / 2f, 0f);
        Vector3 size3D = new Vector3(size.x, size.y, 0.1f);

        // ���Ƶ�ͼ�߿���ɫ��ߣ�
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, size3D);

        // ��ѡ����ʾ��ͼ��Ϣ����������ɫ��
        UnityEditor.Handles.Label(center,
            $"Map:{startPos}\nSize:{size}",
            new GUIStyle { alignment = TextAnchor.MiddleCenter });
    }
#endif
    #endregion

    #region ��ͼ�������߼�
    [Button("���������ͼ")]
    public void GenerateRandomMap()
    {
        if (map == null)
        {
            Debug.LogError("[RandomMapGenerator] ��ͼ����δ���ã�");
            return;
        }

        // ��ʼ��������Ӳ�����ϵͳ���ʵ��
        seed = string.IsNullOrEmpty(Seed) ? DateTime.Now.GetHashCode() : Seed.GetHashCode();
        UnityEngine.Random.InitState(seed);
        rng = new System.Random(seed);

        ClearMap();
        map.Data.position = SaveAndLoad.Instance.SaveData.ActiveMapPos;

        Vector2Int startPos = map.Data.position;
        Vector2Int size = MapSize;

        if (tilesPerFrame > 0)
            StartCoroutine(GenerateMapCoroutine(startPos, size));
        else
        {
            GenerateAllTiles(startPos, size);
            OnGenerationComplete();
        }

        GenerateMapEdges();
    }

    #endregion

    #region �߽�����
    public void GenerateMapEdges()
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var d in dirs)
        {
            Item edge = RunTimeItemManager.Instance.InstantiateItem("MapEdge");
            if (edge is WorldEdge we)
                we.SetupMapEdge(d);
            else
                Debug.LogError("[RandomMapGenerator] �߽�������ʹ���");
        }
        Debug.Log("[RandomMapGenerator] �߽��������");
    }
    #endregion

    #region ��������
    private IEnumerator GenerateMapCoroutine(Vector2Int startPos, Vector2Int size)
    {
        int processed = 0;
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                GenerateTileAtPosition(new Vector2Int(startPos.x + x, startPos.y + y));
                if (++processed % tilesPerFrame == 0)
                    yield return null;
            }
        OnGenerationComplete();
    }

    private void GenerateAllTiles(Vector2Int startPos, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
                GenerateTileAtPosition(new Vector2Int(startPos.x + x, startPos.y + y));
    }
    #endregion

    #region �ؿ������߼� (���� TODO)
    private void GenerateTileAtPosition(Vector2Int position)
    {
        // 1. ������������
        float gx = position.x * noiseScale;
        float gy = position.y * noiseScale;

        // Fixing the CS0019 error by ensuring consistent types for the operation
        float temp = Mathf.Clamp01(Mathf.PerlinNoise(gx + 2000, gy + 2000) + Temp);
        float humid = Mathf.Clamp01(Mathf.PerlinNoise(gx + 5000f, gy + 5000f));
        float precipitation = Mathf.Clamp01(  Mathf.PerlinNoise(gx + 10000f, gy + 10000f)  );
        float solidityNoise = Mathf.Clamp01(  Mathf.PerlinNoise(gx + 20000f, gy + 20000f)+landOceanRatio);
        float hightNoise = Mathf.Clamp01(Mathf.PerlinNoise(gx + 220000f, gy + 220000f));

        EnvironmentFactors env = new EnvironmentFactors
        {
            Temperature = temp,
            Humidity = humid,
            Precipitation = precipitation,
            Solidity = solidityNoise,
            Hight = hightNoise
        };


        // 3. Biome ƥ�䣨��Ϊѭ���汾��
        BiomeData biome = null;
        foreach (var b in biomes)
        {
            if (b.IsEnvironmentValid(env))
            {
                biome = b;
                break;  // �ҵ���һ��ƥ��ľ��˳�ѭ��
            }
        }
        // ����Ҳ������ʵ�Biome��������������
        if (biome == null)
        {
            Debug.LogWarning(env);
            return;
        }

        // 4. ���ɵ�����Ƭ
        GenerateTerrainTile(position, biome, env);

        // 5. ���������Դ
        GenerateRandomResources(position, biome, env);
    }


    /// <summary>
    /// ���ɵ�����Ƭ (ԭ����3)
    /// </summary>
    private void GenerateTerrainTile(Vector2Int position, BiomeData biome, EnvironmentFactors env)
    {
        // ��ȡ�ؿ�Ԥ����
        string key = biome.TerrainConfig.GetTilePrefab(env);

        // ��ȡ�򻺴�TileDataģ��
        if (!tileDataCache.ContainsKey(key))
        {

            string name = biome.TerrainConfig.GetTilePrefab(env);

            var prefab = GameRes.Instance.GetPrefab(name);
            if (prefab == null)
            {
                Debug.LogError($"�޷���ȡԤ����: {name}������Ⱥϵ: {biome.BiomeName}");
                return;
            }

            var blockTile = prefab.GetComponent<IBlockTile>();
            if (blockTile == null)
            {
                Debug.LogError($"Ԥ���� {name} ��δ�ҵ� IBlockTile ���������Ⱥϵ: {biome.BiomeName}");
                return;
            }

            tileDataCache[key] = blockTile.TileData;
        }

        // ������������Ƭ
        var tile = tileDataCache[key].DeepClone();
        tile.position = new Vector3Int(position.x, position.y, 0);
        map.ADDTile(position, tile);
    }

    /// <summary>
    /// ���������Դ (ԭ����4)
    /// </summary>
    private void GenerateRandomResources(Vector2Int position, BiomeData biome, EnvironmentFactors env)
    {
        foreach (var spawn in biome.TerrainConfig.ItemSpawn)
        {
            if (rng.NextDouble() <= spawn.SpawnChance)
            {
                Vector3 spawnPosition = new Vector3(
                    position.x + 0.5f,
                    position.y + 0.5f,
                    0f);

                RunTimeItemManager.Instance.InstantiateItem(
                    spawn.itemName,
                    spawnPosition);
            }
        }
    }
    #endregion

    #region ���߷��������������¼�
    private void ClearMap()
    {
        map.tileMap?.ClearAllTiles();
        map.Data.TileData?.Clear();
        Debug.Log("[RandomMapGenerator] ��ͼ�����");
    }

    private void OnGenerationComplete()
    {
        map.tileMap?.RefreshAllTiles();
        Debug.Log($"[RandomMapGenerator] ��ͼ��� @ {map.Data.position} ��С{map.Data.size} ����{seed}");
        onMapGenerated?.Invoke();
    }
    #endregion
}
