using MemoryPack;
using Newtonsoft.Json;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public partial class Mod_UI_CanvasManager : Module
{
    [ShowInInspector]
    public Dictionary<string, BasePanel> panelRegistry = new();
    public Dictionary<string, UI_Drag> dragRegistry = new();
    private Dictionary<string, TextMeshProUGUI> panelButtonTexts = new();



    public GameObject UIOpen_Parent; // 用于动态生成的按钮
    public GameObject PanelOpenButtonPrefab; // 指定按钮预制体（可在Inspector中设置）


    CanvasSaveData canvasPanelState = new CanvasSaveData();

    [Serializable]
    [MemoryPackable]
    public partial class CanvasSaveData
    {
        [ShowInInspector]
        public Dictionary<string, bool> canvasPanelboolStates = new();
        public Dictionary<string,Vector2> DraggerPos = new(); // 修正：Drager -> Dragger
    }

    public override ModuleData _Data { get => exData; set => exData = (Ex_ModData_MemoryPackable)value; }
 
    public Ex_ModData_MemoryPackable exData = new();

/*    [Button("Open Panel")]
    public void OpenPanel(string name)
    {
        if (panelRegistry.TryGetValue(name, out var panel))
        {
            panel.Open();
            UpdateCanvasSaveData(name, true, panel);
        }
    }

    [Button("Close Panel")]
    public void ClosePanel(string name)
    {
        if (panelRegistry.TryGetValue(name, out var panel))
        {
            panel.Close();
            UpdateCanvasSaveData(name, false, panel);
        }
    }*/

    public override void Load()
    {
        exData.ReadData(ref canvasPanelState);

        // 注册所有 UI_Drag
        var allDragger = item.GetComponentsInChildren<UI_Drag>(true); // 修正：Drager -> Dragger
        foreach (var dragger in allDragger) // 修正：drager -> dragger
        {
            dragRegistry[dragger.gameObject.name] = dragger; // 修正：drager -> dragger

            if (canvasPanelState.DraggerPos.TryGetValue(dragger.gameObject.name, out var pos)) // 修正：Drager -> Dragger
            {
                dragger.rectTransform.anchoredPosition = pos; // 修正：drager -> dragger
            }
        }

        // 注册所有面板
        var allPanels = item.GetComponentsInChildren<BasePanel>(true);
        foreach (var panel in allPanels)
        {
            string panelName = panel.gameObject.name;

            if (panel.CanDrag && panel.Dragger != null) // 修正：Drager -> Dragger
                dragRegistry[panelName] = panel.Dragger; // 修正：Drager -> Dragger

            if (!panelRegistry.ContainsKey(panelName))
                panelRegistry.Add(panelName, panel);

            // 恢复开关状态
            if (canvasPanelState.canvasPanelboolStates.TryGetValue(panelName, out var isOpen))
            {
                if (isOpen) panel.Open();
                else panel.Close();
            }

            // 恢复位置
            if (canvasPanelState.DraggerPos.TryGetValue(panelName, out var pos)) // 修正：Drager -> Dragger
            {
                if (panel.CanDrag && panel.Dragger != null) // 修正：Drager -> Dragger
                {
                    var dragRT = panel.Dragger.GetComponent<RectTransform>(); // 修正：Drager -> Dragger
                    if (dragRT != null)
                        dragRT.anchoredPosition = pos;
                }
                else
                {
                    var selfRT = panel.GetComponent<RectTransform>();
                    if (selfRT != null)
                        selfRT.anchoredPosition = pos;
                }
            }
        }

        // 生成按钮（菜单）
        if (UIOpen_Parent != null && PanelOpenButtonPrefab != null)
        {
            foreach (Transform child in UIOpen_Parent.transform)
                Destroy(child.gameObject);

            foreach (var panelName in panelRegistry.Keys)
            {
                var btnGO = Instantiate(PanelOpenButtonPrefab, UIOpen_Parent.transform);
                btnGO.name = $"Btn_{panelName}";

                var tmpText = btnGO.GetComponentInChildren<TextMeshProUGUI>();
                if (tmpText != null)
                {
                    tmpText.text = panelName;
                    panelButtonTexts[panelName] = tmpText;
                }

                var button = btnGO.GetComponent<UnityEngine.UI.Button>();
                if (button != null)
                {
                    string capturedName = panelName;
                    button.onClick.AddListener(() => TogglePanel(capturedName));
                }

                bool isOpen = canvasPanelState.canvasPanelboolStates.TryGetValue(panelName, out var b) && b;
                UpdateButtonVisual(panelName, isOpen);
            }
        }

        // 在 Load() 方法最后添加这一行：
        foreach (var kv in panelRegistry)
        {
            string panelName = kv.Key;
            bool isOpen = canvasPanelState.canvasPanelboolStates.TryGetValue(panelName, out var b) && b;
            UpdateButtonVisual(panelName, isOpen);
        }
    }


    public void TogglePanel(string name)
    {
        if (panelRegistry.TryGetValue(name, out var panel))
        {
            bool isOpen = panel.IsOpen();
            isOpen = !isOpen;

            if (isOpen)
                panel.Open();
            else
                panel.Close();

            canvasPanelState.canvasPanelboolStates[name] = isOpen;

            RectTransform rt = panel.CanDrag && panel.Dragger != null // 修正：Drager -> Dragger
                ? panel.Dragger.GetComponent<RectTransform>() // 修正：Drager -> Dragger
                : panel.GetComponent<RectTransform>();

            if (rt != null)
                canvasPanelState.DraggerPos[name] = rt.anchoredPosition; // 修正：Drager -> Dragger

            UpdateButtonVisual(name, isOpen);
        }
    }



    // 更新 CanvasSaveData 的方法来保存位置信息
/*    private void UpdateCanvasSaveData(string panelName, bool isOpen, BasePanel panel)
    {
        Vector3 pos = panel.transform.position;
        canvasPanelStates[panelName] = new CanvasSaveData
        {
            IsOpen = isOpen,
            x = pos.x,
            y = pos.y
        };
    }*/

    private void UpdateButtonVisual(string panelName, bool isOpen)
    {
        if (panelButtonTexts.TryGetValue(panelName, out var tmpText))
        {
            tmpText.text = panelName;
            tmpText.color = isOpen ? Color.green : Color.red;
        }
    }
    [Button("Save")]
    public override void Save()
    {
        // 清理之前状态，防止重复/冲突
        canvasPanelState.canvasPanelboolStates.Clear();
        canvasPanelState.DraggerPos.Clear(); // 修正：Drager -> Dragger

        foreach (var pair in panelRegistry)
        {
            string panelName = pair.Key;
            BasePanel panel = pair.Value;

            // 检查面板是否为null避免空引用
            if (panel == null)
                continue;

            // 保存开关状态
            canvasPanelState.canvasPanelboolStates[panelName] = panel.IsOpen();

            // 保存位置，判断是否可以拖拽
            if (panel.CanDrag && panel.Dragger != null) // 修正：Drager -> Dragger
            {
                var dragRT = panel.Dragger.GetComponent<RectTransform>(); // 修正：Drager -> Dragger
                if (dragRT != null)
                    canvasPanelState.DraggerPos[panelName] = dragRT.anchoredPosition; // 修正：Drager -> Dragger
            }
            else
            {
                var selfRT = panel.GetComponent<RectTransform>();
                if (selfRT != null)
                    canvasPanelState.DraggerPos[panelName] = selfRT.anchoredPosition; // 修正：Drager -> Dragger
            }
        }

        exData.WriteData(canvasPanelState);
    }

}