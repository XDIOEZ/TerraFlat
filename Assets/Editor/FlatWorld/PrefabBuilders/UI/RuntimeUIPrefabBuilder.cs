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
    private const string PrefabRoot = "Assets/2_Prefabs/2-1_UI/";
    private const string MainMenuCoreRoot = PrefabRoot + "MainMenu/Core/";
    private const string DialogueRoot = PrefabRoot + "Gameplay/Dialogue/";
    private const string HudRoot = PrefabRoot + "Gameplay/HUD/";
    private const string LoadingRoot = PrefabRoot + "Gameplay/Loading/";
    /// <summary>运行时诊断 UI 的正式 Prefab 目录。</summary>
    private const string DebugRoot = PrefabRoot + "Gameplay/Debug/";
    private const string MobileRoot = PrefabRoot + "Gameplay/Mobile/";
    private const string BuffRoot = PrefabRoot + "Gameplay/Status/Buff/";
    private const string QuestRoot = PrefabRoot + "Gameplay/Status/Quest/";
    private const string SettingsPanelsRoot = PrefabRoot + "Settings/Panels/";
    private const string SettingsComponentsRoot = PrefabRoot + "Settings/Components/";
    private const string UIRootPrefab = "Assets/Resources/UI/UIRoot.prefab";
    /// <summary>承载启动阶段直接引用的管理器 Prefab 路径。</summary>
    private const string WorldManagerPrefab = "Assets/2_Prefabs/Core/Managers/WorldManager.prefab";
    private const string InventoryPanelsRoot = PrefabRoot + "Gameplay/Inventory/Panels/";
    private const string InventoryComponentsRoot = PrefabRoot + "Gameplay/Inventory/Components/";
    private const string NetworkPlayerPrefab = "Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab";
    private const string PlayerPrefab = "Assets/2_Prefabs/Gameplay/Player/Player.prefab";

    private static readonly Color Canvas = new Color(0.025f, 0.043f, 0.058f, 0.99f);
    private static readonly Color Surface = new Color(0.045f, 0.075f, 0.095f, 0.99f);
    private static readonly Color SurfaceRaised = new Color(0.094f, 0.212f, 0.247f, 0.99f);
    private static readonly Color Cream = new Color(0.95f, 0.91f, 0.81f, 1f);
    private static readonly Color Muted = new Color(0.66f, 0.72f, 0.73f, 1f);
    private static readonly Color Amber = new Color(0.83f, 0.49f, 0.23f, 1f);
    private static readonly Color Teal = new Color(0.26f, 0.61f, 0.57f, 1f);
    private static readonly Color Danger = new Color(0.66f, 0.31f, 0.27f, 1f);
    private static readonly Color Border = new Color(0.55f, 0.68f, 0.70f, 0.28f);
    // 手机右侧操作组统一使用同一套安全边距与间距，避免摇杆和按钮各自漂移。
    private const float MobileActionRightMargin = 76f;
    private const float MobileActionBottomMargin = 54f;
    private const float MobileAttackZoneSize = 230f;
    private const float MobileActionButtonSize = 112f;
    private const float MobileActionGap = 16f;
    private const float MobileActionGroupWidth = MobileActionButtonSize * 2f + MobileActionGap;
    private const float MobileActionGroupHeight = MobileAttackZoneSize + MobileActionGap + MobileActionButtonSize;
    // 主菜单设置使用更暖、更低亮度的独立背景，避免通用设置页的蓝绿色底板抢占视觉焦点。
    private static readonly Color MainMenuSettingsCanvas = new Color(0.052f, 0.031f, 0.026f, 1f);
    private static readonly Color MainMenuSettingsSurface = new Color(0.036f, 0.061f, 0.068f, 1f);
    private static readonly Color MainMenuSettingsSection = new Color(0.14f, 0.067f, 0.038f, 0.96f);

    private static TMP_FontAsset font;

    #endregion

    #region 重建入口

    /// <summary>只重建设置主面板与十页容器，不重建八个设置页资产。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Settings Action List UI")]
    public static void RebuildSettingsActionListUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        GameUIPrefabRebuilder.RebuildActionListUI();
        UpdateExistingPrefab(
            MainMenuCoreRoot + "UI_ActionList.prefab",
            ConfigureSettingsActionListPages);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化单面板设置主界面、顶部页签与十页容器。");
    }

    /// <summary>只刷新设置会话页的保存/退出入口与确认层，保留其余设置节点和本地文件 ID。</summary>
    [MenuItem("FlatWorld/UI/Refresh Settings Session Actions UI")]
    public static void RefreshSettingsSessionActionsUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        UpdateExistingPrefab(
            MainMenuCoreRoot + "UI_ActionList.prefab",
            ConfigureSettingsSessionActions);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已刷新设置会话页的保存退出入口与确认层。");
    }

    /// <summary>只重建设置入口和全部设置子分页，避免改动无关运行时 UI。</summary>
    [MenuItem("FlatWorld/UI/Rebuild All Settings Pages UI")]
    public static void RebuildAllSettingsPagesUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        GameUIPrefabRebuilder.RebuildActionListUI();
        Directory.CreateDirectory(SettingsPanelsRoot);
        Directory.CreateDirectory(SettingsComponentsRoot);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.AudioSettings + ".prefab", BuildAudioSettings);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.UISettings + ".prefab", BuildInterfaceSettings);
        SaveCameraControlSettingsPrefab();
        SaveCoordinateDisplaySettingsPrefab();
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.AutoSaveSettings + ".prefab", BuildAutoSaveSettings);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.WorldStreamingSettings + ".prefab", BuildWorldStreamingSettings);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.DifficultySettings + ".prefab", BuildDifficultySettings);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.InputBindingSettings + ".prefab", BuildInputBindingSettings);
        SaveNewPrefab(SettingsComponentsRoot + RuntimeUIPrefabKeys.InputBindingRow + ".prefab", BuildInputBindingRow);
        UpdateExistingPrefab(MainMenuCoreRoot + "UI_ActionList.prefab", ConfigureSettingsActionListPages);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化单面板设置入口与全部内嵌设置分页。");
    }

    [MenuItem("FlatWorld/UI/Rebuild Runtime Prefab UI")]
    public static void RebuildRuntimePrefabUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(SettingsPanelsRoot);
        Directory.CreateDirectory(SettingsComponentsRoot);
        Directory.CreateDirectory(DialogueRoot);
        Directory.CreateDirectory(HudRoot);
        Directory.CreateDirectory(LoadingRoot);
        Directory.CreateDirectory(DebugRoot);
        Directory.CreateDirectory(MobileRoot);
        Directory.CreateDirectory(BuffRoot);
        Directory.CreateDirectory(QuestRoot);

        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.AudioSettings + ".prefab", BuildAudioSettings);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.UISettings + ".prefab", BuildInterfaceSettings);
        SaveCameraControlSettingsPrefab();
        SaveCoordinateDisplaySettingsPrefab();
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.MainMenuSettings + ".prefab", BuildMainMenuSettings);
        SaveMainMenuExitConfirmationPrefab();
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
        SaveSaveStatusPrefab();
        SaveBuffStatusPrefabs();
        SaveQuestTrackerPrefabs();
        SaveMobileControlsPrefab();
        UpdateExistingPrefab(UIRootPrefab, EnsureSafeAreaRoot);

        UpdateExistingPrefab(MainMenuCoreRoot + "UI_ActionList.prefab", ConfigureSettingsActionListPages);
        UpdateExistingPrefab(InventoryPanelsRoot + "UI_Bag.prefab", AddInventorySortButton);
        UpdateExistingPrefab(InventoryComponentsRoot + "UI_Slot.prefab", AddCraftingPreviewLayers);
        UpdateExistingWorldPrefab(NetworkPlayerPrefab, AddNetworkPlayerNameLabel);
        UpdateExistingWorldPrefab(PlayerPrefab, EnsurePlayerBuffStatusHUD);
        UpdateExistingWorldPrefab(PlayerPrefab, EnsurePlayerQuestTrackerHUD);
        UpdateExistingWorldPrefab(PlayerPrefab, EnsurePlayerMobileControlsHUD);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化设置、设置列表分页、显示设置、世界加载、保存状态、Buff 状态、任务追踪、聊天、气泡、玩家坐标、背包整理、制作预览与联机玩家名称 Prefab。运行时不再创建这些视觉节点。");
    }

    /// <summary>只构建手机 HUD、安全区根节点和 Player 挂载，避免重写其它已有 UI Prefab。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Mobile Controls UI")]
    public static void RebuildMobileControlsUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(MobileRoot);
        SaveMobileControlsPrefab();
        UpdateExistingPrefab(UIRootPrefab, EnsureSafeAreaRoot);
        UpdateExistingWorldPrefab(PlayerPrefab, EnsurePlayerMobileControlsHUD);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化手机多点触控 HUD、安全区根节点，并挂载到 Player.prefab。");
    }

    /// <summary>只重建界面与镜头控制设置页，并同步设置入口分页。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Interface Settings UI")]
    public static void RebuildInterfaceSettingsUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

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
    [MenuItem("FlatWorld/UI/Rebuild Dimension Loading UI")]
    public static void RebuildDimensionLoadingUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(LoadingRoot);
        SaveDimensionLoadingPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化维度切换专属加载页并注册 Addressable。");
    }

    /// <summary>只重建启动资源加载面板，并把直接引用同步到 WorldManager。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Resource Loading UI")]
    public static void RebuildResourceLoadingUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(LoadingRoot);
        SaveResourceLoadingPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化启动资源加载 UGUI，并绑定到 WorldManager.prefab。");
    }

    /// <summary>只重建最早启动的运行时调试悬浮窗，并同步 WorldManager 直接引用。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Runtime Debug Overlay")]
    public static void RebuildRuntimeDebugOverlayUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(DebugRoot);
        SaveRuntimeDebugOverlayPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化运行时调试悬浮窗，并绑定到 WorldManager.prefab。");
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

        Directory.CreateDirectory(SettingsPanelsRoot);
        SaveNewPrefab(SettingsPanelsRoot + RuntimeUIPrefabKeys.WorldStreamingSettings + ".prefab",
            BuildWorldStreamingSettings);
        UpdateExistingPrefab(MainMenuCoreRoot + "UI_ActionList.prefab",
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

        Directory.CreateDirectory(SettingsPanelsRoot);
        SaveNewPrefab(
            SettingsPanelsRoot + RuntimeUIPrefabKeys.MainMenuSettings + ".prefab",
            BuildMainMenuSettings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化主菜单设置窗口 Prefab（大小、画质、语言占位项）。");
    }

    /// <summary>只重建主菜单退出确认弹窗，并登记为运行时可寻址 Prefab。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Main Menu Exit Confirmation UI")]
    public static void RebuildMainMenuExitConfirmationUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(MainMenuCoreRoot);
        SaveMainMenuExitConfirmationPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化主菜单退出确认 Prefab。");
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

        Directory.CreateDirectory(HudRoot);
        SavePlayerWorldCoordinatePrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化玩家世界坐标 HUD Prefab。");
    }

    /// <summary>只重建右上角保存状态提示，避免无关运行时 Prefab 被重写。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Save Status HUD")]
    public static void RebuildSaveStatusHUD()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(HudRoot);
        SaveSaveStatusPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化右上角保存状态提示 Prefab。");
    }

    /// <summary>只重建左侧中部 Buff 提示栏及玩家挂载组件，避免无关 Prefab 被重写。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Buff Status HUD")]
    public static void RebuildBuffStatusHUD()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(BuffRoot);
        SaveBuffStatusPrefabs();
        UpdateExistingWorldPrefab(PlayerPrefab, EnsurePlayerBuffStatusHUD);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化左侧中部 Buff 状态提示栏，并挂载到 Player.prefab。");
    }

    /// <summary>只重建右侧任务追踪栏及玩家挂载组件，避免无关运行时 Prefab 被重写。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Quest Tracker HUD")]
    public static void RebuildQuestTrackerHUD()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

        Directory.CreateDirectory(QuestRoot);
        SaveQuestTrackerPrefabs();
        UpdateExistingWorldPrefab(PlayerPrefab, EnsurePlayerQuestTrackerHUD);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化右侧任务追踪栏，并挂载到 Player.prefab。");
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

        Directory.CreateDirectory(SettingsPanelsRoot);
        SaveCoordinateDisplaySettingsPrefab();
        UpdateExistingPrefab(MainMenuCoreRoot + "UI_ActionList.prefab", ConfigureSettingsActionListPages);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Runtime UI] 已固化坐标显示设置 Prefab 与十页设置列表。");
    }

    /// <summary>重建按键绑定面板与动态绑定行，确保布局源和正式 Prefab 保持一致。</summary>
    [MenuItem("FlatWorld/UI/Rebuild Input Binding UI")]
    public static void RebuildInputBindingUI()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"[Runtime UI] 缺少统一字体：{FontPath}");
            return;
        }

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

    /// <summary>保存主菜单退出确认弹窗，并登记为 GameRes 可查询的正式运行时 Prefab。</summary>
    private static void SaveMainMenuExitConfirmationPrefab()
    {
        string prefabPath = MainMenuCoreRoot + RuntimeUIPrefabKeys.MainMenuExitConfirmation + ".prefab";
        SaveNewPrefab(prefabPath, BuildMainMenuExitConfirmation);
        EnsureRuntimePrefabAddressable(prefabPath);
    }

    /// <summary>保存坐标 HUD 并注册 Prefab 标签，确保 GameRes 能按键名加载。</summary>
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

    /// <summary>保存任务追踪面板和可复用条目 Prefab，并登记为运行时 Addressable。</summary>
    private static void SaveQuestTrackerPrefabs()
    {
        string panelPath = QuestRoot + RuntimeUIPrefabKeys.QuestTracker + ".prefab";
        string itemPath = QuestRoot + RuntimeUIPrefabKeys.QuestTrackerItem + ".prefab";
        SaveNewPrefab(panelPath, BuildQuestTrackerHUD);
        SaveNewPrefab(itemPath, BuildQuestTrackerItem);
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

    /// <summary>保存坐标显示设置并登记为可由 GameRes 查询的正式运行时 Prefab。</summary>
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

    /// <summary>构建固定在屏幕左上角的非交互坐标文本，只保留玩家需要读取的坐标值。</summary>
    private static GameObject BuildPlayerWorldCoordinateHUD()
    {
        GameObject root = CreateUIObject(RuntimeUIPrefabKeys.PlayerWorldCoordinate, null);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        SetTopLeft(rootRect, 28f, 28f, 240f, 30f);

        TextMeshProUGUI coordinates = CreateText("坐标文本", root.transform, "X  +0.0    Y  +0.0", 16f, Cream);
        coordinates.fontStyle = FontStyles.Bold;
        coordinates.enableWordWrapping = false;
        coordinates.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(coordinates.rectTransform, 0f, 0f, 240f, 30f);

        return root;
    }

    /// <summary>构建不拦截输入的右上角保存状态卡片，默认隐藏并由 GameSaveStatusHUD 控制显隐。</summary>
    private static GameObject BuildSaveStatusHUD()
    {
        GameObject root = CreateUIObject(RuntimeUIPrefabKeys.SaveStatus, null, typeof(CanvasGroup));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(1f, 1f);
        rootRect.anchorMax = new Vector2(1f, 1f);
        rootRect.pivot = new Vector2(1f, 1f);
        rootRect.anchoredPosition = new Vector2(-32f, -118f);
        rootRect.sizeDelta = new Vector2(260f, 52f);

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        Image background = CreateImage("背景", root.transform, new Color(0.025f, 0.043f, 0.058f, 0.94f));
        background.raycastTarget = false;
        Stretch(background.rectTransform);
        AddOutline(background, new Color(0.83f, 0.49f, 0.23f, 0.48f));

        Image accent = CreateImage("强调线", root.transform, Amber);
        accent.raycastTarget = false;
        RectTransform accentRect = accent.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(4f, -14f);

        TextMeshProUGUI status = CreateText("保存状态文本", root.transform, "正在保存…", 16f, Cream);
        status.fontStyle = FontStyles.Bold;
        status.alignment = TextAlignmentOptions.MidlineLeft;
        status.enableWordWrapping = false;
        status.overflowMode = TextOverflowModes.Ellipsis;
        SetTopStretch(status.rectTransform, new Vector2(18f, 0f), new Vector2(-14f, 0f));
        return root;
    }

    /// <summary>构建屏幕左侧中部上方的非交互 Buff 状态栏，避开手机奔跑按钮，超出高度时由 ScrollRect 裁剪。</summary>
    private static GameObject BuildBuffStatusHUD()
    {
        GameObject root = CreateUIObject(RuntimeUIPrefabKeys.BuffStatus, null, typeof(CanvasGroup));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 0.5f);
        rootRect.anchorMax = new Vector2(0f, 0.5f);
        rootRect.pivot = new Vector2(0f, 0f);
        rootRect.anchoredPosition = new Vector2(32f, 60f);
        rootRect.sizeDelta = new Vector2(160f, 106f);

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        Image background = CreateImage("背景", root.transform, new Color(0.025f, 0.043f, 0.058f, 0.92f));
        background.raycastTarget = false;
        Stretch(background.rectTransform);
        AddOutline(background, new Color(0.83f, 0.49f, 0.23f, 0.46f));

        Image accent = CreateImage("强调线", root.transform, Amber);
        accent.raycastTarget = false;
        RectTransform accentRect = accent.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(4f, -18f);

        TextMeshProUGUI title = CreateText("标题", root.transform, "状态效果 / BUFFS", 13f, Amber);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 1f;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(title.rectTransform, 12f, 9f, 112f, 18f);

        TextMeshProUGUI count = CreateText("数量文本", root.transform, "0", 13f, Muted);
        count.alignment = TextAlignmentOptions.MidlineRight;
        count.enableWordWrapping = false;
        count.overflowMode = TextOverflowModes.Ellipsis;
        count.rectTransform.anchorMin = new Vector2(1f, 1f);
        count.rectTransform.anchorMax = new Vector2(1f, 1f);
        count.rectTransform.pivot = new Vector2(1f, 1f);
        count.rectTransform.anchoredPosition = new Vector2(-10f, -9f);
        count.rectTransform.sizeDelta = new Vector2(28f, 18f);

        GameObject scrollRoot = CreateUIObject("内容列表", root.transform, typeof(ScrollRect));
        RectTransform scrollRect = scrollRoot.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0f, 0f);
        scrollRect.anchorMax = new Vector2(1f, 1f);
        scrollRect.offsetMin = new Vector2(8f, 8f);
        scrollRect.offsetMax = new Vector2(-8f, -36f);

        GameObject viewport = CreateUIObject("Viewport", scrollRoot.transform, typeof(RectMask2D));
        Stretch(viewport.GetComponent<RectTransform>());

        GameObject content = CreateUIObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 7f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollRoot.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 26f;
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRect;

        TextMeshProUGUI empty = CreateText("空状态文本", root.transform, "暂无状态", 11f, Muted);
        empty.alignment = TextAlignmentOptions.Center;
        empty.enableWordWrapping = false;
        empty.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(empty.rectTransform, 8f, 42f, 144f, 24f);

        return root;
    }

    /// <summary>构建 Buff 行：统一占位图标、名称和剩余时间，不依赖具体 Buff 美术资源。</summary>
    private static GameObject BuildBuffStatusItem()
    {
        GameObject root = CreateUIObject(
            RuntimeUIPrefabKeys.BuffStatusItem,
            null,
            typeof(Image),
            typeof(BuffStatusRowView));
        LayoutElement rowElement = root.AddComponent<LayoutElement>();
        rowElement.preferredHeight = 31f;

        Image background = root.GetComponent<Image>();
        background.color = new Color(0.055f, 0.105f, 0.12f, 0.92f);
        background.raycastTarget = false;
        AddOutline(background, new Color(0.55f, 0.68f, 0.70f, 0.22f));

        HorizontalLayoutGroup rowLayout = root.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(5, 5, 4, 4);
        rowLayout.spacing = 5f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        GameObject iconObject = CreateUIObject("占位图标", root.transform, typeof(Image));
        LayoutElement iconElement = iconObject.AddComponent<LayoutElement>();
        iconElement.preferredWidth = 23f;
        iconElement.preferredHeight = 23f;
        Image icon = iconObject.GetComponent<Image>();
        icon.color = new Color(0.26f, 0.61f, 0.57f, 0.85f);
        icon.raycastTarget = false;
        AddOutline(icon, new Color(0.95f, 0.91f, 0.81f, 0.42f));

        TextMeshProUGUI placeholder = CreateText("占位符文本", iconObject.transform, "?", 13f, Cream);
        placeholder.fontStyle = FontStyles.Bold;
        placeholder.alignment = TextAlignmentOptions.Center;
        placeholder.enableWordWrapping = false;
        Stretch(placeholder.rectTransform);

        GameObject info = CreateUIObject("状态信息", root.transform);
        LayoutElement infoElement = info.AddComponent<LayoutElement>();
        infoElement.flexibleWidth = 1f;
        VerticalLayoutGroup infoLayout = info.AddComponent<VerticalLayoutGroup>();
        infoLayout.spacing = 1f;
        infoLayout.childAlignment = TextAnchor.MiddleLeft;
        infoLayout.childControlWidth = true;
        infoLayout.childControlHeight = true;
        infoLayout.childForceExpandWidth = true;
        infoLayout.childForceExpandHeight = false;

        TextMeshProUGUI name = CreateText("状态名称", info.transform, "Buff", 10f, Cream);
        name.fontStyle = FontStyles.Bold;
        name.enableWordWrapping = false;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.gameObject.AddComponent<LayoutElement>().preferredHeight = 13f;

        TextMeshProUGUI remaining = CreateText("剩余时间", info.transform, "剩余 30s", 8f, Muted);
        remaining.enableWordWrapping = false;
        remaining.overflowMode = TextOverflowModes.Ellipsis;
        remaining.gameObject.AddComponent<LayoutElement>().preferredHeight = 10f;

        return root;
    }

    /// <summary>构建屏幕右侧的可折叠任务追踪卡，最多显示四条进行中或待领取任务。</summary>
    private static GameObject BuildQuestTrackerHUD()
    {
        GameObject root = CreateUIObject(RuntimeUIPrefabKeys.QuestTracker, null, typeof(CanvasGroup));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(1f, 1f);
        rootRect.anchorMax = new Vector2(1f, 1f);
        rootRect.pivot = new Vector2(1f, 1f);
        rootRect.anchoredPosition = new Vector2(-24f, -168f);
        rootRect.sizeDelta = new Vector2(300f, 300f);

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        Image background = CreateImage("背景", root.transform, new Color(0.025f, 0.043f, 0.058f, 0.92f));
        background.raycastTarget = false;
        Stretch(background.rectTransform);
        AddOutline(background, new Color(0.83f, 0.49f, 0.23f, 0.46f));

        Image accent = CreateImage("强调线", root.transform, Amber);
        accent.raycastTarget = false;
        RectTransform accentRect = accent.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(4f, -18f);

        TextMeshProUGUI title = CreateText("标题", root.transform, "任务追踪 / QUESTS", 13f, Amber);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 1f;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(title.rectTransform, 16f, 10f, 190f, 22f);

        TextMeshProUGUI count = CreateText("数量文本", root.transform, "0", 13f, Muted);
        count.alignment = TextAlignmentOptions.MidlineRight;
        count.enableWordWrapping = false;
        count.rectTransform.anchorMin = new Vector2(1f, 1f);
        count.rectTransform.anchorMax = new Vector2(1f, 1f);
        count.rectTransform.pivot = new Vector2(1f, 1f);
        count.rectTransform.anchoredPosition = new Vector2(-74f, -10f);
        count.rectTransform.sizeDelta = new Vector2(34f, 22f);

        Button toggleButton = CreateButton("任务面板开关按钮", root.transform, "收起", 52f, 26f, false);
        SetTopRight(toggleButton.GetComponent<RectTransform>(), 14f, 8f, 52f, 26f);

        GameObject listRoot = CreateUIObject("内容列表", root.transform);
        SetTopLeft(listRoot.GetComponent<RectTransform>(), 16f, 48f, 268f, 234f);

        GameObject viewport = CreateUIObject("Viewport", listRoot.transform, typeof(RectMask2D));
        Stretch(viewport.GetComponent<RectTransform>());

        GameObject content = CreateUIObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 8f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI empty = CreateText("空状态文本", root.transform, "暂无进行中的任务", 13f, Muted);
        empty.alignment = TextAlignmentOptions.Center;
        empty.enableWordWrapping = false;
        empty.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(empty.rectTransform, 16f, 150f, 268f, 24f);

        return root;
    }

    /// <summary>构建固定高度任务条目，展示标题、说明、状态、目标摘要和进度条。</summary>
    private static GameObject BuildQuestTrackerItem()
    {
        GameObject root = CreateUIObject(
            RuntimeUIPrefabKeys.QuestTrackerItem,
            null,
            typeof(Image),
            typeof(QuestTrackerRowView));
        LayoutElement rowElement = root.AddComponent<LayoutElement>();
        rowElement.preferredHeight = 72f;

        Image background = root.GetComponent<Image>();
        background.color = new Color(0.055f, 0.105f, 0.12f, 0.92f);
        background.raycastTarget = false;
        AddOutline(background, new Color(0.55f, 0.68f, 0.70f, 0.22f));

        Image statusLine = CreateImage("状态线", root.transform, Amber);
        statusLine.raycastTarget = false;
        RectTransform statusLineRect = statusLine.rectTransform;
        statusLineRect.anchorMin = new Vector2(0f, 0f);
        statusLineRect.anchorMax = new Vector2(0f, 1f);
        statusLineRect.pivot = new Vector2(0f, 0.5f);
        statusLineRect.anchoredPosition = new Vector2(4f, 0f);
        statusLineRect.sizeDelta = new Vector2(3f, -16f);

        TextMeshProUGUI title = CreateText("任务标题", root.transform, "Quest", 15f, Cream);
        title.fontStyle = FontStyles.Bold;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(title.rectTransform, 14f, 7f, 184f, 19f);

        TextMeshProUGUI status = CreateText("任务状态", root.transform, "ACTIVE", 11f, Amber);
        status.fontStyle = FontStyles.Bold;
        status.alignment = TextAlignmentOptions.MidlineRight;
        status.enableWordWrapping = false;
        status.overflowMode = TextOverflowModes.Ellipsis;
        status.rectTransform.anchorMin = new Vector2(1f, 1f);
        status.rectTransform.anchorMax = new Vector2(1f, 1f);
        status.rectTransform.pivot = new Vector2(1f, 1f);
        status.rectTransform.anchoredPosition = new Vector2(-10f, -7f);
        status.rectTransform.sizeDelta = new Vector2(64f, 19f);

        TextMeshProUGUI description = CreateText("任务说明", root.transform, "Description", 11.5f, Muted);
        description.enableWordWrapping = false;
        description.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(description.rectTransform, 14f, 27f, 240f, 18f);

        TextMeshProUGUI objective = CreateText("目标文本", root.transform, "Objective  0/1", 11f, Cream);
        objective.enableWordWrapping = false;
        objective.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(objective.rectTransform, 14f, 47f, 240f, 17f);

        Image progressBackground = CreateImage(
            "进度背景",
            root.transform,
            new Color(0.02f, 0.035f, 0.045f, 0.92f));
        progressBackground.raycastTarget = false;
        RectTransform progressBackgroundRect = progressBackground.rectTransform;
        progressBackgroundRect.anchorMin = new Vector2(0f, 0f);
        progressBackgroundRect.anchorMax = new Vector2(1f, 0f);
        progressBackgroundRect.pivot = new Vector2(0.5f, 0f);
        progressBackgroundRect.offsetMin = new Vector2(14f, 8f);
        progressBackgroundRect.offsetMax = new Vector2(-12f, 12f);

        Image progressFill = CreateImage("进度填充", progressBackground.transform, Amber);
        progressFill.raycastTarget = false;
        progressFill.type = Image.Type.Filled;
        progressFill.fillMethod = Image.FillMethod.Horizontal;
        progressFill.fillOrigin = 0;
        progressFill.fillAmount = 0f;
        Stretch(progressFill.rectTransform);

        return root;
    }

    /// <summary>构建独立运行的日志悬浮窗，折叠入口和展开页都约束在设备安全区内。</summary>
    private static GameObject BuildRuntimeDebugOverlay()
    {
        GameObject root = new GameObject(
            RuntimeUIPrefabKeys.RuntimeDebugOverlay,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);
        // 独立 Canvas 不经过 UIManager 的打开动画，必须以可见缩放直接启动。
        rootRect.localScale = Vector3.one;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = UIManager.GlobalOverlaySortingOrder + 20;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        GameObject safeArea = CreateUIObject("调试安全区", root.transform, typeof(SafeAreaRectController));
        Stretch(safeArea.GetComponent<RectTransform>());

        Button toggleButton = CreateButton("调试悬浮按钮", safeArea.transform, "日志  0", 150f, 62f, false);
        SetTopRight(toggleButton.GetComponent<RectTransform>(), 22f, 22f, 150f, 62f);
        toggleButton.GetComponent<Image>().color = new Color(0.12f, 0.22f, 0.25f, 0.98f);
        SetButtonLabelSize(toggleButton, 18f);
        TextMeshProUGUI toggleLabel = toggleButton.GetComponentInChildren<TextMeshProUGUI>(true);
        toggleLabel.gameObject.name = "悬浮日志数量";

        GameObject panel = CreateUIObject("调试页面", safeArea.transform, typeof(Image), typeof(Shadow));
        SetTopRight(panel.GetComponent<RectTransform>(), 22f, 98f, 980f, 760f);
        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.018f, 0.03f, 0.04f, 0.985f);
        panelImage.raycastTarget = true;
        AddOutline(panelImage, Amber);
        Shadow shadow = panel.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
        shadow.effectDistance = new Vector2(8f, -8f);

        TextMeshProUGUI title = CreateText("调试页标题", panel.transform, "运行日志 / DEBUG", 25f, Cream);
        title.fontStyle = FontStyles.Bold;
        title.enableWordWrapping = false;
        SetTopLeft(title.rectTransform, 24f, 18f, 230f, 38f);

        TextMeshProUGUI summary = CreateText("日志数量摘要", panel.transform, "共 0 条    错误 0    警告 0", 16f, Muted);
        summary.enableWordWrapping = false;
        summary.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(summary.rectTransform, 265f, 22f, 220f, 30f);

        TextMeshProUGUI copyCountLabel = CreateText("复制槽位标题", panel.transform, "复制槽位", 15f, Muted);
        copyCountLabel.enableWordWrapping = false;
        SetTopLeft(copyCountLabel.rectTransform, 493f, 23f, 70f, 30f);

        TMP_InputField copyEntryCountInput = CreateInputField("复制日志条数输入框", panel.transform, "50");
        copyEntryCountInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        copyEntryCountInput.characterLimit = 3;
        SetTopLeft(copyEntryCountInput.GetComponent<RectTransform>(), 570f, 14f, 62f, 48f);

        Button closeButton = CreateButton("关闭调试页按钮", panel.transform, "关闭", 96f, 48f, false);
        SetTopRight(closeButton.GetComponent<RectTransform>(), 18f, 14f, 96f, 48f);
        Button clearButton = CreateButton("清空日志按钮", panel.transform, "清空", 96f, 48f, false);
        SetTopRight(clearButton.GetComponent<RectTransform>(), 124f, 14f, 96f, 48f);
        Button copyButton = CreateButton("复制日志按钮", panel.transform, "复制", 108f, 48f, true);
        SetTopRight(copyButton.GetComponent<RectTransform>(), 230f, 14f, 108f, 48f);
        SetButtonLabelSize(closeButton, 17f);
        SetButtonLabelSize(clearButton, 17f);
        SetButtonLabelSize(copyButton, 17f);

        Image divider = CreateImage("页头分隔线", panel.transform, new Color(0.83f, 0.49f, 0.23f, 0.72f));
        divider.raycastTarget = false;
        RectTransform dividerRect = divider.rectTransform;
        dividerRect.anchorMin = new Vector2(0f, 1f);
        dividerRect.anchorMax = new Vector2(1f, 1f);
        dividerRect.pivot = new Vector2(0.5f, 1f);
        dividerRect.anchoredPosition = new Vector2(0f, -76f);
        dividerRect.sizeDelta = new Vector2(-40f, 2f);

        GameObject scrollRoot = CreateUIObject("日志滚动区", panel.transform, typeof(Image), typeof(ScrollRect));
        RectTransform scrollRootRect = scrollRoot.GetComponent<RectTransform>();
        scrollRootRect.anchorMin = Vector2.zero;
        scrollRootRect.anchorMax = Vector2.one;
        scrollRootRect.offsetMin = new Vector2(22f, 62f);
        scrollRootRect.offsetMax = new Vector2(-22f, -90f);
        Image scrollBackground = scrollRoot.GetComponent<Image>();
        scrollBackground.color = new Color(0.006f, 0.012f, 0.016f, 0.96f);
        scrollBackground.raycastTarget = true;
        AddOutline(scrollBackground, Border);

        GameObject viewport = CreateUIObject("Viewport", scrollRoot.transform, typeof(RectMask2D));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMin = new Vector2(14f, 12f);
        viewportRect.offsetMax = new Vector2(-14f, -12f);

        TextMeshProUGUI logText = CreateText(
            "日志正文",
            viewport.transform,
            "暂无运行日志。",
            15f,
            new Color(0.82f, 0.86f, 0.84f, 1f));
        RectTransform logRect = logText.rectTransform;
        logRect.anchorMin = new Vector2(0f, 1f);
        logRect.anchorMax = new Vector2(1f, 1f);
        logRect.pivot = new Vector2(0.5f, 1f);
        logRect.anchoredPosition = Vector2.zero;
        logRect.sizeDelta = Vector2.zero;
        logText.alignment = TextAlignmentOptions.TopLeft;
        logText.enableWordWrapping = true;
        logText.overflowMode = TextOverflowModes.Overflow;
        logText.richText = false;
        logText.lineSpacing = 4f;
        ContentSizeFitter contentFitter = logText.gameObject.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scrollRect = scrollRoot.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = true;
        scrollRect.decelerationRate = 0.12f;
        scrollRect.scrollSensitivity = 34f;
        scrollRect.viewport = viewportRect;
        scrollRect.content = logRect;

        TextMeshProUGUI status = CreateText(
            "调试操作状态",
            panel.transform,
            "复制时将取最近 50 个去重日志槽位。",
            14f,
            Muted);
        status.enableWordWrapping = false;
        status.overflowMode = TextOverflowModes.Ellipsis;
        status.rectTransform.anchorMin = new Vector2(0f, 0f);
        status.rectTransform.anchorMax = new Vector2(1f, 0f);
        status.rectTransform.pivot = new Vector2(0.5f, 0f);
        status.rectTransform.anchoredPosition = new Vector2(0f, 17f);
        status.rectTransform.sizeDelta = new Vector2(-44f, 30f);

        RuntimeDebugOverlay controller = root.AddComponent<RuntimeDebugOverlay>();
        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("debugPanel").objectReferenceValue = panel;
        serializedController.FindProperty("toggleButton").objectReferenceValue = toggleButton;
        serializedController.FindProperty("copyButton").objectReferenceValue = copyButton;
        serializedController.FindProperty("clearButton").objectReferenceValue = clearButton;
        serializedController.FindProperty("closeButton").objectReferenceValue = closeButton;
        serializedController.FindProperty("toggleLabel").objectReferenceValue = toggleLabel;
        serializedController.FindProperty("summaryText").objectReferenceValue = summary;
        serializedController.FindProperty("logText").objectReferenceValue = logText;
        serializedController.FindProperty("statusText").objectReferenceValue = status;
        serializedController.FindProperty("logScrollRect").objectReferenceValue = scrollRect;
        serializedController.FindProperty("copyEntryCountInput").objectReferenceValue = copyEntryCountInput;
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        panel.SetActive(false);
        return root;
    }

    /// <summary>构建主菜单启动阶段使用的资源加载面板，确保长错误信息在手机上可读。</summary>
    private static GameObject BuildResourceLoading()
    {
        GameObject root = new GameObject(
            RuntimeUIPrefabKeys.ResourceLoading,
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
        canvas.sortingOrder = UIManager.GlobalOverlaySortingOrder + 10;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        root.AddComponent<FullScreenRectController>();

        Image blocker = root.GetComponent<Image>();
        blocker.color = new Color(0.005f, 0.012f, 0.018f, 0.42f);
        blocker.raycastTarget = true;

        GameObject card = CreateUIObject("资源加载内容", root.transform, typeof(Image), typeof(Shadow));
        SetCentered(card.GetComponent<RectTransform>(), Vector2.zero, new Vector2(760f, 340f));
        Image cardImage = card.GetComponent<Image>();
        cardImage.color = new Color(0.025f, 0.043f, 0.058f, 0.98f);
        cardImage.raycastTarget = true;
        AddOutline(cardImage, Amber);
        Shadow shadow = card.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
        shadow.effectDistance = new Vector2(8f, -8f);

        VerticalLayoutGroup layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(42, 42, 30, 28);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText(GameRes.ResourceLoadingTitleKey, card.transform, "正在准备游戏", 28f, Cream);
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;

        TextMeshProUGUI status = CreateText(GameRes.ResourceLoadingStatusKey, card.transform, "正在加载资源…", 18f, Muted);
        status.alignment = TextAlignmentOptions.Center;
        status.enableWordWrapping = true;
        status.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement statusLayout = status.gameObject.AddComponent<LayoutElement>();
        statusLayout.preferredHeight = 112f;
        statusLayout.minHeight = 88f;

        Slider progress = CreateSlider(GameRes.ResourceLoadingProgressKey, card.transform);
        progress.interactable = false;
        progress.value = 0f;
        LayoutElement progressLayout = progress.GetComponent<LayoutElement>();
        progressLayout.flexibleWidth = 0f;
        progressLayout.preferredWidth = 650f;
        progressLayout.preferredHeight = 30f;
        Image progressFill = progress.fillRect.GetComponent<Image>();
        progressFill.gameObject.name = GameRes.ResourceLoadingProgressFillKey;
        progressFill.color = Teal;

        TextMeshProUGUI percent = CreateText(GameRes.ResourceLoadingProgressTextKey, card.transform, "0%", 16f, Teal);
        percent.alignment = TextAlignmentOptions.Center;
        percent.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

        TextMeshProUGUI hint = CreateText("资源加载提示", card.transform, "若加载失败，这里会显示完整原因。", 14f, Muted);
        hint.alignment = TextAlignmentOptions.Center;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        return root;
    }

    /// <summary>构建进入世界阶段的全屏加载面板。</summary>
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
        canvas.sortingOrder = UIManager.GlobalOverlaySortingOrder;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        root.AddComponent<FullScreenRectController>();

        Image overlay = root.GetComponent<Image>();
        // 世界加载期间完全遮挡玩法 UI，避免快捷栏、摇杆和按钮透出。
        overlay.color = new Color(0.012f, 0.022f, 0.028f, 1f);
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

    private static GameObject BuildDimensionLoading()
    {
        GameObject root = new GameObject(
            RuntimeUIPrefabKeys.DimensionLoading,
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
        canvas.sortingOrder = UIManager.GlobalOverlaySortingOrder;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        root.AddComponent<FullScreenRectController>();

        Image blocker = root.GetComponent<Image>();
        blocker.color = Color.clear;
        blocker.raycastTarget = true;

        Image background = CreateImage(GameManager.DimensionLoadingBackgroundKey, root.transform,
            new Color(0.055f, 0.055f, 0.065f, 1f));
        background.raycastTarget = false;
        Stretch(background.rectTransform);

        Image texture = CreateImage(GameManager.DimensionLoadingTextureKey, root.transform,
            new Color(1f, 1f, 1f, 0.16f));
        texture.raycastTarget = false;
        texture.type = Image.Type.Tiled;
        Stretch(texture.rectTransform);

        Image shade = CreateImage("像素暗角", root.transform, new Color(0.01f, 0.012f, 0.015f, 0.46f));
        shade.raycastTarget = false;
        Stretch(shade.rectTransform);

        GameObject card = CreateUIObject("维度加载内容", root.transform, typeof(Image), typeof(Shadow));
        SetCentered(card.GetComponent<RectTransform>(), Vector2.zero, new Vector2(720f, 560f));
        Image cardImage = card.GetComponent<Image>();
        cardImage.color = new Color(0.025f, 0.03f, 0.035f, 0.94f);
        cardImage.raycastTarget = false;
        AddOutline(cardImage, new Color(0.95f, 0.62f, 0.22f, 0.75f));
        Shadow shadow = card.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
        shadow.effectDistance = new Vector2(8f, -8f);

        TextMeshProUGUI eyebrow = CreateText("维度加载标题", card.transform, "维度跃迁", 17f, Muted);
        eyebrow.alignment = TextAlignmentOptions.Center;
        SetCentered(eyebrow.rectTransform, new Vector2(0f, 218f), new Vector2(620f, 34f));

        Image iconFrame = CreateImage("维度图标边框", card.transform, new Color(0.08f, 0.085f, 0.09f, 1f));
        iconFrame.raycastTarget = false;
        SetCentered(iconFrame.rectTransform, new Vector2(0f, 112f), new Vector2(132f, 132f));
        AddOutline(iconFrame, Border);

        Image icon = CreateImage(GameManager.DimensionLoadingIconKey, iconFrame.transform, Color.white);
        icon.raycastTarget = false;
        icon.preserveAspect = true;
        Stretch(icon.rectTransform);
        icon.rectTransform.offsetMin = new Vector2(14f, 14f);
        icon.rectTransform.offsetMax = new Vector2(-14f, -14f);

        TextMeshProUGUI dimensionName = CreateText(GameManager.DimensionLoadingNameKey, card.transform, "地下矿洞", 34f, Amber);
        dimensionName.fontStyle = FontStyles.Bold;
        dimensionName.alignment = TextAlignmentOptions.Center;
        SetCentered(dimensionName.rectTransform, new Vector2(0f, 18f), new Vector2(620f, 56f));

        TextMeshProUGUI status = CreateText(GameManager.DimensionLoadingStatusKey, card.transform, "正在创建目标维度…", 18f, Cream);
        status.alignment = TextAlignmentOptions.Center;
        SetCentered(status.rectTransform, new Vector2(0f, -38f), new Vector2(620f, 34f));

        Slider progress = CreateSlider(GameManager.DimensionLoadingProgressKey, card.transform);
        progress.interactable = false;
        progress.value = 0.48f;
        SetCentered(progress.GetComponent<RectTransform>(), new Vector2(0f, -91f), new Vector2(580f, 30f));
        Transform progressFill = progress.transform.Find("Fill Area/Fill");
        if (progressFill != null)
            progressFill.name = GameManager.DimensionLoadingProgressFillKey;

        TextMeshProUGUI percent = CreateText(GameManager.DimensionLoadingProgressTextKey, card.transform, "48%", 17f, Amber);
        percent.alignment = TextAlignmentOptions.Center;
        SetCentered(percent.rectTransform, new Vector2(0f, -133f), new Vector2(180f, 28f));

        TextMeshProUGUI hint = CreateText(GameManager.DimensionLoadingHintKey, card.transform,
            "维度稳定后将自动抵达。", 14f, Muted);
        hint.alignment = TextAlignmentOptions.Center;
        SetCentered(hint.rectTransform, new Vector2(0f, -192f), new Vector2(620f, 30f));

        Image lowerLine = CreateImage("底部像素强调线", card.transform, Amber);
        lowerLine.raycastTarget = false;
        SetCentered(lowerLine.rectTransform, new Vector2(0f, -238f), new Vector2(580f, 4f));
        return root;
    }

    #endregion

    #region 设置面板

    /// <summary>构建可嵌入主设置内容区的音量分页。</summary>
    private static GameObject BuildAudioSettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.AudioSettings,
            out Transform content);
        CreateSettingsHeader(content, "音量调节");
        CreateSettingsHint(content, "主音量控制全部声音；其他通道可以单独调整。设置会自动保存。", 42f);

        CreateSliderRow(content, "主音量", "MasterVolume");
        CreateSliderRow(content, "音乐音量", "MusicVolume");
        CreateSliderRow(content, "音效音量", "SfxVolume");
        CreateSliderRow(content, "UI 音量", "UIVolume");
        CreateSliderRow(content, "环境音量", "AmbientVolume");
        CreateSliderRow(content, "语音音量", "VoiceVolume");

        Transform footer = CreateFooter(content);
        CreateSettingsButton("恢复默认按钮", footer, "恢复默认", 132f, 42f, false);
        return root;
    }

    /// <summary>构建界面缩放、安全区、移动摇杆模式与触控分区设置页。</summary>
    private static GameObject BuildInterfaceSettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.UISettings,
            out Transform content);
        CreateSettingsHeader(content, "界面设置");
        CreateSettingsHint(
            content,
            "左右触控区决定移动和普通指向摇杆的响应范围；中间区域保留给后续操作。调整会立即保存。",
            54f);

        GameObject scaleRow = CreateRow("界面缩放行", content, 52f);
        CreateRowLabel(scaleRow.transform, "界面缩放", 112f);
        Slider slider = CreateSlider("界面缩放", scaleRow.transform);
        slider.minValue = UIUserSettings.MinimumScale;
        slider.maxValue = UIUserSettings.MaximumScale;
        slider.value = 1f;
        TextMeshProUGUI valueText = CreateText("界面缩放数值", scaleRow.transform, "100%", 16f, Amber);
        valueText.alignment = TextAlignmentOptions.MidlineRight;
        valueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

        GameObject safeAreaRow = CreateRow("安全区域适配行", content, 48f);
        TextMeshProUGUI safeLabel = CreateText("安全区域说明", safeAreaRow.transform, "适配屏幕安全区域", 17f, Cream);
        safeLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateToggle("安全区域适配", safeAreaRow.transform);

        GameObject joystickRow = CreateRow("移动摇杆模式行", content, 48f);
        TextMeshProUGUI joystickLabel = CreateText(
            "移动摇杆模式说明",
            joystickRow.transform,
            "浮动移动摇杆（关闭则固定）",
            17f,
            Cream);
        joystickLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Toggle joystickToggle = CreateToggle("浮动移动摇杆", joystickRow.transform);
        joystickToggle.isOn = true;

        GameObject leftZoneRow = CreateRow("左侧触控区比例行", content, 52f);
        CreateRowLabel(leftZoneRow.transform, "左侧触控区", 128f);
        Slider leftZoneSlider = CreateSlider("左侧触控区比例", leftZoneRow.transform);
        leftZoneSlider.minValue = UIUserSettings.MinimumControlZoneRatio;
        leftZoneSlider.maxValue = UIUserSettings.MaximumControlZoneRatio;
        leftZoneSlider.value = UIUserSettings.DefaultLeftControlZoneRatio;
        TextMeshProUGUI leftZoneValue = CreateText(
            "左侧触控区数值",
            leftZoneRow.transform,
            "33%",
            16f,
            Amber);
        leftZoneValue.alignment = TextAlignmentOptions.MidlineRight;
        leftZoneValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

        GameObject rightZoneRow = CreateRow("右侧触控区比例行", content, 52f);
        CreateRowLabel(rightZoneRow.transform, "右侧触控区", 128f);
        Slider rightZoneSlider = CreateSlider("右侧触控区比例", rightZoneRow.transform);
        rightZoneSlider.minValue = UIUserSettings.MinimumControlZoneRatio;
        rightZoneSlider.maxValue = UIUserSettings.MaximumControlZoneRatio;
        rightZoneSlider.value = UIUserSettings.DefaultRightControlZoneRatio;
        TextMeshProUGUI rightZoneValue = CreateText(
            "右侧触控区数值",
            rightZoneRow.transform,
            "33%",
            16f,
            Amber);
        rightZoneValue.alignment = TextAlignmentOptions.MidlineRight;
        rightZoneValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 58f;

        TextMeshProUGUI zoneStatus = CreateText(
            "触控区域比例文本",
            content,
            "触控区域比例：左 33%｜中 34%｜右 33%",
            16f,
            Amber);
        zoneStatus.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        TextMeshProUGUI status = CreateText("状态文本", content, "安全区域适配：开启（推荐）", 16f, Muted);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        Transform footer = CreateFooter(content);
        CreateSettingsButton("恢复默认按钮", footer, "恢复默认", 132f, 42f, false);
        return root;
    }

    /// <summary>构建可滚动的内嵌镜头控制页。</summary>
    private static GameObject BuildCameraControlSettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.CameraControlSettings,
            out Transform content);
        CreateSettingsHeader(content, "镜头控制");
        CreateSettingsHint(
            content,
            "双指缩放默认关闭。镜头前探正值为提前跟随，负值为惯性；缩放影响系数为正时拉远会增强预测，为负时会减弱。",
            64f);

        GameObject pinchZoomRow = CreateRow("双指缩放行", content, 48f);
        TextMeshProUGUI pinchZoomLabel = CreateText(
            "双指缩放说明",
            pinchZoomRow.transform,
            "双指缩放（关闭则禁用）",
            17f,
            Cream);
        pinchZoomLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Toggle pinchZoomToggle = CreateToggle("双指缩放", pinchZoomRow.transform);
        pinchZoomToggle.isOn = false;

        GameObject lookaheadRow = CreateRow("镜头前探行", content, 52f);
        CreateRowLabel(lookaheadRow.transform, "镜头前探", 128f);
        Slider lookaheadSlider = CreateSlider("镜头前探", lookaheadRow.transform);
        lookaheadSlider.minValue = CameraUserSettings.MinimumLookahead;
        lookaheadSlider.maxValue = CameraUserSettings.MaximumLookahead;
        lookaheadSlider.value = CameraUserSettings.DefaultLookahead;
        TextMeshProUGUI lookaheadValue = CreateText(
            "镜头前探数值",
            lookaheadRow.transform,
            "+0.00s",
            16f,
            Amber);
        lookaheadValue.alignment = TextAlignmentOptions.MidlineRight;
        lookaheadValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;

        GameObject smoothingRow = CreateRow("预判平滑行", content, 52f);
        CreateRowLabel(smoothingRow.transform, "预判平滑", 128f);
        Slider smoothingSlider = CreateSlider("预判平滑", smoothingRow.transform);
        smoothingSlider.minValue = CameraUserSettings.MinimumLookaheadSmoothing;
        smoothingSlider.maxValue = CameraUserSettings.MaximumLookaheadSmoothing;
        smoothingSlider.value = CameraUserSettings.DefaultLookaheadSmoothing;
        TextMeshProUGUI smoothingValue = CreateText(
            "预判平滑数值",
            smoothingRow.transform,
            "0.0",
            16f,
            Amber);
        smoothingValue.alignment = TextAlignmentOptions.MidlineRight;
        smoothingValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;

        GameObject influenceRow = CreateRow("缩放影响系数行", content, 52f);
        CreateRowLabel(influenceRow.transform, "缩放影响系数", 128f);
        Slider influenceSlider = CreateSlider("缩放影响系数", influenceRow.transform);
        influenceSlider.minValue = CameraUserSettings.MinimumLookaheadZoomInfluence;
        influenceSlider.maxValue = CameraUserSettings.MaximumLookaheadZoomInfluence;
        influenceSlider.value = CameraUserSettings.DefaultLookaheadZoomInfluence;
        TextMeshProUGUI influenceValue = CreateText(
            "缩放影响系数数值",
            influenceRow.transform,
            "+0.00",
            16f,
            Amber);
        influenceValue.alignment = TextAlignmentOptions.MidlineRight;
        influenceValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;

        TextMeshProUGUI status = CreateText(
            "状态文本",
            content,
            "调整会立即应用并自动保存。",
            16f,
            Muted);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        Transform footer = CreateFooter(content);
        CreateSettingsButton("恢复默认按钮", footer, "恢复默认", 132f, 42f, false);
        return root;
    }

    /// <summary>构建 HUD 坐标格式选择页；两种显示立即写入本地偏好，不会修改世界或存档坐标。</summary>
    private static GameObject BuildCoordinateDisplaySettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.CoordinateDisplaySettings,
            out Transform content);
        CreateSettingsHeader(content, "显示设置");
        CreateSettingsHint(content, "选择屏幕左上角坐标卡片的显示方式；切换会立即生效并自动保存。", 48f);

        CreateSettingsSection(content, "坐标显示", "POSITION HUD");
        GameObject modeRow = CreateRow("坐标显示方式行", content, 54f);
        CreateRowLabel(modeRow.transform, "显示方式", 100f);
        CreateSettingsButton("世界坐标模式按钮", modeRow.transform, "世界坐标  X / Y", 252f, 46f, true);
        CreateSettingsButton("经纬度模式按钮", modeRow.transform, "经纬度  经 / 纬", 252f, 46f, false);

        TextMeshProUGUI status = CreateText(
            "状态文本",
            content,
            "当前显示：世界坐标（X / Y）",
            16f,
            Muted);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        return root;
    }

    /// <summary>构建主菜单退出确认窗口；取消是默认焦点，确认按钮才真正关闭应用。</summary>
    private static GameObject BuildMainMenuExitConfirmation()
    {
        GameObject root = CreateModalPanelRoot(
            RuntimeUIPrefabKeys.MainMenuExitConfirmation,
            new Vector2(760f, 360f));
        Transform dialog = root.transform.Find("设置对话框");
        dialog.name = "退出确认对话框";
        ConfigureMainMenuModalBackground(root, dialog, "退出确认");

        CreateMainMenuSettingsHeader(
            dialog,
            "退出游戏",
            GameManager.MainMenuExitConfirmationCloseButtonKey);

        TextMeshProUGUI message = CreateText(
            "退出确认提示",
            dialog,
            "确定要退出游戏吗？",
            27f,
            Cream);
        message.alignment = TextAlignmentOptions.Center;
        message.enableWordWrapping = true;
        message.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;

        Transform footer = CreateFooter(dialog);
        footer.GetComponent<LayoutElement>().preferredHeight = 76f;
        SetButtonLabelSize(CreateButton(
            GameManager.MainMenuExitConfirmationCancelButtonKey,
            footer,
            "取消",
            170f,
            64f,
            false), 21f);
        Button confirmButton = CreateButton(
            GameManager.MainMenuExitConfirmationConfirmButtonKey,
            footer,
            "退出游戏",
            190f,
            64f,
            false);
        confirmButton.GetComponent<Image>().color = Danger;
        SetButtonLabelSize(confirmButton, 21f);
        return root;
    }

    /// <summary>构建主菜单设置窗口；运行时由 GameManager 绑定显示、画质、特效质量和语言设置。</summary>
    private static GameObject BuildMainMenuSettings()
    {
        GameObject root = CreateModalPanelRoot(
            RuntimeUIPrefabKeys.MainMenuSettings,
            FlatWorldUIPanelMetrics.SharedModalCardSize);
        Transform dialog = root.transform.Find("设置对话框");
        ConfigureMainMenuModalBackground(root, dialog, "设置");

        CreateMainMenuSettingsHeader(dialog, "游戏设置", "关闭按钮");

        CreateMainMenuSettingsSection(dialog, "显示");
        CreateMainMenuSettingsDropdownRow(
            dialog,
            "窗口大小",
            "窗口大小下拉列表",
            new[] { "1920 × 1080", "1600 × 900", "1280 × 720" });
        CreateMainMenuSettingsDropdownRow(
            dialog,
            "显示模式",
            "显示模式下拉列表",
            new[] { "全屏窗口", "全屏", "窗口" });

        CreateMainMenuSettingsSection(dialog, "画质");
        CreateMainMenuSettingsDropdownRow(
            dialog,
            "画质预设",
            "画质预设下拉列表",
            new[] { "高（推荐）", "中", "低" });
        CreateMainMenuSettingsDropdownRow(
            dialog,
            "特效质量",
            "特效质量下拉列表",
            new[] { "高", "中", "低" });

        CreateMainMenuSettingsSection(dialog, "语言");
        CreateMainMenuSettingsDropdownRow(
            dialog,
            "游戏语言",
            "游戏语言下拉列表",
            new[] { "简体中文", "English" });

        TextMeshProUGUI status = CreateText(
            "设置状态",
            dialog,
            string.Empty,
            18f,
            Muted);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

        Transform footer = CreateFooter(dialog);
        footer.GetComponent<LayoutElement>().preferredHeight = 76f;
        Button resetButton = CreateButton("恢复默认按钮", footer, "恢复所有设置", 210f, 60f, false);
        resetButton.GetComponent<Image>().color = MainMenuSettingsSurface;
        SetButtonLabelSize(resetButton, 20f);
        SetButtonLabelSize(CreateButton("返回按钮", footer, "返回", 130f, 60f, true), 20f);
        return root;
    }

    /// <summary>为主菜单模态窗口建立覆盖刘海区的暗幕、卡片投影和暖黑背景。</summary>
    private static void ConfigureMainMenuModalBackground(
        GameObject root,
        Transform dialog,
        string objectNamePrefix)
    {
        Image rootBlocker = root.GetComponent<Image>();
        rootBlocker.color = new Color(0.004f, 0.008f, 0.012f, 0.01f);

        Image backdrop = CreateImage(
            objectNamePrefix + "全屏背景遮罩",
            root.transform,
            new Color(0.004f, 0.009f, 0.013f, 0.88f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = false;
        backdrop.gameObject.AddComponent<FullScreenRectController>();
        backdrop.transform.SetAsFirstSibling();

        Image shadow = CreateImage(
            objectNamePrefix + "主卡投影",
            root.transform,
            new Color(0f, 0f, 0f, 0.48f));
        SetCentered(
            shadow.rectTransform,
            new Vector2(14f, -16f),
            FlatWorldUIPanelMetrics.SharedModalCardSize);
        shadow.raycastTarget = false;
        shadow.transform.SetSiblingIndex(1);

        Image dialogImage = dialog.GetComponent<Image>();
        dialogImage.color = MainMenuSettingsCanvas;
        dialog.SetAsLastSibling();
    }

    /// <summary>构建带应用与取消的内嵌自动保存草稿页。</summary>
    private static GameObject BuildAutoSaveSettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.AutoSaveSettings,
            out Transform content);
        CreateSettingsHeader(content, "自动保存");
        CreateSettingsHint(content, "自动保存只在游戏世界中按现实时间运行，设置会立即保存。", 48f);

        GameObject modeRow = CreateRow("保存模式", content, 52f);
        CreateRowLabel(modeRow.transform, "保存模式", 130f);
        TMP_Dropdown dropdown = CreateDropdown("自动保存间隔下拉列表", modeRow.transform);
        dropdown.gameObject.AddComponent<LayoutElement>().preferredWidth = 402f;

        GameObject inputRow = CreateRow("自定义间隔", content, 52f);
        CreateRowLabel(inputRow.transform, "间隔（分钟）", 130f);
        TMP_InputField input = CreateInputField("自动保存间隔输入框", inputRow.transform, "输入 1–1440");
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.gameObject.AddComponent<LayoutElement>().preferredWidth = 340f;
        TextMeshProUGUI range = CreateText("范围提示", inputRow.transform, "1–1440", 15f, Muted);
        range.alignment = TextAlignmentOptions.MidlineRight;
        range.gameObject.AddComponent<LayoutElement>().preferredWidth = 62f;

        TextMeshProUGUI status = CreateText("状态文本", content, "当前设置：每 10 分钟自动保存。", 16f, Teal);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        Transform footer = CreateFooter(content);
        CreateSettingsButton("取消按钮", footer, "取消", 104f, 42f, false);
        CreateSettingsButton("应用按钮", footer, "应用", 116f, 42f, true);
        return root;
    }

    /// <summary>构建带应用与取消的内嵌流送性能草稿页。</summary>
    private static GameObject BuildWorldStreamingSettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.WorldStreamingSettings,
            out Transform content);
        CreateSettingsHeader(content, "区块流送性能");
        CreateSettingsHint(content,
            "自动适合多数设备；流畅优先减少 CPU 争用；高吞吐会在安全上限内使用多个后台线程。",
            68f);

        GameObject modeRow = CreateRow("性能模式行", content, 54f);
        CreateRowLabel(modeRow.transform, "性能模式", 122f);
        TMP_Dropdown dropdown = CreateDropdown("性能模式下拉列表", modeRow.transform);
        dropdown.gameObject.AddComponent<LayoutElement>().preferredWidth = 430f;

        TextMeshProUGUI explanation = CreateText(
            "模式说明", content,
            "纯地形数据在后台生成；Tilemap、碰撞和导航始终在主线程逐帧绘制。",
            16f, Muted);
        explanation.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;

        TextMeshProUGUI status = CreateText(
            "状态文本", content, "当前：自动平衡。", 16f, Teal);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        Transform footer = CreateFooter(content);
        CreateSettingsButton("取消按钮", footer, "取消", 104f, 42f, false);
        CreateSettingsButton("应用按钮", footer, "应用", 116f, 42f, true);
        return root;
    }

    /// <summary>构建带应用与取消的内嵌难度草稿页。</summary>
    private static GameObject BuildDifficultySettings()
    {
        GameObject root = CreateSettingsPageRoot(
            RuntimeUIPrefabKeys.DifficultySettings,
            out Transform content);
        CreateSettingsHeader(content, "游戏难度");
        CreateSettingsHint(content, "难度属于当前存档并立即生效。选择预设后点击应用。", 52f);

        for (int i = 0; i < GameDifficultyCatalog.All.Count; i++)
        {
            GameDifficultyDefinition definition = GameDifficultyCatalog.All[i];
            CreateDifficultyOption(content, definition, i == 0);
        }

        TextMeshProUGUI status = CreateText("状态文本", content, "当前存档难度：简单", 16f, Teal);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        Transform footer = CreateFooter(content);
        CreateSettingsButton("取消按钮", footer, "取消", 104f, 42f, false);
        CreateSettingsButton("应用按钮", footer, "应用", 116f, 42f, true);
        return root;
    }

    /// <summary>构建铺满主内容区的内嵌按键绑定页。</summary>
    private static GameObject BuildInputBindingSettings()
    {
        GameObject root = CreateFixedSettingsPageRoot(RuntimeUIPrefabKeys.InputBindingSettings);
        Transform content = root.transform;

        CreateSettingsHeader(content, "按键绑定");
        CreateSettingsHint(content, "先选择玩法控制方式；键鼠与手柄按键可在下方分别修改。", 42f);

        GameObject controlModeRow = CreateRow("控制方式行", content, 58f);
        CreateRowLabel(controlModeRow.transform, "控制方式", 122f);
        TMP_Dropdown controlModeDropdown = CreateDropdown("控制模式下拉列表", controlModeRow.transform);
        LayoutElement controlModeElement = controlModeDropdown.gameObject.AddComponent<LayoutElement>();
        controlModeElement.flexibleWidth = 1f;
        controlModeElement.preferredHeight = 46f;
        controlModeDropdown.ClearOptions();
        controlModeDropdown.AddOptions(new List<string>
        {
            "电脑键鼠控制",
            "手柄控制",
            "手机触屏控制"
        });
        controlModeDropdown.value = 0;
        controlModeDropdown.RefreshShownValue();

        GameObject deviceTabs = CreateUIObject("设备分页", content);
        deviceTabs.AddComponent<LayoutElement>().preferredHeight = 36f;
        HorizontalLayoutGroup tabLayout = deviceTabs.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 10f;
        tabLayout.childAlignment = TextAnchor.MiddleCenter;
        tabLayout.childControlWidth = true;
        tabLayout.childControlHeight = true;
        tabLayout.childForceExpandWidth = false;
        tabLayout.childForceExpandHeight = false;
        CreateButton("键鼠分页按钮", deviceTabs.transform, "键鼠", 132f, 34f, true);
        CreateButton("手柄分页按钮", deviceTabs.transform, "手柄", 132f, 34f, false);

        CreateBindingScrollView(content);
        TextMeshProUGUI status = CreateText("状态文本", content, "选择一项后按下新按键。", 13f, Muted);
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
        Transform footer = CreateFooter(content);
        CreateButton("恢复默认按钮", footer, "恢复默认", 112f, 36f, false);
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
        CreateButton("清除按钮", root.transform, "清除", 86f, 34f, false);
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

    /// <summary>把七个顶部入口和三个世界子页固化为同一主面板内的十个分页。</summary>
    private static void ConfigureSettingsActionListPages(GameObject root)
    {
        RectTransform scrollRect = FindTransform(root.transform, "Scroll View") as RectTransform;
        ScrollRect scroll = scrollRect != null ? scrollRect.GetComponent<ScrollRect>() : null;
        Transform content = scroll != null ? scroll.content : null;
        if (content == null || scrollRect == null || scroll == null)
            throw new MissingReferenceException("UI_ActionList.prefab 缺少 Content 或 Scroll View。");

        SetStretchWithMargins(scrollRect, 54f, 84f, 54f, 174f);
        ConfigureActionListScroll(scrollRect, content);

        Transform worldPage = EnsureActionListPage(
            content,
            SettingsActionListPagination.WorldPageName);
        Transform sessionPage = EnsureActionListPage(
            content,
            SettingsActionListPagination.SessionPageName);

        MoveEntryToPage(root.transform, worldPage, "自动保存");
        MoveEntryToPage(root.transform, worldPage, "流送性能");
        MoveEntryToPage(root.transform, worldPage, "游戏难度");

        ConfigureSettingsSessionActions(root.transform, sessionPage);

        Transform interfacePage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.InterfacePageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.UISettings + ".prefab");
        Transform inputBindingPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.InputBindingPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.InputBindingSettings + ".prefab");
        Transform displayPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.DisplayPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.CoordinateDisplaySettings + ".prefab");
        Transform cameraPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.CameraPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.CameraControlSettings + ".prefab");
        Transform audioPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.AudioPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.AudioSettings + ".prefab");
        Transform autoSavePage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.AutoSavePageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.AutoSaveSettings + ".prefab");
        Transform streamingPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.WorldStreamingPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.WorldStreamingSettings + ".prefab");
        Transform difficultyPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.DifficultyPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.DifficultySettings + ".prefab");

        worldPage.SetSiblingIndex(0);
        interfacePage.SetSiblingIndex(1);
        inputBindingPage.SetSiblingIndex(2);
        displayPage.SetSiblingIndex(3);
        cameraPage.SetSiblingIndex(4);
        audioPage.SetSiblingIndex(5);
        sessionPage.SetSiblingIndex(6);
        autoSavePage.SetSiblingIndex(7);
        streamingPage.SetSiblingIndex(8);
        difficultyPage.SetSiblingIndex(9);
        worldPage.gameObject.SetActive(true);
        sessionPage.gameObject.SetActive(false);
        EnsureActionListTabBar(root.transform);
        RemoveObsoleteActionListPagerControls(root.transform);
    }

    /// <summary>定位正式会话分页并仅刷新该分页及其共用确认层。</summary>
    private static void ConfigureSettingsSessionActions(GameObject root)
    {
        RectTransform scrollRect = FindTransform(root.transform, "Scroll View") as RectTransform;
        ScrollRect scroll = scrollRect != null ? scrollRect.GetComponent<ScrollRect>() : null;
        Transform content = scroll != null ? scroll.content : null;
        if (content == null)
            throw new MissingReferenceException("UI_ActionList.prefab 缺少 Content 或 Scroll View。");

        Transform sessionPage = EnsureActionListPage(
            content,
            SettingsActionListPagination.SessionPageName);
        ConfigureSettingsSessionActions(root.transform, sessionPage);
    }

    /// <summary>把会话页固化为三个稳定入口，并重建唯一保存退出确认层。</summary>
    private static void ConfigureSettingsSessionActions(Transform root, Transform sessionPage)
    {
        RemoveObsoleteSessionExitEntries(root);
        MoveEntryToPage(root, sessionPage, UIText.SaveButton);
        MoveEntryToPage(root, sessionPage, UIText.ReturnToMainMenuButton);
        MoveEntryToPage(root, sessionPage, UIText.ReturnToDesktopButton);
        MoveEntryToPage(root, sessionPage, "恢复所有设置");
        FindDirectChild(sessionPage, UIText.SaveButton).SetSiblingIndex(0);
        FindDirectChild(sessionPage, UIText.ReturnToMainMenuButton).SetSiblingIndex(1);
        FindDirectChild(sessionPage, UIText.ReturnToDesktopButton).SetSiblingIndex(2);
        FindDirectChild(sessionPage, "恢复所有设置").SetSiblingIndex(3);
        RebuildSettingsExitConfirmation(root);
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
            throw new MissingComponentException("UI_ActionList.prefab 的 Content 缺少 RectTransform。");
        Stretch(contentRect);
    }

    /// <summary>创建或更新可铺满视口的单个分页容器。</summary>
    private static Transform EnsureActionListPage(Transform content, string pageName)
    {
        Transform page = FindDirectChild(content, pageName);
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
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return page;
    }

    /// <summary>创建无布局所有权的分页容器，并在其中保留指定页面的嵌套 Prefab 实例。</summary>
    private static Transform EnsureEmbeddedActionListPage(
        Transform content,
        string pageName,
        string prefabPath)
    {
        GameObject pagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (pagePrefab == null)
            throw new FileNotFoundException($"找不到内嵌设置页 Prefab：{prefabPath}", prefabPath);

        Transform page = FindDirectChild(content, pageName);
        if (page == null)
            page = CreateUIObject(pageName, content).transform;

        Stretch(page as RectTransform);
        GridLayoutGroup grid = page.GetComponent<GridLayoutGroup>();
        if (grid != null)
            Object.DestroyImmediate(grid);
        VerticalLayoutGroup vertical = page.GetComponent<VerticalLayoutGroup>();
        if (vertical != null)
            Object.DestroyImmediate(vertical);
        ContentSizeFitter fitter = page.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            Object.DestroyImmediate(fitter);

        GameObject embedded = null;
        for (int index = page.childCount - 1; index >= 0; index--)
        {
            GameObject child = page.GetChild(index).gameObject;
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(child);
            if (embedded == null && source == pagePrefab)
            {
                embedded = child;
                continue;
            }

            Object.DestroyImmediate(child);
        }

        if (embedded == null)
            embedded = PrefabUtility.InstantiatePrefab(pagePrefab, page) as GameObject;
        if (embedded == null)
            throw new MissingReferenceException($"无法嵌套设置页 Prefab：{prefabPath}");

        embedded.name = pagePrefab.name;
        Stretch(embedded.GetComponent<RectTransform>());
        page.gameObject.SetActive(false);
        return page;
    }

    /// <summary>复用既有业务按钮或创建新入口，并将其移动到指定页面。</summary>
    private static void MoveEntryToPage(Transform root, Transform page, string entryName)
    {
        Transform entry = FindTransform(root, entryName);
        Button button = entry != null ? entry.GetComponent<Button>() : null;
        if (button == null)
            button = CreateButton(entryName, page, entryName, 360f, 64f, false);

        button.gameObject.name = entryName;
        button.transform.SetParent(page, false);
        button.gameObject.SetActive(true);

        LayoutElement element = button.GetComponent<LayoutElement>() ??
                                button.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = -1f;
        element.preferredHeight = 64f;
        element.flexibleWidth = 1f;
        button.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 64f);
        ConfigureButtonVisual(button, false, entryName);
        SetButtonLabelSize(button, 18f);
    }

    /// <summary>删除旧会话页的组合式保存/退出入口，新 Prefab 只保留三个单一职责按钮。</summary>
    private static void RemoveObsoleteSessionExitEntries(Transform root)
    {
        string[] obsoleteNames =
        {
            "保存游戏",
            "保存并回到主界面按钮",
            "保存并退出游戏按钮",
            "不保存直接退出"
        };

        for (int index = 0; index < obsoleteNames.Length; index++)
        {
            Transform obsolete = FindTransform(root, obsoleteNames[index]);
            if (obsolete != null)
                Object.DestroyImmediate(obsolete.gameObject);
        }
    }

    /// <summary>
    /// 重建会话页共用的保存退出确认层；340 高度的内容预算为上下边距 56 + 提示 150 + 间距 18 + 按钮区 72 = 296。
    /// 遮罩是设置面板的最后一个兄弟节点，鼠标射线与手柄焦点均不会穿透到底层分页。
    /// </summary>
    private static void RebuildSettingsExitConfirmation(Transform root)
    {
        Transform existing = FindTransform(
            root,
            SettingsExitConfirmationController.LayerName);
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        GameObject layer = CreateUIObject(
            SettingsExitConfirmationController.LayerName,
            root,
            typeof(CanvasRenderer),
            typeof(Image));
        Stretch(layer.GetComponent<RectTransform>());
        Image blocker = layer.GetComponent<Image>();
        blocker.color = new Color(0.008f, 0.014f, 0.018f, 0.82f);
        blocker.raycastTarget = true;

        GameObject dialog = CreateUIObject(
            SettingsExitConfirmationController.DialogName,
            layer.transform,
            typeof(CanvasRenderer),
            typeof(Image));
        SetCentered(
            dialog.GetComponent<RectTransform>(),
            Vector2.zero,
            new Vector2(720f, 340f));
        Image dialogBackground = dialog.GetComponent<Image>();
        dialogBackground.color = Canvas;
        dialogBackground.raycastTarget = true;
        AddOutline(dialogBackground, Amber);
        Shadow shadow = dialog.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.62f);
        shadow.effectDistance = new Vector2(8f, -8f);

        VerticalLayoutGroup dialogLayout = dialog.AddComponent<VerticalLayoutGroup>();
        dialogLayout.padding = new RectOffset(36, 36, 28, 28);
        dialogLayout.spacing = 18f;
        dialogLayout.childAlignment = TextAnchor.MiddleCenter;
        dialogLayout.childControlWidth = true;
        dialogLayout.childControlHeight = true;
        dialogLayout.childForceExpandWidth = true;
        dialogLayout.childForceExpandHeight = false;

        TextMeshProUGUI prompt = CreateText(
            SettingsExitConfirmationController.PromptName,
            dialog.transform,
            "是否保存再退出",
            30f,
            Cream);
        prompt.alignment = TextAlignmentOptions.Center;
        prompt.enableWordWrapping = true;
        prompt.enableAutoSizing = true;
        prompt.fontSizeMin = 22f;
        prompt.fontSizeMax = 30f;
        prompt.gameObject.AddComponent<LayoutElement>().preferredHeight = 150f;

        GameObject actions = CreateUIObject("确认操作区", dialog.transform);
        actions.AddComponent<LayoutElement>().preferredHeight = 72f;
        HorizontalLayoutGroup actionLayout = actions.AddComponent<HorizontalLayoutGroup>();
        actionLayout.spacing = 28f;
        actionLayout.childAlignment = TextAnchor.MiddleCenter;
        actionLayout.childControlWidth = false;
        actionLayout.childControlHeight = true;
        actionLayout.childForceExpandWidth = false;
        actionLayout.childForceExpandHeight = false;

        Button cancelButton = CreateButton(
            SettingsExitConfirmationController.CancelButtonName,
            actions.transform,
            "取消",
            180f,
            64f,
            false);
        cancelButton.GetComponent<Image>().color = new Color(0.30f, 0.33f, 0.35f, 1f);
        SetButtonLabelSize(cancelButton, 22f);

        Button confirmButton = CreateButton(
            SettingsExitConfirmationController.ConfirmButtonName,
            actions.transform,
            "确认",
            180f,
            64f,
            true);
        confirmButton.GetComponent<Image>().color = new Color(0.91f, 0.68f, 0.18f, 1f);
        SetButtonLabelSize(confirmButton, 22f);

        cancelButton.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnRight = confirmButton
        };
        confirmButton.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnLeft = cancelButton
        };

        layer.transform.SetAsLastSibling();
        layer.SetActive(false);
    }

    /// <summary>创建类似浏览器页签的顶部横向分页栏。</summary>
    private static void EnsureActionListTabBar(Transform root)
    {
        Transform tabBar = FindTransform(root, SettingsActionListPagination.TabBarName);
        if (tabBar == null)
            tabBar = CreateUIObject(
                SettingsActionListPagination.TabBarName,
                root,
                typeof(Image)).transform;

        RectTransform tabBarRect = tabBar as RectTransform;
        SetTopStretch(tabBarRect, 54f, 54f, 90f, 68f);

        Image background = tabBar.GetComponent<Image>() ?? tabBar.gameObject.AddComponent<Image>();
        background.color = Surface;
        background.raycastTarget = false;
        AddOutline(background, Border);

        HorizontalLayoutGroup layout = tabBar.GetComponent<HorizontalLayoutGroup>() ??
                                       tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        Button audioTab = EnsureActionListTabButton(root, tabBar, "音量调节", "音量调节");
        Button uiTab = EnsureActionListTabButton(root, tabBar, "UI设置", "UI设置");
        Button displayTab = EnsureActionListTabButton(root, tabBar, "显示设置", "显示设置");
        Button bindingTab = EnsureActionListTabButton(root, tabBar, "按键绑定", "按键绑定");
        Button cameraTab = EnsureActionListTabButton(root, tabBar, "镜头控制", "镜头控制");
        Button worldTab = EnsureActionListTabButton(
            root,
            tabBar,
            SettingsActionListPagination.WorldTabButtonName,
            "世界");
        Button sessionTab = EnsureActionListTabButton(
            root,
            tabBar,
            SettingsActionListPagination.SessionTabButtonName,
            "保存与退出");

        Button[] orderedTabs =
        {
            worldTab,
            uiTab,
            bindingTab,
            displayTab,
            cameraTab,
            audioTab,
            sessionTab
        };
        for (int index = 0; index < orderedTabs.Length; index++)
        {
            orderedTabs[index].transform.SetSiblingIndex(index);
            orderedTabs[index].GetComponent<Image>().color = SurfaceRaised;
        }

        worldTab.GetComponent<Image>().color = new Color(0.16f, 0.40f, 0.42f, 1f);
        sessionTab.GetComponent<Image>().color = SurfaceRaised;
        tabBar.SetAsLastSibling();
    }

    /// <summary>复用或创建单个分页页签，并交给横向布局分配宽度。</summary>
    private static Button EnsureActionListTabButton(
        Transform root,
        Transform tabBar,
        string buttonName,
        string caption)
    {
        Button button = FindTransform(root, buttonName)?.GetComponent<Button>();
        if (button == null)
            button = CreateButton(buttonName, tabBar, caption, 120f, 60f, false);

        button.gameObject.name = buttonName;
        button.gameObject.SetActive(true);
        button.transform.SetParent(tabBar, false);
        ConfigureButtonVisual(button, false, caption);
        LayoutElement element = button.GetComponent<LayoutElement>() ??
                                button.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = 120f;
        element.preferredHeight = 60f;
        element.flexibleWidth = 1f;
        SetButtonLabelSize(button, 16f);
        return button;
    }

    /// <summary>移除旧界面总页及左右翻页节点，确保 Prefab 只保留新的页签导航。</summary>
    private static void RemoveObsoleteActionListPagerControls(Transform root)
    {
        string[] obsoleteNames =
        {
            "设置上一页按钮",
            "设置页码文本",
            "设置下一页按钮",
            "设置页签_界面",
            "设置分页_界面"
        };

        for (int index = 0; index < obsoleteNames.Length; index++)
        {
            Transform obsolete = FindTransform(root, obsoleteNames[index]);
            if (obsolete != null)
                Object.DestroyImmediate(obsolete.gameObject);
        }
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

    /// <summary>确保本地玩家 Prefab 挂载 Buff HUD 控制器；控制器本身只负责实例化正式 UI Prefab。</summary>
    private static void EnsurePlayerBuffStatusHUD(GameObject root)
    {
        if (root == null)
            return;

        if (root.GetComponent<PlayerBuffStatusHUD>() == null)
        {
            root.AddComponent<PlayerBuffStatusHUD>();
            EditorUtility.SetDirty(root);
        }
    }

    /// <summary>确保本地玩家 Prefab 挂载任务追踪控制器；控制器只实例化正式任务 UI Prefab。</summary>
    private static void EnsurePlayerQuestTrackerHUD(GameObject root)
    {
        if (root == null)
            return;

        if (root.GetComponent<PlayerQuestTrackerHUD>() == null)
        {
            root.AddComponent<PlayerQuestTrackerHUD>();
            EditorUtility.SetDirty(root);
        }
    }

    /// <summary>确保本地玩家 Prefab 挂载手机 HUD 控制器；编辑器模拟必须由 Inspector 显式开启。</summary>
    private static void EnsurePlayerMobileControlsHUD(GameObject root)
    {
        if (root == null)
            return;

        if (root.GetComponent<PlayerMobileControlsHUD>() == null)
        {
            root.AddComponent<PlayerMobileControlsHUD>();
            EditorUtility.SetDirty(root);
        }
    }

    /// <summary>在全屏根 Canvas 下固化安全区节点，所有正式面板与手机 HUD 都由 UIManager 挂到这里。</summary>
    private static void EnsureSafeAreaRoot(GameObject root)
    {
        if (root == null)
            return;

        Transform existing = root.transform.Find("SafeAreaRoot");
        GameObject safeAreaObject = existing != null
            ? existing.gameObject
            : new GameObject("SafeAreaRoot", typeof(RectTransform), typeof(SafeAreaRectController));
        if (existing == null)
            safeAreaObject.transform.SetParent(root.transform, false);

        RectTransform rect = safeAreaObject.GetComponent<RectTransform>();
        Stretch(rect);
        if (safeAreaObject.GetComponent<SafeAreaRectController>() == null)
            safeAreaObject.AddComponent<SafeAreaRectController>();
        safeAreaObject.transform.SetAsLastSibling();
        EditorUtility.SetDirty(root);
    }

    /// <summary>
    /// 构建 Android 横屏的正式手机控制层。左右摇杆默认使用 33% 触控区，中间 34% 留空；按钮和攻击摇杆拥有更高射线优先级。
    /// </summary>
    private static GameObject BuildMobileControlsHUD()
    {
        GameObject root = CreateUIObject(RuntimeUIPrefabKeys.MobileControls, null, typeof(CanvasGroup));
        Stretch(root.GetComponent<RectTransform>());
        CanvasGroup rootGroup = root.GetComponent<CanvasGroup>();
        rootGroup.alpha = 1f;
        rootGroup.interactable = true;
        rootGroup.blocksRaycasts = true;

        GameObject aimCursor = CreateUIObject(
            "手机准线",
            root.transform,
            typeof(CanvasRenderer),
            typeof(GamepadCursorGraphic));
        SetCentered(aimCursor.GetComponent<RectTransform>(), Vector2.zero, new Vector2(28f, 28f));
        GamepadCursorGraphic aimCursorGraphic = aimCursor.GetComponent<GamepadCursorGraphic>();
        aimCursorGraphic.color = FlatWorldUITheme.SelectionOutline;
        aimCursorGraphic.raycastTarget = false;
        aimCursor.SetActive(false);

        GameObject gameplay = CreateUIObject("玩法控制层", root.transform);
        Stretch(gameplay.GetComponent<RectTransform>());

        // 手持物丢弃面位于所有玩法控件下方；平时不参与射线，只补齐三段区域中间的世界长按入口。
        GameObject heldItemDropSurface = CreateUIObject(
            "手持物丢弃区",
            gameplay.transform,
            typeof(Image),
            typeof(MobileHeldItemDropSurface));
        Stretch(heldItemDropSurface.GetComponent<RectTransform>());
        Image heldItemDropHit = heldItemDropSurface.GetComponent<Image>();
        heldItemDropHit.color = new Color(1f, 1f, 1f, 0.001f);
        heldItemDropHit.raycastTarget = true;
        heldItemDropSurface.GetComponent<MobileHeldItemDropSurface>()
            .Configure(onlyRaycastWhileHoldingItem: true);

        // 透明捕获层仅覆盖右侧默认配置区，按钮和攻击摇杆随后创建以获得更高射线优先级。
        GameObject aimZone = CreateUIObject(
            "普通指向区",
            gameplay.transform,
            typeof(Image),
            typeof(MobileHeldItemDropSurface));
        RectTransform aimRect = aimZone.GetComponent<RectTransform>();
        aimRect.anchorMin = new Vector2(1f - UIUserSettings.DefaultRightControlZoneRatio, 0f);
        aimRect.anchorMax = Vector2.one;
        aimRect.offsetMin = Vector2.zero;
        aimRect.offsetMax = Vector2.zero;
        Image aimCapture = aimZone.GetComponent<Image>();
        aimCapture.color = new Color(1f, 1f, 1f, 0.001f);
        aimCapture.raycastTarget = true;
        aimZone.GetComponent<MobileHeldItemDropSurface>()
            .Configure(onlyRaycastWhileHoldingItem: false);
        CreateJoystickVisual(aimZone.transform, Vector2.zero, 188f, floating: true);

        GameObject moveZone = CreateUIObject(
            "移动摇杆",
            gameplay.transform,
            typeof(Image),
            typeof(MobileHeldItemDropSurface));
        RectTransform moveRect = moveZone.GetComponent<RectTransform>();
        moveRect.anchorMin = Vector2.zero;
        moveRect.anchorMax = new Vector2(UIUserSettings.DefaultLeftControlZoneRatio, 1f);
        moveRect.offsetMin = Vector2.zero;
        moveRect.offsetMax = Vector2.zero;
        Image moveHit = moveZone.GetComponent<Image>();
        moveHit.color = new Color(1f, 1f, 1f, 0.001f);
        moveHit.raycastTarget = true;
        moveZone.GetComponent<MobileHeldItemDropSurface>()
            .Configure(onlyRaycastWhileHoldingItem: false);
        CreateJoystickVisual(moveZone.transform, Vector2.zero, 188f, floating: true);

        // 右侧操作组以安全区右下角为唯一定位基准，组内所有控件使用局部坐标对齐。
        GameObject actionGroup = CreateUIObject("右侧操作组", gameplay.transform);
        RectTransform actionGroupRect = actionGroup.GetComponent<RectTransform>();
        SetBottomRight(
            actionGroupRect,
            MobileActionRightMargin,
            MobileActionBottomMargin,
            MobileActionGroupWidth,
            MobileActionGroupHeight);

        GameObject attackZone = CreateUIObject(
            "攻击摇杆",
            actionGroup.transform,
            typeof(Image),
            typeof(MobileHeldItemDropSurface));
        SetBottomRight(
            attackZone.GetComponent<RectTransform>(),
            (MobileActionGroupWidth - MobileAttackZoneSize) * 0.5f,
            0f,
            MobileAttackZoneSize,
            MobileAttackZoneSize);
        Image attackHit = attackZone.GetComponent<Image>();
        attackHit.color = new Color(1f, 1f, 1f, 0.001f);
        attackHit.raycastTarget = true;
        attackZone.GetComponent<MobileHeldItemDropSurface>()
            .Configure(onlyRaycastWhileHoldingItem: false);
        CreateJoystickVisual(attackZone.transform, Vector2.zero, 188f, floating: false);

        CreateMobileButton(
            "交互",
            actionGroup.transform,
            "交互",
            Vector2.zero,
            new Vector2(0f, MobileAttackZoneSize + MobileActionGap),
            MobileActionButtonSize);
        CreateMobileButton(
            "使用",
            actionGroup.transform,
            "使用",
            new Vector2(1f, 0f),
            new Vector2(0f, MobileAttackZoneSize + MobileActionGap),
            MobileActionButtonSize);
        CreateMobileButton("奔跑", gameplay.transform, "奔跑", new Vector2(0f, 0.5f), new Vector2(76f, 0f), 104f);

        // 设置与菜单并列固定在右上角，不能随模态面板一起隐藏；设置更靠右，菜单作为返回/退出入口。
        GameObject persistent = CreateUIObject("常驻控制层", root.transform);
        Stretch(persistent.GetComponent<RectTransform>());
        CreateMobileButton("设置", persistent.transform, "设置", new Vector2(1f, 1f), new Vector2(-58f, -58f), 100f);
        CreateMobileButton("菜单", persistent.transform, "菜单", new Vector2(1f, 1f), new Vector2(-166f, -58f), 100f);

        // 快捷栏独立于玩法控制层，打开背包等模态面板时仍可见、可拖放。
        GameObject hotbarAnchor = CreateUIObject("快捷栏锚点", root.transform);
        RectTransform hotbarRect = hotbarAnchor.GetComponent<RectTransform>();
        hotbarRect.anchorMin = hotbarRect.anchorMax = new Vector2(0.5f, 0f);
        hotbarRect.pivot = new Vector2(0.5f, 0f);
        hotbarRect.anchoredPosition = new Vector2(0f, 16f);
        hotbarRect.sizeDelta = new Vector2(760f, 126f);

        BuildMobileDrawer(root.transform);
        return root;
    }

    private static void CreateJoystickVisual(Transform parent, Vector2 position, float diameter, bool floating)
    {
        GameObject baseObject = CreateUIObject("底座", parent, typeof(Image), typeof(CanvasGroup));
        RectTransform baseRect = baseObject.GetComponent<RectTransform>();
        SetCentered(baseRect, position, new Vector2(diameter, diameter));
        Image baseImage = baseObject.GetComponent<Image>();
        baseImage.color = new Color(SurfaceRaised.r, SurfaceRaised.g, SurfaceRaised.b, 0.64f);
        baseImage.raycastTarget = false;
        AddOutline(baseImage, floating ? new Color(0f, 0f, 0f, 0f) : Border);
        CanvasGroup baseGroup = baseObject.GetComponent<CanvasGroup>();
        baseGroup.alpha = floating ? 0f : 1f;
        baseGroup.interactable = false;
        baseGroup.blocksRaycasts = false;

        GameObject knobObject = CreateUIObject("摇杆", baseObject.transform, typeof(Image));
        RectTransform knobRect = knobObject.GetComponent<RectTransform>();
        SetCentered(knobRect, Vector2.zero, new Vector2(82f, 82f));
        Image knobImage = knobObject.GetComponent<Image>();
        knobImage.color = new Color(Teal.r, Teal.g, Teal.b, 0.92f);
        knobImage.raycastTarget = false;
    }

    private static void BuildMobileDrawer(Transform parent)
    {
        GameObject drawer = CreateUIObject("菜单抽屉", parent, typeof(Image));
        RectTransform drawerRect = drawer.GetComponent<RectTransform>();
        drawerRect.anchorMin = new Vector2(1f, 0.5f);
        drawerRect.anchorMax = new Vector2(1f, 0.5f);
        drawerRect.pivot = new Vector2(1f, 0.5f);
        drawerRect.anchoredPosition = new Vector2(-20f, 0f);
        drawerRect.sizeDelta = new Vector2(430f, 690f);
        Image background = drawer.GetComponent<Image>();
        background.color = Canvas;
        background.raycastTarget = true;
        AddOutline(background, Amber);

        TextMeshProUGUI title = CreateText("抽屉标题", drawer.transform, "手机菜单", 22f, Cream);
        title.fontStyle = FontStyles.Bold;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -16f);
        titleRect.sizeDelta = new Vector2(-112f, 48f);

        GameObject buttonGrid = CreateUIObject("抽屉按钮区", drawer.transform);
        RectTransform gridRect = buttonGrid.GetComponent<RectTransform>();
        gridRect.anchorMin = Vector2.zero;
        gridRect.anchorMax = Vector2.one;
        gridRect.offsetMin = new Vector2(22f, 22f);
        gridRect.offsetMax = new Vector2(-22f, -82f);
        GridLayoutGroup grid = buttonGrid.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(184f, 66f);
        grid.spacing = new Vector2(14f, 14f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;

        // 关闭按钮收进标题区，底部留出 14px 间隙，避免压住网格第一行按钮。
        Button closeButton = CreateButton("关闭抽屉", drawer.transform, "关闭", 88f, 52f, false);
        SetTopRight(closeButton.GetComponent<RectTransform>(), 16f, 16f, 88f, 52f);

        CreateButton("背包", buttonGrid.transform, "背包", 184f, 66f, true);
        CreateButton("装备", buttonGrid.transform, "装备", 184f, 66f, false);
        CreateButton("制作", buttonGrid.transform, "制作", 184f, 66f, false);
        CreateButton("状态", buttonGrid.transform, "生存状态", 184f, 66f, false);
        CreateButton("丢弃一个", buttonGrid.transform, "丢弃一个", 184f, 66f, false);
        // 镜头缩放单独占用抽屉底部横向区域，避免继续用两个按钮离散调整视野。
        TextMeshProUGUI zoomLabel = CreateText("镜头缩放标签", drawer.transform, "镜头缩放", 16f, Cream);
        zoomLabel.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform zoomLabelRect = zoomLabel.rectTransform;
        zoomLabelRect.anchorMin = new Vector2(0f, 0f);
        zoomLabelRect.anchorMax = new Vector2(1f, 0f);
        zoomLabelRect.pivot = new Vector2(0.5f, 0f);
        zoomLabelRect.offsetMin = new Vector2(24f, 82f);
        zoomLabelRect.offsetMax = new Vector2(-24f, 112f);

        Slider zoomSlider = CreateSlider("镜头缩放", drawer.transform);
        zoomSlider.minValue = 5f;
        zoomSlider.maxValue = 20f;
        zoomSlider.value = 10f;
        zoomSlider.wholeNumbers = false;
        RectTransform zoomRect = zoomSlider.GetComponent<RectTransform>();
        zoomRect.anchorMin = new Vector2(0f, 0f);
        zoomRect.anchorMax = new Vector2(1f, 0f);
        zoomRect.pivot = new Vector2(0.5f, 0f);
        zoomRect.offsetMin = new Vector2(24f, 24f);
        zoomRect.offsetMax = new Vector2(-24f, 76f);
    }

    private static void CreateMobileButton(
        string name,
        Transform parent,
        string caption,
        Vector2 anchor,
        Vector2 position,
        float size)
    {
        Button button = CreateButton(name, parent, caption, size, size, name == "交互" || name == "使用");
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(size, size);

        if (name == "奔跑")
        {
            GameObject indicator = CreateUIObject("状态标记", button.transform, typeof(Image));
            RectTransform indicatorRect = indicator.GetComponent<RectTransform>();
            indicatorRect.anchorMin = indicatorRect.anchorMax = Vector2.one;
            indicatorRect.pivot = Vector2.one;
            indicatorRect.anchoredPosition = new Vector2(-10f, -10f);
            indicatorRect.sizeDelta = new Vector2(20f, 20f);
            Image indicatorImage = indicator.GetComponent<Image>();
            indicatorImage.color = Border;
            indicatorImage.raycastTarget = false;
        }
    }

    #endregion

    #region 通用控件构建

    /// <summary>创建带页内纵向滚动的可嵌入设置页。</summary>
    private static GameObject CreateSettingsPageRoot(string name, out Transform content)
    {
        GameObject root = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(ScrollRect));
        RectTransform rect = root.GetComponent<RectTransform>();
        Stretch(rect);

        Image image = root.GetComponent<Image>();
        image.color = Canvas;
        image.raycastTarget = true;

        GameObject viewport = CreateUIObject(
            "PageViewport",
            root.transform,
            typeof(RectMask2D));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);

        GameObject contentObject = CreateUIObject("PageContent", viewport.transform);
        RectTransform contentRect = contentObject.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 18);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        contentObject.AddComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = root.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 26f;
        scroll.viewport = viewportRect;
        scroll.content = contentRect;

        content = contentObject.transform;
        return root;
    }

    /// <summary>创建由纵向布局直接铺满内容区的可嵌入设置页。</summary>
    private static GameObject CreateFixedSettingsPageRoot(string name)
    {
        GameObject root = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        Stretch(root.GetComponent<RectTransform>());

        Image image = root.GetComponent<Image>();
        image.color = Canvas;
        image.raycastTarget = true;

        VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 18);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
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

    /// <summary>主菜单设置页使用移动端可读标题栏，不影响游戏内紧凑设置窗口。</summary>
    private static void CreateMainMenuSettingsHeader(Transform parent, string title, string closeButtonName)
    {
        GameObject header = CreateUIObject("标题", parent, typeof(Image));
        header.GetComponent<Image>().color = MainMenuSettingsSurface;
        header.AddComponent<LayoutElement>().preferredHeight = 82f;

        HorizontalLayoutGroup layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 12, 9, 9);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI titleText = CreateText("标题文本", header.transform, title, 32f, Cream);
        titleText.fontStyle = FontStyles.Bold;
        titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Button closeButton = CreateButton(closeButtonName, header.transform, "关闭", 120f, 64f, false);
        closeButton.GetComponent<Image>().color = new Color(0.058f, 0.047f, 0.043f, 1f);
        SetButtonLabelSize(closeButton, 20f);
    }

    /// <summary>创建不含独立关闭按钮的内嵌设置页标题栏。</summary>
    private static void CreateSettingsHeader(Transform parent, string title)
    {
        GameObject header = CreateUIObject("标题", parent, typeof(Image));
        header.GetComponent<Image>().color = SurfaceRaised;
        header.AddComponent<LayoutElement>().preferredHeight = 76f;

        HorizontalLayoutGroup layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 12, 8, 8);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI titleText = CreateText("标题文本", header.transform, title, 25f, Cream);
        titleText.fontStyle = FontStyles.Bold;
        titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
    }

    private static void CreateHint(Transform parent, string value, float height)
    {
        TextMeshProUGUI hint = CreateText("说明文本", parent, value, 13f, Muted);
        hint.enableWordWrapping = true;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
    }

    /// <summary>设置子分页专用的大字号说明文本。</summary>
    private static void CreateSettingsHint(Transform parent, string value, float height)
    {
        TextMeshProUGUI hint = CreateText("说明文本", parent, value, 16f, Muted);
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
        element.preferredHeight = 46f;

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

        TextMeshProUGUI titleText = CreateText(title + "分组标题", section.transform, title, 17f, Amber);
        titleText.fontStyle = FontStyles.Bold;
        titleText.gameObject.AddComponent<LayoutElement>().preferredWidth = 96f;

        if (!string.IsNullOrWhiteSpace(eyebrow))
        {
            TextMeshProUGUI eyebrowText = CreateText(title + "分组英文", section.transform, eyebrow, 12f, Muted);
            eyebrowText.characterSpacing = 2f;
            eyebrowText.alignment = TextAlignmentOptions.MidlineRight;
            eyebrowText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }
    }

    /// <summary>创建主菜单设置页的移动端分组标题。</summary>
    private static void CreateMainMenuSettingsSection(Transform parent, string title)
    {
        GameObject section = CreateUIObject(title + "设置分组", parent, typeof(Image));
        section.AddComponent<LayoutElement>().preferredHeight = 50f;

        Image background = section.GetComponent<Image>();
        background.color = MainMenuSettingsSection;
        AddOutline(background, new Color(0.83f, 0.49f, 0.23f, 0.22f));

        HorizontalLayoutGroup layout = section.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 4, 4);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI titleText = CreateText(title + "分组标题", section.transform, title, 22f, Amber);
        titleText.fontStyle = FontStyles.Bold;
        titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
    }

    /// <summary>创建主菜单设置页的移动端下拉行，扩大文字、选项和触控高度。</summary>
    private static TMP_Dropdown CreateMainMenuSettingsDropdownRow(
        Transform parent,
        string label,
        string dropdownName,
        string[] options)
    {
        GameObject row = CreateRow(label + "行", parent, 72f);
        TextMeshProUGUI labelText = CreateText(label + "标签", row.transform, label, 22f, Cream);
        labelText.gameObject.AddComponent<LayoutElement>().preferredWidth = 160f;

        TMP_Dropdown dropdown = CreateDropdown(dropdownName, row.transform);
        LayoutElement element = dropdown.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = 1100f;
        element.preferredHeight = 64f;
        dropdown.captionText.fontSize = 20f;
        dropdown.itemText.fontSize = 19f;
        Transform item = dropdown.itemText.transform.parent;
        item.GetComponent<LayoutElement>().preferredHeight = 56f;
        ((Image)dropdown.targetGraphic).color = MainMenuSettingsSurface;
        dropdown.template.GetComponent<Image>().color = MainMenuSettingsCanvas;
        item.GetComponent<Image>().color = new Color(0.055f, 0.075f, 0.078f, 1f);
        dropdown.template.sizeDelta = new Vector2(0f, 280f);
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string>(options));
        dropdown.value = 0;
        dropdown.RefreshShownValue();
        return dropdown;
    }

    /// <summary>创建设置页下拉项，并写入仅用于展示的默认选项。</summary>
    private static TMP_Dropdown CreateSettingsDropdownRow(
        Transform parent,
        string label,
        string dropdownName,
        string[] options)
    {
        GameObject row = CreateRow(label + "行", parent, 58f);
        CreateRowLabel(row.transform, label, 122f);

        TMP_Dropdown dropdown = CreateDropdown(dropdownName, row.transform);
        LayoutElement element = dropdown.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = 1040f;
        element.preferredHeight = 52f;
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string>(options));
        dropdown.value = 0;
        dropdown.RefreshShownValue();
        return dropdown;
    }

    private static void CreateRowLabel(Transform parent, string value, float width)
    {
        TextMeshProUGUI label = CreateText(value + "标签", parent, value, 17f, Cream);
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
    }

    private static void CreateSliderRow(Transform parent, string label, string sliderName)
    {
        GameObject row = CreateRow(label + "行", parent, 50f);
        CreateRowLabel(row.transform, label, 112f);
        Slider slider = CreateSlider(sliderName, row.transform);
        slider.value = 1f;
        TextMeshProUGUI value = CreateText(sliderName + "_数值", row.transform, "100%", 16f, Amber);
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
        option.AddComponent<LayoutElement>().preferredHeight = 88f;
        Image image = option.GetComponent<Image>();
        image.color = selected ? Amber : Surface;
        AddOutline(image, selected ? Amber : Border);
        Button button = option.GetComponent<Button>();
        button.targetGraphic = image;
        ConfigureButtonColors(button);

        TextMeshProUGUI title = CreateText("名称", option.transform, definition.DisplayName, 20f, Cream);
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        SetTopStretch(title.rectTransform, new Vector2(18f, -42f), new Vector2(-18f, -8f));

        TextMeshProUGUI description = CreateText("说明", option.transform, definition.Description, 15f, Muted);
        description.enableWordWrapping = false;
        description.overflowMode = TextOverflowModes.Ellipsis;
        description.alignment = TextAlignmentOptions.MidlineLeft;
        description.rectTransform.anchorMin = new Vector2(0f, 0f);
        description.rectTransform.anchorMax = new Vector2(1f, 0f);
        description.rectTransform.offsetMin = new Vector2(18f, 8f);
        description.rectTransform.offsetMax = new Vector2(-18f, 42f);
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
        footer.AddComponent<LayoutElement>().preferredHeight = 56f;
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

    /// <summary>创建设置子分页专用的大字号按钮。</summary>
    private static Button CreateSettingsButton(
        string name,
        Transform parent,
        string caption,
        float width,
        float height,
        bool primary)
    {
        Button button = CreateButton(name, parent, caption, width, height, primary);
        SetButtonLabelSize(button, 16f);
        return button;
    }

    /// <summary>调整按钮文字字号，不改变业务节点与交互组件。</summary>
    private static void SetButtonLabelSize(Button button, float fontSize)
    {
        TextMeshProUGUI label = button != null
            ? button.GetComponentInChildren<TextMeshProUGUI>(true)
            : null;
        if (label != null)
            label.fontSize = fontSize;
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

    /// <summary>只在指定父节点的直接子级中查找命名节点。</summary>
    private static Transform FindDirectChild(Transform parent, string name)
    {
        if (parent == null)
            return null;

        for (int index = 0; index < parent.childCount; index++)
        {
            Transform child = parent.GetChild(index);
            if (child != null && child.name == name)
                return child;
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

    /// <summary>使用四侧边距让矩形随父级完整伸缩。</summary>
    private static void SetStretchWithMargins(
        RectTransform rect,
        float left,
        float bottom,
        float right,
        float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>固定高度并横向拉伸，供顶部页签栏使用。</summary>
    private static void SetTopStretch(
        RectTransform rect,
        float left,
        float right,
        float top,
        float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void SetTopRight(RectTransform rect, float right, float top, float width, float height)
    {
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-right, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void SetBottomLeft(RectTransform rect, float left, float bottom, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(left, bottom);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void SetBottomRight(RectTransform rect, float right, float bottom, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-right, bottom);
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
