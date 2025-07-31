
using MemoryPack;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class Nutrition
{
    [Tooltip("̼ˮ������")]
    public float Carbohydrates = 100;
    public GameValue_float Max_Carbohydrates = new(100); //50
    [Tooltip("֬��")]
    public float Fat = 100;
    public GameValue_float Max_Fat = new(100); //25
    [Tooltip("������")]
    public float Protein = 100;
    public GameValue_float Max_Protein = new(100); //25

    [Tooltip("ˮ")]
    public float Water = 100;
    public GameValue_float Max_Water = new(100);
    [Tooltip("ά����")]
    public float Vitamins = 100;
    public GameValue_float Max_Vitamins = new(100);

    //TODO ����һ������ ���ڴ��Եļ�� ���ڼ���״̬ �ĸ���ռ��
    public float GetHungerRate()
    {
        float rate = 0;

        rate += Carbohydrates / Max_Carbohydrates.Value;

        rate += Fat / Max_Fat.Value;

        rate /= 2;

        return rate;
    }

    //��д+ operator
    public static Nutrition operator +(Nutrition a, Nutrition b)
    {
        Nutrition result = new Nutrition();
        result.Carbohydrates = a.Carbohydrates + b.Carbohydrates;
        result.Protein = a.Protein + b.Protein;
        result.Water = a.Water + b.Water;
        result.Fat = a.Fat + b.Fat;
        result.Vitamins = a.Vitamins + b.Vitamins;
        //ȷ������������a�����ֵ
        result.Carbohydrates = result.Carbohydrates > a.Max_Carbohydrates.Value? a.Max_Carbohydrates.Value : result.Carbohydrates;
        result.Protein = result.Protein > a.Max_Protein.Value? a.Max_Protein.Value : result.Protein;
        result.Water = result.Water > a.Max_Water.Value? a.Max_Water.Value : result.Water;
        result.Fat = result.Fat > a.Max_Fat.Value? a.Max_Fat.Value : result.Fat;
        result.Vitamins = result.Vitamins > a.Max_Vitamins.Value? a.Max_Vitamins.Value : result.Vitamins;
        return result;
    }

    //����һ������ ���µ�ǰֵ �����ֵ
    public void Max()
    {
        Carbohydrates = Max_Carbohydrates.Value;
        Protein = Max_Protein.Value;
        Water = Max_Water.Value;
        Fat = Max_Fat.Value;
        Vitamins = Max_Vitamins.Value;
    }

    [MemoryPackConstructor]
    public Nutrition(float Carbohydrates, float Protein, float Water, float Fat, float Vitamins)
    {
        this.Carbohydrates = Carbohydrates;
        this.Protein = Protein;
        this.Water = Water;
        this.Fat = Fat;
        this.Vitamins = Vitamins;

        Max_Carbohydrates = new GameValue_float(Carbohydrates);
        Max_Protein = new GameValue_float(Protein);
        Max_Water = new GameValue_float(Water);
        Max_Fat = new GameValue_float(Fat);
        Max_Vitamins = new GameValue_float(Vitamins);
    }

    //�հ׹��캯��
    public Nutrition()
    {
    }
}