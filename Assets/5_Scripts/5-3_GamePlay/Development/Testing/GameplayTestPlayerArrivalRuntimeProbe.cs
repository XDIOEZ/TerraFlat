using System;
using System.Collections;
using FlatWorld.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class GameplayTestPlayerArrivalRuntimeProbe
{
    #region 正式地表到达回归

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register() => GameplayTestRuntimeProbe.RegisterAsync("player", "surface_arrival", Capture);

    // 在隔离世界走真实落点、窗口恢复和同世界呈现入口，断言交给 Lua。
    private static IEnumerator Capture(Player player, JObject args, Action<JObject> completed)
    {
        if (GameNetwork.IsOnline) throw new InvalidOperationException("地表到达回归需要隔离离线世界。");
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        Mod_Mover mover = player.GetComponentInChildren<Mod_Mover>(true);
        Mod_Cam camera = player.GetComponentInChildren<Mod_Cam>(true);
        Mod_ChunkLoader loader = player.GetComponentInChildren<Mod_ChunkLoader>(true);
        Mod_GameController controller = player.GetComponentInChildren<Mod_GameController>(true);
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (chunks?.ActiveGenerationProfile == null || mover == null || camera?.ControllerCamera == null ||
            loader == null || controller == null || body == null || camera.HasFollowOverride)
            throw new InvalidOperationException("地表到达回归缺少正式玩家、相机或区块窗口。");
        var scope = new ArrivalScope(player, mover, body, controller);
        GameplayTestRuntimeProbe.RegisterCleanup(scope);
        GameplayTestRuntimeProbe.RegisterAsyncCleanup(scope.Restore);
        controller.AcquireGameplayInputLock(scope);
        mover.enabled = false;
        body.simulated = false;
        body.velocity = new Vector2(3f, -2f);
        body.angularVelocity = 20f;
        Vector2Int distance = loader.CurrentLoadChunkDistanceXY;
        Vector3 destination = WorldTopologyRuntime.NormalizePosition(scope.Original +
            new Vector3(chunks.ActiveGenerationProfile.Width * (distance.x * 2 + 4), 0f));
        Vector3 before = player.transform.position;
        Vector3 cameraBefore = camera.ControllerCamera.transform.position;
        int motionResets = 0;
        void OnReset() => motionResets++;
        mover.MotionReset += OnReset;
        try { PlayerTeleportService.ApplySurfacePose(player, destination); }
        finally { mover.MotionReset -= OnReset; }
        Vector3 expected = WorldLocalPresentation.ProjectPosition(destination);
        var result = new JObject
        {
            ["poseMatches"] = Vector2.Distance(body.position, expected) < .001f &&
                Vector3.Distance(player.transform.position, expected) < .001f &&
                Vector3.Distance(player.Data.transform.position, destination) < .001f,
            ["cameraShiftError"] = ((camera.ControllerCamera.transform.position - cameraBefore) -
                (player.transform.position - before)).magnitude,
            ["velocity"] = body.velocity.magnitude, ["angularVelocity"] = body.angularVelocity,
            ["motionResets"] = motionResets, ["disabledMoverPreserved"] = !mover.enabled,
            ["requestedAfterPose"] = chunks.HasRuntimeArrivalWindow(destination, distance)
        };
        int invalidRejected = 0;
        foreach (Vector3 invalid in new[] { new Vector3(float.NaN, 0f), new Vector3(0f, float.PositiveInfinity),
                     new Vector3(0f, 0f, float.NegativeInfinity) })
        {
            try { PlayerTeleportService.ApplySurfacePose(player, invalid); }
            catch (ArgumentOutOfRangeException) { invalidRejected++; }
        }
        result["invalidRejected"] = invalidRejected;
        result["invalidKeptPose"] = Vector3.Distance(player.transform.position, expected) < .001f;

        chunks.RefreshChunksAroundObservers(new[] { scope.Original }, scope.Original,
            distance.x, distance.x + 1, distance.x + 2);
        result["oldWindowRejected"] = !PlayerTeleportService.IsSurfaceArrivalReady(player, destination);
        chunks.RefreshChunksAroundObservers(new[] { destination }, destination, 1, 2, 3);
        result["undersizedWindowRejected"] = !chunks.HasRuntimeArrivalWindow(destination, new Vector2Int(2, 2));

        double deadline = Time.realtimeSinceStartupAsDouble + 60d;
        IEnumerator arrival = PlayerTeleportService.WaitForSurfaceArrival(player, destination,
            stillValid: () => Time.realtimeSinceStartupAsDouble < deadline);
        try
        {
            if (!arrival.MoveNext()) throw new InvalidOperationException("到达等待缺少跨帧屏障。");
            // 在正式请求之后撤销窗口，检查等待事务能自行补发而不是依赖下一步移动。
            chunks.RefreshChunksAroundObservers(new[] { destination }, destination, 1, 2, 3);
            chunks.ClearRuntimeObserverWindows();
            result["emptyQueueWasReady"] = chunks.AreRuntimeWindowPresentationsReady;
            result["emptyWindowRejected"] = !PlayerTeleportService.IsSurfaceArrivalReady(player, destination);
            do { yield return arrival.Current; } while (arrival.MoveNext());
        }
        finally { (arrival as IDisposable)?.Dispose(); }
        result["recoveredWithoutMovement"] = PlayerTeleportService.IsSurfaceArrivalReady(player, destination) &&
            Vector3.Distance(player.transform.position, expected) < .001f;

        body.simulated = scope.WasSimulated;
        mover.enabled = scope.WasEnabled;
        controller.ReleaseGameplayInputLock(scope);
        int readySignals = 0;
        bool readyAtSignal = true;
        void OnReady(Player actor, string world)
        {
            if (actor != player) return;
            readySignals++;
            readyAtSignal &= world == player.Data.CurrentSceneName &&
                PlayerTeleportService.IsSurfaceArrivalReady(player, scope.Original);
        }
        NetworkDimensionBridge.PresentationReady += OnReady;
        IEnumerator presentation = DimensionManager.Instance.ApplyNetworkPresentation(player,
            WorldAddress.FromWorldKey(player.Data.CurrentSceneName), scope.Original);
        try
        {
            bool running = presentation.MoveNext();
            result["lockedBeforePresentation"] = running && controller.IsGameplayInputLocked && !body.simulated;
            while (running)
            {
                yield return presentation.Current;
                running = presentation.MoveNext();
            }
        }
        finally
        {
            (presentation as IDisposable)?.Dispose();
            NetworkDimensionBridge.PresentationReady -= OnReady;
        }
        result["readySignals"] = readySignals;
        result["readyAtSignal"] = readyAtSignal;
        result["readyAfterPresentation"] = PlayerTeleportService.IsSurfaceArrivalReady(player, scope.Original);
        result["physicsRestored"] = body.simulated == scope.WasSimulated;
        completed(result);
    }

    #endregion

    #region 现场归还

    private sealed class ArrivalScope : IDisposable
    {
        private readonly Player player;
        private readonly Mod_Mover mover;
        private readonly Rigidbody2D body;
        private readonly Mod_GameController controller;
        private readonly bool wasRunning;
        public readonly Vector3 Original;
        public readonly bool WasSimulated, WasEnabled;

        public ArrivalScope(Player player, Mod_Mover mover, Rigidbody2D body, Mod_GameController controller)
        {
            this.player = player; this.mover = mover; this.body = body; this.controller = controller;
            Original = WorldTopologyRuntime.NormalizePosition(player.transform.position);
            WasSimulated = body.simulated; WasEnabled = mover.enabled; wasRunning = mover.RunPreference;
        }

        public IEnumerator Restore()
        {
            if (player == null) yield break;
            controller.AcquireGameplayInputLock(this);
            if (body != null) body.simulated = false;
            PlayerTeleportService.ApplySurfacePose(player, Original);
            double deadline = Time.realtimeSinceStartupAsDouble + 60d;
            yield return PlayerTeleportService.WaitForSurfaceArrival(player, Original,
                stillValid: () => Time.realtimeSinceStartupAsDouble < deadline);
            Physics2D.SyncTransforms();
        }

        public void Dispose()
        {
            if (body != null) { body.velocity = Vector2.zero; body.angularVelocity = 0f; body.simulated = WasSimulated; }
            if (mover != null) { mover.enabled = WasEnabled; mover.SetRunState(wasRunning); }
            if (controller != null) controller.ReleaseGameplayInputLock(this);
        }
    }

    #endregion
}
