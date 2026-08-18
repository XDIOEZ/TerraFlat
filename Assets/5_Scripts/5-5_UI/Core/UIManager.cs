using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 管理 FlatWorld 的统一 PanelRoot、面板实例与手柄交互面版本。
/// 根 Canvas 只在创建或失效时解析；交互面修订号供虚拟光标按需刷新命中结果。
/// </summary>
public class UIManager : MonoBehaviour
{
    #region 单例模式
    private static UIManager _instance;
    public static UIManager Instance
    {
        get
        {
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

    public Transform panelRoot;
    public GameObject panelRootPrefab;
    public GameObject[] panelPrefabs;
    public const int GameplayHudSortingOrder = 0;
    public const int HotbarModalSortingOrder = 1000;
    public const int HeldItemSortingOrder = 1001;
    /// <summary>设置面板的基础 Canvas 层级；高于玩法 HUD，低于加载和调试全屏覆盖层。</summary>
    public const int SettingsPanelSortingOrder = 2000;
    public const int GlobalOverlaySortingOrder = 32000;

    private Canvas rootCanvas;
    private RectTransform safeAreaRoot;
    private int nextSettingsPanelSortingOrder = SettingsPanelSortingOrder;
    private int interactionSurfaceRevision;
    private int lastHandledCancelFrame = -1;

    // 顶层面板查询只在交互面修订号变化时重新扫描，避免手柄路径逐帧分配数组。
    private readonly List<BasePanel> panelQueryBuffer = new List<BasePanel>(16);
    private Transform panelQueryCacheRoot;
    private int panelQueryCacheRevision = int.MinValue;
    private BasePanel cachedTopmostGamepadPanel;
    private BasePanel cachedTopmostGameplayInputPanel;
    private BasePanel cachedTopmostCancelPanel;

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

    /// <summary>面板开关、排序或结构改变时递增，供虚拟光标判断是否需要重新射线。</summary>
    public int InteractionSurfaceRevision => interactionSurfaceRevision;
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

        canvas.overrideSorting = true;
        if (RootCanvas != null)
            canvas.sortingLayerID = RootCanvas.sortingLayerID;

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

        EnsurePanelRootExists();
        if (rootCanvas == null)
            return;

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        if (panelRect == null)
            return;

        panelRect.SetParent(rootCanvas.transform, false);
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = Vector2.zero;

        Canvas canvas = panel.GetComponent<Canvas>();
        if (canvas == null)
            canvas = panel.gameObject.AddComponent<Canvas>();

        canvas.overrideSorting = true;
        canvas.sortingLayerID = rootCanvas.sortingLayerID;
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
    }

    private void EnsurePanelRootExists()
    {
        if (panelRoot != null)
        {
            CacheRootCanvas();
            ResolveSafeAreaRoot();
            UIScaleController.Ensure(rootCanvas != null ? rootCanvas.transform : panelRoot);
            return;
        }

        GameObject existingPanelRoot = GameObject.Find("PanelRoot");
        if (existingPanelRoot != null)
        {
            panelRoot = existingPanelRoot.transform;
            CacheRootCanvas();
            ResolveSafeAreaRoot();
            UIScaleController.Ensure(rootCanvas != null ? rootCanvas.transform : panelRoot);
            return;
        }

        GameObject rootPrefab = panelRootPrefab != null
            ? panelRootPrefab
            : Resources.Load<GameObject>("UI/UIRoot");
        if (rootPrefab == null)
        {
            throw new System.InvalidOperationException(
                "[UIManager] 缺少 Assets/Resources/UI/UIRoot.prefab，禁止运行时程序化创建 Canvas。");
        }

        GameObject canvasObj = Instantiate(rootPrefab);
        canvasObj.name = "PanelRoot";
        panelRoot = canvasObj.transform;
        CacheRootCanvas();
        ResolveSafeAreaRoot();
        UIScaleController.Ensure(rootCanvas != null ? rootCanvas.transform : panelRoot);
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

    #region 面板获取
    public BasePanel GetPanel(string panelName)
    {
        if (panels.TryGetValue(panelName, out List<BasePanel> panelList) && panelList.Count > 0)
        {
            return panelList[0];
        }

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
            panel = FindTopmostCancelPanel(panelRoot, excludedPanel);
        if (panel == null)
            return false;

        NotifyCancelHandled();
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
        panelQueryBuffer.Clear();

        if (panelRoot != null)
        {
            for (int childIndex = panelRoot.childCount - 1; childIndex >= 0; childIndex--)
            {
                Transform child = panelRoot.GetChild(childIndex);
                child.GetComponentsInChildren<BasePanel>(true, panelQueryBuffer);
                for (int panelIndex = panelQueryBuffer.Count - 1; panelIndex >= 0; panelIndex--)
                {
                    BasePanel panel = panelQueryBuffer[panelIndex];
                    if (panel == null)
                        continue;

                    if (cachedTopmostGamepadPanel == null &&
                        panel.IsOpen() && panel.IsGamepadNavigationPrepared)
                    {
                        cachedTopmostGamepadPanel = panel;
                    }

                    if (cachedTopmostGameplayInputPanel == null && panel.IsGameplayInputBlocking)
                        cachedTopmostGameplayInputPanel = panel;

                    if (cachedTopmostCancelPanel == null && panel.IsCancelShortcutTarget)
                        cachedTopmostCancelPanel = panel;
                }

                panelQueryBuffer.Clear();
                if (cachedTopmostGamepadPanel != null &&
                    cachedTopmostGameplayInputPanel != null &&
                    cachedTopmostCancelPanel != null)
                    break;
            }
        }

        panelQueryCacheRoot = panelRoot;
        panelQueryCacheRevision = interactionSurfaceRevision;
        PanelQueryCacheRebuildCount++;
    }

    public static BasePanel FindTopmostCancelPanel(
        Transform root,
        BasePanel excludedPanel = null)
    {
        if (root == null)
            return null;

        for (int childIndex = root.childCount - 1; childIndex >= 0; childIndex--)
        {
            Transform child = root.GetChild(childIndex);
            BasePanel[] childPanels = child.GetComponentsInChildren<BasePanel>(true);
            for (int panelIndex = childPanels.Length - 1; panelIndex >= 0; panelIndex--)
            {
                BasePanel panel = childPanels[panelIndex];
                if (panel == null || panel == excludedPanel || !panel.IsCancelShortcutTarget)
                    continue;

                return panel;
            }
        }

        return null;
    }
    #endregion

    #region 面板创建和销毁

    public BasePanel CreatePanelFromGameObject(GameObject panelPrefab, string panelName = "")
    {
        EnsurePanelRootExists();

        GameObject panelInstance = Instantiate(panelPrefab, panelRoot);
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

        //初始化面板
        _basePanel.Init();

        // 设置子页使用独立高层 Canvas；主世界设置面板由 SettingCanvas 在打开时显式配置。
        if (IsSettingsPanelName(baseName))
            ConfigureSettingsPanelLayer(_basePanel);

        //注册面板
        RegisterPanel(_basePanel, baseName);
        return _basePanel;
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
        string baseName = panel.PanelName;
        // 移除后缀以获取基础名称
        int underscoreIndex = baseName.LastIndexOf('_');
        if (underscoreIndex > 0 && int.TryParse(baseName.Substring(underscoreIndex + 1), out _))
        {
            baseName = baseName.Substring(0, underscoreIndex);
        }

        if (panels.TryGetValue(baseName, out List<BasePanel> panelList))
        {
            panelList.Remove(panel);
            if (panel != null && panel.gameObject != null)
            {
                Destroy(panel.gameObject);
            }
            NotifyInteractionSurfaceChanged();
        }
    }

    public void DestroyAllPanelsOfType(string baseName)
    {
        if (panels.TryGetValue(baseName, out List<BasePanel> panelList))
        {
            for (int i = panelList.Count - 1; i >= 0; i--)
            {
                if (panelList[i] != null && panelList[i].gameObject != null)
                {
                    Destroy(panelList[i].gameObject);
                }
            }
            panelList.Clear();
            NotifyInteractionSurfaceChanged();
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
