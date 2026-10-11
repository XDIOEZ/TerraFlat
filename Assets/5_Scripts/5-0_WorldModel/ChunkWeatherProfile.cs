using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    public static class WeatherElementIds
    {
        #region 本体天气标识
        public const string Rain = "rain";
        public const string Wind = "wind";
        public const string Thunder = "thunder";
        #endregion
    }

    /// <summary>区块创建时汇总一次的固定气候，风力是每格风矢量模长的平均值。</summary>
    [Serializable]
    public readonly struct ChunkWeatherClimate
    {
        #region 固定气候
        public readonly float AirHumidity;
        public readonly float WindX;
        public readonly float WindY;
        public readonly float WindStrength;
        public readonly float TemperatureCelsius;

        public ChunkWeatherClimate(float airHumidity, float windX, float windY,
            float windStrength, float temperatureCelsius)
        {
            if (!Finite(airHumidity) || !Finite(windX) || !Finite(windY) ||
                !Finite(windStrength) || !Finite(temperatureCelsius))
                throw new ArgumentOutOfRangeException(nameof(airHumidity), "区块固定气候须为有限数值。");
            AirHumidity = Math.Max(0f, Math.Min(1f, airHumidity));
            WindX = windX;
            WindY = windY;
            WindStrength = Math.Max(0f, Math.Min(1f, windStrength));
            TemperatureCelsius = temperatureCelsius;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        #endregion
    }

    /// <summary>单个天气要素在这个区块内固定的概率、强度和持续时间。</summary>
    [Serializable]
    public readonly struct WeatherElementProfile
    {
        #region 固定天气参数
        public readonly string Id;
        public readonly float Probability;
        public readonly float Intensity;
        public readonly float DurationSeconds;

        public WeatherElementProfile(string id, float probability, float intensity, float durationSeconds)
        {
            if (string.IsNullOrWhiteSpace(id) || !Unit(probability) || !Unit(intensity) ||
                float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds) || durationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(id), "区块天气要素须有有效标识和有限参数。");
            Id = id;
            Probability = probability;
            Intensity = intensity;
            DurationSeconds = durationSeconds;
        }

        internal void Validate() => _ = new WeatherElementProfile(Id, Probability, Intensity, DurationSeconds);
        private static bool Unit(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
        #endregion
    }

    /// <summary>区块固定天气配置拥有自己的只读副本，不引用生成草稿或可变配置。</summary>
    [Serializable]
    public sealed class ChunkWeatherProfile
    {
        #region 不可变区块天气配置
        public ChunkWeatherClimate Climate { get; }
        public IReadOnlyList<WeatherElementProfile> Elements { get; }

        public ChunkWeatherProfile(ChunkWeatherClimate climate, IReadOnlyList<WeatherElementProfile> elements)
        {
            if (elements == null)
                throw new ArgumentNullException(nameof(elements));
            Climate = climate;
            var copy = new WeatherElementProfile[elements.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < copy.Length; i++)
            {
                elements[i].Validate();
                if (!ids.Add(elements[i].Id))
                    throw new ArgumentException("区块天气要素标识不能重复。", nameof(elements));
                copy[i] = elements[i];
            }
            Elements = Array.AsReadOnly(copy);
        }

        public ChunkWeatherProfile Clone() => new(Climate, Elements);

        public bool TryGetElement(string id, out WeatherElementProfile element)
        {
            foreach (WeatherElementProfile candidate in Elements)
            {
                if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
                {
                    element = candidate;
                    return true;
                }
            }
            element = default;
            return false;
        }
        #endregion
    }
}
