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



    public GameObject UIOpen_Parent; // ��Ŷ�̬���ɵİ�ť
    public GameObject PanelOpenButtonPrefab; // ָ����ťԤ���壨����Inspector�����룩


    CanvasSaveData canvasPanelState = new CanvasSaveData();

    [Serializable]
    [MemoryPackable]
    public partial class CanvasSaveData
    {
        [ShowInInspector]
        public Dictionary<string, bool> canvasPanelboolStates = new();
        public Dictionary<string,Vector2> DragerPos = new();
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
        var allDrager = item.GetComponentsInChildren<UI_Drag>(true);
        foreach (var drager in allDrager)
        {
            dragRegistry[drager.gameObject.name] = drager;

            if (canvasPanelState.DragerPos.TryGetValue(drager.gameObject.name, out var pos))
            {
                drager.rectTransform.anchoredPosition = pos;
            }
        }

        // ע���������
        var allPanels = item.GetComponentsInChildren<BasePanel>(true);
        foreach (var panel in allPanels)
        {
            string panelName = panel.gameObject.name;

            if (panel.CanDrag && panel.Drager != null)
                dragRegistry[panelName] = panel.Drager;

            if (!panelRegistry.ContainsKey(panelName))
                panelRegistry.Add(panelName, panel);

            // �ָ�����״̬
            if (canvasPanelState.canvasPanelboolStates.TryGetValue(panelName, out var isOpen))
            {
                if (isOpen) panel.Open();
                else panel.Close();
            }

            // �ָ�λ��
            if (canvasPanelState.DragerPos.TryGetValue(panelName, out var pos))
            {
                if (panel.CanDrag && panel.Drager != null)
                {
                    var dragRT = panel.Drager.GetComponent<RectTransform>();
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

        // ���ɰ�ť�����䣩
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

        // �� Load() ���������ӣ�
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

            RectTransform rt = panel.CanDrag && panel.Drager != null
                ? panel.Drager.GetComponent<RectTransform>()
                : panel.GetComponent<RectTransform>();

            if (rt != null)
                canvasPanelState.DragerPos[name] = rt.anchoredPosition;

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
        // ���֮ǰ״̬�������ظ�/����
        canvasPanelState.canvasPanelboolStates.Clear();
        canvasPanelState.DragerPos.Clear();

        foreach (var pair in panelRegistry)
        {
            string panelName = pair.Key;
            BasePanel panel = pair.Value;

            // ���濪��״̬
            canvasPanelState.canvasPanelboolStates[panelName] = panel.IsOpen();

            // ����λ�ã��ж��Ƿ����ק��
            if (panel.CanDrag && panel.Drager != null)
            {
                var dragRT = panel.Drager.GetComponent<RectTransform>();
                if (dragRT != null)
                    canvasPanelState.DragerPos[panelName] = dragRT.anchoredPosition;
            }
            else
            {
                var selfRT = panel.GetComponent<RectTransform>();
                if (selfRT != null)
                    canvasPanelState.DragerPos[panelName] = selfRT.anchoredPosition;
            }
        }

        exData.WriteData(canvasPanelState);
    }

}