using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>测试流水线通过 Library 中的一次性清单指针驱动场景，不修改正式测试场景资源。</summary>
public static class GameplayTestSelection
{
    #region 清单与报告地址
    public const string DefaultSuitePath = "GameplayTests/test-suite.json";
    public const string QuickSuitePath = "GameplayTests/quick-suite.json";

    public static string RootDirectory => Path.Combine(
        Path.GetDirectoryName(Application.dataPath), "Library", "FlatWorldGameplayTests");
    public static string SelectionPath => Path.Combine(RootDirectory, "Pipeline", "selected-suite.json");
    public static string LastGameplayReportPath => Path.Combine(RootDirectory, "Pipeline", "last-gameplay-report.txt");

    public static string NormalizeSuitePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path != path.Trim())
            throw new InvalidDataException("测试清单路径不能为空。");
        string normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith("GameplayTests/", StringComparison.Ordinal) ||
            !normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            normalized.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) >= 0)
            throw new InvalidDataException("测试清单只能位于 StreamingAssets/GameplayTests 下的相对 JSON 文件。");
        foreach (string segment in normalized.Split('/'))
            if (segment.Length == 0 || segment == "." || segment == ".." ||
                segment.EndsWith(".", StringComparison.Ordinal) ||
                segment.EndsWith(" ", StringComparison.Ordinal))
                throw new InvalidDataException("测试清单包含非法路径片段。");
        foreach (char c in normalized)
            if (char.IsControl(c))
                throw new InvalidDataException("测试清单路径包含控制字符。");
        return normalized;
    }

    public static string GetAbsoluteSuitePath(string path)
    {
        string normalized = NormalizeSuitePath(path);
        string root = Path.GetFullPath(Application.streamingAssetsPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string resolved = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("测试清单路径不能越出 StreamingAssets。");
        return resolved;
    }
    #endregion

    #region 隔离启动选择
    public static void SelectSuite(string path, string runId)
    {
        string normalized = NormalizeSuitePath(path);
        if (string.IsNullOrWhiteSpace(runId) || runId.Length > 80)
            throw new ArgumentException("无效测试运行编号。", nameof(runId));
        Directory.CreateDirectory(Path.GetDirectoryName(SelectionPath));
        File.WriteAllText(SelectionPath, new JObject
        {
            ["suitePath"] = normalized,
            ["runId"] = runId,
            ["createdUtc"] = DateTime.UtcNow.ToString("O")
        }.ToString(), new UTF8Encoding(false));
        if (File.Exists(LastGameplayReportPath)) File.Delete(LastGameplayReportPath);
    }

    public static string ResolveSuitePath(string fallback = DefaultSuitePath)
    {
        if (!File.Exists(SelectionPath)) return NormalizeSuitePath(fallback);
        JObject selection = JObject.Parse(File.ReadAllText(SelectionPath, Encoding.UTF8));
        // 指针由每轮启动覆盖、结束后清除，不使用本机时钟判定，避免不同测试域的时区误判。
        return NormalizeSuitePath(selection.Value<string>("suitePath"));
    }

    public static void ClearSuite(string runId)
    {
        if (!File.Exists(SelectionPath)) return;
        JObject selection = JObject.Parse(File.ReadAllText(SelectionPath, Encoding.UTF8));
        if (selection.Value<string>("runId") == runId) File.Delete(SelectionPath);
    }

    public static void WriteLastGameplayReport(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LastGameplayReportPath));
        File.WriteAllText(LastGameplayReportPath, path ?? string.Empty, new UTF8Encoding(false));
    }
    #endregion
}
