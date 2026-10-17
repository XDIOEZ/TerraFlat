using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 气候缓存身份与正式生成复用

    // 独立小区块调用正式生成器，Lua 检查缓存构建数和真实地形，不代算气候规则。
    private static JObject WorldClimateCache(JObject args)
    {
        string scenario = (string)args["scenario"] ?? "reuse";
        string algorithm = (string)args["algorithm"] ?? "simple";
        var numbers = SmallGenerationParameters();
        numbers["climate.distance.chunkWidth"] = 16d;
        numbers["climate.distance.chunkHeight"] = 16d;
        numbers["climate.temperature.blendRadius"] = 0d;
        numbers["lake.large.enabled"] = 0d;
        var texts = new Dictionary<string, string> { ["climate.algorithm"] = algorithm };
        var profile = new ChunkGenerationProfileSnapshot("probe:climate-cache", 1, 4, 4, numbers, texts);
        if (scenario == "identity")
            return CaptureClimateCacheIdentities(profile);

        bool wrapped = (bool?)args["wrapped"] ?? false;
        var topology = wrapped
            ? new ChunkGenerationTopologySnapshot(new Int2(0, 0), new Int2(128, 128)) : default;
        var generator = new DeterministicChunkGenerator();
        const int seed = 17413;
        const string dimension = "probe:climate-cache";
        try
        {
            JObject before = JObject.FromObject(generator.CaptureClimateCacheDiagnostics());
            JObject cold = CaptureClimateCacheTerrain(generator, profile, seed, 1, dimension, topology);
            JObject afterCold = JObject.FromObject(generator.CaptureClimateCacheDiagnostics());
            if (scenario == "reuse")
            {
                JObject ecology = CaptureClimateCacheTerrain(generator,
                    profile.WithEcology(2d, Array.Empty<EcologySpawnRuleSnapshot>()),
                    seed, 1, dimension, topology);
                JObject afterEcology = JObject.FromObject(generator.CaptureClimateCacheDiagnostics());
                ChunkGenerationProfileSnapshot expandedProfile = profile.WithChunkSize(5, 5, false);
                JObject expanded = CaptureClimateCacheTerrain(generator, expandedProfile,
                    seed, 1, dimension, topology);
                JObject afterExpanded = JObject.FromObject(generator.CaptureClimateCacheDiagnostics());
                JObject repeated = CaptureClimateCacheTerrain(generator, profile, seed, 1, dimension, topology);
                JObject afterRepeated = JObject.FromObject(generator.CaptureClimateCacheDiagnostics());
                return new JObject
                {
                    ["before"] = before, ["afterCold"] = afterCold, ["afterEcology"] = afterEcology,
                    ["afterExpanded"] = afterExpanded, ["afterRepeated"] = afterRepeated,
                    ["cold"] = cold, ["ecology"] = ecology, ["expanded"] = expanded, ["repeated"] = repeated
                };
            }

            int selectedSeed = seed;
            long selectedEpoch = 1;
            string selectedDimension = dimension;
            ChunkGenerationTopologySnapshot selectedTopology = topology;
            ChunkGenerationProfileSnapshot selectedProfile = profile;
            switch (scenario)
            {
                case "seed": selectedSeed++; break;
                case "epoch": selectedEpoch++; break;
                case "dimension": selectedDimension += "-other"; break;
                case "topology":
                    selectedTopology = new ChunkGenerationTopologySnapshot(new Int2(0, 0), new Int2(128, 128));
                    break;
                case "parameter": selectedProfile = profile.WithNumericParameter("terrain.noiseScale", .015d); break;
                default: throw new ArgumentException("未知气候缓存场景：" + scenario);
            }
            JObject changed = CaptureClimateCacheTerrain(generator, selectedProfile, selectedSeed,
                selectedEpoch, selectedDimension, selectedTopology);
            return new JObject
            {
                ["before"] = before, ["afterCold"] = afterCold,
                ["afterChanged"] = JObject.FromObject(generator.CaptureClimateCacheDiagnostics()),
                ["cold"] = cold, ["changed"] = changed
            };
        }
        finally
        {
            generator.BeginNewWorldHydrologyEpoch(2);
        }
    }

    private static JObject CaptureClimateCacheIdentities(ChunkGenerationProfileSnapshot profile)
    {
        JObject Identity(ChunkGenerationProfileSnapshot selected) => new()
        {
            ["climate"] = selected.ClimateFingerprint.ToString(),
            ["generation"] = selected.GenerationFingerprint.ToString()
        };
        var numbers = new Dictionary<string, double>(profile.NumericParameters, StringComparer.Ordinal);
        var texts = new Dictionary<string, string>(profile.TextParameters, StringComparer.Ordinal);
        var macroProfile = new ChunkGenerationProfileSnapshot(profile.ProfileId, profile.Signature,
            64, 64, numbers, texts);
        var mineralProfile = new ChunkGenerationProfileSnapshot(profile.ProfileId, profile.Signature,
            profile.Width, profile.Height, numbers, texts, caveResourceRules:
            new[] { new CaveResourceRuleSnapshot("probe:ore", "probe:ore", .5d, .02d, 7) });
        var changedText = new Dictionary<string, string>(texts) { ["climate.algorithm"] = "legacy" };
        return new JObject
        {
            ["base"] = Identity(profile),
            ["macro"] = Identity(macroProfile),
            ["expanded"] = Identity(profile.WithChunkSize(5, 5, false)),
            ["ecology"] = Identity(profile.WithEcology(2d, Array.Empty<EcologySpawnRuleSnapshot>())),
            ["minerals"] = Identity(mineralProfile),
            ["pairing"] = Identity(profile.WithCavePortalPairing(
                new CavePortalPairingSnapshot("probe:paired-surface", 27, profile))),
            ["resizedGrid"] = Identity(profile.WithChunkSize(5, 5)),
            ["height"] = Identity(profile.WithNumericParameter("terrain.noiseScale", .015d)),
            ["temperature"] = Identity(profile.WithNumericParameter("climate.temperature.altitudeCoolingStrength", .1d)),
            ["rainfall"] = Identity(profile.WithNumericParameter("climate.distance.minimumRainfall", .2d)),
            ["scale"] = Identity(profile.WithNumericParameter("world.spatialDistanceScale", 2d)),
            ["text"] = Identity(new ChunkGenerationProfileSnapshot(profile.ProfileId, profile.Signature,
                profile.Width, profile.Height, numbers, changedText)),
            ["profile"] = Identity(new ChunkGenerationProfileSnapshot(profile.ProfileId + "-other", profile.Signature,
                profile.Width, profile.Height, numbers, texts)),
            ["signature"] = Identity(new ChunkGenerationProfileSnapshot(profile.ProfileId, profile.Signature + 1,
                profile.Width, profile.Height, numbers, texts))
        };
    }

    private static JObject CaptureClimateCacheTerrain(DeterministicChunkGenerator generator,
        ChunkGenerationProfileSnapshot profile, int seed, long epoch, string dimension,
        ChunkGenerationTopologySnapshot topology)
    {
        using var world = new WorldRuntime("probe:climate-cache", epoch);
        // 留足四边距离，避免后台邻区预热影响冷缓存构建计数。
        var address = new FlatWorld.WorldModel.WorldAddress(dimension, new Int2(16, 16));
        ChunkGenerationRequest request = world.BeginChunkGeneration(address, seed, profile, topology);
        using ChunkGenerationResult generated = generator.Generate(request, CancellationToken.None);
        if (!world.TryCommit(generated, out string reason)) throw new InvalidOperationException(reason);
        if (!world.TryGetChunk(address, out ChunkRuntime chunk) || chunk.Terrain == null)
            throw new InvalidOperationException("气候缓存采样没有生成正式地形。");
        var heightBits = new JArray();
        for (int y = 0; y < chunk.Terrain.Height; y++)
        for (int x = 0; x < chunk.Terrain.Width; x++)
        {
            if (!chunk.Terrain.TryGetEnvironmentValue("height", x, y, out float height))
                throw new InvalidOperationException("气候缓存采样缺少生成高度。");
            heightBits.Add(BitConverter.SingleToInt32Bits(height));
        }
        return new JObject
        {
            ["terrainHash"] = chunk.Terrain.ComputeStableHash().ToString(),
            ["width"] = chunk.Terrain.Width, ["height"] = chunk.Terrain.Height, ["heightBits"] = heightBits
        };
    }

    #endregion
}
