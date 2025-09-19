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



    public GameObject UIOpen_Parent; // ���ڶ�̬���ɵİ�ť
    public GameObject PanelOpenButtonPrefab; // ָ����ťԤ���壨����Inspector�����ã�


    CanvasSaveData canvasPanelState = new CanvasSaveData();

    [Serializable]
    [MemoryPackable]
    public partial class CanvasSaveData
    {
        [ShowInInspector]
        public Dictionary<string, bool> canvasPanelboolStates = new();
        public Dictionary<string,Vector2> DraggerPos = new(); // ������Drager -> Dragger
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

        // ע������ UI_Drag
        var allDragger = item.GetComponentsInChildren<UI_Drag>(true); // ������Drager -> Dragger
        foreach (var dragger in allDragger) // ������drager -> dragger
        {
            dragRegistry[dragger.gameObject.name] = dragger; // ������drager -> dragger

            if (canvasPanelState.DraggerPos.TryGetValue(dragger.gameObject.name, out var pos)) // ������Drager -> Dragger
            {
                dragger.rectTransform.anchoredPosition = pos; // ������drager -> dragger
            }
        }

        // ע���������
        var allPanels = item.GetComponentsInChildren<BasePanel>(true);
        foreach (var panel in allPanels)
        {
            string panelName = panel.gameObject.name;

            if (panel.CanDrag && panel.Dragger != null) // ������Drager -> Dragger
                dragRegistry[panelName] = panel.Dragger; // ������Drager -> Dragger

            if (!panelRegistry.ContainsKey(panelName))
                panelRegistry.Add(panelName, panel);

            // �ָ�����״̬
            if (canvasPanelState.canvasPanelboolStates.TryGetValue(panelName, out var isOpen))
            {
                if (isOpen) panel.Open();
                else panel.Close();
            }

            // �ָ�λ��
            if (canvasPanelState.DraggerPos.TryGetValue(panelName, out var pos)) // ������Drager -> Dragger
            {
                if (panel.CanDrag && panel.Dragger != null) // ������Drager -> Dragger
                {
                    var dragRT = panel.Dragger.GetComponent<RectTransform>(); // ������Drager -> Dragger
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

        // ���ɰ�ť���˵���
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

        // �� Load() ������������һ�У�
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

            RectTransform rt = panel.CanDrag && panel.Dragger != null // ������Drager -> Dragger
                ? panel.Dragger.GetComponent<RectTransform>() // ������Drager -> Dragger
                : panel.GetComponent<RectTransform>();

            if (rt != null)
                canvasPanelState.DraggerPos[name] = rt.anchoredPosition; // ������Drager -> Dragger

            UpdateButtonVisual(name, isOpen);
        }
    }



    // ���� CanvasSaveData �ķ���������λ����Ϣ
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
        // ����֮ǰ״̬����ֹ�ظ�/��ͻ
        canvasPanelState.canvasPanelboolStates.Clear();
        canvasPanelState.DraggerPos.Clear(); // ������Drager -> Dragger

        foreach (var pair in panelRegistry)
        {
            string panelName = pair.Key;
            BasePanel panel = pair.Value;

            // �������Ƿ�Ϊnull���������
            if (panel == null)
                continue;

            // ���濪��״̬
            canvasPanelState.canvasPanelboolStates[panelName] = panel.IsOpen();

            // ����λ�ã��ж��Ƿ������ק
            if (panel.CanDrag && panel.Dragger != null) // ������Drager -> Dragger
            {
                var dragRT = panel.Dragger.GetComponent<RectTransform>(); // ������Drager -> Dragger
                if (dragRT != null)
                    canvasPanelState.DraggerPos[panelName] = dragRT.anchoredPosition; // ������Drager -> Dragger
            }
            else
            {
                var selfRT = panel.GetComponent<RectTransform>();
                if (selfRT != null)
                    canvasPanelState.DraggerPos[panelName] = selfRT.anchoredPosition; // ������Drager -> Dragger
            }
        }

        exData.WriteData(canvasPanelState);
    }

}