using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 钻孔木棍替代与工具损耗采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterDrilledStickCraftingProbe()
        => Register("crafting", "drilled_stick_substitution", CaptureDrilledStickCrafting);

    // 正式定义、配方、产物规则和事务共同采样，Lua持有替代与随机损耗断言。
    private static JObject CaptureDrilledStickCrafting(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().ToDictionary(value => value.Id);
        RuntimeRecipe recipe = EntityBindingRecipe("tools", "core:石斧");
        ItemData Material(string id, float amount) => EntityCraftingDefinitionData(definitions[id], amount);
        float wood = (float)Number(args, "wood", 0), drilled = (float)Number(args, "drilled", 2);
        var input = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        {
            new(0) { itemData = Material("Ore_Stone", 3) },
            new(1) { itemData = Material(Flag(args, "leather", false) ? "LeatherStrap" : "Rope", 2) },
            new(2) { itemData = wood > 0 ? Material("Stick_Wood", wood) : null },
            new(3) { itemData = drilled > 0 ? Material("DrilledStick", drilled) : null }
        }, "drilled stick input") };
        ItemData blocker = EntityProbeData("probe:drilled-stick-blocker", 1);
        var output = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = Flag(args, "full", false) ? blocker : null } }, "drilled stick output") };
        JArray before = EntityCraftingInventorySnapshot(input.Data), outputBefore = EntityCraftingInventorySnapshot(output.Data);
        bool matched = CraftingRecipeMatcher.TryMatchRecipe(input, recipe, new CraftingCapabilities(), out var match);
        bool prepared = false, committed = false, rollbackRestored = false, previewStable = true;
        var samples = new JArray();
        ItemData product = null;
        Random.State savedRandom = Random.state;
        try
        {
            Random.InitState(1104);
            if (matched)
            {
                string outputId = Text(args, "output", "Axe_Stone");
                ItemData Product()
                {
                    ItemData value = Material(outputId, 1);
                    value.Stack.Stackable = false;
                    return value;
                }
                Random.State beforePreview = Random.state;
                for (int i = 0; i < 3; i++)
                {
                    ItemData preview = Product();
                    CraftingOutputRules.Prepare(input, match, new[] { preview }, out _);
                    previewStable &= preview.Durability == preview.MaxDurability;
                }
                previewStable &= Random.state.Equals(beforePreview) && JToken.DeepEquals(before, EntityCraftingInventorySnapshot(input.Data));
                for (int i = 0; i < 32; i++)
                {
                    product = Product();
                    CraftingOutputRules.Prepare(input, match, new[] { product }, out _, isCommit: true);
                    samples.Add(new JObject { ["maximum"] = product.MaxDurability, ["durability"] = product.Durability });
                }
                // 手钻模块载荷与ItemData分别还原，验证损耗不会被共享状态覆写。
                ItemData handDrill = Material("HandDrill_Summoner", 1);
                handDrill.ModuleDataDic[Mod_HandDrill.ModuleId] = new Ex_ModData_MemoryPackable { ID = Mod_HandDrill.ModuleId };
                CraftingOutputRules.Prepare(input, match, new[] { handDrill }, out _, isCommit: true);
                float drillDurability = handDrill.Durability;
                Mod_HandDrill.RestoreRuntimeDurability(handDrill);
                samples.Add(new JObject { ["handDrill"] = true, ["maximum"] = handDrill.MaxDurability,
                    ["durability"] = drillDurability, ["restored"] = handDrill.Durability });
                prepared = CraftingTransaction.TryCreate(input, output, match, new[] { product }, false, out var transaction, out _);
                if (prepared && transaction.Commit(out _))
                {
                    transaction.Complete();
                    committed = true;
                    if (Flag(args, "rollback", false))
                    {
                        transaction.Rollback();
                        rollbackRestored = JToken.DeepEquals(before, EntityCraftingInventorySnapshot(input.Data)) &&
                            JToken.DeepEquals(outputBefore, EntityCraftingInventorySnapshot(output.Data));
                    }
                }
            }
        }
        finally { Random.state = savedRandom; }
        bool consumed = matched && match.Consumptions.Any(value => value.Amount > 0 && value.SlotIndex == 3);
        var rule = new DrilledStickCraftingDurabilityRule();
        bool invalidPlanRejected = !rule.Prepare(input, new CraftingRecipeMatch(recipe, false,
            new[] { new CraftingConsumption(input.Data.itemSlots.Count, 1) }), new ItemData[0], out _, isCommit: true);
        ItemData catalystProduct = Material("Axe_Stone", 1);
        rule.Prepare(input, new CraftingRecipeMatch(recipe, false, new[] { new CraftingConsumption(3, 0) }),
            new[] { catalystProduct }, out _, isCommit: true);
        return new JObject
        {
            ["matched"] = matched, ["prepared"] = prepared, ["committed"] = committed,
            ["consumedDrilled"] = consumed, ["previewStable"] = previewStable, ["samples"] = samples,
            ["inputsUnchanged"] = JToken.DeepEquals(before, EntityCraftingInventorySnapshot(input.Data)),
            ["outputUnchanged"] = JToken.DeepEquals(outputBefore, EntityCraftingInventorySnapshot(output.Data)),
            ["rollbackRestored"] = rollbackRestored,
            ["invalidPlanRejected"] = invalidPlanRejected,
            ["catalystUnchanged"] = catalystProduct.Durability == catalystProduct.MaxDurability,
            ["woodRemaining"] = input.Data.itemSlots[2].itemData?.Stack?.Amount ?? 0,
            ["drilledRemaining"] = input.Data.itemSlots[3].itemData?.Stack?.Amount ?? 0,
            ["reverseMatched"] = CraftingIngredientMatcher.MatchesIdentity(
                new RuntimeRecipeIngredient { ItemName = "DrilledStick", amount = 1 }, Material("Stick_Wood", 1)),
            ["baseMaximum"] = Material(Text(args, "output", "Axe_Stone"), 1).MaxDurability
        };
    }
    #endregion
}
