using FlatWorld.Networking;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// 管理星球级风力的权威读写与 GPU 全局参数；所有植被材质共享同一强度，避免逐对象更新。
/// </summary>
public partial class WeatherMgr
{
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

    [ShowInInspector, ReadOnly, LabelText("全局风力")]
    public float CurrentWindStrength => GetCurrentWindStrength();

    /// <summary>读取当前维度可见的风力；禁用环境反馈的维度返回零。</summary>
    public float GetCurrentWindStrength()
    {
        if (!_weatherRuntimeAllowed)
            return 0f;

        PlanetData planetData = GetActivePlanetData();
        return planetData != null ? Mathf.Clamp01(planetData.WindStrength) : 0f;
    }

    /// <summary>由状态权威修改并广播全局风力。</summary>
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

        float normalizedStrength = Mathf.Clamp01(strength);
        if (Mathf.Approximately(planetData.WindStrength, normalizedStrength))
            return;

        planetData.WindStrength = normalizedStrength;
        PublishAuthoritativeWeatherState();
    }

    /// <summary>把权威风力一次性写入 Shader 全局参数。</summary>
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
}
