// AI-Context: 编辑器主界面 Prefab 构建与局部换肤；只换肤时保留所有节点、RectTransform 和控件命名契约。

using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class MainMenuPrefabBuilder
{
    private const string PrefabPath = "Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_MainMenu.prefab";
    private const string BackgroundRoot = "Assets/6_Art/Generated/MainMenuBackgrounds/";
    private const string MenuIconRoot = "Assets/6_Art/UI/MainMenuIcons/";
    private static readonly string[] BackgroundFiles =
    {
        "01_morning_grassland.png", "02_orbital_world.png", "03_space_launch.png",
        "04_night_base.png", "05_rain_forest.png", "06_volcanic_land.png",
        "PixelCarousel/01_morning_grassland.png", "PixelCarousel/02_rain_forest.png",
        "PixelCarousel/03_snow_aurora.png", "PixelCarousel/04_lava_cavern.png",
        "PixelCarousel/05_night_base.png", "PixelCarousel/06_orbital_stars.png"
    };
    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";

    #region 主菜单专属配色

    // 主菜单使用中性灰按钮、近白文字与灰白点缀，强调色跟随通用主题。
    private static readonly Color ButtonFace = new Color32(88, 88, 88, 255);
    private static readonly Color ButtonBorder = new Color32(55, 55, 55, 255);
    private static readonly Color Label = new Color32(236, 238, 239, 255);
    private static readonly Color Muted = new Color32(207, 207, 207, 255);
    private static Color Accent => FlatWorldUITheme.Accent;
    private static readonly Color TitleShadow = new Color32(12, 17, 20, 200);
    private static readonly Color Atmosphere = new Color(0.025f, 0.04f, 0.035f, 0.08f);

    #endregion

    #region 原位换肤

    /// <summary>同步菜单配色与背景轮播，保留菜单布局、文字和事件。</summary>
    [MenuItem("FlatWorld/其他工具/UI/应用主菜单参考图风格（保持布局）")]
    public static void ApplyReferenceStyle()
    {
        Sprite[] backgrounds = LoadBackgroundSlideshowSprites();

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ConfigureBackgroundSlideshow(root, backgrounds);
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
        Debug.Log("[MainMenu] 参考图风格已应用，保留十二图轮播与现有布局。");
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

    [MenuItem("FlatWorld/其他工具/UI/重建主界面美术")]
    public static void RebuildMainMenu()
    {
        Sprite[] backgrounds = LoadBackgroundSlideshowSprites();

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError("[MainMenu] 缺少字体。");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ClearChildren(root.transform);
            ConfigureRoot(root);
            BuildBackground(root.transform, backgrounds);
            BuildBrand(root.transform, font);
            BuildMenuCard(root.transform, font);
            BuildSettingsButton(root.transform, font);
            ConfigureMainMenuIcons(root);
            BuildBackgroundPreloadIndicator(root.transform);
            ConfigureResponsiveLayout(root);

            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Debug.Log("[MainMenu] 主界面美术已重建：十二图轮播、灰白文字与灰色菜单。");
    }

    #region 背景轮播装配
    private static Sprite[] LoadBackgroundSlideshowSprites()
    {
        var sprites = new Sprite[BackgroundFiles.Length];
        for (int i = 0; i < BackgroundFiles.Length; i++)
        {
            string path = BackgroundRoot + BackgroundFiles[i];
            if (BackgroundFiles[i].StartsWith("PixelCarousel/", System.StringComparison.Ordinal))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                bool changed = importer.textureType != TextureImporterType.Sprite ||
                    importer.spriteImportMode != SpriteImportMode.Single || settings.spriteMeshType != SpriteMeshType.FullRect ||
                    importer.filterMode != FilterMode.Point || importer.wrapMode != TextureWrapMode.Clamp ||
                    importer.mipmapEnabled || importer.isReadable || importer.alphaIsTransparency ||
                    importer.npotScale != TextureImporterNPOTScale.None || importer.maxTextureSize != 2048;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.alphaIsTransparency = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 2048;
                if (changed)
                    importer.SaveAndReimport();
            }
            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprites[i] == null)
                throw new System.InvalidOperationException("[MainMenu] 缺少轮播背景：" + path);
        }
        return sprites;
    }

    /// <summary>只接入背景轮播，保留正式菜单的人工布局与控件。</summary>
    [MenuItem("FlatWorld/其他工具/UI/接入主菜单十二图轮播（保持布局）")]
    public static void ApplyBackgroundSlideshowKeepingLayout()
    {
        Sprite[] sprites = LoadBackgroundSlideshowSprites();
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ConfigureBackgroundSlideshow(root, sprites);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void ConfigureBackgroundSlideshow(GameObject root, Sprite[] sprites)
    {
        Transform backgroundRoot = root.transform.Find("全屏背景");
        Image bottom = RequireComponent<Image>(root, "全屏背景/世界远景");
        Transform existingTop = backgroundRoot.Find("世界远景交叉淡化");
        Image top = existingTop != null ? existingTop.GetComponent<Image>()
            : CreateImage("世界远景交叉淡化", backgroundRoot, Color.white);
        foreach (Image layer in new[] { bottom, top })
        {
            Center(layer.rectTransform);
            layer.type = Image.Type.Simple;
            layer.raycastTarget = false;
            AspectRatioFitter fitter = layer.GetComponent<AspectRatioFitter>();
            if (fitter == null)
                fitter = layer.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        }
        bottom.transform.SetSiblingIndex(0);
        top.transform.SetSiblingIndex(1);
        var controller = backgroundRoot.GetComponent<MainMenuBackgroundSlideshowController>();
        if (controller == null)
            controller = backgroundRoot.gameObject.AddComponent<MainMenuBackgroundSlideshowController>();
        controller.ConfigureBackgroundSlideshow(root.GetComponent<BasePanel>(), sprites, bottom, top);
    }
    #endregion

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
        RuntimeUIPrefabBuilder.ConfigureScaleAnimation(panel);
    }

    private static void BuildBackground(Transform root, Sprite[] sprites)
    {
        RectTransform fullScreenBackground = CreateRect("全屏背景", root);
        Stretch(fullScreenBackground);
        fullScreenBackground.gameObject.AddComponent<FullScreenRectController>();
        CreateImage("世界远景", fullScreenBackground, Color.white);
        Image atmosphere = CreateImage("氛围压暗", fullScreenBackground, Atmosphere);
        Stretch(atmosphere.rectTransform);
        atmosphere.raycastTarget = false;
        ConfigureBackgroundSlideshow(root.gameObject, sprites);
    }

    private static void BuildBrand(Transform root, TMP_FontAsset font)
    {
        RectTransform brand = CreateRect("品牌区", root);
        brand.anchorMin = new Vector2(0f, 1f);
        brand.anchorMax = new Vector2(0f, 1f);
        brand.pivot = new Vector2(0f, 1f);
        brand.anchoredPosition = new Vector2(76f, -60f);
        brand.sizeDelta = new Vector2(720f, 190f);

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
        cardRect.sizeDelta = new Vector2(560f, 524f);
        cardRect.localScale = new Vector3(MainMenuResponsiveLayout.JourneyCardDesignScale,
            MainMenuResponsiveLayout.JourneyCardDesignScale, 1f);
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

        CreateMenuButton(card.transform, font, GameManager.MainMenuContinueButtonKey, "01", "继续旅程", 40f);
        CreateMenuButton(card.transform, font, GameManager.MainMenuNewGameButtonKey, "02", "新建世界", 154f);
        CreateMenuButton(card.transform, font, GameManager.MainMenuMultiplayerButtonKey, "03", "联机模式", 268f, false);
        CreateMenuButton(card.transform, font, GameManager.MainMenuModsButtonKey, "04", "MOD 管理", 382f);
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
            string.Empty,
            font,
            10f,
            Accent,
            FontStyles.Bold,
            TextAlignmentOptions.Left);
        eyebrow.characterSpacing = 2f;
        SetRect(eyebrow.rectTransform, new Vector2(16f, -12f), new Vector2(118f, 18f), new Vector2(0f, 1f));
        eyebrow.gameObject.SetActive(false);

        TMP_Text title = CreateText(
            "设置标题",
            buttonObject.transform,
            "设置",
            font,
            20f,
            Label,
            FontStyles.Bold,
            TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(16f, 0f), new Vector2(118f, 28f), new Vector2(0f, 0.5f));

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

    /// <summary>在菜单左下角放置不拦截交互的后台资源加载圈。</summary>
    private static void BuildBackgroundPreloadIndicator(Transform root)
    {
        RectTransform rect = CreateRect("后台资源加载指示", root);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = new Vector2(96f, 32f);
        rect.sizeDelta = new Vector2(32f, 32f);

        rect.gameObject.AddComponent<CanvasRenderer>();
        MainMenuBackgroundResourceLoadingIndicator indicator =
            rect.gameObject.AddComponent<MainMenuBackgroundResourceLoadingIndicator>();
        indicator.color = new Color32(236, 238, 239, 225);
        indicator.raycastTarget = false;
    }

    /// <summary>重建主菜单时保留标题、旅程按钮与设置入口的屏幕适配引用。</summary>
    private static void ConfigureResponsiveLayout(GameObject root)
    {
        MainMenuResponsiveLayout layout = root.GetComponent<MainMenuResponsiveLayout>();
        if (layout == null)
            layout = root.AddComponent<MainMenuResponsiveLayout>();

        layout.Configure(
            RequireComponent<RectTransform>(root, "品牌区"),
            RequireComponent<RectTransform>(root, "旅程菜单卡"),
            RequireComponent<RectTransform>(root, GameManager.MainMenuSettingsButtonKey));
    }

    #region 主菜单图标

    /// <summary>在不重建菜单的前提下安装图标，保留现有按钮事件与背景布局。</summary>
    [MenuItem("FlatWorld/其他工具/UI/安装主菜单五个图标（保持布局）")]
    public static void ApplyMainMenuIconsKeepingLayout()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ConfigureMainMenuIcons(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        Debug.Log("[MainMenu] 五个菜单图标已更新，原有布局与交互保留。");
    }

    private static void ConfigureMainMenuIcons(GameObject root)
    {
        ConfigureMenuButtonIcon(root, "旅程菜单卡/" + GameManager.MainMenuContinueButtonKey, "continue", false);
        ConfigureMenuButtonIcon(root, "旅程菜单卡/" + GameManager.MainMenuNewGameButtonKey, "new-world", false);
        ConfigureMenuButtonIcon(root, "旅程菜单卡/" + GameManager.MainMenuMultiplayerButtonKey, "multiplayer", false);
        ConfigureMenuButtonIcon(root, "旅程菜单卡/" + GameManager.MainMenuModsButtonKey, "mods", false);
        ConfigureMenuButtonIcon(root, GameManager.MainMenuSettingsButtonKey, "settings", true);
    }

    /// <summary>用离线 PNG Sprite 替换矢量组件，保留菜单图标节点和交互布局。</summary>
    private static void ConfigureMenuButtonIcon(GameObject root, string buttonPath, string iconName, bool isSettings)
    {
        Button button = RequireComponent<Button>(root, buttonPath);
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(MenuIconRoot + iconName + ".png");
        if (sprite == null) throw new MissingReferenceException("缺少菜单 PNG 图标：" + iconName);

        Transform existing = button.transform.Find("菜单图标");
        Image icon = existing != null ? existing.GetComponent<Image>() : null;
        if (icon == null)
        {
            if (existing == null)
                existing = CreateRect("菜单图标", button.transform);

            UnityEngine.UI.Graphic obsoleteImage = existing.GetComponent<UnityEngine.UI.Graphic>();
            if (obsoleteImage != null)
                Object.DestroyImmediate(obsoleteImage, true);
            icon = existing.gameObject.AddComponent<Image>();
        }
        icon.sprite = sprite;
        icon.material = null;
        icon.color = Label;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        SetRect(icon.rectTransform, new Vector2(isSettings ? 16f : 27f, 0f),
            new Vector2(isSettings ? 28f : 32f, isSettings ? 28f : 32f),
            new Vector2(0f, 0.5f));

        if (!isSettings)
        {
            Transform index = button.transform.Find(button.name + "_序号");
            if (index != null)
                index.gameObject.SetActive(false);
            return;
        }

        // 设置按钮原有文字区域较窄，图标和箭头分别固定在两端。
        TMP_Text title = RequireComponent<TMP_Text>(root, buttonPath + "/设置标题");
        SetRect(title.rectTransform, new Vector2(53f, 0f), new Vector2(82f, 28f), new Vector2(0f, 0.5f));
        TMP_Text arrow = RequireComponent<TMP_Text>(root, buttonPath + "/设置箭头");
        SetRect(arrow.rectTransform, new Vector2(-15f, 0f), new Vector2(20f, 30f), new Vector2(1f, 0.5f));
    }

    #endregion

    private static void CreateMenuButton(
        Transform parent,
        TMP_FontAsset font,
        string objectName,
        string index,
        string title,
        float top,
        bool showArrow = true)
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
        SetRect(titleText.rectTransform, new Vector2(88f, 0f), new Vector2(280f, 38f), new Vector2(0f, 0.5f));

        if (showArrow)
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
        FlatWorldUITextLayout.Configure(text, size);
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
