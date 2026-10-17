using System;
using TMPro;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 设备内存数据分页

    private const float DeviceDataRefreshInterval = 1f;

    private TextMeshProUGUI deviceMemoryText;
    private float nextDeviceDataRefreshAt;
    private bool processMemoryUnavailable;

    private void BuildDevicePage()
    {
        GmPageView page = CreatePage(GmPageId.Device);
        AddPageIntro(page.Content, "设备数据", "当前游戏内存与运行设备信息；仅在本页打开时每秒刷新一次。");

        TextMeshProUGUI memoryTitle = CreateText(page.Content, "内存占用", 18f, GmTextPrimary);
        memoryTitle.fontStyle = FontStyles.Bold;
        memoryTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

        deviceMemoryText = CreateText(page.Content, "正在读取内存数据…", 16f, GmTextPrimary);
        deviceMemoryText.enableWordWrapping = true;
        deviceMemoryText.gameObject.AddComponent<LayoutElement>().preferredHeight = 154f;
        RegisterSearchEntry(GmPageId.Device, "游戏内存占用", "设备 内存 RAM Memory Unity Mono 进程", deviceMemoryText.rectTransform);

        AddPageHint(page.Content,
            Application.isEditor
                ? "编辑器模式：进程驻留内存包含 Unity 编辑器；Unity 已分配内存可用于观察游戏运行时的变化。"
                : "进程驻留内存表示游戏进程当前驻留在物理内存中的容量；Unity 已预留内存不等于额外占用。",
            44f);

        TextMeshProUGUI deviceTitle = CreateText(page.Content, "运行设备", 18f, GmTextPrimary);
        deviceTitle.fontStyle = FontStyles.Bold;
        deviceTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

        TextMeshProUGUI deviceInfo = CreateText(page.Content,
            $"平台：{Application.platform}\n处理器：{SystemInfo.processorType}（{SystemInfo.processorCount} 逻辑线程）\n" +
            $"设备物理内存：{SystemInfo.systemMemorySize} MB\n显卡：{SystemInfo.graphicsDeviceName}",
            13f,
            GmTextSecondary);
        deviceInfo.enableWordWrapping = true;
        deviceInfo.gameObject.AddComponent<LayoutElement>().preferredHeight = 112f;
        BuildPerformanceDebugControls(page.Content);
    }

    // 不进入设备数据页时不采样，避免诊断功能影响正常游戏帧率。
    private void RefreshDevicePageIfNeeded()
    {
        if (windowRoot == null || !windowRoot.activeSelf || activeGmPage != GmPageId.Device ||
            Time.unscaledTime < nextDeviceDataRefreshAt)
            return;

        RefreshDevicePage();
    }

    private void RefreshDevicePage()
    {
        RefreshPerformanceDebugControls();
        if (deviceMemoryText == null)
            return;

        nextDeviceDataRefreshAt = Time.unscaledTime + DeviceDataRefreshInterval;
        string processName = Application.isEditor ? "编辑器进程驻留" : "游戏进程驻留";
        string processMemory = "当前平台不支持读取";
        if (!processMemoryUnavailable)
        {
            try
            {
                using (System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess())
                    processMemory = FormatDeviceMemory(process.WorkingSet64);
            }
            catch (Exception)
            {
                // 部分移动平台不提供进程驻留统计，保留 Unity 自身内存数据。
                processMemoryUnavailable = true;
            }
        }

        deviceMemoryText.text =
            $"{processName}：{processMemory}\n" +
            $"Unity 已分配：{FormatDeviceMemory(Profiler.GetTotalAllocatedMemoryLong())}\n" +
            $"Unity 已预留：{FormatDeviceMemory(Profiler.GetTotalReservedMemoryLong())}\n" +
            $"Mono 堆已使用：{FormatDeviceMemory(Profiler.GetMonoUsedSizeLong())}\n" +
            $"Mono 堆容量：{FormatDeviceMemory(Profiler.GetMonoHeapSizeLong())}";
    }

    private static string FormatDeviceMemory(long bytes)
    {
        return bytes >= 0 ? $"{bytes / 1048576d:F1} MiB" : "不可用";
    }

    #endregion
}
