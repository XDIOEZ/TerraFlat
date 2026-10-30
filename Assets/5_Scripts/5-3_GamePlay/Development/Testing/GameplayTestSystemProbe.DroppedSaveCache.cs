using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using MemoryPack;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 掉落物存档载荷缓存采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterDroppedItemSavePayloadCacheRules()
    {
        Register("save", "dropped_payload_cache", ProbeDroppedItemSavePayloadCache);
        Register("save", "dropped_cold_payload", ProbeDroppedItemColdPayload);
        Register("save", "dropped_public_payload", ProbeDroppedItemPublicPayload);
    }

    private static JObject ProbeDroppedItemSavePayloadCache(JObject args)
    {
        var codec = new DroppedSaveCountingCodec();
        using var registration = ModuleInstanceStateCodecs.Register(codec);
        var child = new Data_GeneralItem { IDName = "probe:child", Guid = 4202, Stack = new ItemStack { Amount = 6f } };
        var inventory = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = child }, new(1) }, "probe:bag");
        var inventoryModule = new Inventory_ModuleData { StableName = "bag", ModuleId = "probe:bag" };
        inventoryModule.Data.Add("bag", inventory);
        var fluid = new FluidInventoryState
        {
            Components = new List<FluidComponentState> { new() { FluidId = "probe:fluid", GasMoles = 3m, LiquidMoles = 2m } },
            Ports = new List<FluidOutputReservation> { new() { PortId = "out", FluidId = "probe:fluid", GasMoles = 1m } },
            TotalInternalEnergyJoules = 120d
        };
        var fluidModule = new Ex_ModData_MemoryPackable { StableName = "fluid", ModuleId = "probe:fluid" };
        fluidModule.WriteData(fluid);
        var liquid = new LiquidContainerState { LiquidId = "probe:water", Amount = 3f, Temperature = 18f };
        liquid.Composition["probe:water"] = 3f;
        liquid.PendingOutputs["tap"] = new LiquidPendingBatch { LiquidId = "probe:water", Servings = 1f };
        var liquidModule = new Ex_ModData_MemoryPackable { StableName = "liquid", ModuleId = "probe:liquid" };
        liquidModule.WriteData(liquid);
        var binaryModule = new Ex_ModData_MemoryPackable { StableName = "binary", ModuleId = "probe:binary", BitData = new byte[] { 1, 2, 3 } };
        var counted = new DroppedSaveCountingModule { StableName = "counted", ModuleId = "probe:counted", Value = 4 };
        var item = new Data_GeneralItem
        {
            IDName = "probe:drop", Guid = 4201, Stack = new ItemStack { Amount = 5f },
            MaxDurability = 10f, Durability = 8f,
            MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 21f, Moisture = .1f }
        };
        item.ModuleDataDic.Add("bag", inventoryModule);
        item.ModuleDataDic.Add("fluid", fluidModule);
        item.ModuleDataDic.Add("liquid", liquidModule);
        item.ModuleDataDic.Add("binary", binaryModule);
        item.ModuleDataDic.Add("counted", counted);
        using var simulation = new LightweightDroppedItemSimulation();
        simulation.Create(new LightweightDroppedBody
        { Id = 4201, Amount = 5f, Position = new float2(1f, 2f), Scale = new float2(.5f), Pickable = 1 },
        new LightweightDroppedFlight { Start = new float2(1f, 2f), End = new float2(5f, 2f),
            Control = new float2(3f, 3f), Duration = 2f, ArcHeight = 2f });
        var cache = new DroppedItemPayloadCaptureCache();
        DroppedItemSaveRecord first = DroppedItemSaveRecord.Capture(item, simulation, 4201, cache);
        DroppedItemSaveRecord repeated = DroppedItemSaveRecord.Capture(item, simulation, 4201, cache);
        bool samePayload = ReferenceEquals(first.EncodedItemPayload, repeated.EncodedItemPayload);
        int initialModuleEncodes = codec.Captures;

        LightweightDroppedBody body = simulation.Get(4201);
        body.Amount = 3f; body.Scale = new float2(.75f); body.WaterKind = 1; body.LiquidDepth = .4f;
        simulation.Set(body);
        simulation.SetWater(4201, new LightweightDroppedWaterTransition { TargetDepth = .7f, Duration = 3f, Elapsed = .5f });
        var changes = new List<LightweightDroppedChange>();
        simulation.Step(.25f, default, changes);
        item.Stack.Amount = 3f; item.Durability = 4f;
        item.MatterState.TemperatureCelsius = 80f; item.MatterState.Moisture = .6f;
        item.MatterState.IsBurning = true; item.MatterState.CombustionElapsedSeconds = 2f;
        DroppedItemSaveRecord moved = DroppedItemSaveRecord.Capture(item, simulation, 4201, cache);
        DroppedItemSaveRecord restored = MemoryPackSerializer.Deserialize<DroppedItemSaveRecord>(MemoryPackSerializer.Serialize(moved));
        bool motionReused = ReferenceEquals(first.EncodedItemPayload, moved.EncodedItemPayload);
        int dynamicModuleEncodes = codec.Captures;
        ItemData oldData = first.Data;
        ItemData movedData = restored.Data;

        item.ItemSpecialData = "changed";
        DroppedItemSaveRecord special = DroppedItemSaveRecord.Capture(item, simulation, 4201, cache);
        int rootChangeModuleEncodes = codec.Captures;
        child.Stack.Amount = 2f;
        DroppedItemSaveRecord inventoryChanged = DroppedItemSaveRecord.Capture(item, simulation, 4201, cache);
        ItemData restoredChild = ((Inventory_ModuleData)inventoryChanged.Data.ModuleDataDic["bag"]).Data["bag"].itemSlots[0].itemData;
        child.PhysicsState.Velocity = new Vector2(7f, -2f);
        DroppedItemSaveRecord physicsChanged = DroppedItemSaveRecord.Capture(item, simulation, 4201, cache);
        ItemData restoredPhysicsChild = ((Inventory_ModuleData)physicsChanged.Data.ModuleDataDic["bag"]).Data["bag"].itemSlots[0].itemData;
        fluid.Components[0].GasMoles = 8m; fluid.Ports[0].GasMoles = 2m; fluid.TotalInternalEnergyJoules = 240d;
        fluidModule.WriteData(fluid);
        DroppedItemSaveRecord fluidChanged = DroppedItemSaveRecord.Capture(item, simulation, 4201, cache);
        FluidInventoryState restoredFluid = ((Ex_ModData_MemoryPackable)fluidChanged.Data.ModuleDataDic["fluid"]).GetData<FluidInventoryState>();
        liquid.Composition["probe:water"] = 1.5f; liquid.Amount = 1.5f;
        liquid.PendingOutputs["tap"].Servings = .5f;
        liquidModule.WriteData(liquid);
        DroppedItemSaveRecord liquidChanged = DroppedItemSaveRecord.Capture(item, simulation, 4201, cache);
        LiquidContainerState restoredLiquid = ((Ex_ModData_MemoryPackable)liquidChanged.Data.ModuleDataDic["liquid"]).GetData<LiquidContainerState>();
        binaryModule.BitData[1] = 9;
        DroppedItemSaveRecord binaryChanged = DroppedItemSaveRecord.Capture(item, simulation, 4201, cache);
        byte restoredByte = ((Ex_ModData_MemoryPackable)binaryChanged.Data.ModuleDataDic["binary"]).BitData[1];
        counted.Value = 7;
        DroppedItemSaveRecord moduleChanged = DroppedItemSaveRecord.Capture(item, simulation, 4201, cache);
        int changedModuleEncodes = codec.Captures;
        int restoredModuleValue = ((DroppedSaveCountingModule)moduleChanged.Data.ModuleDataDic["counted"]).Value;
        var replacement = new Data_GeneralItem { IDName = "probe:replacement", Guid = 4201, Stack = new ItemStack { Amount = 3f } };
        DroppedItemSaveRecord replaced = DroppedItemSaveRecord.Capture(replacement, simulation, 4201, cache);

        return new JObject
        {
            ["samePayload"] = samePayload, ["motionReused"] = motionReused,
            ["initialModuleEncodes"] = initialModuleEncodes, ["dynamicModuleEncodes"] = dynamicModuleEncodes,
            ["rootChangeModuleEncodes"] = rootChangeModuleEncodes, ["changedModuleEncodes"] = changedModuleEncodes,
            ["firstAmount"] = oldData.Stack.Amount, ["firstDurability"] = oldData.Durability / oldData.MaxDurability,
            ["firstTemperature"] = oldData.MatterState.TemperatureCelsius,
            ["movedAmount"] = movedData.Stack.Amount, ["movedDurability"] = movedData.Durability / movedData.MaxDurability,
            ["movedTemperature"] = movedData.MatterState.TemperatureCelsius, ["movedMoisture"] = movedData.MatterState.Moisture,
            ["movedBurning"] = movedData.MatterState.IsBurning, ["movedBurnSeconds"] = movedData.MatterState.CombustionElapsedSeconds,
            ["positionX"] = restored.Position.x, ["scaleX"] = restored.Scale.x, ["flight"] = restored.HasFlight,
            ["flightElapsed"] = restored.FlightElapsed, ["water"] = restored.HasWaterTransition,
            ["waterElapsed"] = restored.WaterElapsed, ["waterKind"] = restored.WaterKind,
            ["specialChanged"] = !ReferenceEquals(moved.EncodedItemPayload, special.EncodedItemPayload),
            ["inventoryChanged"] = !ReferenceEquals(special.EncodedItemPayload, inventoryChanged.EncodedItemPayload),
            ["childAmount"] = restoredChild.Stack.Amount,
            ["physicsChanged"] = !ReferenceEquals(inventoryChanged.EncodedItemPayload, physicsChanged.EncodedItemPayload),
            ["childVelocityX"] = restoredPhysicsChild.PhysicsState.Velocity.x,
            ["fluidChanged"] = !ReferenceEquals(physicsChanged.EncodedItemPayload, fluidChanged.EncodedItemPayload),
            ["gasMoles"] = (double)restoredFluid.Components[0].GasMoles, ["reservedGas"] = (double)restoredFluid.Ports[0].GasMoles,
            ["energy"] = restoredFluid.TotalInternalEnergyJoules,
            ["liquidChanged"] = !ReferenceEquals(fluidChanged.EncodedItemPayload, liquidChanged.EncodedItemPayload),
            ["liquidAmount"] = restoredLiquid.Composition["probe:water"], ["liquidReserved"] = restoredLiquid.PendingOutputs["tap"].Servings,
            ["binaryChanged"] = !ReferenceEquals(liquidChanged.EncodedItemPayload, binaryChanged.EncodedItemPayload),
            ["binaryByte"] = restoredByte, ["moduleChanged"] = !ReferenceEquals(binaryChanged.EncodedItemPayload, moduleChanged.EncodedItemPayload),
            ["moduleValue"] = restoredModuleValue, ["replacementChanged"] = !ReferenceEquals(moduleChanged.EncodedItemPayload, replaced.EncodedItemPayload),
            ["replacementId"] = replaced.Data.IDName
        };
    }

    private static JObject ProbeDroppedItemColdPayload(JObject args)
    {
        var source = new Data_GeneralItem
        { IDName = "probe:cold-drop", Guid = 4301, Stack = new ItemStack { Amount = 5f, Volume = 1f }, MaxDurability = 1f, Durability = 1f };
        var record = new DroppedItemSaveRecord { Data = source, Position = new Vector2(2f, 3f), Scale = Vector2.one };
        byte[] frozenBefore = MemoryPackSerializer.Serialize(record);
        DroppedItemSaveRecord cold = MemoryPackSerializer.Deserialize<DroppedItemSaveRecord>(frozenBefore);
        byte[] payloadBefore = cold.EncodedItemPayload;
        var inventory = new Inventory_Data(new List<ItemSlot> { new(0) { SlotMaxVolume = 2f } }, "probe:cold-inventory")
        { IsDepositBlocked = true };
        float blocked = DroppedItemService.TransferColdFuel(cold.Data, inventory);
        inventory.IsDepositBlocked = false;
        float partial = DroppedItemService.TransferColdFuel(cold.Data, inventory);
        int transferredGuid = inventory.itemSlots[0].itemData.Guid;
        DroppedItemSaveRecord firstSave = MemoryPackSerializer.Deserialize<DroppedItemSaveRecord>(MemoryPackSerializer.Serialize(cold));
        bool reusedAfterPartial = ReferenceEquals(payloadBefore, cold.EncodedItemPayload);
        inventory.itemSlots[0].SlotMaxVolume = 4f;
        float merged = DroppedItemService.TransferColdFuel(cold.Data, inventory);
        DroppedItemSaveRecord secondSave = MemoryPackSerializer.Deserialize<DroppedItemSaveRecord>(MemoryPackSerializer.Serialize(cold));
        bool reusedAfterMerge = ReferenceEquals(payloadBefore, cold.EncodedItemPayload);
        DroppedItemSaveRecord original = MemoryPackSerializer.Deserialize<DroppedItemSaveRecord>(frozenBefore);
        DroppedItemSaveRecord projected = cold.CopyForWorldTransfer();
        projected.Position = new Vector2(12f, -4f); projected.Rotation = 90f;
        projected.Data.Stack.Amount = .5f;
        DroppedItemSaveRecord projectedSave = MemoryPackSerializer.Deserialize<DroppedItemSaveRecord>(MemoryPackSerializer.Serialize(projected));
        var savedWorlds = new GameSaveData();
        savedWorlds.DroppedItems.Worlds["probe:source"] = new List<DroppedItemSaveRecord> { cold };
        savedWorlds.DroppedItems.Worlds["probe:target"] = new List<DroppedItemSaveRecord> { projected };
        byte[] archiveBytes = DroppedItemService.CaptureArchive(savedWorlds);
        var restoredWorlds = new GameSaveData();
        DroppedItemService.RestoreArchive(restoredWorlds, archiveBytes);
        bool oldVersionRejected = false;
        try { DroppedItemService.RestoreArchive(new GameSaveData(), MemoryPackSerializer.Serialize(new DroppedItemArchive { Version = 1 })); }
        catch (InvalidOperationException) { oldVersionRejected = true; }
        var canonical = new Data_GeneralItem
        { IDName = "probe:canonical", Guid = 4399, Stack = new ItemStack { Amount = 5f }, MaxDurability = 10f, Durability = 8f };
        byte[] canonicalFirst = new DroppedItemPayloadCaptureCache().Capture(canonical);
        canonical.Stack.Amount = 2f; canonical.Stack.CanBePickedUp = false; canonical.Durability = 3f;
        canonical.MatterState.TemperatureCelsius = 90f; canonical.MatterState.IsBurning = true;
        byte[] canonicalNext = new DroppedItemPayloadCaptureCache().Capture(canonical);
        var unknownCodec = new DroppedSaveUnknownCodec();
        using var registration = ModuleInstanceStateCodecs.Register(unknownCodec);
        var unknown = new Data_GeneralItem { IDName = "probe:unknown", Guid = 4302, Stack = new ItemStack() };
        unknown.ModuleDataDic.Add("unknown", new DroppedSaveUnknownModule { StableName = "unknown", ModuleId = "probe:unknown" });
        var cache = new DroppedItemPayloadCaptureCache();
        byte[] firstUnknown = cache.Capture(unknown), secondUnknown = cache.Capture(unknown);
        var cyclic = new Data_GeneralItem { IDName = "probe:cycle", Guid = 4303, Stack = new ItemStack() };
        var cyclicInventory = new Inventory_ModuleData { StableName = "cycle", ModuleId = "probe:cycle" };
        cyclicInventory.Data.Add("cycle", new Inventory_Data(new List<ItemSlot> { new(0) { itemData = cyclic } }, "cycle"));
        cyclic.ModuleDataDic.Add("cycle", cyclicInventory);
        bool cycleReusable = ItemRuntimeStateFingerprint.TryCalculate(cyclic, false, false, new HashSet<ItemData>(), out _,
            includeRootStackState: false, includeRootDynamicState: false);
        return new JObject
        {
            ["blocked"] = blocked, ["partial"] = partial, ["merged"] = merged,
            ["firstRemainder"] = firstSave.Data.Stack.Amount, ["secondRemainder"] = secondSave.Data.Stack.Amount,
            ["mergedAmount"] = inventory.itemSlots[0].itemData.Stack.Amount,
            ["sourceGuid"] = cold.Guid, ["splitGuid"] = transferredGuid,
            ["reusedAfterPartial"] = reusedAfterPartial, ["reusedAfterMerge"] = reusedAfterMerge,
            ["originalAmount"] = original.Data.Stack.Amount, ["originalX"] = original.Position.x,
            ["projectedAmount"] = projectedSave.Data.Stack.Amount, ["projectedX"] = projectedSave.Position.x,
            ["projectedRotation"] = projectedSave.Rotation, ["sourceAmount"] = cold.Data.Stack.Amount,
            ["sourceX"] = cold.Position.x, ["archiveVersion"] = restoredWorlds.DroppedItems.Version,
            ["archiveSourceAmount"] = restoredWorlds.DroppedItems.Worlds["probe:source"][0].Data.Stack.Amount,
            ["archiveTargetAmount"] = restoredWorlds.DroppedItems.Worlds["probe:target"][0].Data.Stack.Amount,
            ["oldVersionRejected"] = oldVersionRejected,
            ["canonicalIndependentCaptures"] = canonicalFirst.AsSpan().SequenceEqual(canonicalNext),
            ["unknownReused"] = ReferenceEquals(firstUnknown, secondUnknown),
            ["unknownEncodes"] = unknownCodec.Captures, ["cycleReusable"] = cycleReusable
        };
    }

    private static JObject ProbeDroppedItemPublicPayload(JObject args)
    {
        var player = new Data_Player
        { IDName = "probe:nested-player", Guid = 4402, Stack = new ItemStack(), ItemSpecialData = "private-special" };
        player._inventoryData["private"] = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = new Data_GeneralItem { IDName = "probe:secret", Guid = 4403, Stack = new ItemStack { Amount = 9f } } } }, "private");
        var module = new Inventory_ModuleData { StableName = "bag", ModuleId = "probe:bag" };
        module.Data["bag"] = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = player } }, "bag");
        var root = new Data_GeneralItem { IDName = "probe:outer", Guid = 4401, Stack = new ItemStack() };
        root.ModuleDataDic.Add("bag", module);
        using var simulation = new LightweightDroppedItemSimulation();
        simulation.Create(new LightweightDroppedBody { Id = 4401, Amount = 1f, Scale = new float2(1f), Pickable = 1 });
        var cache = new DroppedItemPayloadCaptureCache();
        DroppedItemSaveRecord privateRecord = DroppedItemSaveRecord.Capture(root, simulation, 4401, cache);
        Data_Player Child(DroppedItemSaveRecord value) => (Data_Player)((Inventory_ModuleData)value.Data.ModuleDataDic["bag"]).Data["bag"].itemSlots[0].itemData;
        Data_Player privateBefore = Child(MemoryPackSerializer.Deserialize<DroppedItemSaveRecord>(MemoryPackSerializer.Serialize(privateRecord)));
        bool publicReused;
        string publicSpecial, serializedPublicSpecial;
        int publicInventoryCount, serializedPublicInventoryCount;
        using (ItemSnapshotSerialization.BeginPublicPlayerState(_ => "public-special"))
        {
            DroppedItemSaveRecord firstPublic = DroppedItemSaveRecord.Capture(root, simulation, 4401, cache);
            DroppedItemSaveRecord secondPublic = DroppedItemSaveRecord.Capture(root, simulation, 4401, cache);
            publicReused = ReferenceEquals(firstPublic.EncodedItemPayload, secondPublic.EncodedItemPayload);
            Data_Player publicChild = Child(firstPublic);
            publicSpecial = publicChild.ItemSpecialData;
            publicInventoryCount = publicChild._inventoryData.Count;
            DroppedItemSaveRecord publicArchiveRecord = MemoryPackSerializer.Deserialize<DroppedItemSaveRecord>(MemoryPackSerializer.Serialize(privateRecord));
            Data_Player serializedPublicChild = Child(publicArchiveRecord);
            serializedPublicSpecial = serializedPublicChild.ItemSpecialData;
            serializedPublicInventoryCount = serializedPublicChild._inventoryData.Count;
        }
        DroppedItemSaveRecord privateAfter = MemoryPackSerializer.Deserialize<DroppedItemSaveRecord>(MemoryPackSerializer.Serialize(privateRecord));
        Data_Player privateAfterChild = Child(privateAfter);
        DroppedItemSaveRecord privateRecapture = DroppedItemSaveRecord.Capture(root, simulation, 4401, cache);
        Data_Player privateRecapturedChild = Child(privateRecapture);
        return new JObject
        {
            ["privateBeforeSpecial"] = privateBefore.ItemSpecialData,
            ["privateBeforeCount"] = privateBefore._inventoryData.Count,
            ["publicSpecial"] = publicSpecial, ["publicCount"] = publicInventoryCount,
            ["serializedPublicSpecial"] = serializedPublicSpecial, ["serializedPublicCount"] = serializedPublicInventoryCount,
            ["publicReused"] = publicReused,
            ["privateAfterSpecial"] = privateAfterChild.ItemSpecialData,
            ["privateAfterCount"] = privateAfterChild._inventoryData.Count,
            ["privateRecapturedSpecial"] = privateRecapturedChild.ItemSpecialData,
            ["privateRecapturedCount"] = privateRecapturedChild._inventoryData.Count
        };
    }

    #endregion
}

#region 掉落物扩展模块缓存测试替身

internal sealed class DroppedSaveCountingModule : ModuleData { public byte Value; }
internal sealed class DroppedSaveCountingCodec : IModuleInstanceStateCodec, IModuleInstanceStateFingerprintProvider
{
    public int Captures;
    public string Id => "probe.drop-save.counting";
    public Type DataType => typeof(DroppedSaveCountingModule);
    public ModuleData CreateData() => new DroppedSaveCountingModule();
    public byte[] Capture(ModuleData data, bool publicState) { Captures++; return new[] { ((DroppedSaveCountingModule)data).Value }; }
    public void Restore(ModuleData data, byte[] payload) => ((DroppedSaveCountingModule)data).Value = payload[0];
    public bool TryCalculateStateFingerprint(ModuleData data, bool publicState, out ulong fingerprint)
    { fingerprint = ((DroppedSaveCountingModule)data).Value; return true; }
}
internal sealed class DroppedSaveUnknownModule : ModuleData { }
internal sealed class DroppedSaveUnknownCodec : IModuleInstanceStateCodec
{
    public int Captures;
    public string Id => "probe.drop-save.unknown";
    public Type DataType => typeof(DroppedSaveUnknownModule);
    public ModuleData CreateData() => new DroppedSaveUnknownModule();
    public byte[] Capture(ModuleData data, bool publicState) { Captures++; return new byte[] { 6 }; }
    public void Restore(ModuleData data, byte[] payload) { }
}

#endregion
