using System;
using FlatWorld.WorldModel;
using MemoryPack;

[MemoryPackable]
[Serializable]
public partial class RegionalWeatherRecord
{
    #region 区块天气存档字段

    public int RegionX;
    public int RegionY;
    public double LastStepTime;
    public long CheckIndex;
    public ChunkWeatherProfileRecord Profile;
    public WeatherElementStateRecord[] Elements = Array.Empty<WeatherElementStateRecord>();
    public bool ForcedSnow;
    public double NextLightningTime;
    public double LastLightningTime = -1d;
    public int LightningSequence;
    public int LightningCellX;
    public int LightningCellY;
    public float LightningStrength;
    public int Revision;
    public float ForcedCloudCoverage = -1f;
    public float ForcedFogDensity = -1f;
    public float ForcedWindStrength = -1f;
    public double ForcedElementsEndTime;

    #endregion

    #region 纯天气状态转换

    // 存档复制生成档案和各要素状态，纯天气引擎不依赖序列化框架。
    public static RegionalWeatherRecord FromState(RegionalWeatherState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        WeatherElementState[] sourceElements = state.Elements ?? Array.Empty<WeatherElementState>();
        var elements = new WeatherElementStateRecord[sourceElements.Length];
        for (int i = 0; i < sourceElements.Length; i++)
            elements[i] = sourceElements[i] == null ? null : WeatherElementStateRecord.FromState(sourceElements[i]);

        return new RegionalWeatherRecord
        {
            RegionX = state.Region.X,
            RegionY = state.Region.Y,
            LastStepTime = state.LastStepTime,
            CheckIndex = state.CheckIndex,
            Profile = state.Profile == null ? null : ChunkWeatherProfileRecord.FromProfile(state.Profile),
            Elements = elements,
            ForcedSnow = state.ForcedSnow,
            NextLightningTime = state.NextLightningTime,
            LastLightningTime = state.LastLightningTime,
            LightningSequence = state.LightningSequence,
            LightningCellX = state.LightningCell.X,
            LightningCellY = state.LightningCell.Y,
            LightningStrength = state.LightningStrength,
            Revision = state.Revision,
            ForcedCloudCoverage = state.ForcedCloudCoverage,
            ForcedFogDensity = state.ForcedFogDensity,
            ForcedWindStrength = state.ForcedWindStrength,
            ForcedElementsEndTime = state.ForcedElementsEndTime
        };
    }

    public RegionalWeatherState ToState()
    {
        WeatherElementStateRecord[] sourceElements = Elements ?? Array.Empty<WeatherElementStateRecord>();
        var elements = new WeatherElementState[sourceElements.Length];
        for (int i = 0; i < sourceElements.Length; i++)
            elements[i] = sourceElements[i]?.ToState();

        return new RegionalWeatherState
        {
            Region = new Int2(RegionX, RegionY),
            LastStepTime = LastStepTime,
            CheckIndex = CheckIndex,
            Profile = Profile?.ToProfile(),
            Elements = elements,
            ForcedSnow = ForcedSnow,
            NextLightningTime = NextLightningTime,
            LastLightningTime = LastLightningTime,
            LightningSequence = LightningSequence,
            LightningCell = new Int2(LightningCellX, LightningCellY),
            LightningStrength = LightningStrength,
            Revision = Revision,
            ForcedCloudCoverage = ForcedCloudCoverage,
            ForcedFogDensity = ForcedFogDensity,
            ForcedWindStrength = ForcedWindStrength,
            ForcedElementsEndTime = ForcedElementsEndTime
        };
    }

    #endregion
}

[MemoryPackable]
[Serializable]
public partial class ChunkWeatherProfileRecord
{
    #region 生成档案字段与转换

    public float AirHumidity;
    public float WindX;
    public float WindY;
    public float WindStrength;
    public float TemperatureCelsius;
    public float Precipitation;
    public WeatherElementProfileRecord[] Elements = Array.Empty<WeatherElementProfileRecord>();

    public static ChunkWeatherProfileRecord FromProfile(ChunkWeatherProfile profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        var elements = new WeatherElementProfileRecord[profile.Elements.Count];
        for (int i = 0; i < elements.Length; i++)
            elements[i] = WeatherElementProfileRecord.FromProfile(profile.Elements[i]);

        return new ChunkWeatherProfileRecord
        {
            AirHumidity = profile.Climate.AirHumidity,
            WindX = profile.Climate.WindX,
            WindY = profile.Climate.WindY,
            WindStrength = profile.Climate.WindStrength,
            TemperatureCelsius = profile.Climate.TemperatureCelsius,
            Precipitation = profile.Climate.Precipitation,
            Elements = elements
        };
    }

    public ChunkWeatherProfile ToProfile()
    {
        WeatherElementProfileRecord[] sourceElements = Elements ?? Array.Empty<WeatherElementProfileRecord>();
        var elements = new WeatherElementProfile[sourceElements.Length];
        for (int i = 0; i < sourceElements.Length; i++)
        {
            if (sourceElements[i] == null)
                throw new InvalidOperationException("区块天气存档包含空的要素配置。");
            elements[i] = sourceElements[i].ToProfile();
        }

        return new ChunkWeatherProfile(
            new ChunkWeatherClimate(AirHumidity, WindX, WindY, WindStrength, TemperatureCelsius,
                Precipitation), elements);
    }

    #endregion
}

[MemoryPackable]
[Serializable]
public partial class WeatherElementProfileRecord
{
    #region 天气要素配置与转换

    public string Id;
    public float Probability;
    public float Intensity;
    public float DurationSeconds;

    public static WeatherElementProfileRecord FromProfile(WeatherElementProfile profile)
    {
        return new WeatherElementProfileRecord
        {
            Id = profile.Id,
            Probability = profile.Probability,
            Intensity = profile.Intensity,
            DurationSeconds = profile.DurationSeconds
        };
    }

    public WeatherElementProfile ToProfile() => new WeatherElementProfile(Id, Probability, Intensity, DurationSeconds);

    #endregion
}

[MemoryPackable]
[Serializable]
public partial class WeatherElementStateRecord
{
    #region 天气要素运行态与转换

    public string Id;
    public double StartedTime;
    public double EndTime;
    public float Intensity;

    public static WeatherElementStateRecord FromState(WeatherElementState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        return new WeatherElementStateRecord
        {
            Id = state.Id,
            StartedTime = state.StartedTime,
            EndTime = state.EndTime,
            Intensity = state.Intensity
        };
    }

    public WeatherElementState ToState()
    {
        return new WeatherElementState
        {
            Id = Id,
            StartedTime = StartedTime,
            EndTime = EndTime,
            Intensity = Intensity
        };
    }

    #endregion
}
