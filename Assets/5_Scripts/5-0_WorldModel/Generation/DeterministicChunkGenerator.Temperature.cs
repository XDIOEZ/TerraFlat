using System;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 环世界地理温度带

        // 地理温度按纬度底温、局部变化、海拔、降雨和风向共同合成，不由群系反向指定。
        private static SurfaceClimateSample FinishSurfaceClimate(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY, SurfaceClimateSample sample)
        {
            sample.IsPolarBand = IsInsidePolarBand(request, settings, worldY);
            double baseline = sample.TemperatureCelsius;
            if (request.Topology.IsWrapped && settings.PolarBandEnabled)
            {
                baseline = SampleLatitudeTemperature(request, settings, worldY) +
                    (sample.Temperature * 2d - 1d) * settings.RegionalTemperatureVariationCelsius;
            }
            double rainOffset = -Clamp01(sample.Precipitation) * settings.RainTemperatureCoolingCelsius;
            double windRainDifference = sample.Precipitation - sample.BasePrecipitation;
            double windOffset = -Math.Max(0d, windRainDifference) * settings.WindwardTemperatureCoolingCelsius +
                                Math.Max(0d, -windRainDifference) * settings.LeewardTemperatureWarmingCelsius;
            sample.TemperatureCelsius = baseline + settings.GetAltitudeTemperatureOffsetCelsius(sample.Height) +
                                        rainOffset + windOffset;
            sample.Temperature = settings.NormalizeTemperatureCelsius(sample.TemperatureCelsius);
            // 此处只采样零散雪原资格，极圈是否跳过资格由雪地群系配置决定。
            sample.SnowAllowed = IsSnowRegionAllowed(request, settings, sample.Height,
                sample.TemperatureCelsius, sample.Precipitation, worldX, worldY);
            return sample;
        }

        private static double SampleLatitudeTemperature(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldY)
        {
            double distance = SamplePolarDistance(request, settings, worldY);
            double halfSpan = request.Topology.Span.Y * 0.5d;
            double halfWidth = halfSpan * settings.PolarBandHalfWidth;
            if (distance <= halfWidth)
                return SamplePolarQuadraticTemperature(settings.PolarBandCelsius,
                    settings.PolarBandPeakCelsius, settings.PolarBandEdgeCelsius, Clamp01(distance / halfWidth));
            double progress = Clamp01((distance - halfWidth) / Math.Max(0.000001d, halfSpan - halfWidth));
            double smooth = progress * progress * (3d - 2d * progress);
            return settings.PolarBandEdgeCelsius +
                   (settings.EquatorTemperatureCelsius - settings.PolarBandEdgeCelsius) * smooth;
        }

        private static double SamplePolarDistance(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldY)
        {
            double centerY = request.Topology.Min.Y + request.Topology.Span.Y * settings.PolarBandPosition;
            return Math.Abs(request.Topology.ToDomain().ShortestDelta(
                new double2(0d, centerY), new double2(0d, worldY)).y);
        }

        private static bool IsInsidePolarBand(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldY)
        {
            if (!request.Topology.IsWrapped || !settings.PolarBandEnabled)
                return false;
            double halfWidth = request.Topology.Span.Y * 0.5d * settings.PolarBandHalfWidth;
            return SamplePolarDistance(request, settings, worldY) <= halfWidth;
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

        #endregion

        #region 极圈河流源点

        // 极圈源点只保留固定种子筛选出的 1/20，整条径流继续追踪而不逐格挖断河道。
        internal static bool ShouldGeneratePolarRiverSource(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, Int2 source)
        {
            if (settings.PolarRiverSourceChanceMultiplier >= 1d ||
                !IsInsidePolarBand(request, settings, source.Y))
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

        private static SurfaceBiomeKind ResolveSurfaceBiome(ChunkGenerationSettingsSnapshot settings,
            SurfaceClimateSample sample, double moisture, bool river, double? blendedCelsius = null)
        {
            double celsius = blendedCelsius ?? sample.TemperatureCelsius;
            return SurfaceBiomeClassifier.Resolve(settings, sample.Height,
                settings.NormalizeTemperatureCelsius(celsius), sample.Precipitation, moisture, river,
                sample.SnowAllowed, celsius, sample.IsPolarBand);
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
                total += sample.TemperatureCelsius;
            }
            int diameter = BiomeTemperatureBlendRadius * 2 + 1;
            return total / (diameter * diameter);
        }
        #endregion
    }
}
