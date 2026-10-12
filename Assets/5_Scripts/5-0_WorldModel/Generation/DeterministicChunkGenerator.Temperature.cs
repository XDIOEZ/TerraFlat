using System;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 环世界地理温度带

        // 降水完成后补齐群系区域资格，不回写已确定的地理温度。
        private static SurfaceClimateSample FinishSurfaceClimate(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY, SurfaceClimateSample sample)
        {
            // 极圈是否跳过零散雪原资格由雪地群系配置决定。
            sample.SnowAllowed = IsSnowRegionAllowed(request, settings, sample.Height,
                sample.TemperatureCelsius, sample.Precipitation, worldX, worldY);
            sample.BiomeRegionProbability = settings.HasProbabilisticSurfaceBiomes
                ? SampleBiomeRegionProbability(request, settings, worldX, worldY) : 0d;
            return sample;
        }

        // 地理温度只合成纬度底温、局部变化和海拔，先于降水确定且不受实时天气影响。
        private static SurfaceClimateSample FinishGeographicTemperature(ChunkGenerationRequest request,
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
            sample.TemperatureCelsius = baseline + settings.GetAltitudeTemperatureOffsetCelsius(sample.Height);
            sample.Temperature = settings.NormalizeTemperatureCelsius(sample.TemperatureCelsius);
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
            double equatorCelsius = SampleEquatorTemperature(request, settings, worldX);
            // 赤道高温带总宽度按世界南北跨度计算，其余纬度先平缓升温。
            double equatorHalfWidth = Math.Min(remainingSpan, halfSpan * settings.EquatorBandWidthRatio);
            double equatorStart = halfSpan - equatorHalfWidth;
            double outsideDistance = distance - halfWidth;
            if (equatorStart <= halfWidth + 0.000001d)
                return Lerp(settings.PolarBandEdgeCelsius, equatorCelsius,
                    Smooth(Clamp01(outsideDistance / Math.Max(0.000001d, remainingSpan))));
            double temperateSpan = equatorStart - halfWidth;
            double transition = Math.Min(settings.PolarBandTransitionTiles, temperateSpan * 0.5d);
            double temperateCelsius = settings.PolarBandTransitionCelsius;
            if (outsideDistance <= transition)
                return Lerp(settings.PolarBandEdgeCelsius, temperateCelsius,
                    Smooth(Clamp01(outsideDistance / Math.Max(0.000001d, transition))));
            double shoulderCelsius = (temperateCelsius + equatorCelsius) * 0.5d;
            if (distance < equatorStart)
                return Lerp(temperateCelsius, shoulderCelsius,
                    Smooth(Clamp01((outsideDistance - transition) /
                        Math.Max(0.000001d, temperateSpan - transition))));
            return Lerp(shoulderCelsius, equatorCelsius,
                Smooth(Clamp01((distance - equatorStart) / Math.Max(0.000001d, equatorHalfWidth))));
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
            return ResolveUniformInterpolationProbability(probability, weight);
        }

        private static double ResolveUniformInterpolationProbability(double probability, double weight)
        {
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

        #region 群系区域概率

        // 独立种子的平滑区域概率保留大片群系，并用累计概率校正保证 0.5 表示一半机会。
        private static double SampleBiomeRegionProbability(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            bool wrapped = request.Topology.IsWrapped;
            int repeatX = wrapped ? Math.Max(2, (int)Math.Ceiling(
                request.Topology.Span.X / (double)settings.BiomeRegionSize)) : 0;
            int repeatY = wrapped ? Math.Max(2, (int)Math.Ceiling(
                request.Topology.Span.Y / (double)settings.BiomeRegionSize)) : 0;
            double x = wrapped
                ? (request.Topology.NormalizeX(worldX) - (double)request.Topology.Min.X) /
                  request.Topology.Span.X * repeatX
                : worldX / (double)settings.BiomeRegionSize;
            double y = wrapped
                ? (request.Topology.NormalizeY(worldY) - (double)request.Topology.Min.Y) /
                  request.Topology.Span.Y * repeatY
                : worldY / (double)settings.BiomeRegionSize;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            int x1 = wrapped ? PositiveMod(x0 + 1, repeatX) : x0 + 1;
            int y1 = wrapped ? PositiveMod(y0 + 1, repeatY) : y0 + 1;
            double tx = Smooth(x - x0), ty = Smooth(y - y0);
            const uint salt = 0x8b5ad4c3u;
            double a = Hash01(request.WorldSeed, x0, y0, salt);
            double b = Hash01(request.WorldSeed, x1, y0, salt);
            double c = Hash01(request.WorldSeed, x0, y1, salt);
            double d = Hash01(request.WorldSeed, x1, y1, salt);
            double lower = ResolveUniformInterpolationProbability(Lerp(a, b, tx), tx);
            double upper = ResolveUniformInterpolationProbability(Lerp(c, d, tx), tx);
            return ResolveUniformInterpolationProbability(Lerp(lower, upper, ty), ty);
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
                sample.BiomeRegionProbability, sample.SnowAllowed, celsius, sample.IsPolarBand);
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
