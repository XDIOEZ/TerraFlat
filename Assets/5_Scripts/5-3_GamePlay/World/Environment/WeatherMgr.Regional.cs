using System;
using System.Collections.Generic;
using System.Collections;
using FlatWorld.Networking;
using FlatWorld.Diagnostics;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public readonly struct RegionalLightningEvent
{
    #region 单次区域闪电

    public readonly string WorldKey;
    public readonly Int2 Region;
    public readonly Vector2 Position;
    public readonly float Strength;
    public readonly int Sequence;

    public RegionalLightningEvent(string worldKey, Int2 region, Vector2 position, float strength, int sequence)
    {
        WorldKey = worldKey;
        Region = region;
        Position = position;
        Strength = strength;
        Sequence = sequence;
    }

    #endregion
}

public partial class WeatherMgr
{
    #region 区域运行时

    private RegionalWeatherSettings regionalSettings;
    [SerializeField, Min(0f)] private float surfaceRainWaterPerSecond = 0.02f;
    [SerializeField, Min(0f)] private float meltWaterPerSnowDepth = 5f;
    [SerializeField, Min(0f), Tooltip("雷击落点的伤害半径，边缘衰减到零。")]
    private float lightningDamageRadius = 1.5f;
    [SerializeField, Min(0f), Tooltip("满强度雷击中心的物理伤害，仍计算目标防御。")]
    private float lightningMaximumDamage = 40f;
    public float SurfaceRainWaterPerSecond => surfaceRainWaterPerSecond;
    public float MeltWaterPerSnowDepth => meltWaterPerSnowDepth;
    public float PrecipitationFreezingTemperature => GetSnowConfig().FreezingTemperature;
    public static event Action<RegionalWeatherStateSnapshot> AuthoritativeRegionalWeatherStateChanged;
    public event Action<RegionalLightningEvent> LightningOccurred;

    private RegionalWeatherEngine regionalEngine;
    private PlanetData regionalPlanet;
    private WorldTopologyDomain regionalDomain;
    private double lastRegionalAdvance = double.NegativeInfinity;
    private readonly List<ChunkRuntime> regionalChunks = new();
    private readonly List<Int2> regionalWork = new();
    private readonly Dictionary<Int2, RegionalWeatherAdvanceResult> regionalExposure = new();
    private readonly Dictionary<Int2, int> regionalSaveIndices = new();
    private readonly Dictionary<Int2, int> lightningSeen = new();
    private readonly Dictionary<RuntimeWorldAddress, ChunkRuntime> surfaceChunksLastStep = new();
    private readonly Dictionary<RuntimeWorldAddress, ChunkRuntime> surfaceChunksThisStep = new();
    private readonly WeatherSurfaceWorkQueue surfaceWork = new();
    private WorldRuntime surfaceWorkWorld;
    private long surfaceWorkEpoch;
    private int surfaceWorkFrame = -1;
    private Action<ChunkRuntime, float, float, TemperatureMgr.AmbientChunkSnapshot> applySurfaceWork;
    private const double SurfaceMillisecondsPerFrame = 2d;
    #region 性能分段
    // 准备与出队分别计时，避免把入队前的温度重建漏在预算外。
    private static readonly PerformanceDebugPoint RegionalAdvanceMarker = new("FlatWorld.Weather.AdvanceRegions");
    private static readonly PerformanceDebugPoint SurfaceAdvanceMarker = new("FlatWorld.Weather.AdvanceSurface");
    private static readonly PerformanceDebugPoint SurfacePrepareMarker = new("FlatWorld.Weather.SurfacePrepare");
    #endregion
    private LightningEffectController lightningEffect;

    // 缓存委托，正常排队和逐帧结算不产生闭包。
    private Action<ChunkRuntime, float, float, TemperatureMgr.AmbientChunkSnapshot> SurfaceWorkHandler =>
        applySurfaceWork ??= ApplySurfaceWork;

    // 状态只在推进入口创建；查询不抽随机数，也不生成地形。
    private int windPushQueryFrame = -1;
    private bool hasWindPush;

    /// <summary>每帧只扫描一次天气区域，跨区物体不能用镜头所在区的风力裁定。</summary>
    public bool HasWindPush
    {
        get
        {
            if (!_weatherRuntimeAllowed || regionalEngine == null) return false;
            if (windPushQueryFrame != Time.frameCount)
            {
                windPushQueryFrame = Time.frameCount;
                hasWindPush = regionalEngine.HasWindStrengthAtLeast(WindEnvironmentRules.PushStartStrength);
            }
            return hasWindPush;
        }
    }

    /// <summary>风推动读取同一天气世界的星球重力，不借镜头或其它场景的星球参数。</summary>
    public bool TryGetWindSurfaceGravity(out double gravity)
    {
        gravity = regionalPlanet?.SurfaceGravityMetersPerSecondSquared ?? double.NaN;
        return _weatherRuntimeAllowed && regionalEngine != null && double.IsFinite(gravity) && gravity >= 0d;
    }

    public bool TryGetWeatherAt(Vector2 position, out RegionalWeatherSnapshot sample)
    {
        sample = default;
        if (!_weatherRuntimeAllowed || regionalEngine == null ||
            !float.IsFinite(position.x) || !float.IsFinite(position.y)) return false;
        bool found = regionalEngine.TryGetSnapshot(
            new Int2(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)), out sample);
        if (TryGetWindBlendAt(position, out var blend))
        {
            Unity.Mathematics.float2 wind = blend.Wind;
            sample.WindX = wind.x;
            sample.WindY = wind.y;
            sample.WindStrength = Mathf.Clamp01(Unity.Mathematics.math.length(wind));
            found = true;
        }
        return found;
    }

    /// <summary>提供风区原始风向量与空间权重，推力换算由移动规则负责。</summary>
    public bool TryGetWindBlendAt(Vector2 position, out RegionalWindBlend blend)
    {
        blend = default;
        return _weatherRuntimeAllowed && regionalEngine != null &&
               regionalEngine.TryGetWindBlend(new Unity.Mathematics.double2(position.x, position.y), out blend);
    }

    public float GetTemperatureOffsetAt(Vector2 position) =>
        _weatherRuntimeAllowed && regionalEngine != null &&
        float.IsFinite(position.x) && float.IsFinite(position.y)
            ? regionalEngine.SampleCoolingOffsetAt(new Int2(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)))
            : 0f;

    public float GetAirHumidityDeficitAt(Vector2 position) =>
        TryGetWeatherAt(position, out var sample) ? sample.HumidityDeficit : 0f;

    // 调试只读取生成好的区块天气参数，不再现场采地块计算气候。
    public bool TryGetWeatherProfileAt(Vector2 position, out ChunkWeatherProfile profile)
    {
        profile = null;
        if (!_weatherRuntimeAllowed || regionalEngine == null ||
            !float.IsFinite(position.x) || !float.IsFinite(position.y)) return false;
        if (regionalEngine.TryGetProfile(regionalEngine.ResolveRegion(
            new Int2(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y))), out profile)) return true;
        // 普通客户端只展示本地生成的固定档案，不在此推进权威天气。
        if (ChunkMgr.ExistingInstance?.TryGetRestoredTerrainForEnvironment(position, out var tile) != true) return false;
        profile = tile.Terrain.WeatherProfile;
        return profile != null;
    }

    /// <summary>降水发生由共享天气区决定，雨雪表现只按当地实时温度分流。</summary>
    public bool TryGetPrecipitationAt(Vector2 position, out float rainIntensity, out float snowIntensity)
    {
        rainIntensity = snowIntensity = 0f;
        if (!TryGetWeatherAt(position, out var sample) ||
            !TemperatureMgr.Instance.TryGetAmbientTemperature(position, out float temperature)) return false;
        if (temperature <= PrecipitationFreezingTemperature) snowIntensity = sample.PrecipitationIntensity;
        else rainIntensity = sample.PrecipitationIntensity;
        return true;
    }

    private float GetLocalRainIntensity()
    {
        Camera camera = Camera.main;
        return TryGetPrecipitationAt(camera != null ? (Vector2)camera.transform.position : (Vector2)transform.position,
            out float rain, out _) ? rain : 0f;
    }

    private bool TryGetLocalWeather(out RegionalWeatherSnapshot sample)
    {
        Camera camera = Camera.main;
        return TryGetWeatherAt(camera != null ? (Vector2)camera.transform.position : (Vector2)transform.position, out sample);
    }

    public static WeatherType DescribeWeather(in RegionalWeatherSnapshot sample)
    {
        if (sample.PrecipitationIntensity > 0.001f)
            return sample.LightningActivity > 0.2f || sample.PrecipitationIntensity >= 0.6f && sample.WindStrength >= 0.7f
                ? WeatherType.Storm : WeatherType.Rain;
        if (sample.FogDensity > 0.3f) return WeatherType.Fog;
        return sample.CloudCoverage > 0.4f ? WeatherType.Cloudy : WeatherType.Clear;
    }

    public static WeatherPhase DescribeWeatherPhase(in RegionalWeatherSnapshot sample)
    {
        if (sample.PrecipitationIntensity > 0.001f)
            return sample.PrecipitationIntensity >= 0.85f ? WeatherPhase.RainHeavy :
                sample.PrecipitationIntensity >= 0.5f ? WeatherPhase.RainSteady : WeatherPhase.RainStarting;
        if (sample.FogDensity > 0.3f) return WeatherPhase.Fog;
        return sample.CloudCoverage > 0.4f ? WeatherPhase.Cloudy : WeatherPhase.Clear;
    }

    private bool PrepareRegionalContext()
    {
        PlanetData planet = GetActivePlanetData();
        if (!_weatherRuntimeAllowed || planet == null) return false;
        WorldTopologyDomain domain = WorldTopologyRuntime.GetActiveDomain();
        if (regionalEngine != null && ReferenceEquals(planet, regionalPlanet) &&
            ReferenceEquals(ChunkMgr.ExistingInstance?.WorldRuntime, surfaceWorkWorld) &&
            surfaceWorkWorld?.Epoch == surfaceWorkEpoch &&
            domain.IsWrapped == regionalDomain.IsWrapped &&
            domain.Min.Equals(regionalDomain.Min) && domain.Span.Equals(regionalDomain.Span))
            return true;

        FlushSurfaceWork();
        surfaceWork.Dispose();
        surfaceWorkWorld = ChunkMgr.ExistingInstance?.WorldRuntime;
        surfaceWorkEpoch = surfaceWorkWorld?.Epoch ?? 0;
        regionalSettings = ChunkWeatherConfiguration.Load();
        regionalPlanet = planet;
        regionalDomain = domain;
        Vector2Int chunkSize = PlanetData.NormalizeChunkSize(planet.ChunkSize);
        regionalEngine = new RegionalWeatherEngine(regionalSettings, GetDeterministicSeed(), domain,
            chunkSize.x, chunkSize.y);
        ResetWeatherAudioBaseline();
        regionalSaveIndices.Clear();
        lightningSeen.Clear();
        LightningStrikeSystem.Reset();
        surfaceChunksLastStep.Clear();
        surfaceChunksThisStep.Clear();
        regionalPlanet.RegionalWeatherStates ??= new List<RegionalWeatherRecord>();
        var restoredSnapshots = new List<RegionalWeatherSnapshot>();
        for (int i = 0; i < regionalPlanet.RegionalWeatherStates.Count; i++)
        {
            RegionalWeatherRecord record = regionalPlanet.RegionalWeatherStates[i];
            if (record == null) throw new InvalidOperationException("区域天气档案包含空记录。");
            RegionalWeatherState state = record.ToState();
            state.Region = regionalEngine.ResolveRegion(state.Region);
            if (regionalSaveIndices.ContainsKey(state.Region))
                throw new InvalidOperationException("区域天气档案包含重复区域。");
            regionalEngine.RestoreState(state);
            regionalSaveIndices.Add(state.Region, i);
            lightningSeen[state.Region] = state.LightningSequence;
            if (regionalEngine.TryGetRegionSnapshot(state.Region, out var sample)) restoredSnapshots.Add(sample);
        }
        if (!GameNetwork.HasStateAuthority)
            foreach (var sample in restoredSnapshots) regionalEngine.ApplyReplicatedSnapshot(sample);
        lastRegionalAdvance = double.NegativeInfinity;
        if (lightningEffect == null) lightningEffect = gameObject.AddComponent<LightningEffectController>();
        lightningEffect.Initialize(this);
        return true;
    }

    // 就绪区块共用区域事件，每个天气区只推进一次并向所有成员分发降水。
    private void AdvanceRegionalWeather()
    {
        if (surfaceWork.IsAdvanceBlocked)
            return;
        if (!PrepareRegionalContext() || !GameNetwork.HasStateAuthority ||
            !TryGetCurrentTimeData(out TimeData clock)) return;
        double now = GetRegionalStepTime(clock);
        if (now < lastRegionalAdvance)
        {
            FlushSurfaceWork();
            regionalEngine.RebaseTime(now - lastRegionalAdvance);
            lastRegionalAdvance = double.NegativeInfinity;
            surfaceChunksLastStep.Clear();
        }
        if (now - lastRegionalAdvance < regionalSettings.StepSeconds) return;
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (chunks?.WorldRuntime == null) return;
        // 极端加速先等待积压消费，不推进区域时钟或集中冲刷旧账。
        long maximumPending = (long)Math.Max(1, chunks.Chunks.Count) * regionalSettings.MaximumCatchUpSteps;
        if (surfaceWork.Count + (long)chunks.Chunks.Count > maximumPending)
            return;

        regionalWork.Clear();
        regionalChunks.Clear();
        regionalExposure.Clear();
        surfaceChunksThisStep.Clear();
        foreach (var pair in chunks.Chunks)
        {
            ChunkRuntime chunk = pair.Value;
            if (!chunks.IsTerrainRestoredForEnvironment(chunk)) continue;
            surfaceChunksThisStep[chunk.Address] = chunk;
            if (chunk.Terrain.WeatherProfile == null) continue;
            regionalChunks.Add(chunk);
        }
        regionalChunks.Sort((a, b) => a.Address.ChunkOrigin.Y == b.Address.ChunkOrigin.Y
            ? a.Address.ChunkOrigin.X.CompareTo(b.Address.ChunkOrigin.X)
            : a.Address.ChunkOrigin.Y.CompareTo(b.Address.ChunkOrigin.Y));
        foreach (ChunkRuntime chunk in regionalChunks)
        {
            Int2 region = regionalEngine.ResolveRegion(chunk.Address.ChunkOrigin);
            regionalEngine.RegisterChunk(chunk.Address.ChunkOrigin, chunk.Terrain.WeatherProfile, now);
            if (regionalExposure.TryAdd(region, default)) regionalWork.Add(region);
        }
        regionalWork.Sort((a, b) => a.Y == b.Y ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        using (RegionalAdvanceMarker.Measure(regionalWork.Count))
        foreach (Int2 region in regionalWork)
        {
            if (!lightningSeen.ContainsKey(region) && regionalEngine.TryGetRegionSnapshot(region, out var baseline))
                lightningSeen[region] = baseline.LightningSequence;
            RegionalWeatherAdvanceResult result = regionalEngine.Advance(region, now);
            regionalExposure[region] = result;
            ObserveLightning(result.Snapshot, false);
            AuthoritativeRegionalWeatherStateChanged?.Invoke(new RegionalWeatherStateSnapshot(regionalPlanet.Name, result.Snapshot));
        }

        // 未加载期间只补天气状态，地块只接收本次已实际加载时间内的降水。
        float elapsed = double.IsNegativeInfinity(lastRegionalAdvance) ? 0f :
            (float)Math.Min(now - lastRegionalAdvance, regionalSettings.StepSeconds * regionalSettings.MaximumCatchUpSteps);
        int enqueuedChunks = 0;
        using (SurfacePrepareMarker.Measure())
        {
            if (elapsed > 0f)
                foreach (var pair in surfaceChunksThisStep)
                    if (surfaceChunksLastStep.TryGetValue(pair.Key, out var previous) && ReferenceEquals(previous, pair.Value))
                    {
                        float precipitation = Mathf.Min(regionalEngine.SamplePrecipitationExposureAt(
                            pair.Key.ChunkOrigin, regionalExposure),
                            elapsed);
                        if (WorldWeatherSurfaceSystem.HasSurfaceWork(pair.Value.Terrain, precipitation,
                                surfaceWork.HasPending(pair.Value)) &&
                            TemperatureMgr.Instance != null &&
                            TemperatureMgr.Instance.TryCaptureAmbientChunk(pair.Value, out var temperature))
                        {
                            surfaceWork.Enqueue(pair.Value, elapsed, precipitation, temperature);
                            enqueuedChunks++;
                        }
                    }
            SurfacePrepareMarker.AddWork(enqueuedChunks);
            SurfacePrepareMarker.SetGauge(surfaceWork.Count);
        }
        surfaceChunksLastStep.Clear();
        foreach (var pair in surfaceChunksThisStep) surfaceChunksLastStep[pair.Key] = pair.Value;
        lastRegionalAdvance = now;
        CompleteWeatherAudioBaseline();
        foreach (Int2 region in regionalWork)
            if (regionalEngine.TryGetState(region, out var state)) PersistRegionalState(state);
        UpdateLocalWeatherProjection();
    }

    #endregion

    #region 地表结算分帧

    private bool IsSurfaceWorkCurrent => GameNetwork.HasStateAuthority && surfaceWorkWorld != null &&
        ReferenceEquals(ChunkMgr.ExistingInstance?.WorldRuntime, surfaceWorkWorld) &&
        surfaceWorkWorld.Epoch == surfaceWorkEpoch &&
        ReferenceEquals(GetActivePlanetData(), regionalPlanet);

    private void ApplySurfaceWork(ChunkRuntime chunk, float seconds, float precipitationSeconds,
        TemperatureMgr.AmbientChunkSnapshot temperature) =>
        WorldWeatherSurfaceSystem.AdvanceChunk(chunk, this, seconds, precipitationSeconds, temperature);

    /// <summary>冻结保存载荷期间只阻止新天气步，时间与地表队列消费照常运行。</summary>
    public IDisposable BeginSurfaceSaveCapture() => surfaceWork.BeginAdvanceBarrier();

    private void MaintainSurfaceWork()
    {
        if (surfaceWorkFrame == Time.frameCount)
            return;
        surfaceWorkFrame = Time.frameCount;
        if (surfaceWork.Count == 0)
            return;
        if (!IsSurfaceWorkCurrent)
        {
            surfaceWork.Dispose();
            return;
        }
        using var scope = SurfaceAdvanceMarker.Measure(context: "budget");
        int pendingBefore = surfaceWork.Count;
        // 完整区块事务至少执行一次，毫秒预算只决定是否继续下一块。
        surfaceWork.DrainSlice(SurfaceWorkHandler, SurfaceMillisecondsPerFrame);
        SurfaceAdvanceMarker.AddWork(pendingBefore - surfaceWork.Count);
        SurfaceAdvanceMarker.SetGauge(surfaceWork.Count);
    }

    /// <summary>保存、回拨和解绑前结清已有地表积分；失效世界只释放旧队列。</summary>
    public void FlushSurfaceWork(ChunkRuntime chunk = null)
    {
        if (surfaceWork.Count == 0)
            return;
        if (!IsSurfaceWorkCurrent)
        {
            surfaceWork.Dispose();
            return;
        }
        using var scope = SurfaceAdvanceMarker.Measure(context: "flush");
        int pendingBefore = surfaceWork.Count;
        if (chunk != null)
            surfaceWork.DrainChunk(chunk, SurfaceWorkHandler);
        else
            while (surfaceWork.DrainNext(SurfaceWorkHandler)) { }
        SurfaceAdvanceMarker.AddWork(pendingBefore - surfaceWork.Count);
        SurfaceAdvanceMarker.SetGauge(surfaceWork.Count);
    }

    /// <summary>自动保存只等待开始时已有的账目，后续天气继续按普通帧预算处理。</summary>
    public IEnumerator FlushSurfaceWorkForAutoSave(AutoSaveWorkBudget budget)
    {
        long throughSequence = surfaceWork.LastSequence;
        while (surfaceWork.Count > 0)
        {
            if (!IsSurfaceWorkCurrent)
            {
                surfaceWork.Dispose();
                yield break;
            }
            bool applied;
            using (SurfaceAdvanceMarker.Measure(context: "autosave"))
            {
                applied = surfaceWork.DrainNext(SurfaceWorkHandler, throughSequence);
                SurfaceAdvanceMarker.AddWork(applied ? 1 : 0);
                SurfaceAdvanceMarker.SetGauge(surfaceWork.Count);
            }
            if (!applied)
                yield break;
            if (budget.ShouldYield())
                yield return budget.WaitForNextSlice();
        }
    }

    #endregion

    #region 区域状态保存与手动天气

    private void PersistRegionalStates()
    {
        if (regionalPlanet == null || regionalEngine == null || !GameNetwork.HasStateAuthority) return;
        foreach (RegionalWeatherState state in regionalEngine.EnumerateStates())
            PersistRegionalState(state);
    }

    private void PersistRegionalState(RegionalWeatherState state)
    {
        RegionalWeatherRecord record = RegionalWeatherRecord.FromState(state);
        if (regionalSaveIndices.TryGetValue(state.Region, out int index)) regionalPlanet.RegionalWeatherStates[index] = record;
        else
        {
            regionalSaveIndices[state.Region] = regionalPlanet.RegionalWeatherStates.Count;
            regionalPlanet.RegionalWeatherStates.Add(record);
        }
    }

    public IReadOnlyList<RegionalWeatherStateSnapshot> CaptureRegionalWeatherStates()
    {
        var result = new List<RegionalWeatherStateSnapshot>();
        if (regionalEngine == null || regionalPlanet == null) return result;
        foreach (var state in regionalEngine.EnumerateStates())
            if (regionalEngine.TryGetRegionSnapshot(state.Region, out var sample))
                result.Add(new RegionalWeatherStateSnapshot(regionalPlanet.Name, sample));
        return result;
    }

    public void ApplyReplicatedRegionalWeatherState(RegionalWeatherStateSnapshot state, bool initialState = false)
    {
        if (GameNetwork.HasStateAuthority || !PrepareRegionalContext() ||
            !string.Equals(state.WorldKey, regionalPlanet.Name, StringComparison.Ordinal)) return;
        if (regionalEngine.TryGetRegionSnapshot(state.Region, out var old) &&
            (state.Revision < old.Revision || state.Revision == old.Revision && state.SampleTime < old.SampleTime)) return;
        RegionalWeatherSnapshot sample = state.ToSnapshot();
        regionalEngine.ApplyReplicatedSnapshot(sample);
        ObserveLightning(sample, initialState);
        CompleteWeatherAudioBaseline();
        UpdateLocalWeatherProjection();
        RefreshWeatherFeedback();
    }

    private void ObserveLightning(RegionalWeatherSnapshot sample, bool initialState)
    {
        bool hadBaseline = lightningSeen.TryGetValue(sample.Region, out int previous);
        lightningSeen[sample.Region] = sample.LightningSequence;
        if (initialState || !hadBaseline || sample.LightningSequence == previous || sample.LightningSequence <= 0) return;
        var strike = new RegionalLightningEvent(regionalPlanet.Name, sample.Region,
            new Vector2(sample.LightningCell.X + 0.5f, sample.LightningCell.Y + 0.5f),
            sample.LightningStrength, sample.LightningSequence);
        if (GameNetwork.HasStateAuthority)
            LightningStrikeSystem.Apply(strike, lightningDamageRadius,
                lightningMaximumDamage * Mathf.Clamp01(strike.Strength));
        LightningOccurred?.Invoke(strike);
    }

    // 调试雷击与自然雷击共用落点、伤害和复制链，只允许已加载地表。
    public bool StrikeLightningAt(Vector2 logicalPosition, float strength = 1f)
    {
        if (surfaceWork.IsAdvanceBlocked || !GameNetwork.HasStateAuthority || !PrepareRegionalContext() ||
            !TryGetCurrentTimeData(out var clock) || !float.IsFinite(logicalPosition.x) ||
            !float.IsFinite(logicalPosition.y) || !float.IsFinite(strength) || strength <= 0f ||
            ChunkMgr.ExistingInstance?.TryGetRestoredTerrainForEnvironment(logicalPosition, out var tile) != true ||
            tile.Terrain.WeatherProfile == null) return false;
        double now = GetRegionalStepTime(clock);
        regionalEngine.RegisterChunk(tile.Address.ChunkOrigin, tile.Terrain.WeatherProfile, now);
        Int2 cell = new(Mathf.FloorToInt(logicalPosition.x), Mathf.FloorToInt(logicalPosition.y));
        Int2 region = regionalEngine.ResolveRegion(cell);
        if (!lightningSeen.ContainsKey(region) && regionalEngine.TryGetRegionSnapshot(region, out var baseline))
            lightningSeen[region] = baseline.LightningSequence;
        RegionalWeatherSnapshot sample = regionalEngine.StrikeLightning(cell, now, Mathf.Clamp01(strength));
        ObserveLightning(sample, false);
        if (regionalEngine.TryGetState(region, out var state)) PersistRegionalState(state);
        AuthoritativeRegionalWeatherStateChanged?.Invoke(new RegionalWeatherStateSnapshot(regionalPlanet.Name, sample));
        return true;
    }

    [Sirenix.OdinInspector.Button("玩家上方三格雷击（有真实伤害）")]
    public void StrikeLightningDebug()
    {
        if (surfaceWork.IsAdvanceBlocked)
        {
            Debug.LogWarning("[WeatherMgr] 正在冻结存档天气，雷击操作请稍后重试。");
            return;
        }
        Player player = ItemMgr.GetInstance()?.User_Player;
        if (player != null && player.gameObject.activeInHierarchy)
            StrikeLightningAt(WorldLocalPresentation.ToLogical((Vector2)player.transform.position) + Vector2.up * 3f);
    }

    private void UpdateLocalWeatherProjection()
    {
        if (regionalPlanet == null || !TryGetLocalWeather(out var sample)) return;
        regionalPlanet.CurrentWeather = DescribeWeather(sample);
        regionalPlanet.WeatherPhase = DescribeWeatherPhase(sample);
        regionalPlanet.WeatherIntensity = sample.PrecipitationIntensity > 0f ? sample.PrecipitationIntensity :
            Mathf.Max(sample.CloudCoverage, sample.FogDensity);
        regionalPlanet.WindStrength = sample.WindStrength;
    }

    private bool ForceRegionalWeather(WeatherType weather, float intensity, float durationSeconds)
    {
        if (surfaceWork.IsAdvanceBlocked || !GameNetwork.HasStateAuthority ||
            !PrepareRegionalContext() || !TryGetCurrentTimeData(out var clock)) return false;
        AdvanceRegionalWeather();
        Camera camera = Camera.main;
        Vector2 position = camera != null ? (Vector2)camera.transform.position : (Vector2)transform.position;
        Int2 region = regionalEngine.ResolveRegion(new Int2(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)));
        double now = GetRegionalStepTime(clock);
        regionalEngine.Advance(region, now);
        if (weather == WeatherType.Clear) regionalEngine.Clear(region, now);
        else if (weather is WeatherType.Rain or WeatherType.Storm)
            regionalEngine.ForcePrecipitation(region, now, Mathf.Clamp01(intensity), Mathf.Max(1f, durationSeconds));
        else regionalEngine.ForceElements(region, now, durationSeconds,
            cloudCoverage: weather == WeatherType.Cloudy ? Mathf.Clamp01(intensity) : -1f,
            fogDensity: weather == WeatherType.Fog ? Mathf.Clamp01(intensity) : -1f);
        if (weather == WeatherType.Storm)
        {
            regionalEngine.ForceElement(region, now, WeatherElementIds.Wind, Mathf.Clamp01(intensity), durationSeconds);
            regionalEngine.ForceElement(region, now, WeatherElementIds.Thunder, Mathf.Clamp01(intensity), durationSeconds);
        }
        PersistRegionalStates();
        UpdateLocalWeatherProjection();
        if (regionalEngine.TryGetRegionSnapshot(region, out var sample))
            AuthoritativeRegionalWeatherStateChanged?.Invoke(new RegionalWeatherStateSnapshot(regionalPlanet.Name, sample));
        PublishAuthoritativeWeatherState();
        return true;
    }

    private double GetRegionalStepTime(TimeData clock) =>
        Math.Floor(clock.GetTotalGameTimeSeconds() / regionalSettings.StepSeconds) * regionalSettings.StepSeconds;

    #endregion
}
