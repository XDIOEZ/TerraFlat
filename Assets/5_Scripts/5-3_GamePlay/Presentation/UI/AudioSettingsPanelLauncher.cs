// AI-Context: 设置主面板内嵌音量页；只使用 AudioService 的设置 Provider，不创建独立窗口。
using System.Collections.Generic;
using FlatWorld.Audio;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>绑定内嵌音量页的六个通道、百分比文本与恢复默认操作。</summary>
[DisallowMultipleComponent]
public sealed class AudioSettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    /// <summary>缓存单个音量通道的控件、设置和文本刷新回调。</summary>
    private sealed class VolumeRow
    {
        public Slider Slider;
        public TextMeshProUGUI ValueText;
        public ISettingsSlider Setting;
        public UnityAction<float> RefreshLabel;
    }

    private readonly List<VolumeRow> rows = new List<VolumeRow>(6);
    private ISettingsProvider audioSettingsProvider;
    private AudioSettingsPanelBinder settingsBinder;
    private Button resetButton;
    private bool initialized;

    /// <summary>在指定内嵌页面根节点上复用或挂载音量控制器。</summary>
    public static AudioSettingsPanelLauncher Ensure(Transform pageRoot)
    {
        if (pageRoot == null)
            return null;

        AudioSettingsPanelLauncher launcher =
            pageRoot.GetComponent<AudioSettingsPanelLauncher>();
        if (launcher == null)
            launcher = pageRoot.gameObject.AddComponent<AudioSettingsPanelLauncher>();
        launcher.Initialize();
        return launcher;
    }

    /// <summary>解析页面局部控件并建立设置写入和显示刷新。</summary>
    private void Initialize()
    {
        if (initialized)
            return;

        SettingsProviderRegistry.TryGet(
            AudioService.SettingsProviderId,
            out audioSettingsProvider);
        if (audioSettingsProvider == null)
            audioSettingsProvider = AudioService.Instance;

        settingsBinder = AudioSettingsPanelBinder.Ensure(transform);
        resetButton = FindButton(transform, "恢复默认按钮");
        resetButton?.onClick.AddListener(ResetToDefault);

        BindVolumeRow("MasterVolume");
        BindVolumeRow("MusicVolume");
        BindVolumeRow("SfxVolume");
        BindVolumeRow("UIVolume");
        BindVolumeRow("AmbientVolume");
        BindVolumeRow("VoiceVolume");
        initialized = true;

        if (rows.Count != 6 || resetButton == null)
        {
            Debug.LogError(
                "[AudioSettingsPanelLauncher] 内嵌音量页控件命名契约不完整。",
                this);
        }
    }

    /// <summary>绑定一个音量滑动条与对应百分比文本。</summary>
    private void BindVolumeRow(string sliderName)
    {
        Slider slider = FindSlider(transform, sliderName);
        TextMeshProUGUI valueText = FindText(transform, sliderName + "_数值");
        ISettingsSlider setting = FindVolumeSetting(sliderName);
        if (slider == null || valueText == null || setting == null)
            return;

        slider.minValue = setting.MinValue;
        slider.maxValue = setting.MaxValue;
        UnityAction<float> refreshLabel = value => valueText.text = ToPercent(value);
        slider.onValueChanged.AddListener(refreshLabel);
        rows.Add(new VolumeRow
        {
            Slider = slider,
            ValueText = valueText,
            Setting = setting,
            RefreshLabel = refreshLabel
        });
    }

    /// <summary>恢复音频 Provider 默认值并同步全部控件。</summary>
    private void ResetToDefault()
    {
        audioSettingsProvider?.ResetToDefaults();
        settingsBinder?.Bind();
        RefreshValues();
    }

    /// <summary>从音频设置 Provider 回填六个滑动条和百分比文本。</summary>
    private void RefreshValues()
    {
        for (int index = 0; index < rows.Count; index++)
        {
            VolumeRow row = rows[index];
            if (row.Setting == null)
                continue;

            row.Slider.SetValueWithoutNotify(row.Setting.Value);
            row.ValueText.text = ToPercent(row.Setting.Value);
        }
    }

    /// <summary>按控件名解析对应音频设置项。</summary>
    private ISettingsSlider FindVolumeSetting(string sliderName)
    {
        if (audioSettingsProvider == null)
            return null;

        string key = sliderName switch
        {
            "MasterVolume" => AudioService.MasterVolumeSettingKey,
            "MusicVolume" => AudioService.MusicVolumeSettingKey,
            "SfxVolume" => AudioService.SfxVolumeSettingKey,
            "UIVolume" => AudioService.UiVolumeSettingKey,
            "AmbientVolume" => AudioService.AmbientVolumeSettingKey,
            "VoiceVolume" => AudioService.VoiceVolumeSettingKey,
            _ => null
        };
        return audioSettingsProvider.GetSlider(key);
    }

    /// <summary>音量页显示时重新绑定当前服务并刷新权威值。</summary>
    public void OnSettingsPageShown()
    {
        settingsBinder?.Bind();
        RefreshValues();
    }

    /// <summary>音量页隐藏时无需保留额外草稿。</summary>
    public void OnSettingsPageHidden()
    {
    }

    /// <summary>解除本控制器注册的按钮和滑动条监听。</summary>
    private void OnDestroy()
    {
        if (resetButton != null)
            resetButton.onClick.RemoveListener(ResetToDefault);
        for (int index = 0; index < rows.Count; index++)
        {
            VolumeRow row = rows[index];
            if (row?.Slider != null && row.RefreshLabel != null)
                row.Slider.onValueChanged.RemoveListener(row.RefreshLabel);
        }
    }

    /// <summary>把标准化音量转换为整数百分比。</summary>
    private static string ToPercent(float value)
    {
        return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
    }

    /// <summary>在页面局部按名称查找按钮。</summary>
    private static Button FindButton(Transform root, string buttonName)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int index = 0; index < buttons.Length; index++)
        {
            if (buttons[index] != null && buttons[index].name == buttonName)
                return buttons[index];
        }

        return null;
    }

    /// <summary>在页面局部按名称查找滑动条。</summary>
    private static Slider FindSlider(Transform root, string sliderName)
    {
        Slider[] sliders = root.GetComponentsInChildren<Slider>(true);
        for (int index = 0; index < sliders.Length; index++)
        {
            if (sliders[index] != null && sliders[index].name == sliderName)
                return sliders[index];
        }

        return null;
    }

    /// <summary>在页面局部按名称查找 TMP 文本。</summary>
    private static TextMeshProUGUI FindText(Transform root, string textName)
    {
        TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int index = 0; index < texts.Length; index++)
        {
            if (texts[index] != null && texts[index].name == textName)
                return texts[index];
        }

        return null;
    }
}
