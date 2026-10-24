using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 绳料品质与皮带加固采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterBindingMaterialProbes()
    {
        Register("crafting", "binding_materials", EntityBindingMaterials);
        Register("crafting", "binding_reinforcement", EntityBindingReinforcement);
        Register("crafting", "leather_cutting", EntityLeatherCutting);
    }

    // 正式配方走匹配、品质规则和库存事务，Lua 核对混料、一次加固与回滚。
    private static RuntimeRecipe EntityBindingRecipe(string package, string recipeId)
    {
        string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, "GameConfig/Recipes/crafting/" + package + ".json");
        return RecipeRuntimeFactory.BuildCatalog(RecipeRuntimeFactory.Deserialize(StreamingAssetsTextLoader.ReadAllText(path)),
            _ => true, out _).Single(value => value.Id == recipeId);
    }

    private static JObject EntityLeatherCutting(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().Where(value => !value.Abstract).ToDictionary(value => value.Id);
        RuntimeRecipe recipe = EntityBindingRecipe("sewing", "core:leather_cutting");
        return EntityCraftingHandmadeAttempt(recipe, definitions, Flag(args, "missing", false),
            Flag(args, "full", false), Flag(args, "missingKnife", false) ? 0 : -1);
    }

    private static JObject EntityBindingMaterials(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().ToDictionary(value => value.Id);
        RuntimeRecipe recipe = EntityBindingRecipe("shields", "core:木盾");
        ItemData plank = EntityCraftingDefinitionData(definitions["Plank"], 5);
        ItemData strap = EntityCraftingDefinitionData(definitions["LeatherStrap"], (float)Number(args, "straps", 5));
        ItemData secondStrap = EntityCraftingDefinitionData(definitions["LeatherStrap"], (float)Number(args, "secondStack", 0));
        ItemData rope = EntityCraftingDefinitionData(definitions[Text(args, "ropeId", "Rope")], (float)Number(args, "ropes", 0));
        if (Flag(args, "invalidQuality", false)) strap.Tags.Add(BindingMaterialCraftingRule.DurabilityTagPrefix + "NaN");
        var input = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        {
            new(0) { itemData = plank }, new(1) { itemData = strap.Stack.Amount > 0 ? strap : null },
            new(2) { itemData = secondStrap.Stack.Amount > 0 ? secondStrap : null },
            new(3) { itemData = rope.Stack.Amount > 0 ? rope : null }
        }, "binding material input") };
        ItemData blocker = EntityProbeData("probe:binding-blocker", 1);
        var output = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = Flag(args, "full", false) ? blocker : null } }, "binding material output") };
        var capabilities = new CraftingCapabilities { StationId = "handcraft", InputSlotLimit = 4, ApplyOutputMultiplier = false };
        JArray before = EntityCraftingInventorySnapshot(input.Data);
        bool matched = CraftingRecipeMatcher.TryMatchRecipe(input, recipe, capabilities, out CraftingRecipeMatch match);
        Func<ItemData> factory = ItemInstanceDataFactory.Compile(EntityCraftingDefinitionData(definitions["Shield_Wood"], 1));
        ItemData product = factory();
        CraftedDurabilityQuality.ApplyToCraftedOutput(product, (float)Number(args, "quality", 1));
        bool ruleAccepted = false, success = false;
        if (matched) ruleAccepted = new BindingMaterialCraftingRule().Prepare(input, match, new[] { product }, out _);
        bool previewUnchanged = JToken.DeepEquals(before, EntityCraftingInventorySnapshot(input.Data));
        if (matched && ruleAccepted && CraftingTransaction.TryCreate(input, output, match, new[] { product }, false,
                out CraftingTransaction transaction, out _) && transaction.Commit(out _))
        { transaction.Complete(); success = true; }
        ItemData result = success ? output.Data.itemSlots[0].itemData : null;
        ItemData transferred = factory();
        if (result != null) CraftedDurabilityQuality.CopyInstanceQuality(result, transferred);
        return new JObject
        {
            ["matched"] = matched, ["ruleAccepted"] = ruleAccepted, ["success"] = success,
            ["previewUnchanged"] = previewUnchanged, ["inputsUnchanged"] = JToken.DeepEquals(before, EntityCraftingInventorySnapshot(input.Data)),
            ["maximum"] = result?.MaxDurability ?? 0, ["durability"] = result?.Durability ?? 0,
            ["quality"] = result?.CraftedDurabilityMultiplier ?? 0,
            ["reinforced"] = result?.Tags.Contains(BindingMaterialCraftingRule.ReinforcedTag) == true,
            ["remainingStraps"] = (input.Data.itemSlots[1].itemData?.Stack?.Amount ?? 0) + (input.Data.itemSlots[2].itemData?.Stack?.Amount ?? 0),
            ["remainingRopes"] = input.Data.itemSlots[3].itemData?.Stack?.Amount ?? 0,
            ["outputStillBlocked"] = ReferenceEquals(output.Data.itemSlots[0].itemData, blocker),
            ["transferredMaximum"] = transferred.MaxDurability,
            ["transferredMarker"] = transferred.Tags.Contains(BindingMaterialCraftingRule.ReinforcedTag)
        };
    }

    private static JObject EntityBindingReinforcement(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().ToDictionary(value => value.Id);
        RuntimeRecipe recipe = EntityBindingRecipe("tool_reinforcement", "core:tool_reinforcement_leather_strap");
        ItemData template = EntityCraftingDefinitionData(definitions["Shield_Wood"], 1);
        template.Guid = 18201;
        Func<ItemData> factory = ItemInstanceDataFactory.Compile(template);
        ItemData tool = factory();
        CraftedDurabilityQuality.ApplyToCraftedOutput(tool, (float)Number(args, "quality", 1));
        tool.Durability = (float)Number(args, "durability", 40);
        tool.ItemSpecialData = "{\"binding-probe\":true}";
        tool.Tags.Add("CustomBindingProbeTag");
        if (Flag(args, "reinforced", false)) tool.Tags.Add(BindingMaterialCraftingRule.ReinforcedTag);
        if (Flag(args, "adhesive", false)) tool.Tags.Add(CraftingInstanceOutput.ReinforcedTag);
        if (Flag(args, "nails", false)) tool.Tags.Add("NailReinforced");
        if (Flag(args, "notTool", false)) tool.Tags.Remove("Tool");
        if (Flag(args, "stacked", false)) tool.Stack.Amount = 2;
        ItemData strap = EntityCraftingDefinitionData(definitions["LeatherStrap"], (float)Number(args, "straps", 2));
        var input = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = tool }, new(1) { itemData = strap } }, "binding reinforcement input") };
        ItemData blocker = EntityProbeData("probe:binding-blocker", 1);
        var output = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = Flag(args, "full", false) ? blocker : null } }, "binding reinforcement output") };
        var capabilities = new CraftingCapabilities { StationId = "handcraft", InputSlotLimit = 2, ApplyOutputMultiplier = false };
        JArray before = EntityCraftingInventorySnapshot(input.Data);
        CraftingResult preview = CraftingService.PreviewRecipe(input, output, capabilities, recipe);
        bool previewUnchanged = JToken.DeepEquals(before, EntityCraftingInventorySnapshot(input.Data));
        CraftingResult crafted = CraftingService.CraftRecipe(input, output, capabilities, recipe);
        ItemData product = crafted.Success ? output.Data.itemSlots[0].itemData : null;
        bool repeatRejected = false;
        ItemData restored = null;
        if (product != null)
        {
            restored = factory();
            ItemInstanceSnapshot snapshot = ItemInstanceSnapshot.Capture(product);
            snapshot.RestoreTo(restored);
            snapshot.RestoreTo(restored);
            var repeatInput = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
            { new(0) { itemData = product }, new(1) { itemData = strap } }, "binding repeat input") };
            var repeatOutput = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
            { new(0) }, "binding repeat output") };
            repeatRejected = !CraftingService.PreviewRecipe(repeatInput, repeatOutput, capabilities, recipe).Success;
        }
        return new JObject
        {
            ["preview"] = preview.Success, ["success"] = crafted.Success, ["previewUnchanged"] = previewUnchanged,
            ["inputsUnchanged"] = JToken.DeepEquals(before, EntityCraftingInventorySnapshot(input.Data)),
            ["maximum"] = product?.MaxDurability ?? 0, ["durability"] = product?.Durability ?? 0,
            ["quality"] = product?.CraftedDurabilityMultiplier ?? 0,
            ["toolRemaining"] = input.Data.itemSlots[0].itemData?.Stack?.Amount ?? 0,
            ["strapsRemaining"] = input.Data.itemSlots[1].itemData?.Stack?.Amount ?? 0,
            ["repeatRejected"] = repeatRejected,
            ["identityPreserved"] = product?.Guid == tool.Guid && product?.ItemSpecialData == tool.ItemSpecialData,
            ["tagsPreserved"] = product != null && product.Tags.Contains("CustomBindingProbeTag") &&
                product.Tags.Contains(BindingMaterialCraftingRule.ReinforcedTag) &&
                (!Flag(args, "adhesive", false) || product.Tags.Contains(CraftingInstanceOutput.ReinforcedTag)) &&
                (!Flag(args, "nails", false) || product.Tags.Contains("NailReinforced")),
            ["restoredMaximum"] = restored?.MaxDurability ?? 0, ["restoredDurability"] = restored?.Durability ?? 0,
            ["restoredMarker"] = restored?.Tags.Contains(BindingMaterialCraftingRule.ReinforcedTag) == true,
            ["outputStillBlocked"] = ReferenceEquals(output.Data.itemSlots[0].itemData, blocker)
        };
    }
    #endregion
}
