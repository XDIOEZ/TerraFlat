using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using FlatWorld.WorldModel;

internal static class Program
{
    #region 生产河道与河漫滩等价采样

    private static readonly Type Generator = typeof(DeterministicChunkGenerator);
    private static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type Sampling = Generator.GetNestedType("HydrologySamplingContext", BindingFlags.NonPublic);
    private static readonly Type Window = Generator.GetNestedType("HydrologyOutputWindow", BindingFlags.NonPublic);
    private static readonly Type Center = Generator.GetNestedType("ChannelCenterSample", BindingFlags.NonPublic);
    private static readonly Type Water = Generator.Assembly.GetType("FlatWorld.WorldModel.GeneratedHydrologyCell");
    private static readonly MethodInfo Render = Generator.GetMethod("RenderMacroCenters", PrivateStatic);

    private sealed class CancellingCenters<T> : IReadOnlyDictionary<Int2, T>
    {
        private readonly IReadOnlyDictionary<Int2, T> centers;
        private readonly CancellationTokenSource cancellation;
        public CancellingCenters(object centers, CancellationTokenSource cancellation)
        { this.centers = (IReadOnlyDictionary<Int2, T>)centers; this.cancellation = cancellation; }
        public T this[Int2 key] => centers[key];
        public IEnumerable<Int2> Keys => centers.Keys;
        public IEnumerable<T> Values => centers.Values;
        public int Count => centers.Count;
        public bool ContainsKey(Int2 key) => centers.ContainsKey(key);
        public bool TryGetValue(Int2 key, out T value) => centers.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<Int2, T>> GetEnumerator()
        {
            int count = 0;
            foreach (var pair in centers)
            {
                if (count++ == 64) cancellation.Cancel();
                yield return pair;
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static object Create(Type type, params object[] args) => Activator.CreateInstance(type,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, args, null);

    private static string Hash(IDictionary cells)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        // 连插入顺序也一起核对，避免改变后续字典遍历顺序。
        foreach (DictionaryEntry entry in cells)
        {
            Int2 position = (Int2)entry.Key;
            writer.Write(position.X); writer.Write(position.Y);
            if (entry.Value is double value) writer.Write(BitConverter.DoubleToInt64Bits(value));
            else foreach (var property in Water.GetProperties().OrderBy(p => p.Name))
            {
                object field = property.GetValue(entry.Value);
                if (field is double number) writer.Write(BitConverter.DoubleToInt64Bits(number));
                else if (field is bool flag) writer.Write(flag);
                else writer.Write(Convert.ToInt32(field));
            }
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static object Run(string name, int origin, int span, int padding, int radius,
        int centerCount = 1200, bool ocean = false, bool cancelled = false, bool cancelAfterWork = false)
    {
        var numbers = new Dictionary<string, double>
        {
            ["river.maxWidth"] = 7, ["river.floodplainMaxRadius"] = radius,
            ["river.floodplainMaxSlope"] = 1, ["river.startFlow"] = .1,
            ["river.fullWidthFlow"] = 1, ["river.floodplainStartFlow"] = .1,
            ["terrain.seaLevel"] = ocean ? 1 : 0,
            ["climate.distance.freshwaterRadiusChunks"] = (padding - 34d) / 16d,
            ["climate.distance.chunkWidth"] = 16, ["climate.distance.chunkHeight"] = 16,
            ["biome.peat.stoneBoundaryRadius"] = 2,
            ["airHumidity.radiusTiles"] = padding,
            ["lake.large.enabled"] = 0, ["lake.lava.enabled"] = 0,
            ["structure.enabled"] = 0
        };
        var profile = new ChunkGenerationProfileSnapshot("raster-test", 1, 32, 32, numbers,
            new Dictionary<string, string> { ["climate.algorithm"] = "simple" });
        var topology = span > 0 ? new ChunkGenerationTopologySnapshot(new Int2(-span / 2, -span / 2), new Int2(span, span)) : default;
        var request = new ChunkGenerationRequest(1, new WorldAddress("surface", new Int2(origin, origin)), 17413, 1, profile, topology);
        var settings = (ChunkGenerationSettingsSnapshot)typeof(ChunkGenerationProfileSnapshot)
            .GetProperty("Settings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(profile);
        object window = Create(Window, request);
        var centers = (IDictionary)Create(typeof(Dictionary<,>).MakeGenericType(typeof(Int2), Center));
        var random = new Random(771);
        for (int i = 0; i < centerCount; i++)
        {
            int x = topology.NormalizeX(origin - padding + random.Next(32 + padding * 2));
            int y = topology.NormalizeY(origin - padding + random.Next(32 + padding * 2));
            centers[new Int2(x, y)] = Create(Center, .1 + random.NextDouble(), .6, .8, i % 5 == 0);
        }
        var times = new List<double>();
        long allocated = 0;
        long heightLookups = 0;
        IDictionary water = null, floodplain = null;
        for (int repeat = 0; repeat < 5; repeat++)
        {
            var timing = new ChunkStreamingDiagnostics().BeginRequest(request);
            typeof(ChunkGenerationTiming).GetProperty("CurrentOnThread", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, timing);
            object sampling = Create(Sampling, request, settings);
            water = (IDictionary)Create(typeof(Dictionary<,>).MakeGenericType(typeof(Int2), Water));
            floodplain = new Dictionary<Int2, double>();
            using var cancellation = new CancellationTokenSource();
            if (cancelled) cancellation.Cancel();
            object selectedCenters = cancelAfterWork
                ? Create(typeof(CancellingCenters<>).MakeGenericType(Center), centers, cancellation) : centers;
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            try
            {
                Render.Invoke(null, new object[] { settings, sampling, window, selectedCenters, 3, water, floodplain, cancellation.Token, timing });
                if (cancelled || cancelAfterWork) throw new Exception("Cancelled rendering continued");
            }
            catch (TargetInvocationException exception) when ((cancelled || cancelAfterWork) && exception.InnerException is OperationCanceledException)
            {
                if (cancelAfterWork && water.Count == 0) throw new Exception("Cancellation did not exercise a partially filled raster");
                // 取消请求没有可提交结果，只检查抛出取消，随后重跑检查池化内容不会串请求。
                water.Clear(); floodplain.Clear();
            }
            watch.Stop();
            heightLookups = (long)Sampling.GetProperty("HeightHitCount").GetValue(sampling) +
                (long)Sampling.GetProperty("HeightMissCount").GetValue(sampling);
            typeof(ChunkGenerationTiming).GetProperty("CurrentOnThread", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, null);
            if (repeat > 0) { times.Add(watch.Elapsed.TotalMilliseconds); allocated += GC.GetAllocatedBytesForCurrentThread() - before; }
        }
        times.Sort();
        return new { name, waterCount = water.Count, floodplainCount = floodplain.Count, waterHash = Hash(water),
            floodplainHash = Hash(floodplain), milliseconds = times[times.Count / 2], allocatedBytes = allocated / times.Count,
            heightLookups };
    }

    private static void Main(string[] args)
    {
        var results = new[] {
            Run("dense-overlap", 0, 0, 64, 14),
            Run("negative", -128, 0, 64, 14),
            Run("seam", 96, 256, 64, 14),
            Run("narrow-world", -16, 32, 64, 14),
            Run("large-padding", 0, 0, 322, 14, 4800),
            Run("sparse-window", 0, 0, 600, 14, 300),
            Run("maximum-radius", 0, 0, 64, 96, 140),
            Run("ocean-rejected", 0, 0, 64, 14, ocean: true),
            Run("disabled-floodplain", 0, 0, 64, 0),
            Run("empty", 0, 0, 64, 14, 0),
            Run("cancelled", 0, 0, 64, 14, cancelled: true),
            Run("cancelled-after-work", 0, 0, 64, 14, cancelAfterWork: true),
            Run("after-cancellation", 0, 0, 64, 14)
        };
        string json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(args[0], json);
        Console.WriteLine("Saved production raster results: " + args[0]);
    }

    #endregion
}
