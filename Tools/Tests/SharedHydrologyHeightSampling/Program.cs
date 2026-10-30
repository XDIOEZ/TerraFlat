using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;

internal static class Program
{
    #region 正式宏观水文结果对比

    private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type Generator = typeof(DeterministicChunkGenerator);
    private static readonly Type Cache = Generator.GetNestedType("SharedHydrologyHeightSamplingCache", BindingFlags.NonPublic);
    private static readonly Type Sampling = Generator.GetNestedType("HydrologySamplingContext", BindingFlags.NonPublic);
    private static readonly MethodInfo Build = Generator.GetMethod("BuildMacroHydrologyRegion", NonPublicStatic);
    private static readonly MethodInfo SampleHeight = Generator.GetMethod("SampleHeight", NonPublicStatic);
    private static readonly PropertyInfo CurrentTiming = typeof(ChunkGenerationTiming)
        .GetProperty("CurrentOnThread", NonPublicStatic);

    private static object Create(Type type, params object[] args) => Activator.CreateInstance(type,
        AnyInstance, null, args, null);

    private static object Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, AnyInstance).Invoke(target, args);

    private static long Number(object target, string name) => Convert.ToInt64(
        target.GetType().GetProperty(name, AnyInstance).GetValue(target));

    private static ChunkGenerationProfileSnapshot Profile(string algorithm, int width = 64, int height = 64) => new(
        "macro-height-test", 1, width, height, new Dictionary<string, double>
        {
            ["terrain.seaLevel"] = 0, ["river.runoffCellSize"] = 16,
            ["river.maxTraceSteps"] = 48, ["river.maxWidth"] = 3,
            ["river.floodplainMaxRadius"] = 2, ["river.startFlow"] = .0001,
            ["river.tributaryStartFlow"] = .0001, ["climate.distance.minimumRainfall"] = 1,
            ["climate.distance.maximumRainfall"] = 1, ["climate.distance.chunkWidth"] = 8,
            ["climate.distance.chunkHeight"] = 8, ["climate.distance.freshwaterRadiusChunks"] = 1,
            ["airHumidity.radiusTiles"] = 1, ["biome.peat.stoneBoundaryRadius"] = 1,
            ["lake.large.enabled"] = 0, ["lake.lava.enabled"] = 0, ["structure.enabled"] = 0
        }, new Dictionary<string, string> { ["climate.algorithm"] = algorithm });

    private static ChunkGenerationRequest Request(ChunkGenerationProfileSnapshot profile, int x = 0, int y = 0,
        int seed = 17413, long epoch = 1, string dimension = "surface",
        ChunkGenerationTopologySnapshot topology = default) =>
        new(epoch, new WorldAddress(dimension, new Int2(x, y)), seed, 1, profile, topology);

    private static string RegionHash(object region)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (string name in new[] { "Positions", "Heights", "Flows", "Terminals", "Downstream", "Visible", "PrimaryUpstream" })
        {
            var values = (Array)region.GetType().GetField(name, AnyInstance).GetValue(region);
            writer.Write(name);
            writer.Write(values.Length);
            foreach (object value in values)
            {
                if (value is Int2 position) { writer.Write(position.X); writer.Write(position.Y); }
                else if (value is double number) writer.Write(BitConverter.DoubleToInt64Bits(number));
                else if (value is int integer) writer.Write(integer);
                else writer.Write((bool)value);
            }
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static (string Hash, long Computes, long SharedHits) BuildRegion(
        ChunkGenerationRequest request, object cache)
    {
        var timing = new ChunkStreamingDiagnostics().BeginRequest(request);
        if (timing == null) throw new Exception("Production generation diagnostics are disabled");
        timing.StartGeneration();
        CurrentTiming.SetValue(null, timing);
        object region;
        try
        {
            object[] arguments = Cache == null
                ? new object[] { request, request.Profile.Settings, CancellationToken.None }
                : new object[] { request, request.Profile.Settings, CancellationToken.None, cache };
            region = Build.Invoke(null, arguments);
        }
        finally { CurrentTiming.SetValue(null, null); }
        timing.FinishGeneration();
        var counts = timing.Capture().GenerationCounts;
        counts.TryGetValue("river.macro.height_computes", out long computes);
        counts.TryGetValue("river.macro.height_shared_hits", out long hits);
        return (RegionHash(region), computes, hits);
    }

    private static object[] CaptureMacroCases()
    {
        var results = new List<object>();
        foreach (string algorithm in new[] { "simple", "legacy" })
        foreach (int span in new[] { 0, 256, 328 })
        {
            var topology = span == 0 ? default :
                new ChunkGenerationTopologySnapshot(new Int2(-span / 2, -span / 2), new Int2(span, span));
            var profile = Profile(algorithm);
            object cache = Cache == null ? null : Create(Cache);
            var first = BuildRegion(Request(profile, -64, -64, topology: topology), cache);
            var next = BuildRegion(Request(profile, 0, -64, topology: topology), cache);
            var independent = BuildRegion(Request(profile, 0, -64, topology: topology), Cache == null ? null : Create(Cache));
            if (next.Hash != independent.Hash) throw new Exception("Shared macro region changed its exact result");
            if (Cache != null && (first.Computes <= 0 || first.SharedHits != 0 ||
                next.SharedHits <= 0 || next.Computes >= independent.Computes))
                throw new Exception($"No production sampling reuse for {algorithm}/{span}");
            results.Add(new { name = $"{algorithm}-{span}-first", hash = first.Hash });
            results.Add(new { name = $"{algorithm}-{span}-adjacent", hash = next.Hash,
                computed = next.Computes, hits = next.SharedHits, coldComputed = independent.Computes });
        }
        return results.ToArray();
    }

    #endregion

    #region 共享缓存容量、取消、身份和并发

    private static object NewSampling(ChunkGenerationRequest request, object cache) =>
        Create(Sampling, request, request.Profile.Settings, cache);

    private static double Height(object sampling, int x, int y) => (double)Invoke(sampling,
        "Height", new Int2(x, y));

    private static void Publish(object sampling, CancellationToken token = default) =>
        Invoke(sampling, "PublishCompletedHeightSamples", token);

    private static object Stats(object cache) => Invoke(cache, "CaptureDiagnostics");

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void VerifyIdentity(ChunkGenerationRequest first, ChunkGenerationRequest second,
        bool expectShared, string label)
    {
        object cache = Create(Cache);
        var timing = new ChunkStreamingDiagnostics().BeginRequest(second);
        CurrentTiming.SetValue(null, timing);
        try
        {
            object initial = NewSampling(first, cache);
            Height(initial, 27, -17);
            Publish(initial);
            object next = NewSampling(second, cache);
            double actual = Height(next, 27, -17);
            double direct = (double)SampleHeight.Invoke(null, new object[] { second, second.Profile.Settings, 27, -17 });
            Assert(BitConverter.DoubleToInt64Bits(actual) == BitConverter.DoubleToInt64Bits(direct),
                "Production height differs for " + label);
            Assert(Number(next, "HeightSharedHitCount") == (expectShared ? 1 : 0) &&
                   Number(next, "HeightMissCount") == (expectShared ? 0 : 1),
                "Incorrect height cache identity: " + label);
        }
        finally { CurrentTiming.SetValue(null, null); }
    }

    private static void VerifySharedSamplingIdentities()
    {
        var simple = Profile("simple");
        var first = Request(simple);
        VerifyIdentity(first, Request(simple.WithChunkSize(91, 77, false)), true, "temporary output size");
        VerifyIdentity(first, Request(simple.WithNumericParameter("climate.distance.chunkWidth", 32)), true, "climate grid");
        VerifyIdentity(first, Request(simple.WithNumericParameter("terrain.height.frequency", .3)), true, "unused legacy noise");
        VerifyIdentity(first, Request(simple, seed: 4), false, "seed");
        VerifyIdentity(first, Request(simple, epoch: 2), false, "epoch");
        VerifyIdentity(first, Request(simple, dimension: "other"), false, "dimension");
        VerifyIdentity(first, Request(simple, topology: new ChunkGenerationTopologySnapshot(new Int2(-128, -128), new Int2(256, 256))),
            false, "wrapped topology");
        var wrapped = Request(simple, topology: new ChunkGenerationTopologySnapshot(new Int2(-128, -128), new Int2(256, 256)));
        VerifyIdentity(wrapped, Request(simple, topology: new ChunkGenerationTopologySnapshot(new Int2(-128, -128), new Int2(320, 256))),
            false, "wrapped span");
        VerifyIdentity(wrapped, Request(simple, topology: new ChunkGenerationTopologySnapshot(new Int2(-160, -128), new Int2(256, 256))),
            false, "wrapped origin");
        VerifyIdentity(first, Request(simple.WithNumericParameter("terrain.noiseScale", .015)), false, "simple scale");
        VerifyIdentity(first, Request(simple.WithNumericParameter("terrain.octaves", 6)), false, "simple octaves");
        var legacy = Profile("legacy");
        VerifyIdentity(first, Request(legacy), false, "algorithm");
        foreach (var pair in new Dictionary<string, double>
        {
            ["terrain.height.coordScale"] = 3, ["terrain.height.frequency"] = .09,
            ["terrain.height.octaves"] = 7, ["terrain.height.lacunarity"] = 3,
            ["terrain.height.persistence"] = .7, ["terrain.height.offsetX"] = 123,
            ["terrain.height.offsetY"] = 456, ["terrain.height.secondaryBoostEnabled"] = 0,
            ["terrain.height.secondaryBoostStrength"] = .2, ["world.spatialDistanceScale"] = 2
        })
            VerifyIdentity(Request(legacy), Request(legacy.WithNumericParameter(pair.Key, pair.Value)), false, pair.Key);
        VerifyIdentity(Request(legacy), Request(legacy.WithNumericParameter("terrain.noiseScale", .015)), true,
            "unused simple noise");
        Console.WriteLine("CACHE exact height configuration, dimensions, seeds, topology and epochs PASS");
    }

    private static (ulong Hash, long Computes, long Hits) GenerateAndCommitProductionChunk(
        DeterministicChunkGenerator generator, ChunkGenerationProfileSnapshot profile,
        Int2 origin, ChunkGenerationTopologySnapshot topology)
    {
        using var world = new WorldRuntime("shared-height-test", 1);
        var address = new WorldAddress("surface", origin);
        var request = world.BeginChunkGeneration(address, 17413, profile, topology);
        var timing = new ChunkStreamingDiagnostics().BeginRequest(request);
        timing.StartGeneration();
        using var generated = generator.Generate(request, CancellationToken.None, timing);
        timing.FinishGeneration();
        Assert(world.TryCommit(generated, out _), "Production chunk failed to commit");
        Assert(world.TryGetChunk(address, out ChunkRuntime chunk), "Committed production chunk is missing");
        var counts = timing.Capture().GenerationCounts;
        Assert(counts.TryGetValue("river.macro.height_computes", out long computes) &&
               counts.TryGetValue("river.macro.height_shared_hits", out _), "Macro sampling diagnostics are missing");
        return (chunk.Terrain.ComputeStableHash(), computes, counts["river.macro.height_shared_hits"]);
    }

    private static void VerifyProductionChunkOutput()
    {
        using var json = JsonDocument.Parse(File.ReadAllText("Assets/Resources/Config/Probability/discrete-probability-tables.json"));
        var tables = new List<DiscreteProbabilityTableDefinition>();
        foreach (JsonElement table in json.RootElement.GetProperty("tables").EnumerateArray())
        {
            string id = table.GetProperty("id").GetString();
            if (!id.StartsWith("generation.", StringComparison.Ordinal)) continue;
            var entries = new List<DiscreteProbabilityEntryDefinition>();
            foreach (JsonElement entry in table.GetProperty("entries").EnumerateArray())
                entries.Add(new DiscreteProbabilityEntryDefinition { Value = entry.GetProperty("value").GetDouble(),
                    Percent = entry.GetProperty("percent").GetDouble() });
            tables.Add(new DiscreteProbabilityTableDefinition { Id = id, Entries = entries.ToArray() });
        }
        var catalog = new DiscreteProbabilityCatalog(tables);
        foreach (string algorithm in new[] { "simple", "legacy" })
        foreach (bool wrapped in new[] { false, true })
        {
            var initial = Profile(algorithm, 8, 8);
            var numbers = new Dictionary<string, double>(initial.NumericParameters, StringComparer.Ordinal);
            catalog.WriteGenerationParameters(numbers);
            var profile = new ChunkGenerationProfileSnapshot(initial.ProfileId, initial.Signature,
                initial.Width, initial.Height, numbers, new Dictionary<string, string>(initial.TextParameters))
                .WithNumericParameter("river.hydrologyRegionSize", 64)
                .WithNumericParameter("cave.portal.enabled", 0);
            var topology = wrapped ? new ChunkGenerationTopologySnapshot(new Int2(-128, -128), new Int2(256, 256)) : default;
            var generator = new DeterministicChunkGenerator();
            var fresh = new DeterministicChunkGenerator();
            try
            {
                var first = GenerateAndCommitProductionChunk(generator, profile, new Int2(16, 16), topology);
                var next = GenerateAndCommitProductionChunk(generator, profile, new Int2(80, 16), topology);
                var independent = GenerateAndCommitProductionChunk(fresh, profile, new Int2(80, 16), topology);
                Assert(first.Computes > 0 && first.Hits == 0 && next.Hits > 0 &&
                       next.Computes < independent.Computes && next.Hash == independent.Hash,
                    "Shared height changed committed terrain or did not reduce sampling");
                var resized = profile.WithChunkSize(9, 9, false);
                var changedGrid = profile.WithNumericParameter("climate.distance.chunkWidth", 32);
                var resizedResult = GenerateAndCommitProductionChunk(generator, resized, new Int2(16, 16), topology);
                var resizedReference = GenerateAndCommitProductionChunk(fresh, resized, new Int2(16, 16), topology);
                var gridResult = GenerateAndCommitProductionChunk(generator, changedGrid, new Int2(16, 16), topology);
                var gridReference = GenerateAndCommitProductionChunk(fresh, changedGrid, new Int2(16, 16), topology);
                Assert(resizedResult.Hits > 0 && resizedResult.Hash == resizedReference.Hash &&
                       gridResult.Hits > 0 && gridResult.Hash == gridReference.Hash,
                    "Resized output or climate grid lost height reuse or changed committed terrain");
                Console.WriteLine($"CHUNK {algorithm} wrapped={wrapped}: actual height computations {independent.Computes} -> {next.Computes}; hashes match");
            }
            finally { generator.BeginNewWorldHydrologyEpoch(1); fresh.BeginNewWorldHydrologyEpoch(1); }
        }
    }

    private static void VerifySharedSamplingLifecycle()
    {
        var profile = Profile("simple");
        var request = Request(profile);
        object cache = Create(Cache);
        object cancelled = NewSampling(request, cache);
        for (int i = 0; i < 512; i++) Height(cancelled, i, -17);
        using (var source = new CancellationTokenSource())
        {
            source.Cancel();
            try { Publish(cancelled, source.Token); throw new Exception("Cancelled publication succeeded"); }
            catch (TargetInvocationException e) when (e.InnerException is OperationCanceledException) { }
        }
        Assert(Number(Stats(cache), "samples") == 0, "Partially computed cancelled region published values");
        object retired = NewSampling(request, cache);
        for (int i = 0; i < 128; i++) Height(retired, i, 17);
        Invoke(cache, "RetireWorld", 1L);
        Publish(retired);
        Assert(Number(Stats(cache), "samples") == 0, "Retired region repopulated the current cache");
        object rebuilt = NewSampling(Request(profile, epoch: 2), cache);
        Height(rebuilt, 7, 17);
        Publish(rebuilt);
        Assert(Number(Stats(cache), "samples") == 1, "New epoch cannot publish samples");

        object limited = Create(Cache);
        object full = NewSampling(request, limited);
        int capacity = (int)Number(Stats(limited), "maximumSamplesPerPartition");
        for (int i = 0; i < capacity + 73; i++) Height(full, i, -99);
        Publish(full);
        Assert(Number(Stats(limited), "samples") == capacity, "Per-identity sample capacity is not enforced");
        CurrentTiming.SetValue(null, new ChunkStreamingDiagnostics().BeginRequest(request));
        try
        {
            object warmed = NewSampling(request, limited);
            Height(warmed, 0, -99);
            Height(warmed, capacity + 72, -99);
            Assert(Number(warmed, "HeightMissCount") == 1 && Number(warmed, "HeightSharedHitCount") == 1,
                "Capacity eviction did not discard the oldest sample and retain the latest sample");
        }
        finally { CurrentTiming.SetValue(null, null); }
        for (int seed = 0; seed < 8; seed++)
        {
            object other = NewSampling(Request(profile, seed: seed), limited);
            Height(other, 1, 2);
            Publish(other);
        }
        object limitedStats = Stats(limited);
        Assert(Number(limitedStats, "partitions") == Number(limitedStats, "maximumPartitions"),
            "Identity partitions are not bounded");
        Assert(Number(limitedStats, "samples") <= Number(limitedStats, "maximumPartitions") * capacity,
            "Total cache capacity exceeded");

        object concurrent = Create(Cache);
        Parallel.For(0, 8, worker =>
        {
            object sampling = NewSampling(request, concurrent);
            for (int i = 0; i < 1200; i++)
            {
                int x = i + worker * 200;
                double actual = Height(sampling, x, -23);
                double direct = (double)SampleHeight.Invoke(null, new object[] { request, profile.Settings, x, -23 });
                Assert(BitConverter.DoubleToInt64Bits(actual) == BitConverter.DoubleToInt64Bits(direct),
                    "Concurrent shared sample differs from production sampling");
            }
            Publish(sampling);
        });
        Assert(Number(Stats(concurrent), "samples") == 2600, "Concurrent publication lost distinct samples");
        Console.WriteLine($"CACHE cancellation, epoch, {capacity}-entry capacity, partition eviction and concurrent publication PASS");
    }

    #endregion

    private static void Main(string[] args)
    {
        object[] cases = CaptureMacroCases();
        File.WriteAllText(args[0], JsonSerializer.Serialize(cases, new JsonSerializerOptions { WriteIndented = true }));
        if (Cache != null)
        {
            VerifySharedSamplingIdentities();
            VerifySharedSamplingLifecycle();
            VerifyProductionChunkOutput();
        }
        Console.WriteLine("Saved exact production macro region signatures: " + args[0]);
    }
}
