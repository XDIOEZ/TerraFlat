using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UltEvents;
using InputSystem;
using FlatWorld.Mobile;

[RequireComponent(typeof(Item))]
public class GameController : Module
{
    private const string PreferredInputDeviceKey = "FlatWorld.Input.PreferredDevice";

    public enum InputDeviceType
    {
        KeyboardMouse,
        Gamepad,
        Mobile
    }

#region 输入系统

    public PlayerInputActions _inputActions; // 新输入系统动作集合
    public InputActionAsset InputAsset => _inputActions?.asset;
    public InputBindingService InputBindings { get; private set; }
    public Camera _mainCamera; // 主相机引用
    public bool CtrlIsDown; // Ctrl状态（保留原字段）
    public InputDeviceType CurrentInputDevice => _currentInputDevice; // 当前活跃输入设备
    public bool IsUsingGamepad => _currentInputDevice == InputDeviceType.Gamepad;
    // Mobile 是玩家选择的组合操作方案，不能因为键盘按键或其它设备输入而被切走。
    public bool IsUsingMobile => _preferredInputDevice == InputDeviceType.Mobile;
    public InputDeviceType PreferredInputDevice => _preferredInputDevice;
    public event Action<InputDeviceType> ActiveInputDeviceChanged;
    public event Action AttackStarted;
    public event Action AttackEnded;

    [Header("手柄适配")]
    public bool EnableGamepadAdapter = true; // 是否启用手柄适配
    public bool UseGamepadVirtualCursor = true; // 手柄模式下是否启用虚拟光标
    public float GamepadCursorSpeed = 1300f; // 虚拟光标速度（像素/秒）
    [Min(1f)] public float GamepadCursorRadius = 120f; // 游戏内准星相对玩家的屏幕半径
    public float GamepadCursorDeadZone = 0.18f; // 摇杆死区
    public float CursorClampPadding = 6f; // 光标屏幕边缘留白

    private InputDeviceType _currentInputDevice = InputDeviceType.KeyboardMouse; // 当前输入源缓存
    private InputDeviceType _preferredInputDevice = InputDeviceType.KeyboardMouse; // 设置中锁定的玩法控制方案
    private Vector2 _virtualCursorScreenPosition; // 手柄虚拟光标位置
    private Vector2 _gamepadAimDirection = Vector2.right; // 游戏内手柄准星方向
    private bool _gamepadAimDirectionInitialized; // 是否已经收到有效右摇杆方向
    private bool _gamepadPointerActive; // 手机模式下最近一次是否由手柄接管指向/UI光标
    private bool _hardwareMousePointerActive; // 最近一次鼠标输入是否应作为 UI/世界指针
    private bool _virtualCursorInitialized; // 虚拟光标是否初始化
    private bool _isGameplayInputLocked; // 濒死/过场时是否锁定玩家输入
    private bool _suppressLeftClickUntilRelease;
    private bool _suppressRightClickUntilRelease;
    private bool _suppressMobileAttackUntilRelease;
    private bool _attackInputHeld;
    private Vector2 _mobileAimDirection = Vector2.right;
    private Vector2 _mobileAttackAimDirection = Vector2.right;
    private bool _mobileAimDirectionInitialized;
    private bool _mobileAttackActive;
    private bool _mobileAttackDraggedOutsideDeadZone;
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

    /// <summary>生成输入锁诊断文本，供自动化错误报告定位直接锁与叠加锁所有者。</summary>
    public string DescribeGameplayInputLockState()
    {
        var ownerNames = new List<string>(_gameplayInputLockOwners.Count);
        foreach (object owner in _gameplayInputLockOwners)
        {
            if (owner is UnityEngine.Object unityObject)
            {
                ownerNames.Add($"{owner.GetType().Name}({unityObject.name})");
            }
            else
            {
                ownerNames.Add(owner?.GetType().FullName ?? "null");
            }
        }

        return $"direct={_isGameplayInputLocked}, ownerCount={ownerNames.Count}, " +
               $"owners=[{string.Join(", ", ownerNames)}]";
    }

#endregion

#region Unity生命周期

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Controller;
        }

        _inputActions = new PlayerInputActions();
        LoadPreferredInputDevice();
        ApplyPreferredInputDevice();
        InputBindings = new InputBindingService(_inputActions.asset);
        InputBindings.BindingsChanged += HandleBindingsChanged;
        EventSystemGuard.ConfigureInputActions(_inputActions.asset);
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
        CancelActiveAttackAndMobileInput();
        _inputActions.Disable();
    }

    public override void ModUpdate(float deltaTime)
    {
        if (_preferredInputDevice == InputDeviceType.Mobile && !_gamepadPointerActive)
            UpdateMobileRadialCursor();

        if (!EnableGamepadAdapter || !UseGamepadVirtualCursor)
        {
            return;
        }

        UpdateVirtualCursor(deltaTime);
    }

    public void OnDestroy()
    {
        CancelActiveAttackAndMobileInput();
        InputBindings?.Dispose();
        if (InputBindings != null)
            InputBindings.BindingsChanged -= HandleBindingsChanged;
        EventSystemGuard.ClearInputActions(_inputActions?.asset);
        InputBindings = null;
        _inputActions?.Dispose();
        _inputActions = null;
        _gameplayInputLockOwners.Clear();
        ActiveInputDeviceChanged = null;
        AttackStarted = null;
        AttackEnded = null;
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
        if (obj.control?.device is Gamepad && EventSystemGuard.TryHandleGamepadVirtualCursorClick())
        {
            _suppressLeftClickUntilRelease = true;
            return;
        }

        if (IsGameplayInputLocked || IsPointerOverUI() || EventSystemGuard.IsGamepadUISelectionActive)
        {
            _suppressLeftClickUntilRelease = true;
            return;
        }

        _suppressLeftClickUntilRelease = false;
        LeftClick.Invoke();
        BeginAttack();
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
        EndAttack();
    }

    public void RightClickAction(InputAction.CallbackContext obj) /// 右键按下
    {
        UpdateCurrentInputDevice(obj);
        if (obj.control?.device is Gamepad && EventSystemGuard.TryHandleGamepadContextAction())
        {
            _suppressRightClickUntilRelease = true;
            return;
        }

        bool isMobileUse = obj.control?.device is FlatWorldMobileDevice;
        if (IsGameplayInputLocked || (!isMobileUse && IsPointerOverUI()) || EventSystemGuard.IsGamepadUISelectionActive)
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
        if (isLocked)
            CancelActiveAttackAndMobileInput();
        UIManager.Instance?.NotifyInteractionSurfaceChanged();
    }

    public void AcquireGameplayInputLock(object owner)
    {
        if (owner != null)
        {
            bool added = _gameplayInputLockOwners.Add(owner);
            if (added)
            {
                CancelActiveAttackAndMobileInput();
                UIManager.Instance?.NotifyInteractionSurfaceChanged();
            }
        }
    }

    public void ReleaseGameplayInputLock(object owner)
    {
        if (owner != null)
        {
            if (_gameplayInputLockOwners.Remove(owner))
                UIManager.Instance?.NotifyInteractionSurfaceChanged();
        }
    }

#endregion

#region 对外输入接口

    public Vector2 GetPointerScreenPosition() /// 获取当前屏幕指针坐标
    {
        if (_hardwareMousePointerActive && Mouse.current != null)
        {
            return Mouse.current.position.ReadValue();
        }

        if ((_currentInputDevice == InputDeviceType.Gamepad ||
             _preferredInputDevice == InputDeviceType.Mobile) &&
            UseGamepadVirtualCursor &&
            _virtualCursorInitialized)
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
        if (EventSystemGuard.IsGamepadUISelectionActive)
            return true;

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
        return WorldTopologyRuntime.NormalizePosition(worldPos);
    }

#endregion

#region 手柄适配

    private void RegisterInputCallbacks() /// 注册输入监听
    {
        _inputActions.Win10.LeftClick.performed += LeftClickAction;
        _inputActions.Win10.LeftClick.canceled += LeftClickUpAction;
        _inputActions.Win10.Attack_Player.started += MobileAttackStartedAction;
        _inputActions.Win10.Attack_Player.canceled += MobileAttackEndedAction;
        _inputActions.Win10.RightClick.performed += RightClickAction;
        _inputActions.Win10.RightClick.canceled += RightClickUpAction;

        _inputActions.Win10.Move_Player.performed += HandleMoveInputPerformed;
        _inputActions.Win10.Move_Player.canceled += UpdateCurrentInputDevice;
        // Shift 只是 Mover 的奔跑修饰键，不参与设备切换，避免键盘长按干扰手机触摸/UI 指针。
        _inputActions.Win10.Shift.started += HandleKeyboardModifierStarted;
        _inputActions.Win10.Mouse.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.GamepadCursor.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.GamepadCursor.canceled += UpdateCurrentInputDevice;
        _inputActions.Win10.MobileAim_Player.performed += HandleMobileAim;
        _inputActions.Win10.MobileAim_Player.canceled += HandleMobileAim;
        _inputActions.Win10.MobileAttackAim_Player.performed += HandleMobileAttackAim;
        _inputActions.Win10.MobileAttackAim_Player.canceled += HandleMobileAttackAim;
        _inputActions.Win10.OpenChat.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.SwitchHotBar_Player.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.HotbarPrevious.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.HotbarNext.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.CtrlMouse.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.E.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.F.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.B.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.P.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.H.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.ToggleRun.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.Ctrl.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.ESC.performed += UpdateCurrentInputDevice;
        _inputActions.Win10.Tab.performed += UpdateCurrentInputDevice;
    }

    private void UnregisterInputCallbacks() /// 取消输入监听
    {
        _inputActions.Win10.LeftClick.performed -= LeftClickAction;
        _inputActions.Win10.LeftClick.canceled -= LeftClickUpAction;
        _inputActions.Win10.Attack_Player.started -= MobileAttackStartedAction;
        _inputActions.Win10.Attack_Player.canceled -= MobileAttackEndedAction;
        _inputActions.Win10.RightClick.performed -= RightClickAction;
        _inputActions.Win10.RightClick.canceled -= RightClickUpAction;

        _inputActions.Win10.Move_Player.performed -= HandleMoveInputPerformed;
        _inputActions.Win10.Move_Player.canceled -= UpdateCurrentInputDevice;
        _inputActions.Win10.Shift.started -= HandleKeyboardModifierStarted;
        _inputActions.Win10.Mouse.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.GamepadCursor.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.GamepadCursor.canceled -= UpdateCurrentInputDevice;
        _inputActions.Win10.MobileAim_Player.performed -= HandleMobileAim;
        _inputActions.Win10.MobileAim_Player.canceled -= HandleMobileAim;
        _inputActions.Win10.MobileAttackAim_Player.performed -= HandleMobileAttackAim;
        _inputActions.Win10.MobileAttackAim_Player.canceled -= HandleMobileAttackAim;
        _inputActions.Win10.OpenChat.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.SwitchHotBar_Player.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.HotbarPrevious.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.HotbarNext.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.CtrlMouse.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.E.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.F.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.B.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.P.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.H.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.ToggleRun.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.Ctrl.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.ESC.performed -= UpdateCurrentInputDevice;
        _inputActions.Win10.Tab.performed -= UpdateCurrentInputDevice;
    }

    /// <summary>切换并保存玩家选择的玩法控制方案；UI 指针动作不受玩法绑定遮罩影响。</summary>
    public void SetPreferredInputDevice(InputDeviceType deviceType)
    {
        if (!Enum.IsDefined(typeof(InputDeviceType), deviceType))
            throw new ArgumentOutOfRangeException(nameof(deviceType), deviceType, "未知的输入设备类型");

        if (deviceType == InputDeviceType.Gamepad && !EnableGamepadAdapter)
            deviceType = InputDeviceType.KeyboardMouse;

        _preferredInputDevice = deviceType;
        PlayerPrefs.SetInt(PreferredInputDeviceKey, (int)deviceType);
        PlayerPrefs.Save();
        ApplyPreferredInputDevice();
    }

    /// <summary>恢复全局控制偏好；首次运行时桌面默认键鼠，移动平台默认触屏。</summary>
    private void LoadPreferredInputDevice()
    {
        InputDeviceType platformDefault = Application.isMobilePlatform
            ? InputDeviceType.Mobile
            : InputDeviceType.KeyboardMouse;
        int savedValue = PlayerPrefs.GetInt(PreferredInputDeviceKey, (int)platformDefault);
        _preferredInputDevice = Enum.IsDefined(typeof(InputDeviceType), savedValue)
            ? (InputDeviceType)savedValue
            : platformDefault;

        if (_preferredInputDevice == InputDeviceType.Gamepad && !EnableGamepadAdapter)
            _preferredInputDevice = InputDeviceType.KeyboardMouse;
    }

    /// <summary>切换控制偏好只影响界面呈现，不再用 bindingMask 让不同输入源互相屏蔽。</summary>
    private void ApplyPreferredInputDevice()
    {
        if (_inputActions == null)
            return;

        CancelActiveAttackAndMobileInput();
        EventSystemGuard.SetMobileAimCursorVisible(false);
        _hardwareMousePointerActive = false;
        // 玩法 Action 必须同时接收键盘、手柄和手机虚拟设备；设备偏好不能成为输入互斥锁。
        ClearParallelInputBindingMasks();

        if (_preferredInputDevice == InputDeviceType.Gamepad && !_virtualCursorInitialized)
            InitializeVirtualCursor();

        SetCurrentInputDevice(_preferredInputDevice);
    }

    /// <summary>清除资产及所有 ActionMap 的设备遮罩，防止旧配置再次隔离并行输入。</summary>
    private void ClearParallelInputBindingMasks()
    {
        InputActionAsset inputAsset = _inputActions?.asset;
        if (inputAsset == null)
            return;

        inputAsset.bindingMask = null;
        foreach (InputActionMap actionMap in inputAsset.actionMaps)
            actionMap.bindingMask = null;
    }

    private void UpdateCurrentInputDevice(InputAction.CallbackContext context) /// 校正所选输入源的运行时状态
    {
        InputDevice device = context.control?.device;
        if (device is FlatWorldMobileDevice && _preferredInputDevice == InputDeviceType.Mobile)
        {
            _hardwareMousePointerActive = false;
            DeactivateGamepadInput();
            SetCurrentInputDevice(InputDeviceType.Mobile);
            return;
        }

        if (device is Gamepad && EnableGamepadAdapter &&
            (_preferredInputDevice == InputDeviceType.Gamepad ||
             _preferredInputDevice == InputDeviceType.Mobile))
        {
            _hardwareMousePointerActive = false;
            ActivateGamepadInput();
            if (_preferredInputDevice == InputDeviceType.Gamepad)
                SetCurrentInputDevice(InputDeviceType.Gamepad);
            return;
        }

        if (device is Keyboard || device is Mouse)
        {
            // 键盘修饰键和鼠标点击只退出手柄 UI/虚拟光标，不切换手机玩法方案，也不清空触摸状态。
            DeactivateGamepadInput();
            if (device is Mouse)
                _hardwareMousePointerActive = true;

            if (_preferredInputDevice == InputDeviceType.KeyboardMouse)
                SetCurrentInputDevice(InputDeviceType.KeyboardMouse);
        }
    }

    /// <summary>键盘修饰键只退出手柄 UI 接管，不切换手机方案、不清理触摸状态。</summary>
    private void HandleKeyboardModifierStarted(InputAction.CallbackContext context)
    {
        if (context.control?.device is Keyboard)
            DeactivateGamepadInput();
    }

    private void SetCurrentInputDevice(InputDeviceType deviceType)
    {
        // 组合手机方案始终保留触摸语义；键盘/鼠标只能增加输入，不能把 HUD、摇杆和手机状态切掉。
        if (_preferredInputDevice == InputDeviceType.Mobile &&
            deviceType != InputDeviceType.Mobile)
        {
            if (deviceType == InputDeviceType.Gamepad)
                ActivateGamepadInput();
            return;
        }

        bool deviceChanged = _currentInputDevice != deviceType;
        _currentInputDevice = deviceType;
        // 即使缓存设备类型未变化，也要校正可能由场景切换遗留的全局 UI 手柄状态。
        EventSystemGuard.SetGamepadMode(deviceType == InputDeviceType.Gamepad);
        if (deviceChanged)
            ActiveInputDeviceChanged?.Invoke(deviceType);
    }

    private bool IsGamepadInputAvailable =>
        EnableGamepadAdapter &&
        (_preferredInputDevice == InputDeviceType.Gamepad ||
         _preferredInputDevice == InputDeviceType.Mobile);

    /// <summary>手机模式下只切换手柄的指向/UI输入源，不切走手机 HUD 和触屏控制。</summary>
    private void ActivateGamepadInput()
    {
        if (!IsGamepadInputAvailable)
            return;

        if (!_virtualCursorInitialized)
            InitializeVirtualCursor();

        EventSystemGuard.SetMobileAimCursorVisible(false);
        _gamepadPointerActive = true;
        EventSystemGuard.SetGamepadMode(true);
        EventSystemGuard.NotifyGamepadCursorPosition(_virtualCursorScreenPosition);
    }

    private void DeactivateGamepadInput()
    {
        _gamepadPointerActive = false;
        EventSystemGuard.SetGamepadMode(false);
    }

    #region 手机输入语义与径向指向

    /// <summary>手机攻击只产生攻击语义，不再复用 LeftClick，避免同时触发世界交互或拆除。</summary>
    private void MobileAttackStartedAction(InputAction.CallbackContext context)
    {
        UpdateCurrentInputDevice(context);
        _mobileAttackActive = true;
        _mobileAttackDraggedOutsideDeadZone = false;
        UpdateMobileRadialCursor();

        if (IsGameplayInputLocked)
        {
            _suppressMobileAttackUntilRelease = true;
            return;
        }

        _suppressMobileAttackUntilRelease = false;
        BeginAttack();
    }

    /// <summary>无论方向是否拖出死区，手机攻击抬起都会可靠释放攻击状态。</summary>
    private void MobileAttackEndedAction(InputAction.CallbackContext context)
    {
        UpdateCurrentInputDevice(context);
        _mobileAttackActive = false;

        if (_suppressMobileAttackUntilRelease)
        {
            _suppressMobileAttackUntilRelease = false;
        }
        else
        {
            EndAttack();
        }

        if (!_mobileAimDirectionInitialized && _mobileAttackDraggedOutsideDeadZone)
        {
            _mobileAimDirection = _mobileAttackAimDirection;
            _mobileAimDirectionInitialized = true;
        }

        _mobileAttackDraggedOutsideDeadZone = false;
        UpdateMobileRadialCursor();
    }

    /// <summary>普通指向松手时保留最后有效方向，零向量只结束当前触控所有权。</summary>
    private void HandleMobileAim(InputAction.CallbackContext context)
    {
        UpdateCurrentInputDevice(context);
        Vector2 aim = context.ReadValue<Vector2>();
        if (aim.sqrMagnitude >= GamepadCursorDeadZone * GamepadCursorDeadZone)
        {
            _mobileAimDirection = aim.normalized;
            _mobileAimDirectionInitialized = true;
        }

        if (!_mobileAttackActive)
            UpdateMobileRadialCursor();
    }

    /// <summary>攻击摇杆拖出死区后覆盖普通方向；未拖出时仍沿普通最后方向立即攻击。</summary>
    private void HandleMobileAttackAim(InputAction.CallbackContext context)
    {
        UpdateCurrentInputDevice(context);
        Vector2 aim = context.ReadValue<Vector2>();
        if (aim.sqrMagnitude >= GamepadCursorDeadZone * GamepadCursorDeadZone)
        {
            _mobileAttackAimDirection = aim.normalized;
            _mobileAttackDraggedOutsideDeadZone = true;
        }

        if (_mobileAttackActive)
            UpdateMobileRadialCursor();
    }

    /// <summary>把手机的最终朝向映射到与手柄一致的径向虚拟光标。</summary>
    private void UpdateMobileRadialCursor()
    {
        if (_preferredInputDevice != InputDeviceType.Mobile || _gamepadPointerActive ||
            UIManager.ExistingInstance?.HasOpenGameplayInputBlockingPanel() == true)
        {
            EventSystemGuard.SetMobileAimCursorVisible(false);
            return;
        }

        Vector2 direction;
        bool hasDirection;
        if (_mobileAttackActive && _mobileAttackDraggedOutsideDeadZone)
        {
            direction = _mobileAttackAimDirection;
            hasDirection = true;
        }
        else
        {
            direction = _mobileAimDirection;
            hasDirection = _mobileAimDirectionInitialized;
        }

        if (!hasDirection)
        {
            EventSystemGuard.SetMobileAimCursorVisible(false);
            return;
        }

        _virtualCursorScreenPosition = CalculateGameplayRadialCursorScreenPosition(
            GetPlayerScreenPosition(),
            direction,
            GamepadCursorRadius,
            new Vector2(Screen.width, Screen.height),
            CursorClampPadding);
        _virtualCursorInitialized = true;
        EventSystemGuard.NotifyMobileAimCursorPosition(_virtualCursorScreenPosition);
    }

    private void BeginAttack()
    {
        if (_attackInputHeld)
            return;

        _attackInputHeld = true;
        AttackStarted?.Invoke();
    }

    private void EndAttack()
    {
        if (!_attackInputHeld)
            return;

        _attackInputHeld = false;
        AttackEnded?.Invoke();
    }

    /// <summary>由输入锁、暂停、失焦、禁用和销毁共同调用，杜绝移动或攻击卡住。</summary>
    public void CancelActiveAttackAndMobileInput()
    {
        MobileInputRuntime.ResetAll();
        DeactivateGamepadInput();
        EventSystemGuard.SetMobileAimCursorVisible(false);
        _mobileAttackActive = false;
        _mobileAttackDraggedOutsideDeadZone = false;
        _suppressMobileAttackUntilRelease = false;
        EndAttack();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            CancelActiveAttackAndMobileInput();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            CancelActiveAttackAndMobileInput();
    }

    #endregion

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
        EventSystemGuard.NotifyGamepadCursorPosition(_virtualCursorScreenPosition);
    }

    private void UpdateVirtualCursor(float deltaTime) /// 更新手柄虚拟光标
    {
        if (!IsGamepadInputAvailable ||
            (!_gamepadPointerActive && !EventSystemGuard.IsGamepadUISelectionActive))
        {
            return;
        }

        if (_inputActions == null)
        {
            return;
        }

        Vector2 look = _inputActions.Win10.GamepadCursor.ReadValue<Vector2>();
        float deadZoneSquared = GamepadCursorDeadZone * GamepadCursorDeadZone;
        if (!EventSystemGuard.HasOpenModalGamepadNavigationPanel)
        {
            UpdateGameplayRadialCursor(look, deadZoneSquared);
            return;
        }

        if (look.sqrMagnitude < deadZoneSquared)
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
        EventSystemGuard.NotifyGamepadCursorPosition(_virtualCursorScreenPosition);
    }

    /// <summary>按玩家屏幕位置和右摇杆方向更新游戏内径向准星。</summary>
    private void UpdateGameplayRadialCursor(Vector2 look, float deadZoneSquared)
    {
        if (look.sqrMagnitude >= deadZoneSquared)
        {
            _gamepadAimDirection = look.normalized;
            _gamepadAimDirectionInitialized = true;
        }

        // 未推动过右摇杆前不主动显示准星；一旦确定方向，玩家移动或按其他键时仍持续保持准星。
        if (!_gamepadAimDirectionInitialized)
        {
            return;
        }

        Vector2 playerScreenPosition = GetPlayerScreenPosition();
        _virtualCursorScreenPosition = CalculateGameplayRadialCursorScreenPosition(
            playerScreenPosition,
            _gamepadAimDirection,
            GamepadCursorRadius,
            new Vector2(Screen.width, Screen.height),
            CursorClampPadding);
        _virtualCursorInitialized = true;
        EventSystemGuard.NotifyGamepadCursorPosition(_virtualCursorScreenPosition);
    }

    /// <summary>获取玩家在当前相机下的屏幕中心位置。</summary>
    private Vector2 GetPlayerScreenPosition()
    {
        if (_mainCamera != null)
        {
            Vector3 screenPosition = _mainCamera.WorldToScreenPoint(transform.position);
            if (screenPosition.z >= 0f)
                return new Vector2(screenPosition.x, screenPosition.y);
        }

        return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    }

    /// <summary>计算固定半径准星位置，并限制在屏幕安全范围内。</summary>
    private static Vector2 CalculateGameplayRadialCursorScreenPosition(
        Vector2 playerScreenPosition,
        Vector2 aimDirection,
        float radius,
        Vector2 screenSize,
        float padding)
    {
        Vector2 direction = aimDirection.sqrMagnitude > 0.0001f
            ? aimDirection.normalized
            : Vector2.right;
        Vector2 cursorPosition = playerScreenPosition + direction * Mathf.Max(1f, radius);

        float minX = Mathf.Min(Mathf.Max(0f, padding), screenSize.x * 0.5f);
        float maxX = Mathf.Max(minX, screenSize.x - padding);
        float minY = Mathf.Min(Mathf.Max(0f, padding), screenSize.y * 0.5f);
        float maxY = Mathf.Max(minY, screenSize.y - padding);
        cursorPosition.x = Mathf.Clamp(cursorPosition.x, minX, maxX);
        cursorPosition.y = Mathf.Clamp(cursorPosition.y, minY, maxY);
        return cursorPosition;
    }

    /// <summary>
    /// 左摇杆用于 UI 焦点导航时退出虚拟光标模式。
    /// </summary>
    private void HandleMoveInputPerformed(InputAction.CallbackContext context)
    {
        UpdateCurrentInputDevice(context);
        if (context.control?.device is Gamepad)
            EventSystemGuard.NotifyGamepadFocusInput();
    }

    /// <summary>
    /// 重绑完成后刷新 UI 专用动作，使 UI 与玩家操作保持一致。
    /// </summary>
    private void HandleBindingsChanged()
    {
        ClearParallelInputBindingMasks();
        EventSystemGuard.SynchronizeUIInputBindings(_inputActions?.asset);
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
