#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Localization;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace FlatWorld.Localization.Editor
{
    public static partial class FlatWorldLocalizationSetup
    {
        private static readonly string AdditionalLocalizationOverridesPath = Path.Combine(
            Application.dataPath,
            "Localization",
            "RuntimeLocalizationOverrides.additional.json");

        static FlatWorldLocalizationSetup()
        {
            if (!File.Exists(AdditionalLocalizationOverridesPath))
                throw new FileNotFoundException("缺少本地化补充源。", AdditionalLocalizationOverridesPath);

            JObject root = JObject.Parse(File.ReadAllText(AdditionalLocalizationOverridesPath));
            MergeAdditionalOverrides(root["ui"], EnglishUiOverrides, "UI");
            MergeAdditionalOverrides(root["itemDescriptions"], EnglishDescriptionOverrides, "物品说明");
        }

        #region 补充翻译源

        private static void MergeAdditionalOverrides(
            JToken token,
            IDictionary<string, string> destination,
            string category)
        {
            if (token == null)
                return;
            if (token is not JObject overrides)
                throw new InvalidDataException($"本地化补充源的 {category} 节点必须是对象：{AdditionalLocalizationOverridesPath}");

            foreach (JProperty property in overrides.Properties())
            {
                string value = property.Value.Type == JTokenType.String
                    ? property.Value.Value<string>()
                    : null;
                if (string.IsNullOrWhiteSpace(property.Name) || string.IsNullOrWhiteSpace(value))
                    throw new InvalidDataException($"本地化补充源包含空的 {category} 翻译：{property.Name}");

                destination[property.Name] = value;
            }
        }

        #endregion

        #region 定向同步菜单

        /// <summary>只保存 UI 表相关资源，避免同步时保存其他脏资产。</summary>
        [MenuItem("FlatWorld/其他工具/Localization/Sync Registered Runtime UI Texts Safely")]
        public static void SyncRegisteredRuntimeUiTextsSafely()
        {
            Locale chinese = AssetDatabase.LoadAssetAtPath<Locale>(ChineseLocalePath);
            Locale english = AssetDatabase.LoadAssetAtPath<Locale>(EnglishLocalePath);
            if (chinese == null || english == null)
                throw new InvalidOperationException("FlatWorldUI Locale 尚未初始化，请先运行 Setup Default Tables。");

            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(
                FlatWorldLocalizationService.UiTable);
            if (collection == null)
                throw new InvalidOperationException("FlatWorldUI StringTableCollection 不存在，请先运行 Setup Default Tables。");

            StringTable chineseTable = EnsureTable(collection, chinese);
            StringTable englishTable = EnsureTable(collection, english);
            var syncedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> runtimeEntry in EnglishUiOverrides)
            {
                string sourceText = runtimeEntry.Key;
                string key = FlatWorldLocalizationService.GetUiTextKey(sourceText);
                SetChineseValue(chineseTable, key, sourceText);
                SetEnglishValue(englishTable, key, runtimeEntry.Value, sourceText, key);
                syncedKeys.Add(key);
            }

            EditorUtility.SetDirty(collection);
            EditorUtility.SetDirty(collection.SharedData);
            EditorUtility.SetDirty(chineseTable);
            EditorUtility.SetDirty(englishTable);
            AssetDatabase.SaveAssetIfDirty(collection);
            AssetDatabase.SaveAssetIfDirty(collection.SharedData);
            AssetDatabase.SaveAssetIfDirty(chineseTable);
            AssetDatabase.SaveAssetIfDirty(englishTable);
            Debug.Log($"[FlatWorld Localization] 已安全定向同步 {syncedKeys.Count} 条运行时 UI 文本。");
        }

        /// <summary>只保存主线任务表相关资源，避免同步时保存其他脏资产。</summary>
        [MenuItem("FlatWorld/其他工具/Localization/Sync Starter Mining Quest Text Safely")]
        public static void SyncStarterMiningQuestTextSafely()
        {
            Locale chinese = AssetDatabase.LoadAssetAtPath<Locale>(ChineseLocalePath);
            Locale english = AssetDatabase.LoadAssetAtPath<Locale>(EnglishLocalePath);
            if (chinese == null || english == null)
                throw new InvalidOperationException("FlatWorld Locale 尚未初始化，请先运行 Setup Default Tables。");

            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(
                FlatWorldLocalizationService.DefaultTable);
            if (collection == null)
                throw new InvalidOperationException("FlatWorld StringTableCollection 不存在，请先运行 Setup Default Tables。");

            StringTable chineseTable = EnsureTable(collection, chinese);
            StringTable englishTable = EnsureTable(collection, english);
            string questPath = Path.Combine(Application.dataPath, "StreamingAssets/GameConfig/Quests/starter.json");
            JObject root = JObject.Parse(File.ReadAllText(questPath));
            if (!(root["quests"] is JArray quests))
                throw new InvalidOperationException("starter.json 缺少 quests 列表。");

            JObject starterQuest = null;
            foreach (JToken token in quests)
            {
                if (token is JObject quest && string.Equals(
                        quest.Value<string>("id"), "flatworld:first_chipped_tool", StringComparison.Ordinal))
                {
                    starterQuest = quest;
                    break;
                }
            }
            if (starterQuest == null)
                throw new InvalidOperationException("starter.json 缺少 flatworld:first_chipped_tool 主线。");

            const string questKeyPrefix = "quest.flatworld.survival_mining_path.";
            var questTexts = new List<KeyValuePair<string, string>>();
            AddStarterQuestText(questTexts, starterQuest.Value<string>("titleKey"), starterQuest.Value<string>("title"), questKeyPrefix);
            AddStarterQuestText(questTexts, starterQuest.Value<string>("descriptionKey"), starterQuest.Value<string>("description"), questKeyPrefix);
            if (starterQuest["stages"] is JArray stages)
            {
                foreach (JToken stageToken in stages)
                {
                    if (!(stageToken is JObject stage) || !(stage["objectives"] is JArray objectives))
                        continue;
                    foreach (JToken objectiveToken in objectives)
                    {
                        if (objectiveToken is JObject objective)
                            AddStarterQuestText(questTexts, objective.Value<string>("labelKey"), objective.Value<string>("label"), questKeyPrefix);
                    }
                }
            }
            if (questTexts.Count != 17)
                throw new InvalidOperationException($"主线文本应为 17 条，实际找到 {questTexts.Count} 条。");

            foreach (KeyValuePair<string, string> questText in questTexts)
            {
                if (!EnglishQuestOverrides.TryGetValue(questText.Key, out string translatedText) ||
                    string.IsNullOrWhiteSpace(translatedText) ||
                    string.Equals(translatedText, "Quest Text", StringComparison.Ordinal))
                    throw new InvalidOperationException($"主线文本缺少英文翻译：{questText.Key}");
            }

            var syncedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> questText in questTexts)
                SyncQuestText(chineseTable, englishTable, questText.Key, questText.Value, syncedKeys);

            EditorUtility.SetDirty(collection);
            EditorUtility.SetDirty(collection.SharedData);
            EditorUtility.SetDirty(chineseTable);
            EditorUtility.SetDirty(englishTable);
            AssetDatabase.SaveAssetIfDirty(collection);
            AssetDatabase.SaveAssetIfDirty(collection.SharedData);
            AssetDatabase.SaveAssetIfDirty(chineseTable);
            AssetDatabase.SaveAssetIfDirty(englishTable);
            Debug.Log($"[FlatWorld Localization] 已安全定向同步 starter 主线 {syncedKeys.Count} 条文本。");
        }

        #endregion
    }
}
#endif
