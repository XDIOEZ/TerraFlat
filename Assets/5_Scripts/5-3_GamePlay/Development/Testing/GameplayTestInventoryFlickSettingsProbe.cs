using System;
using System.Collections;
using System.Linq;
using FlatWorld.Settings;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>采样正式甩动偏好、设置会话与控件，业务期望由 Lua 断言。</summary>
public static class GameplayTestInventoryFlickSettingsProbe
{
    #region 测试操作登记
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register()
    {
        GameplayTestSystemProbe.Register("inventory", "flick_settings_normalize", CaptureNormalize);
        GameplayTestSystemProbe.Register("inventory", "flick_settings_gesture", CaptureGesture);
        GameplayTestSystemProbe.Register("ui_settings", "flick_settings_provider", CaptureProvider);
        GameplayTestSystemProbe.Register("ui_settings", "flick_settings_session", CaptureSession);
        GameplayTestRuntimeProbe.RegisterAsync("ui_settings", "flick_settings_controls", CaptureControls);
    }
    #endregion

    #region 正式偏好与阈值采样
    private static JObject CaptureNormalize(JObject args)
    {
        float value = GameplayTestSystemProbe.Text(args, "invalid", "") switch
        {
            "nan" => float.NaN,
            "positive_infinity" => float.PositiveInfinity,
            "negative_infinity" => float.NegativeInfinity,
            _ => (float)GameplayTestSystemProbe.Number(args, "value", 100d)
        };
        return new JObject { ["value"] = InventoryFlickUserSettings.NormalizeThresholdPercent(value),
            ["minimum"] = InventoryFlickUserSettings.MinimumThresholdPercent,
            ["maximum"] = InventoryFlickUserSettings.MaximumThresholdPercent,
            ["default"] = InventoryFlickUserSettings.DefaultThresholdPercent,
            ["step"] = InventoryFlickUserSettings.ThresholdPercentStep,
            ["defaultAngleLimit"] = InventoryFlickUserSettings.DefaultAngleLimitEnabled };
    }

    private static JObject CaptureGesture(JObject args)
    {
        var scope = new GameplayTestInventoryFlickSettingsScope();
        JObject result;
        try
        {
            scope.Apply(args);
            if (args["points"] is not JArray points || points.Count < 2 || points.Count > 128)
                throw new ArgumentException("甩动设置采样需要 2 至 128 个轨迹点。");
            Vector2 Position(JToken point) => new(point[1].Value<float>(), point[2].Value<float>());
            var gesture = new InventoryFlickGesture();
            gesture.Begin(Position(points[0]), points[0][0].Value<double>(), 1f);
            foreach (JToken point in points.Skip(1)) gesture.Record(Position(point), point[0].Value<double>());
            JToken last = points[points.Count - 1];
            result = Snapshot();
            result["accepted"] = gesture.IsFlick(Position(last), last[0].Value<double>(),
                InventoryFlickUserSettings.MinimumDistance, InventoryFlickUserSettings.MinimumSpeed);
        }
        finally { scope.Dispose(); }
        result["preferencesRestored"] = scope.IsRestored;
        return result;
    }

    private static JObject CaptureProvider(JObject args)
    {
        var scope = new GameplayTestInventoryFlickSettingsScope();
        JObject result;
        try
        {
            scope.Apply(new JObject());
            ISettingsProvider provider = scope.Provider;
            ISettingsSlider speed = provider.GetSlider(InventoryFlickUserSettings.SpeedPercentSettingKey);
            ISettingsSlider distance = provider.GetSlider(InventoryFlickUserSettings.DistancePercentSettingKey);
            ISettingsToggle angle = provider.GetToggle(InventoryFlickUserSettings.AngleLimitSettingKey);
            result = new JObject { ["providerId"] = provider.ProviderId,
                ["sliderCount"] = provider.SliderSettings.Count, ["toggleCount"] = provider.ToggleSettings.Count,
                ["speed"] = SliderSnapshot(speed), ["distance"] = SliderSnapshot(distance),
                ["angleKey"] = angle?.Descriptor.Key, ["angleDefault"] = angle?.Value,
                ["registered"] = SettingsProviderRegistry.Providers.Contains(provider) };
            speed.SetValue((float)GameplayTestSystemProbe.Number(args, "speedPercent", 25d));
            result["afterSpeed"] = Snapshot();
            distance.SetValue((float)GameplayTestSystemProbe.Number(args, "distancePercent", 200d));
            result["afterDistance"] = Snapshot();
            angle.SetValue(false);
            result["afterAngle"] = Snapshot();
            result["persisted"] = scope.PersistedSnapshot();
            provider.ResetToDefaults();
            result["reset"] = Snapshot();
        }
        finally { scope.Dispose(); }
        result["preferencesRestored"] = scope.IsRestored;
        return result;
    }

    private static JObject CaptureSession(JObject args)
    {
        var scope = new GameplayTestInventoryFlickSettingsScope();
        JObject result;
        try
        {
            string mode = GameplayTestSystemProbe.Text(args, "mode", "cancel");
            scope.Apply(new JObject());
            SettingsProviderRegistry.CommitEditSession();
            ISettingsProvider provider = scope.Provider;
            ISettingsSlider speed = provider.GetSlider(InventoryFlickUserSettings.SpeedPercentSettingKey);
            ISettingsSlider distance = provider.GetSlider(InventoryFlickUserSettings.DistancePercentSettingKey);
            ISettingsToggle angle = provider.GetToggle(InventoryFlickUserSettings.AngleLimitSettingKey);
            if (mode != "unchanged")
            {
                speed.SetValue(25f); distance.SetValue(200f); angle.SetValue(false);
            }
            if (mode == "roundtrip") provider.ResetToDefaults();
            if (mode == "commit")
            {
                SettingsProviderRegistry.CommitEditSession();
                speed.SetValue(200f); distance.SetValue(25f); angle.SetValue(true);
            }
            if (mode == "reset")
            {
                SettingsProviderRegistry.CommitEditSession();
                provider.ResetToDefaults();
            }
            result = new JObject { ["beforeDiscard"] = Snapshot(),
                ["changed"] = SettingsProviderRegistry.HasEditSessionChanges() };
            result["discarded"] = scope.DiscardSession(out string error, out bool attempted);
            result["error"] = error; result["attempted"] = attempted;
            result["afterDiscard"] = Snapshot();
            result["persisted"] = scope.PersistedSnapshot();
            result["secondDiscard"] = SettingsProviderRegistry.DiscardEditSession(out _, out bool repeated);
            result["secondAttempted"] = repeated;
        }
        finally { scope.Dispose(); }
        result["preferencesRestored"] = scope.IsRestored;
        return result;
    }

    private static JObject Snapshot() => new() { ["speedPercent"] = InventoryFlickUserSettings.SpeedPercent,
        ["distancePercent"] = InventoryFlickUserSettings.DistancePercent,
        ["angleLimit"] = InventoryFlickUserSettings.AngleLimitEnabled,
        ["minimumSpeed"] = InventoryFlickUserSettings.MinimumSpeed,
        ["minimumDistance"] = InventoryFlickUserSettings.MinimumDistance };

    private static JObject SliderSnapshot(ISettingsSlider slider) => slider == null ? null : new JObject {
        ["key"] = slider.Descriptor.Key, ["category"] = slider.Descriptor.Category,
        ["kind"] = slider.Descriptor.ControlType.ToString(), ["value"] = slider.Value,
        ["min"] = slider.MinValue, ["max"] = slider.MaxValue, ["step"] = slider.Step };
    #endregion

    #region 正式游戏操作页控件现场
    private static IEnumerator CaptureControls(Player player, JObject args, Action<JObject> complete)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        UIManager ui = UIManager.ExistingInstance ?? throw new InvalidOperationException("正式 UIManager 尚未就绪。");
        if (ui.TryGetPanel(GameManager.MainMenuSettingsPanelKey, out _))
            throw new InvalidOperationException("当前已有设置窗口，拒绝借用其他来源的面板。");
        var scope = new FlickSettingsWindowScope(ui);
        GameplayTestRuntimeProbe.RegisterCleanup(scope);
        JObject result;
        try
        {
            scope.Settings.Apply(new JObject());
            GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>() ??
                throw new InvalidOperationException("正式 GameManager 尚未就绪。");
            manager.OpenMainMenuSettings();
            if (!ui.TryGetPanel(GameManager.MainMenuSettingsPanelKey, out scope.Panel))
                throw new InvalidOperationException("正式设置入口没有创建窗口。");
            yield return WaitForSettingsPanel(scope.Panel);
            scope.Panel.GetButton("按键绑定").onClick.Invoke();
            yield return null;
            InputBindingPanelLauncher launcher = scope.Panel.GetComponentInChildren<InputBindingPanelLauncher>(true);
            Slider speed = launcher.GetComponentsInChildren<Slider>(true).Single(value => value.name == "甩动转移速度");
            Slider distance = launcher.GetComponentsInChildren<Slider>(true).Single(value => value.name == "甩动转移距离");
            Toggle angle = launcher.GetComponentsInChildren<Toggle>(true).Single(value => value.name == "甩动方向限制");
            TMP_Text speedText = launcher.GetComponentsInChildren<TMP_Text>(true).Single(value => value.name == "甩动转移速度_数值");
            TMP_Text distanceText = launcher.GetComponentsInChildren<TMP_Text>(true).Single(value => value.name == "甩动转移距离_数值");
            ScrollRect scroll = speed.GetComponentInParent<ScrollRect>();
            result = new JObject { ["initial"] = ControlSnapshot(speed, distance, angle, speedText, distanceText),
                ["scrollContainsOptions"] = scroll != null && speed.transform.IsChildOf(scroll.content) &&
                    distance.transform.IsChildOf(scroll.content) && angle.transform.IsChildOf(scroll.content),
                ["wholeNumbers"] = speed.wholeNumbers && distance.wholeNumbers,
                ["speedMin"] = speed.minValue, ["speedMax"] = speed.maxValue,
                ["distanceMin"] = distance.minValue, ["distanceMax"] = distance.maxValue,
                ["active"] = launcher.gameObject.activeInHierarchy && speed.isActiveAndEnabled && angle.isActiveAndEnabled };
            speed.value = 25f; distance.value = 200f; angle.isOn = false;
            result["applied"] = Snapshot();
            result["editedControls"] = ControlSnapshot(speed, distance, angle, speedText, distanceText);
            result["dirtyBeforeSave"] = SettingsProviderRegistry.HasEditSessionChanges();
            scope.Panel.GetButton(SettingsEditSessionController.SaveButtonName).onClick.Invoke();
            result["cleanAfterSave"] = !SettingsProviderRegistry.HasEditSessionChanges();
            result["persisted"] = scope.Settings.PersistedSnapshot();
            speed.value = 200f; distance.value = 25f; angle.isOn = true;
            result["dirtyAfterSave"] = SettingsProviderRegistry.HasEditSessionChanges();
            scope.Panel.Close();
            result["afterClose"] = Snapshot();
            manager.OpenMainMenuSettings();
            yield return WaitForSettingsPanel(scope.Panel);
            scope.Panel.GetButton("按键绑定").onClick.Invoke();
            yield return null;
            result["reopenedControls"] = ControlSnapshot(speed, distance, angle, speedText, distanceText);
        }
        finally { scope.Dispose(); }
        result["preferencesRestored"] = scope.Settings.IsRestored;
        result["inputPreferenceRestored"] = scope.InputPreferenceRestored;
        result["panelUnregistered"] = !ui.GetAllPanelsOfType(GameManager.MainMenuSettingsPanelKey).Contains(scope.Panel);
        complete(result);
    }

    private static JObject ControlSnapshot(Slider speed, Slider distance, Toggle angle, TMP_Text speedText, TMP_Text distanceText)
        => new() { ["speed"] = speed.value, ["distance"] = distance.value, ["angle"] = angle.isOn,
            ["speedText"] = speedText.text, ["distanceText"] = distanceText.text };

    private static IEnumerator WaitForSettingsPanel(BasePanel panel)
    {
        float deadline = Time.realtimeSinceStartup + 8f;
        yield return null;
        while (panel != null && panel.IsVisualTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
        if (panel == null || panel.IsVisualTransitioning) throw new InvalidOperationException("设置窗口过渡没有完成。");
    }

    private sealed class FlickSettingsWindowScope : IDisposable
    {
        private const string InputPreferenceKey = "FlatWorld.InputBindings.v1";
        private readonly bool inputPreferenceExisted = PlayerPrefs.HasKey(InputPreferenceKey);
        private readonly string inputPreference = PlayerPrefs.GetString(InputPreferenceKey);
        public readonly GameplayTestInventoryFlickSettingsScope Settings = new(ownEditSession: false);
        private readonly UIManager ui;
        public BasePanel Panel;
        private bool disposed;
        public FlickSettingsWindowScope(UIManager ui) => this.ui = ui;
        public bool InputPreferenceRestored => PlayerPrefs.HasKey(InputPreferenceKey) == inputPreferenceExisted &&
            (!inputPreferenceExisted || PlayerPrefs.GetString(InputPreferenceKey) == inputPreference);
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (Panel == null) ui.TryGetPanel(GameManager.MainMenuSettingsPanelKey, out Panel);
                if (Panel != null) { Panel.Close(); ui.DestroyPanel(Panel); }
            }
            finally
            {
                try { Settings.Dispose(); }
                finally
                {
                    if (inputPreferenceExisted) PlayerPrefs.SetString(InputPreferenceKey, inputPreference);
                    else PlayerPrefs.DeleteKey(InputPreferenceKey);
                    PlayerPrefs.Save();
                }
            }
        }
    }
    #endregion
}

/// <summary>隔离甩动偏好，恢复有效缓存和原始 PlayerPrefs 值及键存在性。</summary>
internal sealed class GameplayTestInventoryFlickSettingsScope : IDisposable
{
    #region 偏好原值与会话所有权
    private const string SpeedKey = "FlatWorld.Inventory.FlickSpeedPercent";
    private const string DistanceKey = "FlatWorld.Inventory.FlickDistancePercent";
    private const string AngleKey = "FlatWorld.Inventory.FlickAngleLimit";
    private readonly bool speedExisted = PlayerPrefs.HasKey(SpeedKey);
    private readonly bool distanceExisted = PlayerPrefs.HasKey(DistanceKey);
    private readonly bool angleExisted = PlayerPrefs.HasKey(AngleKey);
    private readonly float storedSpeed = PlayerPrefs.GetFloat(SpeedKey);
    private readonly float storedDistance = PlayerPrefs.GetFloat(DistanceKey);
    private readonly int storedAngle = PlayerPrefs.GetInt(AngleKey);
    private readonly float originalSpeed = InventoryFlickUserSettings.SpeedPercent;
    private readonly float originalDistance = InventoryFlickUserSettings.DistancePercent;
    private readonly bool originalAngle = InventoryFlickUserSettings.AngleLimitEnabled;
    private readonly ISettingsProvider previousProvider;
    public readonly ISettingsProvider Provider;
    private bool ownsSession;
    private bool disposed;

    public GameplayTestInventoryFlickSettingsScope(bool ownEditSession = true)
    {
        previousProvider = SettingsProviderRegistry.Providers.FirstOrDefault(value =>
            value.ProviderId == InventoryFlickUserSettings.SettingsProviderId);
        if (!SettingsProviderRegistry.BeginEditSession())
            throw new InvalidOperationException("已有活动设置会话，拒绝覆盖用户正在编辑的偏好。");
        ownsSession = true;
        Provider = InventoryFlickUserSettings.SettingsProvider;
        if (!ownEditSession) DiscardSession(out _, out _);
    }

    public void Apply(JObject args)
    {
        // 先经正式接口建立确定的偏好基线，避免玩家原始越界键干扰同值写入的采样。
        InventoryFlickUserSettings.SetSpeedPercent(InventoryFlickUserSettings.SpeedPercent == 25f ? 200f : 25f);
        InventoryFlickUserSettings.SetDistancePercent(InventoryFlickUserSettings.DistancePercent == 25f ? 200f : 25f);
        InventoryFlickUserSettings.SetAngleLimitEnabled(!InventoryFlickUserSettings.AngleLimitEnabled);
        InventoryFlickUserSettings.SetSpeedPercent((float)GameplayTestSystemProbe.Number(args, "speedPercent", 100d));
        InventoryFlickUserSettings.SetDistancePercent((float)GameplayTestSystemProbe.Number(args, "distancePercent", 100d));
        InventoryFlickUserSettings.SetAngleLimitEnabled(GameplayTestSystemProbe.Flag(args, "angleLimit", true));
    }

    public bool DiscardSession(out string error, out bool attempted)
    {
        ownsSession = false;
        return SettingsProviderRegistry.DiscardEditSession(out error, out attempted);
    }

    public JObject PersistedSnapshot() => new() { ["speedPercent"] = PlayerPrefs.GetFloat(SpeedKey, 100f),
        ["distancePercent"] = PlayerPrefs.GetFloat(DistanceKey, 100f), ["angleLimit"] = PlayerPrefs.GetInt(AngleKey, 1) != 0 };

    public bool IsRestored => InventoryFlickUserSettings.SpeedPercent == originalSpeed &&
        InventoryFlickUserSettings.DistancePercent == originalDistance && InventoryFlickUserSettings.AngleLimitEnabled == originalAngle &&
        PlayerPrefs.HasKey(SpeedKey) == speedExisted && PlayerPrefs.HasKey(DistanceKey) == distanceExisted &&
        PlayerPrefs.HasKey(AngleKey) == angleExisted && (!speedExisted || PlayerPrefs.GetFloat(SpeedKey).Equals(storedSpeed)) &&
        (!distanceExisted || PlayerPrefs.GetFloat(DistanceKey).Equals(storedDistance)) &&
        (!angleExisted || PlayerPrefs.GetInt(AngleKey) == storedAngle);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { if (ownsSession) DiscardSession(out _, out _); }
        finally
        {
            try
            {
                InventoryFlickUserSettings.SetSpeedPercent(originalSpeed);
                InventoryFlickUserSettings.SetDistancePercent(originalDistance);
                InventoryFlickUserSettings.SetAngleLimitEnabled(originalAngle);
            }
            finally
            {
                if (speedExisted) PlayerPrefs.SetFloat(SpeedKey, storedSpeed); else PlayerPrefs.DeleteKey(SpeedKey);
                if (distanceExisted) PlayerPrefs.SetFloat(DistanceKey, storedDistance); else PlayerPrefs.DeleteKey(DistanceKey);
                if (angleExisted) PlayerPrefs.SetInt(AngleKey, storedAngle); else PlayerPrefs.DeleteKey(AngleKey);
                PlayerPrefs.Save();
                if (!ReferenceEquals(previousProvider, Provider))
                {
                    SettingsProviderRegistry.Unregister(Provider);
                    if (previousProvider != null) SettingsProviderRegistry.Register(previousProvider);
                }
            }
        }
    }
    #endregion
}
