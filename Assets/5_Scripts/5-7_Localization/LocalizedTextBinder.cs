using System;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;

namespace FlatWorld.Localization
{
    #region 运行时文本绑定

    /// <summary>统一为动态 TMP 文本绑定本地化模板，并在内容变化或语言切换时刷新。</summary>
    public static class LocalizedTextRuntime
    {
        private sealed class TextSource
        {
            public string Text;
            public object[] Arguments;
            public Func<string> Provider;
            public bool IsFormatted;
            public bool IsBound;
        }

        private static readonly ConditionalWeakTable<TMP_Text, TextSource> Sources =
            new ConditionalWeakTable<TMP_Text, TextSource>();

        public static void SetText(TMP_Text target, string sourceText)
        {
            SetSource(target, sourceText, null, null);
        }

        public static void SetFormat(TMP_Text target, string sourceTemplate, params object[] arguments)
        {
            SetSource(target, sourceTemplate, arguments, null);
        }

        public static void SetProvider(TMP_Text target, Func<string> provider)
        {
            if (provider == null)
                return;

            SetSource(target, string.Empty, null, provider);
        }

        private static void SetSource(TMP_Text target, string sourceText, object[] arguments, Func<string> provider)
        {
            if (target == null)
                return;

            TextSource source = Sources.GetValue(target, _ => new TextSource());
            source.Text = sourceText ?? string.Empty;
            source.Arguments = arguments == null ? null : (object[])arguments.Clone();
            source.Provider = provider;
            source.IsFormatted = source.Arguments != null && source.Arguments.Length > 0;

            LocalizedTextBinder binder = target.GetComponent<LocalizedTextBinder>();
            if (binder == null)
                binder = target.gameObject.AddComponent<LocalizedTextBinder>();

            if (!source.IsBound)
            {
                source.IsBound = true;
                binder.ConfigureRuntime(() => Resolve(source));
                return;
            }

            target.text = Resolve(source);
        }

        private static string Resolve(TextSource source)
        {
            if (source.Provider != null)
                return source.Provider() ?? string.Empty;
            return source.IsFormatted
                ? FlatWorldLocalizationService.GetUiFormat(source.Text, source.Arguments)
                : FlatWorldLocalizationService.GetUiText(source.Text);
        }
    }

    #endregion

    /// <summary>
    /// 将一个 String Table key 绑定到 TMP 文本。挂到任意带 TMP_Text 的 UI 节点即可随语言切换刷新。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("FlatWorld/Localization/Localized Text Binder")]
    public sealed class LocalizedTextBinder : MonoBehaviour
    {
        #region Inspector 字段

        [SerializeField] private TMP_Text target;
        [SerializeField] private string tableName = FlatWorldLocalizationService.DefaultTable;
        [SerializeField] private string key;

        [SerializeField, TextArea(2, 5)]
        private string fallback;

        private Func<string> runtimeTextProvider;
        private bool preserveRuntimeText;
        private string lastBoundText;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            target ??= GetComponent<TMP_Text>();
        }

        private void OnEnable()
        {
            if (!HasBinding)
                return;

            FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
            RefreshCore(true);
        }

        private void OnDisable()
        {
            FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        }

        #endregion

        #region 刷新

        /// <summary>立即按当前语言刷新目标 TMP 文本。</summary>
        [ContextMenu("Refresh Localized Text")]
        public void Refresh()
        {
            RefreshCore(false);
        }

        private void RefreshCore(bool languageRefresh)
        {
            if (!HasBinding)
                return;

            target ??= GetComponent<TMP_Text>();
            if (target == null)
                return;

            // 自动绑定不可在切语言时覆盖被业务系统更新的动态文字。
            if (preserveRuntimeText && languageRefresh && lastBoundText != null &&
                !string.Equals(target.text, lastBoundText, StringComparison.Ordinal))
                return;

            string localized = runtimeTextProvider != null
                ? runtimeTextProvider() ?? string.Empty
                : FlatWorldLocalizationService.Get(key, fallback, tableName);
            target.text = localized;
            lastBoundText = localized;
        }

        /// <summary>由 UI 自动本地化器配置运行时绑定，不改变 Prefab 的布局结构。</summary>
        public void Configure(string localizedTableName, string localizedKey, string sourceFallback,
            bool preserveOverriddenText = false)
        {
            bool wasEnabled = isActiveAndEnabled && HasBinding;
            if (wasEnabled)
                FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;

            tableName = string.IsNullOrWhiteSpace(localizedTableName)
                ? FlatWorldLocalizationService.DefaultTable
                : localizedTableName;
            runtimeTextProvider = null;
            preserveRuntimeText = preserveOverriddenText;
            lastBoundText = null;
            key = localizedKey;
            fallback = sourceFallback;
            target ??= GetComponent<TMP_Text>();

            if (isActiveAndEnabled && HasBinding)
            {
                FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
                Refresh();
            }
        }

        /// <summary>绑定包含动态数据的界面标题，语言变化时重新读取业务名称。</summary>
        public void ConfigureRuntime(Func<string> textProvider)
        {
            if (isActiveAndEnabled && HasBinding)
                FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;

            key = string.Empty;
            runtimeTextProvider = textProvider;
            preserveRuntimeText = false;
            lastBoundText = null;
            target ??= GetComponent<TMP_Text>();
            if (isActiveAndEnabled && HasBinding)
            {
                FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
                Refresh();
            }
        }

        public bool HasBinding => runtimeTextProvider != null || !string.IsNullOrWhiteSpace(key);

        private void HandleLanguageChanged(string _)
        {
            RefreshCore(true);
        }

        #endregion
    }
}
