#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static partial class GameplayTestSystemProbe
{
    #region 库存分类并集与真实索引投影采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterInventoryCategoriesRuntime() => RegisterInventoryCategoriesRules();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterInventoryCategoriesEditor() => RegisterInventoryCategoriesRules();
#endif
    private static void RegisterInventoryCategoriesRules()
    {
        Register("ui_settings", "inventory_category_filter", args => new JObject
        {
            ["matches"] = InventoryCategoryFilter.MatchesAny(
                args["tags"]?.ToObject<List<string>>(), args["selected"]?.ToObject<List<string>>())
        });
        Register("ui_settings", "inventory_filter_projection", CaptureInventoryFilterProjection);
    }

    private static JObject CaptureInventoryFilterProjection(JObject args)
    {
        // 自有停用对象，不创建世界、不读取 GameRes、不修改玩家库存或当前 UI。
        var root = new GameObject("[LuaInventoryCategoryFilter]", typeof(RectTransform));
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

            ItemData Item(params string[] tags) => new Data_GeneralItem
            {
                IDName = string.Empty, Tags = new List<string>(tags), Stack = new ItemStack { Amount = 1 }
            };
            var slots = new List<ItemSlot>();
            for (int i = 0; i < 41; i++) slots.Add(new ItemSlot(i));
            slots[2].itemData = Item("Tool");
            slots[8].itemData = Item("Tool", "InventoryGroup.MechanicalPower");
            slots[15].itemData = Item("InventoryGroup.MechanicalPower");
            slots[26].itemData = Item("Tool", "InventoryGroup.MechanicalPower");
            ItemData original8 = slots[8].itemData;
            var inventory = new VirtualizedInventoryProbe { Data = new Inventory_Data(slots, "filter-probe") };
            InventoryVirtualizedSlotGrid grid = root.AddComponent<InventoryVirtualizedSlotGrid>();
            grid.Bind(inventory, content, template.gameObject, null);
            var samples = new JArray();

            void Sample(string name)
            {
                var active = new List<ItemSlot_UI>();
                foreach (ItemSlot_UI slot in grid.PooledSlots)
                    if (slot.gameObject.activeSelf) active.Add(slot);
                active.Sort((a, b) => a.slotIndex.CompareTo(b.slotIndex));
                var indices = new JArray();
                bool routed = true, dense = true, mapping = true;
                for (int view = 0; view < active.Count; view++)
                {
                    ItemSlot_UI slot = active[view];
                    indices.Add(slot.slotIndex);
                    slot.OnDesktopTap(slot.slotIndex);
                    slot.OnTouchTap(slot.slotIndex);
                    routed &= inventory.LastDesktopIndex == slot.slotIndex && inventory.LastTouchIndex == slot.slotIndex
                        && ReferenceEquals(slot.GetBoundSlotData(), slots[slot.slotIndex]);
                    dense &= ((RectTransform)slot.transform).anchoredPosition == new Vector2(view % 3 * 24f, -(view / 3) * 24f);
                }
                for (int i = 0; i < inventory.itemSlot_UI.Count; i++)
                    mapping &= inventory.itemSlot_UI[i] == null
                        ? !active.Exists(slot => slot.slotIndex == i)
                        : inventory.itemSlot_UI[i].slotIndex == i && inventory.itemSlot_UI[i].gameObject.activeSelf;
                samples.Add(new JObject
                {
                    ["name"] = name, ["indices"] = indices, ["count"] = grid.VisibleSlotCount,
                    ["routed"] = routed, ["dense"] = dense, ["mapping"] = mapping,
                    ["poolCount"] = grid.PooledSlots.Count, ["scrollY"] = content.anchoredPosition.y
                });
            }

            grid.SetFilter(item => item != null && InventoryCategoryFilter.MatchesAny(item.Tags, new[] { "tool" }));
            Sample("tools");
            grid.SetFilter(item => item != null && InventoryCategoryFilter.MatchesAny(item.Tags, new[] { "tool", "mechanical" }));
            Sample("union");
            bool filterPreservedData = slots.Count == 41 && ReferenceEquals(slots[8].itemData, original8);
            slots[39].itemData = Item("Tool", "InventoryGroup.MechanicalPower");
            grid.RefreshSlot(39);
            grid.RefreshVisibleSlots();
            Sample("hiddenSlotAdded");
            slots[8].itemData = null;
            grid.RefreshSlot(8);
            grid.RefreshVisibleSlots();
            Sample("matchingSlotRemoved");
            grid.SetFilter(_ => false);
            Sample("empty");
            grid.SetFilter(null);
            Sample("cleared");
            grid.Unbind();
            return new JObject { ["samples"] = samples, ["filterPreservedData"] = filterPreservedData };
        }
        finally { Object.DestroyImmediate(root); }
    }

    #endregion
}
#endif
