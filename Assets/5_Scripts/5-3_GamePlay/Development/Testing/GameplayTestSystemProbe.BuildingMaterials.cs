using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 建筑真实制作材料采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterBuildingMaterialProbes() => Register("building", "material_provenance", ProbeBuildingMaterials);

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
