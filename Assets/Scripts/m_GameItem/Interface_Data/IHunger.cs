
using UltEvents;

public interface IHunger
{
    Nutrition Nutrition { get; set; }

    public float EatingSpeed { get; set; }

    public UltEvent OnNutrientChanged { get; }
}
