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

/// <summary>
/// 独立的运行时 GM 测试窗口。
/// 不要求任何既有脚本实现接口：通过反射发现当前场景中的安全测试指令，
/// 并通过 ItemMgr 的既有实例化入口完成空投。F4 打开/关闭。
/// </summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public sealed partial class GMReflectionConsole : MonoBehaviour
{
    #region Runtime UI localization

    private static void SetLocalizedText(TMP_Text target, string sourceText)
    {
        LocalizedTextRuntime.SetText(target, sourceText);
    }

    private static void SetLocalizedFormat(TMP_Text target, string sourceTemplate, params object[] arguments)
    {
        LocalizedTextRuntime.SetFormat(target, sourceTemplate, arguments);
    }

    private static void SetLocalizedProvider(TMP_Text target, Func<string> provider)
    {
        LocalizedTextRuntime.SetProvider(target, provider);
    }

    #endregion

    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string AdministratorName = "管理员";
    private const int MaxDiscoveredCommands = 36;
    private const float CatalogBrowserHeaderHeight = 42f;
    private const float CatalogBrowserToolbarHeight = 34f;
    private const float CatalogBrowserControlHeight = 30f;

    private sealed class ReflectedCommand
    {
        public string Category;
        public string Label;
        public Component Target;
        public MethodInfo Method;
        public object[] Arguments;
    }

    private sealed class AirdropItemEntry
    {
        public string ItemId;
        public string DisplayName;
        public Sprite Icon;
    }

    private sealed class AiCreatureEntry
    {
        public string ItemId;
        public string DisplayName;
        public Sprite Icon;
        public Texture IconAtlas;
        public Rect IconUv;
    }

    private readonly List<ReflectedCommand> commands = new List<ReflectedCommand>();
    private readonly List<AirdropItemEntry> availableAirdropItems = new List<AirdropItemEntry>();
    private readonly List<AiCreatureEntry> availableAiCreatures = new List<AiCreatureEntry>();
    private readonly List<StructureDefinitionSO> availableStructures = new List<StructureDefinitionSO>();

    private GameObject windowRoot;
    private GameObject airdropBrowserRoot;
    private GameObject aiCreatureBrowserRoot;
    private RectTransform airdropBrowserRect;
    private RectTransform aiCreatureBrowserRect;
    private TMP_InputField itemIdInput;
    private TMP_InputField amountInput;
    private TMP_InputField airdropSearchInput;
    private TMP_InputField aiCreatureSearchInput;
    private TMP_InputField aiCreatureAmountInput;
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI itemHintText;
    private TextMeshProUGUI airdropBrowserCountText;
    private TextMeshProUGUI airdropBrowserStatusText;
    private TextMeshProUGUI aiCreatureBrowserCountText;
    private TextMeshProUGUI aiCreatureBrowserStatusText;
    private TextMeshProUGUI structureSelectionText;
    private TextMeshProUGUI structureHintText;
    private TextMeshProUGUI commandCountText;
    private Button teleportShortcutButton;
    private Button adminInvincibilityButton;
    private Button playerMoveSpeedButton;
    private TMP_InputField playerMoveSpeedInput;
    private Button playerMoveSpeedApplyButton;
    private TMP_InputField chunkLoadSpeedInput;
    private Button chunkLoadSpeedApplyButton;
    private Button chunkLoadSpeedUnlimitedButton;
    private Button navigationPathButton;
    private Button animalDebugOverlayButton;
    private Button hiveDebugOverlayButton;
    private Transform commandGrid;
    private Transform airdropItemGrid;
    private Transform aiCreatureGrid;
    private GridLayoutGroup airdropItemGridLayout;
    private GridLayoutGroup aiCreatureGridLayout;
    private Sprite dropMarkerSprite;
    private Coroutine restorePreferencesCoroutine;
    private int selectedStructureIndex;
    private static TMP_FontAsset uiFont;

    #region 悬停属性观察

    private readonly GmHoverPropertyInspector hoverPropertyInspector = new GmHoverPropertyInspector();

    private void OnGUI() => hoverPropertyInspector.DrawOverlay();

    #endregion

    #region GM 主题

    // GM 面板跟随全局 UI 主题，不再维护独立的蓝绿/橙色皮肤。
    private static Color GmCanvas => FlatWorldUITheme.Canvas;
    private static Color GmSurfaceLow => FlatWorldUITheme.SurfaceLow;
    private static Color GmSurface => FlatWorldUITheme.Surface;
    private static Color GmSurfaceRaised => FlatWorldUITheme.SurfaceRaised;
    private static Color GmBorder => FlatWorldUITheme.Border;
    private static Color GmTextPrimary => FlatWorldUITheme.TextPrimary;
    private static Color GmTextSecondary => FlatWorldUITheme.TextSecondary;
    private static Color GmAccent => FlatWorldUITheme.Accent;
    private static Color GmAccentHover => FlatWorldUITheme.AccentHover;
    private static Color GmSelection => FlatWorldUITheme.Selection;
    private static Color GmSelectionOutline => FlatWorldUITheme.SelectionOutline;
    private static Color GmDanger => FlatWorldUITheme.Danger;
    // Button 的 Color Tint 会与深灰底色相乘；禁用态只降一级明度，不能再用半透明中灰把底色压成近黑。
    private static Color GmDisabledTint => new(0.82f, 0.82f, 0.82f, 1f);

    /// <summary>统一 GM 按钮底色和描边，选中/关键操作只用暖黄细描边强调。</summary>
    private static void SetGmButtonVisual(Button button, Color surface, bool emphasized = false)
    {
        if (button == null)
            return;

        Image image = button.GetComponent<Image>();
        if (image != null)
            image.color = surface;

        Outline outline = button.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = emphasized ? GmSelectionOutline : GmBorder;
            outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;
            outline.useGraphicAlpha = true;
        }
    }

    /// <summary>统一 GM 非文字 Graphic 的低对比描边。</summary>
    private static void StyleGmOutline(Outline outline, bool emphasized = false)
    {
        if (outline == null)
            return;

        outline.effectColor = emphasized ? GmSelectionOutline : GmBorder;
        outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;
        outline.useGraphicAlpha = true;
    }

    #endregion

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<GMReflectionConsole>() != null)
            return;

        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.IsStartupReady)
        {
            GameRes.StartupResourcesReady -= BootstrapAfterStartupResourcesReady;
            GameRes.StartupResourcesReady += BootstrapAfterStartupResourcesReady;
            resources = GameRes.ExistingInstance;
            if (resources == null || !resources.IsStartupReady)
                return;
        }

        CreateBootstrapConsole();
    }

    /// <summary>资源管理器发布启动资源后再创建依赖公共控件的 GM 界面。</summary>
    private static void BootstrapAfterStartupResourcesReady(GameRes resources)
    {
        GameRes.StartupResourcesReady -= BootstrapAfterStartupResourcesReady;
        if (FindObjectOfType<GMReflectionConsole>() != null)
            return;

        CreateBootstrapConsole();
    }

    /// <summary>创建唯一的运行时 GM 控制台。</summary>
    private static void CreateBootstrapConsole()
    {
        GameObject root = new GameObject("[GM Reflection Console]");
        root.AddComponent<GMReflectionConsole>();
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        worldLayerOverlay = gameObject.AddComponent<GMWorldLayerOverlay>();
        EnsureEventSystem();
        BuildWindow();
        FlatWorldLocalizationService.LanguageChanged += HandleLocalizedCatalogRefresh;
        Mod_ItemPicker.PickerLoaded += HandlePickupPickerLoaded;
        ApplyPersistedTogglePreferences();
        RestartRestorePreferences();
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        RuntimeDebugOverlay.GmPanelOpenRequested += OpenWindow;
    }

    private void OnDestroy()
    {
        FlatWorld.Localization.FlatWorldLocalizationService.LanguageChanged -= HandleLocalizedCatalogRefresh;
        Mod_ItemPicker.PickerLoaded -= HandlePickupPickerLoaded;
        CancelLocationSearches();
        GMConsolePreferences.SavePendingChanges();
        if (FlatWorld.AIECS.Gameplay.AiecsPlayground.Active != null)
            FlatWorld.AIECS.Gameplay.AiecsPlayground.Active.HealthOverlaySuppressed = false;
        CancelTeleportTargeting();
        RuntimeDebugOverlay.GmPanelOpenRequested -= OpenWindow;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (restorePreferencesCoroutine != null)
            StopCoroutine(restorePreferencesCoroutine);
        hoverPropertyInspector.ClearTarget();
        DisposeBuffTargeting();
        DisposeQuestPage();
        UnbindGameEventManager();
    }

    private void HandleLocalizedCatalogRefresh(string localeCode)
    {
        if (airdropBrowserRoot != null && airdropBrowserRoot.activeSelf)
            RebuildAirdropItemGrid();
        if (aiCreatureBrowserRoot != null && aiCreatureBrowserRoot.activeSelf)
            RebuildAiCreatureGrid();
    }
    private void Update()
    {
        // IMGUI 在 Canvas 之后绘制；GM 打开时只隐藏开发血条，不修改玩家配置的 ShowHealth 开关。
        var playground = FlatWorld.AIECS.Gameplay.AiecsPlayground.Active;
        if (playground != null)
            playground.HealthOverlaySuppressed = (windowRoot != null && windowRoot.activeSelf) ||
                (airdropBrowserRoot != null && airdropBrowserRoot.activeSelf) ||
                (aiCreatureBrowserRoot != null && aiCreatureBrowserRoot.activeSelf);
        UpdateBuffTargetListIfNeeded();
        RefreshResponsiveLayoutIfCanvasChanged();
        RefreshAiecsPageIfNeeded();
        RefreshWorldCreaturesPageIfNeeded();
        if (IsPageSectionInView(GmPageId.Device))
            RefreshDevicePageIfNeeded();
        RefreshDayTimeControlIfNeeded();
        RefreshChunkStreamingStatusIfNeeded();
        HandleTeleportInput();
        hoverPropertyInspector.UpdateFromPointer();

        if (Keyboard.current?.f4Key.wasPressedThisFrame != true)
            return;

        bool anyWindowVisible = windowRoot.activeSelf ||
                                (airdropBrowserRoot != null && airdropBrowserRoot.activeSelf) ||
                                (aiCreatureBrowserRoot != null && aiCreatureBrowserRoot.activeSelf);
        SetWindowVisible(!anyWindowVisible);
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        CancelLocationSearches();
        activePageDataDirty = true;
        CancelPendingDayTimeJump();
        CancelTeleportTargeting();
        HandleBuffTargetingSceneChanged();
        HandleQuestPageSceneChanged();
        ApplyPersistedTogglePreferences();
        RestartRestorePreferences();
        if ((windowRoot != null && windowRoot.activeSelf) ||
            (airdropBrowserRoot != null && airdropBrowserRoot.activeSelf) ||
            (aiCreatureBrowserRoot != null && aiCreatureBrowserRoot.activeSelf))
        {
            RefreshRuntimeData(true);
        }
    }

    #region GM 偏好恢复

    /// <summary>立即恢复不依赖场景对象的 GM 开关。</summary>
    private void ApplyPersistedTogglePreferences()
    {
        bool teleportEnabled = GMConsolePreferences.TeleportShortcutEnabled;
        if (Mod_PlayerAdminController.TeleportToMouseShortcutEnabled != teleportEnabled)
            Mod_PlayerAdminController.ToggleTeleportToMouseShortcut();

        WorldNavigationPathDebugOverlay.SetRoutesVisible(
            GMConsolePreferences.NavigationPathVisible);

        AI_DebugOverlay.SetVisible(GMConsolePreferences.AnimalDebugOverlayVisible);
        HiveColonyDebugOverlay.SetVisible(GMConsolePreferences.HiveDebugOverlayVisible);
        worldLayerOverlay.SetMode(GMConsolePreferences.WorldLayerOverlayMode);
        RefreshWorldLayerOverlayButtons();
    }

    /// <summary>场景切换后等待玩家与区块管理器出现，再恢复运行时倍率。</summary>
    private void RestartRestorePreferences()
    {
        if (restorePreferencesCoroutine != null)
            StopCoroutine(restorePreferencesCoroutine);

        restorePreferencesCoroutine = StartCoroutine(RestorePersistedRuntimePreferences());
    }

    private IEnumerator RestorePersistedRuntimePreferences()
    {
        bool playerSpeedRestored = false;
        bool pickupMultiplierRestored = false;
        bool chunkSpeedRestored = false;
        bool chunkBudgetRestored = false;
        var retryDelay = new WaitForSecondsRealtime(0.25f);

        while (!playerSpeedRestored || !pickupMultiplierRestored || !chunkSpeedRestored || !chunkBudgetRestored)
        {
            if (!pickupMultiplierRestored && FindPreferenceItemPicker() is Mod_ItemPicker picker)
            {
                ApplyPickupMultiplierPreference(picker);
                pickupMultiplierRestored = true;
            }

            if (!playerSpeedRestored &&
                FindPreferencePlayerController() is Mod_PlayerAdminController controller)
            {
                playerSpeedRestored = controller.TrySetAdminMoveSpeedMultiplier(
                    GMConsolePreferences.PlayerMoveSpeedMultiplier,
                    out _);
            }

            ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
            if (!chunkSpeedRestored && chunkManager != null)
            {
                float requestedMultiplier = GMConsolePreferences.ChunkLoadSpeedUnlimited
                    ? float.PositiveInfinity
                    : GMConsolePreferences.ChunkLoadSpeedMultiplier;
                chunkSpeedRestored = chunkManager.TrySetChunkLoadSpeedMultiplier(
                    requestedMultiplier,
                    out _);
            }

            if (!chunkBudgetRestored && chunkManager != null)
            {
                chunkManager.RuntimeChunkCommitBudget = GMConsolePreferences.ChunkCommitCount;
                chunkManager.RuntimeChunkCommitMillisecondsBudget = GMConsolePreferences.ChunkCommitMilliseconds;
                chunkManager.RuntimeChunkPresentationStartBudget = GMConsolePreferences.ChunkBaseCount;
                chunkManager.RuntimeChunkPresentationStartMillisecondsBudget = GMConsolePreferences.ChunkBaseMilliseconds;
                chunkManager.RuntimeChunkPresentationContinuationBudget = GMConsolePreferences.ChunkDetailCount;
                chunkManager.RuntimeChunkPresentationContinuationMillisecondsBudget = GMConsolePreferences.ChunkDetailMilliseconds;
                chunkBudgetRestored = true;
            }

            if (!playerSpeedRestored || !pickupMultiplierRestored || !chunkSpeedRestored || !chunkBudgetRestored)
                yield return retryDelay;
        }

        restorePreferencesCoroutine = null;
        if (windowRoot != null && windowRoot.activeSelf)
            RefreshRuntimeData(true);
    }

    /// <summary>重试只查当前本地玩家索引，避免每次遍历场景中的全部脚本。</summary>
    private static Mod_PlayerAdminController FindPreferencePlayerController()
    {
        ItemMgr itemManager = ItemMgr.GetInstance();
        Player player = FindPreferencePlayer(itemManager != null ? itemManager.Player_DIC : null);
        return player != null ? player.GetComponentInChildren<Mod_PlayerAdminController>(true) : null;
    }

    private static Player FindPreferencePlayer(Dictionary<string, Player> players)
    {
        if (players == null)
            return null;

        foreach (KeyValuePair<string, Player> entry in players)
        {
            Player player = entry.Value;
            if (player != null && player.IsLocalProfile)
                return player;
        }

        return null;
    }

    #endregion

    private void SetWindowVisible(bool visible)
    {
        if (!visible)
            CancelLocationSearches();
        CancelTeleportTargeting();
        if (airdropBrowserRoot != null)
            airdropBrowserRoot.SetActive(false);
        if (aiCreatureBrowserRoot != null)
            aiCreatureBrowserRoot.SetActive(false);

        windowRoot.SetActive(visible);
        if (!visible)
        {
            CancelPendingDayTimeJump();
            GMConsolePreferences.SavePendingChanges();
            return;
        }

        ClampTabbedWindowToCanvas();
        RefreshRuntimeData(activePageDataDirty);
        SetStatus("GM 窗口已打开：反射命令仅作用于当前运行场景。", new Color(0.35f, 0.95f, 0.85f));
    }

    /// <summary>日志按钮与外部 UI 统一打开当前 GM 实例。</summary>
    private void OpenWindow()
    {
        SetWindowVisible(true);
    }

    #region UI

    private void BuildWindow()
    {
        BuildTabbedWindow();
    }

    #endregion

    #region Structure teleport

    private void RefreshStructureOptions()
    {
        string previousId = availableStructures.Count > 0 &&
                            selectedStructureIndex >= 0 &&
                            selectedStructureIndex < availableStructures.Count
            ? availableStructures[selectedStructureIndex].StructureId
            : null;

        availableStructures.Clear();
        StructureCatalogSO catalog = StructureCatalogSO.LoadDefault();
        if (catalog?.Definitions != null)
        {
            availableStructures.AddRange(catalog.Definitions
                .Where(definition =>
                    definition != null &&
                    definition.Enabled &&
                    !string.IsNullOrWhiteSpace(definition.StructureId))
                .OrderBy(definition => definition.DisplayName, StringComparer.Ordinal)
                .ThenBy(definition => definition.StructureId, StringComparer.Ordinal));
        }

        selectedStructureIndex = 0;
        if (!string.IsNullOrEmpty(previousId))
        {
            int previousIndex = availableStructures.FindIndex(
                definition => definition.StructureId == previousId);
            if (previousIndex >= 0)
                selectedStructureIndex = previousIndex;
        }

        UpdateStructureSelection();
    }

    private void CycleStructure(int direction)
    {
        if (availableStructures.Count == 0)
        {
            RefreshStructureOptions();
            return;
        }

        selectedStructureIndex =
            (selectedStructureIndex + direction + availableStructures.Count) %
            availableStructures.Count;
        UpdateStructureSelection();
    }

    private void UpdateStructureSelection()
    {
        if (structureSelectionText == null || structureHintText == null)
            return;

        if (availableStructures.Count == 0)
        {
            SetLocalizedText(structureSelectionText, "没有可用的遗迹定义");
            SetLocalizedText(structureHintText, "请检查 Resources/Config/StructureCatalog_Default。");
            return;
        }

        selectedStructureIndex = Mathf.Clamp(
            selectedStructureIndex,
            0,
            availableStructures.Count - 1);
        StructureDefinitionSO definition = availableStructures[selectedStructureIndex];
        string displayName = string.IsNullOrWhiteSpace(definition.DisplayName)
            ? definition.StructureId
            : definition.DisplayName;
        SetLocalizedFormat(
            structureSelectionText,
            "{0}/{1}  {2}  /  {3}",
            selectedStructureIndex + 1, availableStructures.Count, displayName, definition.StructureId);

        if (!TryGetCurrentWorldSeed(out int worldSeed))
        {
            SetLocalizedText(structureHintText, "进入游戏世界后才能按种子定位遗迹。");
            return;
        }

        int count = StructureRuntimeRegistry.Count(worldSeed, definition.StructureId);
        SetLocalizedFormat(
            structureHintText,
            "已加载记录 {0} 个；传送会按世界种子推算最近生成点，无需提前探索。",
            count);
    }

    private void TeleportToSelectedStructure()
    {
        if (availableStructures.Count == 0)
        {
            SetStatus("没有可传送的遗迹定义。", Color.yellow);
            return;
        }

        Transform playerTransform = GetLocalPlayerTransform();
        if (playerTransform == null)
        {
            SetStatus("未找到本地玩家，无法传送。", Color.yellow);
            return;
        }

        if (!TryGetCurrentWorldSeed(out int worldSeed))
        {
            SetStatus("当前世界种子尚未就绪。", Color.yellow);
            return;
        }

        StructureDefinitionSO definition = availableStructures[selectedStructureIndex];
        StructureCatalogSO catalog = StructureCatalogSO.LoadDefault();
        ChunkGenerator_Land landGenerator = FindLandGenerator();
        if (catalog == null || landGenerator == null)
        {
            SetStatus("遗迹目录或地形生成器尚未就绪。", Color.yellow);
            return;
        }

        ChunkGenerator_River riverGenerator = landGenerator.Map?.GetGenerator<ChunkGenerator_River>();
        var terrainPreview = new TerrainPreviewSampler(
            landGenerator,
            riverGenerator,
            SaveDataMgr.Instance?.GetCurrentPlanetData(),
            worldSeed);

        if (!StructureSeedLocator.TryFindNearest(
                worldSeed,
                catalog,
                definition,
                playerTransform.position,
                terrainPreview,
                out StructureRuntimeLocation location,
                out int scannedRegionCount))
        {
            SetStatusFormat(
                "种子定位失败：扫描 {0} 个区域后没有满足生成条件的位置。",
                Color.yellow, scannedRegionCount);
            return;
        }

        Vector3 destination = new(
            location.EntrancePosition.x,
            location.EntrancePosition.y,
            playerTransform.position.z);
        float distance = Vector2.Distance(playerTransform.position, destination);

        Player player = playerTransform.GetComponent<Player>() ??
                        playerTransform.GetComponentInParent<Player>();
        if (!PlayerTeleportService.TryTeleportToSurface(player, destination, succeeded =>
        {
            if (!succeeded)
            {
                SetStatus("遗迹传送未完成，请检查联机状态或稍后重试。", Color.yellow);
                return;
            }
            Debug.Log(
                $"[GM] 已按种子传送到遗迹 {location.DisplayName} ({location.StructureId})，" +
                $"入口={location.EntrancePosition}，距离={distance:F1}，扫描区域={scannedRegionCount}");
            SetWindowVisible(false);
        }))
            SetStatus("当前无法传送，请等待世界就绪或上一笔传送结束。", Color.yellow);
    }

    private static bool TryGetCurrentWorldSeed(out int worldSeed)
    {
        worldSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 0;
        return worldSeed != 0;
    }

    #endregion

    #region GM 当前分页刷新

    // 打开窗口只同步当前页；目录扫描与列表重建留给首次进入页面或场景切换。
    private void RefreshRuntimeData(bool refreshCatalogs = false)
    {
        switch (activeGmPage)
        {
            case GmPageId.Player:
                RefreshTeleportShortcutButton();
                RefreshAdminInvincibilityButton();
                RefreshPlayerMoveSpeedButton();
                RefreshPickupMultiplierControl();
                break;
            case GmPageId.Buff:
                RefreshBuffDefinitions();
                if (refreshCatalogs)
                    RefreshBuffTargetList(false);
                RefreshBuffTargetingControls();
                break;
            case GmPageId.Quests:
                if (refreshCatalogs)
                    RefreshQuestPage();
                else
                {
                    BindQuestRuntime();
                    RefreshQuestRowStates();
                }
                break;
            case GmPageId.Spawn:
                UpdateSummonHint();
                RefreshFixedStructureOptions();
                RefreshAnimalDebugOverlayButton();
                RefreshHiveDebugOverlayButton();
                break;
            case GmPageId.Aiecs:
                RefreshAiecsPage();
                break;
            case GmPageId.WorldCreatures:
                RefreshWorldCreaturesPageIfNeeded();
                break;
            case GmPageId.World:
                RefreshChunkLoadSpeedControl();
                RefreshChunkStreamingBudgetControls();
                RefreshWorldRangeControls();
                RefreshWorldWindControl();
                break;
            case GmPageId.Layers:
                RefreshNavigationPathButton();
                RefreshWorldLayerOverlayButtons();
                break;
            case GmPageId.Structures:
                RefreshPlanetTeleportOptions();
                if (refreshCatalogs)
                    RefreshStructureOptions();
                RefreshBiomeSelection();
                break;
            case GmPageId.GameEvents:
                BindGameEventManager();
                if (refreshCatalogs)
                    RebuildGameEventPage();
                break;
            case GmPageId.Commands:
                if (refreshCatalogs)
                    RebuildReflectedCommands();
                break;
            case GmPageId.Device:
                RefreshDevicePage();
                break;
        }

        activePageDataDirty = false;
    }

    #endregion

}
