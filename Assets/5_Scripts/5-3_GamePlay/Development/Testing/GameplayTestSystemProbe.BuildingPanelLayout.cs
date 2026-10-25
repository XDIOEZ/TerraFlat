#if UNITY_EDITOR
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

public static partial class GameplayTestSystemProbe
{
    #region 建筑正式面板布局采样

    [UnityEditor.InitializeOnLoadMethod]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterBuildingPanelLayoutRules()
    {
        Register("ui_settings", "building_panel_layout", CaptureBuildingPanelLayout);
        Register("ui_settings", "vessel_contents_layers", CaptureVesselContentsLayers);
    }

    // 读取正式容器外观，防止重建面板后坩埚材料重新落回液体遮罩内。
    private static JObject CaptureVesselContentsLayers(JObject args)
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/2-1_UI/Gameplay/Containers/UI_WaterVessel.prefab");
        WaterVesselPanel panel = prefab.GetComponent<WaterVesselPanel>();
        var appearances = new JArray();
        foreach (WaterVesselPanel.VesselAppearance appearance in panel.Appearances)
            appearances.Add(new JObject
            {
                ["id"] = appearance.ItemId,
                ["aboveInterior"] = appearance.ContentsAboveInterior,
                ["bottom"] = appearance.StorageBounds.yMin,
                ["left"] = appearance.StorageBounds.xMin,
                ["right"] = appearance.StorageBounds.xMax
            });
        return new JObject { ["appearances"] = appearances };
    }

    private static JObject CaptureBuildingPanelLayout(JObject args)
    {
        string panelName = args.Value<string>("panel");
        if (panelName != "UI_CompostBin" && panelName != "UI_MeatRack" && panelName != "UI_Furnace" && panelName != "UI_FluidTank")
            throw new ArgumentException("建筑布局采样仅允许堆肥箱、晾肉架、熔炉和便携气罐正式面板。");
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/" + panelName + ".prefab");
        if (prefab == null) throw new InvalidOperationException("建筑正式面板缺失：" + panelName);

        if (panelName == "UI_FluidTank") return CapturePortableTankPanelLayout(prefab);
        if (panelName == "UI_Furnace")
            return new JObject
            {
                ["input"] = CaptureBuildingSlotContainer(FindBuildingPanelRect(prefab, "输入")),
                ["output"] = CaptureBuildingSlotContainer(FindBuildingPanelRect(prefab, "输出"))
            };

        ScrollRect scroll = prefab.GetComponentInChildren<ScrollRect>(true);
        if (scroll == null || scroll.viewport == null || scroll.content == null || scroll.verticalScrollbar == null)
            throw new InvalidOperationException(panelName + " 缺少完整滚动布局引用。");
        GridLayoutGroup grid = scroll.content.GetComponent<GridLayoutGroup>();
        if (grid == null) throw new InvalidOperationException(panelName + " 缺少正式库存网格。");
        RectTransform scrollRect = (RectTransform)scroll.transform;
        return new JObject
        {
            ["scroll"] = CaptureBuildingRectBounds(scrollRect, scrollRect),
            ["viewport"] = CaptureBuildingRectBounds(scroll.viewport, scrollRect),
            ["bar"] = CaptureBuildingRectBounds((RectTransform)scroll.verticalScrollbar.transform, scrollRect),
            ["content"] = CaptureBuildingRectBounds(scroll.content, scroll.viewport),
            ["viewportLocal"] = CaptureBuildingRectBounds(scroll.viewport, scroll.viewport),
            ["columns"] = grid.constraintCount, ["cellWidth"] = grid.cellSize.x,
            ["spacing"] = grid.spacing.x, ["padding"] = grid.padding.left + grid.padding.right,
            ["verticalFit"] = (int)scroll.content.GetComponent<ContentSizeFitter>().verticalFit,
            ["horizontalScroll"] = scroll.horizontal, ["verticalScroll"] = scroll.vertical
        };
    }

    // 采样正式气罐的三段区域和按钮归属，避免底栏操作重新混进正文。
    private static JObject CapturePortableTankPanelLayout(GameObject prefab)
    {
        RectTransform root = (RectTransform)prefab.transform;
        RectTransform footer = FindBuildingPanelRect(prefab, "FWUI_Footer");
        MechanicalPanelView view = prefab.GetComponent<MechanicalPanelView>();
        BuildingPanelActions actions = prefab.GetComponent<BuildingPanelActions>();
        var buttons = new JArray();
        foreach (Button button in new[] { actions.PlaceButton, actions.DismantleButton, view.ActionButton })
            buttons.Add(new JObject
            {
                ["name"] = button.name,
                ["inFooter"] = button.transform.parent == footer,
                ["bounds"] = CaptureBuildingRectBounds((RectTransform)button.transform, root)
            });
        return new JObject
        {
            ["header"] = CaptureBuildingRectBounds(FindBuildingPanelRect(prefab, "FWUI_Header"), root),
            ["body"] = CaptureBuildingRectBounds(view.InnerField, root),
            ["footer"] = CaptureBuildingRectBounds(footer, root),
            ["bodyButtonCount"] = view.InnerField.GetComponentsInChildren<Button>(true).Length,
            ["buttons"] = buttons
        };
    }

    private static JObject CaptureBuildingSlotContainer(RectTransform container)
    {
        var slots = new JArray();
        foreach (ItemSlot_UI slot in container.GetComponentsInChildren<ItemSlot_UI>(true))
            if (slot.transform.parent == container)
                slots.Add(CaptureBuildingRectBounds((RectTransform)slot.transform, container));
        return new JObject { ["bounds"] = CaptureBuildingRectBounds(container, container), ["slots"] = slots };
    }

    private static RectTransform FindBuildingPanelRect(GameObject prefab, string name)
    {
        foreach (RectTransform rect in prefab.GetComponentsInChildren<RectTransform>(true))
            if (rect.name == name) return rect;
        throw new InvalidOperationException(prefab.name + " 缺少正式节点：" + name);
    }

    private static JObject CaptureBuildingRectBounds(RectTransform child, RectTransform parent)
    {
        // 直接采样正式资产矩形，Lua 判断容器预算和越界，不复制布局实现。
        var corners = new Vector3[4];
        child.GetWorldCorners(corners);
        Vector3 min = parent.InverseTransformPoint(corners[0]);
        Vector3 max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 point = parent.InverseTransformPoint(corners[i]);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        return new JObject
        {
            ["left"] = min.x, ["bottom"] = min.y, ["right"] = max.x, ["top"] = max.y,
            ["width"] = child.rect.width, ["height"] = child.rect.height,
            ["scaleX"] = child.localScale.x, ["scaleY"] = child.localScale.y
        };
    }

    #endregion
}
#endif
