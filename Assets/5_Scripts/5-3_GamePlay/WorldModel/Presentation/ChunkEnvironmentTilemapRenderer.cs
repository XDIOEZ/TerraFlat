using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class ChunkEnvironmentTilemapRenderer : MonoBehaviour, IChunkViewRenderer
{
    [SerializeField] private string environmentLayerId = "grass";
    [SerializeField] private float visibleThreshold = 0.5f;
    [SerializeField] private Tilemap tilemap;
    [SerializeField] private TileBase tile;
    private ChunkRuntime boundChunk;

    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new System.ArgumentNullException(nameof(chunk));
        if (chunk.Terrain == null)
            throw new System.InvalidOperationException("Cannot bind environment rendering before data is ready.");
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
        if (tilemap != null)
            tilemap.ClearAllTiles();
        boundChunk = null;
    }

    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (changed.Kind != TerrainChangeKind.Environment || boundChunk?.Terrain == null ||
            tilemap == null || tile == null)
            return;
        bool visible = boundChunk.Terrain.TryGetEnvironmentValue(environmentLayerId,
                           changed.LocalCell.X, changed.LocalCell.Y, out float value) &&
                       value >= visibleThreshold;
        tilemap.SetTile(new Vector3Int(changed.LocalCell.X, changed.LocalCell.Y, 0),
            visible ? tile : null);
    }

    private void Render(ChunkTerrainData terrain)
    {
        if (tilemap == null || tile == null || terrain == null ||
            !terrain.TryCopyEnvironmentLayer(environmentLayerId, out float[] values))
            return;

        var tiles = new TileBase[terrain.CellCount];
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] >= visibleThreshold)
                tiles[i] = tile;
        }
        tilemap.SetTilesBlock(new BoundsInt(0, 0, 0, terrain.Width, terrain.Height, 1), tiles);
    }
}
