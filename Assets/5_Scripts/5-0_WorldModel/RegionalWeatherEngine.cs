using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    /// <summary>固定时间推进独立天气效果，查询只读取快照，不生成地形或执行表现。</summary>
    public sealed class RegionalWeatherEngine
    {
        #region 冻结配置与区域真值
        private readonly RegionalWeatherSettings settings;
        private readonly int worldSeed;
        private readonly WorldTopologyDomain domain;
        private readonly WorldTopologyDomain regionDomain;
        private readonly double regionWidth;
        private readonly double regionHeight;
        private readonly int columns;
        private readonly int rows;
        private readonly Dictionary<Int2, RegionalWeatherState> states = new();
        private readonly Dictionary<Int2, RegionalWeatherSnapshot> replicas = new();
        private bool replicaMode;

        public RegionalWeatherEngine(RegionalWeatherSettings settings, int worldSeed, WorldTopologyDomain domain)
        {
            this.settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Snapshot();
            this.worldSeed = worldSeed;
            this.domain = domain;
            if (domain.IsWrapped)
            {
                if (domain.Span.x <= 0 || domain.Span.y <= 0)
                    throw new ArgumentOutOfRangeException(nameof(domain));
                columns = Math.Max(1, (int)Math.Ceiling(domain.Span.x / (double)this.settings.RegionSizeTiles));
                rows = Math.Max(1, (int)Math.Ceiling(domain.Span.y / (double)this.settings.RegionSizeTiles));
                regionWidth = domain.Span.x / (double)columns;
                regionHeight = domain.Span.y / (double)rows;
                regionDomain = new WorldTopologyDomain(int2.zero, new int2(columns, rows), true);
            }
            else
            {
                regionWidth = regionHeight = this.settings.RegionSizeTiles;
                regionDomain = default;
            }
        }
        #endregion

        #region 固定区域与采样坐标
        public Int2 ResolveRegion(Int2 cell)
        {
            int2 normalized = domain.Normalize(new int2(cell.X, cell.Y));
            if (domain.IsWrapped)
                return new Int2(
                    Math.Min(columns - 1, (int)Math.Floor((normalized.x - (double)domain.Min.x) / regionWidth)),
                    Math.Min(rows - 1, (int)Math.Floor((normalized.y - (double)domain.Min.y) / regionHeight)));
            return new Int2((int)Math.Floor(normalized.x / regionWidth),
                (int)Math.Floor(normalized.y / regionHeight));
        }

        /// <summary>中心与四个象限的固定点，不随加载窗口改变采样集合。</summary>
        public Int2 GetFixedSampleCell(Int2 region, int index)
        {
            if ((uint)index >= 5u)
                throw new ArgumentOutOfRangeException(nameof(index));
            region = NormalizeRegion(region);
            double x = index == 0 ? 0.5d : (index == 2 || index == 4 ? 0.75d : 0.25d);
            double y = index == 0 ? 0.5d : (index >= 3 ? 0.75d : 0.25d);
            int minX = domain.IsWrapped ? domain.Min.x : 0;
            int minY = domain.IsWrapped ? domain.Min.y : 0;
            int2 cell = domain.Normalize(new int2(
                (int)Math.Floor(minX + (region.X + x) * regionWidth),
                (int)Math.Floor(minY + (region.Y + y) * regionHeight)));
            return new Int2(cell.x, cell.y);
        }

        private Int2 NormalizeRegion(Int2 region)
        {
            int2 normalized = regionDomain.Normalize(new int2(region.X, region.Y));
            return new Int2(normalized.x, normalized.y);
        }
        #endregion

        #region 固定推进与有界补算
        public RegionalWeatherAdvanceResult Advance(Int2 region, double now, RegionalWeatherInput input)
        {
            RequireAuthority();
            RequireTime(now);
            region = NormalizeRegion(region);
            RegionalWeatherState state = GetOrCreateState(region, now);
            if (now < state.LastStepTime)
                return new RegionalWeatherAdvanceResult(BuildSnapshot(state));

            double step = settings.StepSeconds;
            double wholeSteps = Math.Floor((now - state.LastStepTime) / step);
            if (wholeSteps > settings.MaximumCatchUpSteps)
            {
                double skippedEnd = state.LastStepTime + (wholeSteps - settings.MaximumCatchUpSteps) * step;
                // 旧时间只补已有降水的环境反馈，不追造随机天气、闪电或地表水量。
                AdvanceExistingPrecipitation(state, skippedEnd - state.LastStepTime, out _, out _);
                state.LastStepTime = skippedEnd;
                state.CheckIndex = GetCheckIndex(skippedEnd);
                state.NextLightningTime = 0d;
                IncrementRevision(state);
                wholeSteps = settings.MaximumCatchUpSteps;
            }

            bool complete = TryNormalizeInput(input, out RegionalWeatherInput normalizedInput);
            if (complete)
                UpdateInput(state, normalizedInput);
            int count = (int)wholeSteps;
            float rainExposure = 0f, snowExposure = 0f;
            bool crossedCheck = false;
            for (int i = 0; i < count; i++)
            {
                AdvanceExistingPrecipitation(state, step, out float rain, out float snow);
                rainExposure += rain;
                snowExposure += snow;
                state.LastStepTime += step;
                long checkIndex = GetCheckIndex(state.LastStepTime);
                bool latest = i == count - 1;
                if (checkIndex > state.CheckIndex)
                {
                    state.CheckIndex = checkIndex;
                    crossedCheck = true;
                }
                if (complete && latest && crossedCheck)
                    TryBeginPrecipitation(state);
                UpdateIndependentEffects(state, (float)step, complete && latest);
                IncrementRevision(state);
            }
            return new RegionalWeatherAdvanceResult(BuildSnapshot(state), rainExposure, snowExposure);
        }

        /// <summary>GM 回调绝对时刻时平移天气时限，保留已消耗湿气、雨冷与事件身份。</summary>
        public void RebaseTime(double deltaSeconds)
        {
            RequireAuthority();
            if (!Finite(deltaSeconds) || Math.Abs(deltaSeconds) > 1e15d)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (deltaSeconds == 0d)
                return;
            foreach (RegionalWeatherState state in states.Values)
            {
                state.LastStepTime = ShiftTime(state.LastStepTime, deltaSeconds);
                state.PrecipitationStartedTime = ShiftTime(state.PrecipitationStartedTime, deltaSeconds);
                state.PrecipitationEndTime = ShiftTime(state.PrecipitationEndTime, deltaSeconds);
                state.CooldownEndTime = ShiftTime(state.CooldownEndTime, deltaSeconds);
                if (state.NextLightningTime > 0d)
                    state.NextLightningTime = ShiftTime(state.NextLightningTime, deltaSeconds);
                if (state.LastLightningTime >= 0d)
                    state.LastLightningTime = ShiftTime(state.LastLightningTime, deltaSeconds);
                state.ForcedElementsEndTime = ShiftTime(state.ForcedElementsEndTime, deltaSeconds);
                state.CheckIndex = GetCheckIndex(state.LastStepTime);
                IncrementRevision(state);
            }
        }

        private static double ShiftTime(double value, double deltaSeconds)
            => Math.Max(0d, Math.Min(1e15d, value + deltaSeconds));

        private RegionalWeatherState GetOrCreateState(Int2 region, double now)
        {
            if (states.TryGetValue(region, out RegionalWeatherState state))
                return state;
            double initialTime = Math.Floor(now / settings.StepSeconds) * settings.StepSeconds;
            state = new RegionalWeatherState
            {
                Region = region,
                LastStepTime = initialTime,
                CheckIndex = GetCheckIndex(initialTime),
                Revision = 1
            };
            states.Add(region, state);
            return state;
        }

        private void UpdateInput(RegionalWeatherState state, RegionalWeatherInput input)
        {
            bool first = !state.HasValidInput;
            if (!first && SameInput(state.LastValidInput, input))
                return;
            state.LastValidInput = input;
            state.HasValidInput = true;
            if (first)
            {
                float length = Length(input.GeographicWindX, input.GeographicWindY);
                if (length > 0.00001f)
                {
                    state.WindX = input.GeographicWindX / length * settings.GeographicWindStrength;
                    state.WindY = input.GeographicWindY / length * settings.GeographicWindStrength;
                }
                state.CloudCoverage = CloudTarget(GetHumidity(state), state.PrecipitationIntensity);
                ApplyForcedElements(state);
            }
            IncrementRevision(state);
        }

        private void AdvanceExistingPrecipitation(RegionalWeatherState state, double elapsed,
            out float rainExposure, out float snowExposure)
        {
            rainExposure = snowExposure = 0f;
            if (elapsed <= 0d)
                return;
            double start = state.LastStepTime;
            double end = start + elapsed;
            if (state.PrecipitationIntensity <= 0f)
            {
                RecoverEnvironment(state, elapsed);
                return;
            }

            double activeStart = Math.Max(start, state.PrecipitationStartedTime);
            double activeEnd = Math.Min(end, state.PrecipitationEndTime);
            float humidity = GetHumidity(state);
            float intensity = CurrentPrecipitationIntensity(state, humidity);
            if (humidity <= settings.HumidityStop || intensity <= 0f || activeEnd <= activeStart)
            {
                EndPrecipitation(state, Math.Max(start, Math.Min(end, state.PrecipitationEndTime)));
                RecoverEnvironment(state, elapsed);
                return;
            }

            float consumption = intensity * settings.HumidityConsumptionPerSecond;
            double drySeconds = Math.Max(0d, (humidity - settings.HumidityStop) / consumption);
            double activeSeconds = Math.Max(0d, Math.Min(activeEnd - activeStart, drySeconds));
            float coolingRate = intensity * settings.CoolingPerSecond;
            double warmSeconds = activeSeconds;
            float initialTemperature = GetTemperature(state);
            if (state.ForcedSnow || initialTemperature <= settings.FreezingTemperature)
                warmSeconds = 0d;
            else if (coolingRate > 0f &&
                state.LastValidInput.TemperatureBeforeRainCooling - settings.MaximumCoolingCelsius <= settings.FreezingTemperature)
                warmSeconds = Math.Min(activeSeconds,
                    Math.Max(0d, (initialTemperature - settings.FreezingTemperature) / coolingRate));
            rainExposure = (float)(intensity * warmSeconds);
            snowExposure = (float)(intensity * (activeSeconds - warmSeconds));
            state.HumidityDeficit = (float)Math.Min(1d,
                state.HumidityDeficit + consumption * activeSeconds);
            state.CoolingAmountCelsius = (float)Math.Min(settings.MaximumCoolingCelsius,
                state.CoolingAmountCelsius + coolingRate * activeSeconds);

            double stoppedAt = activeStart + activeSeconds;
            bool exhausted = activeSeconds >= drySeconds || GetHumidity(state) <= settings.HumidityStop;
            if (exhausted || stoppedAt >= state.PrecipitationEndTime)
            {
                EndPrecipitation(state, stoppedAt);
                RecoverEnvironment(state, Math.Max(0d, end - stoppedAt));
            }
            else
                state.PrecipitationIntensity = CurrentPrecipitationIntensity(state, GetHumidity(state));
        }

        private void RecoverEnvironment(RegionalWeatherState state, double seconds)
        {
            state.HumidityDeficit = (float)Math.Max(0d,
                state.HumidityDeficit - settings.HumidityRecoveryPerSecond * seconds);
            state.CoolingAmountCelsius = (float)Math.Max(0d,
                state.CoolingAmountCelsius - settings.CoolingRecoveryPerSecond * seconds);
        }

        private void EndPrecipitation(RegionalWeatherState state, double endedAt)
        {
            state.PrecipitationIntensity = 0f;
            state.InitialPrecipitationIntensity = 0f;
            state.PrecipitationEndTime = endedAt;
            state.CooldownEndTime = Math.Max(state.CooldownEndTime, endedAt + settings.CooldownSeconds);
            state.ForcedSnow = false;
        }
        #endregion

        #region 条件降水与确定性随机
        private void TryBeginPrecipitation(RegionalWeatherState state)
        {
            if (!state.HasValidInput || state.PrecipitationIntensity > 0f ||
                state.LastStepTime < state.CooldownEndTime)
                return;
            float humidity = GetHumidity(state);
            if (humidity < settings.HumidityStart)
                return;
            float humidityFactor = 0.1f + 0.9f * Ramp(humidity, settings.HumidityStart, settings.FullRainHumidity);
            float temperatureFactor = Lerp(settings.ColdPrecipitationFactor, settings.WarmPrecipitationFactor,
                Ramp(GetTemperature(state), settings.WarmTemperatureStart, settings.WarmTemperatureFull));
            float climateWeight = Lerp(settings.MinimumClimateWeight, settings.MaximumClimateWeight,
                state.LastValidInput.GeographicPrecipitation);
            float probability = Math.Min(settings.MaximumCheckProbability,
                settings.BaseCheckProbability * humidityFactor * temperatureFactor * climateWeight);
            if (Random01(state.Region, state.CheckIndex, 1u) >= probability)
                return;

            float driver = Clamp01((0.35f + 0.65f * humidityFactor) * temperatureFactor * climateWeight /
                (settings.WarmPrecipitationFactor * settings.MaximumClimateWeight));
            float duration = Lerp(settings.MinimumDurationSeconds, settings.MaximumDurationSeconds, driver) *
                Lerp(0.8f, 1.2f, Random01(state.Region, state.CheckIndex, 2u));
            duration = Clamp(duration, settings.MinimumDurationSeconds, settings.HardMaximumDurationSeconds);
            float intensity = Lerp(settings.MinimumIntensity, settings.MaximumIntensity, driver) *
                Lerp(0.8f, 1.2f, Random01(state.Region, state.CheckIndex, 3u));
            BeginPrecipitation(state, state.LastStepTime, Math.Min(settings.MaximumIntensity, intensity), duration, false);
        }

        private void BeginPrecipitation(RegionalWeatherState state, double now, float intensity,
            float durationSeconds, bool asSnow)
        {
            state.PrecipitationStartedTime = now;
            state.PrecipitationEndTime = now + durationSeconds;
            state.InitialPrecipitationIntensity = intensity;
            state.PrecipitationIntensity = intensity;
            state.ForcedSnow = asSnow;
        }

        private float CurrentPrecipitationIntensity(RegionalWeatherState state, float humidity)
        {
            float weakening = Clamp((humidity - settings.HumidityStop) /
                (settings.HumidityStart - settings.HumidityStop), 0.15f, 1f);
            return state.InitialPrecipitationIntensity * weakening;
        }

        private float Random01(Int2 region, long index, uint salt)
        {
            unchecked
            {
                uint hash = Mix((uint)worldSeed ^ salt * 0x9E3779B9u);
                hash = Mix(hash ^ (uint)region.X * 0x85EBCA6Bu);
                hash = Mix(hash ^ (uint)region.Y * 0xC2B2AE35u);
                hash = Mix(hash ^ (uint)index);
                hash = Mix(hash ^ (uint)(index >> 32));
                return (hash >> 8) * (1f / 16777216f);
            }
        }

        private static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                return value ^ (value >> 16);
            }
        }
        #endregion

        #region 独立风、云、雾与雷电
        private void UpdateIndependentEffects(RegionalWeatherState state, float seconds, bool allowNewEvents)
        {
            ExpireForcedElements(state);
            if (!state.HasValidInput)
            {
                ApplyForcedElements(state);
                return;
            }
            RegionalWeatherInput input = state.LastValidInput;
            double wave = state.LastStepTime / settings.WindPerturbationPeriodSeconds;
            long segment = (long)Math.Floor(wave);
            float blend = (float)(wave - segment);
            blend = blend * blend * (3f - 2f * blend);
            float windX = Lerp(Random01(state.Region, segment, 11u), Random01(state.Region, segment + 1, 11u), blend) * 2f - 1f;
            float windY = Lerp(Random01(state.Region, segment, 12u), Random01(state.Region, segment + 1, 12u), blend) * 2f - 1f;
            float geographicLength = Length(input.GeographicWindX, input.GeographicWindY);
            if (geographicLength > 0.00001f)
            {
                windX = windX * settings.WindPerturbationStrength +
                    input.GeographicWindX / geographicLength * settings.GeographicWindStrength;
                windY = windY * settings.WindPerturbationStrength +
                    input.GeographicWindY / geographicLength * settings.GeographicWindStrength;
            }
            else
            {
                windX *= settings.WindPerturbationStrength;
                windY *= settings.WindPerturbationStrength;
            }
            windX += input.TemperatureGradientX * settings.TemperatureGradientWindFactor;
            windY += input.TemperatureGradientY * settings.TemperatureGradientWindFactor;
            float targetLength = Length(windX, windY);
            if (targetLength > 1f)
            {
                windX /= targetLength;
                windY /= targetLength;
            }
            state.WindX = MoveTowards(state.WindX, windX, settings.WindChangePerSecond * seconds);
            state.WindY = MoveTowards(state.WindY, windY, settings.WindChangePerSecond * seconds);
            ApplyForcedWind(state);
            float strength = Clamp01(Length(state.WindX, state.WindY));
            float humidity = GetHumidity(state);
            float temperature = GetTemperature(state);
            state.CloudCoverage = MoveTowards(state.CloudCoverage,
                CloudTarget(humidity, state.PrecipitationIntensity), settings.CloudChangePerSecond * seconds);
            float fog = Ramp(humidity, settings.FogHumidityStart, settings.FogHumidityFull) *
                (1f - Ramp(temperature, settings.FogTemperatureFull, settings.FogTemperatureStart)) *
                Clamp01(1f - strength / settings.FogWindLimit);
            state.FogDensity = MoveTowards(state.FogDensity, fog, settings.FogChangePerSecond * seconds);
            ApplyForcedElements(state);
            float lightning = Ramp(humidity, settings.LightningHumidityStart, settings.LightningHumidityFull) *
                Ramp(temperature, settings.LightningTemperatureStart, settings.LightningTemperatureFull);
            state.LightningActivity = MoveTowards(state.LightningActivity, lightning,
                settings.LightningChangePerSecond * seconds);
            if (lightning <= 0f || state.LightningActivity <= 0.0001f)
            {
                state.NextLightningTime = state.LastLightningTime < 0d ? 0d :
                    state.LastLightningTime + settings.MinimumLightningIntervalSeconds;
                return;
            }
            if (!allowNewEvents)
                return;
            float windBonus = settings.MaximumLightningWindBonus * Ramp(strength, settings.StrongWindStart, 1f);
            double frequency = state.LightningActivity / settings.BaseLightningIntervalSeconds * (1d + windBonus);
            state.NextLightningTime = state.LastLightningTime < 0d ? 0d :
                state.LastLightningTime + settings.MinimumLightningIntervalSeconds;
            if (state.LastStepTime < state.NextLightningTime)
                return;
            double probability = 1d - Math.Exp(-frequency * seconds);
            long absoluteStep = (long)Math.Floor(state.LastStepTime / settings.StepSeconds);
            uint salt = unchecked(21u ^ (uint)state.LightningSequence * 0x9E3779B9u);
            if (Random01(state.Region, absoluteStep, salt) < probability)
            {
                state.LightningSequence = state.LightningSequence == int.MaxValue ? 1 : state.LightningSequence + 1;
                state.LastLightningTime = state.LastStepTime;
                state.NextLightningTime = state.LastLightningTime + settings.MinimumLightningIntervalSeconds;
            }
        }

        private static float CloudTarget(float humidity, float precipitationIntensity)
            => Math.Max(Ramp(humidity, 0.08f, 0.65f), Clamp01(precipitationIntensity * 0.85f));

        private static void ExpireForcedElements(RegionalWeatherState state)
        {
            if (state.LastStepTime < state.ForcedElementsEndTime)
                return;
            state.ForcedCloudCoverage = state.ForcedFogDensity = state.ForcedWindStrength = -1f;
            state.ForcedElementsEndTime = 0d;
        }

        private void ApplyForcedElements(RegionalWeatherState state)
        {
            if (state.ForcedCloudCoverage >= 0f)
                state.CloudCoverage = state.ForcedCloudCoverage;
            if (state.ForcedFogDensity >= 0f)
                state.FogDensity = state.ForcedFogDensity;
            ApplyForcedWind(state);
        }

        private static void ApplyForcedWind(RegionalWeatherState state)
        {
            if (state.ForcedWindStrength < 0f)
                return;
            float length = Length(state.WindX, state.WindY);
            if (length > 0.00001f)
            {
                state.WindX = state.WindX / length * state.ForcedWindStrength;
                state.WindY = state.WindY / length * state.ForcedWindStrength;
            }
        }
        #endregion

        #region 只读查询与边界平滑
        public bool TryGetRegionSnapshot(Int2 region, out RegionalWeatherSnapshot snapshot)
        {
            region = NormalizeRegion(region);
            if (replicaMode)
                return replicas.TryGetValue(region, out snapshot);
            if (states.TryGetValue(region, out RegionalWeatherState state))
            {
                snapshot = BuildSnapshot(state);
                return true;
            }
            snapshot = default;
            return false;
        }

        public bool TryGetSnapshot(Int2 cell, out RegionalWeatherSnapshot snapshot)
        {
            Int2 region = ResolveRegion(cell);
            if (!TryGetRegionSnapshot(region, out RegionalWeatherSnapshot own))
            {
                snapshot = default;
                return false;
            }
            GetBlendCoordinates(cell, out int left, out int bottom, out float x, out float y);
            snapshot = default;
            Accumulate(ref snapshot, GetNeighbourOrOwn(new Int2(left, bottom), own), (1f - x) * (1f - y));
            Accumulate(ref snapshot, GetNeighbourOrOwn(new Int2(left + 1, bottom), own), x * (1f - y));
            Accumulate(ref snapshot, GetNeighbourOrOwn(new Int2(left, bottom + 1), own), (1f - x) * y);
            Accumulate(ref snapshot, GetNeighbourOrOwn(new Int2(left + 1, bottom + 1), own), x * y);
            // 离散事件只属于所在区域，不能把邻区闪电序号插值后重复播放。
            snapshot.Region = own.Region;
            snapshot.SampleTime = own.SampleTime;
            snapshot.LightningSequence = own.LightningSequence;
            snapshot.RemainingSeconds = own.RemainingSeconds;
            snapshot.Revision = own.Revision;
            snapshot.WindStrength = Clamp01(Length(snapshot.WindX, snapshot.WindY));
            return true;
        }

        /// <summary>地表使用与连续表现一致的降水积分权重，不因硬区域边界漏结算邻区雨雪。</summary>
        public RegionalWeatherAdvanceResult SampleExposureAt(Int2 cell,
            IReadOnlyDictionary<Int2, RegionalWeatherAdvanceResult> exposures)
        {
            Int2 region = ResolveRegion(cell);
            if (exposures == null || !exposures.TryGetValue(region, out RegionalWeatherAdvanceResult own))
                return default;
            GetBlendCoordinates(cell, out int left, out int bottom, out float x, out float y);
            float rain = 0f, snow = 0f;
            AccumulateExposure(ref rain, ref snow,
                GetExposureOrOwn(new Int2(left, bottom), exposures, own), (1f - x) * (1f - y));
            AccumulateExposure(ref rain, ref snow,
                GetExposureOrOwn(new Int2(left + 1, bottom), exposures, own), x * (1f - y));
            AccumulateExposure(ref rain, ref snow,
                GetExposureOrOwn(new Int2(left, bottom + 1), exposures, own), (1f - x) * y);
            AccumulateExposure(ref rain, ref snow,
                GetExposureOrOwn(new Int2(left + 1, bottom + 1), exposures, own), x * y);
            RegionalWeatherSnapshot snapshot = TryGetSnapshot(cell, out var sampled) ? sampled : own.Snapshot;
            return new RegionalWeatherAdvanceResult(snapshot, rain, snow);
        }

        private void GetBlendCoordinates(Int2 cell, out int left, out int bottom, out float x, out float y)
        {
            int2 normalized = domain.Normalize(new int2(cell.X, cell.Y));
            double gx = (normalized.x + 0.5d - (domain.IsWrapped ? domain.Min.x : 0)) / regionWidth - 0.5d;
            double gy = (normalized.y + 0.5d - (domain.IsWrapped ? domain.Min.y : 0)) / regionHeight - 0.5d;
            left = (int)Math.Floor(gx);
            bottom = (int)Math.Floor(gy);
            x = (float)(gx - left);
            y = (float)(gy - bottom);
        }

        private RegionalWeatherAdvanceResult GetExposureOrOwn(Int2 region,
            IReadOnlyDictionary<Int2, RegionalWeatherAdvanceResult> exposures, RegionalWeatherAdvanceResult own)
            => exposures.TryGetValue(NormalizeRegion(region), out RegionalWeatherAdvanceResult neighbour) ? neighbour : own;

        private static void AccumulateExposure(ref float rain, ref float snow,
            RegionalWeatherAdvanceResult value, float weight)
        {
            rain += value.RainExposureSeconds * weight;
            snow += value.SnowExposureSeconds * weight;
        }

        private RegionalWeatherSnapshot GetNeighbourOrOwn(Int2 region, RegionalWeatherSnapshot own)
            => TryGetRegionSnapshot(region, out RegionalWeatherSnapshot neighbour) ? neighbour : own;

        private static void Accumulate(ref RegionalWeatherSnapshot target, RegionalWeatherSnapshot value, float weight)
        {
            target.AirHumidity += value.AirHumidity * weight;
            target.HumidityDeficit += value.HumidityDeficit * weight;
            target.CoolingOffsetCelsius += value.CoolingOffsetCelsius * weight;
            target.RainIntensity += value.RainIntensity * weight;
            target.SnowIntensity += value.SnowIntensity * weight;
            target.PrecipitationIntensity += value.PrecipitationIntensity * weight;
            target.WindX += value.WindX * weight;
            target.WindY += value.WindY * weight;
            target.CloudCoverage += value.CloudCoverage * weight;
            target.FogDensity += value.FogDensity * weight;
            target.LightningActivity += value.LightningActivity * weight;
        }

        private RegionalWeatherSnapshot BuildSnapshot(RegionalWeatherState state)
        {
            float intensity = Clamp01(state.PrecipitationIntensity);
            bool snow = state.ForcedSnow || GetTemperature(state) <= settings.FreezingTemperature;
            return new RegionalWeatherSnapshot
            {
                Region = state.Region,
                SampleTime = state.LastStepTime,
                AirHumidity = GetHumidity(state),
                HumidityDeficit = state.HumidityDeficit,
                CoolingOffsetCelsius = -state.CoolingAmountCelsius,
                RainIntensity = snow ? 0f : intensity,
                SnowIntensity = snow ? intensity : 0f,
                PrecipitationIntensity = intensity,
                WindX = state.WindX,
                WindY = state.WindY,
                WindStrength = Clamp01(Length(state.WindX, state.WindY)),
                CloudCoverage = state.CloudCoverage,
                FogDensity = state.FogDensity,
                LightningActivity = state.LightningActivity,
                LightningSequence = state.LightningSequence,
                RemainingSeconds = (float)Math.Max(0d, state.PrecipitationEndTime - state.LastStepTime),
                Revision = state.Revision
            };
        }
        #endregion

        #region 保存与客户端副本
        public IEnumerable<RegionalWeatherState> EnumerateStates()
        {
            var copies = new List<RegionalWeatherState>(states.Count);
            foreach (RegionalWeatherState state in states.Values)
                copies.Add(state.Clone());
            copies.Sort((a, b) => a.Region.X != b.Region.X ? a.Region.X.CompareTo(b.Region.X) : a.Region.Y.CompareTo(b.Region.Y));
            return copies;
        }

        public void RestoreState(RegionalWeatherState state)
        {
            RequireAuthority();
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            ValidateState(state);
            RegionalWeatherState copy = state.Clone();
            copy.Region = NormalizeRegion(copy.Region);
            if (copy.HasValidInput)
            {
                if (!TryNormalizeInput(copy.LastValidInput, out RegionalWeatherInput input))
                    throw new ArgumentException("区域天气的最近有效输入不完整。", nameof(state));
                copy.LastValidInput = input;
            }
            states[copy.Region] = copy;
        }

        /// <summary>客户端只接收现成效果，不重新抽取降水或推导雨雪类型。</summary>
        public void ApplyReplicatedSnapshot(RegionalWeatherSnapshot snapshot)
        {
            RequireTime(snapshot.SampleTime);
            if (!ValidSnapshot(snapshot))
                throw new ArgumentException("区域天气副本含非法数值。", nameof(snapshot));
            if (!replicaMode)
            {
                states.Clear();
                replicaMode = true;
            }
            snapshot.Region = NormalizeRegion(snapshot.Region);
            if (replicas.TryGetValue(snapshot.Region, out RegionalWeatherSnapshot previous) &&
                (snapshot.Revision < previous.Revision ||
                 snapshot.Revision == previous.Revision && snapshot.SampleTime < previous.SampleTime))
                return;
            replicas[snapshot.Region] = snapshot;
        }
        #endregion

        #region 调试入口
        public void ForcePrecipitation(Int2 region, double now, float intensity, float durationSeconds, bool asSnow = false)
        {
            RequireAuthority();
            RequireTime(now);
            if (!Finite(intensity) || !Finite(durationSeconds) || durationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            region = NormalizeRegion(region);
            RegionalWeatherState state = GetOrCreateState(region, now);
            if (!state.HasValidInput) return;
            state.LastStepTime = now;
            state.CheckIndex = GetCheckIndex(now);
            state.HumidityDeficit = 0f;
            BeginPrecipitation(state, now, Clamp01(intensity),
                Math.Min(settings.HardMaximumDurationSeconds, durationSeconds), asSnow);
            state.CloudCoverage = Math.Max(state.CloudCoverage, Clamp01(intensity));
            IncrementRevision(state);
        }

        public void Clear(Int2 region, double now)
        {
            RequireAuthority();
            RequireTime(now);
            RegionalWeatherState state = GetOrCreateState(NormalizeRegion(region), now);
            state.LastStepTime = now;
            state.CheckIndex = GetCheckIndex(now);
            EndPrecipitation(state, now);
            state.ForcedCloudCoverage = state.ForcedFogDensity = 0f;
            state.ForcedWindStrength = -1f;
            state.ForcedElementsEndTime = now + Math.Min(settings.CooldownSeconds, settings.CheckIntervalSeconds);
            state.CloudCoverage = state.FogDensity = 0f;
            IncrementRevision(state);
        }

        public void ForceElements(Int2 region, double now, double durationSeconds,
            float cloudCoverage = -1f, float fogDensity = -1f, float windStrength = -1f)
        {
            RequireAuthority();
            RequireTime(now);
            if (!Finite(durationSeconds) || durationSeconds <= 0d ||
                !OverrideValue(cloudCoverage) || !OverrideValue(fogDensity) || !OverrideValue(windStrength))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            RequireTime(now + durationSeconds);
            RegionalWeatherState state = GetOrCreateState(NormalizeRegion(region), now);
            state.LastStepTime = now;
            state.CheckIndex = GetCheckIndex(now);
            ExpireForcedElements(state);
            if (cloudCoverage >= 0f)
                state.ForcedCloudCoverage = cloudCoverage;
            if (fogDensity >= 0f)
                state.ForcedFogDensity = fogDensity;
            if (windStrength >= 0f)
                state.ForcedWindStrength = windStrength;
            state.ForcedElementsEndTime = now + durationSeconds;
            ApplyForcedElements(state);
            IncrementRevision(state);
        }
        #endregion

        #region 输入与保存数据约束
        private void RequireAuthority()
        {
            if (replicaMode)
                throw new InvalidOperationException("区域天气副本只能读取，不能推进或修改权威状态。");
        }

        private static void RequireTime(double value)
        {
            if (!Finite(value) || value < 0d || value > 1e15d)
                throw new ArgumentOutOfRangeException(nameof(value), "区域天气时间须为有限的绝对游戏秒。");
        }

        private long GetCheckIndex(double time) => (long)Math.Min(long.MaxValue, Math.Floor(time / settings.CheckIntervalSeconds));

        private static bool TryNormalizeInput(RegionalWeatherInput value, out RegionalWeatherInput normalized)
        {
            normalized = default;
            if (!value.IsComplete || !Finite(value.AirHumiditySupply) ||
                !Finite(value.TemperatureBeforeRainCooling) || !Finite(value.GeographicPrecipitation) ||
                !Finite(value.GeographicWindX) || !Finite(value.GeographicWindY) ||
                !Finite(value.TemperatureGradientX) || !Finite(value.TemperatureGradientY))
                return false;
            normalized = value;
            normalized.AirHumiditySupply = Clamp01(value.AirHumiditySupply);
            normalized.GeographicPrecipitation = Clamp01(value.GeographicPrecipitation);
            normalized.GeographicWindX = Clamp(value.GeographicWindX, -1f, 1f);
            normalized.GeographicWindY = Clamp(value.GeographicWindY, -1f, 1f);
            normalized.TemperatureGradientX = Clamp(value.TemperatureGradientX, -100f, 100f);
            normalized.TemperatureGradientY = Clamp(value.TemperatureGradientY, -100f, 100f);
            return true;
        }

        private static bool SameInput(RegionalWeatherInput a, RegionalWeatherInput b)
            => a.AirHumiditySupply == b.AirHumiditySupply && a.TemperatureBeforeRainCooling == b.TemperatureBeforeRainCooling &&
               a.GeographicPrecipitation == b.GeographicPrecipitation && a.GeographicWindX == b.GeographicWindX &&
               a.GeographicWindY == b.GeographicWindY && a.TemperatureGradientX == b.TemperatureGradientX &&
               a.TemperatureGradientY == b.TemperatureGradientY;

        private void ValidateState(RegionalWeatherState state)
        {
            RequireTime(state.LastStepTime);
            if (state.CheckIndex < 0 || state.LightningSequence < 0 || state.Revision < 0 ||
                !Finite(state.PrecipitationStartedTime) || state.PrecipitationStartedTime < 0d ||
                !Finite(state.PrecipitationEndTime) || state.PrecipitationEndTime < 0d ||
                !Finite(state.CooldownEndTime) || state.CooldownEndTime < 0d ||
                !Finite(state.ForcedElementsEndTime) || state.ForcedElementsEndTime < 0d ||
                !OverrideValue(state.ForcedCloudCoverage) || !OverrideValue(state.ForcedFogDensity) ||
                !OverrideValue(state.ForcedWindStrength) ||
                !Finite(state.NextLightningTime) || state.NextLightningTime < 0d || !Finite(state.LastLightningTime) ||
                !Unit(state.InitialPrecipitationIntensity) || !Unit(state.PrecipitationIntensity) ||
                !Unit(state.HumidityDeficit) || !Finite(state.CoolingAmountCelsius) ||
                state.CoolingAmountCelsius < 0f || state.CoolingAmountCelsius > settings.MaximumCoolingCelsius ||
                !Finite(state.WindX) || !Finite(state.WindY) || Math.Abs(state.WindX) > 1f || Math.Abs(state.WindY) > 1f ||
                !Unit(state.CloudCoverage) || !Unit(state.FogDensity) || !Unit(state.LightningActivity))
                throw new ArgumentException("区域天气保存状态超出配置范围。", nameof(state));
        }

        private static bool ValidSnapshot(RegionalWeatherSnapshot value)
            => value.Revision >= 0 && value.LightningSequence >= 0 &&
               Unit(value.AirHumidity) && Unit(value.HumidityDeficit) && Finite(value.CoolingOffsetCelsius) &&
               value.CoolingOffsetCelsius <= 0f && Unit(value.RainIntensity) && Unit(value.SnowIntensity) &&
               Unit(value.PrecipitationIntensity) && Finite(value.WindX) && Finite(value.WindY) &&
               Math.Abs(value.WindX) <= 1f && Math.Abs(value.WindY) <= 1f && Unit(value.WindStrength) &&
               Unit(value.CloudCoverage) && Unit(value.FogDensity) && Unit(value.LightningActivity) &&
               Finite(value.RemainingSeconds) && value.RemainingSeconds >= 0f;

        private float GetHumidity(RegionalWeatherState state)
            => state.HasValidInput ? Clamp01(state.LastValidInput.AirHumiditySupply - state.HumidityDeficit) : 0f;
        private static float GetTemperature(RegionalWeatherState state)
            => state.LastValidInput.TemperatureBeforeRainCooling - state.CoolingAmountCelsius;
        private static void IncrementRevision(RegionalWeatherState state)
            => state.Revision = state.Revision == int.MaxValue ? 1 : state.Revision + 1;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Unit(float value) => Finite(value) && value >= 0f && value <= 1f;
        private static bool OverrideValue(float value) => value == -1f || Unit(value);
        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        private static float Clamp01(float value) => Clamp(value, 0f, 1f);
        private static float Lerp(float from, float to, float weight) => from + (to - from) * weight;
        private static float Ramp(float value, float min, float max) => Clamp01((value - min) / (max - min));
        private static float Length(float x, float y) => (float)Math.Sqrt((double)x * x + (double)y * y);
        private static float MoveTowards(float current, float target, float amount)
            => current < target ? Math.Min(target, current + amount) : Math.Max(target, current - amount);
        #endregion
    }
}
