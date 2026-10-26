using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 石油化工正式目录与事务采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterPetrochemicalProcessingRules()
    {
        Register("processing", "petrochemical_content", ProbePetrochemicalContent);
        Register("processing", "petrochemical_reaction", ProbePetrochemicalReaction);
    }

    private static JObject ProbePetrochemicalContent(JObject args)
    {
        Dictionary<string, ItemDefinitionDto> definitions = MineralContentDefinitions();
        var items = new JObject();
        foreach (string id in new[] { "CrudeOil", "Naphtha", "Ethylene", "Propylene", "Bitumen",
                     "FuelOil", "Plastic", "PolymerizationCatalyst" })
        {
            ItemDefinitionDto item = definitions[id];
            items[id] = new JObject { ["id"] = item.Id, ["gameName"] = item.GameName,
                ["stackable"] = item.Stackable, ["weight"] = item.Weight, ["volume"] = item.Volume,
                ["visual"] = new JObject { ["spriteAddress"] = item.Visual?.SpriteAddress },
                ["reactions"] = JArray.FromObject(item.Reactions) };
        }
        List<LiquidDefinition> liquids = LiquidDefinitionFactory.BuildCatalog(
            LiquidDefinitionFactory.DeserializeCatalog(File.ReadAllText(LiquidCatalogLoader.BuiltInPath)));
        LiquidDefinition oil = liquids.Single(value => value.Id == "core:crude_oil");
        return new JObject { ["items"] = items,
            ["oil"] = new JObject { ["litersPerServing"] = oil.LitersPerServing,
                ["drinkable"] = oil.Drinkable, ["waterContact"] = oil.WorldWater.WaterContact,
                ["temperature"] = oil.HeatProcess.MinimumTemperature,
                ["consumeAmount"] = oil.HeatProcess.ConsumeAmount,
                ["output"] = oil.HeatProcess.OutputItemId, ["amount"] = oil.HeatProcess.OutputAmount } };
    }

    // 只提供正式材料与库存，匹配、全产物容量和提交使用生产接口。
    private static JObject ProbePetrochemicalReaction(JObject args)
    {
        Dictionary<string, ItemDefinitionDto> definitions = MineralContentDefinitions();
        string sourceId = Text(args, "source", "CrudeOil");
        RuntimeItemReactionDefinition reaction = ItemMatterReactionCompiler.CompileReactions(
            definitions[sourceId].Reactions, sourceId).First(value => value.LiquidOutput == null);
        var slots = new List<ItemSlot>();
        foreach (RuntimeRecipeIngredient ingredient in reaction.Recipe.inputs.RowItems_List)
        {
            float amount = (float)Number(args["amounts"] as JObject, ingredient.ItemName, Math.Max(1, ingredient.amount));
            ItemData material = amount > 0 ? EntityCraftingDefinitionData(definitions[ingredient.ItemName], amount) : null;
            if (material != null)
                material.MatterState = new ItemMatterState { Initialized = true,
                    TemperatureCelsius = (float)Number(args["temperatures"] as JObject, ingredient.ItemName,
                        Number(args, "temperature", reaction.MinTemperature ?? 20f)) };
            slots.Add(new ItemSlot(slots.Count) { itemData = material });
        }
        var input = new Inventory { Data = new Inventory_Data(slots, "petrochemical-input") };
        var output = new Inventory { Data = new Inventory_Data(Enumerable.Range(0,
            (int)Number(args, "outputSlots", 3)).Select(index => new ItemSlot(index)).ToList(), "petrochemical-output") };
        JArray before = EntityCraftingInventorySnapshot(input.Data), beforeOutput = EntityCraftingInventorySnapshot(output.Data);
        bool matched = ItemReactionResolver.TryMatchBatch(input, reaction, out CraftingRecipeMatch match, out int batches);
        bool committed = false;
        if (matched)
        {
            List<ItemData> products = reaction.Recipe.outputs.results.Select(product =>
                (ItemData)EntityCraftingDefinitionData(definitions[product.ItemName], product.amount * batches)).ToList();
            if (CraftingTransaction.TryCreate(input, output, match, products, false, out var transaction, out _) &&
                transaction.Commit(out _))
            { transaction.Complete(); committed = true; }
        }
        return new JObject { ["matched"] = matched, ["committed"] = committed, ["batches"] = batches,
            ["unchanged"] = JToken.DeepEquals(before, EntityCraftingInventorySnapshot(input.Data)) &&
                JToken.DeepEquals(beforeOutput, EntityCraftingInventorySnapshot(output.Data)),
            ["input"] = EntityCraftingInventorySnapshot(input.Data), ["output"] = EntityCraftingInventorySnapshot(output.Data) };
    }
    #endregion
}
