using FlatWorld.Networking;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// 管理观察区域风力与 GPU 全局参数，设施消耗仍读取自身位置的风。
/// </summary>
public partial class WeatherMgr
{
    #region 区域风与风浪表现

    private static readonly int GlobalWindStrengthShaderId = Shader.PropertyToID("_GlobalWindStrength");
    private static readonly int OceanWaveFactorsShaderId = Shader.PropertyToID("_OceanWaveFactors");
    private static readonly int OceanWaveTimeShaderId = Shader.PropertyToID("_OceanWaveTime");
    private float oceanWaveTime; // 独立积分风浪时钟，风速变化不跳相位，也不改潮汐时钟

    /// <summary>只推进视觉风浪；暂停时停止，普通客户端读取已复制风力。</summary>
    private void AdvanceOceanWaveClock(float deltaTime)
    {
        if (!_weatherRuntimeAllowed)
            return;
        oceanWaveTime += Mathf.Max(0f, deltaTime) *
            WaterEnvironmentRules.ResolveOceanWaveFactors(GetCurrentWindStrength()).x;
        Shader.SetGlobalFloat(OceanWaveTimeShaderId, oceanWaveTime);
    }

    [ShowInInspector, ReadOnly, LabelText("观察区域风力")]
    public float CurrentWindStrength => GetCurrentWindStrength();

    /// <summary>读取当前维度可见的风力；禁用环境反馈的维度返回零。</summary>
    public float GetCurrentWindStrength()
    {
        if (!_weatherRuntimeAllowed)
            return 0f;

        return TryGetLocalWeather(out var sample) ? sample.WindStrength : 0f;
    }

    /// <summary>由状态权威限时覆盖观察区域风力。</summary>
    public void SetWindStrength(float strength)
    {
        if (!GameNetwork.HasStateAuthority)
        {
            if (EnableDebugLog)
                Debug.LogWarning("[WeatherMgr] 普通客户端不能修改权威风力状态。");
            return;
        }

        PlanetData planetData = GetActivePlanetData();
        if (planetData == null)
            return;

        if (!PrepareRegionalContext() || !TryGetCurrentTimeData(out var clock)) return;
        AdvanceRegionalWeather();
        Camera camera = Camera.main;
        Vector2 position = camera != null ? (Vector2)camera.transform.position : (Vector2)transform.position;
        FlatWorld.WorldModel.Int2 region = regionalEngine.ResolveRegion(new FlatWorld.WorldModel.Int2(
            Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)));
        double now = GetRegionalStepTime(clock);
        regionalEngine.Advance(region, now, SampleRegionalInput(ChunkMgr.ExistingInstance, region));
        regionalEngine.ForceElements(region, now, GetCurrentDayLength() * 0.15d, windStrength: Mathf.Clamp01(strength));
        PersistRegionalStates();
        UpdateLocalWeatherProjection();
        if (regionalEngine.TryGetRegionSnapshot(region, out var sample))
            AuthoritativeRegionalWeatherStateChanged?.Invoke(new RegionalWeatherStateSnapshot(planetData.Name, sample));
        PublishAuthoritativeWeatherState();
    }

    /// <summary>把观察区域风力一次性写入 Shader 全局参数。</summary>
    private void RefreshWindFeedback()
    {
        Shader.SetGlobalFloat(GlobalWindStrengthShaderId, GetCurrentWindStrength());
        Vector3 factors = WaterEnvironmentRules.ResolveOceanWaveFactors(GetCurrentWindStrength());
        Shader.SetGlobalVector(OceanWaveFactorsShaderId, new Vector4(factors.x, factors.y, factors.z, 0f));
    }

    /// <summary>离开世界或进入禁用天气的维度时清除残留风力表现。</summary>
    private void DeactivateWindFeedback()
    {
        oceanWaveTime = 0f;
        Shader.SetGlobalFloat(GlobalWindStrengthShaderId, 0f);
        Vector3 factors = WaterEnvironmentRules.ResolveOceanWaveFactors(0f);
        Shader.SetGlobalVector(OceanWaveFactorsShaderId, new Vector4(factors.x, factors.y, factors.z, 0f));
        Shader.SetGlobalFloat(OceanWaveTimeShaderId, 0f);
    }

    #endregion
}
