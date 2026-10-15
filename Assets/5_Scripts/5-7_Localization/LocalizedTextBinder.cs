using System;
using TMPro;
using UnityEngine;

namespace FlatWorld.Localization
{
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
