using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 天然宿主缓存规则采样

    // 夹具只控制正式扩展的地形输入与计数，缓存命中和宿主判断均调用生产入口。
    private static JObject WorldEcologyNeighborhoodCache(JObject args)
    {
        string scenario = (string)args["scenario"] ?? "identity";
        int seed = (int)Number(args, "seed", 17413d);
        var flower = new EcologySpawnRuleSnapshot("probe:flower", "probe-flower", 1, 1d, 1d, 0,
            .25d, .75d, 0d, 1d, 0d, 1d, providedTags: new[] { "flower" });
        var tree = new EcologySpawnRuleSnapshot("probe:tree", "probe-tree", 1, 1d, 1d, 0,
            0d, 1d, 0d, 1d, 0d, 1d, providedTags: new[] { "tree" });
        var hive = new EcologySpawnRuleSnapshot("probe:hive", "probe-hive", 1, 0d, 1d, 0,
            0d, 1d, 0d, 1d, 0d, 1d, companionOnly: true, companionHostTag: "tree",
            requiredChunkTag: "flower", companionSpawnChance: 1d, requiredTagChunkRadius: 1);
        var numbers = SmallGenerationParameters();
        numbers["river.enabled"] = 0d;
        numbers["lake.large.enabled"] = 0d;
        var profile = new ChunkGenerationProfileSnapshot("probe:ecology-cache", 1, 4, 4,
            numbers, ecologyRules: new[] { flower, tree, hive });
        var changedProfile = new ChunkGenerationProfileSnapshot("probe:ecology-cache", 2, 4, 4,
            numbers, ecologyRules: new[] { flower, tree, hive });
        int builds = 0;
        CancellationTokenSource cancelDuringBuild = null;
        var generator = new DeterministicChunkGenerator(terrainDecorator: (request, terrain, _) =>
        {
            builds++;
            bool warm = scenario != "identity" || request.WorldSeed == seed && request.WorldEpoch == 1 &&
                request.Address.DimensionId == "probe-surface" && request.Address.ChunkOrigin.Equals(new Int2(0, 0)) &&
                request.Profile.Signature == 1 && !request.Topology.IsWrapped;
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                terrain.SetCell(x, y, new TerrainCell(1, 0, 0, 1, 1, TerrainCellFlags.Walkable));
                terrain.SetLiquid(x, y, 0, 0);
                terrain.SetEnvironmentValue("temperature", x, y, warm ? .5f : .1f);
                terrain.SetEnvironmentValue("precipitation", x, y, .5f);
                terrain.SetEnvironmentValue("height", x, y, .5f);
                terrain.SetEnvironmentValue("riverFloodplain", x, y, 0f);
            }
            cancelDuringBuild?.Cancel();
        });
        var samples = new JObject();
        ChunkGenerationRequest Request(long epoch = 1, int? worldSeed = null,
            string dimension = "probe-surface", Int2 origin = default,
            ChunkGenerationProfileSnapshot selected = null,
            ChunkGenerationTopologySnapshot topology = default, long version = 1) =>
            new(epoch, new FlatWorld.WorldModel.WorldAddress(dimension, origin), worldSeed ?? seed,
                version, selected ?? profile, topology);
        void Query(string name, ChunkGenerationRequest request, string tag = "flower",
            CancellationToken token = default)
        {
            int before = builds;
            bool? found = null;
            string exception = WorldException(() => found = generator.HasHostTagInChunk(request, tag, token));
            samples[name] = new JObject { ["found"] = found.HasValue ? new JValue(found.Value) : JValue.CreateNull(),
                ["builds"] = builds, ["built"] = builds - before, ["exception"] = exception };
        }
        try
        {
            if (scenario == "identity")
            {
                Query("initial", Request());
                Query("caseRepeat", Request(), "FLOWER");
                Query("newRequestVersion", Request(version: 2));
                Query("otherSeed", Request(worldSeed: seed + 1));
                Query("negativeRepeat", Request(worldSeed: seed + 1));
                Query("otherEpoch", Request(epoch: 2));
                Query("otherDimension", Request(dimension: "other-surface"));
                Query("otherOrigin", Request(origin: new Int2(4, 0)));
                Query("otherProfile", Request(selected: changedProfile));
                Query("wrapped", Request(topology: new ChunkGenerationTopologySnapshot(new Int2(0, 0), new Int2(16, 16))));
                Query("missingTag", Request(), "missing-host-tag");
            }
            else if (scenario == "lifecycle")
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    cancelDuringBuild = cancellation;
                    Query("cancelledBuild", Request(), token: cancellation.Token);
                    cancelDuringBuild = null;
                }
                Query("retry", Request());
                Query("retryRepeat", Request());
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    Query("cancelledHit", Request(), token: cancellation.Token);
                }
                generator.BeginNewWorldHydrologyEpoch(1);
                Query("retiredEpoch", Request());
                Query("newEpoch", Request(epoch: 2));
                Query("newEpochRepeat", Request(epoch: 2));
            }
            else if (scenario == "prefill")
            {
                samples["first"] = CaptureGeneratedChunk(generator, profile, seed, new Int2(0, 0), default);
                Query("afterGeneration", Request());
                samples["second"] = CaptureGeneratedChunk(generator, profile, seed, new Int2(0, 0), default);
                Query("afterRepeatedGeneration", Request());
            }
            else throw new ArgumentException("未知生态缓存规则采样：" + scenario);
            return new JObject { ["scenario"] = scenario, ["samples"] = samples };
        }
        finally { generator.BeginNewWorldHydrologyEpoch(2); }
    }

    #endregion

    #region 出生直线搜索采样

    // 只用正式地形扩展设置水面和障碍，候选顺序、落点判断与取消都交给生产入口。
    private static JObject WorldSurfaceSpawnLine(JObject args)
    {
        string scenario = Text(args, "scenario", "mixed");
        int budget = (int)Number(args, "budget", 8d);
        var anchor = new Int2((int)Number(args, "x", 0d), (int)Number(args, "y", 0d));
        bool wrapped = scenario is "wrapped" or "wrapped_empty";
        var topology = wrapped
            ? new ChunkGenerationTopologySnapshot(new Int2(-4, -2), new Int2(8, 4)) : default;
        int distance = scenario == "anchor" ? 0 : scenario == "distant" ? 513 : 4;
        var target = new Int2(topology.NormalizeX(anchor.X + distance), topology.NormalizeY(anchor.Y + distance));
        var numbers = SmallGenerationParameters();
        numbers["terrain.seaLevel"] = 0d;
        numbers["river.enabled"] = 0d;
        numbers["lake.large.enabled"] = 0d;
        numbers["climate.polarBand.enabled"] = 0d;
        numbers["climate.temperature.celsiusMin"] = 20d;
        numbers["climate.temperature.celsiusMax"] = 20d;
        var profile = new ChunkGenerationProfileSnapshot("probe:spawn-line", 1, 4, 4, numbers);
        var samples = new JArray();
        using var cancellation = new CancellationTokenSource();
        bool contextMatches = true;
        var generator = new DeterministicChunkGenerator(terrainDecorator: (request, terrain, _) =>
        {
            Int2 cell = request.Address.ChunkOrigin;
            samples.Add(new JObject { ["x"] = cell.X, ["y"] = cell.Y });
            contextMatches &= request.WorldEpoch == 7 && request.WorldSeed == 17413 &&
                              request.Address.DimensionId == "probe-surface";
            bool dry = cell.Equals(target) && scenario is not ("empty" or "wrapped_empty" or "cancel_during");
            bool blocked = !dry && samples.Count % 3 == 2;
            terrain.SetCell(0, 0, new TerrainCell(1, 0, blocked ? 2 : 0, 1, 1,
                blocked ? TerrainCellFlags.Blocking : TerrainCellFlags.Walkable));
            terrain.SetLiquid(0, 0, dry || blocked ? 0 : LiquidTypeCatalog.BuiltIn.GetIndex(
                samples.Count % 3 == 0 ? LiquidTypeCatalog.LavaId : LiquidTypeCatalog.DirtyWaterId),
                dry || blocked ? 0 : 10);
            if (scenario == "cancel_during" && samples.Count == 3) cancellation.Cancel();
        });
        bool found = false, cancelled = false;
        Int2 result = anchor;
        try
        {
            if (scenario == "cancelled") cancellation.Cancel();
            found = generator.TryFindWalkableSurfaceAlongLine("probe-surface", 17413, profile,
                topology, anchor, budget, out result, cancellation.Token, 7);
        }
        catch (OperationCanceledException) { cancelled = true; }
        finally { generator.BeginNewWorldHydrologyEpoch(7); }
        return new JObject
        {
            ["found"] = found, ["cancelled"] = cancelled, ["x"] = result.X, ["y"] = result.Y,
            ["samples"] = samples, ["contextMatches"] = contextMatches
        };
    }

    #endregion

    #region 纯地形冷暖生成采样

    private static Dictionary<string, double> SmallGenerationParameters()
    {
        var numbers = new Dictionary<string, double>
        {
            ["world.spatialDistanceScale"] = 1d,
            ["river.hydrologyRegionSize"] = 64d, ["river.runoffCellSize"] = 16d,
            ["river.maxTraceSteps"] = 32d, ["river.maxWidth"] = 3d,
            ["river.floodplainMaxRadius"] = 2d, ["river.minimumVisibleCourseLength"] = 0d,
            ["climate.distance.oceanRadiusChunks"] = 1d,
            ["climate.distance.freshwaterRadiusChunks"] = 1d,
            ["climate.distance.chunkWidth"] = 1d, ["climate.distance.chunkHeight"] = 1d,
            ["airHumidity.radiusTiles"] = 1d, ["biome.peat.stoneBoundaryRadius"] = 1d,
            ["structure.enabled"] = 0d, ["cave.portal.enabled"] = 0d,
            ["lake.lava.enabled"] = 0d
        };
        DiscreteProbabilityConfiguration.Capture().WriteGenerationParameters(numbers);
        return numbers;
    }

    private static JObject WorldCachedTerrainGeneration(JObject args)
    {
        int seed = (int)Number(args, "seed", 17413d);
        bool wrapped = (bool?)args["wrapped"] ?? false;
        int origin = (int)Number(args, "origin", 0d);
        var numbers = SmallGenerationParameters();
        numbers["terrain.seaLevel"] = 0d;
        numbers["terrain.beachLevel"] = .01d;
        numbers["climate.distance.minimumRainfall"] = 1d;
        numbers["climate.distance.maximumRainfall"] = 1d;
        numbers["climate.polarBand.enabled"] = 0d;
        // 默认 simple 气候不读旧噪声温度范围，关闭海拔降温才能让水文夹具保留液态河湖。
        numbers["climate.temperature.altitudeCoolingStrength"] = 0d;
        numbers["river.startFlow"] = .0001d;
        numbers["river.tributaryStartFlow"] = .0001d;
        numbers["river.floodplainStartFlow"] = .0001d;
        numbers["lake.large.enabled"] = 1d;
        numbers["lake.large.regionSize"] = 32d;
        numbers["lake.large.chance"] = 1d;
        numbers["lake.large.minRadius"] = 8d;
        numbers["lake.large.maxRadius"] = 9d;
        numbers["lake.large.islandChance"] = 0d;
        var profile = new ChunkGenerationProfileSnapshot("probe:cached-hydrology", 1, 32, 32, numbers);
        var topology = wrapped
            ? new ChunkGenerationTopologySnapshot(new Int2(-64, -64), new Int2(128, 128)) : default;
        var point = new Int2(origin, origin);
        var generator = new DeterministicChunkGenerator();
        var fresh = new DeterministicChunkGenerator();
        try
        {
            JObject cold = CaptureGeneratedChunk(generator, profile, seed, point, topology);
            _ = CaptureGeneratedChunk(generator, profile, seed, new Int2(origin + 32, origin), topology);
            JObject warmed = CaptureGeneratedChunk(generator, profile, seed, point, topology);
            JObject independent = CaptureGeneratedChunk(fresh, profile, seed, point, topology);
            return new JObject { ["cold"] = cold, ["warmed"] = warmed, ["independent"] = independent };
        }
        finally
        {
            generator.BeginNewWorldHydrologyEpoch(1);
            fresh.BeginNewWorldHydrologyEpoch(1);
        }
    }

    private static JObject CaptureGeneratedChunk(DeterministicChunkGenerator generator,
        ChunkGenerationProfileSnapshot profile, int seed, Int2 origin,
        ChunkGenerationTopologySnapshot topology)
    {
        using var world = new WorldRuntime("probe:generated-world", 1);
        var address = new FlatWorld.WorldModel.WorldAddress("probe-surface", origin);
        ChunkGenerationRequest request = world.BeginChunkGeneration(address, seed, profile, topology);
        using ChunkGenerationResult generated = generator.Generate(request, CancellationToken.None);
        if (!world.TryCommit(generated, out string error))
            throw new InvalidOperationException("独立地形采样提交失败：" + error);
        if (!world.TryGetChunk(address, out ChunkRuntime chunk) || chunk.Terrain == null)
            throw new InvalidOperationException("独立地形采样缺少已提交区块。");
        ChunkTerrainData terrain = chunk.Terrain;
        var placements = new JArray();
        foreach (NaturalItemPlacement item in chunk.Ecology.Placements)
            placements.Add(new JObject { ["guid"] = item.Guid, ["itemId"] = item.ItemId,
                ["x"] = item.LocalX, ["y"] = item.LocalY, ["offsetX"] = item.OffsetX,
                ["offsetY"] = item.OffsetY, ["hostGuid"] = item.HostGuid, ["ruleId"] = item.RuleId });
        int waterCells = 0, riverCells = 0, lakeCells = 0, environmentValues = 0;
        var heightBits = new JArray();
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
        {
            if (terrain.GetLiquidDepth(x, y) > 0f) waterCells++;
            if (terrain.TryGetEnvironmentValue("riverKind", x, y, out float kind))
            {
                // riverKind 正式环境协议以 1 表示河流、2 表示湖泊。
                if (kind == 1f) riverCells++;
                if (kind == 2f) lakeCells++;
            }
            bool heightFound = terrain.TryGetEnvironmentValue("height", x, y, out float height);
            if (heightFound) environmentValues++;
            heightBits.Add(BitConverter.SingleToInt32Bits(height));
        }
        return new JObject { ["terrainHash"] = terrain.ComputeStableHash().ToString(), ["placements"] = placements,
            ["cellCount"] = terrain.CellCount, ["waterCells"] = waterCells, ["riverCells"] = riverCells,
            ["lakeCells"] = lakeCells, ["environmentValues"] = environmentValues, ["heightBits"] = heightBits };
    }

    #endregion
}
