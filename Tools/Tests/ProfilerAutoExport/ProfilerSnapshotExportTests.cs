using System.Text.Json;
using PuertsUnityMcp.Editor;
using UnityEditor;
using UnityEditorInternal;

internal static class ProfilerSnapshotExportTests
{
    #region 可配置邻帧范围与真实 JSON 导出回归

    internal static void Run(Action<string, Action> test)
    {
        test("默认只导出六十帧摘要，从三百邻帧中挑出低于五十毫秒的最慢三帧", () =>
        {
            Frames();
            ProfilerDriver.Frames[0] = ProfilerDriver.Frames[400] = 500;
            ProfilerDriver.Frames[50] = 40;
            ProfilerDriver.Frames[349] = 30;
            ProfilerDriver.Frames[350] = 20;
            WithReport(200, 60, 300, 3, root =>
            {
                Check(root.GetProperty("schemaVersion").GetInt32() == 5);
                Check(Summaries(root).SequenceEqual(Enumerable.Range(171, 30).Concat(Enumerable.Range(202, 30))));
                Check(Details(root).SequenceEqual(new[] { 51, 350, 351 }));
                Check(root.GetProperty("callTrees").GetArrayLength() == 1);
                Check(root.GetProperty("additionalFrameDetails").EnumerateArray().All(frame => frame.GetProperty("available").GetBoolean()));
                Check(!root.GetProperty("additionalFramePolicy").TryGetProperty("exclusiveThresholdMs", out _));
                Check(root.GetProperty("additionalFramePolicy").GetProperty("firstSearchDisplayFrame").GetInt32() == 51);
                Check(root.GetProperty("additionalFramePolicy").GetProperty("lastSearchDisplayFrame").GetInt32() == 351);
            });
        });
        test("摘要范围大于筛选范围时仍只从筛选范围挑帧，排除目标帧并按帧号处理同耗时", () =>
        {
            Frames();
            ProfilerDriver.Frames[200] = 1000;
            ProfilerDriver.Frames[175] = 900;
            ProfilerDriver.Frames[199] = ProfilerDriver.Frames[201] = 30;
            WithReport(200, 60, 4, 2, root => Check(Details(root).SequenceEqual(new[] { 200, 202 })));
        });
        test("窗口开头不足不向后补齐，详细帧数量也只使用有效候选", () =>
        {
            Frames(3);
            WithReport(0, 60, 300, 8, root =>
            {
                Check(Summaries(root).SequenceEqual(new[] { 2, 3 }));
                Check(Details(root).SequenceEqual(new[] { 2, 3 }));
            });
        });
        test("最新帧只保留前半范围，不把缺失后文补到前面", () =>
        {
            Frames();
            WithReport(400, 60, 300, 3, root =>
            {
                Check(Summaries(root).SequenceEqual(Enumerable.Range(371, 30)));
                Check(Details(root).SequenceEqual(new[] { 251, 252, 253 }));
            });
        });
        test("三项配置分别生效，奇数邻帧数在前方多取一帧", () =>
        {
            Frames();
            WithReport(200, 5, 9, 5, root =>
            {
                Check(Summaries(root).SequenceEqual(new[] { 198, 199, 200, 202, 203 }));
                Check(Details(root).SequenceEqual(new[] { 196, 197, 198, 199, 200 }));
                Check(root.GetProperty("additionalFramePolicy").GetProperty("maximumAdditionalFrames").GetInt32() == 5);
            });
        });
        test("无效或缺失帧不能被选为最慢帧，摘要保留不可用状态", () =>
        {
            Frames();
            ProfilerDriver.Unavailable.Add(199);
            ProfilerDriver.Frames[199] = 1000;
            ProfilerDriver.Frames[201] = 0;
            ProfilerDriver.Frames[202] = double.NaN;
            ProfilerDriver.Frames[203] = double.PositiveInfinity;
            WithReport(200, 2, 8, 10, root =>
            {
                Check(Details(root).SequenceEqual(new[] { 197, 198, 199, 205 }));
                Check(root.GetProperty("nearbyFrames").EnumerateArray().All(frame => !frame.GetProperty("available").GetBoolean()));
            });
        });
        test("摘要与额外详情均可关闭，禁用详情时不读取大筛选范围", () =>
        {
            Frames();
            WithReport(200, 0, int.MaxValue, 0, root =>
            {
                Check(!Summaries(root).Any() && !Details(root).Any());
                Check(ProfilerDriver.reads == 2);
            });
        });
        test("关闭摘要仍能从筛选范围选出额外详情", () =>
        {
            Frames();
            WithReport(200, 0, 6, 2, root => Check(!Summaries(root).Any() && Details(root).SequenceEqual(new[] { 198, 199 })));
        });
        test("大范围按缓存裁剪且不发生整数溢出", () =>
        {
            Frames(5);
            WithReport(2, int.MaxValue, int.MaxValue, int.MaxValue, root =>
            {
                Check(Summaries(root).SequenceEqual(new[] { 1, 2, 4, 5 }));
                Check(Details(root).SequenceEqual(new[] { 1, 2, 4, 5 }));
            });
        });
        test("手动快照保持播放暂停和录制状态，不修改附件回调的帧号", () =>
        {
            Frames();
            EditorApplication.isPaused = true;
            ProfilerDriver.enabled = true;
            WithReport(200, 4, 6, 2, root => Check(EditorApplication.isPaused && ProfilerDriver.enabled));
        });
    }

    private static void Frames(int count = 401)
    {
        for (int frame = 0; frame < count; frame++) ProfilerDriver.Frames[frame] = 10;
    }

    private static IEnumerable<int> Summaries(JsonElement root) => root.GetProperty("nearbyFrames")
        .EnumerateArray().Select(frame => frame.GetProperty("displayFrame").GetInt32());

    private static IEnumerable<int> Details(JsonElement root) => root.GetProperty("additionalFrameDetails")
        .EnumerateArray().Select(frame => frame.GetProperty("displayFrame").GetInt32());

    private static void WithReport(int frame, int summaryCount, int selectionCount, int detailCount, Action<JsonElement> validate)
    {
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        string root = Path.GetFullPath(Path.Combine(temporaryRoot, "FlatWorld-ProfilerSnapshotTest-" + Guid.NewGuid().ToString("N")));
        try
        {
            Directory.CreateDirectory(root);
            string directory = UnityMcpProfilerSnapshot.ExportSelectedFrame(frame, root, summaryCount,
                (pending, id, displayFrame) =>
                {
                    Check(Path.GetFileName(pending).StartsWith(".pending-") && displayFrame == frame + 1);
                    File.WriteAllText(Path.Combine(pending, "extra.json"), "{}");
                    return new[] { "extra.json" };
                }, selectionCount, detailCount);
            Check(!Directory.GetDirectories(root, ".pending-*").Any() && File.Exists(Path.Combine(directory, "extra.json")));
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "profiler-analysis.json")));
            using var console = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "console-logs.json")));
            Check(report.RootElement.GetProperty("snapshotId").GetString() == console.RootElement.GetProperty("snapshotId").GetString());
            validate(report.RootElement);
        }
        finally
        {
            if (Path.GetDirectoryName(root) == temporaryRoot.TrimEnd(Path.DirectorySeparatorChar) && Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Snapshot export assertion failed");
    }

    #endregion
}
