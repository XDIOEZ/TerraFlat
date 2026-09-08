using System;
using System.Collections.Generic;
using FlatWorld.Settings;

/// <summary>世界季节设置的业务入口；元数据不依赖 UI，四季草稿通过一次提交原子生效。</summary>
public sealed class SeasonSettingsProvider : ISettingsProvider
{
    public const string Id = "flatworld.seasons";
    private readonly DayTimeSystem owner; // 当前时间系统
    private readonly ISettingsSlider[] sliders; // 通用设置元数据
    public string ProviderId => Id;
    public string DisplayName => "季节长度";
    public int Order => 80;
    public IReadOnlyList<ISettingsSlider> SliderSettings => sliders;
    public IReadOnlyList<ISettingsToggle> ToggleSettings => Array.Empty<ISettingsToggle>();
    public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
    public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();

    /// <summary>为四季建立独立设置项，具体输入视图可以批量提交任意有限正数。</summary>
    public SeasonSettingsProvider(DayTimeSystem timeSystem)
    {
        owner = timeSystem;
        string[] keys = { "spring", "summer", "autumn", "winter" };
        string[] labels = { "春季", "夏季", "秋季", "冬季" };
        sliders = new ISettingsSlider[4];
        for (int index = 0; index < sliders.Length; index++)
        {
            WorldSeason season = (WorldSeason)index;
            sliders[index] = new SettingsSlider(
                new SettingDescriptor("season." + keys[index], labels[index], SettingControlType.Slider, "世界"),
                0.25f, 120f, 0.25f,
                () => TryRead(out SeasonCycleSettings settings) ? settings.GetDays(season) : 6f,
                value => SetSingle(season, value));
        }
    }

    /// <summary>读取当前世界的独立副本，界面编辑不能提前修改真实日历。</summary>
    public bool TryRead(out SeasonCycleSettings settings)
    {
        settings = null;
        if (owner == null || !owner.TryGetActiveTimeData(out TimeData time))
            return false;
        settings = time.Seasons.Copy();
        return true;
    }

    /// <summary>一次提交四季长度，校验和日历进度调整由时间系统负责。</summary>
    public bool TryApply(float spring, float summer, float autumn, float winter, out string error) =>
        owner.TryChangeSeasonLengths(spring, summer, autumn, winter, out error);

    /// <summary>通用单项设置保留其他三个季节的长度。</summary>
    private void SetSingle(WorldSeason season, float value)
    {
        if (!TryRead(out SeasonCycleSettings settings))
            return;
        switch (season)
        {
            case WorldSeason.Spring: settings.SpringDays = value; break;
            case WorldSeason.Summer: settings.SummerDays = value; break;
            case WorldSeason.Autumn: settings.AutumnDays = value; break;
            case WorldSeason.Winter: settings.WinterDays = value; break;
        }
        TryApply(settings.SpringDays, settings.SummerDays, settings.AutumnDays, settings.WinterDays, out _);
    }

    /// <summary>恢复默认每季六天，继续保留当前季节进度。</summary>
    public void ResetToDefaults() => TryApply(6f, 6f, 6f, 6f, out _);
}
