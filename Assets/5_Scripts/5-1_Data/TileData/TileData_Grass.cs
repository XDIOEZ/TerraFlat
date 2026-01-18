using MemoryPack;

[System.Serializable]
[MemoryPackable]
public partial class TileData_Grass : TileData
{
    public GameValue_float FertileValue = new GameValue_float();

    public override TileData Clone()
    {
        var copy = (TileData_Grass)MemberwiseClone();
        if (FertileValue != null)
        {
            copy.FertileValue = new GameValue_float
            {
                BaseValue = FertileValue.BaseValue,
                BaseAdditive = FertileValue.BaseAdditive,
                AdditiveModifier = FertileValue.AdditiveModifier,
                MultiplicativeModifier = FertileValue.MultiplicativeModifier,
                FinalAdditive = FertileValue.FinalAdditive
            };
        }
        return copy;
    }
}