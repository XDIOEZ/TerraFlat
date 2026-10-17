using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using Newtonsoft.Json.Linq;

/// <summary>使用独立内存 Provider 采样正式设置会话，不写 PlayerPrefs 或操作当前窗口。</summary>
public static partial class GameplayTestSystemProbe
{
    #region 设置会话规则采样

    private static JObject ServiceSettingsSessionValues(JObject args)
    {
        string mode = Text(args, "mode", "unchanged");
        using var session = new SettingsSessionProbeScope();
        var provider = session.Register(new SettingsSessionProbeProvider());
        bool nestedSessionRejected = !SettingsProviderRegistry.BeginEditSession();
        if (mode != "unchanged") provider.WriteControls(1);
        if (mode == "roundtrip") provider.WriteControls(0);
        if (mode == "mixed")
        {
            provider.SliderSettings[0].SetValue(0);
            provider.SwitchSettings[0].TrySetSelectedIndex(0, out _);
        }
        bool changed = SettingsProviderRegistry.HasEditSessionChanges();
        provider.ResetCalls();
        bool restored = session.Discard(out string error, out bool attempted);
        JObject sample = provider.Sample();
        sample["mode"] = mode;
        sample["changed"] = changed;
        sample["restored"] = restored;
        sample["attempted"] = attempted;
        sample["error"] = error;
        sample["nestedSessionRejected"] = nestedSessionRejected;
        sample["secondDiscardSucceeded"] = SettingsProviderRegistry.DiscardEditSession(out _, out bool secondAttempt);
        sample["secondAttempted"] = secondAttempt;
        return sample;
    }

    private static JObject ServiceSettingsSessionCommit(JObject args)
    {
        bool modifyAfterCommit = Flag(args, "modifyAfterCommit", true);
        using var session = new SettingsSessionProbeScope();
        var provider = session.Register(new SettingsSessionTrackedProbeProvider());
        provider.WriteControls(1);
        provider.HiddenValue = 7;
        int committedProviders = SettingsProviderRegistry.CommitEditSession();
        bool cleanAfterCommit = !SettingsProviderRegistry.HasEditSessionChanges();
        if (modifyAfterCommit)
        {
            provider.WriteControls(0);
            provider.HiddenValue = 9;
        }
        provider.ResetCalls();
        bool restored = session.Discard(out string error, out bool attempted);
        JObject sample = provider.Sample();
        sample["modifyAfterCommit"] = modifyAfterCommit;
        sample["committedProviders"] = committedProviders;
        sample["cleanAfterCommit"] = cleanAfterCommit;
        sample["restored"] = restored;
        sample["attempted"] = attempted;
        sample["error"] = error;
        return sample;
    }

    private static JObject ServiceSettingsSessionParticipants(JObject args)
    {
        string mode = Text(args, "mode", "unchanged");
        using var session = new SettingsSessionProbeScope();
        SettingsSessionParticipantProbeProvider provider = mode == "untracked"
            ? new SettingsSessionParticipantProbeProvider()
            : new SettingsSessionTrackedProbeProvider();
        session.Register(provider);
        if (mode == "changed") provider.HiddenValue = 9;
        bool changed = SettingsProviderRegistry.HasEditSessionChanges();
        provider.ResetCalls();
        bool restored = session.Discard(out string error, out bool attempted);
        JObject sample = provider.Sample();
        sample["mode"] = mode;
        sample["changed"] = changed;
        sample["restored"] = restored;
        sample["attempted"] = attempted;
        sample["error"] = error;
        return sample;
    }

    private static JObject ServiceSettingsSessionErrors(JObject args)
    {
        string mode = Text(args, "mode", "write");
        using var session = new SettingsSessionProbeScope();
        var broken = session.Register(new SettingsSessionTrackedProbeProvider { Order = int.MaxValue - 1 });
        var survivor = session.Register(new SettingsSessionProbeProvider());
        broken.WriteControls(1);
        broken.HiddenValue = 9;
        survivor.ToggleSettings[0].SetValue(true);
        broken.FailToggleWrite = mode == "write";
        broken.FailDropdownWrite = mode == "write";
        broken.FailParticipantRestore = mode == "write";
        broken.FailToggleRead = mode == "read";
        broken.FailTracker = mode == "tracker";
        broken.ResetCalls();
        survivor.ResetCalls();
        bool restored = session.Discard(out string error, out bool attempted);
        return new JObject
        {
            ["mode"] = mode, ["restored"] = restored, ["attempted"] = attempted,
            ["error"] = error, ["broken"] = broken.Sample(), ["survivor"] = survivor.Sample(),
            ["sessionEnded"] = !SettingsProviderRegistry.HasEditSessionChanges()
        };
    }

    #endregion

    #region 独立设置 Provider 与会话所有权

    private sealed class SettingsSessionProbeScope : IDisposable
    {
        private readonly List<ISettingsProvider> ownedProviders = new List<ISettingsProvider>();
        private bool ownsSession;

        public SettingsSessionProbeScope()
        {
            // 已有会话时直接拒绝，不能覆盖或清掉用户正在编辑的设置。
            ownsSession = SettingsProviderRegistry.BeginEditSession();
            if (!ownsSession) throw new InvalidOperationException("已有活动设置会话，拒绝运行独立规则夹具。");
        }

        public T Register<T>(T provider) where T : ISettingsProvider
        {
            ownedProviders.Add(provider);
            SettingsProviderRegistry.Register(provider);
            return provider;
        }

        public bool Discard(out string error, out bool attempted)
        {
            ownsSession = false;
            return SettingsProviderRegistry.DiscardEditSession(out error, out attempted);
        }

        public void Dispose()
        {
            try
            {
                if (ownsSession) SettingsProviderRegistry.DiscardEditSession(out _);
            }
            finally
            {
                foreach (ISettingsProvider provider in ownedProviders)
                    SettingsProviderRegistry.Unregister(provider);
            }
        }
    }

    private class SettingsSessionProbeProvider : ISettingsProvider
    {
        private static readonly SettingOption[] Options =
        {
            new SettingOption("zero", "零"), new SettingOption("one", "一")
        };
        protected readonly int[] Calls = new int[4];
        public bool ToggleValue;
        public float SliderValue;
        public int DropdownValue;
        public int SwitchValue;
        public bool FailToggleRead;
        public bool FailToggleWrite;
        public bool FailDropdownWrite;
        public string ProviderId { get; } = "lua-settings-session-" + Guid.NewGuid().ToString("N");
        public string DisplayName => "独立设置会话规则";
        public int Order { get; set; } = int.MaxValue;
        public IReadOnlyList<ISettingsToggle> ToggleSettings { get; }
        public IReadOnlyList<ISettingsSlider> SliderSettings { get; }
        public IReadOnlyList<ISettingsDropdown> DropdownSettings { get; }
        public IReadOnlyList<ISettingsSwitch> SwitchSettings { get; }

        public SettingsSessionProbeProvider()
        {
            ToggleSettings = new ISettingsToggle[] { new SettingsToggle(
                Descriptor("toggle", SettingControlType.Toggle),
                () => FailToggleRead ? throw new InvalidOperationException("probe-getter") : ToggleValue,
                value =>
                {
                    Calls[0]++;
                    ToggleValue = value;
                    if (FailToggleWrite) throw new InvalidOperationException("probe-toggle");
                }) };
            SliderSettings = new ISettingsSlider[] { new SettingsSlider(
                Descriptor("slider", SettingControlType.Slider), 0, 2, 1,
                () => SliderValue, value => { Calls[1]++; SliderValue = value; }) };
            DropdownSettings = new ISettingsDropdown[] { new SettingsDropdown(
                Descriptor("dropdown", SettingControlType.Dropdown), Options, () => DropdownValue,
                value =>
                {
                    Calls[2]++;
                    if (FailDropdownWrite) return "probe-dropdown";
                    DropdownValue = value;
                    return null;
                }) };
            SwitchSettings = new ISettingsSwitch[] { new SettingsSwitch(
                Descriptor("switch", SettingControlType.Switch), Options, () => SwitchValue,
                value => { Calls[3]++; SwitchValue = value; return null; }) };
        }

        private static SettingDescriptor Descriptor(string key, SettingControlType type) =>
            new SettingDescriptor("probe." + key, key, type);

        public void WriteControls(int value)
        {
            ToggleSettings[0].SetValue(value != 0);
            SliderSettings[0].SetValue(value * 2);
            DropdownSettings[0].TrySetSelectedIndex(value, out _);
            SwitchSettings[0].TrySetSelectedIndex(value, out _);
        }

        public void ResetCalls() => Array.Clear(Calls, 0, Calls.Length);
        public void ResetToDefaults() => WriteControls(0);
        public virtual JObject Sample() => new JObject
        {
            ["toggle"] = ToggleValue, ["slider"] = SliderValue,
            ["dropdown"] = DropdownValue, ["switch"] = SwitchValue,
            ["toggleCalls"] = Calls[0], ["sliderCalls"] = Calls[1],
            ["dropdownCalls"] = Calls[2], ["switchCalls"] = Calls[3]
        };
    }

    private class SettingsSessionParticipantProbeProvider : SettingsSessionProbeProvider, ISettingsEditSessionParticipant
    {
        public int HiddenValue = 3;
        public int ParticipantCalls;
        public bool FailParticipantRestore;
        public object CaptureSettingsEditSessionState() => HiddenValue;

        public void RestoreSettingsEditSessionState(object state)
        {
            ParticipantCalls++;
            HiddenValue = (int)state;
            if (FailParticipantRestore) throw new InvalidOperationException("probe-participant");
        }

        public override JObject Sample()
        {
            JObject sample = base.Sample();
            sample["hidden"] = HiddenValue;
            sample["participantCalls"] = ParticipantCalls;
            return sample;
        }
    }

    private sealed class SettingsSessionTrackedProbeProvider : SettingsSessionParticipantProbeProvider, ISettingsEditSessionChangeTracker
    {
        public bool FailTracker;

        public bool HasSettingsEditSessionChanges(object state)
        {
            if (FailTracker) throw new InvalidOperationException("probe-tracker");
            return HiddenValue != (int)state;
        }
    }

    #endregion
}
