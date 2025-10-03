using UnityEngine;
using UnityEngine.InputSystem;
using UltEvents;
using InputSystem;

[RequireComponent(typeof(Item))]
public class PlayerController : Module
{
    #region ����ϵͳ
    public PlayerInputActions _inputActions;
    public Camera _mainCamera;
    public bool CtrlIsDown;
    #endregion

    public UltEvent LeftClick = new UltEvent();
    public UltEvent RightClick = new UltEvent();

    public Ex_ModData _modData;
    public override ModuleData _Data { get => _modData; set => _modData = value as Ex_ModData; }

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Controller;
        }
        _inputActions = new PlayerInputActions();
    }

    #region Unity��������
    public void OnDisable()
    {
        // ȡ���Ҽ�����
        if (_inputActions != null && _inputActions.Win10.RightClick != null)
        {
            _inputActions.Win10.RightClick.performed -= RightClickAction;
        }
        _inputActions.Disable();
    }
    
    public void OnEnable()
    {
        _inputActions.Enable();
        _inputActions.Win10.LeftClick.performed += LeftClickAction;
        // ����Ҽ��������
        _inputActions.Win10.RightClick.performed += RightClickAction;
    }
    
    public void LeftClickAction(InputAction.CallbackContext obj)
    {
        LeftClick.Invoke();
    }
    
    // �Ҽ����������
    public void RightClickAction(InputAction.CallbackContext obj)
    {
        RightClick.Invoke();
    }
    
    
    public void OnDestroy()
    {
        // �����¼�
        LeftClick.Clear();
        RightClick.Clear();
    }

    #region ��ȡ�����������
    public Vector3 GetMouseWorldPosition()
    {
        return _mainCamera.ScreenToWorldPoint(Input.mousePosition);
    }
    #endregion

    #endregion


    #region ���ݴ�ȡ
    public override void Load()
    {
        // TODO: ʵ�ּ����߼�
    }

    public override void Save()
    {
        // TODO: ʵ�ֱ����߼�
    }
    #endregion
}