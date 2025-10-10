using MemoryPack;
using Newtonsoft.Json;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 定义模块面板接口
public interface IModulePanel
{
    void ShowPanel();
    void HidePanel();
    void RefreshUI();
    bool IsPanelVisible(); // 添加检查面板是否可见的方法
}

public partial class Mod_UI_CanvasManager : Module
{
    #region 数据结构定义
    [ShowInInspector]
    public Dictionary<string, BasePanel> panelRegistry = new();
    public Dictionary<string, UI_Drag> dragRegistry = new();
    private Dictionary<string, TextMeshProUGUI> panelButtonTexts = new();
    
    // 添加模块面板注册表
    private Dictionary<string, IModulePanel> modulePanelRegistry = new();

    [Header("UI按钮设置")]
    public GameObject UIOpen_Parent; // 用于动态生成的按钮
    public GameObject PanelOpenButtonPrefab; // 指定按钮预制体（可在Inspector中设置）

    CanvasSaveData canvasPanelState = new CanvasSaveData();

    [Serializable]
    [MemoryPackable]
    public partial class CanvasSaveData
    {
        [ShowInInspector]
        public Dictionary<string, bool> canvasPanelboolStates = new();
        public Dictionary<string, Vector2> DraggerPos = new(); // 拖拽位置数据
    }
    #endregion

    #region 基础字段与属性
    public override ModuleData _Data { get => exData; set => exData = (Ex_ModData_MemoryPackable)value; }
    public Ex_ModData_MemoryPackable exData = new();
    #endregion

    #region Unity生命周期
    public override void Load()
    {
        exData.ReadData(ref canvasPanelState);

        // 注册所有 UI_Drag
        var allDraggers = item.GetComponentsInChildren<UI_Drag>(true);
        foreach (var dragger in allDraggers)
        {
            string draggerName = dragger.gameObject.name;
            dragRegistry[draggerName] = dragger;

            // 注意：不在此处恢复UI_Drag的位置，让各个模块自己管理自己的位置
            // 只有当位置数据存在且面板不是模块面板时才恢复位置
            if (canvasPanelState.DraggerPos.TryGetValue(draggerName, out var pos))
            {
                // 检查这个dragger是否属于模块面板
                bool isModulePanelDragger = IsPartOfModulePanel(dragger.transform);
                
                // 只有非模块面板的dragger才由CanvasManager管理位置
                if (!isModulePanelDragger)
                {
                    Debug.Log($"[CanvasManager] 恢复拖拽组件位置: {draggerName} 从 {dragger.rectTransform.anchoredPosition} 到 {pos}");
                    dragger.rectTransform.anchoredPosition = pos;
                    Debug.Log($"[CanvasManager] 恢复后位置: {draggerName} = {dragger.rectTransform.anchoredPosition}");
                }
            }
        }

        // 注册所有面板（排除自身）
        var allPanels = item.GetComponentsInChildren<BasePanel>(true);
        foreach (var panel in allPanels)
        {
            // 跳过自身面板，避免自己控制自己导致的问题
            if (panel.gameObject == gameObject)
                continue;

            string panelName = panel.gameObject.name;

            // 注册可拖拽面板的拖拽组件
            if (panel.CanDrag && panel.Dragger != null)
                dragRegistry[panelName] = panel.Dragger;

            // 添加到面板注册表
            if (!panelRegistry.ContainsKey(panelName))
                panelRegistry.Add(panelName, panel);

            // 检查这个面板是否属于模块面板
            bool isModulePanel = IsPartOfModulePanel(panel.transform);
            
            // 只有非模块面板才由CanvasManager管理状态
            if (!isModulePanel)
            {
                if (canvasPanelState.canvasPanelboolStates.TryGetValue(panelName, out var isOpen))
                {
                    if (isOpen) panel.Open();
                    else panel.Close();
                }

                // 恢复位置
                if (canvasPanelState.DraggerPos.TryGetValue(panelName, out var pos))
                {
                    if (panel.CanDrag && panel.Dragger != null)
                    {
                        var dragRT = panel.Dragger.GetComponent<RectTransform>();
                        if (dragRT != null)
                        {
                            Debug.Log($"[CanvasManager] 恢复面板拖拽位置: {panelName} 从 {dragRT.anchoredPosition} 到 {pos}");
                            dragRT.anchoredPosition = pos;
                            Debug.Log($"[CanvasManager] 恢复后位置: {panelName} = {dragRT.anchoredPosition}");
                        }
                    }
                    else
                    {
                        var selfRT = panel.GetComponent<RectTransform>();
                        if (selfRT != null)
                        {
                            Debug.Log($"[CanvasManager] 恢复面板位置: {panelName} 从 {selfRT.anchoredPosition} 到 {pos}");
                            selfRT.anchoredPosition = pos;
                            Debug.Log($"[CanvasManager] 恢复后位置: {panelName} = {selfRT.anchoredPosition}");
                        }
                    }
                }
            }
        }

        // 注册所有模块面板
        RegisterModulePanels();

        // 生成控制按钮（菜单）
        GenerateControlButtons();

        // 在按钮生成完成后，同步检查所有面板的实际状态并更新按钮颜色
        SyncAllButtonVisuals();
    }

    [Button("保存")]
    public override void Save()
    {
        // 清理之前状态，防止重复/冲突
        canvasPanelState.canvasPanelboolStates.Clear();
        canvasPanelState.DraggerPos.Clear();

        // 保存所有注册面板的状态（但不保存模块面板的状态）
        foreach (var pair in panelRegistry)
        {
            string panelName = pair.Key;
            BasePanel panel = pair.Value;

            // 检查面板是否为null避免空引用
            if (panel == null)
                continue;

            // 检查这个面板是否属于模块面板，如果是则跳过
            bool isModulePanel = IsPartOfModulePanel(panel.transform);
            
            if (isModulePanel)
                continue;

            // 保存开关状态
            canvasPanelState.canvasPanelboolStates[panelName] = panel.IsOpen();

            // 保存位置，判断是否可以拖拽
            if (panel.CanDrag && panel.Dragger != null)
            {
                var dragRT = panel.Dragger.GetComponent<RectTransform>();
                if (dragRT != null)
                    canvasPanelState.DraggerPos[panelName] = dragRT.anchoredPosition;
            }
            else
            {
                var selfRT = panel.GetComponent<RectTransform>();
                if (selfRT != null)
                    canvasPanelState.DraggerPos[panelName] = selfRT.anchoredPosition;
            }
        }

        // 保存所有模块面板的状态
        foreach (var pair in modulePanelRegistry)
        {
            string moduleName = pair.Key;
            IModulePanel modulePanel = pair.Value;

            // 保存开关状态
            canvasPanelState.canvasPanelboolStates[moduleName] = modulePanel.IsPanelVisible();
            // 注意：不保存模块面板的位置，让模块自己管理自己的位置
        }

        exData.WriteData(canvasPanelState);
    }
    #endregion

    #region 面板注册与管理
    /// <summary>
    /// 检查指定的Transform是否属于模块面板
    /// </summary>
    /// <param name="transform">要检查的Transform</param>
    /// <returns>是否属于模块面板</returns>
    private bool IsPartOfModulePanel(Transform transform)
    {
        // 遍历所有模块，检查这个transform是否在模块的子对象中
        foreach (var modulePair in item.itemMods.Mods)
        {
            if (modulePair.Value is IModulePanel)
            {
                // 获取模块的GameObject
                GameObject moduleGO = modulePair.Value.GetType().GetField("PanleInstance")?.GetValue(modulePair.Value) as GameObject;
                if (moduleGO != null && (transform == moduleGO.transform || transform.IsChildOf(moduleGO.transform)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 注册所有实现了IModulePanel接口的模块
    /// </summary>
    private void RegisterModulePanels()
    {
        modulePanelRegistry.Clear();
        
        // 遍历所有模块，查找实现了IModulePanel接口的模块
        foreach (var modulePair in item.itemMods.Mods)
        {
            if (modulePair.Value is IModulePanel modulePanel)
            {
                string moduleName = modulePair.Key;
                modulePanelRegistry[moduleName] = modulePanel;
                
                // 注意：不在此处恢复模块面板的状态，让模块自己管理自己的状态
                // 模块面板的状态恢复应该在模块自己的Load方法中处理
            }
        }
    }
    #endregion

    #region 控制按钮生成与管理
    /// <summary>
    /// 生成控制按钮
    /// </summary>
    private void GenerateControlButtons()
    {
        // 检查必要组件
        if (UIOpen_Parent == null || PanelOpenButtonPrefab == null)
        {
            Debug.LogWarning("[CanvasManager] 缺少必要的UI组件，无法生成控制按钮");
            return;
        }

        // 清理现有按钮
        foreach (Transform child in UIOpen_Parent.transform)
            Destroy(child.gameObject);

        panelButtonTexts.Clear();

        // 为每个注册的面板生成控制按钮
        foreach (var panelName in panelRegistry.Keys)
        {
            // 检查这个面板是否属于模块面板，如果是则跳过（避免重复控制）
            bool isModulePanel = false;
            foreach (var kv in panelRegistry)
            {
                if (kv.Key == panelName && IsPartOfModulePanel(kv.Value.transform))
                {
                    isModulePanel = true;
                    break;
                }
            }
            
            if (!isModulePanel)
            {
                GenerateButtonForPanel(panelName, false); // false表示这不是模块面板
            }
        }

        // 为每个注册的模块面板生成控制按钮
        foreach (var moduleName in modulePanelRegistry.Keys)
        {
            GenerateButtonForPanel(moduleName, true); // true表示这是模块面板
        }
    }

    /// <summary>
    /// 为面板生成按钮
    /// </summary>
    /// <param name="panelName">面板名称</param>
    /// <param name="isModulePanel">是否为模块面板</param>
    private void GenerateButtonForPanel(string panelName, bool isModulePanel)
    {
        // 检查预制体是否存在
        if (PanelOpenButtonPrefab == null)
        {
            Debug.LogError($"[CanvasManager] PanelOpenButtonPrefab 未设置，无法为 {panelName} 生成按钮");
            return;
        }

        var btnGO = Instantiate(PanelOpenButtonPrefab, UIOpen_Parent.transform);
        btnGO.name = $"Btn_{panelName}";

        // 设置按钮文本
        var tmpText = btnGO.GetComponentInChildren<TextMeshProUGUI>();
        if (tmpText != null)
        {
            tmpText.text = panelName;
            panelButtonTexts[panelName] = tmpText;
        }

        // 设置按钮点击事件
        var button = btnGO.GetComponent<UnityEngine.UI.Button>();
        if (button != null)
        {
            string capturedName = panelName;
            bool capturedIsModulePanel = isModulePanel;
            button.onClick.AddListener(() => TogglePanel(capturedName, capturedIsModulePanel));
        }
    }

    /// <summary>
    /// 同步所有按钮的视觉状态
    /// </summary>
    private void SyncAllButtonVisuals()
    {
        // 同步BasePanel按钮状态
        foreach (var kv in panelRegistry)
        {
            string panelName = kv.Key;
            BasePanel panel = kv.Value;
            
            // 检查这个面板是否属于模块面板，如果是则跳过
            bool isModulePanel = IsPartOfModulePanel(panel.transform);
            
            if (!isModulePanel)
            {
                // 根据面板实际状态更新按钮颜色
                bool isPanelOpen = panel.IsOpen();
                UpdateButtonVisual(panelName, isPanelOpen);
            }
        }

        // 同步模块面板按钮状态
        foreach (var kv in modulePanelRegistry)
        {
            string moduleName = kv.Key;
            IModulePanel modulePanel = kv.Value;
            
            // 根据模块面板实际状态更新按钮颜色
            bool isPanelVisible = modulePanel.IsPanelVisible();
            UpdateButtonVisual(moduleName, isPanelVisible);
        }
    }

    /// <summary>
    /// 更新按钮视觉状态
    /// </summary>
    /// <param name="panelName">面板名称</param>
    /// <param name="isOpen">是否开启</param>
    private void UpdateButtonVisual(string panelName, bool isOpen)
    {
        if (panelButtonTexts.TryGetValue(panelName, out var tmpText))
        {
            tmpText.text = panelName;
            tmpText.color = isOpen ? Color.green : Color.red;
        }
    }

    /// <summary>
    /// 刷新按钮
    /// </summary>
    [Button("刷新按钮")]
    public void RefreshButtons()
    {
        GenerateControlButtons();
        SyncAllButtonVisuals();
    }
    #endregion

    #region 面板控制方法
    /// <summary>
    /// 切换面板开关状态
    /// </summary>
    /// <param name="name">面板名称</param>
    /// <param name="isModulePanel">是否为模块面板</param>
    public void TogglePanel(string name, bool isModulePanel = false)
    {
        if (isModulePanel)
        {
            // 处理模块面板
            if (modulePanelRegistry.TryGetValue(name, out var modulePanel))
            {
                bool isVisible = !modulePanel.IsPanelVisible();

                if (isVisible)
                    modulePanel.ShowPanel();
                else
                    modulePanel.HidePanel();

                // 更新状态数据
                canvasPanelState.canvasPanelboolStates[name] = isVisible;

                // 更新按钮视觉
                UpdateButtonVisual(name, isVisible);
            }
            else
            {
                Debug.LogWarning($"[CanvasManager] 未找到模块面板: {name}");
            }
        }
        else
        {
            // 处理BasePanel
            if (panelRegistry.TryGetValue(name, out var panel))
            {
                bool isOpen = !panel.IsOpen();

                if (isOpen)
                    panel.Open();
                else
                    panel.Close();

                // 更新状态数据
                canvasPanelState.canvasPanelboolStates[name] = isOpen;

                // 保存位置数据
                RectTransform rt = panel.CanDrag && panel.Dragger != null
                    ? panel.Dragger.GetComponent<RectTransform>()
                    : panel.GetComponent<RectTransform>();

                if (rt != null)
                    canvasPanelState.DraggerPos[name] = rt.anchoredPosition;

                // 更新按钮视觉
                UpdateButtonVisual(name, isOpen);
            }
            else
            {
                Debug.LogWarning($"[CanvasManager] 未找到面板: {name}");
            }
        }
    }

    /// <summary>
    /// 打开指定面板
    /// </summary>
    /// <param name="name">面板名称</param>
    /// <param name="isModulePanel">是否为模块面板</param>
    [Button("打开面板")]
    public void OpenPanel(string name, bool isModulePanel = false)
    {
        if (isModulePanel)
        {
            // 处理模块面板
            if (modulePanelRegistry.TryGetValue(name, out var modulePanel))
            {
                modulePanel.ShowPanel();
                canvasPanelState.canvasPanelboolStates[name] = true;
                UpdateButtonVisual(name, true);
            }
            else
            {
                Debug.LogWarning($"[CanvasManager] 未找到模块面板: {name}");
            }
        }
        else
        {
            // 处理BasePanel
            if (panelRegistry.TryGetValue(name, out var panel))
            {
                panel.Open();
                canvasPanelState.canvasPanelboolStates[name] = true;
                UpdateButtonVisual(name, true);
                
                // 保存位置数据
                RectTransform rt = panel.CanDrag && panel.Dragger != null
                    ? panel.Dragger.GetComponent<RectTransform>()
                    : panel.GetComponent<RectTransform>();

                if (rt != null)
                    canvasPanelState.DraggerPos[name] = rt.anchoredPosition;
            }
            else
            {
                Debug.LogWarning($"[CanvasManager] 未找到面板: {name}");
            }
        }
    }

    /// <summary>
    /// 关闭指定面板
    /// </summary>
    /// <param name="name">面板名称</param>
    /// <param name="isModulePanel">是否为模块面板</param>
    [Button("关闭面板")]
    public void ClosePanel(string name, bool isModulePanel = false)
    {
        if (isModulePanel)
        {
            // 处理模块面板
            if (modulePanelRegistry.TryGetValue(name, out var modulePanel))
            {
                modulePanel.HidePanel();
                canvasPanelState.canvasPanelboolStates[name] = false;
                UpdateButtonVisual(name, false);
            }
            else
            {
                Debug.LogWarning($"[CanvasManager] 未找到模块面板: {name}");
            }
        }
        else
        {
            // 处理BasePanel
            if (panelRegistry.TryGetValue(name, out var panel))
            {
                panel.Close();
                canvasPanelState.canvasPanelboolStates[name] = false;
                UpdateButtonVisual(name, false);
                
                // 保存位置数据
                RectTransform rt = panel.CanDrag && panel.Dragger != null
                    ? panel.Dragger.GetComponent<RectTransform>()
                    : panel.GetComponent<RectTransform>();

                if (rt != null)
                    canvasPanelState.DraggerPos[name] = rt.anchoredPosition;
            }
            else
            {
                Debug.LogWarning($"[CanvasManager] 未找到面板: {name}");
            }
        }
    }

    /// <summary>
    /// 获取面板状态
    /// </summary>
    /// <param name="name">面板名称</param>
    /// <param name="isModulePanel">是否为模块面板</param>
    /// <returns>面板是否开启</returns>
    public bool IsPanelOpen(string name, bool isModulePanel = false)
    {
        if (isModulePanel)
        {
            // 处理模块面板
            if (modulePanelRegistry.TryGetValue(name, out var modulePanel))
            {
                return modulePanel.IsPanelVisible();
            }
            return false;
        }
        else
        {
            // 处理BasePanel
            if (panelRegistry.TryGetValue(name, out var panel))
            {
                return panel.IsOpen();
            }
            return false;
        }
    }
    #endregion
}