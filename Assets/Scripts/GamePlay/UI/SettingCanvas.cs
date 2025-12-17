using UnityEngine.InputSystem;
using InputSystem;
using UnityEngine;

public class SettingCanvas : Module
{
    public BasePanel basePanel;
    public GameObject SettingCanvasPrefab;
    public Ex_ModData_MemoryPackable ModSaveData;
    GameController GameController;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Grow;
        }
    }

    public override void Load()
    {
        basePanel = GetComponentInChildren<BasePanel>();
    }

    public override void Save()
    {
    }
    public override void Act()
    {
        base.Act();
    }

    // 添加InputAction引用
    private PlayerInputActions playerInputActions;

    public void Start()
    {
        // 优先从物品所有者获取Controller
        GameController = item.Owner != null
            ? item.Owner.itemMods.GetMod_ByID(ModText.Controller).GetComponent<GameController>()
            : item.itemMods.GetMod_ByID(ModText.Controller).GetComponent<GameController>();

        // 初始化输入系统
        playerInputActions = GameController._inputActions;

        // 绑定ESC按键事件
        playerInputActions.Win10.ESC.performed += OnEscapePressed;
    }


    // ESC按键处理方法
    private void OnEscapePressed(InputAction.CallbackContext context)
    {
        //TODO 检测BasePanle是否存在 不存在 就先实例化
        basePanel = UIManager.Instance.CreatePanelFromGameObject(SettingCanvasPrefab);
        basePanel.GetButton("保存回并到主界面按钮").onClick.AddListener(ExitGame);
        basePanel.GetButton("保存游戏").onClick.AddListener(SaveGame);
        basePanel.GetButton("保存并退出游戏按钮").onClick.AddListener(ClossApp);

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
        GameManager.Instance.StartCoroutine(GameManager.Instance.ExitGameCoroutine(item));
    }
    public void SaveGame()
    {
        GameManager.Instance.SaveGame();
    }
    public void ClossApp()
    {
        // 注意：调用者（此处是SettingCanvas）必须是MonoBehaviour实例
        GameManager.Instance.StartCoroutine(GameManager.Instance.ExitGameCoroutine(item, () =>
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