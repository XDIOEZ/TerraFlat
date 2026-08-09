// AI-Context: 编辑器存档选择 Prefab 重建器；根节点直接组合 BasePanel，保持列表项与控件命名契约。

using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class GameSavePrefabBuilder
{
    private const string PanelPrefabPath = "Assets/2_Prefabs/2-1_UI/Menu_UI/UI_GameSaveManager.prefab";
    private const string ItemPrefabPath = "Assets/2_Prefabs/2-1_UI/存档选择按钮.prefab";
    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";

    private static readonly Color Ink = new Color(0.025f, 0.043f, 0.058f, 0.985f);
    private static readonly Color InkSoft = new Color(0.045f, 0.075f, 0.095f, 0.98f);
    private static readonly Color Surface = new Color(0.06f, 0.095f, 0.115f, 0.98f);
    private static readonly Color Cream = new Color(0.95f, 0.91f, 0.81f, 1f);
    private static readonly Color Muted = new Color(0.64f, 0.70f, 0.71f, 1f);
    private static readonly Color Amber = new Color(0.83f, 0.49f, 0.23f, 1f);
    private static readonly Color Teal = new Color(0.26f, 0.61f, 0.57f, 1f);

    [MenuItem("FlatWorld/UI/Rebuild Save UI")]
    public static void RebuildSaveInterface()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError("[GameSaveUI] 未找到项目像素字体。");
            return;
        }

        RebuildItemPrefab(font);
        AssetDatabase.SaveAssets();

        GameObject itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ItemPrefabPath);
        GameObject root = PrefabUtility.LoadPrefabContents(PanelPrefabPath);
        try
        {
            SaveDataManager_UI controller = root.GetComponent<SaveDataManager_UI>();
            if (controller == null)
            {
                Debug.LogError("[GameSaveUI] 存档 Prefab 缺少 SaveDataManager_UI 控制器。");
                return;
            }

            ClearChildren(root.transform);
            ConfigureRoot(root);
            BuildScrim(root.transform);
            Image card = BuildCard(root.transform);
            BuildHeader(card.transform, font);

            Transform saveContent = BuildSaveList(card.transform, font);
            Transform playerContent = BuildWorkspace(card.transform, font);
            BuildFooter(card.transform, font);

            controller.Save_Player_SelectButton_Prefab = itemPrefab;
            controller.SaveSelectButton_Parent_Content = saveContent;
            controller.Player_SelectButton_Parent_Content = playerContent;

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GameSaveUI] 存档选择界面与动态条目已重建。");
    }

    private static void ConfigureRoot(GameObject root)
    {
        root.name = GameManager.GameSavePanelKey;
        SetUILayerRecursively(root);

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
        panel.PanelName = GameManager.GameSavePanelKey;
    }

    private static void BuildScrim(Transform root)
    {
        Image scrim = CreateImage("存档界面遮罩", root, new Color(0.006f, 0.016f, 0.024f, 0.76f));
        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;
    }

    private static Image BuildCard(Transform root)
    {
        Image shadow = CreateImage("存档主卡投影", root, new Color(0f, 0f, 0f, 0.42f));
        SetRect(shadow.rectTransform, new Vector2(14f, -16f), new Vector2(1400f, 800f), new Vector2(0.5f, 0.5f));
        shadow.raycastTarget = false;

        Image card = CreateImage("存档主卡", root, Ink);
        SetRect(card.rectTransform, Vector2.zero, new Vector2(1400f, 800f), new Vector2(0.5f, 0.5f));

        Outline outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.34f);
        outline.effectDistance = new Vector2(1f, -1f);

        Image accent = CreateImage("存档主卡强调线", card.transform, Amber);
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
        TMP_Text eyebrow = CreateText("存档眉题", card, "ARCHIVE  /  世界存档", font, 16f, Amber, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(eyebrow.rectTransform, new Vector2(42f, -28f), new Vector2(560f, 26f), new Vector2(0f, 1f));
        eyebrow.characterSpacing = 3f;

        TMP_Text title = CreateText("存档标题", card, "选择存档", font, 42f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(42f, -60f), new Vector2(520f, 58f), new Vector2(0f, 1f));

        TMP_Text description = CreateText("存档说明", card, "从上一次篝火继续旅程，并选择本次操控的角色。", font, 18f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(description.rectTransform, new Vector2(42f, -116f), new Vector2(780f, 30f), new Vector2(0f, 1f));

        CreateButton(card, font, GameManager.GameSaveBackButtonKey, "返回主界面", new Vector2(-42f, -42f), new Vector2(170f, 52f), InkSoft, Cream, 17f, new Vector2(1f, 1f));

        Image divider = CreateImage("存档标题分隔线", card, new Color(0.55f, 0.64f, 0.65f, 0.18f));
        divider.rectTransform.anchorMin = new Vector2(0f, 1f);
        divider.rectTransform.anchorMax = new Vector2(1f, 1f);
        divider.rectTransform.pivot = new Vector2(0.5f, 1f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, -162f);
        divider.rectTransform.sizeDelta = new Vector2(-84f, 1f);
        divider.raycastTarget = false;
    }

    private static Transform BuildSaveList(Transform card, TMP_FontAsset font)
    {
        Image panel = CreatePanelCard("世界存档区", card);
        SetRect(panel.rectTransform, new Vector2(42f, -190f), new Vector2(410f, 480f), new Vector2(0f, 1f));

        CreateStepBadge(panel.transform, font, "STEP 01", new Vector2(22f, -22f));
        TMP_Text heading = CreateText("世界存档标题", panel.transform, "世界存档", font, 23f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(heading.rectTransform, new Vector2(22f, -58f), new Vector2(250f, 34f), new Vector2(0f, 1f));

        TMP_Text hint = CreateText("世界存档提示", panel.transform, "选择一个世界；右键条目可管理存档", font, 14f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(hint.rectTransform, new Vector2(22f, -92f), new Vector2(360f, 26f), new Vector2(0f, 1f));

        return CreateScrollList("存档列表", panel.transform, new Vector2(20f, -132f), new Vector2(370f, 326f));
    }

    private static Transform BuildWorkspace(Transform card, TMP_FontAsset font)
    {
        Image panel = CreatePanelCard("存档操作区", card);
        SetRect(panel.rectTransform, new Vector2(474f, -190f), new Vector2(884f, 480f), new Vector2(0f, 1f));

        CreateStepBadge(panel.transform, font, "STEP 02", new Vector2(24f, -22f));
        TMP_Text currentLabel = CreateText("当前存档标签", panel.transform, "当前选择", font, 14f, Muted, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(currentLabel.rectTransform, new Vector2(24f, -58f), new Vector2(230f, 24f), new Vector2(0f, 1f));

        TMP_Text currentSave = CreateText(GameManager.GameSaveSelectedTextKey, panel.transform, GameManager.GameSaveNoSelectionText, font, 30f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(currentSave.rectTransform, new Vector2(24f, -82f), new Vector2(560f, 46f), new Vector2(0f, 1f));

        CreateButton(panel.transform, font, GameManager.GameSaveLoadButtonKey, "载入存档", new Vector2(-24f, -36f), new Vector2(190f, 62f), new Color(0.08f, 0.29f, 0.29f, 1f), Cream, 19f, new Vector2(1f, 1f));
        Button deleteButton = CreateButton(panel.transform, font, GameManager.GameSaveDeleteButtonKey, "删除存档", new Vector2(-24f, -104f), new Vector2(190f, 30f), new Color(0.38f, 0.11f, 0.10f, 1f), Cream, 15f, new Vector2(1f, 1f));
        deleteButton.interactable = false;

        Image divider = CreateImage("当前存档分隔线", panel.transform, new Color(0.55f, 0.64f, 0.65f, 0.17f));
        divider.rectTransform.anchorMin = new Vector2(0f, 1f);
        divider.rectTransform.anchorMax = new Vector2(1f, 1f);
        divider.rectTransform.pivot = new Vector2(0.5f, 1f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, -142f);
        divider.rectTransform.sizeDelta = new Vector2(-48f, 1f);
        divider.raycastTarget = false;

        TMP_Text playerHeading = CreateText("角色列表标题", panel.transform, "可用角色", font, 20f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(playerHeading.rectTransform, new Vector2(24f, -166f), new Vector2(330f, 32f), new Vector2(0f, 1f));
        TMP_Text playerHint = CreateText("角色列表提示", panel.transform, "载入存档后选择角色", font, 13f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(playerHint.rectTransform, new Vector2(24f, -197f), new Vector2(330f, 22f), new Vector2(0f, 1f));
        Transform playerContent = CreateScrollList("存档中的玩家列表", panel.transform, new Vector2(24f, -226f), new Vector2(368f, 224f));

        Image identity = CreateImage("角色身份区", panel.transform, new Color(0.035f, 0.06f, 0.075f, 0.98f));
        SetRect(identity.rectTransform, new Vector2(-24f, -166f), new Vector2(444f, 284f), new Vector2(1f, 1f));

        CreateStepBadge(identity.transform, font, "STEP 03", new Vector2(20f, -18f));
        TMP_Text identityHeading = CreateText("角色身份标题", identity.transform, "本次角色", font, 20f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(identityHeading.rectTransform, new Vector2(20f, -54f), new Vector2(300f, 30f), new Vector2(0f, 1f));
        TMP_Text nameLabel = CreateText("玩家名称标签", identity.transform, "玩家名称", font, 14f, Muted, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(nameLabel.rectTransform, new Vector2(20f, -96f), new Vector2(360f, 24f), new Vector2(0f, 1f));
        CreateInput(identity.transform, font, GameManager.GameSavePlayerInputKey, "选择角色或输入新名称", new Vector2(20f, -124f), new Vector2(404f, 62f));

        TMP_Text identityHint = CreateText("角色身份提示", identity.transform, "角色名称决定进入世界后操控的身份。\n首次进入也可以直接创建新角色。", font, 14f, Muted, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
        SetRect(identityHint.rectTransform, new Vector2(20f, -202f), new Vector2(404f, 58f), new Vector2(0f, 1f));
        identityHint.lineSpacing = 5f;
        return playerContent;
    }

    private static void BuildFooter(Transform card, TMP_FontAsset font)
    {
        Image divider = CreateImage("存档操作分隔线", card, new Color(0.55f, 0.64f, 0.65f, 0.18f));
        divider.rectTransform.anchorMin = new Vector2(0f, 0f);
        divider.rectTransform.anchorMax = new Vector2(1f, 0f);
        divider.rectTransform.pivot = new Vector2(0.5f, 0f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, 104f);
        divider.rectTransform.sizeDelta = new Vector2(-84f, 1f);
        divider.raycastTarget = false;

        TMP_Text flow = CreateText("存档流程提示", card, "选择世界  >  载入存档  >  选择角色  >  进入世界", font, 15f, Muted, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(flow.rectTransform, new Vector2(42f, 38f), new Vector2(760f, 34f), new Vector2(0f, 0f));
        flow.characterSpacing = 1f;

        CreateButton(card, font, GameManager.GameSaveStartButtonKey, "进入世界", new Vector2(-42f, 26f), new Vector2(250f, 66f), new Color(0.70f, 0.36f, 0.16f, 1f), Cream, 21f, new Vector2(1f, 0f));
    }

    private static void RebuildItemPrefab(TMP_FontAsset font)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ItemPrefabPath);
        try
        {
            ClearChildren(root.transform);
            Component[] components = root.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (!(component is Transform) && !(component is CanvasRenderer))
                    Object.DestroyImmediate(component, true);
            }

            root.name = "存档选择按钮";
            root.layer = LayerMask.NameToLayer("UI");
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 72f);

            Image background = root.AddComponent<Image>();
            background.color = InkSoft;
            Button button = root.AddComponent<Button>();
            button.targetGraphic = background;
            // 条目的业务选中态由 GameSaveItemView 统一控制，避免 ColorTint 覆盖确认后的高亮。
            button.transition = Selectable.Transition.None;
            // 动态条目生成后即可参与面板的自动导航，不依赖下一次运行时修复。
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.16f, 1.12f, 1.04f, 1f);
            colors.pressedColor = new Color(0.72f, 0.76f, 0.78f, 1f);
            colors.selectedColor = FlatWorldUITheme.Selection;
            colors.fadeDuration = 0.1f;
            button.colors = colors;

            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredHeight = 72f;
            layout.flexibleWidth = 1f;

            Image accent = CreateImage("选择强调线", root.transform, Teal);
            accent.rectTransform.anchorMin = new Vector2(0f, 0f);
            accent.rectTransform.anchorMax = new Vector2(0f, 1f);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.rectTransform.anchoredPosition = Vector2.zero;
            accent.rectTransform.sizeDelta = new Vector2(5f, 0f);
            accent.enabled = false;
            accent.raycastTarget = false;

            TMP_Text label = CreateText("条目名称", root.transform, "存档条目", font, 18f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
            SetRect(label.rectTransform, new Vector2(20f, 7f), new Vector2(270f, 30f), new Vector2(0f, 0.5f));
            TMP_Text subtitle = CreateText("条目提示", root.transform, "选择  /  右键管理", font, 12f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
            SetRect(subtitle.rectTransform, new Vector2(20f, -20f), new Vector2(250f, 22f), new Vector2(0f, 0.5f));
            TMP_Text arrow = CreateText("条目箭头", root.transform, ">", font, 18f, Amber, FontStyles.Bold, TextAlignmentOptions.Center);
            SetRect(arrow.rectTransform, new Vector2(-18f, 0f), new Vector2(28f, 40f), new Vector2(1f, 0.5f));

            ButtonInfoData info = root.AddComponent<ButtonInfoData>();
            info.SelectImage = accent;
            GameSaveItemView itemView = root.AddComponent<GameSaveItemView>();
            itemView.Background = background;
            itemView.SelectionAccent = accent;
            itemView.Label = (TextMeshProUGUI)label;

            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, ItemPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Transform CreateScrollList(string name, Transform parent, Vector2 position, Vector2 size)
    {
        GameObject scrollObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
        scrollObject.layer = LayerMask.NameToLayer("UI");
        scrollObject.transform.SetParent(parent, false);
        RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
        SetRect(scrollRectTransform, position, size, new Vector2(0f, 1f));
        scrollObject.GetComponent<Image>().color = new Color(0.025f, 0.048f, 0.062f, 0.98f);

        GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportObject.layer = LayerMask.NameToLayer("UI");
        viewportObject.transform.SetParent(scrollObject.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        Stretch(viewport, 8f, 8f, 8f, 8f);

        GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.layer = LayerMask.NameToLayer("UI");
        contentObject.transform.SetParent(viewportObject.transform, false);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.12f;
        scroll.scrollSensitivity = 28f;
        return content;
    }

    private static TMP_InputField CreateInput(Transform parent, TMP_FontAsset font, string name, string placeholderValue, Vector2 position, Vector2 size)
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
        TextMeshProUGUI valueText = (TextMeshProUGUI)CreateText("Text", area, string.Empty, font, 18f, Cream, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        Stretch(valueText.rectTransform);

        TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = area;
        input.placeholder = placeholder;
        input.textComponent = valueText;
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

    private static Button CreateButton(Transform parent, TMP_FontAsset font, string name, string caption, Vector2 position, Vector2 size, Color color, Color textColor, float fontSize, Vector2 pivot)
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
        colors.selectedColor = FlatWorldUITheme.Selection;
        colors.disabledColor = new Color(0.42f, 0.43f, 0.44f, 0.56f);
        colors.fadeDuration = 0.12f;
        button.colors = colors;

        TMP_Text label = CreateText(name + "_文字", buttonObject.transform, caption, font, fontSize, textColor, FontStyles.Bold, TextAlignmentOptions.Center);
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

    private static TMP_Text CreateText(string name, Transform parent, string value, TMP_FontAsset font, float size, Color color, FontStyles style, TextAlignmentOptions alignment, bool wrapping = false)
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
        text.enableWordWrapping = wrapping;
        text.overflowMode = wrapping ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
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

    private static void SetUILayerRecursively(GameObject target)
    {
        target.layer = LayerMask.NameToLayer("UI");
        foreach (Transform child in target.transform)
            SetUILayerRecursively(child.gameObject);
    }
}
