// AI-Context: 编辑器主界面 Prefab 构建与局部换肤；只换肤时保留所有节点、RectTransform 和控件命名契约。

using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class MainMenuPrefabBuilder
{
    private const string PrefabPath = "Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_MainMenu.prefab";
    private const string BackgroundSourcePath = "Assets/6_Art/UI/MainMenu/FlatWorld_MainMenu_Background.png";
    private const string BackgroundPath = "Assets/6_Art/UI/MainMenu/FlatWorld_MainMenu_Background_BlueGreen.png";
    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";

    #region 主菜单专属配色

    // 参考图的中性灰按钮、近白文字与淡金点缀，不修改游戏内通用主题。
    private static readonly Color ButtonFace = new Color32(88, 88, 88, 255);
    private static readonly Color ButtonBorder = new Color32(55, 55, 55, 255);
    private static readonly Color Label = new Color32(236, 238, 239, 255);
    private static readonly Color Muted = new Color32(207, 207, 207, 255);
    private static readonly Color Accent = new Color32(215, 197, 106, 255);
    private static readonly Color TitleShadow = new Color32(12, 17, 20, 200);
    private static readonly Color Atmosphere = new Color(0.025f, 0.04f, 0.035f, 0.08f);

    #endregion

    #region 原位换肤

    /// <summary>只改正式 Prefab 的颜色与背景引用，不重建节点，不触碰布局、文字或事件。</summary>
    [MenuItem("FlatWorld/UI/应用主菜单参考图风格（保持布局）")]
    public static void ApplyReferenceStyle()
    {
        MainMenuBackdropBaker.Bake(BackgroundSourcePath, BackgroundPath);
        ConfigureBackgroundImporter();
        Sprite background = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
        if (background == null)
            throw new System.InvalidOperationException("[MainMenu] 无法加载蓝绿背景。");

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Image world = RequireComponent<Image>(root, "全屏背景/世界远景");
            world.sprite = background;
            world.color = Color.white;
            RequireComponent<Image>(root, "全屏背景/氛围压暗").color = Atmosphere;
            RequireComponent<Image>(root, "品牌区/标题分隔线").color = Accent;

            // 菜单容器仍保留原有尺寸与输入遮挡，只移除卡片及侧边装饰的视觉。
            Image card = RequireComponent<Image>(root, "旅程菜单卡");
            card.color = Color.clear;
            card.GetComponent<Outline>().effectColor = Color.clear;
            RequireComponent<Image>(root, "旅程菜单卡/菜单强调线").color = Color.clear;
            RequireComponent<Image>(root, "旅程菜单卡/联机模式/联机状态").color = ButtonBorder;

            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                button.GetComponent<Image>().color = ButtonFace;
                button.GetComponent<Outline>().effectColor = ButtonBorder;
                ApplyButtonColors(button);
            }

            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                string name = text.gameObject.name;
                text.color = name == "标题阴影" ? TitleShadow
                    : name.EndsWith("_序号", System.StringComparison.Ordinal) || name == "设置眉题" ? Accent
                    : name.EndsWith("_说明", System.StringComparison.Ordinal) || name.EndsWith("箭头", System.StringComparison.Ordinal) ? Muted
                    : Label;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // 只保存目标资源，避免写入当前项目中无关的脏资源。
        AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        Debug.Log("[MainMenu] 参考图风格已应用：灰色按钮、白字淡金、柔焦蓝绿背景；布局与功能保持不变。");
    }

    /// <summary>路径失配时直接停止，避免对错误节点或半套结构静默换肤。</summary>
    private static T RequireComponent<T>(GameObject root, string path) where T : Component
    {
        Transform child = root.transform.Find(path);
        T component = child != null ? child.GetComponent<T>() : null;
        if (component == null)
            throw new System.InvalidOperationException($"[MainMenu] 缺少样式节点 {path} / {typeof(T).Name}。");
        return component;
    }

    /// <summary>仅变更按钮颜色状态；不使用通用主题的彩色选中态，也不改变导航或点击事件。</summary>
    private static void ApplyButtonColors(Button button)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.28f, 1.28f, 1.28f, 1f);
        colors.pressedColor = new Color(0.74f, 0.74f, 0.74f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.1f;
        button.colors = colors;
    }

    #endregion

    [MenuItem("FlatWorld/UI/重建主界面美术")]
    public static void RebuildMainMenu()
    {
        MainMenuBackdropBaker.Bake(BackgroundSourcePath, BackgroundPath);
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
        Debug.Log("[MainMenu] 主界面美术已重建：蓝绿柔焦远景、白字淡金、灰色菜单与联机入口。");
    }

    private static void ConfigureBackgroundImporter()
    {
        AssetDatabase.ImportAsset(BackgroundPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(BackgroundPath) as TextureImporter;
        if (importer == null)
            throw new System.InvalidOperationException("[MainMenu] 无法取得蓝绿背景的纹理导入器。");

        // 检查所有将写入的设置，避免仅颜色空间或压缩发生变化时跳过保存，导致配色无法复现。
        bool changed = importer.textureType != TextureImporterType.Sprite ||
                       importer.spriteImportMode != SpriteImportMode.Single ||
                       importer.mipmapEnabled ||
                       importer.alphaIsTransparency ||
                       !importer.sRGBTexture ||
                       importer.maxTextureSize != 2048 ||
                       importer.textureCompression != TextureImporterCompression.Uncompressed ||
                       importer.filterMode != FilterMode.Bilinear ||
                       importer.wrapMode != TextureWrapMode.Clamp;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = false;
        importer.sRGBTexture = true;
        importer.maxTextureSize = 2048;
        // 柔焦背景包含大面积蓝灰渐变，关闭块压缩，保留参考图的配色和连续色阶。
        importer.textureCompression = TextureImporterCompression.Uncompressed;
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
        RectTransform fullScreenBackground = CreateRect("全屏背景", root);
        Stretch(fullScreenBackground);
        fullScreenBackground.gameObject.AddComponent<FullScreenRectController>();

        Image background = CreateImage("世界远景", fullScreenBackground, Color.white);
        Center(background.rectTransform);
        background.sprite = sprite;
        background.type = Image.Type.Simple;
        background.raycastTarget = false;

        AspectRatioFitter backgroundFitter = background.gameObject.AddComponent<AspectRatioFitter>();
        backgroundFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        backgroundFitter.aspectRatio = sprite.rect.width / sprite.rect.height;

        Image atmosphere = CreateImage("氛围压暗", fullScreenBackground, Atmosphere);
        Stretch(atmosphere.rectTransform);
        atmosphere.raycastTarget = false;
    }

    private static void BuildBrand(Transform root, TMP_FontAsset font)
    {
        RectTransform brand = CreateRect("品牌区", root);
        brand.anchorMin = new Vector2(0f, 1f);
        brand.anchorMax = new Vector2(0f, 1f);
        brand.pivot = new Vector2(0f, 1f);
        brand.anchoredPosition = new Vector2(76f, -60f);
        brand.sizeDelta = new Vector2(720f, 270f);

        TMP_Text shadow = CreateText("标题阴影", brand, "平坦世界", font, 100f, TitleShadow, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(shadow.rectTransform, new Vector2(7f, -48f), new Vector2(700f, 122f), new Vector2(0f, 1f));

        TMP_Text title = CreateText("主标题", brand, "平坦世界", font, 100f, Label, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(0f, -41f), new Vector2(700f, 122f), new Vector2(0f, 1f));
        title.characterSpacing = 2f;

        Image divider = CreateImage("标题分隔线", brand, Accent);
        SetRect(divider.rectTransform, new Vector2(0f, -176f), new Vector2(86f, 4f), new Vector2(0f, 1f));
        divider.raycastTarget = false;

    }

    private static void BuildMenuCard(Transform root, TMP_FontAsset font)
    {
        Image card = CreateImage("旅程菜单卡", root, Color.clear);
        RectTransform cardRect = card.rectTransform;
        cardRect.anchorMin = Vector2.zero;
        cardRect.anchorMax = Vector2.zero;
        cardRect.pivot = Vector2.zero;
        cardRect.anchoredPosition = new Vector2(76f, 76f);
        cardRect.sizeDelta = new Vector2(560f, 410f);
        card.raycastTarget = true;

        Outline cardOutline = card.gameObject.AddComponent<Outline>();
        cardOutline.effectColor = Color.clear;
        cardOutline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;
        cardOutline.useGraphicAlpha = true;

        Image accent = CreateImage("菜单强调线", card.transform, Color.clear);
        accent.rectTransform.anchorMin = new Vector2(0f, 0f);
        accent.rectTransform.anchorMax = new Vector2(0f, 1f);
        accent.rectTransform.pivot = new Vector2(0f, 0.5f);
        accent.rectTransform.anchoredPosition = Vector2.zero;
        accent.rectTransform.sizeDelta = new Vector2(5f, 0f);
        accent.raycastTarget = false;

        CreateMenuButton(card.transform, font, GameManager.MainMenuContinueButtonKey, "01", "继续旅程", "载入已有世界", 40f, false);
        CreateMenuButton(card.transform, font, GameManager.MainMenuNewGameButtonKey, "02", "新建世界", "自定义你的开局", 154f, false);
        CreateMenuButton(card.transform, font, GameManager.MainMenuMultiplayerButtonKey, "03", "联机模式", "与好友共同生存", 268f, true);
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
        rect.anchoredPosition = new Vector2(-76f, -60f);
        rect.sizeDelta = new Vector2(180f, 72f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = ButtonFace;
        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = ButtonBorder;
        outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;
        outline.useGraphicAlpha = true;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ApplyButtonColors(button);

        TMP_Text eyebrow = CreateText(
            "设置眉题",
            buttonObject.transform,
            "OPTIONS",
            font,
            10f,
            Accent,
            FontStyles.Bold,
            TextAlignmentOptions.Left);
        eyebrow.characterSpacing = 2f;
        SetRect(eyebrow.rectTransform, new Vector2(16f, -12f), new Vector2(118f, 18f), new Vector2(0f, 1f));

        TMP_Text title = CreateText(
            "设置标题",
            buttonObject.transform,
            "设置",
            font,
            20f,
            Label,
            FontStyles.Bold,
            TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(16f, -33f), new Vector2(118f, 28f), new Vector2(0f, 1f));

        TMP_Text arrow = CreateText(
            "设置箭头",
            buttonObject.transform,
            ">",
            font,
            18f,
            Muted,
            FontStyles.Bold,
            TextAlignmentOptions.Center);
        SetRect(arrow.rectTransform, new Vector2(-19f, 0f), new Vector2(24f, 30f), Vector2.one * 0.5f);
    }

    private static void CreateMenuButton(
        Transform parent,
        TMP_FontAsset font,
        string objectName,
        string index,
        string title,
        string subtitle,
        float top,
        bool online)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.layer = LayerMask.NameToLayer("UI");
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -top);
        rect.sizeDelta = new Vector2(-48f, 104f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = ButtonFace;

        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = ButtonBorder;
        outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;
        outline.useGraphicAlpha = true;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ApplyButtonColors(button);

        TMP_Text number = CreateText(objectName + "_序号", buttonObject.transform, index, font, 18f, Accent, FontStyles.Bold, TextAlignmentOptions.Center);
        SetRect(number.rectTransform, new Vector2(16f, 0f), new Vector2(54f, 104f), new Vector2(0f, 0.5f));

        TMP_Text titleText = CreateText(objectName + "_标题", buttonObject.transform, title, font, 27f, Label, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(titleText.rectTransform, new Vector2(88f, 13f), new Vector2(280f, 38f), new Vector2(0f, 0.5f));

        TMP_Text subtitleText = CreateText(objectName + "_说明", buttonObject.transform, subtitle, font, 17f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(subtitleText.rectTransform, new Vector2(88f, -23f), new Vector2(300f, 28f), new Vector2(0f, 0.5f));

        if (online)
        {
            Image badge = CreateImage("联机状态", buttonObject.transform, ButtonBorder);
            SetRect(badge.rectTransform, new Vector2(-80f, 0f), new Vector2(72f, 28f), new Vector2(1f, 0.5f));
            TMP_Text badgeText = CreateText("联机状态文字", badge.transform, "ONLINE", font, 13f, Label, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(badgeText.rectTransform);
        }
        else
        {
            TMP_Text arrow = CreateText(objectName + "_箭头", buttonObject.transform, ">", font, 22f, Muted, FontStyles.Bold, TextAlignmentOptions.Center);
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

    /// <summary>居中交给 AspectRatioFitter 管理尺寸。</summary>
    private static void Center(RectTransform rect)
    {
        rect.anchorMin = Vector2.one * 0.5f;
        rect.anchorMax = Vector2.one * 0.5f;
        rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
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
