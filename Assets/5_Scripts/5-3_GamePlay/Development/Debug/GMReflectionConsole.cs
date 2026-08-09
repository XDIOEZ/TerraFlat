using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using TMPro;
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
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string AdministratorName = "管理员";
    private const int MaxDiscoveredCommands = 36;

    private sealed class ReflectedCommand
    {
        public string Category;
        public string Label;
        public Component Target;
        public MethodInfo Method;
        public object[] Arguments;
    }

    private sealed class MemberRestore
    {
        public object Target;
        public MemberInfo Member;
        public object Value;
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
    }

    private readonly List<ReflectedCommand> commands = new List<ReflectedCommand>();
    private readonly List<MemberRestore> f4AdminRestores = new List<MemberRestore>();
    private readonly List<AirdropItemEntry> availableAirdropItems = new List<AirdropItemEntry>();
    private readonly List<AiCreatureEntry> availableAiCreatures = new List<AiCreatureEntry>();
    private readonly List<StructureDefinitionSO> availableStructures = new List<StructureDefinitionSO>();

    private GameObject windowRoot;
    private GameObject airdropBrowserRoot;
    private GameObject aiCreatureBrowserRoot;
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
    private Transform commandGrid;
    private Transform airdropItemGrid;
    private Transform aiCreatureGrid;
    private Sprite dropMarkerSprite;
    private Coroutine restoreAdminCoroutine;
    private Coroutine restorePreferencesCoroutine;
    private bool legacyF4WasRebound;
    private int selectedStructureIndex;
    private static TMP_FontAsset uiFont;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<GMReflectionConsole>() != null)
            return;

        GameObject root = new GameObject("[GM Reflection Console]");
        root.AddComponent<GMReflectionConsole>();
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        EnsureEventSystem();
        BuildWindow();
        RebindLegacyF4Conflict();
        ApplyPersistedTogglePreferences();
        RestartRestorePreferences();
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (restorePreferencesCoroutine != null)
            StopCoroutine(restorePreferencesCoroutine);
        DisposeBuffTargeting();
        UnbindGameEventManager();
    }

    private void Update()
    {
        UpdateBuffTargetListIfNeeded();

        bool f4Pressed = Keyboard.current != null
            ? Keyboard.current.f4Key.wasPressedThisFrame
            : Input.GetKeyDown(KeyCode.F4);

        if (!f4Pressed)
            return;

        // PlayerAdminController 的 F4 是硬编码快捷键。此组件以更早执行顺序临时
        // 取消管理员标记，避免一次 F4 同时触发“手持 +9999”。
        GuardLegacyAdminF4ForThisFrame();
        bool anyWindowVisible = windowRoot.activeSelf ||
                                (airdropBrowserRoot != null && airdropBrowserRoot.activeSelf) ||
                                (aiCreatureBrowserRoot != null && aiCreatureBrowserRoot.activeSelf);
        SetWindowVisible(!anyWindowVisible);
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        RebindLegacyF4Conflict();
        HandleBuffTargetingSceneChanged();
        ApplyPersistedTogglePreferences();
        RestartRestorePreferences();
        if ((windowRoot != null && windowRoot.activeSelf) ||
            (airdropBrowserRoot != null && airdropBrowserRoot.activeSelf) ||
            (aiCreatureBrowserRoot != null && aiCreatureBrowserRoot.activeSelf))
        {
            RefreshRuntimeData();
        }
    }

    #region GM 偏好恢复

    /// <summary>立即恢复不依赖场景对象的 GM 开关。</summary>
    private static void ApplyPersistedTogglePreferences()
    {
        bool teleportEnabled = GMConsolePreferences.TeleportShortcutEnabled;
        if (PlayerAdminController.TeleportToMouseShortcutEnabled != teleportEnabled)
            PlayerAdminController.ToggleTeleportToMouseShortcut();

        WorldNavigationPathDebugOverlay.SetRoutesVisible(
            GMConsolePreferences.NavigationPathVisible);
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
        bool chunkSpeedRestored = false;
        var retryDelay = new WaitForSecondsRealtime(0.25f);

        while (!playerSpeedRestored || !chunkSpeedRestored)
        {
            if (!playerSpeedRestored &&
                FindFirstComponent("PlayerAdminController") is PlayerAdminController controller)
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

            if (!playerSpeedRestored || !chunkSpeedRestored)
                yield return retryDelay;
        }

        restorePreferencesCoroutine = null;
        if (windowRoot != null && windowRoot.activeSelf)
            RefreshRuntimeData();
    }

    #endregion

    private void SetWindowVisible(bool visible)
    {
        if (airdropBrowserRoot != null)
            airdropBrowserRoot.SetActive(false);
        if (aiCreatureBrowserRoot != null)
            aiCreatureBrowserRoot.SetActive(false);

        windowRoot.SetActive(visible);
        if (!visible)
            return;

        ClampTabbedWindowToCanvas();
        RefreshRuntimeData();
        SetStatus("GM 窗口已打开：反射命令仅作用于当前运行场景。", new Color(0.35f, 0.95f, 0.85f));
    }

    #region UI

    private void BuildDesktopWindow()
    {
        GameObject canvasObject = new GameObject("GM Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        windowRoot = CreateUiObject("GM Desktop Tool", canvasObject.transform);
        RectTransform windowRect = windowRoot.GetComponent<RectTransform>();
        windowRect.anchorMin = windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.sizeDelta = new Vector2(1100f, 690f);

        Image windowImage = windowRoot.AddComponent<Image>();
        windowImage.color = new Color(0.93f, 0.93f, 0.93f, 1f);
        Outline windowOutline = windowRoot.AddComponent<Outline>();
        windowOutline.effectColor = new Color(0.38f, 0.38f, 0.38f, 1f);
        windowOutline.effectDistance = new Vector2(1f, -1f);

        VerticalLayoutGroup windowLayout = windowRoot.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(7, 7, 7, 7);
        windowLayout.spacing = 4f;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = false;
        windowLayout.childForceExpandWidth = true;
        windowLayout.childForceExpandHeight = false;

        GameObject titleBar = CreateUiObject("Title Bar", windowRoot.transform);
        titleBar.AddComponent<LayoutElement>().preferredHeight = 36f;
        titleBar.AddComponent<Image>().color = new Color(0.975f, 0.975f, 0.975f, 1f);
        HorizontalLayoutGroup titleLayout = titleBar.AddComponent<HorizontalLayoutGroup>();
        titleLayout.padding = new RectOffset(10, 6, 4, 4);
        titleLayout.spacing = 8f;
        titleLayout.childAlignment = TextAnchor.MiddleLeft;
        titleLayout.childControlWidth = true;
        titleLayout.childForceExpandWidth = false;

        TextMeshProUGUI title = CreateText(titleBar.transform, "FlatWorld GM Tool  /  测试控制台", 17f, new Color(0.12f, 0.12f, 0.12f));
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        TextMeshProUGUI shortcut = CreateText(titleBar.transform, "F4 开启 / 关闭", 12f, new Color(0.35f, 0.35f, 0.35f));
        shortcut.alignment = TextAlignmentOptions.Right;
        shortcut.gameObject.AddComponent<LayoutElement>().preferredWidth = 112f;
        CreateButton(titleBar.transform, "×", () => SetWindowVisible(false), 32f, 27f);

        GameObject menuBar = CreateUiObject("Menu Bar", windowRoot.transform);
        menuBar.AddComponent<LayoutElement>().preferredHeight = 25f;
        menuBar.AddComponent<Image>().color = new Color(0.965f, 0.965f, 0.965f, 1f);
        HorizontalLayoutGroup menuLayout = menuBar.AddComponent<HorizontalLayoutGroup>();
        menuLayout.padding = new RectOffset(8, 8, 1, 1);
        menuLayout.spacing = 2f;
        menuLayout.childAlignment = TextAnchor.MiddleLeft;
        menuLayout.childControlWidth = false;
        menuLayout.childForceExpandWidth = false;
        CreateDesktopMenu(menuBar.transform, "文件", 48f);
        CreateDesktopMenu(menuBar.transform, "工具", 48f);
        CreateDesktopMenu(menuBar.transform, "帮助", 48f);

        GameObject tabBar = CreateUiObject("Tabs", windowRoot.transform);
        tabBar.AddComponent<LayoutElement>().preferredHeight = 30f;
        HorizontalLayoutGroup tabLayout = tabBar.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 2f;
        tabLayout.childAlignment = TextAnchor.LowerLeft;
        tabLayout.childControlWidth = false;
        tabLayout.childForceExpandWidth = false;
        CreateDesktopTab(tabBar.transform, "物品与角色", true, 120f);
        CreateDesktopTab(tabBar.transform, "系统控制", false, 96f);
        CreateDesktopTab(tabBar.transform, "反射命令", false, 96f);

        GameObject mainColumns = CreateUiObject("Main Content", windowRoot.transform);
        mainColumns.AddComponent<LayoutElement>().flexibleHeight = 1f;
        HorizontalLayoutGroup columnsLayout = mainColumns.AddComponent<HorizontalLayoutGroup>();
        columnsLayout.spacing = 7f;
        columnsLayout.childControlWidth = true;
        columnsLayout.childControlHeight = true;
        columnsLayout.childForceExpandWidth = true;
        columnsLayout.childForceExpandHeight = true;

        GameObject leftColumn = CreateUiObject("Left Column", mainColumns.transform);
        LayoutElement leftColumnLayout = leftColumn.AddComponent<LayoutElement>();
        leftColumnLayout.preferredWidth = 372f;
        leftColumnLayout.flexibleWidth = 0f;
        VerticalLayoutGroup leftLayout = leftColumn.AddComponent<VerticalLayoutGroup>();
        leftLayout.spacing = 7f;
        leftLayout.childControlWidth = true;
        leftLayout.childControlHeight = true;
        leftLayout.childForceExpandWidth = true;
        leftLayout.childForceExpandHeight = false;

        GameObject airdropBox = CreateDesktopGroup(leftColumn.transform, "物品空投");
        airdropBox.AddComponent<LayoutElement>().preferredHeight = 192f;
        GameObject itemRow = CreateDesktopRow(airdropBox.transform, 31f);
        CreateDesktopFieldLabel(itemRow.transform, "物品 ID：", 58f);
        itemIdInput = CreateInputField(itemRow.transform, "输入物品 ID", 270f, false);

        GameObject amountRow = CreateDesktopRow(airdropBox.transform, 31f);
        CreateDesktopFieldLabel(amountRow.transform, "数量：", 58f);
        amountInput = CreateInputField(amountRow.transform, "数量", 78f, true);
        amountInput.text = "1";
        CreateButton(amountRow.transform, "召唤", StartAirdrop, 92f, 28f);
        CreateButton(amountRow.transform, "刷新列表", RefreshItemIds, 104f, 28f);

        itemHintText = CreateText(airdropBox.transform, "正在读取物品列表…", 12f, new Color(0.36f, 0.36f, 0.36f));
        itemHintText.enableWordWrapping = true;
        itemHintText.overflowMode = TextOverflowModes.Ellipsis;
        itemHintText.gameObject.AddComponent<LayoutElement>().preferredHeight = 50f;

        GameObject quickBox = CreateDesktopGroup(leftColumn.transform, "快捷控制");
        quickBox.AddComponent<LayoutElement>().preferredHeight = 218f;
        GameObject quickGrid = CreateUiObject("Quick Actions", quickBox.transform);
        quickGrid.AddComponent<LayoutElement>().preferredHeight = 160f;
        GridLayoutGroup quickGridLayout = quickGrid.AddComponent<GridLayoutGroup>();
        quickGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        quickGridLayout.constraintCount = 2;
        quickGridLayout.cellSize = new Vector2(168f, 32f);
        quickGridLayout.spacing = new Vector2(7f, 6f);
        CreateButton(quickGrid.transform, "设为管理员", SetAdministrator, 0f, 32f);
        CreateButton(quickGrid.transform, "传送至鼠标", () => InvokeByTypeName("Mod_PlayerTraits", "TeleportToMousePosition"), 0f, 32f);
        CreateButton(quickGrid.transform, "创造背包", () => InvokeByTypeName("Mod_PlayerTraits", "InitializeCreativeInventoryForAdmin"), 0f, 32f);
        CreateButton(quickGrid.transform, "手持 +9999", () => InvokeByTypeName("PlayerAdminController", "AddAmountToCurrentHandItem", 9999f), 0f, 32f);
        CreateButton(quickGrid.transform, "背包 +999", () => InvokeByTypeName("PlayerAdminController", "AddAmountToAllBagItems", 999f), 0f, 32f);
        CreateButton(quickGrid.transform, "时间 -0.5", () => InvokeByTypeName("PlayerAdminController", "TryUpdateTimeScale", -0.5f), 0f, 32f);
        CreateButton(quickGrid.transform, "时间重置", () => InvokeByTypeName("PlayerAdminController", "ResetTimeScale"), 0f, 32f);
        CreateButton(quickGrid.transform, "区块距离 +1", () => InvokeByTypeName("PlayerAdminController", "IncreaseAdminChunkLoadDistance"), 0f, 32f);

        GameObject commandBox = CreateDesktopGroup(mainColumns.transform, "反射调试命令");
        LayoutElement commandBoxLayout = commandBox.AddComponent<LayoutElement>();
        commandBoxLayout.flexibleWidth = 1f;
        commandBoxLayout.flexibleHeight = 1f;
        GameObject commandHeader = CreateDesktopRow(commandBox.transform, 31f);
        commandCountText = CreateText(commandHeader.transform, "正在扫描反射命令…", 13f, new Color(0.16f, 0.16f, 0.16f));
        commandCountText.fontStyle = FontStyles.Bold;
        commandCountText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton(commandHeader.transform, "重新扫描", RebuildReflectedCommands, 96f, 28f);
        commandGrid = CreateScrollGrid(commandBox.transform);

        statusText = CreateText(windowRoot.transform, "按 F4 打开或关闭此窗口。", 12f, new Color(0.24f, 0.24f, 0.24f));
        statusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 21f;
        windowRoot.SetActive(false);
    }

    private void BuildWindow()
    {
        BuildTabbedWindow();
    }

    // 旧版单页布局暂时保留为回归参考，不再由运行时入口创建。
    private void BuildLegacyWindow()
    {
        GameObject canvasObject = new GameObject("GM Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        windowRoot = CreateUiObject("GM Window", canvasObject.transform);
        RectTransform panelRect = windowRoot.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(860f, 730f);

        Image panelImage = windowRoot.AddComponent<Image>();
        panelImage.color = new Color(0.031f, 0.082f, 0.114f, 0.985f);
        Outline panelOutline = windowRoot.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.48f);
        panelOutline.effectDistance = new Vector2(1f, -1f);

        VerticalLayoutGroup panelLayout = windowRoot.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(20, 20, 18, 18);
        panelLayout.spacing = 8f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        GameObject header = CreateUiObject("Header", windowRoot.transform);
        LayoutElement headerLayout = header.AddComponent<LayoutElement>();
        headerLayout.preferredHeight = 54f;
        Image headerImage = header.AddComponent<Image>();
        headerImage.color = new Color(0.063f, 0.153f, 0.188f, 1f);
        HorizontalLayoutGroup headerGroup = header.AddComponent<HorizontalLayoutGroup>();
        headerGroup.padding = new RectOffset(16, 12, 7, 7);
        headerGroup.spacing = 12f;
        headerGroup.childAlignment = TextAnchor.MiddleLeft;
        headerGroup.childControlWidth = true;
        headerGroup.childForceExpandWidth = false;
        headerGroup.childControlHeight = true;

        TextMeshProUGUI title = CreateText(header.transform, "GM 管理工具", 20f, new Color(0.95f, 0.91f, 0.84f));
        LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
        titleLayout.flexibleWidth = 1f;
        title.fontStyle = FontStyles.Bold;

        TextMeshProUGUI shortcut = CreateText(header.transform, "F4  开启 / 关闭", 12f, new Color(0.66f, 0.71f, 0.71f));
        shortcut.gameObject.AddComponent<LayoutElement>().preferredWidth = 120f;
        shortcut.alignment = TextAlignmentOptions.Right;

        Button closeButton = CreateButton(header.transform, "关闭", () => SetWindowVisible(false), 64f, 34f);
        closeButton.GetComponent<Image>().color = new Color(0.09f, 0.17f, 0.20f, 1f);

        statusText = CreateText(windowRoot.transform, "按 F4 打开或关闭此窗口。", 12f, new Color(0.66f, 0.71f, 0.71f));
        LayoutElement statusLayout = statusText.gameObject.AddComponent<LayoutElement>();
        statusLayout.preferredHeight = 22f;

        CreateSectionTitle(windowRoot.transform, "测试召唤");
        GameObject airdropRow = CreateUiObject("Airdrop Row", windowRoot.transform);
        LayoutElement airdropLayout = airdropRow.AddComponent<LayoutElement>();
        airdropLayout.preferredHeight = 42f;
        HorizontalLayoutGroup airdropGroup = airdropRow.AddComponent<HorizontalLayoutGroup>();
        airdropGroup.spacing = 8f;
        airdropGroup.childControlWidth = false;
        airdropGroup.childControlHeight = true;
        airdropGroup.childForceExpandWidth = false;

        Button airdropButton = CreateButton(airdropRow.transform, "打开物品面板", OpenAirdropBrowser, 180f, 38f);
        airdropButton.GetComponent<Image>().color = new Color(0.66f, 0.32f, 0.15f, 1f);

        Button creatureButton = CreateButton(airdropRow.transform, "打开 AI 生物面板", OpenAiCreatureBrowser, 190f, 38f);
        creatureButton.GetComponent<Image>().color = new Color(0.10f, 0.35f, 0.37f, 1f);

        itemHintText = CreateText(windowRoot.transform, "可搜索并召唤物品或 AI 生物到玩家附近。", 12f, new Color(0.66f, 0.71f, 0.71f));
        itemHintText.enableWordWrapping = false;
        itemHintText.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement hintLayout = itemHintText.gameObject.AddComponent<LayoutElement>();
        hintLayout.preferredHeight = 20f;

        CreateSectionTitle(windowRoot.transform, "常用操作");
        GameObject quickGrid = CreateUiObject("Quick Actions", windowRoot.transform);
        LayoutElement quickLayout = quickGrid.AddComponent<LayoutElement>();
        quickLayout.preferredHeight = 124f;
        GridLayoutGroup quickGridLayout = quickGrid.AddComponent<GridLayoutGroup>();
        quickGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        quickGridLayout.constraintCount = 4;
        quickGridLayout.cellSize = new Vector2(187f, 36f);
        quickGridLayout.spacing = new Vector2(8f, 8f);

        CreateButton(quickGrid.transform, "设为管理员", SetAdministrator, 0f, 35f);
        CreateButton(quickGrid.transform, "传送至鼠标", () => InvokeByTypeName("Mod_PlayerTraits", "TeleportToMousePosition"), 0f, 35f);
        teleportShortcutButton = CreateButton(quickGrid.transform, "Ctrl+T 传送：开", ToggleTeleportShortcut, 0f, 35f);
        RefreshTeleportShortcutButton();
        playerMoveSpeedButton = CreateButton(quickGrid.transform, "玩家移速：1x", CyclePlayerMoveSpeed, 0f, 35f);
        RefreshPlayerMoveSpeedButton();
        CreateButton(quickGrid.transform, "创造背包", () => InvokeByTypeName("Mod_PlayerTraits", "InitializeCreativeInventoryForAdmin"), 0f, 35f);
        CreateButton(quickGrid.transform, "手持 +9999", () => InvokeByTypeName("PlayerAdminController", "AddAmountToCurrentHandItem", 9999f), 0f, 35f);
        CreateButton(quickGrid.transform, "背包 +999", () => InvokeByTypeName("PlayerAdminController", "AddAmountToAllBagItems", 999f), 0f, 35f);
        CreateButton(quickGrid.transform, "时间 -0.5", () => InvokeByTypeName("PlayerAdminController", "TryUpdateTimeScale", -0.5f), 0f, 35f);
        CreateButton(quickGrid.transform, "时间重置", () => InvokeByTypeName("PlayerAdminController", "ResetTimeScale"), 0f, 35f);
        CreateButton(quickGrid.transform, "区块距离 +1", () => InvokeByTypeName("PlayerAdminController", "IncreaseAdminChunkLoadDistance"), 0f, 35f);
        navigationPathButton = CreateButton(quickGrid.transform, "AI 路线提示：关", ToggleNavigationPathHints, 0f, 35f);
        RefreshNavigationPathButton();

        CreateSectionTitle(windowRoot.transform, "遗迹传送");
        GameObject structureRow = CreateUiObject("Structure Teleport Row", windowRoot.transform);
        LayoutElement structureRowLayout = structureRow.AddComponent<LayoutElement>();
        structureRowLayout.preferredHeight = 40f;
        HorizontalLayoutGroup structureRowGroup = structureRow.AddComponent<HorizontalLayoutGroup>();
        structureRowGroup.spacing = 8f;
        structureRowGroup.childControlWidth = false;
        structureRowGroup.childControlHeight = true;
        structureRowGroup.childForceExpandWidth = false;

        CreateButton(structureRow.transform, "‹", () => CycleStructure(-1), 38f, 38f);
        structureSelectionText = CreateValueDisplay(structureRow.transform, "正在读取遗迹目录…", 410f, 38f);
        CreateButton(structureRow.transform, "›", () => CycleStructure(1), 38f, 38f);
        Button teleportButton = CreateButton(
            structureRow.transform,
            "传送到最近遗迹",
            TeleportToSelectedStructure,
            170f,
            38f);
        teleportButton.GetComponent<Image>().color = new Color(0.66f, 0.32f, 0.15f, 1f);
        CreateButton(structureRow.transform, "刷新", RefreshStructureOptions, 76f, 38f);

        structureHintText = CreateText(
            windowRoot.transform,
            "按世界种子推算未探索区域中的最近遗迹生成点。",
            12f,
            new Color(0.66f, 0.71f, 0.71f));
        structureHintText.enableWordWrapping = false;
        structureHintText.overflowMode = TextOverflowModes.Ellipsis;
        structureHintText.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

        GameObject commandHeader = CreateUiObject("Command Header", windowRoot.transform);
        LayoutElement commandHeaderLayout = commandHeader.AddComponent<LayoutElement>();
        commandHeaderLayout.preferredHeight = 30f;
        HorizontalLayoutGroup commandHeaderGroup = commandHeader.AddComponent<HorizontalLayoutGroup>();
        commandHeaderGroup.childAlignment = TextAnchor.MiddleLeft;
        commandHeaderGroup.childControlWidth = false;
        commandHeaderGroup.childForceExpandWidth = false;
        commandHeaderGroup.spacing = 8f;

        commandCountText = CreateText(commandHeader.transform, "反射调试命令", 14f, new Color(0.95f, 0.91f, 0.84f));
        commandCountText.fontStyle = FontStyles.Bold;
        commandCountText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton(commandHeader.transform, "重新扫描", RebuildReflectedCommands, 88f, 30f);

        commandGrid = CreateScrollGrid(windowRoot.transform);
        BuildAirdropBrowser(canvasObject.transform);
        BuildAiCreatureBrowser(canvasObject.transform);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        windowRoot.SetActive(false);
    }

    private void BuildAirdropBrowser(Transform canvasTransform)
    {
        airdropBrowserRoot = CreateUiObject("GM Airdrop Browser", canvasTransform);
        RectTransform panelRect = airdropBrowserRoot.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(1120f, 760f);

        Image panelImage = airdropBrowserRoot.AddComponent<Image>();
        panelImage.color = new Color(0.031f, 0.082f, 0.114f, 0.99f);
        Outline panelOutline = airdropBrowserRoot.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.55f);
        panelOutline.effectDistance = new Vector2(1f, -1f);

        VerticalLayoutGroup panelLayout = airdropBrowserRoot.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(20, 20, 18, 18);
        panelLayout.spacing = 10f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        GameObject header = CreateUiObject("Header", airdropBrowserRoot.transform);
        header.AddComponent<LayoutElement>().preferredHeight = 54f;
        Image headerImage = header.AddComponent<Image>();
        headerImage.color = new Color(0.063f, 0.153f, 0.188f, 1f);
        HorizontalLayoutGroup headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(16, 12, 7, 7);
        headerLayout.spacing = 12f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;

        TextMeshProUGUI title = CreateText(header.transform, "物品空投", 20f, new Color(0.95f, 0.91f, 0.84f));
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        TextMeshProUGUI instruction = CreateText(
            header.transform,
            "选择物品后直接空投到玩家附近",
            12f,
            new Color(0.66f, 0.71f, 0.71f));
        instruction.alignment = TextAlignmentOptions.Right;
        instruction.gameObject.AddComponent<LayoutElement>().preferredWidth = 220f;
        CreateButton(header.transform, "返回", CloseAirdropBrowser, 64f, 34f);

        GameObject toolbar = CreateUiObject("Toolbar", airdropBrowserRoot.transform);
        toolbar.AddComponent<LayoutElement>().preferredHeight = 42f;
        HorizontalLayoutGroup toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 8f;
        toolbarLayout.childAlignment = TextAnchor.MiddleLeft;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;

        airdropSearchInput = CreateInputField(toolbar.transform, "搜索物品名称或 ID", 560f, false);
        airdropSearchInput.onValueChanged.AddListener(_ => RebuildAirdropItemGrid());

        TextMeshProUGUI amountLabel = CreateText(
            toolbar.transform,
            "数量",
            13f,
            new Color(0.82f, 0.82f, 0.78f));
        amountLabel.alignment = TextAlignmentOptions.MidlineRight;
        amountLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;

        amountInput = CreateInputField(toolbar.transform, "数量", 90f, true);
        amountInput.text = "1";
        CreateButton(toolbar.transform, "刷新物品", RefreshItemIds, 96f, 38f);

        airdropBrowserCountText = CreateText(
            toolbar.transform,
            "正在读取物品…",
            12f,
            new Color(0.66f, 0.71f, 0.71f));
        airdropBrowserCountText.alignment = TextAlignmentOptions.MidlineRight;
        airdropBrowserCountText.enableWordWrapping = false;
        airdropBrowserCountText.overflowMode = TextOverflowModes.Ellipsis;
        airdropBrowserCountText.gameObject.AddComponent<LayoutElement>().preferredWidth = 240f;

        airdropItemGrid = CreateCatalogScrollGrid(airdropBrowserRoot.transform, "Airdrop Item Scroll");
        // 让空投物品按钮在可用区域内水平居中，减少右侧多余留白。
        GridLayoutGroup airdropGridLayout = airdropItemGrid.GetComponent<GridLayoutGroup>();
        if (airdropGridLayout != null)
            airdropGridLayout.childAlignment = TextAnchor.UpperCenter;

        airdropBrowserStatusText = CreateText(
            airdropBrowserRoot.transform,
            "点击任意物品即可空投。",
            12f,
            new Color(0.66f, 0.71f, 0.71f));
        airdropBrowserStatusText.enableWordWrapping = false;
        airdropBrowserStatusText.overflowMode = TextOverflowModes.Ellipsis;
        airdropBrowserStatusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        airdropBrowserRoot.SetActive(false);
    }

    private void BuildAiCreatureBrowser(Transform canvasTransform)
    {
        aiCreatureBrowserRoot = CreateUiObject("GM AI Creature Browser", canvasTransform);
        RectTransform panelRect = aiCreatureBrowserRoot.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(1120f, 760f);

        Image panelImage = aiCreatureBrowserRoot.AddComponent<Image>();
        panelImage.color = new Color(0.031f, 0.082f, 0.114f, 0.99f);
        Outline panelOutline = aiCreatureBrowserRoot.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.21f, 0.72f, 0.68f, 0.55f);
        panelOutline.effectDistance = new Vector2(1f, -1f);

        VerticalLayoutGroup panelLayout = aiCreatureBrowserRoot.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(20, 20, 18, 18);
        panelLayout.spacing = 10f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        GameObject header = CreateUiObject("Header", aiCreatureBrowserRoot.transform);
        header.AddComponent<LayoutElement>().preferredHeight = 54f;
        Image headerImage = header.AddComponent<Image>();
        headerImage.color = new Color(0.063f, 0.153f, 0.188f, 1f);
        HorizontalLayoutGroup headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(16, 12, 7, 7);
        headerLayout.spacing = 12f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;

        TextMeshProUGUI title = CreateText(header.transform, "AI 生物召唤", 20f, new Color(0.95f, 0.91f, 0.84f));
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        TextMeshProUGUI instruction = CreateText(
            header.transform,
            "点击生物后生成到玩家附近",
            12f,
            new Color(0.66f, 0.71f, 0.71f));
        instruction.alignment = TextAlignmentOptions.Right;
        instruction.gameObject.AddComponent<LayoutElement>().preferredWidth = 240f;
        CreateButton(header.transform, "返回", CloseAiCreatureBrowser, 64f, 34f);

        GameObject toolbar = CreateUiObject("Toolbar", aiCreatureBrowserRoot.transform);
        toolbar.AddComponent<LayoutElement>().preferredHeight = 42f;
        HorizontalLayoutGroup toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 8f;
        toolbarLayout.childAlignment = TextAnchor.MiddleLeft;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;

        aiCreatureSearchInput = CreateInputField(toolbar.transform, "搜索生物名称或 ID", 560f, false);
        aiCreatureSearchInput.onValueChanged.AddListener(_ => RebuildAiCreatureGrid());

        TextMeshProUGUI amountLabel = CreateText(
            toolbar.transform,
            "数量",
            13f,
            new Color(0.82f, 0.82f, 0.78f));
        amountLabel.alignment = TextAlignmentOptions.MidlineRight;
        amountLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;

        aiCreatureAmountInput = CreateInputField(toolbar.transform, "1-20", 90f, true);
        aiCreatureAmountInput.text = "1";
        CreateButton(toolbar.transform, "刷新生物", RefreshAiCreatureIds, 96f, 38f);

        aiCreatureBrowserCountText = CreateText(
            toolbar.transform,
            "正在读取生物…",
            12f,
            new Color(0.66f, 0.71f, 0.71f));
        aiCreatureBrowserCountText.alignment = TextAlignmentOptions.MidlineRight;
        aiCreatureBrowserCountText.enableWordWrapping = false;
        aiCreatureBrowserCountText.overflowMode = TextOverflowModes.Ellipsis;
        aiCreatureBrowserCountText.gameObject.AddComponent<LayoutElement>().preferredWidth = 240f;

        aiCreatureGrid = CreateCatalogScrollGrid(aiCreatureBrowserRoot.transform, "AI Creature Scroll");

        aiCreatureBrowserStatusText = CreateText(
            aiCreatureBrowserRoot.transform,
            "点击任意生物即可召唤；批量召唤会自动散布，避免重叠。",
            12f,
            new Color(0.66f, 0.71f, 0.71f));
        aiCreatureBrowserStatusText.enableWordWrapping = false;
        aiCreatureBrowserStatusText.overflowMode = TextOverflowModes.Ellipsis;
        aiCreatureBrowserStatusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        aiCreatureBrowserRoot.SetActive(false);
    }

    private static Transform CreateCatalogScrollGrid(Transform parent, string objectName)
    {
        GameObject scrollObject = CreateUiObject(objectName, parent);
        Image scrollImage = scrollObject.AddComponent<Image>();
        scrollImage.color = new Color(0.018f, 0.052f, 0.068f, 1f);
        Outline outline = scrollObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.51f, 0.58f, 0.58f, 0.32f);
        outline.effectDistance = new Vector2(1f, -1f);
        LayoutElement scrollLayout = scrollObject.AddComponent<LayoutElement>();
        scrollLayout.flexibleHeight = 1f;
        scrollLayout.minHeight = 560f;
        scrollLayout.preferredHeight = 560f;

        ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 38f;

        GameObject viewport = CreateUiObject("Viewport", scrollObject.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(10f, 10f);
        viewportRect.offsetMax = new Vector2(-24f, -10f);
        viewport.AddComponent<RectMask2D>();
        scroll.viewport = viewportRect;

        GameObject content = CreateUiObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        GridLayoutGroup grid = content.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 8;
        grid.cellSize = new Vector2(118f, 118f);
        grid.spacing = new Vector2(8f, 8f);
        grid.padding = new RectOffset(2, 2, 2, 8);
        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRect;

        GameObject scrollbarObject = CreateUiObject("Scrollbar", scrollObject.transform);
        RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = new Vector2(-16f, 10f);
        scrollbarRect.offsetMax = new Vector2(-6f, -10f);
        Image scrollbarBackground = scrollbarObject.AddComponent<Image>();
        scrollbarBackground.color = new Color(0.08f, 0.13f, 0.15f, 1f);

        GameObject slidingArea = CreateUiObject("Sliding Area", scrollbarObject.transform);
        RectTransform slidingRect = slidingArea.GetComponent<RectTransform>();
        slidingRect.anchorMin = Vector2.zero;
        slidingRect.anchorMax = Vector2.one;
        slidingRect.offsetMin = new Vector2(2f, 2f);
        slidingRect.offsetMax = new Vector2(-2f, -2f);

        GameObject handle = CreateUiObject("Handle", slidingArea.transform);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = new Color(0.42f, 0.54f, 0.56f, 1f);

        Scrollbar scrollbar = scrollbarObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return content.transform;
    }

    private static GameObject CreateDesktopGroup(Transform parent, string title)
    {
        GameObject group = CreateUiObject(title, parent);
        Image groupImage = group.AddComponent<Image>();
        groupImage.color = new Color(0.955f, 0.955f, 0.955f, 1f);
        Outline outline = group.AddComponent<Outline>();
        outline.effectColor = new Color(0.62f, 0.62f, 0.62f, 1f);
        outline.effectDistance = new Vector2(1f, -1f);

        VerticalLayoutGroup layout = group.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI header = CreateText(group.transform, title, 14f, new Color(0.12f, 0.12f, 0.12f));
        header.fontStyle = FontStyles.Bold;
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
        return group;
    }

    private static GameObject CreateDesktopRow(Transform parent, float height)
    {
        GameObject row = CreateUiObject("Form Row", parent);
        row.AddComponent<LayoutElement>().preferredHeight = height;
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        return row;
    }

    private static void CreateDesktopFieldLabel(Transform parent, string value, float width)
    {
        TextMeshProUGUI label = CreateText(parent, value, 13f, new Color(0.18f, 0.18f, 0.18f));
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
        label.alignment = TextAlignmentOptions.MidlineRight;
    }

    private static void CreateDesktopMenu(Transform parent, string label, float width)
    {
        TextMeshProUGUI menu = CreateText(parent, label, 13f, new Color(0.16f, 0.16f, 0.16f));
        menu.alignment = TextAlignmentOptions.Center;
        menu.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
    }

    private static void CreateDesktopTab(Transform parent, string label, bool selected, float width)
    {
        GameObject tab = CreateUiObject(label, parent);
        Image tabImage = tab.AddComponent<Image>();
        tabImage.color = selected
            ? new Color(0.985f, 0.985f, 0.985f, 1f)
            : new Color(0.86f, 0.86f, 0.86f, 1f);
        tab.AddComponent<LayoutElement>().preferredWidth = width;

        TextMeshProUGUI text = CreateText(tab.transform, label, 13f, new Color(0.14f, 0.14f, 0.14f));
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        GameObject value = new GameObject(objectName, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        return value;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string value, float fontSize, Color color)
    {
        GameObject textObject = CreateUiObject("Text", parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = ResolveUiFont();
        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        return text;
    }

    private static TMP_FontAsset ResolveUiFont()
    {
        if (uiFont != null)
            return uiFont;

        List<TMP_FontAsset> configuredFallbacks = TMP_Settings.fallbackFontAssets;
        if (configuredFallbacks != null)
        {
            for (int i = 0; i < configuredFallbacks.Count; i++)
            {
                TMP_FontAsset candidate = configuredFallbacks[i];
                if (candidate != null && candidate.HasCharacter(0x5F00))
                {
                    uiFont = candidate;
                    return uiFont;
                }
            }
        }

        TMP_FontAsset[] loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        for (int i = 0; i < loadedFonts.Length; i++)
        {
            TMP_FontAsset candidate = loadedFonts[i];
            if (candidate == null)
                continue;

            string fontName = candidate.name;
            if (fontName.IndexOf("zh_hans", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fontName.IndexOf("chinese", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fontName.IndexOf("cjk", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                uiFont = candidate;
                return uiFont;
            }
        }

        uiFont = TMP_Settings.defaultFontAsset;
        return uiFont;
    }

    private static Button CreateButton(Transform parent, string label, UnityAction action, float width, float height)
    {
        GameObject buttonObject = CreateUiObject(label, parent);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.094f, 0.212f, 0.251f, 1f);
        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.51f, 0.58f, 0.58f, 0.28f);
        outline.effectDistance = new Vector2(1f, -1f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.88f, 0.72f, 1f);
        colors.pressedColor = new Color(0.78f, 0.72f, 0.64f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = 0.1f;
        button.colors = colors;

        LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        if (width > 0f)
            layout.preferredWidth = width;

        TextMeshProUGUI text = CreateText(buttonObject.transform, label, 13f, new Color(0.95f, 0.91f, 0.84f));
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return button;
    }

    private static TMP_InputField CreateInputField(Transform parent, string placeholder, float width, bool integerOnly)
    {
        GameObject fieldObject = CreateUiObject("Input " + placeholder, parent);
        Image image = fieldObject.AddComponent<Image>();
        image.color = new Color(0.028f, 0.071f, 0.094f, 1f);
        Outline outline = fieldObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.51f, 0.58f, 0.58f, 0.28f);
        outline.effectDistance = new Vector2(1f, -1f);
        TMP_InputField field = fieldObject.AddComponent<TMP_InputField>();
        field.targetGraphic = image;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.contentType = integerOnly ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.Standard;
        field.gameObject.AddComponent<LayoutElement>().preferredWidth = width;

        GameObject textArea = CreateUiObject("Text Area", fieldObject.transform);
        RectTransform areaRect = textArea.GetComponent<RectTransform>();
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(10f, 3f);
        areaRect.offsetMax = new Vector2(-10f, -3f);
        RectMask2D mask = textArea.AddComponent<RectMask2D>();
        field.textViewport = areaRect;

        TextMeshProUGUI text = CreateText(textArea.transform, string.Empty, 13f, new Color(0.95f, 0.91f, 0.84f));
        text.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        field.textComponent = text;

        TextMeshProUGUI hint = CreateText(textArea.transform, placeholder, 13f, new Color(0.51f, 0.57f, 0.58f));
        RectTransform hintRect = hint.rectTransform;
        hintRect.anchorMin = Vector2.zero;
        hintRect.anchorMax = Vector2.one;
        hintRect.offsetMin = hintRect.offsetMax = Vector2.zero;
        field.placeholder = hint;
        return field;
    }

    private static TextMeshProUGUI CreateValueDisplay(
        Transform parent,
        string value,
        float width,
        float height)
    {
        GameObject fieldObject = CreateUiObject("Value Display", parent);
        Image image = fieldObject.AddComponent<Image>();
        image.color = new Color(0.028f, 0.071f, 0.094f, 1f);
        Outline outline = fieldObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.51f, 0.58f, 0.58f, 0.28f);
        outline.effectDistance = new Vector2(1f, -1f);

        LayoutElement layout = fieldObject.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = height;

        TextMeshProUGUI text = CreateText(
            fieldObject.transform,
            value,
            13f,
            new Color(0.95f, 0.91f, 0.84f));
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 2f);
        textRect.offsetMax = new Vector2(-12f, -2f);
        return text;
    }

    private static void CreateSectionTitle(Transform parent, string title)
    {
        TextMeshProUGUI section = CreateText(parent, title, 14f, new Color(0.90f, 0.60f, 0.35f));
        section.fontStyle = FontStyles.Bold;
        section.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
    }

    private static Transform CreateScrollGrid(Transform parent)
    {
        GameObject scrollObject = CreateUiObject("Reflected Command Scroll", parent);
        Image scrollImage = scrollObject.AddComponent<Image>();
        scrollImage.color = new Color(0.028f, 0.071f, 0.094f, 1f);
        Outline outline = scrollObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.51f, 0.58f, 0.58f, 0.28f);
        outline.effectDistance = new Vector2(1f, -1f);
        LayoutElement scrollLayout = scrollObject.AddComponent<LayoutElement>();
        scrollLayout.flexibleHeight = 1f;
        scrollLayout.minHeight = 180f;
        ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;

        GameObject viewport = CreateUiObject("Viewport", scrollObject.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(7f, 7f);
        viewportRect.offsetMax = new Vector2(-7f, -7f);
        viewport.AddComponent<RectMask2D>();
        scroll.viewport = viewportRect;

        GameObject content = CreateUiObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        GridLayoutGroup grid = content.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.cellSize = new Vector2(315f, 32f);
        grid.spacing = new Vector2(7f, 6f);
        grid.padding = new RectOffset(0, 0, 0, 4);
        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRect;
        return content.transform;
    }

    private static void EnsureEventSystem()
    {
        EventSystemGuard.EnsureExactlyOne();
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
            structureSelectionText.text = "没有可用的遗迹定义";
            structureHintText.text = "请检查 Resources/Config/StructureCatalog_Default。";
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
        structureSelectionText.text =
            $"{selectedStructureIndex + 1}/{availableStructures.Count}  {displayName}  /  {definition.StructureId}";

        if (!TryGetCurrentWorldSeed(out int worldSeed))
        {
            structureHintText.text = "进入游戏世界后才能按种子定位遗迹。";
            return;
        }

        int count = StructureRuntimeRegistry.Count(worldSeed, definition.StructureId);
        structureHintText.text =
            $"已加载记录 {count} 个；传送会按世界种子推算最近生成点，无需提前探索。";
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
            SetStatus(
                $"种子定位失败：扫描 {scannedRegionCount} 个区域后没有满足生成条件的位置。",
                Color.yellow);
            return;
        }

        Vector3 destination = new(
            location.EntrancePosition.x,
            location.EntrancePosition.y,
            playerTransform.position.z);
        float distance = Vector2.Distance(playerTransform.position, destination);

        Rigidbody2D body = playerTransform.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = destination;
        }

        playerTransform.position = destination;
        Player player = playerTransform.GetComponent<Player>() ??
                        playerTransform.GetComponentInParent<Player>();
        if (player?.Data != null)
            player.Data.transform.position = destination;

        ChunkMgr.Instance?.ResetChunkLoadQueue();
        Mod_ChunkLoader chunkLoader = playerTransform.GetComponentInChildren<Mod_ChunkLoader>(true) ??
                                      playerTransform.GetComponentInParent<Mod_ChunkLoader>();
        chunkLoader?.RefreshChunksAroundPlayer();

        Debug.Log(
            $"[GM] 已按种子传送到遗迹 {location.DisplayName} ({location.StructureId})，" +
            $"入口={location.EntrancePosition}，距离={distance:F1}，扫描区域={scannedRegionCount}");
        SetWindowVisible(false);
    }

    private static bool TryGetCurrentWorldSeed(out int worldSeed)
    {
        worldSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 0;
        return worldSeed != 0;
    }

    #endregion

    #region Reflection command discovery

    private void RefreshRuntimeData()
    {
        RebindLegacyF4Conflict();
        BindGameEventManager();
        RefreshBuffDefinitions();
        RefreshBuffTargetList();
        RefreshBuffTargetingControls();
        RefreshTeleportShortcutButton();
        RefreshAdminInvincibilityButton();
        RefreshPlayerMoveSpeedButton();
        RefreshChunkLoadSpeedControl();
        RefreshNavigationPathButton();
        RefreshItemIds();
        RefreshAiCreatureIds();
        RefreshStructureOptions();
        RebuildReflectedCommands();
        RebuildGameEventPage();
    }

    private void CyclePlayerMoveSpeed()
    {
        PlayerAdminController controller = FindFirstComponent("PlayerAdminController") as PlayerAdminController;
        if (controller == null)
        {
            SetStatus("未找到本地玩家，无法调整移动速度。", Color.yellow);
            RefreshPlayerMoveSpeedButton();
            return;
        }

        if (!controller.TryCycleAdminMoveSpeedMultiplier(out float multiplier))
        {
            SetStatus("未找到玩家移动模块，移动速度调整失败。", Color.yellow);
            return;
        }

        GMConsolePreferences.SetPlayerMoveSpeed(multiplier);
        RefreshPlayerMoveSpeedButton();
        SetStatus(
            $"玩家移动速度已调整为 {multiplier:0.#} 倍。",
            multiplier > 1f ? new Color(0.35f, 0.95f, 0.85f) : new Color(0.66f, 0.71f, 0.71f));
    }

    private void ApplyPlayerMoveSpeedInput()
    {
        if (playerMoveSpeedInput == null)
            return;

        string value = playerMoveSpeedInput.text?.Trim();
        bool parsed = float.TryParse(
                          value,
                          NumberStyles.Float,
                          CultureInfo.InvariantCulture,
                          out float requestedMultiplier) ||
                      float.TryParse(
                          value,
                          NumberStyles.Float,
                          CultureInfo.CurrentCulture,
                          out requestedMultiplier);
        if (!parsed || float.IsNaN(requestedMultiplier) || float.IsInfinity(requestedMultiplier))
        {
            SetStatus("请输入有效的移动速度倍率，例如 1、2.5 或 10。", Color.yellow);
            RefreshPlayerMoveSpeedButton();
            return;
        }

        PlayerAdminController controller = FindFirstComponent("PlayerAdminController") as PlayerAdminController;
        if (controller == null)
        {
            SetStatus("未找到本地玩家，无法调整移动速度。", Color.yellow);
            RefreshPlayerMoveSpeedButton();
            return;
        }

        if (!controller.TrySetAdminMoveSpeedMultiplier(requestedMultiplier, out float appliedMultiplier))
        {
            SetStatus("未找到玩家移动模块，移动速度调整失败。", Color.yellow);
            RefreshPlayerMoveSpeedButton();
            return;
        }

        playerMoveSpeedInput.SetTextWithoutNotify(
            appliedMultiplier.ToString("0.##", CultureInfo.InvariantCulture));
        GMConsolePreferences.SetPlayerMoveSpeed(appliedMultiplier);
        RefreshPlayerMoveSpeedButton();
        SetStatus(
            $"玩家移动速度已调整为 {appliedMultiplier:0.##} 倍。",
            appliedMultiplier > 1f
                ? new Color(0.35f, 0.95f, 0.85f)
                : new Color(0.66f, 0.71f, 0.71f));
    }

    private void RefreshPlayerMoveSpeedButton()
    {
        PlayerAdminController controller = FindFirstComponent("PlayerAdminController") as PlayerAdminController;
        float multiplier = controller != null ? controller.AdminMoveSpeedMultiplier : 1f;
        if (playerMoveSpeedInput != null && !playerMoveSpeedInput.isFocused)
            playerMoveSpeedInput.SetTextWithoutNotify(multiplier.ToString("0.##", CultureInfo.InvariantCulture));

        if (playerMoveSpeedButton != null)
        {
            TextMeshProUGUI label = playerMoveSpeedButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
                label.text = $"玩家移速：{multiplier:0.##}x";
        }

        Button statusButton = playerMoveSpeedApplyButton ?? playerMoveSpeedButton;
        Image image = statusButton != null ? statusButton.GetComponent<Image>() : null;
        if (image != null)
        {
            image.color = multiplier > 1f
                ? new Color(0.10f, 0.45f, 0.31f, 1f)
                : new Color(0.094f, 0.212f, 0.251f, 1f);
        }
    }

    private void ApplyChunkLoadSpeedInput()
    {
        if (chunkLoadSpeedInput == null)
            return;

        string value = chunkLoadSpeedInput.text?.Trim();
        bool unlimitedRequested = IsUnlimitedChunkLoadValue(value);
        float requestedMultiplier = float.NaN;
        bool parsed = unlimitedRequested || float.TryParse(
                          value,
                          NumberStyles.Float,
                          CultureInfo.InvariantCulture,
                          out requestedMultiplier) ||
                      float.TryParse(
                          value,
                          NumberStyles.Float,
                          CultureInfo.CurrentCulture,
                          out requestedMultiplier);
        if (unlimitedRequested)
            requestedMultiplier = float.PositiveInfinity;

        if (!parsed || float.IsNaN(requestedMultiplier) || float.IsNegativeInfinity(requestedMultiplier))
        {
            SetStatus("请输入有效倍率，或输入‘无限’取消加载上限。", Color.yellow);
            RefreshChunkLoadSpeedControl();
            return;
        }

        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager == null ||
            !chunkManager.TrySetChunkLoadSpeedMultiplier(requestedMultiplier, out float appliedMultiplier))
        {
            SetStatus("未找到区块管理器，加载速度调整失败。", Color.yellow);
            RefreshChunkLoadSpeedControl();
            return;
        }

        GMConsolePreferences.SetChunkLoadSpeed(
            appliedMultiplier,
            chunkManager.IsChunkLoadSpeedUnlimited);
        RefreshChunkLoadSpeedControl();
        ShowChunkLoadSpeedStatus(chunkManager, appliedMultiplier);
    }

    private void ToggleUnlimitedChunkLoadSpeed()
    {
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager == null)
        {
            SetStatus("未找到区块管理器，加载速度调整失败。", Color.yellow);
            return;
        }

        float requestedMultiplier = chunkManager.IsChunkLoadSpeedUnlimited
            ? GMConsolePreferences.ChunkLoadSpeedMultiplier
            : float.PositiveInfinity;
        if (!chunkManager.TrySetChunkLoadSpeedMultiplier(
                requestedMultiplier,
                out float appliedMultiplier))
        {
            SetStatus("区块加载速度调整失败。", Color.yellow);
            return;
        }

        GMConsolePreferences.SetChunkLoadSpeed(
            appliedMultiplier,
            chunkManager.IsChunkLoadSpeedUnlimited);
        RefreshChunkLoadSpeedControl();
        ShowChunkLoadSpeedStatus(chunkManager, appliedMultiplier);
    }

    private void RefreshChunkLoadSpeedControl()
    {
        float multiplier = ChunkMgr.CurrentChunkLoadSpeedMultiplier;
        bool unlimited = ChunkMgr.CurrentChunkLoadSpeedUnlimited;
        if (chunkLoadSpeedInput != null && !chunkLoadSpeedInput.isFocused)
        {
            chunkLoadSpeedInput.SetTextWithoutNotify(
                unlimited ? "无限" : multiplier.ToString("0.##", CultureInfo.InvariantCulture));
        }

        Image image = chunkLoadSpeedApplyButton != null
            ? chunkLoadSpeedApplyButton.GetComponent<Image>()
            : null;
        if (image != null)
        {
            image.color = unlimited || multiplier > 1f
                ? new Color(0.10f, 0.45f, 0.31f, 1f)
                : new Color(0.094f, 0.212f, 0.251f, 1f);
        }

        if (chunkLoadSpeedUnlimitedButton == null)
            return;

        TextMeshProUGUI label =
            chunkLoadSpeedUnlimitedButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            label.text = unlimited ? "恢复" : "无限";

        Image unlimitedImage = chunkLoadSpeedUnlimitedButton.GetComponent<Image>();
        if (unlimitedImage != null)
        {
            unlimitedImage.color = unlimited
                ? new Color(0.66f, 0.32f, 0.15f, 1f)
                : new Color(0.094f, 0.212f, 0.251f, 1f);
        }
    }

    private static bool IsUnlimitedChunkLoadValue(string value)
    {
        return string.Equals(value, "无限", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "无限制", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "inf", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "infinity", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "unlimited", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowChunkLoadSpeedStatus(ChunkMgr chunkManager, float appliedMultiplier)
    {
        if (chunkManager.IsChunkLoadSpeedUnlimited)
        {
            SetStatus(
                $"区块加载已设为自动最大；后台并发 {chunkManager.EffectiveBackgroundGenerationConcurrency}，仍保留主线程安全预算。",
                new Color(0.35f, 0.95f, 0.85f));
            return;
        }

        SetStatus(
            $"区块加载速度已调整为 {appliedMultiplier:0.##} 倍。",
            appliedMultiplier > 1f
                ? new Color(0.35f, 0.95f, 0.85f)
                : new Color(0.66f, 0.71f, 0.71f));
    }

    private void ToggleTeleportShortcut()
    {
        bool enabled = PlayerAdminController.ToggleTeleportToMouseShortcut();
        GMConsolePreferences.SetTeleportShortcut(enabled);
        RefreshTeleportShortcutButton();
        SetStatus(
            enabled ? "Ctrl+T 鼠标传送已开启。" : "Ctrl+T 鼠标传送已关闭。",
            enabled ? new Color(0.35f, 0.95f, 0.85f) : new Color(0.66f, 0.71f, 0.71f));
    }

    private void ToggleAdminInvincibility()
    {
        PlayerAdminController controller =
            FindFirstComponent("PlayerAdminController") as PlayerAdminController;
        if (controller == null || !controller.TryToggleAdminInvincibility(out bool enabled))
        {
            RefreshAdminInvincibilityButton();
            SetStatus("请先启用管理员模式，才能切换无敌。", Color.yellow);
            return;
        }

        RefreshAdminInvincibilityButton();
        SetStatus(
            enabled ? "管理员无敌已开启。" : "管理员无敌已关闭，生命与生存状态将正常结算。",
            enabled ? new Color(0.35f, 0.95f, 0.85f) : new Color(0.90f, 0.62f, 0.30f));
    }

    private void RefreshAdminInvincibilityButton()
    {
        if (adminInvincibilityButton == null)
            return;

        PlayerAdminController controller =
            FindFirstComponent("PlayerAdminController") as PlayerAdminController;
        bool canToggle = controller != null && controller.IsAdministrator;
        bool enabled = canToggle && controller.IsAdminInvincibilityEnabled;

        TextMeshProUGUI label = adminInvincibilityButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.text = canToggle
                ? (enabled ? "管理员无敌：开" : "管理员无敌：关")
                : "管理员无敌：需权限";
        }

        adminInvincibilityButton.interactable = canToggle;
        Image image = adminInvincibilityButton.GetComponent<Image>();
        if (image != null)
        {
            image.color = !canToggle
                ? new Color(0.12f, 0.16f, 0.18f, 1f)
                : enabled
                    ? new Color(0.10f, 0.45f, 0.31f, 1f)
                    : new Color(0.44f, 0.23f, 0.16f, 1f);
        }
    }

    private void RefreshTeleportShortcutButton()
    {
        if (teleportShortcutButton == null)
            return;

        bool enabled = PlayerAdminController.TeleportToMouseShortcutEnabled;
        TextMeshProUGUI label = teleportShortcutButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            label.text = enabled ? "Ctrl+T 传送：开" : "Ctrl+T 传送：关";

        Image image = teleportShortcutButton.GetComponent<Image>();
        if (image != null)
        {
            image.color = enabled
                ? new Color(0.10f, 0.45f, 0.31f, 1f)
                : new Color(0.094f, 0.212f, 0.251f, 1f);
        }
    }

    private void ToggleNavigationPathHints()
    {
        bool visible = WorldNavigationPathDebugOverlay.ToggleRoutesVisible();
        GMConsolePreferences.SetNavigationPathVisible(visible);
        RefreshNavigationPathButton();
        SetStatus(
            visible ? "AI 导航路线提示已开启。" : "AI 导航路线提示已关闭。",
            visible ? new Color(0.35f, 0.95f, 0.85f) : new Color(0.66f, 0.71f, 0.71f));
    }

    private void RefreshNavigationPathButton()
    {
        if (navigationPathButton == null)
            return;

        bool visible = WorldNavigationPathDebugOverlay.RoutesVisible;
        TextMeshProUGUI label = navigationPathButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            label.text = visible ? "AI 路线提示：开" : "AI 路线提示：关";

        Image image = navigationPathButton.GetComponent<Image>();
        if (image != null)
        {
            image.color = visible
                ? new Color(0.10f, 0.45f, 0.31f, 1f)
                : new Color(0.094f, 0.212f, 0.251f, 1f);
        }
    }

    private void RebuildReflectedCommands()
    {
        commands.Clear();

        AddNamedCommand("环境", "晴天", "GameDebugManager", "SetClearWeather");
        AddNamedCommand("环境", "下雨", "GameDebugManager", "SetRainWeather");
        AddNamedCommand("环境", "环境信息", "GameDebugManager", "ToggleEnvironmentInfo");
        AddNamedCommand("管理员", "视野无限", "Mod_Cam", "EnableUnlimitedView");
        AddNamedCommand("管理员", "刷新区块", "Mod_ChunkLoader", "RefreshChunksAroundPlayer");
        AddNamedCommand("管理员", "区块距离 +1", "PlayerAdminController", "IncreaseAdminChunkLoadDistance");
        AddNamedCommand("管理员", "手持 +9999", "PlayerAdminController", "AddAmountToCurrentHandItem", 9999f);
        AddNamedCommand("管理员", "背包 +999", "PlayerAdminController", "AddAmountToAllBagItems", 999f);
        AddNamedCommand("管理员", "时间恢复", "PlayerAdminController", "ResetTimeScale");
        AddNamedCommand("管理员", "时间 +0.5", "PlayerAdminController", "TryUpdateTimeScale", 0.5f);
        AddNamedCommand("管理员", "时间 -0.5", "PlayerAdminController", "TryUpdateTimeScale", -0.5f);

        foreach (MonoBehaviour behaviour in FindSceneBehaviours())
        {
            Type type = behaviour.GetType();
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < methods.Length && commands.Count < MaxDiscoveredCommands; i++)
            {
                MethodInfo method = methods[i];
                if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0 || !IsSafeAutoCommand(method))
                    continue;

                AddCommand($"扫描/{type.Name}", method.Name, behaviour, method, Array.Empty<object>());
            }
        }

        RebuildCommandButtons();
    }

    private void AddNamedCommand(string category, string label, string typeName, string methodName, params object[] arguments)
    {
        Component target = FindFirstComponent(typeName);
        if (target == null)
            return;

        MethodInfo method = FindCompatibleMethod(target.GetType(), methodName, arguments);
        if (method != null)
            AddCommand(category, label, target, method, arguments ?? Array.Empty<object>());
    }

    private void AddCommand(string category, string label, Component target, MethodInfo method, object[] arguments)
    {
        if (target == null || method == null)
            return;

        string argumentKey = string.Join(",", arguments.Select(argument => argument?.ToString() ?? "<null>"));
        string identity = target.GetInstanceID() + "|" + method.Name + "|" + argumentKey;
        for (int i = 0; i < commands.Count; i++)
        {
            ReflectedCommand existing = commands[i];
            if (existing.Target != null &&
                existing.Target.GetInstanceID() + "|" + existing.Method.Name + "|" +
                string.Join(",", existing.Arguments.Select(argument => argument?.ToString() ?? "<null>")) == identity)
            {
                return;
            }
        }

        commands.Add(new ReflectedCommand
        {
            Category = category,
            Label = label,
            Target = target,
            Method = method,
            Arguments = arguments
        });
    }

    private void RebuildCommandButtons()
    {
        RebuildCommandPage();
    }

    private static bool IsSafeAutoCommand(MethodInfo method)
    {
        string name = method.Name;
        return name == "ToggleEnvironmentInfo" ||
               name == "SetClearWeather" ||
               name == "SetRainWeather" ||
               name == "SetClearWeatherDebug" ||
               name == "SetRainWeatherDebug" ||
               name == "ToggleDebugPanel" ||
               name == "RefreshChunksAroundPlayer" ||
               name == "RefreshChunksForCameraView" ||
               name == "EnableUnlimitedView" ||
               name == "InitializeCreativeInventoryForAdmin" ||
               name == "TeleportToMousePosition" ||
               name == "CleanupNullItems" ||
               name == "LoadAllRuntimeItems";
    }

    private void InvokeCommand(ReflectedCommand command)
    {
        if (command.Target == null)
        {
            SetStatus("指令目标已失效，请重新扫描。", Color.yellow);
            return;
        }

        try
        {
            command.Method.Invoke(command.Target, command.Arguments);
            SetStatus($"已执行：{command.Category} / {command.Label}", new Color(0.35f, 0.95f, 0.85f));
        }
        catch (TargetInvocationException exception)
        {
            SetStatus($"执行失败：{exception.InnerException?.Message ?? exception.Message}", new Color(1f, 0.42f, 0.38f));
            Debug.LogException(exception.InnerException ?? exception);
        }
        catch (Exception exception)
        {
            SetStatus($"执行失败：{exception.Message}", new Color(1f, 0.42f, 0.38f));
            Debug.LogException(exception);
        }
    }

    private void InvokeByTypeName(string typeName, string methodName, params object[] arguments)
    {
        Component target = FindFirstComponent(typeName);
        MethodInfo method = target != null ? FindCompatibleMethod(target.GetType(), methodName, arguments) : null;
        if (target == null || method == null)
        {
            SetStatus($"未找到可用指令：{typeName}.{methodName}", Color.yellow);
            return;
        }

        InvokeCommand(new ReflectedCommand
        {
            Category = typeName,
            Label = methodName,
            Target = target,
            Method = method,
            Arguments = arguments ?? Array.Empty<object>()
        });
    }

    #endregion

    #region Airdrop

    private void OpenAirdropBrowser()
    {
        windowRoot.SetActive(false);
        airdropBrowserRoot.SetActive(true);
        RefreshItemIds();
        SetAirdropBrowserStatus("点击任意物品即可空投到玩家附近。", new Color(0.66f, 0.71f, 0.71f));
    }

    private void CloseAirdropBrowser()
    {
        airdropBrowserRoot.SetActive(false);
        windowRoot.SetActive(true);
    }

    private void OpenAiCreatureBrowser()
    {
        windowRoot.SetActive(false);
        aiCreatureBrowserRoot.SetActive(true);
        RefreshAiCreatureIds();
        SetAiCreatureBrowserStatus(
            "点击任意生物即可召唤；批量召唤会自动散布，避免重叠。",
            new Color(0.66f, 0.71f, 0.71f));
    }

    private void CloseAiCreatureBrowser()
    {
        aiCreatureBrowserRoot.SetActive(false);
        windowRoot.SetActive(true);
    }

    private void RefreshItemIds()
    {
        availableAirdropItems.Clear();
        GameRes gameRes = FindFirstComponent("GameRes") as GameRes;
        if (gameRes != null)
        {
            HashSet<string> discoveredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string itemId in gameRes.GetAllItemIds())
            {
                if (string.IsNullOrWhiteSpace(itemId) ||
                    !discoveredIds.Add(itemId) ||
                    !gameRes.TryGetItemPresentation(
                        itemId,
                        out string displayName,
                        out Sprite icon))
                {
                    continue;
                }

                availableAirdropItems.Add(new AirdropItemEntry
                {
                    ItemId = itemId,
                    DisplayName = displayName,
                    Icon = icon
                });
            }
        }

        availableAirdropItems.Sort((left, right) =>
        {
            int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
            return nameComparison != 0
                ? nameComparison
                : StringComparer.OrdinalIgnoreCase.Compare(left.ItemId, right.ItemId);
        });

        if (itemIdInput != null &&
            availableAirdropItems.Count > 0 &&
            string.IsNullOrWhiteSpace(itemIdInput.text))
        {
            itemIdInput.text = availableAirdropItems[0].ItemId;
        }

        UpdateSummonHint();

        if (airdropBrowserRoot != null && airdropBrowserRoot.activeSelf)
            RebuildAirdropItemGrid();
    }

    private void RefreshAiCreatureIds()
    {
        availableAiCreatures.Clear();
        Component gameRes = FindFirstComponent("GameRes");
        object prefabDictionary = ReadMember(gameRes, "AllPrefabs");
        if (prefabDictionary is IDictionary dictionary)
        {
            HashSet<string> discoveredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!(entry.Key is string itemId) ||
                    string.IsNullOrWhiteSpace(itemId) ||
                    !discoveredIds.Add(itemId) ||
                    !(entry.Value is GameObject prefab) ||
                    !IsAiCreaturePrefab(prefab))
                {
                    continue;
                }

                Item item = prefab.GetComponent<Item>() ?? prefab.GetComponentInChildren<Item>(true);
                if (item == null)
                    continue;

                ItemData data = null;
                try
                {
                    data = item.itemData;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[GM] 读取 AI 生物 {itemId} 的 ItemData 失败：{exception.Message}");
                }

                Sprite icon = item.Sprite != null ? item.Sprite.sprite : null;
                if (icon == null)
                    icon = prefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite;

                availableAiCreatures.Add(new AiCreatureEntry
                {
                    ItemId = itemId,
                    DisplayName = !string.IsNullOrWhiteSpace(data?.GameName) ? data.GameName : itemId,
                    Icon = icon
                });
            }
        }

        availableAiCreatures.Sort((left, right) =>
        {
            int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
            return nameComparison != 0
                ? nameComparison
                : StringComparer.OrdinalIgnoreCase.Compare(left.ItemId, right.ItemId);
        });

        UpdateSummonHint();
        if (aiCreatureBrowserRoot != null && aiCreatureBrowserRoot.activeSelf)
            RebuildAiCreatureGrid();
    }

    private static bool IsAiCreaturePrefab(GameObject prefab)
    {
        if (prefab == null)
            return false;

        MonoBehaviour[] behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            Type type = behaviour.GetType();
            string typeName = type.Name;
            if (string.Equals(typeName, "Mover_AI", StringComparison.Ordinal) ||
                (typeName.StartsWith("AI_", StringComparison.Ordinal) &&
                 !string.Equals(typeName, "AI_AttackController", StringComparison.Ordinal)) ||
                string.Equals(type.FullName, "BehaviorDesigner.Runtime.BehaviorTree", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateSummonHint()
    {
        if (itemHintText == null)
            return;

        if (availableAirdropItems.Count == 0 && availableAiCreatures.Count == 0)
        {
            itemHintText.text = "尚未发现可召唤内容。请等待 GameRes 加载完成，或先进入游戏世界。";
            return;
        }

        itemHintText.text =
            $"已发现 {availableAirdropItems.Count} 个物品、{availableAiCreatures.Count} 个 AI 生物。";
    }

    private void RebuildAirdropItemGrid()
    {
        if (airdropItemGrid == null)
            return;

        for (int i = airdropItemGrid.childCount - 1; i >= 0; i--)
            Destroy(airdropItemGrid.GetChild(i).gameObject);

        string query = airdropSearchInput != null ? airdropSearchInput.text.Trim() : string.Empty;
        List<AirdropItemEntry> visibleItems = string.IsNullOrWhiteSpace(query)
            ? availableAirdropItems
            : availableAirdropItems
                .Where(item =>
                    item.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.ItemId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

        for (int i = 0; i < visibleItems.Count; i++)
            CreateAirdropItemButton(visibleItems[i]);

        if (visibleItems.Count == 0)
        {
            TextMeshProUGUI emptyText = CreateText(
                airdropItemGrid,
                availableAirdropItems.Count == 0 ? "暂无可空投物品" : "没有匹配的物品",
                13f,
                new Color(0.66f, 0.71f, 0.71f));
            emptyText.alignment = TextAlignmentOptions.Center;
        }

        if (airdropBrowserCountText != null)
        {
            airdropBrowserCountText.text = string.IsNullOrWhiteSpace(query)
                ? $"全部 {availableAirdropItems.Count}"
                : $"显示 {visibleItems.Count} / {availableAirdropItems.Count}";
        }
    }

    private void CreateAirdropItemButton(AirdropItemEntry entry)
    {
        CreateCatalogButton(
            airdropItemGrid,
            "Item " + entry.ItemId,
            entry.ItemId,
            entry.DisplayName,
            entry.Icon,
            () => StartAirdrop(entry.ItemId));
    }

    private void RebuildAiCreatureGrid()
    {
        if (aiCreatureGrid == null)
            return;

        for (int i = aiCreatureGrid.childCount - 1; i >= 0; i--)
            Destroy(aiCreatureGrid.GetChild(i).gameObject);

        string query = aiCreatureSearchInput != null ? aiCreatureSearchInput.text.Trim() : string.Empty;
        List<AiCreatureEntry> visibleCreatures = string.IsNullOrWhiteSpace(query)
            ? availableAiCreatures
            : availableAiCreatures
                .Where(creature =>
                    creature.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    creature.ItemId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

        for (int i = 0; i < visibleCreatures.Count; i++)
        {
            AiCreatureEntry creature = visibleCreatures[i];
            CreateCatalogButton(
                aiCreatureGrid,
                "AI Creature " + creature.ItemId,
                creature.ItemId,
                creature.DisplayName,
                creature.Icon,
                () => SpawnAiCreature(creature));
        }

        if (visibleCreatures.Count == 0)
        {
            TextMeshProUGUI emptyText = CreateText(
                aiCreatureGrid,
                availableAiCreatures.Count == 0 ? "暂无可召唤 AI 生物" : "没有匹配的生物",
                13f,
                new Color(0.66f, 0.71f, 0.71f));
            emptyText.alignment = TextAlignmentOptions.Center;
        }

        if (aiCreatureBrowserCountText != null)
        {
            aiCreatureBrowserCountText.text = string.IsNullOrWhiteSpace(query)
                ? $"全部 {availableAiCreatures.Count}"
                : $"显示 {visibleCreatures.Count} / {availableAiCreatures.Count}";
        }
    }

    private static void CreateCatalogButton(
        Transform parent,
        string objectName,
        string itemId,
        string displayName,
        Sprite icon,
        UnityAction onClick)
    {
        GameObject tile = CreateUiObject(objectName, parent);
        Image tileImage = tile.AddComponent<Image>();
        tileImage.color = new Color(0.075f, 0.145f, 0.17f, 1f);
        Outline outline = tile.AddComponent<Outline>();
        outline.effectColor = new Color(0.51f, 0.58f, 0.58f, 0.34f);
        outline.effectDistance = new Vector2(1f, -1f);

        Button button = tile.AddComponent<Button>();
        button.targetGraphic = tileImage;
        button.onClick.AddListener(onClick);
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.84f, 0.63f, 1f);
        colors.pressedColor = new Color(0.76f, 0.68f, 0.58f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        GameObject iconObject = CreateUiObject("Icon", tile.transform);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 1f);
        iconRect.pivot = new Vector2(0.5f, 1f);
        iconRect.anchoredPosition = new Vector2(0f, -8f);
        iconRect.sizeDelta = new Vector2(64f, 64f);
        Image iconImage = iconObject.AddComponent<Image>();
        iconImage.sprite = icon;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        iconImage.color = icon != null ? Color.white : Color.clear;

        if (icon == null)
        {
            TextMeshProUGUI placeholder = CreateText(
                iconObject.transform,
                "?",
                30f,
                new Color(0.51f, 0.58f, 0.58f));
            placeholder.alignment = TextAlignmentOptions.Center;
            RectTransform placeholderRect = placeholder.rectTransform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = placeholderRect.offsetMax = Vector2.zero;
        }

        TextMeshProUGUI nameText = CreateText(
            tile.transform,
            displayName,
            12f,
            new Color(0.95f, 0.91f, 0.84f));
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.enableWordWrapping = false;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform nameRect = nameText.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(1f, 0f);
        nameRect.offsetMin = new Vector2(5f, 23f);
        nameRect.offsetMax = new Vector2(-5f, 45f);

        TextMeshProUGUI idText = CreateText(
            tile.transform,
            itemId,
            9f,
            new Color(0.49f, 0.60f, 0.62f));
        idText.alignment = TextAlignmentOptions.Center;
        idText.enableWordWrapping = false;
        idText.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform idRect = idText.rectTransform;
        idRect.anchorMin = new Vector2(0f, 0f);
        idRect.anchorMax = new Vector2(1f, 0f);
        idRect.offsetMin = new Vector2(5f, 4f);
        idRect.offsetMax = new Vector2(-5f, 22f);
    }

    private void SpawnAiCreature(AiCreatureEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.ItemId))
        {
            SetAiCreatureResult("请选择要召唤的 AI 生物。", Color.yellow);
            return;
        }

        if (aiCreatureAmountInput == null ||
            !int.TryParse(aiCreatureAmountInput.text, out int amount))
        {
            amount = 1;
        }

        amount = Mathf.Clamp(amount, 1, 20);
        if (aiCreatureAmountInput != null)
            aiCreatureAmountInput.text = amount.ToString();

        Transform player = GetLocalPlayerTransform();
        if (player == null)
        {
            SetAiCreatureResult("未找到本地玩家，无法确定召唤位置。", Color.yellow);
            return;
        }

        ItemMgr itemManager = ItemMgr.Instance;
        if (itemManager == null)
        {
            SetAiCreatureResult("召唤失败：未找到 ItemMgr。", new Color(1f, 0.42f, 0.38f));
            return;
        }

        int spawnedCount = 0;
        string firstFailure = null;
        for (int i = 0; i < amount; i++)
        {
            Vector3 spawnPosition = GetAiCreatureSpawnPosition(player.position, i, amount);
            if (TrySpawnInitializedAiCreature(
                    itemManager,
                    entry.ItemId,
                    spawnPosition,
                    out _,
                    out string spawnError))
            {
                spawnedCount++;
            }
            else if (firstFailure == null)
            {
                firstFailure = spawnError;
            }
        }

        if (spawnedCount == amount)
        {
            SetAiCreatureResult(
                $"召唤成功：{entry.DisplayName} × {spawnedCount}",
                new Color(0.35f, 0.95f, 0.85f));
            return;
        }

        string result = spawnedCount > 0
            ? $"部分召唤成功：{entry.DisplayName} {spawnedCount}/{amount}；{firstFailure}"
            : $"召唤失败：{firstFailure}";
        SetAiCreatureResult(result, new Color(1f, 0.42f, 0.38f));
    }

    private static Vector3 GetAiCreatureSpawnPosition(Vector3 playerPosition, int index, int amount)
    {
        const float GoldenAngleRadians = 2.39996323f;
        float angle = index * GoldenAngleRadians;
        float radius = amount == 1 ? 2.5f : 2.5f + Mathf.Sqrt(index) * 0.65f;
        Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
        Vector3 position = playerPosition + offset;
        position.z = playerPosition.z;
        return position;
    }

    /// <summary>
    /// GM 生物必须沿用自然生成的完整链路：ItemMgr 注册后立即 Load，
    /// 否则 AI 的状态机、感知和移动模块都不会完成初始化。
    /// </summary>
    private static bool TrySpawnInitializedAiCreature(
        ItemMgr itemManager,
        string itemId,
        Vector3 spawnPosition,
        out Item spawnedItem,
        out string error)
    {
        spawnedItem = null;
        if (itemManager == null)
        {
            error = "未找到 ItemMgr。";
            return false;
        }

        try
        {
            spawnedItem = itemManager.InstantiateItem(
                itemId,
                spawnPosition,
                Quaternion.identity,
                Vector3.one);
            if (spawnedItem == null)
            {
                error = "ItemMgr 未返回 Item 实例。";
                return false;
            }

            if (!TryGetRuntimeAiActor(spawnedItem, out _))
            {
                error = $"{itemId} 不包含可运行的 AI 模块。";
                DespawnFailedAiCreature(itemManager, spawnedItem);
                spawnedItem = null;
                return false;
            }

            if (!spawnedItem.IsInitialized)
                spawnedItem.Load();

            if (!spawnedItem.IsInitialized ||
                !TryGetRuntimeAiActor(spawnedItem, out IAIActor actor) ||
                !ReferenceEquals(actor.ActorItem, spawnedItem))
            {
                error = $"{itemId} 的 AI 初始化未完成。";
                DespawnFailedAiCreature(itemManager, spawnedItem);
                spawnedItem = null;
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            Debug.LogException(exception);
            DespawnFailedAiCreature(itemManager, spawnedItem);
            spawnedItem = null;
            return false;
        }
    }

    /// <summary>从实例层级寻找真实 AI 标记，避免把仅有相似名称的普通物品当作生物生成。</summary>
    private static bool TryGetRuntimeAiActor(Item item, out IAIActor actor)
    {
        actor = null;
        if (item == null)
            return false;

        MonoBehaviour[] behaviours = item.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IAIActor candidate)
            {
                actor = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>初始化或校验失败时通过 ItemMgr 回收临时实体，避免残留未完成初始化的对象。</summary>
    private static void DespawnFailedAiCreature(ItemMgr itemManager, Item item)
    {
        if (item == null || item.DestructionHandled)
            return;

        try
        {
            itemManager?.DespawnItem(item, saveData: false);
        }
        catch (Exception cleanupException)
        {
            Debug.LogWarning($"[GM] 回收失败的 AI 生物失败：{cleanupException.Message}");
            UnityEngine.Object.Destroy(item.gameObject);
        }
    }

    private void SetAiCreatureResult(string message, Color color)
    {
        SetStatus(message, color);
        SetAiCreatureBrowserStatus(message, color);
    }

    private void SetAiCreatureBrowserStatus(string message, Color color)
    {
        if (aiCreatureBrowserStatusText == null)
            return;

        aiCreatureBrowserStatusText.text = message;
        aiCreatureBrowserStatusText.color = color;
    }

    private void StartAirdrop()
    {
        StartAirdrop(itemIdInput != null ? itemIdInput.text : string.Empty);
    }

    private void StartAirdrop(string itemId)
    {
        itemId = itemId?.Trim();
        if (string.IsNullOrWhiteSpace(itemId))
        {
            SetAirdropResult("请选择要空投的物品。", Color.yellow);
            return;
        }

        if (amountInput == null || !int.TryParse(amountInput.text, out int amount))
            amount = 1;
        amount = Mathf.Clamp(amount, 1, 9999);
        if (amountInput != null)
            amountInput.text = amount.ToString();

        Transform player = GetLocalPlayerTransform();
        if (player == null)
        {
            SetAirdropResult("未找到本地玩家，无法确定空投位置。", Color.yellow);
            return;
        }

        Vector3 sideOffset = UnityEngine.Random.insideUnitCircle.normalized * 1.25f;
        Vector3 landingPosition = player.position + sideOffset;
        landingPosition.z = player.position.z;
        StartCoroutine(AirdropRoutine(itemId, amount, landingPosition));
    }

    private IEnumerator AirdropRoutine(string itemId, int amount, Vector3 landingPosition)
    {
        GameObject marker = CreateAirdropMarker(landingPosition + Vector3.up * 5f);
        float duration = 0.55f;
        float elapsed = 0f;
        Vector3 start = marker.transform.position;

        while (elapsed < duration && marker != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            marker.transform.position = Vector3.Lerp(start, landingPosition, t * t);
            marker.transform.localScale = Vector3.one * Mathf.Lerp(1.15f, 0.65f, t);
            yield return null;
        }

        if (marker != null)
            Destroy(marker);

        if (TrySpawnItemThroughReflection(itemId, amount, landingPosition, out string result))
            SetAirdropResult(result, new Color(0.35f, 0.95f, 0.85f));
        else
            SetAirdropResult(result, new Color(1f, 0.42f, 0.38f));
    }

    private void SetAirdropResult(string message, Color color)
    {
        SetStatus(message, color);
        SetAirdropBrowserStatus(message, color);
    }

    private void SetAirdropBrowserStatus(string message, Color color)
    {
        if (airdropBrowserStatusText == null)
            return;

        airdropBrowserStatusText.text = message;
        airdropBrowserStatusText.color = color;
    }

    private bool TrySpawnItemThroughReflection(string itemId, int amount, Vector3 position, out string result)
    {
        if (!TryResolveItemSpawner(out Component itemManager, out MethodInfo spawnMethod, out string error))
        {
            result = $"空投失败：{error}";
            return false;
        }

        if (!TryInvokeItemSpawner(
                itemManager,
                spawnMethod,
                itemId,
                position,
                out object item,
                out error))
        {
            result = $"空投失败：{error}";
            return false;
        }

        try
        {
            object itemData = ReadMember(item, "itemData");
            object stack = ReadMember(itemData, "Stack");
            if (amount > 1 && stack != null)
                WriteMember(stack, "Amount", Convert.ChangeType(amount, GetMemberType(stack, "Amount") ?? typeof(float)));

            result = $"空投成功：{itemId} × {amount}";
            return true;
        }
        catch (TargetInvocationException exception)
        {
            result = $"空投失败：{exception.InnerException?.Message ?? exception.Message}";
            Debug.LogException(exception.InnerException ?? exception);
            return false;
        }
        catch (Exception exception)
        {
            result = $"空投失败：{exception.Message}";
            Debug.LogException(exception);
            return false;
        }
    }

    private static bool TryResolveItemSpawner(
        out Component itemManager,
        out MethodInfo spawnMethod,
        out string error)
    {
        itemManager = FindFirstComponent("ItemMgr");
        spawnMethod = null;
        if (itemManager == null)
        {
            error = "未找到 ItemMgr。";
            return false;
        }

        spawnMethod = itemManager.GetType().GetMethods(InstanceFlags)
            .FirstOrDefault(method =>
            {
                if (method.Name != "InstantiateItem")
                    return false;

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 5 && parameters[0].ParameterType == typeof(string);
            });

        if (spawnMethod == null)
        {
            error = "ItemMgr 未提供 string ItemId 实例化入口。";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryInvokeItemSpawner(
        Component itemManager,
        MethodInfo spawnMethod,
        string itemId,
        Vector3 position,
        out object item,
        out string error)
    {
        item = null;
        try
        {
            item = spawnMethod.Invoke(itemManager, new object[]
            {
                itemId,
                position,
                Quaternion.identity,
                Vector3.one,
                null
            });

            if (item != null)
            {
                error = null;
                return true;
            }

            error = "ItemMgr 未返回 Item 实例。";
            return false;
        }
        catch (TargetInvocationException exception)
        {
            error = exception.InnerException?.Message ?? exception.Message;
            Debug.LogException(exception.InnerException ?? exception);
            return false;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            Debug.LogException(exception);
            return false;
        }
    }

    private GameObject CreateAirdropMarker(Vector3 position)
    {
        if (dropMarkerSprite == null)
        {
            Texture2D texture = new Texture2D(12, 18, TextureFormat.RGBA32, false);
            Color[] pixels = Enumerable.Repeat(new Color(1f, 0.56f, 0.14f, 1f), texture.width * texture.height).ToArray();
            texture.SetPixels(pixels);
            texture.Apply();
            dropMarkerSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 14f);
        }

        GameObject marker = new GameObject("GM Airdrop Marker");
        marker.transform.position = position;
        SpriteRenderer renderer = marker.AddComponent<SpriteRenderer>();
        renderer.sprite = dropMarkerSprite;
        renderer.color = new Color(1f, 0.65f, 0.2f, 0.92f);
        renderer.sortingOrder = short.MaxValue;
        return marker;
    }

    #endregion

    #region Reflection helpers and compatibility

    private void SetAdministrator()
    {
        PlayerAdminController adminController =
            FindFirstComponent("PlayerAdminController") as PlayerAdminController;
        if (adminController != null && adminController.TryEnableAdministrator())
        {
            RefreshAdminInvincibilityButton();
            SetStatus("管理员已启用（兼容现有 F1 管理员逻辑）。", new Color(0.35f, 0.95f, 0.85f));
            return;
        }

        Component controller = adminController;
        object player = ReadMember(controller, "player") ?? FindFirstComponent("Player");
        object playerData = ReadMember(player, "Data");
        if (playerData == null || !WriteMember(playerData, "Name_User", AdministratorName))
        {
            SetStatus("未找到本地玩家数据，无法设置管理员。", Color.yellow);
            return;
        }

        RefreshAdminInvincibilityButton();
        SetStatus("管理员已启用（兼容现有 F1 管理员逻辑）。", new Color(0.35f, 0.95f, 0.85f));
    }

    private void RebindLegacyF4Conflict()
    {
        foreach (MonoBehaviour behaviour in FindSceneBehaviours())
        {
            if (behaviour.GetType().Name != "GameDebugManager")
                continue;

            FieldInfo keyField = behaviour.GetType().GetField("setClearWeatherKey", InstanceFlags);
            if (keyField != null && keyField.FieldType == typeof(KeyCode) && (KeyCode)keyField.GetValue(behaviour) == KeyCode.F4)
            {
                keyField.SetValue(behaviour, KeyCode.F6);
                legacyF4WasRebound = true;
            }
        }
    }

    private void GuardLegacyAdminF4ForThisFrame()
    {
        Component controller = FindFirstComponent("PlayerAdminController");
        object player = ReadMember(controller, "player");
        object playerData = ReadMember(player, "Data");
        MemberInfo nameMember = FindMember(playerData, "Name_User");
        if (nameMember == null || !(ReadMember(playerData, nameMember) is string name) || name != AdministratorName)
            return;

        WriteMember(playerData, nameMember, "__GM_F4_GUARD__");
        f4AdminRestores.Add(new MemberRestore { Target = playerData, Member = nameMember, Value = name });
        if (restoreAdminCoroutine == null)
            restoreAdminCoroutine = StartCoroutine(RestoreAdminAfterCurrentFrame());
    }

    private IEnumerator RestoreAdminAfterCurrentFrame()
    {
        yield return new WaitForEndOfFrame();
        for (int i = 0; i < f4AdminRestores.Count; i++)
            WriteMember(f4AdminRestores[i].Target, f4AdminRestores[i].Member, f4AdminRestores[i].Value);
        f4AdminRestores.Clear();
        restoreAdminCoroutine = null;
    }

    private Transform GetLocalPlayerTransform()
    {
        Component itemManager = FindFirstComponent("ItemMgr");
        Transform transform = ReadMember(itemManager, "UserPlayerTransform") as Transform;
        if (transform != null)
            return transform;

        Component player = FindFirstComponent("Player");
        return player != null ? player.transform : null;
    }

    private static ChunkGenerator_Land FindLandGenerator()
    {
        Map[] maps = FindObjectsOfType<Map>(true);
        for (int i = 0; i < maps.Length; i++)
        {
            ChunkGenerator_Land generator = maps[i]?.LandGenerator;
            if (generator != null)
                return generator;
        }

        return null;
    }

    private static IEnumerable<MonoBehaviour> FindSceneBehaviours()
    {
        return FindObjectsOfType<MonoBehaviour>(true)
            .Where(behaviour => behaviour != null && behaviour.gameObject.scene.IsValid());
    }

    private static Component FindFirstComponent(string typeName)
    {
        return FindSceneBehaviours().FirstOrDefault(component => component.GetType().Name == typeName);
    }

    private static MethodInfo FindCompatibleMethod(Type type, string name, object[] arguments)
    {
        arguments = arguments ?? Array.Empty<object>();
        foreach (MethodInfo method in type.GetMethods(InstanceFlags))
        {
            if (method.Name != name)
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != arguments.Length)
                continue;

            bool isCompatible = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (arguments[i] != null && !parameters[i].ParameterType.IsInstanceOfType(arguments[i]) &&
                    !IsNumericType(parameters[i].ParameterType))
                {
                    isCompatible = false;
                    break;
                }
            }

            if (isCompatible)
                return method;
        }

        return null;
    }

    private static bool IsNumericType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) ||
               type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }

    private static object ReadMember(object target, string memberName)
    {
        return ReadMember(target, FindMember(target, memberName));
    }

    private static object ReadMember(object target, MemberInfo member)
    {
        if (target == null || member == null)
            return null;

        if (member is FieldInfo field)
            return field.GetValue(target);
        if (member is PropertyInfo property && property.CanRead)
            return property.GetValue(target);
        return null;
    }

    private static bool WriteMember(object target, string memberName, object value)
    {
        return WriteMember(target, FindMember(target, memberName), value);
    }

    private static bool WriteMember(object target, MemberInfo member, object value)
    {
        if (target == null || member == null)
            return false;

        if (member is FieldInfo field)
        {
            field.SetValue(target, value);
            return true;
        }

        if (member is PropertyInfo property && property.CanWrite)
        {
            property.SetValue(target, value);
            return true;
        }

        return false;
    }

    private static MemberInfo FindMember(object target, string memberName)
    {
        if (target == null)
            return null;

        Type type = target.GetType();
        return (MemberInfo)type.GetField(memberName, InstanceFlags) ?? type.GetProperty(memberName, InstanceFlags);
    }

    private static Type GetMemberType(object target, string memberName)
    {
        MemberInfo member = FindMember(target, memberName);
        if (member is FieldInfo field)
            return field.FieldType;
        if (member is PropertyInfo property)
            return property.PropertyType;
        return null;
    }

    private void SetStatus(string message, Color color)
    {
        if (statusText == null)
            return;

        statusText.text = message + (legacyF4WasRebound ? "  （旧天气 F4 已反射改为 F6）" : string.Empty);
        statusText.color = color;
    }

    #endregion
}
