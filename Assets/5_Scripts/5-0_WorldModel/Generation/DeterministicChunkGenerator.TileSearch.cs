using System;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 地块定位

        // 搜索只生成临时地形，复用正式水文、结构和扩展，不创建生态物或加载世界区块。
        public BiomeSearchResult FindGroundTile(string dimensionId, int worldSeed,
            ChunkGenerationProfileSnapshot profile, ChunkGenerationTopologySnapshot topology,
            Int2 anchor, int tileId, CancellationToken cancellationToken, long worldEpoch,
            BiomeSearchProgress progress = null, IReadOnlyCollection<Int2> excludedChunks = null,
            IReadOnlyCollection<Int2> excludedCells = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (tileId <= 0) throw new ArgumentOutOfRangeException(nameof(tileId));
            if (worldEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(worldEpoch));
            dimensionId = string.IsNullOrWhiteSpace(dimensionId) ? "surface" : dimensionId;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, hydrologyRegions.LifetimeToken);
            var query = new DeterministicChunkGenerator(liquidTypes, terrainDecorator) { isBiomeSearchGenerator = true };
            query.hydrologyRegions.UseSearchLifetimeAndCompletedRegions(lifetime, hydrologyRegions);
            var excluded = excludedChunks == null ? new HashSet<Int2>() : new HashSet<Int2>(excludedChunks);
            var rejected = excludedCells == null ? new HashSet<Int2>() : new HashSet<Int2>(excludedCells);
            int startX = topology.IsWrapped ? topology.Min.X : 0;
            int startY = topology.IsWrapped ? topology.Min.Y : 0;
            anchor = new Int2(topology.NormalizeX(anchor.X), topology.NormalizeY(anchor.Y));
            var chunkAnchor = new Int2(FloorDiv(anchor.X - startX, profile.Width),
                FloorDiv(anchor.Y - startY, profile.Height));
            long minX = topology.IsWrapped ? 0 : (long)Math.Ceiling((int.MinValue - (double)startX) / profile.Width);
            long minY = topology.IsWrapped ? 0 : (long)Math.Ceiling((int.MinValue - (double)startY) / profile.Height);
            long maxX = topology.IsWrapped ? (topology.Span.X - 1L) / profile.Width :
                (int.MaxValue - (long)startX - profile.Width + 1) / profile.Width;
            long maxY = topology.IsWrapped ? (topology.Span.Y - 1L) / profile.Height :
                (int.MaxValue - (long)startY - profile.Height + 1) / profile.Height;
            long sampled = 0;
            Int2 found = anchor;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            long lastReport = -200;
            string stage = "搜索附近地块";
            if (Search(Math.Max(minX, (long)chunkAnchor.X - 4), Math.Max(minY, (long)chunkAnchor.Y - 4),
                    Math.Min(maxX, (long)chunkAnchor.X + 4), Math.Min(maxY, (long)chunkAnchor.Y + 4), 1, 0))
                return Finish(true);

            for (long radius = 32; ; radius = Math.Min(radius * 4, 1L << 32))
            {
                lifetime.Token.ThrowIfCancellationRequested();
                long left = topology.IsWrapped ? minX : Math.Max(minX, (long)chunkAnchor.X - radius);
                long bottom = topology.IsWrapped ? minY : Math.Max(minY, (long)chunkAnchor.Y - radius);
                long right = topology.IsWrapped ? maxX : Math.Min(maxX, (long)chunkAnchor.X + radius);
                long top = topology.IsWrapped ? maxY : Math.Min(maxY, (long)chunkAnchor.Y + radius);
                stage = $"覆盖 {right - left + 1} × {top - bottom + 1} 个区块";
                int previous = 0;
                foreach (int step in new[] { 64, 16, 4, 1 })
                {
                    progress?.Report(stage, sampled, step);
                    if (Search(left, bottom, right, top, step, previous)) return Finish(true);
                    previous = step;
                }
                if (topology.IsWrapped || left == minX && bottom == minY && right == maxX && top == maxY)
                    return Finish(false);
            }

            bool Search(long left, long bottom, long right, long top, int step, int previous)
            {
                foreach (Int2 index in Grid(left, bottom, right, top, chunkAnchor, step, previous, lifetime.Token))
                {
                    var origin = new Int2((int)(startX + (long)index.X * profile.Width),
                        (int)(startY + (long)index.Y * profile.Height));
                    if (excluded.Contains(origin)) continue;
                    var request = new ChunkGenerationRequest(worldEpoch, new WorldAddress(dimensionId, origin),
                        worldSeed == 0 ? 1 : worldSeed, 1, profile, topology);
                    using var buffer = new ChunkTerrainBuffer(profile.Width, profile.Height, liquidTypes);
                    query.PopulateTerrain(request, buffer, IsCaveGeneration(request), lifetime.Token, null);
                    using ChunkTerrainData terrain = buffer.Seal();
                    for (int y = 0; y < terrain.Height; y++)
                    for (int x = 0; x < terrain.Width; x++)
                    {
                        lifetime.Token.ThrowIfCancellationRequested();
                        // 非整块世界的边缘只检查边界内的真实格子。
                        if (topology.IsWrapped && ((long)origin.X + x >= (long)startX + topology.Span.X ||
                            (long)origin.Y + y >= (long)startY + topology.Span.Y)) continue;
                        sampled++;
                        if (timer.ElapsedMilliseconds - lastReport >= 200)
                        {
                            lastReport = timer.ElapsedMilliseconds;
                            progress?.Report(stage, sampled, step);
                        }
                        TerrainCell cell = terrain.GetCell(x, y);
                        if (!IsGroundTileDestination(cell, terrain.GetLiquidLevel(x, y), tileId)) continue;
                        found = new Int2(origin.X + x, origin.Y + y);
                        if (rejected.Contains(found)) continue;
                        return true;
                    }
                }
                return false;
            }
            BiomeSearchResult Finish(bool success)
            {
                progress?.Report(success ? "已找到目标地块" : "已完整搜索当前世界，未找到可站立的目标地块", sampled, 0);
                return new BiomeSearchResult(success, found, sampled);
            }
        }

        public static bool IsGroundTileDestination(TerrainCell cell, int liquidLevel, int tileId) =>
            cell.GroundTileId == tileId && cell.IsWalkable && cell.BackTileId == 0 &&
            cell.BlockingTileId == 0 && liquidLevel == 0;

        #endregion
    }
}
