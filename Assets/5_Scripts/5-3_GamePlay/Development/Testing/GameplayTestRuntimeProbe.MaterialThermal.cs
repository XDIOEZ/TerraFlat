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
        Register("processing", "meat_heat_and_dry", CaptureMeatHeatAndDry);
        Register("processing", "material_reaction", CaptureMaterialReaction);
        Register("processing", "material_alloy_atomic", CaptureMaterialAlloyAtomic);
        Register("processing", "material_heating_coefficient", CaptureMaterialHeatingCoefficient);
        Register("processing", "material_heating_window", CaptureMaterialHeatingWindow);
        Register("processing", "furnace_heat_only", (_, _) => MachineWorld.CaptureGameplayFurnaceHeatOnly());
        Register("processing", "induction_power", (_, args) => MachineWorld.CaptureGameplayInductionPower(args));
        Register("processing", "induction_grid", (_, args) => MachineWorld.CaptureGameplayInductionGrid(args));
        Register("processing", "induction_target", (_, _) => MachineWorld.CaptureGameplayInductionTarget());
        Register("processing", "furnace_input_capacity", (_, args) => MachineWorld.CaptureGameplayFurnaceInputCapacity(args));
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

    // 同一正式供热入口分别接受长步和小步，并在满槽后从真实快照恢复材料重试。
    private static JObject CaptureMaterialHeatingWindow(Player _, JObject args)
    {
        var materials = args["materials"] as JArray ?? new JArray(new JObject
            { ["id"] = "Ore_Tungsten", ["amount"] = 2, ["temperature"] = 20 });
        Inventory input = CreateDragInventory("lua.thermal.window.input", materials.Select(value =>
            ThermalMaterial(value.Value<string>("id"), value.Value<float>("amount"),
                value.Value<float?>("temperature") ?? 20f)).ToArray());
        bool full = GameplayTestSystemProbe.Flag(args, "full", false);
        Inventory output = CreateDragInventory("lua.thermal.window.output", full
            ? new[] { ThermalMaterial("StoneSlab", 1f, 20f) } : new ItemData[4]);
        float seconds = (float)GameplayTestSystemProbe.Number(args, "seconds", 400d);
        int segments = Math.Max(1, (int)GameplayTestSystemProbe.Number(args, "segments", 1d));
        for (int index = 0; index < segments; index++)
            FurnaceMaterialHeating.Process(input, output, 3500f, seconds / segments, 1.5f);
        var result = new JObject { ["input"] = ThermalSlots(input), ["output"] = ThermalSlots(output) };
        if (!full) return result;
        foreach (ItemSlot slot in input.Data.itemSlots)
        {
            if (slot.itemData == null) continue;
            ItemInstanceSnapshot snapshot = MemoryPack.MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(
                ItemSnapshotSerialization.SerializePayload(ItemInstanceSnapshot.Capture(slot.itemData)));
            ItemData restored = GameRes.ExistingInstance.CreateItemData(snapshot.DefinitionId);
            snapshot.RestoreTo(restored);
            slot.itemData = restored;
        }
        foreach (ItemSlot slot in output.Data.itemSlots) slot.itemData = null;
        FurnaceMaterialHeating.Process(input, output, 3500f, .1f, 1.5f);
        result["resumedInput"] = ThermalSlots(input);
        result["resumedOutput"] = ThermalSlots(output);
        return result;
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

    // 验证地面散物的热转化与晾架的蒸发走同一份生肉物质定义。
    private static JObject CaptureMeatHeatAndDry(Player player, JObject args)
    {
        ItemData heated = CreateDragItem("Meat", 2f);
        ItemMatterRuntime.EnsureInitialized(heated, 20f);
        string heatedOutput = "";
        float heatedAmount = 0f;
        int heatedSeconds = 0;
        for (int second = 1; second <= 180; second++)
        {
            WorldItemMatterRules.Advance(heated, 800f, 1f, null, out _);
            if (!ItemMatterRuntime.TryCreateSolidTransitionOutputs(heated, out var products)) continue;
            heatedOutput = products[0].IDName;
            heatedAmount = products[0].Stack.Amount;
            heatedSeconds = second;
            break;
        }

        ItemData drying = CreateDragItem("Meat", 2f);
        ItemMatterRuntime.EnsureInitialized(drying, 20f);
        string driedOutput = "";
        float driedAmount = 0f;
        int driedSeconds = 0;
        for (int second = 1; second <= 240; second++)
        {
            ItemMatterRuntime.Advance(drying, 20f, 4f, 1f);
            if (!ItemMatterRuntime.TryCreateSolidTransitionOutputs(drying, out var products)) continue;
            driedOutput = products[0].IDName;
            driedAmount = products[0].Stack.Amount;
            driedSeconds = second;
            break;
        }

        ItemData boundary = CreateDragItem("Meat", 1f);
        ItemMatterRuntime.EnsureInitialized(boundary, 20f);
        boundary.MatterState.Moisture = .2f;
        boundary.MatterState.TemperatureCelsius = 199.99f;
        string lowTemperature = ItemMatterRuntime.GetMatchedTransition(boundary, false)?.OutputItemId ?? "";
        boundary.MatterState.TemperatureCelsius = 200f;
        string cookingBoundary = ItemMatterRuntime.GetMatchedTransition(boundary, false)?.OutputItemId ?? "";
        boundary.MatterState.TemperatureCelsius = 500f;
        string veryHot = ItemMatterRuntime.GetMatchedTransition(boundary, false)?.OutputItemId ?? "";
        boundary.MatterState.TemperatureCelsius = 20f;
        boundary.MatterState.Moisture = .8f;
        bool freshUnchanged = ItemMatterRuntime.GetMatchedTransition(boundary, false) == null;

        Inventory furnaceInput = CreateDragInventory("lua.meat.furnace.input",
            ThermalMaterial("Meat", 1f, 200f));
        Inventory furnaceOutput = CreateDragInventory("lua.meat.furnace.output", new ItemData[1]);
        MaterialThermalProcessor.Process(furnaceInput, furnaceOutput);
        return new JObject
        {
            ["heatedOutput"] = heatedOutput, ["heatedAmount"] = heatedAmount,
            ["heatedSeconds"] = heatedSeconds, ["heatedTemperature"] = heated.MatterState.TemperatureCelsius,
            ["driedOutput"] = driedOutput, ["driedAmount"] = driedAmount,
            ["driedSeconds"] = driedSeconds, ["driedTemperature"] = drying.MatterState.TemperatureCelsius,
            ["lowTemperature"] = lowTemperature, ["cookingBoundary"] = cookingBoundary,
            ["veryHot"] = veryHot, ["freshUnchanged"] = freshUnchanged,
            ["furnaceInput"] = ThermalSlots(furnaceInput), ["furnaceOutput"] = ThermalSlots(furnaceOutput)
        };
    }

    private static JObject CaptureMaterialReaction(Player _, JObject args)
    {
        float temperature = (float)GameplayTestSystemProbe.Number(args, "temperature", 1000d);
        float amount = (float)GameplayTestSystemProbe.Number(args, "amount", 2d);
        bool mixed = GameplayTestSystemProbe.Flag(args, "mixed", false);
        Inventory input;
        // Lua 提供各堆材料初态，反应选择和扣料仍由正式热加工入口处理。
        if (args["materials"] is JArray materials)
            input = CreateDragInventory("lua.thermal.reaction.source", materials.OfType<JObject>().Select(material =>
                ThermalMaterial(GameplayTestSystemProbe.Text(material, "id", "Ore_Copper"),
                    (float)GameplayTestSystemProbe.Number(material, "amount", amount),
                    (float)GameplayTestSystemProbe.Number(material, "temperature", temperature))).ToArray());
        else
            input = mixed
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

    // 温度倍率通过共享供热服务传递，蒸发和液体加工计时保留真实的一秒。
    private static JObject CaptureMaterialHeatingCoefficient(Player player, JObject args)
    {
        float multiplier = (float)GameplayTestSystemProbe.Number(args, "multiplier", 1d);
        Inventory source = CreateDragInventory("lua.coefficient.input", ThermalMaterial("Log", 3f, 100f));
        Inventory output = CreateDragInventory("lua.coefficient.output", ThermalMaterial("Log", 3f, 100f));
        FurnaceMaterialHeating.Process(source, output, 1000f, 1f, multiplier);

        ItemData fresh = CreateDragItem("Log", 3f);
        fresh.MatterState = null;
        Inventory freshSource = CreateDragInventory("lua.coefficient.uninitialized", fresh);
        Inventory freshOutput = CreateDragInventory("lua.coefficient.uninitialized.output", new ItemData[2]);
        FurnaceMaterialHeating.Process(freshSource, freshOutput, 1000f, 1f, multiplier,
            initialMaterialTemperature: 35f);

        ItemData Vessel(string id, float liquidTemperature, string liquidId)
        {
            ItemData item = ThermalMaterial(id, 1f, 100f);
            if (!Mod_WaterVessel.TryRead(item, out var storage, out _))
                throw new InvalidOperationException("倍率测试缺少容器模块：" + id);
            var state = new LiquidContainerState();
            MixedLiquidContents.Add(state, liquidId, 1f, liquidTemperature);
            storage.WriteData(state);
            return item;
        }
        string water = FluidCatalog.Default.Find(FluidIds.PureWater).LiquidId;
        ItemData bucket = Vessel("IronBucket", 20f, water);
        Inventory bucketInventory = CreateDragInventory("lua.coefficient.bucket", bucket);
        FurnaceMaterialHeating.Process(bucketInventory, bucketInventory, 1000f, 1f, multiplier);
        Mod_WaterVessel.TryRead(bucketInventory.Data.itemSlots[0].itemData, out _, out var bucketState);

        ItemData crucible = Vessel("Crucible", 20f, water);
        ItemData freshBowlMaterial = CreateDragItem("Log", 3f);
        freshBowlMaterial.MatterState = null;
        var mortarStorage = (Ex_ModData_MemoryPackable)crucible.ModuleDataDic["mortar"];
        var mortar = new MortarState
        {
            Bowl = new Inventory_Data(new System.Collections.Generic.List<ItemSlot>
                { new(0) { itemData = ThermalMaterial("Log", 3f, 100f) },
                    new(1) { itemData = freshBowlMaterial } }, "lua.coefficient.crucible.bowl")
        };
        mortarStorage.WriteData(mortar);
        Inventory crucibleInventory = CreateDragInventory("lua.coefficient.crucible", crucible);
        FurnaceMaterialHeating.Process(crucibleInventory, crucibleInventory, 1000f, 1f, multiplier,
            initialMaterialTemperature: 35f);
        ItemData finalCrucible = crucibleInventory.Data.itemSlots[0].itemData;
        var finalMortar = new MortarState();
        ((Ex_ModData_MemoryPackable)finalCrucible.ModuleDataDic["mortar"]).ReadData(ref finalMortar);
        Mod_WaterVessel.TryRead(finalCrucible, out _, out var crucibleState);

        ItemData timerVessel = Vessel("IronBucket", 100f, "core:sea_water");
        Inventory timerInventory = CreateDragInventory("lua.coefficient.timer", timerVessel);
        InventoryVesselHeating.ProcessHeat(timerInventory, timerInventory, 100f, 1f, multiplier);
        Mod_WaterVessel.TryRead(timerInventory.Data.itemSlots[0].itemData, out _, out var timerState);

        ItemData leaf = ThermalMaterial("Leaf", 1f, 20f);
        leaf.MatterState.Moisture = .8f;
        ItemMatterRuntime.Advance(leaf, 20f, 1f, 1f, heatingRateMultiplier: multiplier);
        return new JObject
        {
            ["input"] = source.Data.itemSlots[0].itemData?.MatterState?.TemperatureCelsius,
            ["output"] = output.Data.itemSlots[0].itemData?.MatterState?.TemperatureCelsius,
            ["freshTemperature"] = freshSource.Data.itemSlots[0].itemData?.MatterState?.TemperatureCelsius,
            ["freshAmount"] = freshSource.Data.itemSlots[0].itemData?.Stack?.Amount,
            ["freshOutputCount"] = ThermalSlots(freshOutput).Count,
            ["bucket"] = bucketState.Temperature, ["crucibleLiquid"] = crucibleState.Temperature,
            ["crucibleMaterial"] = finalMortar.Bowl.itemSlots[0].itemData?.MatterState?.TemperatureCelsius,
            ["freshCrucibleMaterial"] = finalMortar.Bowl.itemSlots[1].itemData?.MatterState?.TemperatureCelsius,
            ["liquidProcessingSeconds"] = timerState.ProcessingSeconds,
            ["leafMoisture"] = leaf.MatterState.Moisture
        };
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
            float configuredSpeed = furnace.Data.TemperatureUpSpeed;
            JObject panel = CaptureThermalPanelBindings(node, furnace.Input, furnace.Output);
            furnace.Data.Temperature = furnace.Data.MaxTemperature = furnace.Data.MaxTemperatureLimit = 500f;
            furnace.Data.TemperatureUpSpeed = 0f;
            furnace.Fuel.Fuel = new Vector2(100f, 100f);
            furnace.Data.IsSmelting = true;
            Material(furnace.Input, "Log", 3f, 500f);
            furnace.HeatMaterials(1f);
            var row = new JObject { ["id"] = id, ["processorAbsent"] = node.Processor == null,
                ["woodLeft"] = Amount(furnace.Input, "Log"), ["charcoal"] = Amount(furnace.Output, "Charcoal"),
                ["heatingRatePercent"] = furnace.HeatingRatePercent, ["temperatureUpSpeed"] = configuredSpeed,
                ["panel"] = panel, ["inputSlots"] = furnace.Input.Data.itemSlots.Count,
                ["outputSlots"] = furnace.Output.Data.itemSlots.Count };
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

    private static JObject CaptureThermalPanelBindings(MachineEntity node, Inventory input, Inventory output)
    {
        using IMachinePanelSession session = MachinePanelSession.Create(node);
        BasePanel panel = input.itemSlot_UI.First().GetComponentInParent<BasePanel>(true)
            ?? throw new InvalidOperationException("正式加热面板没有库存根节点。");
        bool Bound(Inventory inventory) => inventory.itemSlot_UI.Count == inventory.Data.itemSlots.Count &&
            inventory.itemSlot_UI.Select((ui, index) => ui.slotIndex == index &&
                ReferenceEquals(ui.GetBoundSlotData(), inventory.Data.itemSlots[index])).All(value => value);
        int Visible(string prefix) => panel.GetComponentsInChildren<ItemSlot_UI>(true).Count(slot =>
            (slot.name.StartsWith(prefix + "_", StringComparison.Ordinal) ||
             slot.name.StartsWith(prefix + " ", StringComparison.Ordinal)) && slot.gameObject.activeSelf);
        return new JObject { ["inputBound"] = Bound(input), ["outputBound"] = Bound(output),
            ["visibleInputs"] = Visible("输入"), ["visibleOutputs"] = Visible("输出"),
            ["visibleFuelSlots"] = Visible("燃料"),
            ["ignitionVisible"] = panel.GetButton("合成按钮")?.gameObject.activeSelf == true };
    }

    internal static JObject CaptureGameplayInductionPower(JObject args)
    {
        using var fixture = new GameplayFluidScope();
        MachineEntity node = fixture.Add("InductionStove");
        var stove = node.Logic as InductionStoveLogic
            ?? throw new InvalidOperationException("电磁炉领域未建立。");
        float offered = (float)GameplayTestSystemProbe.Number(args, "watts", 1200d);
        float target = (float)GameplayTestSystemProbe.Number(args, "target", 2000d);
        if (!stove.Execute(InductionStoveLogic.SetTargetTemperatureOperation,
                target.ToString(System.Globalization.CultureInfo.InvariantCulture), null))
            throw new InvalidOperationException("电磁炉测试目标温度被正式命令拒绝。");
        JObject panel = CaptureThermalPanelBindings(node, stove.Input, stove.Output);
        ItemData wood = GameRes.ExistingInstance.CreateItemData("Log");
        wood.Stack.Amount = 3f;
        wood.MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 0f };
        stove.Input.Data.SetOne_ItemData(2, wood);
        float before = stove.Temperature;
        node.ElectricalSuppliedWatts = offered;
        stove.Tick(0f);
        float zero = stove.Temperature;
        stove.Tick(1f);
        var result = new JObject
        {
            ["before"] = before, ["zero"] = zero, ["after"] = stove.Temperature,
            ["supplied"] = stove.SuppliedWatts, ["material"] = stove.Input.Data.itemSlots[2].itemData?.MatterState?.TemperatureCelsius,
            ["inputSlots"] = stove.Input.Data.itemSlots.Count, ["outputSlots"] = stove.Output.Data.itemSlots.Count,
            ["inventoryCount"] = stove.Inventories.Count, ["processorAbsent"] = node.Processor == null,
            ["heatingRatePercent"] = stove.HeatingRatePercent,
            ["voltage"] = node.Definition.Electrical.NominalVoltage, ["ratedWatts"] = node.Definition.Electrical.PowerWatts,
            ["maximum"] = node.Definition.Induction.MaxTemperature,
            ["target"] = stove.TargetTemperature, ["requestedWatts"] = stove.RequestedWatts,
            ["temperatureUpSpeed"] = node.Definition.Induction.TemperatureUpSpeed, ["panel"] = panel
        };
        stove.Input.Data.SetOne_ItemData(2, null);
        node.ElectricalSuppliedWatts = stove.RequestedWatts;
        stove.Tick(1000f);
        result["longStepMaximum"] = stove.Temperature;
        node.ElectricalSuppliedWatts = 0f;
        stove.Tick(1f);
        result["coasting"] = stove.Temperature;
        return result;
    }

    // 正式电池和电线经过生产电网求解器向电磁炉供电，不直接写入供电结果。
    internal static JObject CaptureGameplayInductionGrid(JObject args)
    {
        using var fixture = new GameplayFluidScope();
        MachineEntity stoveNode = fixture.Add("InductionStove", 0);
        var stove = (InductionStoveLogic)stoveNode.Logic;
        float target = (float)GameplayTestSystemProbe.Number(args, "target", 2000d);
        stove.Execute(InductionStoveLogic.SetTargetTemperatureOperation,
            target.ToString(System.Globalization.CultureInfo.InvariantCulture), null);
        MachineEntity left = fixture.Add("Battery_Basic", -1);
        MachineEntity right = fixture.Add("Battery_Basic", 1);
        MachineEntity extraLeft = fixture.Add("Battery_Basic", -2);
        MachineEntity extraRight = fixture.Add("Battery_Basic", 2);
        fixture.Add("Wire_Copper", -2);
        fixture.Add("Wire_Copper", -1);
        fixture.Add("Wire_Copper", 0);
        fixture.Add("Wire_Copper", 1);
        fixture.Add("Wire_Copper", 2);
        left.ElectricalStoredJoules = right.ElectricalStoredJoules =
            extraLeft.ElectricalStoredJoules = extraRight.ElectricalStoredJoules = 60000f;
        fixture.Rebuild();
        float Energy() => left.ElectricalStoredJoules + right.ElectricalStoredJoules +
            extraLeft.ElectricalStoredJoules + extraRight.ElectricalStoredJoules;
        float beforeEnergy = Energy();
        SolveElectricalNetworks(1f);
        float supplied = stoveNode.ElectricalSuppliedWatts;
        float consumed = beforeEnergy - Energy();
        stove.Tick(1f);
        float poweredTemperature = stove.Temperature;
        left.ElectricalStoredJoules = right.ElectricalStoredJoules =
            extraLeft.ElectricalStoredJoules = extraRight.ElectricalStoredJoules = 0f;
        SolveElectricalNetworks(1f);
        stove.Tick(1f);
        return new JObject { ["sameNetwork"] = ReferenceEquals(left.ElectricalNetwork, stoveNode.ElectricalNetwork)
                && ReferenceEquals(right.ElectricalNetwork, stoveNode.ElectricalNetwork)
                && ReferenceEquals(extraLeft.ElectricalNetwork, stoveNode.ElectricalNetwork)
                && ReferenceEquals(extraRight.ElectricalNetwork, stoveNode.ElectricalNetwork),
            ["target"] = stove.TargetTemperature, ["requestedWatts"] = stove.RequestedWatts,
            ["poweredWatts"] = supplied, ["batteryConsumedJoules"] = consumed,
            ["poweredTemperature"] = poweredTemperature, ["emptyWatts"] = stoveNode.ElectricalSuppliedWatts,
            ["coastingTemperature"] = stove.Temperature };
    }

    internal static JObject CaptureGameplayInductionTarget()
    {
        using var fixture = new GameplayFluidScope();
        MachineEntity node = fixture.Add("InductionStove");
        var stove = (InductionStoveLogic)node.Logic;
        float defaultTarget = stove.TargetTemperature;
        bool Set(string value) => stove.Execute(InductionStoveLogic.SetTargetTemperatureOperation, value, null);
        bool minimumAccepted = Set("20"), maximumAccepted = Set("3500");
        var invalid = new JArray();
        foreach (string value in new[] { "19.9", "3500.1", "NaN", "Infinity", "-Infinity", "abc" })
        {
            bool accepted = Set(value);
            invalid.Add(new JObject { ["value"] = value, ["accepted"] = accepted, ["target"] = stove.TargetTemperature });
        }
        var receipts = new JArray();
        void Receive(MachineOperationReceipt receipt)
        {
            if (receipt.EntityId == node.Id)
                receipts.Add(new JObject { ["token"] = receipt.Token, ["entityId"] = receipt.EntityId,
                    ["operation"] = receipt.Operation, ["argument"] = receipt.Argument,
                    ["confirmed"] = receipt.Confirmed, ["accepted"] = receipt.Accepted, ["reason"] = receipt.Reason });
        }
        uint acceptedToken, rejectedToken;
        MachineWorld.OperationCompleted += Receive;
        try
        {
            MachineWorld.RequestOperation(node, InductionStoveLogic.SetTargetTemperatureOperation, "1500", null, out acceptedToken);
            MachineWorld.RequestOperation(node, InductionStoveLogic.SetTargetTemperatureOperation, "NaN", null, out rejectedToken);
        }
        finally { MachineWorld.OperationCompleted -= Receive; }
        Set("3500");
        node.ElectricalSuppliedWatts = 2100f;
        stove.Tick(1000f);
        float hot = stove.Temperature;
        bool lowered = Set("1000");
        float afterCommand = stove.Temperature;
        stove.Tick(1f);
        float cooling = stove.Temperature;
        stove.Capture();
        using var restored = new InductionStoveLogic(node);
        ItemData saved = ItemInstanceDataFactory.CloneRuntime(node.Snapshot);
        Set("2000");
        bool remoteApplied = stove.ApplyRemoteSnapshot(saved);
        node.ElectricalSuppliedWatts = 0f;
        stove.Tick(1f);
        return new JObject { ["default"] = defaultTarget, ["minimumAccepted"] = minimumAccepted,
            ["maximumAccepted"] = maximumAccepted, ["invalid"] = invalid,
            ["entityId"] = node.Id, ["receipts"] = receipts,
            ["acceptedToken"] = acceptedToken, ["rejectedToken"] = rejectedToken,
            ["lowered"] = lowered, ["hot"] = hot, ["afterCommand"] = afterCommand,
            ["cooling"] = cooling, ["savedTarget"] = restored.TargetTemperature,
            ["remoteApplied"] = remoteApplied, ["remoteTarget"] = stove.TargetTemperature,
            ["disconnected"] = stove.Temperature, ["requestedWatts"] = stove.RequestedWatts };
    }

    // 容量测试使用真实物品转移和制作事务，跨槽总量仍由库存生产规则决定。
    internal static JObject CaptureGameplayFurnaceInputCapacity(JObject args)
    {
        string id = GameplayTestSystemProbe.Text(args, "id", "Smelter");
        string itemId = GameplayTestSystemProbe.Text(args, "itemId", "Ore_Gold");
        using var fixture = new GameplayFluidScope();
        MachineEntity node = fixture.Add(id);
        Inventory input = node.Logic is FurnaceLogic furnace ? furnace.Input : ((InductionStoveLogic)node.Logic).Input;
        Inventory output = node.Logic is FurnaceLogic fuelFurnace ? fuelFurnace.Output : ((InductionStoveLogic)node.Logic).Output;
        ItemData material = GameRes.ExistingInstance.CreateItemData(itemId);
        material.Stack.Amount = 500f;
        var source = new Inventory { Data = new Inventory_Data(new System.Collections.Generic.List<ItemSlot>
            { new(0) { itemData = material } }, "lua.furnace.capacity.source") };
        float Total() => input.Data.itemSlots.Sum(slot => slot?.itemData?.Stack?.Amount ?? 0f);
        bool firstTransfer = source.ExecuteMachineTransfer(0, input, 0, "quantity", 1);
        for (int index = 1; index < input.Data.itemSlots.Count; index++)
            source.ExecuteMachineTransfer(0, input, index, "quantity", 500);
        source.ExecuteMachineTransfer(0, input, 0, "quantity", 500);
        float filled = Total();
        int filledSlots = input.Data.itemSlots.Count(slot => slot?.itemData != null);
        bool extraTransfer = source.ExecuteMachineTransfer(0, input, 0, "quantity", 1);
        ItemData product = GameRes.ExistingInstance.CreateItemData(itemId);
        product.Stack.Amount = 1f;
        bool fullGrant = CraftingTransaction.TryCreateGrant(input, new[] { product }, out _, out _);
        input.Data.TryConsumeFromSlot(input.Data.itemSlots[0], 1, out _);
        bool recoveredGrant = CraftingTransaction.TryCreateGrant(input, new[] { product }, out var grant, out _);
        bool committed = recoveredGrant && grant.Commit(out _);
        if (committed) grant.Complete();
        float afterGrant = Total();
        JObject panel = CaptureThermalPanelBindings(node, input, output);
        float panelMaxWeight = input.Data.MaxCarryWeight, panelMaxVolume = input.Data.MaxCarryVolume;
        node.Logic.Capture();
        ItemData snapshot = FastCloner.FastCloner.DeepClone(node.Snapshot);
        bool snapshotApplied = node.Logic.ApplyRemoteSnapshot(snapshot);
        bool postSnapshotExtraTransfer = source.ExecuteMachineTransfer(0, input, 0, "quantity", 1);
        return new JObject
        {
            ["id"] = id, ["itemId"] = itemId, ["firstTransfer"] = firstTransfer,
            ["filled"] = filled, ["nonEmptySlots"] = filledSlots,
            ["remaining"] = source.Data.itemSlots[0].itemData?.Stack?.Amount ?? 0f,
            ["extraTransfer"] = extraTransfer, ["fullGrant"] = fullGrant,
            ["recoveredGrant"] = committed, ["afterGrant"] = afterGrant, ["afterSnapshot"] = Total(),
            ["snapshotApplied"] = snapshotApplied, ["postSnapshotExtraTransfer"] = postSnapshotExtraTransfer,
            ["panelMaxWeight"] = panelMaxWeight, ["panelMaxVolume"] = panelMaxVolume,
            ["maxWeight"] = input.Data.MaxCarryWeight, ["maxVolume"] = input.Data.MaxCarryVolume,
            ["maxAllowedWeight"] = input.Data.MaxAllowedCarryWeight, ["capacityEnabled"] = input.Data.HasCarryCapacity,
            ["unlimitedCarry"] = input.Data.HasUnlimitedCarryCapacity,
            ["unlimitedSlots"] = input.Data.HasUnlimitedSlots, ["panel"] = panel,
            ["outputCapacityEnabled"] = output.Data.HasCarryCapacity,
            ["fuelCapacityEnabled"] = (node.Logic as FurnaceLogic)?.FuelInventory.Data.HasCarryCapacity == true
        };
    }
    #endregion
}
