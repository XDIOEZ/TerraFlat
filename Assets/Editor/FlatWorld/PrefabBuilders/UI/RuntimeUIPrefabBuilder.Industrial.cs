using System;
using System.Linq;
using FlatWorld.Localization;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class RuntimeUIPrefabBuilder
{
    #region 工业与气罐正式页面
    private const string IndustrialBasePath = "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_Mechanical.prefab";

    /// <summary>明确执行时重建完整正式资产；控件继续继承项目公共 Prefab。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Industrial Fluid And Spacesuit Panels")]
    public static void RebuildIndustrialPanels()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在退出 PlayMode 后手动重建页面；运行游戏期间只修改静态资产。");
        foreach (string name in new[] { "UI_IndustrialFluid", "UI_Spacesuit", "UI_FluidTank" })
        {
            GameObject root = PrefabUtility.LoadPrefabContents(IndustrialBasePath);
            try
            {
                ConfigureIndustrialPanel(root, name);
                if (name == "UI_FluidTank") ConfigurePortableTankActions(root, root.GetComponent<MechanicalPanelView>());
                if (name != "UI_Spacesuit") EnsureGasCapacityGauge(root.GetComponent<MechanicalPanelView>());
                if (name == "UI_IndustrialFluid")
                {
                    MechanicalPanelView view = root.GetComponent<MechanicalPanelView>();
                    EnsureFluidReadings(view);
                    view.SetFluidLayout(true, true, true);
                }
                string path = "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/" + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path); EnsureRuntimePrefabAddressable(path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
    }
    private static void ConfigureIndustrialPanel(GameObject root, string name)
    {
        root.name = name;
        var view = root.GetComponent<MechanicalPanelView>() ?? throw new InvalidOperationException("机械基础面板缺少正式视图。");
        view.PreservePrefabLayout = true;
        view.SetBatteryVisible(false);
        ((RectTransform)root.transform).sizeDelta = new Vector2(760, name == "UI_IndustrialFluid" ? 680 : 600);
        view.ProcessingVisuals = root.GetComponentsInChildren<Transform>(true)
            .Where(child => child.name.Contains("INPUT", StringComparison.Ordinal) || (name == "UI_IndustrialFluid" || name == "UI_Spacesuit") && child.name.Contains("OUTPUT", StringComparison.Ordinal))
            .Select(child => child.gameObject).Concat(name == "UI_IndustrialFluid" || name == "UI_Spacesuit"
                ? new[] { view.InputSlot.gameObject, view.OutputSlot.gameObject } : new[] { view.InputSlot.gameObject }).Distinct().ToArray();
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name.StartsWith("FWUI_FlowArrow", StringComparison.Ordinal) || name != "UI_IndustrialFluid" && name != "UI_Spacesuit" && child.name.Contains("OUTPUT", StringComparison.Ordinal)) child.gameObject.SetActive(false);
        view.OutputSlot.gameObject.SetActive(name == "UI_IndustrialFluid" || name == "UI_Spacesuit");
        if (name == "UI_Spacesuit")
            for (Transform ancestor = view.OutputSlot.transform.parent; ancestor != null && ancestor != root.transform; ancestor = ancestor.parent)
                ancestor.gameObject.SetActive(true);
        view.InputSlot.gameObject.SetActive(name != "UI_FluidTank");
        if (name == "UI_FluidTank") foreach (GameObject visual in view.ProcessingVisuals) visual.SetActive(false);
        view.DismantleButton.gameObject.SetActive(false); view.ActionButton.gameObject.SetActive(name != "UI_FluidTank");
        RectTransform status = (RectTransform)view.StatusScroll.transform;
        status.anchorMin = Vector2.zero; status.anchorMax = Vector2.one; status.pivot = new Vector2(.5f, .5f);
        status.anchoredPosition = new Vector2(0, name == "UI_FluidTank" ? 0 : -140);
        status.sizeDelta = new Vector2(-24, name == "UI_FluidTank" ? -24 : -304);
        if (name != "UI_IndustrialFluid") return;
        // 三个正式槽位由同一真实装填库存绑定，重建时保留同名契约。
        RectTransform containerSlots = (RectTransform)view.InputSlot.transform.parent;
        containerSlots.sizeDelta = new Vector2(320, 144);
        for (int index = 1; index < 3; index++)
        {
            ItemSlot_UI slot = UnityEngine.Object.Instantiate(view.InputSlot, containerSlots);
            slot.name = "输入_" + (index + 1);
            ((RectTransform)slot.transform).anchoredPosition = new Vector2(index * 102, 0);
        }
        foreach (GameObject visual in view.ProcessingVisuals)
            ((RectTransform)visual.transform).anchoredPosition += Vector2.down * 34;
        for (int index = 1; index < 3; index++)
        {
            Transform slot = containerSlots.Find("输入_" + (index + 1));
            ((RectTransform)slot).anchoredPosition = ((RectTransform)view.InputSlot.transform).anchoredPosition + Vector2.right * (index * 102);
        }
        AddIndustrialField(root.transform, "FWUI_FluidSettingPrimary", "气体ID / 每秒速率（0停止）", new Vector2(24, -78), new Vector2(245, 40));
        AddIndustrialField(root.transform, "FWUI_FluidSettingSecondary", "恢复压强 kPa", new Vector2(280, -78), new Vector2(200, 40));
        AddIndustrialButton(root.transform, "FWUI_ApplyFluidSettings", "应用配置", new Vector2(494, -78), new Vector2(240, 40), new Vector2(0, 1), new Vector2(0, 1));
        AddIndustrialButton(root.transform, "FWUI_FluidPhase", "输出气体和液体", new Vector2(24, 29), new Vector2(190, 46), Vector2.zero, new Vector2(0, .5f));
        AddIndustrialButton(root.transform, "FWUI_RotateFluid", "旋转方向", new Vector2(226, 29), new Vector2(150, 46), Vector2.zero, new Vector2(0, .5f));
    }

    // 便携罐的放置、拆回和充气共用底栏，正文只承载状态与容量。
    private static void ConfigurePortableTankActions(GameObject root, MechanicalPanelView view)
    {
        RectTransform footer = root.GetComponentsInChildren<RectTransform>(true)
            .First(child => child.name == "FWUI_Footer");
        BuildingPanelActions actions = root.GetComponent<BuildingPanelActions>();
        if (actions.PlaceButton == null)
            actions.PlaceButton = InstantiateSharedControl<Button>("UI_Button", "放置建筑按钮", footer);
        MechanicalPanelView.SetButtonCaption(actions.PlaceButton, "放到地上");
        MechanicalPanelView.SetButtonCaption(actions.DismantleButton, "拆回物品");
        foreach (Button button in new[] { actions.PlaceButton, actions.DismantleButton, view.ActionButton })
        {
            bool action = button == view.ActionButton;
            button.transform.SetParent(footer, false);
            LayoutIndustrialControl((RectTransform)button.transform,
                new Vector2(action ? -24 : 24, 0), new Vector2(160, 46),
                new Vector2(action ? 1 : 0, .5f), new Vector2(action ? 1 : 0, .5f));
            if (!action) button.gameObject.SetActive(false);
            RecordSharedInstance(button.gameObject);
        }
        footer.SetAsLastSibling();
    }
    private static void AddIndustrialField(Transform parent, string name, string hint, Vector2 position, Vector2 size)
    {
        TMP_InputField field = InstantiateSharedControl<TMP_InputField>("UI_InputField", name, parent);
        LayoutIndustrialControl((RectTransform)field.transform, position, size, new Vector2(0, 1), new Vector2(0, 1));
        field.characterLimit = 128;
        if (field.placeholder is TMP_Text placeholder) placeholder.text = hint;
        RecordSharedInstance(field.gameObject);
    }
    private static void AddIndustrialButton(Transform parent, string name, string caption, Vector2 position, Vector2 size, Vector2 anchor, Vector2 pivot)
    {
        Button button = InstantiateSharedControl<Button>("UI_Button", name, parent);
        LayoutIndustrialControl((RectTransform)button.transform, position, size, anchor, pivot);
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        LocalizedTextBinder binder = label.GetComponent<LocalizedTextBinder>();
        if (binder != null) binder.Configure(FlatWorldLocalizationService.UiTable, FlatWorldLocalizationService.GetUiTextKey(caption), caption);
        else label.text = caption;
        RecordSharedInstance(button.gameObject);
    }
    private static void LayoutIndustrialControl(RectTransform control, Vector2 position, Vector2 size, Vector2 anchor, Vector2 pivot)
    { control.anchorMin = control.anchorMax = anchor; control.pivot = pivot; control.anchoredPosition = position; control.sizeDelta = size; }
    #endregion

    #region 通用装备配置入口
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Equipment Configuration Button")]
    public static void RebuildEquipmentConfigurationButton()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在退出 PlayMode 后手动重建装备入口。");
        const string path = "Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Panels/UI_Equipment.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            EnsureEquipmentConfigurationButton(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
    }

    internal static void EnsureEquipmentConfigurationButton(GameObject root)
    {
        // 单独补建与整页重建复用同一个通用入口，保持正式控件及引用一致。
        Button button = root.GetComponentsInChildren<Button>(true)
            .FirstOrDefault(value => value.name == "FWUI_ConfigureEquipment");
        button ??= InstantiateSharedControl<Button>("UI_Button", "FWUI_ConfigureEquipment", root.transform);
        LayoutIndustrialControl((RectTransform)button.transform, new Vector2(0, 28), new Vector2(190, 42),
            new Vector2(.5f, 0), new Vector2(.5f, .5f));
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        var binder = label.GetComponent<LocalizedTextBinder>() ?? label.gameObject.AddComponent<LocalizedTextBinder>();
        binder.Configure(FlatWorldLocalizationService.UiTable,
            FlatWorldLocalizationService.GetUiTextKey("装备配置"), "装备配置");
        RecordSharedInstance(button.gameObject);
    }
    #endregion

    #region 气罐容量正式控件
    // 只有气罐页面持有罐体控件，业务会话决定显隐和真实气压比例。
    private static void EnsureGasCapacityGauge(MechanicalPanelView view)
    {
        if (view.GasCapacity != null && view.GasPercentage != null) return;
        var gaugeObject = new GameObject("FWUI_GasCapacity", typeof(RectTransform), typeof(Slider));
        gaugeObject.layer = view.InnerField.gameObject.layer;
        var gauge = (RectTransform)gaugeObject.transform;
        gauge.SetParent(view.InnerField, false);
        gauge.anchorMin = new Vector2(1, 0); gauge.anchorMax = Vector2.one;
        gauge.anchoredPosition = new Vector2(-148, 0);
        gauge.sizeDelta = new Vector2(200, -64);

        Color border = new Color(.78f, .78f, .78f, 1);
        Color background = new Color(.2f, .2f, .2f, 1);
        Image body = AddGasCapacityImage("Body", gauge, border, Vector2.zero, Vector2.zero);
        body.rectTransform.anchorMax = new Vector2(1, .76f);
        AddGasCapacityImage("Background", body.transform, background, new Vector2(3, 3), new Vector2(-3, -3));
        Image neck = AddGasCapacityImage("Neck", gauge, border, Vector2.zero, Vector2.zero);
        neck.rectTransform.anchorMin = new Vector2(.17f, .76f);
        neck.rectTransform.anchorMax = new Vector2(.83f, 1);
        AddGasCapacityImage("Background", neck.transform, background, new Vector2(3, 3), new Vector2(-3, -3));

        var fillAreaObject = new GameObject("FillArea", typeof(RectTransform));
        fillAreaObject.layer = gaugeObject.layer;
        var fillArea = (RectTransform)fillAreaObject.transform;
        fillArea.SetParent(body.transform, false);
        fillArea.anchorMin = Vector2.zero; fillArea.anchorMax = Vector2.one;
        fillArea.offsetMin = new Vector2(6, 6); fillArea.offsetMax = new Vector2(-6, -6);
        Image fill = AddGasCapacityImage("Fill", fillArea, new Color(.18f, .58f, .35f, 1), Vector2.zero, Vector2.zero);

        var labelObject = new GameObject("Percentage", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.layer = gaugeObject.layer;
        labelObject.transform.SetParent(body.transform, false);
        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.font = view.Status.font; label.fontSharedMaterial = view.Status.fontSharedMaterial;
        label.fontSize = 32; label.enableAutoSizing = true;
        label.fontSizeMin = 18; label.fontSizeMax = 32;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Truncate;
        label.color = Color.white; label.raycastTarget = false;
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(4, 4); label.rectTransform.offsetMax = new Vector2(-4, -4);
        label.text = "50%";

        var slider = gaugeObject.GetComponent<Slider>();
        slider.fillRect = fill.rectTransform; slider.targetGraphic = fill;
        slider.direction = Slider.Direction.BottomToTop;
        slider.minValue = 0; slider.maxValue = 1;
        slider.interactable = false; slider.transition = Selectable.Transition.None;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        slider.SetValueWithoutNotify(.5f);
        view.GasCapacity = slider; view.GasPercentage = label;
        gaugeObject.SetActive(false);
    }

    private static Image AddGasCapacityImage(string name, Transform parent, Color color, Vector2 offsetMin, Vector2 offsetMax)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        var image = child.GetComponent<Image>();
        image.color = color; image.raycastTarget = false;
        image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
        image.rectTransform.offsetMin = offsetMin; image.rectTransform.offsetMax = offsetMax;
        return image;
    }
    #endregion

    #region 水箱与燃料正式控件
    internal static void EnsureFluidReadings(MechanicalPanelView view)
    {
        if (view.WaterCapacity == null)
            view.WaterCapacity = CreateCapacityGauge(view.InnerField, view.Status, "FWUI_WaterCapacity", true,
                new Color(.15f, .48f, .85f, 1), out view.WaterPercentage);
        if (view.FuelRemaining == null)
            view.FuelRemaining = CreateCapacityGauge(view.InnerField, view.Status, "FWUI_FuelRemaining", false,
                new Color(.95f, .7f, .18f, 1), out view.FuelValue);
    }

    internal static Slider CreateCapacityGauge(Transform parent, TMP_Text template, string name, bool vertical, Color color, out TMP_Text label)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Slider), typeof(UIVisualOwner));
        root.layer = parent.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = vertical ? Vector2.zero : new Vector2(0, 0);
        rect.anchorMax = vertical ? new Vector2(0, 1) : new Vector2(1, 0);
        rect.pivot = new Vector2(0, .5f);
        rect.offsetMin = vertical ? new Vector2(16, 24) : new Vector2(138, 156);
        rect.offsetMax = vertical ? new Vector2(98, -24) : new Vector2(-34, 188);
        Image border = AddGasCapacityImage("Body", rect, new Color(.65f, .65f, .65f, 1), Vector2.zero, Vector2.zero);
        AddGasCapacityImage("Background", border.transform, new Color(.18f, .18f, .18f, 1), new Vector2(2, 2), new Vector2(-2, -2));
        var area = new GameObject("FillArea", typeof(RectTransform));
        area.layer = root.layer; area.transform.SetParent(border.transform, false);
        var areaRect = (RectTransform)area.transform;
        areaRect.anchorMin = Vector2.zero; areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(5, 5); areaRect.offsetMax = new Vector2(-5, -5);
        Image fill = AddGasCapacityImage("Fill", areaRect, color, Vector2.zero, Vector2.zero);
        var text = new GameObject("Amount", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        text.layer = root.layer; text.transform.SetParent(rect, false);
        label = text.GetComponent<TMP_Text>();
        label.font = template.font; label.fontSharedMaterial = template.fontSharedMaterial;
        label.fontSize = vertical ? 18 : 20; FlatWorldUITextLayout.Configure(label, label.fontSize);
        label.alignment = TextAlignmentOptions.Center; label.enableWordWrapping = false;
        label.color = Color.white; label.raycastTarget = false;
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(4, 4); label.rectTransform.offsetMax = new Vector2(-4, -4);
        label.text = "0/100";
        Slider slider = root.GetComponent<Slider>();
        slider.fillRect = fill.rectTransform; slider.targetGraphic = fill;
        slider.direction = vertical ? Slider.Direction.BottomToTop : Slider.Direction.LeftToRight;
        slider.minValue = 0; slider.maxValue = 1; slider.interactable = false;
        slider.transition = Selectable.Transition.None; slider.navigation = new Navigation { mode = Navigation.Mode.None };
        slider.SetValueWithoutNotify(0);
        return slider;
    }
    #endregion
}
