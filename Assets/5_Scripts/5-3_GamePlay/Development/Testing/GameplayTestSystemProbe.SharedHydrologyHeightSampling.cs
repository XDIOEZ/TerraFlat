using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using WorldModelAddress = FlatWorld.WorldModel.WorldAddress;

public static partial class GameplayTestSystemProbe
{
    #region 宏观水文共享高度采样

    // 调用正式区块生成和请求诊断，Lua 对实际采样次数及提交地形做断言。
    private static JObject WorldSharedHydrologyHeightSampling(JObject args)
    {
        string scenario = (string)args["scenario"] ?? "reuse";
        string algorithm = (string)args["algorithm"] ?? "simple";
        var numbers = SmallGenerationParameters();
        numbers["terrain.seaLevel"] = 0d;
        numbers["terrain.beachLevel"] = .01d;
        numbers["climate.distance.minimumRainfall"] = 1d;
        numbers["climate.distance.maximumRainfall"] = 1d;
        numbers["climate.polarBand.enabled"] = 0d;
        numbers["climate.temperature.altitudeCoolingStrength"] = 0d;
        numbers["river.startFlow"] = .0001d;
        numbers["river.tributaryStartFlow"] = .0001d;
        numbers["lake.large.enabled"] = 0d;
        var profile = new ChunkGenerationProfileSnapshot("probe:shared-height", 1, 8, 8,
            numbers, new Dictionary<string, string> { ["climate.algorithm"] = algorithm });
        var topology = (bool?)args["wrapped"] == true
            ? new ChunkGenerationTopologySnapshot(new Int2(-128, -128), new Int2(256, 256)) : default;
        const int seed = 17413;
        const string dimension = "probe:shared-height";
        var origin = new Int2(16, 16);
        var adjacent = new Int2(80, 16);
        var generator = new DeterministicChunkGenerator();
        var independent = new DeterministicChunkGenerator();
        try
        {
            if (scenario == "lifecycle")
            {
                JObject before = JObject.FromObject(generator.CaptureSharedHydrologyHeightSamplingDiagnostics());
                var cancelledRequest = new ChunkGenerationRequest(1, new WorldModelAddress(dimension, origin),
                    seed, 1, profile, topology);
                bool cancelled = false;
                using (var source = new CancellationTokenSource())
                {
                    source.Cancel();
                    try { using (generator.Generate(cancelledRequest, source.Token)) { } }
                    catch (OperationCanceledException) { cancelled = true; }
                }
                JObject afterCancel = JObject.FromObject(generator.CaptureSharedHydrologyHeightSamplingDiagnostics());
                CaptureSharedHydrologyHeightTerrain(generator, profile, seed, 1, dimension, origin, topology);
                JObject beforeRetire = JObject.FromObject(generator.CaptureSharedHydrologyHeightSamplingDiagnostics());
                generator.BeginNewWorldHydrologyEpoch(1);
                JObject afterRetire = JObject.FromObject(generator.CaptureSharedHydrologyHeightSamplingDiagnostics());
                bool retiredRejected = false;
                try { using (generator.Generate(cancelledRequest, CancellationToken.None)) { } }
                catch (OperationCanceledException) { retiredRejected = true; }
                return new JObject
                {
                    ["before"] = before, ["afterCancel"] = afterCancel, ["cancelled"] = cancelled,
                    ["beforeRetire"] = beforeRetire, ["afterRetire"] = afterRetire,
                    ["retiredRejected"] = retiredRejected,
                    ["rebuilt"] = CaptureSharedHydrologyHeightTerrain(generator, profile, seed, 2, dimension, origin, topology),
                    ["independent"] = CaptureSharedHydrologyHeightTerrain(independent, profile, seed, 2, dimension, origin, topology)
                };
            }

            if (scenario == "concurrency")
            {
                for (int i = 0; i < 7; i++)
                    CaptureSharedHydrologyHeightTerrain(generator, profile, seed + i, 1, dimension, origin, topology);
                var tasks = new Task<JObject>[4];
                for (int i = 0; i < tasks.Length; i++)
                {
                    var position = new Int2(16 + i * 64, 16);
                    tasks[i] = Task.Run(() => CaptureSharedHydrologyHeightTerrain(generator, profile,
                        seed + 20, 1, dimension, position, topology));
                }
                Task.WhenAll(tasks).GetAwaiter().GetResult();
                bool matches = true;
                for (int i = 0; i < tasks.Length; i++)
                {
                    JObject reference = CaptureSharedHydrologyHeightTerrain(independent, profile, seed + 20,
                        1, dimension, new Int2(16 + i * 64, 16), topology);
                    matches &= (string)tasks[i].Result["hash"] == (string)reference["hash"];
                }
                return new JObject { ["concurrentMatches"] = matches,
                    ["after"] = JObject.FromObject(generator.CaptureSharedHydrologyHeightSamplingDiagnostics()) };
            }

            JObject cold = CaptureSharedHydrologyHeightTerrain(generator, profile, seed, 1, dimension, origin, topology);
            if (scenario == "reuse")
            {
                var resized = profile.WithChunkSize(9, 9, false);
                var climateGrid = profile.WithNumericParameter("climate.distance.chunkWidth", 32d);
                return new JObject
                {
                    ["cold"] = cold,
                    ["adjacent"] = CaptureSharedHydrologyHeightTerrain(generator, profile, seed, 1, dimension, adjacent, topology),
                    ["independent"] = CaptureSharedHydrologyHeightTerrain(independent, profile, seed, 1, dimension, adjacent, topology),
                    ["resized"] = CaptureSharedHydrologyHeightTerrain(generator, resized, seed, 1, dimension, origin, topology),
                    ["resizedIndependent"] = CaptureSharedHydrologyHeightTerrain(independent, resized, seed, 1, dimension, origin, topology),
                    ["climateGrid"] = CaptureSharedHydrologyHeightTerrain(generator, climateGrid, seed, 1, dimension, origin, topology),
                    ["gridIndependent"] = CaptureSharedHydrologyHeightTerrain(independent, climateGrid, seed, 1, dimension, origin, topology)
                };
            }

            int selectedSeed = seed;
            long selectedEpoch = 1;
            string selectedDimension = dimension;
            ChunkGenerationProfileSnapshot selectedProfile = profile;
            ChunkGenerationTopologySnapshot selectedTopology = topology;
            switch (scenario)
            {
                case "seed": selectedSeed++; break;
                case "dimension": selectedDimension += ":other"; break;
                case "epoch": selectedEpoch++; break;
                case "topology": selectedTopology = new ChunkGenerationTopologySnapshot(new Int2(-256, -256), new Int2(512, 512)); break;
                case "height": selectedProfile = profile.WithNumericParameter("terrain.noiseScale", .015d); break;
                case "algorithm":
                    selectedProfile = new ChunkGenerationProfileSnapshot(profile.ProfileId, profile.Signature,
                        profile.Width, profile.Height, numbers,
                        new Dictionary<string, string> { ["climate.algorithm"] = "legacy" });
                    break;
                default: throw new ArgumentException("未知共享高度采样场景：" + scenario);
            }
            return new JObject
            {
                ["changed"] = CaptureSharedHydrologyHeightTerrain(generator, selectedProfile, selectedSeed,
                    selectedEpoch, selectedDimension, origin, selectedTopology),
                ["independent"] = CaptureSharedHydrologyHeightTerrain(independent, selectedProfile, selectedSeed,
                    selectedEpoch, selectedDimension, origin, selectedTopology)
            };
        }
        finally
        {
            generator.BeginNewWorldHydrologyEpoch(2);
            independent.BeginNewWorldHydrologyEpoch(2);
        }
    }

    private static JObject CaptureSharedHydrologyHeightTerrain(DeterministicChunkGenerator generator,
        ChunkGenerationProfileSnapshot profile, int seed, long epoch, string dimension, Int2 origin,
        ChunkGenerationTopologySnapshot topology)
    {
        using var world = new WorldRuntime("probe:shared-height", epoch);
        var address = new WorldModelAddress(dimension, origin);
        ChunkGenerationRequest request = world.BeginChunkGeneration(address, seed, profile, topology);
        ChunkGenerationTiming timing = new ChunkStreamingDiagnostics().BeginRequest(request);
        if (timing == null) throw new InvalidOperationException("共享高度采样需要正式请求诊断。");
        timing.StartGeneration();
        using ChunkGenerationResult generated = generator.Generate(request, CancellationToken.None, timing);
        timing.FinishGeneration();
        if (!world.TryCommit(generated, out string error)) throw new InvalidOperationException(error);
        if (!world.TryGetChunk(address, out ChunkRuntime chunk) || chunk.Terrain == null)
            throw new InvalidOperationException("共享高度采样没有生成正式地形。");
        var counts = timing.Capture().GenerationCounts;
        if (!counts.TryGetValue("river.macro.height_computes", out long computed) ||
            !counts.TryGetValue("river.macro.height_shared_hits", out long sharedHits))
            throw new InvalidOperationException("缺少宏观水文高度采样次数。");
        return new JObject { ["hash"] = chunk.Terrain.ComputeStableHash().ToString(),
            ["computed"] = computed, ["sharedHits"] = sharedHits };
    }

    #endregion
}
