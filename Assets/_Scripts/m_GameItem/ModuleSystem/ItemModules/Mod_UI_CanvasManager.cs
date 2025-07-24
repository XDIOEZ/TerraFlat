using MemoryPack;
using Newtonsoft.Json;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class Mod_UI_CanvasManager : Module
{
    [ShowInInspector]
    public Dictionary<string, BasePanel> panelRegistry = new();

    public GameObject UIOpen_Parent; // 存放动态生成的按钮
    public GameObject PanelOpenButtonPrefab; // 指定按钮预制体（可在Inspector中拖入）

    private Dictionary<string, TextMeshProUGUI> panelButtonTexts = new();

    [ShowInInspector]
    public Dictionary<string, bool> canvasPanelStates = new();

    public override ModuleData _Data { get => exData; set => exData = (Ex_ModData)value; }
    public Ex_ModData exData = new();
    //启动UI按钮的父物体

    [Button("Open Panel")]
    public void OpenPanel(string name)
    {
        if (panelRegistry.TryGetValue(name, out var panel))
        {
            panel.Open();
            canvasPanelStates[name] = true;
        }
    }

    [Button("Close Panel")]
    public void ClosePanel(string name)
    {
        if (panelRegistry.TryGetValue(name, out var panel))
        {
            panel.Close();
            canvasPanelStates[name] = false;
        }
    }

    public override void Load()
    {
        canvasPanelStates = exData.GetData<Dictionary<string, bool>>();

        panelRegistry.Clear();
        var allPanels = item.GetComponentsInChildren<BasePanel>(true); // 只查找自己子物体
        foreach (var panel in allPanels)
        {
            string panelName = panel.gameObject.name;
            if (!panelRegistry.ContainsKey(panelName))
            {
                panelRegistry.Add(panelName, panel);
            }
        }

        // 初始化或恢复状态
        if (canvasPanelStates == null)
        {
            canvasPanelStates = new();
            foreach (var pair in panelRegistry)
            {
                canvasPanelStates[pair.Key] = pair.Value.IsOpen();
            }
        }
        else
        {
            foreach (var pair in panelRegistry)
            {
                if (canvasPanelStates.TryGetValue(pair.Key, out bool state))
                {
                    if (state) pair.Value.Open();
                    else pair.Value.Close();
                }
            }
        }

        // 动态创建按钮
        if (UIOpen_Parent != null && PanelOpenButtonPrefab != null)
        {
            // 清空旧按钮
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
                    panelButtonTexts[panelName] = tmpText;  // 缓存文本引用
                }

                var button = btnGO.GetComponent<UnityEngine.UI.Button>();
                if (button != null)
                {
                    string capturedName = panelName; // 防闭包
                    button.onClick.AddListener(() => TogglePanel(capturedName));
                }

                bool isOpen = false;
                canvasPanelStates.TryGetValue(panelName, out isOpen);
                UpdateButtonVisual(panelName, isOpen);
            }

        }

       


    }
    public void TogglePanel(string name)
    {
        if (panelRegistry.TryGetValue(name, out var panel))
        {
            bool isOpen = panel.IsOpen();
            if (isOpen)
            {
                panel.Close();
                canvasPanelStates[name] = false;
                UpdateButtonVisual(name, false);
            }
            else
            {
                panel.Open();
                canvasPanelStates[name] = true;
                UpdateButtonVisual(name, true);
            }
        }

    }
    private void UpdateButtonVisual(string panelName, bool isOpen)
    {
        if (panelButtonTexts.TryGetValue(panelName, out var tmpText))
        {
            tmpText.text = panelName; // 你也可以加前缀："[打开] " + panelName 等
            tmpText.color = isOpen ? Color.green : Color.red;
        }
    }





    [Button("Save")]
    public override void Save()
    {
        foreach (var pair in panelRegistry)
        {
            canvasPanelStates[pair.Key] = pair.Value.IsOpen();
        }

        exData.WriteData(canvasPanelStates);
    }

}

[System.Serializable]
[MemoryPackable]
public partial class Ex_ModData : ModuleData
{
    public string BitData;

    public T GetData<T>()
    {
        if (string.IsNullOrEmpty(BitData)) return default;
        return JsonConvert.DeserializeObject<T>(BitData);
    }

    public void ReadData<T>(ref T setData)
    {
        if (string.IsNullOrEmpty(BitData)) return;
        setData = JsonConvert.DeserializeObject<T>(BitData);
    }


    public void WriteData<T>(T bitData)
    {
        BitData = JsonConvert.SerializeObject(bitData);
    }
}

[Serializable]
[MemoryPackable]
public partial class Ex_ModData_MemoryPack : ModuleData
{
    public byte[] BitData;

    public T GetData<T>()
    {
        if (BitData == null || BitData.Length == 0) return default;
        return MemoryPackSerializer.Deserialize<T>(BitData);
    }

    public void ReadData<T>(ref T setData)
    {
        if (BitData == null || BitData.Length == 0) return;
        setData = MemoryPackSerializer.Deserialize<T>(BitData);
    }

    public void WriteData<T>(T bitData)
    {
        BitData = MemoryPackSerializer.Serialize(bitData);
    }
}
