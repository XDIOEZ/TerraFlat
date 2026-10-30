#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RuntimeAddress = FlatWorld.WorldModel.WorldAddress;

public static class GameplayTestChunkStreamingOptimizationRuntimeProbe
{
    #region 真实区块窗口移动与折返

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register() => GameplayTestRuntimeProbe.RegisterAsync(
        "world_streaming", "incremental_window_roundtrip", CaptureIncrementalWindowRoundtrip);

    // 临时接管隔离玩家的正式窗口，核对边界增量、折返复用和稳定后的待办清空。
    private static IEnumerator CaptureIncrementalWindowRoundtrip(Player player, JObject args,
        Action<JObject> completed)
    {
        var fixture = new StreamingWindowFixture(player);
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        GameplayTestRuntimeProbe.RegisterAsyncCleanup(fixture.RestoreAndWait);
        using (fixture)
        {
            ChunkMgr manager = fixture.Manager;
            Vector2 home = fixture.Center;
            int width = manager.ActiveGenerationProfile.Width;
            Vector2 away = home + Vector2.right * width;
            fixture.Refresh(home);
            yield return WaitForStreamingWindowSettled(manager);
            RuntimeAddress homeAddress = manager.ResolveWorldAddress(home);
            manager.TryGetChunkRuntime(homeAddress, out ChunkRuntime originalChunk);
            manager.TryGetRuntimeChunkView(homeAddress, out ChunkView originalView);

            manager.RetargetRuntimePresentationQueue(away, new Vector2Int(2, 2));
            fixture.Refresh(home);
            yield return WaitForStreamingWindowSettled(manager);
            bool retargetReturnViewsReady = IsPresentationWindowBound(manager, home, 2);

            fixture.Refresh(away);
            long forwardVisits = manager.RuntimeWindowLastVisitedCells;
            yield return null;
            fixture.Refresh(home);
            long immediateReturnVisits = manager.RuntimeWindowLastVisitedCells;
            yield return WaitForStreamingWindowSettled(manager);
            manager.TryGetRuntimeChunkView(homeAddress, out ChunkView foldedView);

            fixture.Refresh(away);
            yield return WaitForStreamingWindowSettled(manager);
            RuntimeAddress awayAddress = manager.ResolveWorldAddress(away);
            manager.TryGetChunkRuntime(awayAddress, out ChunkRuntime awayChunk);
            manager.TryGetRuntimeChunkView(awayAddress, out ChunkView awayView);
            bool awayReady = awayChunk?.DataStatus == ChunkDataStatus.Ready && awayView?.IsBound == true;
            bool awayActive = awayChunk?.SimulationLeaseCount > 0;

            fixture.Refresh(home);
            yield return WaitForStreamingWindowSettled(manager);
            manager.TryGetChunkRuntime(homeAddress, out ChunkRuntime returnedChunk);
            manager.TryGetRuntimeChunkView(homeAddress, out ChunkView returnedView);
            long beforeIdle = manager.RuntimeBindingReconciliationAttempts;
            for (int frame = 0; frame < 8; frame++) yield return null;
            var result = new JObject
            {
                ["forwardVisitedCells"] = forwardVisits,
                ["immediateReturnVisitedCells"] = immediateReturnVisits,
                ["foldedViewReused"] = originalView != null && ReferenceEquals(originalView, foldedView),
                ["retargetReturnViewsReady"] = retargetReturnViewsReady,
                ["returnedViewReused"] = originalView != null && ReferenceEquals(originalView, returnedView),
                ["returnedDataReused"] = originalChunk != null && ReferenceEquals(originalChunk, returnedChunk),
                ["awayReady"] = awayReady,
                ["awayActive"] = awayActive,
                ["homeActive"] = returnedChunk?.SimulationLeaseCount > 0,
                ["homePresentationLease"] = returnedChunk?.PresentationLeaseCount > 0,
                ["pendingBindings"] = manager.PendingRuntimeBindingReconciliationCount,
                ["pendingPresentations"] = manager.PendingRuntimeChunkPresentationCount,
                ["idleReconciliationAttempts"] = manager.RuntimeBindingReconciliationAttempts - beforeIdle,
                ["worldPreserved"] = GameplayWorldRegistry.ExecutionWorldKey == fixture.WorldKey,
                ["playerPreserved"] = ReferenceEquals(player, ItemMgr.Instance.User_Player)
            };
            yield return fixture.RestoreAndWait();
            result["restoredWindowReady"] = manager.AreRuntimeWindowPresentationsReady;
            completed(result);
        }
    }

    private static IEnumerator WaitForStreamingWindowSettled(ChunkMgr manager)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 120d;
        while (!manager.AreRuntimeWindowPresentationsReady || manager.PendingRuntimeChunkPresentationCount > 0 ||
               manager.PendingRuntimeBindingReconciliationCount > 0)
        {
            if (Time.realtimeSinceStartupAsDouble >= deadline)
                throw new TimeoutException("正式区块窗口没有完成数据恢复与分帧表现：" +
                    manager.DescribeRuntimeEntityPresentationWait(ItemMgr.Instance.User_Player.transform.position));
            yield return null;
        }
    }

    private static bool IsPresentationWindowBound(ChunkMgr manager, Vector2 center, int distance)
    {
        int width = manager.ActiveGenerationProfile.Width, height = manager.ActiveGenerationProfile.Height;
        for (int x = 1 - distance; x < distance; x++)
            for (int y = 1 - distance; y < distance; y++)
                if (!manager.TryGetRuntimeChunkView(manager.ResolveWorldAddress(
                        center + new Vector2(x * width, y * height)), out ChunkView view) || !view.IsBound)
                    return false;
        return true;
    }

    private sealed class StreamingWindowFixture : IDisposable
    {
        internal readonly ChunkMgr Manager;
        internal readonly Vector2 Center;
        internal readonly string WorldKey;
        private readonly Mod_ChunkLoader loader;
        private bool restored;

        internal StreamingWindowFixture(Player player)
        {
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            Manager = ChunkMgr.ExistingInstance;
            loader = player.GetComponentInChildren<Mod_ChunkLoader>(true);
            if (Manager == null || loader == null || !ChunkMgr.IsSurfaceStreamingScene)
                throw new InvalidOperationException("窗口移动回归需要正式隔离地表与玩家区块加载器。");
            WorldKey = GameplayWorldRegistry.ExecutionWorldKey;
            Center = WorldLocalPresentation.ToLogical(player.transform.position);
            loader.SetExternalStreamingManaged(true);
        }

        internal void Refresh(Vector2 center) => Manager.RefreshRuntimeWindow(center,
            activeDistance: 1, destroyDistance: 4, includeLocalPresentation: true,
            prefetchDistance: 3, presentationDistance: 2);

        public IEnumerator RestoreAndWait()
        {
            Dispose();
            yield return WaitForStreamingWindowSettled(Manager);
        }

        public void Dispose()
        {
            if (restored) return;
            restored = true;
            using (GameplayWorldRegistry.UseWorld(WorldKey))
            {
                loader.SetExternalStreamingManaged(false);
                loader.RefreshChunksAroundPlayer();
            }
        }
    }

    #endregion
}
#endif
