using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace FlatWorld.GameplayMCP
{
    /// <summary>公开协议能力和扩展位置，让 Agent 遇到能力缺口时可以自助扩展。</summary>
    [McpForUnityTool(
        "gameplay_capabilities",
        Description = "Describe the current GamePlayMCP protocol and where to extend it when autonomous gameplay hits a capability gap.",
        Group = "core")]
    public static class GameplayCapabilitiesTool
    {
        /// <summary>返回协议版本、动作列表与扩展规则。</summary>
        public static object HandleCommand(JObject parameters)
        {
            return new SuccessResponse("FlatWorld GamePlayMCP capabilities.", new
            {
                protocol = GameplayMcpRuntime.ProtocolVersion,
                sessionActions = new[] { "status", "list_saves", "continue_save", "create_world", "save_exit" },
                controlActions = new[] { "status", "acquire", "release" },
                observationTools = new[] { "gameplay_observe", "gameplay_query" },
                uiTool = "gameplay_ui",
                uiActions = new[] { "tree", "click" },
                gameplayActions = GameplayMcpActionRegistry.ActionNames,
                gameplayActionDescriptions = GameplayMcpActionRegistry.BuildCapabilityObject(),
                extensionPath = GameplayMcpRuntime.ExtensionPath,
                rule = "Use gameplay_ui tree/click for visible UI. When gameplay_act returns capability_gap, add an IGameplayMcpAction with GameplayMcpActionAttribute backed by production APIs. Do not use arbitrary reflection or direct business callback invocation."
            });
        }
    }
}
