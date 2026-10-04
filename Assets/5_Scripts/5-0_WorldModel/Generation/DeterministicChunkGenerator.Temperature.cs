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
            return 1d - progress * progress * (3d - 2d * progress);
        }

        private static double CoolTowardsPolarBand(double temperature, double polarTemperature,
            double influence) => temperature + Math.Min(0d, polarTemperature - temperature) * influence;

        #endregion

        #region 群系温度过渡
        private const int BiomeTemperatureBlendRadius = 8;

        private static double ResolveBiomeTemperature(SurfaceClimateSample sample,
            ChunkGenerationSettingsSnapshot settings)
        {
            double baseline = sample.BaseBiome switch
            {
                SurfaceBiomeKind.Snow => -10d,
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
