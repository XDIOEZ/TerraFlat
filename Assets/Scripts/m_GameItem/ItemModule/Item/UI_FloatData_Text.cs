using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro; // 必须引入 TextMesh Pro 的命名空间

/// <summary>
/// 管理UI中的TextMeshProUGUI组件，用于显示浮点数数据，格式为 "当前值/最大值"。
/// </summary>
public class UI_FloatData_Text : MonoBehaviour
{
    // 将字典的Value类型从 Text 改为 TextMeshProUGUI
    public Dictionary<string, TextMeshProUGUI> TextDisplays = new Dictionary<string, TextMeshProUGUI>();

    void Awake()
    {
        Init();
    }

    /// <summary>
    /// 初始化，查找所有子物体中的TextMeshProUGUI组件并存入字典。
    /// </summary>
    public void Init()
    {
        // 获取所有子物体中的TextMeshProUGUI组件，包括那些当前未激活的
        TextMeshProUGUI[] allTexts = GetComponentsInChildren<TextMeshProUGUI>(true);

        // 如果需要重新初始化，先清空字典
        TextDisplays.Clear();

        // 遍历所有获取到的TextMeshProUGUI组件
        foreach (TextMeshProUGUI textComponent in allTexts)
        {
            // 检查字典中是否已存在同名的Key，以避免重复添加导致错误
            if (!TextDisplays.ContainsKey(textComponent.name))
            {
                // 将组件的游戏对象名称作为Key，组件本身作为Value，添加到字典中
                TextDisplays.Add(textComponent.name, textComponent);
            }
            else
            {
                // 如果发现重名，打印一个警告信息，方便调试
                Debug.LogWarning($"发现重名的TextMeshPro组件: {textComponent.name}。将跳过添加。");
            }
        }
    }

    /// <summary>
    /// 通过名称更新TextMeshProUGUI组件显示的数值。
    /// </summary>
    /// <param name="textName">TextMeshProUGUI组件所在游戏对象的名称 (作为字典的Key)</param>
    /// <param name="currentValue">要显示的当前值</param>
    /// <param name="maxValue">要显示的最大值</param>
    public void UpdateText(string textName, float currentValue, float maxValue)
    {
        // 尝试从字典中根据名称获取TextMeshProUGUI组件
        if (TextDisplays.TryGetValue(textName, out TextMeshProUGUI textComponent))
        {
            // 如果找到了，就格式化字符串并更新Text的显示内容
            // 幸运的是，TextMesh Pro 和标准 Text 组件的 .text 属性用法完全相同
            textComponent.text = $"{Mathf.RoundToInt(currentValue)}/{Mathf.RoundToInt(maxValue)}";
        }
        else
        {
            // 如果在字典中没找到对应名称的Text，打印错误信息
            Debug.LogError($"未找到名为 '{textName}' 的TextMeshPro组件。");
        }
    }
}