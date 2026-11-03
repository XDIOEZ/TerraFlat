using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using UnityEngine;

string temporary = Path.Combine(Path.GetTempPath(), "FlatWorld-RuntimePerformance-" + Guid.NewGuid().ToString("N"));
Application.persistentDataPath = temporary;
try
{
    StartRecorder();
    Check(!RuntimePerformanceReportRecorder.TryExportRecentSeconds(30, out _), "空数据不得伪造报告");
    Check(!RuntimePerformanceReportRecorder.TryExportRecentSeconds(4, out _), "范围下界为5秒");
    Check(!RuntimePerformanceReportRecorder.TryExportRecentSeconds(121, out _), "范围上界为120秒");

    for (int i = 1; i <= 60; i++) Sample(i, i == 55 ? 50f : 16f);
    Check(RuntimePerformanceReportRecorder.AvailableSeconds >= 59, "回看时长应由真实缓存计算");
    Check(RuntimePerformanceReportRecorder.TryExportRecentSeconds(30, out _), "30秒导出应成功启动");
    string fullReport = AwaitExport();
    using (JsonDocument json = ReadArchive(fullReport))
    {
        JsonElement report = json.RootElement;
        Check(report.GetProperty("requestedSeconds").GetInt32() == 30, "保留选择的范围");
        Check(report.GetProperty("completeWindow").GetBoolean(), "完整历史应报告完整覆盖");
        Check(report.GetProperty("frameCount").GetInt32() == 31, "只选最近30秒内的样本");
        Check(report.GetProperty("frames").GetArrayLength() == 31, "序列不丢帧");
        Check(report.GetProperty("slowestFrameMilliseconds").GetSingle() == 50f, "慢帧进入导出");
        Check(report.GetProperty("p99FrameMilliseconds").GetSingle() == 50f, "慢帧分位数正确");
        Check(!report.GetProperty("performanceStatisticsEnabled").GetBoolean(), "未启用统计不伪造累计点位");
    }
    Check(GUIUtility.systemCopyBuffer == fullReport, "输出完成自动复制准确ZIP路径");
    Check(Directory.GetFiles(RuntimePerformanceReportRecorder.ReportsDirectory, "*.pending").Length == 0,
        "正式 ZIP 发布后无临时文件");
    Console.WriteLine("PASS 最近30秒边界、慢帧分位数、ZIP与剪贴板");

    StartRecorder();
    Sample(1, 20f);
    Sample(2, 15f);
    Sample(3, 18f);
    Check(RuntimePerformanceReportRecorder.TryExportRecentSeconds(60, out _), "范围不足仍可导出");
    using (JsonDocument json = ReadArchive(AwaitExport()))
    {
        JsonElement report = json.RootElement;
        Check(!report.GetProperty("completeWindow").GetBoolean(), "不足60秒不能谎称完整");
        Check(report.GetProperty("frameCount").GetInt32() == 3, "不足范围只返回现有帧");
        Check(report.GetProperty("coveredSeconds").GetDouble() < 4d, "报告真实覆盖时长");
    }
    Console.WriteLine("PASS 不足范围保留实际样本并声明缺口");

    StartRecorder();
    for (int i = 1; i <= 33000; i++) Sample(i * 0.01d, 10f);
    Check(RuntimePerformanceReportRecorder.TryExportRecentSeconds(10, out _), "循环缓存覆盖后仍可导出");
    using (JsonDocument json = ReadArchive(AwaitExport()))
    {
        JsonElement report = json.RootElement;
        int count = report.GetProperty("frameCount").GetInt32();
        Check(count >= 999 && count <= 1002, "循环缓存应只选择最近10秒而非旧数据");
        Check(report.GetProperty("completeWindow").GetBoolean(), "环形缓存回绕后仍完整覆盖范围");
    }
    Console.WriteLine("PASS 32768帧缓存回绕后不混入过期帧");
}
finally
{
    if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
}

static void StartRecorder()
{
    InvokeStatic("ResetRuntimeState");
    InvokeStatic("CreateRecorder");
    InvokeInstance("Awake");
}

static void Sample(double seconds, float frameMilliseconds)
{
    Time.realtimeSinceStartupAsDouble = seconds;
    Time.unscaledDeltaTime = frameMilliseconds / 1000f;
    InvokeInstance("Update");
}

static string AwaitExport()
{
    for (int attempt = 0; attempt < 300 && RuntimePerformanceReportRecorder.IsExporting; attempt++)
    {
        Thread.Sleep(10);
        Time.unscaledDeltaTime = 0f;
        InvokeInstance("Update");
    }
    Check(!RuntimePerformanceReportRecorder.IsExporting, "后台导出应按时完成");
    Check(string.IsNullOrEmpty(RuntimePerformanceReportRecorder.LastError),
        "导出错误：" + RuntimePerformanceReportRecorder.LastError);
    string path = RuntimePerformanceReportRecorder.LastReportPath;
    Check(File.Exists(path), "应该发布一个完整的ZIP");
    return path;
}

static JsonDocument ReadArchive(string path)
{
    using var zip = ZipFile.OpenRead(path);
    Check(zip.GetEntry("README.txt") != null && zip.GetEntry("runtime-logs.txt") != null, "报告附件完整");
    using var stream = zip.GetEntry("performance.json").Open();
    return JsonDocument.Parse(stream);
}

static void InvokeStatic(string name) =>
    typeof(RuntimePerformanceReportRecorder).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

static void InvokeInstance(string name) =>
    typeof(RuntimePerformanceReportRecorder).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(GameObject.LastAddedComponent, null);

static void Check(bool success, string reason)
{
    if (!success) throw new InvalidOperationException(reason);
}
