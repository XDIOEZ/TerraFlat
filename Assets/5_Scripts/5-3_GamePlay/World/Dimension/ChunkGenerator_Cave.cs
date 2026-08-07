using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public sealed class ChunkGenerator_Cave : ChunkGeneratorBase
{
    private const int LooseOreSeedSalt = 0x6C8E9CF5;

    // Mine prefabs are authored as one world cell (1x1). Keep generated and restored
    // cave mines at that size so visuals, colliders and navigation agree.
    public const float GeneratedResourceUniformScale = 1f;

    public override GenerationStage Stage => GenerationStage.BaseTerrain;
    public static Quaternion GeneratedResourceRotation => Quaternion.identity;
    public static Vector3 GeneratedResourceScale =>
        new(GeneratedResourceUniformScale, GeneratedResourceUniformScale, 1f);

    [NonSerialized] private bool _jobActive;
    [NonSerialized] private JobHandle _activeHandle;
    [NonSerialized] private NativeArray<byte> _activeOpenMask;
    [NonSerialized] private NativeArray<byte> _activeClassifications;

    public override IEnumerator GenerateAsync(MapGenerationContext context, int workBatchSize)
    {
        if (context?.Map == null)
            throw new ArgumentNullException(nameof(context), "[ChunkGenerator_Cave] Missing map generation context.");

        Map = context.Map;
        DimensionDefinition definition = context.DimensionDefinition;
        if (definition == null || definition.GenerationMode != DimensionGenerationMode.Cave)
            throw new InvalidOperationException("[ChunkGenerator_Cave] A cave dimension definition is required.");

        Tile_Block floorBlock = GameRes.Instance?.GetTileBlock(definition.CaveFloorTileId);
        if (floorBlock?.tileDataTemplate == null)
            throw new InvalidOperationException($"[ChunkGenerator_Cave] Missing cave floor tile: {definition.CaveFloorTileId}");

        Tile_Block wallBlock = GameRes.Instance?.GetTileBlock(definition.CaveWallTileId);
        if (wallBlock?.tileDataTemplate == null)
            throw new InvalidOperationException($"[ChunkGenerator_Cave] Missing cave wall tile: {definition.CaveWallTileId}");

        List<DimensionResourceRule> resources = ResolveResources(definition.CaveResources);
        Vector2 configuredChunkSize = ChunkMgr.GetChunkSize();
        int width = Mathf.Max(1, Mathf.RoundToInt(configuredChunkSize.x));
        int height = Mathf.Max(1, Mathf.RoundToInt(configuredChunkSize.y));
        int cellCount = checked(width * height);
        int haloWidth = checked(width + 2);
        int haloHeight = checked(height + 2);
        int haloCellCount = checked(haloWidth * haloHeight);

        Transform parent = Map.transform.parent;
        Map.Data.position = parent != null
            ? new Vector2Int(Mathf.RoundToInt(parent.position.x), Mathf.RoundToInt(parent.position.y))
            : Vector2Int.zero;
        Map.Data.EnsureTileStorage(width, height);
        Map.Data.ClearAllTiles();
        Map.Data.EnsureEnvironmentStorage(width, height);

        CaveLayoutConfig layoutConfig = CaveLayoutSampler.CreateConfig(
            definition,
            new Vector2Int(width, height),
            context.PlanetData);
        _activeOpenMask = new NativeArray<byte>(
            haloCellCount,
            Allocator.Persistent,
            NativeArrayOptions.UninitializedMemory);
        _activeClassifications = new NativeArray<byte>(
            cellCount,
            Allocator.Persistent,
            NativeArrayOptions.UninitializedMemory);

        var openMaskJob = new CaveOpenMaskJob
        {
            HaloOrigin = new int2(Map.Data.position.x - 1, Map.Data.position.y - 1),
            HaloWidth = haloWidth,
            Config = layoutConfig,
            WorldSeed = context.WorldSeed,
            OpenMask = _activeOpenMask
        };
        JobHandle openMaskHandle = openMaskJob.Schedule(haloCellCount, 64);
        var classificationJob = new CaveClassifyJob
        {
            Width = width,
            HaloWidth = haloWidth,
            OpenMask = _activeOpenMask,
            Classifications = _activeClassifications
        };
        _activeHandle = classificationJob.Schedule(cellCount, 64, openMaskHandle);
        _jobActive = true;

        try
        {
            while (!_activeHandle.IsCompleted)
            {
                if (context.IsCancellationRequested)
                    yield break;
                yield return null;
            }

            _activeHandle.Complete();
            Vector2 safeCenter = definition.DefaultSpawnPosition;
            float safeRadiusSqr = definition.CaveSafeRadius * definition.CaveSafeRadius;
            WorldTopologyDomain topology = layoutConfig.Topology;
            var budget = new ChunkGenerationWorkBudget(Map, Mathf.Max(1, workBatchSize));

            for (int index = 0; index < cellCount; index++)
            {
                int localX = index % width;
                int localY = index / width;
                Vector2Int localPosition = new(localX, localY);
                Vector2Int worldPosition = Map.Data.position + localPosition;

                TileData floorTile = floorBlock.tileDataTemplate.Clone();
                floorTile.position = new Vector3Int(worldPosition.x, worldPosition.y, 0);
                if (!Map.Data.SetBaseTile(worldPosition, floorTile))
                    throw new InvalidOperationException($"Unable to write cave floor at {worldPosition}.");

                Map.Data.SetEnvironmentAtLocal(localX, localY, 0.3f, 12f, 0f, 0.05f);
                Map.Data.SetLightAtLocal(localX, localY, definition.FixedLighting);

                byte classification = _activeClassifications[index];
                if (classification == CaveCellClassification.Closed)
                {
                    TileData wallTile = wallBlock.tileDataTemplate.Clone();
                    wallTile.position = new Vector3Int(worldPosition.x, worldPosition.y, 0);
                    if (!Map.Data.PushTile(worldPosition, wallTile))
                        throw new InvalidOperationException($"Unable to write cave wall at {worldPosition}.");
                }
                else if (math.lengthsq(topology.ShortestDelta(
                             new float2(safeCenter.x, safeCenter.y),
                             new float2(worldPosition.x + 0.5f, worldPosition.y + 0.5f))) > safeRadiusSqr)
                {
                    bool spawnedMine = classification == CaveCellClassification.WallEdge &&
                                       TrySpawnResource(context, definition, resources, worldPosition, localPosition);
                    if (!spawnedMine)
                        TrySpawnLooseOre(context, definition, resources, worldPosition, localPosition);
                }

                if (!budget.ShouldYield())
                    continue;

                yield return null;
                budget.BeginNextFrame();
            }
        }
        finally
        {
            CompleteAndDisposeActiveJob();
        }
    }

    public override void CancelPendingWork()
    {
        CompleteAndDisposeActiveJob();
    }

    public static byte SampleCellClassification(
        Vector2Int worldPosition,
        DimensionDefinition definition,
        int worldSeed,
        Vector2Int chunkSize = default)
    {
        CaveLayoutConfig config = CaveLayoutSampler.CreateConfig(definition, chunkSize);
        return SampleCellClassification(worldPosition, config, worldSeed);
    }

    public static byte SampleCellClassification(
        Vector2Int worldPosition,
        DimensionDefinition definition,
        int worldSeed,
        Vector2Int chunkSize,
        PlanetData planetData)
    {
        CaveLayoutConfig config = CaveLayoutSampler.CreateConfig(definition, chunkSize, planetData);
        return SampleCellClassification(worldPosition, config, worldSeed);
    }

    private static byte SampleCellClassification(
        Vector2Int worldPosition,
        CaveLayoutConfig config,
        int worldSeed)
    {
        int2 position = new(worldPosition.x, worldPosition.y);
        if (!CaveLayoutSampler.IsOpenAtWorld(position, config, worldSeed))
            return CaveCellClassification.Closed;
        return CaveLayoutSampler.IsWallEdge(position, config, worldSeed)
            ? CaveCellClassification.WallEdge
            : CaveCellClassification.Open;
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

    private static bool TrySpawnResource(
        MapGenerationContext context,
        DimensionDefinition definition,
        List<DimensionResourceRule> resources,
        Vector2Int worldPos,
        Vector2Int localPos)
    {
        if (resources.Count == 0 || context.Map.chunk == null)
            return false;

        uint state = MixSeed(worldPos.x, worldPos.y, context.WorldSeed);
        float depositStrength = CaveLayoutSampler.GetDepositStrength(
            worldPos,
            context.WorldSeed,
            context.PlanetData);
        float depositFactor = Mathf.InverseLerp(0.48f, 0.82f, depositStrength);
        if (depositFactor <= 0f)
            return false;

        float spawnRoll = NextUnitFloat(ref state);
        float spawnChance = Mathf.Clamp01(definition.CaveResourceDensity * depositFactor * 1.5f);
        if (spawnRoll > spawnChance)
            return false;

        DimensionResourceRule selected = SelectResource(
            resources,
            worldPos,
            context.WorldSeed,
            context.PlanetData);
        if (selected == null)
            return false;

        int guid = StableGuid(context.WorldSeed, worldPos, selected.ItemId);
        Item item = context.Map.chunk.InstantiateItemInChunkDeterministic(
            selected.ItemId,
            guid,
            new Vector3(worldPos.x + 0.5f, worldPos.y + 0.5f, 0f),
            GeneratedResourceRotation,
            GeneratedResourceScale);

        if (item == null)
            return false;

        item.Load();
        item.Initialize_Env(context.Map.Data.EnvironmentLayers, localPos);
        return true;
    }

    private static bool TrySpawnLooseOre(
        MapGenerationContext context,
        DimensionDefinition definition,
        List<DimensionResourceRule> resources,
        Vector2Int worldPos,
        Vector2Int localPos)
    {
        if (resources.Count == 0 || context.Map.chunk == null || definition.CaveLooseOreDensity <= 0f)
            return false;

        uint state = MixSeed(worldPos.x, worldPos.y, context.WorldSeed ^ LooseOreSeedSalt);
        if (NextUnitFloat(ref state) > Mathf.Clamp01(definition.CaveLooseOreDensity))
            return false;

        DimensionResourceRule selected = SelectResource(
            resources,
            worldPos,
            context.WorldSeed,
            context.PlanetData);
        string pickupItemId = GetLooseOreItemId(selected?.ItemId);
        if (string.IsNullOrEmpty(pickupItemId) || GameRes.Instance?.GetPrefab(pickupItemId, false) == null)
            return false;

        float offsetX = Mathf.Lerp(-0.22f, 0.22f, NextUnitFloat(ref state));
        float offsetY = Mathf.Lerp(-0.22f, 0.22f, NextUnitFloat(ref state));
        float rotation = NextUnitFloat(ref state) * 360f;
        float scale = Mathf.Lerp(0.85f, 1.1f, NextUnitFloat(ref state));
        int guid = StableGuid(context.WorldSeed ^ LooseOreSeedSalt, worldPos, pickupItemId);
        Item item = context.Map.chunk.InstantiateItemInChunkDeterministic(
            pickupItemId,
            guid,
            new Vector3(worldPos.x + 0.5f + offsetX, worldPos.y + 0.5f + offsetY, 0f),
            Quaternion.Euler(0f, 0f, rotation),
            new Vector3(scale, scale, 1f));

        if (item == null)
            return false;

        item.Load();
        item.Initialize_Env(context.Map.Data.EnvironmentLayers, localPos);
        return true;
    }

    public static string GetLooseOreItemId(string mineItemId)
    {
        const string minePrefix = "Mine_";
        return !string.IsNullOrWhiteSpace(mineItemId) && mineItemId.StartsWith(minePrefix, StringComparison.Ordinal)
            ? $"Ore_{mineItemId.Substring(minePrefix.Length)}"
            : null;
    }

    public static bool ApplyGeneratedResourceTransform(DimensionDefinition definition, Item item)
    {
        if (definition == null ||
            definition.GenerationMode != DimensionGenerationMode.Cave ||
            item?.itemData == null ||
            !ContainsResource(definition.CaveResources, item.itemData.IDName))
        {
            return false;
        }

        item.transform.rotation = GeneratedResourceRotation;
        item.transform.localScale = GeneratedResourceScale;
        item.itemData.transform.rotation = GeneratedResourceRotation;
        item.itemData.transform.scale = GeneratedResourceScale;
        return true;
    }

    private static bool ContainsResource(List<DimensionResourceRule> resources, string itemId)
    {
        if (resources == null || string.IsNullOrWhiteSpace(itemId))
            return false;

        for (int i = 0; i < resources.Count; i++)
        {
            if (string.Equals(resources[i]?.ItemId, itemId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static DimensionResourceRule SelectResource(
        List<DimensionResourceRule> resources,
        Vector2Int worldPos,
        int worldSeed,
        PlanetData planetData)
    {
        WorldTopologyDomain topology = WorldTopologyBounds.TryCreate(planetData, out WorldTopologyBounds bounds)
            ? bounds.ToDomain()
            : default;
        for (int i = 0; i < resources.Count; i++)
        {
            DimensionResourceRule rule = resources[i];
            float scale = math.max(0.0001f, rule.VeinScale);
            TerrainNoiseConfig noiseConfig = new()
            {
                noiseType = NoiseType.Height,
                coordScale = 1f,
                frequency = scale,
                octaves = 1,
                lacunarity = 2f,
                persistence = 0.5f,
                coordOffset = new Vector2(rule.NoiseOffset, -rule.NoiseOffset)
            };
            float sample = TerrainNoiseKernel.Sample(
                noiseConfig,
                new Vector2(worldPos.x, worldPos.y),
                1f,
                worldSeed,
                topology);
            if (sample >= math.saturate(rule.VeinThreshold))
                return rule;
        }

        return resources[^1];
    }

    private void CompleteAndDisposeActiveJob()
    {
        if (_jobActive)
            _activeHandle.Complete();
        if (_activeOpenMask.IsCreated)
            _activeOpenMask.Dispose();
        if (_activeClassifications.IsCreated)
            _activeClassifications.Dispose();
        _jobActive = false;
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

    public static class CaveCellClassification
    {
        public const byte Closed = 0;
        public const byte Open = 1;
        public const byte WallEdge = 2;
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    private struct CaveOpenMaskJob : IJobParallelFor
    {
        public int2 HaloOrigin;
        public int HaloWidth;
        public CaveLayoutConfig Config;
        public int WorldSeed;
        [WriteOnly] public NativeArray<byte> OpenMask;

        public void Execute(int index)
        {
            int localX = index % HaloWidth;
            int localY = index / HaloWidth;
            OpenMask[index] = CaveLayoutSampler.IsOpenAtWorld(
                HaloOrigin + new int2(localX, localY),
                Config,
                WorldSeed)
                ? (byte)1
                : (byte)0;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    private struct CaveClassifyJob : IJobParallelFor
    {
        public int Width;
        public int HaloWidth;
        [ReadOnly] public NativeArray<byte> OpenMask;
        [WriteOnly] public NativeArray<byte> Classifications;

        public void Execute(int index)
        {
            int localX = index % Width;
            int localY = index / Width;
            int haloIndex = (localY + 1) * HaloWidth + localX + 1;
            if (OpenMask[haloIndex] == 0)
            {
                Classifications[index] = CaveCellClassification.Closed;
                return;
            }

            bool wallEdge = OpenMask[haloIndex - 1] == 0 ||
                            OpenMask[haloIndex + 1] == 0 ||
                            OpenMask[haloIndex - HaloWidth] == 0 ||
                            OpenMask[haloIndex + HaloWidth] == 0;
            Classifications[index] = wallEdge
                ? CaveCellClassification.WallEdge
                : CaveCellClassification.Open;
        }
    }
}
