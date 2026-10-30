#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>从正式继承目录采样 ECS 行为参数映射。</summary>
    internal static class AiecsBehaviorTreeParameterTestProbe
    {
        #region 真实配置到 ECS 的参数采样
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register() => GameplayTestSystemProbe.Register("ai", "aiecs_tree_parameters", Capture);
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif

        private static JObject Capture(JObject args)
        {
            JObject inputs = GameplayTestSystemProbe.Capture("ai", "animal_tree_mapping_inputs", new JObject());
            var results = new JObject();
            foreach (JObject actor in (JArray)inputs["actors"])
            {
                string id = (string)actor["id"];
                if (id is not ("Wolf" or "SnowLeopard" or "Bear" or "Chicken" or "WildBoar")) continue;
                AiecsBehaviorParameterSnapshot value = Compile(actor);
                results[id] = Snapshot(value);
                if (id == "SnowLeopard")
                {
                    var prototype = new JObject { ["DamageValues"] = new JObject
                    {
                        ["Cutting"] = 0, ["Piercing"] = 0, ["Chopping"] = 0, ["Blunt"] = 22
                    } };
                    JObject merged = AiecsDefinitionCompiler.MergeConfiguredModuleParameters(prototype, actor["damage"] as JObject);
                    CombatDamage damage = merged["DamageValues"].ToObject<CombatDamage>();
                    var parts = GameplayCombatBridge.Values(damage);
                    results[id]["damage"] = new JObject
                    {
                        ["cutting"] = parts.x, ["piercing"] = parts.y, ["chopping"] = parts.z, ["blunt"] = parts.w,
                        ["physical"] = damage.TotalCombatPower,
                        ["bleedingAt83"] = FlatWorld.Combat.CombatRules.BleedingTier(parts, true, true, .83f),
                        ["bleedingAt85"] = FlatWorld.Combat.CombatRules.BleedingTier(parts, true, true, .85f)
                    };
                }
                if (id != "Chicken") continue;
                var ecs = actor["ecs"] is JObject configured ? (JObject)configured.DeepClone() : new JObject();
                ecs["perception"] = new JObject { ["radius"] = 73f };
                ecs["movement"] = new JObject { ["speed"] = 7.25f };
                ecs["reproduction"] = new JObject { ["cooldownDays"] = 9f, ["eggItemId"] = "OverrideEgg" };
                var overridden = (JObject)actor.DeepClone();
                overridden["ecs"] = ecs;
                results["override"] = Snapshot(Compile(overridden));
                results["senseOverride"] = Snapshot(Compile(overridden, 91f));
            }
            return results;
        }

        private static AiecsBehaviorParameterSnapshot Compile(JObject actor, float senseOverride = 0f) =>
            AiecsDefinitionCompiler.CompileBehaviorParameters(actor["ai"] as JObject, actor["ecs"] as JObject,
                actor["detector"] as JObject, actor["mover"] as JObject, actor["flight"] as JObject,
                actor["aquatic"] as JObject, actor["colony"] as JObject, actor["light"] as JObject, senseOverride);

        private static JObject Snapshot(AiecsBehaviorParameterSnapshot value) => new()
        {
            ["sense"] = value.SenseRange, ["chase"] = value.ChaseRange, ["retry"] = value.ChaseRetryDelay,
            ["attack"] = value.AttackRange, ["windup"] = value.Windup, ["active"] = value.Active,
            ["recovery"] = value.Recovery, ["cooldown"] = value.Cooldown,
            ["fleeHealth"] = value.FleeHealthRatio, ["speed"] = value.MoveSpeed,
            ["eggDays"] = value.ReproductionDays, ["eggItem"] = value.EggItemId
        };
        #endregion
    }
}
#endif
