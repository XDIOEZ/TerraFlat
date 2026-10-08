using System.Collections.Generic;
using System;
using UnityEngine;
using TMPro;

public class Mod_DebugInfo : Module, IInstanceUI
{
    public Ex_ModData DebugData;
    public override ModuleData _Data { get => DebugData; set => DebugData = (Ex_ModData)value; }

    [Header("调试面板设置")]
    public BasePanel DebugPanel;        // UI 面板（可选）
    public GameObject Content;          // 条目容器，需要包含 VerticalLayoutGroup
    public GameObject stringPrefab;     // 单条调试信息的预制体，需要包含 TextMeshProUGUI 组件

    [Header("调试信息")]
    public List<string> DebugInfo = new List<string>();

    protected override void OnLoad()
    {
        // 如需加载调试信息，可在这里处理。
       
    }

    protected override void OnSave()
    {
        // 如需保存调试信息，可在这里处理。
    }

    /// <summary>
    /// 设置调试信息列表并刷新 UI。
    /// </summary>
    public void SetDebugInfo(List<string> newInfo)
    {
        DebugInfo = newInfo;
        RefreshDebugPanel();
    }

    /// <summary>
    /// 添加一条调试信息并刷新 UI。
    /// </summary>
    public void AddDebugLine(string line)
    {
        DebugInfo.Add(line);
        RefreshDebugPanel();
    }

    #region 面板能力接口

    // 调试面板同样报告真实开关状态。
    public bool IsPanelOpen => DebugPanel != null && DebugPanel.IsOpen();

    public void I_ShowPanel()
    {
        if (DebugPanel == null)
            throw new System.InvalidOperationException("[Mod_DebugInfo] DebugPanel 为空，无法打开面板");

        DebugPanel.Open();
    }

    public void I_ClosePanel()
    {
        if (DebugPanel == null)
            throw new System.InvalidOperationException("[Mod_DebugInfo] DebugPanel 为空，无法关闭面板");

        DebugPanel.Close();
    }

    public void I_TogglePanel()
    {
        if (DebugPanel == null)
            throw new System.InvalidOperationException("[Mod_DebugInfo] DebugPanel 为空，无法切换面板");

        DebugPanel.Toggle();
    }

    #endregion

    /// <summary>
    /// 根据 DebugInfo 刷新 UI 条目列表。
    /// </summary>
    public void RefreshDebugPanel()
    {
        if (Content == null || stringPrefab == null) return;

        // 清空旧的调试条目。
        foreach (Transform child in Content.transform)
        {
            Destroy(child.gameObject);
        }

        // 创建新的调试条目。
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
