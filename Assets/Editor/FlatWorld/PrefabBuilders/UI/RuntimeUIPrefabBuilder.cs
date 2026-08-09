// AI-Context: 正式运行时 UI 的 Prefab 固化入口；运行时代码只能实例化这些资产，不再创建视觉节点。

using System.IO;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

public static class RuntimeUIPrefabBuilder
{
    #region 路径与视觉常量

    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";
    private const string MenuRoot = "Assets/2_Prefabs/2-1_UI/Menu_UI/";
    private const string RuntimeRoot = "Assets/2_Prefabs/2-1_UI/Runtime/";
    private const string DialogueRoot = RuntimeRoot + "Dialogue/";
    private const string SettingsRoot = RuntimeRoot + "Settings/";
    private const string SystemRoot = RuntimeRoot + "System/";
    private const string InventoryRoot = "Assets/2_Prefabs/2-1_UI/InventoryUI/";
    private const string NetworkPlayerPrefab = "Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab";

    private static readonly Color Canvas = new Color(0.025f, 0.043f, 0.058f, 0.99f);
    private static readonly Color Surface = new Color(0.045f, 0.075f, 0.095f, 0.99f);
    private static readonly Color SurfaceRaised = new Color(0.094f, 0.212f, 0.247f, 0.99f);
    private static readonly Color Cream = new Color(0.95f, 0.91f, 0.81f, 1f);
    private static readonly Color Muted = new Color(0.66f, 0.72f, 0.73f, 1f);
    private static readonly Color Amber = new Color(0.83f, 0.49f, 0.23f, 1f);
    private static readonly Color Teal = new Color(0.26f, 0.61f, 0.57f, 1f);
    private static readonly Color Danger = new Color(0.66f, 0.31f, 0.27f, 1f);
    private static readonly Color Border = new Color(0.55f, 0.68f, 0.70f, 0.28f);

    private static TMP_FontAsset font;

    #endregion

    #region 重建入口

    [MenuItem("FlatWorld/UI/Rebuild Runtime Prefab UI")]
    public static void RebuildRuntimePrefabUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(SettingsRoot);
        Directory.CreateDirectory(DialogueRoot);
        Directory.CreateDirectory(SystemRoot);

        SaveNewPrefab(SettingsRoot + RuntimeUIPrefabKeys.AudioSettings + ".prefab", BuildAudioSettings);
        SaveNewPrefab(SettingsRoot + RuntimeUIPrefabKeys.UISettings + ".prefab", BuildInterfaceSettings);
        SaveCoordinateDisplaySettingsPrefab();
        SaveNewPrefab(SettingsRoot + RuntimeUIPrefabKeys.MainMenuSettings + ".prefab", BuildMainMenuSettings);
        SaveNewPrefab(SettingsRoot + RuntimeUIPrefabKeys.AutoSaveSettings + ".prefab", BuildAutoSaveSettings);
        SaveNewPrefab(SettingsRoot + RuntimeUIPrefabKeys.WorldStreamingSettings + ".prefab", BuildWorldStreamingSettings);
        SaveNewPrefab(SettingsRoot + RuntimeUIPrefabKeys.DifficultySettings + ".prefab", BuildDifficultySettings);
        SaveNewPrefab(SettingsRoot + RuntimeUIPrefabKeys.InputBindingSettings + ".prefab", BuildInputBindingSettings);
        SaveNewPrefab(SettingsRoot + RuntimeUIPrefabKeys.InputBindingRow + ".prefab", BuildInputBindingRow);
        SaveNewPrefab(DialogueRoot + RuntimeUIPrefabKeys.PlayerChatInput + ".prefab", BuildPlayerChatInput);
        SaveNewPrefab(DialogueRoot + RuntimeUIPrefabKeys.CharacterSpeechBubble + ".prefab", BuildSpeechBubble);
        SaveNewPrefab(SystemRoot + RuntimeUIPrefabKeys.WorldLoading + ".prefab", BuildWorldLoading);
        SavePlayerWorldCoordinatePrefab();

        UpdateExistingPrefab(MenuRoot + "Info_Button_List.prefab", ConfigureSettingsActionListPages);
        UpdateExistingPrefab(InventoryRoot + "UI_Bag.prefab", AddInventorySortButton);
        UpdateExistingPrefab(InventoryRoot + "UI_Slot.prefab", AddCraftingPreviewLayers);
        UpdateExistingWorldPrefab(NetworkPlayerPrefab, AddNetworkPlayerNameLabel);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化设置、设置列表分页、显示设置、世界加载、聊天、气泡、玩家坐标、背包整理、制作预览与联机玩家名称 Prefab。运行时不再创建这些视觉节点。");
    }

    /// <summary>只重建区块流送设置和入口，避免小改动重写全部运行时 Prefab。</summary>
    [MenuItem("FlatWorld/UI/Rebuild World Streaming Settings UI")]
    public static void RebuildWorldStreamingSettingsUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(SettingsRoot);
        SaveNewPrefab(SettingsRoot + RuntimeUIPrefabKeys.WorldStreamingSettings + ".prefab",
            BuildWorldStreamingSettings);
        UpdateExistingPrefab(MenuRoot + "Info_Button_List.prefab",
            ConfigureSettingsActionListPages);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化区块流送性能设置 Prefab 与入口按钮。");
    }

    /// <summary>只重建主菜单设置窗口，便于单独调整显示、画质和语言的占位布局。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Main Menu Settings UI")]
    public static void RebuildMainMenuSettingsUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(SettingsRoot);
        SaveNewPrefab(
            SettingsRoot + RuntimeUIPrefabKeys.MainMenuSettings + ".prefab",
            BuildMainMenuSettings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化主菜单设置窗口 Prefab（大小、画质、语言占位项）。");
    }

    /// <summary>只重建左上角常驻的玩家世界坐标 HUD，并确保其进入运行时 Prefab 索引。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Player World Coordinate HUD")]
    public static void RebuildPlayerWorldCoordinateHUD()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(SystemRoot);
        SavePlayerWorldCoordinatePrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化玩家世界坐标 HUD Prefab。");
    }

    /// <summary>只重建坐标显示设置和设置列表分页，避免无关运行时 Prefab 被重写。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Coordinate Display Settings UI")]
    public static void RebuildCoordinateDisplaySettingsUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(SettingsRoot);
        SaveCoordinateDisplaySettingsPrefab();
        UpdateExistingPrefab(MenuRoot + "Info_Button_List.prefab", ConfigureSettingsActionListPages);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化坐标显示设置 Prefab 与设置列表三分页。");
    }

    private static void SaveNewPrefab(string path, System.Func<GameObject> factory)
    {
        GameObject root = factory();
        try
        {
            SetUILayerRecursively(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>保存坐标 HUD 并注册 Prefab 标签，确保 GameRes 能按键名加载。</summary>
    private static void SavePlayerWorldCoordinatePrefab()
    {
        string prefabPath = SystemRoot + RuntimeUIPrefabKeys.PlayerWorldCoordinate + ".prefab";
        SaveNewPrefab(prefabPath, BuildPlayerWorldCoordinateHUD);
        EnsureRuntimePrefabAddressable(prefabPath);
    }

    /// <summary>保存坐标显示设置并登记为可由 GameRes 查询的正式运行时 Prefab。</summary>
    private static void SaveCoordinateDisplaySettingsPrefab()
    {
        string prefabPath = SettingsRoot + RuntimeUIPrefabKeys.CoordinateDisplaySettings + ".prefab";
        SaveNewPrefab(prefabPath, BuildCoordinateDisplaySettings);
        EnsureRuntimePrefabAddressable(prefabPath);
    }

    /// <summary>为新增运行时 UI 建立可重复执行的 Addressables Prefab 条目。</summary>
    private static void EnsureRuntimePrefabAddressable(string prefabPath)
    {
        string guid = AssetDatabase.AssetPathToGUID(prefabPath);
        if (string.IsNullOrWhiteSpace(guid))
            throw new InvalidDataException($"无法注册运行时 UI Prefab：{prefabPath}");

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new System.InvalidOperationException("AddressableAssetSettings 未初始化。");

        AddressableAssetEntry entry = settings.FindAssetEntry(guid) ??
                                      settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
        entry.address = prefabPath;
        entry.SetLabel("Prefab", true, true);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
    }

    private static void UpdateExistingPrefab(string path, System.Action<GameObject> update)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            update(root);
            SetUILayerRecursively(root);
            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void UpdateExistingWorldPrefab(string path, System.Action<GameObject> update)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            update(root);
            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    #endregion

    #region 系统 UI

    /// <summary>构建固定在屏幕左上角的非交互坐标卡片，尺寸为 296×72 设计像素。</summary>
    private static GameObject BuildPlayerWorldCoordinateHUD()
    {
        GameObject root = CreateUIObject(RuntimeUIPrefabKeys.PlayerWorldCoordinate, null);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        SetTopLeft(rootRect, 32f, 32f, 296f, 72f);

        Image background = CreateImage("背景", root.transform, new Color(0.025f, 0.043f, 0.058f, 0.92f));
        background.raycastTarget = false;
        Stretch(background.rectTransform);
        AddOutline(background, new Color(0.83f, 0.49f, 0.23f, 0.42f));

        Shadow shadow = background.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.48f);
        shadow.effectDistance = new Vector2(3f, -3f);

        Image accent = CreateImage("强调线", root.transform, Amber);
        accent.raycastTarget = false;
        RectTransform accentRect = accent.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(4f, -18f);

        TextMeshProUGUI title = CreateText("坐标标题", root.transform, "世界坐标 / POSITION", 10f, Amber);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 1.15f;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(title.rectTransform, 18f, 11f, 258f, 16f);

        TextMeshProUGUI coordinates = CreateText("坐标文本", root.transform, "X  +0.0    Y  +0.0", 16f, Cream);
        coordinates.fontStyle = FontStyles.Bold;
        coordinates.enableWordWrapping = false;
        coordinates.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(coordinates.rectTransform, 18f, 34f, 258f, 26f);

        return root;
    }

    private static GameObject BuildWorldLoading()
    {
        GameObject root = new GameObject(
            RuntimeUIPrefabKeys.WorldLoading,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup),
            typeof(CanvasRenderer),
            typeof(Image));
        Stretch(root.GetComponent<RectTransform>());

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        Image overlay = root.GetComponent<Image>();
        overlay.color = new Color(0.012f, 0.022f, 0.028f, 0.96f);
        overlay.raycastTarget = true;

        GameObject card = CreateUIObject("加载内容", root.transform, typeof(Image), typeof(Shadow));
        SetCentered(card.GetComponent<RectTransform>(), Vector2.zero, new Vector2(660f, 270f));
        Image cardImage = card.GetComponent<Image>();
        cardImage.color = Canvas;
        cardImage.raycastTarget = true;
        AddOutline(cardImage, Amber);
        Shadow shadow = card.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
        shadow.effectDistance = new Vector2(6f, -6f);

        VerticalLayoutGroup layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(34, 34, 28, 26);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText("加载标题", card.transform, "正在进入世界", 28f, Cream);
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;

        TextMeshProUGUI status = CreateText("加载状态", card.transform, "正在准备世界数据…", 17f, Muted);
        status.alignment = TextAlignmentOptions.Center;
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        Slider progress = CreateSlider("加载进度", card.transform);
        progress.interactable = false;
        progress.value = 0.08f;
        LayoutElement progressLayout = progress.GetComponent<LayoutElement>();
        progressLayout.flexibleWidth = 0f;
        progressLayout.preferredWidth = 560f;
        progressLayout.preferredHeight = 28f;

        TextMeshProUGUI percent = CreateText("加载进度文本", card.transform, "8%", 14f, Amber);
        percent.alignment = TextAlignmentOptions.Center;
        percent.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

        TextMeshProUGUI hint = CreateText("加载提示", card.transform, "请稍候，世界准备完成后将自动进入。", 13f, Muted);
        hint.alignment = TextAlignmentOptions.Center;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
        return root;
    }

    #endregion

    #region 设置面板

    private static GameObject BuildAudioSettings()
    {
        GameObject root = CreatePanelRoot(RuntimeUIPrefabKeys.AudioSettings, new Vector2(620f, 515f));
        CreateHeader(root.transform, "音量调节", "关闭按钮");
        CreateHint(root.transform, "主音量控制全部声音；其他通道可以单独调整。设置会自动保存。", 30f);

        CreateSliderRow(root.transform, "主音量", "MasterVolume");
        CreateSliderRow(root.transform, "音乐音量", "MusicVolume");
        CreateSliderRow(root.transform, "音效音量", "SfxVolume");
        CreateSliderRow(root.transform, "UI 音量", "UIVolume");
        CreateSliderRow(root.transform, "环境音量", "AmbientVolume");
        CreateSliderRow(root.transform, "语音音量", "VoiceVolume");

        Transform footer = CreateFooter(root.transform);
        CreateButton("恢复默认按钮", footer, "恢复默认", 104f, 34f, false);
        CreateButton("完成按钮", footer, "完成", 78f, 34f, true);
        return root;
    }

    private static GameObject BuildInterfaceSettings()
    {
        GameObject root = CreatePanelRoot(RuntimeUIPrefabKeys.UISettings, new Vector2(620f, 390f));
        CreateHeader(root.transform, "UI 设置", "关闭按钮");
        CreateHint(root.transform, "调整会立即应用并自动保存；Prefab 中的布局就是运行时看到的基础布局。", 42f);

        GameObject scaleRow = CreateRow("界面缩放行", root.transform, 52f);
        CreateRowLabel(scaleRow.transform, "界面缩放", 112f);
        Slider slider = CreateSlider("界面缩放", scaleRow.transform);
        slider.minValue = UIUserSettings.MinimumScale;
        slider.maxValue = UIUserSettings.MaximumScale;
        slider.value = 1f;
        TextMeshProUGUI valueText = CreateText("界面缩放数值", scaleRow.transform, "100%", 13f, Amber);
        valueText.alignment = TextAlignmentOptions.MidlineRight;
        valueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

        GameObject safeAreaRow = CreateRow("安全区域适配行", root.transform, 48f);
        TextMeshProUGUI safeLabel = CreateText("安全区域说明", safeAreaRow.transform, "适配屏幕安全区域", 14f, Cream);
        safeLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateToggle("安全区域适配", safeAreaRow.transform);

        TextMeshProUGUI status = CreateText("状态文本", root.transform, "安全区域适配：开启（推荐）", 13f, Muted);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        Transform footer = CreateFooter(root.transform);
        CreateButton("恢复默认按钮", footer, "恢复默认", 104f, 34f, false);
        CreateButton("完成按钮", footer, "完成", 78f, 34f, true);
        return root;
    }

    /// <summary>构建 HUD 坐标格式选择页；两种显示立即写入本地偏好，不会修改世界或存档坐标。</summary>
    private static GameObject BuildCoordinateDisplaySettings()
    {
        GameObject root = CreatePanelRoot(
            RuntimeUIPrefabKeys.CoordinateDisplaySettings,
            new Vector2(620f, 360f));
        CreateHeader(root.transform, "显示设置", "关闭按钮");
        CreateHint(root.transform, "选择屏幕左上角坐标卡片的显示方式；切换会立即生效并自动保存。", 36f);

        CreateSettingsSection(root.transform, "坐标显示", "POSITION HUD");
        GameObject modeRow = CreateRow("坐标显示方式行", root.transform, 54f);
        CreateRowLabel(modeRow.transform, "显示方式", 100f);
        CreateButton("世界坐标模式按钮", modeRow.transform, "世界坐标  X / Y", 204f, 42f, true);
        CreateButton("经纬度模式按钮", modeRow.transform, "经纬度  经 / 纬", 204f, 42f, false);

        TextMeshProUGUI status = CreateText(
            "状态文本",
            root.transform,
            "当前显示：世界坐标（X / Y）",
            13f,
            Muted);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        Transform footer = CreateFooter(root.transform);
        CreateButton("完成按钮", footer, "完成", 78f, 34f, true);
        return root;
    }

    /// <summary>构建主菜单设置窗口；当前只提供视觉与占位选项，不接入实际设置逻辑。</summary>
    private static GameObject BuildMainMenuSettings()
    {
        GameObject root = CreateModalPanelRoot(
            RuntimeUIPrefabKeys.MainMenuSettings,
            new Vector2(720f, 600f));
        Transform dialog = root.transform.Find("设置对话框");

        CreateHeader(dialog, "游戏设置", "关闭按钮");
        CreateHint(dialog, "调整显示大小、画质与语言。选项为界面预览，功能将在后续接入。", 36f);

        CreateSettingsSection(dialog, "显示", "DISPLAY");
        CreateSettingsDropdownRow(
            dialog,
            "窗口大小",
            "窗口大小下拉列表",
            new[] { "1920 × 1080", "1600 × 900", "1280 × 720" });
        CreateSettingsDropdownRow(
            dialog,
            "显示模式",
            "显示模式下拉列表",
            new[] { "全屏窗口", "全屏", "窗口" });

        CreateSettingsSection(dialog, "画质", "GRAPHICS");
        CreateSettingsDropdownRow(
            dialog,
            "画质预设",
            "画质预设下拉列表",
            new[] { "高（推荐）", "中", "低" });
        CreateSettingsDropdownRow(
            dialog,
            "特效质量",
            "特效质量下拉列表",
            new[] { "高", "中", "低" });

        CreateSettingsSection(dialog, "语言", "LANGUAGE");
        CreateSettingsDropdownRow(
            dialog,
            "游戏语言",
            "游戏语言下拉列表",
            new[] { "简体中文", "English" });

        TextMeshProUGUI status = CreateText(
            "设置状态",
            dialog,
            "当前为界面预览；设置功能将在后续版本接入。",
            13f,
            Muted);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

        Transform footer = CreateFooter(dialog);
        CreateButton("恢复默认按钮", footer, "恢复默认", 104f, 34f, false);
        CreateButton("返回按钮", footer, "返回", 78f, 34f, true);
        return root;
    }

    private static GameObject BuildAutoSaveSettings()
    {
        GameObject root = CreatePanelRoot(RuntimeUIPrefabKeys.AutoSaveSettings, new Vector2(640f, 410f));
        CreateHeader(root.transform, "自动保存", "关闭按钮");
        CreateHint(root.transform, "自动保存只在游戏世界中按现实时间运行，设置会立即保存。", 38f);

        GameObject modeRow = CreateRow("保存模式", root.transform, 52f);
        CreateRowLabel(modeRow.transform, "保存模式", 130f);
        TMP_Dropdown dropdown = CreateDropdown("自动保存间隔下拉列表", modeRow.transform);
        dropdown.gameObject.AddComponent<LayoutElement>().preferredWidth = 402f;

        GameObject inputRow = CreateRow("自定义间隔", root.transform, 52f);
        CreateRowLabel(inputRow.transform, "间隔（分钟）", 130f);
        TMP_InputField input = CreateInputField("自动保存间隔输入框", inputRow.transform, "输入 1–1440");
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.gameObject.AddComponent<LayoutElement>().preferredWidth = 340f;
        TextMeshProUGUI range = CreateText("范围提示", inputRow.transform, "1–1440", 12f, Muted);
        range.alignment = TextAlignmentOptions.MidlineRight;
        range.gameObject.AddComponent<LayoutElement>().preferredWidth = 62f;

        TextMeshProUGUI status = CreateText("状态文本", root.transform, "当前设置：每 10 分钟自动保存。", 13f, Teal);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        Transform footer = CreateFooter(root.transform);
        CreateButton("取消按钮", footer, "取消", 82f, 36f, false);
        CreateButton("应用按钮", footer, "应用", 92f, 36f, true);
        return root;
    }

    /// <summary>构建区块流送性能模式面板，所有控件均由布局组件约束。</summary>
    private static GameObject BuildWorldStreamingSettings()
    {
        GameObject root = CreatePanelRoot(
            RuntimeUIPrefabKeys.WorldStreamingSettings, new Vector2(680f, 420f));
        CreateHeader(root.transform, "区块流送性能", "关闭按钮");
        CreateHint(root.transform,
            "自动适合多数设备；流畅优先减少 CPU 争用；高吞吐会在安全上限内使用多个后台线程。",
            58f);

        GameObject modeRow = CreateRow("性能模式行", root.transform, 54f);
        CreateRowLabel(modeRow.transform, "性能模式", 122f);
        TMP_Dropdown dropdown = CreateDropdown("性能模式下拉列表", modeRow.transform);
        dropdown.gameObject.AddComponent<LayoutElement>().preferredWidth = 430f;

        TextMeshProUGUI explanation = CreateText(
            "模式说明", root.transform,
            "纯地形数据在后台生成；Tilemap、碰撞和导航始终在主线程逐帧绘制。",
            13f, Muted);
        explanation.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;

        TextMeshProUGUI status = CreateText(
            "状态文本", root.transform, "当前：自动平衡。", 13f, Teal);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        Transform footer = CreateFooter(root.transform);
        CreateButton("取消按钮", footer, "取消", 82f, 36f, false);
        CreateButton("应用按钮", footer, "应用", 92f, 36f, true);
        return root;
    }

    private static GameObject BuildDifficultySettings()
    {
        GameObject root = CreatePanelRoot(RuntimeUIPrefabKeys.DifficultySettings, new Vector2(700f, 450f));
        CreateHeader(root.transform, "游戏难度", "关闭按钮");
        CreateHint(root.transform, "难度属于当前存档并立即生效。选择预设后点击应用。", 46f);

        for (int i = 0; i < GameDifficultyCatalog.All.Count; i++)
        {
            GameDifficultyDefinition definition = GameDifficultyCatalog.All[i];
            CreateDifficultyOption(root.transform, definition, i == 0);
        }

        TextMeshProUGUI status = CreateText("状态文本", root.transform, "当前存档难度：简单", 13f, Teal);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        Transform footer = CreateFooter(root.transform);
        CreateButton("取消按钮", footer, "取消", 82f, 36f, false);
        CreateButton("应用按钮", footer, "应用", 92f, 36f, true);
        return root;
    }

    private static GameObject BuildInputBindingSettings()
    {
        GameObject root = new GameObject(
            RuntimeUIPrefabKeys.InputBindingSettings,
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(BasePanel));
        Stretch(root.GetComponent<RectTransform>());

        Image overlay = root.GetComponent<Image>();
        overlay.color = new Color(0.015f, 0.028f, 0.034f, 0.78f);
        overlay.raycastTarget = true;
        ConfigureBasePanel(root);

        GameObject dialog = CreateUIObject("按键绑定面板", root.transform, typeof(Image));
        RectTransform dialogRect = dialog.GetComponent<RectTransform>();
        SetCentered(dialogRect, Vector2.zero, new Vector2(760f, 720f));
        Image dialogImage = dialog.GetComponent<Image>();
        dialogImage.color = Canvas;
        dialogImage.raycastTarget = true;
        AddOutline(dialogImage, Amber);

        VerticalLayoutGroup layout = dialog.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 18);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateHeader(dialog.transform, "按键绑定", "关闭按钮");
        CreateHint(dialog.transform, "分别设置键鼠与手柄；重复绑定会被拦截，修改后自动保存。", 42f);

        GameObject deviceTabs = CreateUIObject("设备分页", dialog.transform);
        deviceTabs.AddComponent<LayoutElement>().preferredHeight = 40f;
        HorizontalLayoutGroup tabLayout = deviceTabs.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 10f;
        tabLayout.childAlignment = TextAnchor.MiddleCenter;
        tabLayout.childControlWidth = true;
        tabLayout.childControlHeight = true;
        tabLayout.childForceExpandWidth = false;
        tabLayout.childForceExpandHeight = true;
        CreateButton("键鼠分页按钮", deviceTabs.transform, "键鼠", 180f, 38f, true);
        CreateButton("手柄分页按钮", deviceTabs.transform, "手柄", 180f, 38f, false);

        CreateBindingScrollView(dialog.transform);
        TextMeshProUGUI status = CreateText("状态文本", dialog.transform, "选择一项后按下新按键。", 13f, Muted);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
        Transform footer = CreateFooter(dialog.transform);
        CreateButton("恢复默认按钮", footer, "恢复默认", 112f, 36f, false);
        CreateButton("完成按钮", footer, "完成", 82f, 36f, true);
        return root;
    }

    private static GameObject BuildInputBindingRow()
    {
        GameObject root = CreateUIObject(RuntimeUIPrefabKeys.InputBindingRow, null, typeof(Image));
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(680f, 44f);
        Image image = root.GetComponent<Image>();
        image.color = new Color(0.06f, 0.102f, 0.116f, 0.98f);
        image.raycastTarget = false;
        root.AddComponent<LayoutElement>().preferredHeight = 44f;

        HorizontalLayoutGroup layout = root.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 8, 5, 5);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        TextMeshProUGUI label = CreateText("操作名称", root.transform, "向上移动", 15f, Cream);
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        TextMeshProUGUI binding = CreateText("绑定值", root.transform, "W", 14f, Amber);
        binding.alignment = TextAlignmentOptions.MidlineRight;
        binding.gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;
        CreateButton("修改按钮", root.transform, "修改", 86f, 34f, true);
        return root;
    }

    #endregion

    #region 对话 UI

    private static GameObject BuildPlayerChatInput()
    {
        GameObject root = new GameObject(
            RuntimeUIPrefabKeys.PlayerChatInput,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Outline),
            typeof(TMP_InputField));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 42f);
        rect.sizeDelta = new Vector2(780f, 54f);

        Image background = root.GetComponent<Image>();
        background.color = new Color(0.03f, 0.03f, 0.03f, 0.88f);
        AddOutline(background, new Color(1f, 1f, 1f, 0.32f));

        GameObject textArea = CreateUIObject("Text Area", root.transform, typeof(RectMask2D));
        RectTransform areaRect = textArea.GetComponent<RectTransform>();
        Stretch(areaRect);
        areaRect.offsetMin = new Vector2(16f, 7f);
        areaRect.offsetMax = new Vector2(-16f, -7f);

        TextMeshProUGUI placeholder = CreateInputText(
            "Placeholder",
            textArea.transform,
            "输入消息，按 Enter 发送（/ 开头可用于命令）",
            new Color(0.70f, 0.70f, 0.70f, 0.82f));
        TextMeshProUGUI valueText = CreateInputText("Text", textArea.transform, string.Empty, Color.white);

        TMP_InputField field = root.GetComponent<TMP_InputField>();
        field.targetGraphic = background;
        field.textViewport = areaRect;
        field.textComponent = valueText;
        field.placeholder = placeholder;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.contentType = TMP_InputField.ContentType.Standard;
        field.characterLimit = 160;
        field.richText = false;
        field.customCaretColor = true;
        field.caretColor = Color.white;
        field.selectionColor = new Color(0.35f, 0.55f, 1f, 0.55f);
        field.navigation = new Navigation { mode = Navigation.Mode.None };
        return root;
    }

    private static GameObject BuildSpeechBubble()
    {
        GameObject root = new GameObject(
            RuntimeUIPrefabKeys.CharacterSpeechBubble,
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Shadow));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(260f, 72f);

        Image background = root.GetComponent<Image>();
        background.color = new Color(0.08f, 0.09f, 0.10f, 0.94f);
        background.raycastTarget = false;
        Shadow shadow = root.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.48f);
        shadow.effectDistance = new Vector2(3f, -3f);

        CanvasGroup group = root.GetComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = false;

        Image tail = CreateImage("Tail", root.transform, background.color);
        tail.raycastTarget = false;
        RectTransform tailRect = tail.rectTransform;
        tailRect.anchorMin = new Vector2(0.5f, 0f);
        tailRect.anchorMax = new Vector2(0.5f, 0f);
        tailRect.pivot = new Vector2(0.5f, 0.5f);
        tailRect.anchoredPosition = new Vector2(0f, -6f);
        tailRect.sizeDelta = new Vector2(18f, 18f);
        tailRect.localRotation = Quaternion.Euler(0f, 0f, 45f);

        TextMeshProUGUI message = CreateText("Message", root.transform, "角色会在这里说话", 28f, new Color(0.96f, 0.94f, 0.88f, 1f));
        message.enableAutoSizing = true;
        message.fontSizeMin = 20f;
        message.fontSizeMax = 28f;
        message.enableWordWrapping = true;
        message.overflowMode = TextOverflowModes.Ellipsis;
        message.alignment = TextAlignmentOptions.Center;
        Stretch(message.rectTransform);
        message.rectTransform.offsetMin = new Vector2(18f, 12f);
        message.rectTransform.offsetMax = new Vector2(-18f, -12f);
        return root;
    }

    #endregion

    #region 现有 Prefab 固化

    /// <summary>
    /// 将原本堆在同一 ScrollRect 中的设置入口拆成三页。
    /// 每页最多四项，分页控制固定在滚动区域下方，避免入口继续向下溢出。
    /// </summary>
    private static void ConfigureSettingsActionListPages(GameObject root)
    {
        Transform content = FindTransform(root.transform, "Content");
        RectTransform scrollRect = FindTransform(root.transform, "Scroll View") as RectTransform;
        if (content == null || scrollRect == null)
            throw new MissingReferenceException("Info_Button_List.prefab 缺少 Content 或 Scroll View。");

        SetTopLeft(scrollRect, 42f, 148f, 346f, 314f);
        ConfigureActionListScroll(scrollRect, content);

        Transform interfacePage = EnsureActionListPage(
            content,
            SettingsActionListPagination.InterfacePageName);
        Transform worldPage = EnsureActionListPage(
            content,
            SettingsActionListPagination.WorldPageName);
        Transform sessionPage = EnsureActionListPage(
            content,
            SettingsActionListPagination.SessionPageName);

        MoveEntryToPage(root.transform, interfacePage, "音量调节");
        MoveEntryToPage(root.transform, interfacePage, "UI设置");
        MoveEntryToPage(root.transform, interfacePage, "显示设置");
        MoveEntryToPage(root.transform, interfacePage, "按键绑定");

        MoveEntryToPage(root.transform, worldPage, "自动保存");
        MoveEntryToPage(root.transform, worldPage, "流送性能");
        MoveEntryToPage(root.transform, worldPage, "游戏难度");

        MoveEntryToPage(root.transform, sessionPage, "保存游戏");
        MoveEntryToPage(root.transform, sessionPage, "保存并回到主界面按钮");
        MoveEntryToPage(root.transform, sessionPage, "保存并退出游戏按钮");

        interfacePage.SetSiblingIndex(0);
        worldPage.SetSiblingIndex(1);
        sessionPage.SetSiblingIndex(2);
        interfacePage.gameObject.SetActive(true);
        worldPage.gameObject.SetActive(false);
        sessionPage.gameObject.SetActive(false);
        EnsureActionListPagerControls(root.transform);
    }

    /// <summary>设置分页列表的视口与 Content，彻底移除旧的纵向滚动布局所有权。</summary>
    private static void ConfigureActionListScroll(RectTransform scrollRect, Transform content)
    {
        ScrollRect scroll = scrollRect.GetComponent<ScrollRect>();
        if (scroll != null)
        {
            scroll.horizontal = false;
            scroll.vertical = false;
            scroll.inertia = false;
            scroll.verticalScrollbar = null;
        }

        Transform scrollbar = FindTransform(scrollRect, "Scrollbar Vertical");
        if (scrollbar != null)
            scrollbar.gameObject.SetActive(false);

        GridLayoutGroup grid = content.GetComponent<GridLayoutGroup>();
        if (grid != null)
            Object.DestroyImmediate(grid);
        VerticalLayoutGroup verticalLayout = content.GetComponent<VerticalLayoutGroup>();
        if (verticalLayout != null)
            Object.DestroyImmediate(verticalLayout);
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            Object.DestroyImmediate(fitter);

        RectTransform contentRect = content as RectTransform;
        if (contentRect == null)
            throw new MissingComponentException("Info_Button_List.prefab 的 Content 缺少 RectTransform。");
        Stretch(contentRect);
    }

    /// <summary>创建或更新可铺满视口的单个分页容器。</summary>
    private static Transform EnsureActionListPage(Transform content, string pageName)
    {
        Transform page = FindTransform(content, pageName);
        if (page == null)
            page = CreateUIObject(pageName, content).transform;

        RectTransform pageRect = page as RectTransform;
        Stretch(pageRect);

        GridLayoutGroup grid = page.GetComponent<GridLayoutGroup>();
        if (grid != null)
            Object.DestroyImmediate(grid);
        ContentSizeFitter fitter = page.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            Object.DestroyImmediate(fitter);

        VerticalLayoutGroup layout = page.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
            layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return page;
    }

    /// <summary>复用既有业务按钮或创建新入口，并将其移动到指定页面。</summary>
    private static void MoveEntryToPage(Transform root, Transform page, string entryName)
    {
        Transform entry = FindTransform(root, entryName);
        Button button = entry != null ? entry.GetComponent<Button>() : null;
        if (button == null)
            button = CreateButton(entryName, page, entryName, 264f, 52f, false);

        button.gameObject.name = entryName;
        button.transform.SetParent(page, false);
        button.gameObject.SetActive(true);

        LayoutElement element = button.GetComponent<LayoutElement>() ??
                                button.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = 264f;
        element.preferredHeight = 52f;
        button.GetComponent<RectTransform>().sizeDelta = new Vector2(264f, 52f);
        ConfigureButtonVisual(button, false, entryName);
    }

    /// <summary>创建固定在列表底部的翻页按钮与页码文本。</summary>
    private static void EnsureActionListPagerControls(Transform root)
    {
        Button previous = EnsureActionListPagerButton(
            root,
            SettingsActionListPagination.PreviousButtonName,
            "上一页");
        SetTopLeft(previous.GetComponent<RectTransform>(), 42f, 482f, 112f, 38f);

        TextMeshProUGUI pageText = FindTransform(
            root,
            SettingsActionListPagination.PageTextName)?.GetComponent<TextMeshProUGUI>();
        if (pageText == null)
        {
            pageText = CreateText(
                SettingsActionListPagination.PageTextName,
                root,
                "PAGE  1 / 3",
                12f,
                Muted);
        }

        pageText.fontStyle = FontStyles.Bold;
        pageText.alignment = TextAlignmentOptions.Center;
        pageText.raycastTarget = false;
        SetTopLeft(pageText.rectTransform, 164f, 482f, 100f, 38f);

        Button next = EnsureActionListPagerButton(
            root,
            SettingsActionListPagination.NextButtonName,
            "下一页");
        SetTopLeft(next.GetComponent<RectTransform>(), 276f, 482f, 112f, 38f);
    }

    private static Button EnsureActionListPagerButton(
        Transform root,
        string buttonName,
        string caption)
    {
        Button button = FindTransform(root, buttonName)?.GetComponent<Button>();
        if (button == null)
            button = CreateButton(buttonName, root, caption, 112f, 38f, false);

        button.gameObject.name = buttonName;
        button.gameObject.SetActive(true);
        ConfigureButtonVisual(button, false, caption);
        return button;
    }

    private static void AddInventorySortButton(GameObject root)
    {
        Transform existing = FindTransform(root.transform, "整理");
        Button button = existing != null
            ? existing.GetComponent<Button>()
            : CreateButton("整理", root.transform, "整理", 112f, 38f, false);
        if (button == null)
            throw new MissingComponentException("UI_Bag.prefab 的整理节点缺少 Button。");

        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-24f, 10f);
        rect.sizeDelta = new Vector2(112f, 38f);
        LayoutElement layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        ConfigureButtonVisual(button, false, "整理");
    }

    internal static void AddCraftingPreviewLayers(GameObject root)
    {
        ItemSlot_UI slot = root.GetComponent<ItemSlot_UI>();
        Image reference = slot != null ? slot.image : root.GetComponentInChildren<Image>(true);
        if (reference == null)
            throw new MissingReferenceException($"{root.name} 缺少物品图标 Image。");

        Image ghost = EnsurePreviewImage(root.transform, reference, "Crafting Output Ghost");
        ghost.color = new Color(1f, 1f, 1f, 0.28f);
        ghost.gameObject.SetActive(false);

        Image reveal = EnsurePreviewImage(root.transform, reference, "Crafting Output Reveal");
        reveal.color = Color.white;
        reveal.type = Image.Type.Filled;
        reveal.fillMethod = Image.FillMethod.Vertical;
        reveal.fillOrigin = (int)Image.OriginVertical.Bottom;
        reveal.fillAmount = 0f;
        reveal.gameObject.SetActive(false);

        int referenceIndex = reference.transform.GetSiblingIndex();
        ghost.transform.SetSiblingIndex(referenceIndex + 1);
        reveal.transform.SetSiblingIndex(referenceIndex + 2);
    }

    private static Image EnsurePreviewImage(Transform root, Image reference, string name)
    {
        Transform existing = FindTransform(root, name);
        Image image = existing != null ? existing.GetComponent<Image>() : null;
        if (image == null)
        {
            image = CreateImage(name, reference.transform.parent, Color.white);
            CopyRect(reference.rectTransform, image.rectTransform);
        }

        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }

    private static void AddNetworkPlayerNameLabel(GameObject root)
    {
        Transform existing = FindTransform(root.transform, "玩家名称");
        TextMeshPro label = existing != null ? existing.GetComponent<TextMeshPro>() : null;
        if (label == null)
        {
            GameObject labelObject = new GameObject("玩家名称", typeof(TextMeshPro));
            labelObject.transform.SetParent(root.transform, false);
            label = labelObject.GetComponent<TextMeshPro>();
        }

        label.transform.localPosition = new Vector3(0f, 1.35f, 0f);
        label.transform.localRotation = Quaternion.identity;
        label.transform.localScale = Vector3.one;
        label.text = "玩家";
        label.font = font;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 2.4f;
        label.color = Color.white;
        label.sortingOrder = 100;
    }

    #endregion

    #region 通用控件构建

    private static GameObject CreatePanelRoot(string name, Vector2 size)
    {
        GameObject root = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(BasePanel));
        RectTransform rect = root.GetComponent<RectTransform>();
        SetCentered(rect, Vector2.zero, size);

        Image image = root.GetComponent<Image>();
        image.color = Canvas;
        image.raycastTarget = true;
        AddOutline(image, Amber);

        VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 18);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ConfigureBasePanel(root);
        return root;
    }

    /// <summary>创建带遮罩的居中模态面板，根节点负责拦截背景输入。</summary>
    private static GameObject CreateModalPanelRoot(string name, Vector2 size)
    {
        GameObject root = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(BasePanel));
        Stretch(root.GetComponent<RectTransform>());

        Image overlay = root.GetComponent<Image>();
        overlay.color = new Color(0.015f, 0.028f, 0.034f, 0.78f);
        overlay.raycastTarget = true;
        ConfigureBasePanel(root);

        GameObject dialog = CreateUIObject("设置对话框", root.transform, typeof(Image));
        SetCentered(dialog.GetComponent<RectTransform>(), Vector2.zero, size);
        Image dialogImage = dialog.GetComponent<Image>();
        dialogImage.color = Canvas;
        dialogImage.raycastTarget = true;
        AddOutline(dialogImage, Amber);

        VerticalLayoutGroup layout = dialog.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 18);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return root;
    }

    private static void ConfigureBasePanel(GameObject root)
    {
        BasePanel panel = root.GetComponent<BasePanel>();
        panel.PanelName = root.name;
        panel.canvasGroup = root.GetComponent<CanvasGroup>();
        panel.rectTransform = root.GetComponent<RectTransform>();
    }

    private static void CreateHeader(Transform parent, string title, string closeButtonName)
    {
        GameObject header = CreateUIObject("标题", parent, typeof(Image));
        header.GetComponent<Image>().color = SurfaceRaised;
        header.AddComponent<LayoutElement>().preferredHeight = 50f;

        HorizontalLayoutGroup layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 10, 6, 6);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI titleText = CreateText("标题文本", header.transform, title, 21f, Cream);
        titleText.fontStyle = FontStyles.Bold;
        titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton(closeButtonName, header.transform, "关闭", 72f, 34f, false);
    }

    private static void CreateHint(Transform parent, string value, float height)
    {
        TextMeshProUGUI hint = CreateText("说明文本", parent, value, 13f, Muted);
        hint.enableWordWrapping = true;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
    }

    private static GameObject CreateRow(string name, Transform parent, float height)
    {
        GameObject row = CreateUIObject(name, parent);
        row.AddComponent<LayoutElement>().preferredHeight = height;
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    /// <summary>创建设置页的小节标题，统一使用琥珀色强调和英文眉题。</summary>
    private static void CreateSettingsSection(Transform parent, string title, string eyebrow)
    {
        GameObject section = CreateUIObject(title + "设置分组", parent, typeof(Image));
        LayoutElement element = section.AddComponent<LayoutElement>();
        element.preferredHeight = 26f;

        Image background = section.GetComponent<Image>();
        background.color = new Color(0.07f, 0.15f, 0.17f, 0.72f);
        AddOutline(background, new Color(0.55f, 0.68f, 0.70f, 0.16f));

        HorizontalLayoutGroup layout = section.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 3, 3);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI titleText = CreateText(title + "分组标题", section.transform, title, 13f, Amber);
        titleText.fontStyle = FontStyles.Bold;
        titleText.gameObject.AddComponent<LayoutElement>().preferredWidth = 96f;

        TextMeshProUGUI eyebrowText = CreateText(title + "分组英文", section.transform, eyebrow, 10f, Muted);
        eyebrowText.characterSpacing = 2f;
        eyebrowText.alignment = TextAlignmentOptions.MidlineRight;
        eyebrowText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
    }

    /// <summary>创建设置页下拉项，并写入仅用于展示的默认选项。</summary>
    private static TMP_Dropdown CreateSettingsDropdownRow(
        Transform parent,
        string label,
        string dropdownName,
        string[] options)
    {
        GameObject row = CreateRow(label + "行", parent, 44f);
        CreateRowLabel(row.transform, label, 122f);

        TMP_Dropdown dropdown = CreateDropdown(dropdownName, row.transform);
        LayoutElement element = dropdown.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = 480f;
        element.preferredHeight = 40f;
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string>(options));
        dropdown.value = 0;
        dropdown.RefreshShownValue();
        return dropdown;
    }

    private static void CreateRowLabel(Transform parent, string value, float width)
    {
        TextMeshProUGUI label = CreateText(value + "标签", parent, value, 14f, Cream);
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
    }

    private static void CreateSliderRow(Transform parent, string label, string sliderName)
    {
        GameObject row = CreateRow(label + "行", parent, 38f);
        CreateRowLabel(row.transform, label, 88f);
        Slider slider = CreateSlider(sliderName, row.transform);
        slider.value = 1f;
        TextMeshProUGUI value = CreateText(sliderName + "_数值", row.transform, "100%", 13f, Amber);
        value.alignment = TextAlignmentOptions.MidlineRight;
        value.gameObject.AddComponent<LayoutElement>().preferredWidth = 48f;
    }

    private static Slider CreateSlider(string name, Transform parent)
    {
        GameObject root = CreateUIObject(name, parent, typeof(Image), typeof(Slider));
        root.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Image background = root.GetComponent<Image>();
        background.color = new Color(0.14f, 0.23f, 0.25f, 1f);
        AddOutline(background, Border);

        GameObject fillArea = CreateUIObject("Fill Area", root.transform);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        Stretch(fillAreaRect);
        fillAreaRect.offsetMin = new Vector2(8f, 8f);
        fillAreaRect.offsetMax = new Vector2(-8f, -8f);

        Image fill = CreateImage("Fill", fillArea.transform, Teal);
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.sizeDelta = new Vector2(10f, 0f);

        GameObject handleArea = CreateUIObject("Handle Slide Area", root.transform);
        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        Stretch(handleAreaRect);
        handleAreaRect.offsetMin = new Vector2(5f, 0f);
        handleAreaRect.offsetMax = new Vector2(-5f, 0f);

        Image handle = CreateImage("Handle", handleArea.transform, Amber);
        handle.rectTransform.sizeDelta = new Vector2(16f, 26f);

        Slider slider = root.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.direction = Slider.Direction.LeftToRight;
        slider.fillRect = fillRect;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        return slider;
    }

    private static Toggle CreateToggle(string name, Transform parent)
    {
        GameObject root = CreateUIObject(name, parent, typeof(Image), typeof(Toggle));
        LayoutElement element = root.AddComponent<LayoutElement>();
        element.preferredWidth = 58f;
        element.preferredHeight = 30f;
        Image background = root.GetComponent<Image>();
        background.color = SurfaceRaised;
        AddOutline(background, Border);

        Image checkmark = CreateImage("Checkmark", root.transform, Teal);
        RectTransform checkmarkRect = checkmark.rectTransform;
        checkmarkRect.anchorMin = checkmarkRect.anchorMax = new Vector2(0.5f, 0.5f);
        checkmarkRect.sizeDelta = new Vector2(42f, 18f);

        Toggle toggle = root.GetComponent<Toggle>();
        toggle.targetGraphic = background;
        toggle.graphic = checkmark;
        toggle.isOn = true;
        return toggle;
    }

    private static TMP_InputField CreateInputField(string name, Transform parent, string placeholderValue)
    {
        GameObject root = CreateUIObject(name, parent, typeof(Image), typeof(TMP_InputField));
        Image background = root.GetComponent<Image>();
        background.color = Surface;
        AddOutline(background, Border);

        GameObject textArea = CreateUIObject("Text Area", root.transform, typeof(RectMask2D));
        RectTransform area = textArea.GetComponent<RectTransform>();
        Stretch(area);
        area.offsetMin = new Vector2(12f, 3f);
        area.offsetMax = new Vector2(-12f, -3f);

        TextMeshProUGUI valueText = CreateInputText("Text", textArea.transform, string.Empty, Cream);
        TextMeshProUGUI placeholder = CreateInputText("Placeholder", textArea.transform, placeholderValue, Muted);
        placeholder.fontStyle = FontStyles.Italic;

        TMP_InputField input = root.GetComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = area;
        input.textComponent = valueText;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        return input;
    }

    private static TMP_Dropdown CreateDropdown(string name, Transform parent)
    {
        GameObject root = CreateUIObject(name, parent, typeof(Image), typeof(TMP_Dropdown));
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(402f, 42f);
        Image background = root.GetComponent<Image>();
        background.color = Surface;
        AddOutline(background, Border);

        TextMeshProUGUI caption = CreateText("Label", root.transform, "每 10 分钟", 14f, Cream);
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(caption.rectTransform);
        caption.rectTransform.offsetMin = new Vector2(12f, 2f);
        caption.rectTransform.offsetMax = new Vector2(-42f, -2f);

        TextMeshProUGUI arrow = CreateText("Arrow", root.transform, "▼", 13f, Amber);
        arrow.alignment = TextAlignmentOptions.Center;
        arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
        arrow.rectTransform.anchorMax = Vector2.one;
        arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
        arrow.rectTransform.anchoredPosition = Vector2.zero;
        arrow.rectTransform.sizeDelta = new Vector2(36f, 0f);

        GameObject template = CreateUIObject("Template", root.transform, typeof(Image), typeof(ScrollRect));
        RectTransform templateRect = template.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0f, 0f);
        templateRect.anchorMax = new Vector2(1f, 0f);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.anchoredPosition = new Vector2(0f, -3f);
        templateRect.sizeDelta = new Vector2(0f, 224f);
        template.GetComponent<Image>().color = Surface;
        AddOutline(template.GetComponent<Image>(), Border);

        GameObject viewport = CreateUIObject("Viewport", template.transform, typeof(RectMask2D));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMin = new Vector2(3f, 3f);
        viewportRect.offsetMax = new Vector2(-3f, -3f);

        GameObject content = CreateUIObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject item = CreateUIObject("Item", content.transform, typeof(Image), typeof(Toggle));
        item.AddComponent<LayoutElement>().preferredHeight = 31f;
        Image itemBackground = item.GetComponent<Image>();
        itemBackground.color = SurfaceRaised;
        Image checkmark = CreateImage("Item Checkmark", item.transform, Amber);
        RectTransform checkmarkRect = checkmark.rectTransform;
        checkmarkRect.anchorMin = checkmarkRect.anchorMax = new Vector2(0f, 0.5f);
        checkmarkRect.pivot = new Vector2(0f, 0.5f);
        checkmarkRect.anchoredPosition = new Vector2(10f, 0f);
        checkmarkRect.sizeDelta = new Vector2(8f, 18f);
        TextMeshProUGUI itemLabel = CreateText("Item Label", item.transform, "选项", 13f, Cream);
        itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(itemLabel.rectTransform);
        itemLabel.rectTransform.offsetMin = new Vector2(28f, 1f);
        itemLabel.rectTransform.offsetMax = new Vector2(-8f, -1f);
        Toggle itemToggle = item.GetComponent<Toggle>();
        itemToggle.targetGraphic = itemBackground;
        itemToggle.graphic = checkmark;

        ScrollRect scroll = template.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.viewport = viewportRect;
        scroll.content = contentRect;

        TMP_Dropdown dropdown = root.GetComponent<TMP_Dropdown>();
        dropdown.targetGraphic = background;
        dropdown.captionText = caption;
        dropdown.template = templateRect;
        dropdown.itemText = itemLabel;
        template.SetActive(false);
        return dropdown;
    }

    private static void CreateDifficultyOption(Transform parent, GameDifficultyDefinition definition, bool selected)
    {
        GameObject option = CreateUIObject($"难度_{definition.Id}", parent, typeof(Image), typeof(Button));
        option.AddComponent<LayoutElement>().preferredHeight = 72f;
        Image image = option.GetComponent<Image>();
        image.color = selected ? Amber : Surface;
        AddOutline(image, selected ? Amber : Border);
        Button button = option.GetComponent<Button>();
        button.targetGraphic = image;
        ConfigureButtonColors(button);

        TextMeshProUGUI title = CreateText("名称", option.transform, definition.DisplayName, 17f, Cream);
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        SetTopStretch(title.rectTransform, new Vector2(16f, -34f), new Vector2(-16f, -6f));

        TextMeshProUGUI description = CreateText("说明", option.transform, definition.Description, 12.5f, Muted);
        description.enableWordWrapping = false;
        description.overflowMode = TextOverflowModes.Ellipsis;
        description.alignment = TextAlignmentOptions.MidlineLeft;
        description.rectTransform.anchorMin = new Vector2(0f, 0f);
        description.rectTransform.anchorMax = new Vector2(1f, 0f);
        description.rectTransform.offsetMin = new Vector2(16f, 6f);
        description.rectTransform.offsetMax = new Vector2(-16f, 34f);
    }

    private static void CreateBindingScrollView(Transform parent)
    {
        GameObject scrollRoot = CreateUIObject("绑定列表", parent, typeof(Image), typeof(ScrollRect));
        LayoutElement scrollElement = scrollRoot.AddComponent<LayoutElement>();
        scrollElement.minHeight = 180f;
        scrollElement.flexibleHeight = 1f;
        scrollRoot.GetComponent<Image>().color = Surface;

        GameObject viewport = CreateUIObject("Viewport", scrollRoot.transform, typeof(Image), typeof(RectMask2D));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMax = new Vector2(-18f, 0f);
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.001f);

        GameObject content = CreateUIObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(8, 8, 8, 8);
        contentLayout.spacing = 7f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject scrollbarObject = CreateUIObject("Scrollbar Vertical", scrollRoot.transform, typeof(Image), typeof(Scrollbar));
        RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = Vector2.one;
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = new Vector2(-14f, 2f);
        scrollbarRect.offsetMax = new Vector2(-2f, -2f);
        scrollbarObject.GetComponent<Image>().color = SurfaceRaised;
        GameObject slidingArea = CreateUIObject("Sliding Area", scrollbarObject.transform);
        Stretch(slidingArea.GetComponent<RectTransform>());
        Image handle = CreateImage("Handle", slidingArea.transform, Muted);
        Stretch(handle.rectTransform);
        Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.size = 0.35f;

        ScrollRect scroll = scrollRoot.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scroll.verticalScrollbarSpacing = 4f;
    }

    private static Transform CreateFooter(Transform parent)
    {
        GameObject footer = CreateUIObject("底部操作", parent);
        footer.AddComponent<LayoutElement>().preferredHeight = 42f;
        HorizontalLayoutGroup layout = footer.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        GameObject spacer = CreateUIObject("Spacer", footer.transform);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
        return footer.transform;
    }

    private static Button CreateButton(string name, Transform parent, string caption, float width, float height, bool primary)
    {
        GameObject root = CreateUIObject(name, parent, typeof(Image), typeof(Button));
        LayoutElement element = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
        element.preferredWidth = width;
        element.preferredHeight = height;
        Button button = root.GetComponent<Button>();
        ConfigureButtonVisual(button, primary, caption);
        return button;
    }

    private static void ConfigureButtonVisual(Button button, bool primary, string caption)
    {
        Image image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
        image.color = primary ? Amber : SurfaceRaised;
        AddOutline(image, primary ? Amber : Border);
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ConfigureButtonColors(button);
        if (button.GetComponent<FlatWorldUIFeedback>() == null)
            button.gameObject.AddComponent<FlatWorldUIFeedback>();

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null)
            label = CreateText("Text (TMP)", button.transform, caption, 14f, Cream);
        label.text = caption;
        label.font = font;
        label.fontSize = 14f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        Stretch(label.rectTransform);
    }

    private static void ConfigureButtonColors(Button button)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.08f, 1f, 1f);
        colors.pressedColor = new Color(0.74f, 0.78f, 0.80f, 1f);
        colors.selectedColor = FlatWorldUITheme.Selection;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
        colors.fadeDuration = 0.1f;
        button.colors = colors;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string value, float size, Color color)
    {
        GameObject root = CreateUIObject(name, parent, typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        TextMeshProUGUI text = root.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        return text;
    }

    private static TextMeshProUGUI CreateInputText(string name, Transform parent, string value, Color color)
    {
        TextMeshProUGUI text = CreateText(name, parent, value, 25f, color);
        text.enableAutoSizing = true;
        text.fontSizeMin = 18f;
        text.fontSizeMax = 25f;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(text.rectTransform);
        return text;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject root = CreateUIObject(name, parent, typeof(CanvasRenderer), typeof(Image));
        Image image = root.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static GameObject CreateUIObject(string name, Transform parent, params System.Type[] extraComponents)
    {
        System.Type[] components = new System.Type[extraComponents.Length + 1];
        components[0] = typeof(RectTransform);
        for (int i = 0; i < extraComponents.Length; i++)
            components[i + 1] = extraComponents[i];

        GameObject root = new GameObject(name, components);
        if (parent != null)
            root.transform.SetParent(parent, false);
        return root;
    }

    private static void AddOutline(Graphic graphic, Color color)
    {
        Outline outline = graphic.GetComponent<Outline>() ?? graphic.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;
    }

    private static Transform FindTransform(Transform root, string name)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null && transforms[i].name == name)
                return transforms[i];
        }

        return null;
    }

    private static void SetUILayerRecursively(GameObject root)
    {
        int layer = LayerMask.NameToLayer("UI");
        if (layer < 0)
            layer = 5;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            transforms[i].gameObject.layer = layer;
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

    private static void SetCentered(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetTopLeft(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void SetTopStretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void CopyRect(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    #endregion
}
