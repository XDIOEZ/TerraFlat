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
        public sealed class Parameters
        {
            [ToolParameter("Nearby entity radius in world units.", Required = false, DefaultValue = "10")]
            public float radius { get; set; }

            [ToolParameter("Maximum nearby entities returned.", Required = false, DefaultValue = "24")]
            public int maxEntities { get; set; }

            [ToolParameter("Include aggregated inventory summary.", Required = false, DefaultValue = "true")]
            public bool includeInventory { get; set; }
        }

        /// <summary>返回实时玩家、附近实体与资源状态。</summary>
        public static object HandleCommand(JObject parameters)
        {
            float radius = ReadFloat(parameters, "radius", 10f);
            int maxEntities = ReadInt(parameters, "maxEntities", 24);
            bool includeInventory = ReadBool(parameters, "includeInventory", true);
            return new SuccessResponse(
                "FlatWorld gameplay observation.",
                GameplayMcpRuntime.BuildObservation(radius, maxEntities, includeInventory));
        }

        /// <summary>读取浮点工具参数。</summary>
        private static float ReadFloat(JObject parameters, string key, float fallback)
        {
            return float.TryParse(parameters?[key]?.ToString(), out float value) ? value : fallback;
        }

        /// <summary>读取整数工具参数。</summary>
        private static int ReadInt(JObject parameters, string key, int fallback)
        {
            return int.TryParse(parameters?[key]?.ToString(), out int value) ? value : fallback;
        }

        /// <summary>读取布尔工具参数。</summary>
        private static bool ReadBool(JObject parameters, string key, bool fallback)
        {
            return bool.TryParse(parameters?[key]?.ToString(), out bool value) ? value : fallback;
        }
    }
}
