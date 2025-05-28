
using UltEvents;

public interface IHunger
{
    Nutrition Foods { get; set; }

    public float EatingSpeed { get; set; }

    void Eat(IFood food);

    public UltEvent OnNutrientChanged { get; set; }
}
