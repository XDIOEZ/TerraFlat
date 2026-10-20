using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 游泳音效连续状态采样

    private static void RegisterSwimmingAudioOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("audio/swimming_sequence", ServiceSwimmingSequence);
        operations.Add("audio/water_depth_volume", ServiceWaterDepthVolume);
        operations.Add("audio/motion_audio_spatial", ServiceMotionAudioSpatial);
    }

    #region 脚步与游泳共用世界坐标域回归

    // 正式物理帧快照验证环绕、独立声源基线与世界切换后的实时归一化。
    private static JObject ServiceMotionAudioSpatial(JObject args)
    {
        var wrapped = new WorldTopologyDomain(new int2(-16, -16), new int2(32, 32), true);
        var position = new Vector2(81.25f, -81.75f);
        var frame = new Mod_Mover.MotionAudioSpatialFrame(position, wrapped);
        Vector2 footstepDelta = frame.DeltaFrom(new Vector2(15.5f, 14.25f));
        Vector2 swimmingDelta = frame.DeltaFrom(new Vector2(-15.5f, 14.25f));
        var infiniteFrame = new Mod_Mover.MotionAudioSpatialFrame(position, default);
        var reverseSeam = new Mod_Mover.MotionAudioSpatialFrame(new Vector2(-16.25f, 0f), wrapped);
        var halfPeriod = new Mod_Mover.MotionAudioSpatialFrame(new Vector2(16f, 0f), wrapped);

        return new JObject
        {
            ["wrappedPosition"] = frame.Position == new Vector2(-14.75f, 14.25f),
            ["footstepDelta"] = footstepDelta == new Vector2(1.75f, 0f),
            ["swimmingDelta"] = swimmingDelta == new Vector2(.75f, 0f),
            ["independentBaseline"] = footstepDelta != swimmingDelta,
            ["infinitePosition"] = infiniteFrame.Position == position,
            ["infiniteDelta"] = infiniteFrame.DeltaFrom(new Vector2(15.5f, 14.25f)) ==
                new Vector2(65.75f, -96f),
            ["negativeSeam"] = reverseSeam.Position == new Vector2(15.75f, 0f) &&
                reverseSeam.DeltaFrom(new Vector2(-15.75f, 0f)) == new Vector2(-.5f, 0f),
            ["halfPeriodTie"] = halfPeriod.DeltaFrom(Vector2.zero) == new Vector2(-16f, 0f),
            ["stationary"] = frame.DeltaFrom(frame.Position) == Vector2.zero
        };
    }

    #endregion

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
