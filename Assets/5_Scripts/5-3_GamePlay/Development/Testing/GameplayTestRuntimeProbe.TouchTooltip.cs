using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public static partial class GameplayTestRuntimeProbe
{
    #region 手机物品介绍现场
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterTouchTooltipRuntime() => RegisterTouchTooltip();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterTouchTooltipEditor() => RegisterTouchTooltip();
#endif
    private static void RegisterTouchTooltip() => RegisterAsync("inventory", "touch_tooltip", CaptureTouchTooltip);

    // 正式槽位和正式悬浮窗仅使用自有物品，手部库存引用在中断或异常时也恢复。
    private static IEnumerator CaptureTouchTooltip(Player player, JObject args, Action<JObject> completed)
    {
        string mode = GameplayTestSystemProbe.Text(args, "mode", "inspect");
        var scope = new TouchTooltipScope();
        RegisterCleanup(scope);
        JObject result;
        try
        {
            Inventory source = CreateDragInventory("lua.tooltip.source", CreateDragItem("Twine", 6));
            if (mode == "empty") source.Data.SetOne_ItemData(0, null);
            Inventory hand = CreateDragInventory(ModText.Hand, new ItemData[1]);
            if (mode == "held") hand.Data.SetOne_ItemData(0, CreateDragItem("Rope"));
            Inventory_Hand.PlayerHand = hand;
            GameObject prefab = GameRes.ExistingInstance.GetPrefab("UI_Slot", false);
            if (prefab == null || EventSystem.current == null || UIManager.ExistingInstance?.SafeAreaRoot == null)
                throw new InvalidOperationException("手机物品介绍测试缺少正式槽位、EventSystem 或 SafeAreaRoot。");
            scope.Root = new GameObject("LuaTouchTooltip", typeof(RectTransform), typeof(BasePanel));
            scope.Root.transform.SetParent(UIManager.ExistingInstance.SafeAreaRoot, false);
            BasePanel panel = scope.Root.GetComponent<BasePanel>();
            panel.SetGameplayInputBlocking(false);
            panel.Init();
            GameObject slotObject = UnityEngine.Object.Instantiate(prefab, scope.Root.transform, false);
            scope.Slot = slotObject.GetComponent<ItemSlot_UI>();
            source.BindSlotUI(scope.Slot, 0);
            int taps = 0, longPresses = 0, drags = 0;
            scope.Slot.OnTouchTap = _ => taps++;
            scope.Slot.OnTouchLongPress = _ => { longPresses++; return false; };
            scope.Slot.OnMouseDragBegin = _ => { drags++; return null; };
            var pointer = new ExtendedPointerEventData(EventSystem.current)
            {
                pointerId = 918273, pointerType = UIPointerType.Touch,
                button = PointerEventData.InputButton.Left,
                position = new Vector2(Screen.width * .4f, Screen.height * .6f)
            };
            scope.Slot.OnPointerEnter(pointer);
            double started = Time.unscaledTimeAsDouble;
            scope.Slot.OnPointerDown(pointer);
            while (Time.unscaledTimeAsDouble - started < .2d) yield return null;
            bool visibleEarly = InventoryItemTooltip.IsTouchPinned(scope.Slot);
            if (mode == "hand_changed") hand.Data.SetOne_ItemData(0, CreateDragItem("Rope"));
            if (mode == "source_changed") source.Data.SetOne_ItemData(0, CreateDragItem("Rope", 6));
            if (mode == "moved")
            {
                pointer.position += new Vector2(Screen.width, 0);
                scope.Slot.OnPointerMove(pointer);
            }
            if (mode == "early_release") scope.Slot.OnPointerUp(pointer);
            if (mode == "disabled") scope.Root.SetActive(false);
            while (Time.unscaledTimeAsDouble - started < 1.15d) yield return null;
            bool opened = InventoryItemTooltip.IsTouchPinned(scope.Slot);
            if (mode == "inspect") scope.Slot.OnBeginDrag(pointer);
            scope.Slot.OnPointerUp(pointer);
            scope.Slot.OnPointerExit(pointer);
            bool persisted = InventoryItemTooltip.IsTouchPinned(scope.Slot);
            if (mode == "closed_after_show") { panel.Close(); yield return null; }
            bool closedWithPanel = !InventoryItemTooltip.IsTouchPinned(scope.Slot);
            InventoryItemTooltip.DismissTouchAt(new Vector2(-1000f, -1000f));
            result = new JObject { ["visibleEarly"] = visibleEarly, ["opened"] = opened,
                ["persistedAfterRelease"] = persisted, ["dismissedByOutsidePress"] = !InventoryItemTooltip.IsTouchPinned(scope.Slot),
                ["closedWithPanel"] = closedWithPanel,
                ["taps"] = taps, ["longPresses"] = longPresses, ["drags"] = drags,
                ["sourceAmount"] = source.Data.GetItemSlot(0).itemData?.Stack?.Amount ?? 0f };
        }
        finally { scope.Dispose(); }
        result["originalHandRestored"] = ReferenceEquals(Inventory_Hand.PlayerHand, scope.OriginalHand);
        completed(result);
    }

    private sealed class TouchTooltipScope : IDisposable
    {
        public readonly Inventory OriginalHand = Inventory_Hand.PlayerHand;
        public GameObject Root;
        public ItemSlot_UI Slot;
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Inventory_Hand.PlayerHand = OriginalHand;
            if (Slot != null) InventoryItemTooltip.Hide(Slot);
            if (Root != null) { Root.SetActive(false); UnityEngine.Object.Destroy(Root); }
        }
    }
    #endregion
}
