using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>测试清单只定义用例顺序和失败策略，各个 Lua 负责自己的测试流程。</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class GameplayTestSuite
{
    #region 清单数据
    public const int MaximumCases = 512;
    [JsonProperty("schemaVersion", Required = Required.Always)] public int SchemaVersion = 1;
    [JsonProperty("continueAfterFailure")] public bool ContinueAfterFailure = true;
    [JsonProperty("buildShowroom")] public bool BuildShowroom = true;
    [JsonProperty("executionMode")] public string ExecutionMode = "gameplay";
    [JsonProperty("cases", Required = Required.Always)] public List<GameplayTestCase> Cases = new();
    #endregion

    #region 严格解析与校验
    public static GameplayTestSuite Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("测试清单 JSON 为空。");
        using var reader = new JsonTextReader(new StringReader(json))
            { DateParseHandling = DateParseHandling.None, MaxDepth = 32 };
        JObject root = JObject.Load(reader, new JsonLoadSettings
            { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read()) throw new InvalidDataException("测试清单 JSON 包含多余内容。");
        ValidateMembers(root, "测试清单", "schemaVersion", "continueAfterFailure", "buildShowroom", "executionMode", "cases");
        RequireType(root, "schemaVersion", JTokenType.Integer);
        RequireType(root, "continueAfterFailure", JTokenType.Boolean, false);
        RequireType(root, "buildShowroom", JTokenType.Boolean, false);
        RequireType(root, "executionMode", JTokenType.String, false);
        RequireType(root, "cases", JTokenType.Array);
        if (((JArray)root["cases"]).Count < 1 || ((JArray)root["cases"]).Count > MaximumCases)
            throw new InvalidDataException($"测试清单必须包含 1 至 {MaximumCases} 个用例。");
        foreach (JToken token in (JArray)root["cases"])
        {
            if (token is not JObject entry) throw new InvalidDataException("测试 cases 的每项必须是对象。");
            ValidateMembers(entry, "测试用例", "id", "name", "luaPath", "enabled");
            RequireType(entry, "id", JTokenType.String);
            RequireType(entry, "name", JTokenType.String);
            RequireType(entry, "luaPath", JTokenType.String);
            RequireType(entry, "enabled", JTokenType.Boolean, false);
        }

        GameplayTestSuite suite = root.ToObject<GameplayTestSuite>(JsonSerializer.Create(
            new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }));
        if (suite == null || suite.SchemaVersion != 1)
            throw new InvalidDataException($"测试清单 schemaVersion 必须为 1：{suite?.SchemaVersion}");
        if (suite.ExecutionMode is not ("rules" or "gameplay"))
            throw new InvalidDataException("executionMode 必须是 rules 或 gameplay。");
        if (suite.ExecutionMode == "rules" && root.Value<bool?>("buildShowroom") == true)
            throw new InvalidDataException("纯规则清单不能启用物品陈列。");
        if (suite.ExecutionMode == "rules") suite.BuildShowroom = false;
        if (suite.Cases == null || suite.Cases.Count < 1 || suite.Cases.Count > MaximumCases)
            throw new InvalidDataException($"测试清单必须包含 1 至 {MaximumCases} 个用例。");

        // 用例 ID 会成为报告文件名，忽略大小写检查重复，防止 Windows 下覆盖报告。
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasEnabledCase = false;
        foreach (GameplayTestCase testCase in suite.Cases)
        {
            if (testCase == null || string.IsNullOrEmpty(testCase.Id) ||
                !Regex.IsMatch(testCase.Id, "\\A[a-zA-Z0-9_-]{1,64}\\z"))
                throw new InvalidDataException("测试用例 id 必须为 1 至 64 个英文字母、数字、下划线或连字符。");
            if (!ids.Add(testCase.Id)) throw new InvalidDataException($"测试用例 id 重复：{testCase.Id}");
            if (Regex.IsMatch(testCase.Id, "\\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\\z", RegexOptions.IgnoreCase))
                throw new InvalidDataException($"测试用例 id 不能使用 Windows 保留文件名：{testCase.Id}");
            if (string.IsNullOrWhiteSpace(testCase.Name))
                throw new InvalidDataException($"测试用例 {testCase.Id} 缺少 name。");
            testCase.LuaPath = ValidateLuaPath(testCase.LuaPath, testCase.Id);
            hasEnabledCase |= testCase.Enabled;
        }
        if (!hasEnabledCase) throw new InvalidDataException("测试清单至少要启用一个用例。");
        return suite;
    }

    private static void ValidateMembers(JObject value, string label, params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (JProperty property in value.Properties())
            if (!names.Contains(property.Name))
                throw new InvalidDataException($"{label} 包含未知字段：{property.Name}");
    }

    private static void RequireType(JObject value, string name, JTokenType type, bool required = true)
    {
        JToken token = value[name];
        if (token == null && !required) return;
        if (token == null || token.Type != type)
            throw new InvalidDataException($"测试清单字段 {name} 必须是 {type}。");
    }

    private static string ValidateLuaPath(string path, string id)
    {
        if (string.IsNullOrWhiteSpace(path) || path != path.Trim())
            throw new InvalidDataException($"测试用例 {id} 缺少有效的 luaPath。");
        string normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            !normalized.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
            normalized.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) >= 0)
            throw new InvalidDataException($"测试用例 {id} 的 luaPath 必须是 StreamingAssets 下的相对 .lua 路径。");
        foreach (char character in normalized)
            if (char.IsControl(character))
                throw new InvalidDataException($"测试用例 {id} 的 luaPath 不能包含控制字符。");
        foreach (string segment in normalized.Split('/'))
            if (segment.Length == 0 || segment == "." || segment == ".." ||
                segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal))
                throw new InvalidDataException($"测试用例 {id} 的 luaPath 不能包含空目录、点目录或上级目录。");
        return normalized;
    }
    #endregion
}

[Serializable]
[JsonObject(MemberSerialization.OptIn)]
public sealed class GameplayTestCase
{
    #region 用例数据
    [JsonProperty("id", Required = Required.Always)] public string Id;
    [JsonProperty("name", Required = Required.Always)] public string Name;
    [JsonProperty("luaPath", Required = Required.Always)] public string LuaPath;
    [JsonProperty("enabled")] public bool Enabled = true;
    #endregion
}
