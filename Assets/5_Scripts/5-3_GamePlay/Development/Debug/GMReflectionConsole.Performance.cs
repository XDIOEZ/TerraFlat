using System.Globalization;
using FlatWorld.Diagnostics;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 性能诊断操作

    private Button performanceStatisticsButton, performanceSlowButton;
    private TMP_InputField performanceThresholdInput;
    private TextMeshProUGUI performanceSummaryText;

    private void BuildPerformanceDebugControls(Transform parent)
    {
        AddPageHint(parent, "性能 DEBUG：Profiler 细分始终可采样；额外次数统计与慢操作摘要默认关闭。", 42f);
        Transform grid = CreateActionGrid(parent, 2, 338f, 44f, 4);
        performanceStatisticsButton = CreateSearchableButton(grid, GmPageId.Device,
            "性能统计：关", "性能 调试 统计 Profiler performance", TogglePerformanceStatistics, 44f);
        performanceSlowButton = CreateSearchableButton(grid, GmPageId.Device,
            "慢操作摘要：关", "性能 调试 慢摘要 slow", TogglePerformanceSlowSummary, 44f);
        CreateSearchableButton(grid, GmPageId.Device, "输出性能摘要", "性能 统计 摘要 日志 summary",
            PrintPerformanceSummary, 44f);
        CreateSearchableButton(grid, GmPageId.Device, "清空性能统计", "性能 统计 清空 reset",
            ClearPerformanceStatistics, 44f);
        GameObject row = CreateDesktopRow(parent, 44f);
        CreateDesktopFieldLabel(row.transform, "慢阈值 ms", 98f);
        performanceThresholdInput = CreateInputField(row.transform, "0.1～10000", 140f, false);
        performanceThresholdInput.contentType = TMP_InputField.ContentType.DecimalNumber;
        CreateButton(row.transform, "应用阈值", ApplyPerformanceThreshold, 126f, 44f);
        performanceSummaryText = CreateText(parent, "点击输出摘要后显示；统计区段包含子调用，不能相加当作整帧。", 13f, GmTextSecondary);
        performanceSummaryText.enableWordWrapping = true;
        performanceSummaryText.gameObject.AddComponent<LayoutElement>().minHeight = 80f;
        RegisterSearchEntry(GmPageId.Device, "关键性能 DEBUG", "性能 GC 手持 温度 Flow 存档 网络", performanceSummaryText.rectTransform);
        RefreshPerformanceDebugControls();
    }

    private void TogglePerformanceStatistics()
    {
        ISettingsToggle setting = RuntimeDebugOverlayPreferences.SettingsProvider.GetToggle(
            RuntimeDebugOverlayPreferences.PerformanceStatisticsSettingKey);
        setting.SetValue(!setting.Value);
        RefreshPerformanceDebugControls();
    }

    private void TogglePerformanceSlowSummary()
    {
        ISettingsToggle setting = RuntimeDebugOverlayPreferences.SettingsProvider.GetToggle(
            RuntimeDebugOverlayPreferences.PerformanceSlowSummarySettingKey);
        setting.SetValue(!setting.Value);
        RefreshPerformanceDebugControls();
    }

    private void ApplyPerformanceThreshold()
    {
        if (!float.TryParse(performanceThresholdInput.text, NumberStyles.Float, CultureInfo.InvariantCulture,
                out float threshold) || float.IsNaN(threshold) || float.IsInfinity(threshold) || threshold < .1f || threshold > 10000f)
        { SetStatus("慢操作阈值请输入 0.1～10000 毫秒。", GmAccent); return; }
        RuntimeDebugOverlayPreferences.SettingsProvider.GetSlider(
            RuntimeDebugOverlayPreferences.PerformanceThresholdSettingKey).SetValue(threshold);
        RefreshPerformanceDebugControls();
        SetStatus("性能慢操作阈值已更新。", GmTextSecondary);
    }

    private void RefreshPerformanceDebugControls()
    {
        if (performanceStatisticsButton == null) return;
        performanceStatisticsButton.GetComponentInChildren<TextMeshProUGUI>(true).text =
            PerformanceDebugRegistry.StatisticsEnabled ? "性能统计：开" : "性能统计：关";
        performanceSlowButton.GetComponentInChildren<TextMeshProUGUI>(true).text =
            PerformanceDebugRegistry.SlowSummaryEnabled ? "慢操作摘要：开" : "慢操作摘要：关";
        if (performanceThresholdInput != null && !performanceThresholdInput.isFocused)
            performanceThresholdInput.SetTextWithoutNotify(PerformanceDebugRegistry.SlowThresholdMilliseconds.ToString("0.##", CultureInfo.InvariantCulture));
    }

    private void PrintPerformanceSummary()
    {
        string summary = PerformanceDebugRegistry.BuildSummary();
        if (performanceSummaryText != null) performanceSummaryText.text = summary;
        Debug.Log(summary);
        SetStatus("性能摘要已显示并写入日志。", GmTextSecondary);
    }

    private void ClearPerformanceStatistics()
    {
        PerformanceDebugRegistry.Clear();
        if (performanceSummaryText != null) performanceSummaryText.text = "性能统计已清空；当前开关与阈值保留。";
        SetStatus("性能统计已清空。", GmTextSecondary);
    }

    #endregion
}
