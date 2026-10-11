using System;
using System.Collections.Generic;
using FlatWorld.Audio;
using UnityEngine;

public partial class WeatherMgr
{
    #region 天气声音状态

    private const float WeatherAudioThreshold = 0.001f;
    private const float ThunderAudibleRadius = 192f;
    private const float ThunderPropagationSpeed = 64f;
    private const int MaximumPendingThunder = 8;
    private AudioHandle rainAudioHandle;
    private AudioHandle windAudioHandle;
    private float rainAudioVolume;
    private float windAudioVolume;
    private bool weatherAudioSubscribed;
    private bool weatherAudioBaselineReady;
    private readonly List<PendingWeatherThunder> pendingWeatherThunder = new(8);
    private readonly List<AudioHandle> weatherThunderHandles = new(3);

    private readonly struct PendingWeatherThunder
    {
        public readonly RegionalLightningEvent Lightning;
        public readonly float PlayTime;

        public PendingWeatherThunder(RegionalLightningEvent lightning, float playTime)
        {
            Lightning = lightning;
            PlayTime = playTime;
        }
    }

    #endregion

    #region 订阅与释放

    private void ActivateWeatherAudio()
    {
        LightningOccurred -= HandleWeatherLightningAudio;
        LightningOccurred += HandleWeatherLightningAudio;
        weatherAudioSubscribed = true;
        ResetWeatherAudioBaseline();
    }

    // 加载后的首次推进只建立雷声基线，不补播存档期间积累的雷声。
    private void ResetWeatherAudioBaseline()
    {
        weatherAudioBaselineReady = false;
        pendingWeatherThunder.Clear();
        for (int i = 0; i < weatherThunderHandles.Count; i++)
            weatherThunderHandles[i].Stop(0.15f);
        weatherThunderHandles.Clear();
    }

    private void CompleteWeatherAudioBaseline()
    {
        if (weatherAudioSubscribed)
            weatherAudioBaselineReady = true;
    }

    private void DeactivateWeatherAudio()
    {
        LightningOccurred -= HandleWeatherLightningAudio;
        weatherAudioSubscribed = false;
        ResetWeatherAudioBaseline();
        rainAudioHandle.Stop(0.5f);
        windAudioHandle.Stop(0.5f);
        rainAudioHandle = windAudioHandle = AudioHandle.Invalid;
        rainAudioVolume = windAudioVolume = 0f;
    }

    private void OnEnable()
    {
        if (_weatherRuntimeAllowed)
            ActivateWeatherAudio();
    }

    private void OnDisable() => DeactivateWeatherAudio();

    #endregion

    #region 当地雨风循环

    private void MaintainWeatherAudio()
    {
        if (!_weatherRuntimeAllowed || !weatherAudioSubscribed)
            return;

        // 每帧读取实际雨雪分流和当地风力，气温变化也会及时改变雨声。
        float rain = Mathf.Clamp01(GetLocalRainIntensity());
        float wind = Mathf.Clamp01(GetCurrentWindStrength());
        UpdateWeatherLoop(AudioEventIds.WeatherRainLoop, rain, 0.9f * Mathf.Sqrt(rain),
            ref rainAudioHandle, ref rainAudioVolume);
        UpdateWeatherLoop(AudioEventIds.WeatherWindLoop, wind, 0.8f * Mathf.Pow(wind, 1.5f),
            ref windAudioHandle, ref windAudioVolume);
        PlayPendingWeatherThunder();
    }

    private static void UpdateWeatherLoop(string cueId, float intensity, float targetVolume,
        ref AudioHandle handle, ref float currentVolume)
    {
        if (intensity <= WeatherAudioThreshold)
        {
            handle.Stop(0.8f);
            handle = AudioHandle.Invalid;
            currentVolume = 0f;
            return;
        }

        if (!handle.IsPlaying)
        {
            currentVolume = targetVolume;
            AudioPlayOptions options = AudioPlayOptions.Global(currentVolume);
            options.FadeIn = 0.8f;
            options.OverrideLoop = true;
            options.Loop = true;
            handle = AudioService.Instance.Play(cueId, options);
            return;
        }

        currentVolume = Mathf.MoveTowards(currentVolume, targetVolume, Time.unscaledDeltaTime * 0.75f);
        AudioService.Instance.SetVolumeScale(handle, currentVolume);
    }

    #endregion

    #region 单次雷声与传播延迟

    private bool TryGetWeatherAudioPlayer(out Player player)
    {
        player = ItemMgr.GetInstance()?.User_Player;
        return _weatherRuntimeAllowed && isActiveAndEnabled &&
               player != null && player.IsLocalProfile && player.gameObject.activeInHierarchy;
    }

    private void HandleWeatherLightningAudio(RegionalLightningEvent lightning)
    {
        if (!weatherAudioBaselineReady || pendingWeatherThunder.Count >= MaximumPendingThunder ||
            !string.Equals(lightning.WorldKey, regionalPlanet?.Name, StringComparison.Ordinal) ||
            !TryGetWeatherAudioPlayer(out Player player))
            return;

        float distance = WorldTopologyRuntime.ShortestDelta(player.transform.position, lightning.Position).magnitude;
        if (distance >= ThunderAudibleRadius || lightning.Strength <= 0f)
            return;

        pendingWeatherThunder.Add(new PendingWeatherThunder(lightning,
            Time.unscaledTime + distance / ThunderPropagationSpeed));
    }

    private void PlayPendingWeatherThunder()
    {
        for (int i = weatherThunderHandles.Count - 1; i >= 0; i--)
            if (!weatherThunderHandles[i].IsPlaying)
                weatherThunderHandles.RemoveAt(i);

        for (int i = pendingWeatherThunder.Count - 1; i >= 0; i--)
        {
            PendingWeatherThunder pending = pendingWeatherThunder[i];
            if (Time.unscaledTime < pending.PlayTime)
                continue;
            pendingWeatherThunder.RemoveAt(i);
            if (!string.Equals(pending.Lightning.WorldKey, regionalPlanet?.Name, StringComparison.Ordinal) ||
                !TryGetWeatherAudioPlayer(out Player player))
                continue;

            // 环绕地图按最短距离调音量，等待期间走远的玩家也不会听到近雷。
            float distance = WorldTopologyRuntime.ShortestDelta(player.transform.position, pending.Lightning.Position).magnitude;
            float volume = Mathf.Clamp01(pending.Lightning.Strength) * Mathf.Clamp01(1f - distance / ThunderAudibleRadius);
            if (volume <= WeatherAudioThreshold)
                continue;
            AudioHandle handle = AudioService.Instance.Play(AudioEventIds.WeatherThunder, AudioPlayOptions.Global(volume));
            if (handle.IsValid)
                weatherThunderHandles.Add(handle);
        }
    }

    #endregion
}
