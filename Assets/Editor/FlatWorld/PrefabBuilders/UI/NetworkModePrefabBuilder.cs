// AI-Context: 编辑器联机面板 Prefab 构建器；运行时只能通过 GameRes 加载生成的 UI_NetworkMode.prefab。

using FlatWorld.Networking.Gameplay;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class NetworkModePrefabBuilder
{
    private const string PrefabPath = "Assets/2_Prefabs/2-1_UI/MainMenu/WorldSetup/UI_NetworkMode.prefab";
    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";

    private static readonly Color Ink = new Color(0.025f, 0.043f, 0.058f, 0.98f);
    private static readonly Color InkSoft = new Color(0.045f, 0.075f, 0.095f, 0.98f);
    private static readonly Color Cream = new Color(0.95f, 0.91f, 0.81f, 1f);
    private static readonly Color Muted = new Color(0.64f, 0.70f, 0.71f, 1f);
    private static readonly Color Amber = new Color(0.83f, 0.49f, 0.23f, 1f);
    private static readonly Color Teal = new Color(0.26f, 0.61f, 0.57f, 1f);

    #region Prefab 重建入口

    [MenuItem("FlatWorld/UI/Rebuild Network Mode UI")]
    public static void RebuildNetworkModeInterface()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError("[NetworkModeUI] 未找到项目像素字体。");
            return;
        }

        GameObject root = CreateRoot();
        try
        {
            BuildVisualTree(root.transform, font);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[NetworkModeUI] 联机面板 Prefab 已重建：{PrefabPath}");
    }

    private static GameObject CreateRoot()
    {
        GameObject root = new GameObject(
            NetworkModeUIController.NetworkPanelKey,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasGroup),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(GraphicRaycaster),
            typeof(BasePanel));
        root.layer = LayerMask.NameToLayer("UI");

        RectTransform rect = root.GetComponent<RectTransform>();
        Stretch(rect);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 500;

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        Image scrim = root.GetComponent<Image>();
        scrim.color = new Color(0.006f, 0.016f, 0.024f, 0.68f);
        scrim.raycastTarget = true;

        BasePanel panel = root.GetComponent<BasePanel>();
        panel.PanelName = NetworkModeUIController.NetworkPanelKey;
        panel.canvasGroup = canvasGroup;
        panel.rectTransform = rect;
        return root;
    }

    #endregion

    #region 视觉结构

    private static void BuildVisualTree(Transform root, TMP_FontAsset font)
    {
        Image shadow = CreateImage("面板投影", root, new Color(0f, 0f, 0f, 0.38f));
        SetRect(shadow.rectTransform, new Vector2(12f, -14f), FlatWorldUIPanelMetrics.SharedModalCardSize, new Vector2(0.5f, 0.5f));
        shadow.raycastTarget = false;

        Image card = CreateImage("联机主卡片", root, Ink);
        SetRect(card.rectTransform, Vector2.zero, FlatWorldUIPanelMetrics.SharedModalCardSize, new Vector2(0.5f, 0.5f));

        Outline cardOutline = card.gameObject.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.32f);
        cardOutline.effectDistance = new Vector2(1f, -1f);
        cardOutline.useGraphicAlpha = true;

        Image accent = CreateImage("卡片强调线", card.transform, Amber);
        accent.rectTransform.anchorMin = new Vector2(0f, 0f);
        accent.rectTransform.anchorMax = new Vector2(0f, 1f);
        accent.rectTransform.pivot = new Vector2(0f, 0.5f);
        accent.rectTransform.anchoredPosition = Vector2.zero;
        accent.rectTransform.sizeDelta = new Vector2(6f, 0f);
        accent.raycastTarget = false;

        BuildHeader(card.transform, font);
        BuildConnectionForm(card.transform, font);
        BuildSessionSummary(card.transform, font);
        BuildActions(card.transform, font);
    }

    private static void BuildHeader(Transform card, TMP_FontAsset font)
    {
        TMP_Text title = CreateText("标题", card, "联机模式", font, 42f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(42f, -38f), new Vector2(700f, 58f), new Vector2(0f, 1f));

        CreateButton(card, font, "关闭按钮", "×", new Vector2(-34f, -34f), new Vector2(48f, 48f), new Color(0.08f, 0.11f, 0.13f, 0.96f), new Color(0.64f, 0.70f, 0.71f, 0.28f), 28f, new Vector2(1f, 1f));

        Image divider = CreateImage("标题分隔线", card, new Color(0.55f, 0.64f, 0.65f, 0.18f));
        divider.rectTransform.anchorMin = new Vector2(0f, 1f);
        divider.rectTransform.anchorMax = new Vector2(1f, 1f);
        divider.rectTransform.pivot = new Vector2(0.5f, 1f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, -112f);
        divider.rectTransform.sizeDelta = new Vector2(-84f, 1f);
        divider.raycastTarget = false;
    }

    private static void BuildConnectionForm(Transform card, TMP_FontAsset font)
    {
        TMP_Text heading = CreateText("连接设置标题", card, "连接设置", font, 22f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(heading.rectTransform, new Vector2(42f, -150f), new Vector2(760f, 34f), new Vector2(0f, 1f));

        CreateInput(card, font, "玩家名称输入框", "玩家名称", "你在联机世界中的显示名称", "Player_0000", new Vector2(42f, -200f), new Vector2(760f, 66f));
        CreateInput(card, font, "地址输入框", "主机 / UDP 穿透地址", "例如 tunnel.example.com:24567", "127.0.0.1", new Vector2(42f, -300f), new Vector2(520f, 66f));
        CreateInput(card, font, "端口输入框", "主机 / 默认端口", "7777", "7777", new Vector2(580f, -300f), new Vector2(220f, 66f), TMP_InputField.ContentType.IntegerNumber);

        Image notice = CreateImage("同步说明底板", card, new Color(0.07f, 0.105f, 0.125f, 0.92f));
        SetRect(notice.rectTransform, new Vector2(42f, -400f), new Vector2(760f, 84f), new Vector2(0f, 1f));

        Image noticeAccent = CreateImage("同步说明强调线", notice.transform, Teal);
        noticeAccent.rectTransform.anchorMin = new Vector2(0f, 0f);
        noticeAccent.rectTransform.anchorMax = new Vector2(0f, 1f);
        noticeAccent.rectTransform.pivot = new Vector2(0f, 0.5f);
        noticeAccent.rectTransform.anchoredPosition = Vector2.zero;
        noticeAccent.rectTransform.sizeDelta = new Vector2(4f, 0f);
        noticeAccent.raycastTarget = false;

        TMP_Text noticeTitle = CreateText("同步说明标题", notice.transform, "世界同步", font, 17f, Teal, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(noticeTitle.rectTransform, new Vector2(18f, -10f), new Vector2(260f, 26f), new Vector2(0f, 1f));

        TMP_Text noticeText = CreateText("同步说明文字", notice.transform, "可直接粘贴 域名:端口；穿透协议必须为 UDP，地址自带端口时会覆盖默认端口。", font, 15f, Muted, FontStyles.Normal, TextAlignmentOptions.Left, true);
        SetRect(noticeText.rectTransform, new Vector2(18f, -38f), new Vector2(718f, 34f), new Vector2(0f, 1f));
    }

    private static void BuildSessionSummary(Transform card, TMP_FontAsset font)
    {
        Image summary = CreateImage("会话状态卡", card, new Color(0.035f, 0.06f, 0.075f, 0.98f));
        SetRect(summary.rectTransform, new Vector2(-42f, -150f), new Vector2(420f, 420f), new Vector2(1f, 1f));

        Outline outline = summary.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.55f, 0.64f, 0.65f, 0.18f);
        outline.effectDistance = new Vector2(1f, -1f);

        TMP_Text heading = CreateText("会话状态标题", summary.transform, "会话状态", font, 21f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(heading.rectTransform, new Vector2(24f, -24f), new Vector2(220f, 32f), new Vector2(0f, 1f));

        Image statusPill = CreateImage("状态底板", summary.transform, new Color(0.07f, 0.16f, 0.15f, 1f));
        SetRect(statusPill.rectTransform, new Vector2(24f, -70f), new Vector2(372f, 56f), new Vector2(0f, 1f));

        Image statusDot = CreateImage("状态指示点", statusPill.transform, Teal);
        SetRect(statusDot.rectTransform, new Vector2(18f, 0f), new Vector2(10f, 10f), new Vector2(0f, 0.5f));
        statusDot.raycastTarget = false;

        TMP_Text status = CreateText("状态文本", statusPill.transform, "离线", font, 16f, new Color(0.58f, 0.88f, 0.79f, 1f), FontStyles.Bold, TextAlignmentOptions.Left, true);
        status.rectTransform.anchorMin = new Vector2(0f, 0f);
        status.rectTransform.anchorMax = new Vector2(1f, 1f);
        status.rectTransform.offsetMin = new Vector2(38f, 8f);
        status.rectTransform.offsetMax = new Vector2(-12f, -8f);

        TMP_Text playersLabel = CreateText("玩家数量标签", summary.transform, "当前连接", font, 14f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(playersLabel.rectTransform, new Vector2(24f, -146f), new Vector2(300f, 24f), new Vector2(0f, 1f));

        TMP_Text players = CreateText("玩家数量文本", summary.transform, "玩家：0 / 2", font, 28f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(players.rectTransform, new Vector2(24f, -174f), new Vector2(300f, 42f), new Vector2(0f, 1f));

        Image divider = CreateImage("状态分隔线", summary.transform, new Color(0.55f, 0.64f, 0.65f, 0.16f));
        SetRect(divider.rectTransform, new Vector2(24f, -232f), new Vector2(372f, 1f), new Vector2(0f, 1f));
        divider.raycastTarget = false;
    }

    private static void BuildActions(Transform card, TMP_FontAsset font)
    {
        Image divider = CreateImage("操作区分隔线", card, new Color(0.55f, 0.64f, 0.65f, 0.18f));
        divider.rectTransform.anchorMin = new Vector2(0f, 0f);
        divider.rectTransform.anchorMax = new Vector2(1f, 0f);
        divider.rectTransform.pivot = new Vector2(0.5f, 0f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, 128f);
        divider.rectTransform.sizeDelta = new Vector2(-84f, 1f);
        divider.raycastTarget = false;

        CreateButton(card, font, "创建主机按钮", "创建主机", new Vector2(42f, 42f), new Vector2(280f, 68f), new Color(0.70f, 0.36f, 0.16f, 1f), new Color(1f, 0.71f, 0.38f, 0.38f), 20f, Vector2.zero);
        CreateButton(card, font, "加入游戏按钮", "加入好友", new Vector2(342f, 42f), new Vector2(280f, 68f), new Color(0.08f, 0.29f, 0.29f, 1f), new Color(0.36f, 0.78f, 0.72f, 0.34f), 20f, Vector2.zero);
        CreateButton(card, font, "断开按钮", "断开连接", new Vector2(-42f, 42f), new Vector2(240f, 68f), new Color(0.25f, 0.075f, 0.075f, 0.96f), new Color(0.78f, 0.34f, 0.29f, 0.30f), 18f, new Vector2(1f, 0f));
    }

    #endregion

    #region UI 元素工厂

    private static TMP_InputField CreateInput(
        Transform parent,
        TMP_FontAsset font,
        string objectName,
        string labelValue,
        string placeholderValue,
        string initialValue,
        Vector2 position,
        Vector2 size,
        TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard)
    {
        TMP_Text label = CreateText(objectName + "_标签", parent, labelValue, font, 15f, Muted, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(label.rectTransform, position, new Vector2(size.x, 24f), new Vector2(0f, 1f));

        GameObject inputObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        inputObject.layer = LayerMask.NameToLayer("UI");
        inputObject.transform.SetParent(parent, false);

        RectTransform inputRect = inputObject.GetComponent<RectTransform>();
        SetRect(inputRect, position + new Vector2(0f, -26f), size, new Vector2(0f, 1f));

        Image background = inputObject.GetComponent<Image>();
        background.color = InkSoft;

        Outline outline = inputObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.55f, 0.64f, 0.65f, 0.22f);
        outline.effectDistance = new Vector2(1f, -1f);

        GameObject viewportObject = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        viewportObject.layer = LayerMask.NameToLayer("UI");
        viewportObject.transform.SetParent(inputObject.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        Stretch(viewport);
        viewport.offsetMin = new Vector2(16f, 7f);
        viewport.offsetMax = new Vector2(-16f, -7f);

        TMP_Text placeholder = CreateInputText(viewportObject.transform, font, objectName + "_占位文字", placeholderValue);
        placeholder.color = new Color(0.53f, 0.59f, 0.60f, 0.80f);

        TMP_Text valueText = CreateInputText(viewportObject.transform, font, objectName + "_输入文字", initialValue);

        TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = viewport;
        input.textComponent = (TextMeshProUGUI)valueText;
        input.placeholder = placeholder;
        input.contentType = contentType;
        input.text = initialValue;
        input.caretColor = Cream;
        input.selectionColor = new Color(0.83f, 0.49f, 0.23f, 0.42f);
        input.customCaretColor = true;

        ColorBlock colors = input.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.08f, 1f, 1f);
        colors.selectedColor = FlatWorldUITheme.Selection;
        colors.disabledColor = new Color(0.58f, 0.60f, 0.60f, 0.55f);
        colors.fadeDuration = 0.12f;
        input.colors = colors;
        return input;
    }

    private static TMP_Text CreateInputText(Transform parent, TMP_FontAsset font, string objectName, string value)
    {
        TMP_Text text = CreateText(objectName, parent, value, font, 19f, Cream, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        Stretch(text.rectTransform);
        return text;
    }

    private static Button CreateButton(
        Transform parent,
        TMP_FontAsset font,
        string objectName,
        string caption,
        Vector2 position,
        Vector2 size,
        Color color,
        Color outlineColor,
        float fontSize,
        Vector2 pivot)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.layer = LayerMask.NameToLayer("UI");
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        SetRect(rect, position, size, pivot);

        Image image = buttonObject.GetComponent<Image>();
        image.color = color;

        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = new Vector2(1f, -1f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.18f, 1.13f, 1.04f, 1f);
        colors.pressedColor = new Color(0.72f, 0.76f, 0.78f, 1f);
        colors.selectedColor = FlatWorldUITheme.Selection;
        colors.disabledColor = new Color(0.42f, 0.43f, 0.44f, 0.56f);
        colors.fadeDuration = 0.12f;
        button.colors = colors;

        TMP_Text label = CreateText(objectName + "_文字", buttonObject.transform, caption, font, fontSize, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(label.rectTransform);
        return button;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        string value,
        TMP_FontAsset font,
        float fontSize,
        Color color,
        FontStyles style,
        TextAlignmentOptions alignment,
        bool wordWrapping = false)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = wordWrapping;
        text.overflowMode = wordWrapping ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
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

    #endregion
}
