#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static partial class GameplayTestSystemProbe
{
    #region 虚拟库存滚动与回调复用采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterVirtualizedInventoryRuntime() => RegisterVirtualizedInventoryRules();

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterVirtualizedInventoryEditor() => RegisterVirtualizedInventoryRules();
#endif

    private static void RegisterVirtualizedInventoryRules()
        => Register("ui_settings", "virtualized_inventory", CaptureVirtualizedInventory);

    private sealed class VirtualizedInventoryProbe : Inventory
    {
        public int LastDesktopIndex = -1, LastTouchIndex = -1;
        public override void OnDesktopTap(int index) => LastDesktopIndex = index;
        public override void OnTouchTap(int index) => LastTouchIndex = index;
    }

    private static JObject CaptureVirtualizedInventory(JObject args)
    {
        // 停用的独立层级只驱动正式滚动回调，不注册玩家、输入或 UIManager。
        var root = new GameObject("[LuaVirtualizedInventory]", typeof(RectTransform));
        root.SetActive(false);
        try
        {
            RectTransform Rect(string name, Transform parent, Vector2 size)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = size;
                return rect;
            }

            RectTransform scrollRoot = Rect("Scroll", root.transform, new Vector2(72f, 48f));
            ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            RectTransform viewport = Rect("Viewport", scrollRoot, new Vector2(72f, 48f));
            RectTransform content = Rect("Content", viewport, new Vector2(72f, 0f));
            scroll.viewport = viewport;
            scroll.content = content;
            GridLayoutGroup layout = content.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(20f, 20f);
            layout.spacing = new Vector2(4f, 4f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;
            content.gameObject.AddComponent<ContentSizeFitter>();

            RectTransform template = Rect("SlotTemplate", root.transform, new Vector2(20f, 20f));
            template.gameObject.SetActive(false);
            ItemSlot_UI templateSlot = template.gameObject.AddComponent<ItemSlot_UI>();
            templateSlot.image = Rect("Icon", template, Vector2.one).gameObject.AddComponent<Image>();
            templateSlot.text = Rect("Amount", template, Vector2.one).gameObject.AddComponent<TextMeshProUGUI>();
            var slots = new List<ItemSlot>();
            for (int i = 0; i < 41; i++) slots.Add(new ItemSlot(i));
            var inventory = new VirtualizedInventoryProbe { Data = new Inventory_Data(slots, "virtualized-probe") };
            InventoryVirtualizedSlotGrid grid = root.AddComponent<InventoryVirtualizedSlotGrid>();
            grid.Bind(inventory, content, template.gameObject, null);
            int initialPoolCount = grid.PooledSlots.Count;
            var getters = new Func<int, ItemSlot>[initialPoolCount];
            var clearers = new Action<int>[initialPoolCount];
            var desktop = new Action<int>[initialPoolCount];
            var drag = new Func<int, InventoryDragTransaction>[initialPoolCount];
            for (int i = 0; i < initialPoolCount; i++)
            {
                ItemSlot_UI slot = grid.PooledSlots[i];
                getters[i] = slot.GetSlotDataFunc;
                clearers[i] = slot.ClearSlotDataAction;
                desktop[i] = slot.OnDesktopTap;
                drag[i] = slot.OnMouseDragBegin;
            }

            Dictionary<int, ItemSlot_UI> Snapshot()
            {
                var map = new Dictionary<int, ItemSlot_UI>();
                foreach (ItemSlot_UI slot in grid.PooledSlots)
                    if (slot.gameObject.activeSelf) map.Add(slot.slotIndex, slot);
                return map;
            }

            var samples = new JArray();
            void ScrollTo(float row)
            {
                Dictionary<int, ItemSlot_UI> before = Snapshot();
                content.anchoredPosition = new Vector2(0f, row * 24f);
                scroll.onValueChanged.Invoke(Vector2.zero);
                Dictionary<int, ItemSlot_UI> after = Snapshot();
                int retained = 0, entering = 0;
                bool mappingValid = true, callbacksStable = true, routed = true;
                for (int i = 0; i < inventory.itemSlot_UI.Count; i++)
                {
                    ItemSlot_UI mapped = inventory.itemSlot_UI[i];
                    mappingValid &= after.TryGetValue(i, out ItemSlot_UI expected)
                        ? ReferenceEquals(mapped, expected) : mapped == null;
                }
                for (int i = 0; i < grid.PooledSlots.Count; i++)
                {
                    ItemSlot_UI slot = grid.PooledSlots[i];
                    callbacksStable &= ReferenceEquals(getters[i], slot.GetSlotDataFunc)
                        && ReferenceEquals(clearers[i], slot.ClearSlotDataAction)
                        && ReferenceEquals(desktop[i], slot.OnDesktopTap)
                        && ReferenceEquals(drag[i], slot.OnMouseDragBegin);
                    if (!slot.gameObject.activeSelf) continue;
                    if (before.TryGetValue(slot.slotIndex, out ItemSlot_UI previous))
                    {
                        if (ReferenceEquals(previous, slot)) retained++;
                    }
                    else entering++;
                    routed &= ReferenceEquals(slot.GetBoundSlotData(), slots[slot.slotIndex])
                        && ReferenceEquals(slot.GetSlotDataFunc(-1), slots[slot.slotIndex]);
                    slot.OnDesktopTap(slot.slotIndex);
                    slot.OnTouchTap(slot.slotIndex);
                    routed &= inventory.LastDesktopIndex == slot.slotIndex && inventory.LastTouchIndex == slot.slotIndex;
                    Vector2 position = ((RectTransform)slot.transform).anchoredPosition;
                    mappingValid &= position == new Vector2(slot.slotIndex % 3 * 24f, -slot.slotIndex / 3 * 24f);
                }
                var indices = new List<int>(after.Keys);
                indices.Sort();
                samples.Add(new JObject
                {
                    ["row"] = row, ["indices"] = new JArray(indices), ["active"] = after.Count,
                    ["retained"] = retained, ["entering"] = entering, ["mappingValid"] = mappingValid,
                    ["callbacksStable"] = callbacksStable, ["routed"] = routed,
                    ["poolCount"] = grid.PooledSlots.Count
                });
            }

            ScrollTo(2f);
            ScrollTo(2.5f);
            ScrollTo(0f);
            ScrollTo(10f);
            ScrollTo(13f);
            ScrollTo(0f);
            ScrollTo(2f);
            ItemSlot_UI rebound = inventory.itemSlot_UI[12];
            int clearedIndex = -1;
            inventory.Data.Event_RefreshUI += index => clearedIndex = index;
            rebound.ClearSlotDataAction(-1);
            inventory.RebindVirtualSlotUI(rebound, -1);
            inventory.RebindVirtualSlotUI(rebound, 41);
            bool invalidUnchanged = rebound.slotIndex == 12 && ReferenceEquals(inventory.itemSlot_UI[12], rebound);
            grid.RefreshVisibleSlots();
            bool forcedRefreshStable = ReferenceEquals(getters[0], rebound.GetSlotDataFunc)
                && ReferenceEquals(desktop[0], rebound.OnDesktopTap);
            grid.Unbind();
            bool mapCleared = inventory.itemSlot_UI.TrueForAll(slot => slot == null);
            viewport.sizeDelta = new Vector2(72f, 96f);
            grid.Bind(inventory, content, template.gameObject, null);
            int expandedPoolCount = grid.PooledSlots.Count;
            bool expandedMappingValid = true;
            foreach (ItemSlot_UI slot in grid.PooledSlots)
                if (slot.gameObject.activeSelf)
                    expandedMappingValid &= ReferenceEquals(inventory.itemSlot_UI[slot.slotIndex], slot)
                        && ReferenceEquals(slot.GetBoundSlotData(), slots[slot.slotIndex]);
            grid.Unbind();
            return new JObject
            {
                ["poolCount"] = initialPoolCount, ["samples"] = samples, ["clearedIndex"] = clearedIndex,
                ["invalidUnchanged"] = invalidUnchanged, ["forcedRefreshStable"] = forcedRefreshStable,
                ["mapCleared"] = mapCleared, ["expandedPoolCount"] = expandedPoolCount,
                ["expandedMappingValid"] = expandedMappingValid,
                ["layoutRestored"] = layout.enabled && content.GetComponent<ContentSizeFitter>().enabled
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    #endregion
}
#endif
