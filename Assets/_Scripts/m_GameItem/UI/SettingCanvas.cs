using UnityEngine.InputSystem;
using InputSystem;

public class SettingCanvas : Module
{
    public BasePanel basePanel;
    public Ex_ModData_MemoryPackable ModSaveData;
    PlayerController playerController;
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

    // ���InputAction����
    private PlayerInputActions playerInputActions;

    public void Start()
    {
        basePanel.GetButton("����ز��������水ť").onClick.AddListener(ExitGame);
        basePanel.GetButton("������Ϸ").onClick.AddListener(SaveGame);
        basePanel.GetButton("���沢�˳���Ϸ��ť").onClick.AddListener(ClossApp);


        // ���ȴ���Ʒ�����߻�ȡController
        playerController = item.Owner != null
            ? item.Owner.itemMods.GetMod_ByID(ModText.Controller).GetComponent<PlayerController>()
            : item.itemMods.GetMod_ByID(ModText.Controller).GetComponent<PlayerController>();

        // ��ʼ������ϵͳ
        playerInputActions = playerController._inputActions;

        // ��ESC�����¼�
        playerInputActions.Win10.ESC.performed += OnEscapePressed;
    }

    
    // ESC����������
    private void OnEscapePressed(InputAction.CallbackContext context)
    {
        TogglePanel();
    }
    
    // �л������ʾ/����״̬
    private void TogglePanel()
    {
        if (basePanel != null)
        {
            // ������������ʾ�����أ�������ʾ
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
    
    // ����:�ص�������
    public void ExitGame()
    {
        // ����ͨ��StartCoroutine����Э��
        // ע�⣺�����ߣ��˴���SettingCanvas��������MonoBehaviourʵ��
        GameManager.Instance.StartCoroutine(GameManager.Instance.ExitGameCoroutine());
    }
    public void SaveGame()
    {
        GameManager.Instance.SaveGame();
        ItemMgr.Instance.User_Player.Load();
    }
public void ClossApp()
{
        // ע�⣺�����ߣ��˴���SettingCanvas��������MonoBehaviourʵ��
        GameManager.Instance.StartCoroutine(GameManager.Instance.ExitGameCoroutine(() => {
#if UNITY_EDITOR
        // �ڱ༭��ģʽ��ֹͣ����
        UnityEditor.EditorApplication.isPlaying = false;
#else
        // �ڹ����汾���˳�Ӧ��
        Application.Quit();
#endif
    }));
}
    
    // �ڶ�������ʱȡ���¼���
    private void OnDestroy()
    {
        if (playerInputActions != null)
        {
            playerInputActions.Win10.ESC.performed -= OnEscapePressed;
        }
    }
}