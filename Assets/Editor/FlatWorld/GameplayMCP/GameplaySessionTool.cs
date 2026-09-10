using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace FlatWorld.GameplayMCP
{
    /// <summary>为 Agent 提供无需视觉导航菜单的存档会话启动入口。</summary>
    [McpForUnityTool(
        "gameplay_session",
        Description = "Manage the FlatWorld gameplay session without visual menu navigation. Actions: status, list_saves, continue_save, create_world, save_exit. continue_save defaults to the most recent save and an isolated Library copy.",
        Group = "core")]
    public static class GameplaySessionTool
    {
        public sealed class Parameters
        {
            [ToolParameter("One of: status, list_saves, continue_save, create_world, save_exit.", Required = false, DefaultValue = "status")]
            public string action { get; set; }

            [ToolParameter("Save name without .bytes. Empty means most recent save.", Required = false)]
            public string saveName { get; set; }

            [ToolParameter("Player profile name. Empty means the first stable profile in the save.", Required = false)]
            public string playerName { get; set; }

            [ToolParameter("Use a Library copy so autonomous testing cannot overwrite the player's real save.", Required = false, DefaultValue = "true")]
            public bool isolated { get; set; }

            [ToolParameter("World entry timeout in real seconds. Clamped below the MCP bridge command timeout.", Required = false, DefaultValue = "20")]
            public float timeoutSeconds { get; set; }

            [ToolParameter("New-world seed text. Empty lets the production world creator generate one.", Required = false)]
            public string seed { get; set; }

            [ToolParameter("New-world planet/world key. Empty derives it from the save name.", Required = false)]
            public string worldName { get; set; }

            [ToolParameter("New-world topology: wrapped or infinite.", Required = false, DefaultValue = "wrapped")]
            public string topology { get; set; }

            [ToolParameter("New-world radius when using wrapped topology.", Required = false, DefaultValue = "1000")]
            public int radius { get; set; }

            [ToolParameter("New-world global terrain noise scale.", Required = false, DefaultValue = "0.01")]
            public float noiseScale { get; set; }
        }

        /// <summary>执行会话查询或启动。</summary>
        public static async Task<object> HandleCommand(JObject parameters)
        {
            string action = parameters?["action"]?.ToString()?.Trim().ToLowerInvariant() ?? "status";
            switch (action)
            {
                case "list_saves":
                    return new SuccessResponse("FlatWorld saves.", new { saves = GameplayMcpRuntime.ListSaves() });
                case "continue_save":
                {
                    string saveName = parameters?["saveName"]?.ToString();
                    string playerName = parameters?["playerName"]?.ToString();
                    bool isolated = !bool.TryParse(parameters?["isolated"]?.ToString(), out bool parsedIsolated) || parsedIsolated;
                    float timeout = float.TryParse(parameters?["timeoutSeconds"]?.ToString(), out float parsedTimeout)
                        ? parsedTimeout
                        : 20f;
                    JObject result = await GameplayMcpRuntime.ContinueSaveAsync(
                        saveName,
                        playerName,
                        isolated,
                        timeout);
                    return new SuccessResponse("FlatWorld gameplay session request completed.", result);
                }
                case "create_world":
                {
                    string saveName = parameters?["saveName"]?.ToString();
                    string playerName = parameters?["playerName"]?.ToString();
                    string seed = parameters?["seed"]?.ToString();
                    string worldName = parameters?["worldName"]?.ToString();
                    string topology = parameters?["topology"]?.ToString();
                    int radius = int.TryParse(parameters?["radius"]?.ToString(), out int parsedRadius)
                        ? parsedRadius
                        : PlanetData.DefaultRadius;
                    float noiseScale = float.TryParse(
                        parameters?["noiseScale"]?.ToString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float parsedNoiseScale)
                        ? parsedNoiseScale
                        : PlanetData.DefaultNoiseScale;
                    float timeout = float.TryParse(parameters?["timeoutSeconds"]?.ToString(), out float parsedTimeout)
                        ? parsedTimeout
                        : 20f;
                    JObject result = await GameplayMcpRuntime.CreateWorldAsync(
                        saveName,
                        playerName,
                        seed,
                        worldName,
                        topology,
                        radius,
                        noiseScale,
                        timeout);
                    return new SuccessResponse("FlatWorld new-world request completed.", result);
                }
                case "save_exit":
                {
                    float timeout = float.TryParse(parameters?["timeoutSeconds"]?.ToString(), out float parsedTimeout)
                        ? parsedTimeout
                        : 20f;
                    JObject result = await GameplayMcpRuntime.SaveAndExitAsync(timeout);
                    return new SuccessResponse("FlatWorld save-and-exit request completed.", result);
                }
                case "status":
                {
                    JObject observation = GameplayMcpRuntime.BuildObservation(4f, 4, false);
                    return new SuccessResponse("FlatWorld gameplay session status.", observation);
                }
                default:
                    return new ErrorResponse("Unknown action. Use status, list_saves, continue_save, create_world, or save_exit.");
            }
        }
    }
}
