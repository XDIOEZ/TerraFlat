// AI-Context: FlatWorld 全局 UI 视觉规范；集中处理面板、按钮、输入框、滑条、槽位和文字风格，不承载任何业务逻辑。

using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 从主界面提炼出的全局视觉系统。
/// 设计目标是低对比深色底、温暖的关键操作色，以及保留游戏物品像素美术本身。
/// </summary>
public static class FlatWorldUITheme
{
    public static readonly Color Canvas = Hex("08151D", 0.96f);
    public static readonly Color SurfaceLow = Hex("0B1B24", 0.96f);
    public static readonly Color Surface = Hex("102730", 0.97f);
    public static readonly Color SurfaceRaised = Hex("183640", 0.98f);
    public static readonly Color Border = Hex("829395", 0.24f);
    public static readonly Color TextPrimary = Hex("F2E9D6");
    public static readonly Color TextSecondary = Hex("A8B5B5");
    public static readonly Color Accent = Hex("D47E3A");
    public static readonly Color AccentHover = Hex("E59B59");
    // 手柄/键盘导航选中态使用独立强调色，避免与鼠标悬停态混淆。
    public static readonly Color Selection = Hex("F0A35A");
    public static readonly Color SelectionOutline = Hex("FFE0A3", 0.98f);
    public static readonly Vector2 SelectionOutlineDistance = new Vector2(3f, -3f);
    public static readonly Color Teal = Hex("4D9E95");
    public static readonly Color Danger = Hex("A94F45");

    private static readonly string[] BespokePanelNames =
    {
        "UI_Hello",
        "UI_GameSaveManager",
        "UI_NewGame",
        "NewGame",
        "UI_NetworkMode"
    };

    private static readonly string[] PrimaryActionWords =
    {
        "开始", "创建", "确认", "确定", "加载", "合成", "制作", "重生", "应用", "保存"
    };

    private static readonly string[] DestructiveActionWords =
    {
        "删除", "销毁", "丢弃", "断开", "主菜单"
    };

    private static readonly string[] PanelWords =
    {
        "背景", "底板", "面板", "窗口", "卡片", "Background", "Panel", "Window"
    };

    // 只有明确属于设置/难度面板的滑块才允许参与手柄导航。
    private static readonly string[] GamepadInteractiveSliderRoots =
    {
        "UI_NewGame", "NewGame", "UI_AudioSettings", "UI_InterfaceSettings", "Settings", "Setting", "设置", "难度"
    };

    // 常驻 HUD 不应成为手柄焦点；快捷栏由玩家输入动作和自身选中框驱动。
    private static readonly string[] GamepadNavigationExcludedRootNames =
    {
        "UI_HotBar"
    };

    private static readonly (string Key, string Title, string Eyebrow)[] WindowTitles =
    {
        ("UI_Bag", "行囊", "INVENTORY  /  随身物资"),
        ("UI_Equipment", "装备", "EQUIPMENT  /  生存配置"),
        ("UI_HandCraftTable", "手工制作", "CRAFTING  /  基础工艺"),
        ("UI_MakerTable", "制作台", "WORKBENCH  /  精细工艺"),
        ("UI_Furnace", "熔炉", "FURNACE  /  冶炼作业"),
        ("UI_BoneFire", "篝火", "BONFIRE  /  火源管理"),
        ("UI_CompostBin", "堆肥箱", "COMPOST  /  资源循环"),
        ("UI_MeatRack", "晾肉架", "MEAT RACK  /  食物处理"),
        ("UI_FireDrill", "钻木取火", "FIRECRAFT  /  生火作业"),
        ("UI_FlintStrike", "燧石取火", "FIRECRAFT  /  生火作业"),
        ("UI_GameModuleUI", "生存状态", "SURVIVAL  /  模块状态"),
        ("物品信息面板", "物品详情", "ITEM  /  观察记录"),
        ("Info_Button_List", "功能列表", "ACTIONS  /  快捷入口"),
        ("UI_Debug", "调试面板", "DEVELOPMENT  /  运行信息")
    };

    /// <summary>
    /// 将主题应用到一个 UI 子树。方法可重复调用，不会重复添加描边组件。
    /// </summary>
    public static void Apply(Transform root)
    {
        if (root == null)
            return;

        FlatWorldAudioUIFeedback.EnsureFor(root);
        ApplySelectionColors(root);

        if (UsesBespokeVisuals(root))
            return;

        bool isHud = IsHud(root.name);

        StyleImages(root, isHud);
        StyleButtons(root);
        StyleInputFields(root);
        StyleSliders(root);
        StyleToggles(root);
        StyleScrollbars(root);
        StyleTexts(root);
        DecoratePanel(root, isHud);
    }

    /// <summary>
    /// 统一设置所有可导航控件的选中颜色，保留悬停色以区分鼠标悬停与手柄焦点。
    /// </summary>
    public static void ApplySelectionColors(Transform root)
    {
        if (root == null)
            return;

        Selectable[] selectables = root.GetComponentsInChildren<Selectable>(true);
        ApplySelectionColors(selectables);
    }

    /// <summary>复用面板可选控件快照设置选中颜色与导航策略。</summary>
    public static void ApplySelectionColors(IReadOnlyList<Selectable> selectables)
    {
        if (selectables == null)
            return;

        for (int i = 0; i < selectables.Count; i++)
        {
            Selectable selectable = selectables[i];
            if (selectable == null)
                continue;

            ColorBlock colors = selectable.colors;
            colors.selectedColor = Selection;
            selectable.colors = colors;
        }

        ApplyGamepadNavigationPolicy(selectables);
    }

    /// <summary>
    /// 排除纯显示滑块和滚动条，避免它们消耗手柄导航输入或成为默认焦点。
    /// </summary>
    public static void ApplyGamepadNavigationPolicy(Transform root)
    {
        if (root == null)
            return;

        Selectable[] selectables = root.GetComponentsInChildren<Selectable>(true);
        ApplyGamepadNavigationPolicy(selectables);
    }

    /// <summary>复用面板可选控件快照应用导航排除策略。</summary>
    public static void ApplyGamepadNavigationPolicy(IReadOnlyList<Selectable> selectables)
    {
        if (selectables == null)
            return;

        for (int i = 0; i < selectables.Count; i++)
        {
            Selectable selectable = selectables[i];
            if (!IsGamepadNavigationExcluded(selectable))
                continue;

            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.None;
            navigation.selectOnUp = null;
            navigation.selectOnDown = null;
            navigation.selectOnLeft = null;
            navigation.selectOnRight = null;
            selectable.navigation = navigation;
        }

        for (int i = 0; i < selectables.Count; i++)
        {
            Selectable selectable = selectables[i];
            if (selectable == null || IsGamepadNavigationExcluded(selectable))
                continue;

            Navigation navigation = selectable.navigation;
            if (navigation.mode != Navigation.Mode.Explicit)
                continue;

            bool changed = false;
            if (navigation.selectOnUp != null && IsGamepadNavigationExcluded(navigation.selectOnUp))
            {
                navigation.selectOnUp = null;
                changed = true;
            }

            if (navigation.selectOnDown != null && IsGamepadNavigationExcluded(navigation.selectOnDown))
            {
                navigation.selectOnDown = null;
                changed = true;
            }

            if (navigation.selectOnLeft != null && IsGamepadNavigationExcluded(navigation.selectOnLeft))
            {
                navigation.selectOnLeft = null;
                changed = true;
            }

            if (navigation.selectOnRight != null && IsGamepadNavigationExcluded(navigation.selectOnRight))
            {
                navigation.selectOnRight = null;
                changed = true;
            }

            if (changed)
                selectable.navigation = navigation;

            // 旧 Prefab 中常见的“空显式导航”无法移动焦点，运行时统一降级为自动导航。
            if (navigation.mode == Navigation.Mode.Explicit &&
                navigation.selectOnUp == null &&
                navigation.selectOnDown == null &&
                navigation.selectOnLeft == null &&
                navigation.selectOnRight == null)
            {
                navigation.mode = Navigation.Mode.Automatic;
                selectable.navigation = navigation;
            }
        }

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return;

        Selectable currentSelection = eventSystem.currentSelectedGameObject != null
            ? eventSystem.currentSelectedGameObject.GetComponent<Selectable>()
            : null;
        if (currentSelection != null && IsGamepadNavigationExcluded(currentSelection))
            eventSystem.SetSelectedGameObject(null);
    }

    /// <summary>
    /// 判断控件是否不应成为手柄/键盘导航焦点。
    /// </summary>
    public static bool IsGamepadNavigationExcluded(Selectable selectable)
    {
        if (selectable == null)
            return false;

        if (IsUnderGamepadNavigationExcludedRoot(selectable.transform))
            return true;

        if (selectable is Scrollbar)
            return true;

        if (selectable is Slider slider)
            return !IsGamepadInteractiveSlider(slider);

        return false;
    }

    /// <summary>判断控件是否属于不参与手柄焦点的常驻 HUD。</summary>
    private static bool IsUnderGamepadNavigationExcludedRoot(Transform target)
    {
        for (Transform current = target; current != null; current = current.parent)
        {
            for (int i = 0; i < GamepadNavigationExcludedRootNames.Length; i++)
            {
                if (string.Equals(
                        current.name,
                        GamepadNavigationExcludedRootNames[i],
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 设置面板滑块保留手柄调节能力，其他滑块默认按纯显示控件处理。
    /// </summary>
    private static bool IsGamepadInteractiveSlider(Slider slider)
    {
        if (slider == null)
            return false;

        Transform current = slider.transform;
        while (current != null)
        {
            if (ContainsAny(current.name, GamepadInteractiveSliderRoots))
                return true;

            current = current.parent;
        }

        return false;
    }

    private static void StyleImages(Transform root, bool isHud)
    {
        Image[] images = root.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (image == null || IsProtectedArtwork(image.transform))
                continue;

            if (image.GetComponent<Selectable>() != null)
                continue;

            bool isRoot = image.transform == root;
            string objectName = image.name;

            if (IsFillGraphic(image))
            {
                StyleSemanticFill(image);
                continue;
            }

            if (IsSlotTransform(image.transform))
            {
                image.color = Hex("26383A", 0.98f);
                AddOutline(image, Hex("D7A163", 0.30f));
                continue;
            }

            if (isRoot && !isHud)
            {
                image.color = Canvas;
                AddOutline(image, Hex("D7A163", 0.26f));
                continue;
            }

            if (ContainsAny(objectName, "窗口信息", "标题栏", "Header", "TitleBar"))
            {
                image.color = SurfaceRaised;
                continue;
            }

            if (ContainsAny(objectName, PanelWords))
            {
                image.color = IsNestedSurface(image.transform, root) ? Surface : SurfaceLow;
                AddOutline(image, Border);
            }
        }
    }

    private static void StyleButtons(Transform root)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button == null)
                continue;

            Graphic target = button.targetGraphic != null ? button.targetGraphic : button.GetComponent<Graphic>();
            bool slotButton = IsSlotTransform(button.transform);
            bool primary = ContainsAny(button.name, PrimaryActionWords);
            bool destructive = ContainsAny(button.name, DestructiveActionWords);
            bool close = ContainsAny(button.name, "关闭", "返回", "Close", "Back");

            if (target != null)
            {
                if (slotButton)
                    target.color = Hex("273A3C", 0.98f);
                else if (destructive)
                    target.color = Hex("51282A", 0.96f);
                else if (primary)
                    target.color = Hex("A95829", 0.98f);
                else if (close)
                    target.color = Hex("172B32", 0.96f);
                else
                    target.color = SurfaceRaised;

                if (!slotButton)
                    AddOutline(target, destructive ? Hex("D87968", 0.30f) : primary ? Hex("F1B06C", 0.36f) : Border);
            }

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = slotButton
                ? Hex("F4D6A2")
                : primary ? Hex("FFD7A8") : Hex("D5E3DF");
            colors.pressedColor = Hex("B9C3C0", 0.82f);
            colors.selectedColor = slotButton
                ? Hex("E2B878")
                : primary ? AccentHover : destructive ? Hex("E06F5E") : Selection;
            colors.disabledColor = Hex("777D7C", 0.48f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.11f;
            button.colors = colors;

            TextMeshProUGUI[] labels = button.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI label in labels)
            {
                label.color = TextPrimary;
                if (!slotButton)
                    label.fontStyle |= FontStyles.Bold;
            }
        }
    }

    private static void StyleInputFields(Transform root)
    {
        TMP_InputField[] fields = root.GetComponentsInChildren<TMP_InputField>(true);
        foreach (TMP_InputField field in fields)
        {
            if (field == null)
                continue;

            Graphic background = field.targetGraphic != null ? field.targetGraphic : field.GetComponent<Graphic>();
            if (background != null)
            {
                background.color = SurfaceLow;
                AddOutline(background, Border);
            }

            if (field.textComponent != null)
                field.textComponent.color = TextPrimary;

            if (field.placeholder is TextMeshProUGUI placeholder)
                placeholder.color = Hex("849294", 0.82f);

            field.caretColor = TextPrimary;
            field.selectionColor = Hex("D47E3A", 0.42f);
            field.customCaretColor = true;

            ColorBlock colors = field.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Hex("E6D5BB");
            colors.selectedColor = Selection;
            colors.disabledColor = Hex("707879", 0.52f);
            colors.fadeDuration = 0.11f;
            field.colors = colors;
        }
    }

    private static void StyleSliders(Transform root)
    {
        bool keepInputHandle = ContainsAny(root.name, "UI_Canvas", "Settings", "Setting", "设置");
        Slider[] sliders = root.GetComponentsInChildren<Slider>(true);
        foreach (Slider slider in sliders)
        {
            if (slider == null)
                continue;

            Image background = FindNamedImage(slider.transform, "Background", "背景", "底板");
            if (background != null)
            {
                background.color = SurfaceLow;
                background.sprite = null;
                background.type = Image.Type.Simple;
                background.preserveAspect = false;

                RectTransform backgroundRect = background.rectTransform;
                backgroundRect.anchorMin = new Vector2(0f, 0.25f);
                backgroundRect.anchorMax = new Vector2(1f, 0.75f);
                backgroundRect.anchoredPosition = Vector2.zero;
                backgroundRect.sizeDelta = Vector2.zero;
            }

            if (slider.fillRect != null)
            {
                Image fill = slider.fillRect.GetComponent<Image>();
                if (fill != null)
                {
                    fill.color = IsHealthName(slider.name) ? Danger : Accent;
                    fill.sprite = null;
                    fill.type = Image.Type.Simple;
                    fill.preserveAspect = false;
                    fill.raycastTarget = false;
                }

                RectTransform fillRect = slider.fillRect;
                fillRect.anchoredPosition = Vector2.zero;
                fillRect.sizeDelta = Vector2.zero;

                if (fillRect.parent is RectTransform fillArea)
                {
                    fillArea.anchorMin = new Vector2(0f, 0.25f);
                    fillArea.anchorMax = new Vector2(1f, 0.75f);
                    fillArea.anchoredPosition = Vector2.zero;
                    fillArea.sizeDelta = Vector2.zero;
                }
            }

            if (slider.handleRect != null)
            {
                Image handle = slider.handleRect.GetComponent<Image>();
                if (!keepInputHandle)
                {
                    slider.handleRect.gameObject.SetActive(false);
                }
                else if (handle != null)
                {
                    slider.handleRect.gameObject.SetActive(true);
                    slider.handleRect.sizeDelta = new Vector2(7f, 0f);
                    handle.color = TextPrimary;
                    handle.sprite = null;
                    handle.type = Image.Type.Simple;
                    handle.preserveAspect = false;
                    handle.raycastTarget = false;
                    AddOutline(handle, Hex("08151D", 0.54f));
                }
            }
        }
    }

    private static void StyleToggles(Transform root)
    {
        Toggle[] toggles = root.GetComponentsInChildren<Toggle>(true);
        foreach (Toggle toggle in toggles)
        {
            if (toggle == null)
                continue;

            Graphic background = toggle.targetGraphic != null ? toggle.targetGraphic : toggle.GetComponent<Graphic>();
            if (background != null)
            {
                background.color = SurfaceLow;
                AddOutline(background, Border);
            }

            if (toggle.graphic != null)
                toggle.graphic.color = Accent;
        }
    }

    private static void StyleScrollbars(Transform root)
    {
        Scrollbar[] scrollbars = root.GetComponentsInChildren<Scrollbar>(true);
        foreach (Scrollbar scrollbar in scrollbars)
        {
            if (scrollbar == null)
                continue;

            Image background = scrollbar.GetComponent<Image>();
            if (background != null)
                background.color = SurfaceLow;

            if (scrollbar.targetGraphic != null)
                scrollbar.targetGraphic.color = Hex("637679", 0.94f);
        }
    }

    private static void StyleTexts(Transform root)
    {
        TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI text in texts)
        {
            if (text == null || IsProtectedArtwork(text.transform))
                continue;

            if (text.GetComponentInParent<Button>() != null)
                continue;

            if (HasSemanticColor(text.color))
                continue;

            bool heading = text.fontSize >= 24f || ContainsAny(text.name, "标题", "信息", "Title", "Header");
            text.color = heading ? TextPrimary : TextSecondary;
            if (heading)
                text.fontStyle |= FontStyles.Bold;
            text.raycastTarget = false;
        }
    }

    private static void StyleSemanticFill(Image image)
    {
        string fullName = BuildPath(image.transform);
        if (IsHealthName(fullName))
            image.color = Danger;
        else if (ContainsAny(fullName, "食物", "饱食", "饥饿", "Food"))
            image.color = Accent;
        else if (ContainsAny(fullName, "体力", "耐力", "Stamina", "睡眠"))
            image.color = Teal;
        else
            image.color = Accent;
    }

    private static void DecoratePanel(Transform root, bool isHud)
    {
        if (isHud)
            return;

        // 结构级重构后的 Prefab 已自带完整框架；避免运行时再叠加旧版“只换色”标题条。
        if (root.Find("FWUI_Chrome") != null)
            return;

        Transform existingChrome = root.Find("UITheme_Chrome");
        if (existingChrome != null)
        {
            UpdateExistingChrome(root, existingChrome);
            return;
        }

        RectTransform rootRect = root as RectTransform;
        if (rootRect == null)
            return;

        float width = Mathf.Max(Mathf.Abs(rootRect.rect.width), Mathf.Abs(rootRect.sizeDelta.x));
        float height = Mathf.Max(Mathf.Abs(rootRect.rect.height), Mathf.Abs(rootRect.sizeDelta.y));
        if (width < 360f || height < 240f)
            return;

        TMP_FontAsset font = FindFont(root);
        if (font == null)
            return;

        if (root.name.IndexOf("UI_Death", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            AddDeathPresentation(root, font);
            return;
        }

        if (!TryGetWindowTitle(root.name, out string title, out string eyebrow))
            return;

        RectTransform chrome = CreateRect("UITheme_Chrome", root);
        Stretch(chrome);
        chrome.SetAsLastSibling();

        Image header = CreateImage("UITheme_Header", chrome, Hex("17313A", 0.90f));
        header.raycastTarget = false;
        header.rectTransform.anchorMin = new Vector2(0f, 1f);
        header.rectTransform.anchorMax = new Vector2(1f, 1f);
        header.rectTransform.pivot = new Vector2(0.5f, 1f);
        header.rectTransform.anchoredPosition = Vector2.zero;
        header.rectTransform.sizeDelta = new Vector2(0f, 44f);

        Image accent = CreateImage("UITheme_Accent", chrome, Accent);
        accent.raycastTarget = false;
        SetRect(accent.rectTransform, new Vector2(0f, 0f), new Vector2(4f, 44f), new Vector2(0f, 1f));

        TextMeshProUGUI titleText = CreateText("UITheme_Title", chrome, title, font, 18f, TextPrimary, FontStyles.Bold);
        SetRect(titleText.rectTransform, new Vector2(18f, -3f), new Vector2(Mathf.Max(240f, width * 0.55f), 24f), new Vector2(0f, 1f));

        TextMeshProUGUI eyebrowText = CreateText("UITheme_Eyebrow", chrome, eyebrow, font, 9.5f, AccentHover, FontStyles.Bold);
        eyebrowText.characterSpacing = 2f;
        SetRect(eyebrowText.rectTransform, new Vector2(18f, -25f), new Vector2(Mathf.Max(280f, width * 0.62f), 16f), new Vector2(0f, 1f));

        Image divider = CreateImage("UITheme_Divider", chrome, Hex("D4A263", 0.28f));
        divider.raycastTarget = false;
        divider.rectTransform.anchorMin = new Vector2(0f, 1f);
        divider.rectTransform.anchorMax = new Vector2(1f, 1f);
        divider.rectTransform.pivot = new Vector2(0.5f, 1f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, -44f);
        divider.rectTransform.sizeDelta = new Vector2(0f, 1f);
    }

    private static void UpdateExistingChrome(Transform root, Transform chrome)
    {
        if (root.name.IndexOf("UI_Death", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        if (!TryGetWindowTitle(root.name, out string title, out string eyebrow))
            return;

        Image header = chrome.Find("UITheme_Header")?.GetComponent<Image>();
        Image accent = chrome.Find("UITheme_Accent")?.GetComponent<Image>();
        Image divider = chrome.Find("UITheme_Divider")?.GetComponent<Image>();
        TextMeshProUGUI titleText = chrome.Find("UITheme_Title")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI eyebrowText = chrome.Find("UITheme_Eyebrow")?.GetComponent<TextMeshProUGUI>();

        if (header != null)
            header.rectTransform.sizeDelta = new Vector2(0f, 44f);

        if (accent != null)
            SetRect(accent.rectTransform, Vector2.zero, new Vector2(4f, 44f), new Vector2(0f, 1f));

        if (titleText != null)
        {
            titleText.text = FlatWorldLocalizationService.GetUiText(title);
            titleText.fontSize = 18f;
            SetRect(titleText.rectTransform, new Vector2(18f, -3f), new Vector2(titleText.rectTransform.sizeDelta.x, 24f), new Vector2(0f, 1f));
        }

        if (eyebrowText != null)
        {
            eyebrowText.text = FlatWorldLocalizationService.GetUiText(eyebrow);
            eyebrowText.fontSize = 9.5f;
            SetRect(eyebrowText.rectTransform, new Vector2(18f, -25f), new Vector2(eyebrowText.rectTransform.sizeDelta.x, 16f), new Vector2(0f, 1f));
        }

        if (divider != null)
            divider.rectTransform.anchoredPosition = new Vector2(0f, -44f);
    }

    private static void AddDeathPresentation(Transform root, TMP_FontAsset font)
    {
        RectTransform chrome = CreateRect("UITheme_Chrome", root);
        Stretch(chrome);
        chrome.SetAsLastSibling();

        Image veil = CreateImage("UITheme_DeathVeil", chrome, Hex("071219", 0.38f));
        Stretch(veil.rectTransform);
        veil.raycastTarget = false;

        TextMeshProUGUI title = CreateText(
            "UITheme_Title",
            chrome,
            "旅程暂告一段落",
            font,
            34f,
            TextPrimary,
            FontStyles.Bold,
            TextAlignmentOptions.Center);
        SetRect(title.rectTransform, new Vector2(0f, -78f), new Vector2(620f, 48f), new Vector2(0.5f, 1f));

        TextMeshProUGUI subtitle = CreateText(
            "UITheme_Subtitle",
            chrome,
            "整理呼吸，再次回到这片世界。",
            font,
            16f,
            TextSecondary,
            FontStyles.Normal,
            TextAlignmentOptions.Center);
        SetRect(subtitle.rectTransform, new Vector2(0f, -130f), new Vector2(620f, 30f), new Vector2(0.5f, 1f));

        Image accent = CreateImage("UITheme_Accent", chrome, Accent);
        accent.raycastTarget = false;
        SetRect(accent.rectTransform, new Vector2(0f, -168f), new Vector2(76f, 3f), new Vector2(0.5f, 1f));
    }

    private static bool TryGetWindowTitle(string rootName, out string title, out string eyebrow)
    {
        foreach ((string key, string mappedTitle, string mappedEyebrow) in WindowTitles)
        {
            if (rootName.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            title = mappedTitle;
            eyebrow = mappedEyebrow;
            return true;
        }

        title = null;
        eyebrow = null;
        return false;
    }

    private static TMP_FontAsset FindFont(Transform root)
    {
        TextMeshProUGUI text = root.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null && text.font != null)
            return text.font;

        return TMP_Settings.defaultFontAsset;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        string value,
        TMP_FontAsset font,
        float size,
        Color color,
        FontStyles style,
        TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        if (ContainsChinese(value))
        {
            LocalizedTextBinder binder = go.AddComponent<LocalizedTextBinder>();
            binder.Configure(
                FlatWorldLocalizationService.UiTable,
                FlatWorldLocalizationService.GetUiTextKey(value),
                value);
        }

        return text;
    }

    private static bool ContainsChinese(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (char character in value)
        {
            if (character >= '\u4E00' && character <= '\u9FFF')
                return true;
        }

        return false;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rect.anchorMin = pivot;
        rect.anchorMax = pivot;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static bool UsesBespokeVisuals(Transform root)
    {
        string rootName = root.name;
        foreach (string panelName in BespokePanelNames)
        {
            if (rootName.IndexOf(panelName, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static bool IsHud(string rootName)
    {
        return ContainsAny(
            rootName,
            "HotBar", "SelectBox", "UI_Hand", "UI_HP", "UI_Food", "UI_Sleep", "UI_Canvas", "世界面板", "WorldUI");
    }

    private static bool IsProtectedArtwork(Transform transform)
    {
        if (transform == null)
            return false;

        // 槽位的根图参与主题，槽位内部的 Image 通常是物品图标，需要保留原色。
        Transform current = transform.parent;
        while (current != null)
        {
            if (IsSlotTransformDirect(current))
                return true;
            current = current.parent;
        }

        return false;
    }

    private static bool IsSlotTransform(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            if (IsSlotTransformDirect(current))
                return true;
            current = current.parent;
        }

        return false;
    }

    private static bool IsSlotTransformDirect(Transform transform)
    {
        if (transform == null)
            return false;

        if (ContainsAny(transform.name, "UI_Slot", "物品槽", "ItemSlot"))
            return true;

        Component[] components = transform.GetComponents<Component>();
        foreach (Component component in components)
        {
            if (component == null)
                continue;

            string typeName = component.GetType().Name;
            if (typeName.IndexOf("ItemSlot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.Equals("Slot_UI", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsFillGraphic(Image image)
    {
        string objectName = image.name;
        return ContainsAny(objectName, "Fill", "Progress", "进度", "血量", "食物", "体力", "耐力");
    }

    private static bool IsNestedSurface(Transform transform, Transform root)
    {
        Transform parent = transform.parent;
        return parent != null && parent != root;
    }

    private static bool IsHealthName(string value)
    {
        return ContainsAny(value, "血量", "生命", "Health", "HP");
    }

    private static bool HasSemanticColor(Color color)
    {
        Color.RGBToHSV(color, out _, out float saturation, out _);
        return saturation > 0.40f;
    }

    private static Image FindNamedImage(Transform root, params string[] names)
    {
        Image[] images = root.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (ContainsAny(image.name, names))
                return image;
        }

        return null;
    }

    private static void AddOutline(Graphic graphic, Color color)
    {
        if (graphic == null)
            return;

        Outline outline = graphic.GetComponent<Outline>();
        if (outline == null)
            outline = graphic.gameObject.AddComponent<Outline>();

        outline.effectColor = color;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;
    }

    private static bool ContainsAny(string value, params string[] words)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (string word in words)
        {
            if (!string.IsNullOrEmpty(word) && value.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static string BuildPath(Transform transform)
    {
        string result = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            result = current.name + "/" + result;
            current = current.parent;
        }

        return result;
    }

    private static Color Hex(string rgb, float alpha = 1f)
    {
        if (ColorUtility.TryParseHtmlString("#" + rgb, out Color color))
        {
            color.a = alpha;
            return color;
        }

        return Color.white;
    }
}
