
using UltEvents;

public interface IHunger
{
    Nutrition Foods { get; set; }

    public float EatingSpeed { get; set; }

    public UltEvent OnNutrientChanged { get; }
}
