// 按业务职责维护 Prefab，复用公共控件与布局实现。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static FlatWorldEditorUiLayout;

public static partial class RuntimeUIPrefabBuilder
{
    #region 公共控件构建

    /// <summary>创建带页内纵向滚动的可嵌入设置页。</summary>
    private static GameObject CreateSettingsPageRoot(string name, out Transform content)
    {
        GameObject root = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(ItemStepScrollRect));
        RectTransform rect = root.GetComponent<RectTransform>();
        Stretch(rect);

        Image image = root.GetComponent<Image>();
        image.color = Canvas;
        image.raycastTarget = true;

        GameObject viewport = CreateUIObject(
            "PageViewport",
            root.transform,
            typeof(RectMask2D));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);

        GameObject contentObject = CreateUIObject("PageContent", viewport.transform);
        RectTransform contentRect = contentObject.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 18);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        contentObject.AddComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = root.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 26f;
        scroll.viewport = viewportRect;
        scroll.content = contentRect;

        content = contentObject.transform;
        return root;
    }

    /// <summary>创建由纵向布局直接铺满内容区的可嵌入设置页。</summary>
    private static GameObject CreateFixedSettingsPageRoot(string name)
    {
        GameObject root = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        Stretch(root.GetComponent<RectTransform>());

        Image image = root.GetComponent<Image>();
        image.color = Canvas;
        image.raycastTarget = true;

        VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 18);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return root;
    }

    /// <summary>创建无可见蒙板的居中模态面板；透明根节点只负责拦截背景输入。</summary>
    private static GameObject CreateModalPanelRoot(string name, Vector2 size)
    {
        GameObject root = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(BasePanel));
        Stretch(root.GetComponent<RectTransform>());

        Image overlay = root.GetComponent<Image>();
        overlay.color = Color.clear;
        overlay.raycastTarget = true;
        ConfigureBasePanel(root);

        GameObject dialog = CreateUIObject("设置对话框", root.transform, typeof(Image));
        SetCentered(dialog.GetComponent<RectTransform>(), Vector2.zero, size);
        Image dialogImage = dialog.GetComponent<Image>();
        dialogImage.color = Canvas;
        dialogImage.raycastTarget = true;
        AddOutline(dialogImage, Amber);

        VerticalLayoutGroup layout = dialog.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 18);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return root;
    }

    private static void ConfigureBasePanel(GameObject root)
    {
        BasePanel panel = root.GetComponent<BasePanel>();
        panel.PanelName = root.name;
        panel.canvasGroup = root.GetComponent<CanvasGroup>();
        panel.rectTransform = root.GetComponent<RectTransform>();
        ConfigureScaleAnimation(panel);
    }

    /// <summary>让编辑器构建的正式面板与手工维护 Prefab 共用纯缩放开场。</summary>
    public static void ConfigureScaleAnimation(BasePanel panel)
    {
        if (panel == null)
            return;

        BaseUIAnimation currentAnimation = panel.GetComponent<BaseUIAnimation>();
        ScaleUIAnimation scaleAnimation = currentAnimation as ScaleUIAnimation;
        if (scaleAnimation == null)
        {
            if (currentAnimation != null)
                Object.DestroyImmediate(currentAnimation);
            scaleAnimation = panel.gameObject.AddComponent<ScaleUIAnimation>();
        }

        RectTransform motionRoot = panel.rectTransform != null
            ? panel.rectTransform
            : panel.GetComponent<RectTransform>();
        scaleAnimation.SetAnimationId("panel.scale");
        scaleAnimation.SetMotionRoot(motionRoot);
    }

    /// <summary>主菜单设置页使用移动端可读标题栏，不影响游戏内紧凑设置窗口。</summary>
    private static void CreateMainMenuSettingsHeader(Transform parent, string title, string closeButtonName)
    {
        GameObject header = CreateUIObject("标题", parent, typeof(Image));
        header.GetComponent<Image>().color = MainMenuSettingsSurface;
        header.AddComponent<LayoutElement>().preferredHeight = 82f;

        HorizontalLayoutGroup layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 12, 9, 9);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI titleText = CreateText("标题文本", header.transform, title, 32f, Cream);
        titleText.fontStyle = FontStyles.Bold;
        titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Button closeButton = CreateButton(closeButtonName, header.transform, "关闭", 120f, 64f, false);
        closeButton.GetComponent<Image>().color = new Color(0.058f, 0.047f, 0.043f, 1f);
        SetButtonLabelSize(closeButton, 20f);
    }

    /// <summary>创建不含独立关闭按钮的内嵌设置页标题栏。</summary>
    private static void CreateSettingsHeader(Transform parent, string title)
    {
        GameObject header = CreateUIObject("标题", parent, typeof(Image));
        header.GetComponent<Image>().color = SurfaceRaised;
        header.AddComponent<LayoutElement>().preferredHeight = 76f;

        HorizontalLayoutGroup layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 12, 8, 8);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI titleText = CreateText("标题文本", header.transform, title, 25f, Cream);
        titleText.fontStyle = FontStyles.Bold;
        titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
    }

    private static void CreateHint(Transform parent, string value, float height)
    {
        TextMeshProUGUI hint = CreateText("说明文本", parent, value, 13f, Muted);
        hint.enableWordWrapping = true;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
    }

    /// <summary>设置子分页专用的大字号说明文本。</summary>
    private static void CreateSettingsHint(Transform parent, string value, float height)
    {
        TextMeshProUGUI hint = CreateText("说明文本", parent, value, 16f, Muted);
        hint.enableWordWrapping = true;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
    }

    private static GameObject CreateRow(string name, Transform parent, float height)
    {
        GameObject row = CreateUIObject(name, parent);
        row.AddComponent<LayoutElement>().preferredHeight = height;
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    /// <summary>创建设置页的小节标题，统一使用琥珀色强调。</summary>
    private static void CreateSettingsSection(Transform parent, string title, string eyebrow)
    {
        GameObject section = CreateUIObject(title + "设置分组", parent, typeof(Image));
        LayoutElement element = section.AddComponent<LayoutElement>();
        element.preferredHeight = 46f;

        Image background = section.GetComponent<Image>();
        background.color = new Color(0.07f, 0.15f, 0.17f, 0.72f);
        AddOutline(background, new Color(0.55f, 0.68f, 0.70f, 0.16f));

        HorizontalLayoutGroup layout = section.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 3, 3);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI titleText = CreateText(title + "分组标题", section.transform, title, 17f, Amber);
        titleText.fontStyle = FontStyles.Bold;
        titleText.gameObject.AddComponent<LayoutElement>().preferredWidth = 96f;

        if (!string.IsNullOrWhiteSpace(eyebrow))
        {
            TextMeshProUGUI eyebrowText = CreateText(title + "分组英文", section.transform, eyebrow, 12f, Muted);
            eyebrowText.characterSpacing = 2f;
            eyebrowText.alignment = TextAlignmentOptions.MidlineRight;
            eyebrowText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }
    }

    /// <summary>创建设置页下拉项，并写入仅用于展示的默认选项。</summary>
    private static TMP_Dropdown CreateSettingsDropdownRow(
        Transform parent,
        string label,
        string dropdownName,
        string[] options)
    {
        GameObject row = CreateRow(label + "行", parent, 58f);
        CreateRowLabel(row.transform, label, 122f);

        TMP_Dropdown dropdown = CreateDropdown(dropdownName, row.transform);
        LayoutElement element = dropdown.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = 1040f;
        element.preferredHeight = 52f;
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string>(options));
        dropdown.value = 0;
        dropdown.RefreshShownValue();
        return dropdown;
    }

    private static void CreateRowLabel(Transform parent, string value, float width)
    {
        TextMeshProUGUI label = CreateText(value + "标签", parent, value, 17f, Cream);
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
    }

    private static void CreateSliderRow(Transform parent, string label, string sliderName)
    {
        GameObject row = CreateRow(label + "行", parent, 72f);
        CreateRowLabel(row.transform, label, 112f);
        Slider slider = CreateSlider(sliderName, row.transform);
        slider.value = 1f;
        TextMeshProUGUI value = CreateText(sliderName + "_数值", row.transform, "100%", 16f, Amber);
        value.alignment = TextAlignmentOptions.MidlineRight;
        value.gameObject.AddComponent<LayoutElement>().preferredWidth = 48f;
    }

    private static Slider CreateSlider(string name, Transform parent)
    {
        if (!buildingSharedControls)
            return InstantiateSharedControl<Slider>("UI_SliderControl", name, parent);

        GameObject root = CreateUIObject(name, parent, typeof(Image), typeof(Slider));
        LayoutElement layout = root.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.preferredHeight = 26f;
        Image background = root.GetComponent<Image>();
        background.color = FlatWorldUITheme.SurfaceLow;
        AddOutline(background, Border);

        GameObject fillArea = CreateUIObject("Fill Area", root.transform);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        Stretch(fillAreaRect);
        fillAreaRect.offsetMin = new Vector2(8f, 8f);
        fillAreaRect.offsetMax = new Vector2(-8f, -8f);

        Image fill = CreateImage("Fill", fillArea.transform, Teal);
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.sizeDelta = new Vector2(10f, 0f);

        GameObject handleArea = CreateUIObject("Handle Slide Area", root.transform);
        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        Stretch(handleAreaRect);
        handleAreaRect.offsetMin = new Vector2(5f, 0f);
        handleAreaRect.offsetMax = new Vector2(-5f, 0f);

        Image handle = CreateImage("Handle", handleArea.transform, Amber);
        handle.rectTransform.sizeDelta = new Vector2(16f, 26f);

        Slider slider = root.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.direction = Slider.Direction.LeftToRight;
        slider.fillRect = fillRect;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        return slider;
    }

    private static Toggle CreateToggle(string name, Transform parent)
    {
        if (!buildingSharedControls)
            return InstantiateSharedControl<Toggle>("UI_Toggle", name, parent);

        GameObject root = CreateUIObject(name, parent, typeof(Image), typeof(Toggle));
        LayoutElement element = root.AddComponent<LayoutElement>();
        element.preferredWidth = 58f;
        element.preferredHeight = 30f;
        Image background = root.GetComponent<Image>();
        background.color = SurfaceRaised;
        AddOutline(background, Border);

        Image checkmark = CreateImage("Checkmark", root.transform, Teal);
        RectTransform checkmarkRect = checkmark.rectTransform;
        checkmarkRect.anchorMin = checkmarkRect.anchorMax = new Vector2(0.5f, 0.5f);
        checkmarkRect.sizeDelta = new Vector2(42f, 18f);

        Toggle toggle = root.GetComponent<Toggle>();
        toggle.targetGraphic = background;
        toggle.graphic = checkmark;
        toggle.isOn = true;
        return toggle;
    }

    private static TMP_InputField CreateInputField(string name, Transform parent, string placeholderValue)
    {
        if (!buildingSharedControls)
        {
            TMP_InputField shared = InstantiateSharedControl<TMP_InputField>("UI_InputField", name, parent);
            if (shared.placeholder is TMP_Text placeholderText)
                placeholderText.text = placeholderValue;
            return shared;
        }

        GameObject root = CreateUIObject(name, parent, typeof(Image), typeof(TMP_InputField));
        Image background = root.GetComponent<Image>();
        background.color = Surface;
        AddOutline(background, Border);

        GameObject textArea = CreateUIObject("Text Area", root.transform, typeof(RectMask2D));
        RectTransform area = textArea.GetComponent<RectTransform>();
        Stretch(area);
        area.offsetMin = new Vector2(12f, 3f);
        area.offsetMax = new Vector2(-12f, -3f);

        TextMeshProUGUI valueText = CreateInputText("Text", textArea.transform, string.Empty, Cream);
        TextMeshProUGUI placeholder = CreateInputText("Placeholder", textArea.transform, placeholderValue, Muted);
        placeholder.fontStyle = FontStyles.Italic;

        TMP_InputField input = root.GetComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = area;
        input.textComponent = valueText;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        return input;
    }

    private static TMP_Dropdown CreateDropdown(string name, Transform parent)
    {
        if (!buildingSharedControls)
            return InstantiateSharedControl<TMP_Dropdown>("UI_Dropdown", name, parent);

        GameObject root = CreateUIObject(name, parent, typeof(Image), typeof(TMP_Dropdown));
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(402f, 42f);
        Image background = root.GetComponent<Image>();
        background.color = Surface;
        AddOutline(background, Border);

        TextMeshProUGUI caption = CreateText("Label", root.transform, "每 10 分钟", 14f, Cream);
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(caption.rectTransform);
        caption.rectTransform.offsetMin = new Vector2(12f, 2f);
        caption.rectTransform.offsetMax = new Vector2(-42f, -2f);

        TextMeshProUGUI arrow = CreateText("Arrow", root.transform, "▼", 13f, Amber);
        arrow.alignment = TextAlignmentOptions.Center;
        arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
        arrow.rectTransform.anchorMax = Vector2.one;
        arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
        arrow.rectTransform.anchoredPosition = Vector2.zero;
        arrow.rectTransform.sizeDelta = new Vector2(36f, 0f);

        GameObject template = CreateUIObject("Template", root.transform, typeof(Image), typeof(ItemStepScrollRect));
        RectTransform templateRect = template.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0f, 0f);
        templateRect.anchorMax = new Vector2(1f, 0f);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.anchoredPosition = new Vector2(0f, -3f);
        templateRect.sizeDelta = new Vector2(0f, DropdownListLayout.MaxVisibleItems * 31f);
        template.GetComponent<Image>().color = Surface;
        AddOutline(template.GetComponent<Image>(), Border);

        GameObject viewport = CreateUIObject("Viewport", template.transform, typeof(RectMask2D));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMin = new Vector2(3f, 0f);
        viewportRect.offsetMax = new Vector2(-3f, 0f);

        GameObject content = CreateUIObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = Vector2.zero;

        GameObject item = CreateUIObject("Item", content.transform, typeof(Image), typeof(Toggle));
        RectTransform itemRect = item.GetComponent<RectTransform>();
        itemRect.sizeDelta = new Vector2(itemRect.sizeDelta.x, 31f);
        item.AddComponent<LayoutElement>().preferredHeight = 31f;
        Image itemBackground = item.GetComponent<Image>();
        itemBackground.color = SurfaceRaised;
        Image checkmark = CreateImage("Item Checkmark", item.transform, Amber);
        RectTransform checkmarkRect = checkmark.rectTransform;
        checkmarkRect.anchorMin = checkmarkRect.anchorMax = new Vector2(0f, 0.5f);
        checkmarkRect.pivot = new Vector2(0f, 0.5f);
        checkmarkRect.anchoredPosition = new Vector2(10f, 0f);
        checkmarkRect.sizeDelta = new Vector2(8f, 18f);
        TextMeshProUGUI itemLabel = CreateText("Item Label", item.transform, "选项", 13f, Cream);
        itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(itemLabel.rectTransform);
        itemLabel.rectTransform.offsetMin = new Vector2(28f, 1f);
        itemLabel.rectTransform.offsetMax = new Vector2(-8f, -1f);
        Toggle itemToggle = item.GetComponent<Toggle>();
        itemToggle.targetGraphic = itemBackground;
        itemToggle.graphic = checkmark;

        ScrollRect scroll = template.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.viewport = viewportRect;
        scroll.content = contentRect;

        TMP_Dropdown dropdown = root.GetComponent<TMP_Dropdown>();
        dropdown.targetGraphic = background;
        dropdown.captionText = caption;
        dropdown.template = templateRect;
        dropdown.itemText = itemLabel;
        template.SetActive(false);
        DropdownListLayout.EnsureFor(templateRect);
        return dropdown;
    }

    /// <summary>把设置页下拉框扩为移动端稳定触发的尺寸，避免在滚动页中把点按误判为拖动。</summary>
    private static void ConfigureTouchSettingsDropdown(TMP_Dropdown dropdown, float preferredWidth)
    {
        LayoutElement rootLayout = dropdown.gameObject.AddComponent<LayoutElement>();
        rootLayout.preferredWidth = preferredWidth;
        rootLayout.preferredHeight = 64f;
        FlatWorldUITextLayout.Configure(dropdown.captionText, 20f);
        FlatWorldUITextLayout.Configure(dropdown.itemText, 19f);

        Transform item = dropdown.itemText.transform.parent;
        item.GetComponent<LayoutElement>().preferredHeight = 60f;
        RectTransform itemRect = item as RectTransform;
        if (itemRect != null)
            itemRect.sizeDelta = new Vector2(itemRect.sizeDelta.x, 60f);
        dropdown.template.sizeDelta = new Vector2(0f, DropdownListLayout.MaxVisibleItems * 60f);
    }

    private static void CreateDifficultyOption(Transform parent, GameDifficultyDefinition definition, bool selected)
    {
        GameObject option = CreateUIObject($"难度_{definition.Id}", parent, typeof(Image), typeof(Button));
        option.AddComponent<LayoutElement>().preferredHeight = 88f;
        Image image = option.GetComponent<Image>();
        image.color = selected ? Amber : Surface;
        AddOutline(image, selected ? Amber : Border);
        Button button = option.GetComponent<Button>();
        button.targetGraphic = image;
        ConfigureButtonColors(button);

        TextMeshProUGUI title = CreateText("名称", option.transform, definition.DisplayName, 20f, Cream);
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        SetTopStretch(title.rectTransform, new Vector2(18f, -42f), new Vector2(-18f, -8f));

        TextMeshProUGUI description = CreateText("说明", option.transform, definition.Description, 15f, Muted);
        description.enableWordWrapping = false;
        description.overflowMode = TextOverflowModes.Ellipsis;
        description.alignment = TextAlignmentOptions.MidlineLeft;
        description.rectTransform.anchorMin = new Vector2(0f, 0f);
        description.rectTransform.anchorMax = new Vector2(1f, 0f);
        description.rectTransform.offsetMin = new Vector2(18f, 8f);
        description.rectTransform.offsetMax = new Vector2(-18f, 42f);
    }

    private static Transform CreateBindingScrollView(Transform parent)
    {
        GameObject scrollRoot = CreateUIObject("绑定列表", parent, typeof(Image), typeof(ItemStepScrollRect));
        LayoutElement scrollElement = scrollRoot.AddComponent<LayoutElement>();
        scrollElement.minHeight = 180f;
        scrollElement.flexibleHeight = 1f;
        scrollRoot.GetComponent<Image>().color = Surface;

        GameObject viewport = CreateUIObject("Viewport", scrollRoot.transform, typeof(Image), typeof(RectMask2D));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMax = new Vector2(-18f, 0f);
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.001f);

        GameObject content = CreateUIObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(8, 8, 8, 8);
        contentLayout.spacing = 7f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject scrollbarObject = CreateUIObject("Scrollbar Vertical", scrollRoot.transform, typeof(Image), typeof(Scrollbar));
        RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = Vector2.one;
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = new Vector2(-14f, 2f);
        scrollbarRect.offsetMax = new Vector2(-2f, -2f);
        scrollbarObject.GetComponent<Image>().color = SurfaceRaised;
        GameObject slidingArea = CreateUIObject("Sliding Area", scrollbarObject.transform);
        Stretch(slidingArea.GetComponent<RectTransform>());
        Image handle = CreateImage("Handle", slidingArea.transform, Muted);
        Stretch(handle.rectTransform);
        Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.size = 0.35f;

        ScrollRect scroll = scrollRoot.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scroll.verticalScrollbarSpacing = 4f;
        return content.transform;
    }

    private static Transform CreateFooter(Transform parent)
    {
        GameObject footer = CreateUIObject("底部操作", parent);
        footer.AddComponent<LayoutElement>().preferredHeight = 56f;
        HorizontalLayoutGroup layout = footer.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        GameObject spacer = CreateUIObject("Spacer", footer.transform);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
        return footer.transform;
    }

    private static Button CreateButton(string name, Transform parent, string caption, float width, float height, bool primary)
    {
        if (!buildingSharedControls)
        {
            string key = name.Contains("关闭") ? "UI_CloseButton" :
                name.Contains("分页") || name.Contains("切换") || name.Contains("页签") ? "UI_TabButton" : "UI_Button";
            Button shared = InstantiateSharedControl<Button>(key, name, parent);
            LayoutElement sharedLayout = shared.GetComponent<LayoutElement>();
            sharedLayout.preferredWidth = width;
            sharedLayout.preferredHeight = height;
            shared.GetComponentInChildren<TextMeshProUGUI>(true).text = caption;
            return shared;
        }

        GameObject root = CreateUIObject(name, parent, typeof(Image), typeof(Button));
        LayoutElement element = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
        element.preferredWidth = width;
        element.preferredHeight = height;
        Button button = root.GetComponent<Button>();
        ConfigureButtonVisual(button, primary, caption);
        return button;
    }

    /// <summary>创建设置子分页专用的大字号按钮。</summary>
    private static Button CreateSettingsButton(
        string name,
        Transform parent,
        string caption,
        float width,
        float height,
        bool primary)
    {
        Button button = CreateButton(name, parent, caption, width, height, primary);
        SetButtonLabelSize(button, 16f);
        return button;
    }

    /// <summary>调整按钮文字字号，不改变业务节点与交互组件。</summary>
    private static void SetButtonLabelSize(Button button, float fontSize)
    {
        if (ReusableUIControl.OwnsVisuals(button))
            return;

        TextMeshProUGUI label = button != null
            ? button.GetComponentInChildren<TextMeshProUGUI>(true)
            : null;
        if (label != null)
            FlatWorldUITextLayout.Configure(label, fontSize);
    }

    private static void ConfigureButtonVisual(Button button, bool primary, string caption)
    {
        if (ReusableUIControl.OwnsVisuals(button))
        {
            button.GetComponentInChildren<TextMeshProUGUI>(true).text = caption;
            return;
        }

        Image image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
        image.color = primary ? Amber : SurfaceRaised;
        AddOutline(image, primary ? Amber : Border);
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ConfigureButtonColors(button);
        if (button.GetComponent<FlatWorldUIFeedback>() == null)
            button.gameObject.AddComponent<FlatWorldUIFeedback>();

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null)
            label = CreateText("Text (TMP)", button.transform, caption, 14f, Cream);
        label.text = caption;
        label.font = font;
        FlatWorldUITextLayout.Configure(label, 14f);
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        Stretch(label.rectTransform);
    }

    private static void ConfigureButtonColors(Button button)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.08f, 1f, 1f);
        colors.pressedColor = new Color(0.74f, 0.78f, 0.80f, 1f);
        colors.selectedColor = FlatWorldUITheme.Selection;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
        colors.fadeDuration = 0.1f;
        button.colors = colors;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string value, float size, Color color)
    {
        GameObject root = CreateUIObject(name, parent, typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        TextMeshProUGUI text = root.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        FlatWorldUITextLayout.Configure(text, size);
        text.raycastTarget = false;
        return text;
    }

    private static TextMeshProUGUI CreateInputText(string name, Transform parent, string value, Color color)
    {
        TextMeshProUGUI text = CreateText(name, parent, value, 25f, color);
        text.enableAutoSizing = true;
        text.fontSizeMin = FlatWorldUITextLayout.MinimumFontSize(25f);
        text.fontSizeMax = 25f;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(text.rectTransform);
        return text;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject root = CreateUIObject(name, parent, typeof(CanvasRenderer), typeof(Image));
        Image image = root.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static GameObject CreateUIObject(string name, Transform parent, params System.Type[] extraComponents)
    {
        System.Type[] components = new System.Type[extraComponents.Length + 1];
        components[0] = typeof(RectTransform);
        for (int i = 0; i < extraComponents.Length; i++)
            components[i + 1] = extraComponents[i];

        GameObject root = new GameObject(name, components);
        if (parent != null)
            root.transform.SetParent(parent, false);
        return root;
    }

    /// <summary>只在指定父节点的直接子级中查找命名节点。</summary>
    private static Transform FindDirectChild(Transform parent, string name)
    {
        if (parent == null)
            return null;

        for (int index = 0; index < parent.childCount; index++)
        {
            Transform child = parent.GetChild(index);
            if (child != null && child.name == name)
                return child;
        }

        return null;
    }

    private static void SetUILayerRecursively(GameObject root)
    {
        int layer = LayerMask.NameToLayer("UI");
        if (layer < 0)
            layer = 5;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            transforms[i].gameObject.layer = layer;
    }

    private static void SetTopRight(RectTransform rect, float right, float top, float width, float height)
    {
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-right, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void SetBottomLeft(RectTransform rect, float left, float bottom, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(left, bottom);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void SetBottomRight(RectTransform rect, float right, float bottom, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-right, bottom);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void CopyRect(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    #endregion
}
