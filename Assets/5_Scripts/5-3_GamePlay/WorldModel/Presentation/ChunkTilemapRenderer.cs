using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class ChunkTilemapRenderer : MonoBehaviour, IChunkViewRenderer
{
    [SerializeField] private ChunkTilePaletteSO palette;
    [SerializeField] private Tilemap groundTilemap;
    [SerializeField] private Tilemap backTilemap;
    [SerializeField] private Tilemap blockingTilemap;

    private ChunkRuntime boundChunk;

    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new System.ArgumentNullException(nameof(chunk));
        if (chunk.Terrain == null)
            throw new System.InvalidOperationException("Cannot bind terrain rendering before data is ready.");
        if (ReferenceEquals(boundChunk, chunk))
            return;

        Unbind();
        boundChunk = chunk;
        boundChunk.Terrain.Changed += HandleTerrainChanged;
        Render(chunk.Terrain);
    }

    public void Unbind()
    {
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
        if (groundTilemap != null)
            groundTilemap.ClearAllTiles();
        if (backTilemap != null)
            backTilemap.ClearAllTiles();
        if (blockingTilemap != null)
            blockingTilemap.ClearAllTiles();
        boundChunk = null;
    }

    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (boundChunk?.Terrain == null)
            return;
        RenderCell(boundChunk.Terrain, changed.LocalCell.X, changed.LocalCell.Y);
    }

    private void Render(ChunkTerrainData terrain)
    {
        if (terrain == null)
            throw new System.InvalidOperationException("Cannot bind a ChunkView before data is ready.");
        if (palette == null)
            throw new System.InvalidOperationException("ChunkTilePaletteSO is not assigned.");

        int count = terrain.CellCount;
        var ground = groundTilemap != null ? new TileBase[count] : null;
        var back = backTilemap != null ? new TileBase[count] : null;
        var blocking = blockingTilemap != null ? new TileBase[count] : null;
        for (int y = 0; y < terrain.Height; y++)
        {
            for (int x = 0; x < terrain.Width; x++)
            {
                int index = y * terrain.Width + x;
                TerrainCell cell = terrain.GetCell(x, y);
                if (ground != null && cell.GroundTileId != 0)
                    palette.TryGetTile(cell.GroundTileId, out ground[index]);
                if (back != null && cell.BackTileId != 0)
                    palette.TryGetTile(cell.BackTileId, out back[index]);
                if (blocking != null && cell.BlockingTileId != 0)
                    palette.TryGetTile(cell.BlockingTileId, out blocking[index]);
            }
        }

        var bounds = new BoundsInt(0, 0, 0, terrain.Width, terrain.Height, 1);
        if (ground != null)
            groundTilemap.SetTilesBlock(bounds, ground);
        if (back != null)
            backTilemap.SetTilesBlock(bounds, back);
        if (blocking != null)
            blockingTilemap.SetTilesBlock(bounds, blocking);
    }

    private void RenderCell(ChunkTerrainData terrain, int x, int y)
    {
        TerrainCell cell = terrain.GetCell(x, y);
        Vector3Int position = new(x, y, 0);
        if (groundTilemap != null)
            groundTilemap.SetTile(position, Resolve(cell.GroundTileId));
        if (backTilemap != null)
            backTilemap.SetTile(position, Resolve(cell.BackTileId));
        if (blockingTilemap != null)
            blockingTilemap.SetTile(position, Resolve(cell.BlockingTileId));
    }

    private TileBase Resolve(int tileId)
    {
        return tileId != 0 && palette.TryGetTile(tileId, out TileBase tile) ? tile : null;
    }
}
