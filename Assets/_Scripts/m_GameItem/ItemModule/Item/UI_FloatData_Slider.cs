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
        // ��ȡ�����������е�Slider���������δ�����
        Slider[] sliders = GetComponentsInChildren<Slider>(true);

        // ����ֵ䣨�����Ҫ���³�ʼ����
        Sliders.Clear();

        // ������ȡ����Slider���
        foreach (Slider slider in sliders)
        {
            // ����Slider���ɽ�������ֹ�����ק����
            slider.interactable = false; // <-- �����Ĵ�����

            // ����ֵ����Ƿ��Ѵ���ͬ����Key���Ա����ظ����
            if (!Sliders.ContainsKey(slider.name))
            {
                // ��Slider��name��ΪKey��Slider�����ΪValue��ӵ��ֵ���
                Sliders.Add(slider.name, slider);
            }
            else
            {
                Debug.LogWarning($"Duplicate slider name found: {slider.name}. Skipping addition to dictionary.");
            }
        }
    }

    // ʾ�������ͨ�����ƻ�ȡSlider��ֵ
    public float GetSliderValue(string sliderName)
    {
        if (Sliders.TryGetValue(sliderName, out Slider slider))
        {
            return slider.value;
        }
        else
        {
            Debug.LogError($"Slider with name '{sliderName}' not found.");
            return 0f; // ���߷���һ��Ĭ��ֵ��float.NaN
        }
    }

    // ʾ�������ͨ����������Slider��ֵ
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