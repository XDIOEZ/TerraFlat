
using MemoryPack;

[MemoryPackable]
[System.Serializable]
public partial class Nutrition
{
    public float Food = 100;

    public float MaxFood = 100;

    public float Water = 100;

    public float MaxWater = 100;
    //��д+=�������ʵ��Nutrient���ۼ�
    public static Nutrition operator +(Nutrition a, Nutrition b)
    {
        Nutrition result = new Nutrition(0, 0);
        result.Food = a.Food + b.Food;
        return result;
    }
    [MemoryPackConstructor]
    public Nutrition(float Food, float MaxFood)
    {
        this.Food = Food;
        this.MaxFood = MaxFood;

    }
}