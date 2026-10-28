// 按业务职责维护 Prefab，复用公共控件与布局实现。
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class GameUIPrefabRebuilder
{
    #region 生存状态与游戏菜单

    private static void BuildDeathScreen(GameObject root)
    {
        RectTransform rect = root.GetComponent<RectTransform>();
        if (rect == null)
            return;

        Stretch(rect);
        Image rootImage = EnsureImage(root);
        rootImage.color = new Color(0.015f, 0.026f, 0.034f, 0.90f);
        rootImage.sprite = null;
        rootImage.type = Image.Type.Simple;

        RectTransform chrome = CreateRect("FWUI_Chrome", root.transform);
        Stretch(chrome);
        chrome.SetAsFirstSibling();

        Image horizon = CreateImage("FWUI_DeathHorizon", chrome, new Color(0.83f, 0.49f, 0.23f, 0.18f));
        horizon.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        horizon.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        horizon.rectTransform.sizeDelta = new Vector2(0f, 1f);

        Image card = CreateImage("FWUI_DeathCard", chrome, new Color(0.025f, 0.043f, 0.058f, 0.92f));
        SetCentered(card.rectTransform, new Vector2(0f, 8f), new Vector2(860f, 430f));
        AddOutline(card, new Color(0.83f, 0.49f, 0.23f, 0.26f));

        TextMeshProUGUI eyebrow = CreateText("FWUI_DeathEyebrow", card.transform, string.Empty, 17f, Amber, FontStyles.Bold, TextAlignmentOptions.Center);
        SetTopCenter(eyebrow.rectTransform, 42f, 680f, 28f);
        eyebrow.gameObject.SetActive(false);
        eyebrow.characterSpacing = 4f;

        TextMeshProUGUI title = CreateText("FWUI_Death标题", card.transform, "你变成了一具尸体。", 44f, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        SetTopCenter(title.rectTransform, 82f, 720f, 150f);
        // 来源链较长时允许多行并缩小字号，给下方操作保留固定空间。
        title.enableAutoSizing = true;
        title.fontSizeMin = FlatWorldUITextLayout.MinimumFontSize(44f);
        title.fontSizeMax = 44f;
        title.enableWordWrapping = true;
        title.richText = false;

        TextMeshProUGUI copy = CreateText("FWUI_DeathCopy", card.transform, string.Empty, 19f, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
        copy.gameObject.SetActive(false);
        SetTopCenter(copy.rectTransform, 242f, 720f, 30f);
        copy.richText = false;

        Image accent = CreateImage("FWUI_DeathAccent", card.transform, Amber);
        SetTopCenter(accent.rectTransform, 286f, 92f, 4f);

        PlaceBottomButton(root.transform, "重生", -118f, 118f, 230f, 62f, "重新醒来");
        PlaceBottomButton(root.transform, "回到主菜单", 132f, 118f, 230f, 62f, "结束本次旅程");
    }

    private static void BuildGameModuleSelector(GameObject root)
    {
        RectTransform rootRect = root.GetComponent<RectTransform>();
        if (rootRect == null)
            return;
        Stretch(rootRect);

        RectTransform chrome = CreateRect("FWUI_Chrome", root.transform);
        Stretch(chrome);
        chrome.SetAsFirstSibling();

        Image plate = CreateImage("FWUI_ModulePlate", chrome, new Color(0.025f, 0.043f, 0.058f, 0.92f));
        plate.rectTransform.anchorMin = Vector2.one;
        plate.rectTransform.anchorMax = Vector2.one;
        plate.rectTransform.pivot = Vector2.one;
        plate.rectTransform.anchoredPosition = new Vector2(-24f, -24f);
        plate.rectTransform.sizeDelta = new Vector2(374f, 94f);
        AddOutline(plate, Border);

        Image accent = CreateImage("FWUI_ModuleAccent", chrome, Amber);
        accent.rectTransform.anchorMin = Vector2.one;
        accent.rectTransform.anchorMax = Vector2.one;
        accent.rectTransform.pivot = Vector2.one;
        accent.rectTransform.anchoredPosition = new Vector2(-394f, -24f);
        accent.rectTransform.sizeDelta = new Vector2(4f, 94f);

        TextMeshProUGUI label = CreateText("FWUI_Module标题", chrome, string.Empty, 12f, Muted, FontStyles.Bold, TextAlignmentOptions.Left);
        label.rectTransform.anchorMin = Vector2.one;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.pivot = Vector2.one;
        label.rectTransform.anchoredPosition = new Vector2(-42f, -34f);
        label.rectTransform.sizeDelta = new Vector2(334f, 22f);
        label.gameObject.SetActive(false);
        label.characterSpacing = 2f;

        RectTransform dropdown = FindDirectRect(root.transform, "Dropdown");
        if (dropdown != null)
        {
            // 下拉入口用实际用途替换旧的控件占位文案。
            TMP_Text dropdownLabel = dropdown.Find("Label")?.GetComponent<TMP_Text>();
            if (dropdownLabel != null)
                dropdownLabel.text = "生存状态";
            dropdown.anchorMin = Vector2.one;
            dropdown.anchorMax = Vector2.one;
            dropdown.pivot = Vector2.one;
            dropdown.anchoredPosition = new Vector2(-42f, -62f);
            dropdown.sizeDelta = new Vector2(334f, 42f);
            Image image = dropdown.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = SurfaceRaised;
                AddOutline(image, new Color(0.55f, 0.68f, 0.70f, 0.26f));
            }
        }
    }

    private static void BuildSettingsPanel(GameObject root)
    {
        Transform panel = FindTransform(root.transform, "Panel");
        if (panel == null)
            return;
        ConfigureFloatingCard(panel, 540f, 320f, "界面设置", string.Empty);
        PlaceRect(panel, "Slider", 38f, 126f, 464f, 30f);
        PlaceRect(panel, "关闭页面", 38f, 240f, 464f, 52f);
        RectTransform close = FindRect(panel, "关闭页面");
        if (close != null)
            ConfigureActionButton(close.gameObject, "完成设置", true);
    }

    private static void BuildHealthWorldPanel(GameObject root)
    {
        Transform panel = FindTransform(root.transform, "血量模块_世界面板");
        if (panel == null)
            return;

        RectTransform background = FindRect(panel, "背景");
        if (background != null)
        {
            background.sizeDelta = new Vector2(236f, 56f);
            Image image = background.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = new Color(0.025f, 0.043f, 0.058f, 0.86f);
                AddOutline(image, new Color(0.83f, 0.49f, 0.23f, 0.30f));
            }

            Image accent = CreateImage("FWUI_Chrome", background, Amber);
            accent.rectTransform.anchorMin = new Vector2(0f, 0f);
            accent.rectTransform.anchorMax = new Vector2(0f, 1f);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.rectTransform.anchoredPosition = Vector2.zero;
            accent.rectTransform.sizeDelta = new Vector2(4f, 0f);
            accent.transform.SetAsFirstSibling();
        }

        RectTransform healthText = FindRect(panel, "血量");
        if (healthText != null)
        {
            healthText.anchorMin = new Vector2(0.5f, 0.5f);
            healthText.anchorMax = new Vector2(0.5f, 0.5f);
            healthText.pivot = new Vector2(0.5f, 0.5f);
            healthText.anchoredPosition = Vector2.zero;
            healthText.sizeDelta = new Vector2(208f, 40f);
            TMP_Text text = healthText.GetComponent<TMP_Text>();
            if (text != null)
            {
                text.font = font;
                text.fontSize = 18f;
                text.fontStyle = FontStyles.Bold;
                text.color = Cream;
                text.alignment = TextAlignmentOptions.Center;
            }
        }
    }

    private static void BuildDamageReceiverStatusPanel(GameObject root)
    {
        RectTransform frame = PrepareWindow(
            root,
            360f,
            220f,
            "伤害接收器状态",
            string.Empty,
            string.Empty);

        RectTransform hp = FindRect(root.transform, "整体血量");
        if (hp != null)
        {
            hp.anchoredPosition = new Vector2(0f, 50f);
            hp.sizeDelta = new Vector2(300f, 36f);
        }

        RectTransform parts = FindRect(root.transform, "部位耐久列表");
        if (parts != null)
        {
            parts.anchorMin = new Vector2(0.5f, 0f);
            parts.anchorMax = new Vector2(0.5f, 0f);
            parts.pivot = new Vector2(0.5f, 0f);
            parts.anchoredPosition = new Vector2(0f, 24f);
            parts.sizeDelta = new Vector2(300f, 90f);
        }

        RectTransform template = FindRect(root.transform, "部位耐久模板");
        if (template != null)
            template.gameObject.SetActive(false);
    }

    private static void BuildActionList(GameObject root)
    {
        RectTransform frame = PrepareWindow(
            root,
            1440f,
            810f,
            "设置",
            string.Empty,
            string.Empty);
        ConfigureResponsiveActionListFrame(root, frame);

        RectTransform scroll = FindRect(root.transform, "Scroll View");
        if (scroll != null)
        {
            SetStretchWithMargins(scroll, 54f, 84f, 54f, 174f);

            RectTransform content = FindRect(scroll, "Content");
            if (content != null)
            {
                ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
                if (fitter != null)
                    UnityEngine.Object.DestroyImmediate(fitter);

                content.anchorMin = Vector2.zero;
                content.anchorMax = Vector2.one;
                content.pivot = new Vector2(0.5f, 0.5f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = Vector2.zero;

                GridLayoutGroup grid = content.GetComponent<GridLayoutGroup>();
                if (grid != null)
                {
                    grid.padding = new RectOffset(0, 0, 0, 0);
                    grid.childAlignment = TextAnchor.MiddleCenter;
                    grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
                    grid.startAxis = GridLayoutGroup.Axis.Vertical;
                    grid.cellSize = new Vector2(360f, 64f);
                    grid.spacing = new Vector2(0f, 16f);
                    grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                    grid.constraintCount = 1;
                }
            }
        }
        RectTransform oldInfo = FindDirectRect(root.transform, "信息");
        if (oldInfo != null)
            oldInfo.gameObject.SetActive(false);
    }

    /// <summary>让设置主面板占满父安全区的九成，并使框架装饰跟随面板伸缩。</summary>
    private static void ConfigureResponsiveActionListFrame(GameObject root, RectTransform frame)
    {
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.05f, 0.05f);
        rootRect.anchorMax = new Vector2(0.95f, 0.95f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.sizeDelta = Vector2.zero;

        RectTransform inner = FindRect(frame, "FWUI_InnerField");
        if (inner != null)
            SetStretchWithMargins(inner, 14f, 66f, 14f, 92f);

        RectTransform header = FindRect(frame, "FWUI_Header");
        if (header != null)
            SetTopStretch(header, 0f, 0f, 0f, 78f);

        RectTransform eyebrow = FindRect(frame, "FWUI_眉题");
        if (eyebrow != null)
            SetTopStretch(eyebrow, 24f, 120f, 13f, 22f);

        RectTransform title = FindRect(frame, "FWUI_标题");
        if (title != null)
            SetTopStretch(title, 24f, 130f, 33f, 38f);

        RectTransform headerRule = FindRect(frame, "FWUI_HeaderRule");
        if (headerRule != null)
            SetTopStretch(headerRule, 0f, 0f, 77f, 1f);

        RectTransform footerHint = FindRect(frame, "FWUI_FooterHint");
        if (footerHint != null)
        {
            footerHint.anchorMin = Vector2.zero;
            footerHint.anchorMax = new Vector2(1f, 0f);
            footerHint.pivot = Vector2.zero;
            footerHint.offsetMin = new Vector2(24f, 16f);
            footerHint.offsetMax = new Vector2(-286f, 40f);
        }

        RectTransform topTick = FindRect(frame, "FWUI_TickTop");
        if (topTick != null)
        {
            topTick.anchorMin = Vector2.one;
            topTick.anchorMax = Vector2.one;
            topTick.pivot = Vector2.one;
            topTick.anchoredPosition = new Vector2(-24f, 0f);
            topTick.sizeDelta = new Vector2(68f, 2f);
        }
    }

    private static void BuildSaveContextMenu(GameObject root)
    {
        Transform panel = FindTransform(root.transform, "Panel");
        if (panel == null)
            return;

        ConfigureFloatingCard(panel, 324f, 356f, "存档操作", string.Empty);
        PlaceRect(panel, "InputField (TMP)", 24f, 102f, 276f, 52f);
        PlaceRect(panel, "Button_ReName", 24f, 174f, 276f, 50f);
        PlaceRect(panel, "删除按钮", 24f, 236f, 276f, 50f);
        PlaceRect(panel, "Button_Close", 24f, 298f, 276f, 40f);
    }

    private static void BuildNutritionHud(GameObject root)
    {
        Transform panel = FindTransform(root.transform, "Panel");
        if (panel == null)
            return;

        RectTransform rootRect = root.GetComponent<RectTransform>();
        RectTransform panelRect = panel as RectTransform;
        if (panelRect == null)
            return;

        if (rootRect != null)
            Stretch(rootRect);

        EnsureNutritionTemperatureRow(panel);
        EnsureNutritionHealthRow(panel);
        SetBottomLeft(panelRect, new Vector2(28f, 28f), new Vector2(326f, 230f));

        // 参数 HUD 不使用卡片底板或装饰描边，避免透明区域遮挡世界和制造视觉焦点。
        Image panelImage = panel.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.enabled = false;
            panelImage.raycastTarget = false;
        }

        Outline panelOutline = panel.GetComponent<Outline>();
        if (panelOutline != null)
            UnityEngine.Object.DestroyImmediate(panelOutline);

        string[] sliders = { "血量", "碳水", "脂肪", "蛋白质", "水", "维生素", "体温" };
        for (int i = 0; i < sliders.Length; i++)
        {
            RectTransform slider = FindRect(panel, sliders[i]);
            if (slider != null)
            {
                SetTopLeft(slider, 0f, i * 34f, 326f, 26f);
                Slider sliderComponent = slider.GetComponent<Slider>();
                if (sliderComponent != null)
                    sliderComponent.interactable = false;
                ConfigureNutritionRowTexts(slider);
            }
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
    }

    /// <summary>左右分栏对齐状态名称和数值，防止数值越出状态条边缘。</summary>
    private static void ConfigureNutritionRowTexts(RectTransform row)
    {
        TextMeshProUGUI[] texts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI text in texts)
        {
            bool isValue = text.name.StartsWith("DataText_", StringComparison.Ordinal);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(isValue ? 1f : 0f, 0.5f);
            rect.pivot = new Vector2(isValue ? 1f : 0f, 0.5f);
            rect.anchoredPosition = new Vector2(isValue ? -12f : 12f, 0f);
            rect.sizeDelta = new Vector2(isValue ? 130f : 112f, 24f);

            text.alignment = isValue ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
            text.fontSize = 18f;
            text.enableAutoSizing = true;
            text.fontSizeMin = FlatWorldUITextLayout.MinimumFontSize(18f);
            text.fontSizeMax = 18f;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
        }
    }

    /// <summary>
    /// 收尾角色参数 HUD 的输入与装饰状态，避免主题通用规则重新添加整块描边或射线拦截。
    /// </summary>
    private static void FinalizeNutritionHud(GameObject root)
    {
        Transform panel = FindTransform(root.transform, "Panel");
        if (panel == null)
            return;

        Image panelImage = panel.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.enabled = false;
            panelImage.raycastTarget = false;
        }

        Outline panelOutline = panel.GetComponent<Outline>();
        if (panelOutline != null)
            UnityEngine.Object.DestroyImmediate(panelOutline);

        CanvasGroup rootCanvasGroup = root.GetComponent<CanvasGroup>();
        if (rootCanvasGroup != null)
        {
            rootCanvasGroup.interactable = false;
            rootCanvasGroup.blocksRaycasts = false;
        }

        foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
    }

    private static void EnsureNutritionTemperatureRow(Transform panel)
    {
        if (FindTransform(panel, "体温") != null)
            return;

        RectTransform source = FindRect(panel, "维生素");
        if (source == null)
            throw new InvalidOperationException("UI_Food 缺少可复用的维生素状态行。");

        GameObject temperatureRow = UnityEngine.Object.Instantiate(source.gameObject, panel, false);
        temperatureRow.name = "体温";

        TextMeshProUGUI[] labels = temperatureRow.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            TextMeshProUGUI label = labels[i];
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;

            if (label.name.StartsWith("DataText_", StringComparison.Ordinal))
            {
                label.name = "DataText_体温";
                label.text = "36.5°C";
            }
            else if (label.text == "维生素")
            {
                label.text = "体温";
            }
        }
    }

    /// <summary>从现有状态行复制玩家血量行，保持角色参数面板的统一视觉和绑定结构。</summary>
    private static void EnsureNutritionHealthRow(Transform panel)
    {
        if (FindTransform(panel, "血量") != null)
            return;

        RectTransform source = FindRect(panel, "维生素");
        if (source == null)
            throw new InvalidOperationException("UI_Food 缺少可复用的维生素状态行。");

        GameObject healthRow = UnityEngine.Object.Instantiate(source.gameObject, panel, false);
        healthRow.name = "血量";

        TextMeshProUGUI[] labels = healthRow.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            TextMeshProUGUI label = labels[i];
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;

            if (label.name.StartsWith("DataText_", StringComparison.Ordinal))
            {
                label.name = "DataText_血量";
                label.text = "100/100";
            }
            else if (label.text == "维生素")
            {
                label.text = "血量";
            }
        }
    }

    private static void BuildSleepHud(GameObject root)
    {
        // 睡眠遮罩由帐篷控制，不能再叠加通用面板缩放或淡入动画。
        foreach (BaseUIAnimation animation in root.GetComponents<BaseUIAnimation>())
            UnityEngine.Object.DestroyImmediate(animation);

        RectTransform rect = root.GetComponent<RectTransform>();
        if (rect == null)
            return;
        Stretch(rect);

        Image veil = FindDirectRect(root.transform, "Image")?.GetComponent<Image>();
        if (veil != null)
        {
            veil.color = Color.black;
            veil.sprite = null;
            veil.type = Image.Type.Simple;
            Stretch(veil.rectTransform);
            if (veil.GetComponent<FullScreenRectController>() == null)
                veil.gameObject.AddComponent<FullScreenRectController>();
        }

        RectTransform zzzs = FindDirectRect(root.transform, "ZZZs");
        if (zzzs != null)
        {
            zzzs.anchorMin = new Vector2(0.5f, 0.5f);
            zzzs.anchorMax = new Vector2(0.5f, 0.5f);
            zzzs.pivot = new Vector2(0.5f, 0.5f);
            zzzs.anchoredPosition = new Vector2(0f, 30f);
            zzzs.sizeDelta = new Vector2(640f, 220f);
        }

        RectTransform chrome = CreateRect("FWUI_Chrome", root.transform);
        Stretch(chrome);
        chrome.SetAsFirstSibling();
        TextMeshProUGUI copy = CreateText("FWUI_SleepCopy", chrome, string.Empty, 17f, new Color(Cream.r, Cream.g, Cream.b, 0.82f), FontStyles.Bold, TextAlignmentOptions.Center);
        copy.gameObject.SetActive(false);
        copy.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        copy.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        copy.rectTransform.pivot = new Vector2(0.5f, 0f);
        copy.rectTransform.anchoredPosition = new Vector2(0f, 68f);
        copy.rectTransform.sizeDelta = new Vector2(680f, 30f);
        copy.characterSpacing = 3f;
    }

    #endregion
}
