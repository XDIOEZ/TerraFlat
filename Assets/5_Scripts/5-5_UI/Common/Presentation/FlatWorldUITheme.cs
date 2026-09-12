// AI-Context: FlatWorld 全局 UI 视觉规范；集中处理面板、按钮、输入框、滑条、槽位和文字风格，不承载任何业务逻辑。

using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 从主界面参考图提炼出的全局视觉系统。
/// 设计目标是中性灰低对比界面、近白文字、少量暖黄焦点色，并保留游戏物品像素美术本身。
/// </summary>
public static class FlatWorldUITheme
{
    public static readonly Color Canvas = Hex("343434", 0.97f);
    public static readonly Color SurfaceLow = Hex("3D3D3D", 0.98f);
    public static readonly Color Surface = Hex("494949", 0.98f);
    public static readonly Color SurfaceRaised = Hex("595959", 0.99f);
    public static readonly Color Border = Hex("FFFFFF", 0.13f);
    public static readonly Color TextPrimary = Hex("EEEEEE");
    public static readonly Color TextSecondary = Hex("C8C8C8");
    public static readonly Color Accent = Hex("D7C56A");
    public static readonly Color AccentHover = Hex("E4D986");
    // 手柄/键盘导航选中态保持灰阶，只用细暖黄描边区分焦点，避免整块控件变成彩色。
    public static readonly Color Selection = Hex("707070");
    public static readonly Color SelectionOutline = Hex("E1D57C", 0.96f);
    public static readonly Vector2 SelectionOutlineDistance = new Vector2(3f, -3f);
    // 普通 UI 边框统一使用 2 个参考像素，提高手机与高分辨率屏幕下的可辨识度。
    public static readonly Vector2 BorderOutlineDistance = new Vector2(2f, -2f);
    // 文字描边保持 1 个参考像素，避免像素字体被加粗成糊边。
    public static readonly Vector2 TextOutlineDistance = new Vector2(1f, -1f);
    // 手机玩法准线固定为纯白，不与暖黄的导航焦点视觉混用。
    public static readonly Color AimCursor = Color.white;
    public static readonly Color Teal = Hex("A4A4A4");
    public static readonly Color Danger = Hex("8A6662");

    private static readonly string[] BespokePanelNames =
    {
        // 主菜单本身就是本次灰阶参考图的源界面，保留其柔焦背景和专属排版。
        "UI_MainMenu"
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
        "背景", "底板", "面板", "窗口", "卡片", "遮罩", "内容区", "字段", "区域",
        "对话框", "抽屉", "分组", "页面", "顶部栏", "底座", "加载内容",
        "主卡", "存档区", "操作区", "身份区", "状态区", "设置区", "选项页", "详情区",
        "Background", "Panel", "Window", "Card", "Surface", "Field", "Viewport", "Plate", "Chrome", "Container", "Group",
        "FWUI_Body", "FWUI_Header", "FWUI_Footer", "FWUI_InnerField", "FWUI_Section_"
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
        ("UI_Bag", "行囊", string.Empty),
        ("UI_Equipment", "装备", string.Empty),
        ("UI_HandCraftTable", "手工制作", string.Empty),
        ("UI_MakerTable", "制作台", string.Empty),
        ("UI_Furnace", "熔炉", string.Empty),
        ("UI_Bonfire", "篝火", string.Empty),
        ("UI_CompostBin", "堆肥箱", string.Empty),
        ("UI_MeatRack", "晾肉架", string.Empty),
        ("UI_FireDrill", "钻木取火", string.Empty),
        ("UI_FlintStrike", "燧石取火", string.Empty),
        ("UI_ModuleList", "生存状态", string.Empty),
        ("UI_ActionList", "功能列表", string.Empty),
        ("UI_Debug", "调试面板", string.Empty)
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
        StyleDropdowns(root);
        StyleLegacyDropdowns(root);
        StyleSliders(root);
        StyleToggles(root);
        StyleScrollbars(root);
        StyleTexts(root);
        StyleExistingEffects(root);
        DecoratePanel(root, isHud);
    }

    /// <summary>只同步现有 UI Outline 的厚度；供保留专属视觉的主菜单等 Prefab 使用。</summary>
    public static void ApplyBorderThickness(Transform root)
    {
        if (root == null)
            return;

        Outline[] outlines = root.GetComponentsInChildren<Outline>(true);
        foreach (Outline outline in outlines)
        {
            if (outline == null)
                continue;

            Graphic graphic = outline.GetComponent<Graphic>();
            outline.effectDistance = graphic is TextMeshProUGUI
                ? TextOutlineDistance
                : BorderOutlineDistance;
        }
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

            if (IsMobileControlInputSurface(root, image))
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

            if (ContainsAny(objectName, "遮罩", "Scrim", "Blocker", "Overlay"))
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = Hex("242424", 0.72f);
                continue;
            }

            if (image.GetComponent<ScrollRect>() != null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = SurfaceLow;
                AddOutline(image, Border);
                continue;
            }

            if (IsSlotTransform(image.transform))
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = Surface;
                AddOutline(image, Border);
                continue;
            }

            if (isRoot && !isHud)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                // 世界加载页是玩法画面的硬遮挡层，根图必须保持完全不透明，
                // 否则统一主题的半透明 Canvas 会让快捷栏、摇杆等在加载阶段透出来。
                image.color = IsOpaqueLoadingRoot(root)
                    ? new Color(Canvas.r, Canvas.g, Canvas.b, 1f)
                    : Canvas;
                AddOutline(image, Border);
                continue;
            }

            if (isRoot && isHud && string.Equals(objectName, "UI_HotBar", StringComparison.OrdinalIgnoreCase))
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = Hex("343434", 0.82f);
                AddOutline(image, Border);
                continue;
            }

            if (isRoot && isHud && string.Equals(objectName, "UI_SelectBox", StringComparison.OrdinalIgnoreCase))
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = Accent;
                AddOutline(image, Hex("D7C56A", 0.24f));
                continue;
            }

            if (ContainsAny(objectName, "窗口信息", "标题栏", "标题背景", "标题", "Header", "TitleBar"))
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = SurfaceRaised;
                continue;
            }

            // 结构化窗口旧版的角落刻度属于装饰噪音；灰阶简约主题直接隐藏。
            if (ContainsAny(objectName, "FWUI_TickTop", "FWUI_TickBottom"))
            {
                image.color = Border;
                image.gameObject.SetActive(false);
                continue;
            }

            if (ContainsAny(objectName, "FWUI_SectionMarker_"))
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = Hex("D7C56A", 0.62f);
                continue;
            }

            if (ContainsAny(objectName, "摇杆", "Joystick"))
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = SurfaceRaised;
                AddOutline(image, Border);
                continue;
            }

            if (ContainsAny(
                    objectName,
                    "强调线", "状态线", "DeathAccent", "ModuleAccent",
                    "分隔线", "Divider", "HeaderRule", "SectionRule", "AccentRail"))
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = objectName.IndexOf("强调", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              objectName.IndexOf("状态线", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              objectName.IndexOf("Accent", StringComparison.OrdinalIgnoreCase) >= 0
                    ? Accent
                    : Border;
                continue;
            }

            if (ContainsAny(objectName, "DeathHorizon", "Horizon"))
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = Border;
                continue;
            }

            if (ContainsAny(objectName, "状态指示点", "StatusDot", "StatusIndicator"))
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = Teal;
                continue;
            }

            if (ContainsAny(objectName, "占位图标", "菜单箭头"))
            {
                image.color = TextSecondary;
                continue;
            }

            if (ContainsAny(objectName, PanelWords))
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.color = IsNestedSurface(image.transform, root) ? Surface : SurfaceLow;
                AddOutline(image, Border);
            }
        }
    }

    /// <summary>进入世界的加载页负责完整遮挡玩法画面，不能继承普通面板的半透明根色。</summary>
    private static bool IsOpaqueLoadingRoot(Transform root)
    {
        return root != null &&
               string.Equals(root.name, "UI_WorldLoading", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>手机大面积透明输入层只负责接收射线，统一主题不得把它们改成可见面板。</summary>
    private static bool IsMobileControlInputSurface(Transform root, Image image)
    {
        if (root == null || image == null ||
            !ContainsAny(root.name, "MobileControls") ||
            !image.raycastTarget || image.color.a > 0.01f)
        {
            return false;
        }

        string objectName = image.name;
        return string.Equals(objectName, "手持物丢弃区", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "普通指向区", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "移动摇杆", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "攻击摇杆", StringComparison.OrdinalIgnoreCase);
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
            bool hasDedicatedVisualState = button.GetComponent("GameSaveItemView") != null;
            bool primary = ContainsAny(button.name, PrimaryActionWords);
            bool destructive = ContainsAny(button.name, DestructiveActionWords);
            bool close = ContainsAny(button.name, "关闭", "返回", "Close", "Back");

            if (target != null)
            {
                if (target is Image targetImage)
                {
                    targetImage.sprite = null;
                    targetImage.type = Image.Type.Simple;
                    targetImage.preserveAspect = false;
                }

                if (slotButton)
                    target.color = Surface;
                else if (destructive)
                    target.color = Hex("505050", 0.98f);
                else if (primary)
                    target.color = Hex("626262", 0.99f);
                else if (close)
                    target.color = Hex("444444", 0.98f);
                else
                    target.color = SurfaceRaised;

                AddOutline(target, primary ? Hex("D7C56A", 0.24f) : Border);
            }

            // 某些旧按钮把交互 targetGraphic 指向子节点，根节点自身还残留一层旧色 Image。
            // 两层都归一，避免例如模块列表下拉按钮仍露出蓝绿色底。
            Image buttonRootImage = button.GetComponent<Image>();
            if (buttonRootImage != null && buttonRootImage != target)
            {
                buttonRootImage.sprite = null;
                buttonRootImage.type = Image.Type.Simple;
                buttonRootImage.preserveAspect = false;
                buttonRootImage.color = slotButton
                    ? Surface
                    : destructive ? Hex("505050", 0.98f)
                    : primary ? Hex("626262", 0.99f)
                    : close ? Hex("444444", 0.98f)
                    : SurfaceRaised;
                AddOutline(buttonRootImage, primary ? Hex("D7C56A", 0.24f) : Border);
            }

            // 存档条目的焦点/业务选中由 GameSaveItemView 自己维护；不能让 Button 再叠一层 Tint 状态。
            button.transition = hasDedicatedVisualState ? Selectable.Transition.None : Selectable.Transition.ColorTint;
            if (!hasDedicatedVisualState)
                button.spriteState = default;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Hex("F0F0F0");
            colors.pressedColor = Hex("BEBEBE");
            colors.selectedColor = slotButton ? Hex("E4D986") : Hex("E8E8E8");
            colors.disabledColor = Hex("777777", 0.50f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.11f;
            button.colors = colors;

            TextMeshProUGUI[] labels = button.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI label in labels)
            {
                // 物品槽内部 TMP 是数量角标而不是按钮标题，其颜色由槽位 Prefab 自己负责。
                if (slotButton)
                    continue;

                label.color = TextPrimary;
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
                if (background is Image backgroundImage)
                {
                    backgroundImage.sprite = null;
                    backgroundImage.type = Image.Type.Simple;
                    backgroundImage.preserveAspect = false;
                }
                background.color = SurfaceLow;
                AddOutline(background, Border);
            }

            if (field.textComponent != null)
                field.textComponent.color = TextPrimary;

            if (field.placeholder is TextMeshProUGUI placeholder)
                placeholder.color = Hex("A8A8A8", 0.82f);

            field.caretColor = TextPrimary;
            field.selectionColor = Hex("D7C56A", 0.32f);
            field.customCaretColor = true;

            ColorBlock colors = field.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Hex("E5E5E5");
            colors.selectedColor = Selection;
            colors.disabledColor = Hex("777777", 0.52f);
            colors.fadeDuration = 0.11f;
            field.colors = colors;
        }
    }

    private static void StyleDropdowns(Transform root)
    {
        TMP_Dropdown[] dropdowns = root.GetComponentsInChildren<TMP_Dropdown>(true);
        foreach (TMP_Dropdown dropdown in dropdowns)
        {
            if (dropdown == null)
                continue;

            Graphic background = dropdown.targetGraphic != null ? dropdown.targetGraphic : dropdown.GetComponent<Graphic>();
            if (background != null)
            {
                if (background is Image backgroundImage)
                {
                    backgroundImage.sprite = null;
                    backgroundImage.type = Image.Type.Simple;
                    backgroundImage.preserveAspect = false;
                }

                background.color = SurfaceRaised;
                AddOutline(background, Border);
            }

            if (dropdown.captionText != null)
                dropdown.captionText.color = TextPrimary;
            if (dropdown.itemText != null)
                dropdown.itemText.color = TextPrimary;

            ColorBlock colors = dropdown.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Hex("F0F0F0");
            colors.pressedColor = Hex("BEBEBE");
            colors.selectedColor = Hex("E8E8E8");
            colors.disabledColor = Hex("777777", 0.50f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.11f;
            dropdown.colors = colors;

            Image[] images = dropdown.GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                if (image == null || image == background)
                    continue;

                if (ContainsAny(image.name, "Arrow", "箭头"))
                    image.color = TextSecondary;
                else if (ContainsAny(image.name, "Checkmark", "勾选", "选中"))
                    image.color = Accent;
            }
        }
    }

    private static void StyleLegacyDropdowns(Transform root)
    {
        Dropdown[] dropdowns = root.GetComponentsInChildren<Dropdown>(true);
        foreach (Dropdown dropdown in dropdowns)
        {
            if (dropdown == null)
                continue;

            Graphic background = dropdown.targetGraphic != null ? dropdown.targetGraphic : dropdown.GetComponent<Graphic>();
            if (background != null)
            {
                if (background is Image backgroundImage)
                {
                    backgroundImage.sprite = null;
                    backgroundImage.type = Image.Type.Simple;
                    backgroundImage.preserveAspect = false;
                }

                background.color = SurfaceRaised;
                AddOutline(background, Border);
            }

            if (dropdown.captionText != null)
                dropdown.captionText.color = TextPrimary;
            if (dropdown.itemText != null)
                dropdown.itemText.color = TextPrimary;

            ColorBlock colors = dropdown.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Hex("F0F0F0");
            colors.pressedColor = Hex("BEBEBE");
            colors.selectedColor = Hex("E8E8E8");
            colors.disabledColor = Hex("777777", 0.50f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.11f;
            dropdown.colors = colors;

            Image[] images = dropdown.GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                if (image == null || image == background)
                    continue;

                if (ContainsAny(image.name, "Arrow", "箭头"))
                    image.color = TextSecondary;
                else if (ContainsAny(image.name, "Checkmark", "勾选", "选中"))
                    image.color = Accent;
            }
        }
    }

    private static void StyleSliders(Transform root)
    {
        bool keepInputHandle = ContainsAny(root.name, "UI_ModuleSettings", "Settings", "Setting", "设置");
        Slider[] sliders = root.GetComponentsInChildren<Slider>(true);
        foreach (Slider slider in sliders)
        {
            if (slider == null)
                continue;

            Image background = FindNamedImage(slider.transform, "Background", "背景", "底板");
            if (background == null)
                background = slider.GetComponent<Image>();
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
                    if (handle != null)
                    {
                        handle.color = TextPrimary;
                        handle.sprite = null;
                        handle.type = Image.Type.Simple;
                        handle.preserveAspect = false;
                    }
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
                    AddOutline(handle, Hex("2A2A2A", 0.54f));
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
                if (background is Image backgroundImage)
                {
                    backgroundImage.sprite = null;
                    backgroundImage.type = Image.Type.Simple;
                    backgroundImage.preserveAspect = false;
                }
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
            {
                background.sprite = null;
                background.type = Image.Type.Simple;
                background.preserveAspect = false;
                background.color = SurfaceLow;
            }

            if (scrollbar.targetGraphic != null)
            {
                if (scrollbar.targetGraphic is Image handle)
                {
                    handle.sprite = null;
                    handle.type = Image.Type.Simple;
                    handle.preserveAspect = false;
                }
                scrollbar.targetGraphic.color = Hex("8A8A8A", 0.94f);
            }
        }
    }

    private static void StyleTexts(Transform root)
    {
        TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI text in texts)
        {
            if (text == null || IsProtectedArtwork(text.transform))
                continue;

            if (ContainsAny(text.name, "FWUI_眉题", "FWUI_SectionEyebrow_", "UITheme_Eyebrow"))
            {
                text.color = TextSecondary;
                text.gameObject.SetActive(false);
                continue;
            }

            if (text.GetComponentInParent<Button>() != null)
                continue;

            bool heading = text.fontSize >= 24f || ContainsAny(text.name, "标题", "信息", "Title", "Header");
            text.color = heading ? TextPrimary : TextSecondary;
            if (heading)
                text.fontStyle |= FontStyles.Bold;
            text.raycastTarget = false;
        }
    }

    private static void StyleExistingEffects(Transform root)
    {
        Outline[] outlines = root.GetComponentsInChildren<Outline>(true);
        foreach (Outline outline in outlines)
        {
            if (outline == null)
                continue;

            Graphic graphic = outline.GetComponent<Graphic>();
            if (graphic is TextMeshProUGUI)
            {
                outline.effectColor = Hex("242424", 0.72f);
            }
            else if (ContainsAny(outline.name, PrimaryActionWords) ||
                     ContainsAny(outline.name, "Accent", "强调", "UI_SelectBox"))
            {
                outline.effectColor = Hex("D7C56A", 0.24f);
            }
            else
            {
                outline.effectColor = Border;
            }

            outline.effectDistance = graphic is TextMeshProUGUI
                ? TextOutlineDistance
                : BorderOutlineDistance;
            outline.useGraphicAlpha = true;
        }
    }

    private static void StyleSemanticFill(Image image)
    {
        string fullName = BuildPath(image.transform);
        if (IsHealthName(fullName))
            image.color = Danger;
        else if (ContainsAny(fullName, "LongPress Hold", "长按放置"))
            image.color = Hex("D7C56A", 0.46f);
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

        Image header = CreateImage("UITheme_Header", chrome, SurfaceRaised);
        header.raycastTarget = false;
        header.rectTransform.anchorMin = new Vector2(0f, 1f);
        header.rectTransform.anchorMax = new Vector2(1f, 1f);
        header.rectTransform.pivot = new Vector2(0.5f, 1f);
        header.rectTransform.anchoredPosition = Vector2.zero;
        header.rectTransform.sizeDelta = new Vector2(0f, 40f);

        Image accent = CreateImage("UITheme_Accent", chrome, Accent);
        accent.raycastTarget = false;
        SetRect(accent.rectTransform, Vector2.zero, new Vector2(3f, 40f), new Vector2(0f, 1f));

        TextMeshProUGUI titleText = CreateText("UITheme_Title", chrome, title, font, 17f, TextPrimary, FontStyles.Bold);
        SetRect(titleText.rectTransform, new Vector2(16f, -8f), new Vector2(Mathf.Max(240f, width * 0.55f), 24f), new Vector2(0f, 1f));

        if (!string.IsNullOrEmpty(eyebrow))
        {
            TextMeshProUGUI eyebrowText = CreateText("UITheme_Eyebrow", chrome, eyebrow, font, 9.5f, AccentHover, FontStyles.Bold);
            eyebrowText.characterSpacing = 2f;
            SetRect(eyebrowText.rectTransform, new Vector2(16f, -24f), new Vector2(Mathf.Max(280f, width * 0.62f), 14f), new Vector2(0f, 1f));
        }

        Image divider = CreateImage("UITheme_Divider", chrome, Border);
        divider.raycastTarget = false;
        divider.rectTransform.anchorMin = new Vector2(0f, 1f);
        divider.rectTransform.anchorMax = new Vector2(1f, 1f);
        divider.rectTransform.pivot = new Vector2(0.5f, 1f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, -40f);
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
        {
            header.color = SurfaceRaised;
            header.rectTransform.sizeDelta = new Vector2(0f, 40f);
        }

        if (accent != null)
        {
            accent.color = Accent;
            SetRect(accent.rectTransform, Vector2.zero, new Vector2(3f, 40f), new Vector2(0f, 1f));
        }

        if (titleText != null)
        {
            titleText.text = FlatWorldLocalizationService.GetUiText(title);
            titleText.color = TextPrimary;
            titleText.fontSize = 17f;
            SetRect(titleText.rectTransform, new Vector2(16f, -8f), new Vector2(titleText.rectTransform.sizeDelta.x, 24f), new Vector2(0f, 1f));
        }

        if (eyebrowText != null)
        {
            eyebrowText.gameObject.SetActive(!string.IsNullOrEmpty(eyebrow));
            if (!string.IsNullOrEmpty(eyebrow))
            {
                eyebrowText.text = FlatWorldLocalizationService.GetUiText(eyebrow);
                eyebrowText.fontSize = 9.5f;
                SetRect(eyebrowText.rectTransform, new Vector2(16f, -24f), new Vector2(eyebrowText.rectTransform.sizeDelta.x, 14f), new Vector2(0f, 1f));
            }
        }

        if (divider != null)
        {
            divider.color = Border;
            divider.rectTransform.anchoredPosition = new Vector2(0f, -40f);
        }
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
            if (string.Equals(rootName, panelName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsHud(string rootName)
    {
        if (string.IsNullOrEmpty(rootName))
            return false;

        // 必须避免用 "UI_Hand" 的子串判断，否则 UI_HandCraftTable 会被误判成 HUD。
        if (string.Equals(rootName, "UI_Hand", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rootName, "UI_HotBar", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rootName, "UI_SelectBox", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rootName, "UI_Health", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rootName, "UI_Food", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rootName, "UI_Sleep", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rootName, "UI_ModuleSettings", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ContainsAny(
            rootName,
            "HUD", "PlayerWorldCoordinate", "SaveStatus", "BuffStatus", "QuestTracker",
            "SpeechBubble", "PlayerChatInput", "MobileControls", "RuntimeDebugOverlay",
            "世界面板", "WorldUI");
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
        outline.effectDistance = BorderOutlineDistance;
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
