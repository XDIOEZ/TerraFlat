using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 风携水汽与地理降水

        private static readonly ConcurrentDictionary<HeightDrivenRegionKey, Lazy<double>> precipitationCache = new();
        private static readonly ConcurrentQueue<HeightDrivenRegionKey> precipitationCacheOrder = new();

        // 参考 Azgaar passWind：先带背景水汽，海洋补充，陆地降水扣除携水量。
        private static double SampleTransportPrecipitation(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            int stride = settings.PrecipitationCellSize;
            int countX = request.Topology.IsWrapped ? Math.Max(1, request.Topology.Span.X / stride) : 0;
            int countY = request.Topology.IsWrapped ? Math.Max(1, request.Topology.Span.Y / stride) : 0;
            double stepX = countX > 0 ? request.Topology.Span.X / (double)countX : stride;
            double stepY = countY > 0 ? request.Topology.Span.Y / (double)countY : stride;
            int anchorX = request.Topology.IsWrapped ? request.Topology.Min.X : 0;
            int anchorY = request.Topology.IsWrapped ? request.Topology.Min.Y : 0;
            double gridX = (request.Topology.NormalizeX(worldX) - anchorX) / stepX;
            double gridY = (request.Topology.NormalizeY(worldY) - anchorY) / stepY;
            int x = (int)Math.Floor(gridX), y = (int)Math.Floor(gridY);
            double tx = gridX - x, ty = gridY - y;
            double bottom = Lerp(Node(x, y), Node(x + 1, y), tx);
            double top = Lerp(Node(x, y + 1), Node(x + 1, y + 1), tx);
            return Clamp01(Lerp(bottom, top, ty));

            double Node(int nx, int ny)
            {
                if (countX > 0) nx = ((nx % countX) + countX) % countX;
                if (countY > 0) ny = ((ny % countY) + countY) % countY;
                var cell = new Int2((int)Math.Floor(anchorX + nx * stepX),
                    (int)Math.Floor(anchorY + ny * stepY));
                var key = new HeightDrivenRegionKey(request.WorldEpoch, request.Address.DimensionId,
                    request.WorldSeed, request.Profile.GenerationFingerprint, request.Topology, cell, 0, 0);
                var candidate = new Lazy<double>(() => TracePrecipitation(request, settings, cell),
                    System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
                Lazy<double> cached = precipitationCache.GetOrAdd(key, candidate);
                if (ReferenceEquals(candidate, cached))
                {
                    precipitationCacheOrder.Enqueue(key);
                    while (precipitationCache.Count > 8192 && precipitationCacheOrder.TryDequeue(out var oldest))
                        precipitationCache.TryRemove(oldest, out _);
                }
                return cached.Value;
            }
        }

        private static double TracePrecipitation(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, Int2 target)
        {
            var path = new List<SurfaceClimateSample>(settings.PrecipitationTraceSteps);
            var visited = new HashSet<Int2>();
            double x = target.X, y = target.Y;
            for (int step = 0; step < settings.PrecipitationTraceSteps; step++)
            {
                int px = request.Topology.NormalizeX((int)Math.Floor(x));
                int py = request.Topology.NormalizeY((int)Math.Floor(y));
                if (!visited.Add(new Int2(px, py))) break;
                SurfaceClimateSample sample = SampleGeographicClimate(request, settings, px, py);
                path.Add(sample);
                x -= sample.WindX * settings.PrecipitationCellSize;
                y -= sample.WindY * settings.PrecipitationCellSize;
            }
            double humidity = settings.PrecipitationBackgroundWater;
            double rainfall = 0d;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                SurfaceClimateSample sample = path[i];
                rainfall = 0d;
                if (sample.TemperatureCelsius < settings.PrecipitationFreezeCelsius) continue;
                if (sample.Height < settings.SeaLevel)
                {
                    humidity = Math.Min(1d, humidity + settings.PrecipitationOceanRecharge);
                    rainfall = settings.PrecipitationOceanRecharge * settings.PrecipitationAmountScale;
                    continue;
                }
                double nextHeight = i > 0 ? path[i - 1].Height : SampleHeight(request, settings,
                    target.X + (int)Math.Round(sample.WindX * settings.PrecipitationCellSize),
                    target.Y + (int)Math.Round(sample.WindY * settings.PrecipitationCellSize));
                double uplift = Math.Max(0d, nextHeight - sample.Height);
                double normalLoss = humidity * settings.PrecipitationLandLoss;
                double mountainLoss = uplift * settings.WindwardRainGain * humidity;
                double released = Math.Min(humidity, normalLoss + mountainLoss);
                rainfall = released * settings.PrecipitationAmountScale;
                humidity = Math.Max(0d, humidity - released);
            }
            return Clamp01(rainfall);
        }

        // 温度先独立合成，降水只消费地理温度，不反向改变地理温度。
        private static SurfaceClimateSample SampleGeographicClimate(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
            SurfaceClimateSample sample;
            if (settings.SurfaceClimateAlgorithm == SurfaceClimateAlgorithm.LegacyLand)
            {
                LegacyClimateSample raw = LegacyTerrainClimateKernel.SampleClimate(request, settings, worldX, worldY);
                sample = new SurfaceClimateSample
                {
                    Height = raw.Height, Temperature = raw.Temperature, TemperatureCelsius = raw.TemperatureCelsius,
                    WindX = raw.WindX, WindY = raw.WindY
                };
            }
            else
            {
                double noise = Fractal(CreateSeed(request, 0x85ebca6bu), worldX, worldY,
                    settings.ClimateScale, settings.ClimateOctaves, 2.07d, 0.5d, request.Topology);
                double latitude = request.Topology.IsWrapped ? 0d : Math.Min(0.34d, Math.Abs(worldY) * 0.000025d);
                double temperature = Clamp01(noise - latitude);
                LegacyTerrainClimateKernel.SampleWind(request, settings, worldX, worldY, out float wx, out float wy);
                sample = new SurfaceClimateSample
                {
                    Height = SampleHeight(request, settings, worldX, worldY), Temperature = temperature,
                    TemperatureCelsius = -20d + temperature * 65d, WindX = wx, WindY = wy
                };
            }
            return FinishGeographicTemperature(request, settings, worldX, worldY, sample);
        }

        #endregion
    }
}
