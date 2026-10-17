using System.Collections.Generic;
using FlatWorld.Gameplay.InventoryFeedback;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestRuntimeProbe
{
    #region 方向甩动独立库存现场
    // 临时正式面板只提供真实屏幕坐标，选择和提交均调用生产接口，业务期望由 Lua 持有。
    private static JObject CaptureInventoryDirectionalFlick(Player player, JObject args)
    {
        string mode = GameplayTestSystemProbe.Text(args, "mode", "right");
        var root = new GameObject("LuaDirectionalFlick", typeof(RectTransform), typeof(Canvas));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var panels = new List<BasePanel>();
        Inventory previousLast = Inventory.LastOpenedContainer;
        Inventory source = CreateDragInventory("lua.direction.source", CreateDragItem("Twine", 6));
        Inventory right = CreateDragInventory("lua.direction.right", new ItemData[1]);
        Inventory down = CreateDragInventory("lua.direction.down", new ItemData[1]);
        InventoryDragTransaction drag = null;
        InventoryFlickTargets[] previousPanels = InventoryFlickTargets.GetActivePanels();
        int receipts = 0;
        string receiptTarget = string.Empty;
        Vector2? receiptOrigin = null;
        bool unrelatedOriginLeaked = false;
        void Capture(InventoryQuickTransferSignal signal)
        {
            if (!ReferenceEquals(signal.Source, source)) return;
            receipts++;
            receiptOrigin = signal.ScreenOrigin;
            unrelatedOriginLeaked |= InventoryQuickTransferFeedbackEvents.GetScreenOrigin(down, 0).HasValue;
            receiptTarget = ReferenceEquals(signal.Target, right) ? "right" :
                ReferenceEquals(signal.Target, down) ? "down" : "unexpected";
        }
        InventoryQuickTransferFeedbackEvents.Committed += Capture;
        try
        {
            foreach (InventoryFlickTargets registered in previousPanels)
                if (registered != null) registered.enabled = false;
            BasePanel MakePanel(Inventory inventory, Vector2 position)
            {
                var go = new GameObject(inventory.Data.Name, typeof(RectTransform), typeof(BasePanel));
                go.transform.SetParent(root.transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.anchoredPosition = position;
                rect.sizeDelta = new Vector2(60f, 60f);
                BasePanel panel = go.GetComponent<BasePanel>();
                panel.SetGameplayInputBlocking(false);
                panel.Init();
                panels.Add(panel);
                var slotObject = new GameObject("slot", typeof(RectTransform), typeof(ItemSlot_UI));
                slotObject.transform.SetParent(go.transform, false);
                var slotRect = (RectTransform)slotObject.transform;
                slotRect.anchorMin = Vector2.zero;
                slotRect.anchorMax = Vector2.one;
                slotRect.sizeDelta = Vector2.zero;
                ItemSlot_UI slot = slotObject.GetComponent<ItemSlot_UI>();
                slot.InitializeSlot(0, inventory.Data.GetItemSlot, _ => { });
                inventory.itemSlot_UI.Add(slot);
                inventory.SyncQuickTransferTarget(panel);
                return panel;
            }
            Vector2 origin = new(Screen.width * .18f, Screen.height * .72f);
            BasePanel rightPanel = MakePanel(right, new Vector2(Screen.width * .82f, origin.y));
            bool vertical = mode == "down" || mode == "diagonal" || mode == "same_panel";
            BasePanel downPanel = MakePanel(down, vertical ? new Vector2(origin.x, Screen.height * .18f) :
                new Vector2(Screen.width * .65f, Screen.height * .64f));
            Vector2 direction = mode == "down" || mode == "same_panel" ? Vector2.down :
                mode == "diagonal" ? new Vector2(.3f, -1f) : Vector2.right;
            direction *= 120f;
            if (mode == "partial") right.Data.itemSlots[0].SlotMaxVolume = 2;
            if (mode == "full" || mode == "none") right.Data.itemSlots[0].SlotMaxVolume = 0;
            if (mode == "none") down.Data.itemSlots[0].SlotMaxVolume = 0;
            if (mode == "blocked") right.Data.IsDepositBlocked = true;
            if (mode == "tag") right.Data.itemSlots[0].CanAcceptTags.Add(EquipmentSlotCatalog.HeadTag);
            if (mode == "capacity") right.Data.SetCarryCapacity(0f, 0f);
            if (mode == "closed") rightPanel.Close();
            if (mode == "hidden") rightPanel.canvasGroup.alpha = 0;
            if (mode == "disabled") rightPanel.gameObject.SetActive(false);
            if (mode == "output") right.SyncQuickTransferTarget(rightPanel, allowDirectionalDeposit: false);
            if (mode == "released") right.ReleaseRuntimePanel();
            if (mode == "stale_mapping") right.itemSlot_UI[0].InitializeSlot(0, _ => down.Data.GetItemSlot(0), _ => { });
            if (mode == "same_panel")
            {
                down.itemSlot_UI[0].transform.SetParent(rightPanel.transform, true);
                down.SyncQuickTransferTarget(rightPanel);
                downPanel.Close();
            }
            Canvas.ForceUpdateCanvases();
            Inventory selected = InventoryFlickTargets.Resolve(source, 0, origin, direction);
            if (mode == "pc") source.Data.Name = ModText.Hotbar;
            bool unchangedAfterSelection = source.Data.GetItemSlot(0).itemData.Stack.Amount == 6 &&
                right.Data.GetItemSlot(0).itemData == null && down.Data.GetItemSlot(0).itemData == null && receipts == 0;
            drag = source.OnMouseDragBegin(0);
            bool moved = false;
            bool consumed = drag.TryConsumeSourceItem(item =>
            {
                if (!ReferenceEquals(source.Data.GetItemSlot(0).itemData, item)) return false;
                // 夹具只能把临时物品转给自有库存，即使候选隔离回归也不能污染真实玩家。
                if (selected == null || ReferenceEquals(selected, right) || ReferenceEquals(selected, down))
                {
                    if (mode == "pc") source.OnShiftQuickTransfer(0);
                    else source.OnTouchFlickTransfer(0, origin, direction);
                    moved = (source.Data.GetItemSlot(0).itemData?.Stack?.Amount ?? 0f) < 6f;
                }
                return true;
            });
            drag.Complete(false);
            return new JObject { ["selected"] = selected == null ? "none" : ReferenceEquals(selected, right) ? "right" :
                    ReferenceEquals(selected, down) ? "down" : "unexpected",
                ["unchangedAfterSelection"] = unchangedAfterSelection, ["consumed"] = consumed, ["moved"] = moved,
                ["source"] = CaptureDragSlot(source, 0), ["right"] = CaptureDragSlot(right, 0),
                ["down"] = CaptureDragSlot(down, 0), ["receipts"] = receipts, ["receiptTarget"] = receiptTarget,
                ["hasScreenOrigin"] = receiptOrigin.HasValue,
                ["originX"] = receiptOrigin.GetValueOrDefault().x, ["originY"] = receiptOrigin.GetValueOrDefault().y,
                ["releaseX"] = (origin + direction).x, ["releaseY"] = (origin + direction).y,
                ["originScopeCleared"] = !InventoryQuickTransferFeedbackEvents.GetScreenOrigin(source, 0).HasValue,
                ["unrelatedOriginLeaked"] = unrelatedOriginLeaked,
                ["dragLocked"] = source.HasActiveSlotDrag };
        }
        finally
        {
            drag?.Complete(false);
            InventoryQuickTransferFeedbackEvents.Committed -= Capture;
            foreach (BasePanel panel in panels) if (panel != null) panel.Close();
            foreach (InventoryFlickTargets registered in previousPanels)
                if (registered != null) registered.enabled = true;
            Inventory.LastOpenedContainer = previousLast;
            Object.Destroy(root);
        }
    }
    #endregion
}
