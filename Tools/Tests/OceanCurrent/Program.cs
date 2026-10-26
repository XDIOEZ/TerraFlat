using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using FlatWorld.WorldModel;
using UnityEngine;

internal static class Program
{
    #region 海岸洋流生产算法离线回归

    private static int checks;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    private static void Main(string[] args)
    {
        if (args.Contains("--benchmark"))
        {
            BenchmarkWholeGenerationWithRealHeightSampling();
            return;
        }
        ValidateBandStrengthAndDirection();
        ValidateShoreGeometryAndSharedWindows();
        ValidateSymmetricCoastsAndDeepSeaContinuity();
        ValidateRuntimeStrengthAndEstuary();
        ValidateGenerationFieldAgreement();
        Console.WriteLine($"PASS {checks} ocean current checks");
    }

    private static void ValidateBandStrengthAndDirection()
    {
        var defaults = new ChunkGenerationProfileSnapshot("ocean.defaults", 1, 16, 16, new Dictionary<string, double>());
        var scaledDefaults = new ChunkGenerationProfileSnapshot("ocean.defaults.scaled", 1, 16, 16,
            new Dictionary<string, double> { ["world.spatialDistanceScale"] = 4 });
        Check(defaults.Settings.OceanCurrentSearchDistanceTiles == 32 &&
              defaults.Settings.OceanCurrentTransitionWidthTiles == 3, "shipping default search and transition");
        Check(scaledDefaults.Settings.OceanCurrentSearchDistanceTiles == 128 &&
              scaledDefaults.Settings.OceanCurrentTransitionWidthTiles == 12, "shipping distances follow spatial scale");
        double previous = double.NaN;
        for (int i = 1; i <= 3200; i++)
        {
            double distance = i / 100d;
            OceanShoreCurrentKernel.ResolveCurrentVector(1, 0, distance, 16, 3, 64,
                0, 1, out double x, out double y);
            double signed = OceanShoreCurrentKernel.ResolveSignedCurrentStrength(distance, 16, 3);
            Check(x * x + y * y <= 1.00000001, "bounded strength at " + distance);
            Check(distance < 16 ? x < 0 : distance > 16 ? x > 0 : x == 0 && y == 0,
                "nearshore / offshore sign at " + distance);
            Check(signed >= -.45000001 && signed <= 1.00000001, "signed strength bound");
            if (distance >= 14.5 && distance <= 17.5 && !double.IsNaN(previous))
                Check(Math.Abs(x - previous) < .03, "smooth three-tile reversal");
            previous = x;
        }
        foreach (double scale in new[] { .25, 1d, 4d })
        foreach (double distance in new[] { 4d, 15.5, 16d, 16.5, 24d })
        {
            double scaled = OceanShoreCurrentKernel.ResolveSignedCurrentStrength(distance * scale, 16 * scale, 3 * scale);
            double ordinary = OceanShoreCurrentKernel.ResolveSignedCurrentStrength(distance, 16, 3);
            Check(Math.Abs(scaled - ordinary) < 1e-12, "spatial scale preserves gameplay bands");
        }
        Console.WriteLine("PASS inward / outward signs, weak reversal, bounds and spatial scale");
    }

    private static void BenchmarkWholeGenerationWithRealHeightSampling()
    {
        string profileText = File.ReadAllText("Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset");
        var baseline = new Dictionary<string, double>();
        foreach (Match match in Regex.Matches(profileText, @"- Id: ([^\r\n]+)\r?\n    Value: ([^\r\n]+)"))
            if (double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                baseline[match.Groups[1].Value] = value;
        Console.WriteLine("BENCH controlled shoreline generation: actual Surface numeric profile and humidity radius=" + baseline["airHumidity.radiusTiles"]);
        Console.WriteLine("BENCH rivers/lakes/lava/grass and long-range precipitation disabled to isolate coast-query overhead; actual terrain noise retained.");
        foreach (double scale in new[] { 1d, 4d })
        foreach (int reach in new[] { 20, 32, 48, 64 })
        {
            var samples = new List<double>();
            int waterCellCount = 0;
            for (int run = 0; run < 4; run++)
            {
                var numbers = new Dictionary<string, double>(baseline)
                {
                    ["world.spatialDistanceScale"] = scale,
                    ["terrain.ocean.current.searchDistanceTiles"] = reach,
                    ["river.enabled"] = 0, ["lake.large.enabled"] = 0, ["lake.lava.enabled"] = 0,
                    ["grass.density"] = 0, ["climate.distance.oceanRadiusChunks"] = 0,
                    ["climate.distance.freshwaterRadiusChunks"] = 0
                };
                var profile = new ChunkGenerationProfileSnapshot("ocean.benchmark", 80, 16, 16, numbers,
                    new Dictionary<string, string> { ["climate.algorithm"] = "legacy" });
                using var world = new WorldRuntime("ocean.benchmark", 1);
                var generator = new DeterministicChunkGenerator();
                try
                {
                    var request = world.BeginChunkGeneration(new WorldAddress("surface", new Int2(-16, -16)), 17413, profile, default);
                    var timer = Stopwatch.StartNew();
                    using var result = generator.Generate(request, CancellationToken.None);
                    double elapsed = timer.Elapsed.TotalMilliseconds;
                    Check(world.TryCommit(result, out _), "commit benchmark terrain");
                    world.TryGetChunkTerrain(request.Address, out var terrain);
                    waterCellCount = 0;
                    for (int y = 0; y < terrain.Height; y++)
                    for (int x = 0; x < terrain.Width; x++)
                        if (terrain.GetLiquidLevel(x, y) > 0) waterCellCount++;
                    Check(waterCellCount > 0, "benchmark exercises actual ocean generation");
                    if (run > 0) samples.Add(elapsed);
                }
                finally { generator.BeginNewWorldHydrologyEpoch(1); }
            }
            samples.Sort();
            Console.WriteLine($"BENCH scale={scale} reach={reach}: median={samples[1]:F2}ms range={samples[0]:F2}..{samples[2]:F2}ms (16x16 full generator, three warm runs, {waterCellCount} ocean cells)");
        }
    }

    private static void ValidateShoreGeometryAndSharedWindows()
    {
        foreach (string scenario in new[] { "straight", "diagonal", "island", "wrapped", "ocean", "land" })
        {
            double Height(int x, int y)
            {
                if (scenario == "ocean") return .25;
                if (scenario == "land") return .75;
                if (scenario == "island") return x * x + y * y <= 4 ? .75 : .25;
                if (scenario == "diagonal") return x + y <= 0 ? .75 : .25;
                if (scenario == "wrapped") x = (x % 128 + 128) % 128;
                return x <= 0 ? .75 : .25;
            }
            using var whole = new OceanShoreDistanceBathymetryKernel(0, -8, 65, 17, .5, 16, 4, Height,
                currentSearchDistance: 64, currentTransitionWidth: 3);
            foreach (int x in new[] { 4, 8, 15, 16, 17, 24, 48, 63, 64 })
            {
                var current = whole.GetOceanCurrent(x, 8, 0, 1);
                whole.GetOutwardShoreDirection(x, 8, out double normalX, out double normalY);
                using var single = new OceanShoreDistanceBathymetryKernel(x, 0, 1, 1, .5, 16, 4, Height,
                    currentSearchDistance: 64, currentTransitionWidth: 3);
                var individual = single.GetOceanCurrent(0, 0, 0, 1);
                Check(Math.Abs(current.X - individual.X) < 1e-12 && Math.Abs(current.Y - individual.Y) < 1e-12,
                    "shared / individual window " + scenario + ":" + x);
                Check(current.X * current.X + current.Y * current.Y <= 1.00000001, "geometry strength bound");
                if (scenario == "straight")
                {
                    Check(Math.Abs(whole.GetCurrentShoreDistance(x, 8) - x) < .00001, "current coast search reaches 64 tiles");
                    Check(whole.GetShoreDistance(x, 8) == Math.Min(x, 20), "bathymetry distance remains unchanged");
                    if (x < 48) Check(normalX > .99 && Math.Abs(normalY) < .000001, "straight coast normal");
                }
                if (scenario == "diagonal" && x < 48)
                    Check(normalX > .5 && normalY > .5, "diagonal coast normal");
                if (scenario == "island" && x < 48) Check(normalX > .9, "island coast normal");
                if (scenario == "land") Check(current.X == 0 && current.Y == 0, "land has no ocean current");
                if (scenario == "ocean") Check(Math.Abs(current.X) < 1e-12 && current.Y > .999, "open ocean background");
            }
        }
        double WrappedIsland(int x, int y)
        {
            int dx = ((x - 63 + 64) % 128 + 128) % 128 - 64;
            return dx * dx + y * y <= 4 ? .75 : .25;
        }
        using var seamA = new OceanShoreDistanceBathymetryKernel(60, -4, 16, 8, .5, 16, 4, WrappedIsland,
            currentSearchDistance: 64, currentTransitionWidth: 3);
        using var seamB = new OceanShoreDistanceBathymetryKernel(-68, -4, 16, 8, .5, 16, 4, WrappedIsland,
            currentSearchDistance: 64, currentTransitionWidth: 3);
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 16; x++)
        {
            var a = seamA.GetOceanCurrent(x, y, 0, 1);
            var b = seamB.GetOceanCurrent(x, y, 0, 1);
            Check(a.X == b.X && a.Y == b.Y, "wrapped shore direction seam");
        }
        foreach (double scale in new[] { .25, 1d, 4d })
        {
            int batchCalls = 0, singleCalls = 0;
            double BatchHeight(int x, int y) { batchCalls++; return x <= 0 ? .75 : .25; }
            double SingleHeight(int x, int y) { singleCalls++; return x <= 0 ? .75 : .25; }
            var watch = Stopwatch.StartNew();
            using var batch = new OceanShoreDistanceBathymetryKernel(1, 0, 32, 32, .5,
                16 * scale, 4 * scale, BatchHeight, currentSearchDistance: 64 * scale, currentTransitionWidth: 3 * scale);
            for (int x = 0; x < 8; x++)
            {
                using var single = new OceanShoreDistanceBathymetryKernel(x + 1, 0, 1, 1, .5,
                    16 * scale, 4 * scale, SingleHeight, currentSearchDistance: 64 * scale, currentTransitionWidth: 3 * scale);
                var a = batch.GetOceanCurrent(x, 0, 0, 1);
                var b = single.GetOceanCurrent(0, 0, 0, 1);
                Check(a.X == b.X && a.Y == b.Y, "scaled shared kernel");
            }
            Check(batchCalls < singleCalls, "batch amortizes coast height sampling");
            Console.WriteLine($"PASS search scale={scale}: 32x32={batchCalls} height samples; eight single={singleCalls}; elapsed={watch.ElapsedMilliseconds}ms");
        }
        bool cancelled = false;
        try
        {
            using var ignored = new OceanShoreDistanceBathymetryKernel(0, 0, 1, 1, .5, 16, 4,
                (x, y) => .25, new CancellationToken(true), 64, 3);
        }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "cancelled current window releases buffers");
        Console.WriteLine("PASS curved / island / wrapped coast, shared windows, background and cancellation");
    }

    private static void ValidateRuntimeStrengthAndEstuary()
    {
        using var buffer = new ChunkTerrainBuffer(1, 1);
        using ChunkTerrainData terrain = buffer.Seal();
        terrain.SetEnvironmentValue("windX", 0, 0, 1);
        terrain.SetEnvironmentValue("windY", 0, 0, 1);
        terrain.SetEnvironmentValue(OceanShoreCurrentKernel.DirectionXLayer, 0, 0, 0);
        terrain.SetEnvironmentValue(OceanShoreCurrentKernel.DirectionYLayer, 0, 0, 0);
        Check(WaterEnvironmentRules.ResolveOceanCurrentVector(terrain, 0, 0) == Vector2.zero,
            "zero shore current never falls back to wind");
        foreach (float strength in new[] { 0f, .1f, .45f, 1f })
        {
            terrain.SetEnvironmentValue(OceanShoreCurrentKernel.DirectionXLayer, 0, 0, -strength);
            Vector2 current = WaterEnvironmentRules.ResolveOceanCurrentVector(terrain, 0, 0);
            Check(Math.Abs(current.x + strength) < 1e-6 && current.y == 0, "runtime preserves inward strength");
            Check(Math.Abs(WorldItemWaterRules.ResolveDriftSpeed(RuntimeWaterCurrentKind.Ocean, strength) -
                           WorldItemWaterRules.OceanDriftSpeed * strength) < 1e-6, "loose item preserves ocean strength");
        }
        Check(WorldItemWaterRules.ResolveDriftSpeed(RuntimeWaterCurrentKind.Ocean, 99) == WorldItemWaterRules.OceanDriftSpeed,
            "loose item current upper bound");
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.DirectionXLayer, 0, 0, 1);
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.DirectionYLayer, 0, 0, 0);
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.InfluenceLayer, 0, 0, .2f);
        foreach (float strength in new[] { -.0001f, 0, .0001f })
        {
            terrain.SetEnvironmentValue(OceanShoreCurrentKernel.DirectionXLayer, 0, 0, strength);
            Vector2 current = WaterEnvironmentRules.ResolveOceanCurrentVector(terrain, 0, 0);
            Check(Math.Abs(current.x - .2f) < .001 && Math.Abs(current.y) < .001,
                "river plume stays continuous across weak ocean reversal");
        }
        terrain.SetEnvironmentValue(OceanShoreCurrentKernel.DirectionXLayer, 0, 0, -.45f);
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.InfluenceLayer, 0, 0, 1);
        Check(WaterEnvironmentRules.ResolveOceanCurrentVector(terrain, 0, 0).x > .999f,
            "mouth core overrides inward coast flow");
        terrain.SetEnvironmentValue(EstuaryCurrentKernel.InfluenceLayer, 0, 0, 0);
        Check(Math.Abs(WaterEnvironmentRules.ResolveOceanCurrentVector(terrain, 0, 0).x + .45f) < 1e-6,
            "outside mouth retains inward coast flow");
        Check(WaterEnvironmentRules.ResolveCurrentKind(1, true) == RuntimeWaterCurrentKind.River &&
              WaterEnvironmentRules.ResolveCurrentKind(2, true) == RuntimeWaterCurrentKind.None,
            "river / lake classification preserved");
        Console.WriteLine("PASS production runtime vector, drift strength and river-mouth continuity");
    }

    private static void ValidateSymmetricCoastsAndDeepSeaContinuity()
    {
        foreach (int span in new[] { 20, 40 })
        {
            using var channel = new OceanShoreDistanceBathymetryKernel(0, -2, span + 1, 5, .5, 16, 4,
                (x, y) => x <= 0 || x >= span ? .75 : .25, currentSearchDistance: 64, currentTransitionWidth: 3);
            var center = channel.GetOceanCurrent(span / 2, 2, 0, 1);
            Check(center.X == 0 && center.Y == 0, "symmetric channel center stays weak at span " + span);
            var left = channel.GetOceanCurrent(span / 2 - 1, 2, 0, 1);
            var right = channel.GetOceanCurrent(span / 2 + 1, 2, 0, 1);
            Check(span == 20 ? left.X < 0 && right.X > 0 : left.X > 0 && right.X < 0,
                "opposing channel currents match their local shore");
            Check(Math.Abs(left.X + right.X) < .000001 && Math.Abs(left.Y - right.Y) < .000001,
                "symmetric channel has no arbitrary wind spike");
        }
        using var islands = new OceanShoreDistanceBathymetryKernel(-4, -2, 9, 5, .5, 16, 4,
            (x, y) => (x - 8) * (x - 8) + y * y <= 4 || (x + 8) * (x + 8) + y * y <= 4 ? .75 : .25,
            currentSearchDistance: 64, currentTransitionWidth: 3);
        var between = islands.GetOceanCurrent(4, 2, 0, 1);
        Check(between.X == 0 && between.Y == 0, "equal-distance islands preserve a weak central ridge");
        foreach (double distance in new[] { 8d, 16d, 24d })
        {
            OceanShoreCurrentKernel.ResolveCurrentVector(0, 0, distance, 16, 3, 64, 1, 0, out double x, out double y);
            Check(x == 0 && y == 0, "zero nearshore normal does not become full wind");
        }
        OceanShoreCurrentKernel.ResolveCurrentVector(-1, .00001, 56, 16, 3, 64, 1, 0, out double ax, out double ay);
        OceanShoreCurrentKernel.ResolveCurrentVector(-1, -.00001, 56, 16, 3, 64, 1, 0, out double bx, out double by);
        Check(Math.Abs(ax - bx) < .0001 && Math.Abs(ay - by) < .0001,
            "opposite background-current seam is continuous");
        OceanShoreCurrentKernel.ResolveCurrentVector(-1, 0, 19.999, 16, 64, 20, 1, 0, out ax, out ay);
        OceanShoreCurrentKernel.ResolveCurrentVector(-1, 0, 20, 16, 64, 20, 1, 0, out bx, out by);
        Check(Math.Abs(ax - bx) < .001 && Math.Abs(ay - by) < .001,
            "extreme configured transition remains continuous at search limit");
        Console.WriteLine("PASS symmetric channels / islands, zero normals and deep-sea transition continuity");
    }

    private static void ValidateGenerationFieldAgreement()
    {
        foreach (bool wrapped in new[] { false, true })
        foreach (string algorithm in new[] { "simple", "legacy" })
        {
            var full = Generate(32, new Int2(-16, -16), wrapped, algorithm);
            var left = Generate(16, new Int2(-16, -16), wrapped, algorithm);
            var right = Generate(16, new Int2(0, -16), wrapped, algorithm);
            foreach (var pair in left.Concat(right))
                Check(full[pair.Key] == pair.Value, "production current / chunk-width agreement " + algorithm);
            Check(full.Values.Any(v => v.water > 0) && full.Values.Any(v => v.water == 0), "mixed shoreline fixture");
            Console.WriteLine($"PASS production ocean fields and chunk agreement {algorithm}/wrapped={wrapped}");
        }
    }

    private static Dictionary<Int2, (int water, float x, float y)> Generate(int width, Int2 origin, bool wrapped, string algorithm)
    {
        var numbers = new Dictionary<string, double>
        {
            ["terrain.seaLevel"] = .5, ["terrain.beachLevel"] = .51,
            ["terrain.noiseScale"] = .04, ["terrain.scale"] = .04,
            ["terrain.height.frequency"] = 2, ["terrain.height.secondaryBoostEnabled"] = 1,
            ["river.enabled"] = 0, ["lake.large.enabled"] = 0, ["lake.lava.enabled"] = 0,
            ["grass.density"] = 0, ["climate.polarBand.enabled"] = 0,
            ["climate.temperature.celsiusMin"] = 20, ["climate.temperature.celsiusMax"] = 20,
            ["climate.temperature.altitudeCoolingStrength"] = 0,
            ["climate.distance.oceanRadiusChunks"] = 0, ["climate.distance.freshwaterRadiusChunks"] = 0,
            ["airHumidity.radiusTiles"] = 0, ["climate.distance.minimumRainfall"] = .5,
            ["climate.distance.maximumRainfall"] = .5,
            ["climate.distance.chunkWidth"] = 16, ["climate.distance.chunkHeight"] = 16
        };
        var profile = new ChunkGenerationProfileSnapshot("ocean.current.validation", 80, width, 16, numbers,
            new Dictionary<string, string> { ["climate.algorithm"] = algorithm });
        var topology = wrapped ? new ChunkGenerationTopologySnapshot(new Int2(-32, -32), new Int2(64, 64)) : default;
        using var world = new WorldRuntime("ocean.current.validation", 1);
        var generator = new DeterministicChunkGenerator();
        var address = new WorldAddress("surface", origin);
        try
        {
            using var result = generator.Generate(world.BeginChunkGeneration(address, 17413, profile, topology), CancellationToken.None);
            Check(world.TryCommit(result, out _), "commit production terrain");
            world.TryGetChunkTerrain(address, out var terrain);
            var samples = new Dictionary<Int2, (int, float, float)>();
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                bool hasX = terrain.TryGetEnvironmentValue(OceanShoreCurrentKernel.DirectionXLayer, x, y, out float currentX);
                bool hasY = terrain.TryGetEnvironmentValue(OceanShoreCurrentKernel.DirectionYLayer, x, y, out float currentY);
                Check(hasX && hasY, "production publishes coast current layers");
                Check(currentX * currentX + currentY * currentY <= 1.000001f, "production current bound");
                samples.Add(new Int2(origin.X + x, origin.Y + y), (terrain.GetLiquidLevel(x, y), currentX, currentY));
            }
            return samples;
        }
        finally { generator.BeginNewWorldHydrologyEpoch(1); }
    }

    #endregion
}
