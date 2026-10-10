using System;
using System.Collections.Generic;
using FlatWorld.Networking;
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

    [Header("区域自然天气")]
    [SerializeField] private RegionalWeatherSettings regionalSettings = new RegionalWeatherSettings();
    [SerializeField, Min(0f)] private float surfaceRainWaterPerSecond = 0.2f;
    [SerializeField, Min(0f)] private float meltWaterPerSnowDepth = 50f;
    public float SurfaceRainWaterPerSecond => surfaceRainWaterPerSecond;
    public float MeltWaterPerSnowDepth => meltWaterPerSnowDepth;
    public float PrecipitationFreezingTemperature => GetSnowConfig().FreezingTemperature;
    public static event Action<RegionalWeatherStateSnapshot> AuthoritativeRegionalWeatherStateChanged;
    public event Action<RegionalLightningEvent> LightningOccurred;

    private RegionalWeatherEngine regionalEngine;
    private PlanetData regionalPlanet;
    private WorldTopologyDomain regionalDomain;
    private double lastRegionalAdvance = double.NegativeInfinity;
    private bool regionalSettingsLoaded;
    private readonly HashSet<Int2> regionalWorkSet = new();
    private readonly List<Int2> regionalWork = new();
    private readonly Dictionary<Int2, RegionalWeatherInput> regionalInputs = new();
    private readonly Dictionary<Int2, RegionalWeatherAdvanceResult> regionalExposure = new();
    private readonly Dictionary<Int2, int> regionalSaveIndices = new();
    private readonly Dictionary<Int2, int> lightningSeen = new();
    private readonly Dictionary<RuntimeWorldAddress, ChunkRuntime> surfaceChunksLastStep = new();
    private readonly Dictionary<RuntimeWorldAddress, ChunkRuntime> surfaceChunksThisStep = new();
    private float surfaceStepSeconds;
    private readonly float[] sampleTemperatures = new float[5];
    private LightningEffectController lightningEffect;

    // 状态只在推进入口创建；查询不抽随机数、不生成地形、不扣湿度。
    public bool TryGetWeatherAt(Vector2 position, out RegionalWeatherSnapshot sample)
    {
        sample = default;
        return _weatherRuntimeAllowed && regionalEngine != null &&
               float.IsFinite(position.x) && float.IsFinite(position.y) &&
               regionalEngine.TryGetSnapshot(new Int2(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)), out sample);
    }

    public float GetTemperatureOffsetAt(Vector2 position) =>
        TryGetWeatherAt(position, out var sample) ? sample.CoolingOffsetCelsius : 0f;

    public float GetAirHumidityDeficitAt(Vector2 position) =>
        TryGetWeatherAt(position, out var sample) ? sample.HumidityDeficit : 0f;

    /// <summary>雨雪按同一份当地实时温度分流，核心温度查询只读取上面的雨冷接口。</summary>
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
            domain.IsWrapped == regionalDomain.IsWrapped &&
            domain.Min.Equals(regionalDomain.Min) && domain.Span.Equals(regionalDomain.Span))
            return true;

        if (!regionalSettingsLoaded)
        {
            TextAsset configuration = Resources.Load<TextAsset>("Config/Weather/regional-weather");
            if (configuration != null) regionalSettings = JsonUtility.FromJson<RegionalWeatherSettings>(configuration.text);
            regionalSettings ??= new RegionalWeatherSettings();
            regionalSettings.FreezingTemperature = PrecipitationFreezingTemperature;
            regionalSettings.Validate();
            regionalSettingsLoaded = true;
        }
        regionalPlanet = planet;
        regionalDomain = domain;
        regionalEngine = new RegionalWeatherEngine(regionalSettings, GetDeterministicSeed(), domain);
        regionalSaveIndices.Clear();
        lightningSeen.Clear();
        surfaceChunksLastStep.Clear();
        surfaceChunksThisStep.Clear();
        regionalPlanet.RegionalWeatherStates ??= new List<RegionalWeatherRecord>();
        var restoredSnapshots = new List<RegionalWeatherSnapshot>();
        for (int i = 0; i < regionalPlanet.RegionalWeatherStates.Count; i++)
        {
            RegionalWeatherRecord record = regionalPlanet.RegionalWeatherStates[i];
            if (record == null) throw new InvalidOperationException("区域天气档案包含空记录。");
            RegionalWeatherState state = record.ToState();
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

    // 所有区域先采上一步输入，再统一推进，避免遍历次序影响邻区温度。
    private void AdvanceRegionalWeather()
    {
        if (!PrepareRegionalContext() || !GameNetwork.HasStateAuthority ||
            !TryGetCurrentTimeData(out TimeData clock)) return;
        double now = GetRegionalStepTime(clock);
        if (now < lastRegionalAdvance)
        {
            regionalEngine.RebaseTime(now - lastRegionalAdvance);
            lastRegionalAdvance = double.NegativeInfinity;
            surfaceChunksLastStep.Clear();
        }
        if (now - lastRegionalAdvance < regionalSettings.StepSeconds) return;
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (chunks?.WorldRuntime == null) return;

        regionalWorkSet.Clear();
        surfaceChunksThisStep.Clear();
        foreach (var pair in chunks.Chunks)
        {
            ChunkRuntime chunk = pair.Value;
            if (!chunks.IsTerrainRestoredForEnvironment(chunk)) continue;
            surfaceChunksThisStep[chunk.Address] = chunk;
            // 固定网格取区块内部及边缘，较大区块也不会漏掉中间的天气区域。
            int stride = Math.Max(1, regionalSettings.RegionSizeTiles / 2);
            for (int y = 0; y < chunk.Terrain.Height; y += stride)
            {
                for (int x = 0; x < chunk.Terrain.Width; x += stride)
                    regionalWorkSet.Add(regionalEngine.ResolveRegion(new Int2(chunk.Address.ChunkOrigin.X + x,
                        chunk.Address.ChunkOrigin.Y + y)));
                regionalWorkSet.Add(regionalEngine.ResolveRegion(new Int2(chunk.Address.ChunkOrigin.X + chunk.Terrain.Width - 1,
                    chunk.Address.ChunkOrigin.Y + y)));
            }
            for (int x = 0; x < chunk.Terrain.Width; x += stride)
                regionalWorkSet.Add(regionalEngine.ResolveRegion(new Int2(chunk.Address.ChunkOrigin.X + x,
                    chunk.Address.ChunkOrigin.Y + chunk.Terrain.Height - 1)));
            regionalWorkSet.Add(regionalEngine.ResolveRegion(new Int2(chunk.Address.ChunkOrigin.X + chunk.Terrain.Width - 1,
                chunk.Address.ChunkOrigin.Y + chunk.Terrain.Height - 1)));
        }
        foreach (RegionalWeatherState state in regionalEngine.EnumerateStates())
            if (state.PrecipitationIntensity > 0f || state.HumidityDeficit > 0f || state.CoolingAmountCelsius > 0f)
                regionalWorkSet.Add(state.Region);
        regionalWork.Clear();
        regionalWork.AddRange(regionalWorkSet);
        regionalWork.Sort((a, b) => a.Y == b.Y ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        regionalInputs.Clear();
        foreach (Int2 region in regionalWork) regionalInputs[region] = SampleRegionalInput(chunks, region);
        regionalExposure.Clear();
        foreach (Int2 region in regionalWork)
        {
            RegionalWeatherAdvanceResult result = regionalEngine.Advance(region, now, regionalInputs[region]);
            regionalExposure[region] = result;
            ObserveLightning(result.Snapshot, false);
            AuthoritativeRegionalWeatherStateChanged?.Invoke(new RegionalWeatherStateSnapshot(regionalPlanet.Name, result.Snapshot));
        }

        // 未加载期间只补天气状态，地块只接收本次已实际加载时间内的降水。
        float elapsed = double.IsNegativeInfinity(lastRegionalAdvance) ? 0f :
            (float)Math.Min(now - lastRegionalAdvance, regionalSettings.StepSeconds * regionalSettings.MaximumCatchUpSteps);
        surfaceStepSeconds = elapsed;
        if (elapsed > 0f)
            foreach (var pair in surfaceChunksThisStep)
                if (surfaceChunksLastStep.TryGetValue(pair.Key, out var previous) && ReferenceEquals(previous, pair.Value))
                    WorldWeatherSurfaceSystem.AdvanceChunk(pair.Value, this, elapsed, ResolveSurfaceExposure);
        surfaceChunksLastStep.Clear();
        foreach (var pair in surfaceChunksThisStep) surfaceChunksLastStep[pair.Key] = pair.Value;
        lastRegionalAdvance = now;
        PersistRegionalStates();
        UpdateLocalWeatherProjection();
    }

    private RegionalWeatherAdvanceResult ResolveSurfaceExposure(Int2 cell)
    {
        RegionalWeatherAdvanceResult value = regionalEngine.SampleExposureAt(cell, regionalExposure);
        float total = value.RainExposureSeconds + value.SnowExposureSeconds;
        float scale = total > surfaceStepSeconds && total > 0f ? surfaceStepSeconds / total : 1f;
        return new RegionalWeatherAdvanceResult(value.Snapshot, value.RainExposureSeconds * scale, value.SnowExposureSeconds * scale);
    }

    private RegionalWeatherInput SampleRegionalInput(ChunkMgr chunks, Int2 region)
    {
        if (chunks == null) return default;
        var input = new RegionalWeatherInput();
        for (int i = 0; i < 5; i++)
        {
            Int2 cell = regionalEngine.GetFixedSampleCell(region, i);
            Vector2 position = new(cell.X + 0.5f, cell.Y + 0.5f);
            if (!chunks.TryGetRestoredTerrainForEnvironment(position, out var tile) ||
                !chunks.TryGetAirHumiditySupply(position, out float supply) ||
                !TemperatureMgr.Instance.TryGetAmbientTemperature(position, out float temperature) ||
                !tile.Terrain.TryGetEnvironmentValue("precipitation", tile.LocalCell.x, tile.LocalCell.y, out float precipitation) ||
                !tile.Terrain.TryGetEnvironmentValue("windX", tile.LocalCell.x, tile.LocalCell.y, out float windX) ||
                !tile.Terrain.TryGetEnvironmentValue("windY", tile.LocalCell.x, tile.LocalCell.y, out float windY))
                return default;
            sampleTemperatures[i] = temperature;
            temperature -= GetTemperatureOffsetAt(position);
            input.AirHumiditySupply += supply * 0.2f;
            input.TemperatureBeforeRainCooling += temperature * 0.2f;
            input.GeographicPrecipitation += precipitation * 0.2f;
            input.GeographicWindX += windX * 0.2f;
            input.GeographicWindY += windY * 0.2f;
        }
        input.TemperatureGradientX = (sampleTemperatures[2] + sampleTemperatures[4] - sampleTemperatures[1] - sampleTemperatures[3]) * 0.5f;
        input.TemperatureGradientY = (sampleTemperatures[3] + sampleTemperatures[4] - sampleTemperatures[1] - sampleTemperatures[2]) * 0.5f;
        input.IsComplete = true;
        return input;
    }

    private void PersistRegionalStates()
    {
        if (regionalPlanet == null || regionalEngine == null || !GameNetwork.HasStateAuthority) return;
        foreach (RegionalWeatherState state in regionalEngine.EnumerateStates())
        {
            RegionalWeatherRecord record = RegionalWeatherRecord.FromState(state);
            if (regionalSaveIndices.TryGetValue(state.Region, out int index)) regionalPlanet.RegionalWeatherStates[index] = record;
            else
            {
                regionalSaveIndices[state.Region] = regionalPlanet.RegionalWeatherStates.Count;
                regionalPlanet.RegionalWeatherStates.Add(record);
            }
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
        UpdateLocalWeatherProjection();
        RefreshWeatherFeedback();
    }

    private void ObserveLightning(RegionalWeatherSnapshot sample, bool initialState)
    {
        bool hadBaseline = lightningSeen.TryGetValue(sample.Region, out int previous);
        lightningSeen[sample.Region] = Math.Max(previous, sample.LightningSequence);
        if (initialState || !hadBaseline || sample.LightningSequence <= previous) return;
        Int2 center = regionalEngine.GetFixedSampleCell(sample.Region, 0);
        LightningOccurred?.Invoke(new RegionalLightningEvent(regionalPlanet.Name, sample.Region,
            new Vector2(center.X + 0.5f, center.Y + 0.5f), sample.LightningActivity, sample.LightningSequence));
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

    private void ForceRegionalWeather(WeatherType weather, float intensity, float durationSeconds)
    {
        if (!PrepareRegionalContext() || !GameNetwork.HasStateAuthority || !TryGetCurrentTimeData(out var clock)) return;
        AdvanceRegionalWeather();
        Camera camera = Camera.main;
        Vector2 position = camera != null ? (Vector2)camera.transform.position : (Vector2)transform.position;
        Int2 region = regionalEngine.ResolveRegion(new Int2(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)));
        double now = GetRegionalStepTime(clock);
        regionalEngine.Advance(region, now, SampleRegionalInput(ChunkMgr.ExistingInstance, region));
        if (weather == WeatherType.Clear) regionalEngine.Clear(region, now);
        else if (weather is WeatherType.Rain or WeatherType.Storm)
            regionalEngine.ForcePrecipitation(region, now, Mathf.Clamp01(intensity), Mathf.Max(1f, durationSeconds));
        else regionalEngine.ForceElements(region, now, durationSeconds,
            cloudCoverage: weather == WeatherType.Cloudy ? Mathf.Clamp01(intensity) : -1f,
            fogDensity: weather == WeatherType.Fog ? Mathf.Clamp01(intensity) : -1f);
        PersistRegionalStates();
        UpdateLocalWeatherProjection();
        if (regionalEngine.TryGetRegionSnapshot(region, out var sample))
            AuthoritativeRegionalWeatherStateChanged?.Invoke(new RegionalWeatherStateSnapshot(regionalPlanet.Name, sample));
        PublishAuthoritativeWeatherState();
    }

    private double GetRegionalStepTime(TimeData clock) =>
        Math.Floor(clock.GetTotalGameTimeSeconds() / regionalSettings.StepSeconds) * regionalSettings.StepSeconds;

    #endregion
}
