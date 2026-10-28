// 按业务职责维护 Prefab，复用公共控件与布局实现。
using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class GameUIPrefabRebuilder
{
    #region 库存界面

    private static void BuildEquipmentWindow(GameObject root)
    {
        RectTransform chrome = PrepareWindow(root, 680f, 560f, "装备", string.Empty, string.Empty, true);
        SetTopLeft(FindRect(chrome, "FWUI_Header"), 0f, 0f, 680f, 40f);
        SetTopLeft(FindRect(chrome, "FWUI_AccentRail"), 0f, 0f, 3f, 40f);
        SetTopLeft(FindRect(chrome, "FWUI_HeaderRule"), 0f, 40f, 680f, 1f);
        SetTopLeft(FindRect(chrome, "FWUI_标题"), 16f, 8f, 574f, 24f);
        FindRect(chrome, "FWUI_标题").GetComponent<TextMeshProUGUI>().fontSize = 20f;
        SetTopLeft(FindRect(chrome, "FWUI_InnerField"), 14f, 48f, 652f, 498f);
        FindRect(chrome, "FWUI_Footer").gameObject.SetActive(false);

        RectTransform content = FindRect(root.transform, "UI_Content");
        if (content == null) throw new MissingReferenceException("装备页缺少 UI_Content。");
        SetCentered(content, Vector2.zero, new Vector2(630f, 516f));
        List<ItemSlot_UI> slots = new();
        foreach (Transform child in content)
            if (child.TryGetComponent(out ItemSlot_UI slot)) slots.Add(slot);
        List<EquipmentSlotRule> rules = EquipmentSlotCatalog.CreateDefaultRules();
        GameObject slotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SlotPrefabPath);
        while (slots.Count < rules.Count)
        {
            var slotObject = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, content);
            slots.Add(slotObject.GetComponent<ItemSlot_UI>());
        }

        // 正式资产和运行时共用槽位目录，新增眼部后重建也不会退回四槽背包布局。
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].gameObject.SetActive(i < rules.Count);
            if (i >= rules.Count) continue;
            EquipmentSlotRule rule = rules[i];
            SetCentered((RectTransform)slots[i].transform, rule.UiPosition, new Vector2(92f, 92f));
            TextMeshProUGUI label = CreateText(rule.LabelObjectName, root.transform, rule.DisplayName,
                13f, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
            SetCentered(label.rectTransform, rule.UiPosition + new Vector2(0f, -58f), new Vector2(132f, 24f));
            label.gameObject.AddComponent<LocalizedTextBinder>().Configure(FlatWorldLocalizationService.UiTable,
                FlatWorldLocalizationService.GetUiTextKey(rule.DisplayName), rule.DisplayName);
        }

        Image portrait = CreateImage("FWUI_Avatar", root.transform, new Color(.06f, .07f, .08f, .9f));
        portrait.preserveAspect = true;
        SetCentered(portrait.rectTransform, new Vector2(0f, -8f), new Vector2(150f, 220f));
        RuntimeUIPrefabBuilder.EnsureEquipmentConfigurationButton(root);
        RectTransform close = FindRect(root.transform, "关闭");
        close.anchoredPosition = new Vector2(-8f, -2f);
        close.sizeDelta = new Vector2(36f, 36f);
    }

    private static void BuildScrollWindow(
        GameObject root,
        float width,
        float height,
        string title,
        string eyebrow,
        string hint,
        int columns,
        Vector2 cellSize)
    {
        RectTransform frame = PrepareWindow(root, width, height, title, eyebrow, hint, true);
        AddSection(frame, "CONTENTS", "物资清单", 24f, 104f, width - 48f, height - 184f);

        RectTransform scroll = FindRect(root.transform, "Scroll View");
        if (scroll != null)
            SetTopLeft(scroll, 42f, 146f, width - 84f, height - 246f);

        GridLayoutGroup grid = root.GetComponentInChildren<GridLayoutGroup>(true);
        if (grid != null)
        {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.cellSize = cellSize;
            grid.spacing = new Vector2(12f, 12f);
            grid.padding = new RectOffset(12, 12, 12, 18);
            grid.childAlignment = TextAnchor.UpperLeft;
        }

        ScrollRect scrollRect = root.GetComponentInChildren<ScrollRect>(true);
        if (scrollRect != null)
        {
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 34f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            ConfigureInventoryScrollLayout(scrollRect);
        }
    }

    private static void ConfigureInventoryScrollLayout(ScrollRect scroll)
    {
        const float scrollbarWidth = 32f;
        if (scroll.viewport == null || scroll.content == null) return;

        // 滚动条与内容共同使用滚动区坐标，清除旧版手动偏移和固定内容宽度。
        float reservedWidth = scroll.verticalScrollbar != null ? scrollbarWidth : 0f;
        RectTransform viewport = scroll.viewport;
        viewport.localScale = Vector3.one;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.pivot = new Vector2(0f, 1f);
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = new Vector2(-reservedWidth, 0f);

        RectTransform content = scroll.content;
        content.localScale = Vector3.one;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, content.sizeDelta.y);
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (scroll.verticalScrollbar == null) return;
        RectTransform scrollbar = (RectTransform)scroll.verticalScrollbar.transform;
        scrollbar.localScale = Vector3.one;
        scrollbar.anchorMin = new Vector2(1f, 0f);
        scrollbar.anchorMax = Vector2.one;
        scrollbar.pivot = new Vector2(1f, 0.5f);
        scrollbar.anchoredPosition = Vector2.zero;
        scrollbar.sizeDelta = new Vector2(scrollbarWidth, 0f);
    }

    /// <summary>
    /// 在通用 Normalize/Theme 完成后固化行囊灰阶表面，并移除旧的独立槽位皮肤覆盖。
    /// 动态槽位直接沿用通用 UI_Slot，保证所有库存界面保持同一视觉语言。
    /// </summary>
    private static void FinalizeModularInventoryBag(GameObject root)
    {
        ApplyModularBagSurface(root.GetComponent<Image>(), ModularBagOuter);
        ApplyNamedModularBagSurface(root.transform, "FWUI_Body", ModularBagOuter);
        ApplyNamedModularBagSurface(root.transform, "FWUI_Header", ModularBagSurface);
        ApplyNamedModularBagSurface(root.transform, "FWUI_Footer", ModularBagSurface);
        ApplyNamedModularBagSurface(root.transform, "FWUI_InnerField", ModularBagField);

        Image headerRule = FindNamedImage(root.transform, "FWUI_HeaderRule");
        if (headerRule != null)
            headerRule.color = ModularBagLine;

        Image accentRail = FindNamedImage(root.transform, "FWUI_AccentRail");
        if (accentRail != null)
            accentRail.color = ModularBagLine;

        TMP_Text title = FindRect(root.transform, "FWUI_标题")?.GetComponent<TMP_Text>();
        if (title != null)
            title.color = ModularBagText;

        TMP_Text carryWeightLabel = FindRect(root.transform, "FWUI_CarryWeightLabel")?.GetComponent<TMP_Text>();
        if (carryWeightLabel != null)
            carryWeightLabel.color = Color.black;
        TMP_Text carryWeightValue = FindRect(root.transform, "FWUI_CarryWeightValue")?.GetComponent<TMP_Text>();
        if (carryWeightValue != null)
            carryWeightValue.color = Color.black;
        TMP_Text carryVolumeLabel = FindRect(root.transform, "FWUI_CarryVolumeLabel")?.GetComponent<TMP_Text>();
        if (carryVolumeLabel != null)
            carryVolumeLabel.color = Color.black;
        TMP_Text carryVolumeValue = FindRect(root.transform, "FWUI_CarryVolumeValue")?.GetComponent<TMP_Text>();
        if (carryVolumeValue != null)
            carryVolumeValue.color = Color.black;

        ConfigureFooterCapacityProgress(FindRect(root.transform, "FWUI_CarryWeightProgress"), 22f);
        ConfigureFooterCapacityProgress(FindRect(root.transform, "FWUI_CarryVolumeProgress"), 246f);

        ConfigureModularBagButton(root.transform, "关闭", null, ModularBagText, false);
        ConfigureModularBagButton(root.transform, "整理", null, ModularBagText, false);

        InventorySlotVisualProfile profile = root.GetComponent<InventorySlotVisualProfile>();
        if (profile != null)
            UnityEngine.Object.DestroyImmediate(profile, true);

        Scrollbar scrollbar = root.GetComponentInChildren<Scrollbar>(true);
        if (scrollbar != null && scrollbar.targetGraphic is Image handle)
        {
            handle.sprite = null;
            handle.type = Image.Type.Simple;
            handle.preserveAspect = false;
            handle.color = new Color32(138, 138, 138, 240);
        }
    }

    /// <summary>将行囊框架统一为纯灰阶表面。</summary>
    private static void ApplyNamedModularBagSurface(Transform root, string objectName, Color color)
    {
        RectTransform rect = FindRect(root, objectName);
        if (rect != null)
            ApplyModularBagSurface(rect.GetComponent<Image>(), color);
    }

    /// <summary>移除旧九宫格 Sprite 并应用统一灰阶底色。</summary>
    private static void ApplyModularBagSurface(Image image, Color color)
    {
        if (image == null)
            return;

        image.sprite = null;
        image.type = Image.Type.Simple;
        image.preserveAspect = false;
        image.color = color;
    }

    /// <summary>让行囊操作按钮复用图集模块，不改变按钮节点、点击事件或导航。</summary>
    private static void ConfigureModularBagButton(
        Transform root,
        string buttonName,
        Sprite sprite,
        Color textColor,
        bool sliced)
    {
        RectTransform rect = FindDirectRect(root, buttonName);
        if (rect == null)
            return;

        Image image = rect.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = sprite;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = false;
            image.color = sprite != null ? Color.white : SurfaceRaised;
            image.pixelsPerUnitMultiplier = 1f;
        }

        TMP_Text label = rect.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.color = textColor;
    }

    /// <summary>固定行囊 Footer 容量文字的位置，保留右侧整理按钮的触控空间。</summary>
    private static void SetFooterCapacityText(RectTransform rect, float x, float width)
    {
        if (rect == null)
            return;

        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.sizeDelta = new Vector2(width, 30f);
    }

    /// <summary>在 Footer 左侧重建两条容量进度条，黑色文字位于填充层上方。</summary>
    private static void EnsureBagCarryCapacityFooter(Transform root)
    {
        RectTransform footer = FindRect(root, "FWUI_Footer");
        if (footer == null)
            throw new InvalidOperationException("UI_Bag.prefab 缺少 FWUI_Footer，无法安装携带容量显示。");

        DestroyGeneratedElement(footer, "FWUI_CarryWeightLabel");
        DestroyGeneratedElement(footer, "FWUI_CarryWeightValue");
        DestroyGeneratedElement(footer, "FWUI_CarryVolumeLabel");
        DestroyGeneratedElement(footer, "FWUI_CarryVolumeValue");
        DestroyGeneratedElement(footer, "FWUI_CarryWeightProgress");
        DestroyGeneratedElement(footer, "FWUI_CarryVolumeProgress");

        CreateFooterCapacityProgress("FWUI_CarryWeightProgress", footer, 22f);
        CreateFooterCapacityProgress("FWUI_CarryVolumeProgress", footer, 246f);

        TextMeshProUGUI weightLabel = CreateText(
            "FWUI_CarryWeightLabel",
            footer,
            "重量",
            16f,
            Color.black,
            FontStyles.Normal,
            TextAlignmentOptions.Left);
        SetFooterCapacityText(weightLabel.rectTransform, 32f, 42f);

        TextMeshProUGUI weightValue = CreateText(
            "FWUI_CarryWeightValue",
            footer,
            "-- / -- kg",
            16f,
            Color.black,
            FontStyles.Normal,
            TextAlignmentOptions.Left);
        SetFooterCapacityText(weightValue.rectTransform, 78f, 148f);

        TextMeshProUGUI volumeLabel = CreateText(
            "FWUI_CarryVolumeLabel",
            footer,
            "体积",
            16f,
            Color.black,
            FontStyles.Normal,
            TextAlignmentOptions.Left);
        SetFooterCapacityText(volumeLabel.rectTransform, 256f, 42f);

        TextMeshProUGUI volumeValue = CreateText(
            "FWUI_CarryVolumeValue",
            footer,
            "-- / -- L",
            16f,
            Color.black,
            FontStyles.Normal,
            TextAlignmentOptions.Left);
        SetFooterCapacityText(volumeValue.rectTransform, 302f, 148f);
    }

    private static void CreateFooterCapacityProgress(string objectName, Transform footer, float x)
    {
        Image background = CreateImage(objectName, footer, Color.white);
        RectTransform fillArea = CreateRect(objectName + "FillArea", background.transform);
        Image fill = CreateImage(objectName + "Fill", fillArea, FlatWorldUITheme.StatusBarDefault);
        Stretch(fill.rectTransform);

        Slider progress = background.gameObject.AddComponent<Slider>();
        progress.targetGraphic = background;
        progress.fillRect = fill.rectTransform;
        progress.handleRect = null;
        progress.direction = Slider.Direction.LeftToRight;
        progress.minValue = 0f;
        progress.maxValue = 1f;
        progress.wholeNumbers = false;
        progress.interactable = false;
        progress.transition = Selectable.Transition.None;
        progress.navigation = new Navigation { mode = Navigation.Mode.None };
        progress.SetValueWithoutNotify(0f);
        ConfigureFooterCapacityProgress(background.rectTransform, x);
    }

    private static void ConfigureFooterCapacityProgress(RectTransform rect, float x)
    {
        if (rect == null)
            return;

        // 浅色轨道由行囊持有，主题重建后恢复满高填充区，运行时无需补建视觉节点。
        SetFooterCapacityText(rect, x, 210f);
        rect.sizeDelta = new Vector2(210f, 34f);
        ApplyModularBagSurface(rect.GetComponent<Image>(), new Color32(230, 230, 222, 255));

        Slider progress = rect.GetComponent<Slider>();
        if (progress == null || progress.fillRect == null)
            return;

        RectTransform fillArea = progress.fillRect.parent as RectTransform;
        if (fillArea != null)
        {
            Stretch(fillArea);
            fillArea.offsetMin = new Vector2(2f, 2f);
            fillArea.offsetMax = new Vector2(-2f, -2f);
        }
        ApplyModularBagSurface(progress.fillRect.GetComponent<Image>(), FlatWorldUITheme.StatusBarDefault);
    }

    /// <summary>删除当前生成框架中的指定节点，供专用面板裁掉不需要的装饰文案。</summary>
    private static void DestroyGeneratedElement(Transform root, string objectName)
    {
        Transform target = FindTransform(root, objectName);
        if (target != null)
            UnityEngine.Object.DestroyImmediate(target.gameObject);
    }

    #endregion
}
