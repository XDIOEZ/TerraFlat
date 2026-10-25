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
        Register("ui_settings", "fluid_panel_layout", CaptureFluidPanelLayout);
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

    // 读取正式水量与燃料控件，颜色、方向、容量及槽位边界交给 Lua 判断。
    private static JObject CaptureFluidPanelLayout(JObject args)
    {
        string panelName = args.Value<string>("panel");
        string path = panelName switch
        {
            "UI_IndustrialFluid" => "Gameplay/Crafting/UI_IndustrialFluid",
            "UI_Cultivator" => "Gameplay/Crafting/UI_Cultivator",
            "UI_WaterVessel" => "Gameplay/Containers/UI_WaterVessel",
            _ => throw new ArgumentException("水量面板采样仅允许工业设备、培育器与通用水容器。")
        };
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/2-1_UI/" + path + ".prefab");
        if (prefab == null) throw new InvalidOperationException("水量正式面板缺失：" + panelName);
        var previewRoot = new GameObject("水量面板隔离采样", typeof(RectTransform));
        previewRoot.SetActive(false);
        try
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab, previewRoot.transform, false);
            MechanicalPanelView view = instance.GetComponent<MechanicalPanelView>();
            if (panelName == "UI_IndustrialFluid")
            {
                // 复现正式会话先关闭加工区再配置库存的顺序，覆盖父节点隐藏和模式切换。
                view.SetProcessingVisible(false);
                view.SetFluidLayout(false, false, false);
                string mode = args.Value<string>("mode") ?? "fuel";
                view.SetFluidLayout(mode == "fuel", mode == "fuel", mode != "storage");
                foreach (ItemSlot_UI slot in instance.GetComponentsInChildren<ItemSlot_UI>(true))
                    if (slot.name.StartsWith("输入_", StringComparison.Ordinal)) slot.gameObject.SetActive(mode != "storage");
            }
            JObject result = CaptureFluidPanelInstance(instance, panelName);
            RectTransform window = panelName == "UI_WaterVessel"
                ? FindBuildingPanelRect(instance, "设置对话框") : (RectTransform)instance.transform;
            // 真实主题入口不能把领域容量条重新染灰或压成半条轨道。
            FlatWorldUITheme.Apply(instance.transform);
            result["waterAfterTheme"] = CaptureFluidGauge(instance, "FWUI_WaterCapacity", window);
            if (panelName == "UI_IndustrialFluid")
                result["fuelAfterTheme"] = CaptureFluidGauge(instance, "FWUI_FuelRemaining", window);
            FlatWorldUITheme.ApplyRuntimePalette(instance.transform);
            result["waterAfterPalette"] = CaptureFluidGauge(instance, "FWUI_WaterCapacity", window);
            if (panelName == "UI_IndustrialFluid")
                result["fuelAfterPalette"] = CaptureFluidGauge(instance, "FWUI_FuelRemaining", window);
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(previewRoot); }
    }

    private static JObject CaptureFluidPanelInstance(GameObject prefab, string panelName)
    {
        RectTransform root = panelName == "UI_WaterVessel"
            ? FindBuildingPanelRect(prefab, "设置对话框") : (RectTransform)prefab.transform;
        var result = new JObject
        {
            ["root"] = CaptureBuildingRectBounds(root, root),
            ["water"] = CaptureFluidGauge(prefab, "FWUI_WaterCapacity", root)
        };
        if (panelName == "UI_WaterVessel")
        {
            RectTransform picture = FindBuildingPanelRect(prefab, "陶罐剖面");
            RectTransform water = FindBuildingPanelRect(prefab, "FWUI_WaterCapacity");
            VerticalLayoutGroup layout = root.GetComponent<VerticalLayoutGroup>();
            var rows = new JArray();
            foreach (Transform child in root)
            {
                LayoutElement element = child.GetComponent<LayoutElement>();
                rows.Add(new JObject
                {
                    ["name"] = child.name, ["active"] = child.gameObject.activeSelf,
                    ["hasElement"] = element != null, ["ignored"] = element != null && element.ignoreLayout,
                    ["minimum"] = element != null ? element.minHeight : -1,
                    ["preferred"] = element != null ? element.preferredHeight : -1
                });
            }
            // 水容器不激活业务脚本；Lua 根据正式布局所有者参数核对静态基准尺寸。
            result["vesselLayout"] = new JObject
            {
                ["controlWidth"] = layout.childControlWidth, ["controlHeight"] = layout.childControlHeight,
                ["expandWidth"] = layout.childForceExpandWidth, ["expandHeight"] = layout.childForceExpandHeight,
                ["alignment"] = (int)layout.childAlignment,
                ["leftPadding"] = layout.padding.left, ["rightPadding"] = layout.padding.right,
                ["topPadding"] = layout.padding.top, ["bottomPadding"] = layout.padding.bottom,
                ["spacing"] = layout.spacing, ["rows"] = rows,
                ["picture"] = CaptureBuildingRectBounds(picture, root),
                ["pictureLocal"] = CaptureBuildingRectBounds(picture, picture),
                ["waterInPicture"] = CaptureBuildingRectBounds(water, picture)
            };
        }
        if (panelName != "UI_IndustrialFluid") return result;

        MechanicalPanelView view = prefab.GetComponent<MechanicalPanelView>();
        var slots = new JArray();
        foreach (ItemSlot_UI slot in prefab.GetComponentsInChildren<ItemSlot_UI>(true))
            if (slot.name.StartsWith("输入_", StringComparison.Ordinal))
            {
                bool visibleWithinPanel = true;
                for (Transform ancestor = slot.transform; ancestor != prefab.transform; ancestor = ancestor.parent)
                    visibleWithinPanel &= ancestor.gameObject.activeSelf;
                slots.Add(new JObject
                {
                    ["name"] = slot.name,
                    ["visibleWithinPanel"] = visibleWithinPanel,
                    ["bounds"] = CaptureBuildingRectBounds((RectTransform)slot.transform, root)
                });
            }
        result["slotRowVisible"] = view.InputSlot.transform.parent.gameObject.activeSelf;
        result["waterVisible"] = view.WaterCapacity.gameObject.activeSelf;
        result["fuelVisible"] = view.FuelRemaining.gameObject.activeSelf;
        result["body"] = CaptureBuildingRectBounds(view.InnerField, root);
        result["status"] = CaptureBuildingRectBounds((RectTransform)view.StatusScroll.transform, root);
        result["fuel"] = CaptureFluidGauge(prefab, "FWUI_FuelRemaining", root);
        result["slots"] = slots;
        result["action"] = CaptureBuildingRectBounds((RectTransform)view.ActionButton.transform, root);
        result["dismantle"] = CaptureBuildingRectBounds((RectTransform)view.DismantleButton.transform, root);
        result["actionInBody"] = view.ActionButton.transform.IsChildOf(view.InnerField);
        result["preserveLayout"] = view.PreservePrefabLayout;
        return result;
    }

    private static JObject CaptureFluidGauge(GameObject prefab, string nodeName, RectTransform root)
    {
        RectTransform rect = FindBuildingPanelRect(prefab, nodeName);
        Slider slider = rect.GetComponent<Slider>();
        if (slider == null || slider.fillRect == null)
            throw new InvalidOperationException(prefab.name + " 缺少完整容量条：" + nodeName);
        Image fill = slider.fillRect.GetComponent<Image>();
        RectTransform fillArea = (RectTransform)slider.fillRect.parent;
        RectTransform labelRect = FindBuildingPanelRect(rect.gameObject, "Amount");
        TMPro.TMP_Text label = labelRect.GetComponent<TMPro.TMP_Text>();
        var readings = new JArray();
        foreach (Vector2 reading in new[] { new Vector2(0, 100), new Vector2(25, 100), new Vector2(.25f, 100),
                     new Vector2(100, 100), new Vector2(150, 100), new Vector2(10, 0) })
        {
            MechanicalPanelView.SetCapacityReading(slider, label, reading.x, reading.y);
            readings.Add(new JObject { ["amount"] = reading.x, ["capacity"] = reading.y,
                ["value"] = slider.value, ["label"] = label.text });
        }
        var graphics = new JArray();
        foreach (Graphic graphic in rect.GetComponentsInChildren<Graphic>(true))
            graphics.Add(new JObject { ["name"] = graphic.name, ["raycast"] = graphic.raycastTarget });
        return new JObject
        {
            ["bounds"] = CaptureBuildingRectBounds(rect, root),
            ["fillBounds"] = CaptureBuildingRectBounds(slider.fillRect, rect),
            ["fillArea"] = CaptureBuildingRectBounds(fillArea, rect),
            ["fillAreaLayout"] = new JObject
            {
                ["minX"] = fillArea.anchorMin.x, ["minY"] = fillArea.anchorMin.y,
                ["maxX"] = fillArea.anchorMax.x, ["maxY"] = fillArea.anchorMax.y,
                ["left"] = fillArea.offsetMin.x, ["bottom"] = fillArea.offsetMin.y,
                ["right"] = fillArea.offsetMax.x, ["top"] = fillArea.offsetMax.y
            },
            ["localBounds"] = CaptureBuildingRectBounds(rect, rect),
            ["direction"] = (int)slider.direction,
            ["minimum"] = slider.minValue, ["maximum"] = slider.maxValue,
            ["interactable"] = slider.interactable,
            ["color"] = new JObject { ["r"] = fill.color.r, ["g"] = fill.color.g, ["b"] = fill.color.b },
            ["label"] = CaptureBuildingRectBounds(labelRect, rect),
            ["graphics"] = graphics, ["readings"] = readings
        };
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
