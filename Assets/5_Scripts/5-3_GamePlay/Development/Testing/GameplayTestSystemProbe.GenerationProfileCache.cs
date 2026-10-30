using System;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 世界生成配置缓存规则
    // 使用正式缓存核对命中、世界替换和可原位修改的冻结配置。
    private static JObject ServiceGenerationProfileCache(JObject args)
    {
        var planet = new PlanetData { Name = "cache:world", GenerationPresetId = "surface.default",
            ChunkSize = new Vector2Int(16, 16) };
        planet.GenerationNumericOverrides["test.number"] = 1d;
        planet.GenerationTextOverrides["test.text"] = "one";
        planet.Ecology.DataVersion = EcologyWorldSaveData.CurrentDataVersion;
        planet.Ecology.ProfileId = "surface.default";
        planet.Ecology.Rules.Add(new EcologyRuleSaveData { RuleId = "test.rule", ItemId = "test.item" });
        planet.Ecology.Rules[0].ProvidedTags.Add("first");
        planet.Ecology.Generation.NumericParameters.Add(new WorldGenerationNumericParameterSaveData
            { Id = "test.frozen", Value = 1d });
        planet.Ecology.Generation.TextParameters.Add(new WorldGenerationTextParameterSaveData
            { Id = "test.frozenText", Value = "first" });
        planet.Ecology.Generation.CaveResourceRules.Add(new CaveResourceRuleSaveData
            { RuleId = "ore", ItemId = "stone", VeinScale = 1d });
        var profile = new ChunkGenerationProfileSnapshot("cache:profile", 1, 16, 16);
        var tiles = new ChunkGenerationProfileSnapshot("cache:tiles", 1, 16, 16);
        var save = new object(); var catalog = new object(); var resources = new object(); var natural = new object();
        var definition = new DimensionDefinition();
        var cache = new WorldGenerationProfileCache();
        WorldGenerationProfileCache.Inputs Inputs(PlanetData world = null, int seed = 1, int revision = 1,
            bool authority = true, object saveOwner = null) => new(world ?? planet, saveOwner ?? save, catalog,
                resources, revision, natural, definition, seed, authority);
        var original = Inputs();
        cache.Store(original, profile, tiles);
        bool hit = cache.TryGet(original, out var reused, out var reusedTiles) &&
            ReferenceEquals(profile, reused) && ReferenceEquals(tiles, reusedTiles);

        planet.GenerationNumericOverrides["test.number"] = 2d;
        bool numberChanged = !cache.TryGet(original, out _, out _);
        planet.GenerationNumericOverrides["test.number"] = 1d;
        planet.GenerationTextOverrides["test.text"] = "two";
        bool textChanged = !cache.TryGet(original, out _, out _);
        planet.GenerationTextOverrides["test.text"] = "one";
        planet.GenerationPresetId = "cave.default";
        bool presetChanged = !cache.TryGet(original, out _, out _);
        planet.GenerationPresetId = "surface.default";
        planet.ChunkSize = new Vector2Int(32, 16);
        bool sizeChanged = !cache.TryGet(original, out _, out _);
        planet.ChunkSize = new Vector2Int(16, 16);
        planet.SpatialDistanceScale = 2f;
        bool scaleChanged = !cache.TryGet(original, out _, out _);
        planet.SpatialDistanceScale = 1f;
        planet.Ecology.Rules[0].ProvidedTags[0] = "other";
        bool ruleChanged = !cache.TryGet(original, out _, out _);
        planet.Ecology.Rules[0].ProvidedTags[0] = "first";
        planet.Ecology.Rules[0].SpawnChance = .5d;
        bool ruleScalarChanged = !cache.TryGet(original, out _, out _);
        planet.Ecology.Rules[0].SpawnChance = 0d;
        planet.Ecology.Generation.NumericParameters[0].Value = 2d;
        bool frozenNumberChanged = !cache.TryGet(original, out _, out _);
        planet.Ecology.Generation.NumericParameters[0].Value = 1d;
        planet.Ecology.Generation.TextParameters[0].Value = "second";
        bool frozenTextChanged = !cache.TryGet(original, out _, out _);
        planet.Ecology.Generation.TextParameters[0].Value = "first";
        planet.Ecology.Generation.CaveResourceRules[0].VeinScale = 2d;
        bool mineralChanged = !cache.TryGet(original, out _, out _);
        planet.Ecology.Generation.CaveResourceRules[0].VeinScale = 1d;
        definition.PortalTargetDimensionId = "cave";
        bool definitionChanged = !cache.TryGet(original, out _, out _);
        definition.PortalTargetDimensionId = null;
        bool worldChanged = !cache.TryGet(Inputs(new PlanetData { Name = planet.Name }), out _, out _);
        bool seedChanged = !cache.TryGet(Inputs(seed: 2), out _, out _);
        bool resourceChanged = !cache.TryGet(Inputs(revision: 2), out _, out _);
        bool saveChanged = !cache.TryGet(Inputs(saveOwner: new object()), out _, out _);
        bool authorityChanged = !cache.TryGet(Inputs(authority: false), out _, out _);

        bool stable = cache.TryGet(original, out _, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++) stable &= cache.TryGet(original, out _, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        bool hadSubscribers = WorldGenerationRuntimeHooks.HasProfileSubscribers;
        Func<ChunkGenerationProfileSnapshot, ChunkGenerationProfileSnapshot> hook = value => value;
        bool hookDetected;
        try
        {
            WorldGenerationRuntimeHooks.BeforeWorldModelGeneration += hook;
            hookDetected = WorldGenerationRuntimeHooks.HasProfileSubscribers;
        }
        finally { WorldGenerationRuntimeHooks.BeforeWorldModelGeneration -= hook; }
        return new JObject
        {
            ["hit"] = hit, ["stable"] = stable, ["allocatedBytes"] = allocated,
            ["numberChanged"] = numberChanged, ["textChanged"] = textChanged,
            ["presetChanged"] = presetChanged, ["sizeChanged"] = sizeChanged, ["scaleChanged"] = scaleChanged,
            ["ruleChanged"] = ruleChanged, ["ruleScalarChanged"] = ruleScalarChanged,
            ["frozenNumberChanged"] = frozenNumberChanged, ["frozenTextChanged"] = frozenTextChanged,
            ["mineralChanged"] = mineralChanged, ["definitionChanged"] = definitionChanged,
            ["worldChanged"] = worldChanged, ["seedChanged"] = seedChanged,
            ["resourceChanged"] = resourceChanged, ["saveChanged"] = saveChanged,
            ["authorityChanged"] = authorityChanged, ["hookDetected"] = hookDetected,
            ["hookRestored"] = WorldGenerationRuntimeHooks.HasProfileSubscribers == hadSubscribers
        };
    }
    #endregion
}
