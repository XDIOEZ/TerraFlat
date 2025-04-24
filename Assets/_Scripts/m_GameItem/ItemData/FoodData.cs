
using MemoryPack;

[System.Serializable]
[MemoryPackable]
public partial class FoodData : ItemData
{
    public Hunger_Water Energy_food = new Hunger_Water(0, 0);
}