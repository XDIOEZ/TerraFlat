using UnityEngine;
using DG.Tweening;
using UltEvents;

// 🍎 红苹果，作为食物的 Item 实现
public class Apple_Red : Item, IFood
{
    // 数据引用
    public Data_Creature data;

    // 当前吃掉的数值进度
    public float EatingProgress = 0f;

    // 实现 Item 抽象属性
    public override ItemData Item_Data
    {
        get => data;
        set => data = (Data_Creature)value;
    }

    // IFood 实现：能量水分数据
    public Nutrition NutritionData
    {
        get => data.NutritionData;
        set => data.NutritionData = value;
    }

    public IFood SelfFood => this;
 

    /// <summary>
    /// 调用吃的行为
    /// </summary>
    public override void Act()
    {
        var hunger = BelongItem.GetComponentInChildren<FoodEater>();
        if (hunger == null) return;
        hunger.Eat(this);
    }

    /// <summary>
    /// 被吃掉逻辑，返回营养值（如果吃完）
    /// </summary>
    public Nutrition BeEat(float eatSpeed)
    {
        if (data == null || NutritionData == null)
            return null;

        EatingProgress += eatSpeed;

        // 抖动动画
        SelfFood.ShakeItem(transform);

        if (EatingProgress >= NutritionData.MaxFood)
        {
            // 减少堆叠数量
            Item_Data.Stack.Amount--;

            // UI 更新通知
            UpdatedUI_Event?.Invoke();

            // 营养值补满
            NutritionData.Food = NutritionData.MaxFood;
            NutritionData.Water = NutritionData.MaxWater;

            EatingProgress = 0;

            if (Item_Data.Stack.Amount <= 0)
            {
                Destroy(gameObject); // 吃完销毁
            }

            return NutritionData;
        }

        return null;
    }

    public void OnDestroy()
    {
        DestroyItem_Event.Invoke();
        transform.DOKill(); // 停止动画
    }
}
