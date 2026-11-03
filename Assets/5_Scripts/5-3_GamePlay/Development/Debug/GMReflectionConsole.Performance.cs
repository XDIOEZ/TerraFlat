using System;
using System.IO;
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
    private TMP_InputField performanceReportSecondsInput;
    private TextMeshProUGUI performanceReportStatusText;

    private void BuildRuntimePerformanceReportControls(Transform parent)
    {
        TextMeshProUGUI title = CreateText(parent, "一键导出打包版性能报告", 18f, GmTextPrimary);
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
        AddPageHint(parent, "直接导出最近一段时间的帧间隔、FPS、内存快照、性能点累计值和近期日志为一个 ZIP；" +
            "无需连接编辑器。范围不足时仍可导出，并在报告标明实际覆盖时间。完整 CPU 调用树需使用 Unity Profiler。", 60f);

        Transform quickGrid = CreateActionGrid(parent, 3, 220f, 44f, 3);
        CreateSearchableButton(quickGrid, GmPageId.Device, "导出最近 10 秒", "打包 性能 导出 最近 10 秒 ZIP",
            () => ExportRuntimePerformanceReport(10), 44f);
        CreateSearchableButton(quickGrid, GmPageId.Device, "导出最近 30 秒", "打包 性能 导出 最近 30 秒 ZIP",
            () => ExportRuntimePerformanceReport(30), 44f);
        CreateSearchableButton(quickGrid, GmPageId.Device, "导出最近 60 秒", "打包 性能 导出 最近 60 秒 ZIP",
            () => ExportRuntimePerformanceReport(60), 44f);

        GameObject row = CreateDesktopRow(parent, 44f);
        CreateDesktopFieldLabel(row.transform, "自定义范围（秒）", 132f);
        performanceReportSecondsInput = CreateInputField(row.transform, "5～120", 90f, false);
        performanceReportSecondsInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        performanceReportSecondsInput.characterLimit = 3;
        performanceReportSecondsInput.SetTextWithoutNotify("30");
        CreateButton(row.transform, "导出指定范围", ExportCustomRuntimePerformanceReport, 160f, 44f);

        Transform locationGrid = CreateActionGrid(parent, 2, 338f, 40f, 2);
        CreateSearchableButton(locationGrid, GmPageId.Device, "复制最近报告路径", "打包 性能 报告 路径 复制",
            CopyLatestRuntimePerformanceReportPath, 40f);
        CreateSearchableButton(locationGrid, GmPageId.Device, "打开报告目录", "打包 性能 ZIP 文件夹 打开",
            OpenRuntimePerformanceReportsDirectory, 40f);

        performanceReportStatusText = CreateText(parent, string.Empty, 12f, GmTextSecondary);
        performanceReportStatusText.enableWordWrapping = true;
        performanceReportStatusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 72f;
        RefreshRuntimePerformanceReportControls();
    }

    private void ExportCustomRuntimePerformanceReport()
    {
        if (!int.TryParse(performanceReportSecondsInput?.text, out int seconds))
        {
            SetStatus("请输入 5～120 的整数秒数。", Color.yellow);
            return;
        }
        ExportRuntimePerformanceReport(seconds);
    }

    private void ExportRuntimePerformanceReport(int seconds)
    {
        if (!RuntimePerformanceReportRecorder.TryExportRecentSeconds(seconds, out string error))
        {
            SetStatus("性能报告导出失败：" + error, Color.yellow);
            return;
        }
        SetStatus($"正在导出最近 {seconds} 秒的性能 ZIP，写入完成后自动复制文件路径。", GmTextSecondary);
        RefreshRuntimePerformanceReportControls();
    }

    private void CopyLatestRuntimePerformanceReportPath()
    {
        string path = RuntimePerformanceReportRecorder.LastReportPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            SetStatus("还没有可复制的已完成性能报告。", Color.yellow);
            return;
        }
        try
        {
            GUIUtility.systemCopyBuffer = path;
            SetStatus("最新性能 ZIP 文件路径已复制，可直接粘贴或发送给 Agent。", GmTextSecondary);
        }
        catch (Exception exception) { SetStatus("复制失败：" + exception.Message, Color.yellow); }
    }

    private void OpenRuntimePerformanceReportsDirectory()
    {
        string directory = RuntimePerformanceReportRecorder.ReportsDirectory;
        Directory.CreateDirectory(directory);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            using (var process = System.Diagnostics.Process.Start(
                       new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true }))
            { }
        }
        catch (Exception exception) { SetStatus("打开目录失败：" + exception.Message, Color.yellow); }
#else
        GUIUtility.systemCopyBuffer = directory;
        SetStatus("当前平台已复制报告目录路径，请从文件管理器打开。", GmTextSecondary);
#endif
    }

    private void RefreshRuntimePerformanceReportControls()
    {
        if (performanceReportStatusText == null) return;
        string status = RuntimePerformanceReportRecorder.IsExporting ? "正在压缩并写入 ZIP…" :
            !string.IsNullOrEmpty(RuntimePerformanceReportRecorder.LastError)
                ? "上次导出失败：" + RuntimePerformanceReportRecorder.LastError
                : !string.IsNullOrEmpty(RuntimePerformanceReportRecorder.LastReportPath)
                    ? "最近报告（路径已自动复制）：\n" + RuntimePerformanceReportRecorder.LastReportPath
                    : "未导出过报告。";
        SetLocalizedText(performanceReportStatusText,
            $"当前可回看 {RuntimePerformanceReportRecorder.AvailableSeconds:0.#} 秒（最多 {RuntimePerformanceReportRecorder.MaximumExportSeconds} 秒）。\n{status}");
    }

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
        RefreshRuntimePerformanceReportControls();
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
