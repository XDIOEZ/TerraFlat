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
            return GameplayMcpOutput.Invoke("gameplay_observe", parameters, ExecuteCommand, false);
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
            JObject observation = GameplayMcpRuntime.BuildObservation(parameters);
            // 复用正式随身容量口径，避免空槽被误认为还能拾取。
            if (observation["player"] is JObject playerData &&
                GameplayMcpRuntime.TryGetPlayerContext(out Player player, out _, out _, out _))
            {
                // 快捷栏装备与鼠标搬运库存分开观察，避免拿着矿石也被当作挥镐。
                Item equipped = player.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.CurentSelectItem;
                Mod_ColdWeapon weapon = equipped?.GetComponentInChildren<Mod_ColdWeapon>(true);
                Mod_Weapon_AnimationAction animated = equipped?.GetComponentInChildren<Mod_Weapon_AnimationAction>(true);
                playerData["equippedWeapon"] = new JObject
                {
                    ["held"] = equipped?.itemData?.IDName ?? string.Empty,
                    ["guid"] = equipped?.itemData?.Guid,
                    ["runtimeWeapon"] = weapon != null || animated != null,
                    ["backend"] = weapon != null ? "cold_weapon" : animated != null ? "animation" : "none",
                    ["canAttack"] = weapon != null ? new JValue(weapon.CanAttack) : animated != null ? JValue.CreateNull() : new JValue(false),
                    ["attackState"] = weapon?.CurrentState.ToString() ?? string.Empty
                };
                if (PlayerCarryCapacityUtility.TryGetSnapshot(player, out PlayerCarryCapacitySnapshot capacity))
                playerData["carryCapacity"] = new JObject
                {
                    ["weight"] = capacity.CurrentWeight,
                    ["volume"] = capacity.CurrentVolume,
                    ["maxWeight"] = float.IsInfinity(capacity.MaxWeight) ? null : new JValue(capacity.MaxWeight),
                    ["maxVolume"] = float.IsInfinity(capacity.MaxVolume) ? null : new JValue(capacity.MaxVolume),
                    ["unlimited"] = capacity.IsUnlimited
                };
            }
            return new SuccessResponse(
                "FlatWorld gameplay observation.",
                observation);
        }

        #endregion
    }
}
