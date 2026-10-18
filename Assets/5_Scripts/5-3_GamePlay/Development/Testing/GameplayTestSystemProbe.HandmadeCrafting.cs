using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 正式手工配方数据回归

    // 正式手工配方在独立工作台容量内执行，Lua 核对产物、扣料及拒绝时的库存快照。
    private static JObject EntityCraftingHandmadeCatalog(JObject args)
    {
        string recipeRoot = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RecipeCatalogLoader.RelativeRecipeRoot);
        RecipeManifestDto manifest = RecipeCatalogLoader.DeserializeManifest(StreamingAssetsTextLoader.ReadAllText(
            StreamingAssetsTextLoader.CombinePath(recipeRoot, RecipeCatalogLoader.ManifestFileName)));
        RecipeCatalogLoader.ValidateManifest(manifest);
        var definitions = new Dictionary<string, ItemDefinitionDto>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
            if (!definition.Abstract) definitions.Add(definition.Id, definition);
        var packages = new JArray();
        foreach (RecipePackageDto package in manifest.Packages)
            if (package.Enabled) packages.Add(package.Path);
        var samples = new JArray();
        foreach (string packagePath in new[] { "crafting/handmade_equipment.json", "crafting/handmade_utilities.json" })
        {
            RecipeCatalogDto catalog = RecipeRuntimeFactory.Deserialize(StreamingAssetsTextLoader.ReadAllText(
                RecipeCatalogLoader.ResolvePackagePath(recipeRoot, packagePath)));
            List<RuntimeRecipe> recipes = RecipeRuntimeFactory.BuildCatalog(catalog, definitions.ContainsKey, out List<string> warnings);
            foreach (RuntimeRecipe recipe in recipes)
            {
                var expectedOutputs = new JArray();
                foreach (RuntimeRecipeResult result in recipe.outputs.results)
                    expectedOutputs.Add(new JObject { ["id"] = result.ItemName, ["amount"] = result.amount });
                var missingTools = new JArray();
                for (int index = 0; index < recipe.inputs.RowItems_List.Count; index++)
                    if (recipe.inputs.RowItems_List[index].amount == 0)
                        missingTools.Add(EntityCraftingHandmadeAttempt(recipe, definitions, false, false, index));
                samples.Add(new JObject
                {
                    ["id"] = recipe.Id, ["package"] = packagePath, ["warnings"] = new JArray(warnings),
                    ["expectedOutputs"] = expectedOutputs, ["inputKinds"] = recipe.inputs.RowItems_List.Count,
                    ["normal"] = EntityCraftingHandmadeAttempt(recipe, definitions, false, false),
                    ["missing"] = EntityCraftingHandmadeAttempt(recipe, definitions, true, false),
                    ["full"] = EntityCraftingHandmadeAttempt(recipe, definitions, false, true), ["missingTools"] = missingTools
                });
            }
        }
        return new JObject { ["enabledPackages"] = packages, ["recipes"] = samples };
    }

    private static JObject EntityCraftingHandmadeAttempt(RuntimeRecipe recipe,
        Dictionary<string, ItemDefinitionDto> definitions, bool missingMaterial, bool fullOutput, int missingToolIndex = -1)
    {
        var inputSlots = new List<ItemSlot>();
        var expectedInput = new JArray();
        bool removedOne = false;
        foreach (RuntimeRecipeIngredient ingredient in recipe.inputs.RowItems_List)
        {
            string itemId = ingredient.ItemName;
            if (ingredient.matchMode == MatchMode.ByTag)
            {
                itemId = null;
                foreach (ItemDefinitionDto definition in definitions.Values)
                    if (definition.Tags?.Exists(tag => string.Equals(tag, ingredient.Tag, StringComparison.OrdinalIgnoreCase)) == true)
                    { itemId = definition.Id; break; }
                if (itemId == null) throw new InvalidOperationException($"配方 {recipe.Id} 找不到标签材料 {ingredient.Tag}");
            }
            float amount = Mathf.Max(1, ingredient.amount);
            if (missingMaterial && !removedOne && ingredient.amount > 0) { amount -= 1f; removedOne = true; }
            if (inputSlots.Count == missingToolIndex) amount = 0f;
            Data_GeneralItem material = EntityCraftingDefinitionData(definitions[itemId], amount);
            material.Guid = 1800 + inputSlots.Count;
            inputSlots.Add(new ItemSlot(inputSlots.Count) { itemData = amount > 0 ? material : null });
            expectedInput.Add(new JObject { ["id"] = itemId, ["before"] = amount,
                ["after"] = Mathf.Max(0f, amount - ingredient.amount) });
        }
        while (inputSlots.Count < 5) inputSlots.Add(new ItemSlot(inputSlots.Count));
        var input = new Inventory { Data = new Inventory_Data(inputSlots, "independent handmade input") };
        var outputSlots = new List<ItemSlot>();
        for (int index = 0; index < 2; index++)
        {
            Data_GeneralItem blocker = fullOutput ? EntityProbeData("test:handmade_blocker_" + index, index + 2) : null;
            if (blocker != null) blocker.Guid = 1900 + index;
            outputSlots.Add(new ItemSlot(index) { itemData = blocker });
        }
        var output = new Inventory { Data = new Inventory_Data(outputSlots, "independent handmade output") };
        JArray inputBefore = EntityCraftingInventorySnapshot(input.Data), outputBefore = EntityCraftingInventorySnapshot(output.Data);
        var products = new List<ItemData>();
        foreach (RuntimeRecipeResult result in recipe.outputs.results)
            products.Add(EntityCraftingDefinitionData(definitions[result.ItemName], result.amount));
        bool matched = CraftingRecipeMatcher.TryMatchRecipe(input, recipe, new CraftingCapabilities
        { StationId = "workbench", CompatibleStationIds = new[] { "handcraft" }, InputSlotLimit = 5,
            ApplyDifficultyOutputMultiplier = false }, out CraftingRecipeMatch match);
        bool prepared = false, committed = false;
        if (matched)
        {
            prepared = CraftingTransaction.TryCreate(input, output, match, products, false, out CraftingTransaction transaction, out _);
            if (prepared && transaction.Commit(out _))
            { RecipeActionRunner.Execute(recipe, input); transaction.Complete(); committed = true; }
        }
        return new JObject
        {
            ["matched"] = matched, ["prepared"] = prepared, ["committed"] = committed,
            ["removedOne"] = removedOne, ["expectedInput"] = expectedInput,
            ["inputBefore"] = inputBefore, ["outputBefore"] = outputBefore,
            ["inputAfter"] = EntityCraftingInventorySnapshot(input.Data), ["outputAfter"] = EntityCraftingInventorySnapshot(output.Data)
        };
    }

    private static Data_GeneralItem EntityCraftingDefinitionData(ItemDefinitionDto definition, float amount)
    {
        Data_GeneralItem data = EntityProbeData(definition.Id, amount, definition.Weight ?? 0f, definition.Volume ?? 0f);
        data.Stack.Stackable = definition.Stackable ?? true;
        data.Tags = new List<string>(definition.Tags ?? new List<string>());
        data.Durability = definition.Durability ?? 1f;
        data.MaxDurability = definition.MaxDurability ?? 1f;
        return data;
    }

    private static JArray EntityCraftingInventorySnapshot(Inventory_Data inventory)
    {
        var slots = new JArray();
        foreach (ItemSlot slot in inventory.itemSlots)
            slots.Add(new JObject { ["id"] = slot.itemData?.IDName ?? "", ["guid"] = slot.itemData?.Guid ?? 0,
                ["amount"] = slot.itemData?.Stack?.Amount ?? 0f, ["durability"] = slot.itemData?.Durability ?? 0f,
                ["maxDurability"] = slot.itemData?.MaxDurability ?? 0f });
        return slots;
    }

    #endregion
}
