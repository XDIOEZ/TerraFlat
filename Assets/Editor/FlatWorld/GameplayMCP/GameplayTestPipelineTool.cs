using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System;

namespace FlatWorld.GameplayMCP
{
    /// <summary>AI 只发一次启动命令，再读取整体结果，不按逐帧玩法 MCP 反复操作。</summary>
    [McpForUnityTool("gameplay_test_pipeline",
        Description = "Run targeted FlatWorld Lua rules or gameplay suites. Set playModeOnly=true to run only PlayMode Lua without EditMode preflight. Actions: run, status, report, catalog. systemIds selects fast rules; suitePath selects a suite. Never interrupts active PlayMode.",
        Group = "core")]
    public static class GameplayTestPipelineTool
    {
        #region 测试流水线命令
        public sealed class Parameters
        {
            [ToolParameter("run | status | report | catalog", Required = true)]
            public string action { get; set; }

            [ToolParameter("StreamingAssets-relative suite path; defaults to all-systems-gameplay when playModeOnly=true, otherwise all-systems-rules.", Required = false)]
            public string suitePath { get; set; }

            [ToolParameter("Optional list of selected Lua case IDs; all enabled cases run when omitted.", Required = false)]
            public string[] caseIds { get; set; }

            [ToolParameter("Optional registered system IDs, such as ai, mechanical, terrain. Selects fast rules; cannot combine with suitePath/caseIds.", Required = false)]
            public string[] systemIds { get; set; }

            [ToolParameter("Run gameplay suites in PlayMode only, skipping EditMode tests. Not valid with systemIds/rules suites.", Required = false)]
            public bool playModeOnly { get; set; }
        }

        public static object HandleCommand(JObject parameters)
        {
            string action = parameters?.Value<string>("action")?.Trim().ToLowerInvariant();
            List<string> selectedIds = null;
            if (parameters?["caseIds"] is JArray ids)
            {
                selectedIds = new List<string>(ids.Count);
                foreach (JToken id in ids)
                    selectedIds.Add(id.Type == JTokenType.String ? id.Value<string>() : string.Empty);
            }
            JObject result;
            try
            {
                if (parameters?["caseIds"] != null && parameters["caseIds"] is not JArray)
                    throw new ArgumentException("caseIds 必须是数组。");
                if (parameters?["playModeOnly"] != null && parameters["playModeOnly"].Type != JTokenType.Boolean)
                    throw new ArgumentException("playModeOnly 必须是布尔值。");
                bool playModeOnly = parameters?.Value<bool?>("playModeOnly") == true;
                if (playModeOnly && action != "run")
                    throw new ArgumentException("playModeOnly 仅用于 run。");
                string suitePath = parameters?.Value<string>("suitePath");
                if (parameters?["systemIds"] != null)
                {
                    if (action != "run" || playModeOnly || parameters["systemIds"] is not JArray systemIds || selectedIds != null || suitePath != null)
                        throw new ArgumentException("systemIds 仅用于 run，且不能与 suitePath/caseIds 混用。");
                    var selectedSystems = new List<string>();
                    foreach (JToken systemId in systemIds)
                    {
                        if (systemId.Type != JTokenType.String) throw new ArgumentException("systemIds 必须包含文本 ID。");
                        selectedSystems.Add(systemId.Value<string>());
                    }
                    selectedIds = new List<string>(GameplayTestSystemCatalog.SelectRuleCases(selectedSystems));
                    suitePath = GameplayTestSystemCatalog.RulesSuitePath;
                }
                result = action switch
                {
                    "run" => GameplayTestPipelineCoordinator.Start(suitePath, selectedIds, playModeOnly),
                    "status" => GameplayTestPipelineCoordinator.Status(false),
                    "report" => GameplayTestPipelineCoordinator.Status(true),
                    "catalog" => new JObject { ["ok"] = true, ["catalog"] = GameplayTestSystemCatalog.Read(),
                        ["ruleOperations"] = new JArray(GameplayTestSystemProbe.RegisteredOperations) },
                    _ => new JObject { ["ok"] = false, ["message"] = "action 必须为 run、status、report 或 catalog。" }
                };
            }
            catch (Exception exception) { result = new JObject { ["ok"] = false, ["message"] = exception.Message }; }
            return new SuccessResponse("FlatWorld test pipeline.", result);
        }
        #endregion
    }
}
