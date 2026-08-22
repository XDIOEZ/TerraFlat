// AI-Context: 设置面板与 AudioService 的无侵入绑定；控件按中英文节点名匹配，缺失时静默跳过。

using System;
using FlatWorld.Audio;
using FlatWorld.Settings;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class AudioSettingsPanelBinder : MonoBehaviour
{
    private Slider master;
    private Slider music;
    private Slider sfx;
    private Slider ui;
    private Slider ambient;
    private Slider voice;
    private Toggle muted;
    private ISettingsSlider masterSetting;
    private ISettingsSlider musicSetting;
    private ISettingsSlider sfxSetting;
    private ISettingsSlider uiSetting;
    private ISettingsSlider ambientSetting;
    private ISettingsSlider voiceSetting;
    private ISettingsToggle mutedSetting;
    private bool bound;

    public static AudioSettingsPanelBinder Ensure(Transform root)
    {
        if (root == null)
            return null;

        AudioSettingsPanelBinder binder = root.GetComponent<AudioSettingsPanelBinder>();
        if (binder == null)
            binder = root.gameObject.AddComponent<AudioSettingsPanelBinder>();
        binder.Bind();
        return binder;
    }

    private void Awake()
    {
        Bind();
    }

    public void Bind()
    {
        if (bound)
            Unbind();

        master = FindSlider("主音量", "总体音量", "MasterVolume", "Master Volume");
        music = FindSlider("音乐音量", "背景音乐音量", "MusicVolume", "Music Volume");
        sfx = FindSlider("音效音量", "SoundVolume", "SfxVolume", "SFX Volume");
        ui = FindSlider("UI音量", "界面音量", "UIVolume", "UI Volume");
        ambient = FindSlider("环境音量", "AmbientVolume", "Ambient Volume");
        voice = FindSlider("语音音量", "VoiceVolume", "Voice Volume");
        muted = FindToggle("静音", "全部静音", "Mute", "Muted");

        if (!SettingsProviderRegistry.TryGet(
                AudioService.SettingsProviderId,
                out ISettingsProvider provider))
        {
            provider = AudioService.Instance;
        }

        masterSetting = provider.GetSlider(AudioService.MasterVolumeSettingKey);
        musicSetting = provider.GetSlider(AudioService.MusicVolumeSettingKey);
        sfxSetting = provider.GetSlider(AudioService.SfxVolumeSettingKey);
        uiSetting = provider.GetSlider(AudioService.UiVolumeSettingKey);
        ambientSetting = provider.GetSlider(AudioService.AmbientVolumeSettingKey);
        voiceSetting = provider.GetSlider(AudioService.VoiceVolumeSettingKey);
        mutedSetting = provider.GetToggle(AudioService.MutedSettingKey);

        SetSlider(master, masterSetting, OnMasterChanged);
        SetSlider(music, musicSetting, OnMusicChanged);
        SetSlider(sfx, sfxSetting, OnSfxChanged);
        SetSlider(ui, uiSetting, OnUiChanged);
        SetSlider(ambient, ambientSetting, OnAmbientChanged);
        SetSlider(voice, voiceSetting, OnVoiceChanged);

        if (muted != null && mutedSetting != null)
        {
            muted.SetIsOnWithoutNotify(mutedSetting.Value);
            muted.onValueChanged.AddListener(OnMutedChanged);
        }

        bound = true;
    }

    private void OnDestroy()
    {
        Unbind();
    }

    private void Unbind()
    {
        RemoveSlider(master, OnMasterChanged);
        RemoveSlider(music, OnMusicChanged);
        RemoveSlider(sfx, OnSfxChanged);
        RemoveSlider(ui, OnUiChanged);
        RemoveSlider(ambient, OnAmbientChanged);
        RemoveSlider(voice, OnVoiceChanged);
        if (muted != null)
            muted.onValueChanged.RemoveListener(OnMutedChanged);
        bound = false;
    }

    private void OnMasterChanged(float value) => masterSetting?.SetValue(value);
    private void OnMusicChanged(float value) => musicSetting?.SetValue(value);
    private void OnSfxChanged(float value) => sfxSetting?.SetValue(value);
    private void OnUiChanged(float value) => uiSetting?.SetValue(value);
    private void OnAmbientChanged(float value) => ambientSetting?.SetValue(value);
    private void OnVoiceChanged(float value) => voiceSetting?.SetValue(value);
    private void OnMutedChanged(bool value) => mutedSetting?.SetValue(value);

    private Slider FindSlider(params string[] names)
    {
        Slider[] values = GetComponentsInChildren<Slider>(true);
        return FindByName(values, names);
    }

    private Toggle FindToggle(params string[] names)
    {
        Toggle[] values = GetComponentsInChildren<Toggle>(true);
        return FindByName(values, names);
    }

    private static T FindByName<T>(T[] values, string[] names) where T : Component
    {
        for (int i = 0; i < values.Length; i++)
        {
            T value = values[i];
            for (int j = 0; j < names.Length; j++)
            {
                if (string.Equals(value.name, names[j], StringComparison.OrdinalIgnoreCase))
                    return value;
            }
        }

        return null;
    }

    private static void SetSlider(
        Slider slider,
        ISettingsSlider setting,
        UnityEngine.Events.UnityAction<float> callback)
    {
        if (slider == null || setting == null)
            return;

        slider.minValue = setting.MinValue;
        slider.maxValue = setting.MaxValue;
        slider.SetValueWithoutNotify(setting.Value);
        slider.onValueChanged.AddListener(callback);
    }

    private static void RemoveSlider(Slider slider, UnityEngine.Events.UnityAction<float> callback)
    {
        if (slider != null)
            slider.onValueChanged.RemoveListener(callback);
    }
}
