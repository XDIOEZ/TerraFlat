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
            GameplayTestSystemProbe.Register("dialogue", "help_catalog", CaptureHelpCatalog);
            GameplayTestSystemProbe.Register("dialogue", "help_command", CaptureHelpCommand);
            GameplayTestSystemProbe.Register("dialogue", "stamina_rejection", CaptureStaminaRejection);
#if UNITY_EDITOR
            GameplayTestSystemProbe.Register("player", "collider_visual_alignment", PlayerColliderVisualAlignment);
#endif
        }

        #endregion

#if UNITY_EDITOR
        #region 玩家预制体对齐采样

        // 校验玩家预制体的受击范围跟随人物底锚点画布，脚下实体阻挡框仍保持独立。
        private static JObject PlayerColliderVisualAlignment(JObject args)
        {
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Gameplay/Player/Player.prefab");
            Transform visual = prefab != null ? prefab.transform.Find("RollVisual/Module_Animator") : null;
            Transform receiver = prefab != null ? prefab.transform.Find("Module_DamageReciver") : null;
            Transform heldAnchor = prefab != null ? prefab.transform.Find("Module_Hotbar") : null;
            PlayerOxygenWorldBar oxygenBar = prefab != null ? prefab.GetComponentInChildren<PlayerOxygenWorldBar>(true) : null;
            RectTransform oxygenBarRect = oxygenBar != null ? oxygenBar.GetComponent<RectTransform>() : null;
            Mod_CharacterAppearance appearance = prefab != null
                ? prefab.GetComponentInChildren<Mod_CharacterAppearance>(true) : null;
            Mod_TurnBack turning = prefab != null ? prefab.GetComponentInChildren<Mod_TurnBack>(true) : null;
            ScreenSpaceSpeechBubblePresenter speech = prefab != null
                ? prefab.GetComponent<ScreenSpaceSpeechBubblePresenter>() : null;
            BoxCollider2D body = prefab != null ? prefab.GetComponent<BoxCollider2D>() : null;
            BoxCollider2D hitbox = receiver != null ? receiver.GetComponent<BoxCollider2D>() : null;
            if (visual == null || receiver == null || heldAnchor == null || turning == null ||
                speech == null ||
                oxygenBarRect == null || oxygenBarRect.parent != prefab.transform ||
                body == null || hitbox == null ||
                appearance?.profile == null || appearance.profile.PixelsPerUnit <= 0f)
                throw new InvalidOperationException("玩家预制体缺少外观、实体碰撞或独立受击框。");

            float spriteBottom = visual.position.y - prefab.transform.position.y;
            float spriteHeight = appearance.profile.Height / appearance.profile.PixelsPerUnit * visual.lossyScale.y;
            float spriteWidth = appearance.profile.Width / appearance.profile.PixelsPerUnit * visual.lossyScale.x;
            float rollPivotY = visual.parent.position.y - prefab.transform.position.y;
            float bubbleY = new UnityEditor.SerializedObject(speech).FindProperty("worldOffset").vector3Value.y;
            float hitCenterY = receiver.TransformPoint(hitbox.offset).y - prefab.transform.position.y;
            float hitHalfHeight = hitbox.size.y * receiver.lossyScale.y * 0.5f;
            return new JObject
            {
                ["spriteBottom"] = spriteBottom,
                ["spriteTop"] = spriteBottom + spriteHeight,
                ["spriteWidth"] = spriteWidth,
                ["rollPivotY"] = rollPivotY,
                ["heldAnchorY"] = heldAnchor.position.y - prefab.transform.position.y,
                ["turnMirrorY"] = turning.DefaultPosition.y,
                ["bubbleY"] = bubbleY,
                ["oxygenBarY"] = oxygenBarRect.anchoredPosition.y,
                ["oxygenBarOffsetFromSpriteTop"] = oxygenBarRect.anchoredPosition.y - (spriteBottom + spriteHeight),
                ["hitBottom"] = hitCenterY - hitHalfHeight,
                ["hitTop"] = hitCenterY + hitHalfHeight,
                ["hitWidth"] = hitbox.size.x * receiver.lossyScale.x,
                ["hitTrigger"] = hitbox.isTrigger,
                ["bodyTrigger"] = body.isTrigger,
                ["bodyBottom"] = body.offset.y - body.size.y * 0.5f,
                ["bodyTop"] = body.offset.y + body.size.y * 0.5f,
                ["bodyCenter"] = body.offset.y,
                ["bodyWidth"] = body.size.x
            };
        }

        #endregion
#endif

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
            var externalEntries = new JArray();
            int translated = 0;
            int lines = 0;
            foreach (CharacterSpeechConfigEntry entry in loaded.Entries)
            {
                if (entry.Triggers.Contains(CharacterSpeechTrigger.External))
                    externalEntries.Add(new JObject
                    {
                        ["id"] = entry.Id, ["topic"] = entry.Topic,
                        ["externalOnly"] = entry.Triggers.TrueForAll(trigger => trigger == CharacterSpeechTrigger.External),
                        ["priority"] = entry.Priority.ToString(), ["cooldown"] = entry.Cooldown,
                        ["lines"] = new JArray(entry.Lines), ["conditions"] = entry.Conditions.Count
                    });
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
                ["incompleteIds"] = incompleteIds,
                ["externalEntries"] = externalEntries
            };
        }

        #endregion

        #region 体力不足事件采样

        // 使用未加载世界的独立体力模块，核对拒绝事件和实际扣费结果。
        private static JObject CaptureStaminaRejection(JObject args)
        {
            GameObject root = new("体力不足规则采样") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            Mod_Stamina stamina = root.AddComponent<Mod_Stamina>();
            stamina.Data = new Mod_Stamina.StaminaData
            {
                CurrentStamina = (float)GameplayTestSystemProbe.Number(args, "stamina", 0),
                MaxStamina = 100f
            };
            int rejected = 0;
            int consumed = 0;
            string rejectedSource = string.Empty;
            void HandleRejected(Mod_Stamina sender, string source)
            {
                if (sender != stamina) return;
                rejected++;
                rejectedSource = source;
            }
            void HandleConsumed(StaminaConsumptionRecord record) => consumed++;
            Mod_Stamina.ConsumptionRejected += HandleRejected;
            stamina.StaminaConsumed += HandleConsumed;
            try
            {
                float cost = (float)GameplayTestSystemProbe.Number(args, "cost", 1);
                string source = GameplayTestSystemProbe.Text(args, "source", ManualHarvestWork.StaminaSourceId);
                bool continuous = GameplayTestSystemProbe.Flag(args, "continuous", false);
                bool accepted = continuous || stamina.TryConsumeStamina(source, cost);
                if (continuous) stamina.ConsumeStaminaPerSecond(source, cost, 1f);
                return new JObject
                {
                    ["accepted"] = accepted, ["stamina"] = stamina.CurrentValue,
                    ["rejected"] = rejected, ["consumed"] = consumed, ["source"] = rejectedSource
                };
            }
            finally
            {
                Mod_Stamina.ConsumptionRejected -= HandleRejected;
                stamina.StaminaConsumed -= HandleConsumed;
                UnityEngine.Object.DestroyImmediate(root);
            }
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

        #region 命令手册规则采样

        /// <summary>收集正式聊天说明，并用临时提供方采样注册替换、排序、去重与完整书页。</summary>
        private static JObject CaptureHelpCatalog(JObject args)
        {
            PlayerHelpChatCommand.RegisterHelpEntries();
            PlayerCarryWeightChatCommand.RegisterHelpEntries();
            const string firstProvider = "dialogue.rule.help.first";
            const string secondProvider = "dialogue.rule.help.second";
            const string category = "__dialogue_help_rule";
            List<string> probeUsages = new();
            try
            {
                PlayerHelpCatalog.RegisterProvider(firstProvider,
                    () => new[] { new PlayerHelpEntry(category, "旧条目", "/old", "应被替换。") });
                PlayerHelpCatalog.RegisterProvider(firstProvider,
                    () => new[] { new PlayerHelpEntry(category, "后项", "/z", "完整说明 Z。") });
                PlayerHelpCatalog.RegisterProvider(secondProvider, () => new[]
                {
                    new PlayerHelpEntry(category, "后项", "/z", "完整说明 Z。"),
                    new PlayerHelpEntry(category, "前项", "/a", "完整说明 A。")
                });
                foreach (PlayerHelpEntry entry in PlayerHelpCatalog.Collect())
                    if (entry.Category == category)
                        probeUsages.Add(entry.Usage);
            }
            finally
            {
                PlayerHelpCatalog.UnregisterProvider(firstProvider);
                PlayerHelpCatalog.UnregisterProvider(secondProvider);
            }

            IReadOnlyList<PlayerHelpEntry> entries;
            try
            {
                // 主动混入非聊天入口，确保精简目录不会被运行环境中的 GM 登记影响。
                PlayerHelpCatalog.RegisterProvider(firstProvider, () => new[]
                {
                    new PlayerHelpEntry("管理员操作", "打开面板", "F4", "打开 GM 面板。"),
                    new PlayerHelpEntry("调试快捷键", "调试操作", "/debug-probe", "非聊天命令，不能只按斜杠筛选。")
                });
                entries = PlayerHelpCatalog.Collect("聊天命令");
            }
            finally { PlayerHelpCatalog.UnregisterProvider(firstProvider); }
            IReadOnlyList<ReadableBookPageContent> pages = PlayerHelpChatCommand.BuildPages(entries);
            JArray entryData = new();
            JArray pageData = new();
            foreach (PlayerHelpEntry entry in entries)
                entryData.Add(new JObject
                {
                    ["category"] = entry.Category, ["title"] = entry.Title,
                    ["usage"] = entry.Usage, ["description"] = entry.Description
                });
            foreach (ReadableBookPageContent page in pages)
                pageData.Add(new JObject
                {
                    ["index"] = page.PageIndex, ["title"] = page.Title, ["body"] = page.Body,
                    ["customTitle"] = page.HasCustomTitle, ["customBody"] = page.HasCustomBody
                });
            return new JObject { ["probeUsages"] = new JArray(probeUsages), ["entries"] = entryData, ["pages"] = pageData };
        }

        /// <summary>只检查正式帮助命令的参数与无玩家拒绝，不加载资源或执行发书事务。</summary>
        private static JObject CaptureHelpCommand(JObject args)
        {
            GameObject root = new("帮助命令规则采样") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            try
            {
                PlayerHelpChatCommand command = root.AddComponent<PlayerHelpChatCommand>();
                PlayerChatCommandResult result = command.Execute(new PlayerChatCommandContext(null, null, string.Empty,
                    GameplayTestSystemProbe.Text(args, "command", PlayerHelpChatCommand.CommandName),
                    args["arguments"] is JArray tokens ? tokens.ToObject<string[]>() : Array.Empty<string>()));
                return new JObject { ["handled"] = result.Handled, ["feedback"] = result.Feedback };
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        #endregion
    }
}
#endif
