using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

/// <summary>
/// 资源/物品生成器：
/// - 作为 Map.mapGenerators 管线中的一个步骤执行
/// - 基于 ChunkGenerator_Land 写入的 EnvironmentLayers（以及 biome 配置）生成 Item
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

    [NonSerialized]
    private bool _hasLoggedEnvMissing;
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
        Map.Data.EnsureEnvironmentStorage(width, height);

        int spawnedCount = 0;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int worldPos = new Vector2Int(startPos.x + x, startPos.y + y);
                if (context.StructureMask != null && context.StructureMask.IsOccupied(x, y))
                    continue;

                // 以河流生成后写入的最终地块为准，陆地资源不得生成在河流或海水上。
                if (Map.GetTopTile(worldPos) is TileData_Water)
                    continue;

                if (!Map.Data.IsEnvironmentLocalValid(x, y))
                {
                    if (!_hasLoggedEnvMissing)
                    {
                        _hasLoggedEnvMissing = true;
                        Debug.LogWarning("[ChunkGenerator_SpawnItems] ⚠️ 当前格子环境数据缺失，已跳过；请确认 Land 生成器先执行。", Map);
                    }
                    continue;
                }

                BiomeData biome = FindMatchingBiome(usedBiomes, Map.Data.EnvironmentLayers, x, y);
                if (biome == null)
                    continue;

                spawnedCount += GenerateResourcesForBiome(
                    Map,
                    worldPos,
                    new Vector2Int(x, y),
                    biome,
                    globalSpawnMultiplier,
                    context.WorldSeed);
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

    private static BiomeData FindMatchingBiome(System.Collections.Generic.List<BiomeData> biomeList, EnvironmentLayers layers, int x, int y)
    {
        for (int i = 0; i < biomeList.Count; i++)
        {
            var biome = biomeList[i];
            if (biome == null)
                continue;

            if (biome.IsEnvironmentValid(layers, x, y))
                return biome;
        }

        return null;
    }
    #endregion

    #region 资源生成逻辑（从 ChunkGenerator_Land 提取）
    private static int GenerateResourcesForBiome(
        Map map,
        Vector2Int worldPos,
        Vector2Int localPos,
        BiomeData biome,
        float globalSpawnMultiplier,
        int worldSeed)
    {
        if (biome == null || biome.TerrainConfig == null)
            return 0;

        float spawnMultiplier = Mathf.Clamp01(globalSpawnMultiplier);

        // 初始化伪随机数生成器（使用坐标作为种子，确保同一位置生成结果一致）
        uint randomState = MixSeed(worldPos.x, worldPos.y, worldSeed);
        Vector2 spawnCenterPos = new Vector2(worldPos.x + 0.5f, worldPos.y + 0.5f);

        int spawned = 0;
        List<Biome_ItemSpawn_NoSO> spawnedConfigs = null;

        // === 生成配置中的 SO 物品 ===
        // 注意：当前数据结构只有 ItemSpawn_NoSO，这里保持与旧逻辑一致
        if (biome.TerrainConfig.ItemSpawn_NoSO != null)
        {
            foreach (Biome_ItemSpawn_NoSO spawn in biome.TerrainConfig.ItemSpawn_NoSO)
            {
                if (spawn != null && spawn.CompanionOnly)
                    continue;

                if (TrySpawnItem(spawn, map, spawnCenterPos, ref randomState, localPos, biome.BiomeName, spawnMultiplier))
                {
                    spawned += Mathf.Max(1, spawn != null ? spawn.itemCount : 1);
                    RecordSpawnedConfig(ref spawnedConfigs, spawn);
                }
            }
        }

        // === 生成非 SO 物品 ===
        // 注意：旧版 ChunkGenerator_Land 会对同一列表执行两次（可能是历史遗留行为），此处保持一致。
        if (biome.TerrainConfig.ItemSpawn_NoSO != null)
        {
            foreach (Biome_ItemSpawn_NoSO spawn in biome.TerrainConfig.ItemSpawn_NoSO)
            {
                if (spawn != null && spawn.CompanionOnly)
                    continue;

                if (TrySpawnItem(spawn, map, spawnCenterPos, ref randomState, localPos, biome.BiomeName, spawnMultiplier))
                {
                    spawned += Mathf.Max(1, spawn != null ? spawn.itemCount : 1);
                    RecordSpawnedConfig(ref spawnedConfigs, spawn);
                }
            }
        }

        spawned += GenerateCompanionResources(
            biome.TerrainConfig.ItemSpawn_NoSO,
            spawnedConfigs,
            map,
            spawnCenterPos,
            localPos,
            biome.BiomeName,
            spawnMultiplier,
            worldPos,
            worldSeed);

        return spawned;
    }

    private static int GenerateCompanionResources(
        List<Biome_ItemSpawn_NoSO> spawnConfigs,
        List<Biome_ItemSpawn_NoSO> spawnedConfigs,
        Map map,
        Vector2 spawnCenterPos,
        Vector2Int localPos,
        string biomeName,
        float globalSpawnMultiplier,
        Vector2Int worldPos,
        int worldSeed)
    {
        if (spawnConfigs == null || spawnedConfigs == null || spawnedConfigs.Count == 0)
            return 0;

        uint companionRandomState = MixSeed(
            worldPos.x,
            worldPos.y,
            worldSeed ^ 0x51F15EED);

        int spawned = 0;
        for (int i = 0; i < spawnConfigs.Count; i++)
        {
            Biome_ItemSpawn_NoSO companion = spawnConfigs[i];
            if (companion == null ||
                string.IsNullOrWhiteSpace(companion.CompanionHostTag) ||
                companion.CompanionSpawnChance <= 0f)
            {
                continue;
            }

            // 当前格已经独立生成了该资源时，不再额外叠一份。
            if (spawnedConfigs.Contains(companion))
                continue;

            if (!HasSpawnedHostTag(spawnedConfigs, companion.CompanionHostTag))
                continue;

            Vector2 companionOffset = GetCompanionSpawnOffset(
                companion,
                ref companionRandomState);

            if (TrySpawnItem(
                companion,
                map,
                spawnCenterPos,
                ref companionRandomState,
                localPos,
                biomeName,
                globalSpawnMultiplier,
                companion.CompanionSpawnChance,
                companionOffset))
            {
                spawned += Mathf.Max(1, companion.itemCount);
            }
        }

        return spawned;
    }

    private static Vector2 GetCompanionSpawnOffset(
        Biome_ItemSpawn_NoSO companion,
        ref uint randomState)
    {
        float maxRadius = Mathf.Max(0f, companion.CompanionSpawnRadius);
        if (maxRadius <= 0f)
            return companion.CompanionSpawnOffset;

        float minRadius = Mathf.Clamp(companion.CompanionSpawnMinRadius, 0f, maxRadius);
        float angle = NextUnitFloat(ref randomState) * Mathf.PI * 2f;
        float radius = Mathf.Sqrt(Mathf.Lerp(
            minRadius * minRadius,
            maxRadius * maxRadius,
            NextUnitFloat(ref randomState)));

        return companion.CompanionSpawnOffset +
               new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    private static float NextUnitFloat(ref uint randomState)
    {
        return (Xorshift32(ref randomState) & 0xFFFFFF) / (float)0x1000000;
    }

    private static bool HasSpawnedHostTag(
        List<Biome_ItemSpawn_NoSO> spawnedConfigs,
        string hostTag)
    {
        for (int i = 0; i < spawnedConfigs.Count; i++)
        {
            GameObject prefab = spawnedConfigs[i]?.itemPrefab;
            Item prefabItem = prefab != null ? prefab.GetComponent<Item>() : null;
            List<string> tags = prefabItem?.itemData?.Tags;
            if (tags != null && tags.Count > 0 && tags.ContainsTag(hostTag))
                return true;
        }

        return false;
    }

    private static void RecordSpawnedConfig(
        ref List<Biome_ItemSpawn_NoSO> spawnedConfigs,
        Biome_ItemSpawn_NoSO spawn)
    {
        if (spawn == null)
            return;

        spawnedConfigs ??= new List<Biome_ItemSpawn_NoSO>(2);
        if (!spawnedConfigs.Contains(spawn))
            spawnedConfigs.Add(spawn);
    }

    private static bool TrySpawnItem(
        Biome_ItemSpawn_NoSO spawn,
        Map map,
        Vector2 spawnPos,
        ref uint randomState,
        Vector2Int localPos,
        string biomeName,
        float globalSpawnMultiplier,
        float? spawnChanceOverride = null,
        Vector2 spawnOffset = default)
    {
        if (spawn == null)
            return false;

        if (spawn.environmentConditionRange == null)
        {
            Debug.LogWarning($"[ChunkGenerator_SpawnItems] ⚠️ 物品({spawn.itemName}) environmentConditionRange 为空，已跳过", map);
            return false;
        }

        // 1. 环境条件检查
        if (!spawn.environmentConditionRange.IsMatch(map.Data.EnvironmentLayers, localPos.x, localPos.y))
            return false;

        // 2. 概率检查
        float baseChance = spawnChanceOverride ?? spawn.SpawnChance;
        // 旧群系资产没有该字段时可能反序列化为 0，按 1 倍处理以保持兼容。
        float resourceMultiplier = spawn.SpawnChanceMultiplier > 0f
            ? spawn.SpawnChanceMultiplier
            : 1f;
        float effectiveChance = Mathf.Clamp01(baseChance * globalSpawnMultiplier * resourceMultiplier);
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

                int deterministicGuid = unchecked((int)Xorshift32(ref randomState));
                if (deterministicGuid == 0)
                    deterministicGuid = 1;

                Item spawnedItem = map.chunk.InstantiateItemInChunkDeterministic(
                    spawn.itemName,
                    deterministicGuid,
                    new Vector3(spawnPos.x + spawnOffset.x, spawnPos.y + spawnOffset.y, 0f)
                );

                if (spawnedItem == null)
                {
                    Debug.LogWarning($"[资源生成] ⚠️ 无法实例化物品: {spawn.itemName} (群系: {biomeName})", map);
                    continue;
                }

                spawnedItem.Load();

                spawnedItem.Initialize_Env(map.Data.EnvironmentLayers, localPos);
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

    private static uint MixSeed(int x, int y, int worldSeed)
    {
        unchecked
        {
            uint state = 2166136261u;
            state = (state ^ (uint)x) * 16777619u;
            state = (state ^ (uint)y) * 16777619u;
            state = (state ^ (uint)worldSeed) * 16777619u;
            return state == 0u ? 0x9E3779B9u : state;
        }
    }
    #endregion
}
