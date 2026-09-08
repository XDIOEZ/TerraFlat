using UnityEngine;

public partial class WeatherMgr
{
    private SnowCoverConfig snowConfig; // 独立平衡资源。
    private GameObject snowEffect; // 正式粒子资源实例。
    private RainEffectController snowController; // 复用相机覆盖和强度控制能力。
    private bool lastSnowing; // 温度变化也能触发雨雪切换。

    /// <summary>按当地实际气温判断雨雪，冬季暖区仍可降雨，矿洞不产生降雪。</summary>
    public bool IsSnowingAt(Vector3 position) => IsRaining() &&
        TemperatureMgr.Instance.TryGetAmbientTemperature(position, out float temperature) &&
        temperature <= GetSnowConfig().FreezingTemperature;
    /// <summary>所有覆盖渲染共用持久化气候状态，查询不会加载区块。</summary>
    public float GetSnowCoverage(Vector3 position)
    {
        if (!_weatherRuntimeAllowed || !TemperatureMgr.Instance.TryGetClimateBaseline(position, out float baseline)) return 0f;
        return GetActivePlanetData().SeasonalSnow.Sample(baseline);
    }
    /// <summary>读取专属资源，配置缺失直接提示资源装配错误。</summary>
    private SnowCoverConfig GetSnowConfig()
    {
        if (snowConfig == null) snowConfig = Resources.Load<SnowCoverConfig>("Weather/SnowCoverConfig");
        if (snowConfig == null) throw new MissingReferenceException("缺少 Weather/SnowCoverConfig 积雪配置。");
        return snowConfig;
    }
    /// <summary>按天气边界与季节时间细分跳时，未显示的区块也积累相同的雪。</summary>
    private int AdvanceWeatherAndSnow(PlanetData planet, float from, float to)
    {
        if (planet.SeasonalSnow.Initialized && planet.SeasonalSnow.LastTotalTime <= to)
            from = planet.SeasonalSnow.LastTotalTime;
        planet.SeasonalSnow.Initialized = true;
        float dayLength = GetCurrentDayLength();
        int seed = GetDeterministicSeed();
        WeatherEventScheduler.InitializeIfNeeded(planet, from, dayLength, seed, _rainEventConfig);
        if (!DayTimeSystem.Instance.TryGetActiveTimeData(out TimeData clock)) return 0;
        int transitions = 0;
        float cursor = from;
        while (cursor < to)
        {
            float boundary = WeatherEventScheduler.GetCurrentBoundary(planet);
            if (boundary <= cursor)
            {
                transitions += WeatherEventScheduler.Advance(planet, cursor - 1f, cursor, dayLength, seed, _rainEventConfig);
                continue;
            }
            float end = Mathf.Min(to, Mathf.Min(cursor + Mathf.Max(1f, dayLength / 24f), boundary));
            if (end > cursor)
            {
                double midpoint = (cursor + (double)end) * 0.5d;
                float offset = SeasonCalendar.SampleHistorical(clock, midpoint / dayLength).TemperatureOffset + CalculateWeatherTemperatureOffset(planet);
                float precipitation = planet.CurrentWeather is WeatherType.Rain or WeatherType.Storm ? planet.WeatherIntensity : 0f;
                SnowCoverSimulation.Advance(planet.SeasonalSnow, GetSnowConfig(), offset, precipitation, end - cursor);
            }
            transitions += WeatherEventScheduler.Advance(planet, cursor, end, dayLength, seed, _rainEventConfig);
            cursor = end;
        }
        planet.SeasonalSnow.LastTotalTime = to;
        return transitions;
    }
    /// <summary>雨雪粒子互斥；粒子仅为表现，不承担积雪存档。</summary>
    private bool RefreshSnowEffect()
    {
        Camera camera = Camera.main;
        bool active = camera != null && IsSnowingAt(camera.transform.position);
        lastSnowing = active;
        if (!active) { if (snowEffect != null) snowEffect.SetActive(false); return false; }
        if (snowEffect == null)
        {
            GameObject prefab = Resources.Load<GameObject>("Weather/SnowEffect");
            snowEffect = Instantiate(prefab, transform);
            snowController = snowEffect.GetComponent<RainEffectController>();
        }
        snowEffect.SetActive(true);
        snowController.SyncToCamera(camera, CurrentWeatherIntensity);
        return true;
    }
    /// <summary>同一天气阶段内跨冷暖区域时也及时更新雨雪与声音。</summary>
    private void MaintainSnowPresentation()
    {
        Camera camera = Camera.main;
        bool snowing = camera != null && IsSnowingAt(camera.transform.position);
        if (snowing != lastSnowing) RefreshWeatherFeedback();
    }
}
