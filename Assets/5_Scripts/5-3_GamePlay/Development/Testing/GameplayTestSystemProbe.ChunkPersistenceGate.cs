using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using PureChunkMgr = FlatWorld.WorldModel.ChunkMgr;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public static partial class GameplayTestSystemProbe
{
    #region 区块持久化提交与共享需求采样

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RegisterChunkPersistenceGateRules() => Register(
        "world_streaming", "chunk_persistence_gate", CaptureChunkPersistenceGate);

    // 只控制真实生成器与持久化任务的完成顺序，不复制提交规则。
    private static JObject CaptureChunkPersistenceGate(JObject arguments)
    {
        var result = new JObject();
        var generator = new BackgroundSchedulingFixture();
        generator.GenerateAction = (region, _) => generator.MarkReady(region);
        using (var world = new WorldRuntime("probe-world", 1))
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var abandoned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var firstCancellation = new CancellationTokenSource();
            using var abandonedCancellation = new CancellationTokenSource();
            using var manager = new PureChunkMgr(world, generator, 1, null, (address, token) =>
            {
                TaskCompletionSource<bool> selected = address.ChunkOrigin.X == 0 ? gate : abandoned;
                token.Register(() => selected.TrySetCanceled());
                return selected.Task;
            });
            var firstAddress = new RuntimeWorldAddress("probe", new Int2(0, 0));
            Task<ChunkRuntime> first = manager.RequestChunkDataAsync(firstAddress, 1, backgroundSchedulingProfile, firstCancellation.Token);
            Task<ChunkRuntime> second = manager.RequestChunkDataAsync(firstAddress, 1, backgroundSchedulingProfile);
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => generator.IsReady(0), TimeSpan.FromSeconds(5)), "门禁测试生成未结束");
            manager.CommitCompleted();
            result["notCommittedBeforeRead"] = !first.IsCompleted && !second.IsCompleted &&
                world.TryGetChunk(firstAddress, out ChunkRuntime pending) && pending.DataStatus != ChunkDataStatus.Ready;
            firstCancellation.Cancel();
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => first.IsCanceled, TimeSpan.FromSeconds(5)), "单个等待者未取消");
            manager.CommitCompleted();
            result["sharedReadSurvivesCancellation"] = !second.IsCompleted && !gate.Task.IsCanceled;
            gate.TrySetResult(true);
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => { manager.CommitCompleted(); return second.IsCompleted; },
                TimeSpan.FromSeconds(5)), "持久化完成后没有提交区块");
            result["committedAfterRead"] = second.Status == TaskStatus.RanToCompletion && second.Result.DataStatus == ChunkDataStatus.Ready;

            var abandonedAddress = new RuntimeWorldAddress("probe", new Int2(2, 0));
            Task<ChunkRuntime> cancelled = manager.RequestChunkDataAsync(abandonedAddress, 1, backgroundSchedulingProfile, abandonedCancellation.Token);
            abandonedCancellation.Cancel();
            RequireBackgroundScheduling(SpinWait.SpinUntil(() =>
            {
                manager.CommitCompleted();
                return cancelled.IsCanceled && (!world.TryGetChunk(abandonedAddress, out ChunkRuntime chunk) ||
                    chunk.DataStatus != ChunkDataStatus.Generating);
            }, TimeSpan.FromSeconds(5)), "无需求区块未取消");
            result["lastWaiterCancelsWork"] = cancelled.IsCanceled &&
                (!world.TryGetChunk(abandonedAddress, out ChunkRuntime stopped) || stopped.DataStatus != ChunkDataStatus.Ready);
        }

        using (var world = new WorldRuntime("priority-world", 1))
        {
            var firstRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var remainingRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var order = new List<int>();
            var fixture = new BackgroundSchedulingFixture();
            fixture.GenerateAction = (region, _) => fixture.MarkReady(region);
            using var manager = new PureChunkMgr(world, fixture, 1, null, (address, token) =>
            {
                int x = address.ChunkOrigin.X;
                order.Add(x);
                TaskCompletionSource<bool> selected = x == 10 ? firstRead : remainingRead;
                token.Register(() => selected.TrySetCanceled());
                return selected.Task;
            });
            _ = manager.RequestChunkDataAsync(new RuntimeWorldAddress("probe", new Int2(10, 0)), 1, backgroundSchedulingProfile);
            _ = manager.RequestChunkDataAsync(new RuntimeWorldAddress("probe", new Int2(11, 0)), 1, backgroundSchedulingProfile);
            manager.RefreshWindow(new ChunkWindowRequest(new RuntimeWorldAddress("probe", new Int2(0, 0)),
                1, 1, false, 1, backgroundSchedulingProfile));
            firstRead.TrySetResult(true);
            RequireBackgroundScheduling(SpinWait.SpinUntil(() => { manager.CommitCompleted(); return order.Count >= 2; },
                TimeSpan.FromSeconds(5)), "窗口优先读盘未启动");
            result["visibleBeforeQueuedPrefetch"] = order[0] == 10 && order[1] == 0;
            remainingRead.TrySetResult(true);
        }
        return result;
    }

    #endregion
}
