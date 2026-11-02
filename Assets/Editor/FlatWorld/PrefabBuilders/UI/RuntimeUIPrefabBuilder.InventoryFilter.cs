using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class RuntimeUIPrefabBuilder
{
    #region 库存左侧单选分类栏

    internal static void ConfigureInventoryCategoryFilter(GameObject root)
    {
        Transform search = FindTransform(root.transform, "FWUI_BagSearch");
        if (search == null) return;

        // 左侧增加分类栏宽度，原十列物品网格与右侧滚动条保持原尺寸。
        RectTransform panel = (RectTransform)root.transform;
        panel.sizeDelta = new Vector2(Mathf.Max(1148f, panel.sizeDelta.x), panel.sizeDelta.y);
        UIDragResizer resizer = root.GetComponent<UIDragResizer>();
        if (resizer != null)
        {
            resizer.minWidth = Mathf.Max(1148f, resizer.minWidth);
            resizer.minHeight = Mathf.Max(560f, resizer.minHeight);
        }
        RectTransform scroll = FindTransform(root.transform, "Scroll View") as RectTransform;
        if (scroll == null) throw new MissingReferenceException("UI_Bag 缺少 Scroll View。");
        scroll.anchoredPosition = new Vector2(192f, -112f);
        scroll.sizeDelta = new Vector2(-240f, -192f);

        Transform oldDropdown = FindTransform(root.transform, "FWUI_InventoryCategories");
        if (oldDropdown != null)
            Object.DestroyImmediate(oldDropdown.gameObject);

        Transform tabs = FindTransform(root.transform, "FWUI_InventoryCategoryTabs");
        if (tabs == null)
            tabs = CreateUIObject("FWUI_InventoryCategoryTabs", root.transform).transform;
        RectTransform rect = (RectTransform)tabs;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(26f, -112f);
        rect.sizeDelta = new Vector2(140f, -192f);

        VerticalLayoutGroup layout = tabs.GetComponent<VerticalLayoutGroup>() ??
                                     tabs.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 4, 4);
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        const string tabPath = "Assets/2_Prefabs/2-1_UI/Common/Controls/UI_TabButton.prefab";
        GameObject tabPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(tabPath);
        if (tabPrefab == null) throw new MissingReferenceException("缺少库存分类按钮：" + tabPath);
        EnsureInventoryCategoryTab(tabs, tabPrefab, "all", "全部分类", 0);
        int index = 1;
        foreach (InventoryItemCategoryCatalog.Category category in InventoryItemCategoryCatalog.Categories)
            EnsureInventoryCategoryTab(tabs, tabPrefab, category.Id, category.Label, index++);

        TMP_Text empty = FindTransform(root.transform, "FWUI_InventoryFilterEmpty")?.GetComponent<TMP_Text>();
        if (empty == null)
            empty = CreateText("FWUI_InventoryFilterEmpty", root.transform, "没有匹配物品", 24f, Cream);
        empty.raycastTarget = false;
        empty.alignment = TextAlignmentOptions.Center;
        empty.enableWordWrapping = false;
        empty.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform emptyRect = empty.rectTransform;
        emptyRect.anchorMin = emptyRect.anchorMax = emptyRect.pivot = new Vector2(0.5f, 0.5f);
        emptyRect.anchoredPosition = new Vector2(72f, -10f);
        emptyRect.sizeDelta = new Vector2(800f, 48f);
        empty.gameObject.SetActive(false);
    }

    private static void EnsureInventoryCategoryTab(Transform parent, GameObject prefab, string key, string caption, int index)
    {
        string name = "FWUI_Category_" + key;
        Transform existing = parent.Find(name);
        GameObject tab = existing != null ? existing.gameObject :
            (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        tab.name = name;
        tab.transform.SetSiblingIndex(index);
        RectTransform rect = (RectTransform)tab.transform;
        rect.sizeDelta = new Vector2(140f, 36f);
        LayoutElement element = tab.GetComponent<LayoutElement>() ?? tab.AddComponent<LayoutElement>();
        element.preferredWidth = 140f;
        element.preferredHeight = 36f;
        element.flexibleHeight = 0f;
        Button button = tab.GetComponent<Button>();
        if (button != null) button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = caption;
            label.fontSize = 17f;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    #endregion
}
