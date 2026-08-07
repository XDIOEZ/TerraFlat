using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

public sealed class ChunkView : MonoBehaviour
{
    private readonly List<IChunkViewRenderer> renderers = new();
    private WorldRuntime world;
    private ChunkRuntime chunk;
    private ChunkLease presentationLease;
    private ChunkLease navigationLease;
    private IDisposable committedSubscription;
    private bool navigationEnabled;

    public ChunkRuntime Model => chunk;
    public bool IsBound => chunk != null;

    public void Bind(WorldRuntime worldRuntime, ChunkRuntime chunkRuntime, bool includeNavigation = true)
    {
        if (worldRuntime == null)
            throw new ArgumentNullException(nameof(worldRuntime));
        if (chunkRuntime == null)
            throw new ArgumentNullException(nameof(chunkRuntime));
        if (chunkRuntime.DataStatus != ChunkDataStatus.Ready || chunkRuntime.Terrain == null)
            throw new InvalidOperationException($"Chunk data is not ready: {chunkRuntime.DataStatus}");
        if (ReferenceEquals(world, worldRuntime) && ReferenceEquals(chunk, chunkRuntime))
            return;

        Unbind();
        world = worldRuntime;
        chunk = chunkRuntime;
        navigationEnabled = includeNavigation;
        transform.position = new Vector3(chunk.Address.ChunkOrigin.X, chunk.Address.ChunkOrigin.Y, 0f);
        presentationLease = chunk.AcquireLease(ChunkLeaseKind.Presentation);
        if (includeNavigation)
            navigationLease = chunk.AcquireLease(ChunkLeaseKind.Navigation);
        committedSubscription = world.Events.Subscribe<ChunkCommitted>(HandleChunkCommitted);

        CacheRenderers();
        for (int i = 0; i < renderers.Count; i++)
        {
            if (!navigationEnabled && renderers[i] is ChunkNavigationBinder)
                continue;
            renderers[i].Bind(chunk);
        }
        chunk.MarkPresentationBound();
    }

    public void Unbind()
    {
        for (int i = renderers.Count - 1; i >= 0; i--)
            renderers[i].Unbind();
        committedSubscription?.Dispose();
        committedSubscription = null;
        navigationLease?.Dispose();
        navigationLease = null;
        presentationLease?.Dispose();
        presentationLease = null;
        chunk = null;
        world = null;
        navigationEnabled = false;
    }

    private void OnDisable() => Unbind();
    private void OnDestroy() => Unbind();

    private void HandleChunkCommitted(ChunkCommitted committed)
    {
        if (chunk == null || committed.Address != chunk.Address ||
            chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null)
            return;
        for (int i = 0; i < renderers.Count; i++)
        {
            if (!navigationEnabled && renderers[i] is ChunkNavigationBinder)
                continue;
            renderers[i].Unbind();
            renderers[i].Bind(chunk);
        }
    }

    private void CacheRenderers()
    {
        renderers.Clear();
        MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IChunkViewRenderer renderer && !ReferenceEquals(renderer, this))
                renderers.Add(renderer);
        }
    }
}
