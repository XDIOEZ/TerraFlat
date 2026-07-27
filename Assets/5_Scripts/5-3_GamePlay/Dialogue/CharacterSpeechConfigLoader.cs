using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 从项目内 Resources 加载、合并并校验全部自言自语 JSON。
    /// 单个坏文件或坏条目只会被跳过，不会阻断其他有效配置。
    /// </summary>
    public static class CharacterSpeechConfigLoader
    {
        public const int SupportedSchemaVersion = 1;
        public const string ResourcePath = "Dialogue/Soliloquy";

        private static readonly HashSet<string> KnownFacts = new(StringComparer.Ordinal)
        {
            CharacterSpeechFacts.HungerRate,
            CharacterSpeechFacts.HungerTier,
            CharacterSpeechFacts.HungerIsTakingDamage
        };

        #region 公共加载入口

        public static CharacterSpeechConfigLoadResult LoadFromResources(bool logIssues = true)
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>(ResourcePath);
            Array.Sort(assets, CompareTextAssets);

            List<CharacterSpeechConfigSource> sources = new(assets.Length);
            for (int i = 0; i < assets.Length; i++)
            {
                TextAsset asset = assets[i];
                sources.Add(new CharacterSpeechConfigSource($"{asset.name}.json", asset.text));
            }

            return LoadSources(sources, logIssues);
        }

        public static CharacterSpeechConfigLoadResult LoadSources(
            IEnumerable<CharacterSpeechConfigSource> sources,
            bool logIssues = true)
        {
            CharacterSpeechConfigLoadResult result = new();
            if (sources == null)
            {
                AddIssue(result, "<配置集合>", "<无>", string.Empty, "配置来源为空。", logIssues);
                return result;
            }

            List<CharacterSpeechConfigSource> orderedSources = new(sources);
            orderedSources.Sort(CompareSources);

            Dictionary<string, CharacterSpeechConfigEntry> entriesById =
                new(StringComparer.Ordinal);
            Dictionary<string, CharacterSpeechConfigEntry> entriesByCompletionFlag =
                new(StringComparer.Ordinal);

            for (int sourceIndex = 0; sourceIndex < orderedSources.Count; sourceIndex++)
            {
                LoadSource(
                    orderedSources[sourceIndex],
                    entriesById,
                    entriesByCompletionFlag,
                    result,
                    logIssues);
            }

            result.Entries.Sort(CompareEntries);
            return result;
        }

        #endregion

        #region 单文件解析

        private static void LoadSource(
            CharacterSpeechConfigSource source,
            Dictionary<string, CharacterSpeechConfigEntry> entriesById,
            Dictionary<string, CharacterSpeechConfigEntry> entriesByCompletionFlag,
            CharacterSpeechConfigLoadResult result,
            bool logIssues)
        {
            JObject root;
            try
            {
                root = JObject.Parse(source.Text);
            }
            catch (Exception exception)
            {
                AddIssue(
                    result,
                    source.Name,
                    "<文件>",
                    string.Empty,
                    $"JSON 无法反序列化：{exception.Message}",
                    logIssues);
                return;
            }

            if (!int.TryParse(
                    root["schemaVersion"]?.ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int schemaVersion) ||
                schemaVersion != SupportedSchemaVersion)
            {
                AddIssue(
                    result,
                    source.Name,
                    "<文件>",
                    string.Empty,
                    $"不支持 schemaVersion={schemaVersion}，当前仅支持 {SupportedSchemaVersion}。",
                    logIssues);
                return;
            }

            if (root["entries"] is not JArray entriesToken)
            {
                AddIssue(
                    result,
                    source.Name,
                    "<文件>",
                    string.Empty,
                    "entries 必须是数组。",
                    logIssues);
                return;
            }

            JsonSerializer serializer = CreateSerializer();
            for (int entryIndex = 0; entryIndex < entriesToken.Count; entryIndex++)
            {
                if (entriesToken[entryIndex] is not JObject entryObject)
                {
                    AddIssue(
                        result,
                        source.Name,
                        $"<索引 {entryIndex}>",
                        string.Empty,
                        "条目必须是 JSON 对象。",
                        logIssues);
                    continue;
                }

                string entryId = entryObject.Value<string>("id") ?? $"<索引 {entryIndex}>";
                if (!ValidateRawEnums(
                        source.Name,
                        entryId,
                        entryObject,
                        result,
                        logIssues))
                {
                    continue;
                }

                CharacterSpeechConfigEntry entry;
                try
                {
                    entry = entryObject.ToObject<CharacterSpeechConfigEntry>(serializer);
                }
                catch (Exception exception)
                {
                    string factName = TryReadFactName(entryObject);
                    AddIssue(
                        result,
                        source.Name,
                        entryId,
                        factName,
                        $"条目无法反序列化：{exception.Message}",
                        logIssues);
                    continue;
                }

                if (entry == null)
                {
                    AddIssue(
                        result,
                        source.Name,
                        entryId,
                        string.Empty,
                        "条目反序列化结果为空。",
                        logIssues);
                    continue;
                }

                entry.SourceName = source.Name;
                if (!ValidateEntry(entry, result, logIssues))
                    continue;

                if (entriesById.TryGetValue(entry.Id, out CharacterSpeechConfigEntry duplicate))
                {
                    AddIssue(
                        result,
                        source.Name,
                        entry.Id,
                        string.Empty,
                        $"ID 与文件 '{duplicate.SourceName}' 中的条目重复。",
                        logIssues);
                    continue;
                }

                if (!ValidateCompletionFlagConflict(
                        entry,
                        entriesByCompletionFlag,
                        result,
                        logIssues))
                {
                    continue;
                }

                entriesById.Add(entry.Id, entry);
                if (!string.IsNullOrWhiteSpace(entry.CompletionFlag) &&
                    !entriesByCompletionFlag.ContainsKey(entry.CompletionFlag))
                {
                    entriesByCompletionFlag.Add(entry.CompletionFlag, entry);
                }

                if (entry.Enabled)
                    result.Entries.Add(entry);
            }
        }

        #endregion

        #region 条目校验

        private static bool ValidateRawEnums(
            string sourceName,
            string entryId,
            JObject entryObject,
            CharacterSpeechConfigLoadResult result,
            bool logIssues)
        {
            bool valid = true;
                if (!TryParseDefinedEnum(
                    entryObject["priority"],
                    out CharacterSpeechPriority _))
            {
                AddIssue(
                    result,
                    sourceName,
                    entryId,
                    string.Empty,
                    "priority 无效。",
                    logIssues);
                valid = false;
            }

            if (entryObject["triggers"] is JArray triggers)
            {
                for (int i = 0; i < triggers.Count; i++)
                {
                    if (TryParseDefinedEnum(
                            triggers[i],
                            out CharacterSpeechTrigger trigger) &&
                        (trigger == CharacterSpeechTrigger.Idle ||
                         trigger == CharacterSpeechTrigger.StateChanged))
                    {
                        continue;
                    }

                    AddIssue(
                        result,
                        sourceName,
                        entryId,
                        string.Empty,
                        $"trigger '{triggers[i]}' 无效，配置仅支持 Idle 与 StateChanged。",
                        logIssues);
                    valid = false;
                }
            }

            if (entryObject["conditions"] is not JArray conditions)
                return valid;

            for (int i = 0; i < conditions.Count; i++)
            {
                if (conditions[i] is not JObject condition)
                    continue;

                string factName = condition.Value<string>("fact") ?? string.Empty;
                if (TryParseDefinedEnum(
                        condition["operator"],
                        out CharacterSpeechConditionOperator conditionOperator))
                {
                    if (conditionOperator == CharacterSpeechConditionOperator.Exists ||
                        conditionOperator == CharacterSpeechConditionOperator.NotExists ||
                        condition.ContainsKey("value"))
                    {
                        continue;
                    }

                    AddIssue(
                        result,
                        sourceName,
                        entryId,
                        factName,
                        $"操作符 {conditionOperator} 必须提供 value。",
                        logIssues);
                    valid = false;
                    continue;
                }

                AddIssue(
                    result,
                    sourceName,
                    entryId,
                    factName,
                    "condition.operator 无效。",
                    logIssues);
                valid = false;
            }

            return valid;
        }

        private static bool ValidateEntry(
            CharacterSpeechConfigEntry entry,
            CharacterSpeechConfigLoadResult result,
            bool logIssues)
        {
            bool valid = true;
            if (string.IsNullOrWhiteSpace(entry.Id))
                valid &= Report(entry, string.Empty, "entry.id 不能为空。", result, logIssues);

            if (!Enum.IsDefined(typeof(CharacterSpeechPriority), entry.Priority))
                valid &= Report(entry, string.Empty, "priority 无效。", result, logIssues);

            if (entry.Triggers == null || entry.Triggers.Count == 0)
            {
                valid &= Report(entry, string.Empty, "triggers 不能为空。", result, logIssues);
            }
            else
            {
                for (int i = 0; i < entry.Triggers.Count; i++)
                {
                    CharacterSpeechTrigger trigger = entry.Triggers[i];
                    if (trigger != CharacterSpeechTrigger.Idle &&
                        trigger != CharacterSpeechTrigger.StateChanged)
                    {
                        valid &= Report(
                            entry,
                            string.Empty,
                            $"trigger '{trigger}' 无效，配置仅支持 Idle 与 StateChanged。",
                            result,
                            logIssues);
                    }
                }
            }

            if (entry.Conditions == null || entry.Conditions.Count == 0)
            {
                valid &= Report(entry, string.Empty, "conditions 不能为空。", result, logIssues);
            }
            else
            {
                for (int i = 0; i < entry.Conditions.Count; i++)
                    valid &= ValidateCondition(entry, entry.Conditions[i], result, logIssues);
            }

            if (entry.Lines == null || entry.Lines.Count == 0)
            {
                valid &= Report(entry, string.Empty, "lines 不能为空。", result, logIssues);
            }
            else
            {
                for (int i = 0; i < entry.Lines.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(entry.Lines[i]))
                    {
                        valid &= Report(
                            entry,
                            string.Empty,
                            $"lines[{i}] 不能为空或纯空白。",
                            result,
                            logIssues);
                    }
                }
            }

            if (entry.Duration < 0f)
                valid &= Report(entry, string.Empty, "duration 不能小于 0。", result, logIssues);
            if (entry.Cooldown < 0f)
                valid &= Report(entry, string.Empty, "cooldown 不能小于 0。", result, logIssues);
            if (entry.Once && string.IsNullOrWhiteSpace(entry.CompletionFlag))
                valid &= Report(entry, string.Empty, "once=true 时 completionFlag 不能为空。", result, logIssues);

            return valid;
        }

        private static bool ValidateCondition(
            CharacterSpeechConfigEntry entry,
            CharacterSpeechCondition condition,
            CharacterSpeechConfigLoadResult result,
            bool logIssues)
        {
            if (condition == null)
                return Report(entry, string.Empty, "condition 不能为空。", result, logIssues);

            bool valid = true;
            if (string.IsNullOrWhiteSpace(condition.Fact))
            {
                valid &= Report(entry, string.Empty, "condition.fact 不能为空。", result, logIssues);
            }
            else if (!KnownFacts.Contains(condition.Fact))
            {
                valid &= Report(
                    entry,
                    condition.Fact,
                    "未注册的 Fact，可能存在拼写错误或缺少 Contributor 常量。",
                    result,
                    logIssues);
            }

            if (!Enum.IsDefined(typeof(CharacterSpeechConditionOperator), condition.Operator))
            {
                valid &= Report(entry, condition.Fact, "condition.operator 无效。", result, logIssues);
            }
            else if (RequiresNumericValue(condition.Operator) &&
                     !double.TryParse(
                         condition.Value,
                         NumberStyles.Float,
                         CultureInfo.InvariantCulture,
                         out _))
            {
                valid &= Report(
                    entry,
                    condition.Fact,
                    $"操作符 {condition.Operator} 的 value 必须是 InvariantCulture 数值。",
                    result,
                    logIssues);
            }

            return valid;
        }

        private static bool ValidateCompletionFlagConflict(
            CharacterSpeechConfigEntry entry,
            Dictionary<string, CharacterSpeechConfigEntry> entriesByCompletionFlag,
            CharacterSpeechConfigLoadResult result,
            bool logIssues)
        {
            if (string.IsNullOrWhiteSpace(entry.CompletionFlag) ||
                !entriesByCompletionFlag.TryGetValue(
                    entry.CompletionFlag,
                    out CharacterSpeechConfigEntry existing))
            {
                return true;
            }

            if (string.Equals(existing.Topic, entry.Topic, StringComparison.Ordinal))
                return true;

            return Report(
                entry,
                string.Empty,
                $"completionFlag '{entry.CompletionFlag}' 已被不相关条目 '{existing.Id}' 使用。",
                result,
                logIssues);
        }

        private static bool Report(
            CharacterSpeechConfigEntry entry,
            string factName,
            string message,
            CharacterSpeechConfigLoadResult result,
            bool logIssues)
        {
            AddIssue(result, entry.SourceName, entry.Id, factName, message, logIssues);
            return false;
        }

        #endregion

        #region 排序与辅助

        private static JsonSerializer CreateSerializer()
        {
            return JsonSerializer.Create(new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                MissingMemberHandling = MissingMemberHandling.Ignore
            });
        }

        private static bool RequiresNumericValue(CharacterSpeechConditionOperator conditionOperator)
        {
            return conditionOperator == CharacterSpeechConditionOperator.Greater ||
                   conditionOperator == CharacterSpeechConditionOperator.GreaterOrEqual ||
                   conditionOperator == CharacterSpeechConditionOperator.Less ||
                   conditionOperator == CharacterSpeechConditionOperator.LessOrEqual;
        }

        private static bool TryParseDefinedEnum<TEnum>(JToken token, out TEnum parsed)
            where TEnum : struct
        {
            parsed = default;
            return token?.Type == JTokenType.String &&
                   Enum.TryParse(token.Value<string>(), true, out parsed) &&
                   Enum.IsDefined(typeof(TEnum), parsed);
        }

        private static string TryReadFactName(JObject entryObject)
        {
            if (entryObject["conditions"] is not JArray conditions ||
                conditions.Count == 0 ||
                conditions[0] is not JObject firstCondition)
            {
                return string.Empty;
            }

            return firstCondition.Value<string>("fact") ?? string.Empty;
        }

        private static void AddIssue(
            CharacterSpeechConfigLoadResult result,
            string sourceName,
            string entryId,
            string factName,
            string message,
            bool logIssues)
        {
            CharacterSpeechConfigIssue issue =
                new(sourceName, entryId, factName, message);
            result.Issues.Add(issue);
            if (logIssues)
                Debug.LogError(issue.ToString());
        }

        private static int CompareTextAssets(TextAsset left, TextAsset right)
        {
            int nameComparison = string.CompareOrdinal(left.name, right.name);
            return nameComparison != 0
                ? nameComparison
                : string.CompareOrdinal(left.text, right.text);
        }

        private static int CompareSources(
            CharacterSpeechConfigSource left,
            CharacterSpeechConfigSource right)
        {
            int nameComparison = string.CompareOrdinal(left.Name, right.Name);
            return nameComparison != 0
                ? nameComparison
                : string.CompareOrdinal(left.Text, right.Text);
        }

        private static int CompareEntries(
            CharacterSpeechConfigEntry left,
            CharacterSpeechConfigEntry right)
        {
            int priorityComparison = right.Priority.CompareTo(left.Priority);
            return priorityComparison != 0
                ? priorityComparison
                : string.CompareOrdinal(left.Id, right.Id);
        }

        #endregion
    }
}
