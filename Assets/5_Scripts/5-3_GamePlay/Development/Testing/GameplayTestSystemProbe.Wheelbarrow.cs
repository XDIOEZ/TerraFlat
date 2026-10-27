using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 独轮车配置与正式配方采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterWheelbarrowContentRules()
    {
        Register("player", "wheelbarrow_content", CaptureWheelbarrowContent);
        Register("player", "wheelbarrow_push_rules", CaptureWheelbarrowPushRules);
    }

    private static JObject CaptureWheelbarrowPushRules(JObject args)
    {
        static bool Allowed(Vector2 forward, Vector2 toward, Vector2 input)
            => Mod_Wheelbarrow.IsPusherBehindWheelbarrow(forward, toward, input);
        var host = new GameObject("[WheelbarrowRules]");
        host.SetActive(false);
        try
        {
            Mod_Wheelbarrow driver = host.AddComponent<Mod_Wheelbarrow>();
            Vector2 full = driver.ResolveCarrierPush(new Vector2(0f, -10f), false);
            Vector2 partial = driver.ResolveCarrierPush(new Vector2(3f, 4f), false);
            return new JObject
            {
                ["rear"] = Allowed(Vector2.down, Vector2.down, Vector2.down),
                ["rearTurn"] = Allowed(Vector2.down, new Vector2(.6f, -.8f), Vector2.down),
                ["front"] = Allowed(Vector2.down, Vector2.up, Vector2.up),
                ["side"] = Allowed(Vector2.down, Vector2.left, Vector2.left),
                ["away"] = Allowed(Vector2.down, Vector2.down, Vector2.up),
                ["idle"] = Allowed(Vector2.down, Vector2.down, Vector2.zero),
                ["invalid"] = Allowed(Vector2.down, Vector2.down, new Vector2(float.NaN, 0f)),
                ["fullSpeed"] = full.magnitude, ["partialSpeed"] = partial.magnitude,
                ["partialX"] = partial.x, ["partialY"] = partial.y
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    // 从实际目录读取配对、共享库存和配方，制作成功及拒绝均走正式事务。
    private static JObject CaptureWheelbarrowContent(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().Where(value => !value.Abstract)
            .ToDictionary(value => value.Id, StringComparer.OrdinalIgnoreCase);
        var pairs = new JArray();
        foreach (string id in new[] { "Wheelbarrow", "Wheelbarrow_Building" })
        {
            ItemDefinitionDto item = definitions[id];
            var placement = new Mod_Building.Building_Data();
            item.Modules[ModText.Building].Data.ToObject<Ex_ModData>().ReadData(ref placement);
            ItemModuleDefinitionDto inventory = item.Modules.Values.Single(module => module.Id == "Mod_WheelbarrowInventory");
            pairs.Add(new JObject
            {
                ["id"] = id, ["role"] = (int)placement.Role, ["body"] = placement.BuildingPrefabId,
                ["summoner"] = placement.SummonerPrefabId, ["shared"] = JArray.FromObject(placement.SharedModuleIds ?? Array.Empty<string>()),
                ["sprite"] = item.Visual.SpriteAddress, ["slots"] = inventory.Parameters["CargoSlots"],
                ["weight"] = inventory.Parameters["MaxCargoWeightKg"], ["volume"] = inventory.Parameters["MaxCargoVolumeCubicMeters"],
                ["driverCount"] = item.Modules.Values.Count(module => module.Id == "Mod_Wheelbarrow"),
                ["carrierCount"] = item.Modules.Values.Count(module => module.Id == "Mod_Carrier")
            });
        }

        string recipeRoot = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RecipeCatalogLoader.RelativeRecipeRoot);
        RecipeManifestDto manifest = RecipeCatalogLoader.DeserializeManifest(StreamingAssetsTextLoader.ReadAllText(
            StreamingAssetsTextLoader.CombinePath(recipeRoot, RecipeCatalogLoader.ManifestFileName)));
        RecipeCatalogLoader.ValidateManifest(manifest);
        const string package = "crafting/wheelbarrow.json";
        bool enabled = manifest.Packages.Any(value => value.Enabled && value.Path == package);
        RecipeCatalogDto catalog = RecipeRuntimeFactory.Deserialize(StreamingAssetsTextLoader.ReadAllText(
            RecipeCatalogLoader.ResolvePackagePath(recipeRoot, package)));
        RuntimeRecipe recipe = RecipeRuntimeFactory.BuildCatalog(catalog, definitions.ContainsKey, out List<string> warnings)
            .Single(value => value.Id == "core:wooden_wheelbarrow");
        var inputs = new JArray();
        foreach (RuntimeRecipeIngredient ingredient in recipe.inputs.RowItems_List)
            inputs.Add(new JObject { ["id"] = ingredient.ItemName, ["tag"] = ingredient.Tag, ["amount"] = ingredient.amount });
        var outputs = new JArray();
        foreach (RuntimeRecipeResult output in recipe.outputs.results)
            outputs.Add(new JObject { ["id"] = output.ItemName, ["amount"] = output.amount });
        var missing = new JArray();
        for (int index = 0; index < recipe.inputs.RowItems_List.Count; index++)
            missing.Add(EntityCraftingHandmadeAttempt(recipe, definitions, true, false, -1, index));
        return new JObject
        {
            ["pairs"] = pairs, ["packageEnabled"] = enabled, ["warnings"] = new JArray(warnings),
            ["station"] = recipe.RequiredStation, ["inputs"] = inputs, ["outputs"] = outputs,
            ["normal"] = EntityCraftingHandmadeAttempt(recipe, definitions, false, false),
            ["missing"] = missing, ["full"] = EntityCraftingHandmadeAttempt(recipe, definitions, false, true)
        };
    }
    #endregion
}
