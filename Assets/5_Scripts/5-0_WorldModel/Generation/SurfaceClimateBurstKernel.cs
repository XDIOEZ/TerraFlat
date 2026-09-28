using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    /// <summary>后台线程直接调用 Burst 批量数学核；Unity Job 调度仍只允许在主线程。</summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
    public static unsafe class SurfaceClimateBurstKernel
    {
        #region 连续批次输入输出
        [StructLayout(LayoutKind.Sequential)]
        public struct ClimateSample
        {
            public float Height, Temperature, TemperatureCelsius;
            public float BasePrecipitation, Precipitation, WindX, WindY;
        }

        public struct NoiseChannel
        {
            public float CoordinateScale, Frequency, Lacunarity, Persistence, OffsetX, OffsetY;
            public int Octaves;
        }

        public struct Parameters
        {
            public int Seed, OriginX, OriginY, Width, Height;
            public int Wrapped, MinX, MinY, SpanX, SpanY;
            public float WorldCoordinateScale;
            public NoiseChannel HeightNoise, TemperatureNoise, PrecipitationNoise;
            public float HeightBoost, CoolingStart, CoolingStrength, CelsiusMin, CelsiusMax;
            public float WindRegionSize, OrographicDistance, WindwardGain, LeewardLoss;
            public int WindSeedSalt, OrographicSamples;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void SampleDelegate(Parameters* parameters, ClimateSample* output);
        private static readonly Lazy<SampleDelegate> Compiled = new(() =>
            BurstCompiler.CompileFunctionPointer<SampleDelegate>(SampleBatch).Invoke);
        private static readonly bool RunningInUnity =
            Type.GetType("UnityEngine.Application, UnityEngine.CoreModule") != null;

        internal static ClimateSample[] RentAndSample(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings)
        {
            int count = checked(request.Profile.Width * request.Profile.Height);
            ClimateSample[] output = ArrayPool<ClimateSample>.Shared.Rent(count);
            Parameters parameters = new()
            {
                Seed = request.WorldSeed == 0 ? 1 : request.WorldSeed,
                OriginX = request.Address.ChunkOrigin.X,
                OriginY = request.Address.ChunkOrigin.Y,
                Width = request.Profile.Width,
                Height = request.Profile.Height,
                Wrapped = request.Topology.IsWrapped ? 1 : 0,
                MinX = request.Topology.Min.X,
                MinY = request.Topology.Min.Y,
                SpanX = request.Topology.Span.X,
                SpanY = request.Topology.Span.Y,
                WorldCoordinateScale = (float)settings.WorldCoordinateScale,
                HeightNoise = Freeze(settings.HeightNoise, request.WorldSeed, 0),
                TemperatureNoise = Freeze(settings.TemperatureNoise, request.WorldSeed, 3),
                PrecipitationNoise = Freeze(settings.PrecipitationNoise, request.WorldSeed, 2),
                HeightBoost = settings.HeightSecondaryBoostEnabled
                    ? (float)settings.HeightSecondaryBoostStrength : 0f,
                CoolingStart = (float)settings.TemperatureAltitudeCoolingStart,
                CoolingStrength = (float)settings.TemperatureAltitudeCoolingStrength,
                CelsiusMin = (float)settings.TemperatureCelsiusMin,
                CelsiusMax = (float)settings.TemperatureCelsiusMax,
                WindRegionSize = (float)settings.WindRegionSize,
                WindSeedSalt = settings.WindSeedSalt,
                OrographicSamples = settings.OrographicSampleCount,
                OrographicDistance = (float)settings.OrographicSampleDistance,
                WindwardGain = (float)settings.WindwardRainGain,
                LeewardLoss = (float)settings.LeewardRainLoss
            };
            try
            {
                fixed (ClimateSample* destination = output)
                    (RunningInUnity ? Compiled.Value : SampleBatch)(&parameters, destination);
                return output;
            }
            catch
            {
                ArrayPool<ClimateSample>.Shared.Return(output);
                throw;
            }
        }

        internal static void Return(ClimateSample[] output) =>
            ArrayPool<ClimateSample>.Shared.Return(output);

        private static NoiseChannel Freeze(TerrainNoiseChannelSettings source, int worldSeed, int id)
        {
            uint state = unchecked((uint)(worldSeed == 0 ? 1 : worldSeed));
            state ^= unchecked((uint)(id + 1) * 0x9E3779B9u);
            uint x = Mix(state ^ 0xA341316Cu);
            uint y = Mix(state ^ 0xC8013EA4u);
            return new NoiseChannel
            {
                CoordinateScale = (float)source.CoordinateScale,
                Frequency = (float)source.Frequency,
                Lacunarity = (float)source.Lacunarity,
                Persistence = (float)source.Persistence,
                OffsetX = (float)source.OffsetX + ((x & 0xFFFFu) / 65535f - 0.5f) * 8192f,
                OffsetY = (float)source.OffsetY + ((y & 0xFFFFu) / 65535f - 0.5f) * 8192f,
                Octaves = source.Octaves
            };
        }
        #endregion

        #region Burst 气候采样
        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High,
            CompileSynchronously = true, DisableDirectCall = true)]
        public static void SampleBatch(Parameters* parameters, ClimateSample* output)
        {
            Parameters p = *parameters;
            for (int y = 0; y < p.Height; y++)
            for (int x = 0; x < p.Width; x++)
            {
                int worldX = p.OriginX + x;
                int worldY = p.OriginY + y;
                if (p.Wrapped != 0)
                {
                    worldX = Normalize(worldX, p.MinX, p.SpanX);
                    worldY = Normalize(worldY, p.MinY, p.SpanY);
                }
                float height = SampleHeight(p, worldX, worldY);
                float baseTemperature = SampleChannel(p, p.TemperatureNoise, worldX, worldY);
                float temperature = math.saturate(baseTemperature -
                    math.max(0f, math.saturate(height) - p.CoolingStart) * p.CoolingStrength);
                float basePrecipitation = SampleChannel(p, p.PrecipitationNoise, worldX, worldY);
                SampleWind(p, worldX, worldY, out float windX, out float windY);
                float mean = 0f, maximum = 0f;
                for (int i = 1; i <= p.OrographicSamples; i++)
                {
                    float distance = p.OrographicDistance * i / p.OrographicSamples;
                    float upwind = SampleHeight(p, worldX - windX * distance,
                        worldY - windY * distance);
                    mean += upwind;
                    maximum = math.max(maximum, upwind);
                }
                mean /= p.OrographicSamples;
                float precipitation = math.saturate(basePrecipitation +
                    math.max(0f, height - mean) * math.max(0f, p.WindwardGain) -
                    math.max(0f, maximum - height) * math.max(0f, p.LeewardLoss));
                output[y * p.Width + x] = new ClimateSample
                {
                    Height = height,
                    Temperature = temperature,
                    TemperatureCelsius = p.CelsiusMin + (p.CelsiusMax - p.CelsiusMin) * temperature,
                    BasePrecipitation = basePrecipitation,
                    Precipitation = precipitation,
                    WindX = windX,
                    WindY = windY
                };
            }
        }

        private static float SampleHeight(Parameters p, float x, float y)
        {
            float height = SampleChannel(p, p.HeightNoise, x, y);
            float delta = height - 0.5f;
            return math.saturate(height + math.sign(delta) * delta * delta * 4f * p.HeightBoost);
        }

        private static float SampleChannel(Parameters p, NoiseChannel channel, float x, float y)
        {
            float sum = 0f, amplitudeSum = 0f, amplitude = 1f;
            float frequency = channel.Frequency;
            float scale = p.WorldCoordinateScale * channel.CoordinateScale;
            float baseX = x * scale + channel.OffsetX;
            float baseY = y * scale + channel.OffsetY;
            for (int octave = 0; octave < channel.Octaves; octave++)
            {
                float value;
                if (p.Wrapped == 0)
                    value = noise.cnoise(new float2(baseX * frequency, baseY * frequency));
                else
                {
                    float repeatX = math.max(1f, math.round(p.SpanX * scale * frequency));
                    float repeatY = math.max(1f, math.round(p.SpanY * scale * frequency));
                    float phaseX = PositivePhase(channel.OffsetX * frequency, repeatX);
                    float phaseY = PositivePhase(channel.OffsetY * frequency, repeatY);
                    float sampleX = (x - p.MinX) / p.SpanX * repeatX + phaseX;
                    float sampleY = (y - p.MinY) / p.SpanY * repeatY + phaseY;
                    value = noise.pnoise(new float2(sampleX, sampleY), new float2(repeatX, repeatY));
                }
                sum += math.saturate(value * 0.5f + 0.5f) * amplitude;
                amplitudeSum += amplitude;
                amplitude *= channel.Persistence;
                frequency *= channel.Lacunarity;
            }
            return amplitudeSum > 0.000001f ? math.saturate(sum / amplitudeSum) : 0.5f;
        }

        private static void SampleWind(Parameters p, float x, float y,
            out float windX, out float windY)
        {
            int repeatX = 0, repeatY = 0;
            float gridX, gridY;
            if (p.Wrapped != 0)
            {
                repeatX = math.max(1, (int)math.round(p.SpanX / p.WindRegionSize));
                repeatY = math.max(1, (int)math.round(p.SpanY / p.WindRegionSize));
                gridX = (x - p.MinX) / p.SpanX * repeatX;
                gridY = (y - p.MinY) / p.SpanY * repeatY;
            }
            else
            {
                gridX = x / p.WindRegionSize;
                gridY = y / p.WindRegionSize;
            }
            int cellX = (int)math.floor(gridX), cellY = (int)math.floor(gridY);
            float tx = Smooth(math.frac(gridX)), ty = Smooth(math.frac(gridY));
            DirectionAt(Canonical(cellX, repeatX), Canonical(cellY, repeatY), p,
                out float x00, out float y00);
            DirectionAt(Canonical(cellX + 1, repeatX), Canonical(cellY, repeatY), p,
                out float x10, out float y10);
            DirectionAt(Canonical(cellX, repeatX), Canonical(cellY + 1, repeatY), p,
                out float x01, out float y01);
            DirectionAt(Canonical(cellX + 1, repeatX), Canonical(cellY + 1, repeatY), p,
                out float x11, out float y11);
            windX = Lerp(Lerp(x00, x10, tx), Lerp(x01, x11, tx), ty);
            windY = Lerp(Lerp(y00, y10, tx), Lerp(y01, y11, tx), ty);
            float lengthSquared = windX * windX + windY * windY;
            if (lengthSquared <= 0.000001f || !math.isfinite(lengthSquared))
            {
                DirectionAt(Canonical(cellX, repeatX), Canonical(cellY, repeatY), p,
                    out windX, out windY);
                return;
            }
            float inverse = math.rsqrt(lengthSquared);
            windX *= inverse;
            windY *= inverse;
        }

        private static void DirectionAt(int x, int y, Parameters p, out float dx, out float dy)
        {
            uint hash = unchecked((uint)p.Seed);
            hash = Mix(hash ^ unchecked((uint)p.WindSeedSalt));
            hash = Mix(hash ^ unchecked((uint)x * 0x9E3779B9u));
            hash = Mix(hash ^ unchecked((uint)y * 0x85EBCA6Bu));
            float angle = (hash & 0x00FFFFFFu) / 16777216f * math.PI * 2f;
            dx = math.cos(angle);
            dy = math.sin(angle);
        }

        private static uint Mix(uint value)
        {
            value ^= value >> 16; value *= 0x7FEB352Du;
            value ^= value >> 15; value *= 0x846CA68Bu;
            value ^= value >> 16; return value;
        }
        private static int Normalize(int value, int minimum, int span)
        {
            int offset = (value - minimum) % span;
            return minimum + (offset < 0 ? offset + span : offset);
        }
        private static int Canonical(int value, int repeat)
        {
            if (repeat <= 0) return value;
            int result = value % repeat;
            return result < 0 ? result + repeat : result;
        }
        private static float PositivePhase(float value, float period)
            => value - math.floor(value / period) * period;
        private static float Smooth(float value) => value * value * (3f - 2f * value);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        #endregion
    }
}
