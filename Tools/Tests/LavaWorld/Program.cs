using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using FlatWorld.WorldModel;

internal static class Program
{
    private static readonly MethodInfo ConsumeTerrain = typeof(ChunkGenerationResult).GetMethod("ConsumeTerrain", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo EcologyField = typeof(ChunkGenerationResult).GetField("ecology", BindingFlags.NonPublic | BindingFlags.Instance);
    private static int assertions;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }

    private static EcologySpawnRuleSnapshot Rule(string item, string layer = "lava.shore") => new(
        "test." + item, item, 1, 1d, 1d, 1 << (int)SurfaceBiomeKind.Stone,
        0d, 1d, 0d, 1d, 0d, 1d, requiredEnvironmentLayer: layer, minimumEnvironmentValue: 0.5d);

    private static ChunkGenerationProfileSnapshot Profile(int size, bool lava) => new("test.lava", 55, size, size,
        new Dictionary<string, double>
        {
            ["terrain.seaLevel"] = 0, ["terrain.mountainLevel"] = 0,
            ["terrain.snowTemperature"] = 0, ["terrain.snowMinimumPrecipitation"] = 1,
            ["terrain.stoneTileId"] = 3, ["river.enabled"] = 0,
            ["lake.large.enabled"] = 0, ["structure.enabled"] = 0,
            ["lake.lava.enabled"] = lava ? 1 : 0, ["lake.lava.regionSize"] = 64,
            ["lake.lava.chance"] = 1, ["lake.lava.minimumHeight"] = 0,
            ["lake.lava.minRadius"] = 3, ["lake.lava.maxRadius"] = 3,
            ["lake.lava.shoreWidth"] = 2
        }, new Dictionary<string, string> { ["climate.algorithm"] = "simple" },
        ecologyRules: new[] { Rule("Obsidian"), Rule("Mine_Obsidian") });

    private static Dictionary<(int, int), (int Liquid, float Depth)> Sample(int size, bool enabled,
        int origin, ChunkGenerationTopologySnapshot topology, out int lavaCount, out int resources)
    {
        var generator = new DeterministicChunkGenerator();
        var profile = Profile(size, enabled);
        var resultMap = new Dictionary<(int, int), (int, float)>();
        lavaCount = resources = 0;
        for (int y = origin; y < origin + 128; y += size)
        for (int x = origin; x < origin + 128; x += size)
        {
            var request = new ChunkGenerationRequest(1, new WorldAddress("surface", new Int2(x, y)), 1729, 1, profile, topology);
            using var result = generator.Generate(request, CancellationToken.None);
            using var terrain = (ChunkTerrainData)ConsumeTerrain.Invoke(result, null);
            var ecology = (ChunkEcologyData)EcologyField.GetValue(result);
            for (int ly = 0; ly < size; ly++)
            for (int lx = 0; lx < size; lx++)
            {
                int liquid = terrain.GetLiquidTypeIndex(lx, ly);
                float depth = terrain.GetLiquidDepth(lx, ly);
                resultMap[(topology.NormalizeX(x + lx), topology.NormalizeY(y + ly))] = (liquid, depth);
                if (depth <= 0f) continue;
                lavaCount++;
                Check(liquid == LiquidTypeCatalog.BuiltIn.GetIndex(LiquidTypeCatalog.LavaId), "Wrong generated liquid identity");
                Check(terrain.GetCell(lx, ly).GroundTileId == 3, "Lava must retain stone ground");
                Check(depth > 0f && depth <= 1f, "Invalid liquid depth");
            }
            foreach (var placement in ecology.Placements)
            {
                if (placement.ItemId != "Obsidian" && placement.ItemId != "Mine_Obsidian") continue;
                resources++;
                Check(terrain.GetLiquidDepth(placement.LocalX, placement.LocalY) == 0f, "Resource generated inside lava");
                Check(terrain.TryGetEnvironmentValue("lava.shore", placement.LocalX, placement.LocalY, out float shore) &&
                    shore >= 0.5f, "Obsidian generated away from lava shore");
            }
        }
        return resultMap;
    }

    private static void Compare(Dictionary<(int, int), (int, float)> left, Dictionary<(int, int), (int, float)> right)
    {
        Check(left.Count == right.Count, "Different cell counts");
        foreach (var cell in left)
            Check(right.TryGetValue(cell.Key, out var value) && value.Equals(cell.Value), "Chunk partition/wrapped seam changed lava");
    }

    private static void Main()
    {
        var small = Sample(16, true, -64, default, out int lava, out int resources);
        Check(lava > 0 && resources > 0, "Lava lake or shoreline resources never generated");
        Compare(small, Sample(64, true, -64, default, out _, out _));
        Compare(small, Sample(16, true, -64, default, out _, out _));
        Sample(32, false, -64, default, out int disabledLava, out int disabledResources);
        Check(disabledLava == 0 && disabledResources == 0, "Disabled lava generated content");
        var topology = new ChunkGenerationTopologySnapshot(new Int2(0, 0), new Int2(128, 128));
        Compare(Sample(16, true, 0, topology, out _, out _), Sample(16, true, 128, topology, out _, out _));
        var first = new ChunkGenerationProfileSnapshot("test", 55, 16, 16, ecologyRules: new[] { Rule("Obsidian") });
        var second = new ChunkGenerationProfileSnapshot("test", 55, 16, 16, ecologyRules: new[] { Rule("Obsidian", "other.shore") });
        Check(first.EcologyFingerprint != second.EcologyFingerprint, "Environment constraint missing from fingerprint");
        Console.WriteLine($"PASS {assertions} checks: lava={lava}, shorelineResources={resources}; deterministic chunks, negative coordinates, wrapped seams, disabled profile and rule fingerprint.");
    }
}
