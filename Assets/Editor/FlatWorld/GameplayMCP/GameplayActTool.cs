using System.Globalization;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace FlatWorld.GameplayMCP
{
    /// <summary>向 AI 提供可扩展的统一玩法动作入口。</summary>
    [McpForUnityTool(
        "gameplay_act",
        Description = "Perform a live gameplay action, or up to 8 sequential short steps in one call. Set observe=true to return compact live state after execution; observation overrides that profile. Batch stops at the first failed outcome. Use separate calls when the next action depends on fresh observation.",
        Group = "core")]
    public static class GameplayActTool
    {
        public sealed class Parameters
        {
            [ToolParameter("Action name. Required unless steps is supplied. Call gameplay_capabilities for registered actions.", Required = false)]
            public string action { get; set; }

            [ToolParameter("Optional array of 1-8 action parameter objects, executed in order. Timed actions require explicit seconds; total duration at most 8 seconds. No nested batches. Immediate batch actions: look_at, select_hotbar, interact, use, stop, inventory_to_hotbar. Timed: move, move_to, attack, wait, press_key, interact_hold.", Required = false)]
            public JObject[] steps { get; set; }

            [ToolParameter("Return live compact observation after execution, avoiding a separate gameplay_observe call.", Required = false)]
            public bool observe { get; set; }

            [ToolParameter("Observation options, same as gameplay_observe. Implies observe=true; defaults to profile=compact.", Required = false)]
            public object observation { get; set; }

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

            [ToolParameter("Exact ItemDefinition id used by inventory actions.", Required = false)]
            public string itemId { get; set; }

            [ToolParameter("Input System action name for press_key, for example B, E, H, P, ESC or OpenChat. Resolves the player's current effective keyboard binding and therefore follows rebinding.", Required = false)]
            public string inputAction { get; set; }

            [ToolParameter("Physical keyboard key/control for press_key, for example b, e, escape, enter or 1. Use inputAction instead when the gameplay shortcut is rebindable.", Required = false)]
            public string key { get; set; }
        }

        #region 动作与合并观察

        /// <summary>合并已确定的短动作与最终观察，减少 Agent 往返。</summary>
        public static async Task<object> HandleCommand(JObject parameters)
        {
            JObject args = parameters ?? new JObject();
            if (args["observation"] != null && args["observation"] is not JObject)
                return new ErrorResponse("invalid_observation: observation 必须是参数对象。");
            JObject result;
            if (args["steps"] != null)
            {
                if (args["steps"] is not JArray steps || steps.Count < 1 || steps.Count > 8 || args["action"] != null)
                    return new ErrorResponse("invalid_batch: steps 必须包含 1～8 个动作，不能同时传 action。");
                string validationError = ValidateSteps(steps);
                if (validationError != null)
                    return new ErrorResponse(validationError);

                var results = new JArray();
                bool completed = true;
                for (int i = 0; i < steps.Count; i++)
                {
                    JObject stepResult = await GameplayMcpRuntime.ExecuteActionAsync((JObject)steps[i]);
                    results.Add(stepResult);
                    if (FailedOutcome(stepResult))
                    {
                        completed = false;
                        break;
                    }
                    // 同步动作也留一帧给生产系统更新，避免同帧连点读取旧状态。
                    if (i + 1 < steps.Count && !await GameplayMcpRuntime.WaitEditorSecondsAsync(0.001f))
                    {
                        completed = false;
                        break;
                    }
                }
                result = new JObject
                {
                    ["ok"] = completed,
                    ["results"] = results,
                    ["executed"] = results.Count,
                    ["remaining"] = steps.Count - results.Count
                };
            }
            else
                result = await GameplayMcpRuntime.ExecuteActionAsync(args);

            if (args.Value<bool?>("observe") == true || args["observation"] is JObject)
            {
                await GameplayMcpRuntime.WaitEditorSecondsAsync(0.001f);
                JObject options = args["observation"] is JObject supplied ? (JObject)supplied.DeepClone() : new JObject();
                if (options["profile"] == null)
                    options["profile"] = "compact";
                result["observation"] = GameplayMcpRuntime.BuildObservation(options);
            }
            return new SuccessResponse("FlatWorld gameplay action completed.", result);
        }

        /// <summary>在任何动作开始前检查整批输入与等待预算。</summary>
        private static string ValidateSteps(JArray steps)
        {
            double duration = 0;
            foreach (JToken token in steps)
            {
                if (token is not JObject step || step["steps"] != null || step["observe"] != null || step["observation"] != null)
                    return "invalid_batch_step: 每步只能包含单个动作参数，观察放在批次外层。";
                string action = step["action"]?.ToString()?.Trim().ToLowerInvariant();
                bool immediate = action == "look_at" || action == "select_hotbar" || action == "interact" ||
                                 action == "use" || action == "stop" || action == "inventory_to_hotbar";
                bool timed = action == "move" || action == "move_to" || action == "attack" ||
                             action == "wait" || action == "press_key" || action == "interact_hold";
                if (!immediate && !timed)
                    return "unsupported_batch_action: 此动作请单独调用 gameplay_act。";
                if (!timed)
                    continue;
                if (!double.TryParse(step["seconds"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double seconds) || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.05 || seconds > 8)
                    return "invalid_batch_duration: 持续动作必须明确传入 0.05～8 秒的 seconds。";
                duration += seconds + 0.02;
            }
            return duration > 8 ? "batch_too_long: 整批持续动作总时长最多 8 秒，请拆开调用。" : null;
        }

        /// <summary>业务拒绝与导航未到达也结束批次，保留原始结果供 Agent 决策。</summary>
        private static bool FailedOutcome(JObject result)
        {
            if (result.Value<bool?>("ok") != true)
                return true;
            JObject data = result["data"] as JObject;
            return data?.Value<bool?>("notReached") == true || data?.Value<bool?>("timedOut") == true ||
                   data?.Value<bool?>("interacted") == false || data?.Value<bool?>("selected") == false ||
                   data?.Value<bool?>("moved") == false;
        }

        #endregion
    }
}
