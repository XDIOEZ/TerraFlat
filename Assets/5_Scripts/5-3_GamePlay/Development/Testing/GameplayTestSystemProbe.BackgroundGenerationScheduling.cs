using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public static partial class GameplayTestSystemProbe
{
    #region 区块生成与水文预热调度采样

    #if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    #endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RegisterBackgroundGenerationSchedulingRules() => Register(
        "world_streaming", "background_generation_scheduling", CaptureBackgroundGenerationScheduling);

    // 夹具只控制真实调度器的领取和完成时机，Lua 负责判断采样结果。
    private static JObject CaptureBackgroundGenerationScheduling(JObject arguments)
    {
        var result = new JObject();
        CapturePrewarmPriorityAndPromotion(result);
        CaptureRunningPrewarmReuseAndConcurrencyChange(result);
        CaptureSharedChunkDemandCancellation(result);
        CaptureActiveChunkAcceptsPrewarmDemand(result);
        CaptureCompletedSourceWindowRemovalAndReentry(result);
        CaptureQueuedPrewarmDemandAtCapacity(result);
        CapturePrewarmLifetimeAndFailureRecovery(result);
        return result;
    }

    private static void CapturePrewarmPriorityAndPromotion(JObject result)
    {
        using var lifetime = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        using var lastPrewarm = new ManualResetEventSlim();
        var order = new ConcurrentQueue<string>();
        int promotedPrewarmRuns = 0;
        var generator = new BackgroundSchedulingFixture();
        using var scheduler = new ChunkGenerationScheduler(generator, 1);
        generator.GenerateAction = (region, token) =>
        {
            if (region == 0)
            {
                generator.QueuePrewarm(1, lifetime.Token, _ => Interlocked.Increment(ref promotedPrewarmRuns));
                generator.QueuePrewarm(2, lifetime.Token, _ => { order.Enqueue("prewarm"); lastPrewarm.Set(); });
                started.Set();
                AwaitBackgroundSchedulingEvent(release, token);
            }
            else order.Enqueue("chunk" + region);
            generator.MarkReady(region);
        };
        Task<ChunkGenerationResult> seed = scheduler.ScheduleAsync(BackgroundSchedulingRequest(0));
        try
        {
            RequireBackgroundScheduling(started.Wait(TimeSpan.FromSeconds(5)), "首个区块未开始");
            Task<ChunkGenerationResult> promoted = scheduler.ScheduleAsync(BackgroundSchedulingRequest(1));
            Task<ChunkGenerationResult> visible = scheduler.ScheduleAsync(BackgroundSchedulingRequest(3));
            scheduler.PrioritizeQueuedWork(new[] { BackgroundSchedulingRequest(3).Address, BackgroundSchedulingRequest(1).Address });
            release.Set();
            RequireBackgroundScheduling(lastPrewarm.Wait(TimeSpan.FromSeconds(5)), "排队预热未完成");
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => seed.IsCompleted && promoted.IsCompleted && visible.IsCompleted,
                TimeSpan.FromSeconds(5)), "正式区块未完成");
            string[] steps = order.ToArray();
            result["visibleBeforeQueuedPrewarm"] = steps.Length == 3 && steps[0] == "chunk3" &&
                steps[1] == "chunk1" && steps[2] == "prewarm";
            result["promotedWithoutDuplicate"] = promotedPrewarmRuns == 0 && promoted.Status == TaskStatus.RanToCompletion;
            result["sharedLimit"] = generator.MaximumActive <= scheduler.MaxConcurrency;
            ReleaseBackgroundSchedulingResult(seed);
            ReleaseBackgroundSchedulingResult(promoted);
            ReleaseBackgroundSchedulingResult(visible);
        }
        finally { release.Set(); }
    }

    private static void CaptureRunningPrewarmReuseAndConcurrencyChange(JObject result)
    {
        using var lifetime = new CancellationTokenSource();
        using var source = new CancellationTokenSource();
        using var prewarmStarted = new ManualResetEventSlim();
        using var releasePrewarm = new ManualResetEventSlim();
        using var otherStarted = new ManualResetEventSlim();
        using var releaseOther = new ManualResetEventSlim();
        using var laterStarted = new ManualResetEventSlim();
        using var prewarmCancelled = new ManualResetEventSlim();
        int targetColdComputations = 0;
        var generator = new BackgroundSchedulingFixture();
        using var scheduler = new ChunkGenerationScheduler(generator, 2);
        generator.GenerateAction = (region, token) =>
        {
            if (region == 0)
            {
                generator.QueuePrewarm(1, lifetime.Token, prewarmToken =>
                {
                    prewarmStarted.Set();
                    try { AwaitBackgroundSchedulingEvent(releasePrewarm, prewarmToken); }
                    catch (OperationCanceledException) { prewarmCancelled.Set(); throw; }
                    generator.MarkReady(1);
                });
            }
            else if (region == 1)
            {
                if (!generator.IsReady(1)) Interlocked.Increment(ref targetColdComputations);
            }
            else if (region == 2)
            {
                otherStarted.Set();
                AwaitBackgroundSchedulingEvent(releaseOther, token);
            }
            else if (region == 3) laterStarted.Set();
            generator.MarkReady(region);
        };
        Task<ChunkGenerationResult> seed = scheduler.ScheduleAsync(BackgroundSchedulingRequest(0), source.Token);
        try
        {
            RequireBackgroundScheduling(prewarmStarted.Wait(TimeSpan.FromSeconds(5)), "共享区域预热未开始");
            Task<ChunkGenerationResult> target = scheduler.ScheduleAsync(BackgroundSchedulingRequest(1));
            Task<ChunkGenerationResult> other = scheduler.ScheduleAsync(BackgroundSchedulingRequest(2));
            RequireBackgroundScheduling(otherStarted.Wait(TimeSpan.FromSeconds(5)), "其他区域未使用空闲额度");
            result["sameRegionDoesNotOccupyWaitingSlot"] = !target.IsCompleted && scheduler.ActiveCount == 2;
            source.Cancel();
            result["sharedDemandSurvivesOneCancellation"] = !prewarmCancelled.Wait(TimeSpan.FromMilliseconds(120));
            scheduler.SetMaxConcurrency(1);
            Task<ChunkGenerationResult> later = scheduler.ScheduleAsync(BackgroundSchedulingRequest(3));
            releaseOther.Set();
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => other.IsCompleted, TimeSpan.FromSeconds(5)), "其他区域未结束");
            result["loweredLimitRespected"] = !laterStarted.Wait(TimeSpan.FromMilliseconds(120)) && !target.IsCompleted;
            releasePrewarm.Set();
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => target.IsCompleted && later.IsCompleted,
                TimeSpan.FromSeconds(5)), "预热后正式区块未继续");
            result["runningPrewarmReused"] = target.Status == TaskStatus.RanToCompletion && targetColdComputations == 0;
            result["sharedLimit"] = (bool)result["sharedLimit"] && generator.MaximumActive <= 2 && scheduler.MaxConcurrency == 1;
            ReleaseBackgroundSchedulingResult(seed);
            ReleaseBackgroundSchedulingResult(target);
            ReleaseBackgroundSchedulingResult(other);
            ReleaseBackgroundSchedulingResult(later);
        }
        finally { releasePrewarm.Set(); releaseOther.Set(); }
    }

    private static void CaptureSharedChunkDemandCancellation(JObject result)
    {
        using var firstSource = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancelled = new ManualResetEventSlim();
        var generator = new BackgroundSchedulingFixture();
        using var scheduler = new ChunkGenerationScheduler(generator, 2);
        int attempts = 0;
        generator.GenerateAction = (region, token) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                started.Set();
                try { AwaitBackgroundSchedulingEvent(release, token); }
                catch (OperationCanceledException) { cancelled.Set(); throw; }
            }
            generator.MarkReady(region);
        };
        Task<ChunkGenerationResult> first = scheduler.ScheduleAsync(BackgroundSchedulingRequest(4), firstSource.Token);
        try
        {
            RequireBackgroundScheduling(started.Wait(TimeSpan.FromSeconds(5)), "首个共享区块未开始");
            Task<ChunkGenerationResult> second = scheduler.ScheduleAsync(BackgroundSchedulingRequest(4));
            firstSource.Cancel();
            bool remained = !cancelled.Wait(TimeSpan.FromMilliseconds(120));
            release.Set();
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => first.IsCompleted && second.IsCompleted,
                TimeSpan.FromSeconds(5)), "共享区块取消后未完成");
            result["sharedDemandSurvivesOneCancellation"] = (bool)result["sharedDemandSurvivesOneCancellation"] &&
                remained && first.IsCanceled && second.Status == TaskStatus.RanToCompletion;
            ReleaseBackgroundSchedulingResult(first);
            ReleaseBackgroundSchedulingResult(second);
        }
        finally { release.Set(); }
    }

    private static void CaptureCompletedSourceWindowRemovalAndReentry(JObject result)
    {
        using var firstSource = new CancellationTokenSource();
        using var secondSource = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        using var cancelled = new ManualResetEventSlim();
        using var releaseCancelledWork = new ManualResetEventSlim();
        using var restarted = new ManualResetEventSlim();
        var generator = new BackgroundSchedulingFixture();
        using var scheduler = new ChunkGenerationScheduler(generator, 2);
        int secondRequests = 0;
        bool blockedNewPrewarm = false;
        generator.GenerateAction = (region, token) =>
        {
            if (region == 0)
                generator.QueuePrewarm(1, CancellationToken.None, prewarmToken =>
                {
                    started.Set();
                    try { prewarmToken.WaitHandle.WaitOne(); prewarmToken.ThrowIfCancellationRequested(); }
                    catch (OperationCanceledException)
                    {
                        cancelled.Set();
                        releaseCancelledWork.Wait(TimeSpan.FromSeconds(5));
                        throw;
                    }
                });
            else if (region == 2)
            {
                int attempt = Interlocked.Increment(ref secondRequests);
                generator.QueuePrewarm(1, CancellationToken.None, _ =>
                {
                    if (attempt > 1) restarted.Set();
                }, allowNewWork: attempt > 1);
                if (attempt == 1)
                    blockedNewPrewarm = !generator.TryQueuePrewarm(3, CancellationToken.None, _ => { }, false);
            }
            generator.MarkReady(region);
        };
        Task<ChunkGenerationResult> first = scheduler.ScheduleAsync(BackgroundSchedulingRequest(0), firstSource.Token);
        try
        {
            RequireBackgroundScheduling(started.Wait(TimeSpan.FromSeconds(5)), "窗口来源预热未开始");
            Task<ChunkGenerationResult> second = scheduler.ScheduleAsync(BackgroundSchedulingRequest(2), secondSource.Token);
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => first.IsCompleted && second.IsCompleted,
                TimeSpan.FromSeconds(5)), "窗口来源区块未结束");
            firstSource.Dispose();
            scheduler.CancelPrewarmDemandsForSource(BackgroundSchedulingRequest(0).Address);
            bool anotherSourceKeepsWork = !cancelled.Wait(TimeSpan.FromMilliseconds(120));
            secondSource.Dispose();
            scheduler.CancelPrewarmDemandsForSource(BackgroundSchedulingRequest(2).Address);
            bool allSourcesRemoved = cancelled.Wait(TimeSpan.FromSeconds(5));
            Task<ChunkGenerationResult> reentered = scheduler.ScheduleAsync(BackgroundSchedulingRequest(2));
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => reentered.IsCompleted, TimeSpan.FromSeconds(5)),
                "重新进入的区块未完成");
            releaseCancelledWork.Set();
            result["completedSourceLeavesWindowCancels"] = anotherSourceKeepsWork && allSourcesRemoved;
            result["reenteredSourceRestartsPrewarm"] = restarted.Wait(TimeSpan.FromSeconds(5));
            result["sharedDemandSurvivesOneCancellation"] = (bool)result["sharedDemandSurvivesOneCancellation"] &&
                anotherSourceKeepsWork && blockedNewPrewarm;
            ReleaseBackgroundSchedulingResult(first);
            ReleaseBackgroundSchedulingResult(second);
            ReleaseBackgroundSchedulingResult(reentered);
        }
        finally { releaseCancelledWork.Set(); }
    }

    private static void CaptureActiveChunkAcceptsPrewarmDemand(JObject result)
    {
        using var owner = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancelled = new ManualResetEventSlim();
        int duplicatePrewarm = 0;
        var generator = new BackgroundSchedulingFixture();
        using var scheduler = new ChunkGenerationScheduler(generator, 2);
        generator.GenerateAction = (region, token) =>
        {
            if (region == 1)
            {
                started.Set();
                try { AwaitBackgroundSchedulingEvent(release, token); }
                catch (OperationCanceledException) { cancelled.Set(); throw; }
            }
            else generator.QueuePrewarm(1, CancellationToken.None, _ => Interlocked.Increment(ref duplicatePrewarm));
            generator.MarkReady(region);
        };
        Task<ChunkGenerationResult> first = scheduler.ScheduleAsync(BackgroundSchedulingRequest(1), owner.Token);
        try
        {
            RequireBackgroundScheduling(started.Wait(TimeSpan.FromSeconds(5)), "正式区域计算未开始");
            Task<ChunkGenerationResult> source = scheduler.ScheduleAsync(BackgroundSchedulingRequest(0));
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => source.IsCompleted, TimeSpan.FromSeconds(5)),
                "预热来源区块未完成");
            owner.Cancel();
            bool retained = !cancelled.Wait(TimeSpan.FromMilliseconds(120));
            release.Set();
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => first.IsCompleted, TimeSpan.FromSeconds(5)),
                "正式区域计算未结束");
            result["sharedDemandSurvivesOneCancellation"] = (bool)result["sharedDemandSurvivesOneCancellation"] &&
                retained && first.IsCanceled && generator.IsReady(1) && duplicatePrewarm == 0;
            ReleaseBackgroundSchedulingResult(first);
            ReleaseBackgroundSchedulingResult(source);
        }
        finally { release.Set(); }
    }

    private static void CaptureQueuedPrewarmDemandAtCapacity(JObject result)
    {
        using var firstSource = new CancellationTokenSource();
        using var secondSource = new CancellationTokenSource();
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var secondAttached = new ManualResetEventSlim();
        using var releaseSecond = new ManualResetEventSlim();
        using var prewarmStarted = new ManualResetEventSlim();
        using var prewarmCancelled = new ManualResetEventSlim();
        bool acceptedExisting = false, blockedNew = false;
        var generator = new BackgroundSchedulingFixture();
        using var scheduler = new ChunkGenerationScheduler(generator, 1);
        generator.GenerateAction = (region, token) =>
        {
            if (region == 0)
            {
                generator.QueuePrewarm(1, CancellationToken.None, prewarmToken =>
                {
                    prewarmStarted.Set();
                    try { prewarmToken.WaitHandle.WaitOne(); prewarmToken.ThrowIfCancellationRequested(); }
                    catch (OperationCanceledException) { prewarmCancelled.Set(); throw; }
                });
                firstStarted.Set();
                AwaitBackgroundSchedulingEvent(releaseFirst, token);
            }
            else
            {
                acceptedExisting = generator.TryQueuePrewarm(1, CancellationToken.None, _ => { }, false);
                blockedNew = !generator.TryQueuePrewarm(3, CancellationToken.None, _ => { }, false);
                secondAttached.Set();
                AwaitBackgroundSchedulingEvent(releaseSecond, token);
            }
            generator.MarkReady(region);
        };
        Task<ChunkGenerationResult> first = scheduler.ScheduleAsync(BackgroundSchedulingRequest(0), firstSource.Token);
        try
        {
            RequireBackgroundScheduling(firstStarted.Wait(TimeSpan.FromSeconds(5)), "排队预热来源未开始");
            Task<ChunkGenerationResult> second = scheduler.ScheduleAsync(BackgroundSchedulingRequest(2), secondSource.Token);
            releaseFirst.Set();
            RequireBackgroundScheduling(secondAttached.Wait(TimeSpan.FromSeconds(5)), "满缓存场景未登记第二来源");
            firstSource.Cancel();
            bool retainedQueued = scheduler.QueuedCount == 1;
            releaseSecond.Set();
            bool started = prewarmStarted.Wait(TimeSpan.FromSeconds(5));
            secondSource.Cancel();
            bool stopped = prewarmCancelled.Wait(TimeSpan.FromSeconds(5));
            result["sharedDemandSurvivesOneCancellation"] = (bool)result["sharedDemandSurvivesOneCancellation"] &&
                acceptedExisting && blockedNew && retainedQueued && started && stopped;
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => first.IsCompleted && second.IsCompleted,
                TimeSpan.FromSeconds(5)), "满缓存场景的正式任务未结束");
            ReleaseBackgroundSchedulingResult(first);
            ReleaseBackgroundSchedulingResult(second);
        }
        finally { releaseFirst.Set(); releaseSecond.Set(); }
    }

    private static void CapturePrewarmLifetimeAndFailureRecovery(JObject result)
    {
        using var source = new CancellationTokenSource();
        using var lifetime = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        using var cancelled = new ManualResetEventSlim();
        var generator = new BackgroundSchedulingFixture();
        using (var scheduler = new ChunkGenerationScheduler(generator, 1))
        {
            generator.GenerateAction = (region, token) =>
            {
                generator.QueuePrewarm(1, lifetime.Token, prewarmToken =>
                {
                    started.Set();
                    try { prewarmToken.WaitHandle.WaitOne(); prewarmToken.ThrowIfCancellationRequested(); }
                    catch (OperationCanceledException) { cancelled.Set(); throw; }
                });
                generator.MarkReady(region);
            };
            Task<ChunkGenerationResult> seed = scheduler.ScheduleAsync(BackgroundSchedulingRequest(0), source.Token);
            RequireBackgroundScheduling(started.Wait(TimeSpan.FromSeconds(5)), "取消场景预热未开始");
            source.Cancel();
            result["noDemandCancels"] = cancelled.Wait(TimeSpan.FromSeconds(5));
            ReleaseBackgroundSchedulingResult(seed);
        }

        using var world = new CancellationTokenSource();
        using var worldStarted = new ManualResetEventSlim();
        using var worldCancelled = new ManualResetEventSlim();
        int queuedRuns = 0;
        generator = new BackgroundSchedulingFixture();
        using (var scheduler = new ChunkGenerationScheduler(generator, 1))
        {
            generator.GenerateAction = (region, token) =>
            {
                generator.QueuePrewarm(1, world.Token, prewarmToken =>
                {
                    worldStarted.Set();
                    try { prewarmToken.WaitHandle.WaitOne(); prewarmToken.ThrowIfCancellationRequested(); }
                    catch (OperationCanceledException) { worldCancelled.Set(); throw; }
                });
                generator.QueuePrewarm(2, world.Token, _ => Interlocked.Increment(ref queuedRuns));
                generator.MarkReady(region);
            };
            Task<ChunkGenerationResult> seed = scheduler.ScheduleAsync(BackgroundSchedulingRequest(0));
            RequireBackgroundScheduling(worldStarted.Wait(TimeSpan.FromSeconds(5)), "世界退出场景预热未开始");
            RequireBackgroundScheduling(scheduler.QueuedCount == 1, "世界退出场景缺少排队预热");
            world.Cancel();
            result["worldExitCancels"] = worldCancelled.Wait(TimeSpan.FromSeconds(5));
            result["queuedWorldExitRemoved"] = SpinWait.SpinUntil(() => scheduler.QueuedCount == 0 && scheduler.ActiveCount == 0,
                TimeSpan.FromSeconds(5)) && queuedRuns == 0;
            ReleaseBackgroundSchedulingResult(seed);
        }

        using var failed = new ManualResetEventSlim();
        generator = new BackgroundSchedulingFixture();
        using (var scheduler = new ChunkGenerationScheduler(generator, 1))
        {
            int attempts = 0;
            generator.GenerateAction = (region, token) =>
            {
                if (region == 0)
                    generator.QueuePrewarm(1, CancellationToken.None, _ => { failed.Set(); throw new InvalidOperationException("模拟预热失败"); });
                else if (region == 1 && !generator.IsReady(region)) Interlocked.Increment(ref attempts);
                generator.MarkReady(region);
            };
            Task<ChunkGenerationResult> seed = scheduler.ScheduleAsync(BackgroundSchedulingRequest(0));
            RequireBackgroundScheduling(failed.Wait(TimeSpan.FromSeconds(5)), "失败场景预热未开始");
            Task<ChunkGenerationResult> retry = scheduler.ScheduleAsync(BackgroundSchedulingRequest(1));
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => retry.IsCompleted, TimeSpan.FromSeconds(5)), "失败后的正式任务未恢复");
            result["failedPrewarmCanRetry"] = attempts == 1 && retry.Status == TaskStatus.RanToCompletion;
            ReleaseBackgroundSchedulingResult(seed);
            ReleaseBackgroundSchedulingResult(retry);
        }
    }

    private static readonly ChunkGenerationProfileSnapshot backgroundSchedulingProfile =
        new ChunkGenerationProfileSnapshot("background-scheduling-probe", 1, 1, 1);

    private static ChunkGenerationRequest BackgroundSchedulingRequest(int region) =>
        new ChunkGenerationRequest(1, new RuntimeWorldAddress("probe", new Int2(region, 0)), 1, 1,
            backgroundSchedulingProfile, default);

    private static void AwaitBackgroundSchedulingEvent(ManualResetEventSlim wait, CancellationToken token)
    {
        if (!wait.Wait(TimeSpan.FromSeconds(5), token))
            throw new InvalidOperationException("后台调度采样等待超时");
    }

    private static void RequireBackgroundScheduling(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ReleaseBackgroundSchedulingResult(Task<ChunkGenerationResult> task)
    {
        if (task.Status == TaskStatus.RanToCompletion) task.Result.Dispose();
        else if (task.IsFaulted) _ = task.Exception;
    }

    private sealed class BackgroundSchedulingFixture : IChunkPureGenerator
    {
        private readonly ConcurrentDictionary<int, bool> ready = new ConcurrentDictionary<int, bool>();
        private int active;
        private int maximumActive;
        public Action<int, CancellationToken> GenerateAction;
        public int MaximumActive => Volatile.Read(ref maximumActive);
        public bool IsReady(int region) => ready.ContainsKey(region);
        public void MarkReady(int region) => ready[region] = true;
        public object GetPendingGenerationGroupKey(ChunkGenerationRequest request) =>
            IsReady(request.Address.ChunkOrigin.X) ? null : (object)request.Address.ChunkOrigin.X;
        public ChunkGenerationResult Generate(ChunkGenerationRequest request, CancellationToken cancellationToken,
            ChunkGenerationTiming timing = null)
        {
            Enter();
            try
            {
                GenerateAction(request.Address.ChunkOrigin.X, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return new ChunkGenerationResult(request, new ChunkTerrainBuffer(1, 1));
            }
            finally { Interlocked.Decrement(ref active); }
        }
        public void QueuePrewarm(int region, CancellationToken lifetime, Action<CancellationToken> execute,
            bool allowNewWork = true)
        {
            if (!TryQueuePrewarm(region, lifetime, execute, allowNewWork))
                throw new InvalidOperationException("正式生成线程未能提交预热");
        }
        public bool TryQueuePrewarm(int region, CancellationToken lifetime, Action<CancellationToken> execute,
            bool allowNewWork)
        {
            return ChunkGenerationScheduler.TryQueuePrewarmForCurrentGeneration(region, lifetime, token =>
                {
                    Enter();
                    try { execute(token); }
                    finally { Interlocked.Decrement(ref active); }
                }, allowNewWork);
        }
        private void Enter()
        {
            int count = Interlocked.Increment(ref active);
            int previous;
            do
            {
                previous = Volatile.Read(ref maximumActive);
                if (previous >= count) return;
            } while (Interlocked.CompareExchange(ref maximumActive, count, previous) != previous);
        }
    }

    #endregion
}
