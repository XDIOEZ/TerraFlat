#if UNITY_EDITOR
using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class GameplayTestSystemProbe
{
    #region 背包尺寸与安全区布局采样

    [InitializeOnLoadMethod]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterInventoryBagWindowLayoutRules() =>
        Register("ui_settings", "inventory_bag_window_layout", CaptureInventoryBagWindowLayout);

    // 正式资产的正文边界与独立安全区缩放分开采样，不打开或绑定玩家背包。
    private static JObject CaptureInventoryBagWindowLayout(JObject args)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Panels/UI_Bag.prefab");
        if (prefab == null) throw new InvalidOperationException("背包正式 Prefab 缺失。");
        var fixture = new GameObject("[LuaInventoryBagLayout]", typeof(RectTransform));
        var scaleFixture = new GameObject("[LuaInventoryBagScale]", typeof(RectTransform));
        fixture.SetActive(false);
        scaleFixture.SetActive(false);
        try
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab, fixture.transform, false);
            RectTransform root = (RectTransform)instance.transform;
            UIDragResizer resizer = instance.GetComponent<UIDragResizer>();
            SafeAreaScaleGroup sourceScale = instance.GetComponent<SafeAreaScaleGroup>();
            if (resizer == null || sourceScale == null)
                throw new InvalidOperationException("背包缺少拖边尺寸或安全区缩放配置。");
            var scaleConfig = new SerializedObject(sourceScale);
            SerializedProperty targets = scaleConfig.FindProperty("scaledTargets");
            bool scalesRoot = scaleConfig.FindProperty("boundsTarget").objectReferenceValue == root &&
                targets.arraySize == 1 && targets.GetArrayElementAtIndex(0).objectReferenceValue == root;
            Vector2 margin = scaleConfig.FindProperty("safeMargin").vector2Value;

            float width = (float)Number(args, "width", 1920);
            float height = (float)Number(args, "height", 1080);
            float userScale = (float)Number(args, "scale", 1);
            float canvasScale = Mathf.Min(width / 1920f, height / 1080f) * userScale;
            RectTransform available = (RectTransform)scaleFixture.transform;
            available.sizeDelta = new Vector2(
                (width - (float)Number(args, "insetX", 0)) / canvasScale,
                (height - (float)Number(args, "insetY", 0)) / canvasScale);
            RectTransform scaled = new GameObject("FormalBagBounds", typeof(RectTransform)).GetComponent<RectTransform>();
            scaled.SetParent(available, false);
            scaled.localScale = prefab.transform.localScale;
            SafeAreaScaleGroup scale = scaled.gameObject.AddComponent<SafeAreaScaleGroup>();
            scale.Configure(scaled, new[] { scaled }, margin,
                scaleConfig.FindProperty("presentationScale").floatValue);
            scaleFixture.SetActive(true);

            ScrollRect scroll = instance.GetComponentInChildren<ScrollRect>(true);
            GridLayoutGroup grid = scroll.content.GetComponent<GridLayoutGroup>();
            RectTransform header = FindInventoryBagLayoutRect(instance, "FWUI_Header");
            RectTransform inner = FindInventoryBagLayoutRect(instance, "FWUI_InnerField");
            RectTransform footer = FindInventoryBagLayoutRect(instance, "FWUI_Footer");
            var samples = new JArray();
            Vector2[] sizes =
            {
                root.rect.size,
                new Vector2(resizer.minWidth, resizer.minHeight),
                new Vector2(resizer.maxWidth, resizer.maxHeight)
            };
            foreach (Vector2 size in sizes)
            {
                root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
                root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
                scaled.sizeDelta = size;
                scale.ApplyScale();
                samples.Add(new JObject
                {
                    ["root"] = CaptureBuildingRectBounds(root, root),
                    ["header"] = CaptureBuildingRectBounds(header, root),
                    ["inner"] = CaptureBuildingRectBounds(inner, root),
                    ["footer"] = CaptureBuildingRectBounds(footer, root),
                    ["scroll"] = CaptureBuildingRectBounds((RectTransform)scroll.transform, root),
                    ["viewport"] = CaptureBuildingRectBounds(scroll.viewport, root),
                    ["bar"] = scroll.verticalScrollbar != null
                        ? CaptureBuildingRectBounds((RectTransform)scroll.verticalScrollbar.transform, root) : null,
                    ["contentWidth"] = scroll.content.rect.width,
                    ["viewportWidth"] = scroll.viewport.rect.width,
                    ["viewportHeight"] = scroll.viewport.rect.height,
                    ["scaledBounds"] = CaptureBuildingRectBounds(scaled, available),
                    ["scaleX"] = scaled.localScale.x,
                    ["scaleY"] = scaled.localScale.y
                });
            }
            return new JObject
            {
                ["scalesRoot"] = scalesRoot,
                ["available"] = CaptureBuildingRectBounds(available, available),
                ["samples"] = samples,
                ["columns"] = grid.constraintCount,
                ["cellWidth"] = grid.cellSize.x,
                ["cellHeight"] = grid.cellSize.y,
                ["spacing"] = grid.spacing.x,
                ["paddingX"] = grid.padding.left + grid.padding.right,
                ["paddingY"] = grid.padding.top + grid.padding.bottom,
                ["scrollbarBound"] = scroll.verticalScrollbar != null,
                ["scrollbarAutomatic"] = scroll.verticalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHide,
                ["scrollbarActive"] = scroll.verticalScrollbar != null && scroll.verticalScrollbar.gameObject.activeSelf,
                ["verticalFit"] = (int)scroll.content.GetComponent<ContentSizeFitter>().verticalFit,
                ["verticalScroll"] = scroll.vertical,
                ["horizontalScroll"] = scroll.horizontal
            };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
            UnityEngine.Object.DestroyImmediate(scaleFixture);
        }
    }

    private static RectTransform FindInventoryBagLayoutRect(GameObject root, string name)
    {
        foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
            if (rect.name == name) return rect;
        throw new InvalidOperationException("背包缺少布局节点：" + name);
    }

    #endregion
}
#endif
