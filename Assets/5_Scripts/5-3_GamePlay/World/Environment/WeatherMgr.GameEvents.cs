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

        ForceRegionalWeather(weatherType, intensity, Mathf.Max(0.1f, endTotalTime - currentTotalTime));
        if (newOwner)
            planetData.WeatherEventSequence = Mathf.Max(1, planetData.WeatherEventSequence + 1);

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
