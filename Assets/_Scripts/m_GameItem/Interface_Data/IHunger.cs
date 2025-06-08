
using UltEvents;

public interface IHunger
{
    Nutrition Foods { get; set; }

    public float EatingSpeed { get; set; }

    void TakeABite(IFood food);

    public UltEvent OnNutrientChanged { get; set; }
}
