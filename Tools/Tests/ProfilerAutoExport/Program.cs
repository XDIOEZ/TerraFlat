using System.Text.Json;
using FlatWorld.Automation;
using UnityEditor;
using UnityEditorInternal;

internal static class Program
{
    private static int passed;

    private static void Main()
    {
        Test("默认关闭，空闲时不改录制与暂停", () =>
        {
            Frame(0, 100, 1);
            Check(!ProfilerDriver.enabled && !EditorApplication.isPaused && ProfilerDriver.reads == 0);
        });
        Test("非 PlayMode 不采集", () =>
        {
            EditorApplication.isPlaying = false;
            Start(); Frame(0, 100, 1);
            Check(!ProfilerDriver.enabled && !EditorApplication.isPaused);
        });
        Test("开启时忽略已有慢帧，重复更新不重复读取", () =>
        {
            ProfilerDriver.Frames[0] = 100;
            Start(); EditorApplication.Tick(1);
            Check(ProfilerDriver.reads == 0 && !EditorApplication.isPaused);
            Frame(1, 16, 2);
            EditorApplication.Tick(3);
            Check(ProfilerDriver.reads == 1 && !EditorApplication.isPaused);
        });
        Test("严格小于阈值才触发", () =>
        {
            ProfilerAutoExporter.ThresholdFps = 25;
            Start(); Frame(0, 40, 1);
            Check(!EditorApplication.isPaused);
            Frame(1, 41, 2);
            Check(EditorApplication.isPaused);
        });
        #region 持续低帧率时间窗口回归

        Test("必须先观察完整五秒，前四秒全部低帧也不提前触发", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(80, 50);
            Check(!EditorApplication.isPaused && Near(ProfilerAutoExporter.ObservedSeconds, 4));
            SampleFrames(20, 50);
            Check(EditorApplication.isPaused);
        });
        Test("五秒内四秒低帧按耗时触发，不按慢帧数量占比", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(100, 10); SampleFrames(79, 50);
            Check(!EditorApplication.isPaused);
            SampleFrames(1, 50); EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 1);
            using var metadata = JsonDocument.Parse(ProfilerSnapshotToolbar.Metadata);
            var root = metadata.RootElement;
            Check(root.GetProperty("schemaVersion").GetInt32() == 2);
            Check(root.GetProperty("reason").GetString() == "low-fps-time-ratio");
            Check(Near(root.GetProperty("durationSeconds").GetDouble(), 5));
            Check(Near(root.GetProperty("observedSeconds").GetDouble(), 5));
            Check(Near(root.GetProperty("lowFpsSeconds").GetDouble(), 4));
            Check(Near(root.GetProperty("lowFpsRatio").GetDouble(), 0.8));
            Check(Near(root.GetProperty("requiredLowFpsRatio").GetDouble(), 0.8));
            Check(Near(root.GetProperty("windowEndProfilerTimeMs").GetDouble() -
                root.GetProperty("windowStartProfilerTimeMs").GetDouble(), 5000));
        });
        Test("不连续的两秒低帧加两秒低帧也累计触发", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(40, 50); SampleFrames(100, 10); SampleFrames(39, 50);
            Check(!EditorApplication.isPaused);
            SampleFrames(1, 50); Check(EditorApplication.isPaused);
        });
        Test("五秒里短暂掉帧不会触发", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(10, 50); SampleFrames(450, 10);
            Check(!EditorApplication.isPaused && Near(ProfilerAutoExporter.LowFpsSeconds, 0.5));
        });
        Test("五秒累计三点九九秒低帧仍不触发", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(101, 10); SampleFrames(79, 50);
            SampleFrames(1, 40);
            Check(!EditorApplication.isPaused && Near(ProfilerAutoExporter.ObservedSeconds, 5));
            Check(Near(ProfilerAutoExporter.LowFpsSeconds, 3.99));
        });
        Test("填满观察窗口时当前帧已恢复，累计四秒仍会触发", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(80, 50); SampleFrames(100, 10); EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 1);
            using var metadata = JsonDocument.Parse(ProfilerSnapshotToolbar.Metadata);
            Check(metadata.RootElement.GetProperty("measuredFps").GetDouble() == 100);
            Check(Near(metadata.RootElement.GetProperty("lowFpsSeconds").GetDouble(), 4));
        });
        Test("滚动窗口只算边界内的部分帧，过期慢帧不继续累计", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(1, 3500); SampleFrames(200, 10); SampleFrames(20, 50);
            Check(!EditorApplication.isPaused && Near(ProfilerAutoExporter.LowFpsSeconds, 3));
            SampleFrames(40, 50);
            Check(!EditorApplication.isPaused && Near(ProfilerAutoExporter.LowFpsSeconds, 3));
            SampleFrames(20, 50); Check(EditorApplication.isPaused);
        });
        Test("旧窗口中的低帧时间不会跨窗口凑够四秒", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(60, 50); SampleFrames(300, 10); SampleFrames(60, 50);
            Check(!EditorApplication.isPaused && Near(ProfilerAutoExporter.LowFpsSeconds, 3));
            SampleFrames(20, 50); Check(EditorApplication.isPaused);
        });
        Test("Profiler 时间轴空档不会算作低帧时间", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(80, 50);
            ProfilerDriver.FrameStarts[80] = 6000;
            Frame(80, 50, 6.05);
            Check(!EditorApplication.isPaused && Near(ProfilerAutoExporter.LowFpsSeconds, 3));
        });
        Test("单帧卡住超过窗口时最多累计一个窗口的时间", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(1, 10000); EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 1);
            using var metadata = JsonDocument.Parse(ProfilerSnapshotToolbar.Metadata);
            Check(Near(metadata.RootElement.GetProperty("lowFpsSeconds").GetDouble(), 5));
            Check(Near(metadata.RootElement.GetProperty("lowFpsRatio").GetDouble(), 1));
        });
        Test("Profiler 帧区间重叠时不会重复累计", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(1, 3000);
            ProfilerDriver.FrameStarts[1] = 2000;
            Frame(1, 3000, 5); EditorApplication.FlushDelay();
            using var metadata = JsonDocument.Parse(ProfilerSnapshotToolbar.Metadata);
            Check(Near(metadata.RootElement.GetProperty("lowFpsSeconds").GetDouble(), 5));
            Check(Near(metadata.RootElement.GetProperty("lowFpsRatio").GetDouble(), 1));
        });
        Test("修改持续时间清空旧窗口并保存设置", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(60, 50);
            ProfilerAutoExporter.DurationSeconds = 10;
            Check(ProfilerAutoExporter.ObservedSeconds == 0 && ProfilerAutoExporter.LowFpsSeconds == 0);
            Check(EditorPrefs.GetFloat("FlatWorld.ProfilerAuto." + UnityEngine.Application.dataPath + ".DurationSeconds", 0) == 10);
            EditorApplication.Tick(3);
            SampleFrames(80, 50);
            Check(!EditorApplication.isPaused && Near(ProfilerAutoExporter.ObservedSeconds, 4));
        });
        Test("跳过丢失或无效的 Profiler 帧后重新累计", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(60, 50);
            ProfilerDriver.Unavailable.Add(60);
            Frame(60, 50, 3.05); SampleFrames(60, 50);
            Check(!EditorApplication.isPaused && Near(ProfilerAutoExporter.ObservedSeconds, 3));
        });
        Test("Profiler 缓冲覆盖未读取帧时清空旧累计", () =>
        {
            ProfilerAutoExporter.DurationSeconds = 5;
            Start(); SampleFrames(60, 50);
            ProfilerDriver.Frames.Clear(); ProfilerDriver.FrameStarts[70] = 4000;
            Frame(70, 1000, 5);
            Check(!EditorApplication.isPaused && Near(ProfilerAutoExporter.ObservedSeconds, 1));
        });

        #endregion

        Test("先暂停，延迟回调再导出，报告记录精确触发帧", () =>
        {
            Start(); Frame(7, 50, 1);
            Check(EditorApplication.isPaused && ProfilerAutoExporter.IsExportPending && ProfilerSnapshotToolbar.Exports == 0);
            Check(!ProfilerDriver.enabled);
            EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 1 && ProfilerSnapshotToolbar.SelectedFrame == 7);
            Check(EditorApplication.isPaused && !ProfilerDriver.enabled && !ProfilerAutoExporter.IsExportPending);
            using var metadata = JsonDocument.Parse(ProfilerSnapshotToolbar.Metadata);
            var root = metadata.RootElement;
            Check(root.GetProperty("selectedDisplayFrame").GetInt32() == 8);
            Check(root.GetProperty("measuredFps").GetDouble() == 20);
            Check(root.GetProperty("editorPausedBeforeExport").GetBoolean());
            Check(root.GetProperty("snapshotId").GetString() == "test-snapshot");
            for (int i = 0; i < 5; i++) { EditorApplication.Tick(i + 2); EditorApplication.FlushDelay(); }
            Check(ProfilerSnapshotToolbar.Exports == 1);
        });
        Test("不漏掉两次编辑器更新之间的慢帧", () =>
        {
            Start(); ProfilerDriver.Frames[0] = 16; ProfilerDriver.Frames[1] = 100; ProfilerDriver.Frames[2] = 16;
            EditorApplication.Tick(1); EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.SelectedFrame == 1);
        });
        Test("启动缓冲跳过加载慢帧", () =>
        {
            Start(5); Frame(0, 100, 1); Frame(1, 100, 4);
            Check(!EditorApplication.isPaused);
            Frame(2, 16, 5.1); Check(!EditorApplication.isPaused);
            Frame(3, 100, 5.2); Check(EditorApplication.isPaused);
        });
        Test("继续游戏后重新缓冲且不读取暂停前的旧帧", () =>
        {
            Start(2); Frame(0, 100, 3); EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 1);
            EditorApplication.isPaused = false; EditorApplication.Tick(10);
            Frame(1, 100, 11); Check(!EditorApplication.isPaused);
            Frame(2, 16, 12.1); Check(!EditorApplication.isPaused);
            Frame(3, 100, 12.2); EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 2);
        });
        Test("已有录制开关在导出后保持开启", () =>
        {
            ProfilerDriver.enabled = true; Start(); Frame(0, 100, 1); EditorApplication.FlushDelay();
            Check(ProfilerDriver.enabled && EditorApplication.isPaused);
        });
        Test("关闭自动模式恢复自己开启的录制", () =>
        {
            Start(); Check(ProfilerDriver.enabled);
            ProfilerAutoExporter.Enabled = false;
            Check(!ProfilerDriver.enabled);
        });
        Test("手动关闭 Profiler 不被自动模式抢回", () =>
        {
            Start(); ProfilerDriver.enabled = false; EditorApplication.Tick(1);
            Check(!ProfilerAutoExporter.Enabled && !ProfilerDriver.enabled);
        });
        Test("编译/资源导入时不误触发", () =>
        {
            Start(); EditorApplication.isCompiling = true; Frame(0, 100, 1);
            Check(!EditorApplication.isPaused && !ProfilerDriver.enabled);
            EditorApplication.isCompiling = false; EditorApplication.isUpdating = true; EditorApplication.Tick(2);
            Check(!EditorApplication.isPaused && !ProfilerDriver.enabled);
        });
        Test("远程 Player 和 Profile Editor 数据不当成本机游戏 FPS", () =>
        {
            ProfilerDriver.localEditor = false; Start(); Frame(0, 100, 1);
            Check(!ProfilerDriver.enabled && !EditorApplication.isPaused);
            ProfilerDriver.localEditor = true; ProfilerDriver.profileEditor = true; EditorApplication.Tick(2);
            Check(!ProfilerDriver.enabled && !EditorApplication.isPaused);
        });
        Test("无效时长被忽略，未就绪帧会重试", () =>
        {
            Start(); ProfilerDriver.Frames[0] = double.NaN; ProfilerDriver.Frames[1] = double.PositiveInfinity;
            ProfilerDriver.Frames[2] = 0; ProfilerDriver.Frames[3] = -1; ProfilerDriver.Frames[4] = 16;
            EditorApplication.Tick(1); Check(!EditorApplication.isPaused);
            ProfilerDriver.Unavailable.Add(5); Frame(5, 100, 2); Check(!EditorApplication.isPaused);
            ProfilerDriver.Unavailable.Remove(5); EditorApplication.Tick(3); Check(EditorApplication.isPaused);
        });
        Test("清空 Profiler 后重新建立新帧基线", () =>
        {
            Start(); Frame(10, 16, 1); ProfilerDriver.Frames.Clear(); Frame(0, 100, 2);
            EditorApplication.Tick(3); Check(!EditorApplication.isPaused);
            Frame(1, 100, 4); Check(EditorApplication.isPaused);
        });
        Test("中间无效帧不会阻塞后续慢帧", () =>
        {
            Start(); ProfilerDriver.Frames[0] = 16; ProfilerDriver.Unavailable.Add(0);
            Frame(1, 100, 1); EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 1 && ProfilerSnapshotToolbar.SelectedFrame == 1);
        });
        Test("暂停后关闭自动模式可取消尚未写盘的导出", () =>
        {
            Start(); Frame(0, 100, 1); ProfilerAutoExporter.Enabled = false; EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 0 && EditorApplication.isPaused && !ProfilerDriver.enabled);
        });
        Test("暂停后手动继续不强行重新暂停或导出", () =>
        {
            Start(); Frame(0, 100, 1); EditorApplication.isPaused = false; EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 0 && !EditorApplication.isPaused);
        });
        Test("导出失败自动停止，保留暂停且不重复报错", () =>
        {
            Start(); ProfilerSnapshotToolbar.ThrowOnExport = true; Frame(0, 100, 1); EditorApplication.FlushDelay();
            Check(!ProfilerAutoExporter.Enabled && EditorApplication.isPaused && !ProfilerDriver.enabled);
            int logs = UnityEngine.Debug.Messages.Count;
            EditorApplication.Tick(2); EditorApplication.FlushDelay();
            Check(UnityEngine.Debug.Messages.Count == logs);
        });
        Test("触发后切换目标不导出其他目标的数据", () =>
        {
            Start(); Frame(0, 100, 1); ProfilerDriver.connectedProfiler = 2; EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 0 && !ProfilerAutoExporter.Enabled && EditorApplication.isPaused);
        });
        Test("等待写盘时触发帧失效不会导出替代帧", () =>
        {
            Start(); Frame(0, 100, 1); ProfilerDriver.Frames[0] = 16; EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 0 && !ProfilerAutoExporter.Enabled && EditorApplication.isPaused);
        });
        Test("触发帧耗时相同但时间戳已被替换时取消取证", () =>
        {
            Start(); Frame(0, 100, 1); ProfilerDriver.FrameStarts[0] = 5000; EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 0 && !ProfilerAutoExporter.Enabled && EditorApplication.isPaused);
        });
        Test("等待写盘时恢复录制会取消取证且保留外部开关", () =>
        {
            Start(); Frame(0, 100, 1); ProfilerDriver.enabled = true; EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 0 && !ProfilerAutoExporter.Enabled && ProfilerDriver.enabled);
        });
        Test("退出 PlayMode 取消导出并恢复录制", () =>
        {
            Start(); Frame(0, 100, 1); EditorApplication.ExitPlayMode(); EditorApplication.FlushDelay();
            Check(ProfilerSnapshotToolbar.Exports == 0 && !ProfilerDriver.enabled);
        });
        Test("设置拒绝 NaN、无穷和越界值", () =>
        {
            ProfilerAutoExporter.ThresholdFps = float.NaN; Check(ProfilerAutoExporter.ThresholdFps == 30);
            ProfilerAutoExporter.ThresholdFps = -2; Check(ProfilerAutoExporter.ThresholdFps == 1);
            ProfilerAutoExporter.WarmupSeconds = float.PositiveInfinity; Check(ProfilerAutoExporter.WarmupSeconds == 5);
            ProfilerAutoExporter.WarmupSeconds = -1; Check(ProfilerAutoExporter.WarmupSeconds == 0);
            ProfilerAutoExporter.DurationSeconds = float.NaN; Check(ProfilerAutoExporter.DurationSeconds == 5);
            ProfilerAutoExporter.DurationSeconds = float.PositiveInfinity; Check(ProfilerAutoExporter.DurationSeconds == 5);
            ProfilerAutoExporter.DurationSeconds = -1; Check(ProfilerAutoExporter.DurationSeconds == 0.01f);
            ProfilerAutoExporter.DurationSeconds = 121; Check(ProfilerAutoExporter.DurationSeconds == 120);
        });
        Test("域重载前恢复开关并移除监听", () =>
        {
            Start(); AssemblyReloadEvents.Reload(); Frame(0, 100, 1); EditorApplication.FlushDelay();
            Check(!ProfilerDriver.enabled && !EditorApplication.isPaused && ProfilerSnapshotToolbar.Exports == 0);
        });
        Console.WriteLine($"PASS: {passed} tests. Production controller compiled directly; Unity APIs/exporter replaced by test doubles.");
    }

    private static void Test(string name, Action test)
    {
        Reset();
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { throw new Exception("FAIL " + name, error); }
    }

    private static void Reset()
    {
        ProfilerAutoExporter.Enabled = false;
        EditorApplication.isPaused = false;
        EditorApplication.isPlaying = true;
        EditorApplication.isCompiling = EditorApplication.isUpdating = false;
        EditorApplication.timeSinceStartup = 0;
        ProfilerDriver.enabled = ProfilerDriver.profileEditor = false;
        ProfilerDriver.localEditor = true; ProfilerDriver.connectedProfiler = 1;
        ProfilerDriver.Frames.Clear(); ProfilerDriver.FrameStarts.Clear(); ProfilerDriver.Unavailable.Clear(); ProfilerDriver.reads = 0;
        ProfilerAutoExporter.ThresholdFps = 30; ProfilerAutoExporter.WarmupSeconds = 0;
        ProfilerAutoExporter.DurationSeconds = 0.01f;
        ProfilerSnapshotToolbar.Exports = 0; ProfilerSnapshotToolbar.SelectedFrame = -1;
        ProfilerSnapshotToolbar.ThrowOnExport = ProfilerSnapshotToolbar.IsExporting = false;
        UnityEngine.Debug.Messages.Clear();
    }

    private static void Start(float warmup = 0)
    {
        ProfilerAutoExporter.WarmupSeconds = warmup;
        ProfilerAutoExporter.Enabled = true;
        EditorApplication.Tick(0);
    }

    private static void Frame(int index, double milliseconds, double now)
    {
        ProfilerDriver.Frames[index] = milliseconds;
        EditorApplication.Tick(now);
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed");
    }

    private static void SampleFrames(int count, double milliseconds)
    {
        for (int i = 0; i < count; i++)
            Frame(ProfilerDriver.lastFrameIndex + 1, milliseconds, EditorApplication.timeSinceStartup + milliseconds / 1000d);
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 0.000001d;
}
