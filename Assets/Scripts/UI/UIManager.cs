using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    }

    /// <summary>
    /// 初始化所有面板
    /// </summary>
    private void InitializePanels()
    {
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
        
        // 创建面板实例
        Transform parentTransform = parent != null ? parent : (panelRoot != null ? panelRoot : transform);
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
        if (panel != null && !panels.ContainsKey(panel.name))
        {
            panels[panel.name] = panel;
        }
        else if (panel != null && panels.ContainsKey(panel.name))
        {
            panels[panel.PanleName] = panel;
        }
    }
    
    /// <summary>
    /// 通过GameObject实例化面板对象
    /// </summary>
    /// <param name="panelPrefab">面板预制体</param>
    /// <returns>实例化的面板组件</returns>
    public BasePanel CreatePanelFromGameObject(GameObject panelPrefab)
    {
        if (panelPrefab == null)
        {
            Debug.LogWarning("Panel prefab cannot be null!");
            return null;
        }
        
        // 实例化面板对象并设置父对象
        Transform parentTransform = panelRoot != null ? panelRoot : transform;
        GameObject panelInstance = Instantiate(panelPrefab, parentTransform);
        
        // 获取BasePanel组件
        BasePanel panel = panelInstance.GetComponent<BasePanel>();
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