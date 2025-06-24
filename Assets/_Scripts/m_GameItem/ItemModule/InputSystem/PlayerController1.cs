using UnityEngine;
using UnityEngine.InputSystem;
using NaughtyAttributes;
using InputSystem;
using Sirenix.OdinInspector;
using static UnityEngine.InputSystem.Layouts.InputControlLayout;

[RequireComponent(typeof(Item))]
public class PlayerController : MonoBehaviour
{
    [ShowInInspector]
    public IFocusPoint _FocusPoint;
    public ISpeed _Speed;
    public Item ControledItem;
    #region 组件引用
    private IMove movement;
    [ShowNativeProperty] public AttackTrigger Attack { get; private set; }
    [ShowNativeProperty] public FaceMouse Facing { get; private set; }
    [ShowNativeProperty] public TurnBody BodyTurning { get; private set; }
    [ShowNativeProperty] public Runner Running { get; private set; }
    [ShowNativeProperty] public Inventory_HotBar Hotbar { get; private set; }
    //虚拟相机
    [ShowNativeProperty] public CameraFollowManager VirtualCameraManager { get; set; }

    [ShowNativeProperty] public ItemDroper ItemDroper { get; private set; }

    [ShowNativeProperty] public HandForInteract _handForInteract { get; private set; }

    [ShowInInspector]
    public Mover Mover;
  

    public IHunger _Hunger { get; private set; }
    #endregion

    #region 输入系统
    private @PlayerInputActions _inputActions;
    public Camera _mainCamera;

    public bool CtrlIsDown;
    #endregion

    #region Unity生命周期
    public void OnDisable()
    {
        _inputActions.Disable();

    }
    private void OnDestroy()
    {
        _inputActions.Win10.Disable();
    }
    public void Awake()
    {
        VirtualCameraManager = GetComponentInChildren<CameraFollowManager>();
        


    }
    public void OnEnable()
    {
        Set_InputSystem();
    }
    public void Start()
    {
        if(_mainCamera == null)
        _mainCamera = VirtualCameraManager.ControllerCamera;
      //  Set_InputSystem();
        //_mainCamera = Camera.main;
        InitializeComponents();

        SwitchBag();
        SwitchEquip();
        SwitchCraft();
        _Speed = GetComponentInChildren<ISpeed>();
        Debug.Log("初始化完成");
        _FocusPoint = GetComponent<IFocusPoint>();
        ControledItem = GetComponent<Item>();
        _FocusPoint = ControledItem as IFocusPoint;
        Mover = GetComponentInChildren<Mover>();
        VirtualCameraManager = GetComponentInChildren<CameraFollowManager>();
        ItemDroper = GetComponentInChildren<ItemDroper>();
        _Hunger = GetComponent<IHunger>();
        Hotbar = GetComponentInChildren<Inventory_HotBar>();

    }

    private void Update()
    {
        //同步聚焦点=鼠标位置
        _FocusPoint.FocusPointPosition = GetMouseWorldPosition();
        //处理战斗输入
        HandleCombatInput();
        //处理转身
        HandleBodyTurning();
        //处理移动输入
        HandleMovementInput();
        //处理视角切换
        PlayerTakeItem_FaceMouse();
    }
    #endregion

    #region 输入处理
    #region 处理战斗输入
    private void HandleCombatInput()
    {
        if(Attack == null) return;
        var mouseState = GetMouseKeyState();
        if (mouseState != KeyState.Void && Attack != null)
        {
            Attack.TriggerAttack(mouseState, GetMouseWorldPosition());
        }
    }
    #endregion

    #region 处理玩家移动输入
    private void HandleMovementInput()
    {
        if (Mover == null) return;

        Vector2 moveInput = _inputActions.Win10.Move_Player.ReadValue<Vector2>();

        if (moveInput.magnitude != 0)
        {
            _Speed.MoveTargetPosition = moveInput + (Vector2)transform.position;
        }else
        {
            _Speed.MoveTargetPosition = (Vector2)transform.position;
        }

        Mover.Move(_Speed.MoveTargetPosition);
    }
    #endregion

    #region 处理玩家身体转向
    private void HandleBodyTurning()
    {
        if (BodyTurning == null || ControledItem == null) return;

        // 获取聚焦点的位置（世界坐标）
        Vector3 focusPos = _FocusPoint.FocusPointPosition;
        Vector3 itemPos = ControledItem.transform.position;

        // 判断聚焦点在物体左边还是右边
        float direction = focusPos.x - itemPos.x;

        if (!Mathf.Approximately(direction, 0f))
        {
            if (direction < 0)
            {
                // 聚焦点在物体左边
                BodyTurning.TurnBodyToDirection(Vector2.left);
            }
            else
            {
                // 聚焦点在物体右边
                BodyTurning.TurnBodyToDirection(Vector2.right);
            }
        }
    }



    #endregion
    #endregion

    #region 辅助方法
    #region 获取鼠标键盘状态
    private KeyState GetMouseKeyState()
    {
        if (Input.GetMouseButtonDown(0)) return KeyState.Start;
        if (Input.GetMouseButton(0)) return KeyState.Hold;
        if (Input.GetMouseButtonUp(0)) return KeyState.End;
        return KeyState.Void;
    }
    #endregion

    #region 获取鼠标世界坐标
    private Vector3 GetMouseWorldPosition()
    {
        return _mainCamera.ScreenToWorldPoint(Input.mousePosition);
    }
    #endregion
    #endregion


    #region 初始化配置
    #region 初始化各类脚本的引用
    private void InitializeComponents() 
    {
        Attack = GetComponentInChildren<AttackTrigger>();
        Facing = GetComponentInChildren<FaceMouse>();
        BodyTurning = GetComponentInChildren<TurnBody>();
        Running = GetComponentInChildren<Runner>();
        _handForInteract = GetComponentInChildren<HandForInteract>();
       // Hotbar = _playerUI.HotBor.GetComponent<Inventory_HotBar>();
    }
    #endregion

    #region 初始化输入系统的监听
    private void Set_InputSystem()
    {
        //初始化输入系统
        _inputActions = new PlayerInputActions();

        _inputActions.Win10.Enable();

        var win10 = _inputActions.Win10;
        //监听快捷栏
        win10.SwitchHotBar_Player.started += SwitchHotbar;
        //监听物品丢弃
        win10.F.performed += PlayerDropItem;
        //监听背包
        win10.B.performed += SwitchBag;
        win10.B.performed += SwitchEquip;
        win10.B.performed += SwitchCraft;
        //监听设置面板
        win10.ESC.performed += SwitchSetting;
        //监听跑步
        win10.Shift.started += Run;
        win10.Shift.canceled += StopRun;
        //监听视角切换
        win10.CtrlMouse.performed += PovValueChanged;
        //监听鼠标
        win10.Mouse.performed += PlayerTakeItem_FaceMouse;
        //监听鼠标滚轮
        win10.MouseScroll.performed += SwitchHotbarByScroll;
        //监听Ctrl键
        win10.Ctrl.started += (InputAction.CallbackContext context) => { CtrlIsDown = true; };
        win10.Ctrl.canceled += (InputAction.CallbackContext context) => { CtrlIsDown = false; };
        //对E键进行监听
        win10.E.performed += Interact;
        //监听右键
        win10.RightClick.performed += UseItem;
    }
    #endregion
    #endregion

    #region 物品操作

    #region 使用玩家手上的物品
    public void UseItem(InputAction.CallbackContext context = default)
    {
        if (Hotbar.currentObject == null) return;
        Hotbar.currentObject.GetComponent<Item>().Act();
    }
    #endregion
    #region 和工作方块交互
    //和工作方块交互
    public void Interact(InputAction.CallbackContext context = default)
    {
        _handForInteract.Interact_Start();
    }
    #endregion
    #region 丢弃物品
    public void PlayerDropItem(InputAction.CallbackContext context = default)
    {
        if(Hotbar.currentObject != null)
        {
            ItemDroper.DropItemBySlot(Hotbar.CurrentSelectItemSlot);
            Hotbar.DestroyCurrentObject();
        }
        else
        {
            ItemDroper.FastDropItem();

            Hotbar.RefreshUI(Hotbar.CurrentIndex);
        }
        

    }
    #endregion
    #region 修改玩家视角

    public void PovValueChanged(InputAction.CallbackContext context = default)
    {
        //获取鼠标滚轮数值
        Vector2 scrollValue = (Vector2)context.ReadValueAsObject();
        //Debug.Log(scrollValue.y);
        if (scrollValue.y > 0)
        {
            //TODO视野减少
            VirtualCameraManager.ChangeCameraView(-1);
        }
        else if (scrollValue.y < 0)
        {
            //TODO视野增加
            VirtualCameraManager.ChangeCameraView(1);
        }
    }
    #endregion
    #region 使手持物品始终朝向鼠标

    public void PlayerTakeItem_FaceMouse(InputAction.CallbackContext context = default)
    {
        if(Facing == null)
            return;
        context.ReadValue<Vector2>();
        //获取鼠标
        Facing.FaceToMouse(GetMouseWorldPosition());
    }
    #endregion
    #region 切换快捷栏的使用

    private void SwitchHotbar(InputAction.CallbackContext context)
    {
        if (context.control.device is Keyboard keyboard)
        {
            if (int.TryParse(context.control.displayName, out int keyNumber))
            {
                int targetIndex = keyNumber - 1;
                if (targetIndex != Hotbar.CurrentIndex)
                {
                    Hotbar.ChangeSelectBoxPosition(targetIndex);
                    return;
                }
            }
        }
    }
    #endregion
    #region 通过滚轮切换快捷栏

    private void SwitchHotbarByScroll(InputAction.CallbackContext context)
    {
        if (Hotbar == null) return;
        if (CtrlIsDown)
        {
            return;
        }
        Vector2 scrollValue = context.ReadValue<Vector2>();
        //Debug.Log(scrollValue);
        if (scrollValue.y > 0)
        {
            //Debug.Log(Hotbar.CurrentIndex);
            Hotbar.ChangeSelectBoxPosition(Hotbar.CurrentIndex - 1);
        }
        else if (scrollValue.y < 0)
        {
            Hotbar.ChangeSelectBoxPosition(Hotbar.CurrentIndex + 1);
        }
    }
    #endregion
    #region 开关背包

    //开关背包
    public void SwitchBag(InputAction.CallbackContext context = default)
    {
 
    }
    #endregion
    #region 开关设置面板

    //开关设置面板
    public void SwitchSetting(InputAction.CallbackContext context = default)
    {
        //_playerUI.Setting.enabled = !_playerUI.Setting.enabled;
    }
    #endregion
    #region 开关装备栏位

    //开关装备栏
    public void SwitchEquip(InputAction.CallbackContext context = default)
    {
       // _playerUI.Equip.enabled = !_playerUI.Equip.enabled;
    }
    #endregion
    #region 开关手工合成面板

    //开关制作栏
    public void SwitchCraft(InputAction.CallbackContext context = default)
    {
        //_playerUI.Craft.enabled = !_playerUI.Craft.enabled;
    }
    #endregion

    #region 按住启用奔跑


    public void Run(InputAction.CallbackContext context = default)
    {
        if (Running != null)
        {
            Running.StartWork();
           // Debug.Log("Run");
        }
    }
    #endregion
    #region 松开停止奔跑


    public void StopRun(InputAction.CallbackContext context = default)
    {
        if (Running != null)
        {
            Running.StopWork();
            //Debug.Log("Stop");
        }
    }
    #endregion
    #endregion
}