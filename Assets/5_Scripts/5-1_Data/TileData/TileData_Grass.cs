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

    public override void CopyFrom(TileData source)
    {
        if (source is not TileData_Grass grass)
            throw new System.InvalidCastException($"TileData_Grass 无法从 {source?.GetType().Name ?? "null"} 复制");

        CopyBaseFrom(grass);
        if (grass.FertileValue == null)
        {
            FertileValue = null;
            return;
        }

        FertileValue ??= new GameValue_float();
        FertileValue.BaseValue = grass.FertileValue.BaseValue;
        FertileValue.BaseAdditive = grass.FertileValue.BaseAdditive;
        FertileValue.AdditiveModifier = grass.FertileValue.AdditiveModifier;
        FertileValue.MultiplicativeModifier = grass.FertileValue.MultiplicativeModifier;
        FertileValue.FinalAdditive = grass.FertileValue.FinalAdditive;
    }
}
