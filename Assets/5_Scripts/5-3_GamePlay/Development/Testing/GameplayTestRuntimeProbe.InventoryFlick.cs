using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Gameplay.InventoryFeedback;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

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
                    else source.OnFlickTransfer(0, origin, direction);
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

    #region 跨设备甩动指针现场
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterFlickPointerRuntime() => RegisterFlickPointer();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterFlickPointerEditor() => RegisterFlickPointer();
#endif
    private static void RegisterFlickPointer() => RegisterAsync("inventory", "flick_pointer", CaptureFlickPointer);

    // 派发正式指针与拖拽事件，仅使用自有槽位和手部库存，Lua 检查结果。
    private static IEnumerator CaptureFlickPointer(Player player, JObject args, Action<JObject> completed)
    {
        string mode = GameplayTestSystemProbe.Text(args, "mode", "mouse_fast");
        var scope = new FlickPointerScope();
        RegisterCleanup(scope);
        JObject result;
        try
        {
            if (EventSystem.current == null) throw new InvalidOperationException("甩动指针测试缺少 EventSystem。");
            foreach (InventoryFlickTargets panel in scope.PreviousPanels) if (panel != null) panel.enabled = false;
            Inventory source = CreateDragInventory("lua.pointer.source", CreateDragItem("Twine", 6));
            Inventory target = CreateDragInventory("lua.pointer.target", new ItemData[1]);
            Inventory hand = CreateDragInventory(ModText.Hand, new ItemData[1]);
            Inventory_Hand.PlayerHand = hand;
            scope.Root = new GameObject("LuaFlickPointer", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = scope.Root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            Canvas.ForceUpdateCanvases();
            var targetRoot = new GameObject("target", typeof(RectTransform), typeof(BasePanel));
            targetRoot.transform.SetParent(scope.Root.transform, false);
            BasePanel targetPanel = targetRoot.GetComponent<BasePanel>();
            targetPanel.SetGameplayInputBlocking(false);
            targetPanel.Init();
            GameObject prefab = GameRes.ExistingInstance.GetPrefab("UI_Slot", false);
            ItemSlot_UI MakeSlot(Inventory inventory, Transform parent, Vector2 position)
            {
                GameObject instance = Object.Instantiate(prefab, parent, false);
                RectTransform rect = instance.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(100f, 100f);
                rect.position = position;
                Image hit = instance.GetComponent<Image>() ?? instance.AddComponent<Image>();
                hit.raycastTarget = true;
                ItemSlot_UI slot = instance.GetComponent<ItemSlot_UI>();
                inventory.BindSlotUI(slot, 0);
                return slot;
            }
            Vector2 origin = new(-4000f, -4000f);
            ItemSlot_UI sourceSlot = MakeSlot(source, scope.Root.transform, origin);
            Vector2 targetPosition = new(Screen.width * .8f, Screen.height * .5f);
            MakeSlot(target, targetRoot.transform, targetPosition);
            target.SyncQuickTransferTarget(targetPanel);
            Canvas.ForceUpdateCanvases();
            int flicks = 0, taps = 0;
            Vector2 release = Vector2.zero;
            sourceSlot.OnFlickTransfer = (index, start, direction) =>
            {
                flicks++;
                release = start + direction;
                if (!ReferenceEquals(InventoryFlickTargets.Resolve(source, index, start, direction), target))
                    throw new InvalidOperationException("甩动指针夹具只能转入自有目标。");
                source.OnFlickTransfer(index, start, direction);
            };
            sourceSlot.OnDesktopTap = _ => taps++;
            sourceSlot.OnTouchTap = _ => taps++;
            var pointer = new ExtendedPointerEventData(EventSystem.current)
            {
                pointerId = 918274, pointerType = mode == "touch_fast" ? UIPointerType.Touch : UIPointerType.MouseOrPen,
                button = PointerEventData.InputButton.Left, position = origin
            };
            sourceSlot.OnPointerDown(pointer);
            double started = Time.unscaledTimeAsDouble;
            while (Time.unscaledTimeAsDouble - started < .02d) yield return null;
            pointer.position = origin + Vector2.right * 4f;
            sourceSlot.OnBeginDrag(pointer);
            while (Time.unscaledTimeAsDouble - started < .12d) yield return null;
            pointer.position = mode == "mouse_drop" ? targetPosition :
                origin + Vector2.right * (mode == "mouse_short" ? 20f : 2000f);
            sourceSlot.OnDrag(pointer);
            if (mode == "mouse_stopped")
            {
                double stopped = Time.unscaledTimeAsDouble;
                while (Time.unscaledTimeAsDouble - stopped < .16d) yield return null;
            }
            sourceSlot.OnPointerUp(pointer);
            sourceSlot.OnEndDrag(pointer);
            sourceSlot.OnEndDrag(pointer);
            result = new JObject { ["flicks"] = flicks, ["taps"] = taps,
                ["releaseMatched"] = Vector2.Distance(release, pointer.position) < .01f,
                ["sourceAmount"] = source.Data.GetItemSlot(0).itemData?.Stack?.Amount ?? 0f,
                ["targetAmount"] = target.Data.GetItemSlot(0).itemData?.Stack?.Amount ?? 0f,
                ["handAmount"] = hand.Data.GetItemSlot(0).itemData?.Stack?.Amount ?? 0f,
                ["dragLocked"] = source.HasActiveSlotDrag };
        }
        finally { scope.Dispose(); }
        result["handRestored"] = ReferenceEquals(Inventory_Hand.PlayerHand, scope.PreviousHand);
        completed(result);
    }

    private sealed class FlickPointerScope : IDisposable
    {
        public readonly Inventory PreviousHand = Inventory_Hand.PlayerHand;
        public readonly Inventory PreviousLast = Inventory.LastOpenedContainer;
        public readonly InventoryFlickTargets[] PreviousPanels = InventoryFlickTargets.GetActivePanels();
        public GameObject Root;
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Root != null) { Root.SetActive(false); Object.Destroy(Root); }
            Inventory_Hand.PlayerHand = PreviousHand;
            Inventory.LastOpenedContainer = PreviousLast;
            foreach (InventoryFlickTargets panel in PreviousPanels) if (panel != null) panel.enabled = true;
        }
    }
    #endregion
}
