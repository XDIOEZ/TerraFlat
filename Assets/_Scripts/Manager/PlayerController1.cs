using UnityEngine;
using UnityEngine.InputSystem;
using NaughtyAttributes;
using System.Collections.Generic;
using System.Collections;
using Cinemachine;
using InputSystem;

[RequireComponent(typeof(Player))]
public class PlayerController : MonoBehaviour
{
    #region �������
    private IFunction_Move movement;
    [ShowNativeProperty] public AttackTrigger Attack { get; private set; }
    [ShowNativeProperty] public FaceMouse Facing { get; private set; }
    [ShowNativeProperty] public TurnBody BodyTurning { get; private set; }
    [ShowNativeProperty] public Runner Running { get; private set; }
    [ShowNativeProperty] public Inventory_HotBar Hotbar { get; private set; }
    //�������
    [ShowNativeProperty] public CameraFollowManager VirtualCameraManager { get; set; }

    [ShowNativeProperty] public ItemDroper ItemDroper { get; private set; }

    [ShowNativeProperty] public HandForInteract _handForInteract { get; private set; }

    public IFunction_Move Movement
    {
        get
        {
            if (movement == null)
            {
                movement = GetComponentInChildren<IFunction_Move>();
                // Debug.Log("movement is null");
            }
            return movement;
        }
    }

    public ItemUIManager _playerUI;

    public IHunger _Hunger { get; private set; }
    #endregion

    #region ����ϵͳ
    private @PlayerInputActions _inputActions;
    public Camera _mainCamera;

    public bool CtrlIsDown;
    #endregion

    #region Unity��������
    private void Awake()
    {
        VirtualCameraManager = GetComponentInChildren<CameraFollowManager>();
        ItemDroper = GetComponentInChildren<ItemDroper>();
        _Hunger = GetComponent<IHunger>();
        Set_InputSystem();
    }


    

    public void OnDisable()
    {
        _inputActions.Disable();
    }

    //����ʱ����
    public void OnEnable()
    {
        _inputActions.Enable();
    }


    private void Start()
    {
        //     _mainCamera = Camera.main;
        InitializeComponents();

        SwitchBag();
        SwitchEquip();
        SwitchCraft();
        //    SwitchSetting();

        _mainCamera = Camera.main;
    }

    private void Update()
    {
        HandleCombatInput();
        HandleBodyTurning();
        HandleMovementInput();
        PlayerTakeItem_FaceMouse();
    }
    #endregion

    #region ���봦��
    #region ����ս������
    private void HandleCombatInput()
    {
        var mouseState = GetMouseKeyState();
        if (mouseState != KeyState.Void && Attack != null)
        {
            Attack.TriggerAttack(mouseState, GetMouseWorldPosition());
        }
    }
    #endregion

    #region ��������ƶ�����
    private void HandleMovementInput()
    {
        Vector2 moveInput = _inputActions.Win10.Move_Player.ReadValue<Vector2>();
        //���moveInput�Ƿ���Ч
        if (moveInput.magnitude > 1f)
        {
            moveInput.Normalize();
        }
        Movement.Move(moveInput);
    }
    #endregion

    #region �����������ת��
    private void HandleBodyTurning()
    {
        Vector3 mousePos = m_Mouse.Instance.transform.position;
        // �����λ��ת��Ϊ��������
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, transform.position.z));
        if (BodyTurning == null) return;

        float horizontal = Input.GetAxis("Horizontal");

        if (!Mathf.Approximately(horizontal, 0f))
        {
            // ��ȡ��Ļ���
            float screenWidth = Screen.width;

            // �ж�����Ƿ�����Ļ���
            if (mousePos.x < screenWidth / 2 && horizontal < 0)
            {
                BodyTurning.TurnBodyToDirection(Vector2.left);
            }
            // �ж�����Ƿ�����Ļ�Ҳ�
            else if (mousePos.x > screenWidth / 2 && horizontal > 0)
            {
                BodyTurning.TurnBodyToDirection(Vector2.right);
            }
        }
    }

    #endregion
    #endregion

    #region ��������
    #region ��ȡ������״̬
    private KeyState GetMouseKeyState()
    {
        if (Input.GetMouseButtonDown(0)) return KeyState.Start;
        if (Input.GetMouseButton(0)) return KeyState.Hold;
        if (Input.GetMouseButtonUp(0)) return KeyState.End;
        return KeyState.Void;
    }
    #endregion

    #region ��ȡ�����������
    private Vector3 GetMouseWorldPosition()
    {
        return _mainCamera.ScreenToWorldPoint(Input.mousePosition);
    }
    #endregion
    #endregion


    #region ��ʼ������
    #region ��ʼ������ű�������
    private void InitializeComponents()
    {
        Attack = GetComponentInChildren<AttackTrigger>();
        Facing = GetComponentInChildren<FaceMouse>();
        BodyTurning = GetComponentInChildren<TurnBody>();
        Running = GetComponentInChildren<Runner>();
        _handForInteract = GetComponentInChildren<HandForInteract>();
        Hotbar = _playerUI.HotBor.GetComponent<Inventory_HotBar>();
    }
    #endregion

    #region ��ʼ������ϵͳ�ļ���
    private void Set_InputSystem()
    {
        //��ʼ������ϵͳ
        _inputActions = new PlayerInputActions();
        _inputActions.Win10.Enable();
        var win10 = _inputActions.Win10;
        //���������
        win10.SwitchHotBar_Player.started += SwitchHotbar;
        //������Ʒ����
        win10.F.performed += PlayerDropItem;
        //��������
        win10.B.performed += SwitchBag;
        win10.B.performed += SwitchEquip;
        win10.B.performed += SwitchCraft;
        //�����������
        win10.ESC.performed += SwitchSetting;
        //�����ܲ�
        win10.Shift.started += Run;
        win10.Shift.canceled += StopRun;
        //�����ӽ��л�
        win10.CtrlMouse.performed += PovValueChanged;
        //�������
        win10.Mouse.performed += PlayerTakeItem_FaceMouse;
        //����������
        win10.MouseScroll.performed += SwitchHotbarByScroll;
        //����Ctrl��
        win10.Ctrl.started += (InputAction.CallbackContext context) => { CtrlIsDown = true; };
        win10.Ctrl.canceled += (InputAction.CallbackContext context) => { CtrlIsDown = false; };
        //��E�����м���
        win10.E.performed += Interact;
        //�����Ҽ�
        win10.RightClick.performed += UseItem;
    }
    #endregion
    #endregion

    #region ��Ʒ����

    #region ʹ��������ϵ���Ʒ
    public void UseItem(InputAction.CallbackContext context = default)
    {
        if (Hotbar.currentObject == null) return;
        Hotbar.currentObject.GetComponent<Item>().Act();
    }
    #endregion
    #region �͹������齻��
    //�͹������齻��
    public void Interact(InputAction.CallbackContext context = default)
    {
        _handForInteract.Interact_Start();
        _Hunger.Eat(2);
    }
    #endregion
    #region ������Ʒ
    public void PlayerDropItem(InputAction.CallbackContext context = default)
    {
        if (ItemDroper.DropItem() == false)//�����Ʒ����ʧ��
        {
            if (CtrlIsDown)
            {
                ItemDroper.FastDropItem(2);
            }
            else
            {
                ItemDroper.FastDropItem();
            }

        }

    }
    #endregion
    #region �޸�����ӽ�

    public void PovValueChanged(InputAction.CallbackContext context = default)
    {
        //��ȡ��������ֵ
        Vector2 scrollValue = (Vector2)context.ReadValueAsObject();
        //Debug.Log(scrollValue.y);
        if (scrollValue.y > 0)
        {
            //TODO��Ұ����
            VirtualCameraManager.ChangeCameraView(-1);
        }
        else if (scrollValue.y < 0)
        {
            //TODO��Ұ����
            VirtualCameraManager.ChangeCameraView(1);
        }
    }
    #endregion
    #region ʹ�ֳ���Ʒʼ�ճ������

    public void PlayerTakeItem_FaceMouse(InputAction.CallbackContext context = default)
    {
        context.ReadValue<Vector2>();
        //��ȡ���
        Facing.FaceToMouse(GetMouseWorldPosition());
    }
    #endregion
    #region �л��������ʹ��

    private void SwitchHotbar(InputAction.CallbackContext context)
    {
        if (context.control.device is Keyboard keyboard)
        {
            if (int.TryParse(context.control.displayName, out int keyNumber))
            {
                int targetIndex = keyNumber - 1;
                if (targetIndex != Hotbar.CurrentIndex)
                {
                    Hotbar.ChangeIndex(targetIndex);
                    return;
                }
            }
        }
    }
    #endregion
    #region ͨ�������л������

    private void SwitchHotbarByScroll(InputAction.CallbackContext context)
    {
        if (CtrlIsDown)
        {
            return;
        }
        Vector2 scrollValue = context.ReadValue<Vector2>();
        //Debug.Log(scrollValue);
        if (scrollValue.y > 0)
        {
            //Debug.Log(Hotbar.CurrentIndex);
            Hotbar.ChangeIndex(Hotbar.CurrentIndex - 1);
        }
        else if (scrollValue.y < 0)
        {
            Hotbar.ChangeIndex(Hotbar.CurrentIndex + 1);
        }
    }
    #endregion
    #region ���ر���

    //���ر���
    public void SwitchBag(InputAction.CallbackContext context = default)
    {
        _playerUI.Bag.enabled = !_playerUI.Bag.enabled;
    }
    #endregion
    #region �����������

    //�����������
    public void SwitchSetting(InputAction.CallbackContext context = default)
    {
        _playerUI.Setting.enabled = !_playerUI.Setting.enabled;
    }
    #endregion
    #region ����װ����λ

    //����װ����
    public void SwitchEquip(InputAction.CallbackContext context = default)
    {
        _playerUI.Equip.enabled = !_playerUI.Equip.enabled;
    }
    #endregion
    #region �����ֹ��ϳ����

    //����������
    public void SwitchCraft(InputAction.CallbackContext context = default)
    {
        _playerUI.Craft.enabled = !_playerUI.Craft.enabled;
    }
    #endregion
    #region ��ס���ñ���


    public void Run(InputAction.CallbackContext context = default)
    {
        if (Running != null)
        {
            Running.SwitchToRun(true);
            Debug.Log("Run");
        }
    }
    #endregion
    #region �ɿ�ֹͣ����


    public void StopRun(InputAction.CallbackContext context = default)
    {
        if (Running != null)
        {
            Running.SwitchToRun(false);
            Debug.Log("Stop");
        }
    }
    #endregion
    #endregion
}