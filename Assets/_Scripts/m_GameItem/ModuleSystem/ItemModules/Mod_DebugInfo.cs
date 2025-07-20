using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Mod_DebugInfo : Module
{
    public Ex_ModData DebugData;
    public override ModuleData Data { get => DebugData; set => DebugData = (Ex_ModData)value; }

    [Header("调试面板组件")]
    public BasePanel DebugPanel;        // UI面板（可选）
    public GameObject Content;          // 条目的容器，一般是 VerticalLayoutGroup
    public GameObject stringPrefab;     // 单条调试信息的预制体，需包含 TextMeshProUGUI 组件

    [Header("调试信息")]
    public List<string> DebugInfo = new List<string>();

    public override void Load()
    {
        // 如果你需要加载Debug信息，可以在这里处理
       
    }

    public override void Save()
    {
        // 如果你需要保存Debug信息，可以在这里处理
    }

    /// <summary>
    /// 设置调试信息列表，并刷新UI
    /// </summary>
    public void SetDebugInfo(List<string> newInfo)
    {
        DebugInfo = newInfo;
        RefreshDebugPanel();
    }

    /// <summary>
    /// 添加一条调试信息并刷新UI
    /// </summary>
    public void AddDebugLine(string line)
    {
        DebugInfo.Add(line);
        RefreshDebugPanel();
    }

    /// <summary>
    /// 根据 DebugInfo 刷新 UI 面板内容
    /// </summary>
    public void RefreshDebugPanel()
    {
        if (Content == null || stringPrefab == null) return;

        // 清空旧的调试条目
        foreach (Transform child in Content.transform)
        {
            Destroy(child.gameObject);
        }

        // 创建新的调试条目
        foreach (var info in DebugInfo)
        {
            GameObject entry = Instantiate(stringPrefab, Content.transform);
            TextMeshProUGUI text = entry.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                text.text = info;
            }
        }
    }
}
