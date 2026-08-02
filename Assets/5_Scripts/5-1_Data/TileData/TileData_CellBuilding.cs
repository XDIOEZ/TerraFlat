using MemoryPack;

/// <summary>
/// 墙壁等非工作方块的单格运行时数据。
/// 定义数据（最大生命、防御、掉落等）保存在 Tile_Block，格子里只保存会变化的当前生命。
/// </summary>
[System.Serializable]
[MemoryPackable]
public partial class TileData_CellBuilding : TileData
{
    public const int CurrentVersion = 1;

    public int Version = CurrentVersion;
    public float CurrentHp;

    public override TileData Clone()
    {
        return (TileData_CellBuilding)MemberwiseClone();
    }

    public static TileData_CellBuilding FromTile(TileData source, float currentHp)
    {
        if (source == null)
            return null;

        return new TileData_CellBuilding
        {
            Version = CurrentVersion,
            CurrentHp = currentHp,
            ID = source.ID,
            Name = source.Name,
            TileTag = source.TileTag,
            position = source.position,
            DemolitionTime = source.DemolitionTime,
            workTime = source.workTime,
            Penalty = source.Penalty,
            IsWalkable = source.IsWalkable
        };
    }
}
