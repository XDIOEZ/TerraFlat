using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ChunkMgr
{
    #region 地块调试定位

    // 已加载地形优先读当前状态，未加载区域使用冻结后的正式生成上下文。
    public async Task<DeterministicChunkGenerator.BiomeSearchResult> FindGroundTileAsync(
        Int2 anchor, int tileId, CancellationToken cancellationToken,
        DeterministicChunkGenerator.BiomeSearchProgress progress = null)
    {
        EnsureWorldRuntime();
        ChunkGenerationProfileSnapshot profile = PrepareActiveGenerationSnapshot(out int baseSeed);
        ChunkGenerationTopologySnapshot topology = ResolveActiveGenerationTopology();
        string dimension = ResolveCurrentDimensionId();
        int seed = DimensionManager.Instance != null
            ? DimensionManager.Instance.GetActiveGenerationSeed(baseSeed) : baseSeed;
        long epoch = runtimeChunkManager.World.Epoch;
        var excluded = new List<Int2>();
        Int2? nearest = null;
        double bestDistance = double.MaxValue;
        foreach (var pair in Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChunkRuntime chunk = pair.Value;
            ChunkTerrainData terrain = chunk.Terrain;
            if (pair.Key.DimensionId != dimension || chunk.DataStatus != ChunkDataStatus.Ready ||
                terrain == null || terrain.IsDisposed ||
                !activeRuntimeBindings.TryGetValue(pair.Key, out RuntimeChunkBinding binding) ||
                !ReferenceEquals(binding.RestoredTerrainChunk, chunk)) continue;
            excluded.Add(pair.Key.ChunkOrigin);
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                if (!DeterministicChunkGenerator.IsGroundTileDestination(terrain.GetCell(x, y),
                        terrain.GetLiquidLevel(x, y), tileId) || TerrainSupportLayer.GetTileId(terrain, x, y) != 0)
                    continue;
                var cell = new Int2(pair.Key.ChunkOrigin.X + x, pair.Key.ChunkOrigin.Y + y);
                var delta = topology.ToDomain().ShortestDelta(new Unity.Mathematics.int2(anchor.X, anchor.Y),
                    new Unity.Mathematics.int2(cell.X, cell.Y));
                double distance = (double)delta.x * delta.x + (double)delta.y * delta.y;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                nearest = cell;
            }
        }
        if (nearest.HasValue)
            return new DeterministicChunkGenerator.BiomeSearchResult(true, nearest.Value, 0);
        DeterministicChunkGenerator generator = runtimeGenerator;
        var rejected = new HashSet<Int2>();
        WorldRuntime world = WorldRuntime;
        string worldKey = GameplayWorldRegistry.ExecutionWorldKey;
        long sampled = 0;
        while (true)
        {
            var result = await Task.Run(() => generator.FindGroundTile(dimension, seed, profile, topology,
                anchor, tileId, cancellationToken, epoch, progress, excluded, rejected), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (this == null || !ReferenceEquals(WorldRuntime, world) || world.Epoch != epoch ||
                ResolveCurrentDimensionId() != dimension || GameplayWorldRegistry.ExecutionWorldKey != worldKey)
                throw new System.OperationCanceledException(cancellationToken);
            sampled += result.SampledCount;
            if (!result.Found) return new DeterministicChunkGenerator.BiomeSearchResult(false, anchor, sampled);
            // 后台不读取活动存档；回到主线程后排除已挖走、铺盖或灌水的旧生成位置。
            Int2 origin = ResolveWorldAddress(new Vector2(result.Cell.X + .5f, result.Cell.Y + .5f)).ChunkOrigin;
            SaveDataMgr save = SaveDataMgr.Instance;
            if (save != null && save.TryGetChunkDelta(worldKey, new Vector2Int(origin.X, origin.Y), out ChunkSaveRecord record))
            {
                foreach (var cell in record.RuntimeTileDeltas)
                    if (!DeterministicChunkGenerator.IsGroundTileDestination(cell.ToTerrainCell(), 0, tileId))
                        rejected.Add(new Int2(origin.X + cell.LocalPosition.x, origin.Y + cell.LocalPosition.y));
                foreach (var cell in record.LiquidCells)
                    if (cell.LiquidLevel > 0)
                        rejected.Add(new Int2(origin.X + cell.LocalPosition.x, origin.Y + cell.LocalPosition.y));
                foreach (var cell in record.SupportCells)
                    if (cell.TileId != 0)
                        rejected.Add(new Int2(origin.X + cell.LocalPosition.x, origin.Y + cell.LocalPosition.y));
            }
            if (!rejected.Contains(result.Cell))
                return new DeterministicChunkGenerator.BiomeSearchResult(true, result.Cell, sampled);
        }
    }

    #endregion
}
