
using MemoryPack;

[MemoryPackable]
[System.Serializable]
public partial class Hunger_FoodAndWater
{
    public float Food = 100;

    public float MaxFood = 100;

    public float Water = 100;

    public float MaxWater = 100;
    //重写+=运算符，实现Nutrient的累加
    public static Hunger_FoodAndWater operator +(Hunger_FoodAndWater a, Hunger_FoodAndWater b)
    {
        Hunger_FoodAndWater result = new Hunger_FoodAndWater(0, 0);
        result.Food = a.Food + b.Food;
        return result;
    }
    [MemoryPackConstructor]
    public Hunger_FoodAndWater(float Food, float MaxFood)
    {
        this.Food = Food;
        this.MaxFood = MaxFood;

    }
}