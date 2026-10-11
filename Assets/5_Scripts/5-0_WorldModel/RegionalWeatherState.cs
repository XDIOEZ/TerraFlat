using System;

namespace FlatWorld.WorldModel
{
    /// <summary>各天气要素独立记录活动时段，运行时不再反复计算气候。</summary>
    [Serializable]
    public sealed class WeatherElementState
    {
        #region 独立事件时段
        public string Id;
        public double StartedTime;
        public double EndTime;
        public float Intensity;
        public WeatherElementState Clone() => (WeatherElementState)MemberwiseClone();
        #endregion
    }

    /// <summary>区块天气保存真值，外部导出和恢复都复制，禁止修改引擎内部状态。</summary>
    [Serializable]
    public sealed class RegionalWeatherState
    {
        #region 固定气候、要素与时钟
        public Int2 Region;
        public double LastStepTime;
        public long CheckIndex;
        public ChunkWeatherProfile Profile;
        public WeatherElementState[] Elements = Array.Empty<WeatherElementState>();
        #endregion

        #region 手动表现覆盖与雷电序号
        public bool ForcedSnow;
        public float ForcedCloudCoverage = -1f;
        public float ForcedFogDensity = -1f;
        public float ForcedWindStrength = -1f;
        public double ForcedElementsEndTime;
        public double NextLightningTime;
        public double LastLightningTime = -1d;
        public int LightningSequence;
        public int Revision;

        public RegionalWeatherState Clone()
        {
            var copy = (RegionalWeatherState)MemberwiseClone();
            // 固定配置完全只读，可共享；运行中事件必须逐个复制。
            copy.Profile = Profile;
            copy.Elements = new WeatherElementState[Elements?.Length ?? 0];
            for (int i = 0; i < copy.Elements.Length; i++)
                copy.Elements[i] = Elements[i]?.Clone();
            return copy;
        }
        #endregion
    }

    /// <summary>只读消费快照；名称由使用方按独立效果组合派生。</summary>
    public struct RegionalWeatherSnapshot
    {
        #region 连续天气与所属区块
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
