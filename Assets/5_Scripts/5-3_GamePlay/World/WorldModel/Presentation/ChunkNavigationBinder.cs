using FlatWorld.WorldModel;
using UnityEngine;

public sealed class ChunkNavigationBinder : MonoBehaviour, IChunkViewRenderer
{
    private ChunkRuntime boundChunk;

    public void Bind(ChunkRuntime chunk)
    {
        if (chunk == null)
            throw new System.ArgumentNullException(nameof(chunk));
        if (ReferenceEquals(boundChunk, chunk))
            return;
        Unbind();
        boundChunk = chunk;
        boundChunk.Terrain.Changed += HandleTerrainChanged;
        boundChunk.Terrain.LiquidBatchChanged += HandleLiquidBatchChanged;
        boundChunk.Occupancy.Changed += HandleOccupancyChanged;
        WorldNavigationManager.Instance?.RegisterChunkRuntime(chunk);
    }

    public void Unbind()
    {
        if (boundChunk?.Terrain != null)
        {
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
            boundChunk.Terrain.LiquidBatchChanged -= HandleLiquidBatchChanged;
        }
        if (boundChunk != null)
            boundChunk.Occupancy.Changed -= HandleOccupancyChanged;
        if (boundChunk != null)
            WorldNavigationManager.ExistingInstance?.UnregisterChunkRuntime(boundChunk);
        boundChunk = null;
    }

    #region 增量导航同步
    private void HandleTerrainChanged(ChunkTerrainChanged changed)
    {
        if (boundChunk != null && changed.Kind != TerrainChangeKind.Grass)
            WorldNavigationManager.ExistingInstance?.UpdateChunkRuntimeCell(
                boundChunk, changed.LocalCell.X, changed.LocalCell.Y);
    }

    private void HandleLiquidBatchChanged(ChunkLiquidBatchChanged changed)
    {
        if (boundChunk != null)
            WorldNavigationManager.ExistingInstance?.UpdateChunkRuntimeCells(
                boundChunk, changed.CellIndices.Span);
    }

    private void HandleOccupancyChanged(ChunkOccupancyChanged changed)
    {
        if (boundChunk == null) return;
        WorldNavigationManager manager = WorldNavigationManager.ExistingInstance;
        if (changed.Cells.Count == 0)
            manager?.RefreshChunkRuntimeCells(boundChunk);
        else
            manager?.UpdateChunkRuntimeCells(boundChunk, changed.Cells);
    }
    #endregion
}
