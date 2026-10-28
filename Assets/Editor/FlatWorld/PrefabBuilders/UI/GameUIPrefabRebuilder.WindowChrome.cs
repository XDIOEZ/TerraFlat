// 按业务职责维护 Prefab，复用公共控件与布局实现。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class GameUIPrefabRebuilder
{
    #region 公共窗口与外观

    private static void BuildBaseSlider(GameObject root)
    {
        Slider slider = root.GetComponent<Slider>();
        if (slider == null)
            slider = root.GetComponentInChildren<Slider>(true);
        if (slider == null)
            return;

        RectTransform rect = slider.GetComponent<RectTransform>();
        if (rect != null)
            rect.sizeDelta = new Vector2(Mathf.Max(240f, rect.sizeDelta.x), 26f);

        Image background = slider.GetComponent<Image>();
        if (background != null)
            background.color = InkSoft;
        if (slider.fillRect != null && slider.fillRect.TryGetComponent(out Image fill))
            fill.color = Amber;
        if (slider.handleRect != null && slider.handleRect.TryGetComponent(out Image handle))
        {
            handle.color = Cream;
            AddOutline(handle, new Color(0f, 0f, 0f, 0.45f));
        }
    }

    private static void BuildBaseButton(GameObject root)
    {
        Image image = EnsureImage(root);
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = SurfaceRaised;
        AddOutline(image, Border);

        Button button = root.GetComponent<Button>();
        if (button != null)
        {
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.16f, 1.10f, 1f);
            colors.pressedColor = new Color(0.74f, 0.78f, 0.80f, 1f);
            colors.fadeDuration = 0.10f;
            button.colors = colors;
            if (button.GetComponent<FlatWorldUIFeedback>() == null)
                button.gameObject.AddComponent<FlatWorldUIFeedback>();
        }

        TMP_Text label = root.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.font = font;
            label.fontSize = 17f;
            label.fontStyle = FontStyles.Bold;
            label.color = Cream;
            label.alignment = TextAlignmentOptions.Center;
        }
    }

    private static void BuildBaseSelectBox(GameObject root)
    {
        RectTransform rect = root.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.localScale = Vector3.one;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(90f, 90f);
        }

        Image image = EnsureImage(root);
        image.color = Color.white;
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SelectBoxSpritePath);
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = false;
        AddOutline(image, new Color(0.83f, 0.49f, 0.23f, 0.34f));
        Canvas canvas = root.GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = false;
            canvas.sortingOrder = 0;
        }
        TMP_Text label = root.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.color = Cream;
            label.font = font;
        }
    }

    /// <summary>布置面板底板；需要从快捷栏拖入物品的面板不让底板接收射线。</summary>
    private static RectTransform PrepareWindow(GameObject root, float width, float height, string title, string eyebrow, string footerHint, bool allowHotbarInput = false)
    {
        RectTransform rootRect = root.GetComponent<RectTransform>();
        if (rootRect == null)
            throw new InvalidOperationException($"{root.name} 缺少 RectTransform");

        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.sizeDelta = new Vector2(width, height);

        Image rootImage = EnsureImage(root);
        rootImage.color = Ink;
        rootImage.sprite = null;
        rootImage.type = Image.Type.Simple;
        rootImage.raycastTarget = !allowHotbarInput;
        AddOutline(rootImage, new Color(0.83f, 0.49f, 0.23f, 0.30f));

        RectTransform chrome = CreateRect("FWUI_Chrome", root.transform);
        Stretch(chrome);
        chrome.SetAsFirstSibling();

        Image shadow = CreateImage("FWUI_Shadow", chrome, new Color(0.005f, 0.012f, 0.016f, 0.60f));
        Stretch(shadow.rectTransform);
        shadow.rectTransform.offsetMin = new Vector2(12f, -12f);
        shadow.rectTransform.offsetMax = new Vector2(12f, -12f);

        Image body = CreateImage("FWUI_Body", chrome, Ink);
        Stretch(body.rectTransform);

        Image inner = CreateImage("FWUI_InnerField", chrome, new Color(0.045f, 0.075f, 0.095f, 0.74f));
        SetTopLeft(inner.rectTransform, 14f, 92f, width - 28f, height - 158f);

        Image header = CreateImage("FWUI_Header", chrome, new Color(0.075f, 0.18f, 0.215f, 0.96f));
        SetTopLeft(header.rectTransform, 0f, 0f, width, 78f);

        Image accent = CreateImage("FWUI_AccentRail", chrome, Amber);
        SetTopLeft(accent.rectTransform, 0f, 0f, 5f, 78f);

        TextMeshProUGUI eyebrowText = CreateText("FWUI_眉题", chrome, string.Empty, 13f, Amber, FontStyles.Bold, TextAlignmentOptions.Left);
        SetTopLeft(eyebrowText.rectTransform, 24f, 13f, width - 120f, 22f);
        // 眉题仅保留绑定节点，不再重复显示标题。
        eyebrowText.gameObject.SetActive(false);
        eyebrowText.characterSpacing = 3f;

        TextMeshProUGUI titleText = CreateText("FWUI_标题", chrome, title, 28f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetTopLeft(titleText.rectTransform, 24f, 33f, width - 130f, 38f);

        Image rule = CreateImage("FWUI_HeaderRule", chrome, new Color(0.83f, 0.49f, 0.23f, 0.30f));
        SetTopLeft(rule.rectTransform, 0f, 77f, width, 1f);

        Image footer = CreateImage("FWUI_Footer", chrome, new Color(0.035f, 0.072f, 0.088f, 0.98f));
        footer.rectTransform.anchorMin = Vector2.zero;
        footer.rectTransform.anchorMax = new Vector2(1f, 0f);
        footer.rectTransform.pivot = Vector2.zero;
        footer.rectTransform.anchoredPosition = Vector2.zero;
        footer.rectTransform.sizeDelta = new Vector2(0f, 58f);

        TextMeshProUGUI footerText = CreateText("FWUI_FooterHint", chrome, footerHint, 13f, new Color(Muted.r, Muted.g, Muted.b, 0.86f), FontStyles.Normal, TextAlignmentOptions.Left);
        footerText.rectTransform.anchorMin = Vector2.zero;
        footerText.rectTransform.anchorMax = Vector2.zero;
        footerText.rectTransform.pivot = Vector2.zero;
        footerText.rectTransform.anchoredPosition = new Vector2(24f, 16f);
        footerText.rectTransform.sizeDelta = new Vector2(Mathf.Max(250f, width - 310f), 24f);

        AddCornerTicks(chrome, width, height);
        PlaceCloseButton(root.transform, width);
        if (allowHotbarInput)
        {
            UI_Drag dragger = root.GetComponent<UI_Drag>();
            if (dragger == null)
                dragger = root.AddComponent<UI_Drag>();
            dragger.rectTransform = rootRect;
            dragger.draggableImage = rootImage;

            BasePanel basePanel = root.GetComponent<BasePanel>();
            if (basePanel != null)
            {
                basePanel.CanDrag = true;
                basePanel.Dragger = dragger;
                basePanel.rectTransform = rootRect;
            }

            RectTransform hitRect = root.transform.Find("FWUI_DragSurface") as RectTransform;
            if (hitRect == null)
                hitRect = CreateRect("FWUI_DragSurface", root.transform);
            Stretch(hitRect);
            hitRect.SetAsFirstSibling();
            UIWindowDragSurface dragSurface = hitRect.GetComponent<UIWindowDragSurface>();
            if (dragSurface == null)
                dragSurface = hitRect.gameObject.AddComponent<UIWindowDragSurface>();
            dragSurface.Configure(rootRect);
        }
        return chrome;
    }

    private static void AddSection(RectTransform chrome, string eyebrow, string title, float x, float y, float width, float height)
    {
        Image surface = CreateImage("FWUI_Section_" + Sanitize(eyebrow), chrome, new Color(0.063f, 0.153f, 0.188f, 0.70f));
        SetTopLeft(surface.rectTransform, x, y, width, height);
        AddOutline(surface, new Color(0.55f, 0.68f, 0.70f, 0.15f));

        Image marker = CreateImage("FWUI_SectionMarker_" + Sanitize(eyebrow), chrome, new Color(0.26f, 0.61f, 0.57f, 0.72f));
        SetTopLeft(marker.rectTransform, x + 12f, y + 15f, 3f, 27f);

        TextMeshProUGUI eyebrowText = CreateText("FWUI_SectionEyebrow_" + Sanitize(eyebrow), chrome, string.Empty, 10f, Teal, FontStyles.Bold, TextAlignmentOptions.Left);
        SetTopLeft(eyebrowText.rectTransform, x + 24f, y + 10f, width - 36f, 16f);
        eyebrowText.gameObject.SetActive(false);
        eyebrowText.characterSpacing = 2f;

        TextMeshProUGUI titleText = CreateText("FWUI_SectionTitle_" + Sanitize(eyebrow), chrome, title, 16f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetTopLeft(titleText.rectTransform, x + 24f, y + 26f, width - 36f, 24f);

        Image rule = CreateImage("FWUI_SectionRule_" + Sanitize(eyebrow), chrome, new Color(0.55f, 0.68f, 0.70f, 0.14f));
        SetTopLeft(rule.rectTransform, x + 12f, y + 52f, width - 24f, 1f);
    }

    private static void AddFlowArrow(RectTransform chrome, float x, float y)
    {
        TextMeshProUGUI arrow = CreateText("FWUI_FlowArrow_" + Mathf.RoundToInt(x), chrome, "→", 28f, Amber, FontStyles.Bold, TextAlignmentOptions.Center);
        SetTopLeft(arrow.rectTransform, x - 22f, y, 44f, 40f);
    }

    private static void PlaceProgress(Transform root, float x, float y, float width, float height)
    {
        RectTransform background = FindDirectRect(root, "Image_1");
        RectTransform progress = FindDirectRect(root, "Progress");
        if (background != null)
            SetTopLeft(background, x, y, width, height);
        if (progress != null)
            SetTopLeft(progress, x, y, width, height);
    }

    private static void PlaceActionButton(Transform root, string name, float width, float height, string caption)
    {
        RectTransform buttonRect = FindRect(root, name);
        if (buttonRect == null)
            return;
        buttonRect.anchorMin = Vector2.zero;
        buttonRect.anchorMax = Vector2.zero;
        buttonRect.pivot = Vector2.zero;
        buttonRect.anchoredPosition = new Vector2(width - 252f, 12f);
        buttonRect.sizeDelta = new Vector2(228f, 46f);
        ConfigureActionButton(buttonRect.gameObject, caption, true);
    }

    private static void PlaceCloseButton(Transform root, float width)
    {
        RectTransform close = FindDirectRect(root, "关闭");
        if (close == null)
            return;

        close.anchorMin = new Vector2(1f, 1f);
        close.anchorMax = new Vector2(1f, 1f);
        close.pivot = new Vector2(1f, 1f);
        close.anchoredPosition = new Vector2(-16f, -16f);
        close.sizeDelta = new Vector2(44f, 44f);
        ConfigureActionButton(close.gameObject, "×", false);
    }

    private static void PlaceBottomButton(Transform root, string name, float x, float y, float width, float height, string caption)
    {
        RectTransform rect = FindRect(root, name);
        if (rect == null)
            return;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        ConfigureActionButton(rect.gameObject, caption, name == "重生");
    }

    private static void ConfigureActionButton(GameObject gameObject, string caption, bool primary)
    {
        Image image = EnsureImage(gameObject);
        image.color = primary ? new Color(0.70f, 0.36f, 0.16f, 0.98f) : SurfaceRaised;
        image.sprite = null;
        image.type = Image.Type.Simple;
        AddOutline(image, primary ? new Color(1f, 0.71f, 0.38f, 0.40f) : Border);

        Button button = gameObject.GetComponent<Button>();
        if (button != null)
        {
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = primary ? new Color(1.13f, 1.08f, 0.98f, 1f) : new Color(1.18f, 1.16f, 1.10f, 1f);
            colors.pressedColor = new Color(0.74f, 0.78f, 0.80f, 1f);
            colors.selectedColor = FlatWorldUITheme.Selection;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            colors.fadeDuration = 0.10f;
            button.colors = colors;
            if (gameObject.GetComponent<FlatWorldUIFeedback>() == null)
                gameObject.AddComponent<FlatWorldUIFeedback>();
        }

        TMP_Text label = gameObject.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = caption;
            label.font = font;
            label.fontSize = primary ? 18f : 20f;
            label.fontStyle = FontStyles.Bold;
            label.color = Cream;
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
        }
    }

    private static void ConfigureFloatingCard(Transform panel, float width, float height, string title, string eyebrow)
    {
        RectTransform rect = panel as RectTransform;
        if (rect == null)
            return;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);

        Image image = EnsureImage(panel.gameObject);
        image.color = Ink;
        image.sprite = null;
        image.type = Image.Type.Simple;
        AddOutline(image, new Color(0.83f, 0.49f, 0.23f, 0.30f));
        BuildCardHeader(panel, title, eyebrow, width);
    }

    private static void BuildCardHeader(Transform panel, string title, string eyebrow, float width)
    {
        Transform previous = panel.Find("FWUI_Chrome");
        if (previous != null)
            UnityEngine.Object.DestroyImmediate(previous.gameObject);

        RectTransform chrome = CreateRect("FWUI_Chrome", panel);
        Stretch(chrome);
        chrome.SetAsFirstSibling();

        Image header = CreateImage("FWUI_CardHeader", chrome, SurfaceRaised);
        SetTopLeft(header.rectTransform, 0f, 0f, width, 76f);
        Image accent = CreateImage("FWUI_CardAccent", chrome, Amber);
        SetTopLeft(accent.rectTransform, 0f, 0f, 4f, 76f);

        TextMeshProUGUI eyebrowText = CreateText("FWUI_CardEyebrow", chrome, string.Empty, 10f, Amber, FontStyles.Bold, TextAlignmentOptions.Left);
        SetTopLeft(eyebrowText.rectTransform, 18f, 12f, width - 36f, 18f);
        eyebrowText.gameObject.SetActive(false);
        eyebrowText.characterSpacing = 2f;
        TextMeshProUGUI titleText = CreateText("FWUI_Card标题", chrome, title, 22f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetTopLeft(titleText.rectTransform, 18f, 32f, width - 36f, 32f);
    }

    private static void AddCornerTicks(RectTransform chrome, float width, float height)
    {
        Image top = CreateImage("FWUI_TickTop", chrome, Amber);
        SetTopLeft(top.rectTransform, width - 92f, 0f, 68f, 2f);
        Image bottom = CreateImage("FWUI_TickBottom", chrome, Teal);
        bottom.rectTransform.anchorMin = Vector2.zero;
        bottom.rectTransform.anchorMax = Vector2.zero;
        bottom.rectTransform.pivot = Vector2.zero;
        bottom.rectTransform.anchoredPosition = new Vector2(24f, 0f);
        bottom.rectTransform.sizeDelta = new Vector2(48f, 2f);
    }

    private static void ArrangeSlots(RectTransform container, int expectedCount, int columns, float size, float spacing)
    {
        List<RectTransform> slots = new List<RectTransform>();
        for (int i = 0; i < container.childCount; i++)
        {
            RectTransform child = container.GetChild(i) as RectTransform;
            if (child != null && child.GetComponent<Button>() != null)
                slots.Add(child);
        }

        int count = Mathf.Min(expectedCount, slots.Count);
        for (int i = 0; i < count; i++)
        {
            int column = i % Mathf.Max(1, columns);
            int row = i / Mathf.Max(1, columns);
            SetTopLeft(slots[i], column * (size + spacing), row * (size + spacing), size, size);
        }
    }

    /// <summary>与 ArrangeSlots 相同，但按每一行的真实槽位数水平居中；用于熔炉这种分栏面板。</summary>
    private static void ArrangeSlotsCentered(RectTransform container, int expectedCount, int columns, float size, float spacing)
    {
        List<RectTransform> slots = new List<RectTransform>();
        for (int i = 0; i < container.childCount; i++)
        {
            RectTransform child = container.GetChild(i) as RectTransform;
            if (child != null && child.GetComponent<Button>() != null)
                slots.Add(child);
        }

        int count = Mathf.Min(expectedCount, slots.Count);
        int safeColumns = Mathf.Max(1, columns);
        float containerWidth = container.rect.width;
        for (int i = 0; i < count; i++)
        {
            int row = i / safeColumns;
            int column = i % safeColumns;
            int firstIndexInRow = row * safeColumns;
            int itemsInRow = Mathf.Min(safeColumns, count - firstIndexInRow);
            float rowWidth = itemsInRow * size + Mathf.Max(0, itemsInRow - 1) * spacing;
            float rowLeft = Mathf.Max(0f, (containerWidth - rowWidth) * 0.5f);
            SetTopLeft(slots[i], rowLeft + column * (size + spacing), row * (size + spacing), size, size);
        }
    }

    private static void PlaceRect(Transform root, string name, float x, float y, float width, float height)
    {
        RectTransform rect = FindRect(root, name);
        if (rect != null)
            SetTopLeft(rect, x, y, width, height);
    }

    private static void RemoveGeneratedArt(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child.name == "UITheme_Chrome" || child.name.StartsWith("FWUI_", StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        for (int i = descendants.Length - 1; i >= 0; i--)
        {
            Transform descendant = descendants[i];
            if (descendant == null || descendant == root)
                continue;
            if (descendant.name == "UITheme_Chrome" || descendant.name.StartsWith("FWUI_", StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(descendant.gameObject);
        }
    }

    private static void NormalizeTypography(Transform root)
    {
        if (font == null)
            return;

        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            text.font = font;
    }

    private static void NormalizeControls(Transform root)
    {
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            bool isItemSlot = button.GetComponent("ItemSlot_UI") != null ||
                              button.name.IndexOf("Slot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              button.name.Contains("槽");
            if (isItemSlot)
                continue;

            Image image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
            }

            if (button.GetComponent<FlatWorldUIFeedback>() == null)
                button.gameObject.AddComponent<FlatWorldUIFeedback>();
        }

        foreach (ScrollRect scrollRect in root.GetComponentsInChildren<ScrollRect>(true))
        {
            Image image = scrollRect.GetComponent<Image>();
            if (image == null)
                continue;
            image.sprite = null;
            image.type = Image.Type.Simple;
        }
    }

    private static RectTransform FindRect(Transform root, string name)
    {
        Transform found = FindTransform(root, name);
        return found as RectTransform;
    }

    private static RectTransform FindDirectRect(Transform root, string name)
    {
        Transform found = root.Find(name);
        return found as RectTransform;
    }

    private static Image EnsureImage(GameObject gameObject)
    {
        Image image = gameObject.GetComponent<Image>();
        if (image == null)
            image = gameObject.AddComponent<Image>();
        image.raycastTarget = true;
        return image;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string value, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        FlatWorldUITextLayout.Configure(text, size);
        text.raycastTarget = false;
        return text;
    }

    /// <summary>将 HUD 子面板固定到 Canvas 左下角，并保留安全边距。</summary>
    private static void SetBottomLeft(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetTopCenter(RectTransform rect, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static string Sanitize(string value)
    {
        return value.Replace(" ", string.Empty).Replace("/", "_").Replace("—", "_");
    }

    private static string BuildPath(Transform transform)
    {
        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }

    private static void SetUILayerRecursively(GameObject root)
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0)
            uiLayer = 5;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = uiLayer;
    }

    private static void NormalizeCanvasLayers(Transform root)
    {
        if (root == null)
            return;

        foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas != null && !canvas.overrideSorting)
                canvas.sortingOrder = 0;
        }
    }

    #endregion
}
