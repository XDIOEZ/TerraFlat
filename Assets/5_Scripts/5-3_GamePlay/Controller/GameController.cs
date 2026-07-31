using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
    public InputBindingService InputBindings { get; private set; }
    public Camera _mainCamera; // 主相机引用
    public bool CtrlIsDown; // Ctrl状态（保留原字段）
    public InputDeviceType CurrentInputDevice => _currentInputDevice; // 当前活跃输入设备
    public bool IsUsingGamepad => _currentInputDevice == InputDeviceType.Gamepad;
    public event Action<InputDeviceType> ActiveInputDeviceChanged;

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
    private bool _suppressLeftClickUntilRelease;
    private bool _suppressRightClickUntilRelease;
    private readonly List<RaycastResult> _uiRaycastResults = new List<RaycastResult>(8);
    private readonly HashSet<object> _gameplayInputLockOwners = new HashSet<object>();

#endregion

#region 事件与数据

    public UltEvent LeftClick = new UltEvent(); // 左键按下事件
    public UltEvent LeftClickUp = new UltEvent(); // 左键抬起事件
    public UltEvent RightClick = new UltEvent(); // 右键按下事件
    public UltEvent RightClickUp = new UltEvent(); // 右键抬起事件

    public Ex_ModData _modData; // 模组数据
    public override ModuleData _Data { get => _modData; set => _modData = value as Ex_ModData; }

    public bool IsGameplayInputLocked => _isGameplayInputLocked || _gameplayInputLockOwners.Count > 0; // 当前是否锁定玩家输入

#endregion

#region Unity生命周期

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Controller;
        }

        _inputActions = new PlayerInputActions();
        ConfigureDeviceBindings();
        InputBindings = new InputBindingService(_inputActions.asset);
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
        InputBindings?.Dispose();
        InputBindings = null;
        _inputActions?.Dispose();
        _inputActions = null;
        _gameplayInputLockOwners.Clear();
        ActiveInputDeviceChanged = null;
        LeftClick.Clear();
        LeftClickUp.Clear();
        RightClick.Clear();
        RightClickUp.Clear();
    }

#endregion

#region 输入事件

    public void LeftClickAction(InputAction.CallbackContext obj) /// 左键按下
    {
        UpdateCurrentInputDevice(obj);
        if (IsGameplayInputLocked || IsPointerOverUI())
        {
            _suppressLeftClickUntilRelease = true;
            return;
        }

        _suppressLeftClickUntilRelease = false;
        LeftClick.Invoke();
    }

    public void LeftClickUpAction(InputAction.CallbackContext obj) /// 左键抬起
    {
        UpdateCurrentInputDevice(obj);
        if (_suppressLeftClickUntilRelease)
        {
            _suppressLeftClickUntilRelease = false;
            return;
        }

        if (IsGameplayInputLocked)
        {
            return;
        }

        LeftClickUp.Invoke();
    }

    public void RightClickAction(InputAction.CallbackContext obj) /// 右键按下
    {
        UpdateCurrentInputDevice(obj);
        if (IsGameplayInputLocked || IsPointerOverUI())
        {
            _suppressRightClickUntilRelease = true;
            return;
        }

        _suppressRightClickUntilRelease = false;
        RightClick.Invoke();
    }

    public void RightClickUpAction(InputAction.CallbackContext obj) /// 右键抬起
    {
        UpdateCurrentInputDevice(obj);
        if (_suppressRightClickUntilRelease)
        {
            _suppressRightClickUntilRelease = false;
            return;
        }

        if (IsGameplayInputLocked)
        {
            return;
        }

        RightClickUp.Invoke();
    }

    public void SetGameplayInputLocked(bool isLocked) /// 锁定或解锁玩家快捷键输入
    {
        _isGameplayInputLocked = isLocked;
    }

    public void AcquireGameplayInputLock(object owner)
    {
        if (owner != null)
            _gameplayInputLockOwners.Add(owner);
    }

    public void ReleaseGameplayInputLock(object owner)
    {
        if (owner != null)
            _gameplayInputLockOwners.Remove(owner);
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

    public bool IsPointerOverUI()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return false;

        PointerEventData eventData = new PointerEventData(eventSystem)
        {
            position = GetPointerScreenPosition()
        };

        _uiRaycastResults.Clear();
        eventSystem.RaycastAll(eventData, _uiRaycastResults);
        return _uiRaycastResults.Count > 0;
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
        _inputActions.Win10.GamepadCursor.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.GamepadCursor.canceled += UpdateCurrentInputDevice;
        _inputActions.Win10.HotbarPrevious.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.HotbarNext.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.CtrlMouse.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.E.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.F.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.B.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.P.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.H.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.Shift.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.Ctrl.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.ESC.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.Tab.performed += UpdateCurrentInputDevice;
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
        _inputActions.Win10.GamepadCursor.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.GamepadCursor.canceled -= UpdateCurrentInputDevice;
        _inputActions.Win10.HotbarPrevious.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.HotbarNext.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.CtrlMouse.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.E.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.F.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.B.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.P.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.H.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.Shift.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.Ctrl.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.ESC.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.Tab.performed -= UpdateCurrentInputDevice;
    }

    private void ConfigureDeviceBindings()
    {
        if (!EnableGamepadAdapter)
            _inputActions.asset.bindingMask = InputBinding.MaskByGroup("Keyboard&Mouse");
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
            if (!EnableGamepadAdapter)
                return;

            if (!_virtualCursorInitialized)
            {
                InitializeVirtualCursor();
            }
            SetCurrentInputDevice(InputDeviceType.Gamepad);
            return;
        }

        if (device is Keyboard || device is Mouse)
        {
            SetCurrentInputDevice(InputDeviceType.KeyboardMouse);
        }
    }

    private void SetCurrentInputDevice(InputDeviceType deviceType)
    {
        if (_currentInputDevice == deviceType)
            return;

        _currentInputDevice = deviceType;
        ActiveInputDeviceChanged?.Invoke(deviceType);
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

        if (_inputActions == null)
        {
            return;
        }

        Vector2 look = _inputActions.Win10.GamepadCursor.ReadValue<Vector2>();
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
        // 输入绑定由 InputBindingService 构造时独立加载，不进入物品模块存档。
    }

    public override void Save()
    {
        // 输入绑定在重绑完成时独立保存，不进入物品模块存档。
    }

#endregion
}
