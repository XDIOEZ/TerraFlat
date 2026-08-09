using System;
using FlatWorld.Audio;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

public readonly struct WeatherStateSnapshot
{
    public readonly string PlanetName;
    public readonly WeatherType Weather;
    public readonly WeatherPhase Phase;
    public readonly float Intensity;
    public readonly float PhaseStartedTotalTime;
    public readonly float PhaseEndTotalTime;
    public readonly float NextWeatherEventTotalTime;
    public readonly int RandomCursor;
    public readonly int EventSequence;
    public readonly int DataVersion;

    public WeatherStateSnapshot(string planetName, PlanetData planetData)
    {
        PlanetName = planetName ?? string.Empty;
        Weather = planetData != null ? planetData.CurrentWeather : WeatherType.Clear;
        Phase = planetData != null ? planetData.WeatherPhase : WeatherPhase.Clear;
        Intensity = planetData != null ? Mathf.Clamp01(planetData.WeatherIntensity) : 0f;
        PhaseStartedTotalTime = planetData?.WeatherPhaseStartedTotalTime ?? 0f;
        PhaseEndTotalTime = planetData?.WeatherPhaseEndTotalTime ?? 0f;
        NextWeatherEventTotalTime = planetData?.NextWeatherEventTotalTime ?? 0f;
        RandomCursor = planetData?.WeatherRandomCursor ?? 0;
        EventSequence = planetData?.WeatherEventSequence ?? 0;
        DataVersion = planetData?.WeatherDataVersion ?? 0;
    }
}

public partial class WeatherMgr
{
#region 权威天气配置

    [Header("权威降雨事件")]
    [SerializeField] private RainEventScheduleConfig _rainEventConfig = new RainEventScheduleConfig();
    [SerializeField, Tooltip("非零时覆盖世界种子，便于固定结果测试。")]
    private int _deterministicSeedOverride;

    public static event Action<WeatherStateSnapshot> AuthoritativeWeatherStateChanged;

    private DayTimeSystem _subscribedTimeSystem;
    private AudioHandle _rainAudioHandle;
    private WeatherType _lastFeedbackWeather = (WeatherType)(-1);
    private WeatherPhase _lastFeedbackPhase = (WeatherPhase)(-1);
    private float _lastFeedbackIntensity = -1f;

#endregion

#region 查询

    public WeatherPhase GetCurrentWeatherPhase()
    {
        PlanetData planetData = GetActivePlanetData();
        return planetData != null ? planetData.WeatherPhase : WeatherPhase.Clear;
    }

    public float GetCurrentWeatherRemainingTime()
    {
        PlanetData planetData = GetActivePlanetData();
        return planetData != null
            ? WeatherEventScheduler.GetRemainingTime(planetData, GetCurrentTotalTime())
            : 0f;
    }

    public static float CalculateWeatherTemperatureOffset(PlanetData planetData)
    {
        if (planetData == null)
            return DefaultWeatherTemperatureOffset;

        float intensity = Mathf.Clamp01(planetData.WeatherIntensity);
        return planetData.CurrentWeather switch
        {
            WeatherType.Cloudy => planetData.CloudyTemperatureOffset * intensity,
            WeatherType.Rain => planetData.RainTemperatureOffset * intensity,
            WeatherType.Storm => planetData.StormTemperatureOffset * intensity,
            _ => DefaultWeatherTemperatureOffset
        };
    }

    public WeatherStateSnapshot CaptureWeatherState()
    {
        PlanetData planetData = GetActivePlanetData();
        return new WeatherStateSnapshot(planetData?.Name, planetData);
    }

#endregion

#region 权威调度

    private void ActivateWeatherEventSystem()
    {
        SubscribeTimeSystem();
        PlanetData planetData = GetActivePlanetData();
        if (planetData == null)
            return;

        if (GameNetwork.HasStateAuthority)
        {
            WeatherEventScheduler.InitializeIfNeeded(
                planetData,
                GetCurrentTotalTime(),
                GetCurrentDayLength(),
                GetDeterministicSeed(),
                _rainEventConfig);
            PublishAuthoritativeWeatherState();
        }
        else
        {
            NormalizeData(planetData);
        }
    }

    private void MaintainWeatherEventSystem()
    {
        SubscribeTimeSystem();
        RefreshWeatherFeedbackIfChanged();
    }

    private void ShutdownWeatherEventSystem()
    {
        UnsubscribeTimeSystem();
        DeactivateWeatherFeedback();
    }

    private void SubscribeTimeSystem()
    {
        DayTimeSystem timeSystem = DayTimeSystem.Instance;
        if (ReferenceEquals(_subscribedTimeSystem, timeSystem))
            return;

        UnsubscribeTimeSystem();
        _subscribedTimeSystem = timeSystem;
        if (_subscribedTimeSystem != null)
            _subscribedTimeSystem.TimeAdvanced += HandleTimeAdvanced;
    }

    private void UnsubscribeTimeSystem()
    {
        if (_subscribedTimeSystem != null)
            _subscribedTimeSystem.TimeAdvanced -= HandleTimeAdvanced;
        _subscribedTimeSystem = null;
    }

    private void HandleTimeAdvanced(string sceneName, float oldTotalTime, float newTotalTime)
    {
        if (!GameNetwork.HasStateAuthority || !IsRelevantTimeSource(sceneName))
            return;

        PlanetData planetData = GetActivePlanetData();
        if (planetData == null)
            return;

        int transitions = WeatherEventScheduler.Advance(
            planetData,
            oldTotalTime,
            newTotalTime,
            GetCurrentDayLength(),
            GetDeterministicSeed(),
            _rainEventConfig);
        if (transitions <= 0)
            return;

        if (EnableDebugLog)
        {
            Debug.Log(
                $"[WeatherMgr] 天气推进完成，跨越阶段={transitions}，当前阶段={planetData.WeatherPhase}，" +
                $"天气={planetData.CurrentWeather}，强度={planetData.WeatherIntensity:F2}，事件序号={planetData.WeatherEventSequence}");
        }

        PublishAuthoritativeWeatherState();
    }

    private bool IsRelevantTimeSource(string sceneName)
    {
        string activeSceneName = SceneManager.GetActiveScene().name;
        if (string.Equals(sceneName, activeSceneName, StringComparison.Ordinal))
            return true;

        return DayTimeSystem.Instance != null &&
               DayTimeSystem.Instance.TryGetResolvedTimeData(activeSceneName, out string resolvedSceneName, out _) &&
               string.Equals(sceneName, resolvedSceneName, StringComparison.Ordinal);
    }

    private int GetDeterministicSeed()
    {
        if (_deterministicSeedOverride != 0)
            return _deterministicSeedOverride;

        int seed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        return seed == 0 ? 1 : seed;
    }

    private float GetCurrentTotalTime()
    {
        return TryGetCurrentTimeData(out TimeData timeData)
            ? timeData.GetTotalGameTime()
            : 0f;
    }

    private float GetCurrentDayLength()
    {
        return TryGetCurrentTimeData(out TimeData timeData)
            ? Mathf.Max(1f, timeData.DayLength)
            : 1440f;
    }

    private static bool TryGetCurrentTimeData(out TimeData timeData)
    {
        timeData = null;
        string activeSceneName = SceneManager.GetActiveScene().name;
        return DayTimeSystem.Instance != null &&
               DayTimeSystem.Instance.TryGetResolvedTimeData(activeSceneName, out _, out timeData) &&
               timeData != null;
    }

#endregion

#region 手动与网络状态应用

    private void SetAuthoritativeWeather(WeatherType weatherType, float intensity)
    {
        if (!GameNetwork.HasStateAuthority)
        {
            if (EnableDebugLog)
                Debug.LogWarning("[WeatherMgr] 普通客户端不能修改权威天气状态。");
            return;
        }

        PlanetData planetData = GetActivePlanetData();
        if (planetData == null)
        {
            if (EnableDebugLog)
                Debug.LogWarning($"[WeatherMgr] 设置天气失败，未找到当前星球数据，目标天气={weatherType}, 强度={intensity:F2}");
            return;
        }

        float normalizedIntensity = Mathf.Clamp01(intensity);
        WeatherPhase phase = ResolveForcedPhase(weatherType, normalizedIntensity);
        WeatherEventScheduler.ForcePhase(
            planetData,
            phase,
            GetCurrentTotalTime(),
            GetCurrentDayLength(),
            GetDeterministicSeed(),
            _rainEventConfig);
        planetData.CurrentWeather = weatherType;
        planetData.WeatherIntensity = weatherType == WeatherType.Clear ? 0f : normalizedIntensity;

        if (EnableDebugLog)
        {
            Debug.Log(
                $"[WeatherMgr] 设置天气成功，阶段={phase}，天气={weatherType}，强度={planetData.WeatherIntensity:F2}，" +
                $"天气修正={GetWeatherTemperatureOffset():F2}℃，有效环境温度={planetData.GlobalTemperature + GetWeatherTemperatureOffset():F2}℃");
        }

        PublishAuthoritativeWeatherState();
    }

    public void ApplyReplicatedWeatherState(
        string planetName,
        WeatherType weather,
        WeatherPhase phase,
        float intensity,
        float phaseStartedTotalTime,
        float phaseEndTotalTime,
        float nextWeatherEventTotalTime,
        int randomCursor,
        int eventSequence,
        int dataVersion)
    {
        if (GameNetwork.HasStateAuthority)
            return;

        PlanetData planetData = GetActivePlanetData();
        if (planetData == null ||
            (!string.IsNullOrWhiteSpace(planetName) &&
             !string.Equals(planetData.Name, planetName, StringComparison.Ordinal)))
        {
            return;
        }

        planetData.CurrentWeather = weather;
        planetData.WeatherPhase = phase;
        planetData.WeatherIntensity = Mathf.Clamp01(intensity);
        planetData.WeatherPhaseStartedTotalTime = phaseStartedTotalTime;
        planetData.WeatherPhaseEndTotalTime = phaseEndTotalTime;
        planetData.NextWeatherEventTotalTime = nextWeatherEventTotalTime;
        planetData.WeatherRandomCursor = Mathf.Max(0, randomCursor);
        planetData.WeatherEventSequence = Mathf.Max(0, eventSequence);
        planetData.WeatherDataVersion = Mathf.Max(WeatherEventScheduler.CurrentDataVersion, dataVersion);
        NormalizeData(planetData);
        RefreshWeatherFeedback();
    }

    private static WeatherPhase ResolveForcedPhase(WeatherType weatherType, float intensity)
    {
        return weatherType switch
        {
            WeatherType.Cloudy => WeatherPhase.Forecast,
            WeatherType.Rain when intensity >= 0.85f => WeatherPhase.RainHeavy,
            WeatherType.Rain when intensity >= 0.5f => WeatherPhase.RainSteady,
            WeatherType.Rain => WeatherPhase.RainStarting,
            WeatherType.Storm => WeatherPhase.RainHeavy,
            _ => WeatherPhase.Clear
        };
    }

    private void PublishAuthoritativeWeatherState()
    {
        RefreshWeatherFeedback();
        AuthoritativeWeatherStateChanged?.Invoke(CaptureWeatherState());
    }

#endregion

#region 视听反馈

    private void RefreshWeatherFeedbackIfChanged()
    {
        WeatherType weather = GetCurrentWeather();
        WeatherPhase phase = GetCurrentWeatherPhase();
        float intensity = GetCurrentWeatherIntensity();
        if (weather == _lastFeedbackWeather &&
            phase == _lastFeedbackPhase &&
            Mathf.Approximately(intensity, _lastFeedbackIntensity))
        {
            return;
        }

        RefreshWeatherFeedback();
    }

    private void RefreshWeatherFeedback()
    {
        RefreshRainEffect();
        RefreshRainAudio();
        _lastFeedbackWeather = GetCurrentWeather();
        _lastFeedbackPhase = GetCurrentWeatherPhase();
        _lastFeedbackIntensity = GetCurrentWeatherIntensity();
    }

    private void RefreshRainAudio()
    {
        if (!IsRaining())
        {
            if (_rainAudioHandle.IsPlaying)
                _rainAudioHandle.Stop(0.8f);
            _rainAudioHandle = AudioHandle.Invalid;
            return;
        }

        if (_rainAudioHandle.IsPlaying)
            return;

        AudioPlayOptions options = AudioPlayOptions.Global(Mathf.Lerp(0.45f, 0.8f, GetCurrentWeatherIntensity()));
        options.FadeIn = 0.8f;
        options.OverrideLoop = true;
        options.Loop = true;
        _rainAudioHandle = AudioService.Instance.Play(AudioEventIds.WeatherRainLoop, options);
    }

    private void DeactivateWeatherFeedback()
    {
        if (_rainEffectInstance != null)
            _rainEffectInstance.SetActive(false);

        SetRainGroundSplashActive(false, 0f);

        if (_rainAudioHandle.IsPlaying)
            _rainAudioHandle.Stop(0.5f);
        _rainAudioHandle = AudioHandle.Invalid;
        _lastFeedbackWeather = (WeatherType)(-1);
        _lastFeedbackPhase = (WeatherPhase)(-1);
        _lastFeedbackIntensity = -1f;
    }

#endregion
}
