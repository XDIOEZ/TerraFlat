using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// 向自主游玩 Agent 暴露当前可见 UI 的语义树，并允许按树中运行时 ID 执行真实 UI 点击、文本、滚动或拖拽。
    /// </summary>
    [McpForUnityTool(
        "gameplay_ui",
        Description = "Inspect and operate live FlatWorld UI. action=tree returns paged visible controls; click/text/scroll/drag use standard UI events; transfer moves an optional quantity between visible item slots; recipes reads current crafting candidates and progress; select_recipe selects a candidate through its real UI button.",
        Group = "core", Discoverable = false)]
    public static class GameplayUiTool
    {
        #region 统一输入输出

        public static readonly string[] ActionNames = { "tree", "click", "text", "scroll", "drag", "transfer", "recipes", "select_recipe" };

        /// <summary>按需返回数据，错误与分页状态保持完整。</summary>
        public static async Task<object> HandleCommand(JObject parameters)
        {
            return await GameplayMcpOutput.InvokeAsync("gameplay_ui", parameters, ExecuteCommand, false);
        }

        #endregion

        public sealed class Parameters : GameplayMcpOutputParameters
        {
            [ToolParameter("After a successful UI action, wait one Editor update and return the refreshed UI tree in the same call. Uses the same paging options.", Required = false)]
            public bool treeAfter { get; set; }

            [ToolParameter("UI action: tree, click, text, scroll, drag, transfer, recipes, or select_recipe.", Required = false, DefaultValue = "tree")]
            public string action { get; set; }

            [ToolParameter("Runtime UI node id: control for click/text/scroll/drag, destination item_slot for transfer, optional crafting panel/node for recipes/select_recipe.", Required = false)]
            public int targetId { get; set; }

            [ToolParameter("Visible source item_slot node id for action=transfer; targetId is the destination slot.", Required = false)]
            public int sourceId { get; set; }

            [ToolParameter("Positive integer quantity for action=transfer. Omit to move the whole stack through normal drag rules.", Required = false)]
            public int? amount { get; set; }

            [ToolParameter("Exact candidate recipe id for action=select_recipe. targetId identifies its open crafting panel when more than one is open.", Required = false)]
            public string recipeId { get; set; }

            [ToolParameter("Text to type into a focused TMP_InputField/InputField for action=text. Focus the field through action=click first.", Required = false)]
            public string text { get; set; }

            [ToolParameter("Vertical EventSystem scroll delta for action=scroll. Negative scrolls down, positive scrolls up.", Required = false, DefaultValue = "-6")]
            public float deltaY { get; set; }

            [ToolParameter("Horizontal screen-space drag delta in pixels for action=drag.", Required = false, DefaultValue = "0")]
            public float deltaX { get; set; }

            [ToolParameter("Vertical screen-space drag delta in pixels for action=drag.", Required = false, DefaultValue = "0")]
            public float dragDeltaY { get; set; }

            [ToolParameter("Zero-based node offset for tree, or candidate offset for recipes.", Required = false, DefaultValue = "0")]
            public int offset { get; set; }

            [ToolParameter("Page size: tree defaults to 64/max128; recipes defaults to 8/max32.", Required = false)]
            public int limit { get; set; }

            [ToolParameter("Include visible non-interactive text and UI containers so the Agent can understand panel context.", Required = false, DefaultValue = "true")]
            public bool includeText { get; set; }

            [ToolParameter("Return only operable controls, scroll containers, plus canvas roots. Useful for a compact follow-up query.", Required = false, DefaultValue = "false")]
            public bool interactiveOnly { get; set; }
        }

        /// <summary>读取当前语义 UI 树，或按树节点 ID 执行一次真实左键点击。</summary>
        private static async Task<object> ExecuteCommand(JObject parameters)
        {
            JObject args = parameters ?? new JObject();
            string action = args["action"]?.ToString()?.Trim().ToLowerInvariant() ?? "tree";
            object response = action switch
            {
                "tree" => GameplayUiRuntime.BuildTree(args),
                "click" => await GameplayUiRuntime.ClickAsync(args),
                "text" => GameplayUiRuntime.TextInput(args),
                "scroll" => GameplayUiRuntime.Scroll(args),
                "drag" => GameplayUiRuntime.Drag(args),
                "transfer" => GameplayUiRuntime.Transfer(args),
                "recipes" => GameplayUiRuntime.Recipes(args),
                "select_recipe" => await GameplayUiRuntime.SelectRecipeAsync(args),
                _ => new ErrorResponse("unknown_ui_action: 请查询 UI 能力取得当前可用动作。")
            };
            if (action != "tree" && args.Value<bool?>("treeAfter") == true && response is SuccessResponse success)
            {
                await GameplayMcpRuntime.WaitEditorSecondsAsync(0.001f);
                JObject data = JObject.FromObject(success.Data);
                data["tree"] = JObject.FromObject(GameplayUiRuntime.BuildTree(args));
                return new SuccessResponse(success.Message, data);
            }
            return response;
        }
    }
}
