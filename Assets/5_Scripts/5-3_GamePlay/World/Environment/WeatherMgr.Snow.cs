using System;
using UnityEngine;

public partial class WeatherMgr
{
    #region 积雪查询

    private SnowCoverConfig snowConfig; // 独立平衡资源。
    private GameObject snowEffect; // 正式粒子资源实例。
    private RainEffectController snowController; // 复用相机覆盖和强度控制能力。
    private bool lastSnowing; // 温度变化也能触发雨雪切换。

    /// <summary>当前镜头所在地已由降雨和环境温度判定为降雪。</summary>
    public bool IsSnowingNow => lastSnowing;

    /// <summary>降雨与低温共同触发或结束降雪时通知积雪表现。</summary>
    public event Action<bool> SnowingChanged;

    /// <summary>按当地实际气温判断雨雪，冬季暖区仍可降雨，矿洞不产生降雪。</summary>
    public bool IsSnowingAt(Vector3 position) => TryGetPrecipitationAt(position, out _, out float snow) && snow > 0.001f;
    /// <summary>所有覆盖渲染共用持久化气候状态，查询不会加载区块。</summary>
    public float GetSnowCoverage(Vector3 position)
    {
        return WorldSnowSystem.GetSurfaceDepth(position);
    }

    /// <summary>区块表现一次取得积雪状态与星球基温修正，逐格采样直接读取已绑定地形。</summary>
    public bool TryGetSnowCoverageContext(out SnowCoverState snow, out float baselineOffset)
    {
        snow = null;
        baselineOffset = 0f;
        if (!_weatherRuntimeAllowed) return false;

        PlanetData planet = GetActivePlanetData()
            ?? throw new InvalidOperationException("积雪表现缺少当前星球数据。");
        snow = planet.SeasonalSnow
            ?? throw new InvalidOperationException("当前星球缺少积雪状态。");
        baselineOffset = planet.GlobalTemperature - PlanetData.DefaultGlobalTemperature;
        return true;
    }

    #endregion

    #region 积雪推进

    /// <summary>读取专属资源，配置缺失直接提示资源装配错误。</summary>
    private SnowCoverConfig GetSnowConfig()
    {
        if (snowConfig == null) snowConfig = Resources.Load<SnowCoverConfig>("Weather/SnowCoverConfig");
        if (snowConfig == null) throw new MissingReferenceException("缺少 Weather/SnowCoverConfig 积雪配置。");
        return snowConfig;
    }
    #endregion

    #region 降雪表现

    /// <summary>雨雪粒子互斥；粒子仅为表现，不承担积雪存档。</summary>
    private bool RefreshSnowEffect()
    {
        Camera camera = Camera.main;
        float snowIntensity = camera != null && TryGetPrecipitationAt(camera.transform.position, out _, out float snow) ? snow : 0f;
        bool active = snowIntensity > 0.001f;
        SetSnowingState(active);
        if (!active) { if (snowEffect != null) snowEffect.SetActive(false); return false; }
        if (snowEffect == null)
        {
            GameObject prefab = Resources.Load<GameObject>("Weather/SnowEffect");
            snowEffect = Instantiate(prefab, transform);
            snowController = snowEffect.GetComponent<RainEffectController>();
        }
        snowEffect.SetActive(true);
        snowController.SyncToCamera(camera, snowIntensity);
        return true;
    }

    /// <summary>只在雨雪判定真正切换时发布状态，不让区块自行轮询天气。</summary>
    private void SetSnowingState(bool snowing)
    {
        if (lastSnowing == snowing) return;
        lastSnowing = snowing;
        SnowingChanged?.Invoke(snowing);
    }
    /// <summary>同一天气阶段内跨冷暖区域时也及时更新雨雪与声音。</summary>
    private void MaintainSnowPresentation()
    {
        Camera camera = Camera.main;
        bool snowing = camera != null && IsSnowingAt(camera.transform.position);
        if (snowing != lastSnowing) RefreshWeatherFeedback();
    }

    #endregion
}
