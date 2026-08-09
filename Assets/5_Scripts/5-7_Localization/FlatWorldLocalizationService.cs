using System;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace FlatWorld.Localization
{
    /// <summary>
    /// FlatWorld 的本地化薄封装：统一语言切换、文本查询、语言持久化和物品键命名。
    /// 具体翻译内容由 Unity Localization 的 String Table 维护；查询失败时返回调用方提供的旧文本。
    /// </summary>
    public static class FlatWorldLocalizationService
    {
        #region 常量

        public const string DefaultTable = "FlatWorld";
        public const string UiTable = "FlatWorldUI";
        public const string DefaultLocaleCode = "zh-CN";
        public const string FallbackLocaleCode = "en";
        public const string SavedLocaleKey = "FlatWorld.Localization.Locale";

        #endregion

        #region 事件与状态

        private static bool initialized;

        /// <summary>语言切换后发送当前 Locale Code，UI 组件可据此刷新文本。</summary>
        public static event Action<string> LanguageChanged;

        /// <summary>当前语言代码；没有 Localization Settings 时回退到 PlayerPrefs 或默认语言。</summary>
        public static string CurrentLocaleCode
        {
            get
            {
                if (LocalizationSettings.HasSettings)
                {
                    Locale locale = LocalizationSettings.SelectedLocale;
                    if (locale != null && !string.IsNullOrWhiteSpace(locale.Identifier.Code))
                        return locale.Identifier.Code;
                }

                return PlayerPrefs.GetString(SavedLocaleKey, DefaultLocaleCode);
            }
        }

        #endregion

        #region 生命周期与语言切换

        /// <summary>在场景加载前接入 Unity Localization，并恢复上次选择的语言。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeOnLoad()
        {
            Initialize();
        }

        /// <summary>手动初始化；没有配置 Localization Settings 时保持可用并返回旧文本。</summary>
        public static void Initialize()
        {
            if (initialized || !LocalizationSettings.HasSettings)
                return;

            LocalizationSettings.SelectedLocaleChanged += HandleSelectedLocaleChanged;

            string savedLocaleCode = PlayerPrefs.GetString(SavedLocaleKey, string.Empty);
            Locale savedLocale = string.IsNullOrWhiteSpace(savedLocaleCode)
                ? null
                : LocalizationSettings.AvailableLocales?.GetLocale(savedLocaleCode);
            if (savedLocale != null)
            {
                LocalizationSettings.SelectedLocale = savedLocale;
            }
            else
            {
                // 旧版本保存的 Locale 已不可用时，回退到项目默认语言。
                Locale defaultLocale = LocalizationSettings.AvailableLocales?.GetLocale(DefaultLocaleCode);
                if (defaultLocale != null)
                    LocalizationSettings.SelectedLocale = defaultLocale;

                PlayerPrefs.SetString(SavedLocaleKey, DefaultLocaleCode);
                PlayerPrefs.Save();
            }

            initialized = true;
        }

        /// <summary>切换到指定 Locale Code；返回 false 表示项目未配置该语言。</summary>
        public static bool TrySetLocale(string localeCode)
        {
            if (string.IsNullOrWhiteSpace(localeCode) || !LocalizationSettings.HasSettings)
                return false;

            Initialize();
            Locale locale = LocalizationSettings.AvailableLocales?.GetLocale(localeCode.Trim());
            if (locale == null)
                return false;

            LocalizationSettings.SelectedLocale = locale;
            PlayerPrefs.SetString(SavedLocaleKey, locale.Identifier.Code);
            PlayerPrefs.Save();
            return true;
        }

        private static void HandleSelectedLocaleChanged(Locale locale)
        {
            string localeCode = locale?.Identifier.Code;
            if (!string.IsNullOrWhiteSpace(localeCode))
            {
                PlayerPrefs.SetString(SavedLocaleKey, localeCode);
                PlayerPrefs.Save();
            }

            LanguageChanged?.Invoke(localeCode ?? string.Empty);
        }

        #endregion

        #region 文本查询

        /// <summary>按 String Table 和 key 查询文本；缺少表或条目时回退到 fallback，再回退到 key。</summary>
        public static string Get(string key, string fallback = null, string tableName = DefaultTable)
        {
            if (string.IsNullOrWhiteSpace(key))
                return fallback ?? string.Empty;

            if (!LocalizationSettings.HasSettings)
                return string.IsNullOrWhiteSpace(fallback) ? key : fallback;

            Initialize();
            try
            {
                string localized = LocalizationSettings.StringDatabase.GetLocalizedString(
                    tableName,
                    key,
                    fallbackBehavior: FallbackBehavior.UseProjectSettings);

                if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, key, StringComparison.Ordinal))
                    return localized;
            }
            catch (Exception)
            {
                // 本地化资源可能在异步初始化期间尚未可用，调用方仍应看到原始配置文本。
            }

            return string.IsNullOrWhiteSpace(fallback) ? key : fallback;
        }

        /// <summary>生成本体物品名称的稳定 String Table key。</summary>
        public static string GetItemLabelKey(string itemId)
        {
            return $"item.{itemId?.Trim()}.name";
        }

        /// <summary>生成本体物品说明的稳定 String Table key。</summary>
        public static string GetItemDescriptionKey(string itemId)
        {
            return $"item.{itemId?.Trim()}.description";
        }

        /// <summary>为 UI 原始文本生成稳定 key，避免把中文长句直接当作表格主键。</summary>
        public static string GetUiTextKey(string sourceText)
        {
            string normalized = sourceText?.Trim() ?? string.Empty;
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char character in normalized)
                {
                    hash ^= character;
                    hash *= 16777619u;
                }

                return $"ui.text.{hash:X8}";
            }
        }

        /// <summary>按 UI 原始文本查询 UI 表，供运行时动态文本使用。</summary>
        public static string GetUiText(string sourceText)
        {
            return Get(
                GetUiTextKey(sourceText),
                sourceText,
                UiTable);
        }

        /// <summary>按 UI 模板查询并格式化动态文本，模板本身必须进入 UI 表。</summary>
        public static string GetUiFormat(string sourceTemplate, params object[] arguments)
        {
            string localizedTemplate = GetUiText(sourceTemplate);
            try
            {
                return string.Format(localizedTemplate, arguments ?? Array.Empty<object>());
            }
            catch (FormatException)
            {
                // 翻译表模板损坏时保留原模板，避免运行时 UI 直接报错。
                return string.Format(sourceTemplate ?? string.Empty, arguments ?? Array.Empty<object>());
            }
        }

        #endregion
    }
}
