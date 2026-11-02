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
        GameplayTestRuntimeProbe.Register("ui_settings", "new_game_window_close", CaptureNewGameWindowClose);
        GameplayTestRuntimeProbe.Register("ui_settings", "local_float_preference_persistence", CaptureLocalFloatPreferencePersistence);
        GameplayTestRuntimeProbe.Register("ui_settings", "status_hud_readability", CaptureStatusHudReadability);
        GameplayTestRuntimeProbe.RegisterAsync("audio", "voice_lifecycle", CaptureAudio);
        GameplayTestRuntimeProbe.RegisterAsync("rendering", "player_external_effects", CaptureRendering);
        GameplayTestRuntimeProbe.Register("rendering", "combat_effect_prewarm", CaptureCombatEffectPrewarm);
        GameplayTestRuntimeProbe.Register("rendering", "cold_machine_content", CaptureColdMachineContentAndPreparedVisuals);
    }
    #endregion

    #region 生存状态条现场采样
    private static JObject CaptureStatusHudReadability(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        Mod_Food food = player.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);
        food?.RefreshUI();
        BasePanel panel = food?.panelUI;
        if (panel == null) throw new InvalidOperationException("没有找到本地玩家的生存状态面板。");
        UnityEngine.UI.Slider[] sliders = panel.GetComponentsInChildren<UnityEngine.UI.Slider>(true);
        var bars = new JArray();
        foreach (string name in new[] { "血量", "水", "维生素", "体温" })
        {
            UnityEngine.UI.Slider slider = Array.Find(sliders, value => value != null && value.name == name);
            UnityEngine.UI.Graphic fill = slider != null && slider.fillRect != null ? slider.fillRect.GetComponent<UnityEngine.UI.Graphic>() : null;
            panel.TryGetText("DataText_" + name, out TextMeshProUGUI text);
            Color fillColor = fill != null ? fill.color : Color.clear;
            Color textColor = text != null ? text.color : Color.clear;
            bars.Add(new JObject { ["name"] = name,
                ["visible"] = slider != null && slider.gameObject.activeInHierarchy && fill != null && text != null && text.gameObject.activeInHierarchy,
                ["fill"] = new JArray(fillColor.r, fillColor.g, fillColor.b, fillColor.a),
                ["text"] = new JArray(textColor.r, textColor.g, textColor.b, textColor.a) });
        }
        ThreeSegmentStatusBarGraphic segments = panel.GetComponentInChildren<ThreeSegmentStatusBarGraphic>(true);
        panel.TryGetText("DataText_食物", out TextMeshProUGUI foodText);
        if (segments == null || foodText == null)
            throw new InvalidOperationException("食物状态条缺少三色填充或合计数值。");
        Transform foodLegend = panel.transform.Find("Panel/食物/FoodLegend");
        Color foodTextColor = foodText.color;
        var colors = new JArray();
        foreach (Color color in new[] { segments.FirstColor, segments.SecondColor, segments.ThirdColor })
            colors.Add(new JArray(color.r, color.g, color.b, color.a));
        bars.Add(new JObject { ["name"] = "食物", ["visible"] = segments.gameObject.activeInHierarchy,
            ["fill"] = colors[0].DeepClone(), ["segmentColors"] = colors,
            ["text"] = new JArray(foodTextColor.r, foodTextColor.g, foodTextColor.b, foodTextColor.a) });
        Nutrition nutrition = food.Data.nutrition;
        Vector3 targets = segments.TargetFractions;
        return new JObject { ["panelVisible"] = panel.gameObject.activeInHierarchy, ["bars"] = bars,
            ["sliderCount"] = sliders.Length, ["foodText"] = foodText.text,
            ["legendVisible"] = foodLegend != null && foodLegend.gameObject.activeInHierarchy,
            ["nutrition"] = new JArray(nutrition.Carbohydrates, nutrition.Fat, nutrition.Protein),
            ["maxima"] = new JArray(nutrition.Max_Carbohydrates, nutrition.Max_Fat, nutrition.Max_Protein),
            ["fractions"] = new JArray(targets.x, targets.y, targets.z) };
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

    #region 本地浮点偏好写入采样
    // 通过正式Provider采样缓存、持久值和分域通知，每项结束恢复原键及缓存。
    private static JObject CaptureLocalFloatPreferencePersistence(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var specifications = new[]
        {
            (CameraUserSettings.SettingsProvider, CameraUserSettings.LookaheadSettingKey, "FlatWorld.Camera.Lookahead"),
            (CameraUserSettings.SettingsProvider, CameraUserSettings.LookaheadSmoothingSettingKey, "FlatWorld.Camera.LookaheadSmoothing"),
            (CameraUserSettings.SettingsProvider, CameraUserSettings.LookaheadZoomInfluenceSettingKey, "FlatWorld.Camera.LookaheadZoomInfluence"),
            (UIUserSettings.SettingsProvider, UIUserSettings.ScaleSettingKey, "FlatWorld.UI.Scale"),
            (UIUserSettings.SettingsProvider, UIUserSettings.HotbarBottomSpacingSettingKey, "FlatWorld.UI.HotbarBottomSpacing"),
            (UIUserSettings.SettingsProvider, UIUserSettings.TouchControlsOpacitySettingKey, "FlatWorld.Mobile.TouchControlsOpacity"),
            (UIUserSettings.SettingsProvider, UIUserSettings.PinchZoomSensitivitySettingKey, "FlatWorld.Mobile.PinchZoomSensitivity"),
            (UIUserSettings.SettingsProvider, UIUserSettings.LeftControlZoneRatioSettingKey, "FlatWorld.Mobile.LeftControlZoneRatio"),
            (UIUserSettings.SettingsProvider, UIUserSettings.RightControlZoneRatioSettingKey, "FlatWorld.Mobile.RightControlZoneRatio")
        };
        var counts = new Dictionary<string, int> { ["camera"] = 0, ["ui"] = 0, ["hotbar"] = 0, ["touch"] = 0, ["mobile"] = 0 };
        Action onCamera = () => counts["camera"]++;
        Action onUi = () => counts["ui"]++;
        Action onHotbar = () => counts["hotbar"]++;
        Action onTouch = () => counts["touch"]++;
        Action onMobile = () => counts["mobile"]++;
        CameraUserSettings.Changed += onCamera;
        UIUserSettings.Changed += onUi;
        UIUserSettings.HotbarLayoutChanged += onHotbar;
        UIUserSettings.TouchControlsOpacityChanged += onTouch;
        UIUserSettings.MobileControlsChanged += onMobile;
        var observations = new JArray();
        try
        {
            foreach (var specification in specifications)
            {
                ISettingsSlider control = specification.Item1.SliderSettings.Single(value => value.Descriptor.Key == specification.Item2);
                float original = control.Value;
                var preference = new SavedPreference(specification.Item3, 0f);
                var sample = new JObject { ["key"] = control.Descriptor.Key, ["beforeEvents"] = JObject.FromObject(counts) };
                try
                {
                    float target = Mathf.Approximately(original, control.MinValue) ? control.MaxValue : control.MinValue;
                    control.SetValue(target);
                    sample["target"] = target;
                    sample["cached"] = control.Value;
                    sample["stored"] = PlayerPrefs.GetFloat(specification.Item3);
                    sample["changedEvents"] = JObject.FromObject(counts);
                    // 独立标记检测相同缓存值是否仍覆写PlayerPrefs，不把它当成Save次数统计。
                    PlayerPrefs.SetFloat(specification.Item3, -987654f);
                    control.SetValue(target);
                    sample["noOpCached"] = control.Value;
                    sample["noOpStored"] = PlayerPrefs.GetFloat(specification.Item3);
                    sample["noOpEvents"] = JObject.FromObject(counts);
                }
                finally
                {
                    try { control.SetValue(original); }
                    finally { preference.Restore(); PlayerPrefs.Save(); }
                }
                sample["preferenceRestored"] = preference.IsRestored();
                sample["original"] = original;
                sample["restoredCache"] = control.Value;
                observations.Add(sample);
            }
        }
        finally
        {
            CameraUserSettings.Changed -= onCamera;
            UIUserSettings.Changed -= onUi;
            UIUserSettings.HotbarLayoutChanged -= onHotbar;
            UIUserSettings.TouchControlsOpacityChanged -= onTouch;
            UIUserSettings.MobileControlsChanged -= onMobile;
        }
        return new JObject { ["preferences"] = observations };
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
            JArray commonPageControllers = CaptureCommonClientSettingsPageControllerBindings(panel);
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
            JObject sidebarVisuals = CaptureSettingsSidebarTabVisuals(panel);
            var group = panel.GetComponent<CanvasGroup>();
            var result = new JObject
            {
                ["formalWindow"] = panel.GetComponent<SettingsEditSessionController>() != null,
                ["commonPageControllers"] = commonPageControllers,
                ["sidebarVisuals"] = sidebarVisuals,
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

    /// <summary>采样正式设置侧栏、显示页及合并后的可滚动画质页。</summary>
    private static JObject CaptureSettingsSidebarTabVisuals(BasePanel panel)
    {
        Transform tabList = panel.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(node => node.name == SettingsActionListPagination.TabListName);
        if (tabList == null)
            throw new MissingReferenceException("正式设置窗口缺少左侧分页列表。");

        Button[] buttons = tabList.GetComponentsInChildren<Button>(true);
        Button camera = Array.Find(buttons, button => button.name == "镜头控制");
        Button initial = Array.Find(buttons, button => button.name == "UI设置");
        if (buttons.Length != 9 || camera == null || initial == null)
            throw new MissingReferenceException("正式设置窗口左侧分页按钮不完整。");

        SettingsActionListPagination pagination = panel.GetComponent<SettingsActionListPagination>();
        Transform displayPage = pagination?.GetPageRoot(SettingsActionListPagination.DisplayPageName);
        Transform graphicsPage = pagination?.GetPageRoot(SettingsActionListPagination.GraphicsPageName);
        bool displayComplete = displayPage != null &&
            displayPage.GetComponent<CoordinateDisplaySettingsPanelLauncher>() != null &&
            displayPage.GetComponent<FrameRateSettingsPanelController>() != null;
        bool graphicsComplete = graphicsPage != null &&
            graphicsPage.GetComponent<GraphicsSettingsPanelLauncher>() != null &&
            graphicsPage.GetComponentInChildren<VisualEffectsSettingsPanelLauncher>(true) != null &&
            graphicsPage.GetComponentsInChildren<TMP_Dropdown>(true)
                .Any(dropdown => dropdown.name == GameManager.MainMenuSettingsQualityPresetKey) &&
            graphicsPage.GetComponentsInChildren<TMP_Dropdown>(true)
                .Any(dropdown => dropdown.name == GameManager.MainMenuSettingsEffectsQualityKey) &&
            graphicsPage.GetComponentInChildren<ScrollRect>(true)?.vertical == true;
        bool legacyVisualPageRemoved = !panel.GetComponentsInChildren<Transform>(true)
            .Any(node => node.name == SettingsActionListPagination.VisualEffectsPageName);

        JArray SampleColor(Color color) => new JArray(color.r, color.g, color.b, color.a);
        var tabs = new JArray();
        camera.onClick.Invoke();
        try
        {
            foreach (Button button in buttons)
            {
                Image background = button.GetComponent<Image>();
                TMP_Text caption = button.GetComponentInChildren<TMP_Text>(true);
                if (background == null || caption == null)
                    throw new MissingReferenceException("设置页签缺少背景或文字：" + button.name);

                tabs.Add(new JObject
                {
                    ["name"] = button.name,
                    ["background"] = SampleColor(background.color),
                    ["caption"] = SampleColor(caption.color)
                });
            }
        }
        finally { initial.onClick.Invoke(); }

        return new JObject
        {
            ["tabs"] = tabs,
            ["displayComplete"] = displayComplete,
            ["graphicsComplete"] = graphicsComplete,
            ["legacyVisualPageRemoved"] = legacyVisualPageRemoved,
            ["activeBackground"] = SampleColor(FlatWorldUITheme.TabActive),
            ["inactiveBackground"] = SampleColor(FlatWorldUITheme.TabInactive),
            ["activeCaption"] = SampleColor(FlatWorldUITheme.TabLabelActive),
            ["inactiveCaption"] = SampleColor(FlatWorldUITheme.TabLabelInactive)
        };
    }

    // 从正式窗口采样共用页并再次装配，Lua 检查唯一组件、原实例和页面归属。
    private static JArray CaptureCommonClientSettingsPageControllerBindings(BasePanel panel)
    {
        var definitions = new (Type Controller, string Page)[]
        {
            (typeof(AudioSettingsPanelLauncher), SettingsActionListPagination.AudioPageName),
            (typeof(UISettingsPanelLauncher), SettingsActionListPagination.InterfacePageName),
            (typeof(LanguageSettingsPanelLauncher), SettingsActionListPagination.LanguagePageName),
            (typeof(CameraControlSettingsPanelLauncher), SettingsActionListPagination.CameraPageName),
            (typeof(CoordinateDisplaySettingsPanelLauncher), SettingsActionListPagination.DisplayPageName),
            (typeof(RuntimeDebugSettingsPanelLauncher), SettingsActionListPagination.DebugPageName)
        };
        var before = definitions.Select(row => panel.GetComponentsInChildren(row.Controller, true)).ToArray();
        var pagination = panel.GetComponent<SettingsActionListPagination>();
        pagination.EnsureCommonClientSettingsPageControllers();
        return new JArray(definitions.Select((row, index) =>
        {
            Component[] after = panel.GetComponentsInChildren(row.Controller, true);
            return new JObject
            {
                ["controller"] = row.Controller.Name, ["beforeCount"] = before[index].Length,
                ["afterCount"] = after.Length,
                ["instanceRetained"] = before[index].Length == 1 && after.Length == 1 && before[index][0] == after[0],
                ["expectedPageRoot"] = after.Length == 1 && after[0].transform == pagination.GetPageRoot(row.Page)
            };
        }));
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

    #region 新世界窗口关闭流程回归

    /// <summary>通过正式新世界窗口验证世界页右上关闭只回到名称页，名称页右上仍关闭窗口。</summary>
    private static JObject CaptureNewGameWindowClose(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        UIManager ui = UIManager.ExistingInstance ?? throw new InvalidOperationException("正式 UIManager 尚未就绪。");
        if (ui.TryGetPanel(GameManager.NewGamePanelKey, out _))
            throw new InvalidOperationException("当前已有新世界窗口，拒绝借用或关闭其他来源的面板。");

        GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>() ??
            throw new InvalidOperationException("正式 GameManager 尚未就绪。");
        PlanetData originalReadyPlanetData = manager.ReadyPlanetData;
        manager.ReadyPlanetData = originalReadyPlanetData == null
            ? new PlanetData()
            : FastCloner.FastCloner.DeepClone(originalReadyPlanetData);
        BasePanel panel = null;
        JObject result = null;
        try
        {
            manager.OpenNewGame();
            if (!ui.TryGetPanel(GameManager.NewGamePanelKey, out panel) || panel == null)
                throw new InvalidOperationException("正式新世界入口没有创建窗口。");

            Button closeButton = panel.GetButton(GameManager.NewGameBackButtonKey);
            Button worldSettingsButton = panel.GetButton(GameManager.NewGameWorldSettingsButtonKey);
            Button difficultyButton = panel.GetButton(GameManager.NewGameDifficultyButtonKey);
            Button bottomIdentityButton = panel.GetButton(GameManager.NewGameIdentityButtonKey);
            TMP_Text closeLabel = panel.GetText(GameManager.NewGameBackButtonKey + "_文字");
            TMP_InputField playerNameInput = panel.GetInputField(GameManager.NewGamePlayerInputKey);
            Transform[] nodes = panel.GetComponentsInChildren<Transform>(true);
            Transform identityPage = Array.Find(nodes, node => node != null && node.name == GameManager.NewGameIdentityPageKey);
            Transform worldPage = Array.Find(nodes, node => node != null && node.name == GameManager.NewGameWorldPageKey);
            Transform difficultyPage = Array.Find(nodes, node => node != null && node.name == GameManager.NewGameDifficultyPanelKey);
            if (closeButton == null || worldSettingsButton == null || difficultyButton == null || closeLabel == null ||
                playerNameInput == null || identityPage == null || worldPage == null)
                throw new InvalidOperationException("正式新世界面板缺少关闭按钮、名称输入或分页。");

            const string playerNameValue = "ui_close_probe_player";
            const string worldSeedValue = "ui_close_probe_seed";
            playerNameInput.SetTextWithoutNotify(playerNameValue);
            difficultyButton.onClick.Invoke();
            Button difficultyCloseButton = panel.GetButton(GameManager.NewGameDifficultyCloseButtonKey);
            if (difficultyCloseButton == null || difficultyPage == null)
                throw new InvalidOperationException("正式难度设置页没有创建关闭按钮或遮罩。");
            difficultyCloseButton.onClick.Invoke();
            bool difficultyCloseStayedInNewGame = panel.IsOpen() &&
                identityPage.gameObject.activeSelf && !worldPage.gameObject.activeSelf &&
                !difficultyPage.gameObject.activeSelf;

            worldSettingsButton.onClick.Invoke();
            TMP_InputField worldSeedInput = panel.GetInputField(GameManager.NewGameSeedInputKey);
            if (worldSeedInput == null)
                throw new InvalidOperationException("正式世界设置页缺少种子输入框。");
            worldSeedInput.SetTextWithoutNotify(worldSeedValue);

            bool worldPageOpened = worldPage.gameObject.activeSelf && !identityPage.gameObject.activeSelf;
            bool bottomBackHiddenInWorld = bottomIdentityButton == null || !bottomIdentityButton.gameObject.activeSelf;
            string worldCloseText = closeLabel.text;
            closeButton.onClick.Invoke();

            bool worldCloseReturnedToIdentity = panel.IsOpen() &&
                identityPage.gameObject.activeSelf && !worldPage.gameObject.activeSelf;
            bool playerNameRetained = playerNameInput.text == playerNameValue;
            bool worldSeedRetained = worldSeedInput.text == worldSeedValue;
            bool bottomBackHiddenInIdentity = bottomIdentityButton == null || !bottomIdentityButton.gameObject.activeSelf;
            string identityBackText = closeLabel.text;
            closeButton.onClick.Invoke();

            result = new JObject
            {
                ["scope"] = "live-isolated-world",
                ["difficultyCloseStayedInNewGame"] = difficultyCloseStayedInNewGame,
                ["worldPageOpened"] = worldPageOpened,
                ["worldCloseText"] = worldCloseText,
                ["expectedWorldCloseText"] = FlatWorldLocalizationService.GetUiText("关闭"),
                ["worldCloseReturnedToIdentity"] = worldCloseReturnedToIdentity,
                ["playerNameRetained"] = playerNameRetained,
                ["worldSeedRetained"] = worldSeedRetained,
                ["bottomBackHiddenInWorld"] = bottomBackHiddenInWorld,
                ["bottomBackHiddenInIdentity"] = bottomBackHiddenInIdentity,
                ["identityBackText"] = identityBackText,
                ["expectedIdentityBackText"] = FlatWorldLocalizationService.GetUiText("返回主界面"),
                ["identityTopCloseClosesPanel"] = !panel.IsOpen()
            };
        }
        finally
        {
            try
            {
                if (panel == null)
                    ui.TryGetPanel(GameManager.NewGamePanelKey, out panel);
                if (panel != null)
                {
                    panel.Close();
                    ui.DestroyPanel(panel);
                }
            }
            finally
            {
                manager.ReadyPlanetData = originalReadyPlanetData;
            }
        }

        result["panelUnregistered"] = !ui.TryGetPanel(GameManager.NewGamePanelKey, out _);
        return result;
    }

    #endregion
}
