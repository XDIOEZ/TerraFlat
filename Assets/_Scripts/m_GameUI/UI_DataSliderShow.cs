using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;  // 引入TextMeshPro命名空间

public class UI_DataSliderShow : MonoBehaviour
{
    #region UI组件
    [Tooltip("进度条列表")]
    public List<SliderAndText> sliders;  // 将多个Slider和TextMeshProUGUI放入列表中
    #endregion

    #region 数据接口
    [Tooltip("生命值数据接口")]
    public IHealth _health;

    [Tooltip("营养数据接口")]
    public IHunger _nutrition;

    [Tooltip("精力数据接口")]
    public IStamina _energy;
    #endregion

    // Start is called before the first frame update
    private void Start()
    {
        _health = GetComponentInParent<IHealth>();
        _nutrition = GetComponentInParent<IHunger>();
        _energy = GetComponentInParent<IStamina>();

        // 初始化进度条的最大值，检查列表大小，避免下标越界
        if (sliders.Count > 0)
        {
            if (sliders.Count > 0) sliders[0].slider.maxValue = _health.Hp.maxValue;  // 生命值进度条
            if (sliders.Count > 1) sliders[1].slider.maxValue = _nutrition.Foods.MaxFood;  // 营养进度条
            if (sliders.Count > 2) sliders[2].slider.maxValue = _energy.MaxStamina;  // 精力进度条
            if (sliders.Count > 3) sliders[3].slider.maxValue = _nutrition.Foods.MaxWater;  // 水份进度条
        }
        else
        {
            Debug.LogWarning("Slider列表中的元素少于3个，无法正确初始化进度条。");
        }

        // 初始化文本
        UpdateAllSliders();
    }

    #region 更新方法
    /// <summary>
    /// 更新生命值进度条（当前值和最大值）
    /// </summary>
    public void UpdateHpSlider()
    {
        if (sliders.Count > 0 && sliders[0] != null && _health != null)
        {
            sliders[0].slider.value = _health.Hp.Value;  // 当前值
            sliders[0].slider.maxValue = _health.Hp.maxValue;  // 最大值
            sliders[0].text_name.text = $"HP: {Mathf.FloorToInt(sliders[0].slider.value)}/{Mathf.FloorToInt(sliders[0].slider.maxValue)}";
        }
    }

    /// <summary>
    /// 更新营养进度条（当前值和最大值）
    /// </summary>
    public void UpdateNutritionSlider()
    {
        if (sliders.Count > 1 && sliders[1] != null && _nutrition != null)
        {
            sliders[1].slider.value = _nutrition.Foods.Food;  // 当前值
            sliders[1].slider.maxValue = _nutrition.Foods.MaxFood;  // 最大值
            sliders[1].text_name.text = $"Nutrition: {Mathf.FloorToInt(sliders[1].slider.value)}/{Mathf.FloorToInt(sliders[1].slider.maxValue)}";
        }
    }

    /// <summary>
    /// 更新精力进度条（当前值和最大值）
    /// </summary>
    public void UpdateEnergySlider()
    {
        if (sliders.Count > 2 && sliders[2] != null && _energy != null)
        {
            sliders[2].slider.value = _energy.Stamina;  // 当前值
            sliders[2].slider.maxValue = _energy.MaxStamina;  // 最大值
            sliders[2].text_name.text = $"Energy: {Mathf.FloorToInt(sliders[2].slider.value)}/{Mathf.FloorToInt(sliders[2].slider.maxValue)}";
        }
    }

    //更新水份进度条（当前值和最大值）
    public void UpdateWaterSlider()
    {
        if (sliders.Count > 3 && sliders[3] != null && _nutrition.Foods.Water != 0)
        {
            sliders[3].slider.value = _nutrition.Foods.Water;  // 当前值
            sliders[3].slider.maxValue = _nutrition.Foods.MaxWater;  // 最大值
            sliders[3].text_name.text = $"Water: {Mathf.FloorToInt(sliders[3].slider.value)}/{Mathf.FloorToInt(sliders[3].slider.maxValue)}";
        }
    }


    /// <summary>
    /// 更新所有进度条
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
        public Slider slider;  // 进度条
        public TextMeshProUGUI text_name; // 显示文本（改为TextMeshProUGUI）
    }
}
