// AI-Context: 编辑器新游戏 Prefab 重建器；根节点直接组合 BasePanel，不得改名 GameManager 依赖的控件节点。

using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class NewGamePrefabBuilder
{
    private const string PrefabPath = "Assets/2_Prefabs/2-1_UI/Menu_UI/UI_NewGame.prefab";
    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";

    private static readonly Color Ink = new Color(0.025f, 0.043f, 0.058f, 0.985f);
    private static readonly Color InkSoft = new Color(0.045f, 0.075f, 0.095f, 0.98f);
    private static readonly Color Surface = new Color(0.06f, 0.095f, 0.115f, 0.98f);
    private static readonly Color Cream = new Color(0.95f, 0.91f, 0.81f, 1f);
    private static readonly Color Muted = new Color(0.64f, 0.70f, 0.71f, 1f);
    private static readonly Color Amber = new Color(0.83f, 0.49f, 0.23f, 1f);
    private static readonly Color Teal = new Color(0.26f, 0.61f, 0.57f, 1f);

    [MenuItem("FlatWorld/UI/Rebuild New Game UI")]
    public static void RebuildNewGameInterface()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError("[NewGameUI] 未找到项目像素字体。");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ClearChildren(root.transform);
            ConfigureRoot(root);
            BuildScrim(root.transform);
            Image card = BuildCard(root.transform);
            BuildHeader(card.transform, font);
            BuildIdentity(card.transform, font);
            BuildWorldSettings(card.transform, font);
            BuildFooter(card.transform, font);

            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[NewGameUI] 新世界创建界面已重建。");
    }

    private static void ConfigureRoot(GameObject root)
    {
        root.name = "UI_NewGame";
        root.layer = LayerMask.NameToLayer("UI");

        RectTransform rect = root.GetComponent<RectTransform>();
        Stretch(rect);

        CanvasGroup group = root.GetComponent<CanvasGroup>();
        if (group == null)
            group = root.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;

        BasePanel panel = root.GetComponent<BasePanel>();
        if (panel == null)
            panel = root.AddComponent<BasePanel>();

        panel.canvasGroup = group;
        panel.rectTransform = rect;
        panel.PanelName = GameManager.NewGamePanelKey;
    }

    private static void BuildScrim(Transform root)
    {
        Image scrim = CreateImage("新世界界面遮罩", root, new Color(0.006f, 0.016f, 0.024f, 0.76f));
        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;
    }

    private static Image BuildCard(Transform root)
    {
        Image shadow = CreateImage("新世界主卡投影", root, new Color(0f, 0f, 0f, 0.42f));
        SetRect(shadow.rectTransform, new Vector2(14f, -16f), new Vector2(1200f, 760f), new Vector2(0.5f, 0.5f));
        shadow.raycastTarget = false;

        Image card = CreateImage("新世界主卡", root, Ink);
        SetRect(card.rectTransform, Vector2.zero, new Vector2(1200f, 760f), new Vector2(0.5f, 0.5f));

        Outline outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.34f);
        outline.effectDistance = new Vector2(1f, -1f);

        Image accent = CreateImage("新世界主卡强调线", card.transform, Amber);
        accent.rectTransform.anchorMin = new Vector2(0f, 0f);
        accent.rectTransform.anchorMax = new Vector2(0f, 1f);
        accent.rectTransform.pivot = new Vector2(0f, 0.5f);
        accent.rectTransform.anchoredPosition = Vector2.zero;
        accent.rectTransform.sizeDelta = new Vector2(6f, 0f);
        accent.raycastTarget = false;
        return card;
    }

    private static void BuildHeader(Transform card, TMP_FontAsset font)
    {
        TMP_Text eyebrow = CreateText("新世界眉题", card, "NEW WORLD  /  世界生成", font, 16f, Amber, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(eyebrow.rectTransform, new Vector2(42f, -28f), new Vector2(520f, 26f), new Vector2(0f, 1f));
        eyebrow.characterSpacing = 3f;

        TMP_Text title = CreateText("新世界标题", card, "创建新世界", font, 42f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(42f, -60f), new Vector2(600f, 58f), new Vector2(0f, 1f));

        TMP_Text description = CreateText("新世界说明", card, "为新的旅程命名，并决定这个世界最初的轮廓。", font, 18f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(description.rectTransform, new Vector2(42f, -116f), new Vector2(720f, 30f), new Vector2(0f, 1f));

        CreateButton(card, font, GameManager.NewGameBackButtonKey, "返回主界面", new Vector2(-42f, -42f), new Vector2(170f, 52f), InkSoft, 17f, new Vector2(1f, 1f));

        Image divider = CreateImage("新世界标题分隔线", card, new Color(0.55f, 0.64f, 0.65f, 0.18f));
        divider.rectTransform.anchorMin = new Vector2(0f, 1f);
        divider.rectTransform.anchorMax = new Vector2(1f, 1f);
        divider.rectTransform.pivot = new Vector2(0.5f, 1f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, -162f);
        divider.rectTransform.sizeDelta = new Vector2(-84f, 1f);
        divider.raycastTarget = false;
    }

    private static void BuildIdentity(Transform card, TMP_FontAsset font)
    {
        Image panel = CreatePanelCard("身份与存档区", card);
        SetRect(panel.rectTransform, new Vector2(42f, -188f), new Vector2(500f, 452f), new Vector2(0f, 1f));

        CreateStepBadge(panel.transform, font, "STEP 01", new Vector2(24f, -22f));
        TMP_Text heading = CreateText("身份与存档标题", panel.transform, "身份与存档", font, 23f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(heading.rectTransform, new Vector2(24f, -58f), new Vector2(320f, 34f), new Vector2(0f, 1f));

        CreateLabel(panel.transform, font, "玩家名称标签", "玩家名称", "你在这个世界中的身份", new Vector2(24f, -108f), 452f);
        CreateInput(panel.transform, font, GameManager.NewGamePlayerInputKey, "例如：旅人", string.Empty, new Vector2(24f, -152f), new Vector2(452f, 64f), TMP_InputField.ContentType.Standard);

        CreateLabel(panel.transform, font, "存档名称标签", "存档名称", "用于识别这段旅程", new Vector2(24f, -244f), 452f);
        CreateInput(panel.transform, font, GameManager.NewGameSaveInputKey, "例如：篝火以北", string.Empty, new Vector2(24f, -288f), new Vector2(452f, 64f), TMP_InputField.ContentType.Standard);

        Image note = CreateImage("存档命名提示底板", panel.transform, new Color(0.035f, 0.06f, 0.075f, 0.98f));
        SetRect(note.rectTransform, new Vector2(24f, -380f), new Vector2(452f, 50f), new Vector2(0f, 1f));
        Image noteAccent = CreateImage("存档命名提示强调线", note.transform, Teal);
        noteAccent.rectTransform.anchorMin = new Vector2(0f, 0f);
        noteAccent.rectTransform.anchorMax = new Vector2(0f, 1f);
        noteAccent.rectTransform.pivot = new Vector2(0f, 0.5f);
        noteAccent.rectTransform.anchoredPosition = Vector2.zero;
        noteAccent.rectTransform.sizeDelta = new Vector2(4f, 0f);
        noteAccent.raycastTarget = false;

        TMP_Text noteText = CreateText("存档命名提示", note.transform, "名称可稍后修改；存档会保存在本地。", font, 14f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        Stretch(noteText.rectTransform, 18f, 12f, 8f, 8f);
    }

    private static void BuildWorldSettings(Transform card, TMP_FontAsset font)
    {
        Image panel = CreatePanelCard("世界参数区", card);
        SetRect(panel.rectTransform, new Vector2(562f, -188f), new Vector2(596f, 452f), new Vector2(0f, 1f));

        CreateStepBadge(panel.transform, font, "STEP 02", new Vector2(24f, -22f));
        TMP_Text heading = CreateText("世界参数标题", panel.transform, "世界参数", font, 23f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(heading.rectTransform, new Vector2(24f, -58f), new Vector2(320f, 34f), new Vector2(0f, 1f));

        CreateCompactLabel(panel.transform, font, "星球半径标签", "星球半径", "越大，探索范围越广", new Vector2(24f, -108f), 258f);
        CreateCompactLabel(panel.transform, font, "噪声缩放标签", "地形尺度", "越小，地貌越舒展", new Vector2(306f, -108f), 266f);

        CreateInput(panel.transform, font, GameManager.NewGameRadiusInputKey, "1000", "1000", new Vector2(24f, -164f), new Vector2(258f, 64f), TMP_InputField.ContentType.IntegerNumber);
        CreateInput(panel.transform, font, GameManager.NewGameNoiseInputKey, "0.01", "0.01", new Vector2(306f, -164f), new Vector2(266f, 64f), TMP_InputField.ContentType.DecimalNumber);

        Image profile = CreateImage("世界生成概览", panel.transform, new Color(0.035f, 0.06f, 0.075f, 0.98f));
        SetRect(profile.rectTransform, new Vector2(24f, -252f), new Vector2(548f, 174f), new Vector2(0f, 1f));

        TMP_Text profileEyebrow = CreateText("世界生成概览眉题", profile.transform, "GENERATION PROFILE", font, 12f, Teal, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(profileEyebrow.rectTransform, new Vector2(18f, -14f), new Vector2(320f, 22f), new Vector2(0f, 1f));
        profileEyebrow.characterSpacing = 2f;

        CreateProfileRow(profile.transform, font, "地形生成", "程序化生成", 48f);
        CreateProfileRow(profile.transform, font, "世界种子", "自动随机", 82f);
        CreateProfileRow(profile.transform, font, "出生区域", "自动寻找安全陆地", 116f);

        TMP_Text tip = CreateText("世界参数提示", profile.transform, "推荐首次游玩保留默认参数。", font, 13f, new Color(0.74f, 0.67f, 0.54f, 1f), FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(tip.rectTransform, new Vector2(18f, -146f), new Vector2(500f, 22f), new Vector2(0f, 1f));
    }

    private static void BuildFooter(Transform card, TMP_FontAsset font)
    {
        Image divider = CreateImage("新世界操作分隔线", card, new Color(0.55f, 0.64f, 0.65f, 0.18f));
        divider.rectTransform.anchorMin = new Vector2(0f, 0f);
        divider.rectTransform.anchorMax = new Vector2(1f, 0f);
        divider.rectTransform.pivot = new Vector2(0.5f, 0f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, 98f);
        divider.rectTransform.sizeDelta = new Vector2(-84f, 1f);
        divider.raycastTarget = false;

        TMP_Text flow = CreateText("新世界流程提示", card, "确认身份  >  调整世界参数  >  生成世界", font, 15f, Muted, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(flow.rectTransform, new Vector2(42f, 34f), new Vector2(650f, 34f), new Vector2(0f, 0f));
        flow.characterSpacing = 1f;

        CreateButton(card, font, GameManager.NewGameStartButtonKey, "生成新世界", new Vector2(-42f, 22f), new Vector2(250f, 66f), new Color(0.70f, 0.36f, 0.16f, 1f), 21f, new Vector2(1f, 0f));
    }

    private static void CreateLabel(Transform parent, TMP_FontAsset font, string name, string title, string subtitle, Vector2 position, float width)
    {
        TMP_Text titleText = CreateText(name, parent, title, font, 17f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(titleText.rectTransform, position, new Vector2(width, 26f), new Vector2(0f, 1f));
        TMP_Text subtitleText = CreateText(name + "_说明", parent, subtitle, font, 13f, Muted, FontStyles.Normal, TextAlignmentOptions.Right);
        SetRect(subtitleText.rectTransform, position, new Vector2(width, 26f), new Vector2(0f, 1f));
    }

    private static void CreateCompactLabel(Transform parent, TMP_FontAsset font, string name, string title, string subtitle, Vector2 position, float width)
    {
        TMP_Text titleText = CreateText(name, parent, title, font, 16f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(titleText.rectTransform, position, new Vector2(width, 24f), new Vector2(0f, 1f));
        TMP_Text subtitleText = CreateText(name + "_说明", parent, subtitle, font, 12f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(subtitleText.rectTransform, position + new Vector2(0f, -24f), new Vector2(width, 22f), new Vector2(0f, 1f));
    }

    private static void CreateProfileRow(Transform parent, TMP_FontAsset font, string label, string value, float top)
    {
        TMP_Text labelText = CreateText(label + "_标签", parent, label, font, 14f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(labelText.rectTransform, new Vector2(18f, -top), new Vector2(220f, 24f), new Vector2(0f, 1f));
        TMP_Text valueText = CreateText(label + "_数值", parent, value, font, 14f, Cream, FontStyles.Bold, TextAlignmentOptions.Right);
        SetRect(valueText.rectTransform, new Vector2(-18f, -top), new Vector2(280f, 24f), new Vector2(1f, 1f));
    }

    private static TMP_InputField CreateInput(Transform parent, TMP_FontAsset font, string name, string placeholderValue, string initialValue, Vector2 position, Vector2 size, TMP_InputField.ContentType contentType)
    {
        GameObject inputObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        inputObject.layer = LayerMask.NameToLayer("UI");
        inputObject.transform.SetParent(parent, false);
        SetRect(inputObject.GetComponent<RectTransform>(), position, size, new Vector2(0f, 1f));

        Image background = inputObject.GetComponent<Image>();
        background.color = InkSoft;
        Outline outline = inputObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.55f, 0.64f, 0.65f, 0.24f);
        outline.effectDistance = new Vector2(1f, -1f);

        GameObject areaObject = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        areaObject.layer = LayerMask.NameToLayer("UI");
        areaObject.transform.SetParent(inputObject.transform, false);
        RectTransform area = areaObject.GetComponent<RectTransform>();
        Stretch(area, 16f, 16f, 8f, 8f);

        TextMeshProUGUI placeholder = (TextMeshProUGUI)CreateText("Placeholder", area, placeholderValue, font, 17f, new Color(0.53f, 0.59f, 0.60f, 0.82f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        Stretch(placeholder.rectTransform);
        TextMeshProUGUI valueText = (TextMeshProUGUI)CreateText("Text", area, initialValue, font, 18f, Cream, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        Stretch(valueText.rectTransform);

        TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = area;
        input.placeholder = placeholder;
        input.textComponent = valueText;
        input.contentType = contentType;
        input.text = initialValue;
        input.caretColor = Cream;
        input.customCaretColor = true;
        input.selectionColor = new Color(0.83f, 0.49f, 0.23f, 0.42f);
        return input;
    }

    private static Image CreatePanelCard(string name, Transform parent)
    {
        Image panel = CreateImage(name, parent, Surface);
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.55f, 0.64f, 0.65f, 0.18f);
        outline.effectDistance = new Vector2(1f, -1f);
        return panel;
    }

    private static void CreateStepBadge(Transform parent, TMP_FontAsset font, string value, Vector2 position)
    {
        Image badge = CreateImage(value + "_底板", parent, new Color(0.12f, 0.23f, 0.22f, 1f));
        SetRect(badge.rectTransform, position, new Vector2(88f, 26f), new Vector2(0f, 1f));
        TMP_Text text = CreateText(value + "_文字", badge.transform, value, font, 12f, Teal, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.characterSpacing = 1f;
    }

    private static Button CreateButton(Transform parent, TMP_FontAsset font, string name, string caption, Vector2 position, Vector2 size, Color color, float fontSize, Vector2 pivot)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.layer = LayerMask.NameToLayer("UI");
        buttonObject.transform.SetParent(parent, false);
        SetRect(buttonObject.GetComponent<RectTransform>(), position, size, pivot);

        Image image = buttonObject.GetComponent<Image>();
        image.color = color;
        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.28f);
        outline.effectDistance = new Vector2(1f, -1f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.16f, 1.11f, 1.02f, 1f);
        colors.pressedColor = new Color(0.72f, 0.76f, 0.78f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.42f, 0.43f, 0.44f, 0.56f);
        colors.fadeDuration = 0.12f;
        button.colors = colors;

        TMP_Text label = CreateText(name + "_文字", buttonObject.transform, caption, font, fontSize, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
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

    private static TMP_Text CreateText(string name, Transform parent, string value, TMP_FontAsset font, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
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
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float bottom = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rect.anchorMin = pivot;
        rect.anchorMax = pivot;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.GetChild(i).gameObject, true);
    }
}
