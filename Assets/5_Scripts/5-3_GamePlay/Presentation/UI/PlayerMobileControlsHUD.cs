using System.Collections;
using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Mobile;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

/// <summary>
/// 为本地玩家实例化正式手机控制 Prefab，并把节点契约连接到独立虚拟设备、快捷栏、丢弃和相机接口。
/// HUD 仅在本地玩家手动选择 Mobile 控制方式时启用；正式模态面板、输入锁、暂停和失焦会立即隐藏玩法控件并清空全部触摸状态。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
public sealed class PlayerMobileControlsHUD : MonoBehaviour
{
    #region 节点契约与状态

    private const string MoveZoneName = "移动摇杆";
    private const string AimZoneName = "普通指向区";
    private const string AimCursorName = "手机准线";
    private const string AttackZoneName = "攻击摇杆";
    private const string DrawerName = "菜单抽屉";
    private const string GameplayLayerName = "玩法控制层";
    private const string PersistentLayerName = "常驻控制层";
    private const float MoveZoneMarginX = 76f;
    private const float MoveZoneMarginY = 54f;
    private const float FixedMoveZoneSize = 230f;
    private const float PinchZoomSensitivity = 0.02f;
    private const float PinchCenterMinX = 0.25f;
    private const float PinchCenterMaxX = 0.75f;
    private const float PinchCenterMinY = 0.18f;
    private const float PinchCenterMaxY = 0.82f;

    private static readonly Color RunOffColor = new(0.094f, 0.212f, 0.247f, 0.99f);
    private static readonly Color RunOnColor = new(0.26f, 0.61f, 0.57f, 1f);
    private static readonly Color RunOffBorderColor = new(0.55f, 0.68f, 0.70f, 0.28f);
    private static readonly Color RunOnBorderColor = new(0.83f, 0.49f, 0.23f, 1f);

    private static PlayerMobileControlsHUD activeLocalHud;

    private Player player;
    private GameController controller;
    private GameObject viewObject;
    private GameObject gameplayLayer;
    private GameObject persistentLayer;
    private GameObject drawer;
    private RectTransform mobileAimCursor;
    private MobileVirtualJoystick[] joysticks;
    private MobileInputButton[] inputButtons;
    private Canvas hotbarCanvas;
    private Mover mover;
    private Image runButtonImage;
    private Outline runButtonOutline;
    private Image runStateIndicator;
    private Slider cameraZoomSlider;
    private TMP_Text interactionButtonText;
    private EnvironmentInteractionRunner environmentInteractionRunner;
    private readonly List<RaycastResult> pinchRaycastResults = new List<RaycastResult>(8);
    private Coroutine hotbarSetupCoroutine;
    private bool hotbarConfigured;
    private bool geometryInitialized;
    private Vector2 lastSafeAreaSize;
    private int lastScreenWidth;
    private int lastScreenHeight;
    private bool missingPrefabLogged;
    private bool runStateBound;
    private bool pinchActive;
    private float lastPinchDistance;
    private bool hotbarCanvasSortingCached;
    private bool hotbarCanvasOriginalOverrideSorting;
    private int hotbarCanvasOriginalSortingOrder;
    private bool changingViewState;
    private bool hotbarOriginalLayoutCached;
    private RectTransform hotbarOriginalRect;
    private Transform hotbarOriginalParent;
    private int hotbarOriginalSiblingIndex;
    private Vector2 hotbarOriginalAnchorMin;
    private Vector2 hotbarOriginalAnchorMax;
    private Vector2 hotbarOriginalPivot;
    private Vector2 hotbarOriginalAnchoredPosition;
    private Vector2 hotbarOriginalSizeDelta;
    private Vector3 hotbarOriginalLocalScale;

    public bool IsDrawerOpen => drawer != null && drawer.activeSelf;
    /// <summary>本地手机菜单抽屉是否打开，用于允许背包和制作面板并行切换。</summary>
    public static bool IsActiveDrawerOpen => activeLocalHud != null && activeLocalHud.IsDrawerOpen;
    public bool IsViewReady => viewObject != null && gameplayLayer != null &&
                                persistentLayer != null && drawer != null;

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        player = GetComponent<Player>();
        controller = GetComponentInChildren<GameController>(true);
        mover = GetComponentInChildren<Mover>(true);
    }

    private void OnEnable()
    {
        if (player != null)
            player.ProfileContextChanged += RefreshAvailability;
        if (controller != null)
            controller.ActiveInputDeviceChanged += HandleInputDeviceChanged;
        UIUserSettings.MobileControlsChanged -= HandleMobileControlsSettingsChanged;
        UIUserSettings.MobileControlsChanged += HandleMobileControlsSettingsChanged;
        RefreshAvailability();
    }

    private void OnDisable()
    {
        if (player != null)
            player.ProfileContextChanged -= RefreshAvailability;
        if (controller != null)
            controller.ActiveInputDeviceChanged -= HandleInputDeviceChanged;
        UIUserSettings.MobileControlsChanged -= HandleMobileControlsSettingsChanged;
        UnbindRunStateVisual();
        UnsubscribeInteractionSurface();
        UnbindEnvironmentInteractionLabel();
        if (activeLocalHud == this)
            activeLocalHud = null;
        hotbarSetupCoroutine = null;
        ResetAllTouchState();
        SetViewActive(false);
    }

    private void OnDestroy()
    {
        UnsubscribeInteractionSurface();
        UnbindEnvironmentInteractionLabel();
        if (viewObject != null)
            Destroy(viewObject);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            ResetAllTouchState();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            ResetAllTouchState();
    }

    #endregion

    #region 创建与可见性

    private bool ShouldShow()
    {
        if (player == null || !player.IsLocalProfile)
            return false;

        return controller != null &&
               controller.PreferredInputDevice == GameController.InputDeviceType.Mobile;
    }

    /// <summary>设置切换控制方式后立即刷新触屏 HUD，并可靠释放旧触控状态。</summary>
    private void HandleInputDeviceChanged(GameController.InputDeviceType deviceType)
    {
        RefreshAvailability();
    }

    private void RefreshAvailability()
    {
        if (!ShouldShow())
        {
            if (activeLocalHud == this)
                activeLocalHud = null;
            UnsubscribeInteractionSurface();
            ResetAllTouchState();
            SetViewActive(false);
            return;
        }

        activeLocalHud = this;
        EnsureView();
        BindRunStateVisual();
        SubscribeInteractionSurface();
        BindEnvironmentInteractionLabel();
        RefreshInteractionSurface();
    }

    private void EnsureView()
    {
        if (viewObject != null)
            return;

        GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.MobileControls, false);
        if (prefab == null)
        {
            if (!missingPrefabLogged)
            {
                missingPrefabLogged = true;
                Debug.LogError("[MobileHUD] 缺少正式 UI_MobileControls Prefab，请运行手机 UI Builder。", this);
            }
            return;
        }

        RectTransform safeRoot = UIManager.Instance.SafeAreaRoot;
        viewObject = Instantiate(prefab, safeRoot, false);
        viewObject.name = RuntimeUIPrefabKeys.MobileControls;
        FlatWorldUIAutoLocalizer.BindStaticTexts(viewObject.transform);
        gameplayLayer = FindRequired(GameplayLayerName)?.gameObject;
        persistentLayer = FindRequired(PersistentLayerName)?.gameObject;
        drawer = FindRequired(DrawerName)?.gameObject;
        EnsureAimCursorVisual();
        if (drawer != null)
            drawer.SetActive(false);

        ConfigureJoysticks();
        ConfigureVirtualButtons();
        CacheRunButtonVisual();
        ConfigureCameraZoomSlider();
        ConfigureCommands();
        hotbarSetupCoroutine = StartCoroutine(ConfigureHotbarWhenReady());
    }

    private Transform FindRequired(string nodeName)
    {
        if (viewObject == null)
            return null;

        Transform[] nodes = viewObject.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].name == nodeName)
                return nodes[i];
        }

        Debug.LogError($"[MobileHUD] Prefab 缺少节点：{nodeName}", viewObject);
        return null;
    }

    private void EnsureAimCursorVisual()
    {
        Transform existing = viewObject.transform.Find(AimCursorName);
        mobileAimCursor = existing as RectTransform;
        if (mobileAimCursor == null)
        {
            GameObject cursorObject = new GameObject(
                AimCursorName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(GamepadCursorGraphic));
            cursorObject.transform.SetParent(viewObject.transform, false);
            mobileAimCursor = cursorObject.GetComponent<RectTransform>();
            mobileAimCursor.anchorMin = new Vector2(0.5f, 0.5f);
            mobileAimCursor.anchorMax = new Vector2(0.5f, 0.5f);
            mobileAimCursor.pivot = new Vector2(0.5f, 0.5f);
            mobileAimCursor.sizeDelta = new Vector2(28f, 28f);
            cursorObject.transform.SetAsFirstSibling();
        }

        GamepadCursorGraphic cursorGraphic = mobileAimCursor.GetComponent<GamepadCursorGraphic>();
        if (cursorGraphic == null)
            cursorGraphic = mobileAimCursor.gameObject.AddComponent<GamepadCursorGraphic>();
        cursorGraphic.color = FlatWorldUITheme.SelectionOutline;
        cursorGraphic.raycastTarget = false;
        mobileAimCursor.gameObject.SetActive(false);
    }

    private void SetViewActive(bool active)
    {
        if (viewObject == null)
            return;

        // 快捷栏是桌面和手机共用的 UI，不能随着手机 HUD 一起被停用。
        if (!active)
            RestoreHotbarToOriginalParent();

        if (viewObject.activeSelf == active)
            return;

        // SetActive 会同步触发子 BasePanel.OnDisable；这些回调不能在父节点停用过程中
        // 再次调整快捷栏层级，否则 Unity 会拒绝 SetAsLastSibling。
        changingViewState = true;
        try
        {
            viewObject.SetActive(active);
        }
        finally
        {
            changingViewState = false;
        }
    }

    #endregion

    #region 手机准线

    private void LateUpdate()
    {
        UpdatePinchZoom();

        if (mobileAimCursor == null || controller == null || viewObject == null ||
            !ShouldShow() || !viewObject.activeInHierarchy ||
            gameplayLayer == null || !gameplayLayer.activeSelf)
        {
            return;
        }

        RectTransform rootRect = viewObject.transform as RectTransform;
        if (rootRect == null)
            return;

        Canvas canvas = rootRect.GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rootRect,
                controller.GetPointerScreenPosition(),
                eventCamera,
                out Vector2 localPosition))
        {
            mobileAimCursor.anchoredPosition = localPosition;
        }
    }

    /// <summary>在没有模态面板时读取两个独立触点，直接把间距变化交给相机模块。</summary>
    private void UpdatePinchZoom()
    {
        if (!ShouldShow() || viewObject == null || !viewObject.activeInHierarchy ||
            gameplayLayer == null || !gameplayLayer.activeSelf ||
            controller == null || controller.IsGameplayInputLocked ||
            !UIUserSettings.EnablePinchZoom)
        {
            pinchActive = false;
            return;
        }

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen == null)
        {
            pinchActive = false;
            return;
        }

        TouchControl first = null;
        TouchControl second = null;
        int allowedTouchCount = 0;
        for (int i = 0; i < touchscreen.touches.Count; i++)
        {
            TouchControl touch = touchscreen.touches[i];
            if (!touch.press.isPressed || !IsPinchTouchAllowed(touch))
                continue;

            allowedTouchCount++;
            if (first == null)
                first = touch;
            else if (second == null)
                second = touch;
        }

        if (allowedTouchCount != 2 || first == null || second == null)
        {
            pinchActive = false;
            return;
        }

        float distance = Vector2.Distance(first.position.ReadValue(), second.position.ReadValue());
        if (!pinchActive)
        {
            lastPinchDistance = distance;
            pinchActive = true;
            return;
        }

        float distanceDelta = distance - lastPinchDistance;
        lastPinchDistance = distance;
        Mod_Cam cameraModule = GetComponentInChildren<Mod_Cam>(true);
        cameraModule?.ApplyPinchZoom(distanceDelta, PinchZoomSensitivity);
    }

    /// <summary>只允许屏幕中部且未命中真实 UI 的触点参与双指缩放。</summary>
    private bool IsPinchTouchAllowed(TouchControl touch)
    {
        Vector2 position = touch.position.ReadValue();
        if (Screen.width <= 0 || Screen.height <= 0 ||
            position.x < Screen.width * PinchCenterMinX ||
            position.x > Screen.width * PinchCenterMaxX ||
            position.y < Screen.height * PinchCenterMinY ||
            position.y > Screen.height * PinchCenterMaxY)
        {
            return false;
        }

        if (EventSystem.current == null)
            return true;

        PointerEventData eventData = new PointerEventData(EventSystem.current)
        {
            position = position
        };
        pinchRaycastResults.Clear();
        EventSystem.current.RaycastAll(eventData, pinchRaycastResults);
        for (int i = 0; i < pinchRaycastResults.Count; i++)
        {
            GameObject hitObject = pinchRaycastResults[i].gameObject;
            MobileVirtualJoystick joystick = hitObject?.GetComponentInParent<MobileVirtualJoystick>();
            if (joystick != null && joystick.IsWorldDropSurface)
                continue;

            return false;
        }

        return true;
    }

    #endregion

    #region 控件绑定

    private void ConfigureJoysticks()
    {
        ApplyMoveJoystickMode();
        float deadZone = GetAimJoystickDeadZone();
        ConfigureJoystick(AimZoneName, MobileVirtualJoystick.JoystickRole.Aim, floating: true, deadZone: deadZone);
        ConfigureJoystick(AttackZoneName, MobileVirtualJoystick.JoystickRole.Attack, floating: false, deadZone: deadZone);
        joysticks = viewObject.GetComponentsInChildren<MobileVirtualJoystick>(true);
    }

    private float GetAimJoystickDeadZone()
    {
        return controller != null ? controller.AimDeadZone : PlayerAimCursorSystem.DefaultDeadZone;
    }

    private void ConfigureJoystick(
        string nodeName,
        MobileVirtualJoystick.JoystickRole role,
        bool floating,
        float deadZone)
    {
        Transform zone = FindRequired(nodeName);
        if (zone == null)
            return;

        RectTransform baseRect = zone.Find("底座") as RectTransform;
        RectTransform knobRect = baseRect != null ? baseRect.Find("摇杆") as RectTransform : null;
        MobileVirtualJoystick joystick = zone.GetComponent<MobileVirtualJoystick>();
        if (joystick == null)
            joystick = zone.gameObject.AddComponent<MobileVirtualJoystick>();
        joystick.Configure(role, baseRect, knobRect, 92f, floating, deadZone);
    }

    /// <summary>按玩家偏好在左半屏浮动区与左下角固定区之间切换，复用同一摇杆实例。</summary>
    private void ApplyMoveJoystickMode()
    {
        Transform zone = FindRequired(MoveZoneName);
        RectTransform zoneRect = zone as RectTransform;
        if (zoneRect == null)
            return;

        RectTransform baseRect = zone.Find("底座") as RectTransform;
        RectTransform knobRect = baseRect != null ? baseRect.Find("摇杆") as RectTransform : null;
        MobileVirtualJoystick joystick = zone.GetComponent<MobileVirtualJoystick>();
        if (joystick == null)
            joystick = zone.gameObject.AddComponent<MobileVirtualJoystick>();
        joystick.ResetOwnership();
        if (baseRect != null)
            baseRect.anchoredPosition = Vector2.zero;

        bool floating = UIUserSettings.FloatingMoveJoystick;
        if (floating)
        {
            zoneRect.anchorMin = Vector2.zero;
            zoneRect.anchorMax = new Vector2(0.5f, 1f);
            zoneRect.pivot = new Vector2(0.5f, 0.5f);
            zoneRect.offsetMin = Vector2.zero;
            zoneRect.offsetMax = Vector2.zero;
        }
        else
        {
            zoneRect.anchorMin = zoneRect.anchorMax = Vector2.zero;
            zoneRect.pivot = Vector2.zero;
            zoneRect.anchoredPosition = new Vector2(MoveZoneMarginX, MoveZoneMarginY);
            zoneRect.sizeDelta = new Vector2(FixedMoveZoneSize, FixedMoveZoneSize);
        }

        joystick.Configure(
            MobileVirtualJoystick.JoystickRole.Move,
            baseRect,
            knobRect,
            92f,
            floating,
            GetAimJoystickDeadZone());
    }

    private void HandleMobileControlsSettingsChanged()
    {
        ResetAllTouchState();
        if (viewObject == null)
            return;

        ApplyMoveJoystickMode();
        joysticks = viewObject.GetComponentsInChildren<MobileVirtualJoystick>(true);
    }

    private void ConfigureVirtualButtons()
    {
        ConfigureVirtualButton("交互", MobileVirtualButton.Interact);
        CacheInteractionButtonText();
        ConfigureVirtualButton("使用", MobileVirtualButton.Use);
        ConfigureVirtualButton("奔跑", MobileVirtualButton.Run);
        ConfigureVirtualButton("背包", MobileVirtualButton.Inventory);
        ConfigureVirtualButton("装备", MobileVirtualButton.Equipment);
        ConfigureVirtualButton("制作", MobileVirtualButton.Crafting);
        ConfigureVirtualButton("状态", MobileVirtualButton.Survival);
        inputButtons = viewObject.GetComponentsInChildren<MobileInputButton>(true);
    }

    /// <summary>缓存交互按钮文字节点，环境动作变化时只更新文本，不重建按钮。</summary>
    private void CacheInteractionButtonText()
    {
        Transform node = FindRequired("交互");
        interactionButtonText = node?.GetComponentInChildren<TMP_Text>(true);
    }

    private void ConfigureVirtualButton(string nodeName, MobileVirtualButton virtualButton)
    {
        Transform node = FindRequired(nodeName);
        if (node == null)
            return;

        MobileInputButton inputButton = node.GetComponent<MobileInputButton>();
        if (inputButton == null)
            inputButton = node.gameObject.AddComponent<MobileInputButton>();
        inputButton.Configure(virtualButton);
    }

    /// <summary>缓存奔跑开关的两态视觉节点，状态由真实 Mover 统一驱动。</summary>
    private void CacheRunButtonVisual()
    {
        Transform runButton = FindRequired("奔跑");
        runButtonImage = runButton != null ? runButton.GetComponent<Image>() : null;
        runButtonOutline = runButton != null ? runButton.GetComponent<Outline>() : null;
        Transform indicator = runButton != null ? runButton.Find("状态标记") : null;
        runStateIndicator = indicator != null ? indicator.GetComponent<Image>() : null;
    }

    private void BindRunStateVisual()
    {
        if (mover == null)
            mover = GetComponentInChildren<Mover>(true);
        if (mover == null || viewObject == null)
            return;

        if (!runStateBound)
        {
            mover.RunStateChanged += RefreshRunButtonVisual;
            runStateBound = true;
        }

        RefreshRunButtonVisual(mover.IsRunning);
    }

    private void UnbindRunStateVisual()
    {
        if (mover != null && runStateBound)
            mover.RunStateChanged -= RefreshRunButtonVisual;
        runStateBound = false;
    }

    /// <summary>开启时使用青绿底与琥珀指示点，关闭时恢复深色弱提示。</summary>
    private void RefreshRunButtonVisual(bool isRunning)
    {
        if (runButtonImage != null)
            runButtonImage.color = isRunning ? RunOnColor : RunOffColor;
        if (runButtonOutline != null)
            runButtonOutline.effectColor = isRunning ? RunOnBorderColor : RunOffBorderColor;
        if (runStateIndicator != null)
            runStateIndicator.color = isRunning ? RunOnBorderColor : RunOffBorderColor;
    }

    private void ConfigureCommands()
    {
        BindClick("菜单", ToggleDrawer);
        BindClick("关闭抽屉", CloseDrawer);
        BindClick("丢弃一个", DropOne);
        BindClick("设置", OpenSettingsFromButton);
    }

    /// <summary>绑定手机菜单中的镜头缩放滑动条，并使用当前相机视野初始化滑块位置。</summary>
    private void ConfigureCameraZoomSlider()
    {
        Transform node = FindRequired("镜头缩放");
        cameraZoomSlider = node != null ? node.GetComponent<Slider>() : null;
        if (cameraZoomSlider == null)
            return;

        cameraZoomSlider.onValueChanged.RemoveListener(HandleCameraZoomChanged);
        cameraZoomSlider.onValueChanged.AddListener(HandleCameraZoomChanged);
        RefreshCameraZoomSlider();
    }

    /// <summary>把滑动条范围和当前镜头正交尺寸同步，避免打开菜单时显示旧值。</summary>
    private void RefreshCameraZoomSlider()
    {
        if (cameraZoomSlider == null)
            return;

        Mod_Cam cameraModule = GetComponentInChildren<Mod_Cam>(true);
        if (cameraModule == null || cameraModule.Vcam == null)
            return;

        cameraZoomSlider.minValue = cameraModule.MinPovValue;
        cameraZoomSlider.maxValue = cameraModule.MaxPovValue;
        cameraZoomSlider.wholeNumbers = false;
        cameraZoomSlider.SetValueWithoutNotify(
            Mathf.Clamp(cameraModule.CurrentOrthographicSize, cameraModule.MinPovValue, cameraModule.MaxPovValue));
    }

    /// <summary>将手机滑动条的绝对值交给相机模块，保持区块流送范围同步刷新。</summary>
    private void HandleCameraZoomChanged(float value)
    {
        Mod_Cam cameraModule = GetComponentInChildren<Mod_Cam>(true);
        if (cameraModule == null || cameraModule.Vcam == null)
            return;

        cameraModule.SetOrthographicSize(value);
        RefreshCameraZoomSlider();
    }

    private void BindClick(string nodeName, UnityEngine.Events.UnityAction action)
    {
        Transform node = FindRequired(nodeName);
        Button button = node != null ? node.GetComponent<Button>() : null;
        if (button == null)
            return;
        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    /// <summary>把现有九格快捷栏移到安全区内，并限制为安全区宽度 44% 或 760 参考像素。</summary>
    private IEnumerator ConfigureHotbarWhenReady()
    {
        // 快捷栏面板由库存模块稍后创建；等待真实面板出现，避免 Awake 顺序导致手机布局永久漏接。
        while (isActiveAndEnabled && viewObject != null && viewObject.activeInHierarchy &&
               ShouldShow() && !hotbarConfigured)
        {
            hotbarConfigured = TryConfigureHotbarWidth();
            if (!hotbarConfigured)
                yield return null;
        }

        hotbarSetupCoroutine = null;
    }

    private bool TryConfigureHotbarWidth()
    {
        Transform hotbarAnchor = FindRequired("快捷栏锚点");
        Inventory_HotBar hotbar = GetComponentInChildren<Inventory_HotBar>(true);
        RectTransform hotbarRect = hotbar?.RuntimeInventory?.basePanel?.transform as RectTransform;
        if (hotbarAnchor == null || hotbarRect == null)
            return false;

        // UIManager 的面板通知可能在父节点 SetActive 的中途进入这里；此时任何
        // SetParent/SetAsLastSibling 都属于对正在停用层级的修改，必须跳过本次刷新。
        if (changingViewState || viewObject == null || !viewObject.activeInHierarchy ||
            !hotbarAnchor.gameObject.activeInHierarchy)
        {
            return false;
        }

        CacheHotbarOriginalLayout(hotbarRect, hotbarAnchor);

        if (hotbarRect.parent != hotbarAnchor)
            hotbarRect.SetParent(hotbarAnchor, false);
        hotbarRect.SetAsLastSibling();
        CacheHotbarCanvas(hotbarRect);
        float safeWidth = UIManager.Instance.SafeAreaRoot != null
            ? UIManager.Instance.SafeAreaRoot.rect.width
            : 1920f;
        float targetWidth = Mathf.Min(760f, safeWidth * 0.44f);
        float sourceWidth = Mathf.Max(1f, hotbarRect.rect.width);
        float scale = Mathf.Min(1f, targetWidth / sourceWidth);
        hotbarRect.localScale = Vector3.one * scale;
        hotbarRect.anchorMin = hotbarRect.anchorMax = new Vector2(0.5f, 0f);
        hotbarRect.pivot = new Vector2(0.5f, 0f);
        hotbarRect.anchoredPosition = Vector2.zero;
        ApplyHotbarInteractionPriority(UIManager.Instance.HasOpenGameplayInputBlockingPanel());
        return true;
    }

    /// <summary>记录快捷栏在首次切入手机 HUD 前的桌面父节点和布局。</summary>
    private void CacheHotbarOriginalLayout(RectTransform hotbarRect, Transform hotbarAnchor)
    {
        if (hotbarOriginalLayoutCached && hotbarOriginalRect == hotbarRect)
            return;

        hotbarOriginalRect = hotbarRect;
        hotbarOriginalParent = hotbarRect.parent;
        if (hotbarOriginalParent == hotbarAnchor)
            hotbarOriginalParent = UIManager.ExistingInstance?.SafeAreaRoot;

        hotbarOriginalSiblingIndex = hotbarRect.GetSiblingIndex();
        hotbarOriginalAnchorMin = hotbarRect.anchorMin;
        hotbarOriginalAnchorMax = hotbarRect.anchorMax;
        hotbarOriginalPivot = hotbarRect.pivot;
        hotbarOriginalAnchoredPosition = hotbarRect.anchoredPosition;
        hotbarOriginalSizeDelta = hotbarRect.sizeDelta;
        hotbarOriginalLocalScale = hotbarRect.localScale;
        hotbarOriginalLayoutCached = hotbarOriginalParent != null;
    }

    /// <summary>切换到 PC 或销毁手机 HUD 前，将共用快捷栏还原到 SafeAreaRoot。</summary>
    private void RestoreHotbarToOriginalParent()
    {
        if (!hotbarOriginalLayoutCached || hotbarOriginalRect == null ||
            hotbarOriginalParent == null || !hotbarOriginalParent.gameObject.activeInHierarchy)
        {
            return;
        }

        if (hotbarOriginalRect.parent != hotbarOriginalParent)
            hotbarOriginalRect.SetParent(hotbarOriginalParent, false);

        int lastSiblingIndex = Mathf.Max(0, hotbarOriginalParent.childCount - 1);
        hotbarOriginalRect.SetSiblingIndex(Mathf.Clamp(hotbarOriginalSiblingIndex, 0, lastSiblingIndex));
        hotbarOriginalRect.anchorMin = hotbarOriginalAnchorMin;
        hotbarOriginalRect.anchorMax = hotbarOriginalAnchorMax;
        hotbarOriginalRect.pivot = hotbarOriginalPivot;
        hotbarOriginalRect.sizeDelta = hotbarOriginalSizeDelta;
        hotbarOriginalRect.anchoredPosition = hotbarOriginalAnchoredPosition;
        hotbarOriginalRect.localScale = hotbarOriginalLocalScale;
        ApplyHotbarInteractionPriority(false);
    }

    /// <summary>缓存快捷栏独立 Canvas 的原始排序，关闭容器后恢复桌面 HUD 层级。</summary>
    private void CacheHotbarCanvas(RectTransform hotbarRect)
    {
        Canvas nextCanvas = hotbarRect != null ? hotbarRect.GetComponent<Canvas>() : null;
        if (hotbarCanvas == nextCanvas && hotbarCanvasSortingCached)
            return;

        hotbarCanvas = nextCanvas;
        hotbarCanvasSortingCached = hotbarCanvas != null;
        if (!hotbarCanvasSortingCached)
            return;

        hotbarCanvasOriginalOverrideSorting = hotbarCanvas.overrideSorting;
        hotbarCanvasOriginalSortingOrder = hotbarCanvas.sortingOrder;
    }

    /// <summary>模态容器打开时让快捷栏 Canvas 参与最上层射线命中，关闭后还原原始排序。</summary>
    private void ApplyHotbarInteractionPriority(bool modalOpen)
    {
        if (!hotbarCanvasSortingCached || hotbarCanvas == null)
            return;

        if (modalOpen)
        {
            hotbarCanvas.overrideSorting = true;
            hotbarCanvas.sortingOrder = UIManager.HotbarModalSortingOrder;
            return;
        }

        hotbarCanvas.overrideSorting = hotbarCanvasOriginalOverrideSorting;
        hotbarCanvas.sortingOrder = hotbarCanvasOriginalSortingOrder;
    }

    #endregion

    #region 抽屉、返回与玩法接口

    public void ToggleDrawer()
    {
        if (drawer == null)
            return;

        UIManager manager = UIManager.ExistingInstance;
        // 菜单抽屉是背包、制作等玩法面板的并行入口，面板打开时仍允许展开。
        drawer.SetActive(!drawer.activeSelf);
        if (drawer.activeSelf)
            RefreshCameraZoomSlider();
        manager?.NotifyInteractionSurfaceChanged();
    }

    public void CloseDrawer()
    {
        if (drawer == null || !drawer.activeSelf)
            return;
        drawer.SetActive(false);
        UIManager.Instance.NotifyInteractionSurfaceChanged();
    }

    public static bool TryCloseActiveDrawer()
    {
        if (activeLocalHud == null || !activeLocalHud.IsDrawerOpen)
            return false;
        activeLocalHud.CloseDrawer();
        return true;
    }

    private void DropOne()
    {
        Module_DiscardItem dropper = GetComponentInChildren<Module_DiscardItem>(true);
        dropper?.TryDropCurrentSelection(1);
    }

    /// <summary>独立设置按钮先收起抽屉，再脉冲设置 Action，确保返回栈正确打开设置面板。</summary>
    private void OpenSettingsFromButton()
    {
        if (drawer != null)
            drawer.SetActive(false);
        StartCoroutine(PulseVirtualButton(MobileVirtualButton.Settings));
    }

    private static IEnumerator PulseVirtualButton(MobileVirtualButton button)
    {
        MobileInputRuntime.SetButton(button, true);
        yield return null;
        MobileInputRuntime.SetButton(button, false);
    }

    #endregion

    #region 交互面与清理

    private void SubscribeInteractionSurface()
    {
        UIManager manager = UIManager.Instance;
        manager.InteractionSurfaceChanged -= RefreshInteractionSurface;
        manager.InteractionSurfaceChanged += RefreshInteractionSurface;
    }

    private void UnsubscribeInteractionSurface()
    {
        UIManager manager = UIManager.ExistingInstance;
        if (manager != null)
            manager.InteractionSurfaceChanged -= RefreshInteractionSurface;
    }

    /// <summary>把水体等环境动作运行器绑定到手机交互按钮的动态文字。</summary>
    private void BindEnvironmentInteractionLabel()
    {
        TileEffectReceiver receiver = player?.itemMods?.GetMod_ByID<TileEffectReceiver>(ModText.TileEffectReceiver) ??
                                       player?.GetComponentInChildren<TileEffectReceiver>(true);
        EnvironmentInteractionRunner nextRunner = receiver?.EnvironmentInteractions;
        if (environmentInteractionRunner == nextRunner)
        {
            RefreshInteractionButtonLabel();
            return;
        }

        UnbindEnvironmentInteractionLabel();
        environmentInteractionRunner = nextRunner;
        if (environmentInteractionRunner != null)
            environmentInteractionRunner.AvailableActionsChanged += RefreshInteractionButtonLabel;
        RefreshInteractionButtonLabel();
    }

    /// <summary>解除环境动作运行器事件，避免玩家对象销毁后继续回调 HUD。</summary>
    private void UnbindEnvironmentInteractionLabel()
    {
        if (environmentInteractionRunner != null)
            environmentInteractionRunner.AvailableActionsChanged -= RefreshInteractionButtonLabel;
        environmentInteractionRunner = null;
    }

    /// <summary>根据当前首选环境动作显示“交互”或“喝水”等本地化名称。</summary>
    private void RefreshInteractionButtonLabel()
    {
        if (interactionButtonText == null)
            return;

        string displayNameKey = environmentInteractionRunner?.GetPreferredActionDisplayNameKey();
        interactionButtonText.text = FlatWorldLocalizationService.GetUiText(
            string.IsNullOrWhiteSpace(displayNameKey) ? "交互" : displayNameKey);
    }

    private void RefreshInteractionSurface()
    {
        if (viewObject == null || changingViewState || !isActiveAndEnabled)
            return;

        bool blocked = controller != null && controller.IsGameplayInputLocked;
        bool modalOpen = UIManager.Instance.HasOpenGameplayInputBlockingPanel();
        bool gameplayVisible = !blocked && !modalOpen;
        RectTransform safeRoot = UIManager.Instance.SafeAreaRoot;
        Vector2 safeSize = safeRoot != null ? safeRoot.rect.size : new Vector2(Screen.width, Screen.height);
        bool geometryChanged = geometryInitialized &&
                               (safeSize != lastSafeAreaSize ||
                                Screen.width != lastScreenWidth ||
                                Screen.height != lastScreenHeight);
        geometryInitialized = true;
        lastSafeAreaSize = safeSize;
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        // 菜单抽屉用于在背包与制作面板之间切换，不能因玩法面板获得输入锁而自动收起。
        if (gameplayLayer != null)
            gameplayLayer.SetActive(gameplayVisible);
        if (persistentLayer != null)
            persistentLayer.SetActive(ShouldShow());
        if (mobileAimCursor != null)
            mobileAimCursor.gameObject.SetActive(gameplayVisible);
        SetViewActive(ShouldShow());

        if (!ShouldShow() || viewObject == null || !viewObject.activeInHierarchy)
        {
            if (!gameplayVisible || geometryChanged)
                ResetAllTouchState();
            return;
        }

        if (viewObject.activeSelf)
        {
            // 正常游戏时让常驻 HUD 的真实按钮优先接收射线，右侧指向区只响应空白位置。
            // 抽屉展开后它本身成为交互层，必须覆盖任务追踪 HUD；其它模态面板打开后再把快捷栏提到最上层参与拖放。
            if (modalOpen || IsDrawerOpen)
                viewObject.transform.SetAsLastSibling();
            else
                viewObject.transform.SetAsFirstSibling();
        }

        ApplyHotbarInteractionPriority(modalOpen);

        if (hotbarConfigured)
            TryConfigureHotbarWidth();
        else if (hotbarSetupCoroutine == null && isActiveAndEnabled)
            hotbarSetupCoroutine = StartCoroutine(ConfigureHotbarWhenReady());

        if (!gameplayVisible || geometryChanged)
            ResetAllTouchState();
    }

    public void ResetAllTouchState()
    {
        pinchActive = false;
        lastPinchDistance = 0f;

        if (joysticks != null)
        {
            for (int i = 0; i < joysticks.Length; i++)
                joysticks[i]?.ResetOwnership();
        }
        if (inputButtons != null)
        {
            for (int i = 0; i < inputButtons.Length; i++)
                inputButtons[i]?.Release();
        }
        MobileInputRuntime.ResetAll();
        controller?.CancelActiveAttackAndMobileInput();
    }

    #endregion
}
