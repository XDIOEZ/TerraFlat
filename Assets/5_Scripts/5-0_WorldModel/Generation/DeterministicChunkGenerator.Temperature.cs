using System;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 环世界地理温度带

        // 极点带沿 X 绕世界一周，Y 按环绕最短距离降温，上下接缝属于同一条带。
        private static SurfaceClimateSample FinishSurfaceClimate(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY, SurfaceClimateSample sample)
        {
            sample.PolarInfluence = SamplePolarInfluence(request, settings, worldY);
            if (sample.PolarInfluence > 0d)
            {
                double min = settings.SurfaceClimateAlgorithm == SurfaceClimateAlgorithm.LegacyLand
                    ? settings.TemperatureCelsiusMin : -20d;
                double range = settings.SurfaceClimateAlgorithm == SurfaceClimateAlgorithm.LegacyLand
                    ? settings.TemperatureCelsiusMax - min : 65d;
                double polarTemperature = (settings.PolarBandCelsius - min) / Math.Max(0.000001d, range);
                sample.Temperature = Clamp01(CoolTowardsPolarBand(sample.Temperature,
                    polarTemperature, sample.PolarInfluence));
            }
            sample.SnowAllowed = IsSnowRegionAllowed(request, settings, sample.Height,
                sample.Temperature, sample.Precipitation, worldX, worldY);
            return sample;
        }

        private static double SamplePolarInfluence(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldY)
        {
            if (!request.Topology.IsWrapped || !settings.PolarBandEnabled)
                return 0d;
            double centerY = request.Topology.Min.Y + request.Topology.Span.Y * settings.PolarBandPosition;
            double distance = Math.Abs(request.Topology.ToDomain().ShortestDelta(
                new double2(0d, centerY), new double2(0d, worldY)).y);
            double halfWidth = request.Topology.Span.Y * 0.5d * settings.PolarBandHalfWidth;
            double progress = Clamp01(distance / halfWidth);
            double range = settings.PolarBandEdgeCelsius - settings.PolarBandCelsius;
            if (range <= 0.000001d)
                return 1d - progress * progress * (3d - 2d * progress);
            // 纬向距离作为累计概率，让 -25°C 附近占地最多，同时保持靠近极线更冷。
            double temperature = SamplePolarQuadraticTemperature(settings.PolarBandCelsius,
                settings.PolarBandPeakCelsius, settings.PolarBandEdgeCelsius, progress);
            return Clamp01((settings.PolarBandEdgeCelsius - temperature) / range);
        }

        // 对二次权重 1-((T-峰值)/半径)² 的累计概率解析反解，避免逐格循环抽样。
        private static double SamplePolarQuadraticTemperature(double minimum, double peak,
            double maximum, double probability)
        {
            if (maximum <= minimum + 0.000001d || probability <= 0d)
                return minimum;
            if (probability >= 1d)
                return maximum;
            double radius = Math.Max(peak - minimum, maximum - peak);
            double lower = (minimum - peak) / radius;
            double upper = (maximum - peak) / radius;
            double lowerIntegral = lower - lower * lower * lower / 3d;
            double upperIntegral = upper - upper * upper * upper / 3d;
            double integral = lowerIntegral + (upperIntegral - lowerIntegral) * Clamp01(probability);
            double normalized = 2d * Math.Sin(Math.Asin(Math.Max(-1d,
                Math.Min(1d, integral * 1.5d))) / 3d);
            return Math.Max(minimum, Math.Min(maximum, peak + radius * normalized));
        }

        private static double CoolTowardsPolarBand(double temperature, double polarTemperature,
            double influence) => temperature + Math.Min(0d, polarTemperature - temperature) * influence;

        #endregion

        #region 极圈河流源点

        // 极圈源点只保留固定种子筛选出的 1/20，整条径流继续追踪而不逐格挖断河道。
        internal static bool ShouldGeneratePolarRiverSource(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, Int2 source)
        {
            if (settings.PolarRiverSourceChanceMultiplier >= 1d ||
                SamplePolarInfluence(request, settings, source.Y) <= 0d)
                return true;
            return Hash01(request.WorldSeed, request.Topology.NormalizeX(source.X),
                request.Topology.NormalizeY(source.Y), 0x724fa8d3u) <
                settings.PolarRiverSourceChanceMultiplier;
        }

        #endregion

        #region 极圈天然积雪

        private const double PolarSnowMinimumCelsius = -30d;
        private const double PolarSnowMaximumCelsius = -10d;

        // -10°C 对应一层、-30°C 对应十层，中间按温差线性换算后向上取整。
        private static float ResolvePolarSnowDepth(double temperatureCelsius)
        {
            double coldness = Clamp01((PolarSnowMaximumCelsius - temperatureCelsius) /
                                     (PolarSnowMaximumCelsius - PolarSnowMinimumCelsius));
            double layers = Math.Ceiling(1d + coldness * (SnowDepthLayer.LayerCount - 1));
            return (float)(layers / SnowDepthLayer.LayerCount);
        }

        #endregion

        #region 群系温度过渡
        private const int BiomeTemperatureBlendRadius = 8;

        private static double ResolveBiomeTemperature(SurfaceClimateSample sample,
            ChunkGenerationSettingsSnapshot settings)
        {
            // 雪地从外缘向极线按二次峰值分布渐冷，交界温度再由共用邻格窗口混合。
            double baseline = sample.BaseBiome switch
            {
                SurfaceBiomeKind.Snow => settings.PolarBandEdgeCelsius,
                SurfaceBiomeKind.Stone => 10d,
                _ => sample.TemperatureCelsius
            };
            return CoolTowardsPolarBand(baseline, settings.PolarBandCelsius, sample.PolarInfluence);
        }

        // 单格出生查询与完整区块使用同一个世界坐标窗口，环绕边界由气候采样归一化。
        private static double SampleBlendedBiomeTemperature(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            double total = 0d;
            for (int y = -BiomeTemperatureBlendRadius; y <= BiomeTemperatureBlendRadius; y++)
            for (int x = -BiomeTemperatureBlendRadius; x <= BiomeTemperatureBlendRadius; x++)
            {
                var sample = SampleSurfaceClimate(request, settings, worldX + x, worldY + y);
                sample.BaseBiome = SampleBaseSurfaceBiome(request, settings, worldX + x, worldY + y,
                    out _, out _);
                total += ResolveBiomeTemperature(sample, settings);
            }
            int diameter = BiomeTemperatureBlendRadius * 2 + 1;
            return total / (diameter * diameter);
        }
        #endregion
    }
}
