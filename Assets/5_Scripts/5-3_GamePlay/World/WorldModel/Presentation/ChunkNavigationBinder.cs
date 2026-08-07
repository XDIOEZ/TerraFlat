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
        boundChunk.Occupancy.Changed += HandleOccupancyChanged;
        WorldNavigationManager.Instance?.RegisterChunkRuntime(chunk);
    }

    public void Unbind()
    {
        if (boundChunk?.Terrain != null)
            boundChunk.Terrain.Changed -= HandleTerrainChanged;
        if (boundChunk != null)
            boundChunk.Occupancy.Changed -= HandleOccupancyChanged;
        if (boundChunk != null)
            WorldNavigationManager.ExistingInstance?.UnregisterChunkRuntime(boundChunk);
        boundChunk = null;
    }

    private void HandleTerrainChanged(ChunkTerrainChanged changed) => RefreshNavigation();
    private void HandleOccupancyChanged(ChunkOccupancyChanged changed) => RefreshNavigation();

    private void RefreshNavigation()
    {
        if (boundChunk != null)
            WorldNavigationManager.ExistingInstance?.RegisterChunkRuntime(boundChunk);
    }
}
