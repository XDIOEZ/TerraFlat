using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>测试流水线通过 Library 中的一次性清单指针驱动场景，不修改正式测试场景资源。</summary>
public static class GameplayTestSelection
{
    #region 清单与报告地址
    // 默认只执行快速规则，真实世界链路通过对应场景清单单独选择。
    public const string DefaultSuitePath = "GameplayTests/all-systems-rules.json";

    public static string RootDirectory => Application.isEditor
        ? Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "FlatWorldGameplayTests")
        : Path.Combine(Application.persistentDataPath, "FlatWorldGameplayTests");
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
    public static void SelectSuite(string path, string runId, IReadOnlyCollection<string> selectedCaseIds = null)
    {
        string normalized = NormalizeSuitePath(path);
        ValidateRunId(runId);
        Directory.CreateDirectory(Path.GetDirectoryName(SelectionPath));
        var selection = new JObject
        {
            ["suitePath"] = normalized,
            ["runId"] = runId,
            ["createdUtc"] = DateTime.UtcNow.ToString("O")
        };
        if (selectedCaseIds != null)
            selection["selectedCaseIds"] = new JArray(selectedCaseIds);
        File.WriteAllText(SelectionPath, selection.ToString(), new UTF8Encoding(false));
        if (File.Exists(LastGameplayReportPath)) File.Delete(LastGameplayReportPath);
    }

    public static string ResolveSuitePath(string fallback = DefaultSuitePath)
    {
        if (!File.Exists(SelectionPath)) return NormalizeSuitePath(fallback);
        JObject selection = JObject.Parse(File.ReadAllText(SelectionPath, Encoding.UTF8));
        // 指针由每轮启动覆盖、结束后清除，不使用本机时钟判定，避免不同测试域的时区误判。
        return NormalizeSuitePath(selection.Value<string>("suitePath"));
    }

    public static string ResolveRunId(string fallback)
    {
        string runId = File.Exists(SelectionPath)
            ? JObject.Parse(File.ReadAllText(SelectionPath, Encoding.UTF8)).Value<string>("runId")
            : fallback;
        ValidateRunId(runId);
        return runId;
    }

    public static void ValidateRunId(string runId)
    {
        if (string.IsNullOrEmpty(runId) || !Regex.IsMatch(runId, "\\A[a-zA-Z0-9_-]{1,80}\\z") ||
            Regex.IsMatch(runId, "\\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\\z", RegexOptions.IgnoreCase))
            throw new ArgumentException("无效测试运行编号。", nameof(runId));
    }

    /// <summary>仅将用户选中的有效用例标记为启用；原始清单和 Lua 文件都保持不变。</summary>
    public static void ApplySelectedCases(GameplayTestSuite suite, string suitePath)
    {
        if (suite == null) throw new ArgumentNullException(nameof(suite));
        if (!File.Exists(SelectionPath)) return;
        JObject selection = JObject.Parse(File.ReadAllText(SelectionPath, Encoding.UTF8));
        if (!string.Equals(NormalizeSuitePath(selection.Value<string>("suitePath")),
                NormalizeSuitePath(suitePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("本轮测试清单与用例选择不一致。");
        if (selection["selectedCaseIds"] == null) return;
        if (selection["selectedCaseIds"] is not JArray ids)
            throw new InvalidDataException("选中用例列表必须是数组。");
        var selected = new List<string>(ids.Count);
        foreach (JToken id in ids)
        {
            if (id.Type != JTokenType.String)
                throw new InvalidDataException("选中的用例 ID 必须是字符串。");
            selected.Add(id.Value<string>());
        }
        FilterCases(suite, selected);
    }

    /// <summary>测试清单子集验证使用同一入口，防止把禁用用例误当成执行成功。</summary>
    public static void FilterCases(GameplayTestSuite suite, IEnumerable<string> selectedCaseIds)
    {
        if (suite == null || selectedCaseIds == null)
            throw new ArgumentNullException(suite == null ? nameof(suite) : nameof(selectedCaseIds));
        var selected = new HashSet<string>(selectedCaseIds, StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0) throw new InvalidDataException("请至少勾选一个测试案例。");
        var cases = new Dictionary<string, GameplayTestCase>(StringComparer.OrdinalIgnoreCase);
        foreach (GameplayTestCase entry in suite.Cases) cases.Add(entry.Id, entry);
        foreach (string id in selected)
        {
            if (!cases.TryGetValue(id, out GameplayTestCase entry) || !entry.Enabled)
                throw new InvalidDataException("选中的用例不存在或被清单禁用：" + id);
        }
        // 保留清单顺序，递归补齐被勾选用例的前置案例，保证同一次启动执行完成。
        var pending = new Queue<string>(selected);
        while (pending.Count > 0)
        {
            GameplayTestCase entry = cases[pending.Dequeue()];
            foreach (string prerequisite in (IEnumerable<string>)entry.DependsOn ?? Array.Empty<string>())
            {
                if (!cases.TryGetValue(prerequisite, out GameplayTestCase required) || !required.Enabled)
                    throw new InvalidDataException($"测试用例 {entry.Id} 缺少可执行的前置案例：{prerequisite}");
                if (selected.Add(required.Id)) pending.Enqueue(required.Id);
            }
        }
        foreach (GameplayTestCase entry in suite.Cases)
            entry.Enabled = entry.Enabled && selected.Contains(entry.Id);
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
