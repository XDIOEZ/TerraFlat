using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.GameplayMCP
{
    /// <summary>仅用于本次自动化验证：通过正式 Inventory API 切换玩家主背包。</summary>
    [GameplayMcpAction("toggle_bag_test", "Toggle the real player bag through Inventory.SwitchUI for automated gameplay verification.")]
    internal sealed class GameplayBagToggleTestAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters)
        {
            Mod_Inventory bagModule = context.Player?.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag);
            Inventory bag = bagModule?.InventoryInstances?
                .FirstOrDefault(candidate => candidate?.Data != null &&
                                             string.Equals(candidate.Data.Name, ModText.Bag,
                                                 System.StringComparison.Ordinal));
            bag ??= bagModule?.InventoryInstances?.FirstOrDefault(candidate => candidate != null);
            if (bag == null)
            {
                return Task.FromResult(GameplayMcpRuntime.BuildActionError(
                    "bag_missing",
                    "玩家主背包尚未就绪。",
                    false));
            }

            bag.SwitchUI();
            return Task.FromResult(GameplayMcpRuntime.BuildActionSuccess(
                "toggle_bag_test",
                context.Player,
                new JObject { ["toggled"] = true }));
        }
    }

    [GameplayMcpAction("spawn_world_item_test", "Spawn one real runtime Item through ItemMgr for isolated gameplay verification.")]
    internal sealed class GameplaySpawnWorldItemTestAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters)
        {
            string itemId = parameters?["itemId"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(itemId))
                return Task.FromResult(GameplayMcpRuntime.BuildActionError("missing_item_id", "itemId 不能为空。", false));
            float x = parameters?["xOffset"]?.Value<float?>() ?? 1f;
            float y = parameters?["yOffset"]?.Value<float?>() ?? 0f;
            Vector3 position = context.Player.transform.position + new Vector3(x, y, 0f);
            Item item = ItemMgr.Instance.InstantiateItem(itemId, position);
            item.Load();
            return Task.FromResult(GameplayMcpRuntime.BuildActionSuccess(
                "spawn_world_item_test",
                context.Player,
                new JObject
                {
                    ["id"] = item.itemData.IDName,
                    ["guid"] = item.itemData.Guid,
                    ["x"] = item.transform.position.x,
                    ["y"] = item.transform.position.y
                }));
        }
    }

    [GameplayMcpAction("spawn_loot_drop_test", "Spawn one real ECS loot drop through DroppedItemService and verify it becomes a pickable ground item.")]
    internal sealed class GameplaySpawnLootDropTestAction : IGameplayMcpAction
    {
        public async Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters)
        {
            string itemId = parameters?["itemId"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(itemId))
                return GameplayMcpRuntime.BuildActionError("missing_item_id", "itemId 不能为空。", false);
            float x = parameters?["xOffset"]?.Value<float?>() ?? 2f;
            float y = parameters?["yOffset"]?.Value<float?>() ?? 0f;
            Vector2 position = (Vector2)context.Player.transform.position + new Vector2(x, y);
            DroppedItemHandle handle = DroppedItemService.SpawnLoot(
                itemId, position, 1f, destination: position, duration: 0f);
            await GameplayMcpRuntime.WaitEditorSecondsAsync(0.5f);
            bool pickable = DroppedItemService.TryGetSnapshot(handle, out ItemData snapshot);
            DroppedItemService.TryGetPickablePosition(handle, out Vector2 groundPosition);
            return GameplayMcpRuntime.BuildActionSuccess(
                "spawn_loot_drop_test",
                context.Player,
                new JObject
                {
                    ["handleValid"] = handle.IsValid,
                    ["pickable"] = pickable,
                    ["id"] = snapshot?.IDName ?? string.Empty,
                    ["amount"] = snapshot?.Stack?.Amount ?? 0f,
                    ["x"] = groundPosition.x,
                    ["y"] = groundPosition.y
                });
        }
    }

    [GameplayMcpAction("drain_stone_drops_test", "Inspect and remove nearby pickable Stone-tag ECS drops for isolated loot verification.")]
    internal sealed class GameplayDrainStoneDropsTestAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters)
        {
            float radius = parameters?["radius"]?.Value<float?>() ?? 8f;
            var ids = new List<string>();
            int guard = 0;
            while (guard++ < 64 &&
                   DroppedItemService.TryFindNearestTagged(context.Player.transform.position, radius, "Stone", out DroppedItemHandle handle))
            {
                if (!DroppedItemService.TryGetSnapshot(handle, out ItemData snapshot))
                    break;
                ids.Add(snapshot.IDName ?? string.Empty);
                DroppedItemService.Remove(handle);
            }
            return Task.FromResult(GameplayMcpRuntime.BuildActionSuccess(
                "drain_stone_drops_test",
                context.Player,
                new JObject
                {
                    ["count"] = ids.Count,
                    ["ids"] = new JArray(ids),
                    ["largeStoneCount"] = ids.Count(id => string.Equals(id, "LargeStone", System.StringComparison.OrdinalIgnoreCase)),
                    ["oreStoneCount"] = ids.Count(id => string.Equals(id, "Ore_Stone", System.StringComparison.OrdinalIgnoreCase))
                }));
        }
    }
}
