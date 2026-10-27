using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;

internal static class Program
{
    #region 外部世界所有者回归

    private static int assertions;
    private static readonly ChunkGenerationProfileSnapshot Profile = new("ownership-probe", 1, 2, 2);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }

    private static bool WaitUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10));

    private static ChunkGenerationResult Result(ChunkGenerationRequest request, int ground)
    {
        var buffer = new ChunkTerrainBuffer(2, 2);
        buffer.SetCell(0, 0, new TerrainCell(ground, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        return new ChunkGenerationResult(request, buffer);
    }

    private sealed class ControlledGenerator : IChunkPureGenerator
    {
        public readonly ManualResetEventSlim Started = new(false);
        public readonly ManualResetEventSlim Release = new(false);
        public readonly bool Fail;
        public ChunkGenerationResult Result;

        public ControlledGenerator(bool fail = false) => Fail = fail;
        public object GetPendingGenerationGroupKey(ChunkGenerationRequest request) => null;

        public ChunkGenerationResult Generate(ChunkGenerationRequest request, CancellationToken cancellationToken,
            ChunkGenerationTiming timing = null)
        {
            Started.Set();
            Release.Wait();
            if (Fail) throw new InvalidOperationException("old-generation-failure");
            return Result = Program.Result(request, 77);
        }
    }

    private static void Commit(ChunkMgr manager, Task<ChunkRuntime> pending)
    {
        Check(WaitUntil(() => manager.PendingCommitCount > 0), "生成完成通知没有到达");
        manager.CommitCompleted(16);
        Check(pending.IsCompletedSuccessfully, "正式调度器没有提交最新请求");
    }

    private static CancellationTokenSource PendingCancellation(ChunkMgr manager, WorldAddress address)
    {
        var pending = (IDictionary)typeof(ChunkMgr).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(manager);
        object entry = pending[address];
        return (CancellationTokenSource)entry.GetType().GetField("Cancellation").GetValue(entry);
    }

    private static bool IsDisposed(CancellationTokenSource source)
    {
        try { _ = source.Token; return false; }
        catch (ObjectDisposedException) { return true; }
    }

    private static void RunExternalOwnershipAndLateCompletion()
    {
        using var world = new WorldRuntime("ship:ownership-probe", 1);
        var stableAddress = new WorldAddress("interior", new Int2(0, 0));
        var movingAddress = new WorldAddress("interior", new Int2(2, 0));
        ChunkGenerationRequest seedRequest = world.BeginChunkGeneration(stableAddress, 1, Profile);
        using (ChunkGenerationResult seed = Result(seedRequest, 23))
            Check(world.TryCommit(seed, out _), "无法准备外部世界的地形");
        Check(world.TryGetChunk(stableAddress, out ChunkRuntime stable), "已提交区块缺失");

        using ChunkLease retained = world.AcquireChunkLease(stableAddress, ChunkLeaseKind.DataRetention);
        using (ChunkLease view = world.AcquireChunkLease(stableAddress, ChunkLeaseKind.Presentation))
            stable.MarkPresentationBound();
        Check(stable.PresentationStatus == ChunkPresentationStatus.Unbound, "撤销表现租约没有解绑画面");

        var oldGenerator = new ControlledGenerator();
        var oldManager = new ChunkMgr(world, oldGenerator, 1);
        Task<ChunkRuntime> oldRequest = oldManager.RequestChunkDataAsync(movingAddress, 1, Profile);
        Check(oldGenerator.Started.Wait(TimeSpan.FromSeconds(10)), "旧调度器没有启动生成");
        CancellationTokenSource oldCancellation = PendingCancellation(oldManager, movingAddress);
        oldManager.Dispose();
        Check(oldRequest.IsCanceled, "旧调度器释放没有取消等待者");
        Check(world.TryGetChunk(movingAddress, out ChunkRuntime pending) && pending.DataStatus == ChunkDataStatus.Absent,
            "调度器释放后区块仍假装正在生成");
        Check(world.TryGetChunk(stableAddress, out ChunkRuntime retainedChunk) && ReferenceEquals(stable, retainedChunk) &&
              retainedChunk.Terrain.GetCell(0, 0).GroundTileId == 23 && !retainedChunk.Terrain.IsDisposed,
            "外部所有者持有的正式区块或地形被释放");

        var nextGenerator = new ControlledGenerator();
        using var nextManager = new ChunkMgr(world, nextGenerator, 1);
        Task<ChunkRuntime> nextRequest = nextManager.RequestChunkDataAsync(movingAddress, 2, Profile);
        Check(nextGenerator.Started.Wait(TimeSpan.FromSeconds(10)), "新调度器没有接管世界");
        long newVersion = pending.GenerationVersion;
        oldGenerator.Release.Set();
        Check(WaitUntil(() => oldGenerator.Result != null && oldGenerator.Result.IsDisposed), "迟到的旧生成载荷未释放");
        Check(WaitUntil(() => IsDisposed(oldCancellation)), "迟到的旧生成通知未释放取消源");
        Check(oldManager.PendingCommitCount == 0, "关闭后的调度器仍积存迟到通知");
        Check(pending.DataStatus == ChunkDataStatus.Generating && pending.GenerationVersion == newVersion,
            "旧调度器迟到完成破坏了新调度器请求");
        nextGenerator.Release.Set();
        Commit(nextManager, nextRequest);
        Check(pending.Terrain.GetCell(0, 0).GroundTileId == 77 && ReferenceEquals(nextManager.World, world),
            "新调度器没有在原外部世界提交地形");
    }

    private static void RunStaleFailureAndDisposeIsolation()
    {
        using var world = new WorldRuntime("ship:failure-probe", 1);
        var staleAddress = new WorldAddress("interior", new Int2(0, 0));
        var oldGenerator = new ControlledGenerator(fail: true);
        var newGenerator = new ControlledGenerator();
        using var oldManager = new ChunkMgr(world, oldGenerator, 1);
        using var newManager = new ChunkMgr(world, newGenerator, 1);
        int failures = 0;
        using var subscription = world.Events.Subscribe<ChunkGenerationFailed>(_ => failures++);
        Task<ChunkRuntime> oldRequest = oldManager.RequestChunkDataAsync(staleAddress, 1, Profile);
        Check(oldGenerator.Started.Wait(TimeSpan.FromSeconds(10)), "旧失败任务未开始");
        Task<ChunkRuntime> newRequest = newManager.RequestChunkDataAsync(staleAddress, 2, Profile);
        Check(newGenerator.Started.Wait(TimeSpan.FromSeconds(10)), "新任务未开始");
        world.TryGetChunk(staleAddress, out ChunkRuntime target);
        long newVersion = target.GenerationVersion;
        oldGenerator.Release.Set();
        Check(WaitUntil(() => oldManager.PendingCommitCount > 0), "旧失败通知未到达");
        oldManager.CommitCompleted(16);
        Check(oldRequest.IsFaulted && failures == 0 && target.DataStatus == ChunkDataStatus.Generating &&
              target.GenerationVersion == newVersion, "旧失败污染了新调度器任务或发布错误事件");
        _ = oldRequest.Exception;
        oldManager.Dispose();
        Check(target.DataStatus == ChunkDataStatus.Generating && target.GenerationVersion == newVersion,
            "旧调度器释放取消了新调度器任务");
        newGenerator.Release.Set();
        Commit(newManager, newRequest);
    }

    private static void RunStaleSchedulerDisposalWhileNewRequestRuns()
    {
        using var world = new WorldRuntime("ship:dispose-probe", 1);
        var address = new WorldAddress("interior", new Int2(0, 0));
        var oldGenerator = new ControlledGenerator();
        var newGenerator = new ControlledGenerator();
        var oldManager = new ChunkMgr(world, oldGenerator, 1);
        using var newManager = new ChunkMgr(world, newGenerator, 1);
        Task<ChunkRuntime> oldRequest = oldManager.RequestChunkDataAsync(address, 1, Profile);
        Check(oldGenerator.Started.Wait(TimeSpan.FromSeconds(10)), "旧任务未开始");
        Task<ChunkRuntime> newRequest = newManager.RequestChunkDataAsync(address, 2, Profile);
        Check(newGenerator.Started.Wait(TimeSpan.FromSeconds(10)), "新任务未开始");
        world.TryGetChunk(address, out ChunkRuntime target);
        long newVersion = target.GenerationVersion;
        oldGenerator.Release.Set();
        Check(WaitUntil(() => oldManager.PendingCommitCount > 0), "旧结果未排入待处理队列");
        oldManager.Dispose();
        Check(oldRequest.IsCanceled && oldGenerator.Result.IsDisposed, "旧调度器释放时未归还自己的已完成结果");
        Check(target.DataStatus == ChunkDataStatus.Generating && target.GenerationVersion == newVersion,
            "旧调度器释放错误取消了新调度器的同址请求");
        newGenerator.Release.Set();
        Commit(newManager, newRequest);
    }

    private static void RunStaleCancellationWhileNewRequestRuns()
    {
        using var world = new WorldRuntime("ship:cancel-probe", 1);
        var address = new WorldAddress("interior", new Int2(0, 0));
        var oldGenerator = new ControlledGenerator();
        var newGenerator = new ControlledGenerator();
        using var oldManager = new ChunkMgr(world, oldGenerator, 1);
        using var newManager = new ChunkMgr(world, newGenerator, 1);
        oldManager.RefreshWindow(new ChunkWindowRequest(address, 1, 1, true, 1, Profile));
        Task<ChunkRuntime> oldRequest = oldManager.RequestChunkDataAsync(address, 1, Profile);
        Check(oldGenerator.Started.Wait(TimeSpan.FromSeconds(10)), "旧取消任务未开始");
        CancellationTokenSource cancellation = PendingCancellation(oldManager, address);
        Task<ChunkRuntime> newRequest = newManager.RequestChunkDataAsync(address, 2, Profile);
        Check(newGenerator.Started.Wait(TimeSpan.FromSeconds(10)), "新的同址任务未开始");
        world.TryGetChunk(address, out ChunkRuntime target);
        long newVersion = target.GenerationVersion;
        cancellation.Cancel();
        oldGenerator.Release.Set();
        Check(WaitUntil(() => oldManager.PendingCommitCount > 0), "旧取消通知未到达");
        oldManager.CommitCompleted(16);
        Check(oldRequest.IsCanceled && target.DataStatus == ChunkDataStatus.Generating &&
              target.GenerationVersion == newVersion, "旧取消通知或窗口重试覆盖了新调度器请求");
        newGenerator.Release.Set();
        Commit(newManager, newRequest);
    }

    private static void RunStaleSuccessfulResultWhileNewRequestRuns()
    {
        using var world = new WorldRuntime("ship:stale-result-probe", 1);
        var address = new WorldAddress("interior", new Int2(0, 0));
        var oldGenerator = new ControlledGenerator();
        var newGenerator = new ControlledGenerator();
        using var oldManager = new ChunkMgr(world, oldGenerator, 1);
        using var newManager = new ChunkMgr(world, newGenerator, 1);
        Task<ChunkRuntime> oldRequest = oldManager.RequestChunkDataAsync(address, 1, Profile);
        Check(oldGenerator.Started.Wait(TimeSpan.FromSeconds(10)), "旧成功任务未开始");
        Task<ChunkRuntime> newRequest = newManager.RequestChunkDataAsync(address, 2, Profile);
        Check(newGenerator.Started.Wait(TimeSpan.FromSeconds(10)), "新任务未开始");
        world.TryGetChunk(address, out ChunkRuntime target);
        long newVersion = target.GenerationVersion;
        oldGenerator.Release.Set();
        Check(WaitUntil(() => oldManager.PendingCommitCount > 0), "旧成功通知未到达");
        oldManager.CommitCompleted(16);
        Check(oldRequest.IsFaulted && oldGenerator.Result.IsDisposed &&
              target.DataStatus == ChunkDataStatus.Generating && target.GenerationVersion == newVersion,
            "旧成功结果没有释放或覆盖了新调度器的请求");
        _ = oldRequest.Exception;
        newGenerator.Release.Set();
        Commit(newManager, newRequest);
    }

    private static void Main()
    {
        RunExternalOwnershipAndLateCompletion();
        RunStaleFailureAndDisposeIsolation();
        RunStaleSchedulerDisposalWhileNewRequestRuns();
        RunStaleCancellationWhileNewRequestRuns();
        RunStaleSuccessfulResultWhileNewRequestRuns();
        Console.WriteLine($"PASS {assertions} checks: external WorldRuntime, view release, scheduler handoff, pending cancellation, late cleanup and stale failure isolation.");
    }

    #endregion
}
