using System;

namespace FlatWorld.WorldModel
{
    /// <summary>一次采样的完整输入，温度已经剥离本区域上一次雨天降温。</summary>
    public struct RegionalWeatherInput
    {
        #region 环境输入
        public bool IsComplete;
        public float AirHumiditySupply;
        public float TemperatureBeforeRainCooling;
        public float GeographicPrecipitation;
        public float GeographicWindX;
        public float GeographicWindY;
        public float TemperatureGradientX;
        public float TemperatureGradientY;

        public RegionalWeatherInput(bool isComplete, float airHumiditySupply,
            float temperatureBeforeRainCooling, float geographicPrecipitation,
            float geographicWindX, float geographicWindY,
            float temperatureGradientX = 0f, float temperatureGradientY = 0f)
        {
            IsComplete = isComplete;
            AirHumiditySupply = airHumiditySupply;
            TemperatureBeforeRainCooling = temperatureBeforeRainCooling;
            GeographicPrecipitation = geographicPrecipitation;
            GeographicWindX = geographicWindX;
            GeographicWindY = geographicWindY;
            TemperatureGradientX = temperatureGradientX;
            TemperatureGradientY = temperatureGradientY;
        }
        #endregion
    }

    /// <summary>区域保存真值，外部导出和恢复都复制，禁止修改引擎内部状态。</summary>
    [Serializable]
    public sealed class RegionalWeatherState
    {
        #region 时钟与最近有效采样
        public Int2 Region;
        public double LastStepTime;
        public long CheckIndex;
        public bool HasValidInput;
        public RegionalWeatherInput LastValidInput;
        #endregion

        #region 降水与环境反馈
        public double PrecipitationStartedTime;
        public double PrecipitationEndTime;
        public double CooldownEndTime;
        public float InitialPrecipitationIntensity;
        public float PrecipitationIntensity;
        public bool ForcedSnow;
        public float HumidityDeficit;
        public float CoolingAmountCelsius;
        #endregion

        #region 独立天气要素
        public float WindX;
        public float WindY;
        public float CloudCoverage;
        public float FogDensity;
        public float LightningActivity;
        public float ForcedCloudCoverage = -1f;
        public float ForcedFogDensity = -1f;
        public float ForcedWindStrength = -1f;
        public double ForcedElementsEndTime;
        public double NextLightningTime;
        public double LastLightningTime = -1d;
        public int LightningSequence;
        public int Revision;

        public RegionalWeatherState Clone() => (RegionalWeatherState)MemberwiseClone();
        #endregion
    }

    /// <summary>只读消费快照；名称由使用方按独立效果组合派生。</summary>
    public struct RegionalWeatherSnapshot
    {
        #region 连续天气与所属区域
        public Int2 Region;
        public double SampleTime;
        public float AirHumidity;
        public float HumidityDeficit;
        public float CoolingOffsetCelsius;
        public float RainIntensity;
        public float SnowIntensity;
        public float PrecipitationIntensity;
        public float WindX;
        public float WindY;
        public float WindStrength;
        public float CloudCoverage;
        public float FogDensity;
        public float LightningActivity;
        public int LightningSequence;
        public float RemainingSeconds;
        public int Revision;
        #endregion
    }

    /// <summary>本轮有限推进的降水积分，旧时间跳过部分不重复浇水或积雪。</summary>
    public readonly struct RegionalWeatherAdvanceResult
    {
        #region 本轮结算
        public readonly RegionalWeatherSnapshot Snapshot;
        public readonly float RainExposureSeconds;
        public readonly float SnowExposureSeconds;

        public RegionalWeatherAdvanceResult(RegionalWeatherSnapshot snapshot,
            float rainExposureSeconds = 0f, float snowExposureSeconds = 0f)
        {
            Snapshot = snapshot;
            RainExposureSeconds = rainExposureSeconds;
            SnowExposureSeconds = snowExposureSeconds;
        }
        #endregion
    }
}
