using System;

namespace FlatWorld.WorldModel
{
    /// <summary>区域天气的有限数值配置，全部速率使用游戏秒。</summary>
    [Serializable]
    public sealed class RegionalWeatherSettings
    {
        #region 区域与固定时钟
        public int RegionSizeTiles = 32;
        public float StepSeconds = 2f;
        public float CheckIntervalSeconds = 30f;
        public int MaximumCatchUpSteps = 120;
        #endregion

        #region 湿气、降水与温度反馈
        public float HumidityStart = 0.12f;
        public float HumidityStop = 0.06f;
        public float FullRainHumidity = 0.7f;
        public float HumidityConsumptionPerSecond = 0.003f;
        public float HumidityRecoveryPerSecond = 0.0006f;
        public float WarmTemperatureStart = 5f;
        public float WarmTemperatureFull = 30f;
        public float ColdPrecipitationFactor = 0.35f;
        public float WarmPrecipitationFactor = 1.6f;
        public float FreezingTemperature = 0f;
        public float MinimumClimateWeight = 0.35f;
        public float MaximumClimateWeight = 1.3f;
        public float BaseCheckProbability = 0.18f;
        public float MaximumCheckProbability = 0.8f;
        public float MinimumDurationSeconds = 45f;
        public float MaximumDurationSeconds = 240f;
        public float HardMaximumDurationSeconds = 420f;
        public float MinimumIntensity = 0.1f;
        public float MaximumIntensity = 0.9f;
        public float CooldownSeconds = 90f;
        public float MaximumCoolingCelsius = 7f;
        public float CoolingPerSecond = 0.04f;
        public float CoolingRecoveryPerSecond = 0.015f;
        #endregion

        #region 风、云、雾与雷电
        public float GeographicWindStrength = 0.35f;
        public float TemperatureGradientWindFactor = 0.03f;
        public float WindPerturbationStrength = 0.25f;
        public float WindPerturbationPeriodSeconds = 180f;
        public float WindChangePerSecond = 0.015f;
        public float CloudChangePerSecond = 0.015f;
        public float FogChangePerSecond = 0.01f;
        public float FogHumidityStart = 0.18f;
        public float FogHumidityFull = 0.6f;
        public float FogTemperatureStart = 15f;
        public float FogTemperatureFull = 2f;
        public float FogWindLimit = 0.45f;
        public float LightningHumidityStart = 0.2f;
        public float LightningHumidityFull = 0.65f;
        public float LightningTemperatureStart = 15f;
        public float LightningTemperatureFull = 30f;
        public float LightningChangePerSecond = 0.02f;
        public float BaseLightningIntervalSeconds = 60f;
        public float MinimumLightningIntervalSeconds = 5f;
        public float StrongWindStart = 0.4f;
        public float MaximumLightningWindBonus = 0.75f;
        #endregion

        #region 配置快照与约束
        public RegionalWeatherSettings Snapshot()
        {
            Validate();
            return (RegionalWeatherSettings)MemberwiseClone();
        }

        public void Validate()
        {
            if (RegionSizeTiles < 4 || RegionSizeTiles > 1024 ||
                !Positive(StepSeconds) || StepSeconds < 0.1f || StepSeconds > 60f ||
                !Positive(CheckIntervalSeconds) || CheckIntervalSeconds < StepSeconds ||
                MaximumCatchUpSteps < 1 || MaximumCatchUpSteps > 1024 ||
                !Unit(HumidityStart) || !Unit(HumidityStop) ||
                HumidityStop <= 0f || HumidityStop >= HumidityStart ||
                !Unit(FullRainHumidity) || FullRainHumidity <= HumidityStart ||
                !Positive(HumidityConsumptionPerSecond) || !Positive(HumidityRecoveryPerSecond) ||
                !Finite(WarmTemperatureStart) || !Finite(WarmTemperatureFull) ||
                WarmTemperatureFull <= WarmTemperatureStart || !Finite(FreezingTemperature) ||
                !Positive(ColdPrecipitationFactor) || !Positive(WarmPrecipitationFactor) ||
                WarmPrecipitationFactor < ColdPrecipitationFactor ||
                !Positive(MinimumClimateWeight) || !Positive(MaximumClimateWeight) ||
                MaximumClimateWeight < MinimumClimateWeight ||
                !Unit(BaseCheckProbability) || !Unit(MaximumCheckProbability) ||
                MaximumCheckProbability < BaseCheckProbability ||
                !Positive(MinimumDurationSeconds) || !Positive(MaximumDurationSeconds) ||
                !Positive(HardMaximumDurationSeconds) || MaximumDurationSeconds < MinimumDurationSeconds ||
                HardMaximumDurationSeconds < MaximumDurationSeconds ||
                !Unit(MinimumIntensity) || MinimumIntensity <= 0f || !Unit(MaximumIntensity) ||
                MaximumIntensity < MinimumIntensity || !Positive(CooldownSeconds) ||
                !NonNegative(MaximumCoolingCelsius) || !NonNegative(CoolingPerSecond) ||
                !Positive(CoolingRecoveryPerSecond) || !Unit(GeographicWindStrength) ||
                !NonNegative(TemperatureGradientWindFactor) || TemperatureGradientWindFactor > 10f || !Unit(WindPerturbationStrength) ||
                !Positive(WindPerturbationPeriodSeconds) || WindPerturbationPeriodSeconds < StepSeconds || !Positive(WindChangePerSecond) ||
                !Positive(CloudChangePerSecond) || !Positive(FogChangePerSecond) ||
                !Unit(FogHumidityStart) || !Unit(FogHumidityFull) || FogHumidityFull <= FogHumidityStart ||
                !Finite(FogTemperatureStart) || !Finite(FogTemperatureFull) ||
                FogTemperatureStart <= FogTemperatureFull || !Positive(FogWindLimit) || FogWindLimit > 1f ||
                !Unit(LightningHumidityStart) || !Unit(LightningHumidityFull) ||
                LightningHumidityFull <= LightningHumidityStart ||
                !Finite(LightningTemperatureStart) || !Finite(LightningTemperatureFull) ||
                LightningTemperatureFull <= LightningTemperatureStart ||
                !Positive(LightningChangePerSecond) || !Positive(BaseLightningIntervalSeconds) ||
                !Positive(MinimumLightningIntervalSeconds) ||
                MinimumLightningIntervalSeconds > BaseLightningIntervalSeconds ||
                !Unit(StrongWindStart) || StrongWindStart >= 1f || !NonNegative(MaximumLightningWindBonus))
                throw new ArgumentOutOfRangeException(nameof(RegionalWeatherSettings), "区域天气参数超出有限配置范围。");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Positive(float value) => Finite(value) && value > 0f;
        private static bool NonNegative(float value) => Finite(value) && value >= 0f;
        private static bool Unit(float value) => Finite(value) && value >= 0f && value <= 1f;
        #endregion
    }
}
