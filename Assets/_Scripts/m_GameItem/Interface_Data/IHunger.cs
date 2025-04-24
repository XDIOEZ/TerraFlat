
using UltEvents;

public interface IHunger
{
    Hunger_Water Foods { get; set; }

    public float EatingSpeed { get; set; }

    void Eat(float EatSpeed);

    public UltEvent OnNutrientChanged { get; set; }
}
