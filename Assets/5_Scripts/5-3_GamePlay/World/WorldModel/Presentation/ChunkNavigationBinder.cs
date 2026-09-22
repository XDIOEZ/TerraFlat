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

    private void HandleTerrainChanged(ChunkTerrainChanged changed) => RefreshNavigation();
    /// <summary>整个液体 Tick 对本 Chunk 只注册一次导航，后续共享流场仍消费原导航脏块。</summary>
    private void HandleLiquidBatchChanged(ChunkLiquidBatchChanged changed) => RefreshNavigation();
    private void HandleOccupancyChanged(ChunkOccupancyChanged changed) => RefreshNavigation();

    private void RefreshNavigation()
    {
        if (boundChunk != null)
            WorldNavigationManager.ExistingInstance?.RegisterChunkRuntime(boundChunk);
    }
}
