#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using FlatWorld.NaturalEntities;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeAddress = FlatWorld.WorldModel.WorldAddress;

public static partial class GameplayTestWorldScopeRuntimeProbe
{
    #region 分帧世界归属现场

    // 活动场景保留地表，实际洞穴窗口与自有实体共同经过正式管理器的跨帧队列。
    private static IEnumerator CaptureIncrementalWorldScope(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        Scene initialScene = SceneManager.GetActiveScene();
        string initialWorld = GameplayWorldRegistry.ExecutionWorldKey;
        bool initialContext = GameplayWorldRegistry.HasExecutionContext;
        global::WorldAddress sourceAddress = global::WorldAddress.FromWorldKey(player.Data.CurrentSceneName);
        if (!sourceAddress.IsSurface || initialWorld != sourceAddress.WorldKey)
            throw new InvalidOperationException("分帧世界归属回归必须从本轮隔离地表启动。");
        string targetWorld = sourceAddress.WithDimension(global::WorldAddress.CaveDimensionId).WorldKey;
        Vector3 point = WorldLocalPresentation.ToLogical(player.transform.position);
        GameplayWorldRegistry.GetOrCreate(initialWorld);
        var own = new Fixture();
        GameplayTestRuntimeProbe.RegisterCleanup(own);
        var result = new JObject { ["sourceWorld"] = initialWorld, ["targetWorld"] = targetWorld };
        bool contextRestored = true, scenePreserved = true;
        void SampleBoundary()
        {
            contextRestored &= GameplayWorldRegistry.ExecutionWorldKey == initialWorld &&
                GameplayWorldRegistry.HasExecutionContext == initialContext;
            scenePreserved &= SceneManager.GetActiveScene() == initialScene;
        }
        using (own)
        {
            object sourceEcs;
            using (GameplayWorldRegistry.UseWorld(initialWorld))
            {
                RegisterScopeResource(own, point + Vector3.up * 128f, sourceAddress.DimensionId);
                sourceEcs = WorldEntityRuntime.Current;
            }
            yield return GameplayWorldRegistry.PrepareWorld(targetWorld, point);
            GameplayWorldRuntime target = GameplayWorldRegistry.GetOrCreate(targetWorld);
            double deadline = Time.realtimeSinceStartupAsDouble + 90d;
            while (!target.Chunks.AreRuntimeWindowPresentationsReady || target.Chunks.PendingRuntimeChunkPresentationCount > 0)
            {
                SampleBoundary();
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    throw new TimeoutException("活动场景仍在地表时，洞穴真实窗口没有完成分帧表现绑定。");
                yield return null;
            }
            using (GameplayWorldRegistry.UseWorld(targetWorld))
            {
                RuntimeAddress address = target.Chunks.ResolveWorldAddress(point);
                bool found = target.Chunks.TryGetRuntimeChunkView(address, out ChunkView view);
                result["targetWindowReady"] = target.Chunks.AreRuntimeWindowPresentationsReady;
                result["targetViewBound"] = found && view != null && view.IsBound;
                result["targetViewWorld"] = found ? view.gameObject.scene.name : string.Empty;
            }
            foreach (bool cancel in new[] { false, true })
            {
                var routine = new ScopeResourceRoutine(own, target, sourceEcs, point + Vector3.up * 128f);
                using var lease = target.Chunks.CreateTestWorldBindingContinuation(player, routine);
                GameplayTestRuntimeProbe.RegisterCleanup(lease);
                var sample = new JObject { ["stepsBeforeYield"] = routine.Steps.Count };
                if (!cancel)
                {
                    foreach (string owner in new[] { "lua:unregistered-" + Guid.NewGuid().ToString("N"), initialWorld })
                    {
                        JObject rejected = lease.CaptureRejectedOwner(owner);
                        rejected["stepsAfter"] = routine.Steps.Count;
                        sample[owner == initialWorld ? "replacedOwner" : "missingOwner"] = rejected;
                    }
                }
                deadline = Time.realtimeSinceStartupAsDouble + 30d;
                while (!lease.IsFinished)
                {
                    SampleBoundary();
                    if (cancel && routine.Steps.Count > 0) lease.Cancel();
                    if (Time.realtimeSinceStartupAsDouble >= deadline)
                        throw new TimeoutException("自有分帧世界归属记录器没有按正式队列完成或取消。");
                    yield return null;
                }
                SampleBoundary();
                sample["steps"] = routine.Steps;
                sample["disposeWorld"] = routine.DisposeWorld;
                sample["disposeCount"] = routine.DisposeCount;
                sample["error"] = routine.Error;
                result[cancel ? "cancelled" : "completed"] = sample;
            }
            result["contextRestored"] = contextRestored;
            result["scenePreserved"] = scenePreserved && SceneManager.GetActiveScene() == initialScene;
            result["localPlayerPreserved"] = ReferenceEquals(player, ItemMgr.Instance.User_Player);
            own.Dispose();
            result["resourcesRemoved"] = own.Resources.TrueForAll(handle => !NaturalEntityEcsService.Contains(handle));
            completed(result);
        }
    }

    private static NaturalEntityHandle RegisterScopeResource(Fixture own, Vector3 point, string dimension)
    {
        RuntimeItemDefinition definition = GameRes.Instance.ItemDefinitions["Tree_Pine"];
        if (!NaturalEntityEcsService.TryRegister(definition, ItemMgr.Instance.GenerateGuid(), point,
                26f, 0f, dimension, null, out NaturalEntityHandle handle))
            throw new InvalidOperationException("自有分帧自然实体没有经过正式注册入口。");
        own.Resources.Add(handle);
        return handle;
    }

    private sealed class ScopeResourceRoutine : IEnumerator, IDisposable
    {
        private readonly Fixture own;
        private readonly GameplayWorldRuntime target;
        private readonly object sourceEcs;
        private readonly Vector3 point;
        public readonly JArray Steps = new();
        public string DisposeWorld = string.Empty, Error = string.Empty;
        public int DisposeCount;
        public object Current => null;
        public ScopeResourceRoutine(Fixture own, GameplayWorldRuntime target, object sourceEcs, Vector3 point)
        { this.own = own; this.target = target; this.sourceEcs = sourceEcs; this.point = point; }
        public bool MoveNext()
        {
            if (Steps.Count == 2) return false;
            try
            {
                RegisterScopeResource(own, point + Vector3.right * Steps.Count,
                    global::WorldAddress.FromWorldKey(target.WorldKey).DimensionId);
                Steps.Add(new JObject
                {
                    ["world"] = GameplayWorldRegistry.ExecutionWorldKey,
                    ["scene"] = GameplayWorldRegistry.CurrentScene.name,
                    ["managerMatches"] = ReferenceEquals(GameplayWorldRegistry.ExecutionChunks, target.Chunks),
                    ["ecsSeparate"] = !ReferenceEquals(sourceEcs, WorldEntityRuntime.Current),
                    ["ecsDimension"] = WorldEntityRuntime.DimensionId,
                    ["frame"] = Time.frameCount
                });
                return true;
            }
            catch (Exception error) { Error = error.ToString(); throw; }
        }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { DisposeCount++; DisposeWorld = GameplayWorldRegistry.ExecutionWorldKey; }
    }

    #endregion
}

public partial class ChunkMgr
{
    #region 隔离回归的自有续步租约

    // 只向正式队列放入自有枚举器，世界选择、逐帧恢复及取消仍由生产管理器执行。
    internal TestWorldBindingContinuation CreateTestWorldBindingContinuation(Player player, IEnumerator routine)
        => new(this, player, routine);

    internal sealed class TestWorldBindingContinuation : IDisposable
    {
        private readonly ChunkMgr owner;
        private readonly RuntimeAddress address;
        private readonly RuntimeChunkBinding binding;
        private readonly ChunkView view;
        private bool disposed;
        internal TestWorldBindingContinuation(ChunkMgr owner, Player player, IEnumerator routine)
        {
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            this.owner = owner;
            if (owner.WorldRuntime == null || !GameplayWorldRegistry.TryGet(owner.OwnedWorldKey, out GameplayWorldRuntime world) ||
                !ReferenceEquals(world.Chunks, owner)) throw new InvalidOperationException("测试续步必须属于正式注册的隔离世界管理器。");
            using var scope = GameplayWorldRegistry.UseWorld(owner.OwnedWorldKey);
            ChunkView prefab = DimensionManager.Instance.GetActiveChunkViewPrefab();
            if (prefab == null) throw new InvalidOperationException("测试续步缺少正式 ChunkView Prefab。");
            view = UnityEngine.Object.Instantiate(prefab);
            view.gameObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(view.gameObject, world.Scene);
            int origin = int.MinValue / 2;
            do { address = new RuntimeAddress(ResolveCurrentDimensionId(), new FlatWorld.WorldModel.Int2(origin++, origin)); }
            while (owner.activeRuntimeBindings.ContainsKey(address) || owner.Chunks.ContainsKey(address));
            binding = new RuntimeChunkBinding { View = view, WantsPresentation = true,
                PresentationInProgress = true, PresentationRoutine = routine };
            owner.activeRuntimeBindings.Add(address, binding);
            owner.runtimePresentationInProgressCount++;
            owner.runtimePresentationContinuationQueue.Add(address);
            if (owner.runtimePresentationCoroutine == null)
                owner.runtimePresentationCoroutine = owner.StartCoroutine(owner.ProcessRuntimePresentationQueue());
        }
        public bool IsFinished => !binding.PresentationInProgress;
        public void Cancel() => binding.WantsPresentation = false;

        public JObject CaptureRejectedOwner(string key)
        {
            string previous = owner.OwnedWorldKey, context = GameplayWorldRegistry.ExecutionWorldKey;
            int worlds = GameplayWorldRegistry.Worlds.Count;
            bool frameAdvanced = false, disposeRejected = false;
            owner.OwnedWorldKey = key;
            try
            {
                frameAdvanced = owner.ProcessRuntimePresentationFrame();
                try { owner.FinishRuntimeChunkPresentation(binding); }
                catch (InvalidOperationException) { disposeRejected = true; }
            }
            finally { owner.OwnedWorldKey = previous; }
            return new JObject
            {
                ["frameAdvanced"] = frameAdvanced, ["disposeRejected"] = disposeRejected,
                ["worldCountBefore"] = worlds, ["worldCountAfter"] = GameplayWorldRegistry.Worlds.Count,
                ["contextPreserved"] = GameplayWorldRegistry.ExecutionWorldKey == context,
                ["routineRetained"] = binding.PresentationRoutine != null && binding.PresentationInProgress
            };
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.FinishRuntimeChunkPresentation(binding);
            owner.runtimePresentationContinuationQueue.Remove(address);
            owner.runtimePresentationRescheduleBuffer.Remove(address);
            owner.activeRuntimeBindings.Remove(address);
            if (view != null) UnityEngine.Object.Destroy(view.gameObject);
        }
    }

    #endregion
}
#endif
