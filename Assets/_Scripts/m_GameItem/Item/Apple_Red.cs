using UnityEngine;
using DG.Tweening;
using UltEvents;

// 🍎 红苹果，作为食物的 Item 实现
public class Apple_Red : Item, IFood
{
    // 数据引用
    public FoodData _data;

    // 当前吃掉的数值进度
    public float EatingValue = 0f;

    // 实现 Item 抽象属性
    public override ItemData Item_Data
    {
        get => _data;
        set => _data = (FoodData)value;
    }

    // IFood 实现：能量水分数据
    public Hunger_FoodAndWater Foods
    {
        get => _data.Energy_food;
        set => _data.Energy_food = value;
    }

    // 抛出未实现异常（可扩展）
    public UltEvent OnNutrientChanged
    {
        get => throw new System.NotImplementedException();
        set => throw new System.NotImplementedException();
    }

    public IFood SelfFood
    {
        get => this;
        set => throw new System.NotImplementedException();
    }

    /// <summary>
    /// 调用吃的行为
    /// </summary>
    public override void Act()
    {
        var hunger = BelongItem.GetComponent<IHunger>();
        if (hunger == null) return;

        hunger.Eat(this);


    }

    /// <summary>
    /// 被吃掉逻辑，返回营养值（如果吃完）
    /// </summary>
    public Hunger_FoodAndWater BeEat(float eatSpeed)
    {
        if (_data == null || Foods == null)
            return null;

        EatingValue += eatSpeed;

        // 抖动动画
        SelfFood.ShakeItem(transform);

        if (EatingValue >= Foods.MaxFood)
        {
            // 减少堆叠数量
            Item_Data.Stack.Amount--;

            // UI 更新通知
            UpdatedUI_Event?.Invoke();

            // 营养值补满
            Foods.Food = Foods.MaxFood;
            Foods.Water = Foods.MaxWater;

            EatingValue = 0;

            if (Item_Data.Stack.Amount <= 0)
            {
                Destroy(gameObject); // 吃完销毁
            }

            return Foods;
        }

        return null;
    }

    public void OnDestroy()
    {
        DestroyItem_Event.Invoke();
        transform.DOKill(); // 停止动画
    }
}
