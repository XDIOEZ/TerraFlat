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

            int count = SyncItemEntries(collection, chinese, english, includeDescriptions: false);
            EditorUtility.SetDirty(chinese);
            EditorUtility.SetDirty(english);
            EditorUtility.SetDirty(collection.SharedData);
            AssetDatabase.SaveAssetIfDirty(chinese);
            AssetDatabase.SaveAssetIfDirty(english);
            AssetDatabase.SaveAssetIfDirty(collection.SharedData);
            Debug.Log($"[FlatWorld Localization] 已同步 {count} 种物品名称及物品 Tag 多语言条目，业务 ID 与显示文本独立。");
        }

        /// <summary>按正式 Manifest 解析后的物品定义同步名称，说明仍沿用独立同步规则。</summary>
        private static int SyncItemEntries(
            StringTableCollection collection,
            StringTable chineseTable,
            StringTable englishTable,
            bool includeDescriptions = true)
        {
            if (collection == null)
                throw new ArgumentNullException(nameof(collection));

            List<ItemDefinitionDto> definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
            Dictionary<string, string> englishNames = LoadEnglishItemNames();
            var namesByKey = new Dictionary<string, (string Chinese, string English)>(StringComparer.Ordinal);
            var expectedItemKeys = new HashSet<string>(StringComparer.Ordinal);

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

                string descriptionKey = string.IsNullOrWhiteSpace(definition.DescriptionKey)
                    ? FlatWorldLocalizationService.GetItemDescriptionKey(definition.Id)
                    : definition.DescriptionKey.Trim();
                expectedItemKeys.Add(key);
                expectedItemKeys.Add(descriptionKey);
            }

            RemoveStaleGeneratedItemEntries(collection, expectedItemKeys);

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

            SyncItemTagEntries(collection, chineseTable, englishTable);

            return count;
        }

        /// <summary>把稳定 Tag ID 的语言目录同步到 FlatWorld 内容表，运行时只按 key 查询显示文本。</summary>
        private static int SyncItemTagEntries(
            StringTableCollection collection,
            StringTable chineseTable,
            StringTable englishTable)
        {
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(ItemTagLocalizationCatalog.EditorAssetPath);
            if (source == null)
                throw new FileNotFoundException("缺少物品 Tag 本地化目录。", ItemTagLocalizationCatalog.EditorAssetPath);

            ItemTagLocalizationCatalog.CatalogData catalog =
                JsonUtility.FromJson<ItemTagLocalizationCatalog.CatalogData>(source.text);
            if (catalog == null || catalog.schemaVersion != 1 || catalog.tags == null)
                throw new InvalidDataException($"物品 Tag 本地化目录格式无效：{ItemTagLocalizationCatalog.EditorAssetPath}");

            var expectedKeys = new HashSet<string>(StringComparer.Ordinal);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            foreach (ItemTagLocalizationCatalog.TagEntryData tag in catalog.tags)
            {
                string tagId = tag?.id?.Trim();
                if (!ItemTagLocalizationCatalog.IsStableId(tagId))
                    throw new InvalidDataException($"物品 Tag 必须使用稳定 ASCII ID：{tag?.id}");
                if (!seenIds.Add(tagId))
                    throw new InvalidDataException($"物品 Tag 本地化目录存在重复 ID：{tagId}");

                string chinese = FindTagLabel(tag, "zh-CN");
                string english = FindTagLabel(tag, "en");
                if (string.IsNullOrWhiteSpace(chinese) || !ContainsChinese(chinese))
                    throw new InvalidDataException($"物品 Tag {tagId} 缺少有效中文标签。");
                if (string.IsNullOrWhiteSpace(english) || ContainsChinese(english))
                    throw new InvalidDataException($"物品 Tag {tagId} 缺少有效英文标签。");

                string key = FlatWorldLocalizationService.GetItemTagLabelKey(tagId);
                expectedKeys.Add(key);
                chineseTable.AddEntry(key, chinese);
                englishTable.AddEntry(key, english);
                count++;
            }

            RemoveStaleGeneratedItemTagEntries(collection, expectedKeys);
            return count;
        }

        /// <summary>按 Locale Code 读取 Tag 目录中的明确译文。</summary>
        private static string FindTagLabel(ItemTagLocalizationCatalog.TagEntryData tag, string localeCode)
        {
            if (tag?.labels == null)
                return null;

            for (int index = 0; index < tag.labels.Length; index++)
            {
                ItemTagLocalizationCatalog.LocalizedLabelData label = tag.labels[index];
                if (label != null && string.Equals(label.locale?.Trim(), localeCode, StringComparison.OrdinalIgnoreCase))
                    return label.text?.Trim();
            }

            return null;
        }

        /// <summary>移除目录已删除的生成 Tag key，避免 String Table 积累死条目。</summary>
        private static void RemoveStaleGeneratedItemTagEntries(
            StringTableCollection collection,
            ISet<string> expectedKeys)
        {
            var staleKeys = new List<string>();
            foreach (SharedTableData.SharedTableEntry entry in collection.SharedData.Entries)
            {
                string key = entry.Key;
                if (string.IsNullOrWhiteSpace(key) ||
                    !key.StartsWith("tag.", StringComparison.Ordinal) ||
                    !key.EndsWith(".name", StringComparison.Ordinal) ||
                    expectedKeys.Contains(key))
                    continue;

                staleKeys.Add(key);
            }

            foreach (string staleKey in staleKeys)
                collection.RemoveEntry(staleKey);
        }

        /// <summary>移除 Manifest 已不存在物品遗留的默认名称/说明键，避免生成表持续积累死条目。</summary>
        private static void RemoveStaleGeneratedItemEntries(
            StringTableCollection collection,
            ISet<string> expectedItemKeys)
        {
            var staleKeys = new List<string>();
            foreach (SharedTableData.SharedTableEntry entry in collection.SharedData.Entries)
            {
                string key = entry.Key;
                if (string.IsNullOrWhiteSpace(key) ||
                    !key.StartsWith("item.", StringComparison.Ordinal) ||
                    (!key.EndsWith(".name", StringComparison.Ordinal) &&
                     !key.EndsWith(".description", StringComparison.Ordinal)) ||
                    expectedItemKeys.Contains(key))
                    continue;

                staleKeys.Add(key);
            }

            foreach (string staleKey in staleKeys)
                collection.RemoveEntry(staleKey);
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
