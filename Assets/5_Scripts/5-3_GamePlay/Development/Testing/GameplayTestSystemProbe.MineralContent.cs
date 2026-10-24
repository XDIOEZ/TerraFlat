using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.WorldModel;
using MemoryPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 矿物内容与通用事务采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterMineralContentRules()
    {
        Register("processing", "mineral_content", ProbeMineralContent);
        Register("processing", "mineral_reaction", ProbeMineralReaction);
        Register("processing", "induction_temperature", ProbeInductionTemperature);
    }

    private static Dictionary<string, ItemDefinitionDto> MineralContentDefinitions()
        => ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().Where(value => !value.Abstract)
            .ToDictionary(value => value.Id, StringComparer.OrdinalIgnoreCase);

    private static JObject ProbeInductionTemperature(JObject args)
    {
        TextAsset asset = Resources.Load<TextAsset>("Config/Mechanical/mechanical-catalog");
        MachineDefinition definition = JsonConvert.DeserializeObject<MachineCatalogDocument>(asset.text)
            .Nodes.First(value => value.Id == "InductionStove");
        definition.Induction.Validate(definition.Id, definition.Electrical);
        float target = (float)Number(args, "target", definition.Induction.DefaultTargetTemperature);
        return new JObject { ["minimum"] = definition.Induction.MinimumTargetTemperature,
            ["maximum"] = definition.Induction.MaxTemperature, ["default"] = definition.Induction.DefaultTargetTemperature,
            ["maximumWatts"] = definition.Electrical.PowerWatts,
            ["requestedWatts"] = definition.Electrical.PowerWatts * definition.Induction.GetPowerDemandFactor(target),
            ["clamped"] = definition.Induction.ClampTargetTemperature(target),
            ["boundarySeconds"] = ThermalRuntime.GetAmbientTransferSeconds(20d, 3500d, 3422d, 1.5d) };
    }

    // 读取正式展开目录和 Profile 引用，不启动 GameRes 或创建测试世界。
    private static JObject ProbeMineralContent(JObject args)
    {
        Dictionary<string, ItemDefinitionDto> definitions = MineralContentDefinitions();
        string mineId = Text(args, "mine", "Mine_Tungsten");
        string oreId = Text(args, "ore", "Ore_Tungsten");
        string ingotId = Text(args, "ingot", "Ingot_Tungsten");
        ItemDefinitionDto mine = definitions[mineId];
        RuntimeLootTable loot = LootTableCatalogLoader.LoadBuiltIn().First(value => value.Id == mine.LootTableId);
        NaturalGenerationRuleCatalog generation = NaturalGenerationRuleCatalogLoader.LoadBuiltIn();
        var cave = Resources.Load<ChunkGenerationProfileSO>("Config/WorldModel/ChunkGenerationProfile_Cave");
        var surface = Resources.Load<ChunkGenerationProfileSO>("Config/WorldModel/ChunkGenerationProfile_Surface");
        if (cave == null || surface == null) throw new InvalidOperationException("矿物采样缺少正式地形 Profile。");
        var caveRules = new JArray();
        foreach (string ruleId in cave.CaveResourceRuleIds)
        {
            CaveResourceRuleSnapshot rule = generation.GetCaveResourceRule(ruleId);
            caveRules.Add(new JObject { ["id"] = rule.RuleId, ["itemId"] = rule.ItemId,
                ["threshold"] = rule.VeinThreshold, ["scale"] = rule.VeinScale });
        }
        return new JObject
        {
            ["mine"] = MineralDefinitionSample(mine), ["iron"] = MineralDefinitionSample(definitions["Mine_Iron"]),
            ["ore"] = MineralDefinitionSample(definitions[oreId]), ["ingot"] = MineralDefinitionSample(definitions[ingotId]),
            ["loot"] = loot.CreateDamageReceiverEntries(), ["caveRules"] = caveRules,
            ["surfaceCount"] = surface.EcologyRuleIds.Count(id => generation.GetEcologyRule(id).ItemId == mineId) +
                surface.CaveResourceRuleIds.Count(id => generation.GetCaveResourceRule(id).ItemId == mineId),
            ["oreState"] = MineralPortableState(definitions[oreId]),
            ["ingotState"] = MineralPortableState(definitions[ingotId])
        };
    }

    private static JObject MineralDefinitionSample(ItemDefinitionDto definition)
        => new JObject { ["id"] = definition.Id, ["gameName"] = definition.GameName,
            ["description"] = definition.Description, ["stackable"] = definition.Stackable,
            ["canBePickedUp"] = definition.CanBePickedUp, ["weight"] = definition.Weight,
            ["volume"] = definition.Volume, ["entityRuntime"] = definition.EntityRuntime,
            ["visual"] = new JObject { ["spriteAddress"] = definition.Visual?.SpriteAddress },
            ["modules"] = JObject.FromObject(definition.Modules) };

    private static JObject MineralPortableState(ItemDefinitionDto definition)
    {
        Data_GeneralItem first = EntityCraftingDefinitionData(definition, 2);
        Data_GeneralItem second = EntityCraftingDefinitionData(definition, 3);
        first.Guid = 9681;
        second.Guid = 9682;
        first.Stack.CanBePickedUp = definition.CanBePickedUp ?? true;
        first.MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 1800f };
        second.MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 1800f };
        var inventory = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = first } }, "mineral-stack");
        bool added = inventory.TryAddItem(second, true, out float addedAmount);
        byte[] saved = ItemSnapshotSerialization.SerializePayload(ItemInstanceSnapshot.Capture(first));
        ItemInstanceSnapshot snapshot = MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(saved);
        Data_GeneralItem restored = EntityCraftingDefinitionData(definition, 1);
        snapshot.RestoreTo(restored);
        bool serialized = ItemNetworkStateSerialization.TrySerializeItemData(restored, out byte[] payload);
        bool identified = ItemNetworkStateSerialization.TryReadIdentity(payload, out int guid, out string id);
        return new JObject
        {
            ["added"] = added, ["addedAmount"] = addedAmount, ["amount"] = first.Stack.Amount,
            ["savedId"] = snapshot.DefinitionId, ["savedAmount"] = restored.Stack.Amount,
            ["savedTemperature"] = restored.MatterState.TemperatureCelsius,
            ["serialized"] = serialized, ["identified"] = identified, ["networkId"] = id, ["networkGuid"] = guid
        };
    }

    // 仅组装独立库存，温度匹配、扣料、满槽拒绝和提交都调用生产接口。
    private static JObject ProbeMineralReaction(JObject args)
    {
        Dictionary<string, ItemDefinitionDto> definitions = MineralContentDefinitions();
        string oreId = Text(args, "ore", "Ore_Tungsten");
        RuntimeItemReactionDefinition reaction = ItemMatterReactionCompiler.CompileReactions(
            definitions[oreId].Reactions, oreId).First(value => value.Id == Text(args, "reaction", "core:tungsten_ore_to_ingot"));
        var slots = new List<ItemSlot>();
        foreach (RuntimeRecipeIngredient ingredient in reaction.Recipe.inputs.RowItems_List)
        {
            float amount = (float)Number(args["amounts"] as JObject, ingredient.ItemName, ingredient.amount);
            ItemData data = amount <= 0f ? null : EntityCraftingDefinitionData(definitions[ingredient.ItemName], amount);
            if (data != null)
                data.MatterState = new ItemMatterState { Initialized = true,
                    TemperatureCelsius = (float)Number(args["temperatures"] as JObject, ingredient.ItemName,
                        Number(args, "temperature", 20)) };
            slots.Add(new ItemSlot(slots.Count) { itemData = data });
        }
        var input = new Inventory { Data = new Inventory_Data(slots, "mineral-reaction-input") };
        var output = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = Flag(args, "full", false) ? EntityProbeData("mineral:blocker", 1) : null } }, "mineral-reaction-output") };
        JArray before = EntityCraftingInventorySnapshot(input.Data);
        JArray beforeOutput = EntityCraftingInventorySnapshot(output.Data);
        var products = new List<ItemData>();
        foreach (RuntimeRecipeResult result in reaction.Recipe.outputs.results)
            products.Add(EntityCraftingDefinitionData(definitions[result.ItemName], result.amount));
        bool matched = ItemReactionResolver.TryMatch(input, reaction, out CraftingRecipeMatch match);
        bool committed = false;
        if (matched && CraftingTransaction.TryCreate(input, output, match, products, false,
                out CraftingTransaction transaction, out _) && transaction.Commit(out _))
        { transaction.Complete(); committed = true; }
        return new JObject
        {
            ["id"] = reaction.Id, ["minimum"] = reaction.MinTemperature, ["maximum"] = reaction.MaxTemperature,
            ["matched"] = matched, ["committed"] = committed,
            ["unchanged"] = JToken.DeepEquals(before, EntityCraftingInventorySnapshot(input.Data)) &&
                JToken.DeepEquals(beforeOutput, EntityCraftingInventorySnapshot(output.Data)),
            ["input"] = EntityCraftingInventorySnapshot(input.Data),
            ["output"] = EntityCraftingInventorySnapshot(output.Data)
        };
    }
    #endregion
}
