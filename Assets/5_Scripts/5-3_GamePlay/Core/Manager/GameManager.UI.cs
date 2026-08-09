// AI-Context: GameManager 的主菜单、新游戏与存档面板控制分部；直接组合 BasePanel，不使用领域 View 代理。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GameManager
{
    #region UI 控件命名契约

    public const string MainMenuPanelKey = "UI_Hello";
    public const string MainMenuContinueButtonKey = "选择存档";
    public const string MainMenuNewGameButtonKey = "新游戏";
    public const string MainMenuMultiplayerButtonKey = "联机模式";
    public const string MainMenuSettingsButtonKey = "设置";
    public const string MainMenuSettingsPanelKey = RuntimeUIPrefabKeys.MainMenuSettings;
    public const string MainMenuSettingsCloseButtonKey = "关闭按钮";
    public const string MainMenuSettingsReturnButtonKey = "返回按钮";
    public const string MainMenuSettingsPreferredControlKey = "窗口大小下拉列表";
    public const string MainMenuSettingsLanguageDropdownKey = "游戏语言下拉列表";
    public const string MainMenuSettingsLanguageStatusTextKey = "设置状态";

    private static readonly string[] MainMenuSettingsLocaleCodes = { "zh-CN", "en" };
    private static readonly string[] MainMenuSettingsLanguageOptions = { "简体中文", "English" };

    public const string NewGamePanelKey = "NewGame";
    public const string NewGameStartButtonKey = "开始新游戏";
    public const string NewGameBackButtonKey = "返回上一个界面";
    public const string NewGamePlayerInputKey = "新增玩家名称输入框";
    public const string NewGameSaveInputKey = "新增存档名称输入框";
    public const string NewGameRadiusInputKey = "星球半径输入框";
    public const string NewGameNoiseInputKey = "噪声缩放输入框";
    public const string NewGameSeedInputKey = "世界种子输入框";
    public const string NewGameTopologyToggleKey = "有限循环世界";
    public const string NewGameDifficultyButtonKey = "难度设置";
    public const string NewGameDifficultyPanelKey = "新世界难度设置面板";
    public const string NewGameDifficultyCloseButtonKey = "关闭难度设置";
    public const string NewGameDifficultyConfirmButtonKey = "确认难度设置";
    public const string NewGameDifficultyOfficialTabKey = "官方预设分页";
    public const string NewGameDifficultyCustomTabKey = "自定义分页";
    public const string NewGameDifficultyOfficialPageKey = "官方预设页";
    public const string NewGameDifficultyCustomPageKey = "自定义页";
    public const string NewGameDifficultyDropToggleKey = "死亡掉落全部物品";
    public const string NewGameDifficultyCombatCategoryKey = "自定义分类_战斗";
    public const string NewGameDifficultySurvivalCategoryKey = "自定义分类_生存";
    public const string NewGameDifficultyWorldCategoryKey = "自定义分类_世界";
    public const string NewGameDifficultyProductionCategoryKey = "自定义分类_生产";
    public const string NewGameDifficultyCombatPageKey = "自定义分类页_战斗";
    public const string NewGameDifficultySurvivalPageKey = "自定义分类页_生存";
    public const string NewGameDifficultyWorldPageKey = "自定义分类页_世界";
    public const string NewGameDifficultyProductionPageKey = "自定义分类页_生产";
    public const string NewGameDifficultySummaryTextKey = "难度设置_文字";
    public const string NewGameDifficultyTitleTextKey = "难度选择标题";
    public const string NewGameDifficultyDescriptionTextKey = "难度选择说明";
    public const string NewGameDifficultyRuleTextKey = "难度规则摘要";

    public const string NewGameDifficultyPlayerAttackSliderKey = "难度_玩家伤害倍率";
    public const string NewGameDifficultyCreatureAttackSliderKey = "难度_生物伤害倍率";
    public const string NewGameDifficultyCreatureHealthSliderKey = "难度_生物生命倍率";
    public const string NewGameDifficultyEnvironmentalDamageSliderKey = "难度_环境伤害倍率";
    public const string NewGameDifficultyHungerDrainSliderKey = "难度_饥饿消耗倍率";
    public const string NewGameDifficultyStaminaConsumptionSliderKey = "难度_耐力消耗倍率";
    public const string NewGameDifficultyStaminaRecoverySliderKey = "难度_耐力恢复倍率";
    public const string NewGameDifficultyHealingSliderKey = "难度_治疗效果倍率";
    public const string NewGameDifficultyTimeSpeedSliderKey = "难度_时间流逝倍率";
    public const string NewGameDifficultySpawnFrequencySliderKey = "难度_生成频率倍率";
    public const string NewGameDifficultySpawnPopulationSliderKey = "难度_种群上限倍率";
    public const string NewGameDifficultyLootAmountSliderKey = "难度_战利品倍率";
    public const string NewGameDifficultyCropGrowthSliderKey = "难度_作物生长倍率";
    public const string NewGameDifficultySmeltingSpeedSliderKey = "难度_熔炼速度倍率";
    public const string NewGameDifficultyFuelConsumptionSliderKey = "难度_燃料消耗倍率";
    public const string NewGameDifficultyCraftingOutputSliderKey = "难度_制作产量倍率";

    public const string GameSavePanelKey = "UI_GameSaveManager";
    public const string GameSaveStartButtonKey = "开始游戏按钮";
    public const string GameSaveLoadButtonKey = "加载存档按钮";
    public const string GameSaveDeleteButtonKey = "删除存档按钮";
    public const string GameSaveBackButtonKey = "返回按钮";
    public const string GameSavePlayerInputKey = "选择或新增玩家名称输入框";
    public const string GameSaveSelectedTextKey = "选中的存档名称";
    public const string GameSaveNoSelectionText = "尚未选择存档";

    private const string ContextMenuPanelKey = "ContextMenu";

    public const string WorldLoadingTitleKey = "加载标题";
    public const string WorldLoadingStatusKey = "加载状态";
    public const string WorldLoadingProgressKey = "加载进度";
    public const string WorldLoadingProgressTextKey = "加载进度文本";

    private const float WorldLoadingEllipsisFrameSeconds = 0.35f;

    private GameDifficultyId pendingNewWorldDifficulty = GameDifficultyId.Simple;
    private GameDifficultyRuleValues pendingCustomDifficultyRules = new GameDifficultyRuleValues();
    private WorldTopologyMode pendingNewWorldTopology = WorldTopologyMode.Wrapped;

    private GameObject worldLoadingView;
    private Canvas worldLoadingCanvas;
    private TextMeshProUGUI worldLoadingTitle;
    private TextMeshProUGUI worldLoadingStatus;
    private TextMeshProUGUI worldLoadingProgressText;
    private Slider worldLoadingProgress;
    private Coroutine worldLoadingHideCoroutine;
    private Coroutine worldLoadingStatusAnimationCoroutine;
    private string worldLoadingAnimatedStatusSource = string.Empty;
    private string worldLoadingStatusBase = string.Empty;
    private int worldLoadingStatusDotCount;

    private GameSaveStatusHUD saveStatusHUD;
    private int activeSaveOperationCount;
    private bool saveOperationFailed;

    public static string GetNewGameDifficultyPresetButtonKey(GameDifficultyId difficulty)
    {
        return $"官方难度预设_{difficulty}";
    }

    #endregion

    #region 世界加载面板

    partial void InitializeWorldEntryPresentation()
    {
        WorldEntryProgressChanged -= OnWorldEntryProgressChanged;
        WorldEntryProgressChanged += OnWorldEntryProgressChanged;
        saveStatusHUD = GameSaveStatusHUD.Ensure(this);
    }

    partial void DisposeWorldEntryPresentation()
    {
        WorldEntryProgressChanged -= OnWorldEntryProgressChanged;
        activeSaveOperationCount = 0;
        saveOperationFailed = false;
        saveStatusHUD = null;
    }

    #region 保存状态提示

    /// <summary>当前是否存在手动或自动保存任务。</summary>
    public bool IsSaveInProgress => activeSaveOperationCount > 0;

    /// <summary>登记一个保存任务并显示右上角提示。</summary>
    public void BeginSaveStatus()
    {
        if (activeSaveOperationCount == 0)
            saveOperationFailed = false;

        activeSaveOperationCount++;
        saveStatusHUD ??= GameSaveStatusHUD.Ensure(this);
        saveStatusHUD?.BeginSave();
    }

    /// <summary>完成一个保存任务；所有并行保存结束后统一决定成功或失败提示。</summary>
    public void CompleteSaveStatus(bool succeeded)
    {
        if (activeSaveOperationCount <= 0)
            return;

        if (!succeeded)
            saveOperationFailed = true;

        activeSaveOperationCount--;
        if (activeSaveOperationCount > 0)
            return;

        saveStatusHUD?.EndSave(!saveOperationFailed);
        saveOperationFailed = false;
    }

    #endregion

    private void OnWorldEntryProgressChanged(WorldEntryProgressInfo progress)
    {
        if (!EnsureWorldLoadingView())
            return;

        if (worldLoadingHideCoroutine != null)
        {
            StopCoroutine(worldLoadingHideCoroutine);
            worldLoadingHideCoroutine = null;
        }

        UpdateWorldLoadingView(progress.Title, progress.Status, progress.Progress);
        worldLoadingView.SetActive(true);
        worldLoadingCanvas.sortingOrder = 32000;

        if (progress.State == WorldEntryProgressState.Completed)
            worldLoadingHideCoroutine = StartCoroutine(HideWorldLoadingViewAfterDelay(0.15f));
        else if (progress.State == WorldEntryProgressState.Failed)
            worldLoadingHideCoroutine = StartCoroutine(HideWorldLoadingViewAfterDelay(1.5f));
    }

    private bool EnsureWorldLoadingView()
    {
        if (worldLoadingView != null)
            return true;

        if (GameRes.Instance == null)
        {
            Debug.LogError("[GameManager] 无法显示世界加载面板：GameRes 未就绪。");
            return false;
        }

        worldLoadingView = GameRes.Instance.InstantiatePrefab(RuntimeUIPrefabKeys.WorldLoading);
        if (worldLoadingView == null)
        {
            Debug.LogError($"[GameManager] 缺少加载面板 Prefab：{RuntimeUIPrefabKeys.WorldLoading}");
            return false;
        }

        worldLoadingCanvas = worldLoadingView.GetComponent<Canvas>();
        worldLoadingTitle = FindChildRecursive(worldLoadingView.transform, WorldLoadingTitleKey)
            ?.GetComponent<TextMeshProUGUI>();
        worldLoadingStatus = FindChildRecursive(worldLoadingView.transform, WorldLoadingStatusKey)
            ?.GetComponent<TextMeshProUGUI>();
        worldLoadingProgressText = FindChildRecursive(worldLoadingView.transform, WorldLoadingProgressTextKey)
            ?.GetComponent<TextMeshProUGUI>();
        worldLoadingProgress = FindChildRecursive(worldLoadingView.transform, WorldLoadingProgressKey)
            ?.GetComponent<Slider>();

        if (worldLoadingCanvas == null || worldLoadingTitle == null || worldLoadingStatus == null ||
            worldLoadingProgressText == null || worldLoadingProgress == null)
        {
            Debug.LogError("[GameManager] UI_WorldLoading.prefab 的加载控件命名契约不完整。");
            Destroy(worldLoadingView);
            worldLoadingView = null;
            return false;
        }

        DontDestroyOnLoad(worldLoadingView);
        return true;
    }

    private void UpdateWorldLoadingView(string title, string status, float progress)
    {
        if (!EnsureWorldLoadingView())
            return;

        float normalizedProgress = Mathf.Clamp01(progress);
        worldLoadingTitle.text = LocalizeWorldLoadingText(title);
        SetWorldLoadingStatus(LocalizeWorldLoadingText(status));
        worldLoadingProgress.value = normalizedProgress;
        worldLoadingProgressText.text = $"{Mathf.RoundToInt(normalizedProgress * 100f)}%";
    }

    private static string LocalizeWorldLoadingText(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return string.Empty;

        const string planetPrefix = "正在加载星球：";
        if (sourceText.StartsWith(planetPrefix, StringComparison.Ordinal))
        {
            return FlatWorldLocalizationService.GetUiFormat(
                "正在加载星球：{0}",
                sourceText.Substring(planetPrefix.Length));
        }

        const string travelPrefix = "正在前往：";
        if (sourceText.StartsWith(travelPrefix, StringComparison.Ordinal))
        {
            return FlatWorldLocalizationService.GetUiFormat(
                "正在前往：{0}",
                FlatWorldLocalizationService.GetUiText(sourceText.Substring(travelPrefix.Length)));
        }

        return FlatWorldLocalizationService.GetUiText(sourceText);
    }

    private void SetWorldLoadingStatus(string status)
    {
        status ??= string.Empty;
        if (worldLoadingStatusAnimationCoroutine != null &&
            string.Equals(worldLoadingAnimatedStatusSource, status, StringComparison.Ordinal))
        {
            return;
        }

        if (!TryGetAnimatedStatusBase(status, out string statusBase))
        {
            StopWorldLoadingStatusAnimation();
            worldLoadingStatus.text = status;
            return;
        }

        worldLoadingAnimatedStatusSource = status;
        if (!string.Equals(worldLoadingStatusBase, statusBase, StringComparison.Ordinal))
        {
            worldLoadingStatusBase = statusBase;
            worldLoadingStatusDotCount = 1;
            RefreshWorldLoadingStatusDots();
        }

        if (worldLoadingStatusAnimationCoroutine == null)
            worldLoadingStatusAnimationCoroutine = StartCoroutine(AnimateWorldLoadingStatusCoroutine());
    }

    private static bool TryGetAnimatedStatusBase(string status, out string statusBase)
    {
        if (status.EndsWith("...", StringComparison.Ordinal))
        {
            statusBase = status.Substring(0, status.Length - 3);
            return true;
        }

        if (status.EndsWith("…", StringComparison.Ordinal))
        {
            statusBase = status.Substring(0, status.Length - 1);
            return true;
        }

        statusBase = string.Empty;
        return false;
    }

    private IEnumerator AnimateWorldLoadingStatusCoroutine()
    {
        WaitForSecondsRealtime frameDelay = new WaitForSecondsRealtime(WorldLoadingEllipsisFrameSeconds);
        while (worldLoadingStatus != null && !string.IsNullOrEmpty(worldLoadingStatusBase))
        {
            yield return frameDelay;
            worldLoadingStatusDotCount = worldLoadingStatusDotCount % 3 + 1;
            RefreshWorldLoadingStatusDots();
        }

        worldLoadingStatusAnimationCoroutine = null;
    }

    private void RefreshWorldLoadingStatusDots()
    {
        if (worldLoadingStatus == null)
            return;

        int visibleDotCount = Mathf.Clamp(worldLoadingStatusDotCount, 1, 3);
        string visibleDots = new string('.', visibleDotCount);
        string hiddenDots = new string('.', 3 - visibleDotCount);
        worldLoadingStatus.text = hiddenDots.Length == 0
            ? worldLoadingStatusBase + visibleDots
            : $"{worldLoadingStatusBase}{visibleDots}<alpha=#00>{hiddenDots}";
    }

    private void StopWorldLoadingStatusAnimation()
    {
        if (worldLoadingStatusAnimationCoroutine != null)
            StopCoroutine(worldLoadingStatusAnimationCoroutine);
        worldLoadingStatusAnimationCoroutine = null;
        worldLoadingAnimatedStatusSource = string.Empty;
        worldLoadingStatusBase = string.Empty;
        worldLoadingStatusDotCount = 0;
    }

    private IEnumerator HideWorldLoadingViewAfterDelay(float delaySeconds)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, delaySeconds));
        HideWorldLoadingView();
    }

    private void HideWorldLoadingView()
    {
        StopWorldLoadingStatusAnimation();
        if (worldLoadingView != null)
            worldLoadingView.SetActive(false);
        worldLoadingHideCoroutine = null;
    }

    #endregion

    #region UI 预制体

    [Header("UI 预制体")]
    public GameObject UIPrefab_HelloCanvas;
    public GameObject UIPrefab_SaveManager;
    public GameObject UIPrefab_NewGame;
    public GameObject UIPrefab_ContextMenu;

    [Header("UI 面板名称配置")]
    [SerializeField] private string saveManagerPanelName = GameSavePanelKey;
    [SerializeField] private string saveManagerPanelNameLegacy = "存档选择面板";

    #endregion

    #region 面板入口

    public void OpenHellowCanvas()
    {
        if (TryOpenExistingPanel(MainMenuPanelKey))
            return;

        if (UIPrefab_HelloCanvas == null)
        {
            Debug.LogError(
                "[GameManager] 无法创建主菜单：UIPrefab_HelloCanvas 未配置。请检查 WorldManager.prefab 的 UI 预制体引用。",
                this);
            return;
        }

        UIManager uiManager = UIManager.Instance;
        if (uiManager == null)
        {
            Debug.LogError("[GameManager] 无法创建主菜单：UIManager 未就绪。", this);
            return;
        }

        BasePanel panel = uiManager.CreatePanelFromGameObject(UIPrefab_HelloCanvas, MainMenuPanelKey);
        if (panel == null)
        {
            Debug.LogError("[GameManager] 主菜单 Prefab 实例化后未获得 BasePanel。", this);
            return;
        }

        panel.SetButtonOnClick(MainMenuContinueButtonKey, OpenGameSaveManager);
        panel.SetButtonOnClick(MainMenuNewGameButtonKey, OpenNewGame);
        panel.SetButtonOnClick(MainMenuSettingsButtonKey, OpenMainMenuSettings);
        panel.PrepareForGamepadNavigation(MainMenuContinueButtonKey, false);
        panel.Open();
    }

    /// <summary>
    /// 打开主菜单设置面板，并同步语言下拉框的当前选择。
    /// </summary>
    public void OpenMainMenuSettings()
    {
        if (UIManager.Instance != null &&
            UIManager.Instance.TryGetPanel(MainMenuSettingsPanelKey, out BasePanel existingPanel))
        {
            RefreshMainMenuSettingsLanguage(existingPanel);
            existingPanel.Open();
            return;
        }

        if (GameRes.Instance == null)
        {
            Debug.LogError("[GameManager] 无法打开主菜单设置：GameRes 未就绪。", this);
            return;
        }

        GameObject prefab = GameRes.Instance.GetPrefab(MainMenuSettingsPanelKey, false);
        if (prefab == null)
        {
            Debug.LogError(
                $"[GameManager] 缺少主菜单设置 Prefab：{MainMenuSettingsPanelKey}。请检查 Addressables/Prefab 标签。",
                this);
            return;
        }

        UIManager uiManager = UIManager.Instance;
        BasePanel panel = uiManager.CreatePanelFromGameObject(prefab, MainMenuSettingsPanelKey);
        if (panel == null)
        {
            Debug.LogError("[GameManager] 主菜单设置 Prefab 实例化后未获得 BasePanel。", this);
            return;
        }

        panel.SetButtonOnClick(MainMenuSettingsCloseButtonKey, panel.Close);
        panel.SetButtonOnClick(MainMenuSettingsReturnButtonKey, panel.Close);
        BindMainMenuSettingsLanguage(panel);
        panel.PrepareForGamepadNavigation(MainMenuSettingsPreferredControlKey);
        panel.Open();
    }

    #region 主菜单语言设置

    /// <summary>绑定语言下拉框；当前提供简体中文和英语两个 Locale。</summary>
    private void BindMainMenuSettingsLanguage(BasePanel panel)
    {
        TMP_Dropdown languageDropdown = GetMainMenuSettingsLanguageDropdown(panel);
        if (languageDropdown == null)
        {
            Debug.LogError(
                $"[GameManager] 主菜单设置 Prefab 缺少语言下拉列表：{MainMenuSettingsLanguageDropdownKey}",
                panel);
            return;
        }

        FlatWorldLocalizationService.Initialize();
        languageDropdown.ClearOptions();
        languageDropdown.AddOptions(new List<string>(MainMenuSettingsLanguageOptions));
        languageDropdown.onValueChanged.AddListener(
            selectedIndex => OnMainMenuSettingsLanguageChanged(panel, selectedIndex));
        RefreshMainMenuSettingsLanguage(panel);
    }

    /// <summary>按下拉索引切换语言，失败时恢复当前有效选择。</summary>
    private static void OnMainMenuSettingsLanguageChanged(BasePanel panel, int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= MainMenuSettingsLocaleCodes.Length)
        {
            RefreshMainMenuSettingsLanguage(panel);
            return;
        }

        string localeCode = MainMenuSettingsLocaleCodes[selectedIndex];
        if (!FlatWorldLocalizationService.TrySetLocale(localeCode))
        {
            Debug.LogWarning($"[GameManager] 无法切换到未配置的语言：{localeCode}");
            RefreshMainMenuSettingsLanguage(panel);
            SetMainMenuSettingsLanguageStatus(
                panel,
                FlatWorldLocalizationService.GetUiFormat("语言切换失败：{0}", localeCode));
            return;
        }

        RefreshMainMenuSettingsLanguage(panel);
    }

    /// <summary>根据当前 Locale 回填下拉框，并显示即时保存状态。</summary>
    private static void RefreshMainMenuSettingsLanguage(BasePanel panel)
    {
        TMP_Dropdown languageDropdown = GetMainMenuSettingsLanguageDropdown(panel);
        if (languageDropdown == null)
            return;

        int selectedIndex = Array.IndexOf(
            MainMenuSettingsLocaleCodes,
            FlatWorldLocalizationService.CurrentLocaleCode);
        selectedIndex = selectedIndex < 0 ? 0 : selectedIndex;
        languageDropdown.SetValueWithoutNotify(selectedIndex);
        languageDropdown.RefreshShownValue();
        SetMainMenuSettingsLanguageStatus(
            panel,
            selectedIndex == 0
                ? FlatWorldLocalizationService.GetUiText("当前语言：简体中文")
                : FlatWorldLocalizationService.GetUiText("当前语言：English"));
    }

    /// <summary>查找主菜单设置内的语言下拉框。</summary>
    private static TMP_Dropdown GetMainMenuSettingsLanguageDropdown(BasePanel panel)
    {
        Transform dropdownTransform = panel == null
            ? null
            : FindChildRecursive(panel.transform, MainMenuSettingsLanguageDropdownKey);
        return dropdownTransform?.GetComponent<TMP_Dropdown>();
    }

    /// <summary>更新设置面板底部的语言状态文字。</summary>
    private static void SetMainMenuSettingsLanguageStatus(BasePanel panel, string status)
    {
        TextMeshProUGUI statusText = panel?.GetText(MainMenuSettingsLanguageStatusTextKey);
        if (statusText != null)
            statusText.text = status;
    }

    #endregion

    public void OpenContextMenu()
    {
        if (TryOpenExistingPanel(ContextMenuPanelKey))
            return;

        if (UIPrefab_ContextMenu == null)
            return;

        BasePanel panel = UIManager.Instance.CreatePanelFromGameObject(UIPrefab_ContextMenu, ContextMenuPanelKey);
        panel.PrepareForGamepadNavigation();
        panel.Open();
    }

    public void OpenNewGame()
    {
        if (TryOpenExistingPanel(NewGamePanelKey))
            return;

        if (UIPrefab_NewGame == null)
            return;

        BasePanel panel = UIManager.Instance.CreatePanelFromGameObject(UIPrefab_NewGame, NewGamePanelKey);
        panel.SetButtonOnClick(NewGameStartButtonKey, CreateNewWorld);
        panel.SetButtonOnClick(NewGameBackButtonKey, panel.Close);
        panel.GetInputField(NewGamePlayerInputKey)?.onValueChanged.AddListener(OnUpdatePlayerNameChanged);
        panel.GetInputField(NewGameSaveInputKey)?.onValueChanged.AddListener(OnSaveNameChanged);

        if (ReadyPlanetData == null)
            ReadyPlanetData = new PlanetData();

        ReadyPlanetData.Radius = Mathf.Max(1, ReadyPlanetData.Radius);
        ReadyPlanetData.NoiseScale = PlanetData.NormalizeNoiseScale(ReadyPlanetData.NoiseScale);

        TMP_InputField radiusInput = panel.GetInputField(NewGameRadiusInputKey);
        TMP_InputField noiseInput = panel.GetInputField(NewGameNoiseInputKey);
        Toggle topologyToggle = panel.GetToggle(NewGameTopologyToggleKey);
        radiusInput?.SetTextWithoutNotify(ReadyPlanetData.Radius.ToString(CultureInfo.InvariantCulture));
        noiseInput?.SetTextWithoutNotify(ReadyPlanetData.NoiseScale.ToString("0.########", CultureInfo.InvariantCulture));
        radiusInput?.onValueChanged.AddListener(OnPlanetRadiusChanged);
        noiseInput?.onValueChanged.AddListener(OnPlanetNoiseScaleChanged);
        topologyToggle?.SetIsOnWithoutNotify(pendingNewWorldTopology == WorldTopologyMode.Wrapped);
        if (radiusInput != null)
            radiusInput.interactable = pendingNewWorldTopology == WorldTopologyMode.Wrapped;
        topologyToggle?.onValueChanged.AddListener(isOn => OnWorldTopologyChanged(panel, isOn));
        ReadyPlanetData.TopologyMode = pendingNewWorldTopology;
        BindNewGameDifficultyControls(panel);
        panel.PrepareForGamepadNavigation(NewGameStartButtonKey);
        panel.Open();
    }

    public void OpenGameSaveManager()
    {
        if (TryOpenExistingPanel(GameSavePanelKey))
        {
            SaveDataManager_UI.Instance?.RefreshForGamepadOpen();
            return;
        }

        if (UIPrefab_SaveManager == null)
            return;

        BasePanel panel = UIManager.Instance.CreatePanelFromGameObject(UIPrefab_SaveManager, GameSavePanelKey);
        panel.SetButtonOnClick(GameSaveStartButtonKey, OnClick_StartGame_Button);
        panel.SetButtonOnClick(GameSaveLoadButtonKey, OnClick_LoadSaveData_Button);
        panel.SetButtonOnClick(GameSaveDeleteButtonKey, OnClick_DeleteSave_Button);
        panel.SetButtonOnClick(GameSaveBackButtonKey, panel.Close);
        panel.GetInputField(GameSavePlayerInputKey)?.onValueChanged.AddListener(OnUpdatePlayerNameChanged);
        // 动态存档条目在 RefreshForGamepadOpen 后创建；这里仅提供无存档时的安全回退。
        panel.PrepareForGamepadNavigation(GameSaveBackButtonKey);
        panel.Open();
        SaveDataManager_UI.Instance?.RefreshForGamepadOpen();
    }

    private static bool TryOpenExistingPanel(string panelName)
    {
        if (!UIManager.Instance.TryGetPanel(panelName, out BasePanel panel))
            return false;

        panel.Open();
        return true;
    }

    [Tooltip("从新世界面板组装请求并创建世界")]
    public void CreateNewWorld()
    {
        if (!TryBuildNewWorldCreationRequest(out NewWorldCreationRequest request))
            return;

        CreateNewWorld(request);
    }

    private bool TryBuildNewWorldCreationRequest(out NewWorldCreationRequest request)
    {
        request = null;
        BasePanel panel = null;
        UIManager.Instance?.TryGetPanel(NewGamePanelKey, out panel);
        string saveName = panel?.GetInputField(NewGameSaveInputKey)?.text;
        string playerName = panel?.GetInputField(NewGamePlayerInputKey)?.text;
        string worldSeed = panel?.GetInputField(NewGameSeedInputKey)?.text;

        if (panel == null)
        {
            Debug.LogWarning("[GameManager] 新世界面板不存在，无法读取世界生成参数。");
            return false;
        }

        TMP_InputField radiusInput = panel.GetInputField(NewGameRadiusInputKey);
        TMP_InputField noiseInput = panel.GetInputField(NewGameNoiseInputKey);
        Toggle topologyToggle = panel.GetToggle(NewGameTopologyToggleKey);
        WorldTopologyMode topologyMode = topologyToggle != null
            ? (topologyToggle.isOn ? WorldTopologyMode.Wrapped : WorldTopologyMode.Infinite)
            : pendingNewWorldTopology;
        int radius = Mathf.Max(1, ReadyPlanetData?.Radius ?? PlanetData.DefaultRadius);
        if (topologyMode == WorldTopologyMode.Wrapped &&
            !TryParsePlanetRadius(radiusInput?.text, out radius))
        {
            Debug.LogWarning($"[GameManager] 星球半径无效：{radiusInput?.text}。请输入大于 0 的整数。");
            return false;
        }

        if (!TryParseNoiseScale(noiseInput?.text, out float noiseScale))
        {
            Debug.LogWarning(
                $"[GameManager] 世界坐标缩放无效：{noiseInput?.text}。请输入 {PlanetData.MinNoiseScale} 到 {PlanetData.MaxNoiseScale} 之间的有限数值。");
            return false;
        }

        ReadyPlanetData ??= new PlanetData();
        ReadyPlanetData.Radius = radius;
        ReadyPlanetData.NoiseScale = noiseScale;
        ReadyPlanetData.TopologyMode = topologyMode;
        pendingNewWorldTopology = topologyMode;
        radiusInput?.SetTextWithoutNotify(radius.ToString(CultureInfo.InvariantCulture));
        noiseInput.SetTextWithoutNotify(noiseScale.ToString("0.########", CultureInfo.InvariantCulture));

        request = new NewWorldCreationRequest(
            saveName,
            playerName,
            worldSeed,
            ReadyPlanetData,
            ReadyTimeData,
            pendingNewWorldDifficulty,
            pendingCustomDifficultyRules);
        if (!request.TryValidate(out string validationError))
        {
            Debug.LogWarning($"[GameManager] 新世界参数无效：{validationError}");
            request = null;
            return false;
        }

        return true;
    }

    private void BindNewGameDifficultyControls(BasePanel panel)
    {
        pendingNewWorldDifficulty = GameDifficultyId.Simple;
        pendingCustomDifficultyRules = new GameDifficultyRuleValues();

        panel.SetButtonOnClick(NewGameDifficultyButtonKey, () => OpenNewGameDifficultyPanel(panel));
        panel.SetButtonOnClick(NewGameDifficultyCloseButtonKey, () => CloseNewGameDifficultyPanel(panel));
        panel.SetButtonOnClick(NewGameDifficultyConfirmButtonKey, () => ConfirmNewGameDifficulty(panel));
        panel.SetButtonOnClick(NewGameDifficultyOfficialTabKey, () => ShowNewGameDifficultyPage(panel, false));
        panel.SetButtonOnClick(NewGameDifficultyCustomTabKey, () =>
        {
            pendingNewWorldDifficulty = GameDifficultyId.Custom;
            ShowNewGameDifficultyPage(panel, true);
            RefreshNewGameDifficultyDetails(panel);
            RefreshNewGameDifficultySummary(panel);
        });
        panel.SetButtonOnClick(NewGameDifficultyCombatCategoryKey, () => ShowCustomDifficultyCategory(panel, NewGameDifficultyCombatPageKey));
        panel.SetButtonOnClick(NewGameDifficultySurvivalCategoryKey, () => ShowCustomDifficultyCategory(panel, NewGameDifficultySurvivalPageKey));
        panel.SetButtonOnClick(NewGameDifficultyWorldCategoryKey, () => ShowCustomDifficultyCategory(panel, NewGameDifficultyWorldPageKey));
        panel.SetButtonOnClick(NewGameDifficultyProductionCategoryKey, () => ShowCustomDifficultyCategory(panel, NewGameDifficultyProductionPageKey));
        for (int i = 0; i < GameDifficultyCatalog.All.Count; i++)
        {
            GameDifficultyId difficulty = GameDifficultyCatalog.All[i].Id;
            panel.SetButtonOnClick(
                GetNewGameDifficultyPresetButtonKey(difficulty),
                () => SelectNewGameDifficulty(panel, difficulty));
        }

        Toggle customDropToggle = panel.GetToggle(NewGameDifficultyDropToggleKey);
        if (customDropToggle != null)
        {
            customDropToggle.SetIsOnWithoutNotify(false);
            customDropToggle.onValueChanged.AddListener(value =>
            {
                pendingCustomDifficultyRules.DropAllCarriedItems = value;
                pendingNewWorldDifficulty = GameDifficultyId.Custom;
                RefreshNewGameDifficultyDetails(panel);
                RefreshNewGameDifficultySummary(panel);
            });
        }

        BindCustomDifficultySliders(panel);

        ShowNewGameDifficultyPage(panel, false);
        ShowCustomDifficultyCategory(panel, NewGameDifficultyCombatPageKey);
        CloseNewGameDifficultyPanel(panel);
        RefreshNewGameDifficultyDetails(panel);
        RefreshNewGameDifficultySummary(panel);
    }

    private void OpenNewGameDifficultyPanel(BasePanel panel)
    {
        SetNewGameDifficultyPanelVisible(panel, true);
        ShowNewGameDifficultyPage(panel, pendingNewWorldDifficulty == GameDifficultyId.Custom);
        RefreshNewGameDifficultyDetails(panel);
    }

    private static void CloseNewGameDifficultyPanel(BasePanel panel)
    {
        SetNewGameDifficultyPanelVisible(panel, false);
    }

    private void ConfirmNewGameDifficulty(BasePanel panel)
    {
        RefreshNewGameDifficultySummary(panel);
        CloseNewGameDifficultyPanel(panel);
    }

    private static void ShowNewGameDifficultyPage(BasePanel panel, bool showCustom)
    {
        SetChildVisible(panel, NewGameDifficultyOfficialPageKey, !showCustom);
        SetChildVisible(panel, NewGameDifficultyCustomPageKey, showCustom);
        SetButtonSelected(panel.GetButton(NewGameDifficultyOfficialTabKey), !showCustom);
        SetButtonSelected(panel.GetButton(NewGameDifficultyCustomTabKey), showCustom);
    }

    private static void ShowCustomDifficultyCategory(BasePanel panel, string selectedPage)
    {
        string[] pages =
        {
            NewGameDifficultyCombatPageKey,
            NewGameDifficultySurvivalPageKey,
            NewGameDifficultyWorldPageKey,
            NewGameDifficultyProductionPageKey
        };
        string[] buttons =
        {
            NewGameDifficultyCombatCategoryKey,
            NewGameDifficultySurvivalCategoryKey,
            NewGameDifficultyWorldCategoryKey,
            NewGameDifficultyProductionCategoryKey
        };

        for (int i = 0; i < pages.Length; i++)
        {
            bool selected = pages[i] == selectedPage;
            SetChildVisible(panel, pages[i], selected);
            SetButtonSelected(panel.GetButton(buttons[i]), selected);
        }
    }

    private void BindCustomDifficultySliders(BasePanel panel)
    {
        BindMultiplierSlider(panel, NewGameDifficultyPlayerAttackSliderKey, 0f, 3f,
            () => pendingCustomDifficultyRules.PlayerAttackMultiplier,
            value => pendingCustomDifficultyRules.PlayerAttackMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultyCreatureAttackSliderKey, 0f, 3f,
            () => pendingCustomDifficultyRules.CreatureAttackMultiplier,
            value => pendingCustomDifficultyRules.CreatureAttackMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultyCreatureHealthSliderKey, 0.25f, 4f,
            () => pendingCustomDifficultyRules.CreatureHealthMultiplier,
            value => pendingCustomDifficultyRules.CreatureHealthMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultyEnvironmentalDamageSliderKey, 0f, 3f,
            () => pendingCustomDifficultyRules.EnvironmentalDamageMultiplier,
            value => pendingCustomDifficultyRules.EnvironmentalDamageMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultyHungerDrainSliderKey, 0f, 3f,
            () => pendingCustomDifficultyRules.HungerDrainMultiplier,
            value => pendingCustomDifficultyRules.HungerDrainMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultyStaminaConsumptionSliderKey, 0f, 3f,
            () => pendingCustomDifficultyRules.StaminaConsumptionMultiplier,
            value => pendingCustomDifficultyRules.StaminaConsumptionMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultyStaminaRecoverySliderKey, 0f, 3f,
            () => pendingCustomDifficultyRules.StaminaRecoveryMultiplier,
            value => pendingCustomDifficultyRules.StaminaRecoveryMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultyHealingSliderKey, 0f, 3f,
            () => pendingCustomDifficultyRules.HealingMultiplier,
            value => pendingCustomDifficultyRules.HealingMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultyTimeSpeedSliderKey, 0f, 3f,
            () => pendingCustomDifficultyRules.TimeSpeedMultiplier,
            value => pendingCustomDifficultyRules.TimeSpeedMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultySpawnFrequencySliderKey, 0f, 3f,
            () => pendingCustomDifficultyRules.SpawnFrequencyMultiplier,
            value => pendingCustomDifficultyRules.SpawnFrequencyMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultySpawnPopulationSliderKey, 0.25f, 3f,
            () => pendingCustomDifficultyRules.SpawnPopulationMultiplier,
            value => pendingCustomDifficultyRules.SpawnPopulationMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultyLootAmountSliderKey, 0f, 3f,
            () => pendingCustomDifficultyRules.LootAmountMultiplier,
            value => pendingCustomDifficultyRules.LootAmountMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultyCropGrowthSliderKey, 0f, 4f,
            () => pendingCustomDifficultyRules.CropGrowthMultiplier,
            value => pendingCustomDifficultyRules.CropGrowthMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultySmeltingSpeedSliderKey, 0.1f, 4f,
            () => pendingCustomDifficultyRules.SmeltingSpeedMultiplier,
            value => pendingCustomDifficultyRules.SmeltingSpeedMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultyFuelConsumptionSliderKey, 0f, 3f,
            () => pendingCustomDifficultyRules.FuelConsumptionMultiplier,
            value => pendingCustomDifficultyRules.FuelConsumptionMultiplier = value);
        BindMultiplierSlider(panel, NewGameDifficultyCraftingOutputSliderKey, 0.25f, 3f,
            () => pendingCustomDifficultyRules.CraftingOutputMultiplier,
            value => pendingCustomDifficultyRules.CraftingOutputMultiplier = value);
    }

    private void BindMultiplierSlider(
        BasePanel panel,
        string sliderKey,
        float minimum,
        float maximum,
        System.Func<float> readValue,
        System.Action<float> writeValue)
    {
        Slider slider = panel.GetSlider(sliderKey);
        if (slider == null)
            return;

        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.wholeNumbers = false;
        slider.SetValueWithoutNotify(Mathf.Clamp(readValue(), minimum, maximum));
        UpdateMultiplierLabel(panel, sliderKey, slider.value);
        slider.onValueChanged.AddListener(value =>
        {
            writeValue(value);
            pendingNewWorldDifficulty = GameDifficultyId.Custom;
            UpdateMultiplierLabel(panel, sliderKey, value);
            RefreshNewGameDifficultyDetails(panel);
            RefreshNewGameDifficultySummary(panel);
        });
    }

    private static void UpdateMultiplierLabel(BasePanel panel, string sliderKey, float value)
    {
        TMP_Text valueText = panel.GetText(sliderKey + "_数值");
        if (valueText != null)
            valueText.text = $"{Mathf.RoundToInt(value * 100f)}%";
    }

    private void SelectNewGameDifficulty(BasePanel panel, GameDifficultyId difficulty)
    {
        pendingNewWorldDifficulty = GameDifficultyCatalog.Normalize(difficulty);
        RefreshNewGameDifficultyDetails(panel);
        RefreshNewGameDifficultySummary(panel);
    }

    private void RefreshNewGameDifficultyDetails(BasePanel panel)
    {
        GameDifficultyDefinition definition = pendingNewWorldDifficulty == GameDifficultyId.Custom
            ? GameDifficultyCatalog.CreateCustom(pendingCustomDifficultyRules)
            : GameDifficultyCatalog.Get(pendingNewWorldDifficulty);

        TMP_Text title = panel.GetText(NewGameDifficultyTitleTextKey);
        TMP_Text description = panel.GetText(NewGameDifficultyDescriptionTextKey);
        TMP_Text rules = panel.GetText(NewGameDifficultyRuleTextKey);
        string localizedName = FlatWorldLocalizationService.GetUiText(definition.DisplayName);
        if (title != null)
            title.text = localizedName;
        if (description != null)
            description.text = FlatWorldLocalizationService.GetUiText(definition.Description);
        if (rules != null)
        {
            string deathRule = definition.PlayerDeath.DropAllCarriedItems ? "死亡掉落" : "死亡保留";
            rules.text = FlatWorldLocalizationService.GetUiFormat(
                "战斗：玩家 {0} / 生物伤害 {1} / 生物生命 {2}\n生存：饥饿 {3} / 耐力消耗 {4} / {5}\n世界：时间 {6} / 生成 {7} / 战利品 {8}\n生产：生长 {9} / 熔炼 {10} / 制作 {11}",
                FormatMultiplier(definition.CreatureCombat.PlayerAttackMultiplier),
                FormatMultiplier(definition.CreatureCombat.AttackMultiplier),
                FormatMultiplier(definition.CreatureCombat.MaxHealthMultiplier),
                FormatMultiplier(definition.PlayerSurvival.HungerDrainMultiplier),
                FormatMultiplier(definition.PlayerSurvival.StaminaConsumptionMultiplier),
                FlatWorldLocalizationService.GetUiText(deathRule),
                FormatMultiplier(definition.World.TimeSpeedMultiplier),
                FormatMultiplier(definition.World.SpawnFrequencyMultiplier),
                FormatMultiplier(definition.World.LootAmountMultiplier),
                FormatMultiplier(definition.Production.CropGrowthMultiplier),
                FormatMultiplier(definition.Production.SmeltingSpeedMultiplier),
                FormatMultiplier(definition.Production.CraftingOutputMultiplier));
        }

        for (int i = 0; i < GameDifficultyCatalog.All.Count; i++)
        {
            GameDifficultyId difficulty = GameDifficultyCatalog.All[i].Id;
            SetButtonSelected(
                panel.GetButton(GetNewGameDifficultyPresetButtonKey(difficulty)),
                pendingNewWorldDifficulty == difficulty);
        }
    }

    private void RefreshNewGameDifficultySummary(BasePanel panel)
    {
        GameDifficultyDefinition definition = pendingNewWorldDifficulty == GameDifficultyId.Custom
            ? GameDifficultyCatalog.CreateCustom(pendingCustomDifficultyRules)
            : GameDifficultyCatalog.Get(pendingNewWorldDifficulty);

        TMP_Text summary = panel.GetText(NewGameDifficultySummaryTextKey);
        if (summary != null)
            summary.text = FlatWorldLocalizationService.GetUiFormat(
                "难度设置  ·  {0}",
                FlatWorldLocalizationService.GetUiText(definition.DisplayName));
    }

    private static string FormatMultiplier(float multiplier)
    {
        return $"{Mathf.RoundToInt(multiplier * 100f)}%";
    }

    private static void SetNewGameDifficultyPanelVisible(BasePanel panel, bool visible)
    {
        SetChildVisible(panel, NewGameDifficultyPanelKey, visible);
    }

    private static void SetChildVisible(BasePanel panel, string childName, bool visible)
    {
        Transform child = FindChildRecursive(panel.transform, childName);
        if (child != null)
            child.gameObject.SetActive(visible);
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == childName)
                return children[i];
        }

        return null;
    }

    private static void SetButtonSelected(Button button, bool selected)
    {
        if (button?.targetGraphic != null)
            button.targetGraphic.color = selected ? FlatWorldUITheme.Accent : FlatWorldUITheme.SurfaceRaised;
    }

    #endregion

    #region 存档面板事件

    private BasePanel GetSaveManagerPanel()
    {
        if (UIManager.Instance.TryGetPanel(saveManagerPanelName, out BasePanel panel))
            return panel;

        if (!string.IsNullOrEmpty(saveManagerPanelNameLegacy) &&
            UIManager.Instance.TryGetPanel(saveManagerPanelNameLegacy, out panel))
        {
            return panel;
        }

        Debug.LogError($"未找到存档管理面板: {saveManagerPanelName}");
        return null;
    }

    public void OnClick_StartGame_Button()
    {
        BasePanel panel = GetSaveManagerPanel();
        string selectedSaveName = panel?.GetText(GameSaveSelectedTextKey)?.text;
        if (SaveDataMgr.Instance?.SaveData == null || SaveDataMgr.Instance.SaveData.Seed == 0 ||
            string.IsNullOrWhiteSpace(selectedSaveName) ||
            string.Equals(selectedSaveName, GameSaveNoSelectionText, StringComparison.Ordinal))
        {
            Debug.LogWarning("请先选择存档或创建新游戏");
            return;
        }

        if (panel != null)
            ContinueGame(panel.GetInputField(GameSavePlayerInputKey)?.text);
    }

    public void OnClick_LoadSaveData_Button()
    {
        if (SaveDataMgr.Instance == null)
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
            return;
        }

        BasePanel panel = GetSaveManagerPanel();
        string selectedSaveName = panel?.GetText(GameSaveSelectedTextKey)?.text;
        if (string.IsNullOrWhiteSpace(selectedSaveName) ||
            string.Equals(selectedSaveName, GameSaveNoSelectionText, StringComparison.Ordinal))
        {
            Debug.LogWarning("请先选择要载入的存档");
            return;
        }

        string path = Path.Combine(Application.persistentDataPath, "Saves", "LocalSaveData", selectedSaveName + ".bytes");
        SaveDataMgr.Instance.LoadSaveByDisk(path);

        SaveDataManager_UI saveList = SaveDataManager_UI.Instance;
        if (saveList != null)
        {
            saveList.GeneratePlayerButtons();
            saveList.FocusFirstPlayerOrNameInputForGamepad();
        }
    }

    public void OnClick_DeleteSave_Button()
    {
        SaveDataMgr saveDataMgr = SaveDataMgr.Instance;
        if (saveDataMgr == null)
        {
            Debug.LogWarning("SaveAndLoad组件未绑定！");
            return;
        }

        BasePanel panel = GetSaveManagerPanel();
        string selectedSaveName = panel?.GetText(GameSaveSelectedTextKey)?.text;
        if (string.IsNullOrWhiteSpace(selectedSaveName) ||
            string.Equals(selectedSaveName, GameSaveNoSelectionText, StringComparison.Ordinal))
        {
            Debug.LogWarning("请先选择要删除的存档");
            return;
        }

        saveDataMgr.DeleteSave(saveDataMgr.UserSavePath, selectedSaveName);
        if (saveDataMgr.SaveData != null &&
            string.Equals(saveDataMgr.SaveData.saveName, selectedSaveName, StringComparison.Ordinal))
        {
            saveDataMgr.SaveData = null;
            saveDataMgr.CurrentContrrolPlayerName = string.Empty;
        }

        SaveDataManager_UI saveList = SaveDataManager_UI.Instance;
        if (saveList != null)
        {
            saveList.Refresh();
            saveList.ClearSaveSelection();
        }
        else
        {
            panel?.SetText(GameSaveSelectedTextKey, GameSaveNoSelectionText);
            panel?.SetInputFieldText(GameSavePlayerInputKey, string.Empty);
            Button deleteButton = panel?.GetButton(GameSaveDeleteButtonKey);
            if (deleteButton != null)
                deleteButton.interactable = false;
        }
    }

    // 保留旧拼写入口，避免已有 Inspector 事件丢失。
    public void OnClick_DeletSave_Button()
    {
        OnClick_DeleteSave_Button();
    }

    #endregion

    #region 输入事件

    private static void OnUpdatePlayerNameChanged(string playerName)
    {
        if (SaveDataMgr.Instance != null)
            SaveDataMgr.Instance.CurrentContrrolPlayerName = playerName;
    }

    private static void OnSaveNameChanged(string saveName)
    {
        if (SaveDataMgr.Instance?.SaveData != null)
            SaveDataMgr.Instance.SaveData.saveName = saveName;
    }

    private void OnPlanetRadiusChanged(string value)
    {
        if (TryParsePlanetRadius(value, out int radius))
        {
            ReadyPlanetData.Radius = radius;
            return;
        }

        Debug.LogWarning($"输入的半径值无效：{value}");
    }

    private void OnPlanetNoiseScaleChanged(string value)
    {
        if (TryParseNoiseScale(value, out float noiseScale))
        {
            ReadyPlanetData.NoiseScale = noiseScale;
            return;
        }

        Debug.LogWarning($"输入的噪声缩放值无效：{value}");
    }

    private void OnWorldTopologyChanged(BasePanel panel, bool wrapped)
    {
        pendingNewWorldTopology = wrapped ? WorldTopologyMode.Wrapped : WorldTopologyMode.Infinite;
        ReadyPlanetData ??= new PlanetData();
        ReadyPlanetData.TopologyMode = pendingNewWorldTopology;

        TMP_InputField radiusInput = panel?.GetInputField(NewGameRadiusInputKey);
        if (radiusInput != null)
            radiusInput.interactable = wrapped;
    }

    private static bool TryParsePlanetRadius(string value, out int radius)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out radius) && radius > 0;
    }

    private static bool TryParseNoiseScale(string value, out float noiseScale)
    {
        bool parsed = float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out noiseScale) ||
                      float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out noiseScale);
        return parsed && PlanetData.IsValidNoiseScale(noiseScale);
    }

    #endregion
}
