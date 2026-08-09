using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Keeps one shared EventSystem alive across scene and dimension switches.
/// </summary>
public static class EventSystemGuard
{
    private const string CanonicalName = "EventSystem";
    private const string RuntimeUIActionMapName = "FlatWorldUI";

    private static InputActionAsset preferredInputAsset;
    private static GamepadUIRuntimeController runtimeController;
    private static bool gamepadMode;

    /// <summary>当前 UI 是否由手柄驱动，供虚拟光标与玩法输入拦截判断。</summary>
    public static bool IsGamepadMode => gamepadMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneCallbacks()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        EnsureExactlyOne();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureAfterSceneLoad()
    {
        EnsureExactlyOne();
    }

    public static EventSystem EnsureExactlyOne(bool createIfMissing = true)
    {
        EventSystem[] systems = UnityEngine.Object.FindObjectsOfType<EventSystem>(true);
        EventSystem primary = SelectPrimary(systems);

        if (primary == null)
        {
            if (!createIfMissing)
                return null;

            GameObject eventSystemObject = new GameObject(
                CanonicalName,
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(eventSystemObject);
            primary = eventSystemObject.GetComponent<EventSystem>();
            systems = new[] { primary };
        }

        if (!primary.gameObject.activeSelf)
            primary.gameObject.SetActive(true);
        if (!primary.enabled)
            primary.enabled = true;

        #region 输入模块配置

        InputSystemUIInputModule primaryInputModule = primary.GetComponent<InputSystemUIInputModule>();
        if (primaryInputModule == null)
            primaryInputModule = primary.gameObject.AddComponent<InputSystemUIInputModule>();

        BaseInputModule[] inputModules = primary.GetComponents<BaseInputModule>();
        foreach (BaseInputModule inputModule in inputModules)
        {
            if (inputModule == null || inputModule == primaryInputModule)
                continue;

            inputModule.enabled = false;
            Destroy(inputModule);
        }

        if (primaryInputModule.actionsAsset == null
            || primaryInputModule.move == null
            || primaryInputModule.submit == null
            || primaryInputModule.cancel == null)
            primaryInputModule.AssignDefaultActions();

        if (preferredInputAsset != null)
            ConfigureInputModule(primaryInputModule, preferredInputAsset);

        primary.sendNavigationEvents = true;
        primaryInputModule.deselectOnBackgroundClick = false;
        primaryInputModule.enabled = true;

        #endregion

        EnsureRuntimeController(primary)?.SetGamepadMode(gamepadMode);

        EventSystem.current = primary;

        foreach (EventSystem system in systems)
        {
            if (system == null || system == primary)
                continue;

            DisableAndDestroy(system);
        }

        return primary;
    }

    #region 输入资产接入

    /// <summary>
    /// 将本地玩家的输入资产接入 UI 模块，使导航确认键跟随玩家重绑结果。
    /// </summary>
    public static void ConfigureInputActions(InputActionAsset inputAsset)
    {
        preferredInputAsset = inputAsset;
        EventSystem eventSystem = EnsureExactlyOne();
        InputSystemUIInputModule inputModule = eventSystem?.GetComponent<InputSystemUIInputModule>();
        if (inputModule != null && inputAsset != null)
            ConfigureInputModule(inputModule, inputAsset);

        EnsureRuntimeController(eventSystem)?.Configure(inputAsset);
    }

    /// <summary>
    /// 玩家销毁时解除 UI 对已释放输入资产的引用。
    /// </summary>
    public static void ClearInputActions(InputActionAsset inputAsset)
    {
        if (inputAsset == null || !ReferenceEquals(preferredInputAsset, inputAsset))
            return;

        preferredInputAsset = null;
        EventSystem eventSystem = EventSystem.current;
        InputSystemUIInputModule inputModule = eventSystem?.GetComponent<InputSystemUIInputModule>();
        inputModule?.AssignDefaultActions();
        EnsureRuntimeController(eventSystem)?.Configure(null);
    }

    /// <summary>
    /// 同步游戏动作到运行时 UI 动作，避免玩家重绑后 UI 仍使用旧按键。
    /// </summary>
    public static void SynchronizeUIInputBindings(InputActionAsset inputAsset)
    {
        if (inputAsset == null)
            return;

        InputActionMap gameplayMap = inputAsset.FindActionMap("Win10", false);
        if (gameplayMap == null)
            return;

        InputActionMap uiMap = EnsureRuntimeUIActionMap(inputAsset, gameplayMap);
        InputAction moveAction = gameplayMap.FindAction("Move_Player", false);
        InputAction submitAction = gameplayMap.FindAction("LeftClick", false);
        InputAction cancelAction = gameplayMap.FindAction("B", false);
        InputAction escapeAction = gameplayMap.FindAction("ESC", false);

        InputAction navigate = uiMap.FindAction("Navigate", false);
        // W/A/S/D 只用于玩家移动，绝不能驱动 EventSystem 焦点；UI 导航仅接收手柄输入。
        ApplyBindingOverride(navigate, 0, FindFirstGroupBindingPath(moveAction, "Gamepad", "<Gamepad>/leftStick"));

        InputAction submit = uiMap.FindAction("Submit", false);
        List<string> submitGamepadPaths = FindGroupBindingPaths(submitAction, "Gamepad");
        for (int i = 0; i < submitGamepadPaths.Count; i++)
            ApplyBindingOverride(submit, 2 + i, submitGamepadPaths[i]);
        for (int i = submitGamepadPaths.Count; i < submit.bindings.Count - 2; i++)
            ApplyBindingOverride(submit, 2 + i, null);

        InputAction cancel = uiMap.FindAction("Cancel", false);
        // 键盘 B 仍属于库存开关；手柄 B 没有 Win10/B 绑定，由 UI Cancel 使用默认 buttonEast 返回。
        ApplyBindingOverride(cancel, 0, FindFirstGroupBindingPath(escapeAction, "Keyboard&Mouse", "<Keyboard>/escape"));
        ApplyBindingOverride(cancel, 1, FindFirstGroupBindingPath(cancelAction, "Gamepad", "<Gamepad>/buttonEast"));
        ApplyBindingOverride(cancel, 2, FindFirstGroupBindingPath(escapeAction, "Gamepad", "<Gamepad>/start"));

        uiMap.Enable();
    }

    private static void ConfigureInputModule(InputSystemUIInputModule inputModule, InputActionAsset inputAsset)
    {
        if (inputModule == null || inputAsset == null)
            return;

        InputActionMap gameplayMap = inputAsset.FindActionMap("Win10", false);
        if (gameplayMap == null)
            return;

        InputActionMap uiMap = EnsureRuntimeUIActionMap(inputAsset, gameplayMap);
        SynchronizeUIInputBindings(inputAsset);

        inputModule.actionsAsset = inputAsset;
        inputModule.move = CreateReference(uiMap.FindAction("Navigate", false));
        inputModule.submit = CreateReference(uiMap.FindAction("Submit", false));
        inputModule.cancel = CreateReference(uiMap.FindAction("Cancel", false));
        inputModule.point = CreateReference(uiMap.FindAction("MousePoint", false));
        inputModule.leftClick = CreateReference(uiMap.FindAction("MouseLeftClick", false));
        inputModule.rightClick = CreateReference(uiMap.FindAction("MouseRightClick", false));
        inputModule.middleClick = null;
        inputModule.scrollWheel = CreateReference(uiMap.FindAction("MouseScroll", false));
        inputModule.trackedDevicePosition = null;
        inputModule.trackedDeviceOrientation = null;
    }

    private static InputActionMap EnsureRuntimeUIActionMap(
        InputActionAsset inputAsset,
        InputActionMap gameplayMap)
    {
        InputActionMap uiMap = inputAsset.FindActionMap(RuntimeUIActionMapName, false);
        if (uiMap == null)
        {
            uiMap = inputAsset.AddActionMap(RuntimeUIActionMapName);

            InputAction navigate = uiMap.AddAction("Navigate", InputActionType.Value, expectedControlLayout: "Vector2");
            navigate.AddBinding("<Gamepad>/leftStick");

            InputAction submit = uiMap.AddAction("Submit", InputActionType.Button, expectedControlLayout: "Button");
            submit.AddBinding("<Keyboard>/enter");
            submit.AddBinding("<Keyboard>/numpadEnter");
            List<string> submitGamepadPaths = FindGroupBindingPaths(
                gameplayMap.FindAction("LeftClick", false),
                "Gamepad");
            for (int i = 0; i < submitGamepadPaths.Count; i++)
                submit.AddBinding(submitGamepadPaths[i]);
            if (submitGamepadPaths.Count == 0)
                submit.AddBinding("<Gamepad>/buttonSouth");

            InputAction cancel = uiMap.AddAction("Cancel", InputActionType.Button, expectedControlLayout: "Button");
            cancel.AddBinding("<Keyboard>/escape");
            cancel.AddBinding("<Gamepad>/buttonEast");
            cancel.AddBinding("<Gamepad>/start");

            InputAction point = uiMap.AddAction("MousePoint", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            point.AddBinding("<Mouse>/position");
            InputAction leftClick = uiMap.AddAction("MouseLeftClick", InputActionType.PassThrough, expectedControlLayout: "Button");
            leftClick.AddBinding("<Mouse>/leftButton");
            InputAction rightClick = uiMap.AddAction("MouseRightClick", InputActionType.PassThrough, expectedControlLayout: "Button");
            rightClick.AddBinding("<Mouse>/rightButton");
            InputAction scroll = uiMap.AddAction("MouseScroll", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            scroll.AddBinding("<Mouse>/scroll");
        }

        return uiMap;
    }

    private static InputActionReference CreateReference(InputAction action)
    {
        return action == null ? null : InputActionReference.Create(action);
    }

    private static string FindFirstGroupBindingPath(InputAction action, string group, string fallback)
    {
        List<string> paths = FindGroupBindingPaths(action, group);
        return paths.Count > 0 ? paths[0] : fallback;
    }

    private static List<string> FindGroupBindingPaths(InputAction action, string group)
    {
        List<string> paths = new List<string>();
        if (action == null)
            return paths;

        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (binding.isComposite || binding.isPartOfComposite ||
                string.IsNullOrEmpty(binding.effectivePath) ||
                string.IsNullOrEmpty(binding.groups) ||
                binding.groups.IndexOf(group, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            paths.Add(binding.effectivePath);
        }

        return paths;
    }

    private static void ApplyBindingOverride(InputAction action, int bindingIndex, string path)
    {
        if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
            return;

        if (string.IsNullOrEmpty(path))
        {
            action.RemoveBindingOverride(bindingIndex);
            return;
        }

        action.ApplyBindingOverride(bindingIndex, path);
    }

    #endregion

    #region 手柄焦点与上下文操作

    /// <summary>
    /// 更新当前输入设备，供虚拟光标和输入框键盘共享状态。
    /// </summary>
    public static void SetGamepadMode(bool enabled)
    {
        gamepadMode = enabled;
        EventSystem eventSystem = EventSystem.current;
        EnsureRuntimeController(eventSystem)?.SetGamepadMode(enabled);
    }

    public static void NotifyGamepadFocusInput()
    {
        EnsureRuntimeController(EventSystem.current)?.NotifyFocusInput();
    }

    public static void NotifyGamepadCursorPosition(Vector2 screenPosition)
    {
        EnsureRuntimeController(EventSystem.current)?.NotifyCursorPosition(screenPosition);
    }

    public static bool TryHandleGamepadVirtualCursorClick()
    {
        return EnsureRuntimeController(EventSystem.current)?.TryClickVirtualCursor() == true;
    }

    public static bool IsGamepadUISelectionActive
    {
        get
        {
            if (!gamepadMode || EnsureRuntimeController(EventSystem.current)?.IsVirtualCursorMode == true)
                return false;

            GameObject selectedObject = EventSystem.current?.currentSelectedGameObject;
            if (selectedObject == null || !selectedObject.activeInHierarchy)
                return false;

            Selectable selectable = selectedObject.GetComponent<Selectable>() ??
                                     selectedObject.GetComponentInParent<Selectable>();
            return selectable != null && selectable.IsInteractable() &&
                   !FlatWorldUITheme.IsGamepadNavigationExcluded(selectable);
        }
    }

    public static bool IsVirtualKeyboardOpen => GamepadVirtualKeyboardController.IsOpen;

    /// <summary>当前是否存在打开且接入手柄导航的 UI 面板。</summary>
    public static bool HasOpenGamepadNavigationPanel
    {
        get
        {
            UIManager manager = UIManager.ExistingInstance;
            return manager != null && manager.HasOpenGamepadNavigationPanel();
        }
    }

    /// <summary>当前是否存在打开且需要接管玩法输入的模态手柄面板。</summary>
    public static bool HasOpenModalGamepadNavigationPanel
    {
        get
        {
            UIManager manager = UIManager.ExistingInstance;
            return manager != null && manager.HasOpenModalGamepadNavigationPanel();
        }
    }

    public static bool TryHandleGamepadContextAction()
    {
        if (!gamepadMode)
            return false;

        GameObject target = EnsureRuntimeController(EventSystem.current)?.GetInteractionTarget();
        if (target == null)
            return false;

        MonoBehaviour[] behaviours = target.GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IGamepadContextActionHandler handler)
                return handler.HandleGamepadContextAction();
        }

        return false;
    }

    private static GamepadUIRuntimeController EnsureRuntimeController(EventSystem eventSystem)
    {
        if (eventSystem == null)
            return null;

        if (runtimeController == null || runtimeController.gameObject != eventSystem.gameObject)
        {
            runtimeController = eventSystem.GetComponent<GamepadUIRuntimeController>();
            if (runtimeController == null)
                runtimeController = eventSystem.gameObject.AddComponent<GamepadUIRuntimeController>();
        }

        if (preferredInputAsset != null)
            runtimeController.Configure(preferredInputAsset);
        runtimeController.SetGamepadMode(gamepadMode);
        return runtimeController;
    }

    #endregion

    private static EventSystem SelectPrimary(EventSystem[] systems)
    {
        if (systems == null || systems.Length == 0)
            return null;

        EventSystem current = EventSystem.current;
        EventSystem canonical = Array.Find(systems, system =>
            system != null &&
            system.isActiveAndEnabled &&
            string.Equals(system.gameObject.name, CanonicalName, StringComparison.Ordinal));
        if (canonical != null)
            return canonical;

        if (current != null && current.isActiveAndEnabled)
            return current;

        EventSystem active = Array.Find(systems, system => system != null && system.isActiveAndEnabled);
        return active ?? Array.Find(systems, system => system != null);
    }

    private static void DisableAndDestroy(EventSystem duplicate)
    {
        GameObject owner = duplicate.gameObject;
        BaseInputModule[] inputModules = owner.GetComponents<BaseInputModule>();
        foreach (BaseInputModule inputModule in inputModules)
            inputModule.enabled = false;
        duplicate.enabled = false;

        if (IsDedicatedEventSystemObject(owner))
        {
            Destroy(owner);
            return;
        }

        foreach (BaseInputModule inputModule in inputModules)
            Destroy(inputModule);
        Destroy(duplicate);
    }

    private static bool IsDedicatedEventSystemObject(GameObject owner)
    {
        if (owner.transform.childCount > 0)
            return false;

        foreach (Component component in owner.GetComponents<Component>())
        {
            if (component is Transform || component is EventSystem || component is BaseInputModule)
                continue;

            return false;
        }

        return true;
    }

    private static void Destroy(UnityEngine.Object target)
    {
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(target);
        else
            UnityEngine.Object.DestroyImmediate(target);
    }

    private static void OnActiveSceneChanged(Scene previous, Scene next)
    {
        EnsureExactlyOne();
    }
}
