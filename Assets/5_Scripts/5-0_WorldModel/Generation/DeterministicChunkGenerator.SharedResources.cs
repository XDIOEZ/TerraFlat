using System;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 跨区块资源地形校验

        /// <summary>只为稀疏大资源候选读取邻区地形；不递归生成生态或加载邻区表现。</summary>
        private ChunkEcologyData AppendSharedResources(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, ChunkEcologyData ecology, CancellationToken cancellationToken)
        {
            if (request.Profile.SharedEcologyRules.Count == 0 ||
                request.Profile.EcologyGlobalMultiplier <= 0d ||
                request.Profile.SharedEcologyCumulativeChances[request.Profile.SharedEcologyRules.Count - 1] <= 0d) return ecology;
            bool cave = IsCaveGeneration(request);
            var borrowedOrigin = request.Address.ChunkOrigin;
            var buffers = new Dictionary<Int2, ChunkTerrainBuffer> { [borrowedOrigin] = terrain };
            var portals = new Dictionary<Int2, ChunkEcologyData>();
            ChunkGenerationRequest RequestAt(Int2 origin) => new(request.WorldEpoch,
                new WorldAddress(request.Address.DimensionId, origin), request.WorldSeed,
                request.RequestVersion, request.Profile, request.Topology);
            ChunkTerrainBuffer Read(Int2 cell, out Int2 origin, out int x, out int y)
            {
                origin = ChunkEcologyGenerator.SharedOwnerOrigin(cell, request.Profile.Width, request.Profile.Height);
                if (!buffers.TryGetValue(origin, out var data))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    data = new ChunkTerrainBuffer(request.Profile.Width, request.Profile.Height, liquidTypes);
                    buffers.Add(origin, data);
                    var neighbor = RequestAt(origin);
                    PopulateTerrain(neighbor, data, IsCaveGeneration(neighbor), cancellationToken, null);
                }
                x = cell.X - origin.X; y = cell.Y - origin.Y;
                return data;
            }
            bool Accept(EcologySpawnRuleSnapshot rule, Int2 anchor)
            {
                int halfX = rule.FootprintWidth / 2, halfY = rule.FootprintHeight / 2;
                // 矿物适生条件只读取锚点，整片净空仍逐格排除水、障碍与入口安全区。
                var anchorData = Read(anchor, out _, out int anchorX, out int anchorY);
                TerrainCell anchorGround = anchorData.GetCell(anchorX, anchorY);
                if (!rule.MatchesGround(anchorGround.GroundTileId, request.Profile.Settings) ||
                    !rule.Matches(anchorGround.BiomeId, ReadValue(anchorData, "temperature", anchorX, anchorY),
                        ReadValue(anchorData, "precipitation", anchorX, anchorY),
                        ReadValue(anchorData, "height", anchorX, anchorY),
                        ReadValue(anchorData, "riverFloodplain", anchorX, anchorY)) ||
                    !string.IsNullOrEmpty(rule.RequiredEnvironmentLayer) &&
                    (!anchorData.TryGetEnvironmentValue(rule.RequiredEnvironmentLayer, anchorX, anchorY, out float value) ||
                        value < rule.MinimumEnvironmentValue || value > rule.MaximumEnvironmentValue)) return false;
                int safety = request.Profile.Settings.CavePortalEnabled
                    ? (int)Math.Ceiling(request.Profile.Settings.CavePortalSafeRadius) : 0;
                var inspected = new HashSet<Int2>();
                for (int dy = -halfY - safety; dy <= halfY + safety; dy++)
                for (int dx = -halfX - safety; dx <= halfX + safety; dx++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long rawX = (long)anchor.X + dx, rawY = (long)anchor.Y + dy;
                    if (rawX < int.MinValue || rawX > int.MaxValue || rawY < int.MinValue || rawY > int.MaxValue)
                        return false;
                    Int2 cell = new(request.Topology.NormalizeX((int)rawX), request.Topology.NormalizeY((int)rawY));
                    var data = Read(cell, out Int2 origin, out int x, out int y);
                    if (Math.Abs(dx) <= halfX && Math.Abs(dy) <= halfY)
                    {
                        TerrainCell ground = data.GetCell(x, y);
                        if (data.GetLiquidDepth(x, y) > 0f ||
                            (ground.Flags & (TerrainCellFlags.Blocking | TerrainCellFlags.Occupied)) != 0 ||
                            (ground.Flags & TerrainCellFlags.Walkable) == 0 ||
                            ReadValue(data, "structure", x, y) >= .5f ||
                            cave && CaveLayoutKernel.IsInsideDefaultSpawnSafeArea(request,
                                request.Profile.Settings, cell.X, cell.Y)) return false;
                    }
                    if (!request.Profile.Settings.CavePortalEnabled || !inspected.Add(origin)) continue;
                    if (!portals.TryGetValue(origin, out var entrances))
                    {
                        // 入口优先：复用同一正式选择器，不让大矿山覆盖入口及其安全范围。
                        entrances = CaveGenerationFeatureGenerator.GenerateWorldPortalPlacements(
                            RequestAt(origin), data, cancellationToken);
                        portals.Add(origin, entrances);
                    }
                    foreach (var entrance in entrances.Placements)
                        if (ChunkEcologyGenerator.SharedFootprintContains(request.Topology, rule, anchor,
                            new Int2(origin.X + entrance.LocalX, origin.Y + entrance.LocalY), safety)) return false;
                }
                return true;
            }
            try
            {
                var shared = ChunkEcologyGenerator.GenerateShared(request, Accept, cancellationToken);
                if (shared.Count == 0) return ecology;
                var byRule = new Dictionary<string, EcologySpawnRuleSnapshot>(StringComparer.Ordinal);
                foreach (var rule in request.Profile.SharedEcologyRules) byRule[rule.RuleId] = rule;
                var removed = new HashSet<int>();
                foreach (var existing in ecology.Placements)
                {
                    Int2 cell = new(request.Topology.NormalizeX(borrowedOrigin.X + existing.LocalX),
                        request.Topology.NormalizeY(borrowedOrigin.Y + existing.LocalY));
                    foreach (var mine in shared.Placements)
                        if (ChunkEcologyGenerator.SharedFootprintContains(request.Topology,
                            byRule[mine.RuleId], mine.SharedAnchor.Value, cell))
                        { removed.Add(existing.Guid); break; }
                }
                var merged = new List<NaturalItemPlacement>(ecology.Count + shared.Count);
                foreach (var existing in ecology.Placements)
                    if (!removed.Contains(existing.Guid) && !removed.Contains(existing.HostGuid)) merged.Add(existing);
                merged.AddRange(shared.Placements);
                return new ChunkEcologyData(merged);
            }
            finally
            {
                foreach (var pair in buffers)
                    if (!pair.Key.Equals(borrowedOrigin)) pair.Value.Dispose();
            }
        }

        private static float ReadValue(ChunkTerrainBuffer terrain, string channel, int x, int y)
            => terrain.TryGetEnvironmentValue(channel, x, y, out float value) ? value : 0f;

        #endregion
    }
}
