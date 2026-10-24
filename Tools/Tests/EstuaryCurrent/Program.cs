using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using FlatWorld.WorldModel;
using Unity.Mathematics;

internal static class Program
{
    #region 正式核与生成管线回归

    private static int passed;
    private static readonly double Spread = Math.Tan(35d * Math.PI / 180d);
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static EstuaryCurrentSample Sample(double x, double y)
    {
        EstuaryCurrentKernel.TrySample(x, y, 1, 0, 4, 18, Spread, out var sample);
        return sample;
    }

    private static void Main()
    {
        var core = Sample(4, 0);
        Check(core.Influence == 1 && core.X > .999, "mouth core points offshore");
        Check(Sample(8, 3).Y > 0 && Sample(8, -3).Y < 0, "fan diverges both sides");
        Check(Sample(-6, 0).Influence == 0 && Sample(19, 0).Influence == 0 &&
              Sample(4, 12).Influence == 0, "upstream, distal and lateral limits");
        bool valid = true;
        for (int i = 0; i <= 1000; i++)
        {
            EstuaryCurrentKernel.ResolveDirection(-1, 0,
                new EstuaryCurrentSample(1, 0, i / 1000d), out double x, out double y);
            valid &= Math.Abs(x * x + y * y - 1) < 1e-10;
        }
        Check(valid, "all opposing-current blend weights preserve speed");
        EstuaryCurrentKernel.ResolveDirection(-1, 0, Sample(14.4, 0), out var sx, out var sy);
        Check(Math.Abs(sx) < 1e-8 && sy > .999, "opposite midpoint diverts sideways instead of stopping");
        EstuaryCurrentKernel.ResolveDirection(0, 0, core, out sx, out sy);
        Check(sx > .999 && Math.Abs(sy) < 1e-8, "windless sea still carries the river plume");
        EstuaryCurrentKernel.ResolveDirection(-1, 0, default, out sx, out sy);
        Check(sx == -1 && sy == 0, "outside retains original ocean current");
        Check(!EstuaryCurrentKernel.TrySample(double.NaN, 0, 1, 0, 4, 18, Spread, out _) &&
              !EstuaryCurrentKernel.TrySample(0, 0, 0, 0, 4, 18, Spread, out _) &&
              !EstuaryCurrentKernel.TrySample(0, 0, 1, 0, 4, 0, Spread, out _), "invalid/disabled fan rejected");
        // Advect through actual cell-quantized production directions, including the exterior ocean.
        foreach (double startY in new[] { -2d, -1d, 0d, 1d, 2d })
        {
            double x = .5, y = startY, maxX = x;
            for (int step = 0; step < 4000; step++)
            {
                var plume = Sample(Math.Floor(x) + .5, Math.Floor(y) + .5);
                EstuaryCurrentKernel.ResolveDirection(-1, 0, plume, out double vx, out double vy);
                // Same water-only boundary constraint: sea is x >= 0.
                x = Math.Max(.01, x + vx * .05); y += vy * .05;
                maxX = Math.Max(maxX, x);
            }
            Check(maxX > 10 && Math.Abs(y) > 8,
                $"quantized reverse-current trajectory disperses from mouth (y={startY}, final={x:F2},{y:F2})");
        }

        var plain = Generate();
        Check(plain.Count > 50 && plain.ContainsKey(new Int2(5, 0)), "production generator creates offshore fan");
        Check(plain.Keys.All(p => p.X > 0), "production fan never writes land");
        Check(Generate(reverseFlow: true).Count == 0 && Generate(allOcean: true).Count == 0,
            "inland flow and ocean-only centerline are not mouths");
        Check(Generate(length: 0).Count == 0, "configuration can disable estuaries");
        var island = Generate(island: true);
        Check(!island.ContainsKey(new Int2(8, 0)) && !island.ContainsKey(new Int2(13, 0)) &&
              island.ContainsKey(new Int2(5, 0)), "island and its occluded bay reject plume");
        var adjacent = Generate(chunkX: 32);
        Check(Equal(plain, adjacent), "adjacent chunk windows produce identical shared cells");
        var wrapped = Generate(mouthX: 63, span: 128, chunkX: -64);
        var unwrapped = Generate(mouthX: 63, chunkX: 64);
        Check(wrapped.Count == unwrapped.Count && unwrapped.All(pair =>
        {
            int x = ((pair.Key.X + 64) % 128 + 128) % 128 - 64;
            return wrapped.TryGetValue(new Int2(x, pair.Key.Y), out var value) && Same(value, pair.Value);
        }), "world-wrap seam matches ordinary shoreline");
        Check(Equal(Generate(twoMouths: true), Generate(twoMouths: true, reversedInsertion: true)),
            "overlapping fans independent of source enumeration order");
        bool cancelled = false;
        try { Generate(cancelled: true); }
        catch (TargetInvocationException e) when (e.InnerException is OperationCanceledException) { cancelled = true; }
        Check(cancelled, "cancelled generation cannot publish a partial fan");
        Console.WriteLine($"ESTUARY TESTS PASSED: {passed}");
    }

    private static bool Same(EstuaryCurrentSample a, EstuaryCurrentSample b) =>
        a.X == b.X && a.Y == b.Y && a.Influence == b.Influence;
    private static bool Equal(Dictionary<Int2, EstuaryCurrentSample> a, Dictionary<Int2, EstuaryCurrentSample> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && Same(pair.Value, value));

    // Supply deterministic terrain inputs to the production generator, not a duplicate rasterizer.
    private static Dictionary<Int2, EstuaryCurrentSample> Generate(int mouthX = 0, int span = 0,
        int chunkX = 0, bool island = false, bool reverseFlow = false, bool allOcean = false,
        int length = 18, bool twoMouths = false, bool reversedInsertion = false, bool cancelled = false)
    {
        Type generator = typeof(DeterministicChunkGenerator);
        const BindingFlags nested = BindingFlags.NonPublic;
        const BindingFlags members = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        object Create(Type t, params object[] args) => Activator.CreateInstance(t, members, null, args, null);
        var numbers = new Dictionary<string, double>
        {
            ["terrain.seaLevel"] = .5, ["river.maxWidth"] = 7,
            ["river.estuary.length"] = length, ["river.estuary.halfAngleDegrees"] = 35,
            ["climate.distance.freshwaterRadiusChunks"] = 0, ["airHumidity.radiusTiles"] = 0,
            ["lake.large.enabled"] = 0, ["lake.lava.enabled"] = 0
        };
        var profile = new ChunkGenerationProfileSnapshot("estuary-test", 1, 32, 32, numbers);
        var topology = span > 0
            ? new ChunkGenerationTopologySnapshot(new Int2(-span / 2, -span / 2), new Int2(span, span)) : default;
        var request = new ChunkGenerationRequest(1, new WorldAddress("surface", new Int2(chunkX, 0)),
            17413, 1, profile, topology);
        object settings = typeof(ChunkGenerationProfileSnapshot).GetProperty("Settings", members).GetValue(profile);
        Type samplingType = generator.GetNestedType("HydrologySamplingContext", nested);
        object sampling = Create(samplingType, request, settings);
        var heights = (Dictionary<Int2, double>)samplingType.GetField("heightCache", members).GetValue(sampling);
        for (int dx = -40; dx <= 40; dx++)
        for (int dy = -40; dy <= 40; dy++)
        {
            var p = new Int2(topology.NormalizeX(mouthX + dx), topology.NormalizeY(dy));
            heights[p] = (allOcean || dx > 0) && !(island && dx >= 8 && dx <= 9 && Math.Abs(dy) <= 2)
                ? .25 : .75;
        }
        Type center = generator.GetNestedType("ChannelCenterSample", nested);
        var centers = (IDictionary)Create(typeof(Dictionary<,>).MakeGenericType(typeof(Int2), center));
        int[] ys = twoMouths ? reversedInsertion ? new[] { 6, 0 } : new[] { 0, 6 } : new[] { 0 };
        foreach (int y in ys) centers.Add(new Int2(mouthX, y), Create(center, .45, reverseFlow ? -1d : 1d, 0d, false));
        object window = Create(generator.GetNestedType("HydrologyOutputWindow", nested), request);
        return (Dictionary<Int2, EstuaryCurrentSample>)generator.GetMethod("BuildEstuaryCurrents",
            BindingFlags.NonPublic | BindingFlags.Static).Invoke(null,
                new[] { settings, sampling, window, centers, (object)new CancellationToken(cancelled) });
    }

    #endregion
}
