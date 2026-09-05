#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace FlatWorld.Localization.Editor
{
    /// <summary>
    /// 物品名称表的编辑器同步入口：ID 只用于关联，gameName 是本体中文名，
    /// ItemNames.en.json 维护明确英文译名，String Table 是供运行时查询的生成资源。
    /// 仅同步 Manifest 中启用的具体物品；缺名、漏译或翻译键冲突会在写表前报错。
    /// </summary>
    public static partial class FlatWorldLocalizationSetup
    {
        #region 物品名称资源

        private const string EnglishItemNamesPath = "Assets/Localization/ItemNames.en.json";

        /// <summary>只更新物品名称，避免全量同步改动无关的 UI 文本和 Prefab。</summary>
        [MenuItem("FlatWorld/Localization/Sync Item Names")]
        public static void SyncItemNames()
        {
            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(
                FlatWorldLocalizationService.DefaultTable);
            if (collection == null)
                throw new InvalidOperationException("缺少 FlatWorld 内容表，请先完成 Localization 设置。");

            StringTable chinese = collection.GetTable(new LocaleIdentifier("zh-CN")) as StringTable;
            StringTable english = collection.GetTable(new LocaleIdentifier("en")) as StringTable;
            if (chinese == null || english == null)
                throw new InvalidOperationException("物品名称同步需要 zh-CN 和 en 两张内容表。");

            int count = SyncItemEntries(chinese, english, includeDescriptions: false);
            EditorUtility.SetDirty(chinese);
            EditorUtility.SetDirty(english);
            EditorUtility.SetDirty(collection.SharedData);
            AssetDatabase.SaveAssetIfDirty(chinese);
            AssetDatabase.SaveAssetIfDirty(english);
            AssetDatabase.SaveAssetIfDirty(collection.SharedData);
            Debug.Log($"[FlatWorld Localization] 已同步 {count} 种物品的中文名和英文名，ID 与显示名独立。");
        }

        /// <summary>按正式 Manifest 解析后的物品定义同步名称，说明仍沿用独立同步规则。</summary>
        private static int SyncItemEntries(
            StringTable chineseTable, StringTable englishTable, bool includeDescriptions = true)
        {
            List<ItemDefinitionDto> definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
            Dictionary<string, string> englishNames = LoadEnglishItemNames();
            var namesByKey = new Dictionary<string, (string Chinese, string English)>(StringComparer.Ordinal);

            // 完成全目录校验后再写表，避免漏译时生成一半成功、一半仍是 ID 的资源。
            foreach (ItemDefinitionDto definition in definitions)
            {
                if (definition.Abstract)
                    continue;

                string sourceName = definition.GameName?.Trim();
                if (string.IsNullOrWhiteSpace(sourceName) || !ContainsChinese(sourceName))
                    throw new InvalidDataException($"物品 {definition.Id} 的 gameName 必须是明确的中文显示名。");
                if (!englishNames.TryGetValue(definition.Id, out string englishName))
                    throw new InvalidDataException($"物品 {definition.Id} 缺少英文名称，请填写 {EnglishItemNamesPath}。");

                string key = string.IsNullOrWhiteSpace(definition.LabelKey)
                    ? FlatWorldLocalizationService.GetItemLabelKey(definition.Id)
                    : definition.LabelKey.Trim();
                var names = (Chinese: sourceName, English: englishName);
                if (namesByKey.TryGetValue(key, out var existing) && existing != names)
                    throw new InvalidDataException($"物品名称键 {key} 被不同译名共用，请为 {definition.Id} 使用独立 labelKey。");
                namesByKey[key] = names;
            }

            foreach (var pair in namesByKey)
            {
                chineseTable.AddEntry(pair.Key, pair.Value.Chinese);
                englishTable.AddEntry(pair.Key, pair.Value.English);
            }

            int count = 0;
            foreach (ItemDefinitionDto definition in definitions)
            {
                if (definition.Abstract)
                    continue;
                count++;
                if (!includeDescriptions)
                    continue;

                string descriptionKey = string.IsNullOrWhiteSpace(definition.DescriptionKey)
                    ? FlatWorldLocalizationService.GetItemDescriptionKey(definition.Id)
                    : definition.DescriptionKey.Trim();
                string sourceDescription = definition.Description ?? string.Empty;
                SetChineseValue(chineseTable, descriptionKey, sourceDescription);
                SetEnglishValue(englishTable, descriptionKey,
                    GetEnglishDescription(definition.Id, englishNames[definition.Id]),
                    sourceDescription, definition.Id);
            }

            return count;
        }

        /// <summary>读取作者维护的英文名称，不将下划线拆分或 ID 原文当作翻译。</summary>
        private static Dictionary<string, string> LoadEnglishItemNames()
        {
            JObject root = JObject.Parse(File.ReadAllText(EnglishItemNamesPath), new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            if (root.Value<int>("schemaVersion") != 1 || root.Value<string>("locale") != "en" ||
                root["names"] is not JObject sourceNames)
                throw new InvalidDataException($"物品英文名称资源格式无效：{EnglishItemNamesPath}");

            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JProperty entry in sourceNames.Properties())
            {
                string name = entry.Value.Type == JTokenType.String ? entry.Value.Value<string>()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(name) || ContainsChinese(name))
                    throw new InvalidDataException($"物品 {entry.Name} 的英文显示名无效：{EnglishItemNamesPath}");
                names.Add(entry.Name, name);
            }

            return names;
        }

        #endregion
    }
}
#endif
