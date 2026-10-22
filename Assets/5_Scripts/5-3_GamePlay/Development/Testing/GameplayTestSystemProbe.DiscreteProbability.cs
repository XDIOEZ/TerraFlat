using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 离散概率原始采样
    private static JObject EntityDiscreteProbability(JObject args)
    {
        DiscreteProbabilityConfiguration.Reload();
        DiscreteProbabilityTableDefinition Definition() => new()
        {
            Id = "generation.probe",
            Entries = new[]
            {
                new DiscreteProbabilityEntryDefinition { Value = 1d, Percent = 10d },
                new DiscreteProbabilityEntryDefinition { Value = 2d, Percent = 90d }
            }
        };
        var definition = Definition();
        double copiedWeight = -1d;
        void Adjust(string id, ref DiscreteProbabilityState state)
        {
            DiscreteProbabilityState copy = state;
            state.SetWeight(0, 30d);
            copiedWeight = copy.GetWeight(0);
        }
        var original = DiscreteProbability.Build(definition);
        var changed = DiscreteProbability.Build(definition, Adjust);
        definition.Entries[0].Percent = 80d;
        var frozen = new DiscreteProbabilityCatalog(new[] { Definition() }, Adjust);
        var parameters = new Dictionary<string, double>(StringComparer.Ordinal);
        frozen.WriteGenerationParameters(parameters);
        var restored = DiscreteProbabilityCatalog.FromGenerationParameters(parameters).Require("generation.probe");
        var profile = new ChunkGenerationProfileSnapshot("probe", 1, 16, 16, parameters);
        var otherParameters = new Dictionary<string, double>(parameters);
        otherParameters["probability.generation.probe.0.percent"] = 50d;
        otherParameters["probability.generation.probe.1.percent"] = 50d;
        var otherProfile = new ChunkGenerationProfileSnapshot("probe", 1, 16, 16, otherParameters);
        var zeroDefinition = Definition();
        zeroDefinition.Entries[0].Percent = 100d;
        zeroDefinition.Entries[1].Percent = 0d;
        var oneHot = DiscreteProbability.Build(zeroDefinition);
        var production = DiscreteProbabilityConfiguration.Capture();
        var tableIds = new[] { "loot.tree.leaf.amount", "generation.count.surface.forest.leaf",
            "generation.lava.radius", "generation.river.deposit.activation", "generation.river.deposit.length" };
        var productionTables = new JArray();
        foreach (string id in tableIds)
        {
            var table = production.Require(id);
            var entries = new JArray();
            for (int i = 0; i < table.Count; i++)
                entries.Add(new JObject { ["value"] = table.GetValue(i), ["percent"] = table.GetPercent(i) });
            productionTables.Add(new JObject { ["id"] = id, ["entries"] = entries });
        }
        var patches = new JArray();
        for (int i = 0; i <= 10; i++) patches.Add(production.Require("generation.patch.distance." + i).ProbabilityOf(1d));
        string Invalid(Action<DiscreteProbabilityTableDefinition> change)
        {
            var input = Definition();
            change(input);
            return WorldException(() => DiscreteProbability.Build(input));
        }
        void Clear(string id, ref DiscreteProbabilityState state)
        { for (int i = 0; i < state.Count; i++) state.SetWeight(i, 0d); }
        return new JObject
        {
            ["originalPercent"] = original.GetPercent(0), ["changedPercent"] = changed.GetPercent(0),
            ["changedOtherPercent"] = changed.GetPercent(1), ["copiedWeight"] = copiedWeight,
            ["belowBoundary"] = DiscreteProbability.Sample(changed, .249999d),
            ["atBoundary"] = DiscreteProbability.Sample(changed, .25d),
            ["atOne"] = DiscreteProbability.Sample(oneHot, 1d), ["atZero"] = DiscreteProbability.Sample(oneHot, 0d),
            ["rangeSample"] = DiscreteProbability.SampleWithin(changed, .9d, 1d, 1d),
            ["restoredPercent"] = restored.GetPercent(0),
            ["generationFingerprintChanged"] = profile.GenerationFingerprint != otherProfile.GenerationFingerprint,
            ["ecologyFingerprintChanged"] = profile.EcologyFingerprint != otherProfile.EcologyFingerprint,
            ["productionTables"] = productionTables, ["patchProbabilities"] = patches,
            ["negative"] = Invalid(value => value.Entries[0].Percent = -1d),
            ["nan"] = Invalid(value => value.Entries[0].Percent = double.NaN),
            ["wrongTotal"] = Invalid(value => value.Entries[0].Percent = 11d),
            ["duplicate"] = Invalid(value => value.Entries[1].Value = 1d),
            ["zeroAfterAdjustment"] = WorldException(() => DiscreteProbability.Build(Definition(), Clear)),
            ["invalidRandom"] = WorldException(() => DiscreteProbability.Sample(changed, double.NaN)),
            ["invalidRange"] = WorldException(() => DiscreteProbability.SampleWithin(changed, .5d, 3d, 4d))
        };
    }
    #endregion
}
