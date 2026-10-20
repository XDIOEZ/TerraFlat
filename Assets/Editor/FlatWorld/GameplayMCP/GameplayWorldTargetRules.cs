using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameplayMCP
{
    /// <summary>独立失活玩家只验证正式目标令牌解析，不进入世界或调用玩法动作。</summary>
    internal static class GameplayWorldTargetRules
    {
        #region 目标身份与容量契约采样
        [InitializeOnLoadMethod]
        private static void Register() => GameplayTestSystemProbe.Register("testing", "world_targets", Capture);

        private static JObject Capture(JObject parameters)
        {
            var root = new GameObject("[WorldTargetContract]") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var otherRoot = new GameObject("[WorldTargetOtherPlayer]") { hideFlags = HideFlags.HideAndDontSave };
            otherRoot.SetActive(false);
            try
            {
                Player player = root.AddComponent<Player>();
                Player other = otherRoot.AddComponent<Player>();
                bool alive = true;
                int refreshes = 0;
                var projection = new GameplayWorldTargets.Target { Backend = "contract", Guid = 7, ItemId = "test:target",
                    Position = new Vector3(2f, 3f), Aim = new Vector3(2f, 3f), Refresh = () => { refreshes++; return alive; } };
                string token = GameplayWorldTargets.Bind(projection, player);
                bool valid = GameplayWorldTargets.TryResolve(token, player, out var resolved, out _) &&
                    ReferenceEquals(resolved, projection) && resolved.Position == projection.Position;
                int beforeCrossPlayer = refreshes;
                bool crossPlayerRejected = !GameplayWorldTargets.TryResolve(token, other, out _, out _);
                bool foreignNoBackendAccess = refreshes == beforeCrossPlayer;
                bool unknownRejected = !GameplayWorldTargets.TryResolve("unknown", player, out _, out _);
                alive = false;
                bool expiredRejected = !GameplayWorldTargets.TryResolve(token, player, out _, out _);
                alive = true;
                bool expiredCannotRevive = !GameplayWorldTargets.TryResolve(token, player, out _, out _);
                string oldest = GameplayWorldTargets.Bind(projection, player);
                string newest = oldest;
                for (int index = 0; index < 256; index++) newest = GameplayWorldTargets.Bind(projection, player);
                bool bounded = !GameplayWorldTargets.TryResolve(oldest, player, out _, out _) &&
                    GameplayWorldTargets.TryResolve(newest, player, out _, out _);
                bool invalidRadiusRejected = true;
                foreach (JToken radius in new JToken[] { new JValue(float.NaN), new JValue(float.PositiveInfinity),
                    new JValue(0f), new JValue(-1f), new JObject(), new JValue("invalid") })
                    invalidRadiusRejected &= GameplayWorldTargets.Query(player, new JObject { ["radius"] = radius },
                        new HashSet<string>()) is ErrorResponse;
                return new JObject { ["valid"] = valid, ["crossPlayerRejected"] = crossPlayerRejected,
                    ["foreignNoBackendAccess"] = foreignNoBackendAccess, ["unknownRejected"] = unknownRejected,
                    ["expiredRejected"] = expiredRejected, ["expiredCannotRevive"] = expiredCannotRevive,
                    ["bounded"] = bounded, ["invalidRadiusRejected"] = invalidRadiusRejected };
            }
            finally
            {
                // 自有失活夹具没有登记世界索引，销毁前关闭正常物品回收通知。
                otherRoot.GetComponent<Player>()?.PrepareForDespawn(false);
                root.GetComponent<Player>()?.PrepareForDespawn(false);
                Object.DestroyImmediate(otherRoot); Object.DestroyImmediate(root);
            }
        }
        #endregion
    }
}
