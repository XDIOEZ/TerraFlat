using System;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    public static partial class CaveGenerationFeatureGenerator
    {
        #region 矿山线索散矿

        private readonly struct LooseResourceMine
        {
            public LooseResourceMine(int x, int y, NaturalItemPlacement placement)
            {
                X = x;
                Y = y;
                Placement = placement;
                OreItemId = ToLooseOreItemId(placement.ItemId);
            }

            public int X { get; }
            public int Y { get; }
            public NaturalItemPlacement Placement { get; }
            public string OreItemId { get; }
            public bool HasOre => !string.IsNullOrWhiteSpace(OreItemId) &&
                !string.Equals(OreItemId, "Ore_Stone", StringComparison.Ordinal);
        }

        // 矿石与石头使用互斥概率区间，不重复投骰提高石头概率。
        private static ChunkEcologyData AppendLooseResources(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, ChunkEcologyData basis,
            ICaveBaseEcologyResolver resolver, CancellationToken cancellationToken)
        {
            ChunkGenerationSettingsSnapshot settings = request.Profile.Settings;
            if ((settings.CaveLooseOreDensity <= 0d && settings.CaveLooseStoneDensity <= 0d) ||
                request.Profile.CaveResourceRules.Count == 0)
                return basis;

            List<LooseResourceMine> mines = CollectLooseResourceMines(request, basis,
                resolver, cancellationToken);
            if (mines.Count == 0)
                return basis;
            int count = terrain.Width * terrain.Height;
            var nearestAny = new int[count];
            var nearestOre = new int[count];
            var anyDistances = new int[count];
            var oreDistances = new int[count];
            for (int i = 0; i < count; i++)
            {
                nearestAny[i] = nearestOre[i] = -1;
                anyDistances[i] = oreDistances[i] = int.MaxValue;
            }
            int radius = settings.CaveLooseResourceMaxDistance;
            int radiusSquared = radius * radius;
            // 每座矿山只投影自己的有限圆盘，预览大图也不做全图乘矿山数扫描。
            for (int mineIndex = 0; mineIndex < mines.Count; mineIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LooseResourceMine mine = mines[mineIndex];
                for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int distance = dx * dx + dy * dy;
                    if (distance > radiusSquared)
                        continue;
                    var cell = new Int2(request.Topology.NormalizeX(mine.X + dx),
                        request.Topology.NormalizeY(mine.Y + dy));
                    if (!TryGetLocalCell(request, terrain, cell, out int x, out int y))
                        continue;
                    int index = y * terrain.Width + x;
                    if (distance < anyDistances[index])
                    {
                        anyDistances[index] = distance;
                        nearestAny[index] = mineIndex;
                    }
                    if (mine.HasOre && distance < oreDistances[index])
                    {
                        oreDistances[index] = distance;
                        nearestOre[index] = mineIndex;
                    }
                }
            }

            var placements = new List<NaturalItemPlacement>(basis.Placements);
            var claimedGuids = new HashSet<int>();
            var occupied = new HashSet<int>();
            foreach (NaturalItemPlacement placement in basis.Placements)
            {
                claimedGuids.Add(placement.Guid);
                occupied.Add(placement.LocalY * terrain.Width + placement.LocalX);
            }
            int minimumSquared = settings.CaveLooseResourceMinDistance *
                settings.CaveLooseResourceMinDistance;
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                int index = y * terrain.Width + x;
                if ((index & 63) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                if (occupied.Contains(index) || nearestAny[index] < 0 ||
                    anyDistances[index] < minimumSquared)
                    continue;
                TerrainCell cell = terrain.GetCell(x, y);
                if (!cell.IsWalkable || (cell.Flags & (TerrainCellFlags.Blocking |
                        TerrainCellFlags.Occupied)) != 0 || terrain.GetLiquidDepth(x, y) > 0f)
                    continue;
                int worldX = request.Topology.NormalizeX(request.Address.ChunkOrigin.X + x);
                int worldY = request.Topology.NormalizeY(request.Address.ChunkOrigin.Y + y);
                if (CaveLayoutKernel.IsInsideDefaultSpawnSafeArea(request, settings, worldX, worldY))
                    continue;
                uint state = CaveLayoutKernel.Hash(request.WorldSeed ^ unchecked((int)0x6c8e9cf5),
                    worldX, worldY, 0x1b873593);
                double roll = CaveLayoutKernel.NextUnitDouble(ref state);
                int source;
                string itemId;
                if (roll < settings.CaveLooseOreDensity)
                {
                    source = nearestOre[index];
                    if (source < 0)
                        continue;
                    itemId = mines[source].OreItemId;
                }
                else if (roll < Math.Min(1d,
                             settings.CaveLooseOreDensity + settings.CaveLooseStoneDensity))
                {
                    source = nearestAny[index];
                    itemId = "Ore_Stone";
                }
                else
                    continue;
                LooseResourceMine host = mines[source];
                string ruleId = $"cave.loose.{itemId}@{host.X},{host.Y}";
                int guid = CaveLayoutKernel.CreatePlacementGuid(request, worldX, worldY, ruleId);
                // 来源写进规则身份，HostGuid 保持零以支持跨区块线索独立显示。
                AddPlacement(placements, claimedGuids, new NaturalItemPlacement(guid,
                    itemId, x, y, 0f, 0f, ruleId));
            }
            return new ChunkEcologyData(placements);
        }

        private static List<LooseResourceMine> CollectLooseResourceMines(
            ChunkGenerationRequest request, ChunkEcologyData basis,
            ICaveBaseEcologyResolver resolver, CancellationToken cancellationToken)
        {
            var mines = new List<LooseResourceMine>();
            int width = request.Profile.Width, height = request.Profile.Height;
            int radius = request.Profile.Settings.CaveLooseResourceMaxDistance;
            int stepsX = (radius + width - 1) / width;
            int stepsY = (radius + height - 1) / height;
            var origins = new HashSet<Int2>();
            var mineCells = new HashSet<Int2>();
            for (int cy = -stepsY; cy <= stepsY; cy++)
            for (int cx = -stepsX; cx <= stepsX; cx++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var origin = new Int2(request.Topology.NormalizeX(request.Address.ChunkOrigin.X + cx * width),
                    request.Topology.NormalizeY(request.Address.ChunkOrigin.Y + cy * height));
                if (!origins.Add(origin))
                    continue;
                bool current = origin.Equals(request.Address.ChunkOrigin);
                if (!current && resolver == null)
                    continue;
                ChunkEcologyData ecology = current ? basis : resolver.GetCaveBaseEcology(
                    new ChunkGenerationRequest(request.WorldEpoch,
                        new WorldAddress(request.Address.DimensionId, origin), request.WorldSeed,
                        request.RequestVersion, request.Profile, request.Topology), null, cancellationToken);
                foreach (NaturalItemPlacement placement in ecology.Placements)
                {
                    // 只承认矿山阶段实际放下的矿山，不把植物或其他生态配置当作来源。
                    bool mineRule = false;
                    foreach (CaveResourceRuleSnapshot rule in request.Profile.CaveResourceRules)
                        if (placement.RuleId == rule.RuleId && placement.ItemId == rule.ItemId)
                        {
                            mineRule = true;
                            break;
                        }
                    if (!mineRule)
                        continue;
                    int x = request.Topology.NormalizeX(origin.X + placement.LocalX);
                    int y = request.Topology.NormalizeY(origin.Y + placement.LocalY);
                    if (mineCells.Add(new Int2(x, y)))
                        mines.Add(new LooseResourceMine(x, y, placement));
                }
            }
            mines.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            return mines;
        }

        #endregion
    }
}
