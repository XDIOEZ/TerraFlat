using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 动物生态正式目录与栖息地采样
    // 只返回正式解析结果与生产规则输出，动物清单和期望值由 Lua 独立声明。
    private static JObject EntityAnimalEcologyCatalog(JObject args)
    {
        List<ItemDefinitionDto> definitions = null;
        var candidate = new ResourceReloadContext();
        ActorDefinitionCatalogLoader.ConfigureResourceReload(candidate);
        candidate.InCandidate(() => definitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions());
        var actors = new JArray();
        foreach (ItemDefinitionDto actor in definitions)
        {
            if (actor.Abstract) continue;
            actors.Add(new JObject
            {
                ["id"] = actor.Id, ["sprite"] = actor.Visual?.SpriteAddress,
                ["loot"] = actor.LootTableId, ["modules"] = JObject.FromObject(actor.Modules),
                ["tags"] = new JArray(actor.Tags), ["faction"] = actor.ItemData?["factionId"]
            });
        }
        var rules = new JArray();
        foreach (SpawnerConfigDefinition definition in SpawnerConfigCatalogLoader.LoadBuiltIn().Configs)
        {
            SpawnerConfig runtime = definition.CreateRuntimeConfig();
            try
            {
                var species = new JArray();
                foreach (SpawnerConfig.SpawnEntry entry in runtime.SpawnEntries) species.Add(entry.PrefabName);
                var depths = new JArray();
                foreach (float depth in new[] { 0f, .04f, .05f, .35f, .5f, .59f, .6f, 1f })
                    depths.Add(runtime.AllowsWaterDepth(depth));
                rules.Add(new JObject
                {
                    ["id"] = definition.Id, ["species"] = species,
                    ["surface"] = runtime.AllowsDimension(WorldAddress.SurfaceDimensionId),
                    ["cave"] = runtime.AllowsDimension(WorldAddress.CaveDimensionId),
                    ["waterOnly"] = runtime.WaterOnly, ["depths"] = depths,
                    ["shoreRadius"] = runtime.ShoreRadius, ["count"] = runtime.SpawnCount,
                    ["budget"] = runtime.MaxEcologyBudget, ["limit"] = runtime.GroupAliveLimit
                });
            }
            finally { Object.DestroyImmediate(runtime); }
        }
        var loot = new JObject();
        foreach (RuntimeLootTable table in LootTableCatalogLoader.LoadBuiltIn())
        {
            var entries = new JArray();
            foreach (RuntimeLootTableEntry entry in table.Entries)
                entries.Add(new JObject { ["item"] = entry.ItemId, ["min"] = entry.MinAmount, ["max"] = entry.MaxAmount });
            loot[table.Id] = entries;
        }
        return new JObject { ["actors"] = actors, ["rules"] = rules, ["loot"] = loot };
    }
    #endregion
}
