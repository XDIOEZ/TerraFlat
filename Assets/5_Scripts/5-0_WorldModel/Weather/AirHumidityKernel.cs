using System;

namespace FlatWorld.WorldModel
{
    /// <summary>地理与动态空气湿度共用同一份纯数值规则。</summary>
    public readonly struct AirHumiditySettings
    {
        #region 冻结规则

        public AirHumiditySettings(float background, float waterContribution,
            float radiusTiles, float fullSourceDepth)
        {
            Background = Clamp(Finite(background, 0.15f), 0f, 1f);
            WaterContribution = Clamp(Finite(waterContribution, 0.75f), 0f, 1f);
            RadiusTiles = Clamp(Finite(radiusTiles, 12f), 1f, 96f);
            FullSourceDepth = Clamp(Finite(fullSourceDepth, 0.25f), 0.0001f, 1f);
        }

        public float Background { get; }
        public float WaterContribution { get; }
        public float RadiusTiles { get; }
        public float FullSourceDepth { get; }

        /// <summary>距离跟随世界空间倍率，强度和水深阈值保持原物理语义。</summary>
        public static AirHumiditySettings FromProfile(ChunkGenerationProfileSnapshot profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            float scale = Clamp(Read(profile, "world.spatialDistanceScale", 1f), 0.25f, 4f);
            return new AirHumiditySettings(
                Read(profile, "airHumidity.background", 0.15f),
                Read(profile, "airHumidity.waterContribution", 0.75f),
                Read(profile, "airHumidity.radiusTiles", 12f) * scale,
                Read(profile, "airHumidity.fullSourceDepth", 0.25f));
        }

        private static float Read(ChunkGenerationProfileSnapshot profile, string id, float fallback)
            => profile.NumericParameters.TryGetValue(id, out double value)
                ? Finite((float)value, fallback) : fallback;

        private static float Finite(float value, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

        private static float Clamp(float value, float min, float max)
            => Math.Max(min, Math.Min(max, value));

        #endregion
    }

    /// <summary>根据真实水深和最短距离求水体供湿，大片水面取最大贡献。</summary>
    public static class AirHumidityKernel
    {
        #region 统一空气湿度计算

        public const string GeographicLayer = "airHumidity";
        public const string BackgroundLayer = "airHumidity.background";

        public static float GetWaterSourceStrength(bool suppliesHumidity, float liquidDepth,
            AirHumiditySettings settings)
        {
            if (!suppliesHumidity || float.IsNaN(liquidDepth) || float.IsInfinity(liquidDepth) ||
                liquidDepth <= 0f)
                return 0f;
            return settings.WaterContribution * Math.Min(1f, liquidDepth / settings.FullSourceDepth);
        }

        public static float GetInfluence(float sourceStrength, double distanceSquared,
            AirHumiditySettings settings)
        {
            double radiusSquared = (double)settings.RadiusTiles * settings.RadiusTiles;
            if (sourceStrength <= 0f || distanceSquared >= radiusSquared)
                return 0f;
            double weight = Math.Max(0d, 1d - distanceSquared / radiusSquared);
            return (float)(sourceStrength * weight * weight);
        }

        public static float Combine(float background, float maximumInfluence)
            => Math.Max(0f, Math.Min(1f, background + maximumInfluence));

        #endregion
    }
}
