using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 建筑真实制作材料采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterBuildingMaterialProbes()
    {
        Register("building", "material_provenance", ProbeBuildingMaterials);
        Register("building", "material_defaults", ProbeBuildingMaterialDefaults);
    }

    // 目录与正式回收规则提供原始采样，覆盖范围和材料数量交给 Lua 核对。
    private static JObject ProbeBuildingMaterialDefaults(JObject args)
    {
        var definitions = MineralContentDefinitions();
        string root = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RecipeCatalogLoader.RelativeRecipeRoot);
        var manifest = JsonConvert.DeserializeObject<RecipeManifestDto>(StreamingAssetsTextLoader.ReadAllText(
            StreamingAssetsTextLoader.CombinePath(root, RecipeCatalogLoader.ManifestFileName)));
        var recipes = manifest.Packages.Where(package => package.Enabled).SelectMany(package =>
            RecipeRuntimeFactory.BuildCatalog(RecipeRuntimeFactory.Deserialize(StreamingAssetsTextLoader.ReadAllText(
                StreamingAssetsTextLoader.CombinePath(root, package.Path))), definitions.ContainsKey, out _)).ToArray();
        var samples = new JArray();
        foreach (ItemDefinitionDto definition in definitions.Values)
        {
            var module = definition.Modules?.Values.FirstOrDefault(value => value.Id == ModText.Building);
            if (module == null) continue;
            var state = JsonConvert.DeserializeObject<Mod_Building.Building_Data>(module.Data.Value<string>("BitData"));
            if (state.Role != BuildingRole.Summoner) continue;
            ItemData data = ProbeMaterialBuilding(definition.Id, BuildingRole.Summoner);
            ((Ex_ModData)data.ModuleDataDic[ModText.Building]).WriteData(state);
            bool hasDefault = state.CraftingMaterials != null;
            IReadOnlyDictionary<string, float> materials = null;
            bool resolved = hasDefault && BuildingMaterialSalvage.TryGetMaterialAmounts(definition.Id, data, out materials, out _);
            samples.Add(new JObject
            {
                ["id"] = definition.Id, ["hasDefault"] = hasDefault, ["resolved"] = resolved,
                ["materials"] = materials == null ? null : JObject.FromObject(materials),
                ["exactRecipes"] = new JArray(recipes.Where(recipe =>
                    BuildingMaterialSalvage.IsExactSingleBuildingRecipe(recipe, definition.Id)).Select(recipe => recipe.Id))
            });
        }

        RuntimeRecipe coconut = recipes.Single(recipe => recipe.outputs.results.Count == 2 &&
            recipe.outputs.results.Any(output => output.ItemName == "Coconut_Shell"));
        ItemData shell = ProbeMaterialBuilding("Coconut_Shell", BuildingRole.Summoner);
        var shellModule = definitions["Coconut_Shell"].Modules.Values.Single(value => value.Id == ModText.Building);
        ((Ex_ModData)shell.ModuleDataDic[ModText.Building]).WriteData(
            JsonConvert.DeserializeObject<Mod_Building.Building_Data>(shellModule.Data.Value<string>("BitData")));
        bool prepared = new BuildingMaterialCraftingRule().Prepare(null,
            new CraftingRecipeMatch(coconut, false, Array.Empty<CraftingConsumption>()),
            new[] { EntityProbeData("CoconutMeat", 1), shell }, out string error);
        BuildingMaterialSalvage.TryGetMaterialAmounts("Coconut_Shell", shell, out var shellMaterials, out _);
        return new JObject { ["items"] = samples, ["coproductPrepared"] = prepared, ["coproductError"] = error,
            ["shellMaterials"] = JObject.FromObject(shellMaterials) };
    }

    // 真实木箱配方走正式匹配、产物规则和库存事务；夹具仅提供独立建筑模块。
    private static JObject ProbeBuildingMaterials(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().ToDictionary(value => value.Id);
        RuntimeRecipe recipe = EntityBindingRecipe("buildings", "core:木箱子");
        var input = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        {
            new(0) { itemData = EntityCraftingDefinitionData(definitions["Plank"], 6) },
            new(1) { itemData = EntityCraftingDefinitionData(definitions["LeatherStrap"], (float)Number(args, "straps", 0)) },
            new(2) { itemData = EntityCraftingDefinitionData(definitions["Rope"], (float)Number(args, "ropes", 4)) },
            new(3) { itemData = EntityCraftingDefinitionData(definitions["Stick_Wood"], 10) }
        }, "building materials input") };
        for (int index = 1; index <= 2; index++)
            if (input.Data.itemSlots[index].itemData.Stack.Amount <= 0f) input.Data.itemSlots[index].itemData = null;
        var output = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = Flag(args, "full", false) ? EntityProbeData("probe:blocked", 1) : null } }, "building materials output") };
        var capabilities = new CraftingCapabilities { StationId = "handcraft", InputSlotLimit = 4, ApplyOutputMultiplier = false };
        JArray before = EntityCraftingInventorySnapshot(input.Data);
        bool matched = CraftingRecipeMatcher.TryMatchRecipe(input, recipe, capabilities, out var match);
        ItemData product = ProbeMaterialBuilding("Chest_Wood_Summoner", BuildingRole.Summoner);
        Mod_Building.SetCraftingMaterials(product, new Dictionary<string, float> { ["Plank"] = 6, ["Rope"] = 4 });
        product.Stack.Amount = (float)Number(args, "quantity", 1);
        var rule = new BuildingMaterialCraftingRule();
        bool prepared = matched && rule.Prepare(input, match, new[] { product }, out _);
        bool previewUnchanged = JToken.DeepEquals(before, EntityCraftingInventorySnapshot(input.Data));
        bool reorderedIdentity = false;
        if (prepared)
        {
            ItemData reordered = ProbeMaterialBuilding("Chest_Wood_Summoner", BuildingRole.Summoner);
            reordered.Stack.Amount = product.Stack.Amount;
            rule.Prepare(input, new CraftingRecipeMatch(recipe, false, match.Consumptions.Reverse().ToArray()), new[] { reordered }, out _);
            reorderedIdentity = product.HasSameStackIdentity(reordered);
        }
        bool success = false;
        if (prepared && CraftingTransaction.TryCreate(input, output, match, new[] { product }, false, out var transaction, out _) &&
            transaction.Commit(out _))
        { transaction.Complete(); success = true; }

        ItemData restored = ProbeMaterialBuilding("Chest_Wood_Summoner", BuildingRole.Summoner);
        byte[] saved = ItemSnapshotSerialization.SerializePayload(ItemInstanceSnapshot.Capture(product));
        MemoryPack.MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(saved).RestoreTo(restored);
        ItemData body = ProbeMaterialBuilding("Chest_Wood", BuildingRole.PlacedBuilding);
        BuildingModuleStateTransfer.Copy(restored, body, Array.Empty<string>());
        ItemData carrier = ProbeMaterialBuilding("Chest_Wood_Summoner", BuildingRole.Summoner);
        BuildingModuleStateTransfer.Copy(body, carrier, Array.Empty<string>());
        var materials = new JObject();
        var proportional = new JObject();
        var fullRecovery = new JObject();
        bool zeroRecovery = false, isolated = false, differentMaterialsStack = false, invalidRejected = false;
        if (prepared && BuildingMaterialSalvage.TryGetMaterialAmounts("Chest_Wood_Summoner", carrier, out var amounts, out _))
        {
            materials = JObject.FromObject(amounts);
            proportional = JObject.FromObject(BuildingMaterialSalvage.GetProportionalMaterialAmounts(amounts, .5f));
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            try
            {
                zeroRecovery = BuildingMaterialSalvage.GetRandomMaterialAmounts(amounts, 0f).Values.All(value => value == 0);
                fullRecovery = JObject.FromObject(BuildingMaterialSalvage.GetRandomMaterialAmounts(amounts, 1f));
            }
            finally { UnityEngine.Random.state = randomState; }
            var different = new Dictionary<string, float> { ["Plank"] = 6, ["Rope"] = 1, ["LeatherStrap"] = 3 };
            carrier.ItemSpecialData = BuildingMaterialSalvage.CreateMaterialStackIdentity(different);
            differentMaterialsStack = product.HasSameStackIdentity(carrier);
            Mod_Building.SetCraftingMaterials(body, different);
            BuildingMaterialSalvage.TryGetMaterialAmounts("Chest_Wood_Summoner", restored, out var original, out _);
            isolated = JToken.DeepEquals(materials, JObject.FromObject(original));
            Mod_Building.SetCraftingMaterials(body, new Dictionary<string, float> { ["Rope"] = float.NaN });
            invalidRejected = !BuildingMaterialSalvage.TryGetMaterialAmounts("Chest_Wood_Summoner", body, out _, out _);
        }
        return new JObject
        {
            ["matched"] = matched, ["prepared"] = prepared, ["success"] = success,
            ["previewUnchanged"] = previewUnchanged,
            ["inputsUnchanged"] = JToken.DeepEquals(before, EntityCraftingInventorySnapshot(input.Data)),
            ["materials"] = materials, ["proportional"] = proportional, ["fullRecovery"] = fullRecovery,
            ["zeroRecovery"] = zeroRecovery, ["isolated"] = isolated, ["reorderedIdentity"] = reorderedIdentity,
            ["differentMaterialsStack"] = differentMaterialsStack, ["invalidRejected"] = invalidRejected,
            ["spareUntouched"] = input.Data.itemSlots[3].itemData.Stack.Amount == 10f
        };
    }

    private static ItemData ProbeMaterialBuilding(string id, BuildingRole role)
    {
        ItemData data = EntityProbeData(id, 1);
        var module = new Ex_ModData { ID = ModText.Building, Name = ModText.Building, isRunning = true };
        module.WriteData(new Mod_Building.Building_Data
        {
            Version = Mod_Building.CurrentBuildingDataVersion, Role = role,
            BuildingPrefabId = "Chest_Wood", SummonerPrefabId = "Chest_Wood_Summoner"
        });
        data.ModuleDataDic[ModText.Building] = module;
        return data;
    }
    #endregion
}
