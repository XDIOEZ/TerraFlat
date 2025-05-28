using DG.Tweening;
using log4net.Util;
using MemoryPack;
using NaughtyAttributes;
using UltEvents;
using UnityEngine;
using Transform = UnityEngine.Transform;
public class Grass : Item, IFood
{
    [SerializeField]
    private Data_Food Data;
    public override ItemData Item_Data
    {
        get => Data;
        set => Data = (Data_Food)value;
    }
    // ʵ�� IHungry �ӿڵ� Foods ����
    public Nutrition NutritionData
    {
        get => Data.NutritionData; // ��� Data δ��ʼ��������Ĭ��ֵ
        set => Data.NutritionData = value;
    }
    public float EatingValue = 0;
    public IFood SelfFood { get => this; set => throw new System.NotImplementedException(); }


    // ʵ�� IHungry �ӿڵ� OnNutrientChanged �¼�
    public UltEvent OnNutrientChanged { get; set; } = new UltEvent();

    public override void Act()
    {
        // �����������������ֵ
        Data.NutritionData.Food = Mathf.Min(Data.NutritionData.Food + 1, Data.NutritionData.MaxFood);
        OnNutrientChanged?.Invoke();
    }

    public Nutrition BeEat(float eatSpeed)
    {
        if (Item_Data == null || NutritionData == null) return null;

        // ʹ�� DOTween ����������
        SelfFood.ShakeItem(this.transform);

        EatingValue += eatSpeed;
        if (EatingValue >= NutritionData.MaxFood)
        {
            Item_Data.Stack.Amount--;

            UpdatedUI_Event?.Invoke();

            NutritionData.Food = NutritionData.MaxFood;
            NutritionData.Water = NutritionData.MaxWater;

            EatingValue = 0;

            if (Item_Data.Stack.Amount <= 0)
            {
                //ֹͣDoTween����
                transform.DOKill();
                Destroy(gameObject);
            }
            return NutritionData;
        }
        return null;
    }
}


