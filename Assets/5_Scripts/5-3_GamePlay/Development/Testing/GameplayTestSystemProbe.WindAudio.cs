using System;
using FlatWorld.Audio;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 风声音色与连续包络采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterWindAudioRules() => Register("audio", "wind_sequence", ServiceWindSequence);

    private static JObject ServiceWindSequence(JObject args)
    {
        if (args["steps"] is not JArray steps || steps.Count == 0 || steps.Count > 128)
            throw new ArgumentException("风声音效采样需要 1 至 128 个连续步骤。");

        var state = new WeatherMgr.WindAudioState();
        var samples = new JArray();
        foreach (JToken value in steps)
        {
            if (value is not JObject step)
                throw new ArgumentException("风声音效采样步骤必须是对象。");

            // 只驱动正式风声状态，不创建声源，也不读取或改变当前世界天气。
            if (Flag(step, "reset", false))
                state.Reset();
            else
                state.Advance((float)Number(step, "strength", 0),
                    (float)Number(step, "deltaTime", 10),
                    (float)Number(step, "transitionSeconds", 3));
            samples.Add(new JObject
            {
                ["cue"] = state.IsGale ? AudioEventIds.WeatherWindGaleLoop : AudioEventIds.WeatherWindLoop,
                ["gale"] = state.IsGale,
                ["presentedStrength"] = state.PresentedStrength,
                ["normalVolume"] = state.NormalVolume,
                ["galeVolume"] = state.GaleVolume
            });
        }
        return new JObject { ["samples"] = samples };
    }

    #endregion
}
