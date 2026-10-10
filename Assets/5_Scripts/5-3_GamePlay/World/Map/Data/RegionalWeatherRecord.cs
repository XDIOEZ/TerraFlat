using System;
using FlatWorld.WorldModel;
using MemoryPack;

[MemoryPackable]
[Serializable]
public partial class RegionalWeatherRecord
{
    #region 区域天气存档字段

    public int RegionX;
    public int RegionY;
    public double LastStepTime;
    public long CheckIndex;
    public bool HasValidInput;
    public bool InputIsComplete;
    public float AirHumiditySupply;
    public float TemperatureBeforeRainCooling;
    public float GeographicPrecipitation;
    public float GeographicWindX;
    public float GeographicWindY;
    public float TemperatureGradientX;
    public float TemperatureGradientY;
    public double PrecipitationStartedTime;
    public double PrecipitationEndTime;
    public double CooldownEndTime;
    public float InitialPrecipitationIntensity;
    public float PrecipitationIntensity;
    public bool ForcedSnow;
    public float HumidityDeficit;
    public float CoolingAmountCelsius;
    public float WindX;
    public float WindY;
    public float CloudCoverage;
    public float FogDensity;
    public float LightningActivity;
    public double NextLightningTime;
    public double LastLightningTime;
    public int LightningSequence;
    public int Revision;
    public float ForcedCloudCoverage = -1f;
    public float ForcedFogDensity = -1f;
    public float ForcedWindStrength = -1f;
    public double ForcedElementsEndTime;

    #endregion

    #region 纯天气状态转换

    // 存档只保存数值，纯天气引擎不依赖序列化框架。
    public static RegionalWeatherRecord FromState(RegionalWeatherState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        RegionalWeatherInput input = state.LastValidInput;
        return new RegionalWeatherRecord
        {
            RegionX = state.Region.X,
            RegionY = state.Region.Y,
            LastStepTime = state.LastStepTime,
            CheckIndex = state.CheckIndex,
            HasValidInput = state.HasValidInput,
            InputIsComplete = input.IsComplete,
            AirHumiditySupply = input.AirHumiditySupply,
            TemperatureBeforeRainCooling = input.TemperatureBeforeRainCooling,
            GeographicPrecipitation = input.GeographicPrecipitation,
            GeographicWindX = input.GeographicWindX,
            GeographicWindY = input.GeographicWindY,
            TemperatureGradientX = input.TemperatureGradientX,
            TemperatureGradientY = input.TemperatureGradientY,
            PrecipitationStartedTime = state.PrecipitationStartedTime,
            PrecipitationEndTime = state.PrecipitationEndTime,
            CooldownEndTime = state.CooldownEndTime,
            InitialPrecipitationIntensity = state.InitialPrecipitationIntensity,
            PrecipitationIntensity = state.PrecipitationIntensity,
            ForcedSnow = state.ForcedSnow,
            HumidityDeficit = state.HumidityDeficit,
            CoolingAmountCelsius = state.CoolingAmountCelsius,
            WindX = state.WindX,
            WindY = state.WindY,
            CloudCoverage = state.CloudCoverage,
            FogDensity = state.FogDensity,
            LightningActivity = state.LightningActivity,
            NextLightningTime = state.NextLightningTime,
            LastLightningTime = state.LastLightningTime,
            LightningSequence = state.LightningSequence,
            Revision = state.Revision,
            ForcedCloudCoverage = state.ForcedCloudCoverage,
            ForcedFogDensity = state.ForcedFogDensity,
            ForcedWindStrength = state.ForcedWindStrength,
            ForcedElementsEndTime = state.ForcedElementsEndTime
        };
    }

    public RegionalWeatherState ToState()
    {
        return new RegionalWeatherState
        {
            Region = new Int2(RegionX, RegionY),
            LastStepTime = LastStepTime,
            CheckIndex = CheckIndex,
            HasValidInput = HasValidInput,
            LastValidInput = new RegionalWeatherInput
            {
                IsComplete = InputIsComplete,
                AirHumiditySupply = AirHumiditySupply,
                TemperatureBeforeRainCooling = TemperatureBeforeRainCooling,
                GeographicPrecipitation = GeographicPrecipitation,
                GeographicWindX = GeographicWindX,
                GeographicWindY = GeographicWindY,
                TemperatureGradientX = TemperatureGradientX,
                TemperatureGradientY = TemperatureGradientY
            },
            PrecipitationStartedTime = PrecipitationStartedTime,
            PrecipitationEndTime = PrecipitationEndTime,
            CooldownEndTime = CooldownEndTime,
            InitialPrecipitationIntensity = InitialPrecipitationIntensity,
            PrecipitationIntensity = PrecipitationIntensity,
            ForcedSnow = ForcedSnow,
            HumidityDeficit = HumidityDeficit,
            CoolingAmountCelsius = CoolingAmountCelsius,
            WindX = WindX,
            WindY = WindY,
            CloudCoverage = CloudCoverage,
            FogDensity = FogDensity,
            LightningActivity = LightningActivity,
            NextLightningTime = NextLightningTime,
            LastLightningTime = LastLightningTime,
            LightningSequence = LightningSequence,
            Revision = Revision,
            ForcedCloudCoverage = ForcedCloudCoverage,
            ForcedFogDensity = ForcedFogDensity,
            ForcedWindStrength = ForcedWindStrength,
            ForcedElementsEndTime = ForcedElementsEndTime
        };
    }

    #endregion
}
