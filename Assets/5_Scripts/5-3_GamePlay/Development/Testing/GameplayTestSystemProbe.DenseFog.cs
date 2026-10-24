using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 天气雾过渡采样

    private static void RegisterDenseFogOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("rendering/fog_transition", ServiceDenseFogTransition);
        operations.Add("environment/fog_perception", EnvironmentFogPerception);
    }

    private static JObject ServiceDenseFogTransition(JObject args)
    {
        WorldRenderingConfig.RenderingDenseFog defaults = WorldRenderingConfigCatalog.Default.denseFog;
        var config = JsonUtility.FromJson<WorldRenderingConfig.RenderingDenseFog>(JsonUtility.ToJson(defaults));
        config.transitionSeconds = (float)Number(args, "duration", defaults.transitionSeconds);
        var transition = new DenseFogPresentationTransition();
        var samples = new JArray();
        if (args["steps"] is JArray steps)
        {
            // 只推进渲染器共用的正式过渡状态，预期时长和半径由 Lua 判断。
            foreach (JObject step in steps)
            {
                if (Flag(step, "reset", false)) transition.Reset();
                else transition.Step((float)Number(step, "target", 1), (float)Number(step, "seconds", 0),
                    (float)Number(step, "outside", 200), config);
                Vector2 radii = transition.Radii;
                samples.Add(new JObject
                {
                    ["hasValue"] = transition.HasValue, ["visible"] = transition.IsVisible,
                    ["transitioning"] = transition.IsTransitioning, ["progress"] = transition.Progress,
                    ["clearRadius"] = radii.x, ["opaqueRadius"] = radii.y
                });
            }
        }
        return new JObject
        {
            ["samples"] = samples, ["validConfig"] = config.IsValid,
            ["defaultDuration"] = defaults.transitionSeconds,
            ["defaultClearRadius"] = defaults.clearRadius, ["defaultOpaqueRadius"] = defaults.opaqueRadius,
            ["screenClearRadius"] = DenseFogPresentationTransition.ResolveOutsideRadius(
                new Vector2((float)Number(args, "originX", -3), (float)Number(args, "originY", -4)),
                new Vector2((float)Number(args, "rightX", 6), (float)Number(args, "rightY", 0)),
                new Vector2((float)Number(args, "upX", 0), (float)Number(args, "upY", 8)))
        };
    }

    #endregion
}
