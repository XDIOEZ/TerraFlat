using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class ChunkMgr
{
    #region 局部世界区块宿主
    private readonly HashSet<RuntimeWorldAddress> pendingLocalChunkRestorations = new();
    private readonly List<RuntimeWorldAddress> localChunkRestorationBatch = new();
    private bool restoringLocalChunks;
    private int localChunkRestorationSuspensions;

    internal void BeginLocalWorldChunkRestorationBatch() => localChunkRestorationSuspensions++;

    internal void EndLocalWorldChunkRestorationBatch(bool restore)
    {
        if (localChunkRestorationSuspensions <= 0) throw new InvalidOperationException("局部世界恢复批次未开始。");
        localChunkRestorationSuspensions--;
        if (restore && localChunkRestorationSuspensions == 0) RestorePendingLocalWorldChunks();
    }

    internal void AttachLocalWorld(WorldRuntime world, IChunkPureGenerator generator, int chunkSize)
    {
        var tiles = new Dictionary<string, string>(StringComparer.Ordinal);
        if (GameRes.ExistingInstance != null)
            foreach (RuntimeTileDefinition tile in GameRes.ExistingInstance.TileBlockDict.Values)
                if (tile.RuntimeTileId > 0) tiles[$"tile.block.{tile.RuntimeTileId}"] = tile.Id;
        defaultGenerationSnapshot = new ChunkGenerationProfileSnapshot("local.empty", 1, chunkSize, chunkSize,
            textParameters: tiles);
        activeGenerationSnapshot = defaultGenerationSnapshot;
        runtimeTileCatalogSnapshot = defaultGenerationSnapshot;
        AttachOwnedWorldRuntime(world, generator);
        liquidBodies = new WorldLiquidBodySystem(this, world.WorldId);
    }

    internal void BindLocalWorldChunk(ChunkRuntime chunk)
    {
        if (chunk == null || chunk.DataStatus != ChunkDataStatus.Ready) return;
        if (!activeRuntimeBindings.TryGetValue(chunk.Address, out RuntimeChunkBinding binding))
            activeRuntimeBindings.Add(chunk.Address, binding = new RuntimeChunkBinding());
        runtimeWindowTargets.Add(chunk.Address);
        binding.WantsPresentation = true;
        pendingLocalChunkRestorations.Add(chunk.Address);
        RestorePendingLocalWorldChunks();
    }

    internal void UnbindLocalWorldChunk(RuntimeWorldAddress address)
    {
        pendingLocalChunkRestorations.Remove(address);
        runtimeWindowTargets.Remove(address);
        DeactivateRuntimeBinding(address, recycleView: false);
    }

    // 区块就绪时先恢复正式居民，再绑定画面；资源未就绪只重试待恢复区块。
    private void RestorePendingLocalWorldChunks()
    {
        if (restoringLocalChunks || localChunkRestorationSuspensions != 0 || pendingLocalChunkRestorations.Count == 0 ||
            SaveDataMgr.Instance == null || ItemMgr.GetExistingInstance() == null) return;
        restoringLocalChunks = true;
        try
        {
            localChunkRestorationBatch.Clear();
            localChunkRestorationBatch.AddRange(pendingLocalChunkRestorations);
            foreach (RuntimeWorldAddress address in localChunkRestorationBatch)
            {
                if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding) ||
                    !runtimeChunkManager.TryGetChunk(address, out ChunkRuntime chunk) || chunk.Terrain == null)
                {
                    pendingLocalChunkRestorations.Remove(address);
                    continue;
                }
                if (!RestoreRuntimeChunkState(address, binding, chunk) ||
                    !ReferenceEquals(binding.RestoredEntitiesChunk, chunk)) continue;
                pendingLocalChunkRestorations.Remove(address);
                QueueRuntimeChunkPresentation(address, binding, chunk);
            }
        }
        finally { localChunkRestorationBatch.Clear(); restoringLocalChunks = false; }
    }

    internal void ReleaseLocalWorldHost()
    {
        if (IsWorldRuntimeShuttingDown) return;
        runtimeHost?.Bind(null);
        try
        {
            ClearRuntimeWindowBindings();
            pendingLocalChunkRestorations.Clear();
            if (SaveDataMgr.Instance != null && !SaveDataMgr.Instance.CaptureRuntimeTerrainAfterSceneViewUnbinding(OwnedWorldKey))
                throw new InvalidOperationException("局部世界地形保存失败：" + OwnedWorldKey);
            IsWorldRuntimeShuttingDown = true;
            DetachWorldRuntimeFromScene();
        }
        catch
        {
            if (runtimeChunkManager != null)
                foreach (ChunkRuntime chunk in runtimeChunkManager.Chunks.Values) BindLocalWorldChunk(chunk);
            runtimeHost?.Bind(this);
            throw;
        }
    }
    #endregion
}
