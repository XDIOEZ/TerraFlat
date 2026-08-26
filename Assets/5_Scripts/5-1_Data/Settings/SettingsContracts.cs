using System;
using System.Collections.Generic;

namespace FlatWorld.Settings
{
    /// <summary>
    /// 设置项使用的基础控件类型。Switch 表示按钮式的互斥选项切换，
    /// 与 Dropdown 的数据模型相同，但由 UI 决定使用按钮组或其他视觉表现。
    /// </summary>
    public enum SettingControlType
    {
        Toggle,
        Slider,
        Dropdown,
        Switch
    }

    /// <summary>设置项的稳定元数据；Key 是跨 UI、存档和 MOD 的唯一契约。</summary>
    public sealed class SettingDescriptor
    {
        public string Key { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string Category { get; }
        public SettingControlType ControlType { get; }
        public int Order { get; }

        public SettingDescriptor(
            string key,
            string displayName,
            SettingControlType controlType,
            string category = null,
            string description = null,
            int order = 0)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("设置 Key 不能为空。", nameof(key));

            Key = key;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            Category = category ?? string.Empty;
            ControlType = controlType;
            Order = order;
        }
    }

    /// <summary>下拉列表或按钮式切换使用的稳定选项。</summary>
    public sealed class SettingOption
    {
        public string Id { get; }
        public string DisplayName { get; }

        public SettingOption(string id, string displayName)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("设置选项 Id 不能为空。", nameof(id));

            Id = id;
            DisplayName = displayName ?? string.Empty;
        }
    }

    /// <summary>所有设置控件都能提供的只读元数据。</summary>
    public interface ISettingsControl
    {
        SettingDescriptor Descriptor { get; }
    }

    /// <summary>单个布尔开关设置。</summary>
    public interface ISettingsToggle : ISettingsControl
    {
        bool Value { get; }
        void SetValue(bool value);
    }

    /// <summary>单个连续数值设置。</summary>
    public interface ISettingsSlider : ISettingsControl
    {
        float Value { get; }
        float MinValue { get; }
        float MaxValue { get; }
        float Step { get; }
        void SetValue(float value);
    }

    /// <summary>下拉列表设置；修改结果由管理器立即应用。</summary>
    public interface ISettingsDropdown : ISettingsControl
    {
        IReadOnlyList<SettingOption> Options { get; }
        int SelectedIndex { get; }
        bool TrySetSelectedIndex(int index, out string error);
    }

    /// <summary>按钮式互斥切换设置；与下拉列表区分视觉交互而不重复数据契约。</summary>
    public interface ISettingsSwitch : ISettingsControl
    {
        IReadOnlyList<SettingOption> Options { get; }
        int SelectedIndex { get; }
        bool TrySetSelectedIndex(int index, out string error);
    }

    /// <summary>
    /// 设置管理器对 UI 暴露的能力集合。管理器只负责读写自己的状态，
    /// 不知道 Toggle、Slider、TMP_Dropdown 或具体 Prefab 的存在。
    /// </summary>
    public interface ISettingsProvider :
        ISettingsToggleProvider,
        ISettingsSliderProvider,
        ISettingsDropdownProvider,
        ISettingsSwitchProvider
    {
        string ProviderId { get; }
        string DisplayName { get; }
        int Order { get; }
        void ResetToDefaults();
    }

    /// <summary>提供 Toggle 设置的能力接口。</summary>
    public interface ISettingsToggleProvider
    {
        IReadOnlyList<ISettingsToggle> ToggleSettings { get; }
    }

    /// <summary>提供 Slider 设置的能力接口。</summary>
    public interface ISettingsSliderProvider
    {
        IReadOnlyList<ISettingsSlider> SliderSettings { get; }
    }

    /// <summary>提供 Dropdown 设置的能力接口。</summary>
    public interface ISettingsDropdownProvider
    {
        IReadOnlyList<ISettingsDropdown> DropdownSettings { get; }
    }

    /// <summary>提供 Switch 设置的能力接口。</summary>
    public interface ISettingsSwitchProvider
    {
        IReadOnlyList<ISettingsSwitch> SwitchSettings { get; }
    }

    /// <summary>通用布尔设置适配器，供管理器用 getter/setter 接入现有状态。</summary>
    public sealed class SettingsToggle : ISettingsToggle
    {
        private readonly Func<bool> getter;
        private readonly Action<bool> setter;

        public SettingDescriptor Descriptor { get; }
        public bool Value => getter();

        public SettingsToggle(
            SettingDescriptor descriptor,
            Func<bool> getter,
            Action<bool> setter)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            this.getter = getter ?? throw new ArgumentNullException(nameof(getter));
            this.setter = setter ?? throw new ArgumentNullException(nameof(setter));
        }

        public void SetValue(bool value) => setter(value);
    }

    /// <summary>通用数值设置适配器，集中提供范围和步进约束给 UI。</summary>
    public sealed class SettingsSlider : ISettingsSlider
    {
        private readonly Func<float> getter;
        private readonly Action<float> setter;

        public SettingDescriptor Descriptor { get; }
        public float Value => getter();
        public float MinValue { get; }
        public float MaxValue { get; }
        public float Step { get; }

        public SettingsSlider(
            SettingDescriptor descriptor,
            float minValue,
            float maxValue,
            float step,
            Func<float> getter,
            Action<float> setter)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            if (maxValue < minValue)
                throw new ArgumentException("Slider 最大值不能小于最小值。", nameof(maxValue));

            MinValue = minValue;
            MaxValue = maxValue;
            Step = step > 0f ? step : 0f;
            this.getter = getter ?? throw new ArgumentNullException(nameof(getter));
            this.setter = setter ?? throw new ArgumentNullException(nameof(setter));
        }

        public void SetValue(float value) => setter(value);
    }

    /// <summary>通用下拉列表设置适配器。</summary>
    public sealed class SettingsDropdown : ISettingsDropdown
    {
        private readonly Func<int> getter;
        private readonly Func<int, string> setter;

        public SettingDescriptor Descriptor { get; }
        public IReadOnlyList<SettingOption> Options { get; }
        public int SelectedIndex => getter();

        public SettingsDropdown(
            SettingDescriptor descriptor,
            IReadOnlyList<SettingOption> options,
            Func<int> getter,
            Func<int, string> setter)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            this.getter = getter ?? throw new ArgumentNullException(nameof(getter));
            this.setter = setter ?? throw new ArgumentNullException(nameof(setter));
        }

        public bool TrySetSelectedIndex(int index, out string error)
        {
            error = setter(index);
            return string.IsNullOrEmpty(error);
        }
    }

    /// <summary>通用按钮式互斥切换适配器。</summary>
    public sealed class SettingsSwitch : ISettingsSwitch
    {
        private readonly Func<int> getter;
        private readonly Func<int, string> setter;

        public SettingDescriptor Descriptor { get; }
        public IReadOnlyList<SettingOption> Options { get; }
        public int SelectedIndex => getter();

        public SettingsSwitch(
            SettingDescriptor descriptor,
            IReadOnlyList<SettingOption> options,
            Func<int> getter,
            Func<int, string> setter)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            this.getter = getter ?? throw new ArgumentNullException(nameof(getter));
            this.setter = setter ?? throw new ArgumentNullException(nameof(setter));
        }

        public bool TrySetSelectedIndex(int index, out string error)
        {
            error = setter(index);
            return string.IsNullOrEmpty(error);
        }
    }

    /// <summary>
    /// 设置提供者注册表。设置面板只依赖稳定 ProviderId，管理器在自身生命周期内注册/注销。
    /// </summary>
    public static class SettingsProviderRegistry
    {
        private static readonly Dictionary<string, ISettingsProvider> providers =
            new Dictionary<string, ISettingsProvider>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyCollection<ISettingsProvider> Providers => providers.Values;

        public static void Register(ISettingsProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            if (string.IsNullOrWhiteSpace(provider.ProviderId))
                throw new ArgumentException("设置提供者 ProviderId 不能为空。", nameof(provider));

            providers[provider.ProviderId] = provider;
        }

        public static void Unregister(ISettingsProvider provider)
        {
            if (provider == null || string.IsNullOrWhiteSpace(provider.ProviderId))
                return;

            if (providers.TryGetValue(provider.ProviderId, out ISettingsProvider current) &&
                ReferenceEquals(current, provider))
            {
                providers.Remove(provider.ProviderId);
            }
        }

        public static bool TryGet(string providerId, out ISettingsProvider provider)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                provider = null;
                return false;
            }

            return providers.TryGetValue(providerId, out provider);
        }

        /// <summary>按稳定顺序恢复当前已注册的全部设置提供者，并返回实际调用数量。</summary>
        public static int ResetAllToDefaults()
        {
            var snapshot = new List<ISettingsProvider>(providers.Values);
            snapshot.Sort((left, right) =>
            {
                int order = left.Order.CompareTo(right.Order);
                return order != 0
                    ? order
                    : StringComparer.OrdinalIgnoreCase.Compare(
                        left.ProviderId,
                        right.ProviderId);
            });

            for (int i = 0; i < snapshot.Count; i++)
                snapshot[i].ResetToDefaults();
            return snapshot.Count;
        }
    }

    /// <summary>按稳定 Key 查找具体控件契约，避免每个 UI 适配器重复遍历列表。</summary>
    public static class SettingsProviderExtensions
    {
        public static ISettingsToggle GetToggle(this ISettingsToggleProvider provider, string key)
        {
            if (provider == null || string.IsNullOrWhiteSpace(key))
                return null;

            IReadOnlyList<ISettingsToggle> settings = provider.ToggleSettings;
            for (int i = 0; i < settings.Count; i++)
            {
                if (settings[i]?.Descriptor?.Key == key)
                    return settings[i];
            }

            return null;
        }

        public static ISettingsSlider GetSlider(this ISettingsSliderProvider provider, string key)
        {
            if (provider == null || string.IsNullOrWhiteSpace(key))
                return null;

            IReadOnlyList<ISettingsSlider> settings = provider.SliderSettings;
            for (int i = 0; i < settings.Count; i++)
            {
                if (settings[i]?.Descriptor?.Key == key)
                    return settings[i];
            }

            return null;
        }

        public static ISettingsDropdown GetDropdown(
            this ISettingsDropdownProvider provider,
            string key)
        {
            if (provider == null || string.IsNullOrWhiteSpace(key))
                return null;

            IReadOnlyList<ISettingsDropdown> settings = provider.DropdownSettings;
            for (int i = 0; i < settings.Count; i++)
            {
                if (settings[i]?.Descriptor?.Key == key)
                    return settings[i];
            }

            return null;
        }

        public static ISettingsSwitch GetSwitch(this ISettingsSwitchProvider provider, string key)
        {
            if (provider == null || string.IsNullOrWhiteSpace(key))
                return null;

            IReadOnlyList<ISettingsSwitch> settings = provider.SwitchSettings;
            for (int i = 0; i < settings.Count; i++)
            {
                if (settings[i]?.Descriptor?.Key == key)
                    return settings[i];
            }

            return null;
        }
    }
}
