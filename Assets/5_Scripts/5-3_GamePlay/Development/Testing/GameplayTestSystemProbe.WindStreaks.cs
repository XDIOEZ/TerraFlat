using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 风条纹正式规则采样

    private static void RegisterWindStreakOperations(Dictionary<string, Func<JObject, JObject>> operations)
        => operations.Add("rendering/wind_streaks", ServiceWindStreaks);

    private static JObject ServiceWindStreaks(JObject args)
    {
        var config = JsonUtility.FromJson<WorldRenderingConfig.RenderingWindStreaks>(
            JsonUtility.ToJson(WorldRenderingConfigCatalog.Default.windStreaks));
        if (args["config"] is JObject patch)
            JsonUtility.FromJsonOverwrite(patch.ToString(), config);
        var state = new WindStreakPresentation();
        var samples = new JArray();
        if (args["steps"] is JArray steps)
        {
            // 只采正式参数解析与相位积分，风向、暂停和镜头移动的预期由 Lua 判断。
            foreach (JObject step in steps)
            {
                if (Flag(step, "reset", false)) state.Reset();
                else state.Step(new Vector2(
                        Flag(step, "invalidWind", false) ? float.NaN : (float)Number(step, "x", 0.85),
                        (float)Number(step, "y", 0)),
                    new Vector2((float)Number(step, "cameraX", 0), (float)Number(step, "cameraY", 0)),
                    Flag(step, "invalidSeconds", false) ? float.NaN : (float)Number(step, "seconds", 0), config);
                samples.Add(new JObject
                {
                    ["visible"] = state.IsVisible,
                    ["directionX"] = state.Direction.x, ["directionY"] = state.Direction.y,
                    ["phaseX"] = state.Phase.x, ["phaseY"] = state.Phase.y,
                    ["speed"] = state.Parameters.x, ["opacity"] = state.Parameters.y,
                    ["density"] = state.Parameters.z, ["length"] = state.Parameters.w
                });
            }
        }
        return new JObject
        {
            ["validConfig"] = config.IsValid, ["samples"] = samples,
            ["cellX"] = config.cellSize.x, ["cellY"] = config.cellSize.y,
            ["period"] = WindStreakPresentation.PatternRepeatCells
        };
    }

    #endregion
}
