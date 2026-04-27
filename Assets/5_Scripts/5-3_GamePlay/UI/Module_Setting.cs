using UnityEngine.InputSystem;
using InputSystem;
using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;

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
        if (gameController != null && gameController.IsGameplayInputLocked)
        {
            return;
        }

        I_TogglePanel();
    }

    private void EnsurePanelCreated()
    {
        if (basePanel != null && basePanel.gameObject != null)
            return;

        if (SettingCanvasPrefab == null)
            throw new System.InvalidOperationException("[SettingCanvas] SettingCanvasPrefab 为空，无法创建设置面板");

        basePanel = UIManager.Instance.CreatePanelFromGameObject(SettingCanvasPrefab);
        BindButton(UIText.ExitButtons, ExitGame);
        BindButton(UIText.SaveButtons, SaveGame);
        BindButton(UIText.CloseButtons, ClossApp);
        basePanel.SetPanelName(PanelName);
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
        EnsurePanelCreated();
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
    }
}
