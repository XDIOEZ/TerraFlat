using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Localization
{
    /// <summary>
    /// 物品 Tag 的本地化目录：ItemData.Tags 永远保存稳定 ASCII ID，显示与搜索再映射到各语言标签。
    /// 内置目录同时作为 String Table 尚未同步时的编辑期/开发期来源；MOD 可按相同稳定 ID 注册额外语言。
    /// </summary>
    public static class ItemTagLocalizationCatalog
    {
        #region 数据契约

        public const string ResourcePath = "Localization/ItemTagCatalog";
        public const string EditorAssetPath = "Assets/Resources/Localization/ItemTagCatalog.json";

        [Serializable]
        public sealed class CatalogData
        {
            public int schemaVersion;
            public TagEntryData[] tags;
        }

        [Serializable]
        public sealed class TagEntryData
        {
            public string id;
            public LocalizedLabelData[] labels;
        }

        [Serializable]
        public sealed class LocalizedLabelData
        {
            public string locale;
            public string text;
        }

        private static readonly Dictionary<string, Dictionary<string, string>> labelsByTag =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string[]> searchTermsByTag =
            new(StringComparer.OrdinalIgnoreCase);
        private static bool loaded;

        #endregion

        #region 生命周期

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            labelsByTag.Clear();
            searchTermsByTag.Clear();
            loaded = false;
        }

        /// <summary>首次使用时读取内置 Tag 语言目录。</summary>
        private static void EnsureLoaded()
        {
            if (loaded)
                return;

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
                throw new InvalidOperationException($"缺少物品 Tag 本地化目录：Resources/{ResourcePath}");

            CatalogData catalog = JsonUtility.FromJson<CatalogData>(asset.text);
            if (catalog == null || catalog.schemaVersion != 1 || catalog.tags == null)
                throw new InvalidOperationException($"物品 Tag 本地化目录格式无效：{EditorAssetPath}");

            for (int index = 0; index < catalog.tags.Length; index++)
            {
                TagEntryData entry = catalog.tags[index];
                if (entry == null || !IsStableId(entry.id))
                    throw new InvalidOperationException($"物品 Tag 本地化目录包含无效稳定 ID：{entry?.id}");

                if (entry.labels == null || entry.labels.Length == 0)
                    throw new InvalidOperationException($"物品 Tag {entry.id} 没有任何语言标签。");

                for (int labelIndex = 0; labelIndex < entry.labels.Length; labelIndex++)
                {
                    LocalizedLabelData label = entry.labels[labelIndex];
                    if (label == null || string.IsNullOrWhiteSpace(label.locale) || string.IsNullOrWhiteSpace(label.text))
                        throw new InvalidOperationException($"物品 Tag {entry.id} 包含空语言或空译文。");
                    RegisterLocalizationInternal(entry.id.Trim(), label.locale.Trim(), label.text.Trim());
                }
            }

            loaded = true;
        }

        #endregion

        #region 查询与注册

        /// <summary>Tag ID 只允许语言无关的 ASCII 字母、数字及常用命名分隔符。</summary>
        public static bool IsStableId(string tagId)
        {
            if (string.IsNullOrWhiteSpace(tagId))
                return false;

            string value = tagId.Trim();
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool valid = (character >= 'a' && character <= 'z') ||
                             (character >= 'A' && character <= 'Z') ||
                             (character >= '0' && character <= '9') ||
                             character == '.' || character == '_' || character == '-' || character == ':';
                if (!valid)
                    return false;
            }

            return true;
        }

        /// <summary>读取当前语言的 Tag 显示名；String Table 优先，目录文本作为开发期 fallback。</summary>
        public static string GetDisplayName(string tagId)
        {
            if (!IsStableId(tagId))
                return tagId ?? string.Empty;

            EnsureLoaded();
            string fallback = GetCatalogLabel(tagId, FlatWorldLocalizationService.CurrentLocaleCode) ?? tagId;
            return FlatWorldLocalizationService.Get(
                FlatWorldLocalizationService.GetItemTagLabelKey(tagId),
                fallback,
                FlatWorldLocalizationService.DefaultTable);
        }

        /// <summary>读取指定语言 Tag 名称，供跨语言搜索。</summary>
        public static string GetInLocale(string tagId, string localeCode)
        {
            if (!IsStableId(tagId) || string.IsNullOrWhiteSpace(localeCode))
                return string.Empty;

            EnsureLoaded();
            string localized = FlatWorldLocalizationService.GetInLocale(
                FlatWorldLocalizationService.GetItemTagLabelKey(tagId),
                localeCode,
                FlatWorldLocalizationService.DefaultTable);
            return !string.IsNullOrWhiteSpace(localized)
                ? localized
                : GetCatalogLabel(tagId, localeCode) ?? string.Empty;
        }

        /// <summary>匹配稳定 ID 或任意已注册语言文本；搜索与当前 UI 语言无关。</summary>
        public static bool MatchesSearch(string tagId, string query)
        {
            if (string.IsNullOrWhiteSpace(tagId) || string.IsNullOrWhiteSpace(query))
                return false;

            foreach (string term in GetSearchTerms(tagId.Trim()))
            {
                if (!string.IsNullOrWhiteSpace(term) &&
                    term.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>MOD 可为稳定 Tag ID 注册额外 Locale 文本；不同来源不得静默覆盖已有译文。</summary>
        public static void RegisterLocalization(string tagId, string localeCode, string text)
        {
            if (!IsStableId(tagId))
                throw new ArgumentException("Tag 必须使用稳定 ASCII ID。", nameof(tagId));
            if (string.IsNullOrWhiteSpace(localeCode))
                throw new ArgumentException("Locale Code 不能为空。", nameof(localeCode));
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Tag 译文不能为空。", nameof(text));

            EnsureLoaded();
            RegisterLocalizationInternal(tagId.Trim(), localeCode.Trim(), text.Trim());
            searchTermsByTag.Remove(tagId.Trim());
        }

        private static string[] GetSearchTerms(string tagId)
        {
            if (searchTermsByTag.TryGetValue(tagId, out string[] cached))
                return cached;

            EnsureLoaded();
            var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tagId };
            if (labelsByTag.TryGetValue(tagId, out Dictionary<string, string> labels))
            {
                foreach (string label in labels.Values)
                    if (!string.IsNullOrWhiteSpace(label))
                        terms.Add(label);
            }

            string chinese = GetInLocale(tagId, FlatWorldLocalizationService.DefaultLocaleCode);
            string english = GetInLocale(tagId, FlatWorldLocalizationService.FallbackLocaleCode);
            if (!string.IsNullOrWhiteSpace(chinese)) terms.Add(chinese);
            if (!string.IsNullOrWhiteSpace(english)) terms.Add(english);

            string[] result = new string[terms.Count];
            terms.CopyTo(result);
            searchTermsByTag.Add(tagId, result);
            return result;
        }

        private static string GetCatalogLabel(string tagId, string localeCode)
        {
            if (!labelsByTag.TryGetValue(tagId, out Dictionary<string, string> labels))
                return null;
            return labels.TryGetValue(localeCode, out string text) ? text : null;
        }

        private static void RegisterLocalizationInternal(string tagId, string localeCode, string text)
        {
            if (!labelsByTag.TryGetValue(tagId, out Dictionary<string, string> labels))
            {
                labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                labelsByTag.Add(tagId, labels);
            }

            if (labels.TryGetValue(localeCode, out string existing))
            {
                if (!string.Equals(existing, text, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Tag {tagId} 的 {localeCode} 译文冲突：{existing} / {text}");
                return;
            }

            labels.Add(localeCode, text);
        }

        #endregion
    }
}
