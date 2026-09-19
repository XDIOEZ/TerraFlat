using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace FlatWorld.GameplayMCP
{
    /// <summary>把玩家主背包中的指定物品通过正式库存交换流程移动到指定快捷栏槽。</summary>
    [GameplayMcpAction(
        "inventory_to_hotbar",
        "Move an exact itemId from the player's main bag to hotbar slot index through production inventory transactions. Normal stacks use Inventory.TryMoveSlotTo; legacy over-stacked non-stackable tools fall back to Inventory_Data.TransferItemQuantityTo for one valid item.")]
    internal sealed class GameplayInventoryMoveAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters)
        {
            string itemId = parameters?["itemId"]?.ToString()?.Trim() ?? string.Empty;
            int targetIndex = int.TryParse(parameters?["index"]?.ToString(), out int parsedIndex)
                ? parsedIndex
                : -1;
            if (string.IsNullOrEmpty(itemId) || targetIndex < 0)
            {
                return Task.FromResult(GameplayMcpRuntime.BuildActionError(
                    "invalid_inventory_move",
                    "inventory_to_hotbar 需要 itemId 和非负 index。",
                    false));
            }

            Player player = context.Player;
            Mod_Inventory bagModule = player?.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag);
            Inventory bag = bagModule?.InventoryInstances?
                .FirstOrDefault(candidate => candidate?.Data != null &&
                                             string.Equals(candidate.Data.Name, ModText.Bag,
                                                 StringComparison.Ordinal));
            bag ??= bagModule?.InventoryInstances?.FirstOrDefault(candidate => candidate != null);

            Inventory_HotBar hotbar = player?.itemMods?.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar) ??
                                      player?.GetComponentInChildren<Inventory_HotBar>(true);
            Inventory target = hotbar?.RuntimeInventory;
            if (bag?.Data?.itemSlots == null || target?.Data?.itemSlots == null)
            {
                return Task.FromResult(GameplayMcpRuntime.BuildActionError(
                    "inventory_not_ready",
                    "玩家主背包或快捷栏尚未就绪。",
                    false));
            }

            int sourceIndex = bag.Data.itemSlots.FindIndex(slot =>
                string.Equals(slot?.itemData?.IDName, itemId, StringComparison.OrdinalIgnoreCase));
            if (sourceIndex < 0)
            {
                return Task.FromResult(GameplayMcpRuntime.BuildActionError(
                    "inventory_item_missing",
                    $"玩家主背包中没有 {itemId}。",
                    false));
            }

            if (targetIndex >= target.Data.itemSlots.Count)
            {
                return Task.FromResult(GameplayMcpRuntime.BuildActionError(
                    "hotbar_index_out_of_range",
                    $"快捷栏槽位 {targetIndex} 超出范围。",
                    false));
            }

            ItemSlot sourceSlot = bag.Data.itemSlots[sourceIndex];
            ItemSlot targetSlot = target.Data.itemSlots[targetIndex];
            bool moved = bag.TryMoveSlotTo(sourceIndex, target, targetIndex);
            if (!moved &&
                sourceSlot?.itemData?.Stack != null &&
                !sourceSlot.itemData.Stack.Stackable &&
                sourceSlot.itemData.Stack.Amount > 1.0001f &&
                targetSlot?.itemData == null)
            {
                // 历史 GM 存档可能把“不可堆叠”工具保存成 >1 的脏堆叠。
                // 不放宽正式拖拽规则，只通过库存已有的跨库存事务拆出一件合法实例供自动化验收。
                moved = bag.Data.TransferItemQuantityTo(sourceSlot, target.Data, targetSlot, 1);
            }
            if (!moved)
            {
                return Task.FromResult(GameplayMcpRuntime.BuildActionError(
                    "inventory_move_rejected",
                    $"{itemId} 无法移动到快捷栏槽位 {targetIndex}。",
                    false));
            }

            hotbar.TrySelectSlot(targetIndex);
            return Task.FromResult(GameplayMcpRuntime.BuildActionSuccess(
                "inventory_to_hotbar",
                player,
                new JObject
                {
                    ["itemId"] = itemId,
                    ["sourceIndex"] = sourceIndex,
                    ["index"] = targetIndex,
                    ["selected"] = true
                }));
        }
    }
}
