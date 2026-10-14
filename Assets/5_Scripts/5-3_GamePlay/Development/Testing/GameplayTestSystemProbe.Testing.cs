using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 测试体系自身规则
    private static void RegisterTestingOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("testing/suite_contract", TestingSuiteContract);
        operations.Add("testing/lua_contract", TestingLuaContract);
        operations.Add("testing/selection", TestingSelection);
    }

    private static string TestingFailure(Action action)
    {
        try { action(); return string.Empty; }
        catch (Exception exception) { return exception.GetType().Name; }
    }

    private static JObject TestingSuiteContract(JObject args)
    {
        string invalid = Text(args, "invalid", "unknown_field");
        var suite = new JObject { ["schemaVersion"] = 1, ["executionMode"] = "rules", ["buildShowroom"] = false,
            ["cases"] = new JArray(new JObject { ["id"] = "first", ["name"] = "第一个", ["luaPath"] = "GameplayTests/Systems/Navigation/rules.lua" }) };
        GameplayTestSuite valid = GameplayTestSuite.Parse(suite.ToString());
        switch (invalid)
        {
            case "unknown_field": suite["typo"] = true; break;
            case "execution_mode": suite["executionMode"] = "typo"; break;
            case "showroom": suite["buildShowroom"] = true; break;
            case "duplicate_id": ((JArray)suite["cases"]).Add(suite["cases"][0].DeepClone()); break;
            case "escape_path": suite["cases"][0]["luaPath"] = "../outside.lua"; break;
            case "reserved_id": suite["cases"][0]["id"] = "CON"; break;
            case "disabled": suite["cases"][0]["enabled"] = false; break;
            default: throw new ArgumentException("未知清单错误夹具。");
        }
        return new JObject { ["validMode"] = valid.ExecutionMode, ["validCount"] = valid.Cases.Count,
            ["invalidRejected"] = !string.IsNullOrEmpty(TestingFailure(() => GameplayTestSuite.Parse(suite.ToString()))) };
    }

    private static JObject TestingLuaContract(JObject args)
    {
        string mode = Text(args, "mode", "valid");
        string source = mode switch
        {
            "valid" => "local args={penalty=1}; test.system('navigation','weighted_path',args,function(d) assert(d.found and d.totalCost==20) end); args.penalty=1000",
            "no_assert" => "test.system('navigation','corner',{},function(d) return '没有断言' end)",
            "failed_assert" => "test.system('navigation','corner',{},function(d) assert(false,'必须失败') end)",
            "callback_enqueue" => "test.system('navigation','corner',{},function(d) test.log('不能追加'); assert(true) end)",
            "no_cs" => "assert(CS ~= nil)",
            "cycle" => "local t={}; t.self=t; test.system('navigation','corner',t,function(d) assert(true) end)",
            "unknown" => "test.system('not_registered','unknown',{},function(d) assert(true) end)",
            "log_only" => "test.log('不能冒充规则')",
            "live_in_rules" => "test.wait(0.1)",
            _ => throw new ArgumentException("未知 Lua 保护夹具。")
        };
        int failed = 0, samples = 0, commands = 0;
        using var runner = new GameplayTestLuaRunner(null, _ => false, (command, label, success, message) =>
        { if (!success) failed++; if (command == "system_sample") samples++; if (command == "system") commands++; });
        IEnumerator flow = runner.RunRules(source, "@sandbox-regression.lua");
        int moves = 0;
        try { while (flow.MoveNext()) if (++moves > 32) throw new InvalidOperationException("规则自检超过同步步数限制。"); }
        finally { (flow as IDisposable)?.Dispose(); }
        return new JObject { ["succeeded"] = runner.Succeeded, ["failed"] = failed, ["samples"] = samples, ["commands"] = commands };
    }

    private static JObject TestingSelection(JObject args)
    {
        GameplayTestSuite Parse() => GameplayTestSuite.Parse("{\"schemaVersion\":1,\"cases\":[" +
            "{\"id\":\"first\",\"name\":\"一\",\"luaPath\":\"GameplayTests/first.lua\"}," +
            "{\"id\":\"second\",\"name\":\"二\",\"luaPath\":\"GameplayTests/second.lua\"}]}");
        GameplayTestSuite suite = Parse();
        GameplayTestSelection.FilterCases(suite, new[] { "SECOND" });
        return new JObject { ["firstEnabled"] = suite.Cases[0].Enabled, ["secondEnabled"] = suite.Cases[1].Enabled,
            ["unknownRejected"] = !string.IsNullOrEmpty(TestingFailure(() => GameplayTestSelection.FilterCases(Parse(), new[] { "missing" }))),
            ["emptyRejected"] = !string.IsNullOrEmpty(TestingFailure(() => GameplayTestSelection.FilterCases(Parse(), Array.Empty<string>()))) };
    }
    #endregion
}
