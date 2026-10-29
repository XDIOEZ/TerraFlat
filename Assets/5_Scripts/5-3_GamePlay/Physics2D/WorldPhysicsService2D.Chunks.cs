using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public sealed partial class WorldPhysicsService2D
{
    #region 世界区块物理生命周期

    private WorldRuntime chunkWorld;
    private IDisposable chunkCommittedSubscription, chunkSimulationSubscription, chunkEvictedSubscription;
    private readonly Dictionary<RuntimeWorldAddress, WorldChunkPhysicsProjection2D> chunkPhysics = new();
    private readonly Stack<WorldChunkPhysicsProjection2D> chunkPhysicsPool = new();
    private Transform chunkPhysicsRoot;

    public int ChunkPhysicsCount => chunkPhysics.Count;

    public bool TryGetChunkPhysics(RuntimeWorldAddress address, out WorldChunkPhysicsProjection2D projection)
    {
        projection = null;
        return IsValid && chunkPhysics.TryGetValue(address, out projection) && projection != null;
    }

    // 模拟租约和权威地形驱动物理，表现绑定、相机可见性与视图回池不参与决策。
    internal void BindChunkWorld(WorldRuntime world)
    {
        if (disposed) throw new ObjectDisposedException(nameof(WorldPhysicsService2D));
        if (world == null || world.WorldId != WorldKey)
            throw new ArgumentException("区块物理必须绑定所属世界的模型。", nameof(world));
        if (ReferenceEquals(chunkWorld, world)) return;
        DisposeChunkPhysics();
        chunkWorld = world;
        chunkCommittedSubscription = world.Events.Subscribe<ChunkCommitted>(HandlePhysicsChunkCommitted);
        chunkSimulationSubscription = world.Events.Subscribe<ChunkSimulationStatusChanged>(HandlePhysicsChunkSimulationChanged);
        chunkEvictedSubscription = world.Events.Subscribe<ChunkEvicted>(HandlePhysicsChunkEvicted);
        WorldTopologyRuntime.LocalPlayerWrapped += ReprojectChunkPhysicsToLocalAnchor;
        try
        {
            foreach (ChunkRuntime chunk in world.Chunks.Values) ReconcileChunkPhysics(chunk.Address);
        }
        catch { DisposeChunkPhysics(); throw; }
    }

    internal void UnbindChunkWorld(WorldRuntime expectedWorld)
    {
        if (ReferenceEquals(chunkWorld, expectedWorld)) DisposeChunkPhysics();
    }

    private void HandlePhysicsChunkCommitted(ChunkCommitted changed) => ReconcileChunkPhysics(changed.Address);
    private void HandlePhysicsChunkSimulationChanged(ChunkSimulationStatusChanged changed) => ReconcileChunkPhysics(changed.Address);
    private void HandlePhysicsChunkEvicted(ChunkEvicted changed) => ReleaseChunkPhysics(changed.Address);

    private void ReconcileChunkPhysics(RuntimeWorldAddress address)
    {
        if (!IsValid || chunkWorld == null) return;
        if (!chunkWorld.TryGetChunk(address, out ChunkRuntime chunk) || chunk.Terrain == null ||
            chunk.DataStatus != ChunkDataStatus.Ready || chunk.SimulationStatus != ChunkSimulationStatus.Active)
        {
            ReleaseChunkPhysics(address);
            return;
        }

        using var scope = GameplayWorldRegistry.TryGet(WorldKey, out _) ? GameplayWorldRegistry.UseWorld(WorldKey) : null;
        chunkPhysics.TryGetValue(address, out WorldChunkPhysicsProjection2D projection);
        if (projection != null && ReferenceEquals(projection.BoundChunk, chunk) &&
            ReferenceEquals(projection.BoundTerrain, chunk.Terrain)) return;
        if (projection == null)
        {
            while (chunkPhysicsPool.Count > 0 && projection == null) projection = chunkPhysicsPool.Pop();
            if (projection == null) projection = WorldChunkPhysicsProjection2D.Create(this, GetChunkPhysicsRoot());
            chunkPhysics[address] = projection;
        }
        try
        {
            projection.gameObject.SetActive(true);
            projection.Bind(this, chunk);
        }
        catch { ReleaseChunkPhysics(address); throw; }
    }

    private Transform GetChunkPhysicsRoot()
    {
        if (chunkPhysicsRoot != null) return chunkPhysicsRoot;
        var root = new GameObject("World Physics Chunks") { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(root, PrimarySpace.Scene);
        return chunkPhysicsRoot = root.transform;
    }

    private void ReleaseChunkPhysics(RuntimeWorldAddress address)
    {
        if (!chunkPhysics.Remove(address, out WorldChunkPhysicsProjection2D projection) || projection == null) return;
        projection.Unbind();
        projection.gameObject.SetActive(false);
        chunkPhysicsPool.Push(projection);
    }

    private void ReprojectChunkPhysicsToLocalAnchor()
    {
        if (!IsValid || WorldKey != GameplayWorldRegistry.ExecutionWorldKey) return;
        foreach (WorldChunkPhysicsProjection2D projection in chunkPhysics.Values)
            if (projection != null) projection.RefreshLocalPhysicsPosition();
    }

    private void DisposeChunkPhysics()
    {
        chunkCommittedSubscription?.Dispose();
        chunkSimulationSubscription?.Dispose();
        chunkEvictedSubscription?.Dispose();
        chunkCommittedSubscription = chunkSimulationSubscription = chunkEvictedSubscription = null;
        WorldTopologyRuntime.LocalPlayerWrapped -= ReprojectChunkPhysicsToLocalAnchor;
        chunkWorld = null;
        foreach (WorldChunkPhysicsProjection2D projection in chunkPhysics.Values)
            if (projection != null) projection.Unbind();
        chunkPhysics.Clear();
        chunkPhysicsPool.Clear();
        if (chunkPhysicsRoot != null) DestroyPhysicsObject(chunkPhysicsRoot.gameObject);
        chunkPhysicsRoot = null;
    }

    #endregion
}
