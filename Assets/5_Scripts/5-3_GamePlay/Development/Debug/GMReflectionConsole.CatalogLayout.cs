using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using FlatWorld.Localization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 共用控件与内容浏览器布局

    // 两种目录共用窗口骨架，只由调用方提供文案和操作。
    private GameObject CreateCatalogBrowserFrame(
        Transform canvasTransform, string objectName, string titleText,
        string instructionText, float instructionWidth, UnityAction onBack,
        out Transform toolbar)
    {
        GameObject browserRoot = CreateUiObject(objectName, canvasTransform);
        RectTransform panelRect = browserRoot.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(1120f, 760f);

        Image panelImage = browserRoot.AddComponent<Image>();
        panelImage.color = GmCanvas;
        Outline panelOutline = browserRoot.AddComponent<Outline>();
        StyleGmOutline(panelOutline, true);

        VerticalLayoutGroup panelLayout = browserRoot.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(20, 20, 18, 18);
        panelLayout.spacing = 10f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        GameObject header = CreateUiObject("Header", browserRoot.transform);
        LayoutElement headerElement = header.AddComponent<LayoutElement>();
        headerElement.minHeight = CatalogBrowserHeaderHeight;
        headerElement.preferredHeight = CatalogBrowserHeaderHeight;
        headerElement.flexibleHeight = 0f;
        Image headerImage = header.AddComponent<Image>();
        headerImage.color = GmSurfaceRaised;
        HorizontalLayoutGroup headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(12, 10, 4, 4);
        headerLayout.spacing = 10f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText(header.transform, titleText, 18f, GmTextPrimary);
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        TextMeshProUGUI instruction = CreateText(
            header.transform,
            instructionText,
            12f,
            GmTextSecondary);
        instruction.alignment = TextAlignmentOptions.Right;
        instruction.gameObject.AddComponent<LayoutElement>().preferredWidth = instructionWidth;
        CreateButton(header.transform, "返回", onBack, 60f, CatalogBrowserControlHeight);

        GameObject toolbarRoot = CreateUiObject("Toolbar", browserRoot.transform);
        LayoutElement toolbarElement = toolbarRoot.AddComponent<LayoutElement>();
        toolbarElement.minHeight = CatalogBrowserToolbarHeight;
        toolbarElement.preferredHeight = CatalogBrowserToolbarHeight;
        toolbarElement.flexibleHeight = 0f;
        HorizontalLayoutGroup toolbarLayout = toolbarRoot.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 8f;
        toolbarLayout.childAlignment = TextAnchor.MiddleLeft;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;
        toolbarLayout.childForceExpandHeight = false;

        toolbar = toolbarRoot.transform;
        return browserRoot;
    }

    private void BuildAirdropBrowser(Transform canvasTransform)
    {
        airdropBrowserRoot = CreateCatalogBrowserFrame(
            canvasTransform, "GM Airdrop Browser", "物品空投", "选择物品后直接空投到玩家附近",
            220f, CloseAirdropBrowser, out Transform toolbar);
        airdropBrowserRect = airdropBrowserRoot.GetComponent<RectTransform>();

        airdropSearchInput = CreateInputField(toolbar, "搜索物品名称或 ID", 560f, false);
        airdropSearchInput.GetComponent<LayoutElement>().preferredHeight = CatalogBrowserControlHeight;
        airdropSearchInput.onValueChanged.AddListener(_ => RebuildAirdropItemGrid());

        TextMeshProUGUI amountLabel = CreateText(
            toolbar,
            "数量",
            13f,
            GmTextPrimary);
        amountLabel.alignment = TextAlignmentOptions.MidlineRight;
        amountLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;

        amountInput = CreateInputField(toolbar, "数量", 90f, true);
        amountInput.GetComponent<LayoutElement>().preferredHeight = CatalogBrowserControlHeight;
        amountInput.text = "1";
        CreateButton(toolbar, "刷新物品", RefreshItemIds, 92f, CatalogBrowserControlHeight);

        airdropBrowserCountText = CreateText(
            toolbar,
            "正在读取物品…",
            12f,
            GmTextSecondary);
        airdropBrowserCountText.alignment = TextAlignmentOptions.MidlineRight;
        airdropBrowserCountText.enableWordWrapping = false;
        airdropBrowserCountText.overflowMode = TextOverflowModes.Ellipsis;
        airdropBrowserCountText.gameObject.AddComponent<LayoutElement>().preferredWidth = 240f;

        airdropItemGrid = CreateCatalogScrollGrid(airdropBrowserRoot.transform, "Airdrop Item Scroll");
        // 让空投物品按钮在可用区域内水平居中，减少右侧多余留白。
        airdropItemGridLayout = airdropItemGrid.GetComponent<GridLayoutGroup>();
        if (airdropItemGridLayout != null)
            airdropItemGridLayout.childAlignment = TextAnchor.UpperCenter;

        airdropBrowserStatusText = CreateText(
            airdropBrowserRoot.transform,
            "点击任意物品即可空投。",
            12f,
            GmTextSecondary);
        airdropBrowserStatusText.enableWordWrapping = false;
        airdropBrowserStatusText.overflowMode = TextOverflowModes.Ellipsis;
        airdropBrowserStatusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        airdropBrowserRoot.SetActive(false);
    }

    private void BuildAiCreatureBrowser(Transform canvasTransform)
    {
        aiCreatureBrowserRoot = CreateCatalogBrowserFrame(
            canvasTransform, "GM AI Creature Browser", "AI 生物召唤", "点击生物后生成到玩家附近",
            240f, CloseAiCreatureBrowser, out Transform toolbar);
        aiCreatureBrowserRect = aiCreatureBrowserRoot.GetComponent<RectTransform>();

        aiCreatureSearchInput = CreateInputField(toolbar, "搜索生物名称或 ID", 560f, false);
        aiCreatureSearchInput.GetComponent<LayoutElement>().preferredHeight = CatalogBrowserControlHeight;
        aiCreatureSearchInput.onValueChanged.AddListener(_ => RebuildAiCreatureGrid());

        TextMeshProUGUI amountLabel = CreateText(
            toolbar,
            "数量",
            13f,
            GmTextPrimary);
        amountLabel.alignment = TextAlignmentOptions.MidlineRight;
        amountLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;

        aiCreatureAmountInput = CreateInputField(toolbar, "1-20", 90f, true);
        aiCreatureAmountInput.GetComponent<LayoutElement>().preferredHeight = CatalogBrowserControlHeight;
        aiCreatureAmountInput.text = "1";
        CreateButton(toolbar, "刷新生物", RefreshAiCreatureIds, 92f, CatalogBrowserControlHeight);

        aiCreatureBrowserCountText = CreateText(
            toolbar,
            "正在读取生物…",
            12f,
            GmTextSecondary);
        aiCreatureBrowserCountText.alignment = TextAlignmentOptions.MidlineRight;
        aiCreatureBrowserCountText.enableWordWrapping = false;
        aiCreatureBrowserCountText.overflowMode = TextOverflowModes.Ellipsis;
        aiCreatureBrowserCountText.gameObject.AddComponent<LayoutElement>().preferredWidth = 240f;

        aiCreatureGrid = CreateCatalogScrollGrid(aiCreatureBrowserRoot.transform, "AI Creature Scroll");
        aiCreatureGridLayout = aiCreatureGrid.GetComponent<GridLayoutGroup>();
        if (aiCreatureGridLayout != null)
            aiCreatureGridLayout.childAlignment = TextAnchor.UpperCenter;

        aiCreatureBrowserStatusText = CreateText(
            aiCreatureBrowserRoot.transform,
            "点击任意生物即可召唤；批量召唤会自动散布，避免重叠。",
            12f,
            GmTextSecondary);
        aiCreatureBrowserStatusText.enableWordWrapping = false;
        aiCreatureBrowserStatusText.overflowMode = TextOverflowModes.Ellipsis;
        aiCreatureBrowserStatusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(aiCreatureBrowserRect);
        aiCreatureBrowserRoot.SetActive(false);
    }

    private static Transform CreateCatalogScrollGrid(Transform parent, string objectName)
    {
        GameObject scrollObject = CreateUiObject(objectName, parent);
        Image scrollImage = scrollObject.AddComponent<Image>();
        scrollImage.color = GmSurfaceLow;
        Outline outline = scrollObject.AddComponent<Outline>();
        StyleGmOutline(outline);
        LayoutElement scrollLayout = scrollObject.AddComponent<LayoutElement>();
        scrollLayout.flexibleHeight = 1f;
        scrollLayout.minHeight = 220f;
        scrollLayout.preferredHeight = 0f;

        ScrollRect scroll = scrollObject.AddComponent<ItemStepScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 38f;

        GameObject content = CreateScrollViewportWithTopAnchoredContent(scrollObject, scroll,
            new Vector2(10f, 10f), new Vector2(-24f, -10f));
        RectTransform contentRect = content.GetComponent<RectTransform>();
        GridLayoutGroup grid = content.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 8;
        grid.cellSize = new Vector2(118f, 118f);
        grid.spacing = new Vector2(8f, 8f);
        grid.padding = new RectOffset(2, 2, 2, 8);
        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRect;

        CreateVerticalAutoHideScrollbar(scrollObject, scroll,
            new Vector2(-16f, 10f), new Vector2(-6f, -10f));
        return content.transform;
    }

    private static GameObject CreateDesktopRow(Transform parent, float height)
    {
        GameObject row = CreateUiObject("Form Row", parent);
        row.AddComponent<LayoutElement>().preferredHeight = height;
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        return row;
    }

    private static void CreateDesktopFieldLabel(Transform parent, string value, float width)
    {
        TextMeshProUGUI label = CreateText(parent, value, 13f, new Color(0.18f, 0.18f, 0.18f));
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
        label.alignment = TextAlignmentOptions.MidlineRight;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        GameObject value = new GameObject(objectName, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        return value;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string value, float fontSize, Color color)
    {
        GameObject textObject = CreateUiObject("Text", parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = ResolveUiFont();
        SetLocalizedText(text, value);
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        return text;
    }

    private static TMP_FontAsset ResolveUiFont()
    {
        if (uiFont != null)
            return uiFont;

        List<TMP_FontAsset> configuredFallbacks = TMP_Settings.fallbackFontAssets;
        if (configuredFallbacks != null)
        {
            for (int i = 0; i < configuredFallbacks.Count; i++)
            {
                TMP_FontAsset candidate = configuredFallbacks[i];
                if (candidate != null && candidate.HasCharacter(0x5F00))
                {
                    uiFont = candidate;
                    return uiFont;
                }
            }
        }

        TMP_FontAsset[] loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        for (int i = 0; i < loadedFonts.Length; i++)
        {
            TMP_FontAsset candidate = loadedFonts[i];
            if (candidate == null)
                continue;

            string fontName = candidate.name;
            if (fontName.IndexOf("zh_hans", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fontName.IndexOf("chinese", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fontName.IndexOf("cjk", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                uiFont = candidate;
                return uiFont;
            }
        }

        uiFont = TMP_Settings.defaultFontAsset;
        return uiFont;
    }

    private static Button CreateButton(Transform parent, string label, UnityAction action, float width, float height)
    {
        GameObject buttonObject = CreateUiObject(label, parent);
        Image image = buttonObject.AddComponent<Image>();
        image.color = GmSurfaceRaised;
        Outline outline = buttonObject.AddComponent<Outline>();
        StyleGmOutline(outline);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.selectedColor = new Color(1.04f, 1.04f, 1.04f, 1f);
        colors.disabledColor = GmDisabledTint;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.11f;
        button.colors = colors;

        LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        if (width > 0f)
            layout.preferredWidth = width;

        TextMeshProUGUI text = CreateText(buttonObject.transform, label, 13f, GmTextPrimary);
        text.fontStyle |= FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return button;
    }

    private static TMP_InputField CreateInputField(Transform parent, string placeholder, float width, bool integerOnly)
    {
        GameObject fieldObject = CreateUiObject("Input " + placeholder, parent);
        Image image = fieldObject.AddComponent<Image>();
        image.color = GmSurfaceLow;
        Outline outline = fieldObject.AddComponent<Outline>();
        StyleGmOutline(outline);
        TMP_InputField field = fieldObject.AddComponent<TMP_InputField>();
        field.targetGraphic = image;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.contentType = integerOnly ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.Standard;
        field.gameObject.AddComponent<LayoutElement>().preferredWidth = width;

        GameObject textArea = CreateUiObject("Text Area", fieldObject.transform);
        RectTransform areaRect = textArea.GetComponent<RectTransform>();
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(10f, 3f);
        areaRect.offsetMax = new Vector2(-10f, -3f);
        RectMask2D mask = textArea.AddComponent<RectMask2D>();
        field.textViewport = areaRect;

        TextMeshProUGUI text = CreateText(textArea.transform, string.Empty, 13f, GmTextPrimary);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        field.textComponent = text;

        TextMeshProUGUI hint = CreateText(textArea.transform, placeholder, 13f, GmTextSecondary);
        RectTransform hintRect = hint.rectTransform;
        hintRect.anchorMin = Vector2.zero;
        hintRect.anchorMax = Vector2.one;
        hintRect.offsetMin = hintRect.offsetMax = Vector2.zero;
        field.placeholder = hint;
        field.caretColor = GmTextPrimary;
        field.selectionColor = new Color(GmAccent.r, GmAccent.g, GmAccent.b, 0.32f);
        field.customCaretColor = true;
        return field;
    }

    private static TextMeshProUGUI CreateValueDisplay(
        Transform parent,
        string value,
        float width,
        float height)
    {
        GameObject fieldObject = CreateUiObject("Value Display", parent);
        Image image = fieldObject.AddComponent<Image>();
        image.color = GmSurfaceLow;
        Outline outline = fieldObject.AddComponent<Outline>();
        StyleGmOutline(outline);

        LayoutElement layout = fieldObject.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = height;

        TextMeshProUGUI text = CreateText(
            fieldObject.transform,
            value,
            13f,
            GmTextPrimary);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 2f);
        textRect.offsetMax = new Vector2(-12f, -2f);
        return text;
    }

    private static void CreateSectionTitle(Transform parent, string title)
    {
        TextMeshProUGUI section = CreateText(parent, title, 14f, GmAccent);
        section.fontStyle = FontStyles.Bold;
        section.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
    }

    private static Transform CreateScrollGrid(Transform parent)
    {
        GameObject scrollObject = CreateUiObject("Reflected Command Scroll", parent);
        Image scrollImage = scrollObject.AddComponent<Image>();
        scrollImage.color = GmSurfaceLow;
        Outline outline = scrollObject.AddComponent<Outline>();
        StyleGmOutline(outline);
        LayoutElement scrollLayout = scrollObject.AddComponent<LayoutElement>();
        scrollLayout.flexibleHeight = 1f;
        scrollLayout.minHeight = 180f;
        ScrollRect scroll = scrollObject.AddComponent<ItemStepScrollRect>();
        scroll.horizontal = false;

        GameObject content = CreateScrollViewportWithTopAnchoredContent(scrollObject, scroll,
            new Vector2(7f, 7f), new Vector2(-7f, -7f));
        RectTransform contentRect = content.GetComponent<RectTransform>();
        GridLayoutGroup grid = content.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.cellSize = new Vector2(315f, 32f);
        grid.spacing = new Vector2(7f, 6f);
        grid.padding = new RectOffset(0, 0, 0, 4);
        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRect;
        return content.transform;
    }

    private static void EnsureEventSystem()
    {
        EventSystemGuard.EnsureExactlyOne();
    }

    #endregion
}
