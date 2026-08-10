using Sirenix.OdinInspector;
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
    private Canvas rootCanvas;
    private int interactionSurfaceRevision;
    private int lastHandledCancelFrame = -1;

    /// <summary>缓存的 PanelRoot Canvas；仅在根节点失效或替换时重新解析。</summary>
    public Canvas RootCanvas
    {
        get
        {
            EnsurePanelRootExists();
            return rootCanvas;
        }
    }

    /// <summary>面板开关、排序或结构改变时递增，供虚拟光标判断是否需要重新射线。</summary>
    public int InteractionSurfaceRevision => interactionSurfaceRevision;
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
            UIScaleController.Ensure(panelRoot);
            return;
        }

        GameObject existingPanelRoot = GameObject.Find("PanelRoot");
        if (existingPanelRoot != null)
        {
            panelRoot = existingPanelRoot.transform;
            CacheRootCanvas();
            UIScaleController.Ensure(panelRoot);
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
        UIScaleController.Ensure(panelRoot);
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
            (rootCanvas.transform == panelRoot || rootCanvas.transform.IsChildOf(panelRoot)))
        {
            return;
        }

        Canvas nextCanvas = panelRoot.GetComponent<Canvas>() ??
                            panelRoot.GetComponentInChildren<Canvas>(true);
        if (rootCanvas == nextCanvas)
            return;

        rootCanvas = nextCanvas;
        NotifyInteractionSurfaceChanged();
        if (rootCanvas == null)
            Debug.LogError("[UIManager] PanelRoot 缺少 Canvas，虚拟光标无法绑定。", panelRoot);
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
    }

    /// <summary>
    /// 关闭最上层的临时面板。excludedPanel 通常是设置面板本身。
    /// </summary>
    public bool TryCloseTopmostCancelPanel(BasePanel excludedPanel = null)
    {
        EnsurePanelRootExists();
        BasePanel panel = FindTopmostCancelPanel(panelRoot, excludedPanel);
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
        EnsurePanelRootExists();
        BasePanel panel = FindTopmostGamepadPanel(panelRoot);
        if (panel == null)
            return false;

        panel.SelectDefaultForGamepad();
        return true;
    }

    /// <summary>判断当前是否存在打开且已接入手柄焦点导航的面板。</summary>
    public bool HasOpenGamepadNavigationPanel()
    {
        return panelRoot != null && FindTopmostGamepadPanel(panelRoot) != null;
    }

    /// <summary>判断当前是否存在打开且需要接管玩法输入的模态手柄面板。</summary>
    public bool HasOpenModalGamepadNavigationPanel()
    {
        if (panelRoot == null)
            return false;

        for (int childIndex = panelRoot.childCount - 1; childIndex >= 0; childIndex--)
        {
            Transform child = panelRoot.GetChild(childIndex);
            BasePanel[] childPanels = child.GetComponentsInChildren<BasePanel>(true);
            for (int panelIndex = childPanels.Length - 1; panelIndex >= 0; panelIndex--)
            {
                BasePanel panel = childPanels[panelIndex];
                if (panel != null && panel.IsCancelShortcutTarget)
                    return true;
            }
        }

        return false;
    }

    private static BasePanel FindTopmostGamepadPanel(Transform root)
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
                if (panel != null && panel.IsOpen() && panel.IsGamepadNavigationPrepared)
                    return panel;
            }
        }

        return null;
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
