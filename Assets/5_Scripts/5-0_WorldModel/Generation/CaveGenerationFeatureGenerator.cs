using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 新版区块的洞穴物品阶段。
    /// 地形完成后在纯数据中输出可采集藤蔓、洞壁矿脉、散落矿石和跨维度传送门，主线程只负责由 ChunkView 实例化。
    /// </summary>
    public static class CaveGenerationFeatureGenerator
    {
        #region 地表配对缓存

        // 缓存按“冻结地表配置 + 门户随机种子 + 正式概率格”区分；洞穴窗口通常只会命中极少数入口格。
        private const int MaxSurfacePortalSelectionCacheEntries = 512;
        private const int SurfacePortalShrubSalt = 0x2c1b3c6d;
        private static readonly ConcurrentDictionary<SurfacePortalSelectionKey,
            Lazy<SurfacePortalSelection>> SurfacePortalSelections = new();
        private static readonly DeterministicChunkGenerator SurfacePortalTerrainGenerator = new();

        private readonly struct SurfacePortalSelection
        {
            public SurfacePortalSelection(Int2 cell, int candidateIndex)
            {
                Cell = cell;
                CandidateIndex = candidateIndex;
                HasValue = true;
            }

            public bool HasValue { get; }
            public Int2 Cell { get; }
            public int CandidateIndex { get; }
        }

        private readonly struct SurfacePortalSelectionKey :
            IEquatable<SurfacePortalSelectionKey>
        {
            public SurfacePortalSelectionKey(ulong pairingFingerprint, int portalSeed,
                Int2 portalChunkOrigin)
            {
                PairingFingerprint = pairingFingerprint;
                PortalSeed = portalSeed;
                PortalChunkOrigin = portalChunkOrigin;
            }

            private ulong PairingFingerprint { get; }
            private int PortalSeed { get; }
            private Int2 PortalChunkOrigin { get; }

            public bool Equals(SurfacePortalSelectionKey other)
            {
                return PairingFingerprint == other.PairingFingerprint &&
                       PortalSeed == other.PortalSeed &&
                       PortalChunkOrigin.Equals(other.PortalChunkOrigin);
            }

            public override bool Equals(object obj) =>
                obj is SurfacePortalSelectionKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)PairingFingerprint ^
                               (int)(PairingFingerprint >> 32);
                    hash = hash * 397 ^ PortalSeed;
                    hash = hash * 397 ^ PortalChunkOrigin.GetHashCode();
                    return hash;
                }
            }
        }

        #endregion

        #region 入口

        /// <summary>生成洞穴矿脉、散矿与返回地表的出口。</summary>
        public static ChunkEcologyData GenerateCave(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, CancellationToken cancellationToken)
        {
            if (terrain == null)
                throw new ArgumentNullException(nameof(terrain));

            ChunkGenerationSettingsSnapshot settings = request.Profile.Settings;
            var placements = new List<NaturalItemPlacement>();
            var claimedGuids = new HashSet<int>();
            var portalCells = new HashSet<int>();
            AddCavePortals(request, terrain, settings, placements, claimedGuids, portalCells,
                cancellationToken);

            IReadOnlyList<CaveResourceRuleSnapshot> resourceRules =
                request.Profile.CaveResourceRules;
            int startX = request.Address.ChunkOrigin.X;
            int startY = request.Address.ChunkOrigin.Y;
            for (int localY = 0; localY < terrain.Height; localY++)
            {
                for (int localX = 0; localX < terrain.Width; localX++)
                {
                    if (((localY * terrain.Width + localX) & 63) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    if (portalCells.Contains(localY * terrain.Width + localX))
                        continue;

                    TerrainCell cell = terrain.GetCell(localX, localY);
                    if ((cell.Flags & TerrainCellFlags.Walkable) == 0)
                        continue;

                    int worldX = request.Topology.NormalizeX(startX + localX);
                    int worldY = request.Topology.NormalizeY(startY + localY);
                    if (CaveLayoutKernel.IsInsideDefaultSpawnSafeArea(
                            request, settings, worldX, worldY))
                    {
                        continue;
                    }

                    if (TryAddVine(request, settings, worldX, worldY, localX, localY,
                            placements, claimedGuids))
                    {
                        continue;
                    }

                    if (resourceRules == null || resourceRules.Count == 0)
                        continue;

                    bool spawnedMine = CaveLayoutKernel.IsWallEdge(
                                           request, settings, worldX, worldY) &&
                                       TryAddWallResource(request, settings, resourceRules,
                                           worldX, worldY, localX, localY, placements, claimedGuids);
                    if (!spawnedMine)
                    {
                        TryAddLooseOre(request, settings, resourceRules, worldX, worldY,
                            localX, localY, placements, claimedGuids);
                    }
                }
            }

            return placements.Count == 0 ? ChunkEcologyData.Empty : new ChunkEcologyData(placements);
        }

        /// <summary>把地表生态结果叠加天然矿洞入口，并让入口格优先于生态物品。</summary>
        public static ChunkEcologyData AppendSurfacePortals(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, ChunkEcologyData ecology)
        {
            if (terrain == null)
                throw new ArgumentNullException(nameof(terrain));

            ChunkGenerationSettingsSnapshot settings = request.Profile.Settings;
            var portalPlacements = new List<NaturalItemPlacement>();
            var claimedGuids = new HashSet<int>();
            var portalCells = new HashSet<int>();
            AddSurfacePortals(request, terrain, settings, portalPlacements, claimedGuids,
                portalCells);
            var shrubPlacements = new List<NaturalItemPlacement>();
            AddSurfacePortalShrubs(request, terrain, settings, ecology, portalPlacements,
                shrubPlacements, claimedGuids);
            if (portalPlacements.Count == 0 && shrubPlacements.Count == 0)
                return ecology ?? ChunkEcologyData.Empty;

            var merged = new List<NaturalItemPlacement>((ecology?.Count ?? 0) +
                portalPlacements.Count + shrubPlacements.Count);
            IReadOnlyList<NaturalItemPlacement> existing = ecology?.Placements;
            if (existing != null)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    NaturalItemPlacement placement = existing[i];
                    int key = placement.LocalY * terrain.Width + placement.LocalX;
                    if (portalCells.Contains(key))
                        continue;
                    AddPlacement(merged, claimedGuids, placement);
                }
            }
            for (int i = 0; i < portalPlacements.Count; i++)
                AddPlacement(merged, claimedGuids, portalPlacements[i]);
            for (int i = 0; i < shrubPlacements.Count; i++)
                AddPlacement(merged, claimedGuids, shrubPlacements[i]);
            return new ChunkEcologyData(merged);
        }

        #endregion

        #region 洞穴入口周边灌木

        /// <summary>
        /// 在天然洞穴入口安全区外增加灌木概率。只复用草原/森林的 Bush 规则，
        /// 因此沙漠、沙滩、石地和雪地不会因为靠近入口而长出灌木。
        /// </summary>
        private static void AddSurfacePortalShrubs(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, ChunkGenerationSettingsSnapshot settings,
            ChunkEcologyData existingEcology,
            IReadOnlyList<NaturalItemPlacement> portalPlacements,
            List<NaturalItemPlacement> placements, HashSet<int> claimedGuids)
        {
            if (!settings.CavePortalShrubEnabled ||
                settings.CavePortalShrubChanceMultiplier <= 0d ||
                settings.CavePortalShrubRadius <= settings.CavePortalSafeRadius ||
                portalPlacements == null || portalPlacements.Count == 0 ||
                request.Profile.EcologyGlobalMultiplier <= 0d)
            {
                return;
            }

            IReadOnlyList<EcologySpawnRuleSnapshot> rules = request.Profile.EcologyRules;
            if (rules == null || rules.Count == 0)
                return;

            var occupiedCells = new HashSet<int>();
            IReadOnlyList<NaturalItemPlacement> existing = existingEcology?.Placements;
            if (existing != null)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    NaturalItemPlacement placement = existing[i];
                    occupiedCells.Add(placement.LocalY * terrain.Width + placement.LocalX);
                }
            }

            for (int portalIndex = 0; portalIndex < portalPlacements.Count; portalIndex++)
            {
                NaturalItemPlacement portal = portalPlacements[portalIndex];
                int portalWorldX = request.Topology.NormalizeX(
                    request.Address.ChunkOrigin.X + portal.LocalX);
                int portalWorldY = request.Topology.NormalizeY(
                    request.Address.ChunkOrigin.Y + portal.LocalY);
                int radius = settings.CavePortalShrubRadius;
                int minX = portalWorldX - radius;
                int maxX = portalWorldX + radius;
                int minY = portalWorldY - radius;
                int maxY = portalWorldY + radius;

                for (int worldY = minY; worldY <= maxY; worldY++)
                {
                    for (int worldX = minX; worldX <= maxX; worldX++)
                    {
                        int offsetX = worldX - portalWorldX;
                        int offsetY = worldY - portalWorldY;
                        int distanceSquared = offsetX * offsetX + offsetY * offsetY;
                        if (distanceSquared <= settings.CavePortalSafeRadius *
                            settings.CavePortalSafeRadius || distanceSquared > radius * radius)
                        {
                            continue;
                        }

                        Int2 normalized = new(
                            request.Topology.NormalizeX(worldX),
                            request.Topology.NormalizeY(worldY));
                        if (!TryGetLocalCell(request, terrain, normalized,
                                out int localX, out int localY))
                        {
                            continue;
                        }

                        int cellKey = localY * terrain.Width + localX;
                        if (!occupiedCells.Add(cellKey))
                            continue;

                        TerrainCell cell = terrain.GetCell(localX, localY);
                        if (!IsSurfacePortalShrubCellAvailable(terrain, cell, localX, localY))
                        {
                            occupiedCells.Remove(cellKey);
                            continue;
                        }

                        double temperature = ReadEnvironment(terrain, "temperature", localX,
                            localY);
                        double precipitation = ReadEnvironment(terrain, "precipitation", localX,
                            localY);
                        double height = ReadEnvironment(terrain, "height", localX, localY);
                        double riverFloodplain = ReadEnvironment(terrain, "riverFloodplain",
                            localX, localY);
                        EcologySpawnRuleSnapshot rule = FindSurfacePortalShrubRule(
                            rules, cell.BiomeId, temperature, precipitation, height,
                            riverFloodplain);
                        if (rule == null)
                        {
                            occupiedCells.Remove(cellKey);
                            continue;
                        }

                        double chance = request.Profile.EcologyGlobalMultiplier *
                            rule.SpawnChance * rule.SpawnChanceMultiplier *
                            settings.CavePortalShrubChanceMultiplier;
                        if (!PassesSurfacePortalShrubChance(request, normalized.X, normalized.Y,
                                chance))
                        {
                            occupiedCells.Remove(cellKey);
                            continue;
                        }

                        string ruleId = $"cave.portal.shrub.{rule.RuleId}";
                        int guid = CaveLayoutKernel.CreatePlacementGuid(request,
                            normalized.X, normalized.Y, ruleId);
                        AddPlacement(placements, claimedGuids, new NaturalItemPlacement(guid,
                            rule.ItemId, localX, localY, 0f, 0f, ruleId));
                    }
                }
            }
        }

        /// <summary>只允许可行走、无结构且属于草原或森林的地表格进入入口灌木判定。</summary>
        private static bool IsSurfacePortalShrubCellAvailable(ChunkTerrainBuffer terrain,
            TerrainCell cell, int localX, int localY)
        {
            if ((cell.Flags & (TerrainCellFlags.Water | TerrainCellFlags.Blocking |
                               TerrainCellFlags.Occupied)) != 0 ||
                (cell.Flags & TerrainCellFlags.Walkable) == 0)
            {
                return false;
            }

            return (cell.BiomeId == (int)SurfaceBiomeKind.Grassland ||
                    cell.BiomeId == (int)SurfaceBiomeKind.Forest) &&
                   ReadEnvironment(terrain, "structure", localX, localY) < 0.5f;
        }

        /// <summary>从当前冻结 Profile 中找到符合环境条件的草原/森林 Bush 规则。</summary>
        private static EcologySpawnRuleSnapshot FindSurfacePortalShrubRule(
            IReadOnlyList<EcologySpawnRuleSnapshot> rules, int biomeId,
            double temperature, double precipitation, double height,
            double riverFloodplain)
        {
            if (biomeId != (int)SurfaceBiomeKind.Grassland &&
                biomeId != (int)SurfaceBiomeKind.Forest)
            {
                return null;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                EcologySpawnRuleSnapshot rule = rules[i];
                if (rule == null || rule.CompanionOnly ||
                    !string.Equals(rule.ItemId, "Bush", StringComparison.OrdinalIgnoreCase) ||
                    !rule.Matches(biomeId, temperature, precipitation, height,
                        riverFloodplain))
                {
                    continue;
                }

                return rule;
            }

            return null;
        }

        /// <summary>使用独立盐值判定入口灌木，避免与普通生态随机流互相牵连。</summary>
        private static bool PassesSurfacePortalShrubChance(ChunkGenerationRequest request,
            int worldX, int worldY, double chance)
        {
            chance = Clamp01(chance);
            if (chance <= 0d)
                return false;
            if (chance >= 1d)
                return true;

            uint state = CaveLayoutKernel.Hash(request.WorldSeed, worldX, worldY,
                SurfacePortalShrubSalt);
            return CaveLayoutKernel.NextUnitDouble(ref state) < chance;
        }

        #endregion

        #region 天然传送门

        private static void AddSurfacePortals(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, ChunkGenerationSettingsSnapshot settings,
            List<NaturalItemPlacement> placements, HashSet<int> claimedGuids,
            HashSet<int> portalCells)
        {
            if (!settings.CavePortalEnabled ||
                string.IsNullOrWhiteSpace(settings.CavePortalItemId) ||
                string.IsNullOrWhiteSpace(settings.CavePortalTargetDimensionId))
            {
                return;
            }

            if (UsesSinglePortalChunk(request, terrain, settings))
            {
                AddSurfacePortalsForChunk(request, terrain, settings, request.Address.ChunkOrigin,
                    placements, claimedGuids, portalCells);
                return;
            }

            List<Int2> origins = CollectPortalChunkOrigins(request, terrain, settings);
            for (int i = 0; i < origins.Count; i++)
            {
                AddSurfacePortalsForChunk(request, terrain, settings, origins[i], placements,
                    claimedGuids, portalCells);
            }
        }

        /// <summary>一个正式概率格只选择第一个实际可放置的地表入口。</summary>
        private static void AddSurfacePortalsForChunk(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, ChunkGenerationSettingsSnapshot settings,
            Int2 portalChunkOrigin, List<NaturalItemPlacement> placements,
            HashSet<int> claimedGuids, HashSet<int> portalCells)
        {
            if (!CaveLayoutKernel.ShouldGeneratePortal(request, settings, portalChunkOrigin))
                return;

            for (int candidateIndex = 0;
                 candidateIndex < CaveLayoutKernel.PortalCandidateCount;
                 candidateIndex++)
            {
                Int2 candidate = CaveLayoutKernel.GetPortalCandidate(request, settings,
                    portalChunkOrigin, candidateIndex);
                if (!TryGetLocalCell(request, terrain, candidate, out int localX, out int localY))
                    continue;

                TerrainCell cell = terrain.GetCell(localX, localY);
                if (!IsSurfacePortalCellAvailable(cell,
                        ReadEnvironment(terrain, "structure", localX, localY)))
                {
                    continue;
                }

                int guid = CaveLayoutKernel.CreatePlacementGuid(request, candidate.X,
                    candidate.Y, "cave.portal.surface", candidateIndex);
                AddPlacement(placements, claimedGuids, new NaturalItemPlacement(guid,
                    settings.CavePortalItemId, localX, localY, 0f, 0f,
                    "cave.portal.surface", targetDimensionId:
                    settings.CavePortalTargetDimensionId));
                portalCells.Add(localY * terrain.Width + localX);
                // 一个区块只需要一个可用入口；后续候选只用于跨地表/洞穴布局兜底。
                return;
            }
        }

        private static void AddCavePortals(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, ChunkGenerationSettingsSnapshot settings,
            List<NaturalItemPlacement> placements, HashSet<int> claimedGuids,
            HashSet<int> portalCells, CancellationToken cancellationToken)
        {
            if (!settings.CavePortalEnabled ||
                string.IsNullOrWhiteSpace(settings.CavePortalItemId) ||
                string.IsNullOrWhiteSpace(settings.CavePortalTargetDimensionId))
            {
                return;
            }

            if (UsesSinglePortalChunk(request, terrain, settings))
            {
                AddCavePortalsForChunk(request, terrain, settings, request.Address.ChunkOrigin,
                    placements, claimedGuids, portalCells, cancellationToken);
                return;
            }

            List<Int2> origins = CollectPortalChunkOrigins(request, terrain, settings);
            for (int i = 0; i < origins.Count; i++)
            {
                AddCavePortalsForChunk(request, terrain, settings, origins[i], placements,
                    claimedGuids, portalCells, cancellationToken);
            }
        }

        /// <summary>
        /// 洞穴出口必须复算对应地表的第一个有效候选点。
        /// 旧实现会把四个候选全放进洞穴，导致一个地表入口附近出现多个蓝色出口；
        /// 现在只保留与地表入口同坐标的唯一出口。
        /// </summary>
        private static void AddCavePortalsForChunk(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, ChunkGenerationSettingsSnapshot settings,
            Int2 portalChunkOrigin, List<NaturalItemPlacement> placements,
            HashSet<int> claimedGuids, HashSet<int> portalCells,
            CancellationToken cancellationToken)
        {
            // 洞穴不再使用自身的随机条件决定出口；地表冻结配置是唯一真源。
            if (!TryResolvePairedSurfacePortal(request, portalChunkOrigin,
                    cancellationToken, out SurfacePortalSelection selection) ||
                !TryGetLocalCell(request, terrain, selection.Cell, out int localX,
                    out int localY))
            {
                return;
            }

            TerrainCell cell = terrain.GetCell(localX, localY);
            if ((cell.Flags & TerrainCellFlags.Walkable) == 0)
                return;

            int guid = CaveLayoutKernel.CreatePlacementGuid(request, selection.Cell.X,
                selection.Cell.Y, "cave.portal.cave", selection.CandidateIndex);
            AddPlacement(placements, claimedGuids, new NaturalItemPlacement(guid,
                settings.CavePortalItemId, localX, localY, 0f, 0f,
                "cave.portal.cave", targetDimensionId:
                settings.CavePortalTargetDimensionId));
            portalCells.Add(localY * terrain.Width + localX);
        }

        /// <summary>用冻结的地表 Profile 复算候选格，保证洞穴不会出现无对应入口的出口。</summary>
        private static bool TryResolvePairedSurfacePortal(ChunkGenerationRequest request,
            Int2 portalChunkOrigin, CancellationToken cancellationToken,
            out SurfacePortalSelection selection)
        {
            selection = default;
            CavePortalPairingSnapshot pairing = request.Profile.PortalPairing;
            if (pairing == null || pairing.SurfaceProfile == null)
                return false;

            cancellationToken.ThrowIfCancellationRequested();
            Int2 normalizedOrigin = pairing.SurfaceTopology.IsWrapped
                ? new Int2(pairing.SurfaceTopology.NormalizeX(portalChunkOrigin.X),
                    pairing.SurfaceTopology.NormalizeY(portalChunkOrigin.Y))
                : portalChunkOrigin;
            ChunkGenerationRequest surfacePortalRequest = CreateSurfacePortalRequest(
                request, pairing, pairing.SurfaceProfile, normalizedOrigin);
            var key = new SurfacePortalSelectionKey(pairing.Fingerprint,
                CaveLayoutKernel.GetPortalSeed(surfacePortalRequest,
                    pairing.SurfaceProfile.Settings), normalizedOrigin);
            if (SurfacePortalSelections.Count >= MaxSurfacePortalSelectionCacheEntries)
                SurfacePortalSelections.Clear();

            Lazy<SurfacePortalSelection> lazy = SurfacePortalSelections.GetOrAdd(key,
                _ => new Lazy<SurfacePortalSelection>(() => ResolvePairedSurfacePortal(
                        surfacePortalRequest),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            selection = lazy.Value;
            return selection.HasValue;
        }

        /// <summary>生成一个小型地表纯数据区块，严格复用地表入口的可放置判断。</summary>
        private static SurfacePortalSelection ResolvePairedSurfacePortal(
            ChunkGenerationRequest surfacePortalRequest)
        {
            ChunkGenerationProfileSnapshot sourceProfile = surfacePortalRequest.Profile;
            ChunkGenerationSettingsSnapshot sourceSettings = sourceProfile.Settings;
            if (!CaveLayoutKernel.ShouldGeneratePortal(surfacePortalRequest, sourceSettings,
                    surfacePortalRequest.Address.ChunkOrigin))
            {
                return default;
            }

            ChunkGenerationProfileSnapshot surfaceProfile =
                CreatePortalEligibilitySurfaceProfile(sourceProfile);
            ChunkGenerationRequest surfaceRequest = CreateSurfacePortalRequest(
                surfacePortalRequest, surfaceProfile,
                surfacePortalRequest.Address.ChunkOrigin);
            using ChunkGenerationResult result = SurfacePortalTerrainGenerator.Generate(
                surfaceRequest, CancellationToken.None);
            using ChunkTerrainData surfaceTerrain = result.ConsumeTerrain();

            for (int candidateIndex = 0;
                 candidateIndex < CaveLayoutKernel.PortalCandidateCount;
                 candidateIndex++)
            {
                Int2 candidate = CaveLayoutKernel.GetPortalCandidate(surfacePortalRequest,
                    sourceSettings, surfacePortalRequest.Address.ChunkOrigin, candidateIndex);
                if (!TryGetLocalCell(surfaceRequest, surfaceTerrain.Width, surfaceTerrain.Height,
                        candidate, out int localX, out int localY))
                {
                    continue;
                }

                TerrainCell cell = surfaceTerrain.GetCell(localX, localY);
                float structure = ReadEnvironment(surfaceTerrain, "structure", localX, localY);
                if (IsSurfacePortalCellAvailable(cell, structure))
                    return new SurfacePortalSelection(candidate, candidateIndex);
            }

            return default;
        }

        /// <summary>构造仅用于复核地表入口的请求，始终保留地表自身种子和拓扑。</summary>
        private static ChunkGenerationRequest CreateSurfacePortalRequest(
            ChunkGenerationRequest referenceRequest, CavePortalPairingSnapshot pairing,
            ChunkGenerationProfileSnapshot profile, Int2 origin)
        {
            return new ChunkGenerationRequest(referenceRequest.WorldEpoch,
                new WorldAddress(pairing.SurfaceDimensionId, origin), pairing.SurfaceWorldSeed,
                referenceRequest.RequestVersion, profile, pairing.SurfaceTopology);
        }

        /// <summary>从已有地表请求派生关闭入口物品阶段的纯地形复核请求。</summary>
        private static ChunkGenerationRequest CreateSurfacePortalRequest(
            ChunkGenerationRequest surfacePortalRequest,
            ChunkGenerationProfileSnapshot profile, Int2 origin)
        {
            return new ChunkGenerationRequest(surfacePortalRequest.WorldEpoch,
                new WorldAddress(surfacePortalRequest.Address.DimensionId, origin),
                surfacePortalRequest.WorldSeed, surfacePortalRequest.RequestVersion,
                profile, surfacePortalRequest.Topology);
        }

        /// <summary>入口选择只依赖地形和结构，不需要为了复核而生成生态物。</summary>
        private static ChunkGenerationProfileSnapshot CreatePortalEligibilitySurfaceProfile(
            ChunkGenerationProfileSnapshot source)
        {
            var numbers = new Dictionary<string, double>(source.NumericParameters,
                StringComparer.Ordinal)
            {
                ["cave.portal.enabled"] = 0d
            };
            return new ChunkGenerationProfileSnapshot(
                source.ProfileId,
                source.Signature,
                source.Width,
                source.Height,
                numbers,
                new Dictionary<string, string>(source.TextParameters, StringComparer.Ordinal),
                0d,
                Array.Empty<EcologySpawnRuleSnapshot>(),
                Array.Empty<CaveResourceRuleSnapshot>());
        }

        /// <summary>地表入口与洞穴复核共用同一套禁止水域、障碍和结构占用的条件。</summary>
        private static bool IsSurfacePortalCellAvailable(TerrainCell cell, float structure)
        {
            return (cell.Flags & (TerrainCellFlags.Water | TerrainCellFlags.Blocking |
                                  TerrainCellFlags.Occupied)) == 0 &&
                   (cell.Flags & TerrainCellFlags.Walkable) != 0 &&
                   structure < 0.5f;
        }

        /// <summary>正常运行时一个 Chunk 对应一个概率格，避免为每个区块额外分配集合。</summary>
        private static bool UsesSinglePortalChunk(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, ChunkGenerationSettingsSnapshot settings)
        {
            Int2 portalChunkSize = CaveLayoutKernel.GetPortalChunkSize(request, settings);
            return terrain.Width == portalChunkSize.X && terrain.Height == portalChunkSize.Y;
        }

        /// <summary>连续大区块预览按正式概率格拆分，避免临时预览尺寸改变传送门密度。</summary>
        private static List<Int2> CollectPortalChunkOrigins(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, ChunkGenerationSettingsSnapshot settings)
        {
            Int2 portalChunkSize = CaveLayoutKernel.GetPortalChunkSize(request, settings);
            int firstX = FloorDiv(request.Address.ChunkOrigin.X, portalChunkSize.X);
            int firstY = FloorDiv(request.Address.ChunkOrigin.Y, portalChunkSize.Y);
            int lastX = FloorDiv(request.Address.ChunkOrigin.X + terrain.Width - 1,
                portalChunkSize.X);
            int lastY = FloorDiv(request.Address.ChunkOrigin.Y + terrain.Height - 1,
                portalChunkSize.Y);
            var origins = new List<Int2>(Math.Max(1,
                (lastX - firstX + 1) * (lastY - firstY + 1)));
            HashSet<Int2> uniqueOrigins = request.Topology.IsWrapped ? new HashSet<Int2>() : null;
            for (int chunkY = firstY; chunkY <= lastY; chunkY++)
            {
                for (int chunkX = firstX; chunkX <= lastX; chunkX++)
                {
                    Int2 raw = new(chunkX * portalChunkSize.X,
                        chunkY * portalChunkSize.Y);
                    Int2 normalized = request.Topology.IsWrapped
                        ? new Int2(request.Topology.NormalizeX(raw.X),
                            request.Topology.NormalizeY(raw.Y))
                        : raw;
                    if (uniqueOrigins != null && !uniqueOrigins.Add(normalized))
                        continue;
                    origins.Add(normalized);
                }
            }
            return origins;
        }

        /// <summary>整除向下取整，负坐标预览仍按正确的正式区块边界分组。</summary>
        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }

        #endregion

        #region 可采集藤蔓

        /// <summary>复用现有 Twine 自然物，使矿洞藤蔓可拾取、可存档并能参与制作。</summary>
        private static bool TryAddVine(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY,
            int localX, int localY, List<NaturalItemPlacement> placements,
            HashSet<int> claimedGuids)
        {
            if (!CaveLayoutKernel.ShouldPlaceVine(request, settings, worldX, worldY))
                return false;

            const string ruleId = "cave.vine.twine";
            uint state = CaveLayoutKernel.Hash(request.WorldSeed, worldX, worldY,
                unchecked((int)0x56b7c12d));
            float offsetX = (float)Lerp(-0.16d, 0.16d,
                CaveLayoutKernel.NextUnitDouble(ref state));
            float offsetY = (float)Lerp(-0.12d, 0.12d,
                CaveLayoutKernel.NextUnitDouble(ref state));
            int guid = CaveLayoutKernel.CreatePlacementGuid(request, worldX, worldY, ruleId);
            AddPlacement(placements, claimedGuids, new NaturalItemPlacement(guid, "Twine",
                localX, localY, offsetX, offsetY, ruleId));
            return true;
        }

        #endregion

        #region 矿物

        private static bool TryAddWallResource(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            IReadOnlyList<CaveResourceRuleSnapshot> rules, int worldX, int worldY,
            int localX, int localY, List<NaturalItemPlacement> placements,
            HashSet<int> claimedGuids)
        {
            double depositStrength = CaveLayoutKernel.GetDepositStrength(request, worldX, worldY);
            double depositFactor = InverseLerp(0.48d, 0.82d, depositStrength);
            if (depositFactor <= 0d)
                return false;

            uint state = CaveLayoutKernel.Hash(request.WorldSeed, worldX, worldY,
                unchecked((int)0x7f4a7c15));
            double chance = Clamp01(settings.CaveResourceDensity * depositFactor * 1.5d);
            if (CaveLayoutKernel.NextUnitDouble(ref state) > chance)
                return false;

            CaveResourceRuleSnapshot selected = SelectResource(request, rules, worldX, worldY);
            if (selected == null)
                return false;

            int guid = CaveLayoutKernel.CreatePlacementGuid(request, worldX, worldY,
                selected.RuleId);
            AddPlacement(placements, claimedGuids, new NaturalItemPlacement(guid,
                selected.ItemId, localX, localY, 0f, 0f, selected.RuleId));
            return true;
        }

        private static void TryAddLooseOre(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            IReadOnlyList<CaveResourceRuleSnapshot> rules, int worldX, int worldY,
            int localX, int localY, List<NaturalItemPlacement> placements,
            HashSet<int> claimedGuids)
        {
            if (settings.CaveLooseOreDensity <= 0d)
                return;

            uint state = CaveLayoutKernel.Hash(request.WorldSeed ^ unchecked((int)0x6c8e9cf5),
                worldX, worldY, 0x1b873593);
            if (CaveLayoutKernel.NextUnitDouble(ref state) > settings.CaveLooseOreDensity)
                return;

            CaveResourceRuleSnapshot selected = SelectResource(request, rules, worldX, worldY);
            string itemId = ToLooseOreItemId(selected?.ItemId);
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            float offsetX = (float)Lerp(-0.22d, 0.22d,
                CaveLayoutKernel.NextUnitDouble(ref state));
            float offsetY = (float)Lerp(-0.22d, 0.22d,
                CaveLayoutKernel.NextUnitDouble(ref state));
            int guid = CaveLayoutKernel.CreatePlacementGuid(request, worldX, worldY,
                $"cave.loose.{itemId}");
            AddPlacement(placements, claimedGuids, new NaturalItemPlacement(guid, itemId,
                localX, localY, offsetX, offsetY, $"cave.loose.{selected.RuleId}"));
        }

        private static CaveResourceRuleSnapshot SelectResource(ChunkGenerationRequest request,
            IReadOnlyList<CaveResourceRuleSnapshot> rules, int worldX, int worldY)
        {
            if (rules == null || rules.Count == 0)
                return null;
            for (int i = 0; i < rules.Count; i++)
            {
                CaveResourceRuleSnapshot rule = rules[i];
                if (rule != null && CaveLayoutKernel.SampleVein(request, worldX, worldY,
                        rule) >= rule.VeinThreshold)
                {
                    return rule;
                }
            }
            return rules[rules.Count - 1];
        }

        private static string ToLooseOreItemId(string mineItemId)
        {
            const string minePrefix = "Mine_";
            return !string.IsNullOrWhiteSpace(mineItemId) &&
                   mineItemId.StartsWith(minePrefix, StringComparison.Ordinal)
                ? $"Ore_{mineItemId.Substring(minePrefix.Length)}"
                : null;
        }

        #endregion

        #region 辅助

        private static void AddPlacement(List<NaturalItemPlacement> placements,
            HashSet<int> claimedGuids, NaturalItemPlacement placement)
        {
            int guid = placement.Guid;
            while (!claimedGuids.Add(guid))
                guid = guid == int.MaxValue ? 1 : guid + 1;
            if (guid == placement.Guid)
            {
                placements.Add(placement);
                return;
            }

            placements.Add(new NaturalItemPlacement(guid, placement.ItemId,
                placement.LocalX, placement.LocalY, placement.OffsetX, placement.OffsetY,
                placement.RuleId, placement.HostGuid, placement.TargetDimensionId));
        }

        private static bool TryGetLocalCell(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, Int2 worldCell, out int localX, out int localY)
        {
            return TryGetLocalCell(request, terrain.Width, terrain.Height, worldCell,
                out localX, out localY);
        }

        /// <summary>把世界坐标换算到指定纯地形数据的本地区域，兼容有限世界边界绕回。</summary>
        private static bool TryGetLocalCell(ChunkGenerationRequest request,
            int terrainWidth, int terrainHeight, Int2 worldCell, out int localX, out int localY)
        {
            int originX = request.Address.ChunkOrigin.X;
            int originY = request.Address.ChunkOrigin.Y;
            localX = worldCell.X - originX;
            localY = worldCell.Y - originY;
            if (request.Topology.IsWrapped)
            {
                if (localX < 0)
                    localX += request.Topology.Span.X;
                if (localY < 0)
                    localY += request.Topology.Span.Y;
            }
            return (uint)localX < (uint)terrainWidth &&
                   (uint)localY < (uint)terrainHeight;
        }

        private static float ReadEnvironment(ChunkTerrainBuffer terrain, string layerId,
            int x, int y)
        {
            return terrain.TryGetEnvironmentValue(layerId, x, y, out float value)
                ? value
                : 0f;
        }

        private static float ReadEnvironment(ChunkTerrainData terrain, string layerId,
            int x, int y)
        {
            return terrain.TryGetEnvironmentValue(layerId, x, y, out float value)
                ? value
                : 0f;
        }

        private static double InverseLerp(double from, double to, double value)
        {
            if (to <= from)
                return value >= to ? 1d : 0d;
            return Clamp01((value - from) / (to - from));
        }

        private static double Lerp(double left, double right, double t) =>
            left + (right - left) * t;
        private static double Clamp01(double value) =>
            value < 0d ? 0d : value > 1d ? 1d : value;

        #endregion
    }
}
