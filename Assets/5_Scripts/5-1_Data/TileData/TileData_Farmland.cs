using MemoryPack;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class TileData_Farmland : TileData
{
    #region 水肥状态

    public const float DefaultMaxFertility = 1f;
    public const int MaxWaterLevel = 10;

    public GameValue_float fertilityValue = new GameValue_float(1f); // 肥力，作为生长倍率基础
    public int waterValue = 4; // 当前水分只保存 0～10 整级。
    public float waterSupplyRemainder; // 降水的不足一份进度独立累计。
    public float waterConsumptionRemainder; // 所有作物共用不足一份的耗水进度。

    [MemoryPackIgnore]
    public int maxWater => MaxWaterLevel;

    [MemoryPackIgnore]
    public float Fertility => fertilityValue?.Value ?? 0f;

    [MemoryPackIgnore]
    public float maxFertility => DefaultMaxFertility;

    public void NormalizeValues()
    {
        fertilityValue ??= new GameValue_float(0f);
        waterValue = Mathf.Clamp(waterValue, 0, MaxWaterLevel);
        waterSupplyRemainder = waterValue >= MaxWaterLevel ? 0f : NormalizeRemainder(waterSupplyRemainder);
        waterConsumptionRemainder = waterValue <= 0 ? 0f : NormalizeRemainder(waterConsumptionRemainder);
        SetFertility(Mathf.Clamp(Fertility, 0f, maxFertility));
    }

    public void AddWater(int amount)
    {
        if (amount < 0)
            throw new System.ArgumentOutOfRangeException(nameof(amount));

        NormalizeValues();
        waterValue += Mathf.Min(MaxWaterLevel - waterValue, amount);
        if (waterValue >= MaxWaterLevel) waterSupplyRemainder = 0f;
    }

    public void ConsumeWater(int amount)
    {
        if (amount < 0)
            throw new System.ArgumentOutOfRangeException(nameof(amount));

        NormalizeValues();
        waterValue -= Mathf.Min(waterValue, amount);
        if (waterValue <= 0) waterConsumptionRemainder = 0f;
    }

    /// <summary>连续降水累计满一份后才增加一级，满水后的多余进度直接丢弃。</summary>
    public void AccumulateWaterSupply(float amount)
    {
        if (!float.IsFinite(amount) || amount < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(amount));
        NormalizeValues();
        TryAccumulateWaterSupply(waterValue, waterSupplyRemainder, amount,
            out waterValue, out waterSupplyRemainder);
    }

    /// <summary>连续耗水共用地块进度，避免多次 Tick 各自取整后永远不扣水。</summary>
    public void AccumulateWaterConsumption(float amount)
    {
        if (!float.IsFinite(amount) || amount < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(amount));
        NormalizeValues();
        float pending = waterConsumptionRemainder + amount;
        if (pending >= waterValue)
        {
            waterValue = 0;
            waterConsumptionRemainder = 0f;
            return;
        }
        // 抹平分段累计的浮点尾差，避免整天两格停在一格加 0.999999。
        int levels = Mathf.Min(waterValue, Mathf.FloorToInt(pending + 0.00001f));
        waterValue -= levels;
        waterConsumptionRemainder = waterValue <= 0 ? 0f : Mathf.Max(0f, pending - levels);
    }

    /// <summary>单格与批量降水共用整级结算，不把时间进度当作真实水分。</summary>
    public static bool TryAccumulateWaterSupply(int water, float remainder, float amount,
        out int nextWater, out float nextRemainder)
    {
        nextWater = water;
        nextRemainder = remainder;
        if (!float.IsFinite(amount) || amount <= 0f || water >= MaxWaterLevel) return false;
        float pending = NormalizeRemainder(remainder) + amount;
        int missing = MaxWaterLevel - Mathf.Clamp(water, 0, MaxWaterLevel);
        if (pending >= missing)
        {
            nextWater = MaxWaterLevel;
            nextRemainder = 0f;
        }
        else
        {
            int levels = Mathf.FloorToInt(pending);
            nextWater = Mathf.Clamp(water, 0, MaxWaterLevel) + levels;
            nextRemainder = pending - levels;
        }
        return nextWater != water || nextRemainder != remainder;
    }

    private static float NormalizeRemainder(float value) =>
        float.IsFinite(value) ? Mathf.Clamp(value, 0f, 0.99999994f) : 0f;

    #endregion

    #region 肥力与副本

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

    public override void CopyFrom(TileData source)
    {
        if (source is not TileData_Farmland farmland)
            throw new System.InvalidCastException($"TileData_Farmland 无法从 {source?.GetType().Name ?? "null"} 复制");

        CopyBaseFrom(farmland);
        waterValue = farmland.waterValue;
        waterSupplyRemainder = farmland.waterSupplyRemainder;
        waterConsumptionRemainder = farmland.waterConsumptionRemainder;
        if (farmland.fertilityValue == null)
        {
            fertilityValue = null;
            return;
        }

        fertilityValue ??= new GameValue_float();
        fertilityValue.BaseValue = farmland.fertilityValue.BaseValue;
        fertilityValue.BaseAdditive = farmland.fertilityValue.BaseAdditive;
        fertilityValue.AdditiveModifier = farmland.fertilityValue.AdditiveModifier;
        fertilityValue.MultiplicativeModifier = farmland.fertilityValue.MultiplicativeModifier;
        fertilityValue.FinalAdditive = farmland.fertilityValue.FinalAdditive;
    }

    #endregion
}
