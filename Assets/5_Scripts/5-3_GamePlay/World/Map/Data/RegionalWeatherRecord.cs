using System;
using FlatWorld.WorldModel;
using MemoryPack;

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class RegionalWeatherRecord
{
    #region 区块天气存档字段

    [MemoryPack.MemoryPackOrder(15)] public int RegionX;
    [MemoryPack.MemoryPackOrder(16)] public int RegionY;
    [MemoryPack.MemoryPackOrder(8)] public double LastStepTime;
    [MemoryPack.MemoryPackOrder(0)] public long CheckIndex;
    [MemoryPack.MemoryPackOrder(14)] public ChunkWeatherProfileRecord Profile;
    [MemoryPack.MemoryPackOrder(1)] public WeatherElementStateRecord[] Elements = Array.Empty<WeatherElementStateRecord>();
    [MemoryPack.MemoryPackOrder(5)] public bool ForcedSnow;
    [MemoryPack.MemoryPackOrder(13)] public double NextLightningTime;
    [MemoryPack.MemoryPackOrder(7)] public double LastLightningTime = -1d;
    [MemoryPack.MemoryPackOrder(11)] public int LightningSequence;
    [MemoryPack.MemoryPackOrder(9)] public int LightningCellX;
    [MemoryPack.MemoryPackOrder(10)] public int LightningCellY;
    [MemoryPack.MemoryPackOrder(12)] public float LightningStrength;
    [MemoryPack.MemoryPackOrder(17)] public int Revision;
    [MemoryPack.MemoryPackOrder(2)] public float ForcedCloudCoverage = -1f;
    [MemoryPack.MemoryPackOrder(4)] public float ForcedFogDensity = -1f;
    [MemoryPack.MemoryPackOrder(6)] public float ForcedWindStrength = -1f;
    [MemoryPack.MemoryPackOrder(3)] public double ForcedElementsEndTime;

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

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class ChunkWeatherProfileRecord
{
    #region 生成档案字段与转换

    [MemoryPack.MemoryPackOrder(0)] public float AirHumidity;
    [MemoryPack.MemoryPackOrder(5)] public float WindX;
    [MemoryPack.MemoryPackOrder(6)] public float WindY;
    [MemoryPack.MemoryPackOrder(4)] public float WindStrength;
    [MemoryPack.MemoryPackOrder(3)] public float TemperatureCelsius;
    [MemoryPack.MemoryPackOrder(2)] public float Precipitation;
    [MemoryPack.MemoryPackOrder(1)] public WeatherElementProfileRecord[] Elements = Array.Empty<WeatherElementProfileRecord>();

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

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class WeatherElementProfileRecord
{
    #region 天气要素配置与转换

    [MemoryPack.MemoryPackOrder(1)] public string Id;
    [MemoryPack.MemoryPackOrder(3)] public float Probability;
    [MemoryPack.MemoryPackOrder(2)] public float Intensity;
    [MemoryPack.MemoryPackOrder(0)] public float DurationSeconds;

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

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class WeatherElementStateRecord
{
    #region 天气要素运行态与转换

    [MemoryPack.MemoryPackOrder(1)] public string Id;
    [MemoryPack.MemoryPackOrder(5)] public double StartedTime;
    [MemoryPack.MemoryPackOrder(0)] public double EndTime;
    [MemoryPack.MemoryPackOrder(2)] public float Intensity;
    [MemoryPack.MemoryPackOrder(3)] public double NaturalRecoveryEndTime;
    [MemoryPack.MemoryPackOrder(4)] public float PendingProbabilityBonus;

    public static WeatherElementStateRecord FromState(WeatherElementState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        return new WeatherElementStateRecord
        {
            Id = state.Id,
            StartedTime = state.StartedTime,
            EndTime = state.EndTime,
            Intensity = state.Intensity,
            NaturalRecoveryEndTime = state.NaturalRecoveryEndTime,
            PendingProbabilityBonus = state.PendingProbabilityBonus
        };
    }

    public WeatherElementState ToState()
    {
        return new WeatherElementState
        {
            Id = Id,
            StartedTime = StartedTime,
            EndTime = EndTime,
            Intensity = Intensity,
            NaturalRecoveryEndTime = NaturalRecoveryEndTime,
            PendingProbabilityBonus = PendingProbabilityBonus
        };
    }

    #endregion
}
