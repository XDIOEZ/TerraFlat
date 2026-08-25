using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>仅在收到 Library 请求时执行一次严格 Android APK 构建，并输出结构化结果。</summary>
[InitializeOnLoad]
internal static class CodexAndroidBuildRunner
{
    private const string RequestPath = "Library/CodexRunAndroidBuild.request";
    private const string ResultPath = "Library/CodexAndroidBuild.result.json";
    private const int RunnerRevision = 5;
    private static bool _scheduled;

    /// <summary>脚本加载后检查是否存在待处理的本地构建请求。</summary>
    static CodexAndroidBuildRunner()
    {
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
        WriteResult(new
        {
            success = false,
            result = "Running",
            startedAt = DateTime.Now.ToString("O")
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

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });

            BuildSummary summary = report.summary;
            object[] messages = report.steps
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
                success = summary.result == BuildResult.Succeeded,
                result = summary.result.ToString(),
                outputPath,
                totalErrors = summary.totalErrors,
                totalWarnings = summary.totalWarnings,
                totalSize = summary.totalSize,
                durationSeconds = summary.totalTime.TotalSeconds,
                messages
            });
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteResult(new
            {
                success = false,
                result = "Exception",
                exception = exception.ToString()
            });
        }
    }

    /// <summary>以 UTF-8 JSON 保存本地构建结果。</summary>
    private static void WriteResult(object value)
    {
        File.WriteAllText(ResultPath, JsonConvert.SerializeObject(value, Formatting.Indented));
    }
}
