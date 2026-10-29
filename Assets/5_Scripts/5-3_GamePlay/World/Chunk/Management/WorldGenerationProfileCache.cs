using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;

// 缓存冻结配置并核对所有可原位修改的生成输入，不持有区块差量。
internal sealed class WorldGenerationProfileCache
{
    #region 世界生成配置缓存
    internal readonly struct Inputs
    {
        public Inputs(PlanetData planet, object save, object catalog, object resources, int resourceVersion,
            object naturalCatalog, DimensionDefinition definition, int seed, bool frozen, bool authority)
        {
            Planet = planet; Save = save; Catalog = catalog; Resources = resources;
            ResourceVersion = resourceVersion; NaturalCatalog = naturalCatalog; Definition = definition;
            Seed = seed; Frozen = frozen; Authority = authority;
        }
        public readonly PlanetData Planet;
        public readonly object Save, Catalog, Resources, NaturalCatalog;
        public readonly DimensionDefinition Definition;
        public readonly int ResourceVersion, Seed;
        public readonly bool Frozen, Authority;
    }

    private Inputs inputs;
    private string worldName, preset, portalItem, portalTarget;
    private int chunkWidth, chunkHeight;
    private float spatialScale;
    private bool paired;
    private Dictionary<string, double> numbers;
    private Dictionary<string, string> texts;
    private FrozenConfigurationSnapshot frozenConfiguration;
    private ChunkGenerationProfileSnapshot profile, tileCatalog;

    public bool TryGet(Inputs current, out ChunkGenerationProfileSnapshot result,
        out ChunkGenerationProfileSnapshot currentTileCatalog)
    {
        result = null; currentTileCatalog = null;
        PlanetData planet = current.Planet;
        DimensionDefinition definition = current.Definition;
        if (profile == null || planet == null || !ReferenceEquals(inputs.Planet, planet) ||
            !ReferenceEquals(inputs.Save, current.Save) || !ReferenceEquals(inputs.Catalog, current.Catalog) ||
            !ReferenceEquals(inputs.Resources, current.Resources) ||
            !ReferenceEquals(inputs.NaturalCatalog, current.NaturalCatalog) ||
            !ReferenceEquals(inputs.Definition, definition) || inputs.ResourceVersion != current.ResourceVersion ||
            inputs.Seed != current.Seed || inputs.Frozen != current.Frozen || inputs.Authority != current.Authority ||
            worldName != planet.Name || preset != planet.GenerationPresetId ||
            chunkWidth != planet.ChunkSize.x || chunkHeight != planet.ChunkSize.y ||
            !spatialScale.Equals(planet.SpatialDistanceScale) ||
            paired != (definition?.PairPortalsWithTargetWorld ?? false) ||
            portalItem != definition?.PortalItemId || portalTarget != definition?.PortalTargetDimensionId ||
            !SameValues(numbers, planet.GenerationNumericOverrides) ||
            !SameValues(texts, planet.GenerationTextOverrides) ||
            (current.Frozen && !frozenConfiguration.Matches(planet.Ecology))) return false;
        result = profile; currentTileCatalog = tileCatalog;
        return true;
    }

    public void Store(Inputs current, ChunkGenerationProfileSnapshot result,
        ChunkGenerationProfileSnapshot currentTileCatalog)
    {
        PlanetData planet = current.Planet;
        inputs = current;
        worldName = planet.Name; preset = planet.GenerationPresetId;
        chunkWidth = planet.ChunkSize.x; chunkHeight = planet.ChunkSize.y;
        spatialScale = planet.SpatialDistanceScale;
        paired = current.Definition?.PairPortalsWithTargetWorld ?? false;
        portalItem = current.Definition?.PortalItemId; portalTarget = current.Definition?.PortalTargetDimensionId;
        numbers = CopyValues(planet.GenerationNumericOverrides);
        texts = CopyValues(planet.GenerationTextOverrides);
        frozenConfiguration = current.Frozen ? new FrozenConfigurationSnapshot(planet.Ecology) : null;
        profile = result; tileCatalog = currentTileCatalog;
    }

    public void Clear()
    {
        inputs = default; profile = tileCatalog = null; numbers = null; texts = null;
        frozenConfiguration = null; worldName = preset = portalItem = portalTarget = null;
    }

    private static Dictionary<string, T> CopyValues<T>(Dictionary<string, T> source) =>
        source == null ? null : new Dictionary<string, T>(source, StringComparer.Ordinal);

    internal static bool SameDouble(double left, double right) =>
        BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);

    internal static bool SameNullableDouble(double? left, double? right) =>
        left.HasValue == right.HasValue && (!left.HasValue || SameDouble(left.Value, right.Value));

    private static bool SameValues(Dictionary<string, double> saved, Dictionary<string, double> current)
    {
        if (saved == null || current == null) return saved == null && current == null;
        if (saved.Count != current.Count) return false;
        foreach (var pair in current)
            if (!saved.TryGetValue(pair.Key, out double value) || !SameDouble(value, pair.Value)) return false;
        return true;
    }

    private static bool SameValues<T>(Dictionary<string, T> saved, Dictionary<string, T> current)
    {
        if (saved == null || current == null) return saved == null && current == null;
        if (saved.Count != current.Count) return false;
        foreach (var pair in current)
            if (!saved.TryGetValue(pair.Key, out T value) || !EqualityComparer<T>.Default.Equals(value, pair.Value))
                return false;
        return true;
    }
    #endregion

    #region 冻结存档配置比较
    private sealed class FrozenConfigurationSnapshot
    {
        private readonly EcologyWorldSaveData source;
        private readonly int version;
        private readonly string profileId;
        private readonly ulong ecologyFingerprint;
        private readonly double multiplier;
        private readonly EcologyRuleSaveData[] rules;
        private readonly WorldGenerationProfileSaveData generation;
        private readonly int generationVersion;
        private readonly string generationProfileId;
        private readonly ulong generationFingerprint;
        private readonly (bool present, string id, double value)[] numbers;
        private readonly (bool present, string id, string value)[] texts;
        private readonly (bool present, string id, string item, double threshold, double scale, int offset)[] minerals;

        public FrozenConfigurationSnapshot(EcologyWorldSaveData ecology)
        {
            source = ecology;
            if (ecology == null) return;
            version = ecology.DataVersion; profileId = ecology.ProfileId;
            ecologyFingerprint = ecology.ConfigurationFingerprint; multiplier = ecology.GlobalMultiplier;
            if (ecology.Rules != null)
            {
                rules = new EcologyRuleSaveData[ecology.Rules.Count];
                for (int i = 0; i < rules.Length; i++) rules[i] = ecology.Rules[i]?.CopyGenerationConfiguration();
            }
            generation = ecology.Generation;
            if (generation == null) return;
            generationVersion = generation.DataVersion; generationProfileId = generation.ProfileId;
            generationFingerprint = generation.GenerationFingerprint;
            if (generation.NumericParameters != null)
            {
                numbers = new (bool, string, double)[generation.NumericParameters.Count];
                for (int i = 0; i < numbers.Length; i++)
                { var item = generation.NumericParameters[i]; numbers[i] = (item != null, item?.Id, item?.Value ?? 0d); }
            }
            if (generation.TextParameters != null)
            {
                texts = new (bool, string, string)[generation.TextParameters.Count];
                for (int i = 0; i < texts.Length; i++)
                { var item = generation.TextParameters[i]; texts[i] = (item != null, item?.Id, item?.Value); }
            }
            if (generation.CaveResourceRules != null)
            {
                minerals = new (bool, string, string, double, double, int)[generation.CaveResourceRules.Count];
                for (int i = 0; i < minerals.Length; i++)
                { var item = generation.CaveResourceRules[i]; minerals[i] = (item != null, item?.RuleId, item?.ItemId,
                    item?.VeinThreshold ?? 0d, item?.VeinScale ?? 0d, item?.NoiseOffset ?? 0); }
            }
        }

        public bool Matches(EcologyWorldSaveData current)
        {
            if (!ReferenceEquals(source, current)) return false;
            if (current == null) return true;
            if (version != current.DataVersion || profileId != current.ProfileId ||
                ecologyFingerprint != current.ConfigurationFingerprint || !SameDouble(multiplier, current.GlobalMultiplier) ||
                !ReferenceEquals(generation, current.Generation) || !SameCount(rules, current.Rules)) return false;
            if (rules != null)
                for (int i = 0; i < rules.Length; i++)
                    if (rules[i] == null ? current.Rules[i] != null :
                        !rules[i].MatchesGenerationConfiguration(current.Rules[i])) return false;
            if (generation == null) return true;
            if (generationVersion != generation.DataVersion || generationProfileId != generation.ProfileId ||
                generationFingerprint != generation.GenerationFingerprint ||
                !SameCount(numbers, generation.NumericParameters) || !SameCount(texts, generation.TextParameters) ||
                !SameCount(minerals, generation.CaveResourceRules)) return false;
            if (numbers != null)
                for (int i = 0; i < numbers.Length; i++)
                { var item = generation.NumericParameters[i]; var saved = numbers[i];
                  if (saved.present != (item != null) || (item != null &&
                      (saved.id != item.Id || !SameDouble(saved.value, item.Value)))) return false; }
            if (texts != null)
                for (int i = 0; i < texts.Length; i++)
                { var item = generation.TextParameters[i]; var saved = texts[i];
                  if (saved.present != (item != null) || (item != null &&
                      (saved.id != item.Id || saved.value != item.Value))) return false; }
            if (minerals != null)
                for (int i = 0; i < minerals.Length; i++)
                { var item = generation.CaveResourceRules[i]; var saved = minerals[i];
                  if (saved.present != (item != null) || (item != null &&
                      (saved.id != item.RuleId || saved.item != item.ItemId ||
                       !SameDouble(saved.threshold, item.VeinThreshold) || !SameDouble(saved.scale, item.VeinScale) ||
                       saved.offset != item.NoiseOffset))) return false; }
            return true;
        }

        private static bool SameCount<TSaved, TCurrent>(TSaved[] saved, List<TCurrent> current) =>
            saved == null ? current == null : current != null && saved.Length == current.Count;
    }
    #endregion
}

public partial class EcologyRuleSaveData
{
    #region 冻结规则缓存比较
    internal EcologyRuleSaveData CopyGenerationConfiguration()
    {
        var copy = (EcologyRuleSaveData)MemberwiseClone();
        copy.ProvidedTags = ProvidedTags == null ? null : new List<string>(ProvidedTags);
        return copy;
    }

    internal bool MatchesGenerationConfiguration(EcologyRuleSaveData other)
    {
        if (other == null) return false;
        if (ProvidedTags == null || other.ProvidedTags == null)
        { if (ProvidedTags != null || other.ProvidedTags != null) return false; }
        else
        {
            if (ProvidedTags.Count != other.ProvidedTags.Count) return false;
            for (int i = 0; i < ProvidedTags.Count; i++)
                if (ProvidedTags[i] != other.ProvidedTags[i]) return false;
        }
        return RuleId == other.RuleId &&
            ItemId == other.ItemId &&
            ItemCount == other.ItemCount &&
            ItemCountDistribution == other.ItemCountDistribution &&
            ItemCountMin == other.ItemCountMin &&
            WorldGenerationProfileCache.SameDouble(SpawnChance, other.SpawnChance) &&
            WorldGenerationProfileCache.SameDouble(SpawnChanceMultiplier, other.SpawnChanceMultiplier) &&
            DistributionMode == other.DistributionMode &&
            PatchSpacing == other.PatchSpacing &&
            WorldGenerationProfileCache.SameDouble(PatchRadius, other.PatchRadius) &&
            WorldGenerationProfileCache.SameDouble(PatchChance, other.PatchChance) &&
            BiomeMask == other.BiomeMask &&
            WorldGenerationProfileCache.SameDouble(MinTemperature, other.MinTemperature) &&
            WorldGenerationProfileCache.SameDouble(MaxTemperature, other.MaxTemperature) &&
            WorldGenerationProfileCache.SameDouble(MinPrecipitation, other.MinPrecipitation) &&
            WorldGenerationProfileCache.SameDouble(MaxPrecipitation, other.MaxPrecipitation) &&
            WorldGenerationProfileCache.SameDouble(MinHeight, other.MinHeight) &&
            WorldGenerationProfileCache.SameDouble(MaxHeight, other.MaxHeight) &&
            CompanionOnly == other.CompanionOnly &&
            CompanionHostTag == other.CompanionHostTag &&
            WorldGenerationProfileCache.SameDouble(CompanionSpawnChance, other.CompanionSpawnChance) &&
            WorldGenerationProfileCache.SameDouble(CompanionOffsetX, other.CompanionOffsetX) &&
            WorldGenerationProfileCache.SameDouble(CompanionOffsetY, other.CompanionOffsetY) &&
            WorldGenerationProfileCache.SameDouble(CompanionMinRadius, other.CompanionMinRadius) &&
            WorldGenerationProfileCache.SameDouble(CompanionMaxRadius, other.CompanionMaxRadius) &&
            WorldGenerationProfileCache.SameDouble(MinRiverFloodplainStrength, other.MinRiverFloodplainStrength) &&
            RequiredChunkTag == other.RequiredChunkTag &&
            RequiredTagChunkRadius == other.RequiredTagChunkRadius &&
            WorldGenerationProfileCache.SameNullableDouble(MaxRiverFloodplainStrength, other.MaxRiverFloodplainStrength) &&
            RequiredEnvironmentLayer == other.RequiredEnvironmentLayer &&
            WorldGenerationProfileCache.SameDouble(MinimumEnvironmentValue, other.MinimumEnvironmentValue) &&
            WorldGenerationProfileCache.SameNullableDouble(MaximumEnvironmentValue, other.MaximumEnvironmentValue) &&
            RequireNaturalPlantableGround == other.RequireNaturalPlantableGround &&
            SharedRadius == other.SharedRadius &&
            FootprintWidth == other.FootprintWidth &&
            FootprintHeight == other.FootprintHeight;
    }
    #endregion
}
