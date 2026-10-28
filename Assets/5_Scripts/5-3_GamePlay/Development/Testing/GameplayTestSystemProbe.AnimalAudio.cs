using System;
using System.Collections.Generic;
using FlatWorld.Audio;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 动物待机叫声连续调度采样

    private static void RegisterAnimalAudioOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("audio/animal_idle_sequence", ServiceAnimalIdleSequence);
        operations.Add("audio/propagation_ranges", ServiceAudioPropagationRanges);
    }

    #region JSON 传播档位采样

    [Serializable]
    private sealed class PropagationCatalogDocument
    {
        public AudioCue[] cues;
        public TextAsset propagationRanges;
    }

    private static JObject ServiceAudioPropagationRanges(JObject args)
    {
        var first = ScriptableObject.CreateInstance<AudioCue>();
        var second = ScriptableObject.CreateInstance<AudioCue>();
        var catalog = ScriptableObject.CreateInstance<AudioCatalog>();
        TextAsset propagation = args["propagation"] is JObject configuration
            ? new TextAsset(configuration.ToString()) : null;
        first.hideFlags = second.hideFlags = catalog.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            // 独立 Cue 通过正式目录解析 JSON，采样重建前后的实际距离，不在夹具内计算档位。
            JsonUtility.FromJsonOverwrite(new JObject { ["id"] = Text(args, "first", "probe.first") }.ToString(), first);
            JsonUtility.FromJsonOverwrite(new JObject { ["id"] = Text(args, "second", "probe.second") }.ToString(), second);
            var document = new PropagationCatalogDocument
            {
                cues = new[] { first, second },
                propagationRanges = propagation != null ? propagation : Resources.Load<TextAsset>("Audio/PropagationRanges")
            };
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(document), catalog);
            catalog.RebuildIndex();
            float firstMaxDistance = first.MaxDistance, secondMaxDistance = second.MaxDistance;
            document.propagationRanges = null;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(document), catalog);
            catalog.RebuildIndex();
            return new JObject
            {
                ["firstMaxDistance"] = firstMaxDistance, ["secondMaxDistance"] = secondMaxDistance,
                ["firstResetMaxDistance"] = first.MaxDistance, ["secondResetMaxDistance"] = second.MaxDistance
            };
        }
        finally
        {
            if (propagation != null) UnityEngine.Object.DestroyImmediate(propagation);
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(second);
            UnityEngine.Object.DestroyImmediate(first);
        }
    }

    #endregion

    private static JObject ServiceAnimalIdleSequence(JObject args)
    {
        if (args["steps"] is not JArray steps || steps.Count == 0 || steps.Count > 128)
            throw new ArgumentException("动物待机叫声采样需要 1 至 128 个连续步骤。");

        var states = new Dictionary<int, AnimalAmbientAudioSchedule>();
        var samples = new JArray();
        UnityEngine.Random.State beforeRandom = UnityEngine.Random.state;
        foreach (JToken value in steps)
        {
            if (value is not JObject step)
                throw new ArgumentException("动物待机叫声采样步骤必须是对象。");

            double slotValue = Number(step, "slot", 1);
            if (slotValue != Math.Truncate(slotValue) || slotValue < 1 || slotValue > 8)
                throw new ArgumentException("动物待机叫声采样槽位必须为 1 至 8 的整数。");
            int slot = (int)slotValue;
            if (!states.TryGetValue(slot, out AnimalAmbientAudioSchedule state))
            {
                state = new AnimalAmbientAudioSchedule();
                states.Add(slot, state);
            }

            float beforeRemaining = state.RemainingSeconds;
            bool beforeInitialized = state.IsInitialized;
            float deltaTime = (float)Number(step, "deltaTime", 0);
            bool due = false;
            // 只编排正式状态机并返回原始计时，不在夹具中重算随机间隔或发声资格。
            if (Flag(step, "clear", false))
                state.Clear();
            else if (Flag(step, "reset", false))
            {
                double seed = Number(step, "seed", 1);
                if (seed != Math.Truncate(seed) || seed < 0 || seed > uint.MaxValue)
                    throw new ArgumentException("动物待机叫声种子必须为有效无符号整数。");
                state.Reset((uint)seed,
                    (float)Number(step, "intervalMinSeconds", 10),
                    (float)Number(step, "intervalMaxSeconds", 10),
                    (float)Number(step, "initialDelayMinSeconds", 5),
                    (float)Number(step, "initialDelayMaxSeconds", 5));
            }
            else
            {
                if (Flag(step, "untilDue", false)) deltaTime = state.RemainingSeconds;
                due = state.Advance(deltaTime,
                    Flag(step, "eligible", true), Flag(step, "alreadyPlaying", false));
            }

            samples.Add(new JObject
            {
                ["slot"] = slot, ["deltaTime"] = deltaTime, ["due"] = due,
                ["beforeInitialized"] = beforeInitialized, ["beforeRemaining"] = beforeRemaining,
                ["initialized"] = state.IsInitialized, ["remaining"] = state.RemainingSeconds
            });
        }

        return new JObject
        {
            ["samples"] = samples,
            ["randomUnchanged"] = beforeRandom.Equals(UnityEngine.Random.state)
        };
    }

    #endregion
}
