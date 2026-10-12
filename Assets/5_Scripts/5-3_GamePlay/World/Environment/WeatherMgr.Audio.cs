using System;
using System.Collections.Generic;
using FlatWorld.Audio;
using UnityEngine;

public partial class WeatherMgr
{
    #region 天气声音状态

    private const float WeatherAudioThreshold = 0.001f;
    private const float WindAudioStartStrength = 0.5f;
    private const float WindAudioMaximumVolume = 0.4f;
    private const float ThunderAudibleRadius = 192f;
    private const float ThunderNearRadius = 64f;
    private const float ThunderPropagationSpeed = 64f;
    private const int MaximumPendingThunder = 8;
    private AudioHandle rainAudioHandle;
    private AudioHandle windAudioHandle;
    private float rainAudioVolume;
    private float windAudioVolume;
    private bool weatherAudioSubscribed;
    private bool weatherAudioBaselineReady;
    private readonly List<PendingWeatherThunder> pendingWeatherThunder = new(8);
    private readonly List<ActiveWeatherThunder> weatherThunderHandles = new(6);

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

    private readonly struct ActiveWeatherThunder
    {
        public readonly AudioHandle Handle;
        public readonly Vector2 StrikePosition;

        public ActiveWeatherThunder(AudioHandle handle, Vector2 strikePosition)
        {
            Handle = handle;
            StrikePosition = strikePosition;
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
            weatherThunderHandles[i].Handle.Stop(0.15f);
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

        // 雨风声音消费同一份表现过渡，进入和离开区块都从当前音量继续变化。
        float rain = _presentedRainIntensity;
        float wind = _presentedWindStrength;
        UpdateWeatherLoop(AudioEventIds.WeatherRainLoop, 0.9f * rain,
            ref rainAudioHandle, ref rainAudioVolume, followPresentation: true);
        // 弱风保持安静，风力超过一半才逐渐出声，满风音量降低一半。
        float audibleWind = Mathf.InverseLerp(WindAudioStartStrength, 1f, wind);
        UpdateWeatherLoop(AudioEventIds.WeatherWindLoop, WindAudioMaximumVolume * Mathf.Pow(audibleWind, 1.5f),
            ref windAudioHandle, ref windAudioVolume);
        PlayPendingWeatherThunder();
    }

    private static void UpdateWeatherLoop(string cueId, float targetVolume,
        ref AudioHandle handle, ref float currentVolume, bool followPresentation = false)
    {
        // 雨声直接跟随已平滑的雨量，避免小雨刚开始就被开方放大。
        currentVolume = followPresentation ? targetVolume :
            Mathf.MoveTowards(currentVolume, targetVolume, Time.unscaledDeltaTime * 0.75f);
        if (currentVolume <= WeatherAudioThreshold && targetVolume <= WeatherAudioThreshold)
        {
            handle.Stop(0.8f);
            handle = AudioHandle.Invalid;
            currentVolume = 0f;
            return;
        }

        if (!handle.IsPlaying)
        {
            AudioPlayOptions options = AudioPlayOptions.Global(currentVolume);
            options.FadeIn = followPresentation ? 0f : 0.8f;
            options.OverrideLoop = true;
            options.Loop = true;
            handle = AudioService.Instance.Play(cueId, options);
        }

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

        Vector2 logicalPlayer = WorldLocalPresentation.ToLogical((Vector2)player.transform.position);
        float distance = WorldTopologyRuntime.ShortestDelta(logicalPlayer, lightning.Position).magnitude;
        if (distance >= ThunderAudibleRadius || lightning.Strength <= 0f)
            return;

        pendingWeatherThunder.Add(new PendingWeatherThunder(lightning,
            Time.unscaledTime + distance / ThunderPropagationSpeed));
    }

    private void PlayPendingWeatherThunder()
    {
        for (int i = pendingWeatherThunder.Count - 1; i >= 0; i--)
        {
            PendingWeatherThunder pending = pendingWeatherThunder[i];
            if (Time.unscaledTime < pending.PlayTime)
                continue;
            pendingWeatherThunder.RemoveAt(i);
            if (!string.Equals(pending.Lightning.WorldKey, regionalPlanet?.Name, StringComparison.Ordinal) ||
                !TryGetWeatherAudioPlayer(out Player player))
                continue;

            // 雷声和雷光共用事件落点，距离选音色，空间声源负责方位与衰减。
            Vector2 playerPosition = player.transform.position;
            Vector2 logicalPlayer = WorldLocalPresentation.ToLogical(playerPosition);
            float distance = WorldTopologyRuntime.ShortestDelta(logicalPlayer, pending.Lightning.Position).magnitude;
            float strength = Mathf.Clamp01(pending.Lightning.Strength);
            if (distance >= ThunderAudibleRadius || strength <= WeatherAudioThreshold)
                continue;
            string cueId = distance <= ThunderNearRadius
                ? AudioEventIds.WeatherThunder : AudioEventIds.WeatherThunderFar;
            AudioHandle handle = AudioService.Instance.Play(cueId,
                AudioPlayOptions.At(WorldLocalPresentation.ProjectPosition(pending.Lightning.Position, playerPosition), strength));
            if (handle.IsValid)
                weatherThunderHandles.Add(new ActiveWeatherThunder(handle, pending.Lightning.Position));
        }
    }

    private void LateUpdate()
    {
        bool hasPlayer = TryGetWeatherAudioPlayer(out Player player);
        for (int i = weatherThunderHandles.Count - 1; i >= 0; i--)
        {
            ActiveWeatherThunder thunder = weatherThunderHandles[i];
            if (!thunder.Handle.IsPlaying)
            {
                weatherThunderHandles.RemoveAt(i);
                continue;
            }
            if (!hasPlayer)
                continue;

            // 玩家移动或跨过环绕边界后，落雷仍保持在最近的正确方位。
            Vector2 playerPosition = player.transform.position;
            AudioService.Instance.SetWorldPosition(thunder.Handle,
                WorldLocalPresentation.ProjectPosition(thunder.StrikePosition, playerPosition));
        }
    }

    #endregion
}
