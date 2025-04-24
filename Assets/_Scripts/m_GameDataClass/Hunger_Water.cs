
using MemoryPack;

[MemoryPackable]
[System.Serializable]
public partial class Hunger_Water
{
    public float Food = 100;

    public float MaxFood = 100;

    public float Water = 100;

    public float MaxWater = 100;
    //��д+=�������ʵ��Nutrient���ۼ�
    public static Hunger_Water operator +(Hunger_Water a, Hunger_Water b)
    {
        Hunger_Water result = new Hunger_Water(0, 0);
        result.Food = a.Food + b.Food;
        return result;
    }
    [MemoryPackConstructor]
    public Hunger_Water(float Food, float MaxFood)
    {
        this.Food = Food;
        this.MaxFood = MaxFood;

    }
}