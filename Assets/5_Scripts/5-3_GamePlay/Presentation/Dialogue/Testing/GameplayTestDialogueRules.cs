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
            GameplayTestSystemProbe.Register("dialogue", "builtin_locales", CaptureBuiltinLocales);
            GameplayTestSystemProbe.Register("dialogue", "carry_weight_command", CaptureCarryWeightCommand);
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

        /// <summary>核对正式台词资源是否为每句中文提供了可用的英文译文。</summary>
        private static JObject CaptureBuiltinLocales(JObject args)
        {
            CharacterSpeechConfigLoadResult loaded = CharacterSpeechConfigLoader.LoadFromResources(logIssues: false);
            var incompleteIds = new JArray();
            int translated = 0;
            int lines = 0;
            foreach (CharacterSpeechConfigEntry entry in loaded.Entries)
            {
                lines += entry.Lines.Count;
                List<string> english = null;
                bool complete = entry.LocalizedLines != null &&
                    entry.LocalizedLines.TryGetValue("en", out english) &&
                    english != null &&
                    english.Count == entry.Lines.Count;
                if (complete)
                {
                    for (int i = 0; i < english.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(english[i]))
                        {
                            complete = false;
                            break;
                        }
                        foreach (char character in english[i])
                        {
                            if (character >= '\u4e00' && character <= '\u9fff')
                            {
                                complete = false;
                                break;
                            }
                        }
                        if (!complete)
                            break;
                    }
                }
                if (complete)
                    translated += english.Count;
                else
                    incompleteIds.Add(entry.Id);
            }

            return new JObject
            {
                ["entries"] = loaded.Entries.Count,
                ["lines"] = lines,
                ["translatedLines"] = translated,
                ["issues"] = loaded.Issues.Count,
                ["incompleteIds"] = incompleteIds
            };
        }

        #endregion

        #region 携带容量命令规则采样

        /// <summary>调用正式解析与处理器，只构造未加载模块的临时玩家并采样拒绝后的原值。</summary>
        private static JObject CaptureCarryWeightCommand(JObject args)
        {
            string[] arguments = args["arguments"] is JArray tokens
                ? tokens.ToObject<string[]>()
                : Array.Empty<string>();
            bool parsed = PlayerCarryWeightChatCommand.TryParseCapacity(arguments, out float capacity);
            GameObject root = new("携带容量命令规则采样") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            Player player = root.AddComponent<Player>();
            try
            {
                player.BindData(new Data_Player());
                player.SetProfileContext(true, false, "carry_weight_rule");
                PlayerCarryWeightChatCommand command = root.AddComponent<PlayerCarryWeightChatCommand>();
                PlayerChatCommandResult result = command.Execute(new PlayerChatCommandContext(
                    GameplayTestSystemProbe.Flag(args, "withPlayer", false) ? player : null,
                    null, string.Empty, GameplayTestSystemProbe.Text(args, "command", "carryweight"), arguments));
                return new JObject
                {
                    ["parsed"] = parsed,
                    ["capacity"] = parsed ? capacity : 0f,
                    ["handled"] = result.Handled,
                    ["feedback"] = result.Feedback,
                    ["playerWeight"] = player.Data.MaxCarryWeight,
                    ["playerVolume"] = player.Data.MaxCarryVolume
                };
            }
            finally
            {
                try { player.PrepareForDespawn(saveData: false); }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        #endregion
    }
}
#endif
