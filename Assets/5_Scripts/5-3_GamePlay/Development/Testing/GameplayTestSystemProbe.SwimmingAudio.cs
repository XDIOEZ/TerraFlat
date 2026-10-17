using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 游泳音效连续状态采样

    private static void RegisterSwimmingAudioOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("audio/swimming_sequence", ServiceSwimmingSequence);
        operations.Add("audio/water_depth_volume", ServiceWaterDepthVolume);
    }

    private static JObject ServiceWaterDepthVolume(JObject args)
    {
        float depth = (float)Number(args, "depth", 0);
        return new JObject
        {
            ["volume"] = Mod_Mover.ResolveWaterAudioVolume(depth),
            ["wetFootstepVolume"] = Mod_Mover.ResolveFootstepDepthVolume(FlatWorld.Audio.AudioEventIds.FootstepWater, depth),
            ["dryFootstepVolume"] = Mod_Mover.ResolveFootstepDepthVolume(FlatWorld.Audio.AudioEventIds.FootstepGrass, depth)
        };
    }

    private static JObject ServiceSwimmingSequence(JObject args)
    {
        if (args["steps"] is not JArray steps || steps.Count == 0 || steps.Count > 128)
            throw new ArgumentException("游泳音效采样需要 1 至 128 个连续步骤。");

        var state = new Mod_Mover.SwimmingAudioState();
        var samples = new JArray();
        foreach (JToken value in steps)
        {
            if (value is not JObject step)
                throw new ArgumentException("游泳音效采样步骤必须是对象。");

            // 连续驱动正式状态机并返回原始结果，入出水和循环包络由 Lua 断言。
            string cue = null;
            if (Flag(step, "reset", false))
                state.Reset();
            else
                cue = state.Advance(
                    (float)Number(step, "depth", 0),
                    (float)Number(step, "immersion", 0),
                    Flag(step, "waterContact", true),
                    Flag(step, "moving", false),
                    (float)Number(step, "distance", 0),
                    (float)Number(step, "deltaTime", 0.1));
            samples.Add(new JObject
            {
                ["cue"] = cue ?? string.Empty,
                ["hasBaseline"] = state.HasBaseline,
                ["inWater"] = state.InWater,
                ["shouldPlayLoop"] = state.ShouldPlayLoop,
                ["loopVolume"] = state.LoopVolume,
                ["waterVolume"] = state.WaterVolume,
                ["transitionVolume"] = state.TransitionVolume
            });
        }
        return new JObject { ["samples"] = samples };
    }

    #endregion
}
