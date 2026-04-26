using UnityEngine;
using UnityEngine.InputSystem;
using UltEvents;
using InputSystem;

[RequireComponent(typeof(Item))]
public class GameController : Module
{
    public enum InputDeviceType
    {
        KeyboardMouse,
        Gamepad
    }

#region 输入系统

    public PlayerInputActions _inputActions; // 新输入系统动作集合
    public Camera _mainCamera; // 主相机引用
    public bool CtrlIsDown; // Ctrl状态（保留原字段）
    public InputDeviceType CurrentInputDevice => _currentInputDevice; // 当前活跃输入设备

    [Header("手柄适配")]
    public bool EnableGamepadAdapter = true; // 是否启用手柄适配
    public bool UseGamepadVirtualCursor = true; // 手柄模式下是否启用虚拟光标
    public float GamepadCursorSpeed = 1300f; // 虚拟光标速度（像素/秒）
    public float GamepadCursorDeadZone = 0.18f; // 摇杆死区
    public float CursorClampPadding = 6f; // 光标屏幕边缘留白

    private InputDeviceType _currentInputDevice = InputDeviceType.KeyboardMouse; // 当前输入源缓存
    private Vector2 _virtualCursorScreenPosition; // 手柄虚拟光标位置
    private bool _virtualCursorInitialized; // 虚拟光标是否初始化
    private bool _isGameplayInputLocked; // 濒死/过场时是否锁定玩家输入

#endregion

#region 事件与数据

    public UltEvent LeftClick = new UltEvent(); // 左键按下事件
    public UltEvent LeftClickUp = new UltEvent(); // 左键抬起事件
    public UltEvent RightClick = new UltEvent(); // 右键按下事件
    public UltEvent RightClickUp = new UltEvent(); // 右键抬起事件

    public Ex_ModData _modData; // 模组数据
    public override ModuleData _Data { get => _modData; set => _modData = value as Ex_ModData; }

    public bool IsGameplayInputLocked => _isGameplayInputLocked; // 当前是否锁定玩家输入

#endregion

#region Unity生命周期

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Controller;
        }

        _inputActions = new PlayerInputActions();
        InjectGamepadBindings();
        InitializeVirtualCursor();
    }

    public void OnEnable()
    {
        _inputActions.Enable();
        RegisterInputCallbacks();
    }

    public void OnDisable()
    {
        if (_inputActions == null)
        {
            return;
        }

        UnregisterInputCallbacks();
        _inputActions.Disable();
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!EnableGamepadAdapter || !UseGamepadVirtualCursor)
        {
            return;
        }

        UpdateVirtualCursor(deltaTime);
    }

    public void OnDestroy()
    {
        LeftClick.Clear();
        LeftClickUp.Clear();
        RightClick.Clear();
        RightClickUp.Clear();
    }

#endregion

#region 输入事件

    public void LeftClickAction(InputAction.CallbackContext obj) /// 左键按下
    {
        if (_isGameplayInputLocked)
        {
            return;
        }

        UpdateCurrentInputDevice(obj);
        LeftClick.Invoke();
    }

    public void LeftClickUpAction(InputAction.CallbackContext obj) /// 左键抬起
    {
        if (_isGameplayInputLocked)
        {
            return;
        }

        UpdateCurrentInputDevice(obj);
        LeftClickUp.Invoke();
    }

    public void RightClickAction(InputAction.CallbackContext obj) /// 右键按下
    {
        if (_isGameplayInputLocked)
        {
            return;
        }

        UpdateCurrentInputDevice(obj);
        RightClick.Invoke();
    }

    public void RightClickUpAction(InputAction.CallbackContext obj) /// 右键抬起
    {
        if (_isGameplayInputLocked)
        {
            return;
        }

        UpdateCurrentInputDevice(obj);
        RightClickUp.Invoke();
    }

    public void SetGameplayInputLocked(bool isLocked) /// 锁定或解锁玩家快捷键输入
    {
        _isGameplayInputLocked = isLocked;
    }

#endregion

#region 对外输入接口

    public Vector2 GetPointerScreenPosition() /// 获取当前屏幕指针坐标
    {
        if (_currentInputDevice == InputDeviceType.Gamepad && UseGamepadVirtualCursor && _virtualCursorInitialized)
        {
            return _virtualCursorScreenPosition;
        }

        if (Mouse.current != null)
        {
            return Mouse.current.position.ReadValue();
        }

        return Input.mousePosition;
    }

    public Vector3 GetMouseWorldPosition() /// 获取指针世界坐标（鼠标或手柄虚拟光标）
    {
        if (_mainCamera == null)
        {
            throw new MissingReferenceException("[GameController] _mainCamera 为空，无法计算指针世界坐标");
        }

        Vector2 screenPos = GetPointerScreenPosition();
        Vector3 worldPos = _mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Mathf.Abs(_mainCamera.transform.position.z)));
        worldPos.z = 0f;
        return worldPos;
    }

#endregion

#region 手柄适配

    private void RegisterInputCallbacks() /// 注册输入监听
    {
        _inputActions.Win10.LeftClick.performed += LeftClickAction;
        _inputActions.Win10.LeftClick.canceled += LeftClickUpAction;
        _inputActions.Win10.RightClick.performed += RightClickAction;
        _inputActions.Win10.RightClick.canceled += RightClickUpAction;

        _inputActions.Win10.Move_Player.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.Move_Player.canceled += UpdateCurrentInputDevice;
        _inputActions.Win10.Mouse.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.CtrlMouse.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.E.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.F.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.Shift.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.Ctrl.performed += UpdateCurrentInputDevice;
    }

    private void UnregisterInputCallbacks() /// 取消输入监听
    {
        _inputActions.Win10.LeftClick.performed -= LeftClickAction;
        _inputActions.Win10.LeftClick.canceled -= LeftClickUpAction;
        _inputActions.Win10.RightClick.performed -= RightClickAction;
        _inputActions.Win10.RightClick.canceled -= RightClickUpAction;

        _inputActions.Win10.Move_Player.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.Move_Player.canceled -= UpdateCurrentInputDevice;
        _inputActions.Win10.Mouse.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.CtrlMouse.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.E.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.F.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.Shift.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.Ctrl.performed -= UpdateCurrentInputDevice;
    }

    private void InjectGamepadBindings() /// 运行时注入手柄绑定
    {
        if (!EnableGamepadAdapter)
        {
            return;
        }

        AddBindingIfMissing(_inputActions.Win10.Move_Player, "<Gamepad>/leftStick");
        AddBindingIfMissing(_inputActions.Win10.LeftClick, "<Gamepad>/rightTrigger");
        AddBindingIfMissing(_inputActions.Win10.LeftClick, "<Gamepad>/buttonSouth");
        AddBindingIfMissing(_inputActions.Win10.RightClick, "<Gamepad>/leftTrigger");
        AddBindingIfMissing(_inputActions.Win10.RightClick, "<Gamepad>/leftShoulder");
        AddBindingIfMissing(_inputActions.Win10.E, "<Gamepad>/buttonWest");
        AddBindingIfMissing(_inputActions.Win10.F, "<Gamepad>/buttonNorth");
        AddBindingIfMissing(_inputActions.Win10.Shift, "<Gamepad>/leftStickPress");
        AddBindingIfMissing(_inputActions.Win10.Ctrl, "<Gamepad>/rightStickPress");
        AddBindingIfMissing(_inputActions.Win10.ESC, "<Gamepad>/start");
        AddBindingIfMissing(_inputActions.Win10.Tab, "<Gamepad>/select");
        AddBindingIfMissing(_inputActions.Win10.CtrlMouse, "<Gamepad>/dpad");
        AddBindingIfMissing(_inputActions.Win10.MouseScroll, "<Gamepad>/dpad");
    }

    private static void AddBindingIfMissing(InputAction action, string path) /// 添加绑定（去重）
    {
        for (int i = 0; i < action.bindings.Count; i++)
        {
            if (action.bindings[i].path == path)
            {
                return;
            }
        }

        action.AddBinding(path);
    }

    private void UpdateCurrentInputDevice(InputAction.CallbackContext context) /// 根据事件更新输入源
    {
        if (context.control == null)
        {
            return;
        }

        InputDevice device = context.control.device;
        if (device is Gamepad)
        {
            _currentInputDevice = InputDeviceType.Gamepad;
            if (!_virtualCursorInitialized)
            {
                InitializeVirtualCursor();
            }
            return;
        }

        if (device is Keyboard || device is Mouse)
        {
            _currentInputDevice = InputDeviceType.KeyboardMouse;
        }
    }

    private void InitializeVirtualCursor() /// 初始化虚拟光标
    {
        if (Mouse.current != null)
        {
            _virtualCursorScreenPosition = Mouse.current.position.ReadValue();
        }
        else
        {
            _virtualCursorScreenPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        _virtualCursorInitialized = true;
    }

    private void UpdateVirtualCursor(float deltaTime) /// 更新手柄虚拟光标
    {
        if (_currentInputDevice != InputDeviceType.Gamepad)
        {
            return;
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad == null)
        {
            return;
        }

        Vector2 look = gamepad.rightStick.ReadValue();
        if (look.sqrMagnitude < GamepadCursorDeadZone * GamepadCursorDeadZone)
        {
            return;
        }

        if (!_virtualCursorInitialized)
        {
            InitializeVirtualCursor();
        }

        _virtualCursorScreenPosition += look * (GamepadCursorSpeed * deltaTime);

        float minX = CursorClampPadding;
        float maxX = Mathf.Max(minX, Screen.width - CursorClampPadding);
        float minY = CursorClampPadding;
        float maxY = Mathf.Max(minY, Screen.height - CursorClampPadding);
        _virtualCursorScreenPosition.x = Mathf.Clamp(_virtualCursorScreenPosition.x, minX, maxX);
        _virtualCursorScreenPosition.y = Mathf.Clamp(_virtualCursorScreenPosition.y, minY, maxY);
    }

#endregion

#region 数据存取

    public override void Load()
    {
        // TODO: 实现加载逻辑（例如玩家自定义键位）
    }

    public override void Save()
    {
        // TODO: 实现保存逻辑
    }

#endregion
}