public interface IBlockTile
{
    public TileData TileData { get; set; }
    public void Tile_Enter(Item item, TileData tileData);
    public void Tile_Update(Item item, TileData tileData);
    public void Tile_Exit(Item item, TileData tileData);
}

