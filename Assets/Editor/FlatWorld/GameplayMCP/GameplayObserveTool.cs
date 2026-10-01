using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace FlatWorld.GameplayMCP
{
    /// <summary>向 AI 暴露高信息密度、低 Token 的游戏世界观察工具。</summary>
    [McpForUnityTool(
        "gameplay_observe",
        Description = "Observe the live FlatWorld player and nearby world as compact structured data. Prefer this over screenshots for gameplay iteration.",
        Group = "core")]
    public static class GameplayObserveTool
    {
        #region 统一输入输出

        /// <summary>按需返回数据，错误与分页状态保持完整。</summary>
        public static object HandleCommand(JObject parameters)
        {
            return GameplayMcpOutput.Finish(ExecuteCommand(parameters), parameters, false);
        }

        #endregion

        #region 观察参数与响应

        public sealed class Parameters : GameplayMcpOutputParameters
        {
            [ToolParameter("Observation profile: full or compact. Defaults to output mode. Explicit include flags override profile defaults.", Required = false)]
            public string profile { get; set; }

            [ToolParameter("Include the 3x3 terrain grid. Defaults to false in compact, true in full.", Required = false)]
            public bool? includeTerrain { get; set; }

            [ToolParameter("Include nearby ECS drops. Defaults to false in compact, true in full.", Required = false)]
            public bool? includeDrops { get; set; }

            [ToolParameter("Include nearby runtime entities. Defaults to false in compact, true in full.", Required = false)]
            public bool? includeNearby { get; set; }

            [ToolParameter("Nearby entity radius in world units.", Required = false, DefaultValue = "10")]
            public float radius { get; set; }

            [ToolParameter("Maximum nearby entities returned. Defaults to 6 in compact, 24 in full.", Required = false)]
            public int maxEntities { get; set; }

            [ToolParameter("Include aggregated inventory summary. Defaults to false in compact, true in full.", Required = false)]
            public bool? includeInventory { get; set; }
        }

        /// <summary>返回实时玩家、附近实体与资源状态。</summary>
        private static object ExecuteCommand(JObject parameters)
        {
            return new SuccessResponse(
                "FlatWorld gameplay observation.",
                GameplayMcpRuntime.BuildObservation(parameters));
        }

        #endregion
    }
}
