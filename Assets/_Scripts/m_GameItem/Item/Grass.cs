using MemoryPack;
using NaughtyAttributes;
using UltEvents;
using UnityEngine;
public class Grass : Item, IFood
{
    [SerializeField]
    private FoodData Data;
    public override ItemData Item_Data
    {
        get => Data;
        set
        {
            Data = (FoodData)value;
        }
    }

    // 实现 IHungry 接口的 Foods 属性
    public Hunger_Water Foods
    {
        get => Data.Energy_food; // 如果 Data 未初始化，返回默认值
        set => Data.Energy_food = value;
    }

    // 实现 IHungry 接口的 OnNutrientChanged 事件
    public UltEvent OnNutrientChanged { get; set; } = new UltEvent();

    public override void Act()
    {
        // 限制能量不超过最大值
        Data.Energy_food.Food = Mathf.Min(Data.Energy_food.Food + 1, Data.Energy_food.MaxFood);
        OnNutrientChanged?.Invoke();
    }

    // 实现 IHungry 接口的 BeEat 方法（被吃时减少能量）
    public Hunger_Water BeEat(float eatAmount)
    {
        // 减少能量并触发事件
        Data.Energy_food.Food = Mathf.Max(Data.Energy_food.Food - eatAmount, 0);
        OnNutrientChanged?.Invoke();

        // 如果能量为0，可以销毁草
        if (Data.Energy_food.Food <= 0)
        {
            Destroy(gameObject);
            return Foods;
        }
        return null;
    }
}

public interface IFood
{
    [ShowNativeProperty]
    Hunger_Water Foods { get; set; }

    Hunger_Water BeEat(float BeEatSpeed);
}

