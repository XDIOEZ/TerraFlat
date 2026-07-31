// AI-Context: GameManager 的主菜单、新游戏与存档面板控制分部；直接组合 BasePanel，不使用领域 View 代理。

using System;
using System.Collections;
using System.Globalization;
using System.IO;
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

    public const string NewGamePanelKey = "NewGame";
    public const string NewGameStartButtonKey = "开始新游戏";
    public const string NewGameBackButtonKey = "返回上一个界面";
    public const string NewGamePlayerInputKey = "新增玩家名称输入框";
    public const string NewGameSaveInputKey = "新增存档名称输入框";
    public const string NewGameRadiusInputKey = "星球半径输入框";
    public const string NewGameNoiseInputKey = "噪声缩放输入框";
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
    public const string GameSaveBackButtonKey = "返回按钮";
    public const string GameSavePlayerInputKey = "选择或新增玩家名称输入框";
    public const string GameSaveSelectedTextKey = "选中的存档名称";

    private const string ContextMenuPanelKey = "ContextMenu";

    public const string WorldLoadingTitleKey = "加载标题";
    public const string WorldLoadingStatusKey = "加载状态";
    public const string WorldLoadingProgressKey = "加载进度";
    public const string WorldLoadingProgressTextKey = "加载进度文本";

    private GameDifficultyId pendingNewWorldDifficulty = GameDifficultyId.Simple;
    private GameDifficultyRuleValues pendingCustomDifficultyRules = new GameDifficultyRuleValues();

    private GameObject worldLoadingView;
    private Canvas worldLoadingCanvas;
    private TextMeshProUGUI worldLoadingTitle;
    private TextMeshProUGUI worldLoadingStatus;
    private TextMeshProUGUI worldLoadingProgressText;
    private Slider worldLoadingProgress;
    private Coroutine worldLoadingCompletionCoroutine;
    private bool isWorldEntryLoading;

    public static string GetNewGameDifficultyPresetButtonKey(GameDifficultyId difficulty)
    {
        return $"官方难度预设_{difficulty}";
    }

    #endregion

    #region 世界加载面板

    private bool BeginWorldEntryLoading(string title, string status, float progress)
    {
        if (isWorldEntryLoading)
        {
            Debug.LogWarning("[GameManager] 世界进入流程已在执行，忽略重复请求。");
            return false;
        }

        if (!EnsureWorldLoadingView())
            return false;

        isWorldEntryLoading = true;
        Event_PlayerEnterWorld -= OnWorldEntryPlayerReady;
        Event_PlayerEnterWorld += OnWorldEntryPlayerReady;
        SetWorldLoadingView(title, status, progress);
        worldLoadingView.SetActive(true);
        worldLoadingCanvas.sortingOrder = 32000;
        return true;
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

    private void SetWorldLoadingView(string title, string status, float progress)
    {
        if (!EnsureWorldLoadingView())
            return;

        float normalizedProgress = Mathf.Clamp01(progress);
        worldLoadingTitle.text = title;
        worldLoadingStatus.text = status;
        worldLoadingProgress.value = normalizedProgress;
        worldLoadingProgressText.text = $"{Mathf.RoundToInt(normalizedProgress * 100f)}%";
    }

    private void OnWorldEntryPlayerReady(Player player)
    {
        if (!isWorldEntryLoading)
            return;

        Event_PlayerEnterWorld -= OnWorldEntryPlayerReady;
        if (worldLoadingCompletionCoroutine != null)
            StopCoroutine(worldLoadingCompletionCoroutine);
        worldLoadingCompletionCoroutine = StartCoroutine(CompleteWorldEntryLoadingCoroutine());
    }

    private IEnumerator CompleteWorldEntryLoadingCoroutine()
    {
        SetWorldLoadingView("正在进入世界", "正在加载玩家周围区域…", 0.78f);
        yield return null;

        float displayedProgress = 0.78f;
        while (ChunkMgr.Instance != null && ChunkMgr.Instance.HasPendingChunkLoads)
        {
            displayedProgress = Mathf.MoveTowards(
                displayedProgress,
                0.95f,
                Mathf.Max(0.002f, Time.unscaledDeltaTime * 0.08f));
            SetWorldLoadingView("正在进入世界", "正在生成并加载周围区块…", displayedProgress);
            yield return null;
        }

        yield return null;
        yield return null;
        SetWorldLoadingView("加载完成", "世界已经准备完毕。", 1f);
        yield return new WaitForSecondsRealtime(0.15f);
        HideWorldLoadingView();
    }

    private void FailWorldEntryLoading(string message, Exception exception = null)
    {
        Event_PlayerEnterWorld -= OnWorldEntryPlayerReady;
        if (exception != null)
            Debug.LogException(exception, this);
        Debug.LogError($"[GameManager] {message}", this);

        if (worldLoadingCompletionCoroutine != null)
            StopCoroutine(worldLoadingCompletionCoroutine);
        worldLoadingCompletionCoroutine = StartCoroutine(HideFailedWorldLoadingCoroutine(message));
    }

    private IEnumerator HideFailedWorldLoadingCoroutine(string message)
    {
        SetWorldLoadingView("加载失败", message, 0f);
        yield return new WaitForSecondsRealtime(1.5f);
        HideWorldLoadingView();
    }

    private void HideWorldLoadingView()
    {
        Event_PlayerEnterWorld -= OnWorldEntryPlayerReady;
        if (worldLoadingView != null)
            worldLoadingView.SetActive(false);
        worldLoadingCompletionCoroutine = null;
        isWorldEntryLoading = false;
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
            return;

        BasePanel panel = UIManager.Instance.CreatePanelFromGameObject(UIPrefab_HelloCanvas, MainMenuPanelKey);
        panel.SetButtonOnClick(MainMenuContinueButtonKey, OpenGameSaveManager);
        panel.SetButtonOnClick(MainMenuNewGameButtonKey, OpenNewGame);
        panel.PrepareForGamepadNavigation(MainMenuContinueButtonKey, false);
        panel.Open();
    }

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
        radiusInput?.SetTextWithoutNotify(ReadyPlanetData.Radius.ToString(CultureInfo.InvariantCulture));
        noiseInput?.SetTextWithoutNotify(ReadyPlanetData.NoiseScale.ToString("0.########", CultureInfo.InvariantCulture));
        radiusInput?.onValueChanged.AddListener(OnPlanetRadiusChanged);
        noiseInput?.onValueChanged.AddListener(OnPlanetNoiseScaleChanged);
        BindNewGameDifficultyControls(panel);
        panel.PrepareForGamepadNavigation(NewGameStartButtonKey);
        panel.Open();
    }

    public void OpenGameSaveManager()
    {
        if (TryOpenExistingPanel(GameSavePanelKey))
        {
            SaveDataManager_UI.Instance?.Refresh();
            return;
        }

        if (UIPrefab_SaveManager == null)
            return;

        BasePanel panel = UIManager.Instance.CreatePanelFromGameObject(UIPrefab_SaveManager, GameSavePanelKey);
        panel.SetButtonOnClick(GameSaveStartButtonKey, OnClick_StartGame_Button);
        panel.SetButtonOnClick(GameSaveLoadButtonKey, OnClick_LoadSaveData_Button);
        panel.SetButtonOnClick(GameSaveBackButtonKey, panel.Close);
        panel.GetInputField(GameSavePlayerInputKey)?.onValueChanged.AddListener(OnUpdatePlayerNameChanged);
        panel.PrepareForGamepadNavigation(GameSaveStartButtonKey);
        panel.Open();
        SaveDataManager_UI.Instance?.Refresh();
    }

    private static bool TryOpenExistingPanel(string panelName)
    {
        if (!UIManager.Instance.TryGetPanel(panelName, out BasePanel panel))
            return false;

        panel.Open();
        return true;
    }

    private bool TryReadNewGameCreationInputs(out string saveName, out string playerName)
    {
        BasePanel panel = null;
        UIManager.Instance?.TryGetPanel(NewGamePanelKey, out panel);
        saveName = panel?.GetInputField(NewGameSaveInputKey)?.text;
        playerName = panel?.GetInputField(NewGamePlayerInputKey)?.text;

        if (panel == null)
        {
            Debug.LogWarning("[GameManager] 新世界面板不存在，无法读取世界生成参数。");
            return false;
        }

        TMP_InputField radiusInput = panel.GetInputField(NewGameRadiusInputKey);
        TMP_InputField noiseInput = panel.GetInputField(NewGameNoiseInputKey);
        if (!TryParsePlanetRadius(radiusInput?.text, out int radius))
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
        radiusInput.SetTextWithoutNotify(radius.ToString(CultureInfo.InvariantCulture));
        noiseInput.SetTextWithoutNotify(noiseScale.ToString("0.########", CultureInfo.InvariantCulture));
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
        if (title != null)
            title.text = definition.DisplayName;
        if (description != null)
            description.text = definition.Description;
        if (rules != null)
        {
            string deathRule = definition.PlayerDeath.DropAllCarriedItems ? "死亡掉落" : "死亡保留";
            rules.text =
                $"战斗：玩家 {FormatMultiplier(definition.CreatureCombat.PlayerAttackMultiplier)} / 生物伤害 {FormatMultiplier(definition.CreatureCombat.AttackMultiplier)} / 生物生命 {FormatMultiplier(definition.CreatureCombat.MaxHealthMultiplier)}\n" +
                $"生存：饥饿 {FormatMultiplier(definition.PlayerSurvival.HungerDrainMultiplier)} / 耐力消耗 {FormatMultiplier(definition.PlayerSurvival.StaminaConsumptionMultiplier)} / {deathRule}\n" +
                $"世界：时间 {FormatMultiplier(definition.World.TimeSpeedMultiplier)} / 生成 {FormatMultiplier(definition.World.SpawnFrequencyMultiplier)} / 战利品 {FormatMultiplier(definition.World.LootAmountMultiplier)}\n" +
                $"生产：生长 {FormatMultiplier(definition.Production.CropGrowthMultiplier)} / 熔炼 {FormatMultiplier(definition.Production.SmeltingSpeedMultiplier)} / 制作 {FormatMultiplier(definition.Production.CraftingOutputMultiplier)}";
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
            summary.text = $"难度设置  ·  {definition.DisplayName}";
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

    #region 新世界难度

    private void ApplyPendingNewWorldDifficulty(GameSaveData saveData)
    {
        if (saveData == null)
            return;

        saveData.Difficulty = GameDifficultyCatalog.Normalize(pendingNewWorldDifficulty);
        GameDifficultyCatalog.WriteCustomRules(saveData, pendingCustomDifficultyRules);
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
        if (SaveDataMgr.Instance?.SaveData == null || SaveDataMgr.Instance.SaveData.Seed == 0)
        {
            Debug.LogWarning("请先选择存档或创建新游戏");
            return;
        }

        BasePanel panel = GetSaveManagerPanel();
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
        if (!string.IsNullOrEmpty(selectedSaveName))
        {
            string path = Path.Combine(Application.persistentDataPath, "Saves", "LocalSaveData", selectedSaveName + ".bytes");
            SaveDataMgr.Instance.LoadSaveByDisk(path);
        }

        SaveDataManager_UI.Instance?.GeneratePlayerButtons();
    }

    public void OnClick_DeletSave_Button()
    {
        if (SaveMenuRightMenuUI.Instance.SelectInfo.Path == "")
        {
            SaveDataMgr.Instance.SaveData.PlayerData_Dict.Remove(SaveMenuRightMenuUI.Instance.SelectInfo.Name);
        }
        else if (SaveDataMgr.Instance != null)
        {
            string selectedSaveName = GetSaveManagerPanel()?.GetText(GameSaveSelectedTextKey)?.text;
            if (!string.IsNullOrEmpty(selectedSaveName))
            {
                string saveDirectory = Path.Combine(Application.persistentDataPath, "Saves", "LocalSaveData");
                SaveDataMgr.Instance.DeleteSave(saveDirectory, selectedSaveName);
            }
        }

        SaveDataManager_UI.Instance?.Refresh();
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
