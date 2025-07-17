using System;
using UnityEngine;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using Force.DeepCloner;
using UltEvents;

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

/*    [Header("��������")]
    [Tooltip("��������ϵ����ԽС����Խ��Χ�ĸߵ����")]
    private float noiseScale = 0.01f;

    [Tooltip("½�غ��������½��ռ�ȣ�����ռ��")]
    private float landOceanRatio = 0.5f;

    [Tooltip("�¶�ƫ��")]
    private float temp = 0.0f;

    [Tooltip("����뾶")]
    private float plantRadius = 100;*/

    [Tooltip("�������")]
    public float Equator = 0;

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

    //Debugʹ��
    public Dictionary<Vector2Int,Color> ColorDicitionary  = new ();
    #endregion

    #region �ڲ�����
    /// <summary>
    /// ��ͼ���ӣ��ַ�����ʽ���Ӵ浵��ȡ
    /// </summary>
    private int Seed => SaveAndLoad.Instance.SaveData.Seed;

    public float PlantRadius { get => SaveAndLoad.Instance.SaveData.Active_PlanetData.Radius;}
    public float Temp { get => SaveAndLoad.Instance.SaveData.Active_PlanetData.TemperatureOffset; }
    public float LandOceanRatio { get => SaveAndLoad.Instance.SaveData.Active_PlanetData.OceanHeight;}
    public float NoiseScale { get => SaveAndLoad.Instance.SaveData.Active_PlanetData.NoiseScale;  }

    /// <summary>
    /// ϵͳ���ɸ������ʵ����������Դ����
    /// </summary>
    public static System.Random  rng;

    /// <summary>
    /// ���� TileData ģ�壬�����ظ����ؿ���
    /// </summary>
    private Dictionary<string, TileData> tileDataCache = new Dictionary<string, TileData>();
    #endregion

    #region Unity��������
    private void Start()
    {
        rng = new System.Random(SaveAndLoad.Instance.SaveData.Seed);
      /*  if (map.Data.TileCount < SaveAndLoad.Instance.SaveData.MapSize.x * SaveAndLoad.Instance.SaveData.MapSize.y)
            GenerateRandomMap();*/
    }
    public void Awake()
    {
        map.GenerateMap += GenerateRandomMap;
    }
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (map == null || map.Data == null) return;

        Vector2Int startPos = map.Data.position;
        Vector2Int size = SaveAndLoad.Instance.SaveData.MapSize;
        Vector3 center = new Vector3(startPos.x + size.x / 2f, startPos.y + size.y / 2f, 0f);
        Vector3 size3D = new Vector3(size.x, size.y, 0.1f);

        // ���Ƶ�ͼ�߿���ɫ��ߣ�
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, size3D);


        //����ColorDicitionary ��ʾ��ɫ
        // ������ɫ�飨����ColorDictionary��
/*
        foreach (var kvp in ColorDicitionary)
        {
            Vector2Int cellPos = kvp.Key;
            Color cellColor = kvp.Value;

            // �����������꣨����ÿ����Ԫ��Ϊ1x1��λ��
            Vector3 worldPos = new Vector3(startPos.x + cellPos.x, startPos.y + cellPos.y, 0f);

            // ������ɫ������������
            Gizmos.color = cellColor;
            Gizmos.DrawCube(worldPos, new Vector3(1f, 1f, 0.1f));
        }*/
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

      

        ClearMap();

        map.Data.position = SaveAndLoad.Instance.SaveData.Active_MapPos;

        Vector2Int startPos = map.Data.position;
        Vector2Int size = SaveAndLoad.Instance.SaveData.MapSize;

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
        float gx = position.x * NoiseScale;
        float gy = position.y * NoiseScale;

        // Fixing the CS0019 error by ensuring consistent types for the operation

        float seedofNoise = (float)Seed*0.000001f;
        // ʹ�������޸���������������
        float temp = Mathf.PerlinNoise(gx + 2000 + seedofNoise, gy + 2000 + seedofNoise);
        float humid = Mathf.PerlinNoise(gx + 5000f + seedofNoise, gy + 5000f + seedofNoise);
        float precip = Mathf.PerlinNoise(gx + 10000f + seedofNoise, gy + 10000f + seedofNoise);
        float solidity = Mathf.PerlinNoise(gx + 20000f + seedofNoise, gy + 20000f + seedofNoise);
        float hight = Mathf.PerlinNoise(gx + 220000f + seedofNoise, gy + 220000f + seedofNoise);
        //λ�ڳ������ �ҵ���뾶Ϊ100
        //(0+ 50)/100 = 0.5 ���¶�ƫ��ϵ��   ���Գ���������¶�Ϊ 0.5 + 0.5 = 1
        temp += Temp + (PlantRadius / 2f - Mathf.Abs(position.y)) / PlantRadius;
        solidity += LandOceanRatio;


        EnvironmentFactors env = new EnvironmentFactors
        {
            Temperature = Mathf.Clamp01(temp),
            Humidity = Mathf.Clamp01(humid),
            Precipitation = Mathf.Clamp01(precip),
            Solidity = Mathf.Clamp01(solidity),
            Hight = Mathf.Clamp01(hight)
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

        //����biome.PreviewColor ʵ��Debug��ɫ����ʾ OnDrawGizmos()
        ColorDicitionary[position] =  biome.PreviewColor;
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
        foreach (Biome_ItemSpawn spawn in biome.TerrainConfig.ItemSpawn)
        {
            if(spawn.environmentConditionRange.IsMatch(env))
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
        Debug.Log($"[RandomMapGenerator] ��ͼ��� @ {map.Data.position} ��С{map.Data.size} ����{Seed}");
        onMapGenerated?.Invoke();
    }
    #endregion
}
