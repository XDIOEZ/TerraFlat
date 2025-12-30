using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    // 单例实例
    private static UIManager _instance;
    public static UIManager Instance
    {
        get
        {
            // 如果实例不存在，尝试查找
            if (_instance == null)
            {
                _instance = FindObjectOfType<UIManager>();

                // 如果还是找不到，创建一个新的
                if (_instance == null)
                {
                    GameObject singletonObject = new GameObject("UIManager");
                    _instance = singletonObject.AddComponent<UIManager>();
                }
            }
            return _instance;
        }
    }

    // 存储所有面板的字典
    [ShowInInspector]
    public Dictionary<string, BasePanel> panels = new Dictionary<string, BasePanel>();


    // 面板的父对象
    public Transform panelRoot;

    // panelRoot的预制体，可在Inspector中挂接
    public GameObject panelRootPrefab;

    // 预制体引用（可选）
    public GameObject[] panelPrefabs;

    private void Awake()
    {
        // 确保只有一个UIManager实例
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

        // 初始化面板字典
        InitializePanels();

        // 确保panelRoot存在
        EnsurePanelRootExists();
    }

    /// <summary>
    /// 确保panelRoot存在，如果不存在则在当前激活场景中创建
    /// </summary>
    private void EnsurePanelRootExists()
    {
        // 如果panelRoot已经存在，直接返回
        if (panelRoot != null)
            return;

        // 尝试在场景中查找现有的PanelRoot
        GameObject existingPanelRoot = GameObject.Find("PanelRoot");
        if (existingPanelRoot != null)
        {
            panelRoot = existingPanelRoot.transform;
            return;
        }

        // 创建新的panelRoot
        GameObject canvasObj;

        // 如果提供了panelRootPrefab，优先使用预制体实例化
        if (panelRootPrefab != null)
        {
            canvasObj = Instantiate(panelRootPrefab);
            canvasObj.name = "PanelRoot";
        }
        else
        {
            // 否则创建默认的Canvas对象
            canvasObj = new GameObject("PanelRoot");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // 添加CanvasScaler组件
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // 添加GraphicRaycaster组件
            canvasObj.AddComponent<GraphicRaycaster>();
        }

        // 确保panelRoot不会被销毁（除非场景切换）
        // 注意：我们不使用DontDestroyOnLoad，这样它会随着场景切换而销毁
        panelRoot = canvasObj.transform;

        //        Debug.Log("PanelRoot created in current active scene.");
    }

    /// <summary>
    /// 初始化所有面板
    /// </summary>
    private void InitializePanels()
    {
        // 确保panelRoot存在
        EnsurePanelRootExists();

        panels.Clear();

        // 查找场景中所有的BasePanel组件
        /* BasePanel[] allPanels = FindObjectsOfType<BasePanel>(true);
         foreach (BasePanel panel in allPanels)
         {
             if (!panels.ContainsKey(panel.name))
             {
                 panels[panel.name] = panel;
             }
             else
             {
                 // 如果存在同名面板，添加警告
                 Debug.LogWarning($"Duplicate panel name found: {panel.name}");
             }
         }*/
    }

    /// <summary>
    /// 获取指定名称的面板
    /// </summary>
    /// <param name="panelName">面板名称</param>
    /// <returns>BasePanel组件，如果不存在返回null</returns>
    public BasePanel GetPanel(string panelName)
    {
        if (panels.TryGetValue(panelName, out BasePanel panel))
        {
            return panel;
        }

        Debug.LogWarning($"Panel '{panelName}' not found!");
        return null;
    }

    /// <summary>
    /// 显示指定面板
    /// </summary>
    /// <param name="panelName">面板名称</param>
    public void ShowPanel(string panelName)
    {
        BasePanel panel = GetPanel(panelName);
        if (panel != null)
        {
            panel.Open();
        }
    }

    /// <summary>
    /// 隐藏指定面板
    /// </summary>
    /// <param name="panelName">面板名称</param>
    public void HidePanel(string panelName)
    {
        BasePanel panel = GetPanel(panelName);
        if (panel != null)
        {
            panel.Close();
        }
    }

    /// <summary>
    /// 切换面板显示状态
    /// </summary>
    /// <param name="panelName">面板名称</param>
    public void TogglePanel(string panelName)
    {
        BasePanel panel = GetPanel(panelName);
        if (panel != null)
        {
            panel.Toggle();
        }
    }

    /// <summary>
    /// 检查面板是否打开
    /// </summary>
    /// <param name="panelName">面板名称</param>
    /// <returns>面板是否打开</returns>
    public bool IsPanelOpen(string panelName)
    {
        BasePanel panel = GetPanel(panelName);
        if (panel != null)
        {
            return panel.IsOpen();
        }
        return false;
    }

    /// <summary>
    /// 隐藏所有面板
    /// </summary>
    public void HideAllPanels()
    {
        foreach (var panel in panels.Values)
        {
            panel.Close();
        }
    }

    /// <summary>
    /// 显示所有面板
    /// </summary>
    public void ShowAllPanels()
    {
        foreach (var panel in panels.Values)
        {
            panel.Open();
        }
    }

    /// <summary>
    /// 通过预制体创建新面板
    /// </summary>
    /// <param name="panelPrefabName">面板预制体名称</param>
    /// <param name="parent">父对象</param>
    /// <returns>创建的面板</returns>
    public BasePanel CreatePanel(string panelPrefabName, Transform parent = null)
    {
        // 查找预制体
        GameObject panelPrefab = null;
        foreach (GameObject prefab in panelPrefabs)
        {
            if (prefab != null && prefab.name == panelPrefabName)
            {
                panelPrefab = prefab;
                break;
            }
        }

        if (panelPrefab == null)
        {
            Debug.LogWarning($"Panel prefab '{panelPrefabName}' not found!");
            return null;
        }

        // 确保panelRoot存在
        EnsurePanelRootExists();

        // 创建面板实例
        Transform parentTransform = parent != null ? parent : panelRoot;
        GameObject panelInstance = Instantiate(panelPrefab, parentTransform);

        // 获取BasePanel组件
        BasePanel panel = panelInstance.GetComponent<BasePanel>();
        if (panel != null)
        {
            // 添加到字典中
            if (!panels.ContainsKey(panelInstance.name))
            {
                panels[panelInstance.name] = panel;
            }
            return panel;
        }
        else
        {
            Debug.LogWarning($"Panel prefab '{panelPrefabName}' does not have a BasePanel component!");
            Destroy(panelInstance);
            return null;
        }
    }

    /// <summary>
    /// 销毁指定面板
    /// </summary>
    /// <param name="panelName">面板名称</param>
    public void DestroyPanel(string panelName)
    {
        if (panels.TryGetValue(panelName, out BasePanel panel))
        {
            panels.Remove(panelName);
            if (panel != null && panel.gameObject != null)
            {
                Destroy(panel.gameObject);
            }
        }
    }

    /// <summary>
    /// 销毁指定面板
    /// </summary>
    /// <param name="panelName">面板名称</param>
    public void DestroyPanel(BasePanel panel)
    {
        if (panels.TryGetValue(panel.PanelName, out BasePanel existingPanel))
        {
            panels.Remove(panel.PanelName);
            if (existingPanel != null && existingPanel.gameObject != null)
            {
                Destroy(existingPanel.gameObject);
            }
        }
    }

    /// <summary>
    /// 刷新面板列表（当动态添加面板时调用）
    /// </summary>
    public void RefreshPanels()
    {
        InitializePanels();
    }

    /// <summary>
    /// 获取所有面板名称
    /// </summary>
    /// <returns>面板名称列表</returns>
    public List<string> GetAllPanelNames()
    {
        return new List<string>(panels.Keys);
    }

    /// <summary>
    /// 设置面板的可见性
    /// </summary>
    /// <param name="panelName">面板名称</param>
    /// <param name="isVisible">是否可见</param>
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
    /// 获取指定标签的面板列表
    /// </summary>
    /// <param name="tag">标签名称</param>
    /// <returns>匹配标签的面板列表</returns>
    public List<BasePanel> GetPanelsByTag(string tag)
    {
        List<BasePanel> taggedPanels = new List<BasePanel>();

        foreach (var panel in panels.Values)
        {
            if (panel != null && panel.gameObject.CompareTag(tag))
            {
                taggedPanels.Add(panel);
            }
        }

        return taggedPanels;
    }

    /// <summary>
    /// 显示指定标签的所有面板
    /// </summary>
    /// <param name="tag">标签名称</param>
    public void ShowPanelsByTag(string tag)
    {
        List<BasePanel> taggedPanels = GetPanelsByTag(tag);
        foreach (BasePanel panel in taggedPanels)
        {
            panel.Open();
        }
    }

    /// <summary>
    /// 隐藏指定标签的所有面板
    /// </summary>
    /// <param name="tag">标签名称</param>
    public void HidePanelsByTag(string tag)
    {
        List<BasePanel> taggedPanels = GetPanelsByTag(tag);
        foreach (BasePanel panel in taggedPanels)
        {
            panel.Close();
        }
    }

    /// <summary>
    /// 注册面板到UIManager
    /// </summary>
    /// <param name="panel">要注册的面板</param>
    public void RegisterPanel(BasePanel panel)
    {
        panels[panel.PanelName] = panel;
    }

    /// <summary>
    /// 通过GameObject实例化面板对象
    /// </summary>
    /// <param name="panelPrefab">面板预制体</param>
    /// <returns>实例化的面板组件</returns>
    public BasePanel CreatePanelFromGameObject(GameObject panelPrefab, string panelName = "")
    {
        if (panelPrefab == null)
        {
            Debug.LogWarning("Panel prefab cannot be null!");
            return null;
        }



        // 确保panelRoot存在
        EnsurePanelRootExists();

        // 实例化面板对象并设置父对象
        Transform parentTransform = panelRoot;
        GameObject panelInstance = Instantiate(panelPrefab, parentTransform);


        // 获取BasePanel组件
        BasePanel panel = panelInstance.GetComponent<BasePanel>();

        if (!string.IsNullOrEmpty(panelName))
        {
            panelInstance.name = panelName;
            panel.PanelName = panelName;
        }


        if (panel != null)
        {
            // 自动注册面板
            RegisterPanel(panel);
            return panel;
        }
        else
        {
            Debug.LogWarning($"Panel prefab '{panelPrefab.name}' does not have a BasePanel component!");
            Destroy(panelInstance);
            return null;
        }

    }
}