using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ChunkGenerator_Cave : ChunkGeneratorBase
{
    public override void Generate(MapGenerationContext context)
    {
        IEnumerator routine = GenerateAsync(context, int.MaxValue);
        while (routine.MoveNext())
        {
        }
    }

    public override IEnumerator GenerateAsync(MapGenerationContext context, int workBatchSize)
    {
        if (context?.Map == null)
        {
            LogNullMap(nameof(ChunkGenerator_Cave));
            yield break;
        }

        Map = context.Map;
        DimensionDefinition definition = context.DimensionDefinition;
        if (definition == null || definition.GenerationMode != DimensionGenerationMode.Cave)
        {
            Debug.LogError("[ChunkGenerator_Cave] 缺少矿洞维度配置。", Map);
            yield break;
        }

        Tile_Block floorBlock = GameRes.Instance?.GetTileBlock(definition.CaveFloorTileId);
        if (floorBlock?.tileDataTemplate == null)
        {
            Debug.LogError($"[ChunkGenerator_Cave] 找不到地面配置：{definition.CaveFloorTileId}", Map);
            yield break;
        }

        Tile_Block wallBlock = GameRes.Instance?.GetTileBlock(definition.CaveWallTileId);
        if (wallBlock?.tileDataTemplate == null)
        {
            Debug.LogError($"[ChunkGenerator_Cave] 找不到岩壁配置：{definition.CaveWallTileId}", Map);
            yield break;
        }

        List<DimensionResourceRule> resources = ResolveResources(definition.CaveResources);
        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        int width = Mathf.Max(1, Mathf.RoundToInt(chunkSize.x));
        int height = Mathf.Max(1, Mathf.RoundToInt(chunkSize.y));
        int batchSize = Mathf.Max(1, workBatchSize);
        int processed = 0;

        Map.Data.position = new Vector2Int(
            Mathf.RoundToInt(Map.transform.parent.position.x),
            Mathf.RoundToInt(Map.transform.parent.position.y));
        Map.Data.EnsureTileDataArray(width, height, initCells: false);
        Map.Data.ClearAllTiles();
        Map.Data.EnsureEnvironmentStorage(width, height);

        Vector2 safeCenter = definition.DefaultSpawnPosition;
        float safeRadiusSqr = definition.CaveSafeRadius * definition.CaveSafeRadius;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int worldPos = new Vector2Int(Map.Data.position.x + x, Map.Data.position.y + y);
                TileData floorTile = floorBlock.tileDataTemplate.Clone();
                Map.ADDTileData(worldPos, floorTile);
                Map.Data.SetEnvironmentAtLocal(x, y, 0.3f, 12f, 0.7f, 0f, 0.95f, 0.05f);
                Map.Data.SetLightAtLocal(x, y, definition.FixedLighting);

                bool isOpen = CaveLayoutSampler.IsOpenAtWorld(worldPos, definition, context.WorldSeed);
                if (!isOpen)
                {
                    TileData wallTile = wallBlock.tileDataTemplate.Clone();
                    Map.ADDTileData(worldPos, wallTile);
                }
                else if ((new Vector2(worldPos.x + 0.5f, worldPos.y + 0.5f) - safeCenter).sqrMagnitude > safeRadiusSqr &&
                         CaveLayoutSampler.IsWallEdge(worldPos, definition, context.WorldSeed))
                {
                    TrySpawnResource(context, definition, resources, worldPos, new Vector2Int(x, y));
                }

                processed++;
                if (processed >= batchSize)
                {
                    processed = 0;
                    yield return null;
                }
            }
        }
    }

    private static List<DimensionResourceRule> ResolveResources(List<DimensionResourceRule> configured)
    {
        List<DimensionResourceRule> available = new();
        if (configured == null || GameRes.Instance == null)
            return available;

        for (int i = 0; i < configured.Count; i++)
        {
            DimensionResourceRule rule = configured[i];
            if (rule != null && !string.IsNullOrWhiteSpace(rule.ItemId) && GameRes.Instance.GetPrefab(rule.ItemId, false) != null)
                available.Add(rule);
        }

        return available;
    }

    private static void TrySpawnResource(
        MapGenerationContext context,
        DimensionDefinition definition,
        List<DimensionResourceRule> resources,
        Vector2Int worldPos,
        Vector2Int localPos)
    {
        if (resources.Count == 0 || context.Map.chunk == null)
            return;

        uint state = MixSeed(worldPos.x, worldPos.y, context.WorldSeed);
        float depositStrength = CaveLayoutSampler.GetDepositStrength(worldPos, context.WorldSeed);
        float depositFactor = Mathf.InverseLerp(0.48f, 0.82f, depositStrength);
        if (depositFactor <= 0f)
            return;

        float spawnRoll = NextUnitFloat(ref state);
        float spawnChance = Mathf.Clamp01(definition.CaveResourceDensity * depositFactor * 1.5f);
        if (spawnRoll > spawnChance)
            return;

        DimensionResourceRule selected = SelectResource(resources, worldPos, context.WorldSeed);
        if (selected == null)
            return;

        int guid = StableGuid(context.WorldSeed, worldPos, selected.ItemId);
        float rotation = NextUnitFloat(ref state) * 360f;
        float scaleValue = Mathf.Lerp(0.85f, 1.15f, NextUnitFloat(ref state));
        Item item = context.Map.chunk.InstantiateItemInChunkDeterministic(
            selected.ItemId,
            guid,
            new Vector3(worldPos.x + 0.5f, worldPos.y + 0.5f, 0f),
            Quaternion.Euler(0f, 0f, rotation),
            new Vector3(scaleValue, scaleValue, 1f));

        if (item == null)
            return;

        item.Load();
        item.Initialize_Env(context.Map.Data.EnvironmentLayers, localPos);
    }

    private static DimensionResourceRule SelectResource(
        List<DimensionResourceRule> resources,
        Vector2Int worldPos,
        int worldSeed)
    {
        float seedOffset = Mathf.Abs(worldSeed % 100000) * 0.001f;
        for (int i = 0; i < resources.Count; i++)
        {
            DimensionResourceRule rule = resources[i];
            float scale = Mathf.Max(0.0001f, rule.VeinScale);
            float sample = Mathf.PerlinNoise(
                (worldPos.x + rule.NoiseOffset + seedOffset) * scale,
                (worldPos.y - rule.NoiseOffset + seedOffset) * scale);
            if (sample >= Mathf.Clamp01(rule.VeinThreshold))
                return rule;
        }

        return resources[^1];
    }

    private static int StableGuid(int worldSeed, Vector2Int worldPos, string itemId)
    {
        unchecked
        {
            uint hash = MixSeed(worldPos.x, worldPos.y, worldSeed);
            for (int i = 0; i < itemId.Length; i++)
                hash = (hash ^ itemId[i]) * 16777619u;
            int guid = (int)hash;
            return guid == 0 ? 1 : guid;
        }
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

    private static float NextUnitFloat(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0xFFFFFF) / (float)0x1000000;
    }
}
