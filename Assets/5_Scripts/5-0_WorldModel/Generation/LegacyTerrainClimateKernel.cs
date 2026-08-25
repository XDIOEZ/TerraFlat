using System;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 旧版 ChunkGenerator_Land 的单点气候结果。
    /// 高度、基础温度和基础降水来自旧版三通道噪声；温度已叠加海拔降温，最终降水已叠加迎风增雨与背风雨影。
    /// 风向始终是单位向量，可直接写入区块环境层。
    /// </summary>
    internal readonly struct LegacyClimateSample
    {
        public LegacyClimateSample(double height, double temperature, double temperatureCelsius,
            double basePrecipitation, double precipitation, double windX, double windY)
        {
            Height = height;
            Temperature = temperature;
            TemperatureCelsius = temperatureCelsius;
            BasePrecipitation = basePrecipitation;
            Precipitation = precipitation;
            WindX = windX;
            WindY = windY;
        }

        public double Height { get; }
        public double Temperature { get; }
        public double TemperatureCelsius { get; }
        public double BasePrecipitation { get; }
        public double Precipitation { get; }
        public double WindX { get; }
        public double WindY { get; }
    }

    /// <summary>
    /// 不引用 Unity 的旧版地形与气候采样核。
    /// 它逐项迁移 TerrainNoiseKernel 的经典 Perlin、周期 Perlin、高度二次强化、
    /// RegionalRandomWindFieldProvider 和 ApplyOrographicPrecipitation，供后台区块线程安全调用。
    /// </summary>
    internal static class LegacyTerrainClimateKernel
    {
        private const float DefaultChannelValue = 0.5f;
        private const int HeightChannelId = 0;
        private const int PrecipitationChannelId = 2;
        private const int TemperatureChannelId = 3;

        #region 公共采样入口

        /// <summary>采样旧版高度通道，并应用旧版高度二次强化。</summary>
        internal static double SampleHeight(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            NormalizeWorldCell(request, ref worldX, ref worldY);
            return SampleHeightAt(request, settings, worldX, worldY);
        }

        /// <summary>采样旧版基础降水，再按区域风向和逆风地形计算最终降水。</summary>
        internal static double SamplePrecipitation(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            return SampleClimate(request, settings, worldX, worldY).Precipitation;
        }

        /// <summary>一次返回地表格需要的高度、基础/地形降水和风向。</summary>
        internal static LegacyClimateSample SampleClimate(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            NormalizeWorldCell(request, ref worldX, ref worldY);
            float height = SampleHeightAt(request, settings, worldX, worldY);
            float baseTemperature = SampleChannel(request, settings,
                settings.TemperatureNoise,
                TemperatureChannelId, worldX, worldY);
            float temperature = (float)settings.ApplyAltitudeTemperatureCooling(
                height, baseTemperature);
            double temperatureCelsius = Lerp(
                settings.TemperatureCelsiusMin,
                settings.TemperatureCelsiusMax,
                temperature);
            float basePrecipitation = SampleChannel(request, settings,
                settings.PrecipitationNoise,
                PrecipitationChannelId, worldX, worldY);
            SampleWind(request, settings, worldX, worldY, out float windX, out float windY);

            int sampleCount = settings.OrographicSampleCount;
            float sampleDistance = (float)settings.OrographicSampleDistance;
            float meanUpwindHeight = 0f;
            float maxUpwindHeight = 0f;
            for (int sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
            {
                float distance = sampleDistance * sampleIndex / sampleCount;
                float upwindX = worldX - windX * distance;
                float upwindY = worldY - windY * distance;
                float upwindHeight = SampleHeightAt(request, settings, upwindX, upwindY);
                meanUpwindHeight += upwindHeight;
                if (upwindHeight > maxUpwindHeight)
                    maxUpwindHeight = upwindHeight;
            }

            meanUpwindHeight /= sampleCount;
            float precipitation = ApplyOrographicPrecipitation(
                basePrecipitation,
                height,
                meanUpwindHeight,
                maxUpwindHeight,
                (float)settings.WindwardRainGain,
                (float)settings.LeewardRainLoss);
            return new LegacyClimateSample(height, temperature, temperatureCelsius,
                basePrecipitation, precipitation, windX, windY);
        }

        #endregion

        #region 地形与降水噪声

        /// <summary>采样任意浮点世界坐标的高度，供逆风坡度检查复用。</summary>
        private static float SampleHeightAt(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, float worldX, float worldY)
        {
            float height = SampleChannel(request, settings, settings.HeightNoise, HeightChannelId,
                worldX, worldY);
            return ApplyHeightBoost(height, settings.HeightSecondaryBoostEnabled,
                (float)settings.HeightSecondaryBoostStrength);
        }

        /// <summary>迁移旧 TerrainNoiseKernel.SampleBurst，包括有限世界周期采样。</summary>
        private static float SampleChannel(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, TerrainNoiseChannelSettings config,
            int channelId, float worldX, float worldY)
        {
            float coordinateScale = (float)config.CoordinateScale;
            float baseFrequency = (float)config.Frequency;
            float lacunarity = (float)config.Lacunarity;
            float persistence = (float)config.Persistence;
            float noiseScale = (float)settings.WorldCoordinateScale;
            GetSeedOffset(request.WorldSeed, channelId, out float seedOffsetX,
                out float seedOffsetY);
            float offsetX = (float)config.OffsetX + seedOffsetX;
            float offsetY = (float)config.OffsetY + seedOffsetY;

            float sum = 0f;
            float amplitudeSum = 0f;
            float amplitude = 1f;
            float octaveFrequency = baseFrequency;
            if (!request.Topology.IsWrapped)
            {
                float sampleBaseX = worldX * noiseScale * coordinateScale + offsetX;
                float sampleBaseY = worldY * noiseScale * coordinateScale + offsetY;
                for (int octave = 0; octave < config.Octaves; octave++)
                {
                    float value = ClassicNoise(
                        sampleBaseX * octaveFrequency,
                        sampleBaseY * octaveFrequency);
                    sum += Clamp01(value * 0.5f + 0.5f) * amplitude;
                    amplitudeSum += amplitude;
                    amplitude *= persistence;
                    octaveFrequency *= lacunarity;
                }
            }
            else
            {
                float spanX = request.Topology.Span.X;
                float spanY = request.Topology.Span.Y;
                float relativeX = worldX - request.Topology.Min.X;
                float relativeY = worldY - request.Topology.Min.Y;
                for (int octave = 0; octave < config.Octaves; octave++)
                {
                    float repeatX = Math.Max(1f, Round(spanX * noiseScale * coordinateScale *
                        octaveFrequency));
                    float repeatY = Math.Max(1f, Round(spanY * noiseScale * coordinateScale *
                        octaveFrequency));
                    float phaseX = PositivePhase(offsetX * octaveFrequency, repeatX);
                    float phaseY = PositivePhase(offsetY * octaveFrequency, repeatY);
                    float sampleX = relativeX / spanX * repeatX + phaseX;
                    float sampleY = relativeY / spanY * repeatY + phaseY;
                    float value = PeriodicClassicNoise(sampleX, sampleY, repeatX, repeatY);
                    sum += Clamp01(value * 0.5f + 0.5f) * amplitude;
                    amplitudeSum += amplitude;
                    amplitude *= persistence;
                    octaveFrequency *= lacunarity;
                }
            }

            return !IsFinite(sum) || !IsFinite(amplitudeSum) || amplitudeSum <= 0.000001f
                ? DefaultChannelValue
                : Clamp01(sum / amplitudeSum);
        }

        /// <summary>应用旧版中值两侧平方强化，使高地更高、低地更低。</summary>
        private static float ApplyHeightBoost(float height, bool enabled, float strength)
        {
            float safeHeight = Clamp01(IsFinite(height) ? height : DefaultChannelValue);
            if (!enabled)
                return safeHeight;
            float delta = safeHeight - 0.5f;
            float direction = delta < 0f ? -1f : delta > 0f ? 1f : 0f;
            return Clamp01(safeHeight + direction * delta * delta * 4f * Math.Max(0f, strength));
        }

        /// <summary>按世界种子和通道编号生成旧版固定噪声偏移。</summary>
        private static void GetSeedOffset(int worldSeed, int channelId,
            out float offsetX, out float offsetY)
        {
            uint state = unchecked((uint)(worldSeed == 0 ? 1 : worldSeed));
            state ^= unchecked((uint)(channelId + 1) * 0x9E3779B9u);
            uint x = Mix(state ^ 0xA341316Cu);
            uint y = Mix(state ^ 0xC8013EA4u);
            offsetX = ((x & 0xFFFFu) / 65535f - 0.5f) * 8192f;
            offsetY = ((y & 0xFFFFu) / 65535f - 0.5f) * 8192f;
        }

        #endregion

        #region 区域风场与地形降雨

        /// <summary>迁移旧 RegionalRandomWindFieldProvider 的区域方向平滑插值。</summary>
        private static void SampleWind(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, float worldX, float worldY,
            out float windX, out float windY)
        {
            float regionSize = (float)settings.WindRegionSize;
            float gridX;
            float gridY;
            int repeatX = 0;
            int repeatY = 0;
            if (request.Topology.IsWrapped)
            {
                repeatX = Math.Max(1, (int)Round(request.Topology.Span.X / regionSize));
                repeatY = Math.Max(1, (int)Round(request.Topology.Span.Y / regionSize));
                gridX = (worldX - request.Topology.Min.X) / request.Topology.Span.X * repeatX;
                gridY = (worldY - request.Topology.Min.Y) / request.Topology.Span.Y * repeatY;
            }
            else
            {
                gridX = worldX / regionSize;
                gridY = worldY / regionSize;
            }

            int cellX = (int)Math.Floor(gridX);
            int cellY = (int)Math.Floor(gridY);
            float tX = Smooth(Frac(gridX));
            float tY = Smooth(Frac(gridY));
            DirectionAt(Canonical(cellX, repeatX), Canonical(cellY, repeatY), request.WorldSeed,
                settings.WindSeedSalt, out float x00, out float y00);
            DirectionAt(Canonical(cellX + 1, repeatX), Canonical(cellY, repeatY),
                request.WorldSeed, settings.WindSeedSalt, out float x10, out float y10);
            DirectionAt(Canonical(cellX, repeatX), Canonical(cellY + 1, repeatY),
                request.WorldSeed, settings.WindSeedSalt, out float x01, out float y01);
            DirectionAt(Canonical(cellX + 1, repeatX), Canonical(cellY + 1, repeatY),
                request.WorldSeed, settings.WindSeedSalt, out float x11, out float y11);

            float bottomX = Lerp(x00, x10, tX);
            float bottomY = Lerp(y00, y10, tX);
            float topX = Lerp(x01, x11, tX);
            float topY = Lerp(y01, y11, tX);
            windX = Lerp(bottomX, topX, tY);
            windY = Lerp(bottomY, topY, tY);
            float lengthSquared = windX * windX + windY * windY;
            if (!IsFinite(windX) || !IsFinite(windY) || lengthSquared <= 0.000001f)
            {
                DirectionAt(Canonical(cellX, repeatX), Canonical(cellY, repeatY),
                    request.WorldSeed, settings.WindSeedSalt, out windX, out windY);
                return;
            }

            float inverseLength = 1f / (float)Math.Sqrt(lengthSquared);
            windX *= inverseLength;
            windY *= inverseLength;
        }

        /// <summary>迁移旧 ApplyOrographicPrecipitation 的迎风抬升和背风雨影公式。</summary>
        private static float ApplyOrographicPrecipitation(float basePrecipitation,
            float currentHeight, float meanUpwindHeight, float maxUpwindHeight,
            float windwardGain, float leewardLoss)
        {
            float safeBase = Clamp01(IsFinite(basePrecipitation)
                ? basePrecipitation
                : DefaultChannelValue);
            float safeHeight = Clamp01(IsFinite(currentHeight)
                ? currentHeight
                : DefaultChannelValue);
            float safeMean = Clamp01(IsFinite(meanUpwindHeight)
                ? meanUpwindHeight
                : safeHeight);
            float safeMaximum = Clamp01(IsFinite(maxUpwindHeight)
                ? maxUpwindHeight
                : safeHeight);
            float uplift = Math.Max(0f, safeHeight - safeMean);
            float rainShadow = Math.Max(0f, safeMaximum - safeHeight);
            return Clamp01(safeBase + uplift * Math.Max(0f, windwardGain) -
                           rainShadow * Math.Max(0f, leewardLoss));
        }

        /// <summary>为环绕世界把风区索引规范到有限范围；无限世界保持原值。</summary>
        private static int Canonical(int cell, int repeat)
        {
            if (repeat <= 0)
                return cell;
            int result = cell % repeat;
            return result < 0 ? result + repeat : result;
        }

        /// <summary>按风区坐标、世界种子和旧版盐值生成一个单位方向。</summary>
        private static void DirectionAt(int regionX, int regionY, int worldSeed, int seedSalt,
            out float directionX, out float directionY)
        {
            uint hash = unchecked((uint)(worldSeed == 0 ? 1 : worldSeed));
            hash = Mix(hash ^ unchecked((uint)seedSalt));
            hash = Mix(hash ^ unchecked((uint)regionX * 0x9E3779B9u));
            hash = Mix(hash ^ unchecked((uint)regionY * 0x85EBCA6Bu));
            float angle = (hash & 0x00FFFFFFu) / 16777216f * (float)Math.PI * 2f;
            directionX = (float)Math.Cos(angle);
            directionY = (float)Math.Sin(angle);
        }

        #endregion

        #region 经典 Perlin 移植

        /// <summary>Unity.Mathematics noise.cnoise(float2) 的无 Unity 等价实现。</summary>
        private static float ClassicNoise(float x, float y)
        {
            return ClassicNoiseCore(x, y, 0f, 0f, false);
        }

        /// <summary>Unity.Mathematics noise.pnoise(float2,float2) 的无 Unity 等价实现。</summary>
        private static float PeriodicClassicNoise(float x, float y, float repeatX, float repeatY)
        {
            return ClassicNoiseCore(x, y, repeatX, repeatY, true);
        }

        /// <summary>计算普通或周期二维经典 Perlin，标量顺序与 Unity.Mathematics 1.3.2 保持一致。</summary>
        private static float ClassicNoiseCore(float x, float y, float repeatX, float repeatY,
            bool periodic)
        {
            float ix0 = Floor(x);
            float iy0 = Floor(y);
            float ix1 = ix0 + 1f;
            float iy1 = iy0 + 1f;
            float fx0 = Frac(x);
            float fy0 = Frac(y);
            float fx1 = fx0 - 1f;
            float fy1 = fy0 - 1f;
            if (periodic)
            {
                ix0 %= repeatX;
                ix1 %= repeatX;
                iy0 %= repeatY;
                iy1 %= repeatY;
            }

            ix0 = Mod289(ix0);
            ix1 = Mod289(ix1);
            iy0 = Mod289(iy0);
            iy1 = Mod289(iy1);
            float i00 = Permute(Permute(ix0) + iy0);
            float i10 = Permute(Permute(ix1) + iy0);
            float i01 = Permute(Permute(ix0) + iy1);
            float i11 = Permute(Permute(ix1) + iy1);

            Gradient(i00, out float gx00, out float gy00);
            Gradient(i10, out float gx10, out float gy10);
            Gradient(i01, out float gx01, out float gy01);
            Gradient(i11, out float gx11, out float gy11);
            NormalizeGradient(ref gx00, ref gy00);
            NormalizeGradient(ref gx01, ref gy01);
            NormalizeGradient(ref gx10, ref gy10);
            NormalizeGradient(ref gx11, ref gy11);

            float n00 = gx00 * fx0 + gy00 * fy0;
            float n10 = gx10 * fx1 + gy10 * fy0;
            float n01 = gx01 * fx0 + gy01 * fy1;
            float n11 = gx11 * fx1 + gy11 * fy1;
            float fadeX = Fade(fx0);
            float fadeY = Fade(fy0);
            float bottom = Lerp(n00, n10, fadeX);
            float top = Lerp(n01, n11, fadeX);
            return 2.3f * Lerp(bottom, top, fadeY);
        }

        /// <summary>从旧版置换值得到梯度方向。</summary>
        private static void Gradient(float permutation, out float x, out float y)
        {
            x = Frac(permutation * (1f / 41f)) * 2f - 1f;
            y = Math.Abs(x) - 0.5f;
            x -= Floor(x + 0.5f);
        }

        /// <summary>使用 Unity.Mathematics 相同的 Taylor 近似归一化梯度。</summary>
        private static void NormalizeGradient(ref float x, ref float y)
        {
            float inverseLength = 1.79284291400159f -
                                  0.85373472095314f * (x * x + y * y);
            x *= inverseLength;
            y *= inverseLength;
        }

        private static float Mod289(float value) =>
            value - Floor(value * (1f / 289f)) * 289f;

        private static float Permute(float value) => Mod289((34f * value + 1f) * value);
        private static float Fade(float value) => value * value * value *
            (value * (value * 6f - 15f) + 10f);

        #endregion

        #region 数值辅助

        private static void NormalizeWorldCell(ChunkGenerationRequest request,
            ref int worldX, ref int worldY)
        {
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
        }

        private static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return value;
            }
        }

        private static float PositivePhase(float value, float period) =>
            value - Floor(value / period) * period;

        private static float Floor(float value) => (float)Math.Floor(value);
        private static float Round(float value) => (float)Math.Round(value);
        private static float Frac(float value) => value - Floor(value);
        private static float Smooth(float value) => value * value * (3f - 2f * value);
        private static float Lerp(float left, float right, float t) => left + (right - left) * t;
        private static double Lerp(double left, double right, double t) =>
            left + (right - left) * t;
        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
