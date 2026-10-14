#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>台词程序集自己登记规则采样，游戏核心不反向依赖显示系统。</summary>
    public static class GameplayTestDialogueRules
    {
        #region 测试操作注册

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterRuntime() => Register();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif

        public static void Register()
        {
            GameplayTestSystemProbe.Register("dialogue", "condition", CaptureCondition);
            GameplayTestSystemProbe.Register("dialogue", "config", CaptureConfiguration);
        }

        #endregion

        #region 正式条件与配置采样

        private static JObject CaptureCondition(JObject args)
        {
            string actual = GameplayTestSystemProbe.Text(args, "actual", "0.5");
            string expected = GameplayTestSystemProbe.Text(args, "expected", "0.3");
            string op = GameplayTestSystemProbe.Text(args, "operator", "Greater");
            var condition = new CharacterSpeechCondition
            {
                Fact = "probe.value", Value = expected,
                Operator = (CharacterSpeechConditionOperator)Enum.Parse(typeof(CharacterSpeechConditionOperator), op)
            };
            var entry = new CharacterSpeechConfigEntry { Id = "probe", Conditions = new List<CharacterSpeechCondition> { condition } };
            var context = new CharacterSpeechContext(null, CharacterSpeechTrigger.Debug, 0);
            if (GameplayTestSystemProbe.Flag(args, "exists", true)) context.SetFact(condition.Fact, actual);
            return new JObject { ["matched"] = CharacterSpeechConditionEvaluator.EvaluateAll(entry, context) };
        }

        private static JObject CaptureConfiguration(JObject args)
        {
            // 构造私有配置文本，由生产加载器负责 schema、Fact、重复 ID 与优先级。
            string json = GameplayTestSystemProbe.Text(args, "json", "{}");
            var sources = new[] { new CharacterSpeechConfigSource("Lua 系统回归.json", json) };
            CharacterSpeechConfigLoadResult loaded = CharacterSpeechConfigLoader.LoadSources(sources, logIssues: false);
            var ids = new JArray();
            foreach (CharacterSpeechConfigEntry entry in loaded.Entries) ids.Add(entry.Id);
            return new JObject { ["ids"] = ids, ["count"] = loaded.Entries.Count, ["issues"] = loaded.Issues.Count };
        }

        #endregion
    }
}
#endif
