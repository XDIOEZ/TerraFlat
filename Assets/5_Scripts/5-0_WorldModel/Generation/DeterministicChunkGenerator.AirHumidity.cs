using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 天然水体与地理空气湿度

        private static ChunkGenerationRequest ExpandGenerationRequest(ChunkGenerationRequest source, int padding)
        {
            return new ChunkGenerationRequest(source.WorldEpoch,
                new WorldAddress(source.Address.DimensionId, source.Address.ChunkOrigin - new Int2(padding, padding)),
                source.WorldSeed, source.RequestVersion,
                source.Profile.WithChunkSize(source.Profile.Width + padding * 2, source.Profile.Height + padding * 2),
                source.Topology);
        }

        /// <summary>生成湿度只读取天然水文与冻结气候，缓存重复水源和混合温度。</summary>
        private sealed class GeographicAirHumiditySampler
        {
            private readonly struct InfluenceOffset
            {
                public InfluenceOffset(Int2 offset, double distanceSquared, float maximumInfluence)
                { Offset = offset; DistanceSquared = distanceSquared; MaximumInfluence = maximumInfluence; }
                public Int2 Offset { get; }
                public double DistanceSquared { get; }
                public float MaximumInfluence { get; }
            }

            private readonly ChunkGenerationRequest request;
            private readonly ChunkGenerationSettingsSnapshot settings;
            private readonly GeneratedHydrologyMap hydrology;
            private readonly AirHumiditySettings humiditySettings;
            private readonly IReadOnlyList<LavaBasin> volcanic;
            private readonly InfluenceOffset[] offsets;
            private readonly CancellationToken cancellation;
            private readonly Dictionary<Int2, SurfaceClimateSample> climateSamples = new();
            private readonly Dictionary<Int2, float> blendedTemperatures = new();
            private readonly Dictionary<Int2, float> sourceStrengths = new();

            public GeographicAirHumiditySampler(ChunkGenerationRequest request,
                ChunkGenerationSettingsSnapshot settings, GeneratedHydrologyMap hydrology,
                IReadOnlyList<LavaBasin> volcanic = null, CancellationToken cancellation = default)
            {
                this.request = request;
                this.settings = settings;
                this.hydrology = hydrology;
                this.volcanic = volcanic ?? Array.Empty<LavaBasin>();
                this.cancellation = cancellation;
                humiditySettings = AirHumiditySettings.FromProfile(request.Profile);
                offsets = BuildOffsets();
            }

            public float Sample(int worldX, int worldY)
            {
                float maximumInfluence = 0f;
                Int2 target = Normalize(worldX, worldY);
                for (int i = 0; i < offsets.Length; i++)
                {
                    if ((i & 63) == 0)
                        cancellation.ThrowIfCancellationRequested();
                    InfluenceOffset offset = offsets[i];
                    if (offset.MaximumInfluence <= maximumInfluence)
                        continue;
                    Int2 source = Normalize(target.X + offset.Offset.X, target.Y + offset.Offset.Y);
                    float influence = AirHumidityKernel.GetInfluence(GetSourceStrength(source),
                        offset.DistanceSquared, humiditySettings);
                    maximumInfluence = Math.Max(maximumInfluence, influence);
                }
                return AirHumidityKernel.Combine(humiditySettings.Background, maximumInfluence);
            }

            private float GetSourceStrength(Int2 source)
            {
                if (sourceStrengths.TryGetValue(source, out float cached))
                    return cached;
                SurfaceClimateSample climate = GetClimate(source);
                float depth;
                if (climate.Height < settings.SeaLevel)
                {
                    depth = (float)(1d - Math.Pow(Clamp01(climate.Height /
                        Math.Max(0.0001d, settings.SeaLevel)), 2d));
                }
                else if (hydrology != null && hydrology.TryGet(source.X, source.Y,
                             out GeneratedHydrologyCell water))
                {
                    depth = (float)water.Depth;
                }
                else
                {
                    sourceStrengths.Add(source, 0f);
                    return 0f;
                }

                // 与正式地格使用同一液深量化、零下结冰和石地岩浆覆盖规则。
                depth = QuantizeGeneratedLiquidDepth(depth);
                if (depth > 0f && (GetBlendedTemperature(source) < 0f || IsLavaCovered(source, climate)))
                    depth = 0f;
                float strength = AirHumidityKernel.GetWaterSourceStrength(depth > 0f, depth, humiditySettings);
                sourceStrengths.Add(source, strength);
                return strength;
            }

            private bool IsLavaCovered(Int2 source, SurfaceClimateSample climate)
            {
                if (volcanic.Count == 0)
                    return false;
                double moisture = Clamp01(climate.Precipitation * 0.78d + (1d - climate.Height) * 0.22d);
                if (ResolveSurfaceBiome(settings, climate, moisture, false) != SurfaceBiomeKind.Stone)
                    return false;
                foreach (LavaBasin basin in volcanic)
                    if (basin.TrySample(request.Topology, source.X, source.Y, out float depth, out _) && depth > 0f)
                        return true;
                return false;
            }

            private float GetBlendedTemperature(Int2 source)
            {
                if (blendedTemperatures.TryGetValue(source, out float cached))
                    return cached;
                int radius = settings.BiomeTemperatureBlendRadius;
                double total = 0d;
                // 行列求和顺序与单格正式温度一致，邻格气候只采样一次。
                for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                    total += GetClimate(Normalize(source.X + x, source.Y + y)).TemperatureCelsius;
                int diameter = radius * 2 + 1;
                float blended = (float)(total / (diameter * diameter));
                blendedTemperatures.Add(source, blended);
                return blended;
            }

            private SurfaceClimateSample GetClimate(Int2 source)
            {
                if (!climateSamples.TryGetValue(source, out SurfaceClimateSample sample))
                {
                    if ((climateSamples.Count & 63) == 0)
                        cancellation.ThrowIfCancellationRequested();
                    sample = SampleSurfaceClimate(request, settings, source.X, source.Y);
                    climateSamples.Add(source, sample);
                }
                return sample;
            }

            private InfluenceOffset[] BuildOffsets()
            {
                int radius = (int)Math.Ceiling(humiditySettings.RadiusTiles);
                double radiusSquared = (double)humiditySettings.RadiusTiles * humiditySettings.RadiusTiles;
                WorldTopologyDomain domain = request.Topology.ToDomain();
                var visited = new HashSet<Int2>();
                var result = new List<InfluenceOffset>();
                for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    double2 delta = domain.ShortestDelta(new double2(0d, 0d), new double2(x, y));
                    var offset = new Int2((int)delta.x, (int)delta.y);
                    double distanceSquared = delta.x * delta.x + delta.y * delta.y;
                    if (distanceSquared >= radiusSquared || !visited.Add(offset))
                        continue;
                    result.Add(new InfluenceOffset(offset, distanceSquared,
                        AirHumidityKernel.GetInfluence(humiditySettings.WaterContribution,
                            distanceSquared, humiditySettings)));
                }
                // 先查近邻，已找到的强供湿会跳过无法再提高结果的远源。
                result.Sort((left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
                return result.ToArray();
            }

            private Int2 Normalize(int worldX, int worldY)
                => new(request.Topology.NormalizeX(worldX), request.Topology.NormalizeY(worldY));
        }

        #endregion
    }
}
