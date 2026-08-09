using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

/// <summary>
/// 统一处理手柄 UI 的焦点模式与虚拟光标模式。
/// 左摇杆/十字键进入焦点导航，右摇杆进入虚拟光标；两种模式互斥，避免玩家不知道 A 键会作用于哪里。
/// </summary>
[DisallowMultipleComponent]
public sealed class GamepadUIRuntimeController : MonoBehaviour
{
    private const float DeviceDetectionStickDeadZone = 0.2f;

    #region 运行时状态

    private readonly List<RaycastResult> raycastResults = new List<RaycastResult>(16);

    private InputActionAsset inputAsset;
    private InputAction cancelAction;
    private InputAction submitAction;
    private RectTransform canvasRect;
    private Canvas canvas;
    private RectTransform cursorRect;
    private GamepadCursorGraphic cursorGraphic;
    private PointerEventData pointerEventData;
    private EventSystem pointerEventSystem;
    private GameObject hoveredObject;
    private TMP_InputField suppressedInputField;
    private int suppressedInputFieldFrame = -1;
    private Vector2 cursorScreenPosition;
    private bool cursorPositionInitialized;
    private bool gamepadMode;
    private bool cursorMode;

    public bool IsGamepadMode => gamepadMode;
    public bool IsVirtualCursorMode => cursorMode;

    #endregion

    #region Unity生命周期

    private void OnEnable()
    {
        GamepadVirtualKeyboardController.Closed += OnVirtualKeyboardClosed;
    }

    private void OnDisable()
    {
        GamepadVirtualKeyboardController.Closed -= OnVirtualKeyboardClosed;
        UnbindCancelAction();
        submitAction = null;
        suppressedInputField = null;
        suppressedInputFieldFrame = -1;
        ClearHoverTarget();
        SetCursorVisible(false);
    }

    private void Update()
    {
        DetectInputModeWithoutPlayerController();

        if (!gamepadMode)
            return;

        EnsureCursorVisual();
        TryOpenKeyboardForSelectedInputFieldOnSubmit();

        if (!cursorMode)
            return;

        PositionCursor();
        UpdateHoverTarget();
    }

    #endregion

    #region 菜单输入源检测

    /// <summary>
    /// 主菜单没有玩家控制器时，直接监听真实输入设备，保证焦点框仍会随键鼠和手柄切换。
    /// </summary>
    private void DetectInputModeWithoutPlayerController()
    {
        if (inputAsset != null)
            return;

        if (WasKeyboardOrMouseUsedThisFrame())
        {
            EventSystemGuard.SetGamepadMode(false);
            return;
        }

        if (WasGamepadUsedThisFrame())
            EventSystemGuard.SetGamepadMode(true);
    }

    /// <summary>检测键鼠按键、移动和滚轮输入。</summary>
    private static bool WasKeyboardOrMouseUsedThisFrame()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            return true;

        Mouse mouse = Mouse.current;
        return mouse != null &&
               (mouse.leftButton.wasPressedThisFrame ||
                mouse.rightButton.wasPressedThisFrame ||
                mouse.middleButton.wasPressedThisFrame ||
                mouse.delta.ReadValue().sqrMagnitude > 0.01f ||
                mouse.scroll.ReadValue().sqrMagnitude > 0.01f);
    }

    /// <summary>检测常用手柄按键、十字键和双摇杆输入。</summary>
    private static bool WasGamepadUsedThisFrame()
    {
        Gamepad gamepad = Gamepad.current;
        if (gamepad == null)
            return false;

        float deadZoneSquared = DeviceDetectionStickDeadZone * DeviceDetectionStickDeadZone;
        if (gamepad.leftStick.ReadValue().sqrMagnitude >= deadZoneSquared ||
            gamepad.rightStick.ReadValue().sqrMagnitude >= deadZoneSquared)
        {
            return true;
        }

        return gamepad.buttonSouth.wasPressedThisFrame ||
               gamepad.buttonEast.wasPressedThisFrame ||
               gamepad.buttonWest.wasPressedThisFrame ||
               gamepad.buttonNorth.wasPressedThisFrame ||
               gamepad.startButton.wasPressedThisFrame ||
               gamepad.selectButton.wasPressedThisFrame ||
               gamepad.leftShoulder.wasPressedThisFrame ||
               gamepad.rightShoulder.wasPressedThisFrame ||
               gamepad.leftTrigger.wasPressedThisFrame ||
               gamepad.rightTrigger.wasPressedThisFrame ||
               gamepad.leftStickButton.wasPressedThisFrame ||
               gamepad.rightStickButton.wasPressedThisFrame ||
               gamepad.dpad.up.wasPressedThisFrame ||
               gamepad.dpad.down.wasPressedThisFrame ||
               gamepad.dpad.left.wasPressedThisFrame ||
               gamepad.dpad.right.wasPressedThisFrame;
    }

    #endregion

    #region 外部控制

    /// <summary>
    /// 将玩家输入资产接入 UI，并解析虚拟光标动作。
    /// </summary>
    public void Configure(InputActionAsset asset)
    {
        inputAsset = asset;
        BindCancelAction(asset);
        BindSubmitAction(asset);
        EnsureCursorVisual();
    }

    /// <summary>
    /// 切换当前输入设备模式。
    /// </summary>
    public void SetGamepadMode(bool enabled)
    {
        gamepadMode = enabled;
        if (!enabled)
        {
            suppressedInputField = null;
            suppressedInputFieldFrame = -1;
            ExitCursorMode(false);
        }
    }

    /// <summary>
    /// 左摇杆或十字键输入进入焦点导航模式。
    /// </summary>
    public void NotifyFocusInput()
    {
        if (!gamepadMode)
            return;

        // 纯游戏场景和常驻 HUD 下左摇杆只负责移动，不能抢走右摇杆准星；打开模态面板后才切回 UI 焦点。
        if (cursorMode)
        {
            UIManager manager = UIManager.ExistingInstance;
            if (manager == null || !manager.HasOpenModalGamepadNavigationPanel())
                return;
        }

        ExitCursorMode(true);
        ClearSuppressedInputFieldIfSelectionChanged();
    }

    /// <summary>
    /// 接收 GameController 已经计算好的屏幕光标位置。
    /// </summary>
    public void NotifyCursorPosition(Vector2 screenPosition)
    {
        if (!gamepadMode)
            return;

        cursorScreenPosition = screenPosition;
        cursorPositionInitialized = true;
        EnterCursorMode();
    }

    /// <summary>
    /// 当前手柄虚拟光标是否可以执行一次 UI 点击。
    /// </summary>
    public bool TryClickVirtualCursor()
    {
        if (!gamepadMode || !cursorMode)
            return false;

        UpdateHoverTarget();
        if (hoveredObject == null)
            return false;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return false;

        // 槽位的手柄确认必须走独立事件，不能伪装成键鼠 PointerDown，避免两套交换状态互相覆盖。
        if (TryHandleGamepadPrimaryAction(hoveredObject))
            return true;

        PointerEventData data = GetPointerEventData(eventSystem);
        data.button = PointerEventData.InputButton.Left;
        data.clickCount = 1;
        data.clickTime = Time.unscaledTime;

        // 同时发送按下、抬起和点击，兼容 Button 与旧式 ItemSlot_UI 的 PointerDown 业务。
        ExecuteEvents.Execute(hoveredObject, data, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(hoveredObject, data, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(hoveredObject, data, ExecuteEvents.pointerClickHandler);

        TMP_InputField inputField = hoveredObject.GetComponentInParent<TMP_InputField>();
        if (inputField != null)
        {
            ExitCursorMode(false);
            eventSystem.SetSelectedGameObject(inputField.gameObject);
        }

        return true;
    }

    /// <summary>
    /// 获取当前手柄次要操作的目标对象。
    /// </summary>
    public GameObject GetInteractionTarget()
    {
        return cursorMode ? hoveredObject : EventSystem.current?.currentSelectedGameObject;
    }

    /// <summary>
    /// 向当前虚拟光标目标发送手柄主要操作；普通 Button 继续使用原有 Pointer 事件。
    /// </summary>
    private static bool TryHandleGamepadPrimaryAction(GameObject target)
    {
        if (target == null)
            return false;

        MonoBehaviour[] behaviours = target.GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IGamepadPrimaryActionHandler handler &&
                handler.HandleGamepadPrimaryAction())
            {
                return true;
            }
        }

        return false;
    }

    #endregion

    #region 全局返回

    private void BindCancelAction(InputActionAsset asset)
    {
        InputAction nextAction = asset?.FindActionMap("FlatWorldUI", false)?.FindAction("Cancel", false);
        if (ReferenceEquals(cancelAction, nextAction))
            return;

        UnbindCancelAction();
        cancelAction = nextAction;
        if (cancelAction != null)
            cancelAction.performed += OnCancelPerformed;
    }

    private void UnbindCancelAction()
    {
        if (cancelAction != null)
            cancelAction.performed -= OnCancelPerformed;
        cancelAction = null;
    }

    /// <summary>接入跟随玩家重绑结果的 UI 确认动作。</summary>
    private void BindSubmitAction(InputActionAsset asset)
    {
        submitAction = asset?.FindActionMap("FlatWorldUI", false)?.FindAction("Submit", false);
    }

    private void OnCancelPerformed(InputAction.CallbackContext context)
    {
        // 手柄 B 现在只存在于 FlatWorldUI/Cancel 的默认回退绑定，首次按下时也要切换到手柄模式。
        if (context.control?.device is Gamepad && !gamepadMode)
            EventSystemGuard.SetGamepadMode(true);

        if (!gamepadMode)
            return;

        if (GamepadVirtualKeyboardController.IsOpen)
        {
            GamepadVirtualKeyboardController.Cancel();
            return;
        }

        UIManager manager = FindObjectOfType<UIManager>();
        if (manager == null || manager.WasCancelHandledThisFrame())
            return;

        manager.TryCloseTopmostCancelPanel();
    }

    #endregion

    #region 输入框与焦点

    /// <summary>
    /// 只有手柄确认当前 TMP 输入框时，才打开游戏内虚拟键盘。
    /// </summary>
    private void TryOpenKeyboardForSelectedInputFieldOnSubmit()
    {
        if (!WasGamepadSubmitPressedThisFrame())
            return;

        if (cursorMode || GamepadVirtualKeyboardController.IsOpen)
            return;

        EventSystem eventSystem = EventSystem.current;
        GameObject selectedObject = eventSystem?.currentSelectedGameObject;
        TMP_InputField inputField = selectedObject != null
            ? selectedObject.GetComponentInParent<TMP_InputField>()
            : null;
        if (inputField == null || !inputField.interactable || !inputField.isActiveAndEnabled)
            return;

        ClearSuppressedInputFieldIfSelectionChanged();
        if (inputField == suppressedInputField)
        {
            if (Time.frameCount == suppressedInputFieldFrame)
                return;

            suppressedInputField = null;
            suppressedInputFieldFrame = -1;
        }

        GamepadVirtualKeyboardController.Show(inputField);
    }

    /// <summary>记录虚拟键盘关闭当帧，防止关闭键的同一次确认重新打开输入框。</summary>
    private void OnVirtualKeyboardClosed(TMP_InputField inputField)
    {
        suppressedInputField = inputField;
        suppressedInputFieldFrame = Time.frameCount;
    }

    /// <summary>焦点离开原输入框后解除关闭保护。</summary>
    private void ClearSuppressedInputFieldIfSelectionChanged()
    {
        if (suppressedInputField == null)
            return;

        GameObject selectedObject = EventSystem.current?.currentSelectedGameObject;
        if (selectedObject == null ||
            selectedObject.GetComponentInParent<TMP_InputField>() != suppressedInputField)
        {
            suppressedInputField = null;
            suppressedInputFieldFrame = -1;
        }
    }

    /// <summary>判断本帧是否由手柄提交，避免键盘 Enter 误打开虚拟键盘。</summary>
    private bool WasGamepadSubmitPressedThisFrame()
    {
        if (submitAction != null)
        {
            for (int i = 0; i < submitAction.controls.Count; i++)
            {
                InputControl control = submitAction.controls[i];
                if (control.device is Gamepad &&
                    control is ButtonControl button &&
                    button.wasPressedThisFrame)
                {
                    return true;
                }
            }

            return false;
        }

        // 主菜单没有玩家输入资产，使用系统默认的 A/确认键作为兜底。
        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.buttonSouth.wasPressedThisFrame;
    }

    #endregion

    #region 虚拟光标

    private void EnterCursorMode()
    {
        if (cursorMode)
            return;

        cursorMode = true;
        ClearHoverTarget();
        EventSystem.current?.SetSelectedGameObject(null);
        SetCursorVisible(true);
    }

    private void ExitCursorMode(bool restoreFocus)
    {
        if (!cursorMode)
        {
            SetCursorVisible(false);
            return;
        }

        cursorMode = false;
        ClearHoverTarget();
        SetCursorVisible(false);

        if (restoreFocus && EventSystem.current?.currentSelectedGameObject == null)
        {
            UIManager manager = FindObjectOfType<UIManager>();
            manager?.SelectTopmostGamepadPanel();
        }
    }

    private void EnsureCursorVisual()
    {
        Canvas targetCanvas = FindTargetCanvas();
        if (targetCanvas == null)
            return;

        if (canvas == targetCanvas && cursorRect != null)
            return;

        if (cursorRect != null)
            Destroy(cursorRect.gameObject);

        canvas = targetCanvas;
        canvasRect = canvas.GetComponent<RectTransform>();
        GameObject cursorObject = new GameObject(
            "GamepadVirtualCursor",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(GamepadCursorGraphic));
        cursorObject.transform.SetParent(canvas.transform, false);
        cursorRect = cursorObject.GetComponent<RectTransform>();
        cursorRect.anchorMin = new Vector2(0.5f, 0.5f);
        cursorRect.anchorMax = new Vector2(0.5f, 0.5f);
        cursorRect.pivot = new Vector2(0.5f, 0.5f);
        cursorRect.sizeDelta = new Vector2(28f, 28f);
        cursorGraphic = cursorObject.GetComponent<GamepadCursorGraphic>();
        cursorGraphic.color = FlatWorldUITheme.SelectionOutline;
        cursorGraphic.raycastTarget = false;
        cursorObject.transform.SetAsLastSibling();
        SetCursorVisible(cursorMode);
    }

    private Canvas FindTargetCanvas()
    {
        GameObject panelRoot = GameObject.Find("PanelRoot");
        Canvas panelCanvas = panelRoot != null
            ? panelRoot.GetComponentInChildren<Canvas>(true)
            : null;
        if (panelCanvas != null)
            return panelCanvas;

        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null && canvases[i].isRootCanvas)
                return canvases[i];
        }

        return null;
    }

    private void PositionCursor()
    {
        if (cursorRect == null || canvasRect == null || !cursorPositionInitialized)
            return;

        Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                cursorScreenPosition,
                eventCamera,
                out Vector2 localPosition))
        {
            cursorRect.anchoredPosition = localPosition;
        }
    }

    private void SetCursorVisible(bool visible)
    {
        if (cursorRect != null)
            cursorRect.gameObject.SetActive(visible);
    }

    private void UpdateHoverTarget()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return;

        PointerEventData data = GetPointerEventData(eventSystem);
        raycastResults.Clear();
        eventSystem.RaycastAll(data, raycastResults);

        GameObject nextObject = null;
        for (int i = 0; i < raycastResults.Count; i++)
        {
            GameObject candidate = raycastResults[i].gameObject;
            if (candidate == null || candidate == cursorRect?.gameObject)
                continue;

            nextObject = ExecuteEvents.GetEventHandler<IPointerEnterHandler>(candidate);
            if (nextObject == null)
                nextObject = candidate.GetComponentInParent<Selectable>()?.gameObject;
            if (nextObject != null)
                break;
        }

        if (nextObject == hoveredObject)
            return;

        if (hoveredObject != null)
            ExecuteEvents.Execute(hoveredObject, data, ExecuteEvents.pointerExitHandler);

        hoveredObject = nextObject;
        if (hoveredObject != null)
            ExecuteEvents.Execute(hoveredObject, data, ExecuteEvents.pointerEnterHandler);
    }

    private void ClearHoverTarget()
    {
        if (hoveredObject == null)
            return;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null)
        {
            PointerEventData data = GetPointerEventData(eventSystem);
            ExecuteEvents.Execute(hoveredObject, data, ExecuteEvents.pointerExitHandler);
        }

        hoveredObject = null;
    }

    private PointerEventData GetPointerEventData(EventSystem eventSystem)
    {
        if (pointerEventData == null || pointerEventSystem != eventSystem)
        {
            pointerEventData = new PointerEventData(eventSystem);
            pointerEventSystem = eventSystem;
        }

        pointerEventData.position = cursorScreenPosition;
        pointerEventData.delta = Vector2.zero;
        pointerEventData.button = PointerEventData.InputButton.Left;
        return pointerEventData;
    }

    #endregion
}

/// <summary>
/// 无需额外贴图的十字准星图形，作为手柄虚拟光标的可视化标记。
/// </summary>
public sealed class GamepadCursorGraphic : Graphic
{
    private const float OuterRadius = 12f;
    private const float InnerRadius = 7f;
    private const float ArmWidth = 2f;

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        AddQuad(vertexHelper, new Vector2(-OuterRadius, -ArmWidth), new Vector2(-InnerRadius, ArmWidth));
        AddQuad(vertexHelper, new Vector2(InnerRadius, -ArmWidth), new Vector2(OuterRadius, ArmWidth));
        AddQuad(vertexHelper, new Vector2(-ArmWidth, -OuterRadius), new Vector2(ArmWidth, -InnerRadius));
        AddQuad(vertexHelper, new Vector2(-ArmWidth, InnerRadius), new Vector2(ArmWidth, OuterRadius));
    }

    private void AddQuad(VertexHelper vertexHelper, Vector2 min, Vector2 max)
    {
        int index = vertexHelper.currentVertCount;
        Color vertexColor = color;
        vertexHelper.AddVert(new Vector3(min.x, min.y), vertexColor, Vector2.zero);
        vertexHelper.AddVert(new Vector3(min.x, max.y), vertexColor, Vector2.zero);
        vertexHelper.AddVert(new Vector3(max.x, max.y), vertexColor, Vector2.zero);
        vertexHelper.AddVert(new Vector3(max.x, min.y), vertexColor, Vector2.zero);
        vertexHelper.AddTriangle(index, index + 1, index + 2);
        vertexHelper.AddTriangle(index + 2, index + 3, index);
    }
}
