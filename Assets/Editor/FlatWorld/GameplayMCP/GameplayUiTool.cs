using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// 向自主游玩 Agent 暴露当前可见 UI 的语义树，并允许按树中运行时 ID 执行真实 UI 点击、滚动或拖拽。
    /// </summary>
    [McpForUnityTool(
        "gameplay_ui",
        Description = "Inspect and operate live FlatWorld UI without screenshots. action=tree returns a paged semantic UI tree for active canvases; action=click performs a real EventSystem left click; action=scroll sends a real EventSystem scroll event to a ScrollRect; action=drag sends the standard EventSystem pointer/drag chain to a draggable UI node.",
        Group = "core")]
    public static class GameplayUiTool
    {
        public sealed class Parameters
        {
            [ToolParameter("UI action: tree, click, scroll, or drag.", Required = false, DefaultValue = "tree")]
            public string action { get; set; }

            [ToolParameter("Runtime UI node id returned by action=tree. Required for action=click, action=scroll, or action=drag.", Required = false)]
            public int targetId { get; set; }

            [ToolParameter("Vertical EventSystem scroll delta for action=scroll. Negative scrolls down, positive scrolls up.", Required = false, DefaultValue = "-6")]
            public float deltaY { get; set; }

            [ToolParameter("Horizontal screen-space drag delta in pixels for action=drag.", Required = false, DefaultValue = "0")]
            public float deltaX { get; set; }

            [ToolParameter("Vertical screen-space drag delta in pixels for action=drag.", Required = false, DefaultValue = "0")]
            public float dragDeltaY { get; set; }

            [ToolParameter("Zero-based semantic node offset for action=tree paging.", Required = false, DefaultValue = "0")]
            public int offset { get; set; }

            [ToolParameter("Maximum semantic nodes returned by action=tree. Defaults to 64 and is capped at 128.", Required = false, DefaultValue = "64")]
            public int limit { get; set; }

            [ToolParameter("Include visible non-interactive text and UI containers so the Agent can understand panel context.", Required = false, DefaultValue = "true")]
            public bool includeText { get; set; }

            [ToolParameter("Return only operable controls, scroll containers, plus canvas roots. Useful for a compact follow-up query.", Required = false, DefaultValue = "false")]
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
                "scroll" => GameplayUiRuntime.Scroll(args),
                "drag" => GameplayUiRuntime.Drag(args),
                _ => new ErrorResponse("unknown_ui_action: action 只支持 tree、click、scroll 或 drag。")
            };
        }
    }
}
