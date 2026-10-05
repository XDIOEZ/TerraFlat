using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace FlatWorld.GameplayMCP
{
    /// <summary>管理 AI 与真人输入之间的唯一玩家控制租约。</summary>
    [McpForUnityTool(
        "gameplay_control",
        Description = "Acquire, release, or inspect the exclusive FlatWorld gameplay control lease used by the AI agent.",
        Group = "core")]
    public static class GameplayControlTool
    {
        #region 统一输入输出

        /// <summary>按需返回数据，错误与分页状态保持完整。</summary>
        public static object HandleCommand(JObject parameters)
        {
            return GameplayMcpOutput.Invoke("gameplay_control", parameters, ExecuteCommand, false);
        }

        #endregion

        public sealed class Parameters : GameplayMcpOutputParameters
        {
            [ToolParameter("One of: acquire, release, status.", Required = false, DefaultValue = "status")]
            public string action { get; set; }
        }

        /// <summary>获取、释放或查询控制租约。</summary>
        private static object ExecuteCommand(JObject parameters)
        {
            string action = parameters?["action"]?.ToString()?.Trim().ToLowerInvariant() ?? "status";
            if (action == "release")
            {
                if (!GameplayMcpRuntime.TryReleaseControl(out string releaseError))
                    return new ErrorResponse(releaseError);
                return new SuccessResponse("GamePlayMCP control released.", new { owned = false });
            }

            if (!GameplayMcpRuntime.TryGetPlayerContext(
                    out _,
                    out Mod_GameController controller,
                    out _,
                    out string error))
            {
                return new ErrorResponse(error);
            }

            if (action == "acquire")
            {
                if (!GameplayMcpRuntime.TryEnsureControl(out _, out controller, out _, out error))
                    return new ErrorResponse(error);
            }
            else if (action != "status")
            {
                return new ErrorResponse("Unknown action. Use acquire, release, or status.");
            }

            return new SuccessResponse("GamePlayMCP control status.", new
            {
                active = controller.HasExternalGameplayControl,
                owned = GameplayMcpRuntime.OwnsControl(controller),
                owner = controller.ExternalGameplayControlOwnerName
            });
        }
    }
}
