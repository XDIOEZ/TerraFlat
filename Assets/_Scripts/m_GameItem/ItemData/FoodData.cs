
using MemoryPack;

[System.Serializable]
[MemoryPackable]
public partial class FoodData : ItemData
{
    public Hunger_FoodAndWater Energy_food = new Hunger_FoodAndWater(0, 0);
}