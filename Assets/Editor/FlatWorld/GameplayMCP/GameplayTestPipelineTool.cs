using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace FlatWorld.GameplayMCP
{
    /// <summary>AI 只发一次启动命令，再读取整体结果，不按逐帧玩法 MCP 反复操作。</summary>
    [McpForUnityTool("gameplay_test_pipeline",
        Description = "Run FlatWorld EditMode tests first, then one PlayMode gameplay Lua suite only if all EditMode tests passed. Actions: run, status, report; optional suitePath such as GameplayTests/quick-suite.json. Does not interrupt an active PlayMode.",
        Group = "core")]
    public static class GameplayTestPipelineTool
    {
        #region 测试流水线命令
        public sealed class Parameters
        {
            [ToolParameter("run | status | report", Required = true)]
            public string action { get; set; }

            [ToolParameter("StreamingAssets-relative suite path; default GameplayTests/quick-suite.json.", Required = false)]
            public string suitePath { get; set; }

            [ToolParameter("Optional list of selected Lua case IDs; all enabled cases run when omitted.", Required = false)]
            public string[] caseIds { get; set; }
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
            JObject result = action switch
            {
                "run" => GameplayTestPipelineCoordinator.Start(parameters?.Value<string>("suitePath"), selectedIds),
                "status" => GameplayTestPipelineCoordinator.Status(false),
                "report" => GameplayTestPipelineCoordinator.Status(true),
                _ => new JObject { ["ok"] = false, ["message"] = "action 必须为 run、status 或 report。" }
            };
            return new SuccessResponse("FlatWorld test pipeline.", result);
        }
        #endregion
    }
}
