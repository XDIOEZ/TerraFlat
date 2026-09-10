using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace FlatWorld.GameplayMCP
{
    /// <summary>向 AI 提供可扩展的统一玩法动作入口。</summary>
    [McpForUnityTool(
        "gameplay_act",
        Description = "Perform one live FlatWorld gameplay action through production APIs. Call gameplay_capabilities for the current action set. Unknown actions return capability_gap so the protocol can evolve.",
        Group = "core")]
    public static class GameplayActTool
    {
        public sealed class Parameters
        {
            [ToolParameter("Action name. Call gameplay_capabilities for the current registered action set.")]
            public string action { get; set; }

            [ToolParameter("World X coordinate or movement X direction.", Required = false)]
            public float x { get; set; }

            [ToolParameter("World Y coordinate or movement Y direction.", Required = false)]
            public float y { get; set; }

            [ToolParameter("Action duration, press_key hold duration, or move_to timeout in real seconds.", Required = false)]
            public float seconds { get; set; }

            [ToolParameter("Arrival tolerance for move_to.", Required = false)]
            public float tolerance { get; set; }

            [ToolParameter("Use running movement when supported.", Required = false)]
            public bool run { get; set; }

            [ToolParameter("Runtime Item Guid returned by gameplay_observe.", Required = false)]
            public int targetGuid { get; set; }

            [ToolParameter("Zero-based hotbar slot index.", Required = false)]
            public int index { get; set; }

            [ToolParameter("Input System action name for press_key, for example B, E, H, P, ESC or OpenChat. Resolves the player's current effective keyboard binding and therefore follows rebinding.", Required = false)]
            public string inputAction { get; set; }

            [ToolParameter("Physical keyboard key/control for press_key, for example b, e, escape, enter or 1. Use inputAction instead when the gameplay shortcut is rebindable.", Required = false)]
            public string key { get; set; }
        }

        /// <summary>执行一次可等待的正式玩法动作。</summary>
        public static async Task<object> HandleCommand(JObject parameters)
        {
            JObject result = await GameplayMcpRuntime.ExecuteActionAsync(parameters ?? new JObject());
            return new SuccessResponse("FlatWorld gameplay action completed.", result);
        }
    }
}
