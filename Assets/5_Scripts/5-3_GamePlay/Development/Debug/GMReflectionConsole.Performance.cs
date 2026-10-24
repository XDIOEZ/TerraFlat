using System;
using System.Globalization;
using FlatWorld.Diagnostics;
using FlatWorld.Localization;
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
        SetLocalizedText(performanceStatisticsButton.GetComponentInChildren<TextMeshProUGUI>(true),
            PerformanceDebugRegistry.StatisticsEnabled ? "性能统计：开" : "性能统计：关");
        SetLocalizedText(performanceSlowButton.GetComponentInChildren<TextMeshProUGUI>(true),
            PerformanceDebugRegistry.SlowSummaryEnabled ? "慢操作摘要：开" : "慢操作摘要：关");
        if (performanceThresholdInput != null && !performanceThresholdInput.isFocused)
            performanceThresholdInput.SetTextWithoutNotify(PerformanceDebugRegistry.SlowThresholdMilliseconds.ToString("0.##", CultureInfo.InvariantCulture));
    }

    private void PrintPerformanceSummary()
    {
        string summary = PerformanceDebugRegistry.BuildSummary();
        if (performanceSummaryText != null)
            SetLocalizedProvider(performanceSummaryText, () => LocalizePerformanceSummary(summary));
        Debug.Log(summary);
        SetStatus("性能摘要已显示并写入日志。", GmTextSecondary);
    }

    // 保留性能点名称和采样值，只替换可翻译的固定标签。
    private static string LocalizePerformanceSummary(string summary)
    {
        if (string.IsNullOrEmpty(summary)) return string.Empty;
        string[] lines = summary.Replace("\r", string.Empty).Split('\n');
        if (lines.Length > 0 && lines[0].StartsWith("[性能 DEBUG] ", StringComparison.Ordinal))
            lines[0] = FlatWorldLocalizationService.GetUiText("[性能 DEBUG]") + " " +
                       FlatWorldLocalizationService.GetUiText("区段为包含子调用的 Total，不可相加当作整帧。");

        if (lines.Length > 1 && TryReadPerformanceSummaryFields(lines[1], out string statistics,
                out string slowSummary, out string threshold, out string registered, out string excess))
        {
            lines[1] = FlatWorldLocalizationService.GetUiFormat(
                "性能统计 {0}，慢操作摘要 {1}，阈值 {2} ms，注册点位 {3}，超额点位 {4}",
                FlatWorldLocalizationService.GetUiText(statistics.Equals("True", StringComparison.OrdinalIgnoreCase) ? "开" : "关"),
                FlatWorldLocalizationService.GetUiText(slowSummary.Equals("True", StringComparison.OrdinalIgnoreCase) ? "开" : "关"),
                threshold, registered, excess);
        }

        for (int i = 2; i < lines.Length; i++)
            lines[i] = LocalizePerformanceRow(lines[i]);
        return string.Join("\n", lines);
    }

    private static bool TryReadPerformanceSummaryFields(string line, out string statistics,
        out string slowSummary, out string threshold, out string registered, out string excess)
    {
        statistics = slowSummary = threshold = registered = excess = string.Empty;
        return TryReadPerformanceField(line, "统计=", "，慢摘要=", out statistics) &&
               TryReadPerformanceField(line, "，慢摘要=", "，阈值=", out slowSummary) &&
               TryReadPerformanceField(line, "，阈值=", "ms，注册=", out threshold) &&
               TryReadPerformanceField(line, "ms，注册=", "，超额点位=", out registered) &&
               TryReadPerformanceField(line, "，超额点位=", null, out excess);
    }

    private static bool TryReadPerformanceField(string line, string prefix, string suffix, out string value)
    {
        value = string.Empty;
        int start = line.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return false;
        start += prefix.Length;
        int end = suffix == null ? line.Length : line.IndexOf(suffix, start, StringComparison.Ordinal);
        if (end < start) return false;
        value = line.Substring(start, end - start);
        return true;
    }

    private static string LocalizePerformanceRow(string line)
    {
        int dataStart = line.IndexOf(" | ", StringComparison.Ordinal);
        if (dataStart < 0) return line;
        string pointName = line.Substring(0, dataStart);
        string[] fields = line.Substring(dataStart + 3).Split(' ');
        for (int i = 0; i < fields.Length; i++)
        {
            int separator = fields[i].IndexOf('=');
            if (separator <= 0) continue;
            string fieldName = fields[i].Substring(0, separator);
            string fieldValue = fields[i].Substring(separator + 1);
            fields[i] = FlatWorldLocalizationService.GetUiFormat("{0}={1}",
                GetLocalizedPerformanceFieldName(fieldName), fieldValue);
        }
        return pointName + " | " + string.Join(" ", fields);
    }

    private static string GetLocalizedPerformanceFieldName(string fieldName)
    {
        string sourceLabel = fieldName switch
        {
            "calls" => "调用次数",
            "work" => "工作量",
            "total" => "总耗时",
            "max" => "最大耗时",
            "gauge" => "当前量",
            "peak" => "峰值",
            _ => null
        };
        return sourceLabel == null
            ? fieldName
            : FlatWorldLocalizationService.GetUiText(sourceLabel);
    }

    private void ClearPerformanceStatistics()
    {
        PerformanceDebugRegistry.Clear();
        if (performanceSummaryText != null)
            SetLocalizedText(performanceSummaryText, "性能统计已清空；当前开关与阈值保留。");
        SetStatus("性能统计已清空。", GmTextSecondary);
    }

    #endregion
}
