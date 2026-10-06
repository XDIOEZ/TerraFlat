using System;
using System.Collections.Generic;
using FlatWorld.Settings;

/// <summary>世界季节只读入口；四季长度由轨道物理派生，不再提供独立滑块。</summary>
public sealed class SeasonSettingsProvider : ISettingsProvider
{
    public const string Id = "flatworld.seasons";
    private readonly DayTimeSystem owner; // 当前时间系统
    private readonly ISettingsSlider[] sliders; // 通用设置元数据
    public string ProviderId => Id;
    public string DisplayName => "季节周期";
    public int Order => 80;
    public IReadOnlyList<ISettingsSlider> SliderSettings => sliders;
    public IReadOnlyList<ISettingsToggle> ToggleSettings => Array.Empty<ISettingsToggle>();
    public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
    public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();

    /// <summary>只注册读取入口，避免通用设置系统重新暴露可写季节长度。</summary>
    public SeasonSettingsProvider(DayTimeSystem timeSystem)
    {
        owner = timeSystem;
        sliders = Array.Empty<ISettingsSlider>();
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

    /// <summary>保留旧调用边界，但写入会被时间系统拒绝。</summary>
    public bool TryApply(float spring, float summer, float autumn, float winter, out string error) =>
        owner.TryChangeSeasonLengths(spring, summer, autumn, winter, out error);

    public void ResetToDefaults() { }
}
