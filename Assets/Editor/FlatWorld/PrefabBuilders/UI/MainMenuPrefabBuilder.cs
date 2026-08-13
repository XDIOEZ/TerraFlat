// AI-Context: 编辑器主界面 Prefab 重建器；根节点直接组合 BasePanel，修改视觉时必须保留控件命名契约。

using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class MainMenuPrefabBuilder
{
    private const string PrefabPath = "Assets/2_Prefabs/2-1_UI/Menu_UI/UI_Hello.prefab";
    private const string BackgroundPath = "Assets/6_Art/UI/MainMenu/FlatWorld_MainMenu_Background.png";
    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";

    private static readonly Color Ink = new Color(0.025f, 0.043f, 0.058f, 0.94f);
    private static readonly Color InkSoft = new Color(0.045f, 0.075f, 0.095f, 0.94f);
    private static readonly Color Cream = new Color(0.95f, 0.91f, 0.81f, 1f);
    private static readonly Color Muted = new Color(0.66f, 0.72f, 0.73f, 1f);
    private static readonly Color Amber = new Color(0.83f, 0.49f, 0.23f, 1f);
    private static readonly Color Teal = new Color(0.26f, 0.61f, 0.57f, 1f);

    [MenuItem("FlatWorld/UI/重建主界面美术")]
    public static void RebuildMainMenu()
    {
        ConfigureBackgroundImporter();

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Sprite backgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
        if (font == null || backgroundSprite == null)
        {
            Debug.LogError($"[MainMenu] 缺少字体或背景图。Font={font != null}, Background={backgroundSprite != null}");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ClearChildren(root.transform);
            ConfigureRoot(root);
            BuildBackground(root.transform, backgroundSprite);
            BuildBrand(root.transform, font);
            BuildMenuCard(root.transform, font);
            BuildSettingsButton(root.transform, font);

            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MainMenu] 主界面美术已重建：像素远景、品牌区、菜单卡与联机入口。");
    }

    private static void ConfigureBackgroundImporter()
    {
        AssetDatabase.ImportAsset(BackgroundPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(BackgroundPath) as TextureImporter;
        if (importer == null)
            return;

        bool changed = importer.textureType != TextureImporterType.Sprite ||
                       importer.spriteImportMode != SpriteImportMode.Single ||
                       importer.mipmapEnabled ||
                       importer.maxTextureSize != 2048 ||
                       importer.filterMode != FilterMode.Bilinear ||
                       importer.wrapMode != TextureWrapMode.Clamp;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = false;
        importer.sRGBTexture = true;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;

        if (changed)
            importer.SaveAndReimport();
    }

    private static void ConfigureRoot(GameObject root)
    {
        root.name = GameManager.MainMenuPanelKey;
        SetUILayerRecursively(root);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

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
        panel.PanelName = GameManager.MainMenuPanelKey;
    }

    private static void BuildBackground(Transform root, Sprite sprite)
    {
        Image background = CreateImage("世界远景", root, Color.white);
        Stretch(background.rectTransform);
        background.sprite = sprite;
        background.type = Image.Type.Simple;
        background.raycastTarget = false;

        Image atmosphere = CreateImage("氛围压暗", root, new Color(0.015f, 0.035f, 0.055f, 0.08f));
        Stretch(atmosphere.rectTransform);
        atmosphere.raycastTarget = false;
    }

    private static void BuildBrand(Transform root, TMP_FontAsset font)
    {
        RectTransform brand = CreateRect("品牌区", root);
        brand.anchorMin = new Vector2(0f, 1f);
        brand.anchorMax = new Vector2(0f, 1f);
        brand.pivot = new Vector2(0f, 1f);
        brand.anchoredPosition = new Vector2(108f, -74f);
        brand.sizeDelta = new Vector2(720f, 270f);

        TMP_Text shadow = CreateText("标题阴影", brand, "平坦世界", font, 100f, new Color(0.015f, 0.025f, 0.03f, 0.72f), FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(shadow.rectTransform, new Vector2(7f, -48f), new Vector2(700f, 122f), new Vector2(0f, 1f));

        TMP_Text title = CreateText("主标题", brand, "平坦世界", font, 100f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(0f, -41f), new Vector2(700f, 122f), new Vector2(0f, 1f));
        title.characterSpacing = 2f;

        Image divider = CreateImage("标题分隔线", brand, Amber);
        SetRect(divider.rectTransform, new Vector2(0f, -176f), new Vector2(86f, 4f), new Vector2(0f, 1f));
        divider.raycastTarget = false;

    }

    private static void BuildMenuCard(Transform root, TMP_FontAsset font)
    {
        Image card = CreateImage("旅程菜单卡", root, Ink);
        RectTransform cardRect = card.rectTransform;
        cardRect.anchorMin = Vector2.zero;
        cardRect.anchorMax = Vector2.zero;
        cardRect.pivot = Vector2.zero;
        cardRect.anchoredPosition = new Vector2(108f, 118f);
        cardRect.sizeDelta = new Vector2(470f, 410f);
        card.raycastTarget = true;

        Outline cardOutline = card.gameObject.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.25f);
        cardOutline.effectDistance = new Vector2(1f, -1f);
        cardOutline.useGraphicAlpha = true;

        Image accent = CreateImage("菜单强调线", card.transform, Amber);
        accent.rectTransform.anchorMin = new Vector2(0f, 0f);
        accent.rectTransform.anchorMax = new Vector2(0f, 1f);
        accent.rectTransform.pivot = new Vector2(0f, 0.5f);
        accent.rectTransform.anchoredPosition = Vector2.zero;
        accent.rectTransform.sizeDelta = new Vector2(5f, 0f);
        accent.raycastTarget = false;

        CreateMenuButton(card.transform, font, GameManager.MainMenuContinueButtonKey, "01", "继续旅程", "载入已有世界", 112f, false, false);
        CreateMenuButton(card.transform, font, GameManager.MainMenuNewGameButtonKey, "02", "新建世界", "自定义你的开局", 202f, false, false);
        CreateMenuButton(card.transform, font, GameManager.MainMenuMultiplayerButtonKey, "03", "联机模式", "与好友共同生存", 292f, false, true);
    }

    /// <summary>创建主菜单右上角的设置入口；当前只负责展示，不绑定设置逻辑。</summary>
    private static void BuildSettingsButton(Transform root, TMP_FontAsset font)
    {
        GameObject buttonObject = new GameObject(
            GameManager.MainMenuSettingsButtonKey,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.layer = LayerMask.NameToLayer("UI");
        buttonObject.transform.SetParent(root, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-108f, -74f);
        rect.sizeDelta = new Vector2(188f, 58f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = InkSoft;
        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.55f, 0.68f, 0.70f, 0.30f);
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.18f, 1.15f, 1.08f, 1f);
        colors.pressedColor = new Color(0.74f, 0.78f, 0.80f, 1f);
        colors.selectedColor = FlatWorldUITheme.Selection;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.12f;
        button.colors = colors;

        TMP_Text eyebrow = CreateText(
            "设置眉题",
            buttonObject.transform,
            "OPTIONS",
            font,
            11f,
            Amber,
            FontStyles.Bold,
            TextAlignmentOptions.Left);
        eyebrow.characterSpacing = 2f;
        SetRect(eyebrow.rectTransform, new Vector2(18f, -8f), new Vector2(126f, 18f), new Vector2(0f, 1f));

        TMP_Text title = CreateText(
            "设置标题",
            buttonObject.transform,
            "设置",
            font,
            21f,
            Cream,
            FontStyles.Bold,
            TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(18f, -29f), new Vector2(126f, 28f), new Vector2(0f, 1f));

        TMP_Text arrow = CreateText(
            "设置箭头",
            buttonObject.transform,
            ">",
            font,
            22f,
            Muted,
            FontStyles.Bold,
            TextAlignmentOptions.Center);
        SetRect(arrow.rectTransform, new Vector2(-22f, 0f), new Vector2(28f, 36f), Vector2.one * 0.5f);
    }

    private static void CreateMenuButton(
        Transform parent,
        TMP_FontAsset font,
        string objectName,
        string index,
        string title,
        string subtitle,
        float top,
        bool primary,
        bool online)
    {
        Color baseColor = primary ? new Color(0.70f, 0.36f, 0.16f, 0.98f) : InkSoft;
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.layer = LayerMask.NameToLayer("UI");
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -top);
        rect.sizeDelta = new Vector2(-60f, 70f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = baseColor;

        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = primary ? new Color(1f, 0.71f, 0.38f, 0.42f) : new Color(0.55f, 0.68f, 0.70f, 0.22f);
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = primary ? new Color(1.13f, 1.08f, 0.98f, 1f) : new Color(1.22f, 1.19f, 1.12f, 1f);
        colors.pressedColor = new Color(0.74f, 0.78f, 0.80f, 1f);
        colors.selectedColor = FlatWorldUITheme.Selection;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.12f;
        button.colors = colors;

        TMP_Text number = CreateText(objectName + "_序号", buttonObject.transform, index, font, 18f, primary ? Cream : Amber, FontStyles.Bold, TextAlignmentOptions.Center);
        SetRect(number.rectTransform, new Vector2(16f, 0f), new Vector2(46f, 70f), new Vector2(0f, 0.5f));

        TMP_Text titleText = CreateText(objectName + "_标题", buttonObject.transform, title, font, 24f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(titleText.rectTransform, new Vector2(78f, 8f), new Vector2(240f, 34f), new Vector2(0f, 0.5f));

        TMP_Text subtitleText = CreateText(objectName + "_说明", buttonObject.transform, subtitle, font, 15f, primary ? new Color(0.96f, 0.86f, 0.73f, 0.9f) : Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(subtitleText.rectTransform, new Vector2(78f, -20f), new Vector2(250f, 24f), new Vector2(0f, 0.5f));

        if (online)
        {
            Image badge = CreateImage("联机状态", buttonObject.transform, new Color(0.12f, 0.31f, 0.31f, 1f));
            SetRect(badge.rectTransform, new Vector2(-80f, 0f), new Vector2(72f, 28f), new Vector2(1f, 0.5f));
            TMP_Text badgeText = CreateText("联机状态文字", badge.transform, "ONLINE", font, 13f, Teal, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(badgeText.rectTransform);
        }
        else
        {
            TMP_Text arrow = CreateText(objectName + "_箭头", buttonObject.transform, ">", font, 22f, primary ? Cream : Muted, FontStyles.Bold, TextAlignmentOptions.Center);
            SetRect(arrow.rectTransform, new Vector2(-32f, 0f), new Vector2(32f, 40f), new Vector2(1f, 0.5f));
        }
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

    private static void SetAnchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
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
            Object.DestroyImmediate(root.GetChild(i).gameObject);
    }

    private static void SetUILayerRecursively(GameObject target)
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        target.layer = uiLayer;
        foreach (Transform child in target.transform)
            SetUILayerRecursively(child.gameObject);
    }
}
