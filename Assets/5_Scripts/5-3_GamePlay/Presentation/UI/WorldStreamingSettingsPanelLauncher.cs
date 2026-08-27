// AI-Context: 设置主面板内嵌区块流送性能页；模式仅在点击应用时提交。
using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>绑定内嵌流送性能页，并在自动、流畅和高吞吐模式之间切换。</summary>
[DisallowMultipleComponent]
public sealed class WorldStreamingSettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    private SettingsActionListPagination pagination;
    private TMP_Dropdown modeDropdown;
    private TextMeshProUGUI statusText;
    private Button cancelButton;
    private Button applyButton;
    private ISettingsDropdown modeSetting;
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
        modeDropdown = FindComponent<TMP_Dropdown>(transform, "性能模式下拉列表");
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
        initialized = true;

        if (modeDropdown == null || statusText == null || cancelButton == null ||
            applyButton == null || modeSetting == null)
        {
            Debug.LogError(
                "[WorldStreamingSettings] 内嵌流送性能页控件命名契约不完整。",
                this);
        }
    }

    /// <summary>提交当前下拉草稿并刷新实际调度状态。</summary>
    private void Apply()
    {
        if (modeDropdown == null || modeSetting == null)
            return;
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
        if (statusText == null)
            return;

        ChunkMgr manager = ChunkMgr.ExistingInstance;
        int workers = manager != null
            ? manager.EffectiveBackgroundGenerationConcurrency
            : WorldStreamingPreferences.ResolveBaseGenerationConcurrency(2);
        WorldStreamingPerformanceMode mode = modeSetting != null
            ? (WorldStreamingPerformanceMode)modeSetting.SelectedIndex
            : WorldStreamingPreferences.Mode;
        statusText.text = mode switch
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
    }

    /// <summary>流送页显示时丢弃旧草稿并读取已生效模式。</summary>
    public void OnSettingsPageShown()
    {
        modeDropdown?.SetValueWithoutNotify(modeSetting != null ? modeSetting.SelectedIndex : 0);
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
    }

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
