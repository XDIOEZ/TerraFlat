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
        if (!ForceRegionalWeather(weatherType, intensity, Mathf.Max(0.1f, endTotalTime - currentTotalTime)))
            return false;
        _gameEventWeatherOwnerId = ownerEventId;
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

        if (!ForceRegionalWeather(WeatherType.Clear, 0f, 1f))
            return false;
        _gameEventWeatherOwnerId = null;
        return true;
    }
}
