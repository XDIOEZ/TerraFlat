using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Mod_DebugInfo : Module
{
    public Ex_ModData DebugData;
    public override ModuleData Data { get => DebugData; set => DebugData = (Ex_ModData)value; }

    [Header("����������")]
    public BasePanel DebugPanel;        // UI��壨��ѡ��
    public GameObject Content;          // ��Ŀ��������һ���� VerticalLayoutGroup
    public GameObject stringPrefab;     // ����������Ϣ��Ԥ���壬����� TextMeshProUGUI ���

    [Header("������Ϣ")]
    public List<string> DebugInfo = new List<string>();

    public override void Load()
    {
        // �������Ҫ����Debug��Ϣ�����������ﴦ��
       
    }

    public override void Save()
    {
        // �������Ҫ����Debug��Ϣ�����������ﴦ��
    }

    /// <summary>
    /// ���õ�����Ϣ�б���ˢ��UI
    /// </summary>
    public void SetDebugInfo(List<string> newInfo)
    {
        DebugInfo = newInfo;
        RefreshDebugPanel();
    }

    /// <summary>
    /// ���һ��������Ϣ��ˢ��UI
    /// </summary>
    public void AddDebugLine(string line)
    {
        DebugInfo.Add(line);
        RefreshDebugPanel();
    }

    /// <summary>
    /// ���� DebugInfo ˢ�� UI �������
    /// </summary>
    public void RefreshDebugPanel()
    {
        if (Content == null || stringPrefab == null) return;

        // ��վɵĵ�����Ŀ
        foreach (Transform child in Content.transform)
        {
            Destroy(child.gameObject);
        }

        // �����µĵ�����Ŀ
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
