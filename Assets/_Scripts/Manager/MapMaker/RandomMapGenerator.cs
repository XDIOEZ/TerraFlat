using System;
using UnityEngine;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using Force.DeepCloner;
using UltEvents;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Text;
using Codice.Client.BaseCommands;

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

    [ShowInInspector]
    public PlanetData plantData => SaveDataMgr.Instance.Active_PlanetData;

    public Vector2 ChunkSize => ChunkMgr.GetChunkSize();
    [Tooltip("�������")]
    public float Equator = 0;

    [Header("����Ⱥϵ�б�")]
    [Tooltip("��ͬ�¶�/ʪ�ȶ�Ӧ������Ⱥϵ����")]
    public List<BiomeData> biomes;

    [Header("����ѡ��")]
    [Tooltip("ÿ֡���ɵ����ؿ��� (0=ȫ����������)")]
    public int tilesPerFrame = 1;

    [Header("�߽�����")]
    [Tooltip("�Ƿ����������ڵ�ͼ������޷�Խ�")]
    public bool seamlessBorders = true;

    [Header("�߽���ɷ�Χ")]
    [Range(1, 20)]
    [Tooltip("�����޷�����ʱ�ı߽��Ͽ�ȣ���������")]
    public int transitionRange = 5;
    //Debugʹ��
    public Dictionary<Vector2Int,Color> ColorDicitionary  = new ();

    #endregion

    #region �ڲ�����
    /// <summary>
    /// ��ͼ���ӣ��ַ�����ʽ���Ӵ浵��ȡ
    /// </summary>
    private int Seed => SaveDataMgr.Instance.SaveData.Seed;

    public float PlantRadius { get => plantData.Radius;}
    public float Temp { get => plantData.TemperatureOffset; }
    public float LandOceanRatio { get => plantData.OceanHeight;}
    public float NoiseScale { get => plantData.NoiseScale;  }

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
    public void Awake()
    {
        RandomMapGenerator.rng = new System.Random(SaveDataMgr.Instance.SaveData.Seed);
        map.OnMapGenerated_Start += GenerateRandomMap_TileData;
    }
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (map == null || map.Data == null) return;

        Vector2 startPos = map.Data.position;
        Vector2 size = ChunkMgr.GetChunkSize();
        Vector3 center = new Vector3(startPos.x + size.x / 2f, startPos.y + size.y / 2f, 0f);
        Vector3 size3D = new Vector3(size.x, size.y, 0.1f);

        // ���Ƶ�ͼ�߿���ɫ��ߣ�
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, size3D);

        UnityEditor.Handles.Label(center,
            $"Map:{startPos}\nSize:{size}",
            new GUIStyle { alignment = TextAnchor.MiddleCenter });
    }
#endif
    #endregion

    #region ��ͼ�������߼�
    [Button("���������ͼ")]
    public void GenerateRandomMap_TileData()
    {
        if (map == null)
        {
            Debug.LogError("[RandomMapGenerator] ��ͼ����δ���ã�");
            return;
        }

        ClearMap();


        // Fix for CS0029: Convert Vector3 to Vector2Int explicitly using Vector3Int.RoundToInt and then accessing x and y properties.
        map.Data.position = new Vector2Int(
           Mathf.RoundToInt(transform.parent.transform.position.x),
           Mathf.RoundToInt(transform.parent.transform.position.y)
        );

        Vector2Int startPos = map.Data.position;
        Vector2 size = ChunkSize;

        if (tilesPerFrame == 114514)
        {
            ChunkMgr.Instance.StartCoroutine(GenerateMapCoroutine(startPos, size));
        }
        else
        {
            GenerateAllTiles(startPos, size);
            OnGenerationComplete();
        }

    }

    #endregion

    #region �߽�����
    public void GenerateMapEdges()
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var d in dirs)
        {
            Item edge = ItemMgr.Instance.InstantiateItem("MapEdge",default,default,default,map.ParentObject);
            if (edge is WorldEdge we)
                we.SetupMapEdge(d, map.Data.position);
            else
                Debug.LogError("[RandomMapGenerator] �߽�������ʹ���");
        }
        Debug.Log("[RandomMapGenerator] �߽��������");
    }
    #endregion

    #region ��������
    private IEnumerator GenerateMapCoroutine(Vector2Int startPos, Vector2 size)
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

    private void GenerateAllTiles(Vector2Int startPos, Vector2 size)
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
        solidity -= LandOceanRatio;
        hight -= LandOceanRatio;


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
    private static uint Xorshift32(ref uint state)
    {
        // �򵥿��ٵ�α�����������
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private void GenerateRandomResources(Vector2Int ChunkPosition, BiomeData biome, EnvironmentFactors env)
    {
        // ���ɳ�ʼ���ӣ�Ψһ��ȷ���ԣ�
        uint state = (uint)(ChunkPosition.x * 114514 ^ ChunkPosition.y * 1919810);

        foreach (Biome_ItemSpawn spawn in biome.TerrainConfig.ItemSpawn)
        {
            if (spawn.environmentConditionRange.IsMatch(env))
            {
                // ��ȡ [0,1) ��α�����
                float chance = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;

                if (chance <= spawn.SpawnChance)
                {
                    float offsetX = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;
                    float offsetY = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;

                    Vector2 spawnPosition = new Vector2(
                        (ChunkPosition.x + (int)offsetX) - 0.5f,
                        (ChunkPosition.y + (int)offsetY) - 0.5f
                    );
                    ItemMgr.Instance.InstantiateItem(
                        spawn.itemName,
                        spawnPosition,
                        default,
                        default,
                        map.ParentObject
                    );
                }
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
        map.Data.TileLoaded = true;
    }
    #endregion
}
