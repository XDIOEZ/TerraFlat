using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class TileData_Farmland : TileData
{
    public const float DefaultMaxFertility = 1f;

    public GameValue_float fertilityValue = new GameValue_float(1f); // 肥力，作为生长倍率基础
    public float waterValue = 40f; // 当前水分
    public float maxWater = 100f; // 最大水分

    [MemoryPackIgnore]
    public float Fertility => fertilityValue?.Value ?? 0f;

    [MemoryPackIgnore]
    public float maxFertility => DefaultMaxFertility;

    public void NormalizeValues()
    {
        fertilityValue ??= new GameValue_float(0f);
        maxWater = Mathf.Max(0.01f, maxWater);
        waterValue = Mathf.Clamp(waterValue, 0f, maxWater);
        SetFertility(Mathf.Clamp(Fertility, 0f, maxFertility));
    }

    public void AddWater(float amount)
    {
        if (amount < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(amount));

        NormalizeValues();
        waterValue = Mathf.Min(maxWater, waterValue + amount);
    }

    public void ConsumeWater(float amount)
    {
        if (amount < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(amount));

        NormalizeValues();
        waterValue = Mathf.Max(0f, waterValue - amount);
    }

    public void AddFertility(float amount)
    {
        if (amount < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(amount));

        NormalizeValues();
        SetFertility(Mathf.Min(maxFertility, Fertility + amount));
    }

    public void ConsumeFertility(float amount)
    {
        if (amount < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(amount));

        NormalizeValues();
        SetFertility(Mathf.Max(0f, Fertility - amount));
    }

    private void SetFertility(float value)
    {
        fertilityValue ??= new GameValue_float();
        float multiplier = (1f + fertilityValue.AdditiveModifier) * fertilityValue.MultiplicativeModifier;
        if (Mathf.Abs(multiplier) < 0.0001f)
        {
            fertilityValue.BaseAdditive = 0f;
            fertilityValue.AdditiveModifier = 0f;
            fertilityValue.MultiplicativeModifier = 1f;
            fertilityValue.FinalAdditive = 0f;
            fertilityValue.BaseValue = value;
            return;
        }

        fertilityValue.BaseValue =
            (value - fertilityValue.FinalAdditive) / multiplier - fertilityValue.BaseAdditive;
    }

    public override TileData Clone()
    {
        var copy = (TileData_Farmland)MemberwiseClone();
        if (fertilityValue != null)
        {
            copy.fertilityValue = new GameValue_float
            {
                BaseValue = fertilityValue.BaseValue,
                BaseAdditive = fertilityValue.BaseAdditive,
                AdditiveModifier = fertilityValue.AdditiveModifier,
                MultiplicativeModifier = fertilityValue.MultiplicativeModifier,
                FinalAdditive = fertilityValue.FinalAdditive
            };
        }
        return copy;
    }
}
