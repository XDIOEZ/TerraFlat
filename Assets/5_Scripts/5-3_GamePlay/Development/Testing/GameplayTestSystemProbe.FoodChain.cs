using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 食物产业链正式目录采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterFoodChainCatalogRules()
        => Register("agriculture", "food_chain_catalog", CaptureFoodChainCatalog);

    // 返回正式继承定义和液体规则，业务期望由 Lua 独立断言。
    private static JObject CaptureFoodChainCatalog(JObject args)
    {
        var requested = new HashSet<string>((args["ids"] as JArray ?? new JArray())
            .Values<string>(), StringComparer.Ordinal);
        var items = new JObject();
        List<ItemDefinitionDto> definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
        var itemIds = new HashSet<string>(definitions.Select(value => value.Id), StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in definitions)
        {
            if (!requested.Contains(definition.Id)) continue;
            var modules = new JObject();
            foreach (var pair in definition.Modules ?? new Dictionary<string, ItemModuleDefinitionDto>())
                modules[pair.Key] = new JObject
                {
                    ["enabled"] = pair.Value.Enabled != false,
                    ["data"] = pair.Value.Data?.DeepClone(),
                    ["parameters"] = pair.Value.Parameters?.DeepClone()
                };
            items[definition.Id] = new JObject
            {
                ["sprite"] = definition.Visual?.SpriteAddress,
                ["modules"] = modules,
                ["processing"] = JToken.FromObject(definition.Processing ?? new()),
                ["reactions"] = JToken.FromObject(definition.Reactions ?? new())
            };
        }

        string recipePath = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath,
            "GameConfig/Recipes/cooking/advanced_food.json");
        List<RuntimeRecipe> cookingRecipes = RecipeRuntimeFactory.BuildCatalog(
            RecipeRuntimeFactory.Deserialize(StreamingAssetsTextLoader.ReadAllText(recipePath)),
            itemIds.Contains, out List<string> recipeWarnings);
        var recipes = new JObject();
        foreach (RuntimeRecipe recipe in cookingRecipes)
        {
            var inputs = new JObject();
            var outputs = new JObject();
            foreach (RuntimeRecipeIngredient input in recipe.inputs.RowItems_List)
                inputs[input.ItemName] = input.amount;
            foreach (RuntimeRecipeResult output in recipe.outputs.results)
                outputs[output.ItemName] = output.amount;
            recipes[recipe.Id] = new JObject { ["inputs"] = inputs, ["outputs"] = outputs };
        }

        List<LiquidDefinition> liquids = LiquidDefinitionFactory.BuildCatalog(
            LiquidDefinitionFactory.DeserializeCatalog(File.ReadAllText(LiquidCatalogLoader.BuiltInPath)));
        LiquidDefinition tea = liquids.Single(value => value.Id == "core:tea_infusion");
        var sources = new JObject();
        foreach (string id in new[] { "core:pure_water", "core:drinkable_water" })
        {
            LiquidIngredientReaction reaction = liquids.Single(value => value.Id == id).IngredientReactions
                .Single(value => value.ItemId == "TeaLeaf");
            sources[id] = new JObject { ["amount"] = reaction.Amount, ["result"] = reaction.ResultLiquidId,
                ["full"] = reaction.RequireFullContainer };
        }
        return new JObject
        {
            ["items"] = items,
            ["recipes"] = recipes,
            ["recipeWarnings"] = JArray.FromObject(recipeWarnings),
            ["tea"] = new JObject { ["sources"] = sources, ["litersPerServing"] = tea.LitersPerServing,
                ["hydration"] = tea.HydrationPerServing, ["temperature"] = tea.HeatProcess.MinimumTemperature,
                ["consume"] = tea.HeatProcess.ConsumeAmount, ["output"] = tea.HeatProcess.OutputItemId,
                ["outputAmount"] = tea.HeatProcess.OutputAmount }
        };
    }
    #endregion
}
