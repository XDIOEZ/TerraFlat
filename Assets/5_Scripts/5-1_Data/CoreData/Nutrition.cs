
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class Nutrition
{
    [Tooltip("碳水化合物")]
    public float Carbohydrates = 500;
    [Tooltip("碳水化合物容纳上限（用于比例计算和进食约束）")]
    public float Max_Carbohydrates = 500;


    [Tooltip("脂肪")]
    public float Fat = 500;
    [Tooltip("脂肪容纳上限（用于比例计算和进食约束）")]
    public float Max_Fat = 500;


    [Tooltip("蛋白质")]
    public float Protein = 500;
    [Tooltip("蛋白质容纳上限（用于比例计算和进食约束）")]
    public float Max_Protein = 500;


    [Tooltip("水")]
    public float Water = 500;
    [Tooltip("水容纳上限（用于比例计算和进食约束）")]
    public float Max_Water = 500;
    [Tooltip("维生素")]
    public float Vitamins = 500;
    [Tooltip("维生素容纳上限（用于比例计算和进食约束）")]
    public float Max_Vitamins = 500;
    //TODO 创建一个方法 用于粗略的检测 处于饥饿状态 的概率占比
    public float GetFoodRate()
    {
        float rate = 0;

        if (Max_Carbohydrates > 0)
            rate += Carbohydrates / Max_Carbohydrates;

        if (Max_Fat > 0)
            rate += Fat / Max_Fat;

        rate /= 2;

        return rate;
    }

    //重写+ operator
    public static Nutrition operator +(Nutrition a, Nutrition b)
    {
        if (a == null)
            return b;
        if (b == null)
            return a;

        // 营养增减只改变当前值；容量上限始终继承被食用者，不能被临时食物或 Buff 重置。
        return new Nutrition
        {
            Max_Carbohydrates = a.Max_Carbohydrates,
            Max_Protein = a.Max_Protein,
            Max_Water = a.Max_Water,
            Max_Fat = a.Max_Fat,
            Max_Vitamins = a.Max_Vitamins,
            Carbohydrates = Mathf.Clamp(a.Carbohydrates + b.Carbohydrates, 0f, a.Max_Carbohydrates),
            Protein = Mathf.Clamp(a.Protein + b.Protein, 0f, a.Max_Protein),
            Water = Mathf.Clamp(a.Water + b.Water, 0f, a.Max_Water),
            Fat = Mathf.Clamp(a.Fat + b.Fat, 0f, a.Max_Fat),
            Vitamins = Mathf.Clamp(a.Vitamins + b.Vitamins, 0f, a.Max_Vitamins)
        };
    }

    //新增一个方法 更新最大值 到当前值
    [Button("更新最大值到当前值")]
    public void UpdateMaxToCurrent()
    {
        Max_Carbohydrates = Carbohydrates;
        Max_Protein = Protein;
        Max_Water = Water;
        Max_Fat = Fat;
        Max_Vitamins = Vitamins;
    }

    //新增一个方法 更新当前值 到最大值
    public void Max()
    {
        Carbohydrates = Max_Carbohydrates;
        Protein = Max_Protein;
        Water = Max_Water;
        Fat = Max_Fat;
        Vitamins = Max_Vitamins;
    }

    [MemoryPackConstructor]
    public Nutrition(float Carbohydrates, float Protein, float Water, float Fat, float Vitamins)
    {
        this.Carbohydrates = Carbohydrates;
        this.Protein = Protein;
        this.Water = Water;
        this.Fat = Fat;
        this.Vitamins = Vitamins;

        Max_Carbohydrates = Carbohydrates;
        Max_Protein = Protein;
        Max_Water = Water;
        Max_Fat = Fat;
        Max_Vitamins = Vitamins;
    }

    //空白构造函数
    public Nutrition()
    {
    }

    /// <summary>
    /// 按「碳水 -> 脂肪 -> 蛋白质」顺序消耗能量。
    /// 返回 true 表示成功满足本次能量需求；false 表示总量不足，不执行扣减。
    /// </summary>
    public bool TryConsumeEnergy(float energy)
    {
        if (energy <= 0f)
            return true;

        float totalEnergy = Carbohydrates + Fat + Protein;
        if (totalEnergy < energy)
            return false;

        float remain = energy;

        float consumeCarbohydrates = Mathf.Min(Carbohydrates, remain);
        Carbohydrates -= consumeCarbohydrates;
        remain -= consumeCarbohydrates;

        if (remain <= 0f)
            return true;

        float consumeFat = Mathf.Min(Fat, remain);
        Fat -= consumeFat;
        remain -= consumeFat;

        if (remain <= 0f)
            return true;

        float consumeProtein = Mathf.Min(Protein, remain);
        Protein -= consumeProtein;
        remain -= consumeProtein;

        return remain <= 0f;
    }

}
