using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// 向自主游玩 Agent 暴露当前可见 UI 的语义树，并允许按树中运行时 ID 执行真实 UI 点击。
    /// </summary>
    [McpForUnityTool(
        "gameplay_ui",
        Description = "Inspect and click live FlatWorld UI without screenshots. action=tree returns a paged semantic UI tree for active canvases; action=click accepts a targetId from that tree and performs a real EventSystem left click only when the target is currently raycastable and interactable.",
        Group = "core")]
    public static class GameplayUiTool
    {
        public sealed class Parameters
        {
            [ToolParameter("UI action: tree or click.", Required = false, DefaultValue = "tree")]
            public string action { get; set; }

            [ToolParameter("Runtime UI node id returned by action=tree. Required for action=click.", Required = false)]
            public int targetId { get; set; }

            [ToolParameter("Zero-based semantic node offset for action=tree paging.", Required = false, DefaultValue = "0")]
            public int offset { get; set; }

            [ToolParameter("Maximum semantic nodes returned by action=tree. Defaults to 64 and is capped at 128.", Required = false, DefaultValue = "64")]
            public int limit { get; set; }

            [ToolParameter("Include visible non-interactive text and UI containers so the Agent can understand panel context.", Required = false, DefaultValue = "true")]
            public bool includeText { get; set; }

            [ToolParameter("Return only clickable controls plus canvas roots. Useful for a compact follow-up query.", Required = false, DefaultValue = "false")]
            public bool interactiveOnly { get; set; }
        }

        /// <summary>读取当前语义 UI 树，或按树节点 ID 执行一次真实左键点击。</summary>
        public static object HandleCommand(JObject parameters)
        {
            JObject args = parameters ?? new JObject();
            string action = args["action"]?.ToString()?.Trim().ToLowerInvariant() ?? "tree";
            return action switch
            {
                "tree" => GameplayUiRuntime.BuildTree(args),
                "click" => GameplayUiRuntime.Click(args),
                _ => new ErrorResponse("unknown_ui_action: action 只支持 tree 或 click。")
            };
        }
    }
}
