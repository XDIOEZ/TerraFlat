using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>按天气强度分段调整自然事件的出现概率。</summary>
    [Serializable]
    public struct WeatherProbabilityBand
    {
        #region 强度门槛与概率倍率
        public float MinimumIntensity;
        public float Multiplier;
        #endregion
    }

    /// <summary>天气要素只由固定区块气候生成概率、强度和持续时间。</summary>
    [Serializable]
    public sealed class WeatherElementRule
    {
        #region 气候权重与三个数值范围
        public string Id;
        public float HumidityWeight;
        public float PrecipitationWeight;
        public float WindWeight;
        public float TemperatureWeight;
        public float MinimumTemperatureCelsius;
        public float MaximumTemperatureCelsius = 40f;
        public bool ParabolicEventVariation;
        public float MinimumProbability;
        public float MaximumProbability;
        public float MinimumIntensity;
        public float MaximumIntensity;
        public float MinimumDurationSeconds;
        public float MaximumDurationSeconds;
        public WeatherProbabilityBand[] ProbabilityBands = Array.Empty<WeatherProbabilityBand>();

        public WeatherElementRule Clone()
        {
            var copy = (WeatherElementRule)MemberwiseClone();
            copy.ProbabilityBands = (WeatherProbabilityBand[])ProbabilityBands.Clone();
            return copy;
        }

        /// <summary>只改自然事件抽签概率，未达到任何配置门槛时沿用原概率。</summary>
        public float ResolveProbabilityMultiplier(float intensity)
        {
            float multiplier = 1f;
            foreach (WeatherProbabilityBand band in ProbabilityBands)
            {
                if (intensity < band.MinimumIntensity) break;
                multiplier = band.Multiplier;
            }
            return multiplier;
        }

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id) ||
                !NonNegative(HumidityWeight) || !NonNegative(WindWeight) || !NonNegative(PrecipitationWeight) ||
                !NonNegative(TemperatureWeight) ||
                TemperatureWeight > 0f && (!Finite(MinimumTemperatureCelsius) || !Finite(MaximumTemperatureCelsius) ||
                    MaximumTemperatureCelsius <= MinimumTemperatureCelsius) ||
                !Finite(HumidityWeight + WindWeight + PrecipitationWeight + TemperatureWeight) ||
                HumidityWeight + WindWeight + PrecipitationWeight + TemperatureWeight <= 0f ||
                !Unit(MinimumProbability) || !Unit(MaximumProbability) || MaximumProbability < MinimumProbability ||
                !Unit(MinimumIntensity) || !Unit(MaximumIntensity) || MaximumIntensity < MinimumIntensity ||
                !NonNegative(MinimumDurationSeconds) || MinimumDurationSeconds <= 0f ||
                !NonNegative(MaximumDurationSeconds) || MaximumDurationSeconds < MinimumDurationSeconds ||
                ProbabilityBands == null)
                throw new ArgumentOutOfRangeException(nameof(WeatherElementRule), "区块天气要素参数超出有限配置范围。");
            float previousIntensity = -1f;
            foreach (WeatherProbabilityBand band in ProbabilityBands)
            {
                if (!Unit(band.MinimumIntensity) || !Unit(band.Multiplier) || band.MinimumIntensity <= previousIntensity)
                    throw new ArgumentOutOfRangeException(nameof(ProbabilityBands), "天气概率分段须按强度门槛递增且倍率在零到一之间。");
                previousIntensity = band.MinimumIntensity;
            }
        }

        internal WeatherElementProfile CreateProfile(ChunkWeatherClimate climate)
        {
            float temperature = TemperatureWeight <= 0f ? 0f : (float)Math.Max(0d, Math.Min(1d,
                ((double)climate.TemperatureCelsius - MinimumTemperatureCelsius) /
                ((double)MaximumTemperatureCelsius - MinimumTemperatureCelsius)));
            // 温湿条件控制起雾概率以及强度、时长上限，具体事件再抽取中间高两端低的变化。
            float factor = (climate.AirHumidity * HumidityWeight + climate.WindStrength * WindWeight +
                            climate.Precipitation * PrecipitationWeight + temperature * TemperatureWeight) /
                           (HumidityWeight + WindWeight + PrecipitationWeight + TemperatureWeight);
            factor = Math.Max(0f, Math.Min(1f, factor));
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
        public float WindTransitionWidth = 32f; // 风区边界两侧共同组成按距离渐弱的过渡带。
        public float StepSeconds = 2f;
        public float CheckIntervalSeconds = 30f;
        public float NaturalEventProbabilityMultiplier = 0.3f; // 所有自然天气统一减少出现机会，强风倍率再独立叠加。
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
                MinimumDurationSeconds = 45f, MaximumDurationSeconds = 180f,
                ProbabilityBands = new[]
                {
                    new WeatherProbabilityBand { MinimumIntensity = 0.8f, Multiplier = 0.5f },
                    new WeatherProbabilityBand { MinimumIntensity = 0.9f, Multiplier = 0.25f },
                    new WeatherProbabilityBand { MinimumIntensity = 1f, Multiplier = 0.1f }
                }
            },
            new WeatherElementRule
            {
                Id = WeatherElementIds.Thunder, PrecipitationWeight = 0.7f, WindWeight = 0.3f,
                MinimumProbability = 0f, MaximumProbability = 0.015f,
                MinimumIntensity = 0.15f, MaximumIntensity = 0.8f,
                MinimumDurationSeconds = 15f, MaximumDurationSeconds = 60f
            },
            new WeatherElementRule
            {
                Id = WeatherElementIds.Fog, HumidityWeight = 1f, TemperatureWeight = 1f,
                MinimumTemperatureCelsius = 0f, MaximumTemperatureCelsius = 40f, ParabolicEventVariation = true,
                MinimumProbability = 0f, MaximumProbability = 0.3f,
                MinimumIntensity = 0.1f, MaximumIntensity = 1f,
                MinimumDurationSeconds = 60f, MaximumDurationSeconds = 240f
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

        /// <summary>区域抽签时应用当前配置，区块冻结的基础气候概率保持独立。</summary>
        public float ResolveActivationProbability(WeatherElementProfile profile)
        {
            float probability = profile.Probability * NaturalEventProbabilityMultiplier;
            foreach (WeatherElementRule rule in Elements)
                if (string.Equals(rule.Id, profile.Id, StringComparison.Ordinal))
                    return probability * rule.ResolveProbabilityMultiplier(profile.Intensity);
            return probability;
        }

        public WeatherElementProfile SampleNaturalEvent(WeatherElementProfile profile, float first, float second, float third)
        {
            foreach (WeatherElementRule rule in Elements)
            {
                if (!string.Equals(rule.Id, profile.Id, StringComparison.Ordinal) || !rule.ParabolicEventVariation)
                    continue;
                if (!Finite(first) || first < 0f || first > 1f || !Finite(second) || second < 0f || second > 1f ||
                    !Finite(third) || third < 0f || third > 1f)
                    throw new ArgumentOutOfRangeException(nameof(first), "天气强度抽样须为零到一的有限随机数。");
                // 三个均匀随机数取中位数，得到密度为 6x(1-x) 的抛物线分布，中雾多、两端少。
                float factor = Math.Max(Math.Min(first, second), Math.Min(Math.Max(first, second), third));
                float minimumIntensity = Math.Min(rule.MinimumIntensity, profile.Intensity);
                float minimumDuration = Math.Min(rule.MinimumDurationSeconds, profile.DurationSeconds);
                return new WeatherElementProfile(profile.Id, profile.Probability,
                    minimumIntensity + (profile.Intensity - minimumIntensity) * factor,
                    minimumDuration + (profile.DurationSeconds - minimumDuration) * factor);
            }
            return profile;
        }

        public void Validate()
        {
            if (RegionSizeInChunks < 1 || RegionSizeInChunks > 64 ||
                !Finite(WindTransitionWidth) || WindTransitionWidth < 0f ||
                !Finite(StepSeconds) || StepSeconds < 0.1f || StepSeconds > 60f ||
                !Finite(CheckIntervalSeconds) || CheckIntervalSeconds < StepSeconds ||
                !Finite(NaturalEventProbabilityMultiplier) || NaturalEventProbabilityMultiplier < 0f || NaturalEventProbabilityMultiplier > 1f ||
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
