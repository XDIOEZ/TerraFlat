using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 管理 FlatWorld 的场景 UI、跨场景 PanelRoot、面板实例与手柄交互面版本。
/// 根 Canvas 只在创建或失效时解析；交互面修订号供虚拟光标按需刷新命中结果。
/// </summary>
public class UIManager : MonoBehaviour
{
    #region 单例模式
    private static UIManager _instance;
    private static bool _isShuttingDown;

    /// <summary>退出播放模式或应用退出时，禁止 UI 事件继续触发新的交互刷新。</summary>
    public static bool IsShuttingDown => _isShuttingDown;

    /// <summary>重置禁用域重载时残留的静态单例状态。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        _instance = null;
        _isShuttingDown = false;
    }

    public static UIManager Instance
    {
        get
        {
            // 关停阶段只允许读取现有实例，禁止惰性单例触发 Awake 并重新创建 PanelRoot。
            if (_isShuttingDown)
                return _instance;

            if (_instance == null)
            {
                _instance = FindObjectOfType<UIManager>();

                if (_instance == null)
                {
                    GameObject singletonObject = new GameObject("UIManager");
                    _instance = singletonObject.AddComponent<UIManager>();
                }
            }
            return _instance;
        }
    }

    /// <summary>只读取当前已存在的 UIManager，不在输入轮询中隐式创建对象。</summary>
    public static UIManager ExistingInstance => _instance;
    #endregion

    #region 字段声明
    [ShowInInspector]
    public Dictionary<string, List<BasePanel>> panels = new Dictionary<string, List<BasePanel>>();

    [Tooltip("跨场景 UI 根；玩家界面显式创建在这里，普通面板默认归当前场景。")]
    public Transform panelRoot;
    public GameObject panelRootPrefab;
    public GameObject[] panelPrefabs;
    public const int GameplayHudSortingOrder = 0;
    /// <summary>指针手持物是纯视觉拖拽层，固定占用 Canvas 排序上限，始终高于其它游戏 UI。</summary>
    public const int HeldItemSortingOrder = 32767;
    /// <summary>设置面板的基础 Canvas 层级；高于玩法 HUD，低于加载和调试全屏覆盖层。</summary>
    public const int SettingsPanelSortingOrder = 2000;
    public const int GlobalOverlaySortingOrder = 32000;

    private Canvas rootCanvas;
    private RectTransform safeAreaRoot;
    private readonly Dictionary<int, RectTransform> scenePanelRoots = new Dictionary<int, RectTransform>();
    private bool creatingSceneUi;
    private int nextSettingsPanelSortingOrder = SettingsPanelSortingOrder;
    private int interactionSurfaceRevision;
    private int lastHandledCancelFrame = -1;
    private bool presentationUiHidden;
    private readonly Dictionary<Canvas, bool> presentationCanvasStates = new Dictionary<Canvas, bool>();
    private readonly Dictionary<GraphicRaycaster, bool> presentationRaycasterStates =
        new Dictionary<GraphicRaycaster, bool>();

    // 顶层面板查询只在交互面修订号变化时重新扫描，避免手柄路径逐帧分配数组。
    private readonly List<BasePanel> panelQueryBuffer = new List<BasePanel>(16);
    private Transform panelQueryCacheRoot;
    private int panelQueryCacheRevision = int.MinValue;
    private BasePanel cachedTopmostGamepadPanel;
    private BasePanel cachedTopmostGameplayInputPanel;
    private BasePanel cachedTopmostCancelPanel;
    private readonly HeldItemWorldDropPanelQuery heldItemWorldDropPanelQuery = new HeldItemWorldDropPanelQuery();

    /// <summary>Profiler 可读取的顶层面板缓存重建次数。</summary>
    public int PanelQueryCacheRebuildCount { get; private set; }

    /// <summary>面板、输入锁、抽屉或返回栈变化时通知常驻 HUD 立即刷新可交互状态。</summary>
    public event Action InteractionSurfaceChanged;

    /// <summary>缓存的 PanelRoot Canvas；仅在根节点失效或替换时重新解析。</summary>
    public Canvas RootCanvas
    {
        get
        {
            EnsurePanelRootExists();
            return rootCanvas;
        }
    }

    /// <summary>正式面板和手机控制的只读安全区根节点；全屏背景仍由外层根 Canvas 承载。</summary>
    public RectTransform SafeAreaRoot
    {
        get
        {
            EnsurePanelRootExists();
            return safeAreaRoot;
        }
    }

    /// <summary>当前场景的默认 UI 根，随所属场景卸载而销毁。</summary>
    public RectTransform ScenePanelRoot => GetScenePanelRoot(SceneManager.GetActiveScene());

    /// <summary>面板开关、排序或结构改变时递增，供虚拟光标判断是否需要重新射线。</summary>
    public int InteractionSurfaceRevision => interactionSurfaceRevision;

    /// <summary>宣传片录制模式是否正在隐藏全部运行时 UI。</summary>
    public bool IsPresentationUiHidden => presentationUiHidden;
    #endregion

    #region 面板层级

    /// <summary>
    /// 为设置类面板建立独立高优先级 Canvas，避免仅依赖 PanelRoot 的兄弟顺序被其它 UI 覆盖。
    /// </summary>
    public void ConfigureSettingsPanelLayer(BasePanel panel)
    {
        if (panel == null)
            return;

        Canvas canvas = panel.GetComponent<Canvas>();
        if (canvas == null)
            canvas = panel.gameObject.AddComponent<Canvas>();

        Canvas ownerCanvas = canvas.rootCanvas;
        canvas.overrideSorting = true;
        if (ownerCanvas != null)
            canvas.sortingLayerID = ownerCanvas.sortingLayerID;

        // 同一批设置页按创建顺序递增，打开子设置页时始终位于主设置页之上。
        if (canvas.sortingOrder < SettingsPanelSortingOrder)
        {
            canvas.sortingOrder = nextSettingsPanelSortingOrder++;
        }

        // 独立 Canvas 需要自己的射线入口，否则动态添加 Canvas 后控件可能无法点击。
        if (panel.GetComponent<GraphicRaycaster>() == null)
            panel.gameObject.AddComponent<GraphicRaycaster>();

        panel.transform.SetAsLastSibling();
        NotifyInteractionSurfaceChanged();
    }

    /// <summary>把需要遮挡整个游戏界面的面板移动到外层根 Canvas，并提升到全局覆盖层。</summary>
    public void ConfigureGlobalOverlayPanel(BasePanel panel)
    {
        if (panel == null)
            return;

        // 覆盖层只提升显示顺序，保留面板原来的场景或跨场景归属。
        Canvas ownerCanvas = panel.GetComponentInParent<Canvas>()?.rootCanvas;
        if (ownerCanvas == null)
            return;

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        if (panelRect == null)
            return;

        panelRect.SetParent(ownerCanvas.transform, false);
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = Vector2.zero;

        Canvas canvas = panel.GetComponent<Canvas>();
        if (canvas == null)
            canvas = panel.gameObject.AddComponent<Canvas>();

        canvas.overrideSorting = true;
        canvas.sortingLayerID = ownerCanvas.sortingLayerID;
        canvas.sortingOrder = GlobalOverlaySortingOrder;

        if (panel.GetComponent<GraphicRaycaster>() == null)
            panel.gameObject.AddComponent<GraphicRaycaster>();

        panelRect.SetAsLastSibling();
        NotifyInteractionSurfaceChanged();
    }

    public static void NormalizeCanvasLayers(Transform root)
    {
        if (root == null)
            return;

        Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas != null && !canvas.overrideSorting)
                canvas.sortingOrder = GameplayHudSortingOrder;
        }
    }

    private static bool IsSettingsPanelName(string panelName)
    {
        if (string.IsNullOrEmpty(panelName))
            return false;

        return panelName.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0 ||
               panelName.IndexOf("设置", StringComparison.Ordinal) >= 0;
    }

    #endregion

    #region 初始化
    private void Awake()
    {
        // 退出播放模式后即使有迟到的对象回调，也不能再创建新的 UIManager。
        if (_isShuttingDown)
        {
            Destroy(gameObject);
            return;
        }

        EventSystemGuard.EnsureExactlyOne();

        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        InitializePanels();
        EnsurePanelRootExists();
        SceneManager.sceneUnloaded += HandleSceneUnloaded;
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard?.f1Key.wasPressedThisFrame != true)
            return;

        // Ctrl+F1 保留给管理员快捷键，裸 F1 专用于宣传片录制模式。
        if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)
            return;

        TogglePresentationUiVisibility();
    }

    /// <summary>退出播放模式前停止交互面事件，避免销毁阶段重新创建 UI。</summary>
    private void OnApplicationQuit()
    {
        if (_instance == this)
            _isShuttingDown = true;
    }

    /// <summary>清理单例引用和事件，避免场景卸载后继续访问已销毁的 UI。</summary>
    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        Canvas.preWillRenderCanvases -= EnforcePresentationUiHidden;
        RestorePresentationUi();

        if (_instance != this)
            return;

        // OnApplicationQuit 在编辑器强制停止播放时序中可能晚于其它对象的销毁回调。
        _isShuttingDown = true;
        _instance = null;
        InteractionSurfaceChanged = null;
    }

    private void EnsurePanelRootExists()
    {
        // 根节点失效时的重建只允许发生在正常运行阶段，销毁阶段必须保持无创建语义。
        if (_isShuttingDown)
            return;

        if (panelRoot != null)
        {
            CacheRootCanvas();
            AttachRootCanvasToOwner();
            ResolveSafeAreaRoot();
            UIScaleController.Ensure(rootCanvas != null ? rootCanvas.transform : panelRoot);
            return;
        }

        GameObject rootPrefab = GetRootPrefab();
        GameObject canvasObj = Instantiate(rootPrefab, transform);
        canvasObj.name = "PanelRoot";
        panelRoot = canvasObj.transform;
        CacheRootCanvas();
        ResolveSafeAreaRoot();
        UIScaleController.Ensure(rootCanvas != null ? rootCanvas.transform : panelRoot);
    }

    private GameObject GetRootPrefab()
    {
        GameObject rootPrefab = panelRootPrefab != null
            ? panelRootPrefab
            : Resources.Load<GameObject>("UI/UIRoot");
        if (rootPrefab == null)
        {
            throw new System.InvalidOperationException(
                "[UIManager] 缺少 Assets/Resources/UI/UIRoot.prefab，禁止运行时程序化创建 Canvas。");
        }

        return rootPrefab;
    }

    private RectTransform GetScenePanelRoot(Scene scene)
    {
        if (_isShuttingDown)
            return null;
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("[UIManager] 默认 UI 必须归属一个已加载的场景。");

        if (scenePanelRoots.TryGetValue(scene.handle, out RectTransform existingRoot) && existingRoot != null)
            return existingRoot;

        RectTransform sceneSafeArea;
        // 根 Prefab 的 OnEnable 会通知交互变化，登记完成前暂缓广播以免重入创建。
        creatingSceneUi = true;
        try
        {
            GameObject sceneUi = Instantiate(GetRootPrefab());
            sceneUi.name = "SceneUI";
            SceneManager.MoveGameObjectToScene(sceneUi, scene);
            // 场景面板固定高于普通玩家 HUD，两根的显示与焦点查询使用同一排序规则。
            sceneUi.GetComponent<Canvas>().sortingOrder = GameplayHudSortingOrder + 1;
            sceneSafeArea = sceneUi.transform.Find("SafeAreaRoot") as RectTransform;
            if (sceneSafeArea == null)
            {
                Destroy(sceneUi);
                throw new InvalidOperationException("[UIManager] UIRoot.prefab 缺少 SafeAreaRoot。");
            }

            scenePanelRoots[scene.handle] = sceneSafeArea;
            SafeAreaRectController.Ensure(sceneSafeArea);
            UIScaleController.Ensure(sceneUi.transform);
        }
        finally
        {
            creatingSceneUi = false;
        }
        NotifyInteractionSurfaceChanged();
        return sceneSafeArea;
    }

    /// <summary>附属弹窗和输入界面沿用拥有者的 UI 根与销毁时机。</summary>
    public Transform GetPanelRootFor(Transform owner)
    {
        EnsurePanelRootExists();
        if (owner != null && rootCanvas != null && owner.IsChildOf(rootCanvas.transform))
            return panelRoot;
        return GetScenePanelRoot(owner != null ? owner.gameObject.scene : SceneManager.GetActiveScene());
    }

    private void HandleActiveSceneChanged(Scene previous, Scene current)
    {
        NotifyInteractionSurfaceChanged();
    }

    private void HandleSceneUnloaded(Scene scene)
    {
        scenePanelRoots.Remove(scene.handle);
        foreach (List<BasePanel> panelList in panels.Values)
            panelList.RemoveAll(panel => panel == null);
        NotifyInteractionSurfaceChanged();
    }

    private void AttachRootCanvasToOwner()
    {
        // UI 根随持久管理器存活，卸载星区场景不能销毁仍由同一玩家持有的 HUD。
        if (rootCanvas != null && rootCanvas.transform.parent != transform)
            rootCanvas.transform.SetParent(transform, true);
    }

    /// <summary>只在根 Canvas 无效或不再属于 PanelRoot 时重新解析引用。</summary>
    private void CacheRootCanvas()
    {
        if (panelRoot == null)
        {
            if (rootCanvas != null)
            {
                rootCanvas = null;
                NotifyInteractionSurfaceChanged();
            }
            return;
        }

        if (rootCanvas != null &&
            (rootCanvas.transform == panelRoot ||
             rootCanvas.transform.IsChildOf(panelRoot) ||
             panelRoot.IsChildOf(rootCanvas.transform)))
        {
            return;
        }

        Canvas nextCanvas = panelRoot.GetComponent<Canvas>() ??
                            panelRoot.GetComponentInParent<Canvas>() ??
                            panelRoot.GetComponentInChildren<Canvas>(true);
        if (rootCanvas == nextCanvas)
            return;

        rootCanvas = nextCanvas;
        NotifyInteractionSurfaceChanged();
        if (rootCanvas == null)
            Debug.LogError("[UIManager] PanelRoot 缺少 Canvas，虚拟光标无法绑定。", panelRoot);
    }

    /// <summary>把运行时面板根切到正式 Prefab 中的 SafeAreaRoot，并保证安全区组件生效。</summary>
    private void ResolveSafeAreaRoot()
    {
        if (rootCanvas == null)
            return;

        RectTransform nextSafeArea = rootCanvas.transform.Find("SafeAreaRoot") as RectTransform;
        if (nextSafeArea == null)
        {
            safeAreaRoot = null;
            panelRoot = rootCanvas.transform;
            Debug.LogError("[UIManager] UIRoot.prefab 缺少 SafeAreaRoot，面板已暂时回退到全屏根节点。", rootCanvas);
            return;
        }

        safeAreaRoot = nextSafeArea;
        panelRoot = safeAreaRoot;
        SafeAreaRectController.Ensure(safeAreaRoot);
    }

    private void InitializePanels()
    {
        EnsurePanelRootExists();
        panels.Clear();
        NotifyInteractionSurfaceChanged();
    }
    #endregion

    #region 宣传片录制 UI

    /// <summary>切换宣传片录制 UI 隐藏状态；不会改变面板自身的开关状态。</summary>
    public void TogglePresentationUiVisibility()
    {
        SetPresentationUiHidden(!presentationUiHidden);
    }

    /// <summary>
    /// 设置宣传片录制 UI 隐藏状态。
    /// 隐藏时仅停用 Canvas 与 GraphicRaycaster，恢复时还原进入录制模式前的 enabled 状态。
    /// </summary>
    public void SetPresentationUiHidden(bool hidden)
    {
        if (presentationUiHidden == hidden)
            return;

        presentationUiHidden = hidden;
        if (hidden)
        {
            CaptureAndDisablePresentationUi();
            Canvas.preWillRenderCanvases -= EnforcePresentationUiHidden;
            Canvas.preWillRenderCanvases += EnforcePresentationUiHidden;
        }
        else
        {
            Canvas.preWillRenderCanvases -= EnforcePresentationUiHidden;
            RestorePresentationUi();
        }

        NotifyInteractionSurfaceChanged();
    }

    /// <summary>在 Canvas 真正渲染前捕获并隐藏录制期间新创建的 UI，避免动态 HUD 短暂闪现。</summary>
    private void EnforcePresentationUiHidden()
    {
        if (!presentationUiHidden)
            return;

        CaptureAndDisablePresentationUi();
    }

    /// <summary>记录当前 UI 组件状态并停用渲染与 UI 射线，不关闭业务面板。</summary>
    private void CaptureAndDisablePresentationUi()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null)
                continue;

            if (!presentationCanvasStates.ContainsKey(canvas))
                presentationCanvasStates.Add(canvas, canvas.enabled);
            canvas.enabled = false;
        }

        GraphicRaycaster[] raycasters = FindObjectsByType<GraphicRaycaster>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < raycasters.Length; i++)
        {
            GraphicRaycaster raycaster = raycasters[i];
            if (raycaster == null)
                continue;

            if (!presentationRaycasterStates.ContainsKey(raycaster))
                presentationRaycasterStates.Add(raycaster, raycaster.enabled);
            raycaster.enabled = false;
        }
    }

    /// <summary>恢复进入录制模式前保存的 Canvas 与射线器状态。</summary>
    private void RestorePresentationUi()
    {
        foreach (KeyValuePair<Canvas, bool> pair in presentationCanvasStates)
        {
            if (pair.Key != null)
                pair.Key.enabled = pair.Value;
        }
        presentationCanvasStates.Clear();

        foreach (KeyValuePair<GraphicRaycaster, bool> pair in presentationRaycasterStates)
        {
            if (pair.Key != null)
                pair.Key.enabled = pair.Value;
        }
        presentationRaycasterStates.Clear();
    }

    #endregion

    #region 面板获取
    public BasePanel GetPanel(string panelName)
    {
        if (TryGetPanel(panelName, out BasePanel panel))
            return panel;

        Debug.Log($"Panel '{panelName}' not found!");
        return null;
    }

    public bool TryGetPanel(string panelName, out BasePanel panel)
    {
        panel = null;
        if (!panels.TryGetValue(panelName, out List<BasePanel> panelList))
            return false;

        panelList.RemoveAll(item => item == null);
        if (panelList.Count == 0)
            return false;

        panel = panelList[0];
        return true;
    }

    public List<BasePanel> GetAllPanelsOfType(string panelName)
    {
        if (panels.TryGetValue(panelName, out List<BasePanel> panelList))
        {
            panelList.RemoveAll(panel => panel == null);
            return panelList;
        }
        return new List<BasePanel>();
    }
    #endregion

    #region 面板显示/隐藏
    public void ShowPanel(string panelName)
    {
        BasePanel panel = GetPanel(panelName);
        if (panel != null)
        {
            panel.Open();
        }
    }

    public void HidePanel(string panelName)
    {
        BasePanel panel = GetPanel(panelName);
        if (panel != null)
        {
            panel.Close();
        }
    }

    public void TogglePanel(string panelName)
    {
        BasePanel panel = GetPanel(panelName);
        if (panel != null)
        {
            panel.Toggle();
        }
    }

    public void HideAllPanels()
    {
        foreach (var panelList in panels.Values)
        {
            foreach (var panel in panelList)
            {
                if (panel != null)
                    panel.Close();
            }
        }
    }

    public void ShowAllPanels()
    {
        foreach (var panelList in panels.Values)
        {
            foreach (var panel in panelList)
            {
                if (panel != null)
                    panel.Open();
            }
        }
    }

    public void SetPanelVisible(string panelName, bool isVisible)
    {
        BasePanel panel = GetPanel(panelName);
        if (panel != null)
        {
            if (isVisible)
            {
                panel.Open();
            }
            else
            {
                panel.Close();
            }
        }
    }

    /// <summary>
    /// 同一帧内，EventSystem 和游戏输入可能同时收到 Escape；记录消费状态可避免一次按键又打开设置面板。
    /// </summary>
    public bool WasCancelHandledThisFrame()
    {
        return lastHandledCancelFrame == Time.frameCount;
    }

    public void NotifyCancelHandled()
    {
        lastHandledCancelFrame = Time.frameCount;
    }

    /// <summary>通知虚拟光标当前可命中的 UI 表面已经变化。</summary>
    public void NotifyInteractionSurfaceChanged()
    {
        if (_isShuttingDown || creatingSceneUi)
            return;

        unchecked
        {
            interactionSurfaceRevision++;
        }
        InteractionSurfaceChanged?.Invoke();
    }

    /// <summary>
    /// 关闭最上层的临时面板。excludedPanel 通常是设置面板本身。
    /// </summary>
    public bool TryCloseTopmostCancelPanel(BasePanel excludedPanel = null)
    {
        EnsurePanelQueryCache();
        BasePanel panel = cachedTopmostCancelPanel;
        if (panel == excludedPanel)
        {
            panel = FindTopmostCancelPanel(rootCanvas != null ? rootCanvas.transform : null, excludedPanel);
            BasePanel scenePanel = FindTopmostCancelPanel(GetCurrentSceneCanvasRoot(), excludedPanel);
            if (IsPanelAbove(scenePanel, panel))
                panel = scenePanel;
        }
        if (panel == null)
            return false;

        NotifyCancelHandled();
        if (panel.TryHandleCancelShortcut())
            return true;

        panel.Close();
        return true;
    }

    /// <summary>
    /// 找到当前最上层、已接入手柄导航的打开面板，并把焦点交还给它。
    /// </summary>
    public bool SelectTopmostGamepadPanel()
    {
        EnsurePanelQueryCache();
        BasePanel panel = cachedTopmostGamepadPanel;
        if (panel == null)
            return false;

        panel.SelectDefaultForGamepad();
        return true;
    }

    /// <summary>
    /// 将手柄焦点限制在当前最上层导航面板，阻止 Automatic Navigation 跳到背景 UI。
    /// </summary>
    public bool ConstrainSelectionToTopmostGamepadPanel()
    {
        EnsurePanelQueryCache();
        BasePanel panel = cachedTopmostGamepadPanel;
        if (panel == null)
            return false;

        GameObject selectedObject = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        if (selectedObject != null &&
            (selectedObject.transform == panel.transform || selectedObject.transform.IsChildOf(panel.transform)))
        {
            return false;
        }

        panel.SelectDefaultForGamepad();
        return true;
    }

    /// <summary>判断当前是否存在打开且已接入手柄焦点导航的面板。</summary>
    public bool HasOpenGamepadNavigationPanel()
    {
        EnsurePanelQueryCache();
        return cachedTopmostGamepadPanel != null;
    }

    /// <summary>判断当前是否存在打开且已接入手柄导航取消契约的模态面板。</summary>
    public bool HasOpenModalGamepadNavigationPanel()
    {
        EnsurePanelQueryCache();
        return cachedTopmostCancelPanel != null;
    }

    /// <summary>判断当前是否存在打开且必须阻断手机/玩法输入的面板，与手柄导航资格解耦。</summary>
    public bool HasOpenGameplayInputBlockingPanel()
    {
        EnsurePanelQueryCache();
        return cachedTopmostGameplayInputPanel != null;
    }

    #region 手持物世界丢弃资格

    /// <summary>所有打开的阻断面板都允许世界丢弃时，才开放窗口外的空白区域。</summary>
    public bool CanDropHeldItemInWorld()
    {
        EnsurePanelQueryCache();
        return heldItemWorldDropPanelQuery.CanDropHeldItemInWorld;
    }

    /// <summary>只认可当前打开且已授权面板登记的输入锁，不放行其它玩法锁。</summary>
    public bool IsHeldItemDropCompatibleLockOwner(object owner)
    {
        EnsurePanelQueryCache();
        return heldItemWorldDropPanelQuery.IsCompatibleLockOwner(owner);
    }

    /// <summary>窗口内部的透明空白也属于 UI，仅窗口外的屏幕位置可以丢弃。</summary>
    public bool CanDropHeldItemAtScreenPosition(Vector2 screenPosition, Camera eventCamera)
    {
        EnsurePanelQueryCache();
        return heldItemWorldDropPanelQuery.CanDropAtScreenPosition(screenPosition, eventCamera);
    }

    /// <summary>与顶层面板查询共用一次扫描，独立规则夹具也复用同一份聚合逻辑。</summary>
    public sealed class HeldItemWorldDropPanelQuery
    {
        private readonly HashSet<object> compatibleOwners = new HashSet<object>();
        private readonly List<UIWindowDragSurface> windowSurfaces = new List<UIWindowDragSurface>(4);
        private readonly List<(RectTransform Rect, Canvas Canvas)> protectedWindows =
            new List<(RectTransform, Canvas)>(8);

        public bool CanDropHeldItemInWorld { get; private set; } = true;

        public void Clear()
        {
            CanDropHeldItemInWorld = true;
            compatibleOwners.Clear();
            protectedWindows.Clear();
        }

        public void Include(BasePanel panel)
        {
            if (panel == null || !panel.isActiveAndEnabled || !panel.IsOpen())
                return;

            if (panel.IsGameplayInputBlocking && !panel.AllowsHeldItemWorldDrop)
                CanDropHeldItemInWorld = false;

            object owner = panel.HeldItemWorldDropLockOwner;
            if (panel.AllowsHeldItemWorldDrop && IsAliveOwner(owner))
                compatibleOwners.Add(owner);

            if (!panel.AllowsHeldItemWorldDrop)
                return;

            // 正式组合窗口的根节点可能全屏，优先记录拖动面声明的实际可见窗口。
            panel.GetComponentsInChildren(true, windowSurfaces);
            bool hasWindowBounds = false;
            foreach (UIWindowDragSurface surface in windowSurfaces)
            {
                RectTransform bounds = surface.WindowRect;
                if (bounds == null)
                    continue;

                protectedWindows.Add((bounds, bounds.GetComponentInParent<Canvas>()));
                hasWindowBounds = true;
            }
            windowSurfaces.Clear();
            if (!hasWindowBounds && panel.rectTransform != null)
                protectedWindows.Add((panel.rectTransform, panel.rectTransform.GetComponentInParent<Canvas>()));
        }

        public bool IsCompatibleLockOwner(object owner)
        {
            return IsAliveOwner(owner) && compatibleOwners.Contains(owner);
        }

        public bool CanDropAtScreenPosition(Vector2 screenPosition, Camera eventCamera)
        {
            if (!CanDropHeldItemInWorld || float.IsNaN(screenPosition.x) || float.IsNaN(screenPosition.y) ||
                float.IsInfinity(screenPosition.x) || float.IsInfinity(screenPosition.y))
                return false;

            foreach ((RectTransform bounds, Canvas canvas) in protectedWindows)
            {
                if (bounds == null || !bounds.gameObject.activeInHierarchy)
                    continue;

                Canvas root = canvas != null ? canvas.rootCanvas : null;
                Camera camera = root == null ? eventCamera : root.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null : root.worldCamera != null ? root.worldCamera : eventCamera;
                if (RectTransformUtility.RectangleContainsScreenPoint(bounds, screenPosition, camera))
                    return false;
            }

            return true;
        }

        private static bool IsAliveOwner(object owner)
        {
            return owner != null && (owner is not UnityEngine.Object unityOwner || unityOwner != null);
        }
    }

    #endregion

    /// <summary>按当前修订号缓存最上层导航面板、玩法模态面板和取消目标。</summary>
    private void EnsurePanelQueryCache()
    {
        // 缓存命中时不再重复解析 Canvas、UIScaleController 或其它根组件。
        if (panelRoot == null)
            EnsurePanelRootExists();

        if (panelQueryCacheRoot == panelRoot &&
            panelQueryCacheRevision == interactionSurfaceRevision)
        {
            return;
        }

        cachedTopmostGamepadPanel = null;
        cachedTopmostGameplayInputPanel = null;
        cachedTopmostCancelPanel = null;
        heldItemWorldDropPanelQuery.Clear();
        panelQueryBuffer.Clear();

        InspectPanelQueryRoot(rootCanvas != null ? rootCanvas.transform : null);
        InspectPanelQueryRoot(GetCurrentSceneCanvasRoot());

        panelQueryCacheRoot = panelRoot;
        panelQueryCacheRevision = interactionSurfaceRevision;
        PanelQueryCacheRebuildCount++;
    }

    private Transform GetCurrentSceneCanvasRoot()
    {
        return scenePanelRoots.TryGetValue(SceneManager.GetActiveScene().handle, out RectTransform root) && root != null
            ? root.parent
            : null;
    }

    private void InspectPanelQueryRoot(Transform root)
    {
        if (root != null)
        {
            for (int childIndex = root.childCount - 1; childIndex >= 0; childIndex--)
            {
                Transform child = root.GetChild(childIndex);
                child.GetComponentsInChildren<BasePanel>(true, panelQueryBuffer);
                for (int panelIndex = panelQueryBuffer.Count - 1; panelIndex >= 0; panelIndex--)
                {
                    BasePanel panel = panelQueryBuffer[panelIndex];
                    if (panel == null)
                        continue;

                    heldItemWorldDropPanelQuery.Include(panel);

                    if (panel.IsOpen() && panel.gameObject.activeInHierarchy && panel.IsGamepadNavigationPrepared &&
                        IsPanelAbove(panel, cachedTopmostGamepadPanel))
                    {
                        cachedTopmostGamepadPanel = panel;
                    }

                    if (panel.IsGameplayInputBlocking && IsPanelAbove(panel, cachedTopmostGameplayInputPanel))
                        cachedTopmostGameplayInputPanel = panel;

                    if (panel.IsCancelShortcutTarget && IsPanelAbove(panel, cachedTopmostCancelPanel))
                        cachedTopmostCancelPanel = panel;
                }

                panelQueryBuffer.Clear();
            }
        }
    }

    private static bool IsPanelAbove(BasePanel candidate, BasePanel current)
    {
        if (candidate == null)
            return false;
        return current == null || GetPanelSortingOrder(candidate) > GetPanelSortingOrder(current);
    }

    private static int GetPanelSortingOrder(BasePanel panel)
    {
        Canvas canvas = panel.GetComponentInParent<Canvas>();
        while (canvas != null && !canvas.isRootCanvas && !canvas.overrideSorting)
            canvas = canvas.transform.parent?.GetComponentInParent<Canvas>();
        return canvas != null ? canvas.sortingOrder : GameplayHudSortingOrder;
    }

    public static BasePanel FindTopmostCancelPanel(
        Transform root,
        BasePanel excludedPanel = null)
    {
        if (root == null)
            return null;

        BasePanel topmost = null;
        for (int childIndex = root.childCount - 1; childIndex >= 0; childIndex--)
        {
            Transform child = root.GetChild(childIndex);
            BasePanel[] childPanels = child.GetComponentsInChildren<BasePanel>(true);
            for (int panelIndex = childPanels.Length - 1; panelIndex >= 0; panelIndex--)
            {
                BasePanel panel = childPanels[panelIndex];
                if (panel == null || panel == excludedPanel || !panel.IsCancelShortcutTarget)
                    continue;

                if (IsPanelAbove(panel, topmost))
                    topmost = panel;
            }
        }

        return topmost;
    }
    #endregion

    #region 面板创建和销毁

    public BasePanel CreatePanelFromGameObject(
        GameObject panelPrefab,
        string panelName = "",
        bool initializeClosed = false,
        Transform parent = null)
    {
        if (_isShuttingDown)
            return null;
        // 普通面板默认随场景销毁，只有明确传入跨场景根的玩家界面才保留。
        GameObject panelInstance = Instantiate(panelPrefab, parent != null ? parent : ScenePanelRoot);
        NormalizeCanvasLayers(panelInstance.transform);

        BasePanel _basePanel = panelInstance.GetComponent<BasePanel>();
        if (_basePanel == null)
            _basePanel = panelInstance.AddComponent<BasePanel>();

        string baseName = string.IsNullOrEmpty(panelName) ? panelPrefab.name : panelName;
        
        // 先清理空引用，再按有效面板数量计数，避免命名后缀冲突。
        int count = 0;
        if (panels.TryGetValue(baseName, out List<BasePanel> existingPanels))
        {
            existingPanels.RemoveAll(panel => panel == null);
            count = existingPanels.Count;
        }

        // 根据已存在的数量添加后缀
        string finalName = count > 0 ? $"{baseName}_{count}" : baseName;
        
        panelInstance.name = finalName;
        _basePanel.PanelName = finalName;

        // 需要首次 Open 触发生命周期事件的面板，必须在注册前明确初始化为关闭态。
        if (initializeClosed)
            _basePanel.InitClosed();
        else
            _basePanel.Init();

        // 设置子页使用独立高层 Canvas；主世界设置面板由 Mod_SettingCanvas 在打开时显式配置。
        if (IsSettingsPanelName(baseName))
            ConfigureSettingsPanelLayer(_basePanel);

        //注册面板
        RegisterPanel(_basePanel, baseName);
        return _basePanel;
    }

    /// <summary>玩家模块显式使用跨场景 UI 根，生命周期仍由所属模块清理。</summary>
    public BasePanel CreatePersistentPanelFromGameObject(
        GameObject panelPrefab,
        string panelName = "",
        bool initializeClosed = false)
    {
        EnsurePanelRootExists();
        return CreatePanelFromGameObject(panelPrefab, panelName, initializeClosed, panelRoot);
    }

    public void DestroyPanel(string panelName)
    {
        foreach (var kvp in panels)
        {
            var panelList = kvp.Value;
            for (int i = panelList.Count - 1; i >= 0; i--)
            {
                if (panelList[i] != null && panelList[i].PanelName == panelName)
                {
                    panelList[i].gameObject.SetActive(false);
                    Destroy(panelList[i].gameObject);
                    panelList.RemoveAt(i);
                    NotifyInteractionSurfaceChanged();
                    break;
                }
            }
        }
    }

    public void DestroyPanel(BasePanel panel)
    {
        if (panel == null)
            return;
        // 面板归属按实例注销，避免重命名或跨世界注册变化导致旧 UI 漏销毁。
        foreach (List<BasePanel> panelList in panels.Values)
            panelList.Remove(panel);
        // Unity 延迟销毁时先移出交互层，避免同一帧的新玩家被旧窗口夺取射线。
        panel.gameObject.SetActive(false);
        Destroy(panel.gameObject);
        NotifyInteractionSurfaceChanged();
    }

    public void DestroyAllPanelsOfType(string baseName)
    {
        if (panels.TryGetValue(baseName, out List<BasePanel> panelList))
        {
            for (int i = panelList.Count - 1; i >= 0; i--)
            {
                if (panelList[i] != null && panelList[i].gameObject != null)
                {
                    panelList[i].gameObject.SetActive(false);
                    Destroy(panelList[i].gameObject);
                }
            }
            panelList.Clear();
            NotifyInteractionSurfaceChanged();
        }
    }

    /// <summary>
    /// F5 资源热更新提交后清空全部运行时 UI 实例，但保留 UIRoot / SafeAreaRoot 本身。
    /// 下一轮玩家模块和全局 HUD 必须从最新 GameRes Prefab 重新实例化，禁止继续复用旧资源会话中的实例。
    /// </summary>
    public void DestroyRuntimeUiInstancesForResourceReload()
    {
        EnsurePanelRootExists();

        presentationCanvasStates.Clear();
        presentationRaycasterStates.Clear();
        panels.Clear();

        DestroyRuntimeUiChildren(rootCanvas, safeAreaRoot);
        foreach (RectTransform sceneRoot in scenePanelRoots.Values)
        {
            if (sceneRoot != null)
                DestroyRuntimeUiChildren(sceneRoot.GetComponentInParent<Canvas>(), sceneRoot);
        }

        nextSettingsPanelSortingOrder = SettingsPanelSortingOrder;
        panelQueryBuffer.Clear();
        panelQueryCacheRoot = null;
        panelQueryCacheRevision = int.MinValue;
        cachedTopmostGamepadPanel = null;
        cachedTopmostGameplayInputPanel = null;
        cachedTopmostCancelPanel = null;
        NotifyInteractionSurfaceChanged();
    }

    private void DestroyRuntimeUiChildren(Canvas canvas, Transform safeRoot)
    {
        if (safeRoot != null)
        {
            for (int i = safeRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = safeRoot.GetChild(i);
                if (child != null)
                    Destroy(child.gameObject);
            }
        }

        if (canvas != null)
        {
            Transform root = canvas.transform;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child == null || child == safeRoot)
                    continue;

                Destroy(child.gameObject);
            }
        }
    }
    #endregion

    #region 面板管理
    public void RegisterPanel(BasePanel panel, string baseName)
    {
        if (!panels.ContainsKey(baseName))
        {
            panels[baseName] = new List<BasePanel>();
        }
        panels[baseName].Add(panel);
        NotifyInteractionSurfaceChanged();
    }

    public void RefreshPanels()
    {
        InitializePanels();
    }

    public List<string> GetAllPanelNames()
    {
        return new List<string>(panels.Keys);
    }
    #endregion

    #region 标签操作
    public List<BasePanel> GetPanelsByTag(string tag)
    {
        List<BasePanel> taggedPanels = new List<BasePanel>();

        foreach (var panelList in panels.Values)
        {
            foreach (var panel in panelList)
            {
                if (panel != null && panel.gameObject.CompareTag(tag))
                {
                    taggedPanels.Add(panel);
                }
            }
        }

        return taggedPanels;
    }

    public void ShowPanelsByTag(string tag)
    {
        List<BasePanel> taggedPanels = GetPanelsByTag(tag);
        foreach (BasePanel panel in taggedPanels)
        {
            panel.Open();
        }
    }

    public void HidePanelsByTag(string tag)
    {
        List<BasePanel> taggedPanels = GetPanelsByTag(tag);
        foreach (BasePanel panel in taggedPanels)
        {
            panel.Close();
        }
    }
    #endregion
}
