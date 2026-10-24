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

    /// <summary>触发概率直接读表，每次事件独立读取强度和持续时间的离散配置。</summary>
    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class WeatherElementRule
    {
        #region 气候权重与三个数值范围
        public string Id;
        public float HumidityWeight;
        public float PrecipitationWeight;
        public float WindWeight;
        public float TemperatureWeight;
        public float MinimumTemperatureCelsius;
        public float MaximumTemperatureCelsius = 40f;
        public float ActivationProbability;
        public float MinimumIntensity;
        public float MaximumIntensity;
        public float MinimumDurationSeconds;
        public float MaximumDurationSeconds;
        public WeatherEventDistribution IntensityDistribution;
        public WeatherEventDistribution DurationDistribution;
        public WeatherProbabilityBand[] ProbabilityBands = Array.Empty<WeatherProbabilityBand>();
        public float RecoveryProbabilityMultiplier = 1f;
        public float RecoverySeconds = 0f; // 默认不叠加结束后的概率抑制。

        public WeatherElementRule Clone()
        {
            var copy = (WeatherElementRule)MemberwiseClone();
            copy.ProbabilityBands = (WeatherProbabilityBand[])ProbabilityBands.Clone();
            copy.IntensityDistribution = IntensityDistribution?.Clone();
            copy.DurationDistribution = DurationDistribution?.Clone();
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
                !Unit(ActivationProbability) ||
                !Unit(MinimumIntensity) || !Unit(MaximumIntensity) || MaximumIntensity < MinimumIntensity ||
                !NonNegative(MinimumDurationSeconds) || MinimumDurationSeconds <= 0f ||
                !NonNegative(MaximumDurationSeconds) || MaximumDurationSeconds < MinimumDurationSeconds ||
                !Unit(RecoveryProbabilityMultiplier) || !NonNegative(RecoverySeconds) ||
                ProbabilityBands == null)
                throw new ArgumentOutOfRangeException(nameof(WeatherElementRule), "区块天气要素参数超出有限配置范围。");
            IntensityDistribution?.Validate(MinimumIntensity, MaximumIntensity);
            DurationDistribution?.Validate(MinimumDurationSeconds, MaximumDurationSeconds);
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
            // 显式概率表不受气候影响，未配事件表的夹具仍可使用气候取值。
            float factor = (climate.AirHumidity * HumidityWeight + climate.WindStrength * WindWeight +
                            climate.Precipitation * PrecipitationWeight + temperature * TemperatureWeight) /
                           (HumidityWeight + WindWeight + PrecipitationWeight + TemperatureWeight);
            factor = Math.Max(0f, Math.Min(1f, factor));
            return new WeatherElementProfile(Id,
                ActivationProbability,
                IntensityDistribution == null ? Lerp(MinimumIntensity, MaximumIntensity, factor) : MaximumIntensity,
                DurationDistribution == null ? Lerp(MinimumDurationSeconds, MaximumDurationSeconds, factor) : MaximumDurationSeconds);
        }

        private static float Lerp(float from, float to, float factor) => from + (to - from) * factor;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool NonNegative(float value) => Finite(value) && value >= 0f;
        private static bool Unit(float value) => Finite(value) && value >= 0f && value <= 1f;
        #endregion
    }

    [Serializable]
    [MemoryPack.MemoryPackable]
    public partial struct WeatherAssociationRule
    {
        #region 天气关联
        public string SourceId;
        public string TargetId;
        public float ProbabilityBonus;
        #endregion
    }

    /// <summary>共享天气区的覆盖范围、固定时钟与可扩展要素配置，速率统一使用游戏秒。</summary>
    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class RegionalWeatherSettings
    {
        #region 固定时钟与天气要素
        // 相邻多个地形区块共享一场天气，避免走几步就穿过整片雨区。
        public int RegionSizeInChunks = 8;
        public float WindTransitionWidth = 32f; // 风区边界两侧共同组成按距离渐弱的过渡带。
        public float StepSeconds = 2f;
        public float CheckIntervalSeconds = 300f; // 每五个游戏分钟独立检查全部自然天气要素。
        public float NaturalEventProbabilityMultiplier = 1f; // 默认直接使用JSON中配置的触发率。
        public int MaximumCatchUpSteps = 120;
        public float FreezingTemperature = 0f;
        public float MinimumLightningIntervalSeconds = 20f;
        public float LightningSeparationRadius = 384f; // 可被同一玩家听见的邻近雷击共用最短间隔。
        public WeatherAssociationRule[] Associations = Array.Empty<WeatherAssociationRule>();
        public WeatherElementRule[] Elements =
        {
            CreateDefaultRule(WeatherElementIds.Rain, 0f, 0f, 1f),
            CreateDefaultRule(WeatherElementIds.Wind, 0f, 1f, 0f),
            CreateDefaultRule(WeatherElementIds.Thunder, 0f, .3f, .7f),
            CreateDefaultRule(WeatherElementIds.Fog, 1f, 0f, 0f)
        };

        private static WeatherElementRule CreateDefaultRule(string id, float humidity, float wind, float precipitation)
            => new WeatherElementRule
            {
                Id = id, HumidityWeight = humidity, WindWeight = wind, PrecipitationWeight = precipitation,
                TemperatureWeight = id == WeatherElementIds.Fog ? 1f : 0f,
                ActivationProbability = .1f,
                MinimumIntensity = .1f, MaximumIntensity = 1f,
                MinimumDurationSeconds = 300f, MaximumDurationSeconds = 1440f,
                IntensityDistribution = new WeatherEventDistribution
                {
                    Values = new[] { .1f, .2f, .3f, .4f, .5f, .6f, .7f, .8f, .9f, 1f },
                    Probabilities = new[] { .1f, .1f, .1f, .1f, .1f, .1f, .1f, .1f, .1f, .1f }
                },
                DurationDistribution = new WeatherEventDistribution
                {
                    Values = new[] { 300f, 427f, 553f, 680f, 807f, 933f, 1060f, 1187f, 1313f, 1440f },
                    Probabilities = new[] { .1f, .1f, .1f, .1f, .1f, .1f, .1f, .1f, .1f, .1f }
                }
            };
        #endregion

        #region 配置冻结与生成
        public RegionalWeatherSettings Snapshot()
        {
            Validate();
            var copy = (RegionalWeatherSettings)MemberwiseClone();
            copy.Associations = (WeatherAssociationRule[])Associations.Clone();
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
            => ResolveActivationProbability(profile, 0d, 0d);

        /// <summary>各区域要素按自己的恢复完成时刻调节概率，近期历史不改变基础气候。</summary>
        public float ResolveActivationProbability(WeatherElementProfile profile, double now, double naturalRecoveryEndTime,
            float associationBonus = 0f)
        {
            if (!Finite(now) || now < 0d)
                throw new ArgumentOutOfRangeException(nameof(now), "天气概率采样时刻须为非负有限游戏秒。");
            if (!Finite(naturalRecoveryEndTime) || naturalRecoveryEndTime < 0d)
                throw new ArgumentOutOfRangeException(nameof(naturalRecoveryEndTime), "天气恢复完成时刻须为非负有限游戏秒。");
            if (!Finite(associationBonus) || associationBonus < 0f || associationBonus > 1f)
                throw new ArgumentOutOfRangeException(nameof(associationBonus));
            float probability = profile.Probability * NaturalEventProbabilityMultiplier;
            foreach (WeatherElementRule rule in Elements)
            {
                if (string.Equals(rule.Id, profile.Id, StringComparison.Ordinal))
                {
                    double remaining = Math.Max(0d, naturalRecoveryEndTime - now);
                    float recovering = rule.RecoverySeconds > 0f
                        ? (float)Math.Min(1d, remaining / rule.RecoverySeconds) : 0f;
                    float recoveryMultiplier = 1f - (1f - rule.RecoveryProbabilityMultiplier) * recovering;
                    return Math.Min(1f, probability * rule.ResolveProbabilityMultiplier(profile.Intensity) * recoveryMultiplier + associationBonus);
                }
            }
            return Math.Min(1f, probability + associationBonus);
        }

        /// <summary>恢复从自然事件计划结束后开始，记录完成时刻可保留回拨后的剩余恢复量。</summary>
        public double ResolveNaturalRecoveryEndTime(string elementId, double eventEndTime)
        {
            if (string.IsNullOrWhiteSpace(elementId))
                throw new ArgumentException("天气要素标识不能为空。", nameof(elementId));
            if (!Finite(eventEndTime) || eventEndTime < 0d)
                throw new ArgumentOutOfRangeException(nameof(eventEndTime), "天气事件结束时刻须为非负有限游戏秒。");
            foreach (WeatherElementRule rule in Elements)
                if (string.Equals(rule.Id, elementId, StringComparison.Ordinal))
                    return eventEndTime + (double)rule.RecoverySeconds;
            return eventEndTime;
        }

        public WeatherElementProfile SampleNaturalEvent(WeatherElementProfile profile, double first, double second, double third)
        {
            foreach (WeatherElementRule rule in Elements)
            {
                if (!string.Equals(rule.Id, profile.Id, StringComparison.Ordinal))
                    continue;
                if (rule.IntensityDistribution == null && rule.DurationDistribution == null)
                    return profile;
                if (!Finite(first) || first < 0f || first > 1f || !Finite(second) || second < 0f || second > 1f ||
                    !Finite(third) || third < 0f || third > 1f)
                    throw new ArgumentOutOfRangeException(nameof(first), "天气强度抽样须为零到一的有限随机数。");
                // 强度和时长分别读取配置表，区域风向长度不再决定事件强度。
                return new WeatherElementProfile(profile.Id, profile.Probability,
                    rule.IntensityDistribution?.Sample(rule.MinimumIntensity, rule.MaximumIntensity, first) ?? profile.Intensity,
                    rule.DurationDistribution?.Sample(rule.MinimumDurationSeconds, rule.MaximumDurationSeconds, second) ?? profile.DurationSeconds);
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
            if (Associations == null)
                throw new ArgumentNullException(nameof(Associations));
            foreach (WeatherAssociationRule association in Associations)
                if (!ids.Contains(association.SourceId ?? "") || !ids.Contains(association.TargetId ?? "") ||
                    association.SourceId == association.TargetId || !Finite(association.ProbabilityBonus) ||
                    association.ProbabilityBonus < 0f || association.ProbabilityBonus > 1f)
                    throw new ArgumentException("天气关联须引用不同的已有要素，概率增量须在零到一之间。", nameof(Associations));
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        #endregion
    }
}
