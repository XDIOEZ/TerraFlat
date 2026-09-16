using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace FlatWorld.GameplayMCP
{
    /// <summary>公开协议能力、GM 命令清单和扩展位置，让 Agent 能直接选择可用能力。</summary>
    [McpForUnityTool(
        "gameplay_capabilities",
        Description = "Describe the current GamePlayMCP protocol, the explicitly supported GM commands, and where to extend it when autonomous gameplay hits a capability gap.",
        Group = "core")]
    public static class GameplayCapabilitiesTool
    {
        /// <summary>返回协议版本、动作列表、GM 命令与扩展规则。</summary>
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
                gmTool = "gameplay_gm",
                gmCommands = GameplayMcpGmCommandRegistry.BuildCapabilityObject(),
                gmUsage = "Call gameplay_gm with command=enable_invincibility to enable administrator invincibility directly; it enables administrator mode if needed and does not require opening the GM UI.",
                gameplayActions = GameplayMcpActionRegistry.ActionNames,
                gameplayActionDescriptions = GameplayMcpActionRegistry.BuildCapabilityObject(),
                extensionPath = GameplayMcpRuntime.ExtensionPath,
                rule = "Use gameplay_ui tree/click for visible UI. Use gameplay_gm only with its listed commands. When gameplay_act returns capability_gap, add an IGameplayMcpAction with GameplayMcpActionAttribute backed by production APIs. Do not use arbitrary reflection, arbitrary console commands, or direct business callback invocation."
            });
        }
    }
}
