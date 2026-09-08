using System;
using FlatWorld.Networking;
using UnityEngine.SceneManagement;

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
        TryGetResolvedTimeData(SceneManager.GetActiveScene().name, out _, out time);

    /// <summary>读取当前季节快照，供 HUD、环境与植物共享。</summary>
    public bool TryGetCurrentSeason(out SeasonSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetActiveTimeData(out TimeData time))
            return false;
        snapshot = SeasonCalendar.Sample(time);
        return true;
    }

    /// <summary>只由权威端提交四季长度，并保留正在经历的季节进度。</summary>
    public bool TryChangeSeasonLengths(float spring, float summer, float autumn, float winter, out string error)
    {
        if (!GameNetwork.HasStateAuthority || !TryGetActiveTimeData(out TimeData time))
        {
            error = "请在自己管理的世界中调整季节。";
            return false;
        }
        try
        {
            SeasonCalendar.ChangeLengths(time, spring, summer, autumn, winter);
        }
        catch (System.IO.InvalidDataException)
        {
            error = "每个季节的天数必须是大于 0 的有限数值。";
            return false;
        }
        SeasonSettingsChanged?.Invoke();
        error = string.Empty;
        return true;
    }

    /// <summary>季节温差与天气使用同一维度资格，地下等禁止天气的维度不受地表四季影响。</summary>
    public static float GetSeasonTemperatureOffset()
    {
        if (DimensionManager.ExistingInstance?.ActiveDefinition?.SuppressWeather == true)
            return 0f;
        return Instance != null && Instance.TryGetCurrentSeason(out SeasonSnapshot snapshot)
            ? snapshot.TemperatureOffset : 0f;
    }
    #endregion
}
