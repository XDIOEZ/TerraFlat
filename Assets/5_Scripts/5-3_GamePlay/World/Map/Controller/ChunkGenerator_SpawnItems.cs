using System;
using System.Collections;
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
    public override GenerationStage Stage => GenerationStage.Ecology;
    #region 配置参数
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
    public override IEnumerator GenerateAsync(MapGenerationContext context, int workBatchSize)
    {
        return GenerateCells(context, Mathf.Max(1, workBatchSize));
    }

    private IEnumerator GenerateCells(MapGenerationContext context, int maxCellsPerFrame)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (context.Map == null)
            throw new InvalidOperationException("[ChunkGenerator_SpawnItems] Map is null.");

        Map = context.Map;

        if (Map.Data == null)
            throw new InvalidOperationException("[ChunkGenerator_SpawnItems] Map.Data is null.");

        if (context.BiomeResolver == null || context.BiomeIndices == null)
            throw new InvalidOperationException("[ChunkGenerator_SpawnItems] 缺少基础地形阶段生成的 Biome 缓存。");

        Vector2Int startPos = Map.Data.position;
        Vector2 size = ChunkMgr.GetChunkSize();
        int width = (int)size.x;
        int height = (int)size.y;
        Map.Data.EnsureEnvironmentStorage(width, height);

        int spawnedCount = 0;
        var budget = new ChunkGenerationWorkBudget(Map, maxCellsPerFrame);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int worldPos = new Vector2Int(startPos.x + x, startPos.y + y);
                spawnedCount += GenerateCell(
                    context,
                    worldPos,
                    new Vector2Int(x, y));

                if (!budget.ShouldYield())
                    continue;

                yield return null;
                budget.BeginNextFrame();
            }
        }

        if (logSummary)
        {
            Debug.Log($"[ChunkGenerator_SpawnItems] ✅ 生成物品完成，总生成数量: {spawnedCount}", Map);
        }
    }

    private int GenerateCell(
        MapGenerationContext context,
        Vector2Int worldPosition,
        Vector2Int localPosition)
    {
        if (context.StructureMask != null &&
            context.StructureMask.IsOccupied(localPosition.x, localPosition.y))
        {
            return 0;
        }

        if (Map.GetTopTile(worldPosition) is TileData_Water)
            return 0;

        if (!Map.Data.IsEnvironmentLocalValid(localPosition.x, localPosition.y))
        {
            if (!_hasLoggedEnvMissing)
            {
                _hasLoggedEnvMissing = true;
                Debug.LogWarning("[ChunkGenerator_SpawnItems] 环境数据缺失，已跳过；请确认 Land 先执行。", Map);
            }
            return 0;
        }

        if (!context.TryGetResolvedBiome(localPosition, out BiomeData biome))
            throw new InvalidOperationException($"[ChunkGenerator_SpawnItems] {worldPosition} 没有已解析的 Biome。");

        return GenerateResourcesForBiome(
            Map,
            worldPosition,
            localPosition,
            biome,
            globalSpawnMultiplier,
            context.WorldSeed);
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
            Biome_ItemSpawn_NoSO config = spawnedConfigs[i];
            if (config == null)
                continue;

            List<string> tags = null;
            if (!string.IsNullOrWhiteSpace(config.itemName))
            {
                ItemData runtimeData = GameRes.Instance?.CreateItemData(config.itemName);
                tags = runtimeData?.Tags;
            }

            // 兼容尚未迁移到 JSON ItemId 的旧群系配置；新配置不再要求 itemPrefab。
            if ((tags == null || tags.Count == 0) && config.itemPrefab != null)
            {
                Item prefabItem = config.itemPrefab.GetComponent<Item>();
                tags = prefabItem?.itemData?.Tags;
            }

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

                BerryBush berryBush = spawnedItem.GetComponentInChildren<BerryBush>(true);
                if (berryBush != null)
                {
                    berryBush.InitializeNaturalStock(unchecked((uint)deterministicGuid));
                }

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
