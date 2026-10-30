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
        Register("crafting", "needle_work_clicks", ProbeNeedleWorkClicks);
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
        { transaction.Complete(); success = match.TryConsumeWorkingToolDurability(input); }
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

    private static JObject ProbeNeedleWorkClicks(JObject args)
    {
        var definitions = MetalCraftingDefinitions();
        RuntimeRecipe recipe = MetalCraftingRecipes("sewing", definitions).Find(value => value.Id == "core:leather_gloves");
        ItemData needle = EntityCraftingDefinitionData(definitions["Needle_Bone"], 1);
        needle.Durability = (float)Number(args, "durability", 2);
        var input = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        {
            new(0) { itemData = needle }, new(1) { itemData = EntityCraftingDefinitionData(definitions["Thread"], 1) },
            new(2) { itemData = EntityCraftingDefinitionData(definitions["Leather"], 2) }
        }, "needle-click-input") };
        var output = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = Flag(args, "full", false) ? EntityProbeData("blocker", 1) : null } }, "needle-click-output") };
        var capabilities = new CraftingCapabilities { StationId = "handcraft", InputSlotLimit = 3 };
        var state = new RecipeProcessingState { Input = input.Data, Output = output.Data };
        using var processor = new RecipeProcessor(input, output, capabilities, state);
        processor.SelectRecipe(recipe, 3f);
        ItemData product = EntityCraftingDefinitionData(definitions["Traveler_Gloves"], 1);
        processor.PreviewOverride = () => CraftingRecipeMatcher.TryMatchRecipe(input, recipe, capabilities, out var match) &&
            CraftingTransaction.CanCreate(input, output, match, new[] { product }, false, out _)
                ? CraftingResult.Succeeded(recipe, new[] { product })
                : CraftingResult.Failed(CraftingFailureReason.ConditionsNotMet, "制作条件不足");
        processor.CommitOverride = actor =>
        {
            if (Flag(args, "failCommit", false)) return CraftingResult.Failed(CraftingFailureReason.CommitFailed, "提交失败");
            if (!CraftingRecipeMatcher.TryMatchRecipe(input, recipe, capabilities, out var match) ||
                !CraftingTransaction.TryCreate(input, output, match, new[] { product }, false, out var transaction, out _, true))
                return CraftingResult.Failed(CraftingFailureReason.CommitFailed, "制作失败");
            if (Flag(args, "changeToolBeforeWear", false)) input.Data.NotifySlotsChanged();
            if (!transaction.Commit(out _)) return CraftingResult.Failed(CraftingFailureReason.InventoryChanged, "工具槽已变化");
            transaction.Complete();
            return CraftingResult.Succeeded(recipe, new[] { product });
        };
        var samples = new JArray();
        for (int click = 0; click < (int)Number(args, "clicks", 3); click++)
        {
            if (click > 0 && Flag(args, "replace", false) && input.Data.itemSlots[0].itemData == null)
                input.Data.SetOne_ItemData(0, EntityCraftingDefinitionData(definitions["Needle_Bone"], 1));
            bool completed = processor.AdvanceManually(1f, null, out bool worked);
            var snapshot = new Ex_ModData_MemoryPackable();
            snapshot.WriteData(state);
            RecipeProcessingState restored = snapshot.GetData<RecipeProcessingState>();
            samples.Add(new JObject { ["worked"] = worked, ["completed"] = completed,
                ["progress"] = state.Progress, ["durability"] = input.Data.itemSlots[0].itemData?.Durability ?? 0f,
                ["paused"] = processor.PausedForWorkingTool, ["output"] = output.Data.itemSlots[0].itemData?.IDName ?? "",
                ["restoredPaused"] = restored.PausedForWorkingTool, ["restoredProgress"] = restored.Progress,
                ["restoredRecipeId"] = restored.RecipeId });
        }
        return new JObject { ["samples"] = samples };
    }

    private static JObject ProbeManualStationWear(JObject args)
    {
        var station = EntityProbeData("rule:station", 1);
        station.MaxDurability = (float)Number(args, "maximum", 500);
        station.Durability = (float)Number(args, "durability", 10);
        float before = station.Durability;
        float rate = (float)Number(args, "rate", 1);
        bool accepted = ManualProcessingRuntime.CanPerformStationWork(station, rate);
        bool previewUnchanged = station.Durability == before;
        bool consumed = Flag(args, "success", true) && ManualProcessingRuntime.TryConsumeStationWork(station, rate);
        var carrier = EntityProbeData("rule:carrier", 1);
        carrier.MaxDurability = station.MaxDurability;
        carrier.Durability = carrier.MaxDurability;
        BuildingModuleStateTransfer.CopyDurabilityRatio(station, carrier);
        var replaced = EntityProbeData("rule:replaced", 1);
        replaced.MaxDurability = station.MaxDurability;
        BuildingModuleStateTransfer.CopyDurabilityRatio(carrier, replaced);
        var capabilities = new CraftingCapabilities { StationId = "hand_forge", RequireExplicitStation = true };
        return new JObject { ["accepted"] = accepted, ["consumed"] = consumed, ["cost"] = accepted ? rate : 0f,
            ["previewUnchanged"] = previewUnchanged, ["durability"] = station.Durability,
            ["stack"] = station.Stack.Amount, ["maximum"] = station.MaxDurability,
            ["carrierDurability"] = carrier.Durability, ["replacedDurability"] = replaced.Durability,
            ["genericRejected"] = !capabilities.SupportsStation(""),
            ["workbenchRejected"] = !capabilities.SupportsStation("workbench"),
            ["forgeAccepted"] = capabilities.SupportsStation("hand_forge") };
    }
    #endregion
}
