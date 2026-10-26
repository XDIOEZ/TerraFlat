using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FlatWorld.WorldModel;

internal static class Program
{
    #region 海洋陆架与正式生成验证

    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    private static void Main()
    {
        ValidateHundredUnitWaterVolumesAndVisualNotifications();
        using (var straight = new OceanShoreDistanceBathymetryKernel(-1, -3, 40, 7, .5, 16, 4,
                   (x, y) => x <= 0 ? .75 : .25))
        {
            int[] expected = { 0, 1, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 5, 7, 9, 10 };
            for (int distance = 0; distance <= 20; distance++)
            {
                Check(straight.GetShoreDistance(distance + 1, 3) == distance, "straight shore distance");
                int level = LiquidCellValue.FromGeneratedDepth(straight.GetInitialLiquidDepth(distance + 1, 3));
                Check(level == expected[distance] * 10, "shelf / slope boundary at " + distance);
                Check(OceanShoreDistanceBathymetryKernel.ResolveNaturalOceanBedDepthLevels(level) ==
                      (level > 0 ? expected[distance] + 1 : 0), "ocean bed keeps original ten visual levels");
            }
        }
        // 独立暴力距离只作测试判据，覆盖斜岸、零散岛屿、负坐标与分块边缘。
        var islands = new[] { new Int2(-7, 2), new Int2(16, -9), new Int2(28, 23) };
        double Height(int x, int y) => islands.Any(p => p.X == x && p.Y == y) ? .75 : .25;
        using (var field = new OceanShoreDistanceBathymetryKernel(-20, -20, 64, 64, .5, 16, 4, Height))
        {
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                double exact = Math.Min(20, islands.Min(p => Math.Sqrt(
                    (double)(x - 20 - p.X) * (x - 20 - p.X) + (double)(y - 20 - p.Y) * (y - 20 - p.Y))));
                Check(Math.Abs(field.GetShoreDistance(x, y) - exact) < .000002, "Euclidean island distance");
                if ((x + y) % 23 != 0) continue;
                using var single = new OceanShoreDistanceBathymetryKernel(x - 20, y - 20, 1, 1, .5, 16, 4, Height);
                Check(single.GetInitialLiquidDepth(0, 0) == field.GetInitialLiquidDepth(x, y), "single / chunk agreement");
            }
        }
        bool cancelled = false;
        try
        {
            using var ignored = new OceanShoreDistanceBathymetryKernel(0, 0, 1, 1, .5, 16, 4, Height,
                new CancellationToken(true));
        }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "cancelled windows release pooled buffers");

        foreach (bool wrapped in new[] { false, true })
        foreach (string algorithm in new[] { "simple", "legacy" })
        {
            var full = Generate(32, new Int2(-16, -16), wrapped, algorithm);
            var left = Generate(16, new Int2(-16, -16), wrapped, algorithm);
            var right = Generate(16, new Int2(0, -16), wrapped, algorithm);
            foreach (var pair in left.Concat(right))
                Check(full[pair.Key] == pair.Value, "production chunk-size / wrap agreement " + algorithm);
            Check(full.Values.Any(v => v.level > 0) && full.Values.Any(v => v.level == 0), "mixed shoreline fixture");
            Console.WriteLine("PASS production shoreline, bed, chunk boundaries and humidity: " + algorithm + "/wrapped=" + wrapped);
        }
        Generate(8, new Int2(0, 0), false, "legacy", frozen: true, allOcean: true);
        Console.WriteLine("PASS frozen deep ocean retains bed and removes liquid");
        Console.WriteLine("PASS " + checks + " bathymetry checks");
    }

    private static Dictionary<Int2, (int level, int bed, float humidity)> Generate(int width, Int2 origin,
        bool wrapped, string algorithm, bool frozen = false, bool allOcean = false)
    {
        var numbers = new Dictionary<string, double>
        {
            ["terrain.seaLevel"] = allOcean ? 1 : .5,
            ["terrain.beachLevel"] = .51,
            ["terrain.noiseScale"] = .04, ["terrain.scale"] = .04,
            ["terrain.height.frequency"] = 2,
            ["terrain.height.secondaryBoostEnabled"] = allOcean ? 0 : 1,
            ["river.enabled"] = 0, ["lake.large.enabled"] = 0, ["lake.lava.enabled"] = 0,
            ["grass.density"] = 0, ["climate.polarBand.enabled"] = 0,
            ["climate.temperature.celsiusMin"] = frozen ? -20 : 20,
            ["climate.temperature.celsiusMax"] = frozen ? -20 : 20,
            ["climate.temperature.altitudeCoolingStrength"] = 0,
            ["climate.distance.oceanRadiusChunks"] = 0,
            ["climate.distance.freshwaterRadiusChunks"] = 0, ["airHumidity.radiusTiles"] = 2,
            ["climate.distance.minimumRainfall"] = .5,
            ["climate.distance.maximumRainfall"] = .5,
            ["climate.distance.chunkWidth"] = 16, ["climate.distance.chunkHeight"] = 16
        };
        var profile = new ChunkGenerationProfileSnapshot("bathymetry.validation", 80, width, 16, numbers,
            new Dictionary<string, string> { ["climate.algorithm"] = algorithm });
        Check(profile.Settings.OceanShelfWidthTiles == 16 && profile.Settings.OceanSlopeWidthTiles == 4, "default distances");
        var topology = wrapped ? new ChunkGenerationTopologySnapshot(new Int2(-32, -32), new Int2(64, 64)) : default;
        using var world = new WorldRuntime("bathymetry.validation", 1);
        var generator = new DeterministicChunkGenerator();
        var address = new WorldAddress("surface", origin);
        try
        {
            using var result = generator.Generate(world.BeginChunkGeneration(address, 17413, profile, topology), CancellationToken.None);
            Check(world.TryCommit(result, out _), "commit production terrain");
            world.TryGetChunkTerrain(address, out var terrain);
            var samples = new Dictionary<Int2, (int, int, float)>();
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                int level = terrain.GetLiquidLevel(x, y);
                int bed = TerrainSurfaceHeightLayer.GetNaturalDepthLevels(terrain, x, y);
                terrain.TryGetEnvironmentValue("height", x, y, out float height);
                terrain.TryGetEnvironmentValue("airHumidity", x, y, out float humidity);
                bool ocean = height < profile.Settings.SeaLevel;
                Check(ocean ? frozen ? level == 0 && bed >= 2 : level > 0 && bed == LiquidCellValue.ToVisualLevel(level) + 1 : level == 0 && bed == 0,
                    "production water / bed / freeze agreement");
                if (allOcean) Check(bed == 11, "all-ocean floor reaches full depth");
                samples.Add(new Int2(origin.X + x, origin.Y + y), (level, bed, humidity));
            }
            return samples;
        }
        finally { generator.BeginNewWorldHydrologyEpoch(1); }
    }

    private static void ValidateHundredUnitWaterVolumesAndVisualNotifications()
    {
        Check(LiquidCellValue.MaximumLevel == 100 && LiquidCellValue.LitersPerUnit == 10,
            "one full cell stores 100 units / 1000 liters");
        Check(LiquidCellValue.NormalizeLevel(-1) == 0 && LiquidCellValue.NormalizeLevel(101) == 100,
            "water units clamp at zero and one hundred");
        int[] amounts = { 0, 1, 9, 10, 11, 90, 91, 99, 100 };
        int[] visuals = { 0, 1, 1, 1, 2, 9, 10, 10, 10 };
        for (int i = 0; i < amounts.Length; i++)
        {
            Check(LiquidCellValue.ToVisualLevel(amounts[i]) == visuals[i], "positive tail rounds water surface upward");
            Check(Math.Abs(LiquidCellValue.ToNormalizedDepth(amounts[i]) - amounts[i] / 100f) < .000001f,
                "real water depth retains hundredth precision");
            Check(Math.Abs(LiquidCellValue.ToVisualDepth(amounts[i]) - visuals[i] / 10f) < .000001f,
                "visual water depth retains only ten states");
        }
        Check(LiquidCellValue.FromGeneratedDepth(.000001) == 10 && LiquidCellValue.FromGeneratedDepth(.21) == 30 &&
            LiquidCellValue.FromGeneratedDepth(.6f) == 60, "generation multiplies original levels by ten");
        Check(WorldLiquidVolumeConversion.ReferenceServingLiters == .25f &&
            WorldLiquidVolumeConversion.WorldUnitsToLiquidServings(1, .25f) == 40,
            "one world unit converts to forty drinking servings without changing drinking size");
        Check(WorldLiquidVolumeConversion.WorldUnitsToLiquidServings(1, .5f) == 20,
            "world conversion reads each liquid serving volume");
        Check(WorldLiquidVolumeConversion.LiquidServingsToWholeWorldUnits(39.999f, .25f) == 0 &&
            WorldLiquidVolumeConversion.LiquidServingsToWholeWorldUnits(40, .25f) == 1,
            "less than ten liters never consumes a world unit");
        Check(WorldLiquidVolumeConversion.QuantizeWholeWorldLiquidServings(80, 79, .25f) == 40,
            "partial world batches preserve every sub-ten-liter tail");
        Check(WorldLiquidVolumeConversion.GetContainerCapacityLiters(200) == 50 &&
            WorldLiquidVolumeConversion.GetContainerCapacityLiters(80) == 20,
            "bucket and coconut container capacities use liters");
        Check(WorldLiquidVolumeConversion.LiquidServingsToWholeWorldUnits(float.NaN, .25f) == 0 &&
            WorldLiquidVolumeConversion.LiquidServingsToWholeWorldUnits(40, 0) == 0,
            "invalid volume conversion cannot manufacture water");
        foreach (float servingLiters in new[] { .3f, .7f, 1.3f })
        {
            for (int units = 1; units <= 100; units++)
            {
                float servings = WorldLiquidVolumeConversion.WorldUnitsToLiquidServings(units, servingLiters);
                Check(WorldLiquidVolumeConversion.LiquidServingsToWholeWorldUnits(servings, servingLiters) == units,
                    "fractional serving volumes round-trip every world unit without losing a ten-liter batch");
                Check(WorldLiquidVolumeConversion.LiquidServingsToWholeWorldUnits(MathF.BitDecrement(servings), servingLiters) < units,
                    "one ULP below a complete fractional batch is rejected without epsilon manufacturing water");
            }
            float belowBatch = WorldLiquidVolumeConversion.WorldUnitsToLiquidServings(1, servingLiters) - .001f;
            Check(WorldLiquidVolumeConversion.LiquidServingsToWholeWorldUnits(belowBatch, servingLiters) == 0,
                "fractional serving volumes still reject an actually incomplete ten-liter batch");
        }
        using var buffer = new ChunkTerrainBuffer(3, 1);
        int water = buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        buffer.SetLiquid(0, 0, water, 100); buffer.SetLiquid(1, 0, water, 20); buffer.SetLiquid(2, 0, water, 1);
        using ChunkTerrainData terrain = buffer.Seal();
        var flags = new List<bool>();
        terrain.Changed += change => { if (change.Kind == TerrainChangeKind.Liquid) flags.Add(change.AffectsLiquidVisuals); };
        terrain.SetLiquid(0, 0, water, 99); terrain.SetLiquid(0, 0, water, 90);
        terrain.SetLiquid(0, 0, water, 89); terrain.SetLiquid(0, 0, water, 0);
        Check(flags.SequenceEqual(new[] { false, true, false, true }), "single-cell notifications distinguish water units and visual levels");
        terrain.SetLiquid(0, 0, water, 100);
        int changes = 0; int[] changed = null, visible = null;
        terrain.LiquidBatchChanged += change => { changes++; changed = change.CellIndices.ToArray(); visible = change.VisualCellIndices.ToArray(); };
        terrain.SetLiquidBatch(new[] { new LiquidCellUpdate(0, water, 99), new LiquidCellUpdate(1, water, 19), new LiquidCellUpdate(2, 0, 0) });
        Check(changes == 1 && changed.SequenceEqual(new[] { 0, 1, 2 }) && visible.SequenceEqual(new[] { 2 }),
            "batch same-level subtraction preserves all data and refreshes only dry transition");
        terrain.SetLiquidBatch(new[] { new LiquidCellUpdate(0, water, 90), new LiquidCellUpdate(1, water, 10), new LiquidCellUpdate(2, water, 1) });
        Check(changes == 2 && visible.SequenceEqual(new[] { 0, 1, 2 }), "batch crossings and wet transition refresh every affected surface");
    }

    #endregion
}
