using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 一条自然生态物品规则的无 Unity 配置副本。
    /// 概率按“每个候选地块一次判定”计算，所有字段都能直接转换为 JSON。
    /// </summary>
    public sealed class EcologySpawnRuleSnapshot
    {
        #region 数据快照

        public EcologySpawnRuleSnapshot(
            string ruleId,
            string itemId,
            int itemCount,
            double spawnChance,
            double spawnChanceMultiplier,
            int biomeMask,
            double minTemperature,
            double maxTemperature,
            double minPrecipitation,
            double maxPrecipitation,
            double minHeight,
            double maxHeight,
            IEnumerable<string> providedTags = null,
            bool companionOnly = false,
            string companionHostTag = null,
            double companionSpawnChance = 0d,
            double companionOffsetX = 0d,
            double companionOffsetY = 0d,
            double companionMinRadius = 0d,
            double companionMaxRadius = 0d)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
                throw new ArgumentException("Ecology rule id is required.", nameof(ruleId));
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Ecology item id is required.", nameof(itemId));

            RuleId = ruleId.Trim();
            ItemId = itemId.Trim();
            ItemCount = Math.Max(1, itemCount);
            SpawnChance = Clamp01(spawnChance);
            SpawnChanceMultiplier = Math.Max(0d, Finite(spawnChanceMultiplier, 1d));
            BiomeMask = biomeMask;
            MinTemperature = Clamp01(minTemperature);
            MaxTemperature = Math.Max(MinTemperature, Clamp01(maxTemperature));
            MinPrecipitation = Clamp01(minPrecipitation);
            MaxPrecipitation = Math.Max(MinPrecipitation, Clamp01(maxPrecipitation));
            MinHeight = Clamp01(minHeight);
            MaxHeight = Math.Max(MinHeight, Clamp01(maxHeight));
            ProvidedTags = new ReadOnlyCollection<string>(NormalizeTags(providedTags));
            CompanionOnly = companionOnly;
            CompanionHostTag = companionHostTag?.Trim() ?? string.Empty;
            CompanionSpawnChance = Clamp01(companionSpawnChance);
            CompanionOffsetX = Finite(companionOffsetX, 0d);
            CompanionOffsetY = Finite(companionOffsetY, 0d);
            CompanionMinRadius = Math.Max(0d, Finite(companionMinRadius, 0d));
            CompanionMaxRadius = Math.Max(CompanionMinRadius, Finite(companionMaxRadius, 0d));
        }

        public string RuleId { get; }
        public string ItemId { get; }
        public int ItemCount { get; }
        public double SpawnChance { get; }
        public double SpawnChanceMultiplier { get; }
        /// <summary>0 表示不限制群系；其他值按 SurfaceBiomeKind 的位编号匹配。</summary>
        public int BiomeMask { get; }
        public double MinTemperature { get; }
        public double MaxTemperature { get; }
        public double MinPrecipitation { get; }
        public double MaxPrecipitation { get; }
        public double MinHeight { get; }
        public double MaxHeight { get; }
        public IReadOnlyList<string> ProvidedTags { get; }
        public bool CompanionOnly { get; }
        public string CompanionHostTag { get; }
        public double CompanionSpawnChance { get; }
        public double CompanionOffsetX { get; }
        public double CompanionOffsetY { get; }
        public double CompanionMinRadius { get; }
        public double CompanionMaxRadius { get; }

        #endregion

        #region 规则校验

        /// <summary>检查地形群系和三个生成期环境通道是否符合规则。</summary>
        public bool Matches(int biomeId, double temperature, double precipitation, double height)
        {
            if (BiomeMask != 0 && (biomeId < 0 || biomeId >= 31 ||
                (BiomeMask & (1 << biomeId)) == 0))
            {
                return false;
            }

            return temperature >= MinTemperature && temperature <= MaxTemperature &&
                   precipitation >= MinPrecipitation && precipitation <= MaxPrecipitation &&
                   height >= MinHeight && height <= MaxHeight;
        }

        private static List<string> NormalizeTags(IEnumerable<string> tags)
        {
            var result = new List<string>();
            if (tags == null)
                return result;

            foreach (string tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag))
                    continue;
                string normalized = tag.Trim();
                bool alreadyAdded = false;
                for (int i = 0; i < result.Count; i++)
                {
                    if (string.Equals(result[i], normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyAdded = true;
                        break;
                    }
                }
                if (!alreadyAdded)
                    result.Add(normalized);
            }
            return result;
        }

        private static double Finite(double value, double fallback)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
        }

        private static double Clamp01(double value)
        {
            value = Finite(value, 0d);
            return value < 0d ? 0d : value > 1d ? 1d : value;
        }

        #endregion
    }

    /// <summary>
    /// 一个自然物品的确定性放置记录。
    /// 位置以区块内格子和小数偏移保存，避免生成线程引用 Unity Vector2。
    /// </summary>
    public readonly struct NaturalItemPlacement
    {
        #region 数据

        public NaturalItemPlacement(int guid, string itemId, int localX, int localY,
            float offsetX, float offsetY, string ruleId, int hostGuid = 0,
            string targetDimensionId = null)
        {
            Guid = guid == 0 ? 1 : guid;
            ItemId = itemId ?? string.Empty;
            LocalX = localX;
            LocalY = localY;
            OffsetX = offsetX;
            OffsetY = offsetY;
            RuleId = ruleId ?? string.Empty;
            HostGuid = hostGuid;
            TargetDimensionId = targetDimensionId?.Trim() ?? string.Empty;
        }

        public int Guid { get; }
        public string ItemId { get; }
        public int LocalX { get; }
        public int LocalY { get; }
        public float OffsetX { get; }
        public float OffsetY { get; }
        public string RuleId { get; }
        public int HostGuid { get; }
        public bool IsCompanion => HostGuid != 0;
        /// <summary>非空表示该自然物是跨维度传送门；目标位置默认与当前世界格一一对应。</summary>
        public string TargetDimensionId { get; }
        public bool IsDimensionPortal => !string.IsNullOrWhiteSpace(TargetDimensionId);

        #endregion
    }

    /// <summary>一个区块的自然物品生成结果，只保存纯数据，不持有 Item 或 GameObject。</summary>
    public sealed class ChunkEcologyData
    {
        #region 数据

        private readonly NaturalItemPlacement[] placements;

        public ChunkEcologyData(IEnumerable<NaturalItemPlacement> placements = null)
        {
            this.placements = placements == null
                ? Array.Empty<NaturalItemPlacement>()
                : new List<NaturalItemPlacement>(placements).ToArray();
        }

        public static ChunkEcologyData Empty { get; } = new();
        public IReadOnlyList<NaturalItemPlacement> Placements => placements;
        public int Count => placements.Length;

        #endregion
    }

    /// <summary>
    /// 在已完成的纯地形上执行生态阶段。
    /// 宿主和伴生物都由规则声明，不读取 Prefab 标签，因此后台生成可以完全无头运行。
    /// </summary>
    public static class ChunkEcologyGenerator
    {
        #region 生成常量

        private const uint PlacementSalt = 0x6e636f6cU;
        private const uint CompanionSalt = 0x636f6d70U;
        private const uint OffsetSalt = 0x6f666673U;

        #endregion

        #region 生成入口

        public static ChunkEcologyData Generate(
            ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain,
            double globalMultiplier,
            IReadOnlyList<EcologySpawnRuleSnapshot> rules,
            CancellationToken cancellationToken)
        {
            if (terrain == null)
                throw new ArgumentNullException(nameof(terrain));
            if (request.Profile.Settings.Mode != ChunkGenerationMode.Surface ||
                globalMultiplier <= 0d || rules == null || rules.Count == 0)
            {
                return ChunkEcologyData.Empty;
            }

            var placements = new List<NaturalItemPlacement>();
            var claimedGuids = new HashSet<int>();
            // 每个格子只需要当前格的宿主关系；复用字典避免为每个可走格分配一次 Dictionary。
            var hosts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var hostRules = new List<EcologySpawnRuleSnapshot>();
            var companionRules = new List<EcologySpawnRuleSnapshot>();
            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                EcologySpawnRuleSnapshot rule = rules[ruleIndex];
                if (rule == null)
                    continue;
                if (rule.CompanionOnly)
                    companionRules.Add(rule);
                else
                    hostRules.Add(rule);
            }
            int startX = request.Address.ChunkOrigin.X;
            int startY = request.Address.ChunkOrigin.Y;

            for (int y = 0; y < terrain.Height; y++)
            {
                for (int x = 0; x < terrain.Width; x++)
                {
                    if (((y * terrain.Width + x) & 63) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    TerrainCell cell = terrain.GetCell(x, y);
                    hosts.Clear();
                    if (!IsValidNaturalCell(cell, terrain, x, y))
                        continue;

                    double temperature = ReadEnvironment(terrain, "temperature", x, y);
                    double precipitation = ReadEnvironment(terrain, "precipitation", x, y);
                    double height = ReadEnvironment(terrain, "height", x, y);

                    // 先处理宿主规则，保证伴生物的宿主关系与规则顺序无关。
                    for (int ruleIndex = 0; ruleIndex < hostRules.Count; ruleIndex++)
                    {
                        EcologySpawnRuleSnapshot rule = hostRules[ruleIndex];
                        if (!rule.Matches(cell.BiomeId, temperature, precipitation, height))
                        {
                            continue;
                        }
                        double chance = Clamp01(globalMultiplier * rule.SpawnChance *
                            rule.SpawnChanceMultiplier);
                        if (!PassesChance(request, startX + x, startY + y, rule, chance,
                            PlacementSalt))
                        {
                            continue;
                        }

                        for (int itemIndex = 0; itemIndex < rule.ItemCount; itemIndex++)
                        {
                            int guid = CreateGuid(request, startX + x, startY + y,
                                rule, itemIndex, 0, claimedGuids);
                            placements.Add(new NaturalItemPlacement(guid, rule.ItemId, x, y,
                                0f, 0f, rule.RuleId));
                            for (int tagIndex = 0; tagIndex < rule.ProvidedTags.Count; tagIndex++)
                            {
                                string tag = rule.ProvidedTags[tagIndex];
                                if (!hosts.TryGetValue(tag, out int currentHostGuid) ||
                                    guid < currentHostGuid)
                                {
                                    hosts[tag] = guid;
                                }
                            }
                        }
                    }

                    for (int ruleIndex = 0; ruleIndex < companionRules.Count; ruleIndex++)
                    {
                        EcologySpawnRuleSnapshot rule = companionRules[ruleIndex];
                        if (string.IsNullOrWhiteSpace(rule.CompanionHostTag) ||
                            !rule.Matches(cell.BiomeId, temperature, precipitation, height) ||
                            !hosts.TryGetValue(rule.CompanionHostTag, out int hostGuid))
                        {
                            continue;
                        }

                        double companionChance = rule.CompanionSpawnChance;
                        double chance = Clamp01(globalMultiplier * companionChance *
                            rule.SpawnChanceMultiplier);
                        if (!PassesChance(request, startX + x, startY + y, rule, chance,
                            CompanionSalt))
                        {
                            continue;
                        }

                        for (int itemIndex = 0; itemIndex < rule.ItemCount; itemIndex++)
                        {
                            int guid = CreateGuid(request, startX + x, startY + y,
                                rule, itemIndex, hostGuid, claimedGuids);
                            ResolveCompanionOffset(request, startX + x, startY + y, rule,
                                itemIndex, out float offsetX, out float offsetY);
                            placements.Add(new NaturalItemPlacement(guid, rule.ItemId, x, y,
                                offsetX, offsetY, rule.RuleId, hostGuid));
                        }
                    }
                }
            }

            return placements.Count == 0 ? ChunkEcologyData.Empty : new ChunkEcologyData(placements);
        }

        #endregion

        #region 生成辅助

        private static bool IsValidNaturalCell(TerrainCell cell, ChunkTerrainBuffer terrain,
            int x, int y)
        {
            if ((cell.Flags & (TerrainCellFlags.Water | TerrainCellFlags.Blocking |
                               TerrainCellFlags.Occupied)) != 0 ||
                (cell.Flags & TerrainCellFlags.Walkable) == 0)
            {
                return false;
            }

            return ReadEnvironment(terrain, "structure", x, y) < 0.5d;
        }

        private static double ReadEnvironment(ChunkTerrainBuffer terrain, string layerId,
            int x, int y)
        {
            return terrain.TryGetEnvironmentValue(layerId, x, y, out float value) ? value : 0d;
        }

        private static bool PassesChance(ChunkGenerationRequest request, int worldX, int worldY,
            EcologySpawnRuleSnapshot rule, double chance, uint salt)
        {
            if (chance <= 0d)
                return false;
            if (chance >= 1d)
                return true;
            ulong seed = CreateSeed(request, rule.RuleId, salt);
            return Hash01(seed, worldX, worldY) <= chance;
        }

        private static int CreateGuid(ChunkGenerationRequest request, int worldX, int worldY,
            EcologySpawnRuleSnapshot rule, int itemIndex, int hostGuid,
            HashSet<int> claimedGuids)
        {
            ulong seed = CreateSeed(request, rule.RuleId, PlacementSalt);
            uint raw = Hash(seed ^ (uint)hostGuid, worldX + itemIndex * 31,
                worldY - itemIndex * 17);
            int guid = unchecked((int)(raw & 0x7fffffffU));
            if (guid == 0)
                guid = 1;
            while (!claimedGuids.Add(guid))
            {
                guid = guid == int.MaxValue ? 1 : guid + 1;
            }
            return guid;
        }

        private static void ResolveCompanionOffset(ChunkGenerationRequest request,
            int worldX, int worldY, EcologySpawnRuleSnapshot rule, int itemIndex,
            out float offsetX, out float offsetY)
        {
            double minRadius = rule.CompanionMinRadius;
            double maxRadius = rule.CompanionMaxRadius;
            double radius = minRadius;
            double angle = 0d;
            if (maxRadius > 0d)
            {
                ulong seed = CreateSeed(request, rule.RuleId, OffsetSalt);
                uint angleHash = Hash(seed, worldX + itemIndex * 13, worldY + itemIndex * 7);
                uint radiusHash = Hash(seed ^ 0x9e3779b9U, worldX - itemIndex * 5,
                    worldY + itemIndex * 11);
                angle = angleHash / (double)uint.MaxValue * Math.PI * 2d;
                double t = radiusHash / (double)uint.MaxValue;
                radius = Math.Sqrt(minRadius * minRadius +
                    (maxRadius * maxRadius - minRadius * minRadius) * t);
            }

            offsetX = (float)(rule.CompanionOffsetX + Math.Cos(angle) * radius);
            offsetY = (float)(rule.CompanionOffsetY + Math.Sin(angle) * radius);
        }

        private static ulong CreateSeed(ChunkGenerationRequest request, string ruleId,
            uint salt)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                hash = (hash ^ (uint)request.WorldSeed) * 1099511628211UL;
                hash = (hash ^ salt) * 1099511628211UL;
                for (int i = 0; i < request.Address.DimensionId.Length; i++)
                    hash = (hash ^ request.Address.DimensionId[i]) * 1099511628211UL;
                for (int i = 0; i < ruleId.Length; i++)
                    hash = (hash ^ ruleId[i]) * 1099511628211UL;
                return hash == 0 ? 0xd1b54a32d192ed03UL : hash;
            }
        }

        private static uint Hash(ulong seed, int x, int y)
        {
            unchecked
            {
                ulong value = seed;
                value ^= (ulong)(uint)x * 0x9e3779b185ebca87UL;
                value ^= (ulong)(uint)y * 0xc2b2ae3d27d4eb4fUL;
                value ^= value >> 30;
                value *= 0xbf58476d1ce4e5b9UL;
                value ^= value >> 27;
                value *= 0x94d049bb133111ebUL;
                value ^= value >> 31;
                return (uint)(value >> 32);
            }
        }

        private static double Hash01(ulong seed, int x, int y)
        {
            return Hash(seed, x, y) / (double)uint.MaxValue;
        }

        private static double Clamp01(double value)
        {
            return value <= 0d ? 0d : value >= 1d ? 1d : value;
        }

        #endregion
    }
}
