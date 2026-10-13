using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>一轮测试共用一个文件夹结果包，截图写盘后只保留最近十轮。</summary>
public static class GameplayTestArtifacts
{
    #region 结果目录
    private const int RetainedPackageCount = 10;

    public static string GetRunDirectory(string runId)
    {
        GameplayTestSelection.ValidateRunId(runId);
        return Path.Combine(GameplayTestSelection.RootDirectory, "Runs", runId);
    }

    public static void Initialize(GameplayTestReport report)
    {
        if (report == null) throw new ArgumentNullException(nameof(report));
        report.RunDirectory = GetRunDirectory(report.RunId);
        report.ReportPath = Path.Combine(report.RunDirectory, "report.json");
        report.SummaryPath = Path.Combine(report.RunDirectory, "report.md");
        report.ScreenshotPath = Path.Combine(report.RunDirectory, "screenshots", "final.png");
        report.PackagePath = report.RunDirectory;
        Directory.CreateDirectory(report.RunDirectory);
    }

    public static string CaseReportPath(GameplayTestReport report, string caseId)
        => Path.Combine(CaseDirectory(report, caseId), "report.json");

    public static string CaseScreenshotPath(GameplayTestReport report, string caseId)
        => Path.Combine(CaseDirectory(report, caseId), "end.png");

    private static string CaseDirectory(GameplayTestReport report, string caseId)
    {
        if (report == null) throw new ArgumentNullException(nameof(report));
        if (string.IsNullOrEmpty(caseId) || !Regex.IsMatch(caseId, "\\A[a-zA-Z0-9_-]{1,64}\\z") ||
            Regex.IsMatch(caseId, "\\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\\z", RegexOptions.IgnoreCase))
            throw new ArgumentException("无效测试用例编号。", nameof(caseId));
        return Path.Combine(GetRunDirectory(report.RunId), "cases", caseId);
    }
    #endregion

    #region 帧末截图
    public static IEnumerator CaptureScreenshot(MonoBehaviour owner, string path, Action<string> completedError)
    {
        if (owner == null || !owner.isActiveAndEnabled || !Application.isPlaying ||
            Application.isBatchMode || Screen.width <= 0 || Screen.height <= 0 ||
            SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            completedError?.Invoke("当前没有可截图的游戏画面，或运行在无画面的批处理环境。");
            yield break;
        }
        bool completed = false;
        string error = null;
        Coroutine capture = owner.StartCoroutine(CaptureAtFrameEnd(path, message =>
        {
            error = message;
            completed = true;
        }));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // 帧末等待由独立协程负责，外层按真实时间超时，避免切到 Scene 视图时永久卡住。
            while (!completed && owner != null && owner.isActiveAndEnabled && Application.isPlaying &&
                   timer.Elapsed.TotalSeconds < 8d)
                yield return null;
            if (!completed)
                error = owner == null || !Application.isPlaying
                    ? "截图完成前测试场景或 PlayMode 已退出。"
                    : "等待游戏帧末截图超时；请保持 Game 视图可渲染。";
            completedError?.Invoke(error);
        }
        finally
        {
            if (!completed && owner != null && capture != null) owner.StopCoroutine(capture);
        }
    }

    private static IEnumerator CaptureAtFrameEnd(string path, Action<string> completed)
    {
        yield return new WaitForEndOfFrame();
        Texture2D texture = null;
        string error = null;
        try
        {
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null) throw new IOException("Unity 未返回截图纹理。");
            byte[] png = texture.EncodeToPNG();
            if (png == null || png.Length == 0) throw new IOException("截图 PNG 编码为空。");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, png);
        }
        catch (Exception exception) { error = exception.ToString(); }
        finally
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            completed(error);
        }
    }
    #endregion

    #region 阅读报告与结果包
    public static void WriteSummary(GameplayTestReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# FlatWorld 玩法测试报告");
        text.AppendLine();
        text.AppendLine("- 本轮编号：`" + report.RunId + "`");
        text.AppendLine("- 测试清单：`" + report.TestSuitePath + "`");
        text.AppendLine("- 结果：**" + Markdown(report.Status) + "**");
        text.AppendLine($"- 用例：通过 {report.PassedCaseCount}，失败 {report.FailedCaseCount}，跳过 {report.SkippedCaseCount}，中断 {report.InterruptedCaseCount}");
        text.AppendLine($"- 耗时：{report.DurationSeconds:F2} 秒；Unity 错误 {report.ErrorCount}，警告 {report.WarningCount}");
        text.AppendLine("- [完整 JSON 报告](report.json)");
        text.AppendLine();
        if (!string.IsNullOrEmpty(report.ScreenshotPath) && File.Exists(report.ScreenshotPath))
            text.AppendLine("![测试结束画面](screenshots/final.png)");
        else
            text.AppendLine("结束截图：" + Markdown(report.ScreenshotError ?? "未生成。"));
        text.AppendLine();
        text.AppendLine("| 用例 | 结果 | 用例报告 | 结束截图 | 原因 |");
        text.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (GameplayTestCaseReport testCase in report.Cases)
        {
            string casePath = "cases/" + testCase.Id + "/";
            string screenshot = !string.IsNullOrEmpty(testCase.ScreenshotPath) && File.Exists(testCase.ScreenshotPath)
                ? "[截图](" + casePath + "end.png)" : Markdown(testCase.ScreenshotError ?? "未执行或未生成");
            text.AppendLine("| " + Markdown(testCase.Name) + " (`" + testCase.Id + "`) | " +
                Markdown(testCase.Status) + " | [JSON](" + casePath + "report.json) | " + screenshot + " | " +
                Markdown(testCase.Reason) + " |");
        }
        if (!string.IsNullOrEmpty(report.ArtifactError))
        {
            text.AppendLine();
            text.AppendLine("产物写入错误：" + Markdown(report.ArtifactError));
        }
        File.WriteAllText(report.SummaryPath, text.ToString(), new UTF8Encoding(false));
    }

    public static string CreatePackage(string runDirectory)
    {
        string source = Path.GetFullPath(runDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string expected = Path.GetFullPath(GetRunDirectory(Path.GetFileName(source)));
        if (!string.Equals(source, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("测试包只能来自本轮 Runs 结果目录。");
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("本轮测试结果目录不存在：" + source);
        CleanupPackages(source);
        return source;
    }

    private static string Markdown(string value)
        => (value ?? string.Empty).Replace("\\", "\\\\").Replace("|", "\\|")
            .Replace("\r", string.Empty).Replace("\n", "<br>");
    #endregion

    #region 包体留存清理
    private static void CleanupPackages(string currentRunDirectory)
    {
        var root = new DirectoryInfo(Path.GetDirectoryName(currentRunDirectory));
        DirectoryInfo[] directories = Array.FindAll(root.GetDirectories(), directory =>
            (directory.Attributes & FileAttributes.ReparsePoint) == 0 && IsRunId(directory.Name));
        Array.Sort(directories, (left, right) =>
        {
            int order = right.CreationTimeUtc.CompareTo(left.CreationTimeUtc);
            return order != 0 ? order : StringComparer.Ordinal.Compare(right.Name, left.Name);
        });
        // 本轮文件夹计入十份配额，按创建时间清掉更早的结果，历史 ZIP 不再重复留存。
        int retained = 1;
        foreach (DirectoryInfo directory in directories)
        {
            if (string.Equals(directory.FullName, currentRunDirectory, StringComparison.OrdinalIgnoreCase)) continue;
            if (retained < RetainedPackageCount)
            {
                retained++;
                continue;
            }
            try { directory.Delete(true); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("[GameplayTest] 旧测试包清理失败：" + directory.FullName + "\n" + exception.Message);
            }
        }
        foreach (FileInfo archive in root.GetFiles("*.zip*"))
        {
            string suffix = archive.Name.EndsWith(".zip.tmp", StringComparison.OrdinalIgnoreCase) ? ".zip.tmp" : ".zip";
            if (!archive.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                !IsRunId(archive.Name.Substring(0, archive.Name.Length - suffix.Length))) continue;
            try { archive.Delete(); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("[GameplayTest] 重复压缩包清理失败：" + archive.FullName + "\n" + exception.Message);
            }
        }
    }

    private static bool IsRunId(string value)
    {
        try
        {
            GameplayTestSelection.ValidateRunId(value);
            return true;
        }
        catch (ArgumentException) { return false; }
    }
    #endregion
}
