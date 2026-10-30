using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;

namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
}

namespace Newtonsoft.Json.Linq
{
    public sealed class JObject : Dictionary<string, object> { }
}

namespace FlatWorld.WorldModel
{
    public readonly struct Int2
    {
        public Int2(int x, int y) { X = x; Y = y; }
        public int X { get; }
        public int Y { get; }
    }
    public readonly struct WorldAddress : IEquatable<WorldAddress>
    {
        public WorldAddress(string dimensionId, Int2 origin) { DimensionId = dimensionId; ChunkOrigin = origin; }
        public string DimensionId { get; }
        public Int2 ChunkOrigin { get; }
        public bool Equals(WorldAddress other) => DimensionId == other.DimensionId &&
            ChunkOrigin.X == other.ChunkOrigin.X && ChunkOrigin.Y == other.ChunkOrigin.Y;
        public override bool Equals(object value) => value is WorldAddress other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(DimensionId, ChunkOrigin.X, ChunkOrigin.Y);
    }
    public sealed class ChunkGenerationProfileSnapshot
    {
        public ChunkGenerationProfileSnapshot(string id, int signature, int width, int height) { }
    }
    public readonly struct ChunkGenerationTopologySnapshot { }
    public readonly struct ChunkGenerationRequest
    {
        public ChunkGenerationRequest(long epoch, WorldAddress address, int seed, long version,
            ChunkGenerationProfileSnapshot profile, ChunkGenerationTopologySnapshot topology) => Address = address;
        public WorldAddress Address { get; }
    }
    public sealed class ChunkTerrainBuffer
    {
        public ChunkTerrainBuffer(int width, int height) { }
    }
    public sealed class ChunkGenerationResult : IDisposable
    {
        public ChunkGenerationResult(ChunkGenerationRequest request, ChunkTerrainBuffer terrain) { }
        public void Dispose() { }
    }
    public sealed class ChunkGenerationTiming
    {
        public void StartGeneration() { }
        public void FinishGeneration() { }
    }
    public interface IChunkPureGenerator
    {
        object GetPendingGenerationGroupKey(ChunkGenerationRequest request);
        ChunkGenerationResult Generate(ChunkGenerationRequest request, CancellationToken token, ChunkGenerationTiming timing = null);
    }
    internal interface IChunkGenerationPrewarmSchedulerBinding
    {
        void BindChunkGenerationPrewarmScheduler(ChunkGenerationScheduler scheduler);
        void UnbindChunkGenerationPrewarmScheduler(ChunkGenerationScheduler scheduler);
    }
}

public static partial class GameplayTestSystemProbe
{
    public static void Register(string systemId, string operationId,
        Func<Newtonsoft.Json.Linq.JObject, Newtonsoft.Json.Linq.JObject> handler) { }
}

internal static class Program
{
    private sealed class SearchFixtureGenerator : IChunkPureGenerator, IChunkGenerationPrewarmSchedulerBinding
    {
        public ChunkGenerationScheduler Bound;
        public readonly ManualResetEventSlim Started = new(false);
        public readonly ManualResetEventSlim Release = new(false);
        public readonly ConcurrentQueue<string> Order = new();
        public void BindChunkGenerationPrewarmScheduler(ChunkGenerationScheduler scheduler) => Interlocked.Exchange(ref Bound, scheduler);
        public void UnbindChunkGenerationPrewarmScheduler(ChunkGenerationScheduler scheduler) => Interlocked.CompareExchange(ref Bound, null, scheduler);
        public object GetPendingGenerationGroupKey(ChunkGenerationRequest request) => null;
        public ChunkGenerationResult Generate(ChunkGenerationRequest request, CancellationToken token, ChunkGenerationTiming timing = null)
        {
            if (request.Address.ChunkOrigin.X == 0)
            {
                Started.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(5), token)) throw new Exception("搜索预热测试超时");
            }
            else Order.Enqueue("visible");
            return new ChunkGenerationResult(request, new ChunkTerrainBuffer(1, 1));
        }
    }

    private static ChunkGenerationRequest SearchFixtureRequest(int x) => new(1,
        new WorldAddress("probe", new Int2(x, 0)), 1, 1,
        new ChunkGenerationProfileSnapshot("search", 1, 1, 1), default);

    private static void CheckSearchPrewarmBindingAndCancellation()
    {
        var generator = new SearchFixtureGenerator();
        using var cancelledSearch = new CancellationTokenSource();
        using var retainedSearch = new CancellationTokenSource();
        using var prewarmed = new ManualResetEventSlim();
        using var scheduler = new ChunkGenerationScheduler(generator, 1);
        if (!ReferenceEquals(generator.Bound, scheduler)) throw new Exception("搜索预热未绑定所属调度器");
        var seed = scheduler.ScheduleAsync(SearchFixtureRequest(0));
        try
        {
            if (!generator.Started.Wait(TimeSpan.FromSeconds(5))) throw new Exception("搜索夹具未开始");
            var source = SearchFixtureRequest(4).Address;
            bool accepted = generator.Bound.QueuePrewarmForGenerationDemand(100, CancellationToken.None,
                source, cancelledSearch.Token, false, _ => throw new Exception("已取消的搜索预热被执行"));
            scheduler.CancelPrewarmDemandsForSource(source);
            if (!accepted || scheduler.QueuedCount != 1) throw new Exception("窗口撤销误伤独立搜索预热");
            cancelledSearch.Cancel();
            if (scheduler.QueuedCount != 0) throw new Exception("搜索取消没有撤销排队预热");
            accepted = generator.Bound.QueuePrewarmForGenerationDemand(101, CancellationToken.None,
                source, retainedSearch.Token, false, _ => { generator.Order.Enqueue("prewarm"); prewarmed.Set(); });
            var visible = scheduler.ScheduleAsync(SearchFixtureRequest(2));
            generator.Release.Set();
            if (!accepted || !prewarmed.Wait(TimeSpan.FromSeconds(5)) ||
                !SpinWait.SpinUntil(() => seed.IsCompleted && visible.IsCompleted, TimeSpan.FromSeconds(5)))
                throw new Exception("搜索预热或正式任务没有完成");
            if (string.Join(",", generator.Order) != "visible,prewarm") throw new Exception("搜索预热没有遵循正式任务优先级");
            if (seed.Status == TaskStatus.RanToCompletion) seed.Result.Dispose();
            if (visible.Status == TaskStatus.RanToCompletion) visible.Result.Dispose();
        }
        finally { generator.Release.Set(); }
        var replacement = new ChunkGenerationScheduler(generator, 1);
        scheduler.Dispose();
        if (!ReferenceEquals(generator.Bound, replacement)) throw new Exception("旧调度器清除了新的绑定");
        replacement.Dispose();
        if (generator.Bound != null) throw new Exception("调度器释放后预热绑定仍保留");
        Console.WriteLine("PASS search prewarm: bound scheduler, priority, source cancellation and replacement ownership");
    }

    private static void Main()
    {
        if (Environment.ProcessorCount < 6) throw new Exception("测试需要至少两个实际后台并发额度");
        var method = typeof(GameplayTestSystemProbe).GetMethod("CaptureBackgroundGenerationScheduling",
            BindingFlags.NonPublic | BindingFlags.Static);
        for (int round = 1; round <= 3; round++)
        {
            var data = (Newtonsoft.Json.Linq.JObject)method.Invoke(null, new object[] { new Newtonsoft.Json.Linq.JObject() });
            foreach (KeyValuePair<string, object> entry in data)
            {
                if (!(entry.Value is bool value) || !value)
                    throw new Exception($"round {round}: {entry.Key} failed");
            }
            Console.WriteLine($"PASS round {round}: {data.Count} scheduler assertions");
        }
        CheckSearchPrewarmBindingAndCancellation();
    }
}
