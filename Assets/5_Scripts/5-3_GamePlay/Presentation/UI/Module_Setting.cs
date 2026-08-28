using UnityEngine.InputSystem;
using InputSystem;
using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;
using FlatWorld.Networking;
using FlatWorld.Settings;

/// <summary>
/// 玩家设置面板模块：负责实例化正式 UI_ActionList Prefab、绑定设置分页与世界会话操作，
/// 并统一管理面板期间的玩法输入锁和单机暂停。返回主界面与返回桌面共用一个保存退出确认层，
/// 最终保存与清理仍交给 GameManager 的权威退出流程。
/// </summary>
public class SettingCanvas : Module, IInstanceUI
{
    [ReadOnly]
    public BasePanel basePanel;
    public GameObject SettingCanvasPrefab;
    public Ex_ModData_MemoryPackable ModSaveData;
    public string PanelName = "设置面板";
    
    // 输入InputAction组件
    private PlayerInputActions playerInputActions;
    GameController gameController;
    private Mod_PlayerDeathState playerDeathState;
    private SettingsExitConfirmationController exitConfirmation;
    private Button returnToMainMenuButton;
    private Button returnToDesktopButton;
    private bool settingsPauseActive;
    private float timeScaleBeforeSettings;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    public override void Awake()
    {
        _Data.ID = ModText.Setting;
    }

    private void OnValidate()
    {
        _Data.ID = ModText.Setting;
    }

    public override void Load()
    {
        gameController = item.itemMods.GetMod_ByID(ModText.Controller).GetComponent<GameController>();
        // 初始化输入系统
        playerInputActions = gameController._inputActions;
        playerDeathState = item.itemMods.GetMod_ByID<Mod_PlayerDeathState>(
            Mod_PlayerDeathState.ModuleId);

        // 绑定ESC按键事件
        playerInputActions.Win10.ESC.performed += OnEscapePressed;
    }

    public override void Save()
    {
    }
    public override void Act()
    {
        base.Act();
    }


    // ESC按键响应
    private void OnEscapePressed(InputAction.CallbackContext context)
    {
        if (gameController != null && !gameController.IsGameplayInputAllowed(context))
            return;

        UIManager uiManager = UIManager.Instance;
        if (uiManager.WasCancelHandledThisFrame())
        {
            return;
        }

        if (exitConfirmation != null && exitConfirmation.TryClose())
        {
            uiManager.NotifyCancelHandled();
            return;
        }

        // Android 返回键顺序：临时面板之后先关闭不锁玩法的手机抽屉，再切换设置面板。
        if (PlayerMobileControlsHUD.TryCloseActiveDrawer())
        {
            uiManager.NotifyCancelHandled();
            return;
        }

        bool panelOpen = basePanel != null && basePanel.IsOpen();
        if (gameController != null && gameController.IsGameplayInputLocked && !panelOpen)
        {
            return;
        }

        I_TogglePanel();
    }

    /// <summary>创建唯一设置主面板，并把各设置逻辑绑定到十个内嵌分页。</summary>
    private bool EnsurePanelCreated()
    {
        if (basePanel != null && basePanel.gameObject != null)
            return false;

        if (SettingCanvasPrefab == null)
            throw new System.InvalidOperationException("[SettingCanvas] SettingCanvasPrefab 为空，无法创建设置面板");

        basePanel = UIManager.Instance.CreatePanelFromGameObject(SettingCanvasPrefab);
        BindButton(UIText.SaveButton, SaveGame);
        returnToMainMenuButton = BindButton(
            UIText.ReturnToMainMenuButton,
            RequestReturnToMainMenu);
        returnToDesktopButton = BindButton(
            UIText.ReturnToDesktopButton,
            RequestReturnToDesktop);
        exitConfirmation = SettingsExitConfirmationController.Ensure(
            basePanel,
            ExecuteExitDecision);

        SettingsActionListPagination pagination =
            SettingsActionListPagination.Ensure(basePanel.transform);
        Transform worldPage = pagination?.GetPageRoot(SettingsActionListPagination.WorldPageName);
        AudioSettingsPanelLauncher.Ensure(
            pagination?.GetPageRoot(SettingsActionListPagination.AudioPageName));
        UISettingsPanelLauncher.Ensure(
            pagination?.GetPageRoot(SettingsActionListPagination.InterfacePageName));
        CameraControlSettingsPanelLauncher.Ensure(
            pagination?.GetPageRoot(SettingsActionListPagination.CameraPageName));
        CoordinateDisplaySettingsPanelLauncher.Ensure(
            pagination?.GetPageRoot(SettingsActionListPagination.DisplayPageName));
        AutoSaveSettingsPanelLauncher.Ensure(
            pagination?.GetPageRoot(SettingsActionListPagination.AutoSavePageName),
            pagination);
        WorldStreamingSettingsPanelLauncher.Ensure(
            pagination?.GetPageRoot(SettingsActionListPagination.WorldStreamingPageName),
            pagination);
        DifficultySettingsPanelLauncher.Ensure(
            pagination?.GetPageRoot(SettingsActionListPagination.DifficultyPageName),
            worldPage,
            pagination);
        InputBindingPanelLauncher.Ensure(
            pagination?.GetPageRoot(SettingsActionListPagination.InputBindingPageName),
            basePanel,
            gameController);
        PlayerSuicideButton.Ensure(basePanel.transform, playerDeathState);
        BindButton("恢复所有设置", ResetAllSettings);
        basePanel.RefreshUIComponents();
        pagination?.RefreshPageLifecycles();
        basePanel.SetPanelName(PanelName);
        basePanel.PrepareForGamepadNavigation();
        basePanel.Opened += AcquirePanelInputLock;
        basePanel.Closed += ReleasePanelInputLock;
        basePanel.Opened += AcquireSettingsPause;
        basePanel.Closed += ReleaseSettingsPause;
        return true;
    }

    /// <summary>按唯一 Prefab 节点名绑定行为，节点缺失时直接中止面板初始化。</summary>
    private Button BindButton(string buttonName, UnityEngine.Events.UnityAction action)
    {
        Button button = basePanel.GetButton(buttonName);
        if (button == null)
        {
            throw new System.NullReferenceException(
                $"[SettingCanvas] 未找到按钮：{buttonName}");
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
        return button;
    }

    // 切换面板显示/隐藏状态
    private void TogglePanel()
    {
        if (basePanel != null)
        {
            // 根据当前状态切换显示与隐藏
            if (basePanel.IsVisible())
                basePanel.Close();
            else
            {
                OpenSettingsPanel();
            }
        }
    }

    #region 设置面板层级

    /// <summary>打开设置前重新确认高优先级 Canvas，确保对话气泡和玩法 HUD 无法盖住设置。</summary>
    private void OpenSettingsPanel()
    {
        if (basePanel == null)
            return;

        UIManager.Instance.ConfigureSettingsPanelLayer(basePanel);
        basePanel.Open();
        basePanel.transform.SetAsLastSibling();
    }

    #endregion

    public void I_ShowPanel()
    {
        EnsurePanelCreated();
        OpenSettingsPanel();
    }

    public void I_ClosePanel()
    {
        if (basePanel == null)
            throw new System.InvalidOperationException("[SettingCanvas] basePanel 为空，无法关闭设置面板");

        basePanel.Close();
    }

    /// <summary>恢复全部已注册设置与两类输入绑定默认值。</summary>
    private void ResetAllSettings()
    {
        SettingsProviderRegistry.ResetAllToDefaults();
        gameController?.InputBindings?.ResetToDefaults();
    }

    public void I_TogglePanel()
    {
        if (EnsurePanelCreated())
        {
            OpenSettingsPanel();
            return;
        }

        TogglePanel();
    }

    #region 会话操作

    /// <summary>打开返回游戏主界面的保存确认。</summary>
    private void RequestReturnToMainMenu()
    {
        exitConfirmation.Open(SettingsExitDestination.MainMenu, returnToMainMenuButton);
    }

    /// <summary>打开返回桌面的保存确认。</summary>
    private void RequestReturnToDesktop()
    {
        exitConfirmation.Open(SettingsExitDestination.Desktop, returnToDesktopButton);
    }

    /// <summary>只保存当前世界，不关闭设置面板。</summary>
    private void SaveGame()
    {
        GameManager.Instance.SaveGame();
    }

    /// <summary>按确认层决策进入 GameManager 的权威退出与清理流程。</summary>
    private void ExecuteExitDecision(
        SettingsExitDestination destination,
        bool saveBeforeExit)
    {
        if (destination == SettingsExitDestination.MainMenu)
        {
            GameManager.Instance.StartCoroutine(
                GameManager.Instance.BackToHelloScene_Coroutine(
                    item,
                    saveCurrentGame: saveBeforeExit));
            return;
        }

        GameManager.Instance.StartCoroutine(
            GameManager.Instance.BackToHelloScene_Coroutine(
                item,
                onComplete: QuitApplication,
                saveCurrentGame: saveBeforeExit));
    }

    /// <summary>退出游戏构建；编辑器中对应停止播放。</summary>
    private static void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    #endregion

    // 对象销毁时取消事件绑定
    private void OnDestroy()
    {
        if (playerInputActions != null)
        {
            playerInputActions.Win10.ESC.performed -= OnEscapePressed;
        }

        if (basePanel != null)
        {
            basePanel.Opened -= AcquirePanelInputLock;
            basePanel.Closed -= ReleasePanelInputLock;
            basePanel.Opened -= AcquireSettingsPause;
            basePanel.Closed -= ReleaseSettingsPause;
        }

        ReleasePanelInputLock();
        ReleaseSettingsPause();
    }

    #region 单机设置暂停

    /// <summary>单机打开世界设置时暂停时间；联机设置不修改全局时间流速。</summary>
    private void AcquireSettingsPause()
    {
        GameManager gameManager = GameManager.Instance;
        if (settingsPauseActive ||
            GameNetwork.IsOnline ||
            gameManager == null ||
            !gameManager.IsInGameWorld)
        {
            return;
        }

        timeScaleBeforeSettings = Time.timeScale;
        Time.timeScale = 0f;
        settingsPauseActive = true;
    }

    /// <summary>关闭设置或销毁模块时恢复打开设置前的时间流速。</summary>
    private void ReleaseSettingsPause()
    {
        if (!settingsPauseActive)
            return;

        Time.timeScale = timeScaleBeforeSettings;
        settingsPauseActive = false;
    }

    #endregion

    private void AcquirePanelInputLock()
    {
        gameController?.AcquireGameplayInputLock(this);
    }

    private void ReleasePanelInputLock()
    {
        gameController?.ReleaseGameplayInputLock(this);
    }
}
