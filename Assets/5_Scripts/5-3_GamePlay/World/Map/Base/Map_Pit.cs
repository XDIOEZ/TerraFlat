using UnityEngine;

public class Map_Pit : Map
{
    public override void Load()
    {
        chunk = GetComponentInParent<Chunk>();
        chunk.Map = this;
        chunk.ResetLifecycleState();
        Data.TileLoaded = true;
        LoadTileData_To_TileMap_Ansync();
    }

    protected override int TilemapLoadBatchSize => 500;
}
