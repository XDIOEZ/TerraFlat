using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>天气要素只由固定区块气候生成概率、强度和持续时间。</summary>
    [Serializable]
    public sealed class WeatherElementRule
    {
        #region 气候权重与三个数值范围
        public string Id;
        public float HumidityWeight;
        public float PrecipitationWeight;
        public float WindWeight;
        public float MinimumProbability;
        public float MaximumProbability;
        public float MinimumIntensity;
        public float MaximumIntensity;
        public float MinimumDurationSeconds;
        public float MaximumDurationSeconds;

        public WeatherElementRule Clone() => (WeatherElementRule)MemberwiseClone();

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id) ||
                !NonNegative(HumidityWeight) || !NonNegative(WindWeight) || !NonNegative(PrecipitationWeight) ||
                !Finite(HumidityWeight + WindWeight + PrecipitationWeight) ||
                HumidityWeight + WindWeight + PrecipitationWeight <= 0f ||
                !Unit(MinimumProbability) || !Unit(MaximumProbability) || MaximumProbability < MinimumProbability ||
                !Unit(MinimumIntensity) || !Unit(MaximumIntensity) || MaximumIntensity < MinimumIntensity ||
                !NonNegative(MinimumDurationSeconds) || MinimumDurationSeconds <= 0f ||
                !NonNegative(MaximumDurationSeconds) || MaximumDurationSeconds < MinimumDurationSeconds)
                throw new ArgumentOutOfRangeException(nameof(WeatherElementRule), "区块天气要素参数超出有限配置范围。");
        }

        internal WeatherElementProfile CreateProfile(ChunkWeatherClimate climate)
        {
            // 雨天消费两层距离降水，空气湿度和风仍可供其它天气要素独立组合。
            float factor = (climate.AirHumidity * HumidityWeight + climate.WindStrength * WindWeight +
                            climate.Precipitation * PrecipitationWeight) /
                           (HumidityWeight + WindWeight + PrecipitationWeight);
            return new WeatherElementProfile(Id,
                Lerp(MinimumProbability, MaximumProbability, factor),
                Lerp(MinimumIntensity, MaximumIntensity, factor),
                Lerp(MinimumDurationSeconds, MaximumDurationSeconds, factor));
        }

        private static float Lerp(float from, float to, float factor) => from + (to - from) * factor;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool NonNegative(float value) => Finite(value) && value >= 0f;
        private static bool Unit(float value) => Finite(value) && value >= 0f && value <= 1f;
        #endregion
    }

    /// <summary>共享天气区的覆盖范围、固定时钟与可扩展要素配置，速率统一使用游戏秒。</summary>
    [Serializable]
    public sealed class RegionalWeatherSettings
    {
        #region 固定时钟与天气要素
        // 相邻多个地形区块共享一场天气，避免走几步就穿过整片雨区。
        public int RegionSizeInChunks = 8;
        public float StepSeconds = 2f;
        public float CheckIntervalSeconds = 30f;
        public int MaximumCatchUpSteps = 120;
        public float FreezingTemperature = 0f;
        public float MinimumLightningIntervalSeconds = 20f;
        public float LightningSeparationRadius = 384f; // 可被同一玩家听见的邻近雷击共用最短间隔。
        public WeatherElementRule[] Elements =
        {
            new WeatherElementRule
            {
                Id = WeatherElementIds.Rain, PrecipitationWeight = 1f, WindWeight = 0f,
                MinimumProbability = 0f, MaximumProbability = 0.16f,
                MinimumIntensity = 0.1f, MaximumIntensity = 0.85f,
                MinimumDurationSeconds = 45f, MaximumDurationSeconds = 180f
            },
            new WeatherElementRule
            {
                Id = WeatherElementIds.Wind, HumidityWeight = 0f, WindWeight = 1f,
                MinimumProbability = 0f, MaximumProbability = 0.25f,
                MinimumIntensity = 0.1f, MaximumIntensity = 1f,
                MinimumDurationSeconds = 45f, MaximumDurationSeconds = 180f
            },
            new WeatherElementRule
            {
                Id = WeatherElementIds.Thunder, PrecipitationWeight = 0.7f, WindWeight = 0.3f,
                MinimumProbability = 0f, MaximumProbability = 0.015f,
                MinimumIntensity = 0.15f, MaximumIntensity = 0.8f,
                MinimumDurationSeconds = 15f, MaximumDurationSeconds = 60f
            }
        };
        #endregion

        #region 配置冻结与生成
        public RegionalWeatherSettings Snapshot()
        {
            Validate();
            var copy = (RegionalWeatherSettings)MemberwiseClone();
            copy.Elements = new WeatherElementRule[Elements.Length];
            for (int i = 0; i < Elements.Length; i++)
                copy.Elements[i] = Elements[i].Clone();
            return copy;
        }

        public ChunkWeatherProfile CreateProfile(ChunkWeatherClimate climate)
        {
            Validate();
            var profiles = new WeatherElementProfile[Elements.Length];
            for (int i = 0; i < Elements.Length; i++)
                profiles[i] = Elements[i].CreateProfile(climate);
            return new ChunkWeatherProfile(climate, profiles);
        }

        public void Validate()
        {
            if (RegionSizeInChunks < 1 || RegionSizeInChunks > 64 ||
                !Finite(StepSeconds) || StepSeconds < 0.1f || StepSeconds > 60f ||
                !Finite(CheckIntervalSeconds) || CheckIntervalSeconds < StepSeconds ||
                MaximumCatchUpSteps < 1 || MaximumCatchUpSteps > 1024 ||
                !Finite(FreezingTemperature) || !Finite(MinimumLightningIntervalSeconds) ||
                MinimumLightningIntervalSeconds <= 0f || !Finite(LightningSeparationRadius) ||
                LightningSeparationRadius < 0f || Elements == null || Elements.Length == 0)
                throw new ArgumentOutOfRangeException(nameof(RegionalWeatherSettings), "区块天气参数超出有限配置范围。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (WeatherElementRule element in Elements)
            {
                if (element == null)
                    throw new ArgumentException("区块天气要素不能为空。", nameof(Elements));
                element.Validate();
                if (!ids.Add(element.Id))
                    throw new ArgumentException("区块天气要素标识不能重复。", nameof(Elements));
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        #endregion
    }
}
