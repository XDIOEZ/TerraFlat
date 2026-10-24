using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 火种材料与自然消失规则
    private static void RegisterTimedDespawnOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("item_runtime/timed_despawn", TimedDespawnProbe);
        operations.Add("item_runtime/tinder_material", TinderMaterialProbe);
    }

    private static JObject TimedDespawnProbe(JObject args)
    {
        float lifetime = (float)Number(args, "lifetime", 1440);
        var state = new Ex_ModData_MemoryPackable { StableName = "lifetime", ModuleId = Mod_TimedDespawn.ModuleId };
        var samples = new JArray();
        foreach (JObject step in args["steps"] as JArray ?? new JArray())
        {
            if (Flag(step, "restore", false))
            {
                ModuleInstanceSnapshot snapshot = ModuleInstanceSnapshot.Capture(state);
                state = new Ex_ModData_MemoryPackable { StableName = "lifetime", ModuleId = Mod_TimedDespawn.ModuleId };
                snapshot.RestoreTo(state);
            }
            state.Enabled = Flag(step, "enabled", true);
            bool expired = TimedDespawnRules.IsExpiredAt(state, lifetime,
                Text(step, "world", "earth"), Number(step, "now", 0));
            TimedDespawnState saved = state.GetData<TimedDespawnState>();
            samples.Add(new JObject { ["expired"] = expired, ["elapsed"] = saved.ElapsedGameSeconds,
                ["initialized"] = saved.Initialized, ["last"] = saved.LastGameSeconds });
        }

        ItemData ember = EntityProbeData("FireSeed", 7);
        ember.Stack.Amount = 5;
        var slot = new ItemSlot(0) { itemData = ember };
        var inventory = new Inventory_Data(new List<ItemSlot> { slot }, "lifetime-probe");
        ItemData stranger = EntityProbeData("probe:replacement", 8);
        bool staleRejected = !TimedDespawnRules.RemoveExpiredSlot(inventory, slot, stranger);
        bool removed = TimedDespawnRules.RemoveExpiredSlot(inventory, slot, ember);
        return new JObject { ["samples"] = samples, ["staleRejected"] = staleRejected,
            ["removed"] = removed, ["empty"] = slot.itemData == null, ["amount"] = ember.Stack.Amount,
            ["noProducts"] = inventory.itemSlots.All(value => value.itemData == null) };
    }

    private static JObject TinderMaterialProbe(JObject args)
    {
        string root = Path.Combine(Application.streamingAssetsPath, "GameConfig/Items/shells");
        JObject cotton = JObject.Parse(File.ReadAllText(Path.Combine(root, "basic_items.json")))["items"]
            .OfType<JObject>().Single(value => (string)value["id"] == "Cotton");
        JObject ember = JObject.Parse(File.ReadAllText(Path.Combine(root, "tools.json")))["items"]
            .OfType<JObject>().Single(value => (string)value["id"] == "FireSeed");
        var matter = ItemMatterReactionCompiler.CompileMatter(cotton["matter"].ToObject<ItemMatterDefinitionDto>(),
            cotton["tags"].ToObject<List<string>>(), "Cotton");
        var material = new ItemMatterState { Initialized = true, TemperatureCelsius = 99f };
        bool coldRejected = ItemMatterRuntime.SelectMatchedTransition(matter, material, false) == null;
        material.TemperatureCelsius = 100f;
        RuntimeItemMatterTransition transition = ItemMatterRuntime.SelectMatchedTransition(matter, material, false);
        return WithEntityProbeItem(item =>
        {
            var flint = item.gameObject.AddComponent<Mod_FlintStrike>();
            ItemData cottonData = EntityProbeData("Cotton", 1);
            cottonData.Tags = cotton["tags"].ToObject<List<string>>();
            ItemData stone = EntityProbeData("Stone", 2);
            stone.Tags = new List<string>();
            JObject lifetime = (JObject)ember["modules"]["lifetime"];
            return new JObject { ["flintAcceptsCotton"] = flint.IsValidTinder(cottonData),
                ["flintRejectsStone"] = !flint.IsValidTinder(stone), ["coldRejected"] = coldRejected,
                ["heatOutput"] = transition?.OutputItemId, ["heatRatio"] = transition?.OutputAmountMultiplier,
                ["weaveOutput"] = (string)cotton["processing"]["weave"]["outputs"][0]["itemId"],
                ["lifetime"] = (float)lifetime["parameters"]["LifetimeGameSeconds"],
                ["moduleId"] = (string)lifetime["id"], ["enabled"] = (bool)lifetime["enabled"],
                ["notFood"] = !((JObject)ember["modules"]).Properties().Any(property =>
                    (string)property.Value["prefab"] == "Module_Food") };
        });
    }
    #endregion
}
