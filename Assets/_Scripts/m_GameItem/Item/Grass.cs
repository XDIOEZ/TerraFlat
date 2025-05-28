using DG.Tweening;
using log4net.Util;
using MemoryPack;
using NaughtyAttributes;
using UltEvents;
using UnityEngine;
using Transform = UnityEngine.Transform;
public class Grass : Item, IFood
{
    [SerializeField]
    private Data_Food Data;
    public override ItemData Item_Data
    {
        get => Data;
        set => Data = (Data_Food)value;
    }
    // 实现 IHungry 接口的 Foods 属性
    public Nutrition NutritionData
    {
        get => Data.NutritionData; // 如果 Data 未初始化，返回默认值
        set => Data.NutritionData = value;
    }
    public float EatingValue = 0;
    public IFood SelfFood { get => this; set => throw new System.NotImplementedException(); }


    // 实现 IHungry 接口的 OnNutrientChanged 事件
    public UltEvent OnNutrientChanged { get; set; } = new UltEvent();

    public override void Act()
    {
        // 限制能量不超过最大值
        Data.NutritionData.Food = Mathf.Min(Data.NutritionData.Food + 1, Data.NutritionData.MaxFood);
        OnNutrientChanged?.Invoke();
    }

    public Nutrition BeEat(float eatSpeed)
    {
        if (Item_Data == null || NutritionData == null) return null;

        // 使用 DOTween 做抖动动画
        SelfFood.ShakeItem(this.transform);

        EatingValue += eatSpeed;
        if (EatingValue >= NutritionData.MaxFood)
        {
            Item_Data.Stack.Amount--;

            UpdatedUI_Event?.Invoke();

            NutritionData.Food = NutritionData.MaxFood;
            NutritionData.Water = NutritionData.MaxWater;

            EatingValue = 0;

            if (Item_Data.Stack.Amount <= 0)
            {
                //停止DoTween动画
                transform.DOKill();
                Destroy(gameObject);
            }
            return NutritionData;
        }
        return null;
    }
}


