using System;

public partial class DayTimeSystem
{
    #region 季节查询与设置
    public event Action SeasonSettingsChanged; // 当前世界季节配置变化
    private SeasonSettingsProvider seasonSettingsProvider; // 当前实例的设置入口

    /// <summary>随时间系统注册季节设置入口。</summary>
    private void RegisterSeasonSettings()
    {
        seasonSettingsProvider ??= new SeasonSettingsProvider(this);
        FlatWorld.Settings.SettingsProviderRegistry.Register(seasonSettingsProvider);
    }

    /// <summary>时间系统退出时撤销设置入口，避免沿用旧世界实例。</summary>
    private void UnregisterSeasonSettings() => FlatWorld.Settings.SettingsProviderRegistry.Unregister(seasonSettingsProvider);

    /// <summary>解析活动维度实际引用的世界时间，不创建新的世界或时钟。</summary>
    public bool TryGetActiveTimeData(out TimeData time) =>
        TryGetResolvedTimeData(GetCurrentActiveSceneName(), out _, out time);

    /// <summary>读取当前季节快照，供 HUD、环境与植物共享。</summary>
    public bool TryGetCurrentSeason(out SeasonSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetActiveTimeData(out TimeData time))
            return false;
        snapshot = SeasonCalendar.Sample(time);
        return true;
    }

    /// <summary>季节长度由自转、公转和离心率决定，不再允许独立改写四季天数。</summary>
    public bool TryChangeSeasonLengths(float spring, float summer, float autumn, float winter, out string error)
    {
        error = "季节长度由星球公转周期与轨道离心率自动计算，不能单独修改。";
        return false;
    }

    /// <summary>季节温差与天气使用同一维度资格，地下等禁止天气的维度不受地表四季影响。</summary>
    public static float GetSeasonTemperatureOffset(
        float poleProximity = 1f,
        float baselineCelsius = PlanetData.DefaultGlobalTemperature)
    {
        if (DimensionManager.ExistingInstance?.ActiveDefinition?.SuppressWeather == true)
            return 0f;
        if (Instance == null || !Instance.TryGetActiveTimeData(out TimeData time))
            return 0f;
        SeasonSnapshot snapshot = SeasonCalendar.Sample(time);
        return OrbitalSeasonPhysics.ResolveTemperatureOffset(
            time, snapshot, poleProximity, baselineCelsius, time.GetTotalGameTimeSeconds());
    }
    #endregion
}
