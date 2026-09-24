using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>按玩家到实体的世界距离划分模拟频率；零表示三级范围外暂停玩法 Tick。</summary>
public enum SimulationRangeTier : byte
{
    Stopped = 0,
    Near = 1,
    Middle = 2,
    Far = 3
}

/// <summary>
/// 客户端模拟范围偏好。三个半径以世界格为单位，默认 32、64、96；
/// 相机缩放只改变显示，不改变距离判断。设置属于设备性能偏好，不进入世界存档。
/// </summary>
public static class SimulationRangePreferences
{
    #region 范围和持久化

    private const string NearKey = "FlatWorld.SimulationRange.Near.v1";
    private const string MiddleKey = "FlatWorld.SimulationRange.Middle.v1";
    private const string FarKey = "FlatWorld.SimulationRange.Far.v1";

    public const string SettingsProviderId = "simulation-range";
    public const string NearSettingKey = "simulationRange.near";
    public const string MiddleSettingKey = "simulationRange.middle";
    public const string FarSettingKey = "simulationRange.far";

    public const int DefaultNear = 32;
    public const int DefaultMiddle = 64;
    public const int DefaultFar = 96;
    public const int MinimumRadius = 8;
    public const int MaximumRadius = 256;
    public const int RadiusStep = 8;

    private static bool loaded;
    private static int nearRadius, middleRadius, farRadius;
    private static readonly ISimulationRangeSettingsProvider provider = new RangeSettingsProvider();

    public static event Action Changed;
    public static ISimulationRangeSettingsProvider SettingsProvider
    {
        get
        {
            SettingsProviderRegistry.Register(provider);
            return provider;
        }
    }

    public static int NearRadius { get { Load(); return nearRadius; } }
    public static int MiddleRadius { get { Load(); return middleRadius; } }
    public static int FarRadius { get { Load(); return farRadius; } }

    /// <summary>整体校验后一次提交，避免三档之间出现临时重叠或反序。</summary>
    public static bool TrySetRadii(int near, int middle, int far, out string error)
    {
        if (near < MinimumRadius || far > MaximumRadius ||
            near % RadiusStep != 0 || middle % RadiusStep != 0 || far % RadiusStep != 0 ||
            near >= middle || middle >= far)
        {
            error = $"模拟范围须为 {MinimumRadius}～{MaximumRadius} 格、间隔 {RadiusStep} 格，且一级 < 二级 < 三级。";
            return false;
        }

        Load();
        error = null;
        if (nearRadius == near && middleRadius == middle && farRadius == far)
            return true;

        nearRadius = near;
        middleRadius = middle;
        farRadius = far;
        PlayerPrefs.SetInt(NearKey, near);
        PlayerPrefs.SetInt(MiddleKey, middle);
        PlayerPrefs.SetInt(FarKey, far);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>仅计算档位；调用方决定哪些系统能休眠。</summary>
    public static SimulationRangeTier ResolveTier(float squaredDistance)
    {
        Load();
        if (squaredDistance <= nearRadius * nearRadius) return SimulationRangeTier.Near;
        if (squaredDistance <= middleRadius * middleRadius) return SimulationRangeTier.Middle;
        if (squaredDistance <= farRadius * farRadius) return SimulationRangeTier.Far;
        return SimulationRangeTier.Stopped;
    }

    /// <summary>此值是实体更新频率上限；模块自身可声明更慢的固定间隔。</summary>
    public static float TickInterval(SimulationRangeTier tier)
    {
        return tier switch
        {
            SimulationRangeTier.Near => 1f / 60f,
            SimulationRangeTier.Middle => 1f / 30f,
            SimulationRangeTier.Far => 1f / 10f,
            _ => 0f
        };
    }

    private static void Load()
    {
        if (loaded) return;
        int near = PlayerPrefs.GetInt(NearKey, DefaultNear);
        int middle = PlayerPrefs.GetInt(MiddleKey, DefaultMiddle);
        int far = PlayerPrefs.GetInt(FarKey, DefaultFar);
        if (near < MinimumRadius || far > MaximumRadius ||
            near % RadiusStep != 0 || middle % RadiusStep != 0 || far % RadiusStep != 0 ||
            near >= middle || middle >= far)
        {
            near = DefaultNear;
            middle = DefaultMiddle;
            far = DefaultFar;
        }
        nearRadius = near;
        middleRadius = middle;
        farRadius = far;
        loaded = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(provider);
        Changed = null;
        loaded = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterProviderOnLoad() => SettingsProviderRegistry.Register(provider);

    #endregion

    #region 设置契约

    /// <summary>三个滑块共用一次提交入口，UI 可以先校验整组草稿。</summary>
    public interface ISimulationRangeSettingsProvider : ISettingsProvider
    {
        bool TrySetRadii(int near, int middle, int far, out string error);
    }

    private sealed class RangeSettingsProvider : ISimulationRangeSettingsProvider
    {
        private readonly IReadOnlyList<ISettingsSlider> sliders;

        public RangeSettingsProvider()
        {
            sliders = new ISettingsSlider[]
            {
                CreateSlider(NearSettingKey, "一级模拟范围（60 Hz）", 0, () => NearRadius,
                    value => TrySetRadii(value, MiddleRadius, FarRadius, out _)),
                CreateSlider(MiddleSettingKey, "二级模拟范围（30 Hz）", 1, () => MiddleRadius,
                    value => TrySetRadii(NearRadius, value, FarRadius, out _)),
                CreateSlider(FarSettingKey, "三级模拟范围（10 Hz）", 2, () => FarRadius,
                    value => TrySetRadii(NearRadius, MiddleRadius, value, out _))
            };
        }

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "模拟范围";
        public int Order => 61;
        public IReadOnlyList<ISettingsToggle> ToggleSettings => Array.Empty<ISettingsToggle>();
        public IReadOnlyList<ISettingsSlider> SliderSettings => sliders;
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();

        public bool TrySetRadii(int near, int middle, int far, out string error) =>
            SimulationRangePreferences.TrySetRadii(near, middle, far, out error);

        public void ResetToDefaults() =>
            SimulationRangePreferences.TrySetRadii(DefaultNear, DefaultMiddle, DefaultFar, out _);

        private static ISettingsSlider CreateSlider(string key, string name, int order,
            Func<int> getter, Action<int> setter)
        {
            return new SettingsSlider(
                new SettingDescriptor(key, name, SettingControlType.Slider, "world", order: order),
                MinimumRadius, MaximumRadius, RadiusStep,
                () => getter(), value => setter(Mathf.RoundToInt(value / RadiusStep) * RadiusStep));
        }
    }

    #endregion
}
