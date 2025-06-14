using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;  // ����TextMeshPro�����ռ�

public class UI_DataSliderShow : MonoBehaviour
{
    #region UI���
    [Tooltip("�������б�")]
    public List<SliderAndText> sliders;  // �����Slider��TextMeshProUGUI�����б���
    #endregion

    #region ���ݽӿ�
    [Tooltip("����ֵ���ݽӿ�")]
    public IHealth _health;

    [Tooltip("Ӫ�����ݽӿ�")]
    public IHunger _nutrition;

    [Tooltip("�������ݽӿ�")]
    public IStamina _energy;
    #endregion

    // Start is called before the first frame update
    private void Start()
    {
        _health = GetComponentInParent<IHealth>();
        _nutrition = GetComponentInParent<IHunger>();
        _energy = GetComponentInParent<IStamina>();

        // ��ʼ�������������ֵ������б��С�������±�Խ��
        if (sliders.Count > 0)
        {
            if (sliders.Count > 0) sliders[0].slider.maxValue = _health.Hp.maxValue;  // ����ֵ������
            if (sliders.Count > 1) sliders[1].slider.maxValue = _nutrition.Foods.MaxFood;  // Ӫ��������
            if (sliders.Count > 2) sliders[2].slider.maxValue = _energy.MaxStamina;  // ����������
            if (sliders.Count > 3) sliders[3].slider.maxValue = _nutrition.Foods.MaxWater;  // ˮ�ݽ�����
        }
        else
        {
            Debug.LogWarning("Slider�б��е�Ԫ������3�����޷���ȷ��ʼ����������");
        }

        // ��ʼ���ı�
        UpdateAllSliders();
    }

    #region ���·���
    /// <summary>
    /// ��������ֵ����������ǰֵ�����ֵ��
    /// </summary>
    public void UpdateHpSlider()
    {
        if (sliders.Count > 0 && sliders[0] != null && _health != null)
        {
            sliders[0].slider.value = _health.Hp.Value;  // ��ǰֵ
            sliders[0].slider.maxValue = _health.Hp.maxValue;  // ���ֵ
            sliders[0].text_name.text = $"HP: {Mathf.FloorToInt(sliders[0].slider.value)}/{Mathf.FloorToInt(sliders[0].slider.maxValue)}";
        }
    }

    /// <summary>
    /// ����Ӫ������������ǰֵ�����ֵ��
    /// </summary>
    public void UpdateNutritionSlider()
    {
        if (sliders.Count > 1 && sliders[1] != null && _nutrition != null)
        {
            sliders[1].slider.value = _nutrition.Foods.Food;  // ��ǰֵ
            sliders[1].slider.maxValue = _nutrition.Foods.MaxFood;  // ���ֵ
            sliders[1].text_name.text = $"Nutrition: {Mathf.FloorToInt(sliders[1].slider.value)}/{Mathf.FloorToInt(sliders[1].slider.maxValue)}";
        }
    }

    /// <summary>
    /// ���¾�������������ǰֵ�����ֵ��
    /// </summary>
    public void UpdateEnergySlider()
    {
        if (sliders.Count > 2 && sliders[2] != null && _energy != null)
        {
            sliders[2].slider.value = _energy.Stamina;  // ��ǰֵ
            sliders[2].slider.maxValue = _energy.MaxStamina;  // ���ֵ
            sliders[2].text_name.text = $"Energy: {Mathf.FloorToInt(sliders[2].slider.value)}/{Mathf.FloorToInt(sliders[2].slider.maxValue)}";
        }
    }

    //����ˮ�ݽ���������ǰֵ�����ֵ��
    public void UpdateWaterSlider()
    {
        if (sliders.Count > 3 && sliders[3] != null && _nutrition.Foods.Water != 0)
        {
            sliders[3].slider.value = _nutrition.Foods.Water;  // ��ǰֵ
            sliders[3].slider.maxValue = _nutrition.Foods.MaxWater;  // ���ֵ
            sliders[3].text_name.text = $"Water: {Mathf.FloorToInt(sliders[3].slider.value)}/{Mathf.FloorToInt(sliders[3].slider.maxValue)}";
        }
    }


    /// <summary>
    /// �������н�����
    /// </summary>
    public void UpdateAllSliders()
    {
        UpdateHpSlider();
        UpdateNutritionSlider();
        UpdateEnergySlider();
        UpdateWaterSlider();
    }
    #endregion

    public void FixedUpdate()
    {
        if (_health == null || _nutrition == null || _energy == null)
        {
            return;
        }

        UpdateAllSliders();
    }

    [System.Serializable]
    public class SliderAndText
    {
        public Slider slider;  // ������
        public TextMeshProUGUI text_name; // ��ʾ�ı�����ΪTextMeshProUGUI��
    }
}
