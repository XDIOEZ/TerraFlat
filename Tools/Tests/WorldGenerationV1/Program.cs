using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using FlatWorld.WorldModel;

internal static class Program
{
    private static void Main(string[] args)
    {
        var values = new Dictionary<string, string>();
        foreach (string mode in new[] { "surface", "cave" })
        foreach (bool wrapped in new[] { false, true })
            values[mode + (wrapped ? "-wrapped" : "-infinite")] = GenerateGoldenSignature(mode, wrapped);
        File.WriteAllText(args[0], JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));

        var profile = new ChunkGenerationProfileSnapshot("version-check", 81, 8, 8);
        if (profile.GenerationAlgorithmVersion != 1 ||
            profile.WithNumericParameter("terrain.noiseScale", .02).GenerationAlgorithmVersion != 1 ||
            profile.WithChunkSize(16, 16).GenerationAlgorithmVersion != 1 ||
            profile.WithEcology(1, Array.Empty<EcologySpawnRuleSnapshot>()).GenerationAlgorithmVersion != 1)
            throw new Exception("generation algorithm version was lost while deriving the profile");
        try
        {
            profile.WithNumericParameter(WorldGenerationAlgorithmVersion.ParameterId, 2);
            throw new Exception("unknown generation version was accepted");
        }
        catch (NotSupportedException) { }
        Console.WriteLine("PASS: default version, derived profiles, unknown version rejection");
    }

    private static string GenerateGoldenSignature(string mode, bool wrapped)
    {
        var numbers = new Dictionary<string, double>
        {
            ["terrain.seaLevel"] = 0, ["river.runoffCellSize"] = 16, ["river.maxTraceSteps"] = 48,
            ["river.maxWidth"] = 3, ["river.floodplainMaxRadius"] = 2, ["river.startFlow"] = .0001,
            ["river.tributaryStartFlow"] = .0001, ["climate.distance.minimumRainfall"] = 1,
            ["climate.distance.maximumRainfall"] = 1, ["climate.distance.freshwaterRadiusChunks"] = 1,
            ["airHumidity.radiusTiles"] = 1, ["biome.peat.stoneBoundaryRadius"] = 1,
            ["lake.large.enabled"] = 0, ["lake.lava.enabled"] = 0, ["structure.enabled"] = 0,
            ["river.hydrologyRegionSize"] = 64, ["cave.portal.enabled"] = 0
        };
        using var json = JsonDocument.Parse(File.ReadAllText("Assets/Resources/Config/Probability/discrete-probability-tables.json"));
        var tables = new List<DiscreteProbabilityTableDefinition>();
        foreach (var table in json.RootElement.GetProperty("tables").EnumerateArray())
        {
            string id = table.GetProperty("id").GetString();
            if (!id.StartsWith("generation.", StringComparison.Ordinal)) continue;
            var entries = new List<DiscreteProbabilityEntryDefinition>();
            foreach (var entry in table.GetProperty("entries").EnumerateArray())
                entries.Add(new DiscreteProbabilityEntryDefinition { Value = entry.GetProperty("value").GetDouble(), Percent = entry.GetProperty("percent").GetDouble() });
            tables.Add(new DiscreteProbabilityTableDefinition { Id = id, Entries = entries.ToArray() });
        }
        new DiscreteProbabilityCatalog(tables).WriteGenerationParameters(numbers);
        var rule = new EcologySpawnRuleSnapshot("golden-tree", "core:tree", 1, .3, 1, -1, 0, 1, 0, 1, 0, 1);
        var profile = new ChunkGenerationProfileSnapshot("golden-" + mode, 81, 8, 8, numbers,
            new Dictionary<string, string> { ["terrain.mode"] = mode, ["climate.algorithm"] = "simple" },
            ecologyRules: new[] { rule });
        var topology = wrapped ? new ChunkGenerationTopologySnapshot(new Int2(-128, -128), new Int2(256, 256)) : default;
        using var world = new WorldRuntime("v1-golden", 1);
        var generator = new DeterministicChunkGenerator();
        try
        {
            using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes);
            foreach (var origin in new[] { new Int2(-16, 8), new Int2(16, 16), new Int2(80, -24) })
            {
                var address = new WorldAddress(mode, origin);
                var request = world.BeginChunkGeneration(address, 17413, profile, topology);
                using var generated = generator.Generate(request, CancellationToken.None);
                if (!world.TryCommit(generated, out _) || !world.TryGetChunk(address, out ChunkRuntime chunk))
                    throw new Exception("could not commit golden chunk");
                writer.Write(chunk.Terrain.ComputeStableHash());
                for (int y = 0; y < chunk.Terrain.Height; y++)
                    for (int x = 0; x < chunk.Terrain.Width; x++)
                    {
                        chunk.Terrain.TryGetEnvironmentValue("height", x, y, out float height);
                        writer.Write(BitConverter.SingleToInt32Bits(height));
                    }
                writer.Write(chunk.Ecology.Count);
                foreach (var placement in chunk.Ecology.Placements)
                {
                    writer.Write(placement.Guid); writer.Write(placement.ItemId ?? "");
                    writer.Write(placement.LocalX); writer.Write(placement.LocalY);
                    writer.Write(placement.OffsetX); writer.Write(placement.OffsetY); writer.Write(placement.RuleId ?? "");
                }
            }
            writer.Flush();
            return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
        }
        finally { generator.BeginNewWorldHydrologyEpoch(1); }
    }
}
