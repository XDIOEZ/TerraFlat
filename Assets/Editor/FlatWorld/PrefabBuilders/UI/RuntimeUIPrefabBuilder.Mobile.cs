// 按业务职责维护 Prefab，复用公共控件与布局实现。
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class RuntimeUIPrefabBuilder
{
    #region 手机操作界面

    /// <summary>
    /// 构建 Android 横屏的正式手机控制层。左右摇杆默认使用 33% 触控区，中间 34% 留空；按钮和攻击摇杆拥有更高射线优先级。
    /// </summary>
    private static GameObject BuildMobileControlsHUD()
    {
        GameObject root = CreateUIObject(RuntimeUIPrefabKeys.MobileControls, null, typeof(CanvasGroup));
        Stretch(root.GetComponent<RectTransform>());
        CanvasGroup rootGroup = root.GetComponent<CanvasGroup>();
        rootGroup.alpha = 1f;
        rootGroup.interactable = true;
        rootGroup.blocksRaycasts = true;

        GameObject aimCursor = CreateUIObject(
            "手机准线",
            root.transform,
            typeof(CanvasRenderer),
            typeof(GamepadCursorGraphic));
        SetCentered(aimCursor.GetComponent<RectTransform>(), Vector2.zero, new Vector2(28f, 28f));
        GamepadCursorGraphic aimCursorGraphic = aimCursor.GetComponent<GamepadCursorGraphic>();
        aimCursorGraphic.color = FlatWorldUITheme.AimCursor;
        aimCursorGraphic.raycastTarget = false;
        aimCursor.SetActive(false);

        GameObject gameplay = CreateUIObject("玩法控制层", root.transform);
        Stretch(gameplay.GetComponent<RectTransform>());

        // 手持物丢弃面位于所有玩法控件下方；平时不参与射线，只补齐三段区域中间的世界长按入口。
        GameObject heldItemDropSurface = CreateUIObject(
            "手持物丢弃区",
            gameplay.transform,
            typeof(Image),
            typeof(MobileHeldItemDropSurface));
        Stretch(heldItemDropSurface.GetComponent<RectTransform>());
        Image heldItemDropHit = heldItemDropSurface.GetComponent<Image>();
        heldItemDropHit.color = new Color(1f, 1f, 1f, 0.001f);
        heldItemDropHit.raycastTarget = true;
        heldItemDropSurface.GetComponent<MobileHeldItemDropSurface>()
            .Configure(onlyRaycastWhileHoldingItem: true);

        // 透明捕获层仅覆盖右侧默认配置区，按钮和攻击摇杆随后创建以获得更高射线优先级。
        GameObject aimZone = CreateUIObject(
            "普通指向区",
            gameplay.transform,
            typeof(Image),
            typeof(MobileHeldItemDropSurface));
        RectTransform aimRect = aimZone.GetComponent<RectTransform>();
        aimRect.anchorMin = new Vector2(1f - UIUserSettings.DefaultRightControlZoneRatio, 0f);
        aimRect.anchorMax = Vector2.one;
        aimRect.offsetMin = Vector2.zero;
        aimRect.offsetMax = Vector2.zero;
        Image aimCapture = aimZone.GetComponent<Image>();
        aimCapture.color = new Color(1f, 1f, 1f, 0.001f);
        aimCapture.raycastTarget = true;
        aimZone.GetComponent<MobileHeldItemDropSurface>()
            .Configure(onlyRaycastWhileHoldingItem: false);
        CreateJoystickVisual(aimZone.transform, Vector2.zero, 188f, floating: true);
        RectTransform aimHandle = aimZone.transform.Find("底座") as RectTransform;
        if (aimHandle != null)
        {
            MobileControlLayoutNode aimLayout =
                aimHandle.gameObject.AddComponent<MobileControlLayoutNode>();
            aimLayout.ConfigureJoystick(UIUserSettings.MobileAimControlId, aimRect, aimHandle,
                MobileVirtualJoystick.JoystickRole.Aim);
            aimLayout.ConfigureEditorGeometry(
                Vector2.one,
                Vector2.one,
                Vector2.one,
                new Vector2(-MobileActionRightMargin, -180f),
                new Vector2(MobileAttackZoneSize, MobileAttackZoneSize));
        }

        GameObject moveZone = CreateUIObject(
            "移动摇杆",
            gameplay.transform,
            typeof(Image),
            typeof(MobileHeldItemDropSurface));
        RectTransform moveRect = moveZone.GetComponent<RectTransform>();
        moveRect.anchorMin = Vector2.zero;
        moveRect.anchorMax = new Vector2(UIUserSettings.DefaultLeftControlZoneRatio, 1f);
        moveRect.offsetMin = Vector2.zero;
        moveRect.offsetMax = Vector2.zero;
        Image moveHit = moveZone.GetComponent<Image>();
        moveHit.color = new Color(1f, 1f, 1f, 0.001f);
        moveHit.raycastTarget = true;
        moveZone.GetComponent<MobileHeldItemDropSurface>()
            .Configure(onlyRaycastWhileHoldingItem: false);
        CreateJoystickVisual(moveZone.transform, Vector2.zero, 188f, floating: true);
        Transform moveHandle = moveZone.transform.Find("底座");
        if (moveHandle != null)
        {
            MobileControlLayoutNode moveLayout =
                moveHandle.gameObject.AddComponent<MobileControlLayoutNode>();
            moveLayout.ConfigureJoystick(UIUserSettings.MobileMoveControlId, moveRect,
                moveHandle as RectTransform, MobileVirtualJoystick.JoystickRole.Move);
            moveLayout.ConfigureEditorGeometry(
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
                new Vector2(MobileActionRightMargin, MobileActionBottomMargin),
                new Vector2(MobileAttackZoneSize, MobileAttackZoneSize));
        }

        // 右侧操作组以安全区右下角为唯一定位基准，组内所有控件使用局部坐标对齐。
        GameObject actionGroup = CreateUIObject("右侧操作组", gameplay.transform);
        RectTransform actionGroupRect = actionGroup.GetComponent<RectTransform>();
        SetBottomRight(
            actionGroupRect,
            MobileActionRightMargin,
            MobileActionBottomMargin,
            MobileActionGroupWidth,
            MobileActionGroupHeight);

        GameObject attackZone = CreateUIObject(
            "攻击摇杆",
            actionGroup.transform,
            typeof(Image),
            typeof(MobileHeldItemDropSurface));
        SetBottomRight(
            attackZone.GetComponent<RectTransform>(),
            (MobileActionGroupWidth - MobileAttackZoneSize) * 0.5f,
            0f,
            MobileAttackZoneSize,
            MobileAttackZoneSize);
        Image attackHit = attackZone.GetComponent<Image>();
        attackHit.color = new Color(1f, 1f, 1f, 0.001f);
        attackHit.raycastTarget = true;
        attackZone.GetComponent<MobileHeldItemDropSurface>()
            .Configure(onlyRaycastWhileHoldingItem: false);
        CreateJoystickVisual(attackZone.transform, Vector2.zero, 188f, floating: false);
        attackZone.AddComponent<MobileControlLayoutNode>()
            .ConfigureJoystick(
                UIUserSettings.MobileAttackControlId,
                attackZone.GetComponent<RectTransform>(),
                attackZone.transform.Find("底座") as RectTransform,
                MobileVirtualJoystick.JoystickRole.Attack);

        Button interactButton = CreateMobileButton(
            "交互",
            actionGroup.transform,
            "交互",
            Vector2.zero,
            new Vector2(0f, MobileAttackZoneSize + MobileActionGap),
            MobileActionButtonSize);
        interactButton.gameObject.AddComponent<MobileControlLayoutNode>()
            .Configure(
                UIUserSettings.MobileInteractControlId,
                interactButton.GetComponent<RectTransform>());
        Button useButton = CreateMobileButton(
            "使用",
            actionGroup.transform,
            "使用",
            new Vector2(1f, 0f),
            new Vector2(0f, MobileAttackZoneSize + MobileActionGap),
            MobileActionButtonSize);
        useButton.gameObject.AddComponent<MobileControlLayoutNode>()
            .Configure(
                UIUserSettings.MobileUseControlId,
                useButton.GetComponent<RectTransform>());
        Button rollButton = CreateMobileButton(
            "翻滚",
            gameplay.transform,
            "翻滚",
            new Vector2(0f, 0.5f),
            new Vector2(76f, 0f),
            104f);
        rollButton.gameObject.AddComponent<MobileControlLayoutNode>()
            .Configure(
                UIUserSettings.MobileRollControlId,
                rollButton.GetComponent<RectTransform>());

        // 设置与右侧菜单开关独立于玩法层，打开模态面板时仍能切换菜单。
        GameObject persistent = CreateUIObject("常驻控制层", root.transform);
        Stretch(persistent.GetComponent<RectTransform>());
        CreateMobileButton("设置", persistent.transform, "设置", new Vector2(1f, 1f), new Vector2(-58f, -58f), 100f);
        CreateMobileDrawerToggle(persistent.transform);

        // 快捷栏独立于玩法控制层，打开背包等模态面板时仍可见、可拖放。
        GameObject hotbarAnchor = CreateUIObject("快捷栏锚点", root.transform);
        RectTransform hotbarRect = hotbarAnchor.GetComponent<RectTransform>();
        hotbarRect.anchorMin = hotbarRect.anchorMax = new Vector2(0.5f, 0f);
        hotbarRect.pivot = new Vector2(0.5f, 0f);
        hotbarRect.anchoredPosition = new Vector2(0f, 16f);
        hotbarRect.sizeDelta = new Vector2(760f, 126f);
        CreateMobileHotbarSideButtons(hotbarAnchor.transform);

        BuildMobileDrawer(root.transform);
        return root;
    }

    /// <summary>构建全屏触屏布局编辑器；实际玩法控件视觉仍由 UI_MobileControls Prefab 提供。</summary>
    private static GameObject BuildMobileControlLayoutEditor()
    {
        GameObject root = CreateUIObject(
            RuntimeUIPrefabKeys.MobileControlLayoutEditor,
            null,
            typeof(Image),
            typeof(CanvasGroup),
            typeof(BasePanel),
            typeof(MobileControlLayoutEditor));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);
        Image backdrop = root.GetComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.72f);
        backdrop.raycastTarget = true;
        CanvasGroup group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        BasePanel panel = root.GetComponent<BasePanel>();
        panel.PanelName = RuntimeUIPrefabKeys.MobileControlLayoutEditor;
        panel.canvasGroup = group;
        panel.rectTransform = rootRect;
        ConfigureScaleAnimation(panel);

        GameObject preview = CreateUIObject("预览根", root.transform);
        Stretch(preview.GetComponent<RectTransform>());

        GameObject header = CreateUIObject("布局编辑顶部栏", root.transform, typeof(Image));
        RectTransform headerRect = header.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = Vector2.one;
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = Vector2.zero;
        headerRect.sizeDelta = new Vector2(0f, 82f);
        header.GetComponent<Image>().color = Canvas;
        AddOutline(header.GetComponent<Image>(), Border);

        TextMeshProUGUI title = CreateText(
            "标题文本",
            header.transform,
            "触屏布局",
            24f,
            Cream);
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.rectTransform.anchorMin = new Vector2(0f, 0f);
        title.rectTransform.anchorMax = new Vector2(0.32f, 1f);
        title.rectTransform.offsetMin = new Vector2(24f, 0f);
        title.rectTransform.offsetMax = Vector2.zero;
        title.raycastTarget = false;

        TextMeshProUGUI hint = CreateText(
            "尺寸文本",
            header.transform,
            "点选控件调整大小",
            18f,
            Muted);
        hint.alignment = TextAlignmentOptions.MidlineRight;
        hint.rectTransform.anchorMin = new Vector2(0.32f, 0f);
        hint.rectTransform.anchorMax = Vector2.one;
        hint.rectTransform.offsetMin = Vector2.zero;
        hint.rectTransform.offsetMax = new Vector2(-204f, 0f);
        hint.enableWordWrapping = false;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        hint.raycastTarget = false;
        Button decrease = CreateButton("缩小按钮", header.transform, "-", 76f, 60f, false);
        Button increase = CreateButton("放大按钮", header.transform, "+", 76f, 60f, false);
        RectTransform decreaseRect = decrease.GetComponent<RectTransform>();
        RectTransform increaseRect = increase.GetComponent<RectTransform>();
        decreaseRect.anchorMin = decreaseRect.anchorMax = new Vector2(1f, 0.5f);
        increaseRect.anchorMin = increaseRect.anchorMax = new Vector2(1f, 0.5f);
        decreaseRect.anchoredPosition = new Vector2(-146f, 0f);
        increaseRect.anchoredPosition = new Vector2(-62f, 0f);
        decreaseRect.sizeDelta = increaseRect.sizeDelta = new Vector2(76f, 60f);

        GameObject opacityRow = CreateUIObject("透明度设置栏", root.transform, typeof(Image));
        RectTransform opacityRowRect = opacityRow.GetComponent<RectTransform>();
        opacityRowRect.anchorMin = new Vector2(0.25f, 1f);
        opacityRowRect.anchorMax = new Vector2(0.75f, 1f);
        opacityRowRect.pivot = new Vector2(0.5f, 1f);
        opacityRowRect.anchoredPosition = new Vector2(0f, -94f);
        opacityRowRect.sizeDelta = new Vector2(0f, 62f);
        opacityRow.GetComponent<Image>().color = Canvas;
        HorizontalLayoutGroup opacityLayout = opacityRow.AddComponent<HorizontalLayoutGroup>();
        opacityLayout.padding = new RectOffset(16, 16, 10, 10);
        opacityLayout.spacing = 12f;
        opacityLayout.childAlignment = TextAnchor.MiddleLeft;
        opacityLayout.childControlWidth = true;
        opacityLayout.childControlHeight = true;
        opacityLayout.childForceExpandWidth = false;
        opacityLayout.childForceExpandHeight = false;
        CreateRowLabel(opacityRow.transform, "触屏控件透明度", 128f);
        Slider opacitySlider = CreateSlider("触屏控件透明度", opacityRow.transform);
        opacitySlider.minValue = UIUserSettings.MinimumTouchControlsOpacityPercent;
        opacitySlider.maxValue = UIUserSettings.MaximumTouchControlsOpacityPercent;
        opacitySlider.wholeNumbers = true;
        opacitySlider.value = UIUserSettings.DefaultTouchControlsOpacityPercent;
        TextMeshProUGUI opacityValue = CreateText(
            "触屏控件透明度数值", opacityRow.transform,
            $"{Mathf.RoundToInt(UIUserSettings.DefaultTouchControlsOpacityPercent)}%", 16f, Amber);
        opacityValue.alignment = TextAlignmentOptions.MidlineRight;
        opacityValue.raycastTarget = false;
        opacityValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

        GameObject footer = CreateUIObject("布局编辑底部栏", root.transform, typeof(Image));
        RectTransform footerRect = footer.GetComponent<RectTransform>();
        footerRect.anchorMin = Vector2.zero;
        footerRect.anchorMax = new Vector2(1f, 0f);
        footerRect.pivot = new Vector2(0.5f, 0f);
        footerRect.anchoredPosition = Vector2.zero;
        footerRect.sizeDelta = new Vector2(0f, 86f);
        footer.GetComponent<Image>().color = Canvas;
        AddOutline(footer.GetComponent<Image>(), Border);

        HorizontalLayoutGroup footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.padding = new RectOffset(24, 24, 12, 12);
        footerLayout.spacing = 12f;
        footerLayout.childAlignment = TextAnchor.MiddleCenter;
        footerLayout.childControlWidth = true;
        footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = false;
        footerLayout.childForceExpandHeight = true;

        TextMeshProUGUI status = CreateText(
            "状态文本",
            footer.transform,
            "拖动控件后点击保存。",
            15f,
            Muted);
        status.alignment = TextAlignmentOptions.MidlineLeft;
        status.enableWordWrapping = false;
        status.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement statusLayout = status.gameObject.AddComponent<LayoutElement>();
        statusLayout.flexibleWidth = 1f;
        statusLayout.minWidth = 0f;
        status.raycastTarget = false;

        CreateButton("恢复默认按钮", footer.transform, "恢复默认", 132f, 60f, false);
        CreateButton("取消按钮", footer.transform, "取消", 112f, 60f, false);
        CreateButton("保存按钮", footer.transform, "保存", 112f, 60f, true);
        return root;
    }

    private static void CreateJoystickVisual(Transform parent, Vector2 position, float diameter, bool floating)
    {
        GameObject baseObject = CreateUIObject("底座", parent, typeof(Image), typeof(CanvasGroup));
        RectTransform baseRect = baseObject.GetComponent<RectTransform>();
        SetCentered(baseRect, position, new Vector2(diameter, diameter));
        Image baseImage = baseObject.GetComponent<Image>();
        baseImage.color = new Color(SurfaceRaised.r, SurfaceRaised.g, SurfaceRaised.b, 0.64f);
        baseImage.raycastTarget = false;
        AddOutline(baseImage, floating ? new Color(0f, 0f, 0f, 0f) : Border);
        CanvasGroup baseGroup = baseObject.GetComponent<CanvasGroup>();
        baseGroup.alpha = floating ? 0f : 1f;
        baseGroup.interactable = false;
        baseGroup.blocksRaycasts = false;

        GameObject knobObject = CreateUIObject("摇杆", baseObject.transform, typeof(Image));
        RectTransform knobRect = knobObject.GetComponent<RectTransform>();
        SetCentered(knobRect, Vector2.zero, new Vector2(82f, 82f));
        Image knobImage = knobObject.GetComponent<Image>();
        knobImage.color = new Color(Teal.r, Teal.g, Teal.b, 0.92f);
        knobImage.raycastTarget = false;
    }

    /// <summary>创建右侧居中的三角开关，图形方向由 HUD 随抽屉开关状态旋转。</summary>
    private static void CreateMobileDrawerToggle(Transform parent)
    {
        GameObject root = CreateUIObject("菜单", parent, typeof(Image), typeof(Button));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-10f, 0f);
        rect.sizeDelta = new Vector2(MobileDrawerToggleWidth, 100f);

        Image background = root.GetComponent<Image>();
        background.color = SurfaceRaised;
        AddOutline(background, Border);
        Button button = root.GetComponent<Button>();
        button.targetGraphic = background;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ConfigureButtonColors(button);
        root.AddComponent<FlatWorldUIFeedback>();

        GameObject arrow = CreateUIObject("菜单箭头", root.transform, typeof(CanvasRenderer), typeof(UITriangleGraphic));
        SetCentered(arrow.GetComponent<RectTransform>(), Vector2.zero, new Vector2(22f, 28f));
        UITriangleGraphic graphic = arrow.GetComponent<UITriangleGraphic>();
        graphic.color = Cream;
        graphic.raycastTarget = false;
    }

    /// <summary>构建右侧居中的窄抽屉，纵向占安全区 44%；按钮与缩放控件统一放入可裁剪滚动列表。</summary>
    private static void BuildMobileDrawer(Transform parent)
    {
        GameObject drawer = CreateUIObject("菜单抽屉", parent, typeof(Image));
        RectTransform drawerRect = drawer.GetComponent<RectTransform>();
        drawerRect.anchorMin = new Vector2(1f, 0.28f);
        drawerRect.anchorMax = new Vector2(1f, 0.72f);
        drawerRect.pivot = new Vector2(1f, 0.5f);
        drawerRect.anchoredPosition = new Vector2(-MobileDrawerToggleWidth - 20f, 0f);
        drawerRect.sizeDelta = new Vector2(MobileDrawerWidth, 0f);
        Image background = drawer.GetComponent<Image>();
        background.color = Canvas;
        background.raycastTarget = true;
        AddOutline(background, Amber);

        GameObject scrollObject = CreateUIObject("菜单滚动区", drawer.transform, typeof(ItemStepScrollRect));
        RectTransform scrollRect = scrollObject.GetComponent<RectTransform>();
        Stretch(scrollRect);
        scrollRect.offsetMin = new Vector2(16f, 16f);
        scrollRect.offsetMax = new Vector2(-16f, -16f);

        GameObject viewport = CreateUIObject("Viewport", scrollObject.transform, typeof(Image), typeof(RectMask2D));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewport.GetComponent<Image>().color = Color.clear;
        viewport.GetComponent<Image>().raycastTarget = true;

        GameObject content = CreateUIObject("抽屉按钮区", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;

        foreach (string buttonName in new[] { "装备" })
        {
            Button button = CreateButton(buttonName, content.transform, buttonName, MobileDrawerWidth - 32f, MobileDrawerButtonHeight, false);
            SetButtonLabelSize(button, 22f);
        }

        // 缩放也是列表条目，窄屏或大 UI 缩放时可滚动到达，不占用固定页脚。
        GameObject zoomRow = CreateUIObject("镜头缩放区", content.transform, typeof(LayoutElement));
        zoomRow.GetComponent<LayoutElement>().preferredHeight = 110f;
        TextMeshProUGUI zoomLabel = CreateText("镜头缩放标签", zoomRow.transform, "镜头缩放", 20f, Cream);
        zoomLabel.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform zoomLabelRect = zoomLabel.rectTransform;
        zoomLabelRect.anchorMin = new Vector2(0f, 1f);
        zoomLabelRect.anchorMax = new Vector2(1f, 1f);
        zoomLabelRect.pivot = new Vector2(0.5f, 1f);
        zoomLabelRect.anchoredPosition = Vector2.zero;
        zoomLabelRect.sizeDelta = new Vector2(0f, 34f);

        Slider zoomSlider = CreateSlider("镜头缩放", zoomRow.transform);
        zoomSlider.minValue = 5f;
        zoomSlider.maxValue = 20f;
        zoomSlider.value = 10f;
        zoomSlider.wholeNumbers = false;
        RectTransform zoomRect = zoomSlider.GetComponent<RectTransform>();
        zoomRect.anchorMin = new Vector2(0f, 0f);
        zoomRect.anchorMax = new Vector2(1f, 0f);
        zoomRect.pivot = new Vector2(0.5f, 0f);
        zoomRect.offsetMin = new Vector2(0f, 4f);
        zoomRect.offsetMax = new Vector2(0f, 64f);
        drawer.SetActive(false);
    }

    /// <summary>创建快捷栏左右两侧的常驻入口：左侧制作、右侧背包；运行时挂入真实快捷栏 Canvas。</summary>
    private static void CreateMobileHotbarSideButtons(Transform hotbarAnchor)
    {
        CreateMobileHotbarSideButton(hotbarAnchor, "制作", false, false);
        CreateMobileHotbarSideButton(hotbarAnchor, "背包", true, true);
    }

    /// <summary>创建单个快捷栏侧边入口，并排除十格布局控制。</summary>
    private static void CreateMobileHotbarSideButton(
        Transform hotbarAnchor,
        string name,
        bool rightSide,
        bool accent)
    {
        Button button = CreateButton(
            name,
            hotbarAnchor,
            name,
            MobileHotbarSideButtonSize,
            MobileHotbarSideButtonSize,
            accent);
        LayoutElement layout = button.GetComponent<LayoutElement>();
        layout.ignoreLayout = true;

        RectTransform rect = button.GetComponent<RectTransform>();
        float edge = rightSide ? 1f : 0f;
        rect.anchorMin = rect.anchorMax = new Vector2(edge, 0.5f);
        rect.pivot = new Vector2(rightSide ? 0f : 1f, 0.5f);
        rect.anchoredPosition = new Vector2(
            rightSide ? MobileHotbarSideButtonGap : -MobileHotbarSideButtonGap,
            0f);
        rect.sizeDelta = Vector2.one * MobileHotbarSideButtonSize;
        SetButtonLabelSize(button, 14f);
    }

    private static Button CreateMobileButton(
        string name,
        Transform parent,
        string caption,
        Vector2 anchor,
        Vector2 position,
        float size)
    {
        Button button = CreateButton(name, parent, caption, size, size, name == "交互" || name == "使用");
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(size, size);

        return button;
    }

    #endregion
}
