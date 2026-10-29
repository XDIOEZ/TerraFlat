using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 地表高度与阴影采样

    private static void RegisterTerrainHeightShadowOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("rendering/terrain_height", ServiceTerrainHeight);
        operations.Add("rendering/terrain_height_save", ServiceTerrainHeightSave);
        operations.Add("rendering/terrain_height_neighbours", ServiceTerrainHeightNeighbours);
        operations.Add("rendering/terrain_shadow_masks", ServiceTerrainShadowMasks);
        operations.Add("rendering/terrain_shadow_dirty_queue", ServiceTerrainShadowDirtyQueue);
        operations.Add("rendering/terrain_shadow_surface_cache", ServiceTerrainShadowSurfaceCache);
        operations.Add("terrain/natural_waterbed", ServiceNaturalWaterbed);
        operations.Add("terrain/ocean_shelf_bathymetry", ServiceOceanShelfBathymetry);
    }

    // 只提供直岸、岛屿与环绕岸线输入，距离、水量和底床均由正式生成核计算。
    private static JObject ServiceOceanShelfBathymetry(JObject args)
    {
        string scenario = Text(args, "scenario", "straight");
        var profile = new ChunkGenerationProfileSnapshot("probe:ocean-shelf", 1, 32, 32,
            new Dictionary<string, double>());
        var settings = profile.Settings;
        double Height(int x, int y)
        {
            if (scenario == "ocean") return .25d;
            if (scenario == "land") return .75d;
            if (scenario == "island") return x == 0 && y == 0 ? .75d : .25d;
            if (scenario == "wrapped") x = (x % 64 + 64) % 64;
            return x <= 0 ? .75d : .25d;
        }
        bool cancelled = false;
        try
        {
            using var ignored = new OceanShoreDistanceBathymetryKernel(0, 0, 1, 1, .5d,
                settings.OceanShelfWidthTiles, settings.OceanSlopeWidthTiles, Height,
                new System.Threading.CancellationToken(true));
        }
        catch (OperationCanceledException) { cancelled = true; }
        using var whole = new OceanShoreDistanceBathymetryKernel(0, 0, 65, 1, .5d,
            settings.OceanShelfWidthTiles, settings.OceanSlopeWidthTiles, Height);
        var rows = new JArray();
        for (int x = 0; x <= 64; x++)
        {
            using var single = new OceanShoreDistanceBathymetryKernel(x, 0, 1, 1, .5d,
                settings.OceanShelfWidthTiles, settings.OceanSlopeWidthTiles, Height);
            int level = LiquidCellValue.FromGeneratedDepth(whole.GetInitialLiquidDepth(x, 0));
            rows.Add(new JObject { ["x"] = x, ["distance"] = whole.GetShoreDistance(x, 0),
                ["level"] = level, ["singleLevel"] = LiquidCellValue.FromGeneratedDepth(single.GetInitialLiquidDepth(0, 0)),
                ["bed"] = OceanShoreDistanceBathymetryKernel.ResolveNaturalOceanBedDepthLevels(level) });
        }
        using var diagonal = new OceanShoreDistanceBathymetryKernel(3, 4, 1, 1, .5d, 16, 4,
            (x, y) => x == 0 && y == 0 ? .75d : .25d);
        return new JObject { ["shelf"] = settings.OceanShelfWidthTiles, ["slope"] = settings.OceanSlopeWidthTiles,
            ["cancelled"] = cancelled, ["diagonalDistance"] = diagonal.GetShoreDistance(0, 0), ["samples"] = rows };
    }

    // 直接推进阴影正式脏格队列，比较稀疏变更与全格重建的实际遍历数量。
    private static JObject ServiceTerrainShadowSurfaceCache(JObject args)
    {
        const int width = 32, height = 32;
        var cache = new TerrainShadowSurfaceSampleCache<int>();
        cache.ConfigureForChunkWithOneCellHalo(width, height);
        cache.BeginFlushPass();
        int reads = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int sampleX = x + dx, sampleY = y + dy;
            if (cache.TryGetCachedSurfaceSample(sampleX, sampleY, out _, out _)) continue;
            reads++;
            cache.StoreSurfaceSample(sampleX, sampleY, true, sampleY * width + sampleX);
        }

        cache.BeginFlushPass();
        int missingReads = 0;
        for (int i = 0; i < 2; i++)
        {
            if (cache.TryGetCachedSurfaceSample(-1, -1, out _, out _)) continue;
            missingReads++;
            cache.StoreSurfaceSample(-1, -1, false, 0);
        }
        bool unknownCached = missingReads == 1 &&
            cache.TryGetCachedSurfaceSample(-1, -1, out bool available, out _) && !available;

        int minimumDepth = 3;
        cache.BeginFlushPass();
        int firstMinimumDepth = ReadCurrentMinimumDepth();
        minimumDepth = 8;
        cache.BeginFlushPass();
        int nextMinimumDepth = ReadCurrentMinimumDepth();

        int terrainValue = 2;
        cache.BeginFlushPass();
        int beforeTerrainChange = ReadTerrainValue();
        terrainValue = 7;
        cache.InvalidateCurrentFlushPass();
        int afterTerrainChange = ReadTerrainValue();

        cache.BeginFlushPass();
        int left = ReadWrappedCell(-1), right = ReadWrappedCell(width);
        bool cachedMasksMatch = CompareCachedAndUncachedShadowMasks(out bool missingBoundaryMatches,
            out bool supportCovered, out bool liquidBoundaryCovered);
        return new JObject
        {
            ["baselineReads"] = width * height * 9,
            ["cachedReads"] = reads,
            ["unknownCached"] = unknownCached,
            ["configurationReloaded"] = firstMinimumDepth == 3 && nextMinimumDepth == 8,
            ["changeInvalidated"] = beforeTerrainChange == 2 && afterTerrainChange == 7,
            ["wrappedHaloDistinct"] = left == width - 1 && right == 0 && left != right,
            ["outsideHaloRejected"] = !cache.TryGetCachedSurfaceSample(-2, 0, out _, out _),
            ["cachedMasksMatch"] = cachedMasksMatch,
            ["missingBoundaryMatches"] = missingBoundaryMatches,
            ["supportCovered"] = supportCovered,
            ["liquidBoundaryCovered"] = liquidBoundaryCovered
        };

        int ReadWrappedCell(int x)
        {
            if (cache.TryGetCachedSurfaceSample(x, 0, out _, out int value)) return value;
            value = (x % width + width) % width;
            cache.StoreSurfaceSample(x, 0, true, value);
            return value;
        }

        int ReadCurrentMinimumDepth()
        {
            if (cache.TryGetCachedSurfaceSample(0, 0, out bool sampleAvailable, out int value))
                return sampleAvailable ? value : -1;
            cache.StoreSurfaceSample(0, 0, true, minimumDepth);
            return minimumDepth;
        }

        int ReadTerrainValue()
        {
            if (cache.TryGetCachedSurfaceSample(1, 1, out bool sampleAvailable, out int value))
                return sampleAvailable ? value : -1;
            cache.StoreSurfaceSample(1, 1, true, terrainValue);
            return terrainValue;
        }
    }

    private static bool CompareCachedAndUncachedShadowMasks(out bool boundaryMatches,
        out bool supportCovered, out bool liquidBoundaryCovered)
    {
        const int size = 16;
        using var buffer = new ChunkTerrainBuffer(size, size, LiquidTypeCatalog.BuiltIn);
        using ChunkTerrainData terrain = buffer.Seal();
        var random = new System.Random(7219);
        int waterType = LiquidTypeCatalog.BuiltIn.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int kind = random.Next(5);
            if (kind != 0)
            {
                bool blocking = kind == 1;
                terrain.SetCell(x, y, new TerrainCell(1, 0, blocking ? 2 : 0, 0, 1,
                    blocking ? TerrainCellFlags.Blocking : TerrainCellFlags.Walkable));
                TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, x, y, random.Next(4));
                TerrainSurfaceHeightLayer.SetCoverLevels(terrain, x, y, random.Next(4));
            }
            if (random.Next(6) == 0)
                terrain.SetLiquid(x, y, waterType, random.Next(1, 11));
        }

        var ground = new TerrainCell(1, 0, 0, 0, 1, TerrainCellFlags.Walkable);
        terrain.SetCell(0, 0, ground);
        TerrainSupportLayer.Set(terrain, 0, 0, 23, 1);
        terrain.SetCell(0, 8, ground);
        terrain.SetLiquid(0, 8, waterType, 5);
        terrain.SetCell(1, 8, ground);
        supportCovered = TerrainSupportLayer.GetExposedTileId(terrain, 0, 0) == 23;
        liquidBoundaryCovered = terrain.GetLiquidLevel(0, 8) == 5;

        var address = new FlatWorld.WorldModel.WorldAddress("probe:terrain_shadow_cache", default);
        var samples = new TerrainShadowSurfaceSampleCache<TerrainSurfaceHeight>();
        samples.ConfigureForChunkWithOneCellHalo(size, size);
        samples.BeginFlushPass();
        boundaryMatches = true;
        bool allMatches = true;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool uncachedVisible = TerrainHeightShadowManager.TryGetShadowMasks(null, address, terrain,
                x, y, false, out Vector4 uncachedEdges, out Vector4 uncachedCorners);
            bool cachedVisible = TerrainHeightShadowManager.TryGetShadowMasks(null, address, terrain,
                x, y, false, out Vector4 cachedEdges, out Vector4 cachedCorners, samples);
            bool matches = uncachedVisible == cachedVisible && uncachedEdges.Equals(cachedEdges) &&
                           uncachedCorners.Equals(cachedCorners);
            allMatches &= matches;
            if (x == 0 || y == 0 || x == size - 1 || y == size - 1)
                boundaryMatches &= matches;
        }
        return allMatches;
    }

    private static JObject ServiceTerrainShadowDirtyQueue(JObject args)
    {
        var queue = new TerrainShadowDirtyQueue();
        queue.Reset(64 * 64);
        queue.MarkAll();
        List<int> full = queue.BeginDrain();
        int fullCount = full.Count;
        queue.CompleteDrain(fullCount);

        const int rounds = 128;
        int sparseVisits = 0;
        bool deduplicated = true;
        for (int i = 0; i < rounds; i++)
        {
            queue.Mark(11); queue.Mark(12); queue.Mark(13); queue.Mark(11);
            List<int> batch = queue.BeginDrain();
            deduplicated &= batch.Count == 3 && queue.PendingCount == 0;
            sparseVisits += batch.Count;
            queue.CompleteDrain(batch.Count);
        }
        bool drained = queue.PendingCount == 0;

        queue.Mark(2); queue.Mark(3); queue.Mark(4);
        List<int> interrupted = queue.BeginDrain();
        queue.Mark(3); queue.Mark(8);
        queue.CompleteDrain(1);
        bool retried = queue.PendingCount == 3;
        List<int> retry = queue.BeginDrain();
        bool retryContents = retry.Count == 3 && retry.Contains(3) && retry.Contains(4) && retry.Contains(8);
        queue.MarkAll();
        queue.CompleteDrain(retry.Count);
        bool fullReentrant = queue.PendingCount == 64 * 64;
        List<int> rebuilt = queue.BeginDrain();
        bool rebuilding = rebuilt.Count == 64 * 64;
        queue.CompleteDrain(rebuilt.Count);
        bool empty = queue.PendingCount == 0;

        queue.Clear();
        queue.Mark(2);
        bool cleared = queue.PendingCount == 0;
        queue.Reset(16);
        queue.Mark(-1); queue.Mark(16); queue.Mark(4); queue.Mark(4);
        bool reset = queue.PendingCount == 1;
        var neighbourhoods = new JArray();
        foreach (Int2 cell in new[] { new Int2(2, 2), new Int2(0, 0), new Int2(4, 4),
            new Int2(-1, 2), new Int2(5, 2), new Int2(2, -1), new Int2(2, 5),
            new Int2(-1, -1), new Int2(5, -1), new Int2(-1, 5), new Int2(5, 5),
            new Int2(-2, 2), new Int2(6, 2), new Int2(2, -2), new Int2(2, 6) })
        {
            queue.Reset(25);
            queue.MarkArea(cell.X, cell.Y, 5, 5);
            queue.MarkArea(cell.X, cell.Y, 5, 5);
            List<int> area = queue.BeginDrain();
            neighbourhoods.Add(new JObject { ["x"] = cell.X, ["y"] = cell.Y,
                ["indices"] = new JArray(area) });
            queue.CompleteDrain(area.Count);
        }
        return new JObject
        {
            ["fullCount"] = fullCount, ["sparseVisits"] = sparseVisits,
            ["fullScanVisits"] = rounds * 64 * 64, ["deduplicated"] = deduplicated,
            ["drained"] = drained, ["retried"] = retried,
            ["retryContents"] = retryContents, ["fullReentrant"] = fullReentrant,
            ["rebuilding"] = rebuilding, ["empty"] = empty,
            ["cleared"] = cleared, ["reset"] = reset, ["neighbourhoods"] = neighbourhoods
        };
    }

    // 只构造八邻格高度，实际边与角的筛选直接调用正式提交入口。
    private static JObject ServiceTerrainShadowMasks(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(3, 3);
        using ChunkTerrainData terrain = buffer.Seal();
        var ground = new TerrainCell(1, 0, 0, 0, 1, TerrainCellFlags.Walkable);
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++) terrain.SetCell(x, y, ground);
        int mask = (int)Number(args, "mask", 16);
        int difference = (int)Number(args, "difference", 1);
        Int2[] offsets = { new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
            new(-1, -1), new(1, -1), new(-1, 1), new(1, 1) };
        for (int index = 0; index < offsets.Length; index++)
        {
            if ((mask & (1 << index)) == 0) continue;
            int x = 1 + offsets[index].X, y = 1 + offsets[index].Y;
            TerrainSurfaceHeightLayer.SetCoverLevels(terrain, x, y, Math.Max(0, difference));
            TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, x, y, Math.Max(0, -difference));
            if (Flag(args, "wallCaster", false))
                terrain.SetCell(x, y, new TerrainCell(1, 0, 2, 0, 1, TerrainCellFlags.Blocking));
            if (Flag(args, "missingCaster", false)) terrain.SetCell(x, y, default);
        }
        if (Flag(args, "wallReceiver", false))
            terrain.SetCell(1, 1, new TerrainCell(1, 0, 2, 0, 1, TerrainCellFlags.Blocking));
        if (Flag(args, "missingReceiver", false)) terrain.SetCell(1, 1, default);
        return SampleTerrainShadowMasks(null,
            new FlatWorld.WorldModel.WorldAddress("probe:terrain_height", default), terrain, 1, 1);
    }

    private static JObject SampleTerrainShadowMasks(WorldRuntime world,
        FlatWorld.WorldModel.WorldAddress address, ChunkTerrainData terrain, int x, int y)
    {
        bool visible = TerrainHeightShadowManager.TryGetShadowMasks(world, address, terrain, x, y, false,
            out Vector4 edges, out Vector4 corners);
        return new JObject { ["visible"] = visible,
            ["edges"] = new JArray(edges.x, edges.y, edges.z, edges.w),
            ["corners"] = new JArray(corners.x, corners.y, corners.z, corners.w) };
    }

    // 夹具只构造权威地形并采样正式高度规则，所有预期由 Lua 判断。
    private static JObject ServiceTerrainHeight(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(2, 1, LiquidTypeCatalog.BuiltIn);
        using ChunkTerrainData terrain = buffer.Seal();
        float height = (float)Number(args, "height", .55);
        terrain.SetCell(0, 0, new TerrainCell(1, 0, 0, 0, 1, TerrainCellFlags.Walkable));
        bool wall = Flag(args, "wall", false);
        terrain.SetCell(1, 0, new TerrainCell(4, 0, wall ? 2 : 0, 0, 1,
            wall ? TerrainCellFlags.Blocking : TerrainCellFlags.Walkable));
        if (!Flag(args, "missingHeight", false))
        {
            terrain.SetEnvironmentValue("height", 0, 0, height);
            terrain.SetEnvironmentValue("height", 1, 0, (float)Number(args, "neighbourHeight", height));
        }
        TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, 0, 0, (int)Number(args, "depth", 0));
        TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, 1, 0, (int)Number(args, "neighbourDepth", 0));
        TerrainSurfaceHeightLayer.SetCoverLevels(terrain, 0, 0, (int)Number(args, "covers", 0));
        terrain.SetEnvironmentValue(TerrainSurfaceHeightLayer.NaturalDepthLevelsLayerId, 0, 0,
            (float)Number(args, "naturalDepth", 0));
        terrain.SetEnvironmentValue(TerrainSurfaceHeightLayer.NaturalDepthLevelsLayerId, 1, 0,
            (float)Number(args, "neighbourNaturalDepth", 0));
        if (Flag(args, "liquid", false))
            terrain.SetLiquid(0, 0, LiquidTypeCatalog.BuiltIn.GetIndex(LiquidTypeCatalog.DirtyWaterId),
                (int)Number(args, "liquidLevel", 5));
        if (Flag(args, "neighbourLiquid", false))
            terrain.SetLiquid(1, 0, LiquidTypeCatalog.BuiltIn.GetIndex(LiquidTypeCatalog.DirtyWaterId),
                (int)Number(args, "neighbourLiquidLevel", 5));
        if (Flag(args, "support", false)) TerrainSupportLayer.Set(terrain, 0, 0, 23, 1);
        bool useGeneratedHeight = Flag(args, "surface", true);
        TerrainSurfaceHeightLayer.TryGetSurface(terrain, 0, 0, useGeneratedHeight, out TerrainSurfaceHeight current,
            (int)Number(args, "minimumDepth", 0));
        TerrainSurfaceHeightLayer.TryGetSurface(terrain, 1, 0, useGeneratedHeight, out TerrainSurfaceHeight neighbour);
        bool outside = TerrainSurfaceHeightLayer.TryGetSurface(terrain, -1, 0, useGeneratedHeight, out _);
        return new JObject
        {
            ["base"] = TerrainSurfaceHeightLayer.GetBaseLevel(terrain, 0, 0, useGeneratedHeight),
            ["level"] = current.Level, ["neighbour"] = neighbour.Level, ["top"] = neighbour.TopLevel,
            ["inner"] = TerrainSurfaceHeightLayer.GetShadowDelta(current, neighbour),
            ["outer"] = TerrainSurfaceHeightLayer.GetShadowDelta(neighbour, current),
            ["receiver"] = current.ReceivesShadow, ["neighbourReceiver"] = neighbour.ReceivesShadow,
            ["outside"] = outside, ["waterLevel"] = terrain.GetLiquidLevel(0, 0),
            ["neighbourWaterLevel"] = terrain.GetLiquidLevel(1, 0)
        };
    }

    // 正式生成与液体改写都在独立世界执行，Lua 检查底床不随抽水、倒水改变。
    private static JObject ServiceNaturalWaterbed(JObject args)
    {
        string scenario = Text(args, "scenario", "freshwater");
        bool cave = scenario == "cave";
        bool ocean = scenario == "ocean" || scenario == "frozen_ocean";
        bool frozen = scenario == "frozen_ocean";
        var numbers = SmallGenerationParameters();
        numbers["terrain.seaLevel"] = ocean ? 1d : 0d;
        numbers["terrain.beachLevel"] = ocean ? 1d : .01d;
        numbers["climate.polarBand.enabled"] = 0d;
        numbers["climate.temperature.altitudeCoolingStrength"] = 0d;
        numbers["climate.temperature.celsiusMin"] = frozen ? -20d : 20d;
        numbers["climate.temperature.celsiusMax"] = frozen ? -20d : 20d;
        numbers["climate.distance.minimumRainfall"] = 1d;
        numbers["climate.distance.maximumRainfall"] = 1d;
        numbers["river.enabled"] = ocean || cave ? 0d : 1d;
        numbers["river.startFlow"] = .0001d;
        numbers["river.tributaryStartFlow"] = .0001d;
        numbers["river.floodplainStartFlow"] = .0001d;
        numbers["lake.large.enabled"] = ocean || cave ? 0d : 1d;
        numbers["lake.large.regionSize"] = 32d;
        numbers["lake.large.chance"] = 1d;
        numbers["lake.large.minRadius"] = 8d;
        numbers["lake.large.maxRadius"] = 9d;
        numbers["lake.large.islandChance"] = 0d;
        numbers["cave.river.enabled"] = 1d;
        numbers["cave.river.connectionChance"] = 1d;
        numbers["cave.network.extraConnectionChance"] = 1d;
        var texts = new Dictionary<string, string>
        {
            ["terrain.mode"] = cave ? "cave" : "surface", ["climate.algorithm"] = "legacy"
        };
        int size = cave ? 64 : 32;
        DiscreteProbabilityConfiguration.Capture().WriteGenerationParameters(numbers);
        var profile = new ChunkGenerationProfileSnapshot("probe:natural-waterbed", 1, size, size, numbers, texts);
        using var world = new WorldRuntime("probe:natural-waterbed", 1);
        var address = new FlatWorld.WorldModel.WorldAddress(cave ? "cave" : "surface",
            cave ? new Int2(-32, -32) : default);
        var generator = new DeterministicChunkGenerator();
        try
        {
            using ChunkGenerationResult generated = generator.Generate(
                world.BeginChunkGeneration(address, 17413, profile), System.Threading.CancellationToken.None);
            if (!world.TryCommit(generated, out string reason)) throw new InvalidOperationException(reason);
            world.TryGetChunkTerrain(address, out ChunkTerrainData terrain);
            var samples = new JArray();
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                int liquid = terrain.GetLiquidLevel(x, y);
                int naturalDepth = TerrainSurfaceHeightLayer.GetNaturalDepthLevels(terrain, x, y);
                TerrainSurfaceHeightLayer.TryGetSurface(terrain, x, y, !cave, out var initial);
                int baseline = TerrainSurfaceHeightLayer.GetBaseLevel(terrain, x, y, !cave);
                terrain.TryGetEnvironmentValue("height", x, y, out float height);
                terrain.TryGetEnvironmentValue("riverKind", x, y, out float kind);
                terrain.SetLiquid(x, y, 0, 0);
                TerrainSurfaceHeightLayer.TryGetSurface(terrain, x, y, !cave, out var drained);
                terrain.SetLiquid(x, y, terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 5);
                TerrainSurfaceHeightLayer.TryGetSurface(terrain, x, y, !cave, out var poured);
                terrain.TryGetEnvironmentValue("height", x, y, out float afterHeight);
                samples.Add(new JObject { ["liquid"] = liquid, ["naturalDepth"] = naturalDepth,
                    ["base"] = baseline, ["initial"] = initial.Level, ["drained"] = drained.Level,
                    ["poured"] = poured.Level, ["afterDepth"] = TerrainSurfaceHeightLayer.GetNaturalDepthLevels(terrain, x, y),
                    ["height"] = height, ["afterHeight"] = afterHeight, ["kind"] = kind });
            }
            return new JObject { ["samples"] = samples };
        }
        finally { generator.BeginNewWorldHydrologyEpoch(1); }
    }

    private static JObject ServiceTerrainHeightSave(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(1, 1);
        using ChunkTerrainData terrain = buffer.Seal();
        terrain.SetCell(0, 0, new TerrainCell(31, 0, 0, 0, 1, TerrainCellFlags.Walkable));
        terrain.SetEnvironmentValue("height", 0, 0, .55f);
        terrain.SetEnvironmentValue(TerrainSurfaceHeightLayer.NaturalDepthLevelsLayerId, 0, 0,
            (float)Number(args, "naturalDepth", 0));
        GroundTileLayerSystem.WriteDepth(terrain, Vector2Int.zero, 5);
        TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, 0, 0, 6);
        GroundLayerCellSaveData snapshot = GroundTileLayerSystem.Capture(terrain, Vector2Int.zero);
        GroundLayerCellSaveData serialized = MemoryPackSerializer.Deserialize<GroundLayerCellSaveData>(
            MemoryPackSerializer.Serialize(snapshot));
        GroundTileLayerSystem.WriteDepth(terrain, Vector2Int.zero, 1);
        TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, 0, 0, 1);
        GroundTileLayerSystem.Restore(terrain, serialized);
        terrain.TryGetEnvironmentValue("height", 0, 0, out float rawHeight);
        TerrainSurfaceHeightLayer.TryGetSurface(terrain, 0, 0, true, out TerrainSurfaceHeight surface);
        bool negativeRejected = false;
        try { TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, 0, 0, -1); }
        catch (ArgumentOutOfRangeException) { negativeRejected = true; }
        return new JObject
        {
            ["resourceDepth"] = GroundTileLayerSystem.ReadDepth(terrain, Vector2Int.zero),
            ["surfaceDepth"] = TerrainSurfaceHeightLayer.GetExcavatedLevels(terrain, 0, 0),
            ["savedDepth"] = serialized.ExcavatedSurfaceLevels, ["level"] = surface.Level,
            ["naturalDepth"] = TerrainSurfaceHeightLayer.GetNaturalDepthLevels(terrain, 0, 0),
            ["rawHeight"] = rawHeight, ["negativeRejected"] = negativeRejected
        };
    }

    private static JObject ServiceTerrainHeightNeighbours(JObject args)
    {
        DimensionManager originalDimensions = DimensionManager.ExistingInstance;
        using var world = new WorldRuntime("probe:terrain_height", 1);
        var profile = new ChunkGenerationProfileSnapshot("probe:terrain_height", 1, 16, 16);
        Vector2Int origin = WorldTopologyRuntime.NormalizeCell(new Vector2Int(1000000, -1000000));
        int dx = (int)Number(args, "dx", 1), dy = (int)Number(args, "dy", 0);
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || (dx == 0 && dy == 0))
            throw new ArgumentOutOfRangeException(nameof(args));
        int receiverX = dx < 0 ? 0 : dx > 0 ? 15 : 8;
        int receiverY = dy < 0 ? 0 : dy > 0 ? 15 : 8;
        var sample = new Int2(receiverX + dx, receiverY + dy);
        int neighbourX = sample.X - dx * 16, neighbourY = sample.Y - dy * 16;
        Vector2Int right = WorldTopologyRuntime.NormalizeCell(origin + new Vector2Int(dx * 16, dy * 16));
        var address = new FlatWorld.WorldModel.WorldAddress("probe:terrain_height", new Int2(origin.x, origin.y));
        var neighbourAddress = new FlatWorld.WorldModel.WorldAddress("probe:terrain_height", new Int2(right.x, right.y));
        ChunkTerrainData Commit(FlatWorld.WorldModel.WorldAddress target)
        {
            ChunkGenerationRequest request = world.BeginChunkGeneration(target, 1, profile);
            using var buffer = new ChunkTerrainBuffer(16, 16);
            using var result = new ChunkGenerationResult(request, buffer);
            if (!world.TryCommit(result, out string reason)) throw new InvalidOperationException(reason);
            world.TryGetChunkTerrain(target, out ChunkTerrainData terrain);
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                terrain.SetCell(x, y, new TerrainCell(1, 0, 0, 0, 1, TerrainCellFlags.Walkable));
                TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, x, y, 1);
            }
            if (target == neighbourAddress)
                TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, neighbourX, neighbourY, 0);
            return terrain;
        }
        ChunkTerrainData current = Commit(address);
        JObject Masks() => SampleTerrainShadowMasks(world, address, current, receiverX, receiverY);
        bool missing = TerrainHeightShadowManager.TryGetSurfaceHeight(world, address, sample, out _);
        JObject missingMasks = Masks();
        ChunkTerrainData neighbour = Commit(neighbourAddress);
        bool available = TerrainHeightShadowManager.TryGetSurfaceHeight(world, address, sample, out var first);
        JObject loadedMasks = Masks();
        TerrainSurfaceHeightLayer.SetExcavatedLevels(neighbour, neighbourX, neighbourY, 3);
        bool changed = TerrainHeightShadowManager.TryGetSurfaceHeight(world, address, sample, out var lowered);
        JObject changedMasks = Masks();
        bool evicted = world.EvictChunk(neighbourAddress);
        bool afterEviction = TerrainHeightShadowManager.TryGetSurfaceHeight(world, address, sample, out _);
        JObject evictedMasks = Masks();
        Commit(neighbourAddress);
        bool reloaded = TerrainHeightShadowManager.TryGetSurfaceHeight(world, address, sample, out var restored);
        JObject reloadedMasks = Masks();
        return new JObject
        {
            ["missing"] = missing, ["available"] = available, ["initial"] = first.Level,
            ["changed"] = changed, ["lowered"] = lowered.Level, ["evicted"] = evicted,
            ["afterEviction"] = afterEviction, ["reloaded"] = reloaded, ["restored"] = restored.Level,
            ["missingMasks"] = missingMasks, ["loadedMasks"] = loadedMasks,
            ["changedMasks"] = changedMasks, ["evictedMasks"] = evictedMasks,
            ["reloadedMasks"] = reloadedMasks,
            ["dimensionServicePreserved"] = ReferenceEquals(originalDimensions, DimensionManager.ExistingInstance)
        };
    }

    #endregion
}
