using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UI_FloatData_Slider : MonoBehaviour
{
    public Dictionary<string, Slider> Sliders = new Dictionary<string, Slider>();

    void Awake()
    {
        Init();
    }

    public void Init()
    {
        // 获取所有子物体中的Slider组件，包括未激活的
        Slider[] sliders = GetComponentsInChildren<Slider>(true);

        // 清空字典（如果需要重新初始化）
        Sliders.Clear();

        // 遍历获取到的Slider组件
        foreach (Slider slider in sliders)
        {
            // 设置Slider不可交互，禁止玩家拖拽控制
            slider.interactable = false; // <-- 新增的代码行

            // 检查字典中是否已存在同名的Key，以避免重复添加
            if (!Sliders.ContainsKey(slider.name))
            {
                // 将Slider的name作为Key，Slider组件作为Value添加到字典中
                Sliders.Add(slider.name, slider);
            }
            else
            {
                Debug.LogWarning($"Duplicate slider name found: {slider.name}. Skipping addition to dictionary.");
            }
        }
    }

    // 示例：如何通过名称获取Slider的值
    public float GetSliderValue(string sliderName)
    {
        if (Sliders.TryGetValue(sliderName, out Slider slider))
        {
            return slider.value;
        }
        else
        {
            Debug.LogError($"Slider with name '{sliderName}' not found.");
            return 0f; // 或者返回一个默认值或float.NaN
        }
    }

    // 示例：如何通过名称设置Slider的值
    public void SetSliderValue(string sliderName, float value)
    {
        if (Sliders.TryGetValue(sliderName, out Slider slider))
        {
            slider.value = value;
        }
        else
        {
            Debug.LogError($"Slider with name '{sliderName}' not found.");
        }
    }
}