/*using UnityEngine;
using UnityEngine.UI;

public class ModuleValueDisplay : MonoBehaviour
{
    public string ModuleName;
    public Item item;
    public Slider slider;
    public IUI_Slider uiSlider_Data;
    public Module module;

    private float lastMaxValue;

    void Start()
    {
        if (slider == null)
        {
            slider = GetComponentInChildren<Slider>();
        }

        if (item == null)
        {
            item = GetComponentInParent<Item>();
        }

        if (item != null && item.Mods.ContainsKey(ModuleName))
        {
            module = item.Mods[ModuleName];

            if (module is IUI_Slider)
            {
                uiSlider_Data = module as IUI_Slider;

                if (slider != null && uiSlider_Data != null)
                {
                    slider.maxValue = uiSlider_Data.MaxValue;
                    slider.value = uiSlider_Data.CurrentValue;
                    lastMaxValue = uiSlider_Data.MaxValue;
                }
            }
            else
            {
                Debug.LogWarning($"模块 {ModuleName} 没有实现 IUI_Slider 接口。");
            }
        }
        else
        {
            Debug.LogWarning($"Item 未找到模块 {ModuleName}，请检查配置。");
        }
    }

    void Update()
    {
        if (uiSlider_Data != null && slider != null)
        {
            // 只有最大值发生变化时才更新，避免不必要的赋值
            if (slider.maxValue != uiSlider_Data.MaxValue)
            {
                slider.maxValue = uiSlider_Data.MaxValue;
            }

            slider.value = uiSlider_Data.CurrentValue;
        }
    }
}
*/