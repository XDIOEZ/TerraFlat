using MemoryPack;
using NaughtyAttributes;
using UltEvents;
using UnityEngine;
public class Grass : Item, IFood
{
    [SerializeField]
    private FoodData Data;
    public override ItemData Item_Data
    {
        get => Data;
        set
        {
            Data = (FoodData)value;
        }
    }

    // ʵ�� IHungry �ӿڵ� Foods ����
    public Hunger_Water Foods
    {
        get => Data.Energy_food; // ��� Data δ��ʼ��������Ĭ��ֵ
        set => Data.Energy_food = value;
    }

    // ʵ�� IHungry �ӿڵ� OnNutrientChanged �¼�
    public UltEvent OnNutrientChanged { get; set; } = new UltEvent();

    public override void Act()
    {
        // �����������������ֵ
        Data.Energy_food.Food = Mathf.Min(Data.Energy_food.Food + 1, Data.Energy_food.MaxFood);
        OnNutrientChanged?.Invoke();
    }

    // ʵ�� IHungry �ӿڵ� BeEat ����������ʱ����������
    public Hunger_Water BeEat(float eatAmount)
    {
        // ���������������¼�
        Data.Energy_food.Food = Mathf.Max(Data.Energy_food.Food - eatAmount, 0);
        OnNutrientChanged?.Invoke();

        // �������Ϊ0���������ٲ�
        if (Data.Energy_food.Food <= 0)
        {
            Destroy(gameObject);
            return Foods;
        }
        return null;
    }
}

public interface IFood
{
    [ShowNativeProperty]
    Hunger_Water Foods { get; set; }

    Hunger_Water BeEat(float BeEatSpeed);
}

