using UnityEngine.InputSystem;
using InputSystem;
using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;
using FlatWorld.Networking;

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
        UIManager uiManager = UIManager.Instance;
        if (uiManager.WasCancelHandledThisFrame() ||
            uiManager.TryCloseTopmostCancelPanel(basePanel))
        {
            return;
        }

        bool panelOpen = basePanel != null && basePanel.IsOpen();
        if (gameController != null && gameController.IsGameplayInputLocked && !panelOpen)
        {
            return;
        }

        I_TogglePanel();
    }

    private bool EnsurePanelCreated()
    {
        if (basePanel != null && basePanel.gameObject != null)
            return false;

        if (SettingCanvasPrefab == null)
            throw new System.InvalidOperationException("[SettingCanvas] SettingCanvasPrefab 为空，无法创建设置面板");

        basePanel = UIManager.Instance.CreatePanelFromGameObject(SettingCanvasPrefab);
        AudioSettingsPanelBinder.Ensure(basePanel.transform);
        BindButton(UIText.ExitButtons, ExitGame);
        BindButton(UIText.SaveButtons, SaveGame);
        BindButton(UIText.CloseButtons, ClossApp);
        AudioSettingsPanelLauncher.Ensure(basePanel.transform);
        UISettingsPanelLauncher.Ensure(basePanel.transform);
        CoordinateDisplaySettingsPanelLauncher.Ensure(basePanel.transform);
        AutoSaveSettingsPanelLauncher.Ensure(basePanel.transform);
        WorldStreamingSettingsPanelLauncher.Ensure(basePanel.transform);
        DifficultySettingsPanelLauncher.Ensure(basePanel.transform);
        InputBindingPanelLauncher.Ensure(basePanel.transform, gameController);
        SettingsActionListPagination.Ensure(basePanel.transform);
        basePanel.SetPanelName(PanelName);
        basePanel.PrepareForGamepadNavigation();
        basePanel.Opened += AcquirePanelInputLock;
        basePanel.Closed += ReleasePanelInputLock;
        basePanel.Opened += AcquireSettingsPause;
        basePanel.Closed += ReleaseSettingsPause;
        return true;
    }

    private void BindButton(string[] buttonNames, UnityEngine.Events.UnityAction action)
    {
        foreach (string buttonName in buttonNames)
        {
            Button button = basePanel.GetButton(buttonName);
            if (button != null)
            {
                button.onClick.RemoveListener(action);
                button.onClick.AddListener(action);
                return;
            }
        }

        throw new System.NullReferenceException($"[SettingCanvas] 未找到按钮：{string.Join(" / ", buttonNames)}");
    }

    // 切换面板显示/隐藏状态
    private void TogglePanel()
    {
        if (basePanel != null)
        {
            // 根据当前状态切换显示与隐藏
            if (basePanel.IsVisible())
            {
                basePanel.Close();
            }
            else
            {
                basePanel.Open();
            }
        }
    }

    public void I_ShowPanel()
    {
        EnsurePanelCreated();
        basePanel.Open();
    }

    public void I_ClosePanel()
    {
        if (basePanel == null)
            throw new System.InvalidOperationException("[SettingCanvas] basePanel 为空，无法关闭设置面板");

        basePanel.Close();
    }

    public void I_TogglePanel()
    {
        if (EnsurePanelCreated())
        {
            basePanel.Open();
            return;
        }

        TogglePanel();
    }

    // 返回主菜单
    public void ExitGame()
    {
        // 通过协程返回主菜单场景
        // 注意：这里的调用者是SettingCanvas所在的Module，不是普通MonoBehaviour
        GameManager.Instance.StartCoroutine(GameManager.Instance.BackToHelloScene_Coroutine(item));
    }
    public void SaveGame()
    {
        GameManager.Instance.SaveGame();
    }
    public void ClossApp()
    {
        // 注意：这里的调用者是SettingCanvas所在的Module，不是普通MonoBehaviour
        GameManager.Instance.StartCoroutine(GameManager.Instance.BackToHelloScene_Coroutine(item, () =>
        {
#if UNITY_EDITOR
            // 在编辑器模式下停止播放
            UnityEditor.EditorApplication.isPlaying = false;

#else
        // 在构建版本中退出应用
        Application.Quit();
#endif
        }));
    }

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
