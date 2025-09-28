using MemoryPack;
using Newtonsoft.Json;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// ����ģ�����ӿ�
public interface IModulePanel
{
    void ShowPanel();
    void HidePanel();
    void RefreshUI();
    bool IsPanelVisible(); // ��Ӽ������Ƿ�ɼ��ķ���
}

public partial class Mod_UI_CanvasManager : Module
{
    #region ���ݽṹ����
    [ShowInInspector]
    public Dictionary<string, BasePanel> panelRegistry = new();
    public Dictionary<string, UI_Drag> dragRegistry = new();
    private Dictionary<string, TextMeshProUGUI> panelButtonTexts = new();
    
    // ���ģ�����ע���
    private Dictionary<string, IModulePanel> modulePanelRegistry = new();

    [Header("UI��ť����")]
    public GameObject UIOpen_Parent; // ���ڶ�̬���ɵİ�ť
    public GameObject PanelOpenButtonPrefab; // ָ����ťԤ���壨����Inspector�����ã�

    CanvasSaveData canvasPanelState = new CanvasSaveData();

    [Serializable]
    [MemoryPackable]
    public partial class CanvasSaveData
    {
        [ShowInInspector]
        public Dictionary<string, bool> canvasPanelboolStates = new();
        public Dictionary<string, Vector2> DraggerPos = new(); // ��קλ������
    }
    #endregion

    #region �����ֶ�������
    public override ModuleData _Data { get => exData; set => exData = (Ex_ModData_MemoryPackable)value; }
    public Ex_ModData_MemoryPackable exData = new();
    #endregion

    #region Unity��������
    public override void Load()
    {
        exData.ReadData(ref canvasPanelState);

        // ע������ UI_Drag
        var allDraggers = item.GetComponentsInChildren<UI_Drag>(true);
        foreach (var dragger in allDraggers)
        {
            string draggerName = dragger.gameObject.name;
            dragRegistry[draggerName] = dragger;

            // ע�⣺���ڴ˴��ָ�UI_Drag��λ�ã��ø���ģ���Լ������Լ���λ��
            // ֻ�е�λ�����ݴ�������岻��ģ�����ʱ�Żָ�λ��
            if (canvasPanelState.DraggerPos.TryGetValue(draggerName, out var pos))
            {
                // ������dragger�Ƿ�����ģ�����
                bool isModulePanelDragger = IsPartOfModulePanel(dragger.transform);
                
                // ֻ�з�ģ������dragger����CanvasManager����λ��
                if (!isModulePanelDragger)
                {
                    Debug.Log($"[CanvasManager] �ָ���ק���λ��: {draggerName} �� {dragger.rectTransform.anchoredPosition} �� {pos}");
                    dragger.rectTransform.anchoredPosition = pos;
                    Debug.Log($"[CanvasManager] �ָ���λ��: {draggerName} = {dragger.rectTransform.anchoredPosition}");
                }
            }
        }

        // ע��������壨�ų�����
        var allPanels = item.GetComponentsInChildren<BasePanel>(true);
        foreach (var panel in allPanels)
        {
            // ����������壬�����Լ������Լ����µ�����
            if (panel.gameObject == gameObject)
                continue;

            string panelName = panel.gameObject.name;

            // ע�����ק������ק���
            if (panel.CanDrag && panel.Dragger != null)
                dragRegistry[panelName] = panel.Dragger;

            // ��ӵ����ע���
            if (!panelRegistry.ContainsKey(panelName))
                panelRegistry.Add(panelName, panel);

            // ����������Ƿ�����ģ�����
            bool isModulePanel = IsPartOfModulePanel(panel.transform);
            
            // ֻ�з�ģ��������CanvasManager����״̬
            if (!isModulePanel)
            {
                if (canvasPanelState.canvasPanelboolStates.TryGetValue(panelName, out var isOpen))
                {
                    if (isOpen) panel.Open();
                    else panel.Close();
                }

                // �ָ�λ��
                if (canvasPanelState.DraggerPos.TryGetValue(panelName, out var pos))
                {
                    if (panel.CanDrag && panel.Dragger != null)
                    {
                        var dragRT = panel.Dragger.GetComponent<RectTransform>();
                        if (dragRT != null)
                        {
                            Debug.Log($"[CanvasManager] �ָ������קλ��: {panelName} �� {dragRT.anchoredPosition} �� {pos}");
                            dragRT.anchoredPosition = pos;
                            Debug.Log($"[CanvasManager] �ָ���λ��: {panelName} = {dragRT.anchoredPosition}");
                        }
                    }
                    else
                    {
                        var selfRT = panel.GetComponent<RectTransform>();
                        if (selfRT != null)
                        {
                            Debug.Log($"[CanvasManager] �ָ����λ��: {panelName} �� {selfRT.anchoredPosition} �� {pos}");
                            selfRT.anchoredPosition = pos;
                            Debug.Log($"[CanvasManager] �ָ���λ��: {panelName} = {selfRT.anchoredPosition}");
                        }
                    }
                }
            }
        }

        // ע������ģ�����
        RegisterModulePanels();

        // ���ɿ��ư�ť���˵���
        GenerateControlButtons();

        // �ڰ�ť������ɺ�ͬ�������������ʵ��״̬�����°�ť��ɫ
        SyncAllButtonVisuals();
    }

    [Button("����")]
    public override void Save()
    {
        // ����֮ǰ״̬����ֹ�ظ�/��ͻ
        canvasPanelState.canvasPanelboolStates.Clear();
        canvasPanelState.DraggerPos.Clear();

        // ��������ע������״̬����������ģ������״̬��
        foreach (var pair in panelRegistry)
        {
            string panelName = pair.Key;
            BasePanel panel = pair.Value;

            // �������Ƿ�Ϊnull���������
            if (panel == null)
                continue;

            // ����������Ƿ�����ģ����壬�����������
            bool isModulePanel = IsPartOfModulePanel(panel.transform);
            
            if (isModulePanel)
                continue;

            // ���濪��״̬
            canvasPanelState.canvasPanelboolStates[panelName] = panel.IsOpen();

            // ����λ�ã��ж��Ƿ������ק
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

        // ��������ģ������״̬
        foreach (var pair in modulePanelRegistry)
        {
            string moduleName = pair.Key;
            IModulePanel modulePanel = pair.Value;

            // ���濪��״̬
            canvasPanelState.canvasPanelboolStates[moduleName] = modulePanel.IsPanelVisible();
            // ע�⣺������ģ������λ�ã���ģ���Լ������Լ���λ��
        }

        exData.WriteData(canvasPanelState);
    }
    #endregion

    #region ���ע�������
    /// <summary>
    /// ���ָ����Transform�Ƿ�����ģ�����
    /// </summary>
    /// <param name="transform">Ҫ����Transform</param>
    /// <returns>�Ƿ�����ģ�����</returns>
    private bool IsPartOfModulePanel(Transform transform)
    {
        // ��������ģ�飬������transform�Ƿ���ģ����Ӷ�����
        foreach (var modulePair in item.itemMods.Mods)
        {
            if (modulePair.Value is IModulePanel)
            {
                // ��ȡģ���GameObject
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
    /// ע������ʵ����IModulePanel�ӿڵ�ģ��
    /// </summary>
    private void RegisterModulePanels()
    {
        modulePanelRegistry.Clear();
        
        // ��������ģ�飬����ʵ����IModulePanel�ӿڵ�ģ��
        foreach (var modulePair in item.itemMods.Mods)
        {
            if (modulePair.Value is IModulePanel modulePanel)
            {
                string moduleName = modulePair.Key;
                modulePanelRegistry[moduleName] = modulePanel;
                
                // ע�⣺���ڴ˴��ָ�ģ������״̬����ģ���Լ������Լ���״̬
                // ģ������״̬�ָ�Ӧ����ģ���Լ���Load�����д���
            }
        }
    }
    #endregion

    #region ���ư�ť���������
    /// <summary>
    /// ���ɿ��ư�ť
    /// </summary>
    private void GenerateControlButtons()
    {
        // ����Ҫ���
        if (UIOpen_Parent == null || PanelOpenButtonPrefab == null)
        {
            Debug.LogWarning("[CanvasManager] ȱ�ٱ�Ҫ��UI������޷����ɿ��ư�ť");
            return;
        }

        // �������а�ť
        foreach (Transform child in UIOpen_Parent.transform)
            Destroy(child.gameObject);

        panelButtonTexts.Clear();

        // Ϊÿ��ע���������ɿ��ư�ť
        foreach (var panelName in panelRegistry.Keys)
        {
            // ����������Ƿ�����ģ����壬������������������ظ����ƣ�
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
                GenerateButtonForPanel(panelName, false); // false��ʾ�ⲻ��ģ�����
            }
        }

        // Ϊÿ��ע���ģ��������ɿ��ư�ť
        foreach (var moduleName in modulePanelRegistry.Keys)
        {
            GenerateButtonForPanel(moduleName, true); // true��ʾ����ģ�����
        }
    }

    /// <summary>
    /// Ϊ������ɰ�ť
    /// </summary>
    /// <param name="panelName">�������</param>
    /// <param name="isModulePanel">�Ƿ�Ϊģ�����</param>
    private void GenerateButtonForPanel(string panelName, bool isModulePanel)
    {
        // ���Ԥ�����Ƿ����
        if (PanelOpenButtonPrefab == null)
        {
            Debug.LogError($"[CanvasManager] PanelOpenButtonPrefab δ���ã��޷�Ϊ {panelName} ���ɰ�ť");
            return;
        }

        var btnGO = Instantiate(PanelOpenButtonPrefab, UIOpen_Parent.transform);
        btnGO.name = $"Btn_{panelName}";

        // ���ð�ť�ı�
        var tmpText = btnGO.GetComponentInChildren<TextMeshProUGUI>();
        if (tmpText != null)
        {
            tmpText.text = panelName;
            panelButtonTexts[panelName] = tmpText;
        }

        // ���ð�ť����¼�
        var button = btnGO.GetComponent<UnityEngine.UI.Button>();
        if (button != null)
        {
            string capturedName = panelName;
            bool capturedIsModulePanel = isModulePanel;
            button.onClick.AddListener(() => TogglePanel(capturedName, capturedIsModulePanel));
        }
    }

    /// <summary>
    /// ͬ�����а�ť���Ӿ�״̬
    /// </summary>
    private void SyncAllButtonVisuals()
    {
        // ͬ��BasePanel��ť״̬
        foreach (var kv in panelRegistry)
        {
            string panelName = kv.Key;
            BasePanel panel = kv.Value;
            
            // ����������Ƿ�����ģ����壬�����������
            bool isModulePanel = IsPartOfModulePanel(panel.transform);
            
            if (!isModulePanel)
            {
                // �������ʵ��״̬���°�ť��ɫ
                bool isPanelOpen = panel.IsOpen();
                UpdateButtonVisual(panelName, isPanelOpen);
            }
        }

        // ͬ��ģ����尴ť״̬
        foreach (var kv in modulePanelRegistry)
        {
            string moduleName = kv.Key;
            IModulePanel modulePanel = kv.Value;
            
            // ����ģ�����ʵ��״̬���°�ť��ɫ
            bool isPanelVisible = modulePanel.IsPanelVisible();
            UpdateButtonVisual(moduleName, isPanelVisible);
        }
    }

    /// <summary>
    /// ���°�ť�Ӿ�״̬
    /// </summary>
    /// <param name="panelName">�������</param>
    /// <param name="isOpen">�Ƿ���</param>
    private void UpdateButtonVisual(string panelName, bool isOpen)
    {
        if (panelButtonTexts.TryGetValue(panelName, out var tmpText))
        {
            tmpText.text = panelName;
            tmpText.color = isOpen ? Color.green : Color.red;
        }
    }

    /// <summary>
    /// ˢ�°�ť
    /// </summary>
    [Button("ˢ�°�ť")]
    public void RefreshButtons()
    {
        GenerateControlButtons();
        SyncAllButtonVisuals();
    }
    #endregion

    #region �����Ʒ���
    /// <summary>
    /// �л���忪��״̬
    /// </summary>
    /// <param name="name">�������</param>
    /// <param name="isModulePanel">�Ƿ�Ϊģ�����</param>
    public void TogglePanel(string name, bool isModulePanel = false)
    {
        if (isModulePanel)
        {
            // ����ģ�����
            if (modulePanelRegistry.TryGetValue(name, out var modulePanel))
            {
                bool isVisible = !modulePanel.IsPanelVisible();

                if (isVisible)
                    modulePanel.ShowPanel();
                else
                    modulePanel.HidePanel();

                // ����״̬����
                canvasPanelState.canvasPanelboolStates[name] = isVisible;

                // ���°�ť�Ӿ�
                UpdateButtonVisual(name, isVisible);
            }
            else
            {
                Debug.LogWarning($"[CanvasManager] δ�ҵ�ģ�����: {name}");
            }
        }
        else
        {
            // ����BasePanel
            if (panelRegistry.TryGetValue(name, out var panel))
            {
                bool isOpen = !panel.IsOpen();

                if (isOpen)
                    panel.Open();
                else
                    panel.Close();

                // ����״̬����
                canvasPanelState.canvasPanelboolStates[name] = isOpen;

                // ����λ������
                RectTransform rt = panel.CanDrag && panel.Dragger != null
                    ? panel.Dragger.GetComponent<RectTransform>()
                    : panel.GetComponent<RectTransform>();

                if (rt != null)
                    canvasPanelState.DraggerPos[name] = rt.anchoredPosition;

                // ���°�ť�Ӿ�
                UpdateButtonVisual(name, isOpen);
            }
            else
            {
                Debug.LogWarning($"[CanvasManager] δ�ҵ����: {name}");
            }
        }
    }

    /// <summary>
    /// ��ָ�����
    /// </summary>
    /// <param name="name">�������</param>
    /// <param name="isModulePanel">�Ƿ�Ϊģ�����</param>
    [Button("�����")]
    public void OpenPanel(string name, bool isModulePanel = false)
    {
        if (isModulePanel)
        {
            // ����ģ�����
            if (modulePanelRegistry.TryGetValue(name, out var modulePanel))
            {
                modulePanel.ShowPanel();
                canvasPanelState.canvasPanelboolStates[name] = true;
                UpdateButtonVisual(name, true);
            }
            else
            {
                Debug.LogWarning($"[CanvasManager] δ�ҵ�ģ�����: {name}");
            }
        }
        else
        {
            // ����BasePanel
            if (panelRegistry.TryGetValue(name, out var panel))
            {
                panel.Open();
                canvasPanelState.canvasPanelboolStates[name] = true;
                UpdateButtonVisual(name, true);
                
                // ����λ������
                RectTransform rt = panel.CanDrag && panel.Dragger != null
                    ? panel.Dragger.GetComponent<RectTransform>()
                    : panel.GetComponent<RectTransform>();

                if (rt != null)
                    canvasPanelState.DraggerPos[name] = rt.anchoredPosition;
            }
            else
            {
                Debug.LogWarning($"[CanvasManager] δ�ҵ����: {name}");
            }
        }
    }

    /// <summary>
    /// �ر�ָ�����
    /// </summary>
    /// <param name="name">�������</param>
    /// <param name="isModulePanel">�Ƿ�Ϊģ�����</param>
    [Button("�ر����")]
    public void ClosePanel(string name, bool isModulePanel = false)
    {
        if (isModulePanel)
        {
            // ����ģ�����
            if (modulePanelRegistry.TryGetValue(name, out var modulePanel))
            {
                modulePanel.HidePanel();
                canvasPanelState.canvasPanelboolStates[name] = false;
                UpdateButtonVisual(name, false);
            }
            else
            {
                Debug.LogWarning($"[CanvasManager] δ�ҵ�ģ�����: {name}");
            }
        }
        else
        {
            // ����BasePanel
            if (panelRegistry.TryGetValue(name, out var panel))
            {
                panel.Close();
                canvasPanelState.canvasPanelboolStates[name] = false;
                UpdateButtonVisual(name, false);
                
                // ����λ������
                RectTransform rt = panel.CanDrag && panel.Dragger != null
                    ? panel.Dragger.GetComponent<RectTransform>()
                    : panel.GetComponent<RectTransform>();

                if (rt != null)
                    canvasPanelState.DraggerPos[name] = rt.anchoredPosition;
            }
            else
            {
                Debug.LogWarning($"[CanvasManager] δ�ҵ����: {name}");
            }
        }
    }

    /// <summary>
    /// ��ȡ���״̬
    /// </summary>
    /// <param name="name">�������</param>
    /// <param name="isModulePanel">�Ƿ�Ϊģ�����</param>
    /// <returns>����Ƿ���</returns>
    public bool IsPanelOpen(string name, bool isModulePanel = false)
    {
        if (isModulePanel)
        {
            // ����ģ�����
            if (modulePanelRegistry.TryGetValue(name, out var modulePanel))
            {
                return modulePanel.IsPanelVisible();
            }
            return false;
        }
        else
        {
            // ����BasePanel
            if (panelRegistry.TryGetValue(name, out var panel))
            {
                return panel.IsOpen();
            }
            return false;
        }
    }
    #endregion
}