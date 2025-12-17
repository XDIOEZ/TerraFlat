using UnityEngine.InputSystem;
using InputSystem;
using UnityEngine;
using Sirenix.OdinInspector;

public class SettingCanvas : Module
{
    [ReadOnly]
    public BasePanel basePanel;
    public GameObject SettingCanvasPrefab;
    public Ex_ModData_MemoryPackable ModSaveData;
    public string PanelName = "设置面板";
    
    // 添加InputAction引用
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


    // ESC按键处理方法
    private void OnEscapePressed(InputAction.CallbackContext context)
    {
        // 检测BasePanel是否存在 不存在 就先实例化
        if (basePanel == null || basePanel.gameObject == null)
        {
            basePanel = UIManager.Instance.CreatePanelFromGameObject(SettingCanvasPrefab);
            basePanel.GetButton("保存并回到主界面按钮").onClick.AddListener(ExitGame);
            basePanel.GetButton("保存游戏").onClick.AddListener(SaveGame);
            basePanel.GetButton("保存并退出游戏按钮").onClick.AddListener(ClossApp);
            basePanel.SetPanelName(PanelName);
            basePanel.Open();
            return;
        }

        TogglePanel();
    }

    // 切换面板显示/隐藏状态
    private void TogglePanel()
    {
        if (basePanel != null)
        {
            // 如果面板正在显示则隐藏，否则显示
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

    // 方法:回到主场景
    public void ExitGame()
    {
        // 必须通过StartCoroutine启动协程
        // 注意：调用者（此处是SettingCanvas）必须是MonoBehaviour实例
        GameManager.Instance.StartCoroutine(GameManager.Instance.BackToHelloScene_Coroutine(item));
    }
    public void SaveGame()
    {
        GameManager.Instance.SaveGame();
    }
    public void ClossApp()
    {
        // 注意：调用者（此处是SettingCanvas）必须是MonoBehaviour实例
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

    // 在对象销毁时取消事件绑定
    private void OnDestroy()
    {
        if (playerInputActions != null)
        {
            playerInputActions.Win10.ESC.performed -= OnEscapePressed;
        }
    }
}