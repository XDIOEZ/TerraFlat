#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

public static class GameplayTestFixedStepClockRuntimeProbe
{
    #region 独立游戏时钟与原生物理采样

    private const int CatchUpHitchMilliseconds = 200;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register()
    {
        GameplayTestRuntimeProbe.RegisterAsync("core_lifecycle", "fixed_step_clock", CaptureFixedStepClock);
        GameplayTestRuntimeProbe.RegisterAsync("player", "independent_presentation_frames", CaptureIndependentPresentationFrames);
    }

    #region 真实绘制帧与权威位姿隔离
    private static IEnumerator CaptureIndependentPresentationFrames(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var fixture = new IndependentPresentationFixture(player);
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        fixture.Begin();
        double deadline = Time.realtimeSinceStartupAsDouble + 8d;
        while (Time.realtimeSinceStartupAsDouble < deadline &&
            (fixture.Frames < 24 || fixture.IntermediateFrames < 5)) yield return null;
        completed(fixture.Sample());
    }

    private sealed class IndependentPresentationFixture : IDisposable
    {
        private const string RateKey = "FlatWorld.Simulation.TickRate";
        private readonly Player player;
        private readonly Mod_Cam camera;
        private readonly Mod_Mover mover;
        private readonly Rigidbody2D body;
        private readonly Vector3 original;
        private readonly float timeScale = Time.timeScale;
        private readonly int fps = Application.targetFrameRate, vSync = QualitySettings.vSyncCount;
        private readonly int rate = GameplayTickRateUserSettings.SelectedIndex, storedRate = PlayerPrefs.GetInt(RateKey);
        private readonly bool hadRate = PlayerPrefs.HasKey(RateKey);
        private Mod_Mover.DrivenMotionLease motion;
        private IDisposable wind;
        private bool disposed;
        private ulong previousTick;
        private Vector3 previousLogic, previousDisplay;
        private int lastFrame = -1;
        private float maxViewError, maxCameraTargetError, maxBodyError;
        private bool queriesPure = true;
        private int callbacks, missingManager, missingView, hiddenView, missingCameraTarget;
        public int Frames { get; private set; }
        public int IntermediateFrames { get; private set; }

        public IndependentPresentationFixture(Player player)
        {
            this.player = player;
            original = WorldTopologyRuntime.NormalizePosition(player.transform.position);
            camera = player.GetComponentInChildren<Mod_Cam>(true);
            mover = player.GetComponentInChildren<Mod_Mover>(true);
            body = WorldPhysicsService2D.GetItemRigidbody(player);
        }

        public void Begin()
        {
            if (mover == null || body == null || camera == null || camera.HasFollowOverride)
                throw new InvalidOperationException("显示分离回归需要隔离玩家的普通移动和默认镜头。");
            GameplayTickRateUserSettings.TrySetSelectedIndex(0, out _);
            // 慢放扩大 Tick 间隙，在低性能 Editor 中也能观测多张真实渲染帧。
            Time.timeScale = .25f;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 0;
            wind = mover.SuppressWindMotion();
            if (!mover.TryAcquireDrivenMotion(Vector2.right * 2f, out motion))
                throw new InvalidOperationException("显示分离回归无法取得正式主动移动租约。");
            RenderPipelineManager.endContextRendering += ObserveRenderedFrame;
        }

        private void ObserveRenderedFrame(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (lastFrame == Time.frameCount || player == null) return;
            lastFrame = Time.frameCount;
            callbacks++;
            WorldRenderingManager rendering = WorldRenderingManager.ExistingInstance;
            if (rendering == null) { missingManager++; return; }
            if (!rendering.TryGetItemPresentation(player, out var view)) { missingView++; return; }
            if (!view.gameObject.activeInHierarchy) { hiddenView++; return; }
            if (camera.Vcam?.Follow == null) { missingCameraTarget++; return; }
            Vector3 logic = player.transform.position;
            Vector3 display = rendering.GetItemRenderedWorldPosition(player);
            ulong tick = GameplayFixedStepSimulation.TickIndex;
            if (Frames > 2 && tick == previousTick && logic.Equals(previousLogic) &&
                (display - previousDisplay).sqrMagnitude > .0000000001f) IntermediateFrames++;
            for (int i = 0; i < 8; i++)
                queriesPure &= rendering.GetItemRenderedWorldPosition(player).Equals(display) &&
                    player.transform.position.Equals(logic) && GameplayFixedStepSimulation.TickIndex == tick;
            maxViewError = Mathf.Max(maxViewError, Vector3.Distance(view.transform.position, display));
            maxCameraTargetError = Mathf.Max(maxCameraTargetError, Vector3.Distance(camera.Vcam.Follow.position, display));
            maxBodyError = Mathf.Max(maxBodyError, Vector2.Distance(body.position, logic));
            previousTick = tick; previousLogic = logic; previousDisplay = display; Frames++;
        }

        public JObject Sample() => new()
        {
            ["frames"] = Frames, ["intermediateFrames"] = IntermediateFrames,
            ["viewError"] = maxViewError, ["cameraTargetError"] = maxCameraTargetError,
            ["bodyError"] = maxBodyError, ["queriesPure"] = queriesPure,
            ["tps"] = GameplayFixedStepSimulation.TicksPerSecond, ["timeScale"] = Time.timeScale,
            ["callbacks"] = callbacks, ["missingManager"] = missingManager, ["missingView"] = missingView,
            ["hiddenView"] = hiddenView, ["missingCameraTarget"] = missingCameraTarget,
            ["playerWorld"] = GameplayWorldRegistry.GetItemWorldKey(player),
            ["shownWorld"] = GameplayWorldRegistry.LocalPresentationWorldKey,
            ["renderingRegistrations"] = WorldRenderingManager.ExistingInstance?.RegisteredItemCount ?? -1,
            ["nativeInterpolationDisabled"] = body.interpolation == RigidbodyInterpolation2D.None
        };

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            RenderPipelineManager.endContextRendering -= ObserveRenderedFrame;
            motion?.Dispose(); wind?.Dispose();
            Time.timeScale = timeScale;
            Application.targetFrameRate = fps; QualitySettings.vSyncCount = vSync;
            GameplayTickRateUserSettings.TrySetSelectedIndex(rate, out _);
            if (hadRate) PlayerPrefs.SetInt(RateKey, storedRate); else PlayerPrefs.DeleteKey(RateKey);
            PlayerPrefs.Save();
            if (player != null) PlayerTeleportService.ApplySurfacePose(player, original);
        }
    }
    #endregion

    private sealed class FixedStepClockObservation : IGameplayFixedStepSimulationSystem
    {
        public readonly List<JObject> Steps = new();

        public void SimulateFixedStep(float deltaTime)
        {
            Steps.Add(new JObject
            {
                ["tick"] = (long)GameplayFixedStepSimulation.TickIndex,
                ["frame"] = Time.frameCount,
                ["delta"] = deltaTime,
                ["unityFixedTime"] = Time.fixedTimeAsDouble,
                ["clock"] = GameplayFixedStepSimulation.CurrentTime,
                ["physicsDispatch"] = (long)GameplayWorldRegistry.PhysicsDispatchCount,
                ["physicsSceneSteps"] = (long)GameplayWorldRegistry.PhysicsSceneStepCount,
                ["manualPhysicsMode"] = Physics2D.simulationMode == SimulationMode2D.Script
            });
        }
    }

    private sealed class FixedStepClockFixture : IDisposable
    {
        private const string ShowFpsKey = "FlatWorld.UI.PlayerShowFps";
        public readonly FixedStepClockObservation Observer = new();
        public readonly float OriginalTimeScale = Time.timeScale;
        public readonly float OriginalFixedDeltaTime = Time.fixedDeltaTime;
        public readonly int OriginalTargetFrameRate = Application.targetFrameRate;
        public readonly int OriginalVSyncCount = QualitySettings.vSyncCount;
        public readonly bool OriginalShowFps = PlayerWorldCoordinateDisplayPreferences.ShowFps;
        public readonly bool HadShowFpsPreference = PlayerPrefs.HasKey(ShowFpsKey);
        public readonly int OriginalShowFpsPreference = PlayerPrefs.GetInt(ShowFpsKey);
        public int BeforeRegister, AfterRegister, BeforeUnregister, AfterUnregister;
        private bool registered, disposed;

        public void RegisterObserver()
        {
            BeforeRegister = GameplayFixedStepSimulation.RegisteredSystemCount;
            GameplayFixedStepSimulation.Register(Observer, GameplayFixedStepSimulationPhase.AfterPhysics);
            registered = true;
            AfterRegister = GameplayFixedStepSimulation.RegisteredSystemCount;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (registered)
                {
                    BeforeUnregister = GameplayFixedStepSimulation.RegisteredSystemCount;
                    GameplayFixedStepSimulation.Unregister(Observer);
                    AfterUnregister = GameplayFixedStepSimulation.RegisteredSystemCount;
                    registered = false;
                }
            }
            finally
            {
                Time.timeScale = OriginalTimeScale;
                Application.targetFrameRate = OriginalTargetFrameRate;
                QualitySettings.vSyncCount = OriginalVSyncCount;
                PlayerWorldCoordinateDisplayPreferences.SetShowFps(OriginalShowFps);
                if (HadShowFpsPreference) PlayerPrefs.SetInt(ShowFpsKey, OriginalShowFpsPreference);
                else PlayerPrefs.DeleteKey(ShowFpsKey);
                PlayerPrefs.Save();
            }
        }
    }

    // 记录真实游戏 Tick、手动原生物理、Unity 固定时钟和渲染帧，不在测试里自行推进游戏世界。
    private static IEnumerator CaptureFixedStepClock(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var fixture = new FixedStepClockFixture();
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        try
        {
            var hosts = UnityEngine.Object.FindObjectsOfType<GameplayFixedStepSimulationDriver>(true);
            GameManager manager = GameManager.ExistingInstance;
            var result = new JObject
            {
                ["hostCount"] = hosts.Length,
                ["managerOwnsActiveHost"] = hosts.Length == 1 && manager != null &&
                    hosts[0].gameObject == manager.gameObject && hosts[0].isActiveAndEnabled,
                ["defaultTicksPerSecond"] = GameplayFixedStepSimulation.DefaultTicksPerSecond,
                ["defaultTickDelta"] = GameplayFixedStepSimulation.DefaultTickDeltaTime,
                ["selectedTicksPerSecond"] = GameplayTickRateUserSettings.SelectedTicksPerSecond,
                ["effectiveTicksPerSecond"] = GameplayTickRateUserSettings.EffectiveTicksPerSecond,
                ["maximumCatchUpTicks"] = GameplaySimulationFrameClock.MaximumCatchUpTicks,
                ["manualPhysicsMode"] = Physics2D.simulationMode == SimulationMode2D.Script,
                ["original"] = new JObject
                {
                    ["timeScale"] = fixture.OriginalTimeScale,
                    ["fixedDelta"] = fixture.OriginalFixedDeltaTime,
                    ["targetFrameRate"] = fixture.OriginalTargetFrameRate,
                    ["vSyncCount"] = fixture.OriginalVSyncCount,
                    ["showFps"] = fixture.OriginalShowFps,
                    ["showFpsKeyExists"] = fixture.HadShowFpsPreference,
                    ["showFpsStored"] = fixture.OriginalShowFpsPreference
                }
            };
            fixture.RegisterObserver();
            PlayerWorldCoordinateDisplayPreferences.SetShowFps(true);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 30;
            Time.timeScale = 1f;
            yield return null;
            yield return null;
            yield return CaptureWindow(fixture.Observer, 1.5d, value => result["normal"] = value);
            yield return CaptureBoundedCatchUpAfterHitch(fixture.Observer, value => result["normalHitch"] = value);

            Time.timeScale = 2f;
            yield return null;
            yield return null;
            yield return CaptureWindow(fixture.Observer, 1.5d, value => result["double"] = value);
            yield return CaptureBoundedCatchUpAfterHitch(fixture.Observer, value => result["doubleHitch"] = value);

            Time.timeScale = 0f;
            yield return null;
            yield return CaptureWindow(fixture.Observer, 1d, value => result["paused"] = value);

            Time.timeScale = 1f;
            yield return null;
            yield return CaptureWindow(fixture.Observer, .35d, value => result["resumed"] = value);
            fixture.Dispose();
            result["beforeRegister"] = fixture.BeforeRegister;
            result["afterRegister"] = fixture.AfterRegister;
            result["beforeUnregister"] = fixture.BeforeUnregister;
            result["afterUnregister"] = fixture.AfterUnregister;
            result["restored"] = new JObject
            {
                ["timeScale"] = Time.timeScale,
                ["fixedDelta"] = Time.fixedDeltaTime,
                ["targetFrameRate"] = Application.targetFrameRate,
                ["vSyncCount"] = QualitySettings.vSyncCount,
                ["showFps"] = PlayerWorldCoordinateDisplayPreferences.ShowFps,
                ["showFpsKeyExists"] = PlayerPrefs.HasKey("FlatWorld.UI.PlayerShowFps"),
                ["showFpsStored"] = PlayerPrefs.GetInt("FlatWorld.UI.PlayerShowFps")
            };
            completed(result);
        }
        finally { fixture.Dispose(); }
    }

    private static IEnumerator CaptureWindow(FixedStepClockObservation observer, double seconds, Action<JObject> completed)
    {
        observer.Steps.Clear();
        double startWall = Time.realtimeSinceStartupAsDouble;
        double startFixed = Time.fixedTimeAsDouble;
        double startClock = GameplayFixedStepSimulation.CurrentTime;
        double startGameTime = Time.timeAsDouble;
        long startPhysicsDispatch = (long)GameplayWorldRegistry.PhysicsDispatchCount;
        long startPhysicsSceneSteps = (long)GameplayWorldRegistry.PhysicsSceneStepCount;
        long startTick = (long)GameplayFixedStepSimulation.TickIndex;
        int startFrame = Time.frameCount;
        float fixedDelta = Time.fixedDeltaTime;
        float tickDelta = GameplayFixedStepSimulation.TickDeltaTime;
        float timeScale = Time.timeScale;
        int targetFrameRate = Application.targetFrameRate;
        int vSyncCount = QualitySettings.vSyncCount;
        var frames = new JArray();
        while (Time.realtimeSinceStartupAsDouble - startWall < seconds)
        {
            yield return null;
            frames.Add(new JObject
            {
                ["scaledDelta"] = Time.deltaTime,
                ["unscaledDelta"] = Time.unscaledDeltaTime,
                ["maximumDelta"] = Time.maximumDeltaTime
            });
        }
        Transform fpsNode = UIManager.Instance?.panelRoot?.Find("PlayerWorldCoordinateHUD/FPS文本");
        TextMeshProUGUI fpsText = fpsNode != null ? fpsNode.GetComponent<TextMeshProUGUI>() : null;
        completed(new JObject
        {
            ["startWall"] = startWall, ["endWall"] = Time.realtimeSinceStartupAsDouble,
            ["startFixedTime"] = startFixed, ["endFixedTime"] = Time.fixedTimeAsDouble,
            ["startClock"] = startClock, ["endClock"] = GameplayFixedStepSimulation.CurrentTime,
            ["startPhysicsDispatch"] = startPhysicsDispatch,
            ["endPhysicsDispatch"] = (long)GameplayWorldRegistry.PhysicsDispatchCount,
            ["startPhysicsSceneSteps"] = startPhysicsSceneSteps,
            ["endPhysicsSceneSteps"] = (long)GameplayWorldRegistry.PhysicsSceneStepCount,
            ["startGameTime"] = startGameTime, ["endGameTime"] = Time.timeAsDouble,
            ["startTick"] = startTick, ["endTick"] = (long)GameplayFixedStepSimulation.TickIndex,
            ["startFrame"] = startFrame, ["endFrame"] = Time.frameCount,
            ["fixedDelta"] = fixedDelta, ["endFixedDelta"] = Time.fixedDeltaTime,
            ["tickDelta"] = tickDelta, ["endTickDelta"] = GameplayFixedStepSimulation.TickDeltaTime,
            ["timeScale"] = timeScale, ["endTimeScale"] = Time.timeScale,
            ["targetFrameRate"] = targetFrameRate, ["endTargetFrameRate"] = Application.targetFrameRate,
            ["vSyncCount"] = vSyncCount, ["endVSyncCount"] = QualitySettings.vSyncCount,
            ["steps"] = new JArray(observer.Steps), ["frames"] = frames,
            ["hudVisible"] = fpsText != null && fpsText.isActiveAndEnabled,
            ["hudText"] = fpsText != null ? fpsText.text : string.Empty
        });
    }

    // 故意阻塞一帧，核对独立游戏时钟的追赶预算与物理分发次数。
    private static IEnumerator CaptureBoundedCatchUpAfterHitch(FixedStepClockObservation observer, Action<JObject> completed)
    {
        observer.Steps.Clear();
        double startWall = Time.realtimeSinceStartupAsDouble;
        double startGameTime = Time.timeAsDouble;
        double startFixedTime = Time.fixedTimeAsDouble;
        double startClock = GameplayFixedStepSimulation.CurrentTime;
        long startPhysicsDispatch = (long)GameplayWorldRegistry.PhysicsDispatchCount;
        long startTick = (long)GameplayFixedStepSimulation.TickIndex;
        int startFrame = Time.frameCount;
        float timeScale = Time.timeScale;

        System.Threading.Thread.Sleep(CatchUpHitchMilliseconds);
        yield return null;

        completed(new JObject
        {
            ["hitchMilliseconds"] = CatchUpHitchMilliseconds,
            ["startWall"] = startWall, ["endWall"] = Time.realtimeSinceStartupAsDouble,
            ["startGameTime"] = startGameTime, ["endGameTime"] = Time.timeAsDouble,
            ["startFixedTime"] = startFixedTime, ["endFixedTime"] = Time.fixedTimeAsDouble,
            ["startClock"] = startClock, ["endClock"] = GameplayFixedStepSimulation.CurrentTime,
            ["startPhysicsDispatch"] = startPhysicsDispatch,
            ["endPhysicsDispatch"] = (long)GameplayWorldRegistry.PhysicsDispatchCount,
            ["startTick"] = startTick, ["endTick"] = (long)GameplayFixedStepSimulation.TickIndex,
            ["startFrame"] = startFrame, ["endFrame"] = Time.frameCount,
            ["timeScale"] = timeScale, ["endTimeScale"] = Time.timeScale,
            ["fixedDelta"] = Time.fixedDeltaTime, ["maximumDelta"] = Time.maximumDeltaTime,
            ["tickDelta"] = GameplayFixedStepSimulation.TickDeltaTime,
            ["manualPhysicsMode"] = Physics2D.simulationMode == SimulationMode2D.Script,
            ["scaledDelta"] = Time.deltaTime, ["unscaledDelta"] = Time.unscaledDeltaTime,
            ["steps"] = new JArray(observer.Steps)
        });
    }

    #endregion
}
#endif
