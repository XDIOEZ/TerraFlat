using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources.Editor;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace FlatWorld.GameplayMCP
{
    /// <summary>编辑态规则检查真实发现元数据和网关拒绝，不进入世界或执行玩法。</summary>
    internal static class GameplayGatewayRules
    {
        #region 网关静态契约采样
        [InitializeOnLoadMethod]
        private static void Register() => GameplayTestSystemProbe.Register("testing", "gameplay_gateway", Capture);

        private static JObject Capture(JObject _)
        {
            IToolDiscoveryService discovery = MCPServiceLocator.ToolDiscovery;
            var gateway = discovery.GetToolMetadata("gameplay");
            var operations = GameplayGatewayTool.Operations;
            var publicTools = discovery.DiscoverAllTools().Where(tool => tool.Discoverable && tool.Namespace == "FlatWorld.GameplayMCP").ToArray();
            var registered = discovery.GetEnabledTools();
            var states = (JObject)((SuccessResponse)ToolStates.HandleCommand(new JObject())).Data;
            var advertised = (JArray)states["tools"];
            var catalog = (JObject)((SuccessResponse)GameplayGatewayTool.Discover(new JObject())).Data;
            var uiContract = (JObject)((SuccessResponse)GameplayGatewayTool.Discover(new JObject { ["operation"] = "ui" })).Data;
            var uiMetadata = discovery.GetToolMetadata("gameplay_ui");
            var unknown = GameplayGatewayTool.HandleCommand(new JObject { ["operation"] = "read_console" }).GetAwaiter().GetResult();
            var malformed = GameplayGatewayTool.HandleCommand(new JObject { ["operation"] = "ui", ["arguments"] = new JArray() }).GetAwaiter().GetResult();
            CommandRegistry.Initialize();
            return new JObject
            {
                ["publicTools"] = new JArray(publicTools.Select(tool => tool.Name)),
                ["onlyTwoParameters"] = gateway.Parameters.Count == 2 && gateway.Parameters.Any(p => p.Name == "operation" && p.Type == "string") &&
                    gateway.Parameters.Any(p => p.Name == "arguments" && p.Type == "object"),
                ["internalOperationsHidden"] = operations.All(entry => discovery.GetToolMetadata(entry.Tool)?.Discoverable == false) &&
                    !advertised.Any(tool => operations.Any(entry => entry.Tool == tool.Value<string>("name"))) &&
                    !registered.Any(tool => operations.Any(entry => entry.Tool == tool.Name)),
                ["legacyHandlerRegistered"] = CommandRegistry.GetHandler("gameplay_observe") != null,
                ["catalogHasNoContracts"] = catalog["domains"] is JArray && catalog["inputSchema"] == null && catalog["operations"] == null,
                ["schemaUsesLiveMetadata"] = JToken.DeepEquals(uiContract["inputSchema"], GameplayGatewayTool.BuildInputSchema(uiMetadata)),
                ["unknownRejected"] = unknown is ErrorResponse unknownError && unknownError.Code.StartsWith("unknown_operation:"),
                ["malformedRejected"] = malformed is ErrorResponse malformedError && malformedError.Code.StartsWith("invalid_arguments:"),
                ["unknownDomainRejected"] = GameplayGatewayTool.Discover(new JObject { ["domain"] = "unregistered" }) is ErrorResponse,
                ["publicMetadataChars"] = new JArray(advertised.Where(tool => tool.Value<string>("name") == "gameplay")).ToString(Formatting.None).Length,
                ["internalMetadataChars"] = new JArray(operations.Select(entry => JObject.FromObject(discovery.GetToolMetadata(entry.Tool)))).ToString(Formatting.None).Length
            };
        }
        #endregion
    }
}
