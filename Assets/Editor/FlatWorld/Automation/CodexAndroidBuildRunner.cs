using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>仅在收到 Library 请求时执行一次严格 Android APK 构建，并输出结构化结果。</summary>
[InitializeOnLoad]
internal static class CodexAndroidBuildRunner
{
    #region 构建请求与执行

    private const string RequestPath = "Library/CodexRunAndroidBuild.request";
    private const string ResultPath = "Library/CodexAndroidBuild.result.json";
    private const int RunnerRevision = 7;
    private static bool _scheduled;

    /// <summary>脚本加载后检查是否存在待处理的本地构建请求。</summary>
    static CodexAndroidBuildRunner()
    {
        // 编辑器运行期间也要响应后来提交的构建请求。
        EditorApplication.update += ScheduleIfRequested;
        ScheduleIfRequested();
    }

    /// <summary>在编辑器空闲帧安排构建，避免与脚本编译或资源导入冲突。</summary>
    private static void ScheduleIfRequested()
    {
        _ = RunnerRevision;
        if (_scheduled || !File.Exists(RequestPath))
            return;

        _scheduled = true;
        EditorApplication.delayCall += Run;
    }

    /// <summary>使用当前启用场景生成 APK，且不传入跳过内容校验的命令行参数。</summary>
    private static void Run()
    {
        _scheduled = false;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            ScheduleIfRequested();
            return;
        }

        File.Delete(RequestPath);
        string runId = Guid.NewGuid().ToString("N");
        DateTimeOffset startedAt = DateTimeOffset.Now;
        var buildClock = System.Diagnostics.Stopwatch.StartNew();
        WriteResult(new
        {
            runId,
            runnerRevision = RunnerRevision,
            success = false,
            result = "Running",
            startedAt = startedAt.ToString("O")
        });
        try
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("当前 Unity 构建目标不是 Android。");

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("Build Settings 中没有启用的场景。");

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                                 ?? throw new InvalidOperationException("无法确定项目根目录。");
            string outputPath = Path.Combine(
                projectRoot,
                "Builds",
                "Android",
                $"FlatWorld-Android-{DateTime.Now:yyyyMMdd-HHmmss}.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);
            EditorUserBuildSettings.buildAppBundle = false;

            BuildReport report = BuildWithoutReportPopup(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });

            BuildSummary summary = report.summary;
            BuildStep[] buildSteps = report.steps;
            object[] messages = buildSteps
                .SelectMany(step => step.messages)
                .Where(message => message.type == LogType.Error || message.type == LogType.Warning)
                .Select(message => (object)new
                {
                    type = message.type.ToString(),
                    message = message.content
                })
                .ToArray();
            WriteResult(new
            {
                runId,
                runnerRevision = RunnerRevision,
                startedAt = startedAt.ToString("O"),
                completedAt = DateTimeOffset.Now.ToString("O"),
                success = summary.result == BuildResult.Succeeded,
                result = summary.result.ToString(),
                outputPath,
                totalErrors = summary.totalErrors,
                totalWarnings = summary.totalWarnings,
                totalSize = summary.totalSize,
                apkSizeBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0L,
                durationSeconds = summary.totalTime.TotalSeconds,
                wallDurationSeconds = buildClock.Elapsed.TotalSeconds,
                messages,
                // 保留步骤层级，分析时只对同层耗时求和，避免父子步骤重复计时。
                steps = buildSteps.Select((step, index) => new
                {
                    index,
                    step.name,
                    step.depth,
                    durationSeconds = step.duration.TotalSeconds
                }).ToArray()
            }, Path.ChangeExtension(outputPath, ".build.json"));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteResult(new
            {
                runId,
                runnerRevision = RunnerRevision,
                startedAt = startedAt.ToString("O"),
                completedAt = DateTimeOffset.Now.ToString("O"),
                wallDurationSeconds = buildClock.Elapsed.TotalSeconds,
                success = false,
                result = "Exception",
                exception = exception.ToString()
            });
        }
    }

    #endregion

    #region Addressables 报告窗口

    private static BuildReport BuildWithoutReportPopup(BuildPlayerOptions options)
    {
        // 1.22.3 自动报告窗口会对未关联文件的内存报告调用 ReadFull，自动打包仅暂时跳过弹窗。
        PropertyInfo autoOpenReport = typeof(ProjectConfigData).GetProperty(
            "AutoOpenAddressablesReport", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("无法读取 Addressables 自动报告窗口设置。");
        bool previousAutoOpen = (bool)autoOpenReport.GetValue(null);
        try
        {
            autoOpenReport.SetValue(null, false);
            return BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            autoOpenReport.SetValue(null, previousAutoOpen);
        }
    }

    #endregion

    #region 构建结果保存

    /// <summary>以 UTF-8 JSON 保存本地构建结果。</summary>
    private static void WriteResult(object value, string archivePath = null)
    {
        string json = JsonConvert.SerializeObject(value, Formatting.Indented);
        File.WriteAllText(ResultPath, json);
        if (archivePath != null)
            File.WriteAllText(archivePath, json);
    }

    #endregion
}
