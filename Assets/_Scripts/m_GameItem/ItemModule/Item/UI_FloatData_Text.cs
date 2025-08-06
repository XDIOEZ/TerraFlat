using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro; // �������� TextMesh Pro �������ռ�

/// <summary>
/// ����UI�е�TextMeshProUGUI�����������ʾ���������ݣ���ʽΪ "��ǰֵ/���ֵ"��
/// </summary>
public class UI_FloatData_Text : MonoBehaviour
{
    // ���ֵ��Value���ʹ� Text ��Ϊ TextMeshProUGUI
    public Dictionary<string, TextMeshProUGUI> TextDisplays = new Dictionary<string, TextMeshProUGUI>();

    void Awake()
    {
        Init();
    }

    /// <summary>
    /// ��ʼ�������������������е�TextMeshProUGUI����������ֵ䡣
    /// </summary>
    public void Init()
    {
        // ��ȡ�����������е�TextMeshProUGUI�����������Щ��ǰδ�����
        TextMeshProUGUI[] allTexts = GetComponentsInChildren<TextMeshProUGUI>(true);

        // �����Ҫ���³�ʼ����������ֵ�
        TextDisplays.Clear();

        // �������л�ȡ����TextMeshProUGUI���
        foreach (TextMeshProUGUI textComponent in allTexts)
        {
            // ����ֵ����Ƿ��Ѵ���ͬ����Key���Ա����ظ���ӵ��´���
            if (!TextDisplays.ContainsKey(textComponent.name))
            {
                // ���������Ϸ����������ΪKey�����������ΪValue����ӵ��ֵ���
                TextDisplays.Add(textComponent.name, textComponent);
            }
            else
            {
                // ���������������ӡһ��������Ϣ���������
                Debug.LogWarning($"����������TextMeshPro���: {textComponent.name}����������ӡ�");
            }
        }
    }

    /// <summary>
    /// ͨ�����Ƹ���TextMeshProUGUI�����ʾ����ֵ��
    /// </summary>
    /// <param name="textName">TextMeshProUGUI���������Ϸ��������� (��Ϊ�ֵ��Key)</param>
    /// <param name="currentValue">Ҫ��ʾ�ĵ�ǰֵ</param>
    /// <param name="maxValue">Ҫ��ʾ�����ֵ</param>
    public void UpdateText(string textName, float currentValue, float maxValue)
    {
        // ���Դ��ֵ��и������ƻ�ȡTextMeshProUGUI���
        if (TextDisplays.TryGetValue(textName, out TextMeshProUGUI textComponent))
        {
            // ����ҵ��ˣ��͸�ʽ���ַ���������Text����ʾ����
            // ���˵��ǣ�TextMesh Pro �ͱ�׼ Text ����� .text �����÷���ȫ��ͬ
            textComponent.text = $"{Mathf.RoundToInt(currentValue)}/{Mathf.RoundToInt(maxValue)}";
        }
        else
        {
            // ������ֵ���û�ҵ���Ӧ���Ƶ�Text����ӡ������Ϣ
            Debug.LogError($"δ�ҵ���Ϊ '{textName}' ��TextMeshPro�����");
        }
    }
}