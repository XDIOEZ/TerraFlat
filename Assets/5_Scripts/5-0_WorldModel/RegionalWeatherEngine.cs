using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    /// <summary>按区块固定配置推进独立天气事件，查询不生成地形也不重新采样气候。</summary>
    public sealed class RegionalWeatherEngine
    {
        #region 冻结配置与区块真值
        private readonly RegionalWeatherSettings settings;
        private readonly int worldSeed;
        private readonly WorldTopologyDomain domain;
        private readonly int chunkWidth;
        private readonly int chunkHeight;
        private readonly Dictionary<Int2, RegionalWeatherState> states = new();
        private readonly Dictionary<Int2, RegionalWeatherSnapshot> replicas = new();
        private bool replicaMode;

        public RegionalWeatherEngine(RegionalWeatherSettings settings, int worldSeed,
            WorldTopologyDomain domain, int chunkWidth, int chunkHeight)
        {
            this.settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Snapshot();
            if (chunkWidth <= 0 || chunkHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkWidth));
            if (domain.IsWrapped && (domain.Span.x <= 0 || domain.Span.y <= 0))
                throw new ArgumentOutOfRangeException(nameof(domain));
            this.worldSeed = worldSeed;
            this.domain = domain;
            this.chunkWidth = chunkWidth;
            this.chunkHeight = chunkHeight;
        }
        #endregion

        #region 规范区块坐标与注册
        public Int2 ResolveRegion(Int2 cell)
        {
            int2 normalized = domain.Normalize(new int2(cell.X, cell.Y));
            int2 origin = domain.Normalize(new int2(
                FloorDiv(normalized.x, chunkWidth) * chunkWidth,
                FloorDiv(normalized.y, chunkHeight) * chunkHeight));
            return new Int2(origin.x, origin.y);
        }

        public void RegisterChunk(Int2 origin, ChunkWeatherProfile profile, double now)
        {
            RequireAuthority();
            RequireTime(now);
            if (profile == null)
                return;
            RegionalWeatherState state = GetOrCreateState(ResolveRegion(origin), now);
            // 已存档的固定配置随区块事件恢复，不因重新加载而重算。
            if (state.Profile != null)
                return;
            state.Profile = profile;
            foreach (WeatherElementProfile element in state.Profile.Elements)
                GetOrCreateElement(state, element.Id);
            IncrementRevision(state);
        }

        private RegionalWeatherState GetOrCreateState(Int2 region, double now)
        {
            if (states.TryGetValue(region, out RegionalWeatherState state))
                return state;
            double initialTime = Math.Floor(now / settings.StepSeconds) * settings.StepSeconds;
            state = new RegionalWeatherState
            {
                Region = region, LastStepTime = initialTime,
                CheckIndex = GetCheckIndex(initialTime), Revision = 1
            };
            states.Add(region, state);
            return state;
        }

        private static WeatherElementState FindElement(RegionalWeatherState state, string id)
        {
            foreach (WeatherElementState element in state.Elements)
                if (string.Equals(element.Id, id, StringComparison.Ordinal))
                    return element;
            return null;
        }

        private static WeatherElementState GetOrCreateElement(RegionalWeatherState state, string id)
        {
            WeatherElementState found = FindElement(state, id);
            if (found != null)
                return found;
            var created = new WeatherElementState { Id = id };
            int count = state.Elements.Length;
            Array.Resize(ref state.Elements, count + 1);
            state.Elements[count] = created;
            return created;
        }
        #endregion

        #region 固定推进与有界补算
        public RegionalWeatherAdvanceResult Advance(Int2 region, double now, bool allowNewEvents = true)
        {
            RequireAuthority();
            RequireTime(now);
            RegionalWeatherState state = GetOrCreateState(ResolveRegion(region), now);
            if (now < state.LastStepTime)
                return new RegionalWeatherAdvanceResult(BuildSnapshot(state));

            double step = settings.StepSeconds;
            double wholeSteps = Math.Floor((now - state.LastStepTime) / step);
            bool jumped = wholeSteps > 1d;
            if (wholeSteps > settings.MaximumCatchUpSteps)
            {
                state.LastStepTime += (wholeSteps - settings.MaximumCatchUpSteps) * step;
                state.CheckIndex = GetCheckIndex(state.LastStepTime);
                ExpireElements(state);
                IncrementRevision(state);
                wholeSteps = settings.MaximumCatchUpSteps;
            }

            float rainExposure = 0f, snowExposure = 0f;
            int count = (int)wholeSteps;
            bool crossedCheck = false;
            for (int i = 0; i < count; i++)
            {
                AccumulatePrecipitation(state, state.LastStepTime, state.LastStepTime + step,
                    out float rain, out float snow);
                rainExposure += rain;
                snowExposure += snow;
                state.LastStepTime += step;
                ExpireElements(state);
                long checkIndex = GetCheckIndex(state.LastStepTime);
                if (checkIndex > state.CheckIndex)
                {
                    state.CheckIndex = checkIndex;
                    crossedCheck = true;
                }
                // 过去只结算已有事件，到当前落点至多抽取一次新天气。
                if (allowNewEvents && i == count - 1 && crossedCheck)
                    TryBeginElements(state);
                // 跳时不补播任何过去的雷声或闪电，只重新建立后续雷电时钟。
                if (allowNewEvents && !jumped && i == count - 1)
                    UpdateLightning(state);
                IncrementRevision(state);
            }
            if (jumped && count > 0)
                ScheduleFutureLightning(state);
            return new RegionalWeatherAdvanceResult(BuildSnapshot(state), rainExposure, snowExposure);
        }

        private void TryBeginElements(RegionalWeatherState state)
        {
            if (state.Profile == null)
                return;
            foreach (WeatherElementProfile profile in state.Profile.Elements)
            {
                WeatherElementState element = GetOrCreateElement(state, profile.Id);
                if (element.Intensity > 0f || profile.Intensity <= 0f || profile.Probability <= 0f ||
                    Random01(state.Region, state.CheckIndex, profile.Id) >= profile.Probability)
                    continue;
                BeginElement(state, element, state.LastStepTime, profile.Intensity, profile.DurationSeconds);
            }
        }

        private void BeginElement(RegionalWeatherState state, WeatherElementState element,
            double now, float intensity, double durationSeconds)
        {
            element.StartedTime = now;
            element.EndTime = now + durationSeconds;
            element.Intensity = intensity;
            if (element.Id == WeatherElementIds.Rain)
                state.ForcedSnow = false;
            if (element.Id == WeatherElementIds.Thunder)
                state.NextLightningTime = state.LastLightningTime < 0d ? now :
                    Math.Max(now, state.LastLightningTime + settings.MinimumLightningIntervalSeconds);
        }

        private void AccumulatePrecipitation(RegionalWeatherState state, double start, double end,
            out float rain, out float snow)
        {
            rain = snow = 0f;
            WeatherElementState precipitation = FindElement(state, WeatherElementIds.Rain);
            if (precipitation == null || precipitation.Intensity <= 0f)
                return;
            double overlap = Math.Max(0d, Math.Min(end, precipitation.EndTime) -
                Math.Max(start, precipitation.StartedTime));
            float exposure = (float)(overlap * precipitation.Intensity);
            bool asSnow = state.ForcedSnow ||
                state.Profile != null && state.Profile.Climate.TemperatureCelsius <= settings.FreezingTemperature;
            if (asSnow) snow = exposure;
            else rain = exposure;
        }

        private static void ExpireElements(RegionalWeatherState state)
        {
            foreach (WeatherElementState element in state.Elements)
            {
                if (state.LastStepTime < element.EndTime)
                    continue;
                element.Intensity = 0f;
                if (element.Id == WeatherElementIds.Rain)
                    state.ForcedSnow = false;
            }
            if (state.LastStepTime >= state.ForcedElementsEndTime)
            {
                state.ForcedCloudCoverage = state.ForcedFogDensity = state.ForcedWindStrength = -1f;
                state.ForcedElementsEndTime = 0d;
            }
        }

        private void UpdateLightning(RegionalWeatherState state)
        {
            float activity = ActiveIntensity(state, WeatherElementIds.Thunder);
            if (activity <= 0f || state.LastStepTime < state.NextLightningTime ||
                state.LastLightningTime >= 0d &&
                state.LastStepTime - state.LastLightningTime < settings.MinimumLightningIntervalSeconds)
                return;
            int sequence = state.LightningSequence == int.MaxValue ? 1 : state.LightningSequence + 1;
            int2 cell = domain.Normalize(new int2(
                state.Region.X + (int)(Random01(state.Region, sequence, "lightning.x") * chunkWidth),
                state.Region.Y + (int)(Random01(state.Region, sequence, "lightning.y") * chunkHeight)));
            CommitLightning(state, new Int2(cell.x, cell.y), activity);
        }

        // 落点只由权威端决定，保存和复制同时携带同一次雷击的强度。
        private void CommitLightning(RegionalWeatherState state, Int2 cell, float strength)
        {
            state.LightningSequence = state.LightningSequence == int.MaxValue ? 1 : state.LightningSequence + 1;
            state.LightningCell = cell;
            state.LightningStrength = strength;
            state.LastLightningTime = state.LastStepTime;
            state.NextLightningTime = state.LastStepTime + LightningInterval(strength);
        }

        private double LightningInterval(float activity)
            => settings.MinimumLightningIntervalSeconds / Math.Max(0.1f, activity);

        private void ScheduleFutureLightning(RegionalWeatherState state)
        {
            float activity = ActiveIntensity(state, WeatherElementIds.Thunder);
            state.NextLightningTime = activity > 0f ? state.LastStepTime + LightningInterval(activity) : 0d;
        }

        /// <summary>回拨游戏时刻只平移事件时段和雷电时钟，不重抽天气。</summary>
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
                foreach (WeatherElementState element in state.Elements)
                {
                    element.StartedTime = ShiftTime(element.StartedTime, deltaSeconds);
                    element.EndTime = ShiftTime(element.EndTime, deltaSeconds);
                }
                if (state.NextLightningTime > 0d)
                    state.NextLightningTime = ShiftTime(state.NextLightningTime, deltaSeconds);
                if (state.LastLightningTime >= 0d)
                    state.LastLightningTime = ShiftTime(state.LastLightningTime, deltaSeconds);
                if (state.ForcedElementsEndTime > 0d)
                    state.ForcedElementsEndTime = ShiftTime(state.ForcedElementsEndTime, deltaSeconds);
                state.CheckIndex = GetCheckIndex(state.LastStepTime);
                IncrementRevision(state);
            }
        }

        private static double ShiftTime(double time, double delta)
            => Math.Max(0d, Math.Min(1e15d, time + delta));

        private float Random01(Int2 region, long checkIndex, string id)
        {
            unchecked
            {
                ulong value = 14695981039346656037UL;
                for (int i = 0; i < id.Length; i++)
                    value = (value ^ id[i]) * 1099511628211UL;
                value ^= (uint)worldSeed;
                value ^= (ulong)(uint)region.X * 0x9e3779b185ebca87UL;
                value ^= (ulong)(uint)region.Y * 0xc2b2ae3d27d4eb4fUL;
                value ^= (ulong)checkIndex * 0x165667b19e3779f9UL;
                value ^= value >> 30;
                value *= 0xbf58476d1ce4e5b9UL;
                value ^= value >> 27;
                value *= 0x94d049bb133111ebUL;
                value ^= value >> 31;
                return (value >> 40) / 16777216f;
            }
        }
        #endregion

        #region 只读区块查询与降水积分
        public bool TryGetRegionSnapshot(Int2 region, out RegionalWeatherSnapshot snapshot)
        {
            region = ResolveRegion(region);
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
            => TryGetRegionSnapshot(ResolveRegion(cell), out snapshot);

        public bool TryGetProfile(Int2 region, out ChunkWeatherProfile profile)
        {
            if (states.TryGetValue(ResolveRegion(region), out RegionalWeatherState state) && state.Profile != null)
            {
                profile = state.Profile;
                return true;
            }
            profile = null;
            return false;
        }

        public bool TryGetState(Int2 region, out RegionalWeatherState state)
        {
            if (states.TryGetValue(ResolveRegion(region), out RegionalWeatherState value))
            {
                state = value.Clone();
                return true;
            }
            state = null;
            return false;
        }

        public float SampleCoolingOffsetAt(Int2 cell) => 0f;

        public RegionalWeatherAdvanceResult SampleExposureAt(Int2 cell,
            IReadOnlyDictionary<Int2, RegionalWeatherAdvanceResult> exposures)
        {
            if (exposures == null || !exposures.TryGetValue(ResolveRegion(cell), out RegionalWeatherAdvanceResult own))
                return default;
            return new RegionalWeatherAdvanceResult(
                TryGetSnapshot(cell, out RegionalWeatherSnapshot sampled) ? sampled : own.Snapshot,
                own.RainExposureSeconds, own.SnowExposureSeconds);
        }

        public float SamplePrecipitationExposureAt(Int2 cell,
            IReadOnlyDictionary<Int2, RegionalWeatherAdvanceResult> exposures)
        {
            if (exposures == null || !exposures.TryGetValue(ResolveRegion(cell), out RegionalWeatherAdvanceResult own))
                return 0f;
            return own.RainExposureSeconds + own.SnowExposureSeconds;
        }

        private static float ActiveIntensity(RegionalWeatherState state, string id)
        {
            WeatherElementState element = FindElement(state, id);
            return element != null && state.LastStepTime >= element.StartedTime && state.LastStepTime < element.EndTime
                ? element.Intensity : 0f;
        }

        private RegionalWeatherSnapshot BuildSnapshot(RegionalWeatherState state)
        {
            float precipitation = ActiveIntensity(state, WeatherElementIds.Rain);
            float wind = ActiveIntensity(state, WeatherElementIds.Wind);
            float thunder = ActiveIntensity(state, WeatherElementIds.Thunder);
            bool hasOverride = state.LastStepTime < state.ForcedElementsEndTime;
            if (hasOverride && state.ForcedWindStrength >= 0f)
                wind = state.ForcedWindStrength;
            ChunkWeatherClimate climate = state.Profile?.Climate ?? default;
            float directionLength = Length(climate.WindX, climate.WindY);
            float directionX, directionY;
            if (directionLength > 0.00001f)
            {
                directionX = climate.WindX / directionLength;
                directionY = climate.WindY / directionLength;
            }
            else
            {
                double angle = Random01(state.Region, 0L, WeatherElementIds.Wind) * Math.PI * 2d;
                directionX = (float)Math.Cos(angle);
                directionY = (float)Math.Sin(angle);
            }
            bool snow = state.ForcedSnow || state.Profile != null &&
                climate.TemperatureCelsius <= settings.FreezingTemperature;
            float remainingSeconds = 0f;
            foreach (WeatherElementState element in state.Elements)
                if (element.Intensity > 0f && state.LastStepTime < element.EndTime)
                    remainingSeconds = Math.Max(remainingSeconds, (float)(element.EndTime - state.LastStepTime));
            if (hasOverride)
                remainingSeconds = Math.Max(remainingSeconds, (float)(state.ForcedElementsEndTime - state.LastStepTime));
            return new RegionalWeatherSnapshot
            {
                Region = state.Region, SampleTime = state.LastStepTime,
                AirHumidity = climate.AirHumidity, HumidityDeficit = 0f, CoolingOffsetCelsius = 0f,
                RainIntensity = snow ? 0f : precipitation, SnowIntensity = snow ? precipitation : 0f,
                PrecipitationIntensity = precipitation,
                WindX = directionX * wind, WindY = directionY * wind, WindStrength = wind,
                CloudCoverage = hasOverride && state.ForcedCloudCoverage >= 0f
                    ? state.ForcedCloudCoverage : Math.Max(precipitation, thunder),
                FogDensity = hasOverride && state.ForcedFogDensity >= 0f ? state.ForcedFogDensity : 0f,
                LightningActivity = thunder, LightningSequence = state.LightningSequence,
                LightningCell = state.LightningCell, LightningStrength = state.LightningStrength,
                RemainingSeconds = remainingSeconds,
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
            copies.Sort((a, b) => a.Region.X != b.Region.X
                ? a.Region.X.CompareTo(b.Region.X) : a.Region.Y.CompareTo(b.Region.Y));
            return copies;
        }

        public void RestoreState(RegionalWeatherState state)
        {
            RequireAuthority();
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            ValidateState(state);
            RegionalWeatherState copy = state.Clone();
            copy.Region = ResolveRegion(copy.Region);
            states[copy.Region] = copy;
        }

        public void ApplyReplicatedSnapshot(RegionalWeatherSnapshot snapshot)
        {
            RequireTime(snapshot.SampleTime);
            if (!ValidSnapshot(snapshot))
                throw new ArgumentException("区块天气副本含非法数值。", nameof(snapshot));
            if (!replicaMode)
            {
                states.Clear();
                replicaMode = true;
            }
            snapshot.Region = ResolveRegion(snapshot.Region);
            if (replicas.TryGetValue(snapshot.Region, out RegionalWeatherSnapshot previous) &&
                (snapshot.Revision < previous.Revision ||
                 snapshot.Revision == previous.Revision && snapshot.SampleTime < previous.SampleTime))
                return;
            replicas[snapshot.Region] = snapshot;
        }
        #endregion

        #region 手动天气入口
        public RegionalWeatherSnapshot StrikeLightning(Int2 cell, double now, float strength)
        {
            RequireAuthority();
            RequireTime(now);
            if (!Unit(strength) || strength <= 0f)
                throw new ArgumentOutOfRangeException(nameof(strength));
            int2 normalized = domain.Normalize(new int2(cell.X, cell.Y));
            cell = new Int2(normalized.x, normalized.y);
            RegionalWeatherState state = GetOrCreateState(ResolveRegion(cell), now);
            state.LastStepTime = now;
            state.CheckIndex = GetCheckIndex(now);
            ExpireElements(state);
            CommitLightning(state, cell, strength);
            IncrementRevision(state);
            return BuildSnapshot(state);
        }

        public void ForceElement(Int2 region, double now, string elementId, float intensity, float durationSeconds)
        {
            RequireAuthority();
            RequireTime(now);
            if (string.IsNullOrWhiteSpace(elementId) || !Finite(intensity) ||
                !Finite(durationSeconds) || durationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            RequireTime(now + durationSeconds);
            RegionalWeatherState state = GetOrCreateState(ResolveRegion(region), now);
            state.LastStepTime = now;
            state.CheckIndex = GetCheckIndex(now);
            ExpireElements(state);
            BeginElement(state, GetOrCreateElement(state, elementId), now, Clamp01(intensity), durationSeconds);
            IncrementRevision(state);
        }

        public void ForcePrecipitation(Int2 region, double now, float intensity, float durationSeconds, bool asSnow = false)
        {
            ForceElement(region, now, WeatherElementIds.Rain, intensity, durationSeconds);
            states[ResolveRegion(region)].ForcedSnow = asSnow;
        }

        public void Clear(Int2 region, double now)
        {
            RequireAuthority();
            RequireTime(now);
            RegionalWeatherState state = GetOrCreateState(ResolveRegion(region), now);
            state.LastStepTime = now;
            state.CheckIndex = GetCheckIndex(now);
            foreach (WeatherElementState element in state.Elements)
            {
                element.StartedTime = element.EndTime = now;
                element.Intensity = 0f;
            }
            state.ForcedSnow = false;
            state.ForcedCloudCoverage = state.ForcedFogDensity = state.ForcedWindStrength = -1f;
            state.ForcedElementsEndTime = state.NextLightningTime = 0d;
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
            RegionalWeatherState state = GetOrCreateState(ResolveRegion(region), now);
            state.LastStepTime = now;
            state.CheckIndex = GetCheckIndex(now);
            ExpireElements(state);
            if (cloudCoverage >= 0f) state.ForcedCloudCoverage = cloudCoverage;
            if (fogDensity >= 0f) state.ForcedFogDensity = fogDensity;
            if (windStrength >= 0f) state.ForcedWindStrength = windStrength;
            state.ForcedElementsEndTime = now + durationSeconds;
            IncrementRevision(state);
        }
        #endregion

        #region 有限值与保存约束
        private void RequireAuthority()
        {
            if (replicaMode)
                throw new InvalidOperationException("区块天气副本只能读取，不能推进或修改权威状态。");
        }

        private static void RequireTime(double value)
        {
            if (!Finite(value) || value < 0d || value > 1e15d)
                throw new ArgumentOutOfRangeException(nameof(value), "区块天气时间须为有限的绝对游戏秒。");
        }

        private long GetCheckIndex(double time) => (long)Math.Floor(time / settings.CheckIntervalSeconds);

        private static void ValidateState(RegionalWeatherState state)
        {
            RequireTime(state.LastStepTime);
            if (state.CheckIndex < 0L || state.LightningSequence < 0 || state.Revision < 0 ||
                !Finite(state.ForcedElementsEndTime) || state.ForcedElementsEndTime < 0d ||
                !OverrideValue(state.ForcedCloudCoverage) || !OverrideValue(state.ForcedFogDensity) ||
                !OverrideValue(state.ForcedWindStrength) ||
                !Finite(state.NextLightningTime) || state.NextLightningTime < 0d ||
                !Finite(state.LastLightningTime) || state.LastLightningTime < -1d ||
                !Unit(state.LightningStrength) || state.Elements == null)
                throw new ArgumentException("区块天气保存状态超出配置范围。", nameof(state));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (WeatherElementState element in state.Elements)
            {
                if (element == null || string.IsNullOrWhiteSpace(element.Id) || !ids.Add(element.Id) ||
                    !Finite(element.StartedTime) || element.StartedTime < 0d ||
                    !Finite(element.EndTime) || element.EndTime < element.StartedTime || !Unit(element.Intensity))
                    throw new ArgumentException("区块天气事件包含非法标识、时段或强度。", nameof(state));
            }
        }

        private static bool ValidSnapshot(RegionalWeatherSnapshot value)
            => value.Revision >= 0 && value.LightningSequence >= 0 &&
               Unit(value.AirHumidity) && value.HumidityDeficit == 0f && value.CoolingOffsetCelsius == 0f &&
               Unit(value.RainIntensity) && Unit(value.SnowIntensity) && Unit(value.PrecipitationIntensity) &&
               Finite(value.WindX) && Finite(value.WindY) && Math.Abs(value.WindX) <= 1f &&
               Math.Abs(value.WindY) <= 1f && Unit(value.WindStrength) &&
               Unit(value.CloudCoverage) && Unit(value.FogDensity) && Unit(value.LightningActivity) &&
               Unit(value.LightningStrength) &&
               Finite(value.RemainingSeconds) && value.RemainingSeconds >= 0f;

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }
        private static void IncrementRevision(RegionalWeatherState state)
            => state.Revision = state.Revision == int.MaxValue ? 1 : state.Revision + 1;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Unit(float value) => Finite(value) && value >= 0f && value <= 1f;
        private static bool OverrideValue(float value) => value == -1f || Unit(value);
        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
        private static float Length(float x, float y) => (float)Math.Sqrt((double)x * x + (double)y * y);
        #endregion
    }
}
