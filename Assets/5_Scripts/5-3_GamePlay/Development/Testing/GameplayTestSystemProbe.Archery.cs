using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 弓弩正式配置与蓄力时序采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterArcheryRules()
        => Register("combat", "archery_cycle", EntityArcheryCycle);

    /// <summary>读取正式弓弩定义并驱动生产蓄力时钟，断言由 Lua 按操作顺序执行。</summary>
    private static JObject EntityArcheryCycle(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
        var weapon = definitions.Single(value => value.Id == Text(args, "weaponId", "Crossbow_Wood"));
        JObject parameters = weapon.Modules["bow"].Parameters;
        Enum.TryParse(parameters.Value<string>(nameof(Mod_Bow.ReleaseMode)), out ProjectileChargeReleaseMode mode);
        float fullCharge = parameters.Value<float>(nameof(Mod_Bow.FullChargeSeconds));
        float staminaRate = parameters.Value<float>(nameof(Mod_Bow.StaminaConsumePerSecond));
        var cycle = new ProjectileChargeCycle();
        var samples = new JArray();
        float stamina = 0f;
        int shots = 0;
        foreach (JObject step in args["steps"] as JArray ?? new JArray())
        {
            bool started = false;
            var release = ProjectileChargeReleaseResult.None;
            string action = Text(step, "action", "advance");
            switch (action)
            {
                case "press": started = cycle.TryPress(); break;
                case "advance":
                    stamina += staminaRate * cycle.Advance((float)Number(step, "seconds", 0), fullCharge, mode);
                    break;
                case "release":
                    release = cycle.Release(fullCharge, mode);
                    if (release == ProjectileChargeReleaseResult.Fire) shots++;
                    break;
                case "cancel": cycle.Cancel(); break;
                default: throw new ArgumentException("未知弓弩测试动作：" + action);
            }
            samples.Add(new JObject { ["action"] = action, ["started"] = started,
                ["release"] = release.ToString(), ["active"] = cycle.IsActive, ["charging"] = cycle.IsCharging,
                ["charge"] = cycle.GetCharge01(fullCharge), ["stamina"] = stamina, ["shots"] = shots });
        }
        var recurve = definitions.Single(value => value.Id == "Bow_Recurve").Modules["bow"].Parameters;
        JObject woodBowParameters = definitions.Single(value => value.Id == "Bow_Wood").Modules["bow"].Parameters;
        Vector2 launchProbeWeaponPosition = new(8f, 6f);
        Vector2 launchProbeOwnerPosition = new(2f, 1f);
        Vector2 launchProbeDirection = Vector2.right;
        float launchProbeOffset = woodBowParameters.Value<float>(nameof(Mod_Bow.SpawnForwardOffset));
        Vector2 launchProbePosition = Mod_Bow.ResolveWeaponForwardLaunchPosition(
            launchProbeWeaponPosition, launchProbeDirection, launchProbeOffset);
        // 弹药采样读取正式继承结果，避免只测蓄力时序而漏掉缺失模块。
        var ammunition = new JArray();
        foreach (JToken ammoId in args["ammoIds"] as JArray ?? new JArray())
        {
            var definition = definitions.Single(value => value.Id == ammoId.Value<string>());
            ammunition.Add(new JObject { ["id"] = definition.Id,
                ["tags"] = new JArray(definition.Tags?.ToArray() ?? Array.Empty<string>()),
                ["modules"] = JObject.FromObject(definition.Modules) });
        }
        return new JObject { ["weaponId"] = weapon.Id, ["mode"] = mode.ToString(),
            ["parameters"] = parameters.DeepClone(), ["recurveParameters"] = recurve.DeepClone(),
            ["woodBowStamina"] = definitions.Single(value => value.Id == "Bow_Wood").Modules["bow"].Parameters
                .Value<float>(nameof(Mod_Bow.StaminaConsumePerSecond)),
            ["blowgunStamina"] = definitions.Single(value => value.Id == "Blowgun_Wood").Modules["bow"].Parameters
                .Value<float>(nameof(Mod_Bow.StaminaConsumePerSecond)),
            ["launchPositionProbe"] = new JObject
            {
                ["weapon"] = new JObject { ["x"] = launchProbeWeaponPosition.x, ["y"] = launchProbeWeaponPosition.y },
                ["owner"] = new JObject { ["x"] = launchProbeOwnerPosition.x, ["y"] = launchProbeOwnerPosition.y },
                ["direction"] = new JObject { ["x"] = launchProbeDirection.x, ["y"] = launchProbeDirection.y },
                ["forwardOffset"] = launchProbeOffset,
                ["position"] = new JObject { ["x"] = launchProbePosition.x, ["y"] = launchProbePosition.y }
            },
            ["samples"] = samples, ["stamina"] = stamina, ["shots"] = shots, ["ammunition"] = ammunition };
    }
    #endregion
}
