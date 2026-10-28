// AI-Context: 正式运行时 UI 的 Prefab 固化入口；运行时代码只能实例化这些资产，不再创建视觉节点。

using System.IO;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class RuntimeUIPrefabBuilder
{
    #region 路径与共享配置


    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";
    private const string PrefabRoot = "Assets/2_Prefabs/2-1_UI/";
    private const string MainMenuCoreRoot = PrefabRoot + "MainMenu/Core/";
    private const string DialogueRoot = PrefabRoot + "Gameplay/Dialogue/";
    private const string HudRoot = PrefabRoot + "Gameplay/HUD/";
    private const string LoadingRoot = PrefabRoot + "Gameplay/Loading/";
    /// <summary>运行时诊断 UI 的正式 Prefab 目录。</summary>
    private const string DebugRoot = PrefabRoot + "Gameplay/Debug/";
    private const string MobileRoot = PrefabRoot + "Gameplay/Mobile/";
    private const string BuffRoot = PrefabRoot + "Gameplay/Status/Buff/";
    private const string SettingsPanelsRoot = PrefabRoot + "Settings/Panels/";
    private const string SettingsComponentsRoot = PrefabRoot + "Settings/Components/";
    private const string UIRootPrefab = "Assets/Resources/UI/UIRoot.prefab";
    /// <summary>承载启动阶段直接引用的管理器 Prefab 路径。</summary>
    private const string WorldManagerPrefab = "Assets/2_Prefabs/Core/Managers/WorldManager.prefab";
    private const string InventoryPanelsRoot = PrefabRoot + "Gameplay/Inventory/Panels/";
    private const string InventoryComponentsRoot = PrefabRoot + "Gameplay/Inventory/Components/";
    private const string NetworkPlayerPrefab = "Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab";
    private const string PlayerPrefab = "Assets/2_Prefabs/Gameplay/Player/Player.prefab";

    private static Color Canvas => FlatWorldUITheme.Canvas;
    private static Color Surface => FlatWorldUITheme.SurfaceLow;
    private static Color SurfaceRaised => FlatWorldUITheme.SurfaceRaised;
    private static Color Cream => FlatWorldUITheme.TextPrimary;
    private static Color Muted => FlatWorldUITheme.TextSecondary;
    private static Color Amber => FlatWorldUITheme.Accent;
    private static Color Teal => FlatWorldUITheme.Teal;
    private static Color Danger => FlatWorldUITheme.Danger;
    private static Color Border => FlatWorldUITheme.Border;
    // 手机右侧操作组统一使用同一套安全边距与间距，避免摇杆和按钮各自漂移。
    private const float MobileActionRightMargin = 76f;
    private const float MobileActionBottomMargin = 54f;
    private const float MobileAttackZoneSize = 230f;
    private const float MobileActionButtonSize = 112f;
    private const float MobileActionGap = 16f;
    private const float MobileActionGroupWidth = MobileActionButtonSize * 2f + MobileActionGap;
    private const float MobileActionGroupHeight = MobileAttackZoneSize + MobileActionGap + MobileActionButtonSize;
    private const float MobileHotbarSideButtonSize = 82f;
    private const float MobileHotbarSideButtonGap = 12f;
    // 右侧抽屉为单列滚动列表；三角开关保留至少 60 逻辑像素的触控宽度。
    private const float MobileDrawerWidth = 280f;
    private const float MobileDrawerButtonHeight = 76f;
    private const float MobileDrawerToggleWidth = 60f;
    // 主菜单设置与游戏内设置共用灰阶材质语言，只保留层级明度差。
    private static Color MainMenuSettingsCanvas => FlatWorldUITheme.Canvas;
    private static Color MainMenuSettingsSurface => FlatWorldUITheme.SurfaceLow;

    private static TMP_FontAsset font;

    #endregion

    #region 重建入口

    /// <summary>版本号使用安全区右下角的透明文本，自动读取构建版本。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Refresh Version Label UI")]
    public static void RefreshVersionLabelUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        UpdateExistingPrefab(UIRootPrefab, root =>
        {
            Transform parent = FindTransform(root.transform, "SafeAreaRoot") ?? root.transform;
            TextMeshProUGUI label = FindTransform(parent, "FWUI_Version")?.GetComponent<TextMeshProUGUI>();
            if (label == null)
                label = CreateText("FWUI_Version", parent, string.Empty, 17f, Muted);
            label.text = $"v{PlayerSettings.bundleVersion}";
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.BottomRight;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-14f, 8f);
            rect.sizeDelta = new Vector2(180f, 28f);
            if (label.GetComponent<ApplicationVersionLabel>() == null)
                label.gameObject.AddComponent<ApplicationVersionLabel>();
        });
        AssetDatabase.SaveAssets();
    }

    /// <summary>只重建设置主面板与分页容器，不重建设置页资产。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Settings Action List UI")]
    public static void RebuildSettingsActionListUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        SaveGameSettingsPrefab();
        GameUIPrefabRebuilder.RebuildActionListUI();
        UpdateExistingPrefab(
            MainMenuCoreRoot + "UI_ActionList.prefab",
            ConfigureSettingsActionListPages);
        SaveMainMenuSettingsPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        FlatWorld.Localization.Editor.FlatWorldLocalizationSetup.SyncRuntimeUiTexts(
            "设置", "视觉特效", "显示设置", "镜头控制", "语言", "提示", "存档加载失败", "确定",
            "界面语言会立即切换，点击保存后保留修改。", "当前语言：简体中文", "当前语言：English");
        Debug.Log("[Runtime UI] 已固化游戏内设置主界面，并同步主菜单客户端设置子集。");
    }

    /// <summary>只刷新设置会话页的保存/退出入口与确认层，保留其余设置节点和本地文件 ID。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Refresh Settings Session Actions UI")]
    public static void RefreshSettingsSessionActionsUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        UpdateExistingPrefab(
            MainMenuCoreRoot + "UI_ActionList.prefab",
            ConfigureSettingsSessionActions);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已刷新设置会话页的保存退出入口与确认层。");
    }

    /// <summary>只重建设置入口和全部设置子分页，避免改动无关运行时 UI。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild All Settings Pages UI")]
    public static void RebuildAllSettingsPagesUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        GameUIPrefabRebuilder.RebuildActionListUI();
        Directory.CreateDirectory(SettingsPanelsRoot);
        Directory.CreateDirectory(SettingsComponentsRoot);
        Directory.CreateDirectory(MobileRoot);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.AudioSettings + ".prefab", BuildAudioSettings);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.UISettings + ".prefab", BuildInterfaceSettings);
        SaveCameraControlSettingsPrefab();
        SaveCoordinateDisplaySettingsPrefab();
        SaveVisualEffectsSettingsPrefab();
        SaveDebugSettingsPrefab();
        SaveGameSettingsPrefab();
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.AutoSaveSettings + ".prefab", BuildAutoSaveSettings);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.WorldStreamingSettings + ".prefab", BuildWorldStreamingSettings);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.DifficultySettings + ".prefab", BuildDifficultySettings);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.InputBindingSettings + ".prefab", BuildInputBindingSettings);
        SaveNewPrefab(SettingsComponentsRoot + RuntimeUIPrefabKeys.InputBindingRow + ".prefab", BuildInputBindingRow);
        SaveMobileControlLayoutEditorPrefab();
        UpdateExistingPrefab(MainMenuCoreRoot + "UI_ActionList.prefab", ConfigureSettingsActionListPages);
        SaveMainMenuSettingsPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化全部设置分页，并同步主菜单客户端设置子集。");
    }

    /// <summary>只重建难度设置页，避免难度界面迭代时触碰其他设置资产。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Difficulty Settings UI")]
    public static void RebuildDifficultySettingsUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(SettingsPanelsRoot);
        SaveNewPrefab(
            SettingsPanelsRoot + RuntimeUIPrefabKeys.DifficultySettings + ".prefab",
            BuildDifficultySettings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化 0-20 级难度设置页。");
    }

    [MenuItem("FlatWorld/其他工具/UI/Rebuild Runtime Prefab UI")]
    public static void RebuildRuntimePrefabUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(SettingsPanelsRoot);
        Directory.CreateDirectory(SettingsComponentsRoot);
        Directory.CreateDirectory(DialogueRoot);
        Directory.CreateDirectory(HudRoot);
        Directory.CreateDirectory(LoadingRoot);
        Directory.CreateDirectory(DebugRoot);
        Directory.CreateDirectory(MobileRoot);
        Directory.CreateDirectory(BuffRoot);

        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.AudioSettings + ".prefab", BuildAudioSettings);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.UISettings + ".prefab", BuildInterfaceSettings);
        SaveCameraControlSettingsPrefab();
        SaveCoordinateDisplaySettingsPrefab();
        SaveVisualEffectsSettingsPrefab();
        SaveDebugSettingsPrefab();
        SaveMainMenuExitConfirmationPrefab();
        SaveGameSettingsPrefab();
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.AutoSaveSettings + ".prefab", BuildAutoSaveSettings);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.WorldStreamingSettings + ".prefab", BuildWorldStreamingSettings);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.DifficultySettings + ".prefab", BuildDifficultySettings);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.InputBindingSettings + ".prefab", BuildInputBindingSettings);
        SaveNewPrefab(SettingsComponentsRoot + RuntimeUIPrefabKeys.InputBindingRow + ".prefab", BuildInputBindingRow);
        SaveNewPrefab(DialogueRoot + RuntimeUIPrefabKeys.PlayerChatInput + ".prefab", BuildPlayerChatInput);
        SaveNewPrefab(DialogueRoot + RuntimeUIPrefabKeys.CharacterSpeechBubble + ".prefab", BuildSpeechBubble);
        SaveResourceLoadingPrefab();
        SaveRuntimeDebugOverlayPrefab();
        SaveNewPrefab(LoadingRoot + RuntimeUIPrefabKeys.WorldLoading + ".prefab", BuildWorldLoading);
        SaveDimensionLoadingPrefab();
        SavePlayerWorldCoordinatePrefab();
        SaveShipFlightHudPrefab();
        SaveSaveStatusPrefab();
        SaveBuffStatusPrefabs();
        SaveMobileControlsPrefab();
        SaveMobileControlLayoutEditorPrefab();
        UpdateExistingPrefab(UIRootPrefab, EnsureSafeAreaRoot);

        UpdateExistingPrefab(MainMenuCoreRoot + "UI_ActionList.prefab", ConfigureSettingsActionListPages);
        SaveMainMenuSettingsPrefab();
        UpdateExistingPrefab(InventoryPanelsRoot + "UI_Bag.prefab", ConfigureInventoryActionButtons);
        UpdateExistingPrefab(InventoryComponentsRoot + "UI_Slot.prefab", AddCraftingPreviewLayers);
        UpdateExistingWorldPrefab(NetworkPlayerPrefab, AddNetworkPlayerNameLabel);
        UpdateExistingWorldPrefab(PlayerPrefab, EnsurePlayerBuffStatusHUD);
        UpdateExistingWorldPrefab(PlayerPrefab, EnsurePlayerShipFlightHUD);
        UpdateExistingWorldPrefab(PlayerPrefab, EnsurePlayerMobileControlsHUD);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化设置、设置列表分页、显示设置、世界加载、保存状态、Buff 状态、聊天、气泡、玩家坐标、背包整理与排序、制作预览与联机玩家名称 Prefab。运行时不再创建这些视觉节点。");
    }

    /// <summary>只构建手机 HUD、安全区根节点和 Player 挂载，避免重写其它已有 UI Prefab。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Mobile Controls UI")]
    public static void RebuildMobileControlsUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(MobileRoot);
        SaveMobileControlsPrefab();
        SaveMobileControlLayoutEditorPrefab();
        UpdateExistingPrefab(UIRootPrefab, EnsureSafeAreaRoot);
        UpdateExistingWorldPrefab(PlayerPrefab, EnsurePlayerMobileControlsHUD);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化手机多点触控 HUD、安全区根节点，并挂载到 Player.prefab。");
    }

    /// <summary>只更新触控布局编辑页，不重建 HUD、玩家或根 Canvas。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Touch Layout Editor Only")]
    public static void RebuildTouchLayoutEditorOnly()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
            throw new MissingReferenceException($"缺少设置字体：{FontPath}");
        SaveMobileControlLayoutEditorPrefab();
        AssetDatabase.SaveAssets();
    }

    /// <summary>只重建界面与镜头控制设置页，并同步设置入口分页。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Interface Settings UI")]
    public static void RebuildInterfaceSettingsUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(SettingsPanelsRoot);
        string prefabPath = SettingsPanelsRoot + RuntimeUIPrefabKeys.UISettings + ".prefab";
        SaveNewPrefab(prefabPath, BuildInterfaceSettings);
        EnsureRuntimePrefabAddressable(prefabPath);
        SaveCameraControlSettingsPrefab();
        UpdateExistingPrefab(
            MainMenuCoreRoot + "UI_ActionList.prefab",
            ConfigureSettingsActionListPages);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化界面设置、镜头控制与设置入口分页。");
    }

    /// <summary>只重建维度切换加载页，避免覆盖用户正在调整的其它运行时 UI。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Dimension Loading UI")]
    public static void RebuildDimensionLoadingUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(LoadingRoot);
        SaveDimensionLoadingPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化维度切换专属加载页并注册 Addressable。");
    }

    /// <summary>只重建启动资源加载面板，并把直接引用同步到 WorldManager。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Resource Loading UI")]
    public static void RebuildResourceLoadingUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(LoadingRoot);
        SaveResourceLoadingPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化启动资源加载 UGUI，并绑定到 WorldManager.prefab。");
    }

    /// <summary>只重建最早启动的运行时调试悬浮窗，并同步 WorldManager 直接引用。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Runtime Debug Overlay")]
    public static void RebuildRuntimeDebugOverlayUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(DebugRoot);
        SaveRuntimeDebugOverlayPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化运行时调试悬浮窗，并绑定到 WorldManager.prefab。");
    }

    /// <summary>只重建区块流送设置和入口，避免小改动重写全部运行时 Prefab。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild World Streaming Settings UI")]
    public static void RebuildWorldStreamingSettingsUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(SettingsPanelsRoot);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.WorldStreamingSettings + ".prefab",
            BuildWorldStreamingSettings);
        UpdateExistingPrefab(MainMenuCoreRoot + "UI_ActionList.prefab",
            ConfigureSettingsActionListPages);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化区块流送性能设置 Prefab 与入口按钮。");
    }

    /// <summary>只重建自动保存设置页，避免触控布局调整重写其它设置 Prefab。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Auto Save Settings UI")]
    public static void RebuildAutoSaveSettingsUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(SettingsPanelsRoot);
        SaveNewPrefab(
            SettingsPanelsRoot + RuntimeUIPrefabKeys.AutoSaveSettings + ".prefab",
            BuildAutoSaveSettings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化移动端可触控的自动保存设置 Prefab。");
    }

    /// <summary>只重建主菜单设置窗口；基础结构复制游戏内设置，仅保留客户端本地设置分页。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Main Menu Settings UI")]
    public static void RebuildMainMenuSettingsUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(SettingsPanelsRoot);
        SaveMainMenuSettingsPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化主菜单客户端设置窗口，并移除世界与会话分页。");
    }

    /// <summary>只重建主菜单退出确认弹窗，并登记为运行时可寻址 Prefab。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Main Menu Exit Confirmation UI")]
    public static void RebuildMainMenuExitConfirmationUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(MainMenuCoreRoot);
        SaveMainMenuExitConfirmationPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化主菜单退出确认 Prefab。");
    }

    /// <summary>只重建左上角常驻的玩家坐标与 FPS HUD，并确保其进入运行时 Prefab 索引。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Player World Coordinate HUD")]
    public static void RebuildPlayerWorldCoordinateHUD()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(HudRoot);
        SavePlayerWorldCoordinatePrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化玩家世界坐标 HUD Prefab。");
    }

    /// <summary>只重建右上角保存状态提示，避免无关运行时 Prefab 被重写。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Save Status HUD")]
    public static void RebuildSaveStatusHUD()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(HudRoot);
        SaveSaveStatusPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化右上角保存状态提示 Prefab。");
    }

    /// <summary>只重建左侧中部 Buff 提示栏及玩家挂载组件，避免无关 Prefab 被重写。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Buff Status HUD")]
    public static void RebuildBuffStatusHUD()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(BuffRoot);
        SaveBuffStatusPrefabs();
        UpdateExistingWorldPrefab(PlayerPrefab, EnsurePlayerBuffStatusHUD);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化左侧中部 Buff 状态提示栏，并挂载到 Player.prefab。");
    }

    /// <summary>只重建坐标显示设置和设置列表分页，避免无关运行时 Prefab 被重写。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Coordinate Display Settings UI")]
    public static void RebuildCoordinateDisplaySettingsUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(SettingsPanelsRoot);
        SaveCoordinateDisplaySettingsPrefab();
        UpdateExistingPrefab(MainMenuCoreRoot + "UI_ActionList.prefab", ConfigureSettingsActionListPages);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化坐标显示设置 Prefab 与十页设置列表。");
    }

    /// <summary>重建按键绑定面板与动态绑定行，确保布局源和正式 Prefab 保持一致。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Input Binding UI")]
    public static void RebuildInputBindingUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(SettingsPanelsRoot);
        Directory.CreateDirectory(SettingsComponentsRoot);
        SaveNewPrefab(
            SettingsPanelsRoot + RuntimeUIPrefabKeys.InputBindingSettings + ".prefab",
            BuildInputBindingSettings);
        SaveNewPrefab(
            SettingsComponentsRoot + RuntimeUIPrefabKeys.InputBindingRow + ".prefab",
            BuildInputBindingRow);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化按键绑定面板与绑定行 Prefab。");
    }

    /// <summary>只重建按键绑定面板布局，保留动态绑定行 Prefab 的稳定文件 ID。</summary>
    [MenuItem("FlatWorld/其他工具/UI/Rebuild Input Binding Panel UI")]
    public static void RebuildInputBindingPanelUI()
    {
        if (!TryLoadRequiredPrefabBuilderFont(FontPath, "Runtime UI", out font))
            return;

        Directory.CreateDirectory(SettingsPanelsRoot);
        SaveNewPrefab(
            SettingsPanelsRoot + RuntimeUIPrefabKeys.InputBindingSettings + ".prefab",
            BuildInputBindingSettings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化横向紧凑的按键绑定面板 Prefab。");
    }

    private static void SaveNewPrefab(string path, System.Func<GameObject> factory)
    {
        GameObject root = factory();
        try
        {
            SetUILayerRecursively(root);
            FlatWorldUITheme.Apply(root.transform);
            PrepareSharedControlsForSave(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// 以正式游戏内设置 Prefab 为主菜单设置的结构基线，再定向裁成纯客户端设置子集。
    /// 只覆盖 prefab 文件本体，保留目标 .meta/GUID，避免破坏既有 Addressables 引用。
    /// </summary>
    private static void SaveMainMenuSettingsPrefab()
    {
        string sourcePath = MainMenuCoreRoot + "UI_ActionList.prefab";
        string targetPath = SettingsPanelsRoot + RuntimeUIPrefabKeys.MainMenuSettings + ".prefab";
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"找不到游戏内设置基线 Prefab：{sourcePath}", sourcePath);

        Directory.CreateDirectory(SettingsPanelsRoot);
        File.Copy(sourcePath, targetPath, true);
        AssetDatabase.ImportAsset(
            targetPath,
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        UpdateExistingPrefab(targetPath, ConfigureMainMenuSettingsActionList);
        EnsureRuntimePrefabAddressable(targetPath);
    }

    /// <summary>保存主菜单退出确认弹窗，并登记为 GameRes 可查询的正式运行时 Prefab。</summary>
    private static void SaveMainMenuExitConfirmationPrefab()
    {
        string prefabPath = MainMenuCoreRoot + RuntimeUIPrefabKeys.MainMenuExitConfirmation + ".prefab";
        SaveNewPrefab(prefabPath, BuildMainMenuExitConfirmation);
        EnsureRuntimePrefabAddressable(prefabPath);
    }

    /// <summary>保存坐标与 FPS HUD 并注册 Prefab 标签，确保 GameRes 能按键名加载。</summary>
    private static void SavePlayerWorldCoordinatePrefab()
    {
        string prefabPath = HudRoot + RuntimeUIPrefabKeys.PlayerWorldCoordinate + ".prefab";
        SaveNewPrefab(prefabPath, BuildPlayerWorldCoordinateHUD);
        EnsureRuntimePrefabAddressable(prefabPath);
    }

    /// <summary>保存右上角保存状态提示并登记为运行时 Prefab。</summary>
    private static void SaveSaveStatusPrefab()
    {
        string prefabPath = HudRoot + RuntimeUIPrefabKeys.SaveStatus + ".prefab";
        SaveNewPrefab(prefabPath, BuildSaveStatusHUD);
        EnsureRuntimePrefabAddressable(prefabPath);
    }

    /// <summary>保存 Buff 状态面板和可复用行 Prefab，并登记为运行时 Addressable。</summary>
    private static void SaveBuffStatusPrefabs()
    {
        string panelPath = BuffRoot + RuntimeUIPrefabKeys.BuffStatus + ".prefab";
        string itemPath = BuffRoot + RuntimeUIPrefabKeys.BuffStatusItem + ".prefab";
        SaveNewPrefab(panelPath, BuildBuffStatusHUD);
        SaveNewPrefab(itemPath, BuildBuffStatusItem);
        EnsureRuntimePrefabAddressable(panelPath);
        EnsureRuntimePrefabAddressable(itemPath);
    }

    /// <summary>保存正式手机 HUD，并登记为 GameRes 可寻址 Prefab。</summary>
    private static void SaveMobileControlsPrefab()
    {
        string prefabPath = MobileRoot + RuntimeUIPrefabKeys.MobileControls + ".prefab";
        SaveNewPrefab(prefabPath, BuildMobileControlsHUD);
        EnsureRuntimePrefabAddressable(prefabPath);
    }

    /// <summary>保存触屏玩法控件布局编辑器，并登记为 GameRes 可寻址 Prefab。</summary>
    private static void SaveMobileControlLayoutEditorPrefab()
    {
        string prefabPath = MobileRoot + RuntimeUIPrefabKeys.MobileControlLayoutEditor + ".prefab";
        SaveNewPrefab(prefabPath, BuildMobileControlLayoutEditor);
        EnsureRuntimePrefabAddressable(prefabPath);
    }

    /// <summary>保存启动资源加载面板、登记 Prefab 标签，并建立启动阶段可用的直接引用。</summary>
    private static void SaveResourceLoadingPrefab()
    {
        string prefabPath = LoadingRoot + RuntimeUIPrefabKeys.ResourceLoading + ".prefab";
        SaveNewPrefab(prefabPath, BuildResourceLoading);
        EnsureRuntimePrefabAddressable(prefabPath);
        BindResourceLoadingPrefab(prefabPath);
    }

    /// <summary>保存调试悬浮窗、登记 Prefab 标签，并建立启动阶段可用的直接引用。</summary>
    private static void SaveRuntimeDebugOverlayPrefab()
    {
        string prefabPath = DebugRoot + RuntimeUIPrefabKeys.RuntimeDebugOverlay + ".prefab";
        SaveNewPrefab(prefabPath, BuildRuntimeDebugOverlay);
        // Unity 保存新建的独立 Canvas 时可能重写根缩放，保存后再固化一次正式 Prefab。
        UpdateExistingPrefab(prefabPath, root => root.transform.localScale = Vector3.one);
        EnsureRuntimePrefabAddressable(prefabPath);
        BindRuntimeDebugOverlayPrefab(prefabPath);
    }

    /// <summary>把调试悬浮窗 Prefab 绑定到高优先级启动器，避免依赖 GameRes 或 Addressables。</summary>
    private static void BindRuntimeDebugOverlayPrefab(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            throw new FileNotFoundException($"找不到调试悬浮窗 Prefab：{prefabPath}", prefabPath);

        UpdateExistingWorldPrefab(WorldManagerPrefab, root =>
        {
            RuntimeDebugOverlayLauncher launcher = root.GetComponent<RuntimeDebugOverlayLauncher>();
            if (launcher == null)
                launcher = root.AddComponent<RuntimeDebugOverlayLauncher>();

            SerializedObject serializedLauncher = new SerializedObject(launcher);
            SerializedProperty prefabProperty = serializedLauncher.FindProperty("overlayPrefab");
            if (prefabProperty == null)
                throw new System.MissingFieldException(nameof(RuntimeDebugOverlayLauncher), "overlayPrefab");

            prefabProperty.objectReferenceValue = prefab;
            serializedLauncher.ApplyModifiedPropertiesWithoutUndo();
        });
    }

    /// <summary>把资源加载 Prefab 直接绑定给 GameRes，避免启动时反向依赖 Addressables。</summary>
    private static void BindResourceLoadingPrefab(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            throw new FileNotFoundException($"找不到资源加载 Prefab：{prefabPath}", prefabPath);

        UpdateExistingWorldPrefab(WorldManagerPrefab, root =>
        {
            GameRes gameRes = root.GetComponentInChildren<GameRes>(true);
            if (gameRes == null)
                throw new MissingComponentException($"{WorldManagerPrefab} 缺少 GameRes。");

            SerializedObject serializedGameRes = new SerializedObject(gameRes);
            SerializedProperty prefabProperty = serializedGameRes.FindProperty("resourceLoadingPrefab");
            if (prefabProperty == null)
                throw new System.MissingFieldException(nameof(GameRes), "resourceLoadingPrefab");

            prefabProperty.objectReferenceValue = prefab;
            serializedGameRes.ApplyModifiedPropertiesWithoutUndo();
        });
    }

    /// <summary>保存维度加载页并登记为 GameRes 可寻址 Prefab。</summary>
    private static void SaveDimensionLoadingPrefab()
    {
        string prefabPath = LoadingRoot + RuntimeUIPrefabKeys.DimensionLoading + ".prefab";
        SaveNewPrefab(prefabPath, BuildDimensionLoading);
        EnsureRuntimePrefabAddressable(prefabPath);
    }

    /// <summary>保存镜头控制设置页并登记为 GameRes 可寻址 Prefab。</summary>
    private static void SaveCameraControlSettingsPrefab()
    {
        string prefabPath =
            SettingsPanelsRoot + RuntimeUIPrefabKeys.CameraControlSettings + ".prefab";
        SaveNewPrefab(prefabPath, BuildCameraControlSettings);
        EnsureRuntimePrefabAddressable(prefabPath);
    }

    /// <summary>保存左上角 HUD 设置并登记为可由 GameRes 查询的正式运行时 Prefab。</summary>
    private static void SaveCoordinateDisplaySettingsPrefab()
    {
        string prefabPath = SettingsPanelsRoot + RuntimeUIPrefabKeys.CoordinateDisplaySettings + ".prefab";
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
            FlatWorldUITheme.Apply(root.transform);
            PrepareSharedControlsForSave(root);
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
}
