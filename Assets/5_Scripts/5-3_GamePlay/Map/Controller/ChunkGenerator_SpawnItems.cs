using System;
using UnityEngine;
using Sirenix.OdinInspector;

/// <summary>
/// 资源/物品生成器：
/// - 作为 Map.mapGenerators 管线中的一个步骤执行
/// - 基于 ChunkGenerator_Land 写入的 EnvironmentData（以及 biome 配置）生成 Item
/// - 建议放在“所有会修改地表的生成器”之后（例如 Land -> River -> SpawnItems），避免把物品刷在水上
/// </summary>
[Serializable]
public class ChunkGenerator_SpawnItems : ChunkGeneratorBase
{
    #region 配置参数
    [Header("依赖")]
    [Tooltip("不填则自动使用 Map 上的 ChunkGenerator_Land.biomes")]
    public System.Collections.Generic.List<BiomeData> biomes;

    [Header("生成控制")]
    [Tooltip("全局实例化倍率（0.1 约等于原生成量的10%）")]
    [Range(0f, 1f)]
    public float globalSpawnMultiplier = 1f;

    [Header("调试")]
    public bool logSummary = true;
    #endregion

    #region 管线入口
    [Button("生成物品")]
    public override void Generate(MapGenerationContext context)
    {
        if (context == null)
        {
            LogNullContext(nameof(ChunkGenerator_SpawnItems));
            return;
        }

        if (context.Map == null)
        {
            LogNullMap(nameof(ChunkGenerator_SpawnItems));
            return;
        }

        Map = context.Map;

        if (Map.Data == null)
        {
            Debug.LogError("[ChunkGenerator_SpawnItems] ❌ Map.Data 为空，无法生成物品", Map);
            return;
        }

        if (Map.Data.EnvironmentData == null)
        {
            Debug.LogError("[ChunkGenerator_SpawnItems] ❌ Map.Data.EnvironmentData 为空：请确保 ChunkGenerator_Land 在本生成器之前执行", Map);
            return;
        }

        var usedBiomes = ResolveBiomes();
        if (usedBiomes == null || usedBiomes.Count == 0)
        {
            Debug.LogError("[ChunkGenerator_SpawnItems] ❌ biomes 为空：无法根据群系生成物品", Map);
            return;
        }

        Vector2Int startPos = Map.Data.position;
        Vector2 size = ChunkMgr.GetChunkSize();
        int width = (int)size.x;
        int height = (int)size.y;

        int spawnedCount = 0;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int worldPos = new Vector2Int(startPos.x + x, startPos.y + y);
                EnvironmentFactors env = Map.Data.EnvironmentData[x, y];

                BiomeData biome = FindMatchingBiome(usedBiomes, env);
                if (biome == null)
                    continue;

                spawnedCount += GenerateResourcesForBiome(Map, worldPos, biome, env, globalSpawnMultiplier);
            }
        }

        if (logSummary)
        {
            Debug.Log($"[ChunkGenerator_SpawnItems] ✅ 生成物品完成，总生成数量: {spawnedCount}", Map);
        }
    }
    #endregion

    #region 生物群系
    private System.Collections.Generic.List<BiomeData> ResolveBiomes()
    {
        if (biomes != null && biomes.Count > 0)
            return biomes;

        // 尝试从 LandGenerator 取
        if (Map != null)
        {
            var land = Map.GetGenerator<ChunkGenerator_Land>();
            if (land != null && land.biomes != null && land.biomes.Count > 0)
                return land.biomes;
        }

        return biomes;
    }

    private static BiomeData FindMatchingBiome(System.Collections.Generic.List<BiomeData> biomeList, EnvironmentFactors env)
    {
        for (int i = 0; i < biomeList.Count; i++)
        {
            var biome = biomeList[i];
            if (biome == null)
                continue;

            if (biome.IsEnvironmentValid(env))
                return biome;
        }

        return null;
    }
    #endregion

    #region 资源生成逻辑（从 ChunkGenerator_Land 提取）
    private static int GenerateResourcesForBiome(Map map, Vector2Int worldPos, BiomeData biome, EnvironmentFactors env, float globalSpawnMultiplier)
    {
        if (biome == null || biome.TerrainConfig == null)
            return 0;

        float spawnMultiplier = Mathf.Clamp01(globalSpawnMultiplier);

        // 初始化伪随机数生成器（使用坐标作为种子，确保同一位置生成结果一致）
        uint randomState = (uint)(worldPos.x * 114514 ^ worldPos.y * 1919810);
        Vector2 spawnCenterPos = new Vector2(worldPos.x + 0.5f, worldPos.y + 0.5f);

        int spawned = 0;

        // === 生成配置中的 SO 物品 ===
        // 注意：当前数据结构只有 ItemSpawn_NoSO，这里保持与旧逻辑一致
        if (biome.TerrainConfig.ItemSpawn_NoSO != null)
        {
            foreach (Biome_ItemSpawn_NoSO spawn in biome.TerrainConfig.ItemSpawn_NoSO)
            {
                if (TrySpawnItem(spawn, map, spawnCenterPos, ref randomState, env, biome.BiomeName, spawnMultiplier))
                    spawned += Mathf.Max(1, spawn != null ? spawn.itemCount : 1);
            }
        }

        // === 生成非 SO 物品 ===
        // 注意：旧版 ChunkGenerator_Land 会对同一列表执行两次（可能是历史遗留行为），此处保持一致。
        if (biome.TerrainConfig.ItemSpawn_NoSO != null)
        {
            foreach (Biome_ItemSpawn_NoSO spawn in biome.TerrainConfig.ItemSpawn_NoSO)
            {
                if (TrySpawnItem(spawn, map, spawnCenterPos, ref randomState, env, biome.BiomeName, spawnMultiplier))
                    spawned += Mathf.Max(1, spawn != null ? spawn.itemCount : 1);
            }
        }

        return spawned;
    }

    private static bool TrySpawnItem(
        Biome_ItemSpawn_NoSO spawn,
        Map map,
        Vector2 spawnPos,
        ref uint randomState,
        EnvironmentFactors env,
        string biomeName,
        float globalSpawnMultiplier)
    {
        if (spawn == null)
            return false;

        if (spawn.environmentConditionRange == null)
        {
            Debug.LogWarning($"[ChunkGenerator_SpawnItems] ⚠️ 物品({spawn.itemName}) environmentConditionRange 为空，已跳过", map);
            return false;
        }

        // 1. 环境条件检查
        if (!spawn.environmentConditionRange.IsMatch(env))
            return false;

        // 2. 概率检查
        float effectiveChance = Mathf.Clamp01(spawn.SpawnChance * globalSpawnMultiplier);
        if (effectiveChance <= 0f)
            return false;

        float randomValue = (Xorshift32(ref randomState) & 0xFFFFFF) / (float)0x1000000;
        if (randomValue > effectiveChance)
            return false;

        int count = Mathf.Max(1, spawn.itemCount);
        bool anySpawned = false;

        for (int i = 0; i < count; i++)
        {
            try
            {
                if (map == null || map.chunk == null)
                {
                    Debug.LogWarning("[资源生成] ⚠️ 无法生成物品：map或chunk为 null", map);
                    continue;
                }

                Item spawnedItem = map.chunk.InstantiateItemInChunk(
                    spawn.itemName,
                    new Vector3(spawnPos.x, spawnPos.y, 0f)
                );

                if (spawnedItem == null)
                {
                    Debug.LogWarning($"[资源生成] ⚠️ 无法实例化物品: {spawn.itemName} (群系: {biomeName})", map);
                    continue;
                }

                spawnedItem.Load();

                spawnedItem.Initialize_Env(env);
                anySpawned = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[资源生成] ❌ 生成物品异常 {spawn.itemName}: {ex.Message}", map);
            }
        }

        return anySpawned;
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
