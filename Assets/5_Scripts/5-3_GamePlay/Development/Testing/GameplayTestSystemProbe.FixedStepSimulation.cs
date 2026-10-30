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

    #endregion
}

internal sealed class GameplayTestFixedStepDisabledProbe : MonoBehaviour, IGameplayFixedStepSimulationSystem
{
    #region 停用组件采样
    public Action<float> Record;
    public void SimulateFixedStep(float deltaTime) => Record(deltaTime);
    #endregion
}
