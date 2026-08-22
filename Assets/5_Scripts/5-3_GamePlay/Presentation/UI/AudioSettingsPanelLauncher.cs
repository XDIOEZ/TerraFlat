// AI-Context: 设置面板的音量入口与独立音量窗口；只使用 AudioService 的公开总线 API，不直接操作 AudioSource。
using System.Collections.Generic;
using FlatWorld.Audio;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class AudioSettingsPanelLauncher : MonoBehaviour
{
    private const string EntryButtonName = "音量调节";

    private sealed class VolumeRow
    {
        public Slider Slider;
        public TextMeshProUGUI ValueText;
        public ISettingsSlider Setting;
    }

    private readonly List<VolumeRow> rows = new List<VolumeRow>(6);
    private Button entryButton;
    private BasePanel volumePanel;
    private ISettingsProvider audioSettingsProvider;

    public static AudioSettingsPanelLauncher Ensure(Transform settingsPanel)
    {
        if (settingsPanel == null)
            return null;

        AudioSettingsPanelLauncher launcher = settingsPanel.GetComponent<AudioSettingsPanelLauncher>();
        if (launcher == null)
            launcher = settingsPanel.gameObject.AddComponent<AudioSettingsPanelLauncher>();
        launcher.EnsureEntryButton();
        return launcher;
    }

private void EnsureEntryButton()
    {
        if (entryButton == null)
            entryButton = FindButton(transform, EntryButtonName);

        if (entryButton == null)
        {
            Debug.LogError(
                $"[AudioSettingsPanelLauncher] Prefab 缺少入口按钮“{EntryButtonName}”。",
                this);
            return;
        }

        entryButton.onClick.RemoveListener(Open);
        entryButton.onClick.AddListener(Open);
    }

private void Open()
    {
        EnsureVolumePanel();
        if (volumePanel == null)
            return;

        volumePanel.Open();
        volumePanel.transform.SetAsLastSibling();
        AudioSettingsPanelBinder.Ensure(volumePanel.transform);
        RefreshValues();
    }

private void EnsureVolumePanel()
    {
        if (volumePanel != null)
            return;

        GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.AudioSettings);
        if (prefab == null)
        {
            Debug.LogError(
                $"[AudioSettingsPanelLauncher] 缺少 Prefab：{RuntimeUIPrefabKeys.AudioSettings}。",
                this);
            return;
        }

        volumePanel = UIManager.Instance.CreatePanelFromGameObject(
            prefab,
            RuntimeUIPrefabKeys.AudioSettings);
        volumePanel.GetButton("关闭按钮")?.onClick.AddListener(Close);
        volumePanel.GetButton("恢复默认按钮")?.onClick.AddListener(ResetToDefault);
        volumePanel.GetButton("完成按钮")?.onClick.AddListener(Close);

        SettingsProviderRegistry.TryGet(
            AudioService.SettingsProviderId,
            out audioSettingsProvider);
        if (audioSettingsProvider == null)
            audioSettingsProvider = AudioService.Instance;

        rows.Clear();
        BindVolumeRow("MasterVolume");
        BindVolumeRow("MusicVolume");
        BindVolumeRow("SfxVolume");
        BindVolumeRow("UIVolume");
        BindVolumeRow("AmbientVolume");
        BindVolumeRow("VoiceVolume");

        if (rows.Count != 6)
            Debug.LogError("[AudioSettingsPanelLauncher] 音量 Prefab 控件命名契约不完整。", volumePanel);

        AudioSettingsPanelBinder.Ensure(volumePanel.transform);
        RefreshValues();
        volumePanel.PrepareForGamepadNavigation("MasterVolume");
        volumePanel.Close();
    }

private void BindVolumeRow(string sliderName)
    {
        Slider slider = volumePanel.GetSlider(sliderName);
        TextMeshProUGUI valueText = volumePanel.GetText(sliderName + "_数值");
        ISettingsSlider setting = FindVolumeSetting(sliderName);
        if (slider == null || valueText == null || setting == null)
            return;

        slider.minValue = setting.MinValue;
        slider.maxValue = setting.MaxValue;
        slider.SetValueWithoutNotify(setting.Value);
        slider.onValueChanged.AddListener(value => valueText.text = ToPercent(value));
        rows.Add(new VolumeRow
        {
            Slider = slider,
            ValueText = valueText,
            Setting = setting
        });
    }




    private void ResetToDefault()
    {
        audioSettingsProvider?.ResetToDefaults();
        AudioSettingsPanelBinder.Ensure(volumePanel.transform);
        RefreshValues();
    }

private void RefreshValues()
    {
        if (volumePanel == null)
            return;

        for (int i = 0; i < rows.Count; i++)
        {
            ISettingsSlider setting = rows[i].Setting;
            if (setting == null)
                continue;

            rows[i].Slider.SetValueWithoutNotify(setting.Value);
            rows[i].ValueText.text = ToPercent(setting.Value);
        }
    }

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

private void Close()
    {
        volumePanel?.Close();
    }

private void OnDestroy()
    {
        if (entryButton != null)
            entryButton.onClick.RemoveListener(Open);
        if (volumePanel != null)
            Destroy(volumePanel.gameObject);
    }













    private static string ToPercent(float value)
    {
        return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
    }


private static Button FindButton(Transform root, string buttonName)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].name == buttonName)
                return buttons[i];
        }

        return null;
    }
}
