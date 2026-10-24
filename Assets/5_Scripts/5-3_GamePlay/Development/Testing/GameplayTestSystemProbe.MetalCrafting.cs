using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 针与手动砧规则采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterMetalCraftingRules()
    {
        Register("crafting", "metal_tools_catalog", ProbeMetalToolsCatalog);
        Register("crafting", "needle_sewing", ProbeNeedleSewing);
        Register("crafting", "manual_station_wear", ProbeManualStationWear);
    }

    private static Dictionary<string, ItemDefinitionDto> MetalCraftingDefinitions()
    {
        var definitions = new Dictionary<string, ItemDefinitionDto>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
            if (!definition.Abstract) definitions.Add(definition.Id, definition);
        return definitions;
    }

    private static List<RuntimeRecipe> MetalCraftingRecipes(string package, Dictionary<string, ItemDefinitionDto> definitions)
        => RecipeRuntimeFactory.BuildCatalog(RecipeRuntimeFactory.Deserialize(StreamingAssetsTextLoader.ReadAllText(
            StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, "GameConfig/Recipes/crafting/" + package + ".json"))),
            definitions.ContainsKey, out _);

    private static JObject ProbeMetalToolsCatalog(JObject args)
    {
        var definitions = MetalCraftingDefinitions();
        var items = new JArray();
        foreach (string id in new[] { "Needle_Bone", "Needle_Copper", "Needle_Iron", "CopperAnvil", "CopperAnvil_Summoner", "IronAnvil", "IronAnvil_Summoner",
                     "CopperHammer", "IronHammer", "SteelHammer" })
            items.Add(JObject.FromObject(definitions[id]));
        var forging = new JArray();
        foreach (RuntimeRecipe recipe in MetalCraftingRecipes("forging", definitions))
            forging.Add(new JObject { ["id"] = recipe.Id, ["station"] = recipe.RequiredStation,
                ["seconds"] = recipe.ProcessingSeconds, ["input"] = recipe.inputs.RowItems_List[0].ItemName,
                ["inputAmount"] = recipe.inputs.RowItems_List[0].amount, ["output"] = recipe.outputs.results[0].ItemName,
                ["outputAmount"] = recipe.outputs.results[0].amount });
        var salvage = new JArray();
        foreach (RuntimeRecipe recipe in MetalCraftingRecipes("mechanical", definitions))
        {
            string output = recipe.outputs.results[0].ItemName;
            if (output != "CopperAnvil_Summoner" && output != "IronAnvil_Summoner") continue;
            salvage.Add(new JObject { ["output"] = output,
                ["materials"] = JObject.FromObject(BuildingMaterialSalvage.GetProportionalMaterialAmounts(recipe, .5f)) });
        }
        return new JObject { ["items"] = items, ["forging"] = forging, ["salvage"] = salvage };
    }

    private static JObject ProbeNeedleSewing(JObject args)
    {
        var definitions = MetalCraftingDefinitions();
        string id = Text(args, "needle", "Needle_Copper");
        RuntimeRecipe recipe = MetalCraftingRecipes("sewing", definitions).Find(value => value.Id == "core:leather_gloves");
        Data_GeneralItem needle = EntityCraftingDefinitionData(definitions[id], (float)Number(args, "count", 1));
        needle.Guid = 9471;
        needle.Durability = (float)Number(args, "durability", 1);
        var input = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        {
            new(0) { itemData = needle },
            new(1) { itemData = EntityCraftingDefinitionData(definitions["Thread"], 1) },
            new(2) { itemData = EntityCraftingDefinitionData(definitions["Leather"], 2) }, new(3)
        }, "needle-rule-input") };
        var output = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = Flag(args, "full", false) ? EntityProbeData("blocker", 1) : null } }, "needle-rule-output") };
        JArray beforeInput = EntityCraftingInventorySnapshot(input.Data), beforeOutput = EntityCraftingInventorySnapshot(output.Data);
        bool matched = CraftingRecipeMatcher.TryMatchRecipe(input, recipe,
            new CraftingCapabilities { StationId = "handcraft", InputSlotLimit = 4 }, out CraftingRecipeMatch match);
        bool success = false;
        if (matched && CraftingTransaction.TryCreate(input, output, match,
                new[] { EntityCraftingDefinitionData(definitions["Traveler_Gloves"], 1) }, false, out CraftingTransaction transaction, out _)
            && transaction.Commit(out _))
        { RecipeActionRunner.Execute(recipe, input); transaction.Complete(); success = true; }
        ItemData remaining = input.Data.GetItemSlot(0).itemData;
        return new JObject { ["matched"] = matched, ["success"] = success,
            ["count"] = remaining?.Stack.Amount ?? 0, ["durability"] = remaining?.Durability ?? 0,
            ["maximum"] = needle.MaxDurability, ["guid"] = remaining?.Guid ?? 0,
            ["unchanged"] = JToken.DeepEquals(beforeInput, EntityCraftingInventorySnapshot(input.Data)) &&
                JToken.DeepEquals(beforeOutput, EntityCraftingInventorySnapshot(output.Data)),
            ["output"] = output.Data.GetItemSlot(0).itemData?.IDName,
            ["thread"] = input.Data.GetItemSlot(1).itemData?.Stack.Amount ?? 0,
            ["leather"] = input.Data.GetItemSlot(2).itemData?.Stack.Amount ?? 0 };
    }

    private static JObject ProbeManualStationWear(JObject args)
    {
        var station = EntityProbeData("rule:station", 1);
        station.MaxDurability = (float)Number(args, "maximum", 500);
        station.Durability = (float)Number(args, "durability", 10);
        var products = new[] { EntityProbeData("rule:product", (float)Number(args, "products", 10)) };
        float before = station.Durability;
        float rate = (float)Number(args, "rate", 1);
        bool accepted = ManualProcessingRuntime.CanComplete(station, products, rate, out float cost);
        bool previewUnchanged = station.Durability == before;
        bool consumed = Flag(args, "success", true) && ManualProcessingRuntime.TryConsumeCompletedProducts(station, products, rate);
        var carrier = EntityProbeData("rule:carrier", 1);
        carrier.MaxDurability = station.MaxDurability;
        carrier.Durability = carrier.MaxDurability;
        BuildingModuleStateTransfer.CopyDurabilityRatio(station, carrier);
        var replaced = EntityProbeData("rule:replaced", 1);
        replaced.MaxDurability = station.MaxDurability;
        BuildingModuleStateTransfer.CopyDurabilityRatio(carrier, replaced);
        var capabilities = new CraftingCapabilities { StationId = "hand_forge", RequireExplicitStation = true };
        return new JObject { ["accepted"] = accepted, ["consumed"] = consumed, ["cost"] = cost,
            ["previewUnchanged"] = previewUnchanged, ["durability"] = station.Durability,
            ["stack"] = station.Stack.Amount, ["maximum"] = station.MaxDurability,
            ["carrierDurability"] = carrier.Durability, ["replacedDurability"] = replaced.Durability,
            ["genericRejected"] = !capabilities.SupportsStation(""),
            ["workbenchRejected"] = !capabilities.SupportsStation("workbench"),
            ["forgeAccepted"] = capabilities.SupportsStation("hand_forge") };
    }
    #endregion
}
