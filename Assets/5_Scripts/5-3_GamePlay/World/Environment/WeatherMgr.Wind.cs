using FlatWorld.Networking;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// 管理星球级风力的权威读写与 GPU 全局参数；所有植被材质共享同一强度，避免逐对象更新。
/// </summary>
public partial class WeatherMgr
{
    private static readonly int GlobalWindStrengthShaderId = Shader.PropertyToID("_GlobalWindStrength");

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
    }

    /// <summary>离开世界或进入禁用天气的维度时清除残留风力表现。</summary>
    private static void DeactivateWindFeedback()
    {
        Shader.SetGlobalFloat(GlobalWindStrengthShaderId, 0f);
    }
}
