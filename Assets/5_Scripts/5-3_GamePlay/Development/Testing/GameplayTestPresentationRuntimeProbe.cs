using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Audio;
using FlatWorld.Localization;
using FlatWorld.Settings;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>表现现场使用正式设置窗口、玩家渲染与音频服务，夹具只持有自有对象和偏好恢复。</summary>
public static partial class GameplayTestPresentationRuntimeProbe
{
    #region 真实表现操作登记
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntimeOperations() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorOperations() => Register();
#endif
    private static void Register()
    {
        GameplayTestRuntimeProbe.RegisterAsync("ui_settings", "settings_locale_open", CaptureSettings);
        GameplayTestRuntimeProbe.Register("ui_settings", "settings_locale_close", CloseSettings);
        GameplayTestRuntimeProbe.RegisterAsync("audio", "voice_lifecycle", CaptureAudio);
        GameplayTestRuntimeProbe.RegisterAsync("rendering", "player_external_effects", CaptureRendering);
    }
    #endregion

    #region 偏好原值与画面等待
    private sealed class SavedPreference
    {
        private readonly string key;
        private readonly bool existed;
        private readonly object value;
        public SavedPreference(string key, object defaultValue)
        {
            this.key = key;
            existed = PlayerPrefs.HasKey(key);
            value = defaultValue is string text ? PlayerPrefs.GetString(key, text) :
                defaultValue is int integer ? (object)PlayerPrefs.GetInt(key, integer) :
                PlayerPrefs.GetFloat(key, (float)defaultValue);
        }
        public void Restore()
        {
            if (!existed) PlayerPrefs.DeleteKey(key);
            else if (value is string text) PlayerPrefs.SetString(key, text);
            else if (value is int integer) PlayerPrefs.SetInt(key, integer);
            else PlayerPrefs.SetFloat(key, (float)value);
        }
        public bool IsRestored() => PlayerPrefs.HasKey(key) == existed && (!existed ||
            (value is string text ? PlayerPrefs.GetString(key) == text :
             value is int integer ? PlayerPrefs.GetInt(key) == integer :
             PlayerPrefs.GetFloat(key).Equals((float)value)));
    }

    private static List<SavedPreference> CaptureAudioPreferences()
    {
        var values = new List<SavedPreference>();
        foreach (string suffix in new[] { "Master", "Music", "Sfx", "UI", "Ambient", "Voice" })
            values.Add(new SavedPreference("FlatWorld.Audio.v1." + suffix, 1f));
        values.Add(new SavedPreference("FlatWorld.Audio.v1.Muted", 0));
        return values;
    }

    private static IEnumerator WaitForPanel(BasePanel panel)
    {
        float deadline = Time.realtimeSinceStartup + 8f;
        yield return null;
        while (panel != null && panel.IsVisualTransitioning && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (panel == null || panel.IsVisualTransitioning)
            throw new InvalidOperationException("正式设置窗口在真实时间 8 秒内没有完成视觉过渡。");
        Canvas.ForceUpdateCanvases();
        yield return null;
    }

    private static IEnumerator ChangeSettingsLocale(TMP_Dropdown language, TMP_Text text,
        int index, string localeCode, string expectedText, JObject sample)
    {
        string beforeLocale = FlatWorldLocalizationService.CurrentLocaleCode;
        bool changeRequested = beforeLocale != localeCode;
        bool received = false;
        Action<string> onChanged = value => { if (value == localeCode) received = true; };
        FlatWorldLocalizationService.LanguageChanged += onChanged;
        float started = Time.realtimeSinceStartup;
        try
        {
            language.value = index;
            // 正式语言事件在资源预载完成后才发出，等待真实 Binder 刷新而不手动改文字。
            do
            {
                yield return null;
            }
            while (language != null && text != null && Time.realtimeSinceStartup - started < 8f &&
                (FlatWorldLocalizationService.CurrentLocaleCode != localeCode ||
                 changeRequested && !received || text.text != expectedText));
            sample["beforeLocale"] = beforeLocale;
            sample["requestedLocale"] = localeCode;
            sample["actualLocale"] = FlatWorldLocalizationService.CurrentLocaleCode;
            sample["changeRequested"] = changeRequested;
            sample["languageEventReceived"] = received;
            sample["dropdownIndex"] = language != null ? language.value : -1;
            sample["text"] = text != null ? text.text : null;
            sample["textInstanceId"] = text != null ? text.GetInstanceID() : 0;
            sample["elapsedSeconds"] = Time.realtimeSinceStartup - started;
            sample["completed"] = language != null && text != null && language.value == index &&
                FlatWorldLocalizationService.CurrentLocaleCode == localeCode &&
                (!changeRequested || received) && text.text == expectedText;
        }
        finally { FlatWorldLocalizationService.LanguageChanged -= onChanged; }
    }
    #endregion

    #region 正式设置窗口与语言现场
    private static SettingsFixture activeSettings;

    private sealed class SettingsFixture : IDisposable
    {
        public readonly UIManager Ui;
        public readonly AudioService Audio;
        public readonly AudioUserSettings OriginalAudio;
        public readonly string OriginalLocale;
        public readonly bool OriginalInputBlocking;
        public readonly List<SavedPreference> Preferences;
        public BasePanel Panel;
        private bool disposed;

        public SettingsFixture(Player player)
        {
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            Ui = UIManager.ExistingInstance ?? throw new InvalidOperationException("正式 UIManager 尚未就绪。");
            Audio = UnityEngine.Object.FindObjectOfType<AudioService>() ??
                throw new InvalidOperationException("正式 AudioService 尚未就绪。");
            if (Ui.TryGetPanel(GameManager.MainMenuSettingsPanelKey, out _))
                throw new InvalidOperationException("当前已有主菜单设置窗口，拒绝借用或销毁其他来源的面板。");
            OriginalInputBlocking = Ui.HasOpenGameplayInputBlockingPanel();
            OriginalAudio = JsonUtility.FromJson<AudioUserSettings>(JsonUtility.ToJson(Audio.UserSettings));
            OriginalLocale = FlatWorldLocalizationService.CurrentLocaleCode;
            Preferences = CaptureAudioPreferences();
            Preferences.Add(new SavedPreference(FlatWorldLocalizationService.SavedLocaleKey, ""));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (Panel == null) Ui.TryGetPanel(GameManager.MainMenuSettingsPanelKey, out Panel);
                if (Panel != null)
                {
                    Panel.Close();
                    Ui.DestroyPanel(Panel);
                }
            }
            finally
            {
                try
                {
                    Audio.SetMasterVolume(OriginalAudio.Master, false);
                    foreach (AudioBus bus in Enum.GetValues(typeof(AudioBus)))
                        Audio.SetBusVolume(bus, OriginalAudio.GetBusVolume(bus), false);
                    Audio.SetMuted(OriginalAudio.Muted, false);
                    if (!FlatWorldLocalizationService.TrySetLocale(OriginalLocale))
                        throw new InvalidOperationException("设置现场无法恢复原语言：" + OriginalLocale);
                }
                finally
                {
                    foreach (SavedPreference preference in Preferences) preference.Restore();
                    PlayerPrefs.Save();
                    if (ReferenceEquals(activeSettings, this)) activeSettings = null;
                }
            }
        }
    }

    private static IEnumerator CaptureSettings(Player player, JObject args, Action<JObject> complete)
    {
        if (activeSettings != null) throw new InvalidOperationException("前一设置现场还未释放。");
        var fixture = new SettingsFixture(player);
        activeSettings = fixture;
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        bool retained = false;
        try
        {
            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>() ??
                throw new InvalidOperationException("正式 GameManager 尚未就绪。");
            manager.OpenMainMenuSettings();
            if (!fixture.Ui.TryGetPanel(GameManager.MainMenuSettingsPanelKey, out fixture.Panel))
                throw new InvalidOperationException("正式设置入口没有创建窗口。");
            BasePanel panel = fixture.Panel;
            var saveButton = panel.GetButton(SettingsEditSessionController.SaveButtonName);
            var saveText = saveButton != null ? saveButton.GetComponentInChildren<TMP_Text>(true) : null;
            var binder = saveText != null ? saveText.GetComponent<LocalizedTextBinder>() : null;
            var language = panel.GetComponentsInChildren<TMP_Dropdown>(true)
                .SingleOrDefault(value => value.name == GameManager.MainMenuSettingsLanguageDropdownKey);
            var volume = panel.GetComponentsInChildren<Slider>(true).SingleOrDefault(value => value.name == "MasterVolume");
            if (saveButton == null || saveText == null || binder == null || !binder.HasBinding || language == null || volume == null)
                throw new InvalidOperationException("正式设置窗口缺少保存按钮、语言下拉、音量滑块或正式 TMP 翻译绑定。");
            yield return WaitForPanel(panel);

            ISettingsDropdown setting = FlatWorldLocalizationService.SettingsProvider.GetDropdown(FlatWorldLocalizationService.LocaleSettingKey);
            int zh = setting.Options.ToList().FindIndex(value => value.Id == "zh-CN");
            int en = setting.Options.ToList().FindIndex(value => value.Id == "en");
            if (zh < 0 || en < 0) throw new InvalidOperationException("正式语言 Provider 缺少中英文稳定选项。");
            string key = FlatWorldLocalizationService.GetUiTextKey("保存");
            string expectedZh = FlatWorldLocalizationService.GetInLocale(key, "zh-CN", FlatWorldLocalizationService.UiTable);
            string expectedEn = FlatWorldLocalizationService.GetInLocale(key, "en", FlatWorldLocalizationService.UiTable);
            string nodeName = saveText.name;
            int textInstanceId = saveText.GetInstanceID();
            int binderInstanceId = binder.GetInstanceID();
            var chinese = new JObject();
            var english = new JObject();
            var discardedChinese = new JObject();
            yield return ChangeSettingsLocale(language, saveText, zh, "zh-CN", expectedZh, chinese);
            string observedZh = saveText.text;
            volume.value = .4f;
            yield return ChangeSettingsLocale(language, saveText, en, "en", expectedEn, english);
            string observedEn = saveText.text;
            bool unsaved = SettingsProviderRegistry.HasEditSessionChanges();
            saveButton.onClick.Invoke();
            bool committedClean = !SettingsProviderRegistry.HasEditSessionChanges();
            volume.value = .7f;
            yield return ChangeSettingsLocale(language, saveText, zh, "zh-CN", expectedZh, discardedChinese);
            bool changedAfterSave = SettingsProviderRegistry.HasEditSessionChanges();
            panel.Close();
            bool discardedLocale = FlatWorldLocalizationService.CurrentLocaleCode == "en";
            float discardedVolume = fixture.Audio.UserSettings.Master;
            bool cleanAfterClose = !SettingsProviderRegistry.HasEditSessionChanges();
            manager.OpenMainMenuSettings();
            yield return WaitForPanel(panel);
            float reopenedAt = Time.realtimeSinceStartup;
            // 放弃修改后语言资源可能异步就绪，等正式 Binder 恢复译文而不是立即读取旧字。
            while (saveText != null && saveText.text != expectedEn &&
                   Time.realtimeSinceStartup - reopenedAt < 8f)
                yield return null;
            bool reopenedEnglishReady = saveText != null && saveText.text == expectedEn;

            int opens = 0, closes = 0;
            Action onOpen = () => opens++;
            Action onClose = () => closes++;
            panel.Opened += onOpen;
            panel.Closed += onClose;
            try { panel.Open(); panel.Open(); }
            finally { panel.Opened -= onOpen; panel.Closed -= onClose; }
            bool invalidLocale = FlatWorldLocalizationService.TrySetLocale("lua-probe-missing-locale");
            var group = panel.GetComponent<CanvasGroup>();
            var result = new JObject
            {
                ["formalWindow"] = panel.GetComponent<SettingsEditSessionController>() != null,
                ["registered"] = fixture.Ui.GetAllPanelsOfType(GameManager.MainMenuSettingsPanelKey).Contains(panel),
                ["open"] = panel.IsOpen(), ["blocking"] = panel.IsGameplayInputBlocking,
                ["managerBlocking"] = fixture.Ui.HasOpenGameplayInputBlockingPanel(),
                ["debugWorldLabelsSuppressed"] = !AI_DebugOverlay.CanDrawWorldLabels,
                ["navigationPrepared"] = panel.IsGamepadNavigationPrepared,
                ["interactive"] = group != null && group.interactable && group.blocksRaycasts && group.alpha > .99f,
                ["expectedChinese"] = expectedZh, ["observedChinese"] = observedZh,
                ["expectedEnglish"] = expectedEn, ["observedEnglish"] = observedEn,
                ["nodeNamePreserved"] = saveText.name == nodeName,
                ["textInstanceId"] = textInstanceId, ["currentTextInstanceId"] = saveText.GetInstanceID(),
                ["binderInstanceId"] = binderInstanceId, ["currentBinderInstanceId"] = binder.GetInstanceID(),
                ["sameBoundText"] = ReferenceEquals(saveButton.GetComponentInChildren<TMP_Text>(true), saveText) &&
                    ReferenceEquals(saveText.GetComponent<LocalizedTextBinder>(), binder),
                ["chineseSwitch"] = chinese, ["englishSwitch"] = english,
                ["discardedChineseSwitch"] = discardedChinese,
                ["unsavedBeforeCommit"] = unsaved, ["commitClearedChanges"] = committedClean,
                ["changedAfterCommit"] = changedAfterSave, ["discardedToCommittedLocale"] = discardedLocale,
                ["discardedToCommittedVolume"] = discardedVolume, ["cleanAfterClose"] = cleanAfterClose,
                ["duplicateOpenEvents"] = opens, ["unexpectedCloseEvents"] = closes,
                ["invalidLocaleAccepted"] = invalidLocale,
                ["currentLocale"] = FlatWorldLocalizationService.CurrentLocaleCode,
                ["reopenedEnglish"] = saveText.text,
                ["reopenedEnglishReady"] = reopenedEnglishReady,
                ["reopenedEnglishWaitSeconds"] = Time.realtimeSinceStartup - reopenedAt,
                ["fallback"] = FlatWorldLocalizationService.Get("lua.missing.translation", "Lua 缺项回退", FlatWorldLocalizationService.UiTable),
                ["currentVolume"] = fixture.Audio.UserSettings.Master
            };
            retained = true;
            complete(result);
        }
        finally { if (!retained) fixture.Dispose(); }
    }

    private static JObject CloseSettings(Player player, JObject args)
    {
        SettingsFixture fixture = activeSettings ?? throw new InvalidOperationException("没有可关闭的自有设置现场。");
        fixture.Dispose();
        return new JObject
        {
            ["panelUnregistered"] = !fixture.Ui.GetAllPanelsOfType(GameManager.MainMenuSettingsPanelKey).Contains(fixture.Panel),
            ["closed"] = fixture.Panel == null || !fixture.Panel.IsOpen(),
            ["inputBlockingRestored"] = fixture.Ui.HasOpenGameplayInputBlockingPanel() == fixture.OriginalInputBlocking,
            ["localeRestored"] = FlatWorldLocalizationService.CurrentLocaleCode == fixture.OriginalLocale,
            ["volumeRestored"] = Mathf.Approximately(fixture.Audio.UserSettings.Master, fixture.OriginalAudio.Master),
            ["preferencesRestored"] = fixture.Preferences.All(value => value.IsRestored()),
            ["fixtureReleased"] = activeSettings == null
        };
    }
    #endregion
}
