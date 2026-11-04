using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
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
    private const string PatchRequestPath = "Library/CodexRunAndroidPatch.request";
    private const string PatchResultPath = "Library/CodexAndroidPatch.result.json";
    private const int RunnerRevision = 12;
    private static bool _scheduled;

    [MenuItem("Tools/Android/自动补丁并运行 (Patch And Run)")]
    private static void RequestFastPatch()
    {
        File.WriteAllText(PatchRequestPath, "auto");
        ScheduleIfRequested();
    }

    [MenuItem("Tools/Android/仅脚本补丁并运行 (Scripts Only)")]
    private static void RequestScriptsOnlyPatch()
    {
        File.WriteAllText(PatchRequestPath, "scripts");
        ScheduleIfRequested();
    }

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
        if (_scheduled || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
            (!File.Exists(RequestPath) && !File.Exists(PatchRequestPath)))
            return;

        _scheduled = true;
        EditorApplication.update += Run;
    }

    /// <summary>使用当前启用场景生成 APK，且不传入跳过内容校验的命令行参数。</summary>
    private static void Run()
    {
        EditorApplication.update -= Run;
        try { RunRequestedAndroidBuild(); }
        finally { _scheduled = false; }
    }

    private static void RunRequestedAndroidBuild()
    {
        bool patch = File.Exists(PatchRequestPath);
        string[] patchRequestLines = patch ? File.ReadAllLines(PatchRequestPath) : Array.Empty<string>();
        bool scriptOnly = patch && string.Equals(
            patchRequestLines.FirstOrDefault()?.Trim(), "scripts", StringComparison.OrdinalIgnoreCase);
        string preferredDevice = patchRequestLines.Length > 1 ? patchRequestLines[1].Trim() : null;
        string requestPath = patch ? PatchRequestPath : RequestPath;
        if (!File.Exists(requestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
        {
            return;
        }

        File.Delete(requestPath);
        string resultPath = patch ? PatchResultPath : ResultPath;
        string runId = Guid.NewGuid().ToString("N");
        DateTimeOffset startedAt = DateTimeOffset.Now;
        var buildClock = System.Diagnostics.Stopwatch.StartNew();
        WriteResult(new
        {
            runId,
            runnerRevision = RunnerRevision,
            mode = scriptOnly ? "ScriptsOnlyPatchAndRun" : patch ? "PatchAndRun" : "FullBuild",
            success = false,
            result = "Running",
            startedAt = startedAt.ToString("O")
        }, resultPath: resultPath);
        try
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("当前 Unity 构建目标不是 Android。");
            // 菜单与文件请求共用 Build Settings 的明确目标，不允许默认设备枚举回退。
            if (patch)
                preferredDevice = SelectAndroidDeploymentDevice(preferredDevice);
            if (patch && !HasConnectedAndroidDevice(preferredDevice))
                throw new InvalidOperationException("无线/USB ADB 尚未连接已授权的 Android 手机，取消补丁构建。请先执行 adb devices -l。");

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("Build Settings 中没有启用的场景。");

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                                 ?? throw new InvalidOperationException("无法确定项目根目录。");
            string outputPath = Path.Combine(projectRoot, "Builds", "Android",
                patch ? "FlatWorld-Android-FastPatch.apk" : $"FlatWorld-Android-{DateTime.Now:yyyyMMdd-HHmmss}.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);
            EditorUserBuildSettings.buildAppBundle = false;

            // 自动补丁由 Unity 判断增量范围；仅脚本模式才强制跳过 Player 数据。
            BuildOptions buildOptions = patch
                ? BuildOptions.Development | BuildOptions.PatchPackage | BuildOptions.AutoRunPlayer |
                  (scriptOnly ? BuildOptions.BuildScriptsOnly : BuildOptions.None)
                : BuildOptions.None;
            FlatWorldBuildContentPolicy.SkipContentGeneration = scriptOnly;
            BuildReport report = BuildWithoutReportPopup(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = buildOptions
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
                deploymentDevice = preferredDevice,
                buildOptions = buildOptions.ToString(),
                mode = scriptOnly ? "ScriptsOnlyPatchAndRun" : patch ? "PatchAndRun" : "FullBuild",
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
            }, Path.ChangeExtension(outputPath, ".build.json"), resultPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteResult(new
            {
                runId,
                runnerRevision = RunnerRevision,
                mode = scriptOnly ? "ScriptsOnlyPatchAndRun" : patch ? "PatchAndRun" : "FullBuild",
                deploymentDevice = preferredDevice,
                startedAt = startedAt.ToString("O"),
                completedAt = DateTimeOffset.Now.ToString("O"),
                wallDurationSeconds = buildClock.Elapsed.TotalSeconds,
                success = false,
                result = "Exception",
                exception = exception.ToString()
            }, resultPath: resultPath);
        }
        finally
        {
            FlatWorldBuildContentPolicy.SkipContentGeneration = false;
        }
    }

    /// <summary>未连接真机时直接拒绝耗时构建，不卸载现有 APK 或清理用户数据。</summary>
    private static bool HasConnectedAndroidDevice(string requiredSerial = null)
    {
        string editorDirectory = Path.GetDirectoryName(EditorApplication.applicationPath);
        string adbPath = Path.Combine(editorDirectory, "Data", "PlaybackEngines", "AndroidPlayer",
            "SDK", "platform-tools", "adb.exe");
        if (!File.Exists(adbPath))
            throw new FileNotFoundException("找不到 Unity 自带的 ADB。", adbPath);

        using var adb = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = "devices -l",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            }
        };
        adb.Start();
        if (!adb.WaitForExit(5000))
        {
            adb.Kill();
            return false;
        }

        string output = adb.StandardOutput.ReadToEnd();
        return adb.ExitCode == 0 && output.Split('\n')
            .Any(line =>
            {
                string[] columns = line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                return columns.Length >= 2 && columns[1] == "device" &&
                       (string.IsNullOrWhiteSpace(requiredSerial) ||
                        string.Equals(columns[0], requiredSerial, StringComparison.OrdinalIgnoreCase));
            });
    }

    /// <summary>按本机请求选择无线 ADB 目标，避免重复设备同时连接时部署到错误通道。</summary>
    private static string SelectAndroidDeploymentDevice(string deviceSerial)
    {
        PropertyInfo property = typeof(EditorUserBuildSettings).GetProperty(
            "androidCurrentDeploymentTargetId", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null || !property.CanWrite)
            throw new InvalidOperationException("当前 Unity Editor 不支持设置 Android 设备目标。");
        if (string.IsNullOrWhiteSpace(deviceSerial))
            deviceSerial = property.GetValue(null) as string;
        if (string.IsNullOrWhiteSpace(deviceSerial))
            throw new InvalidOperationException("请在 Android Build Settings 的 Run Device 中选择明确设备，或在补丁请求第二行填写 ADB 序列号。");
        property.SetValue(null, deviceSerial);
        if (!string.Equals(property.GetValue(null) as string, deviceSerial, StringComparison.Ordinal))
            throw new InvalidOperationException("Unity 未接受指定的 Android 部署设备。");
        Debug.Log($"[AndroidPatch] 已指定补丁部署设备：{deviceSerial}");
        return deviceSerial;
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
        var previousAddressablesBuilder = AddressablesPlayerBuildProcessor.BuildAddressablesOverride;
        try
        {
            autoOpenReport.SetValue(null, false);
            // 记录正式资源构建边界，区分资源等待与 Player/设备部署耗时。
            AddressablesPlayerBuildProcessor.BuildAddressablesOverride = settings =>
            {
                Debug.Log("[AndroidPatch] Addressables 构建开始。");
                var clock = System.Diagnostics.Stopwatch.StartNew();
                AddressablesPlayerBuildResult result = null;
                try
                {
                    if (previousAddressablesBuilder != null)
                        result = previousAddressablesBuilder(settings);
                    else
                        AddressableAssetSettings.BuildPlayerContent(out result);
                    return result;
                }
                finally
                {
                    Debug.Log($"[AndroidPatch] Addressables 构建结束：{clock.Elapsed.TotalSeconds:F3}s，错误：{result?.Error}");
                }
            };
            return BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            autoOpenReport.SetValue(null, previousAutoOpen);
            AddressablesPlayerBuildProcessor.BuildAddressablesOverride = previousAddressablesBuilder;
        }
    }

    #endregion

    #region 构建结果保存

    /// <summary>以 UTF-8 JSON 保存本地构建结果。</summary>
    private static void WriteResult(object value, string archivePath = null, string resultPath = ResultPath)
    {
        string json = JsonConvert.SerializeObject(value, Formatting.Indented);
        File.WriteAllText(resultPath, json);
        if (archivePath != null)
            File.WriteAllText(archivePath, json);
    }

    #endregion
}

/// <summary>脚本补丁复用上一版完整 Player 资源，正式构建仍执行全部资源烘焙。</summary>
public static class FlatWorldBuildContentPolicy
{
    public static bool SkipContentGeneration { get; internal set; }
}
