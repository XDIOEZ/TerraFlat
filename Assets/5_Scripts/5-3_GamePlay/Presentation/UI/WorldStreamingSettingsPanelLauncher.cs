// AI-Context: 设置主面板内嵌区块流送性能页；模式仅在点击应用时提交。
using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>绑定流送性能与模拟范围设置，应用按钮一次提交当前页面草稿。</summary>
[DisallowMultipleComponent]
public sealed class WorldStreamingSettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    private SettingsActionListPagination pagination;
    private TMP_Dropdown modeDropdown;
    private Slider nearSlider;
    private Slider middleSlider;
    private Slider farSlider;
    private TextMeshProUGUI nearLabel;
    private TextMeshProUGUI middleLabel;
    private TextMeshProUGUI farLabel;
    private TextMeshProUGUI rangeText;
    private TextMeshProUGUI statusText;
    private Button cancelButton;
    private Button applyButton;
    private ISettingsDropdown modeSetting;
    private SimulationRangePreferences.ISimulationRangeSettingsProvider rangeProvider;
    private ISettingsSlider nearSetting, middleSetting, farSetting;
    private bool initialized;

    /// <summary>在指定内嵌页面根节点上复用或挂载流送性能控制器。</summary>
    public static WorldStreamingSettingsPanelLauncher Ensure(
        Transform pageRoot,
        SettingsActionListPagination pagination)
    {
        if (pageRoot == null)
            return null;

        WorldStreamingSettingsPanelLauncher launcher =
            pageRoot.GetComponent<WorldStreamingSettingsPanelLauncher>();
        if (launcher == null)
            launcher = pageRoot.gameObject.AddComponent<WorldStreamingSettingsPanelLauncher>();
        launcher.Initialize(pagination);
        return launcher;
    }

    /// <summary>解析页面局部控件并绑定流送设置 Provider。</summary>
    private void Initialize(SettingsActionListPagination ownerPagination)
    {
        pagination = ownerPagination;
        if (initialized)
            return;

        modeSetting = WorldStreamingPreferences.SettingsProvider.GetDropdown(
            WorldStreamingPreferences.ModeSettingKey);
        rangeProvider = SimulationRangePreferences.SettingsProvider;
        nearSetting = rangeProvider.GetSlider(SimulationRangePreferences.NearSettingKey);
        middleSetting = rangeProvider.GetSlider(SimulationRangePreferences.MiddleSettingKey);
        farSetting = rangeProvider.GetSlider(SimulationRangePreferences.FarSettingKey);
        modeDropdown = FindComponent<TMP_Dropdown>(transform, "性能模式下拉列表");
        nearSlider = FindComponent<Slider>(transform, "一级模拟范围滑块");
        middleSlider = FindComponent<Slider>(transform, "二级模拟范围滑块");
        farSlider = FindComponent<Slider>(transform, "三级模拟范围滑块");
        nearLabel = FindComponent<TextMeshProUGUI>(transform, "一级模拟范围标签");
        middleLabel = FindComponent<TextMeshProUGUI>(transform, "二级模拟范围标签");
        farLabel = FindComponent<TextMeshProUGUI>(transform, "三级模拟范围标签");
        rangeText = FindComponent<TextMeshProUGUI>(transform, "说明文本");
        statusText = FindComponent<TextMeshProUGUI>(transform, "状态文本");
        cancelButton = FindComponent<Button>(transform, "取消按钮");
        applyButton = FindComponent<Button>(transform, "应用按钮");

        if (modeDropdown != null)
        {
            modeDropdown.ClearOptions();
            modeDropdown.AddOptions(GetSettingOptionLabels(modeSetting?.Options));
        }

        cancelButton?.onClick.AddListener(Cancel);
        applyButton?.onClick.AddListener(Apply);
        ConfigureRangeSlider(nearSlider, nearSetting);
        ConfigureRangeSlider(middleSlider, middleSetting);
        ConfigureRangeSlider(farSlider, farSetting);
        nearSlider?.onValueChanged.AddListener(OnRangeDraftChanged);
        middleSlider?.onValueChanged.AddListener(OnRangeDraftChanged);
        farSlider?.onValueChanged.AddListener(OnRangeDraftChanged);
        initialized = true;

        if (modeDropdown == null || statusText == null || cancelButton == null ||
            applyButton == null || modeSetting == null || nearSlider == null ||
            middleSlider == null || farSlider == null || nearLabel == null ||
            middleLabel == null || farLabel == null || rangeText == null ||
            nearSetting == null || middleSetting == null || farSetting == null)
        {
            Debug.LogError(
                "[WorldStreamingSettings] 内嵌流送性能页控件命名契约不完整。",
                this);
        }
    }

    /// <summary>提交当前下拉草稿并刷新实际调度状态。</summary>
    private void Apply()
    {
        if (modeDropdown == null || modeSetting == null || rangeProvider == null ||
            nearSlider == null || middleSlider == null || farSlider == null)
            return;
        if (!rangeProvider.TrySetRadii(RadiusValue(nearSlider), RadiusValue(middleSlider),
                RadiusValue(farSlider), out string rangeError))
        {
            if (statusText != null)
                statusText.text = FlatWorldLocalizationService.GetUiText(rangeError);
            return;
        }
        if (!modeSetting.TrySetSelectedIndex(modeDropdown.value, out string error))
        {
            if (statusText != null)
                statusText.text = FlatWorldLocalizationService.GetUiText(error);
            return;
        }

        RefreshStatus();
    }

    /// <summary>放弃当前视图草稿并返回世界设置入口页。</summary>
    private void Cancel()
    {
        pagination?.ShowWorldPage();
    }

    /// <summary>根据已生效模式刷新并发数和说明文本。</summary>
    private void RefreshStatus()
    {
        if (statusText == null || nearSetting == null || middleSetting == null || farSetting == null)
            return;

        ChunkMgr manager = ChunkMgr.ExistingInstance;
        int workers = manager != null
            ? manager.EffectiveBackgroundGenerationConcurrency
            : WorldStreamingPreferences.ResolveBaseGenerationConcurrency(2);
        WorldStreamingPerformanceMode mode = modeSetting != null
            ? (WorldStreamingPerformanceMode)modeSetting.SelectedIndex
            : WorldStreamingPreferences.Mode;
        string modeStatus = mode switch
        {
            WorldStreamingPerformanceMode.Smooth =>
                FlatWorldLocalizationService.GetUiFormat(
                    "当前：单后台线程生成 + 主线程逐帧绘制（{0} 个生成任务并发）。",
                    workers),
            WorldStreamingPerformanceMode.Throughput =>
                FlatWorldLocalizationService.GetUiFormat(
                    "当前：安全多线程高吞吐（{0} 个生成任务并发）。",
                    workers),
            _ => FlatWorldLocalizationService.GetUiFormat(
                "当前：自动平衡（{0} 个生成任务并发）。",
                workers)
        };
        statusText.text = modeStatus + "\n" + FlatWorldLocalizationService.GetUiFormat(
            "模拟：一级 {0} 格 / 二级 {1} 格 / 三级 {2} 格；三级外暂停。",
            nearSetting.Value, middleSetting.Value, farSetting.Value);
    }

    /// <summary>流送页显示时丢弃旧草稿并读取已生效模式。</summary>
    public void OnSettingsPageShown()
    {
        modeDropdown?.SetValueWithoutNotify(modeSetting != null ? modeSetting.SelectedIndex : 0);
        SetRangeDraft(nearSlider, nearSetting);
        SetRangeDraft(middleSlider, middleSetting);
        SetRangeDraft(farSlider, farSetting);
        RefreshRangeDraft();
        RefreshStatus();
    }

    /// <summary>流送页隐藏时不提交当前下拉草稿。</summary>
    public void OnSettingsPageHidden()
    {
    }

    /// <summary>解除页面按钮监听。</summary>
    private void OnDestroy()
    {
        cancelButton?.onClick.RemoveListener(Cancel);
        applyButton?.onClick.RemoveListener(Apply);
        nearSlider?.onValueChanged.RemoveListener(OnRangeDraftChanged);
        middleSlider?.onValueChanged.RemoveListener(OnRangeDraftChanged);
        farSlider?.onValueChanged.RemoveListener(OnRangeDraftChanged);
    }

    #region 模拟范围草稿

    /// <summary>采用 Provider 定义的可调区间，离散步长在显示和提交时统一取整。</summary>
    private static void ConfigureRangeSlider(Slider slider, ISettingsSlider setting)
    {
        if (slider == null || setting == null) return;
        slider.minValue = setting.MinValue / setting.Step;
        slider.maxValue = setting.MaxValue / setting.Step;
        slider.wholeNumbers = true;
    }

    private static void SetRangeDraft(Slider slider, ISettingsSlider setting)
    {
        if (slider != null && setting != null)
            slider.SetValueWithoutNotify(setting.Value / setting.Step);
    }

    private static int RadiusValue(Slider slider) =>
        Mathf.RoundToInt(slider.value) *
        SimulationRangePreferences.RadiusStep;

    private void OnRangeDraftChanged(float _) => RefreshRangeDraft();

    /// <summary>在三个滑块上方说明顺序与当前草稿，便于触屏玩家核对数值。</summary>
    private void RefreshRangeDraft()
    {
        if (rangeText == null || nearSlider == null || middleSlider == null || farSlider == null)
            return;
        rangeText.text = FlatWorldLocalizationService.GetUiText(
            "模拟范围随与玩家的距离降低更新频率。三级外暂停；镜头缩放不改变模拟范围。");
        if (nearLabel != null) nearLabel.text = FlatWorldLocalizationService.GetUiFormat(
            "一级模拟范围（60 Hz）：{0} 格", RadiusValue(nearSlider));
        if (middleLabel != null) middleLabel.text = FlatWorldLocalizationService.GetUiFormat(
            "二级模拟范围（30 Hz）：{0} 格", RadiusValue(middleSlider));
        if (farLabel != null) farLabel.text = FlatWorldLocalizationService.GetUiFormat(
            "三级模拟范围（10 Hz）：{0} 格", RadiusValue(farSlider));
    }

    #endregion

    /// <summary>把稳定选项元数据转换为本地化下拉标签。</summary>
    private static List<string> GetSettingOptionLabels(IReadOnlyList<SettingOption> options)
    {
        var labels = new List<string>(options?.Count ?? 0);
        if (options == null)
            return labels;

        for (int index = 0; index < options.Count; index++)
            labels.Add(FlatWorldLocalizationService.GetUiText(options[index]?.DisplayName));
        return labels;
    }

    /// <summary>在页面局部按名称查找指定组件。</summary>
    private static T FindComponent<T>(Transform root, string objectName) where T : Component
    {
        T[] components = root.GetComponentsInChildren<T>(true);
        for (int index = 0; index < components.Length; index++)
        {
            if (components[index] != null && components[index].name == objectName)
                return components[index];
        }

        return null;
    }
}
