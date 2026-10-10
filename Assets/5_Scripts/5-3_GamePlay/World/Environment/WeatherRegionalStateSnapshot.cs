using FlatWorld.WorldModel;

public readonly struct RegionalWeatherStateSnapshot
{
    #region 区域天气复制字段

    public readonly string WorldKey;
    public readonly Int2 Region;
    public readonly double SampleTime;
    public readonly int Revision;
    public readonly float AirHumidity;
    public readonly float HumidityDeficit;
    public readonly float CoolingOffsetCelsius;
    public readonly float RainIntensity;
    public readonly float SnowIntensity;
    public readonly float PrecipitationIntensity;
    public readonly float WindX;
    public readonly float WindY;
    public readonly float WindStrength;
    public readonly float CloudCoverage;
    public readonly float FogDensity;
    public readonly float LightningActivity;
    public readonly int LightningSequence;
    public readonly float RemainingSeconds;

    #endregion

    #region 状态快照构造

    // 复制快照只携带当前效果和反馈，不把客户端变成第二份模拟者。
    public RegionalWeatherStateSnapshot(string worldKey, RegionalWeatherSnapshot snapshot)
        : this(worldKey, snapshot.Region, snapshot.SampleTime, snapshot.Revision,
            snapshot.AirHumidity, snapshot.HumidityDeficit, snapshot.CoolingOffsetCelsius,
            snapshot.RainIntensity, snapshot.SnowIntensity, snapshot.PrecipitationIntensity,
            snapshot.WindX, snapshot.WindY, snapshot.WindStrength, snapshot.CloudCoverage,
            snapshot.FogDensity, snapshot.LightningActivity, snapshot.LightningSequence,
            snapshot.RemainingSeconds)
    {
    }

    public RegionalWeatherStateSnapshot(
        string worldKey,
        Int2 region,
        double sampleTime,
        int revision,
        float airHumidity,
        float humidityDeficit,
        float coolingOffsetCelsius,
        float rainIntensity,
        float snowIntensity,
        float precipitationIntensity,
        float windX,
        float windY,
        float windStrength,
        float cloudCoverage,
        float fogDensity,
        float lightningActivity,
        int lightningSequence,
        float remainingSeconds)
    {
        WorldKey = worldKey ?? string.Empty;
        Region = region;
        SampleTime = sampleTime;
        Revision = revision;
        AirHumidity = airHumidity;
        HumidityDeficit = humidityDeficit;
        CoolingOffsetCelsius = coolingOffsetCelsius;
        RainIntensity = rainIntensity;
        SnowIntensity = snowIntensity;
        PrecipitationIntensity = precipitationIntensity;
        WindX = windX;
        WindY = windY;
        WindStrength = windStrength;
        CloudCoverage = cloudCoverage;
        FogDensity = fogDensity;
        LightningActivity = lightningActivity;
        LightningSequence = lightningSequence;
        RemainingSeconds = remainingSeconds;
    }

    public RegionalWeatherSnapshot ToSnapshot()
    {
        return new RegionalWeatherSnapshot
        {
            Region = Region,
            SampleTime = SampleTime,
            Revision = Revision,
            AirHumidity = AirHumidity,
            HumidityDeficit = HumidityDeficit,
            CoolingOffsetCelsius = CoolingOffsetCelsius,
            RainIntensity = RainIntensity,
            SnowIntensity = SnowIntensity,
            PrecipitationIntensity = PrecipitationIntensity,
            WindX = WindX,
            WindY = WindY,
            WindStrength = WindStrength,
            CloudCoverage = CloudCoverage,
            FogDensity = FogDensity,
            LightningActivity = LightningActivity,
            LightningSequence = LightningSequence,
            RemainingSeconds = RemainingSeconds
        };
    }

    #endregion
}
