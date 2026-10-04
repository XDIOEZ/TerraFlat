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
            bool polarEnabled = request.Topology.IsWrapped && settings.PolarBandEnabled;
            double polarDistance = polarEnabled ? SamplePolarDistance(request, settings, worldX, worldY) : 0d;
            sample.IsPolarBand = polarEnabled &&
                                 polarDistance <= request.Topology.Span.Y * 0.5d * settings.PolarBandHalfWidth;
            double baseline = sample.TemperatureCelsius;
            if (polarEnabled)
            {
                baseline = SampleLatitudeTemperature(request, settings, worldX, polarDistance) +
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
            ChunkGenerationSettingsSnapshot settings, int worldX, double distance)
        {
            double halfSpan = request.Topology.Span.Y * 0.5d;
            double halfWidth = halfSpan * settings.PolarBandHalfWidth;
            if (distance <= halfWidth)
                return SampleQuadraticPeakTemperature(settings.PolarBandCelsius,
                    settings.PolarBandPeakCelsius, settings.PolarBandEdgeCelsius, Clamp01(distance / halfWidth));
            double remainingSpan = halfSpan - halfWidth;
            double transition = Math.Min(settings.PolarBandTransitionTiles, remainingSpan);
            double equatorCelsius = SampleEquatorTemperature(request, settings, worldX);
            double temperateCelsius = transition >= remainingSpan
                ? equatorCelsius : settings.PolarBandTransitionCelsius;
            double outsideDistance = distance - halfWidth;
            // 极圈外先在短距离内回到温带底温，随后继续保留朝赤道渐暖的纬度变化。
            if (outsideDistance <= transition)
                return Lerp(settings.PolarBandEdgeCelsius, temperateCelsius,
                    Smooth(Clamp01(outsideDistance / Math.Max(0.000001d, transition))));
            return Lerp(temperateCelsius, equatorCelsius,
                Smooth(Clamp01((outsideDistance - transition) /
                    Math.Max(0.000001d, remainingSpan - transition))));
        }

        private static double SamplePolarDistance(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            double centerY = request.Topology.Min.Y + request.Topology.Span.Y * settings.PolarBandPosition +
                             SamplePolarBoundaryOffset(request, settings, worldX);
            return Math.Abs(request.Topology.ToDomain().ShortestDelta(
                new double2(0d, centerY), new double2(0d, worldY)).y);
        }

        private static bool IsInsidePolarBand(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            if (!request.Topology.IsWrapped || !settings.PolarBandEnabled)
                return false;
            double halfWidth = request.Topology.Span.Y * 0.5d * settings.PolarBandHalfWidth;
            return SamplePolarDistance(request, settings, worldX, worldY) <= halfWidth;
        }

        #endregion

        #region 赤道二次峰值底温

        // 赤道底温按固定种子随机控制点平滑变化，二次概率峰默认位于 45℃。
        private static double SampleEquatorTemperature(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX)
        {
            double probability = SampleWrappedRandomControlPoints(request, worldX,
                settings.EquatorSpacingTiles, 0x4a68b923u, true);
            return SampleQuadraticPeakTemperature(settings.EquatorMinimumCelsius,
                settings.EquatorPeakCelsius, settings.EquatorMaximumCelsius, probability);
        }

        #endregion

        #region 极圈边界随机偏移

        private static double SamplePolarBoundaryOffset(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX)
        {
            if (!request.Topology.IsWrapped || !settings.PolarBandEnabled || settings.PolarBoundaryOffsetTiles <= 0d)
                return 0d;
            double halfSpan = request.Topology.Span.Y * 0.5d;
            double halfWidth = halfSpan * settings.PolarBandHalfWidth;
            double amplitude = Math.Min(settings.PolarBoundaryOffsetTiles,
                Math.Min(halfWidth, halfSpan - halfWidth) * 0.5d);
            double coarse = SampleWrappedRandomControlPoints(request, worldX, settings.PolarBoundarySpacingTiles, 0x832f91a7u);
            double detail = SampleWrappedRandomControlPoints(request, worldX, settings.PolarBoundarySpacingTiles * 0.25d, 0xc7b35e29u);
            // 大小两组固定种子随机偏移平滑连接，整条冷带只平移而不扩大配置宽度。
            return amplitude * Lerp(coarse, detail, settings.PolarBoundaryDetailStrength);
        }

        private static double SampleWrappedRandomControlPoints(ChunkGenerationRequest request,
            int worldX, double spacing, uint salt, bool uniformProbability = false)
        {
            int span = request.Topology.Span.X;
            int repeat = Math.Max(2, (int)Math.Ceiling(span / Math.Max(8d, spacing)));
            double position = (request.Topology.NormalizeX(worldX) - (double)request.Topology.Min.X) / span * repeat;
            int left = (int)Math.Floor(position);
            int right = (left + 1) % repeat;
            double a = Hash01(request.WorldSeed, left, 0, salt) * 2d - 1d;
            double b = Hash01(request.WorldSeed, right, 0, salt) * 2d - 1d;
            // 首尾控制点共用同一随机值，跨区块和地图环绕处也连续。
            double weight = Smooth(position - left);
            double value = Lerp(a, b, weight);
            if (!uniformProbability)
                return value;
            // 校正随机插值的累计概率，避免平滑过程把温度概率峰推离配置值。
            double probability = Clamp01((value + 1d) * 0.5d);
            double smaller = Math.Min(weight, 1d - weight);
            double larger = 1d - smaller;
            if (smaller <= 0.000000001d)
                return probability;
            if (probability < smaller)
                return probability * probability / (2d * smaller * larger);
            if (probability <= larger)
                return (probability - smaller * 0.5d) / larger;
            return 1d - (1d - probability) * (1d - probability) / (2d * smaller * larger);
        }

        #endregion

        #region 共用二次峰值温度分布

        // 对二次权重 1-((T-峰值)/半径)² 的累计概率解析反解，避免逐格循环抽样。
        private static double SampleQuadraticPeakTemperature(double minimum, double peak,
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
                !IsInsidePolarBand(request, settings, source.X, source.Y))
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
        private static SurfaceBiomeKind ResolveSurfaceBiome(ChunkGenerationSettingsSnapshot settings,
            SurfaceClimateSample sample, double moisture, bool river, double? blendedCelsius = null)
        {
            return ResolveSurfaceBiomeRule(settings, sample, moisture, river, blendedCelsius)?.Biome
                   ?? settings.SurfaceFallbackBiome;
        }

        private static SurfaceBiomeRuleSnapshot ResolveSurfaceBiomeRule(ChunkGenerationSettingsSnapshot settings,
            SurfaceClimateSample sample, double moisture, bool river, double? blendedCelsius = null)
        {
            double celsius = blendedCelsius ?? sample.TemperatureCelsius;
            return SurfaceBiomeClassifier.ResolveRule(settings, sample.Height,
                settings.NormalizeTemperatureCelsius(celsius), sample.Precipitation, moisture, river,
                sample.SnowAllowed, celsius, sample.IsPolarBand);
        }

        // 单格出生查询与完整区块使用同一个世界坐标窗口，环绕边界由气候采样归一化。
        private static double SampleBlendedBiomeTemperature(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            double total = 0d;
            int radius = settings.BiomeTemperatureBlendRadius;
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                var sample = SampleSurfaceClimate(request, settings, worldX + x, worldY + y);
                total += sample.TemperatureCelsius;
            }
            int diameter = radius * 2 + 1;
            return total / (diameter * diameter);
        }
        #endregion
    }
}
