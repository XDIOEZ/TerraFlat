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
    #region 装备窗口快捷转移现场
    // 使用正式装备窗口生命周期，不由夹具手动登记目标，防止漏接打开入口却让测试通过。
    private static JObject CaptureInventoryEquipmentQuickTransfer(Player player, JObject args)
    {
        string mode = GameplayTestSystemProbe.Text(args, "mode", "shift");
        Inventory previousLast = Inventory.LastOpenedContainer;
        InventoryFlickTargets[] previousPanels = InventoryFlickTargets.GetActivePanels();
        Inventory source = CreateDragInventory(ModText.Hotbar,
            CreateDragItem(mode == "invalid" ? "Twine" : "Spacesuit_Head"));
        var equipment = new Inventory_Equipment
        {
            InventoryPanel_Prefab = GameRes.ExistingInstance.GetPrefab("UI_Equipment")
        };
        equipment.EnsureSlotSchema();
        int head = equipment.GetSlotIndex(EquipmentSlotCatalog.Head);
        var result = new JObject();
        try
        {
            foreach (InventoryFlickTargets registered in previousPanels)
                if (registered != null) registered.enabled = false;
            equipment.EnsurePanelCreated();
            equipment.basePanel.Open();
            result["registeredOnOpen"] = ReferenceEquals(Inventory.LastOpenedContainer, equipment);
            equipment.basePanel.Close();
            result["releasedOnClose"] = !ReferenceEquals(Inventory.LastOpenedContainer, equipment) &&
                !source.HasVisibleFlickTarget();
            if (mode != "closed") equipment.basePanel.Open();
            if (mode == "full") equipment.Data.SetOne_ItemData(head, CreateDragItem("Spacesuit_Head"));
            result["sourceBefore"] = CaptureDragSlot(source, 0);
            result["targetBefore"] = CaptureDragSlot(equipment, head);
            Canvas.ForceUpdateCanvases();
            Vector2 target = RectTransformUtility.WorldToScreenPoint(null, equipment.itemSlot_UI[head].transform.position);
            Vector2 origin = target + Vector2.down * 200f;
            Vector2 direction = target - origin;
            result["flickSelected"] = ReferenceEquals(
                InventoryFlickTargets.Resolve(source, 0, origin, direction), equipment);
            if (mode == "flick") source.OnFlickTransfer(0, origin, direction);
            else source.OnShiftQuickTransfer(0);
            result["sourceAfter"] = CaptureDragSlot(source, 0);
            result["targetAfter"] = CaptureDragSlot(equipment, head);
            return result;
        }
        finally
        {
            equipment.ReleaseRuntimePanel();
            Inventory.LastOpenedContainer = previousLast;
            foreach (InventoryFlickTargets registered in previousPanels)
                if (registered != null) registered.enabled = true;
        }
    }
    #endregion

    #region 方向甩动独立库存现场
    // 临时正式面板只提供真实屏幕坐标，选择和提交均调用生产接口，业务期望由 Lua 持有。
    private static JObject CaptureInventoryDirectionalFlick(Player player, JObject args)
    {
        string mode = GameplayTestSystemProbe.Text(args, "mode", "right");
        if (mode.StartsWith("window_", StringComparison.Ordinal)) return CaptureInventoryFlickWindow(mode);
        using var settings = new GameplayTestInventoryFlickSettingsScope();
        settings.Apply(args);
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
            bool vertical = mode == "down" || mode == "diagonal" || mode == "same_panel" || mode == "full_outside_fallback";
            BasePanel downPanel = MakePanel(down, vertical ? new Vector2(origin.x, Screen.height * .18f) :
                new Vector2(Screen.width * .65f, Screen.height * .70f));
            Vector2 direction = mode == "down" || mode == "same_panel" ? Vector2.down :
                mode == "diagonal" ? new Vector2(.1f, -1f) : Vector2.right;
            direction *= 120f;
            if (mode == "partial") right.Data.itemSlots[0].SlotMaxVolume = 2;
            if (mode == "full" || mode == "none" || mode == "full_outside_fallback") right.Data.itemSlots[0].SlotMaxVolume = 0;
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

    // 正式 WindowRect 提供整窗边界，槽映射和裁剪仍通过生产候选接口读取。
    private static JObject CaptureInventoryFlickWindow(string mode)
    {
        var scope = new FlickPointerScope();
        try
        {
            scope.Settings.Apply(new JObject());
            foreach (InventoryFlickTargets registered in scope.PreviousPanels)
                if (registered != null) registered.enabled = false;
            Inventory source = CreateDragInventory("lua.window.source", CreateDragItem("Twine", 6));
            bool multi = mode == "window_multi_gap" || mode == "window_multi_right";
            Inventory target = CreateDragInventory("lua.window.target", new ItemData[multi ? 2 : 1]);
            Inventory second = CreateDragInventory("lua.window.second", new ItemData[1]);
            scope.Root = new GameObject("LuaFlickWindow", typeof(RectTransform), typeof(Canvas));
            scope.Root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.ForceUpdateCanvases();
            var panelObject = new GameObject("fullScreenOwner", typeof(RectTransform), typeof(BasePanel));
            panelObject.transform.SetParent(scope.Root.transform, false);
            var panelRect = (RectTransform)panelObject.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
            BasePanel panel = panelObject.GetComponent<BasePanel>();
            panel.SetGameplayInputBlocking(false);
            panel.Init();
            float unit = Mathf.Min(Screen.width, Screen.height) / 1000f;
            RectTransform MakeRect(string name, Transform parent, Vector2 position, Vector2 size)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rect = (RectTransform)go.transform;
                rect.sizeDelta = size * unit;
                rect.position = position * unit;
                return rect;
            }
            RectTransform MakeWindow(Vector2 position, Vector2 size)
            {
                RectTransform rect = MakeRect("window", panelObject.transform, position, size);
                RectTransform surface = MakeRect("dragSurface", rect, position, size);
                // MaskableGraphic 在 RectMask2D 裁剪时必须有真实 CanvasRenderer。
                surface.gameObject.AddComponent<CanvasRenderer>();
                surface.gameObject.AddComponent<UIWindowDragSurface>().Configure(rect);
                return rect;
            }
            ItemSlot_UI MakeSlot(Inventory inventory, int index, RectTransform window, Vector2 position)
            {
                RectTransform rect = MakeRect("slot", window, position, new Vector2(20f, 20f));
                ItemSlot_UI slot = rect.gameObject.AddComponent<ItemSlot_UI>();
                slot.InitializeSlot(index, inventory.Data.GetItemSlot, _ => { });
                inventory.itemSlot_UI.Add(slot);
                return slot;
            }
            Vector2 origin = new Vector2(100f, 100f) * unit;
            Vector2 destination = new Vector2(850f, 300f) * unit;
            if (multi)
            {
                origin = new Vector2(500f, 50f) * unit;
                RectTransform left = MakeWindow(new Vector2(150f, 650f), new Vector2(100f, 200f));
                RectTransform right = MakeWindow(new Vector2(850f, 650f), new Vector2(100f, 200f));
                MakeSlot(target, 0, left, new Vector2(150f, 650f));
                MakeSlot(target, 1, right, new Vector2(850f, 650f));
                destination = new Vector2(mode == "window_multi_right" ? 850f : 500f, 650f) * unit;
            }
            else
            {
                RectTransform window = MakeWindow(new Vector2(600f, 600f), new Vector2(600f, 600f));
                ItemSlot_UI slot = MakeSlot(target, 0, window, new Vector2(600f, 850f));
                if (mode == "window_shared_inputs") MakeSlot(second, 0, window, new Vector2(850f, 350f));
                if (mode == "window_outside") destination = origin + Vector2.left * 200f * unit;
                if (mode == "window_stale") slot.InitializeSlot(0, _ => second.Data.GetItemSlot(0), _ => { });
                if (mode == "window_hidden") window.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
                if (mode == "window_mask_clip")
                {
                    RectTransform viewport = MakeRect("viewport", panelObject.transform, new Vector2(700f, 700f), new Vector2(400f, 400f));
                    viewport.gameObject.AddComponent<RectMask2D>();
                    window.SetParent(viewport, true);
                }
                if (mode == "window_screen_clip")
                {
                    window.position = new Vector2(100f, 600f) * unit;
                    window.sizeDelta = new Vector2(800f, 600f) * unit;
                    slot.transform.position = new Vector2(200f, 850f) * unit;
                    destination = new Vector2(-300f, 300f) * unit;
                }
            }
            target.SyncQuickTransferTarget(panel);
            if (mode == "window_shared_inputs") second.SyncQuickTransferTarget(panel);
            Canvas.ForceUpdateCanvases();
            Vector2 direction = destination - origin;
            bool visible = source.HasVisibleFlickTarget();
            bool inDirection = source.HasVisibleFlickTarget(origin, direction);
            Inventory selected = InventoryFlickTargets.Resolve(source, 0, origin, direction);
            if (selected != null && !ReferenceEquals(selected, target) && !ReferenceEquals(selected, second))
                throw new InvalidOperationException("窗口方向夹具只能转入自有目标。");
            source.OnFlickTransfer(0, origin, direction);
            float targetAmount = 0f;
            foreach (ItemSlot slot in target.Data.itemSlots) targetAmount += slot.itemData?.Stack?.Amount ?? 0f;
            return new JObject { ["visible"] = visible, ["inDirection"] = inDirection,
                ["selected"] = selected == null ? "none" : ReferenceEquals(selected, target) ? "target" : "second",
                ["sourceAmount"] = source.Data.GetItemSlot(0).itemData?.Stack?.Amount ?? 0f,
                ["targetAmount"] = targetAmount, ["secondAmount"] = second.Data.GetItemSlot(0).itemData?.Stack?.Amount ?? 0f };
        }
        finally { scope.Dispose(); }
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
            scope.Settings.Apply(args);
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
            Vector2 targetPosition = new(Screen.width * .8f, Screen.height * .5f);
            Vector2 origin = new(-4000f, targetPosition.y);
            ItemSlot_UI sourceSlot = MakeSlot(source, scope.Root.transform, origin);
            MakeSlot(target, targetRoot.transform, targetPosition);
            target.SyncQuickTransferTarget(targetPanel);
            if (mode == "mouse_no_target" || mode == "touch_no_target" || mode == "mouse_output_only" || mode == "mouse_self_only")
                target.SyncQuickTransferTarget(targetPanel, allowDirectionalDeposit: false);
            if (mode == "mouse_self_only")
            {
                sourceSlot.transform.SetParent(targetRoot.transform, true);
                sourceSlot.transform.position = targetPosition;
                source.SyncQuickTransferTarget(targetPanel);
            }
            if (mode == "mouse_hand_only") target.Data.Name = ModText.Hand;
            if (mode == "mouse_full_target" || mode == "mouse_full_wrong_way") target.Data.GetItemSlot(0).SlotMaxVolume = 0;
            if (mode == "mouse_closed_target" || mode == "mouse_open_during_drag") targetPanel.Close();
            if (mode == "mouse_hidden_target") targetPanel.canvasGroup.alpha = 0f;
            if (mode == "mouse_disabled_target") targetRoot.SetActive(false);
            Canvas.ForceUpdateCanvases();
            int flicks = 0, taps = 0;
            Vector2 release = Vector2.zero;
            sourceSlot.OnFlickTransfer = (index, start, direction) =>
            {
                flicks++;
                release = start + direction;
                Inventory selected = InventoryFlickTargets.Resolve(source, index, start, direction);
                if (selected != null && !ReferenceEquals(selected, target))
                    throw new InvalidOperationException("甩动指针夹具只能转入自有目标。");
                source.OnFlickTransfer(index, start, direction);
            };
            sourceSlot.OnDesktopTap = _ => taps++;
            sourceSlot.OnTouchTap = _ => taps++;
            var pointer = new ExtendedPointerEventData(EventSystem.current)
            {
                pointerId = 918274, pointerType = mode.StartsWith("touch_", StringComparison.Ordinal) ? UIPointerType.Touch : UIPointerType.MouseOrPen,
                button = PointerEventData.InputButton.Left, position = origin
            };
            sourceSlot.OnPointerDown(pointer);
            double started = Time.unscaledTimeAsDouble;
            while (Time.unscaledTimeAsDouble - started < .02d) yield return null;
            pointer.position = origin + Vector2.right * 4f;
            sourceSlot.OnBeginDrag(pointer);
            if (mode == "mouse_close_during_drag" || mode == "touch_close_during_drag") targetPanel.Close();
            if (mode == "mouse_open_during_drag") targetPanel.Open();
            while (Time.unscaledTimeAsDouble - started < .12d) yield return null;
            Vector2 releaseDirection = mode.EndsWith("wrong_way", StringComparison.Ordinal) ? Vector2.left :
                mode.EndsWith("sideways", StringComparison.Ordinal) ? Vector2.up :
                mode.EndsWith("edge_in", StringComparison.Ordinal) ? new Vector2(Mathf.Cos(9f * Mathf.Deg2Rad), Mathf.Sin(9f * Mathf.Deg2Rad)) :
                mode.EndsWith("edge_out", StringComparison.Ordinal) ? new Vector2(Mathf.Cos(12f * Mathf.Deg2Rad), Mathf.Sin(12f * Mathf.Deg2Rad)) : Vector2.right;
            pointer.position = mode == "mouse_drop" ? targetPosition :
                origin + releaseDirection * (mode == "mouse_short" ? 20f : 2000f);
            sourceSlot.OnDrag(pointer);
            if (mode == "mouse_stopped")
            {
                double stopped = Time.unscaledTimeAsDouble;
                while (Time.unscaledTimeAsDouble - stopped < .16d) yield return null;
            }
            bool hasTargetBeforeRelease = source.HasVisibleFlickTarget();
            bool hasTargetInDirection = source.HasVisibleFlickTarget(origin, pointer.position - origin);
            sourceSlot.OnPointerUp(pointer);
            sourceSlot.OnEndDrag(pointer);
            sourceSlot.OnEndDrag(pointer);
            result = new JObject { ["flicks"] = flicks, ["taps"] = taps,
                ["hasTargetBeforeRelease"] = hasTargetBeforeRelease,
                ["hasTargetInDirection"] = hasTargetInDirection,
                ["releaseMatched"] = Vector2.Distance(release, pointer.position) < .01f,
                ["sourceAmount"] = source.Data.GetItemSlot(0).itemData?.Stack?.Amount ?? 0f,
                ["targetAmount"] = target.Data.GetItemSlot(0).itemData?.Stack?.Amount ?? 0f,
                ["handAmount"] = hand.Data.GetItemSlot(0).itemData?.Stack?.Amount ?? 0f,
                ["dragLocked"] = source.HasActiveSlotDrag };
        }
        finally { scope.Dispose(); }
        result["handRestored"] = ReferenceEquals(Inventory_Hand.PlayerHand, scope.PreviousHand);
        result["preferencesRestored"] = scope.Settings.IsRestored;
        completed(result);
    }

    private sealed class FlickPointerScope : IDisposable
    {
        public readonly GameplayTestInventoryFlickSettingsScope Settings = new();
        public readonly Inventory PreviousHand = Inventory_Hand.PlayerHand;
        public readonly Inventory PreviousLast = Inventory.LastOpenedContainer;
        public readonly InventoryFlickTargets[] PreviousPanels = InventoryFlickTargets.GetActivePanels();
        public GameObject Root;
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (Root != null) { Root.SetActive(false); Object.Destroy(Root); }
                Inventory_Hand.PlayerHand = PreviousHand;
                Inventory.LastOpenedContainer = PreviousLast;
                foreach (InventoryFlickTargets panel in PreviousPanels) if (panel != null) panel.enabled = true;
            }
            finally { Settings.Dispose(); }
        }
    }
    #endregion
}
