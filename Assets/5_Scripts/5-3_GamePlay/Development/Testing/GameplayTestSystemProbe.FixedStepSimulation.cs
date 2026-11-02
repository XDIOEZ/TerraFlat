using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 固定步调度规则采样

    private static void RegisterFixedStepSimulationOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("core_lifecycle/fixed_step_scheduler", CaptureFixedStepSimulationScheduler);
    }

    private sealed class FixedStepSimulationProbeSystem : IGameplayFixedStepSimulationSystem
    {
        private readonly Action<float> simulate;
        public FixedStepSimulationProbeSystem(Action<float> simulate) => this.simulate = simulate;
        public void SimulateFixedStep(float deltaTime) => simulate(deltaTime);
    }

    private sealed class FixedStepSimulationReadinessProbeSystem : IGameplayFixedStepSimulationSystem,
        IGameplayFixedStepSimulationReadiness
    {
        private readonly Action<float> simulate;
        public bool CanAccumulateFixedStepTime { get; set; } = true;
        public FixedStepSimulationReadinessProbeSystem(Action<float> simulate) => this.simulate = simulate;
        public void SimulateFixedStep(float deltaTime) => simulate(deltaTime);
    }

    // 只安排假系统和记录正式调度器的调用，由 Lua 校验顺序及生命周期。
    private static JObject CaptureFixedStepSimulationScheduler(JObject args)
    {
        var scheduler = new GameplayFixedStepSimulationScheduler();
        var trace = new JArray();
        FixedStepSimulationProbeSystem System(string name, Action<float> onStep = null)
            => new(delta =>
            {
                trace.Add(new JObject { ["name"] = name, ["delta"] = delta });
                onStep?.Invoke(delta);
            });

        var host = new GameObject("FixedStepSchedulerProbe");
        try
        {
            var disabled = host.AddComponent<GameplayTestFixedStepDisabledProbe>();
            disabled.Record = delta => trace.Add(new JObject { ["name"] = "disabled", ["delta"] = delta });
            var inactiveObject = new GameObject("InactiveEntityProbe");
            inactiveObject.transform.SetParent(host.transform, false);
            var inactive = inactiveObject.AddComponent<GameplayTestFixedStepDisabledProbe>();
            inactive.Record = delta => trace.Add(new JObject { ["name"] = "inactive", ["delta"] = delta });
            var removed = System("removed");
            var reregistered = System("reregistered");
            var late = System("late");
            bool changed = false;
            var first = System("first", _ =>
            {
                if (changed) return;
                changed = true;
                scheduler.Unregister(removed);
                scheduler.Unregister(reregistered);
                scheduler.Register(late, GameplayFixedStepSimulationPhase.Gameplay);
                scheduler.Register(reregistered, GameplayFixedStepSimulationPhase.Gameplay);
                disabled.enabled = false;
                inactiveObject.SetActive(false);
            });

            scheduler.Register(System("after_physics"), GameplayFixedStepSimulationPhase.AfterPhysics);
            scheduler.Register(System("physics"), GameplayFixedStepSimulationPhase.Physics);
            scheduler.Register(removed, GameplayFixedStepSimulationPhase.Physics);
            scheduler.Register(disabled, GameplayFixedStepSimulationPhase.Physics);
            scheduler.Register(inactive, GameplayFixedStepSimulationPhase.Physics);
            scheduler.Register(reregistered, GameplayFixedStepSimulationPhase.Physics);
            scheduler.Register(System("space"), GameplayFixedStepSimulationPhase.SpaceSimulation);
            scheduler.Register(System("prepare_physics"), GameplayFixedStepSimulationPhase.PhysicsPreparation);
            scheduler.Register(System("gameplay_first"), GameplayFixedStepSimulationPhase.Gameplay);
            scheduler.Register(System("gameplay_second"), GameplayFixedStepSimulationPhase.Gameplay);
            scheduler.Register(System("navigation"), GameplayFixedStepSimulationPhase.Navigation);
            scheduler.Register(System("world"), GameplayFixedStepSimulationPhase.WorldSimulation);
            scheduler.Register(first, GameplayFixedStepSimulationPhase.WorldTime);
            scheduler.Register(System("time_peer"), GameplayFixedStepSimulationPhase.WorldTime);

            JObject SampleStep(float delta)
            {
                trace.Clear();
                scheduler.SimulateFixedStep(delta);
                return new JObject
                {
                    ["tick"] = (long)scheduler.TickIndex,
                    ["registered"] = scheduler.RegisteredSystemCount,
                    ["calls"] = trace.DeepClone()
                };
            }

            var result = new JObject
            {
                ["beforeTick"] = (long)scheduler.TickIndex,
                ["beforeRegistered"] = scheduler.RegisteredSystemCount,
                ["first"] = SampleStep(.125f),
                ["disabled"] = SampleStep(.25f)
            };
            disabled.enabled = true;
            inactiveObject.SetActive(true);
            result["reenabled"] = SampleStep(.5f);
            scheduler.Unregister(disabled);
            scheduler.Unregister(inactive);
            scheduler.Unregister(late);
            scheduler.Unregister(reregistered);
            result["unregistered"] = SampleStep(.75f);
            result["oneStepPerGroup"] = CaptureFixedStepGrouping(60, 1);
            result["twoStepsPerGroup"] = CaptureFixedStepGrouping(30, 2);
            result["tiered60Fps"] = CaptureTieredFixedStepGrouping(60, 1);
            result["tiered30Fps"] = CaptureTieredFixedStepGrouping(30, 2);
            result["tiered15Fps"] = CaptureTieredFixedStepGrouping(15, 4);
            result["tieredBase30Tps"] = CaptureTieredFixedStepGrouping(30, 1, baseTickRate: 30);
            result["tieredBase120Tps"] = CaptureTieredFixedStepGrouping(120, 1, baseTickRate: 120);
            result["tieredDoubleSpeed"] = CaptureTieredFixedStepGrouping(60, 2);
            result["tieredPaused"] = CaptureTieredFixedStepGrouping(30, 2, pauseAtHalf: true);
            result["tieredHitch"] = CaptureTieredFixedStepHitch();
            result["tieredDisabled"] = CaptureTieredFixedStepDisabled();
            result["tieredReadiness"] = CaptureTieredFixedStepReadiness();
            result["tickRatePreference"] = CaptureTickRatePreference();
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    private static JObject CaptureFixedStepGrouping(int groups, int stepsPerGroup)
    {
        var scheduler = new GameplayFixedStepSimulationScheduler();
        int calls = 0;
        double elapsed = 0d;
        scheduler.Register(new FixedStepSimulationProbeSystem(delta => { calls++; elapsed += delta; }),
            GameplayFixedStepSimulationPhase.Gameplay);
        int startFrame = Time.frameCount;
        for (int group = 0; group < groups; group++)
            for (int step = 0; step < stepsPerGroup; step++)
                scheduler.SimulateFixedStep(1f / 60f);
        return new JObject
        {
            ["groups"] = groups, ["ticks"] = (long)scheduler.TickIndex,
            ["calls"] = calls, ["elapsed"] = elapsed,
            ["startFrame"] = startFrame, ["endFrame"] = Time.frameCount
        };
    }

    // 用正式调度器采样所有频率，不模拟机器产量或物理引擎。
    private static JObject CaptureTieredFixedStepGrouping(int groups, int stepsPerGroup, bool pauseAtHalf = false,
        int baseTickRate = 60)
    {
        var scheduler = new GameplayFixedStepSimulationScheduler();
        GameplayFixedStepTickRate[] rates =
        {
            GameplayFixedStepTickRate.Hz60, GameplayFixedStepTickRate.Hz30, GameplayFixedStepTickRate.Hz20,
            GameplayFixedStepTickRate.Hz10, GameplayFixedStepTickRate.Hz5
        };
        var calls = new int[rates.Length];
        var seconds = new double[rates.Length];
        var largestStep = new float[rates.Length];
        var registered = new IGameplayFixedStepSimulationSystem[rates.Length];
        for (int i = 0; i < rates.Length; i++)
        {
            int index = i;
            registered[i] = new FixedStepSimulationProbeSystem(delta =>
            {
                calls[index]++;
                seconds[index] += delta;
                largestStep[index] = Math.Max(largestStep[index], delta);
            });
            scheduler.Register(registered[i], GameplayFixedStepSimulationPhase.Gameplay, rates[i]);
        }
        scheduler.Register(registered[2], GameplayFixedStepSimulationPhase.Gameplay, GameplayFixedStepTickRate.Hz60);
        int uniqueCount = scheduler.RegisteredSystemCount;
        JObject Capture()
        {
            var snapshot = new JArray();
            for (int i = 0; i < rates.Length; i++)
                snapshot.Add(new JObject
                {
                    ["rate"] = (int)rates[i], ["calls"] = calls[i],
                    ["seconds"] = seconds[i], ["largestStep"] = largestStep[i]
                });
            return new JObject { ["ticks"] = (long)scheduler.TickIndex, ["tiers"] = snapshot };
        }

        JObject pauseSnapshot = null;
        for (int group = 0; group < groups; group++)
        {
            if (pauseAtHalf && group == groups / 2)
            {
                // 暂停时没有 FixedUpdate，不能人为推进累计器或补欠下的时间。
                pauseSnapshot = Capture();
            }
            for (int step = 0; step < stepsPerGroup; step++) scheduler.SimulateFixedStep(1f / baseTickRate);
        }
        JObject result = Capture();
        result["uniqueRegistrations"] = uniqueCount;
        if (pauseSnapshot != null) result["beforeResume"] = pauseSnapshot;
        return result;
    }

    private static JObject CaptureTieredFixedStepHitch()
    {
        var scheduler = new GameplayFixedStepSimulationScheduler();
        int calls = 0;
        float deliveredSeconds = 0f;
        var system = new FixedStepSimulationProbeSystem(delta => { calls++; deliveredSeconds += delta; });
        scheduler.Register(system, GameplayFixedStepSimulationPhase.Gameplay, GameplayFixedStepTickRate.Hz20);
        scheduler.SimulateFixedStep(.35f);
        int initialCalls = calls;
        scheduler.SimulateFixedStep(1f / 60f);
        bool physicsRejectsLowRate = false;
        try
        {
            scheduler.Register(new FixedStepSimulationProbeSystem(_ => { }),
                GameplayFixedStepSimulationPhase.Physics, GameplayFixedStepTickRate.Hz20);
        }
        catch (ArgumentException) { physicsRejectsLowRate = true; }
        return new JObject
        {
            ["callsAfterHitch"] = initialCalls, ["callsAfterNextStep"] = calls,
            ["deliveredSeconds"] = deliveredSeconds, ["registered"] = scheduler.RegisteredSystemCount,
            ["physicsRejectsLowRate"] = physicsRejectsLowRate
        };
    }

    private static JObject CaptureTieredFixedStepDisabled()
    {
        var scheduler = new GameplayFixedStepSimulationScheduler();
        var host = new GameObject("TieredDisabledProbe");
        try
        {
            var system = host.AddComponent<GameplayTestFixedStepDisabledProbe>();
            int calls = 0;
            double elapsed = 0d;
            system.Record = delta => { calls++; elapsed += delta; };
            scheduler.Register(system, GameplayFixedStepSimulationPhase.Gameplay, GameplayFixedStepTickRate.Hz20);
            scheduler.SimulateFixedStep(1f / 60f);
            scheduler.SimulateFixedStep(1f / 60f);
            system.enabled = false;
            scheduler.SimulateFixedStep(1f / 60f);
            scheduler.SimulateFixedStep(1f / 60f);
            system.enabled = true;
            scheduler.SimulateFixedStep(1f / 60f);
            scheduler.SimulateFixedStep(1f / 60f);
            int beforeDue = calls;
            scheduler.SimulateFixedStep(1f / 60f);
            return new JObject { ["beforeDue"] = beforeDue, ["calls"] = calls, ["seconds"] = elapsed };
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    private static JObject CaptureTieredFixedStepReadiness()
    {
        var scheduler = new GameplayFixedStepSimulationScheduler();
        int calls = 0;
        double seconds = 0d;
        var system = new FixedStepSimulationReadinessProbeSystem(delta => { calls++; seconds += delta; });
        scheduler.Register(system, GameplayFixedStepSimulationPhase.Gameplay, GameplayFixedStepTickRate.Hz20);
        scheduler.SimulateFixedStep(1f / 60f);
        scheduler.SimulateFixedStep(1f / 60f);
        system.CanAccumulateFixedStepTime = false;
        scheduler.SimulateFixedStep(1f / 60f);
        scheduler.SimulateFixedStep(1f / 60f);
        system.CanAccumulateFixedStepTime = true;
        scheduler.SimulateFixedStep(1f / 60f);
        scheduler.SimulateFixedStep(1f / 60f);
        int prematureCalls = calls;
        scheduler.SimulateFixedStep(1f / 60f);
        return new JObject { ["prematureCalls"] = prematureCalls, ["calls"] = calls, ["seconds"] = seconds };
    }

    private static JObject CaptureTickRatePreference()
    {
        const string key = "FlatWorld.Simulation.TickRate";
        bool hadPreference = PlayerPrefs.HasKey(key);
        int originalPreference = PlayerPrefs.GetInt(key);
        int originalSelection = GameplayTickRateUserSettings.SelectedIndex;
        float originalStep = Time.fixedDeltaTime;
        float originalMaximum = Time.maximumDeltaTime;
        var samples = new JArray();
        try
        {
            for (int index = 0; index < 3; index++)
            {
                bool accepted = GameplayTickRateUserSettings.TrySetSelectedIndex(index, out _);
                samples.Add(new JObject
                {
                    ["accepted"] = accepted,
                    ["selectedIndex"] = GameplayTickRateUserSettings.SelectedIndex,
                    ["effectiveRate"] = GameplayTickRateUserSettings.EffectiveTicksPerSecond,
                    ["fixedStep"] = Time.fixedDeltaTime,
                    ["maximumStep"] = Time.maximumDeltaTime,
                    ["savedRate"] = PlayerPrefs.GetInt(key)
                });
            }
            int indexBeforeInvalid = GameplayTickRateUserSettings.SelectedIndex;
            bool rejectedInvalid = !GameplayTickRateUserSettings.TrySetSelectedIndex(-1, out _) &&
                !GameplayTickRateUserSettings.TrySetSelectedIndex(3, out _) &&
                GameplayTickRateUserSettings.SelectedIndex == indexBeforeInvalid;
            return new JObject { ["samples"] = samples, ["rejectedInvalid"] = rejectedInvalid };
        }
        finally
        {
            GameplayTickRateUserSettings.TrySetSelectedIndex(originalSelection, out _);
            if (hadPreference) PlayerPrefs.SetInt(key, originalPreference);
            else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            Time.fixedDeltaTime = originalStep;
            Time.maximumDeltaTime = originalMaximum;
        }
    }

    #endregion
}

internal sealed class GameplayTestFixedStepDisabledProbe : MonoBehaviour, IGameplayFixedStepSimulationSystem
{
    #region 停用组件采样
    public Action<float> Record;
    public void SimulateFixedStep(float deltaTime) => Record(deltaTime);
    #endregion
}
