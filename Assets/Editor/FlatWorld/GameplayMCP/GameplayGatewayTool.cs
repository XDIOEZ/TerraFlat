using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace FlatWorld.GameplayMCP
{
    /// <summary>短入口只公开通用路由，详细契约在游玩时按操作读取。</summary>
    [McpForUnityTool("gameplay", Description = "FlatWorld gameplay. Omit operation for a short directory; discover reads one domain/operation contract. Then call that operation with arguments.", Group = "core")]
    public static class GameplayGatewayTool
    {
        #region 有界入口与操作白名单
        public sealed class Parameters
        {
            [ToolParameter("Operation from the directory, or discover.", Required = false)]
            public string operation { get; set; }

            [ToolParameter("Operation arguments. For discover: domain or operation.", Required = false)]
            public object arguments { get; set; }
        }

        internal sealed class Operation
        {
            public readonly string Name, Domain, Tool;
            public Operation(string name, string domain, string tool) { Name = name; Domain = domain; Tool = tool; }
        }

        // 仅转发明确登记的玩法命令，不根据外部输入查找任意编辑器工具或类型。
        internal static readonly Operation[] Operations =
        {
            new("session", "session", "gameplay_session"),
            new("control", "session", "gameplay_control"),
            new("observe", "observation", "gameplay_observe"),
            new("query", "observation", "gameplay_query"),
            new("act", "action", "gameplay_act"),
            new("ui", "ui", "gameplay_ui"),
            new("gm", "gm", "gameplay_gm"),
            new("aiecs", "diagnostics", "gameplay_aiecs_debug"),
            new("chunks", "diagnostics", "gameplay_chunk_render_debug"),
            new("hydrology", "diagnostics", "gameplay_hydrology_debug"),
            new("spawner", "diagnostics", "gameplay_spawner_debug"),
            new("tests", "testing", "gameplay_test_pipeline"),
            new("capabilities", "protocol", "gameplay_capabilities")
        };

        public static async Task<object> HandleCommand(JObject parameters)
        {
            if (parameters?["arguments"] != null && parameters["arguments"].Type != JTokenType.Null &&
                parameters["arguments"] is not JObject)
                return new ErrorResponse("invalid_arguments: arguments 必须是对象。");
            string operation = parameters?["operation"]?.ToString()?.Trim().ToLowerInvariant();
            JObject arguments = parameters?["arguments"] is JObject supplied ? (JObject)supplied.DeepClone() : new JObject();
            if (string.IsNullOrEmpty(operation) || operation == "discover")
                return Discover(arguments);
            Operation selected = FindOperation(operation);
            if (selected == null)
                return new ErrorResponse("unknown_operation: 请先调用 gameplay(operation=discover) 查询操作目录。");
            if (!MCPServiceLocator.ToolDiscovery.IsToolEnabled(selected.Tool))
                return new ErrorResponse("operation_disabled: 此玩法操作已在 MCP 工具设置中禁用。");
            if (arguments["output"] == null) arguments["output"] = "compact";
            CommandRegistry.Initialize();
            return await CommandRegistry.InvokeCommandAsync(selected.Tool, arguments);
        }

        private static Operation FindOperation(string name)
            => Operations.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal) ||
                                                   string.Equals(entry.Tool, name, StringComparison.Ordinal));
        #endregion

        #region 按需契约
        internal static object Discover(JObject arguments)
        {
            string requested = arguments?["operation"]?.ToString()?.Trim().ToLowerInvariant();
            string domain = arguments?["domain"]?.ToString()?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(requested))
            {
                Operation selected = FindOperation(requested);
                return selected == null
                    ? new ErrorResponse("unknown_operation: 未登记的玩法操作。")
                    : Describe(selected);
            }
            if (!string.IsNullOrEmpty(domain))
            {
                Operation[] selected = Operations.Where(entry => entry.Domain == domain).ToArray();
                if (selected.Length == 0) return new ErrorResponse("unknown_domain: 未登记的玩法域。");
                if (selected.Length == 1) return Describe(selected[0]);
                return new SuccessResponse("FlatWorld gameplay domain.", new
                {
                    domain, operations = selected.Select(entry => entry.Name).ToArray(),
                    next = "discover with arguments.operation to read one contract"
                });
            }
            return new SuccessResponse("FlatWorld gameplay directory.", new
            {
                domains = Operations.Select(entry => entry.Domain).Distinct().ToArray(),
                next = new { operation = "discover", arguments = new { domain = "session" } }
            });
        }

        private static object Describe(Operation operation)
        {
            ToolMetadata metadata = MCPServiceLocator.ToolDiscovery.GetToolMetadata(operation.Tool);
            if (metadata == null) return new ErrorResponse("operation_unavailable: 玩法接口元数据尚未就绪。");
            var contract = new JObject
            {
                ["operation"] = operation.Name, ["domain"] = operation.Domain,
                ["description"] = metadata.Description,
                ["enabled"] = MCPServiceLocator.ToolDiscovery.IsToolEnabled(operation.Tool),
                ["inputSchema"] = BuildInputSchema(metadata), ["defaultOutput"] = "compact"
            };
            if (operation.Name == "act") contract["actions"] = GameplayMcpActionRegistry.BuildCapabilityObject();
            if (operation.Name == "gm") contract["commands"] = GameplayMcpGmCommandRegistry.BuildCapabilityObject();
            return new SuccessResponse("FlatWorld gameplay operation contract.", contract);
        }

        internal static JObject BuildInputSchema(ToolMetadata metadata)
        {
            var properties = new JObject();
            var required = new JArray();
            foreach (ParameterMetadata parameter in metadata.Parameters ?? new List<ParameterMetadata>())
            {
                var property = new JObject { ["type"] = parameter.Type, ["description"] = parameter.Description };
                if (parameter.Type == "array") property["items"] = new JObject();
                if (!string.IsNullOrEmpty(parameter.DefaultValue))
                {
                    try { property["default"] = JToken.Parse(parameter.DefaultValue); }
                    catch (Newtonsoft.Json.JsonReaderException) { property["default"] = parameter.DefaultValue; }
                }
                properties[parameter.Name] = property;
                if (parameter.Required) required.Add(parameter.Name);
            }
            var schema = new JObject { ["type"] = "object", ["properties"] = properties };
            if (required.Count > 0) schema["required"] = required;
            return schema;
        }
        #endregion
    }
}
