using System;
using FlatWorld.Networking;
using UnityEngine;

public partial class WeatherMgr
{
    private string _gameEventWeatherOwnerId;

    public bool ApplyGameEventWeather(
        string ownerEventId,
        WeatherType weatherType,
        float intensity,
        float endTotalTime)
    {
        if (!GameNetwork.HasStateAuthority ||
            string.IsNullOrWhiteSpace(ownerEventId) ||
            weatherType == WeatherType.Clear)
        {
            return false;
        }

        PlanetData planetData = GetActivePlanetData();
        if (planetData == null)
            return false;

        float currentTotalTime = GetCurrentTotalTime();
        bool newOwner = !string.Equals(
            _gameEventWeatherOwnerId,
            ownerEventId,
            StringComparison.Ordinal);
        _gameEventWeatherOwnerId = ownerEventId;

        planetData.WeatherDataVersion = WeatherEventScheduler.CurrentDataVersion;
        planetData.CurrentWeather = weatherType;
        planetData.WeatherPhase = ResolveForcedPhase(weatherType, intensity);
        planetData.WeatherIntensity = Mathf.Clamp01(intensity);
        planetData.WeatherPhaseStartedTotalTime = currentTotalTime;
        planetData.WeatherPhaseEndTotalTime = Mathf.Max(currentTotalTime + 0.1f, endTotalTime);
        planetData.NextWeatherEventTotalTime = 0f;
        if (newOwner)
            planetData.WeatherEventSequence = Mathf.Max(1, planetData.WeatherEventSequence + 1);

        NormalizeData(planetData);
        PublishAuthoritativeWeatherState();
        return true;
    }

    public bool ClearGameEventWeather(string ownerEventId)
    {
        if (!GameNetwork.HasStateAuthority ||
            string.IsNullOrWhiteSpace(ownerEventId) ||
            !string.Equals(_gameEventWeatherOwnerId, ownerEventId, StringComparison.Ordinal))
        {
            return false;
        }

        _gameEventWeatherOwnerId = null;
        SetAuthoritativeWeather(WeatherType.Clear, 0f);
        return true;
    }
}
