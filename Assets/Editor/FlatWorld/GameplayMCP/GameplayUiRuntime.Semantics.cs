using System;
using System.Linq;
using System.Threading.Tasks;
using FlatWorld.Networking;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FlatWorld.GameplayMCP
{
    internal static partial class GameplayUiRuntime
    {
        #region 可见槽位之间的数量搬运

        public static object Transfer(JObject parameters)
        {
            if (!Application.isPlaying) return new ErrorResponse("not_playing: 需要 Play Mode。");
            int sourceId = ReadInt(parameters, "sourceId", 0), targetId = ReadInt(parameters, "targetId", 0);
            if (sourceId == targetId) return new ErrorResponse("same_slot: 来源和目标不能相同。");
            if (!TryResolveVisibleSlot(sourceId, out ItemSlot_UI sourceUi, out string error) ||
                !TryResolveVisibleSlot(targetId, out ItemSlot_UI targetUi, out error)) return new ErrorResponse(error);
            Inventory source = sourceUi.BoundInventory, target = targetUi.BoundInventory;
            ItemSlot sourceSlot = sourceUi.GetBoundSlotData(), targetSlot = targetUi.GetBoundSlotData();
            ItemData original = sourceSlot.itemData;
            if (original?.Stack == null || original.Stack.Amount <= 0f)
                return new ErrorResponse("source_empty: 来源槽没有可搬运物品。");
            if (ReferenceEquals(sourceSlot, targetSlot)) return new ErrorResponse("same_slot: 两个节点绑定了相同槽位。");
            if (ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction())
                return new ErrorResponse("inventory_pending: 正在等待库存事务确认。");
            if (source.HasActiveSlotDrag || target.HasActiveSlotDrag)
                return new ErrorResponse("inventory_dragging: 库存仍有拖拽事务，请结束后重试。");
            float before = original.Stack.Amount;
            string itemId = original.IDName;
            int quantity = 0;
            bool explicitAmount = parameters["amount"] != null && parameters["amount"].Type != JTokenType.Null;
            if (explicitAmount && (!int.TryParse(parameters["amount"].ToString(), out quantity) || quantity <= 0))
                return new ErrorResponse("invalid_amount: amount 必须是正整数，省略表示整组搬运。");
            bool swaps = !explicitAmount && targetSlot.itemData != null && !targetSlot.itemData.CanStackWith(original);
            bool submitted = explicitAmount
                ? source.TryTransferQuantityTo(sourceUi.slotIndex, target, targetUi.slotIndex, quantity)
                : source.TryMoveSlotTo(sourceUi.slotIndex, target, targetUi.slotIndex);
            if (!submitted) return new ErrorResponse("transfer_rejected: 正式库存规则拒绝搬运，请检查容量、接收规则或待确认事务。");
            // 正式整组拖拽可重定向装备目标，实际搬运量以来源引用和余量变化为准。
            float sourceRemaining = ReferenceEquals(sourceSlot.itemData, original) ? original.Stack.Amount : 0f;
            float moved = Mathf.Max(0f, before - sourceRemaining);
            bool pending = moved <= .0001f && !GameNetwork.HasStateAuthority &&
                (source.MachineOwner != null || target.MachineOwner != null ||
                 source is SpacesuitTankInventory || target is SpacesuitTankInventory);
            float requested = explicitAmount ? quantity : before;
            bool completed = moved + .0001f >= requested;
            return new SuccessResponse("FlatWorld inventory transfer result.", new
            {
                action = "transfer", sourceId, targetId, itemId,
                requested, moved = Round(moved), completed, pending,
                status = completed ? "completed" : pending ? "pending" : moved > .0001f ? "partial" : "unchanged",
                swapped = swaps && moved > .0001f,
                sourceAfter = SlotState(sourceSlot), targetAfter = SlotState(targetSlot)
            });
        }

        private static JObject SlotState(ItemSlot slot) => new JObject
        { ["itemId"] = slot?.itemData?.IDName ?? string.Empty, ["amount"] = Round(slot?.itemData?.Stack?.Amount ?? 0f) };

        private static bool TryResolveVisibleSlot(int id, out ItemSlot_UI slot, out string error)
        {
            slot = null;
            error = "slot_unavailable: 请重新读取当前 UI 树并使用可见槽位节点。";
            GameObject node = EditorUtility.InstanceIDToObject(id) as GameObject;
            if (!IsQueryableRuntimeObject(node) || !node.activeInHierarchy || !IsInteractable(node) ||
                !IsVisibleThroughCanvasGroups(node.transform) || EventSystem.current == null ||
                !TryFindDragRaycast(EventSystem.current, node, out _, out _, out _, out _)) return false;
            slot = node.GetComponent<ItemSlot_UI>();
            Inventory inventory = slot?.BoundInventory;
            int index = slot != null ? slot.slotIndex : -1;
            if (inventory?.Data?.GetItemSlot(index) == null || inventory.itemSlot_UI == null ||
                (uint)index >= (uint)inventory.itemSlot_UI.Count || inventory.itemSlot_UI[index] != slot ||
                !ReferenceEquals(inventory.Data.GetItemSlot(index), slot.GetBoundSlotData())) return false;
            error = null;
            return true;
        }

        #endregion

        #region 按需制作信息与正式候选选择

        public static object Recipes(JObject parameters)
        {
            if (!TryResolveCraftingView(parameters, out CraftingStationController controller, out string error))
                return new ErrorResponse(error);
            int offset = Mathf.Max(0, ReadInt(parameters, "offset", 0));
            int limit = Mathf.Clamp(ReadInt(parameters, "limit", 8), 1, 32);
            bool details = !GameplayMcpOutput.IsCompact(parameters);
            var matches = controller.Candidates;
            var page = new JArray();
            for (int i = offset; i < matches.Count && page.Count < limit; i++)
            {
                RuntimeRecipe recipe = matches[i].Recipe;
                CraftingResult preview = controller.Preview(recipe);
                var node = new JObject
                {
                    ["recipeId"] = recipe.Id, ["name"] = recipe.name,
                    ["selected"] = ReferenceEquals(recipe, controller.SelectedRecipe),
                    ["buttonId"] = controller.FindRecipeButton(recipe.Id)?.gameObject.GetInstanceID(),
                    ["canCraft"] = preview.Success, ["reason"] = preview.FailureReason.ToString()
                };
                if (details)
                {
                    node["inputs"] = new JArray(recipe.inputs.RowItems_List.Select(input => new JObject
                    {
                        ["match"] = input.matchMode.ToString(), ["itemId"] = input.ItemName,
                        ["tag"] = input.Tag, ["amount"] = input.amount
                    }));
                    node["outputs"] = new JArray(recipe.outputs.results.Select(output => new JObject
                    { ["itemId"] = output.ItemName, ["amount"] = output.amount }));
                    node["failure"] = preview.Message;
                }
                page.Add(node);
            }
            bool truncated = offset + page.Count < matches.Count;
            return new SuccessResponse("FlatWorld open crafting station.", new
            {
                action = "recipes", panelId = controller.Panel.gameObject.GetInstanceID(),
                selectedRecipeId = controller.SelectedRecipe?.Id,
                authoritativeRecipeId = controller.AuthoritativeRecipeId,
                completedWork = Round(controller.CompletedWork), requiredWork = Round(controller.RequiredWork),
                remainingWork = Round(Mathf.Max(0f, controller.RequiredWork - controller.CompletedWork)),
                total_count = matches.Count, offset, returned_count = page.Count, limit, truncated,
                next_offset = truncated ? (int?)(offset + page.Count) : null, recipes = page,
                scope = "current_input_candidates"
            });
        }

        public static async Task<object> SelectRecipeAsync(JObject parameters)
        {
            if (!TryResolveCraftingView(parameters, out CraftingStationController controller, out string error))
                return new ErrorResponse(error);
            string id = parameters.Value<string>("recipeId")?.Trim();
            if (string.IsNullOrEmpty(id)) return new ErrorResponse("missing_recipe_id: 需要当前候选的 recipeId。");
            Button button = controller.FindRecipeButton(id);
            if (button == null || !button.isActiveAndEnabled) return new ErrorResponse("recipe_unavailable: 配方不在当前候选列表。");
            ScrollRect scroll = button.GetComponentInParent<ScrollRect>();
            for (int attempt = 0; attempt < 16; attempt++)
            {
                if (button == null || controller.IsDisposed || !button.gameObject.activeInHierarchy)
                    return new ErrorResponse("recipe_changed: 配方面板或候选已变化，请重新查询。");
                if (EventSystem.current != null && TryFindClickableRaycast(EventSystem.current, button.gameObject,
                        out _, out _, out _))
                {
                    object response = await ClickAsync(new JObject { ["targetId"] = button.gameObject.GetInstanceID() });
                    if (response is not SuccessResponse) return response;
                    if (controller.IsDisposed || controller.Panel == null)
                        return new ErrorResponse("recipe_changed: 点击期间制作面板已关闭。");
                    bool pending = controller.UsesAuthoritativeProcessor &&
                        !string.Equals(controller.AuthoritativeRecipeId, id, StringComparison.Ordinal);
                    bool completed = !pending && string.Equals(controller.SelectedRecipe?.Id, id, StringComparison.Ordinal);
                    return new SuccessResponse("FlatWorld recipe selection result.", new
                    {
                        action = "select_recipe", recipeId = id,
                        selectedRecipeId = controller.SelectedRecipe?.Id,
                        completed, pending, status = completed ? "completed" : pending ? "pending" : "unchanged",
                        authoritativeRecipeId = controller.AuthoritativeRecipeId
                    });
                }
                if (scroll == null || scroll.viewport == null || !TryGetScreenRect(button.gameObject, out Rect buttonRect) ||
                    !TryGetScreenRect(scroll.viewport.gameObject, out Rect viewport)) break;
                float direction = buttonRect.center.y < viewport.yMin ? -1f :
                    buttonRect.center.y > viewport.yMax ? 1f : 0f;
                if (direction == 0f) break;
                Scroll(new JObject { ["targetId"] = scroll.gameObject.GetInstanceID(), ["deltaY"] = direction * 100f });
                if (!await GameplayMcpRuntime.WaitEditorSecondsAsync(.001f)) break;
            }
            return new ErrorResponse("recipe_occluded: 候选按钮被遮挡或无法滚动到可见区域。");
        }

        private static bool TryResolveCraftingView(JObject args, out CraftingStationController controller, out string error)
        {
            controller = null;
            error = "crafting_panel_unavailable: 需要打开一个制作面板，多个面板时请提供 targetId。";
            if (!Application.isPlaying) return false;
            int id = ReadInt(args, "targetId", 0);
            CraftingStationView[] views = id != 0
                ? new[] { (EditorUtility.InstanceIDToObject(id) as GameObject)?.GetComponentInParent<CraftingStationView>() }
                : UnityEngine.Object.FindObjectsOfType<CraftingStationView>();
            foreach (CraftingStationView view in views)
            {
                if (view == null || !view.gameObject.activeInHierarchy || view.Controller == null ||
                    view.Controller.IsDisposed || view.Controller.Panel == null || !view.Controller.Panel.IsOpen() ||
                    !IsQueryableRuntimeObject(view.gameObject) || !IsVisibleThroughCanvasGroups(view.transform)) continue;
                if (controller != null) { controller = null; return false; }
                controller = view.Controller;
            }
            return controller != null;
        }

        #endregion
    }
}
