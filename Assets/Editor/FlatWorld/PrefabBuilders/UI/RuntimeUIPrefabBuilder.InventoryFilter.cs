using TMPro;
using UnityEditor;
using UnityEngine;
using static FlatWorldEditorUiLayout;

public static partial class RuntimeUIPrefabBuilder
{
    #region 库存分类复选下拉

    private static void ConfigureInventoryCategoryFilter(GameObject root)
    {
        Transform search = FindTransform(root.transform, "FWUI_BagSearch");
        if (search == null) return;
        Transform categories = FindTransform(root.transform, "FWUI_InventoryCategories");
        if (categories == null)
        {
            const string path = "Assets/2_Prefabs/2-1_UI/Common/Controls/UI_MultiSelectDropdown.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new System.InvalidOperationException("缺少库存分类复选下拉预制体：" + path);
            categories = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, search.parent)).transform;
            categories.name = "FWUI_InventoryCategories";
        }
        RectTransform rect = (RectTransform)categories;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(580f, -39f);
        rect.sizeDelta = new Vector2(224f, 54f);

        Transform emptyTransform = root.transform.Find("FWUI_InventoryFilterEmpty");
        TMP_Text empty = emptyTransform != null ? emptyTransform.GetComponent<TMP_Text>() : null;
        if (empty == null)
            empty = CreateText("FWUI_InventoryFilterEmpty", root.transform, "没有匹配物品", 24f, Cream);
        empty.raycastTarget = false;
        empty.alignment = TextAlignmentOptions.Center;
        empty.enableWordWrapping = false;
        empty.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform emptyRect = empty.rectTransform;
        emptyRect.anchorMin = emptyRect.anchorMax = emptyRect.pivot = new Vector2(0.5f, 0.5f);
        emptyRect.anchoredPosition = new Vector2(0f, -10f);
        emptyRect.sizeDelta = new Vector2(800f, 48f);
        empty.gameObject.SetActive(false);
    }

    #endregion
}
