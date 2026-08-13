using System.Collections;
using FlatWorld.Mobile;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 为本地玩家实例化正式手机控制 Prefab，并把节点契约连接到独立虚拟设备、快捷栏、丢弃和相机接口。
/// HUD 在 Android 或显式编辑器模拟模式下启用；正式模态面板、输入锁、暂停和失焦会立即隐藏玩法控件并清空全部触摸状态。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
public sealed class PlayerMobileControlsHUD : MonoBehaviour
{
    #region 节点契约与状态

    private const string MoveZoneName = "移动摇杆";
    private const string AimZoneName = "普通指向区";
    private const string AttackZoneName = "攻击摇杆";
    private const string DrawerName = "菜单抽屉";
    private const string GameplayLayerName = "玩法控制层";

    [SerializeField, Tooltip("仅在 Unity 编辑器中显式显示手机 HUD，便于横屏和安全区验收。")]
    private bool enableEditorSimulation;

    private static PlayerMobileControlsHUD activeLocalHud;

    private Player player;
    private GameController controller;
    private GameObject viewObject;
    private GameObject gameplayLayer;
    private GameObject drawer;
    private MobileVirtualJoystick[] joysticks;
    private MobileInputButton[] inputButtons;
    private Coroutine hotbarSetupCoroutine;
    private bool hotbarConfigured;
    private bool geometryInitialized;
    private Vector2 lastSafeAreaSize;
    private int lastScreenWidth;
    private int lastScreenHeight;
    private bool missingPrefabLogged;

    public bool IsDrawerOpen => drawer != null && drawer.activeSelf;
    public bool IsViewReady => viewObject != null && gameplayLayer != null && drawer != null;

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        player = GetComponent<Player>();
        controller = GetComponentInChildren<GameController>(true);
    }

    private void OnEnable()
    {
        if (player != null)
            player.ProfileContextChanged += RefreshAvailability;
        RefreshAvailability();
    }

    private void OnDisable()
    {
        if (player != null)
            player.ProfileContextChanged -= RefreshAvailability;
        UnsubscribeInteractionSurface();
        if (activeLocalHud == this)
            activeLocalHud = null;
        hotbarSetupCoroutine = null;
        ResetAllTouchState();
        SetViewActive(false);
    }

    private void OnDestroy()
    {
        UnsubscribeInteractionSurface();
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

#if UNITY_EDITOR
        return Application.isMobilePlatform || enableEditorSimulation;
#else
        return Application.isMobilePlatform;
#endif
    }

    private void RefreshAvailability()
    {
        if (!ShouldShow())
        {
            if (activeLocalHud == this)
                activeLocalHud = null;
            ResetAllTouchState();
            SetViewActive(false);
            return;
        }

        activeLocalHud = this;
        EnsureView();
        SubscribeInteractionSurface();
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
        drawer = FindRequired(DrawerName)?.gameObject;
        if (drawer != null)
            drawer.SetActive(false);

        ConfigureJoysticks();
        ConfigureVirtualButtons();
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

    private void SetViewActive(bool active)
    {
        if (viewObject != null)
            viewObject.SetActive(active);
    }

    #endregion

    #region 控件绑定

    private void ConfigureJoysticks()
    {
        ConfigureJoystick(MoveZoneName, MobileVirtualJoystick.JoystickRole.Move, floating: false);
        ConfigureJoystick(AimZoneName, MobileVirtualJoystick.JoystickRole.Aim, floating: true);
        ConfigureJoystick(AttackZoneName, MobileVirtualJoystick.JoystickRole.Attack, floating: false);
        joysticks = viewObject.GetComponentsInChildren<MobileVirtualJoystick>(true);
    }

    private void ConfigureJoystick(string nodeName, MobileVirtualJoystick.JoystickRole role, bool floating)
    {
        Transform zone = FindRequired(nodeName);
        if (zone == null)
            return;

        RectTransform baseRect = zone.Find("底座") as RectTransform;
        RectTransform knobRect = baseRect != null ? baseRect.Find("摇杆") as RectTransform : null;
        MobileVirtualJoystick joystick = zone.GetComponent<MobileVirtualJoystick>();
        if (joystick == null)
            joystick = zone.gameObject.AddComponent<MobileVirtualJoystick>();
        joystick.Configure(role, baseRect, knobRect, 92f, floating);
    }

    private void ConfigureVirtualButtons()
    {
        ConfigureVirtualButton("交互", MobileVirtualButton.Interact);
        ConfigureVirtualButton("使用", MobileVirtualButton.Use);
        ConfigureVirtualButton("奔跑", MobileVirtualButton.Run);
        ConfigureVirtualButton("背包", MobileVirtualButton.Inventory);
        ConfigureVirtualButton("装备", MobileVirtualButton.Equipment);
        ConfigureVirtualButton("制作", MobileVirtualButton.Crafting);
        ConfigureVirtualButton("状态", MobileVirtualButton.Survival);
        inputButtons = viewObject.GetComponentsInChildren<MobileInputButton>(true);
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

    private void ConfigureCommands()
    {
        BindClick("菜单", ToggleDrawer);
        BindClick("关闭抽屉", CloseDrawer);
        BindClick("丢弃一个", DropOne);
        BindClick("镜头+", () => ChangeCameraView(-1f));
        BindClick("镜头-", () => ChangeCameraView(1f));
        BindClick("设置", OpenSettingsFromDrawer);
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
        while (isActiveAndEnabled && viewObject != null && !hotbarConfigured)
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

        hotbarRect.SetParent(hotbarAnchor, false);
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
        return true;
    }

    #endregion

    #region 抽屉、返回与玩法接口

    public void ToggleDrawer()
    {
        if (drawer == null)
            return;
        drawer.SetActive(!drawer.activeSelf);
        UIManager.Instance.NotifyInteractionSurfaceChanged();
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

    private void ChangeCameraView(float delta)
    {
        Mod_Cam cameraModule = GetComponentInChildren<Mod_Cam>(true);
        cameraModule?.ChangeCameraView(delta);
    }

    /// <summary>抽屉设置按钮先显式收起抽屉，再脉冲设置 Action，确保返回栈真正打开设置面板。</summary>
    private void OpenSettingsFromDrawer()
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

    private void RefreshInteractionSurface()
    {
        if (viewObject == null)
            return;

        bool blocked = controller != null && controller.IsGameplayInputLocked;
        bool modalOpen = UIManager.Instance.HasOpenModalGamepadNavigationPanel();
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

        if ((modalOpen || blocked) && drawer != null && drawer.activeSelf)
            drawer.SetActive(false);
        if (gameplayLayer != null)
            gameplayLayer.SetActive(gameplayVisible);
        viewObject.SetActive(ShouldShow());

        if (hotbarConfigured)
            TryConfigureHotbarWidth();
        else if (hotbarSetupCoroutine == null && isActiveAndEnabled)
            hotbarSetupCoroutine = StartCoroutine(ConfigureHotbarWhenReady());

        if (!gameplayVisible || geometryChanged)
            ResetAllTouchState();
    }

    public void ResetAllTouchState()
    {
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
