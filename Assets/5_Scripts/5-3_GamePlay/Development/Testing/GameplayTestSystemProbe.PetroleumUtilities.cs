using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 石油用途正式规则登记
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterPetroleumUtilityRules()
    {
        Register("crafting", "petroleum_utilities", ProbePetroleumUtilityRecipe);
        Register("building", "bitumen_floor_definition", ProbeBitumenFloorDefinition);
        Register("industrial_fluids", "fuel_oil_engine_definition", ProbeFuelOilEngineDefinition);
    }

    private static JObject ProbeFuelOilEngineDefinition(JObject args)
    {
        MachineDefinition machine = JsonConvert.DeserializeObject<MachineCatalogDocument>(
            Resources.Load<TextAsset>("Config/Mechanical/mechanical-catalog").text).Nodes.Single(value => value.Id == "FuelEngine");
        machine.Validate();
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().ToDictionary(value => value.Id);
        var pairs = new JArray();
        foreach (string id in new[] { "FuelEngine", "FuelEngine_Summoner" })
        {
            ItemDefinitionDto definition = definitions[id];
            Mod_Building.Building_Data state = new();
            definition.Modules[ModText.Building].Data.ToObject<Ex_ModData>().ReadData(ref state);
            pairs.Add(new JObject { ["id"] = id, ["role"] = (int)state.Role,
                ["body"] = state.BuildingPrefabId, ["carrier"] = state.SummonerPrefabId,
                ["machine"] = definition.Modules["机械扭矩模块"].Parameters["DefinitionId"],
                ["health"] = definition.Health.MaxHp });
        }
        return new JObject { ["kind"] = machine.Fluid.Kind,
            ["ambientCombustion"] = MachineLogicRegistry.GetFluidDeviceBehavior(machine.Fluid.Kind) is FluidAmbientCombustionEngineBehavior,
            ["torque"] = machine.Torque, ["rpm"] = machine.Rpm,
            ["ports"] = JArray.FromObject(machine.Fluid.Ports),
            ["inputs"] = JArray.FromObject(machine.Fluid.Reactions.Single().Inputs),
            ["joules"] = machine.Fluid.Reactions[0].ChemicalJoulesPerReaction,
            ["efficiency"] = machine.Fluid.Reactions[0].MechanicalEfficiency, ["pairs"] = pairs };
    }

    private static JObject ProbeBitumenFloorDefinition(JObject args)
    {
        string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, "GameConfig/Tiles/buildings.json");
        TileDefinitionDto tile = TileDefinitionFactory.DeserializeCatalog(StreamingAssetsTextLoader.ReadAllText(path))
            .Tiles.Single(value => value.Id == "Tile_Floor_Bitumen");
        ItemDefinitionDto item = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().Single(value => value.Id == "Floor_Bitumen_Summoner");
        Mod_Building.Building_Data state = new();
        item.Modules[ModText.Building].Data.ToObject<Ex_ModData>().ReadData(ref state);
        return new JObject { ["tileId"] = tile.RuntimeTileId, ["tileAsset"] = tile.TileAsset,
            ["loadCapacity"] = tile.LoadCapacity, ["placement"] = JObject.FromObject(tile.GroundPlacement),
            ["carrier"] = state.SummonerPrefabId, ["placementTile"] = state.TileBlockId,
            ["stackable"] = item.Stackable == true, ["walkable"] = tile.Data.Parameters["isWalkable"] };
    }
    #endregion

    #region 热沥青温度资格与库存事务采样
    // 仅组装正式物品，所有温度匹配、强化和扣料交给生产接口。
    private static JObject ProbePetroleumUtilityRecipe(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().Where(value => !value.Abstract)
            .ToDictionary(value => value.Id, StringComparer.OrdinalIgnoreCase);
        string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, "GameConfig/Recipes/crafting/petroleum_utilities.json");
        RuntimeRecipe recipe = RecipeRuntimeFactory.BuildCatalog(RecipeRuntimeFactory.Deserialize(StreamingAssetsTextLoader.ReadAllText(path)),
            definitions.ContainsKey, out List<string> warnings).Single(value => value.Id == Text(args, "recipe", "core:petroleum_hot_bitumen_tool"));
        var slots = new List<ItemSlot>();
        int missing = (int)Number(args, "missing", -1);
        foreach (RuntimeRecipeIngredient ingredient in recipe.inputs.RowItems_List)
        {
            string id = ingredient.matchMode == MatchMode.ByTag ? ingredient.Tag == "Tool" ? "Axe_Stone" : "Rope" : ingredient.ItemName;
            float amount = ingredient.amount - (slots.Count == missing ? 1 : 0);
            ItemData material = EntityCraftingDefinitionData(definitions[id], amount);
            material.Guid = 9700 + slots.Count;
            if (id == "Bitumen") material.MatterState = new ItemMatterState
            { Initialized = !Flag(args, "uninitialized", false), TemperatureCelsius = (float)Number(args, "temperature", 150) };
            if (ingredient.Tag == "Tool")
            {
                material.Stack.Stackable = false;
                material.MaxDurability = 100;
                material.Durability = 20;
                if (Flag(args, "reinforced", false)) material.Tags.Add(CraftingInstanceOutput.ReinforcedTag);
            }
            slots.Add(new ItemSlot(slots.Count) { itemData = amount > 0 ? material : null });
        }
        var input = new Inventory { Data = new Inventory_Data(slots, "petroleum-utility-input") };
        var output = new Inventory { Data = new Inventory_Data(new List<ItemSlot> { new(0)
            { itemData = Flag(args, "full", false) ? EntityProbeData("petroleum:blocker", 1) : null } }, "petroleum-utility-output") };
        JArray beforeInput = EntityCraftingInventorySnapshot(input.Data), beforeOutput = EntityCraftingInventorySnapshot(output.Data);
        var capabilities = new CraftingCapabilities { StationId = Text(args, "station", recipe.RequiredStation ?? "handcraft"),
            CompatibleStationIds = new[] { "handcraft" }, InputSlotLimit = 5, ApplyOutputMultiplier = false };
        bool matched = CraftingRecipeMatcher.TryMatchRecipe(input, recipe, capabilities, out CraftingRecipeMatch match);
        if (Flag(args, "cooledBeforeCommit", false))
        {
            foreach (ItemSlot slot in slots)
                if (slot.itemData?.IDName == "Bitumen") slot.itemData.MatterState.TemperatureCelsius = 149.9f;
        }
        bool qualifiedAtCommit = CraftingRecipeMatcher.TryMatchRecipe(input, recipe, capabilities, out match);
        var products = new List<ItemData>();
        bool productsValid = qualifiedAtCommit;
        if (qualifiedAtCommit)
            foreach (RuntimeRecipeResult product in recipe.outputs.results)
            {
                if (!string.IsNullOrEmpty(product.CopyInputTag))
                {
                    if (CraftingInstanceOutput.TryCreate(input, recipe, match, product, out ItemData instance, out _)) products.Add(instance);
                    else productsValid = false;
                }
                else products.Add(EntityCraftingDefinitionData(definitions[product.ItemName], product.amount));
            }
        bool committed = false;
        if (productsValid && CraftingTransaction.TryCreate(input, output, match, products, false, out CraftingTransaction transaction, out _)
            && transaction.Commit(out _))
        { transaction.Complete(); committed = true; }
        ItemData result = committed ? output.Data.itemSlots[0].itemData : null;
        return new JObject { ["warnings"] = new JArray(warnings), ["id"] = recipe.Id,
            ["matched"] = matched, ["qualifiedAtCommit"] = qualifiedAtCommit, ["committed"] = committed,
            ["inputBefore"] = beforeInput, ["inputAfter"] = EntityCraftingInventorySnapshot(input.Data),
            ["outputBefore"] = beforeOutput, ["outputAfter"] = EntityCraftingInventorySnapshot(output.Data),
            ["unchanged"] = JToken.DeepEquals(beforeInput, EntityCraftingInventorySnapshot(input.Data)) &&
                JToken.DeepEquals(beforeOutput, EntityCraftingInventorySnapshot(output.Data)),
            ["reinforced"] = result?.Tags.Contains(CraftingInstanceOutput.ReinforcedTag) == true,
            ["maximum"] = result?.MaxDurability ?? 0, ["durability"] = result?.Durability ?? 0 };
    }
    #endregion
}
