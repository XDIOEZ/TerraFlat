using DG.Tweening.Core.Easing;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using InputSystem;

public class SettingCanvas : Module
{
    [Tooltip("�˳������水ť")]
    public Button Button_ExitGame;
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
        Button_ExitGame.onClick.AddListener(ExitGame);

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
    
    // �ڶ�������ʱȡ���¼���
    private void OnDestroy()
    {
        if (playerInputActions != null)
        {
            playerInputActions.Win10.ESC.performed -= OnEscapePressed;
        }
    }
}