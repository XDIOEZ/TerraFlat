using System;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

public readonly struct WeatherStateSnapshot
{
    public readonly string PlanetName;
    public readonly WeatherType Weather;
    public readonly WeatherPhase Phase;
    public readonly float Intensity;
    public readonly float WindStrength;
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
        WindStrength = planetData != null ? Mathf.Clamp01(planetData.WindStrength) : 0f;
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

    [SerializeField, Tooltip("非零时覆盖世界种子，便于固定结果测试。")]
    private int _deterministicSeedOverride;

    public static event Action<WeatherStateSnapshot> AuthoritativeWeatherStateChanged;

    private DayTimeSystem _subscribedTimeSystem;
    private WeatherType _lastFeedbackWeather = (WeatherType)(-1);
    private WeatherPhase _lastFeedbackPhase = (WeatherPhase)(-1);
    private float _lastFeedbackIntensity = -1f;
    private float _lastFeedbackWindStrength = -1f;

#endregion

#region 查询

    public WeatherPhase GetCurrentWeatherPhase()
    {
        return TryGetLocalWeather(out var sample) ? DescribeWeatherPhase(sample) : WeatherPhase.Clear;
    }

    public float GetCurrentWeatherRemainingTime()
    {
        return TryGetLocalWeather(out var sample) ? sample.RemainingSeconds : 0f;
    }

    public static float CalculateWeatherTemperatureOffset(PlanetData planetData)
    {
        return ExistingInstance != null && ReferenceEquals(ExistingInstance.regionalPlanet, planetData)
            ? ExistingInstance.GetWeatherTemperatureOffset() : 0f;
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
        ActivateWeatherAudio();
        PlanetData planetData = GetActivePlanetData();
        if (planetData == null)
            return;

        PrepareRegionalContext();
        lightningEffect?.Initialize(this);
        AdvanceRegionalWeather();
        NormalizeData(planetData);
    }

    private void MaintainWeatherEventSystem()
    {
        SubscribeTimeSystem();
        AdvanceRegionalWeather();
        MaintainWindPushField();
        MaintainSurfaceWork();
        UpdateLocalWeatherProjection();
        MaintainWeatherPresentation(Time.deltaTime);
        RefreshWeatherFeedbackIfChanged();
        MaintainWeatherAudio();
    }

    private void ShutdownWeatherEventSystem()
    {
        UnsubscribeTimeSystem();
        FlushSurfaceWork();
        PersistRegionalStates();
        lightningEffect?.Shutdown();
        LightningStrikeSystem.Reset();
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

        AdvanceRegionalWeather();
    }

    private bool IsRelevantTimeSource(string sceneName)
    {
        DayTimeSystem timeSystem = DayTimeSystem.Instance;
        string activeSceneName = timeSystem != null
            ? timeSystem.GetCurrentActiveSceneName() : SceneManager.GetActiveScene().name;
        if (string.Equals(sceneName, activeSceneName, StringComparison.Ordinal))
            return true;

        return timeSystem != null &&
               timeSystem.TryGetResolvedTimeData(activeSceneName, out string resolvedSceneName, out _) &&
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
        DayTimeSystem timeSystem = DayTimeSystem.Instance;
        return timeSystem != null &&
               timeSystem.TryGetResolvedTimeData(timeSystem.GetCurrentActiveSceneName(), out _, out timeData) &&
               timeData != null;
    }

#endregion

#region 手动与网络状态应用

    private void SetAuthoritativeWeather(WeatherType weatherType, float intensity)
    {
        if (surfaceWork.IsAdvanceBlocked)
        {
            Debug.LogWarning("[WeatherMgr] 正在冻结存档天气，修改天气请稍后重试。");
            return;
        }
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

        ForceRegionalWeather(weatherType, intensity, Mathf.Max(30f, GetCurrentDayLength() * 0.15f));
    }

    public void ApplyReplicatedWeatherState(
        string planetName,
        WeatherType weather,
        WeatherPhase phase,
        float intensity,
        float windStrength,
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
        planetData.WindStrength = Mathf.Clamp01(windStrength);
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
            WeatherType.Cloudy => WeatherPhase.Cloudy,
            WeatherType.Rain when intensity >= 0.85f => WeatherPhase.RainHeavy,
            WeatherType.Rain when intensity >= 0.5f => WeatherPhase.RainSteady,
            WeatherType.Rain => WeatherPhase.RainStarting,
            WeatherType.Storm => WeatherPhase.RainHeavy,
            WeatherType.Fog => WeatherPhase.Fog,
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
        float windStrength = GetCurrentWindStrength();
        if (weather == _lastFeedbackWeather &&
            phase == _lastFeedbackPhase &&
            Mathf.Approximately(intensity, _lastFeedbackIntensity) &&
            Mathf.Approximately(windStrength, _lastFeedbackWindStrength))
        {
            return;
        }

        RefreshWeatherFeedback();
    }

    private void RefreshWeatherFeedback()
    {
        RefreshRainEffect();
        RefreshWindFeedback();
        _lastFeedbackWeather = GetCurrentWeather();
        _lastFeedbackPhase = GetCurrentWeatherPhase();
        _lastFeedbackIntensity = GetCurrentWeatherIntensity();
        _lastFeedbackWindStrength = GetCurrentWindStrength();
    }

    private void DeactivateWeatherFeedback()
    {
        ResetWeatherPresentation();
        DeactivateWindFeedback();
        if (snowEffect != null) snowEffect.SetActive(false);
        SetSnowingState(false);

        if (_rainEffectInstance != null)
            _rainEffectInstance.SetActive(false);

        if (_rainGroundSplashInstance != null)
            _rainGroundSplashInstance.SetActive(false);

        DeactivateWeatherAudio();
        _lastFeedbackWeather = (WeatherType)(-1);
        _lastFeedbackPhase = (WeatherPhase)(-1);
        _lastFeedbackIntensity = -1f;
        _lastFeedbackWindStrength = -1f;
    }

#endregion
}
