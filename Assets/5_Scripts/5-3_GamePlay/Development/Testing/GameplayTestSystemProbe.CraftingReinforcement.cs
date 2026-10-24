using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 采集与实例强化规则登记
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterReinforcementRules()
    {
        Register("crafting", "tool_reinforcement", EntityToolReinforcement);
        Register("crafting", "tool_reinforcement_catalog", EntityReinforcementCatalog);
        Register("crafting", "wood_shield_recipe", EntityWoodShieldRecipe);
        Register("crafting", "wood_shield_reinforcement", EntityWoodShieldReinforcement);
        Register("agriculture", "manual_harvest_work", EntityManualHarvestWork);
        Register("agriculture", "crop_health_catalog", EntityCropHealthCatalog);
        Register("combat", "equipment_durability", EntityEquipmentDurability);
        Register("building", "placement_distance", EntityPlacementDistance);
    }

    private static JObject EntityManualHarvestWork(JObject args)
    {
        int progress = 0;
        int steps = (int)Number(args, "steps", ManualHarvestWork.DefaultSteps);
        var completed = new JArray();
        var progressValues = new JArray();
        for (int i = 0; i < 10; i++)
        {
            completed.Add(ManualHarvestWork.Advance(ref progress, steps));
            progressValues.Add(progress);
        }
        return new JObject { ["defaultSteps"] = ManualHarvestWork.DefaultSteps, ["completed"] = completed, ["progress"] = progressValues,
            ["staminaCost"] = ManualHarvestWork.StaminaCost, ["shakeDuring"] = ManualHarvestWork.ShakeAngle(.02f),
            ["shakeBefore"] = ManualHarvestWork.ShakeAngle(-1f), ["shakeAfter"] = ManualHarvestWork.ShakeAngle(.3f) };
    }

    private static JObject EntityCropHealthCatalog(JObject args)
    {
        var crops = new JArray();
        foreach (ItemDefinitionDto definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
        {
            if (definition.Abstract || definition.Modules == null || !definition.Modules.Values.Any(module =>
                module?.Enabled != false && module?.Prefab == "Module_Crop")) continue;
            ItemModuleDefinitionDto health = definition.Modules.Values.FirstOrDefault(module =>
                module?.Enabled != false && module?.Prefab == "Module_DamageReciver");
            crops.Add(new JObject { ["id"] = definition.Id,
                ["hp"] = health?.Parameters?["Data"]?["Hp"]?.Value<float>() ?? -1f,
                ["maximum"] = health?.Parameters?["Data"]?["MaxHp"]?.Value<float>() ?? -1f });
        }
        return new JObject { ["crops"] = crops };
    }

    private static JObject EntityEquipmentDurability(JObject args)
    {
        float knife = Mod_ResourceHarvest.ResolveAffinityMultiplier(ResourceToolKind.Knife, 1, ResourceToolKind.Knife, 1, 1f);
        ItemData ResolveTemplate(string prefab)
        {
            var template = new Data_GeneralItem { MaxDurability = 777f, Durability = 777f, Tags = new List<string> { "Tool" } };
            EquipmentDurabilityRules.ApplyDefaults(new ItemDefinitionDto { Id = "probe_Obsidian", Modules =
                new Dictionary<string, ItemModuleDefinitionDto> { ["action"] = new() { Prefab = prefab, Enabled = true } } }, template);
            return template;
        }
        ItemData coldWeapon = ResolveTemplate("Mod_ColdWeapon");
        ItemData shovel = ResolveTemplate("Module_Shovel");
        return new JObject {
            ["wood"] = EquipmentDurabilityRules.ResolveMaterialMaximum("Axe_Wood"),
            ["stone"] = EquipmentDurabilityRules.ResolveMaterialMaximum("Axe_Stone"),
            ["bronze"] = EquipmentDurabilityRules.ResolveMaterialMaximum("Axe_Bronze"),
            ["iron"] = EquipmentDurabilityRules.ResolveMaterialMaximum("Axe_Iron"),
            ["obsidian"] = EquipmentDurabilityRules.ResolveMaterialMaximum("Dagger_Obsidian Stone"),
            ["obsidianNonWeapon"] = EquipmentDurabilityRules.ResolveMaterialMaximum("Obsidian Stone", 777f, false),
            ["unclassified"] = EquipmentDurabilityRules.ResolveMaterialMaximum("Unclassified", 60f),
            ["coldWeaponMaximum"] = coldWeapon.MaxDurability,
            ["coldWeaponTagged"] = coldWeapon.Tags.Contains(EquipmentDurabilityRules.AttackEquipmentTag),
            ["obsidianToolMaximum"] = shovel.MaxDurability,
            ["positiveHitWear"] = EquipmentDurabilityRules.ResolveRemaining(50f, true),
            ["zeroDamageHitWear"] = EquipmentDurabilityRules.ResolveRemaining(50f, true),
            ["damageRejectedAfterHitWear"] = EquipmentDurabilityRules.ResolveRemaining(50f, true),
            ["missWear"] = EquipmentDurabilityRules.ResolveRemaining(50f, false),
            ["lastPoint"] = EquipmentDurabilityRules.ResolveRemaining(1f, true),
            ["knifeCropMultiplier"] = knife,
            ["knifeWrongTarget"] = Mod_ResourceHarvest.ResolveAffinityMultiplier(ResourceToolKind.Axe, 1, ResourceToolKind.Knife, 1, 1f),
            ["postDefense"] = FlatWorld.Combat.CombatRules.ResolvePhysical(10f, 4f, 1f, knife),
            ["blockedAffinity"] = FlatWorld.Combat.CombatRules.ResolvePhysical(4f, 10f, 1f, knife) };
    }

    private static JObject EntityPlacementDistance(JObject args) => EntityWithQuestPlayer(player =>
    {
        player.BindData(new Data_Player { IDName = "probe:placement-distance" });
        var interaction = player.gameObject.AddComponent<Mod_InteractSender>();
        interaction.maxInteractDistance = 7.5f;
        var bag = player.gameObject.AddComponent<Mod_Inventory>();
        bag.item = player;
        bag.Data.ID = ModText.Bag;
        bag.Data.StableName = "probe_bag";
        var inventory = new Inventory { Data = new Inventory_Data(new List<ItemSlot> { new(0) }, ModText.Bag) };
        bag.InventoryInstances.Add(inventory);
        player.itemMods.AddMod(bag);
        float survival = Mod_Building.ResolveMaxPlacementDistance(player, 10f);
        float fallback = Mod_Building.ResolveMaxPlacementDistance(null, 3.25f);
        inventory.Data.SetUnlimitedCarryCapacity(true);
        return new JObject { ["survival"] = survival, ["fallback"] = fallback,
            ["creativeUnlimited"] = float.IsPositiveInfinity(Mod_Building.ResolveMaxPlacementDistance(player, 10f)) };
    });
    #endregion

    #region 正式强化配方与库存事务采样
    private static JObject EntityReinforcementCatalog(JObject args)
    {
        var expected = new HashSet<string>(new[] { "Axe_Stone", "Axe_Iron", "Pickaxe_Stone", "Pickaxe_Iron",
            "Dagger_Stone", "Dagger_Obsidian", "Scissors", "WoodHammer", "StoneHammer", "Bow_Wood", "Bow_Recurve",
            "Blowgun_Wood", "StoneThrowingRope", "Shovel_Wood", "Hoe_Stone", "FishingRod" });
        var rows = new JArray();
        foreach (ItemDefinitionDto definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
            if (!definition.Abstract && expected.Contains(definition.Id))
                rows.Add(new JObject { ["id"] = definition.Id, ["tool"] = definition.Tags?.Contains("Tool") == true,
                    ["stackable"] = definition.Stackable ?? true, ["maximum"] = definition.MaxDurability ?? 0f });
        return new JObject { ["tools"] = rows, ["expectedCount"] = expected.Count };
    }

    private static JObject EntityToolReinforcement(JObject args)
    {
        string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath,
            "GameConfig/Recipes/crafting/tool_reinforcement.json");
        RecipeCatalogDto catalog = RecipeRuntimeFactory.Deserialize(StreamingAssetsTextLoader.ReadAllText(path));
        List<RuntimeRecipe> recipes = RecipeRuntimeFactory.BuildCatalog(catalog, _ => true, out List<string> warnings);
        bool adhesive = Flag(args, "adhesive", false);
        RuntimeRecipe recipe = recipes.Single(value => value.Id.EndsWith(adhesive ? "_adhesive" : "_hot_resin", StringComparison.Ordinal));
        var template = EntityProbeData("probe:reinforcement-tool", 1f);
        template.Guid = 17001;
        template.Stack.Stackable = false;
        template.Tags = new List<string> { "Tool" };
        template.MaxDurability = 100f;
        template.Durability = 100f;
        Func<ItemData> factory = ItemInstanceDataFactory.Compile(template);
        ItemData tool = factory();
        tool.Durability = 20f;
        tool.ItemSpecialData = "{\"probe\":\"retained\"}";
        tool.Tags.Add("CustomInstanceTag");
        if (Flag(args, "reinforced", false)) tool.Tags.Add(CraftingInstanceOutput.ReinforcedTag);
        if (Flag(args, "stackedTool", false)) tool.Stack.Amount = 2f;
        var glue = EntityProbeData(adhesive ? "probe:glue" : "Resin", 3f);
        glue.Guid = 17002;
        glue.Tags = new List<string> { adhesive ? "Adhesive" : "Resin" };
        float temperature = (float)Number(args, "temperature", 200);
        if (Flag(args, "invalidTemperature", false)) temperature = float.NaN;
        glue.MatterState = new ItemMatterState { Initialized = !Flag(args, "uninitialized", false), TemperatureCelsius = temperature };
        var input = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = tool }, new(1) { itemData = glue } }, "reinforcement input") };
        var blocker = EntityProbeData("probe:output-blocker", 1f);
        var output = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = Flag(args, "full", false) ? blocker : null } }, "reinforcement output") };
        var capabilities = new CraftingCapabilities { StationId = Flag(args, "workbench", false) ? "workbench" : "handcraft", InputSlotLimit = 2 };
        bool matched = CraftingRecipeMatcher.TryMatchRecipe(input, recipe, capabilities, out _);
        CraftingResult preview = CraftingService.PreviewRecipe(input, output, capabilities, recipe);
        bool sourceUnchangedAfterPreview = tool.Durability == 20f && tool.MaxDurability == 100f && glue.Stack.Amount == 3f;
        int dataNotifications = 0;
        input.Data.Event_OnDataChanged += _ => dataNotifications++;
        if (Flag(args, "coolBeforeCommit", false))
        {
            glue.MatterState.TemperatureCelsius = 199.9f;
            input.Data.NotifyItemStateChanged(glue);
        }
        bool matchedBeforeCommit = CraftingRecipeMatcher.TryMatchRecipe(input, recipe, capabilities, out _);
        CraftingResult result = CraftingService.CraftRecipe(input, output, capabilities, recipe);
        ItemData product = result.Success ? output.Data.itemSlots[0].itemData : null;
        bool snapshotTags = false, snapshotIdentity = false, secondRejected = false;
        float restoredMax = 0f, restoredDurability = 0f;
        if (product != null)
        {
            ItemData restored = factory();
            ItemInstanceSnapshot.Capture(product).RestoreTo(restored);
            restoredMax = restored.MaxDurability;
            restoredDurability = restored.Durability;
            snapshotTags = restored.Tags.Contains("CustomInstanceTag") && restored.Tags.Contains(CraftingInstanceOutput.ReinforcedTag);
            snapshotIdentity = restored.Guid == 17001 && restored.ItemSpecialData == tool.ItemSpecialData;
            var secondInput = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
            { new(0) { itemData = restored }, new(1) { itemData = glue } }, "second reinforcement input") };
            secondRejected = !CraftingRecipeMatcher.TryMatchRecipe(secondInput, recipe, capabilities, out _);
        }
        return new JObject
        {
            ["warnings"] = new JArray(warnings), ["matched"] = matched, ["preview"] = preview.Success,
            ["sourceUnchangedAfterPreview"] = sourceUnchangedAfterPreview, ["matchedBeforeCommit"] = matchedBeforeCommit,
            ["success"] = result.Success, ["failure"] = result.FailureReason.ToString(),
            ["toolRemaining"] = input.Data.itemSlots[0].itemData?.Stack?.Amount ?? 0f,
            ["glueRemaining"] = input.Data.itemSlots[1].itemData?.Stack?.Amount ?? 0f,
            ["outputAmount"] = product?.Stack?.Amount ?? 0f, ["outputGuid"] = product?.Guid ?? 0,
            ["maximum"] = product?.MaxDurability ?? 0f, ["durability"] = product?.Durability ?? 0f,
            ["snapshotTags"] = snapshotTags, ["snapshotIdentity"] = snapshotIdentity,
            ["restoredMaximum"] = restoredMax, ["restoredDurability"] = restoredDurability,
            ["repeatRejected"] = secondRejected, ["dataNotifications"] = dataNotifications,
            ["outputStillBlocked"] = !result.Success && ReferenceEquals(output.Data.itemSlots[0].itemData, blocker)
        };
    }
    #endregion

    #region 木盾正式配方与钉子加固采样
    // 只采样正式匹配、实例复制、库存事务和快照，业务断言由 Lua 持有。
    private static RuntimeRecipe EntityWoodShieldRecipeDefinition(bool reinforcement, out List<string> warnings)
    {
        string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath,
            "GameConfig/Recipes/crafting/shields.json");
        var definitions = new HashSet<string>(ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().Select(value => value.Id));
        return RecipeRuntimeFactory.BuildCatalog(RecipeRuntimeFactory.Deserialize(StreamingAssetsTextLoader.ReadAllText(path)),
            definitions.Contains, out warnings).Single(value => reinforcement
                ? value.Id == "core:wood_shield_nail_reinforcement" : value.Id == "core:木盾");
    }

    private static JObject EntityWoodShieldRecipe(JObject args)
    {
        RuntimeRecipe recipe = EntityWoodShieldRecipeDefinition(false, out List<string> warnings);
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().ToDictionary(value => value.Id);
        ItemData plank = EntityCraftingDefinitionData(definitions[Text(args, "plankId", "Plank")], (float)Number(args, "planks", 5));
        ItemData rope = EntityCraftingDefinitionData(definitions[Text(args, "ropeId", "Rope")], (float)Number(args, "ropes", 5));
        var input = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = plank }, new(1) { itemData = rope } }, "shield materials") };
        var blocker = EntityProbeData("probe:shield-blocker", 1);
        var output = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = Flag(args, "full", false) ? blocker : null } }, "shield output") };
        var capabilities = new CraftingCapabilities { StationId = "handcraft", InputSlotLimit = 2, ApplyOutputMultiplier = false };
        bool matched = CraftingRecipeMatcher.TryMatchRecipe(input, recipe, capabilities, out CraftingRecipeMatch match);
        ItemData product = EntityCraftingDefinitionData(definitions[recipe.outputs.results[0].ItemName], recipe.outputs.results[0].amount);
        bool prepared = false, committed = false;
        if (matched)
        {
            prepared = CraftingTransaction.TryCreate(input, output, match, new[] { product }, false, out CraftingTransaction transaction, out _);
            if (prepared && transaction.Commit(out _)) { transaction.Complete(); committed = true; }
        }
        return new JObject { ["warnings"] = new JArray(warnings), ["matched"] = matched, ["prepared"] = prepared,
            ["success"] = committed, ["planks"] = input.Data.itemSlots[0].itemData?.Stack?.Amount ?? 0f,
            ["ropes"] = input.Data.itemSlots[1].itemData?.Stack?.Amount ?? 0f,
            ["outputId"] = output.Data.itemSlots[0].itemData?.IDName ?? "",
            ["outputAmount"] = output.Data.itemSlots[0].itemData?.Stack?.Amount ?? 0f,
            ["outputStillBlocked"] = ReferenceEquals(output.Data.itemSlots[0].itemData, blocker) };
    }

    private static JObject EntityWoodShieldReinforcement(JObject args)
    {
        RuntimeRecipe recipe = EntityWoodShieldRecipeDefinition(true, out List<string> warnings);
        ItemDefinitionDto definition = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().Single(value => value.Id == "Shield_Wood");
        ItemData template = EntityCraftingDefinitionData(definition, 1);
        template.Guid = 18001;
        Func<ItemData> factory = ItemInstanceDataFactory.Compile(template);
        ItemData shield = factory();
        CraftedDurabilityQuality.ApplyToCraftedOutput(shield, (float)Number(args, "quality", 1));
        shield.Durability = (float)Number(args, "durability", 40);
        shield.ItemSpecialData = "{\"probe\":\"shield-state\"}";
        shield.Tags.Add("CustomShieldTag");
        if (Flag(args, "reinforced", false)) shield.Tags.Add(recipe.outputs.results[0].AddTag);
        if (Flag(args, "adhesive", false)) shield.Tags.Add(CraftingInstanceOutput.ReinforcedTag);
        if (Flag(args, "stacked", false)) shield.Stack.Amount = 2;
        if (Flag(args, "otherShield", false)) shield.IDName = "probe:other-shield";
        float durabilityBefore = shield.Durability, maximumBefore = shield.MaxDurability;
        var nails = EntityProbeData("Nail", (float)Number(args, "nails", 6));
        float nailsBefore = nails.Stack.Amount;
        var input = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = shield }, new(1) { itemData = nails } }, "shield reinforcement input") };
        var blocker = EntityProbeData("probe:shield-blocker", 1);
        var output = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = Flag(args, "full", false) ? blocker : null } }, "shield reinforcement output") };
        var capabilities = new CraftingCapabilities { StationId = Flag(args, "workbench", false) ? "workbench" : "handcraft",
            InputSlotLimit = 2, ApplyOutputMultiplier = false };
        bool matched = CraftingRecipeMatcher.TryMatchRecipe(input, recipe, capabilities, out _);
        CraftingResult preview = CraftingService.PreviewRecipe(input, output, capabilities, recipe);
        bool previewUnchanged = shield.Durability == durabilityBefore && shield.MaxDurability == maximumBefore &&
            nails.Stack.Amount == nailsBefore && (Flag(args, "reinforced", false) || !shield.Tags.Contains(recipe.outputs.results[0].AddTag));
        CraftingResult result = CraftingService.CraftRecipe(input, output, capabilities, recipe);
        ItemData product = result.Success ? output.Data.itemSlots[0].itemData : null;
        ItemData restored = null;
        bool repeatRejected = false;
        if (product != null)
        {
            restored = factory();
            ItemInstanceSnapshot snapshot = ItemInstanceSnapshot.Capture(product);
            snapshot.RestoreTo(restored);
            snapshot.RestoreTo(restored);
            var repeatInput = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
            { new(0) { itemData = restored }, new(1) { itemData = EntityProbeData("Nail", 3) } }, "repeat shield reinforcement") };
            repeatRejected = !CraftingRecipeMatcher.TryMatchRecipe(repeatInput, recipe, capabilities, out _);
        }
        return new JObject { ["warnings"] = new JArray(warnings), ["matched"] = matched, ["preview"] = preview.Success,
            ["previewUnchanged"] = previewUnchanged, ["success"] = result.Success,
            ["shieldRemaining"] = input.Data.itemSlots[0].itemData?.Stack?.Amount ?? 0f,
            ["nailsRemaining"] = input.Data.itemSlots[1].itemData?.Stack?.Amount ?? 0f,
            ["outputAmount"] = product?.Stack?.Amount ?? 0f, ["outputId"] = product?.IDName ?? "",
            ["maximum"] = product?.MaxDurability ?? 0f, ["durability"] = product?.Durability ?? 0f,
            ["quality"] = product?.CraftedDurabilityMultiplier ?? 0f,
            ["identityPreserved"] = restored != null && restored.Guid == shield.Guid && restored.ItemSpecialData == shield.ItemSpecialData,
            ["tagsPreserved"] = restored != null && restored.Tags.Contains("CustomShieldTag") &&
                restored.Tags.Contains(recipe.outputs.results[0].AddTag) && (!Flag(args, "adhesive", false) || restored.Tags.Contains(CraftingInstanceOutput.ReinforcedTag)),
            ["restoredMaximum"] = restored?.MaxDurability ?? 0f, ["restoredDurability"] = restored?.Durability ?? 0f,
            ["repeatRejected"] = repeatRejected, ["outputStillBlocked"] = ReferenceEquals(output.Data.itemSlots[0].itemData, blocker) };
    }
    #endregion
}
