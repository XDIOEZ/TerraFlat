using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestRuntimeProbe
{
    #region 材料自身热转化采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterMaterialThermalRuntimeProbes()
    {
        Register("processing", "material_transition", CaptureMaterialTransition);
        Register("processing", "material_transition_atomic", CaptureMaterialTransitionAtomic);
        Register("processing", "material_reaction", CaptureMaterialReaction);
        Register("processing", "material_alloy_atomic", CaptureMaterialAlloyAtomic);
        Register("processing", "furnace_heat_only", (_, _) => MachineWorld.CaptureGameplayFurnaceHeatOnly());
    }

    // 夹具只提供材料初态，分组、产物、库存事务和温度继承全部调用正式入口。
    private static ItemData ThermalMaterial(string id, float amount, float temperature)
    {
        ItemData item = CreateDragItem(id, amount);
        item.MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = temperature };
        return item;
    }

    private static JArray ThermalSlots(Inventory inventory)
    {
        var slots = new JArray();
        foreach (ItemSlot slot in inventory.Data.itemSlots)
        {
            ItemData item = slot?.itemData;
            if (item == null) continue;
            slots.Add(new JObject { ["id"] = item.IDName, ["amount"] = item.Stack.Amount,
                ["temperature"] = item.MatterState?.TemperatureCelsius,
                ["initialized"] = item.MatterState?.Initialized == true });
        }
        return slots;
    }

    private static JObject CaptureMaterialTransition(Player _, JObject args)
    {
        string id = GameplayTestSystemProbe.Text(args, "id", "Log");
        float temperature = (float)GameplayTestSystemProbe.Number(args, "temperature", 500d);
        JArray amounts = args["amounts"] as JArray
            ?? new JArray((float)GameplayTestSystemProbe.Number(args, "amount", 1d));
        Inventory input = CreateDragInventory("lua.thermal.source", amounts.Select(value =>
            ThermalMaterial(id, value.Value<float>(), temperature)).ToArray());
        Inventory output = CreateDragInventory("lua.thermal.output", new ItemData[4]);
        MaterialThermalProcessor.Process(input, output);
        return new JObject { ["input"] = ThermalSlots(input), ["output"] = ThermalSlots(output) };
    }

    private static JObject CaptureMaterialTransitionAtomic(Player _, JObject args)
    {
        ItemData source = ThermalMaterial("Log", 4f, 500f);
        Inventory input = CreateDragInventory("lua.thermal.atomic.source", source);
        Inventory output = CreateDragInventory("lua.thermal.atomic.output", new ItemData[1]);
        CraftingResult blocked = ItemMatterRuntime.TransformSolidSlot(input, output, 0, "lua.thermal.atomic");
        var result = new JObject { ["blockedSuccess"] = blocked.Success,
            ["blockedInput"] = ThermalSlots(input), ["blockedOutput"] = ThermalSlots(output),
            ["sameSource"] = ReferenceEquals(source, input.Data.itemSlots[0].itemData) };
        output.Data.itemSlots.Add(new ItemSlot(1));
        CraftingResult resumed = ItemMatterRuntime.TransformSolidSlot(input, output, 0, "lua.thermal.atomic");
        result["resumedSuccess"] = resumed.Success;
        result["input"] = ThermalSlots(input);
        result["output"] = ThermalSlots(output);
        result["duplicateSuccess"] = ItemMatterRuntime.TransformSolidSlot(input, output, 0, "lua.thermal.atomic").Success;
        return result;
    }

    private static JObject CaptureMaterialReaction(Player _, JObject args)
    {
        float temperature = (float)GameplayTestSystemProbe.Number(args, "temperature", 1000d);
        float amount = (float)GameplayTestSystemProbe.Number(args, "amount", 2d);
        bool mixed = GameplayTestSystemProbe.Flag(args, "mixed", false);
        Inventory input = mixed
            ? CreateDragInventory("lua.thermal.reaction.source", ThermalMaterial("Ore_Copper", 2f, 999.99f),
                ThermalMaterial("Ore_Copper", 2f, temperature))
            : CreateDragInventory("lua.thermal.reaction.source", ThermalMaterial("Ore_Copper", amount, temperature));
        Inventory output = CreateDragInventory("lua.thermal.reaction.output", new ItemData[2]);
        MaterialThermalProcessor.Process(input, output);
        return new JObject { ["input"] = ThermalSlots(input), ["output"] = ThermalSlots(output) };
    }

    private static JObject CaptureMaterialAlloyAtomic(Player _, JObject args)
    {
        Inventory input = CreateDragInventory("lua.thermal.alloy.source",
            ThermalMaterial("Ore_Copper", 9f, 1000f), ThermalMaterial("Ore_Tin", 1f, 1000f));
        Inventory output = CreateDragInventory("lua.thermal.alloy.output", ThermalMaterial("Ingot_Copper", 1f, 1000f));
        MaterialThermalProcessor.Process(input, output);
        var result = new JObject { ["blockedInput"] = ThermalSlots(input), ["blockedOutput"] = ThermalSlots(output) };
        output.Data.SetOne_ItemData(0, null);
        MaterialThermalProcessor.Process(input, output);
        result["input"] = ThermalSlots(input);
        result["output"] = ThermalSlots(output);
        return result;
    }
    #endregion
}

public static partial class MachineWorld
{
    #region 全部炉型供热入口采样
    // 遍历正式炉体目录，独立机器世界只设置热初态，不伪造材料反应结果。
    internal static JObject CaptureGameplayFurnaceHeatOnly()
    {
        var rows = new JArray();
        string[] ids = GameRes.ExistingInstance.ItemDefinitions.Keys
            .Where(id => MachineCatalog.Get(id)?.Content?.Find<Mod_Furnace>() != null)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        foreach (string id in ids)
        {
            using var fixture = new GameplayFluidScope();
            MachineEntity node = fixture.Add(id);
            FurnaceLogic furnace = node.Logic as FurnaceLogic
                ?? throw new InvalidOperationException("正式炉体缺少供热领域：" + id);
            void Material(Inventory inventory, string itemId, float amount, float temperature)
            {
                ItemData item = GameRes.ExistingInstance.CreateItemData(itemId);
                item.Stack.Amount = amount;
                item.MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = temperature };
                inventory.Data.SetOne_ItemData(0, item);
            }
            float Amount(Inventory inventory, string itemId) => inventory.Data.itemSlots
                .Where(slot => slot?.itemData?.IDName == itemId).Sum(slot => slot.itemData.Stack.Amount);
            furnace.Data.Temperature = furnace.Data.MaxTemperature = furnace.Data.MaxTemperatureLimit = 500f;
            furnace.Data.TemperatureUpSpeed = 0f;
            furnace.Fuel.Fuel = new Vector2(100f, 100f);
            furnace.Data.IsSmelting = true;
            Material(furnace.Input, "Log", 3f, 500f);
            furnace.HeatMaterials(1f);
            var row = new JObject { ["id"] = id, ["processorAbsent"] = node.Processor == null,
                ["woodLeft"] = Amount(furnace.Input, "Log"), ["charcoal"] = Amount(furnace.Output, "Charcoal") };
            foreach (ItemSlot slot in furnace.Input.Data.itemSlots) slot.itemData = null;
            foreach (ItemSlot slot in furnace.Output.Data.itemSlots) slot.itemData = null;
            furnace.Data.Temperature = furnace.Data.MaxTemperature = furnace.Data.MaxTemperatureLimit = 1000f;
            Material(furnace.Input, "Log", 3f, 100f);
            Material(furnace.Output, "Charcoal", 1f, 1000f);
            furnace.HeatMaterials(1f);
            row["coldWoodLeft"] = Amount(furnace.Input, "Log");
            row["coldWoodTemperature"] = furnace.Input.Data.itemSlots[0].itemData?.MatterState?.TemperatureCelsius;
            row["hotCharcoalLeft"] = Amount(furnace.Output, "Charcoal");
            row["ash"] = Amount(furnace.Output, "WoodAsh");
            rows.Add(row);
        }
        return new JObject { ["furnaces"] = rows };
    }
    #endregion
}
