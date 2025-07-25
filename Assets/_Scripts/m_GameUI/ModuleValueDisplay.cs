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
                Debug.LogWarning($"ģ�� {ModuleName} û��ʵ�� IUI_Slider �ӿڡ�");
            }
        }
        else
        {
            Debug.LogWarning($"Item δ�ҵ�ģ�� {ModuleName}���������á�");
        }
    }

    void Update()
    {
        if (uiSlider_Data != null && slider != null)
        {
            // ֻ�����ֵ�����仯ʱ�Ÿ��£����ⲻ��Ҫ�ĸ�ֵ
            if (slider.maxValue != uiSlider_Data.MaxValue)
            {
                slider.maxValue = uiSlider_Data.MaxValue;
            }

            slider.value = uiSlider_Data.CurrentValue;
        }
    }
}
*/