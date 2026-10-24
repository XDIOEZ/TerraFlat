using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 机械材质与真实共享槽采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterMechanicalCraftingRules()
    {
        Register("crafting", "mechanical_materials", ProbeMechanicalMaterials);
        Register("mechanical", "workbench_cabinet", ProbeWorkbenchCabinet);
    }

    private static JObject ProbeMechanicalMaterials(JObject args)
    {
        var definitions = MechanicalCraftingDefinitions();
        var samples = new JArray();
        foreach (RuntimeRecipe recipe in MechanicalCraftingRecipes(definitions))
        {
            string product = recipe.outputs.results[0].ItemName;
            if (product != "MechanicalHammer_Summoner" && product != "Sawmill_Summoner") continue;
            var primary = new Inventory { Data = MachineInventory.NewData("工作台", 5) };
            var cabinet = new Inventory { Data = MachineInventory.NewData("工具柜", 1) };
            for (int i = 0; i < recipe.inputs.RowItems_List.Count; i++)
            {
                RuntimeRecipeIngredient ingredient = recipe.inputs.RowItems_List[i];
                ItemData data = EntityCraftingDefinitionData(definitions[ingredient.ItemName], ingredient.amount);
                data.Guid = 18000 + i;
                if (i < 5) primary.Data.itemSlots[i].itemData = data;
                else cabinet.Data.itemSlots[0].itemData = data;
            }
            using var linked = new LinkedCraftingInput(primary);
            using var other = new LinkedCraftingInput(new Inventory { Data = MachineInventory.NewData("另一工作台", 5) });
            linked.SetShared(cabinet); other.SetShared(cabinet);
            var capabilities = new CraftingCapabilities { StationId = "workbench", InputSlotLimit = 6, ApplyOutputMultiplier = false };
            bool matched = CraftingRecipeMatcher.TryMatchRecipe(linked, recipe, capabilities, out CraftingRecipeMatch match);
            bool withoutCabinet = CraftingRecipeMatcher.TryMatchRecipe(primary, recipe, capabilities, out _);
            ItemData nail = cabinet.Data.itemSlots[0].itemData;
            nail.Stack.Amount = 3;
            bool missing = CraftingRecipeMatcher.TryMatchRecipe(linked, recipe, capabilities, out _);
            nail.Stack.Amount = 4;
            var output = new Inventory { Data = MachineInventory.NewData("产物", 1) };
            ItemData crafted = EntityCraftingDefinitionData(definitions[product], 1);
            crafted.Guid = 18100;
            CraftingOutputRules.ApplyInstanceTag(crafted, recipe.outputs.results[0].AddTag);
            var products = new List<ItemData> { crafted };
            output.Data.itemSlots[0].itemData = EntityProbeData("test:blocked", 1);
            bool full = CraftingTransaction.CanCreate(linked, output, match, products, false, out _);
            float afterFull = cabinet.Data.itemSlots[0].itemData?.Stack.Amount ?? 0f;
            output.Data.itemSlots[0].itemData = null;
            bool prepared = CraftingTransaction.TryCreate(linked, output, match, products, false, out CraftingTransaction transaction, out _);
            bool committed = prepared && transaction.Commit(out _);
            if (committed) transaction.Complete();
            float afterCraft = cabinet.Data.itemSlots[0].itemData?.Stack.Amount ?? 0f;
            bool sameSlot = ReferenceEquals(other.Data.itemSlots[5], cabinet.Data.itemSlots[0]) &&
                ReferenceEquals(linked.Data.itemSlots[5], cabinet.Data.itemSlots[0]);
            transaction?.Rollback();
            float afterRollback = cabinet.Data.itemSlots[0].itemData?.Stack.Amount ?? 0f;
            bool stalePrepared = CraftingTransaction.TryCreate(linked, output, match, products, false, out var stale, out _);
            var replacement = new Inventory { Data = MachineInventory.NewData("替换工具柜", 1) };
            replacement.Data.itemSlots[0].itemData = EntityCraftingDefinitionData(definitions[recipe.inputs.RowItems_List[5].ItemName], 4);
            linked.SetShared(replacement);
            bool staleRejected = stalePrepared && !stale.Commit(out _);
            linked.SetShared(null);
            ItemData body = ItemInstanceDataFactory.Create(EntityProbeData("test:machine_body", 1));
            CraftedDurabilityQuality.CopyInstanceQuality(crafted, body);
            ItemData restored = ItemInstanceSnapshot.Capture(body).CreateColdData();
            ItemData repacked = ItemInstanceDataFactory.Create(EntityProbeData(product, 1));
            CraftedDurabilityQuality.CopyInstanceQuality(restored, repacked);
            var machine = new MachineEntity { Definition = new MachineDefinition { RequiredRpm = 20 }, Snapshot = repacked, Rpm = 20 };
            float efficiency = MachineWorld.GetWorkEfficiency(machine);
            machine.Rpm = -10;
            float halfReverse = MachineWorld.GetWorkEfficiency(machine);
            machine.Rpm = 0;
            samples.Add(new JObject
            {
                ["id"] = recipe.Id, ["output"] = product, ["inputs"] = JArray.FromObject(recipe.inputs.RowItems_List.ConvertAll(value =>
                    new { id = value.ItemName, amount = value.amount })),
                ["matched"] = matched, ["withoutCabinet"] = withoutCabinet, ["missing"] = missing,
                ["fullPrepared"] = full, ["afterFull"] = afterFull, ["prepared"] = prepared, ["committed"] = committed,
                ["afterCraft"] = afterCraft, ["sameSlot"] = sameSlot, ["afterRollback"] = afterRollback,
                ["staleRejected"] = staleRejected, ["replacementAmount"] = replacement.Data.itemSlots[0].itemData.Stack.Amount,
                ["detachedSlots"] = linked.Data.itemSlots.Count, ["efficiency"] = efficiency, ["halfReverse"] = halfReverse,
                ["stopped"] = MachineWorld.GetWorkEfficiency(machine), ["durabilityMultiplier"] = repacked.CraftedDurabilityMultiplier
            });
        }
        return new JObject { ["recipes"] = samples };
    }

    private static Dictionary<string, ItemDefinitionDto> MechanicalCraftingDefinitions()
    {
        var definitions = new Dictionary<string, ItemDefinitionDto>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
            if (!definition.Abstract) definitions.Add(definition.Id, definition);
        return definitions;
    }

    private static List<RuntimeRecipe> MechanicalCraftingRecipes(Dictionary<string, ItemDefinitionDto> definitions)
        => RecipeRuntimeFactory.BuildCatalog(RecipeRuntimeFactory.Deserialize(StreamingAssetsTextLoader.ReadAllText(
            StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, "GameConfig/Recipes/crafting/mechanical.json"))),
            definitions.ContainsKey, out _);

    private static JObject ProbeWorkbenchCabinet(JObject args)
    {
        MachineEntity Cabinet(int id, int x, int y) => new()
        {
            Id = id, Cell = new Vector2Int(x, y),
            Definition = new MachineDefinition { Id = "ToolCabinet", Kind = "storage", Ports = "none", ProvidesWorkbenchInput = true }
        };
        var bench = new MachineEntity { Id = 1, Cell = Vector2Int.zero,
            Definition = new MachineDefinition { Id = "WorkBench", Ports = "none" } };
        var graph = new MechanicalNetworkGraph(new Vector2Int(16, 16), Vector2Int.zero, value => value);
        var positions = new JArray();
        for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++)
            {
                if (x == 0 && y == 0) continue;
                var cabinet = Cabinet(2, x, y);
                graph.Rebuild(new[] { bench, cabinet });
                positions.Add(new JObject { ["x"] = x, ["y"] = y,
                    ["linked"] = MachineWorld.FindAdjacentCraftingInput(graph, bench)?.Id == cabinet.Id });
            }
        var first = Cabinet(2, 1, 0); var second = Cabinet(3, -1, -1);
        graph.Rebuild(new[] { bench, second, first });
        int selected = MachineWorld.FindAdjacentCraftingInput(graph, bench)?.Id ?? 0;
        int preferred = MachineWorld.FindAdjacentCraftingInput(graph, bench, second.Id)?.Id ?? 0;
        graph.Rebuild(new[] { bench, second });
        int fallback = MachineWorld.FindAdjacentCraftingInput(graph, bench, first.Id)?.Id ?? 0;
        graph.Rebuild(new[] { bench });
        return new JObject { ["positions"] = positions, ["selected"] = selected, ["preferred"] = preferred,
            ["fallback"] = fallback, ["removed"] = MachineWorld.FindAdjacentCraftingInput(graph, bench) == null };
    }
    #endregion
}
