using MemoryPack;

[System.Serializable]
[MemoryPackable]
public partial class TileData_Farmland : TileData
{
    public GameValue_float fertilityValue = new GameValue_float(1f); // 肥力，作为生长倍率基础
    public float waterValue = 40f; // 当前水分
    public float maxWater = 100f; // 最大水分

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
