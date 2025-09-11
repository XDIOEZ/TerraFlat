using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using Force.DeepCloner;
using UltEvents;

/// <summary>
/// �����ͼ��������
/// - �������� + ����Ⱥϵ��Biome��
/// - ֧�ַ�֡���� / ���ͼ�޷��ν� / Ⱥϵ��Դ�������
/// </summary>
public class RandomMapGenerator : MonoBehaviour
{
    #region ���ò���
    [Header("��ͼ����")]
    [Required] public Map map; // ��ͼ�������

    [ShowInInspector]
    public PlanetData plantData => SaveDataMgr.Instance.Active_PlanetData;

    public Vector2 ChunkSize => ChunkMgr.GetChunkSize();

    [Tooltip("�������")] public float Equator = 0;

    [Header("����Ⱥϵ�б�")]
    [Tooltip("��ͬ�¶�/ʪ�ȶ�Ӧ������Ⱥϵ����")]
    public List<BiomeData> biomes;

    [Header("����ѡ��")]
    [Tooltip("ÿ֡���ɵ����ؿ��� (0=��������)")]
    public int tilesPerFrame = 1;

    [Header("�߽�����")]
    public bool seamlessBorders = true;

    [Header("�߽���ɷ�Χ")]
    [Range(1, 20)]
    [Tooltip("�����޷�����ʱ�ı߽��Ͽ�ȣ���������")]
    public int transitionRange = 5;

    // Debug ��������ɫ�ֵ�
    public Dictionary<Vector2Int, Color> ColorDicitionary = new();

    #region ���� Inspector ���������� ����
    // ��Щ���̳��� BaseNoise���������� Inspector �ﻻ��ͬ�����㷨
    [Header("Ӧ������")]
    [Tooltip("�߶�����(½��/����)")]
    public BaseNoise LandNoise;

    [Tooltip("ʪ��")]
    public BaseNoise HumidityNoise;

    [Tooltip("��ˮ")]
    public BaseNoise PrecipitationNoise;

    [Tooltip("�¶�")]
    public BaseNoise TemperatureNoise;

    [Tooltip("����")]
    public BaseNoise RieverNoise;

    [Tooltip("�ؿ�̻��̶�/��Ӳ�� (������ʯ/��������)")]
    public BaseNoise SolidityNoise;

    #endregion
    #endregion

    #region �ڲ�����
    private int Seed => SaveDataMgr.Instance.SaveData.Seed;

    public float PlantRadius => plantData.Radius;
    public float Temp => plantData.TemperatureOffset;
    public float LandOceanRatio => plantData.OceanHeight;
    public float NoiseScale => plantData.NoiseScale;

    public static System.Random rng; // ϵͳ�����ʵ��

    private Dictionary<string, TileData> tileDataCache = new(); // TileData ����
    #endregion

    #region Unity ��������
    public void Awake()
    {
        rng = new System.Random(Seed);
        map.OnMapGenerated_Start += GenerateRandomMap_TileData;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (map == null || map.Data == null) return;

        Vector2 startPos = map.Data.position;
        Vector2 size = ChunkMgr.GetChunkSize();
        Vector3 center = new(startPos.x + size.x / 2f, startPos.y + size.y / 2f, 0f);
        Vector3 size3D = new(size.x, size.y, 0.1f);

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, size3D);

        UnityEditor.Handles.Label(center,
            $"Map:{startPos}\nSize:{size}",
            new GUIStyle { alignment = TextAnchor.MiddleCenter });
    }
#endif
    #endregion

    #region ���߼�
    [Button("���������ͼ")]
    public void GenerateRandomMap_TileData()
    {
        if (map == null)
        {
            Debug.LogError("[RandomMapGenerator] ��ͼ����δ���ã�");
            return;
        }

        ClearMap();

        map.Data.position = new Vector2Int(
            Mathf.RoundToInt(transform.parent.position.x),
            Mathf.RoundToInt(transform.parent.position.y)
        );

        Vector2Int startPos = map.Data.position;
        Vector2 size = ChunkSize;

        if (tilesPerFrame == 114514) // ���⣺Э�̷�֡����
            ChunkMgr.Instance.StartCoroutine(GenerateMapCoroutine(startPos, size));
        else
        {
            GenerateAllTiles(startPos, size);
            OnGenerationComplete();
        }
    }
    #endregion

    #region ��ͼ��������
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

    #region �ؿ������߼�
    private void GenerateTileAtPosition(Vector2Int position)
    {
        // 1. �������� ���� ת������������
        float gx = position.x * NoiseScale;
        float gy = position.y * NoiseScale;

        // ʹ�� ScriptableObject ���������ý��в���
        float temp = TemperatureNoise != null ? TemperatureNoise.Sample(gx, gy, Seed) : 0.5f;
        float humid = HumidityNoise != null ? HumidityNoise.Sample(gx, gy, Seed) : 0.5f;
        float precip = PrecipitationNoise != null ? PrecipitationNoise.Sample(gx, gy, Seed) : 0.5f;
        float solidity = SolidityNoise != null ? SolidityNoise.Sample(gx, gy, Seed) : 0.5f; // solidity ��֮ǰд���� solidit
        float hight = LandNoise != null ? LandNoise.Sample(gx, gy, Seed) : 0.5f;
        float Water = RieverNoise.Sample(gx, gy, Seed);
        // 2. ��װΪ��������
        EnvironmentFactors env = new()
        {
            Temperature = Mathf.Clamp01(temp),
            Humidity = Mathf.Clamp01(humid),
            Precipitation = Mathf.Clamp01(precip),
            Solidity = Mathf.Clamp01(solidity),
            Hight = Mathf.Clamp01(hight)
        };

        // 3. Biome ƥ�� ���� �ҵ���һ������������Ⱥϵ
        BiomeData biome = null;
        foreach (var b in biomes)
        {
            if (b.IsEnvironmentValid(env))
            {
                biome = b;
                break;
            }
        }
        if (biome == null) return; // û��ƥ��Ⱥϵ������

        // 4. ����Ԥ����ɫ�������ã�
        ColorDicitionary[position] = biome.PreviewColor;

        // 5. ������Ƭ����Դ
        GenerateTerrainTile(position, biome, env);
        if(Water > 0.5f)
        AddTile(position, RieverNoise.biomeData.TerrainConfig.TileSpawns[0].TileDataName);
        GenerateRandomResources(position, biome, env);
    }

    public void AddTile(Vector2Int position, string key)
    {
        if (!tileDataCache.ContainsKey(key))
        {
            var prefab = GameRes.Instance.GetPrefab(key);
            if (prefab == null)
            {
                return;
            }

            var blockTile = prefab.GetComponent<IBlockTile>();
            if (blockTile == null)
            {
                return;
            }

            tileDataCache[key] = blockTile.TileData;
        }
        var tile = tileDataCache[key].DeepClone();
        map.ADDTile(position, tile);
    }
    private void GenerateTerrainTile(Vector2Int position, BiomeData biome, EnvironmentFactors env)
    {
        string key = biome.TerrainConfig.GetTilePrefab(env);

        if (!tileDataCache.ContainsKey(key))
        {
            var prefab = GameRes.Instance.GetPrefab(key);
            if (prefab == null)
            {
                Debug.LogError($"�޷���ȡԤ����: {key}��Ⱥϵ: {biome.BiomeName}");
                return;
            }

            var blockTile = prefab.GetComponent<IBlockTile>();
            if (blockTile == null)
            {
                Debug.LogError($"Ԥ���� {key} ȱ�� IBlockTile��Ⱥϵ: {biome.BiomeName}");
                return;
            }

            tileDataCache[key] = blockTile.TileData;
        }

        var tile = tileDataCache[key].DeepClone();
        tile.position = new Vector3Int(position.x, position.y, 0);
        map.ADDTile(position, tile);
    }



    private void GenerateRandomResources(Vector2Int pos, BiomeData biome, EnvironmentFactors env)
    {
        uint state = (uint)(pos.x * 114514 ^ pos.y * 1919810);

        foreach (Biome_ItemSpawn spawn in biome.TerrainConfig.ItemSpawn)
        {
            if (!spawn.environmentConditionRange.IsMatch(env)) continue;

            float chance = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;
            if (chance > spawn.SpawnChance) continue;

            float offsetX = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;
            float offsetY = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;

            Vector2 spawnPos = new(pos.x + offsetX + 0.5f, pos.y + offsetY + 0.5f);

            ItemMgr.Instance.InstantiateItem(
                spawn.itemName,
                spawnPos,
                default,
                default,
                map.ParentObject
            );
        }
    }
    #endregion

    #region �߽�
    public void GenerateMapEdges()
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var d in dirs)
        {
            Item edge = ItemMgr.Instance.InstantiateItem("MapEdge", default, default, default, map.ParentObject);
            if (edge is WorldEdge we) we.SetupMapEdge(d, map.Data.position);
            else Debug.LogError("[RandomMapGenerator] �߽�������ʹ���");
        }
        Debug.Log("[RandomMapGenerator] �߽��������");
    }
    #endregion

    #region ���߷���
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
    private static uint Xorshift32(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
    #endregion
}
