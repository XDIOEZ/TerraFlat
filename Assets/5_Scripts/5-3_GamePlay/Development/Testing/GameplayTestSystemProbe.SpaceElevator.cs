using System;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 太空电梯生产规则采样
    private static void RegisterSpaceElevatorOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("space/elevator_journey", SpaceElevatorJourney);
        operations.Add("space/elevator_cargo", SpaceElevatorCargo);
        operations.Add("space/elevator_warehouses", SpaceElevatorWarehouses);
        operations.Add("space/elevator_snapshot", SpaceElevatorSnapshot);
        operations.Add("space/elevator_station", SpaceElevatorStation);
        operations.Add("space/elevator_authority", SpaceElevatorAuthority);
        operations.Add("space/elevator_geography", SpaceElevatorGeography);
        operations.Add("space/elevator_drag_guard", SpaceElevatorDragGuard);
    }

    private static JObject ElevatorReading(SpaceElevatorState state) => new()
    {
        ["cabin"] = state.CabinEndpoint.ToString(), ["destination"] = state.Destination.ToString(),
        ["travelling"] = state.Travelling, ["remaining"] = state.RemainingSeconds,
        ["deliveryPending"] = state.DeliveryPending, ["cargoDestination"] = state.CargoDestination.ToString(),
        ["groundAccess"] = SpaceElevatorDomain.CanAccessCargo(state, SpaceElevatorEndpoint.Ground),
        ["stationAccess"] = SpaceElevatorDomain.CanAccessCargo(state, SpaceElevatorEndpoint.Station)
    };

    private static JObject ElevatorScope(string operation) => new()
    {
        ["operation"] = operation, ["scope"] = "production-domain",
        ["limitations"] = "仅采样正式领域、库存和内存快照；未运行真实终端、玩家迁移、存档磁盘、原生物理、UI 或远程联机。"
    };

    private static JObject SpaceElevatorJourney(JObject args)
    {
        var state = new SpaceElevatorState { ElevatorId = "elevator-rules" };
        JObject result = ElevatorScope("elevator_journey");
        result["initial"] = ElevatorReading(state);
        result["started"] = SpaceElevatorDomain.TryBegin(state, SpaceElevatorEndpoint.Station, 12d, out _);
        result["departed"] = ElevatorReading(state);
        string moving = JsonConvert.SerializeObject(state);
        result["repeated"] = SpaceElevatorDomain.TryBegin(state, SpaceElevatorEndpoint.Ground, 12d, out string repeatedReason);
        result["repeatedReason"] = repeatedReason;
        result["repeatedUnchanged"] = moving == JsonConvert.SerializeObject(state);
        var invalidSteps = new JArray();
        foreach (double seconds in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
        {
            bool arrived = SpaceElevatorDomain.Step(state, seconds);
            invalidSteps.Add(new JObject { ["arrived"] = arrived, ["unchanged"] = moving == JsonConvert.SerializeObject(state) });
        }
        result["invalidSteps"] = invalidSteps;
        result["earlyArrival"] = SpaceElevatorDomain.Step(state, 5d);
        result["during"] = ElevatorReading(state);
        result["arrival"] = SpaceElevatorDomain.Step(state, 20d);
        result["station"] = ElevatorReading(state);
        result["arrivalRepeated"] = SpaceElevatorDomain.Step(state, 1d);
        result["returnStarted"] = SpaceElevatorDomain.TryBegin(state, SpaceElevatorEndpoint.Ground, 6d, out _);
        result["returnEarlyArrival"] = SpaceElevatorDomain.Step(state, 3d);
        result["returnDuring"] = ElevatorReading(state);
        result["returnArrival"] = SpaceElevatorDomain.Step(state, 3d);
        result["returned"] = ElevatorReading(state);

        var invalidStarts = new JArray();
        void Reject(string name, SpaceElevatorState candidate, SpaceElevatorEndpoint endpoint, double duration)
        {
            string before = JsonConvert.SerializeObject(candidate);
            bool accepted = SpaceElevatorDomain.TryBegin(candidate, endpoint, duration, out string reason);
            invalidStarts.Add(new JObject { ["name"] = name, ["accepted"] = accepted, ["reason"] = reason,
                ["unchanged"] = before == JsonConvert.SerializeObject(candidate) });
        }
        Reject("missing", null, SpaceElevatorEndpoint.Station, 12d);
        Reject("already_here", new SpaceElevatorState(), SpaceElevatorEndpoint.Ground, 12d);
        Reject("invalid_destination", new SpaceElevatorState(), (SpaceElevatorEndpoint)99, 12d);
        Reject("zero", new SpaceElevatorState(), SpaceElevatorEndpoint.Station, 0d);
        Reject("negative", new SpaceElevatorState(), SpaceElevatorEndpoint.Station, -1d);
        Reject("nan", new SpaceElevatorState(), SpaceElevatorEndpoint.Station, double.NaN);
        Reject("infinity", new SpaceElevatorState(), SpaceElevatorEndpoint.Station, double.PositiveInfinity);
        result["invalidStarts"] = invalidStarts;
        result["missingAccess"] = SpaceElevatorDomain.CanAccessCargo(null, SpaceElevatorEndpoint.Ground);
        var pending = new SpaceElevatorState { CabinEndpoint = SpaceElevatorEndpoint.Station,
            CargoDestination = SpaceElevatorEndpoint.Station, DeliveryPending = true,
            CargoSnapshots = new List<string> { SpaceSession.Encode(EntityProbeData("probe:waiting-cargo", 3f)) } };
        string pendingCargo = pending.CargoSnapshots[0];
        result["pendingRecallStarted"] = SpaceElevatorDomain.TryBegin(pending, SpaceElevatorEndpoint.Ground, 1d, out _);
        result["pendingRecalled"] = SpaceElevatorDomain.Step(pending, 1d);
        result["pendingRecall"] = ElevatorReading(pending);
        result["pendingRideStarted"] = SpaceElevatorDomain.TryBegin(pending, SpaceElevatorEndpoint.Station, 1d, out _);
        result["pendingRode"] = SpaceElevatorDomain.Step(pending, 1d);
        result["pendingRide"] = ElevatorReading(pending);
        result["pendingCargoUnchanged"] = pending.CargoSnapshots.Count == 1 && pending.CargoSnapshots[0] == pendingCargo;
        return result;
    }

    private static JObject SpaceElevatorAuthority(JObject args)
    {
        JObject result = ElevatorScope("elevator_authority");
        result["offlineLocal"] = SpaceSession.HasSpaceElevatorAuthority(true, true);
        result["localHost"] = SpaceSession.HasSpaceElevatorAuthority(true, true);
        result["remoteClient"] = SpaceSession.HasSpaceElevatorAuthority(false, true);
        result["remoteActorOnServer"] = SpaceSession.HasSpaceElevatorAuthority(true, false);
        result["noAuthorityOrActor"] = SpaceSession.HasSpaceElevatorAuthority(false, false);
        return result;
    }

    private static JObject SpaceElevatorGeography(JObject args)
    {
        JObject result = ElevatorScope("elevator_geography");
        var numbers = new Dictionary<string, double>
        {
            ["climate.polarBand.enabled"] = 1d, ["climate.polarBand.position"] = 0d,
            ["climate.polarBand.halfWidth"] = .1d, ["climate.equator.widthRatio"] = .1d,
            ["climate.polarBand.boundary.offsetTiles"] = 0d
        };
        var topology = new ChunkGenerationTopologySnapshot(new Int2(-100, -200), new Int2(200, 400));
        ChunkGenerationRequest Request(Dictionary<string, double> settings, bool wrapped = true)
            => new(1, new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0)), 17413, 1,
                new ChunkGenerationProfileSnapshot("probe:elevator-geography", 1, 16, 16, settings),
                wrapped ? topology : default);
        ChunkGenerationRequest request = Request(numbers);
        var samples = new JArray();
        void Sample(string name, ChunkGenerationRequest context, int x, int y)
            => samples.Add(new JObject { ["name"] = name,
                ["accepted"] = DeterministicChunkGenerator.IsInsideEquatorBand(context, x, y) });
        Sample("center", request, 0, 0);
        Sample("lower_edge", request, 0, -20);
        Sample("upper_edge", request, 0, 20);
        Sample("below_band", request, 0, -21);
        Sample("above_band", request, 0, 21);
        Sample("temperate", request, 0, 100);
        Sample("polar", request, 0, -200);
        Sample("wrapped_x", request, 200, 0);
        Sample("wrapped_y", request, 0, 400);
        Sample("unbounded", Request(numbers, false), 0, 0);
        var disabled = new Dictionary<string, double>(numbers) { ["climate.polarBand.enabled"] = 0d };
        Sample("disabled_climate", Request(disabled), 0, 0);
        result["samples"] = samples;

        var shiftedNumbers = new Dictionary<string, double>(numbers)
            { ["climate.polarBand.boundary.offsetTiles"] = 32d };
        ChunkGenerationRequest shifted = Request(shiftedNumbers);
        var shiftedBands = new JArray();
        foreach (int x in new[] { -100, -50, 0, 50, 99 })
        {
            int first = int.MaxValue, last = int.MinValue, count = 0;
            bool wrapStable = true;
            for (int y = -200; y < 200; y++)
            {
                // 读取正式地理带本身，不从温度阈值或测试内的纬度公式猜资格。
                bool inside = DeterministicChunkGenerator.IsInsideEquatorBand(shifted, x, y);
                wrapStable &= inside == DeterministicChunkGenerator.IsInsideEquatorBand(shifted, x + 200, y + 400);
                if (!inside) continue;
                first = Math.Min(first, y); last = Math.Max(last, y); count++;
            }
            shiftedBands.Add(new JObject { ["x"] = x, ["first"] = first, ["last"] = last,
                ["count"] = count, ["wrapStable"] = wrapStable });
        }
        result["shiftedBands"] = shiftedBands;
        var anchors = new JArray();
        foreach (double x in new[] { 0d, .25d, .5d, .75d, 1d })
        {
            SpaceVector2 point = SpaceElevatorDomain.GetEquatorAnchorOffset(x, 1000d, -Math.PI * .25d);
            anchors.Add(new JObject { ["normalizedX"] = x, ["x"] = point.X, ["y"] = point.Y });
        }
        result["anchors"] = anchors;
        SpaceVector2 middle = SpaceElevatorDomain.GetEquatorAnchorOffset(.5d, 1000d, -Math.PI * .25d);
        result["stationClearance"] = new JArray
        {
            new JObject { ["name"] = "same_longitude", ["accepted"] = SpaceElevatorDomain.HasStationClearance(middle, middle) },
            new JObject { ["name"] = "near_longitude", ["accepted"] = SpaceElevatorDomain.HasStationClearance(middle,
                SpaceElevatorDomain.GetEquatorAnchorOffset(.5001d, 1000d, -Math.PI * .25d)) },
            new JObject { ["name"] = "separate_longitude", ["accepted"] = SpaceElevatorDomain.HasStationClearance(middle,
                SpaceElevatorDomain.GetEquatorAnchorOffset(.505d, 1000d, -Math.PI * .25d)) },
            new JObject { ["name"] = "below_clearance", ["accepted"] = SpaceElevatorDomain.HasStationClearance(default, new SpaceVector2(7.999d, 0d)) },
            new JObject { ["name"] = "at_clearance", ["accepted"] = SpaceElevatorDomain.HasStationClearance(default, new SpaceVector2(8d, 0d)) },
            new JObject { ["name"] = "invalid", ["accepted"] = SpaceElevatorDomain.HasStationClearance(default, new SpaceVector2(double.NaN, 0d)) },
            new JObject { ["name"] = "expanded_overlap", ["accepted"] = SpaceElevatorDomain.HasStationFootprintClearance(
                new SpaceVector2(20d, 0d), default, new SpaceVector2(-2d, -2d), new SpaceVector2(30d, 3d)) },
            new JObject { ["name"] = "expanded_below_gap", ["accepted"] = SpaceElevatorDomain.HasStationFootprintClearance(
                new SpaceVector2(32.999d, 0d), default, new SpaceVector2(-2d, -2d), new SpaceVector2(30d, 3d)) },
            new JObject { ["name"] = "expanded_at_gap", ["accepted"] = SpaceElevatorDomain.HasStationFootprintClearance(
                new SpaceVector2(33d, 0d), default, new SpaceVector2(-2d, -2d), new SpaceVector2(30d, 3d)) },
            new JObject { ["name"] = "expanded_invalid", ["accepted"] = SpaceElevatorDomain.HasStationFootprintClearance(
                new SpaceVector2(double.NaN, 0d), default, new SpaceVector2(-2d, -2d), new SpaceVector2(30d, 3d)) }
        };
        return result;
    }
    #endregion

    #region 两端仓库与真实库存转移
    private static Inventory_Data ElevatorInventory(string name, params ItemSlot[] slots)
        => new(new List<ItemSlot>(slots), "probe:elevator:" + name);

    private static JArray ElevatorInventoryReading(Inventory_Data inventory)
    {
        var slots = new JArray();
        if (inventory?.itemSlots == null) return slots;
        foreach (ItemSlot slot in inventory.itemSlots)
            slots.Add(new JObject { ["id"] = slot?.itemData?.IDName ?? string.Empty,
                ["guid"] = slot?.itemData?.Guid ?? 0, ["amount"] = slot?.itemData?.Stack?.Amount ?? 0f,
                ["special"] = slot?.itemData?.ItemSpecialData ?? string.Empty });
        return slots;
    }

    private static JObject SpaceElevatorCargo(JObject args)
    {
        var samples = new JArray();
        void Transfer(string name, Inventory_Data source, Inventory_Data destination, int sourceIndex = 0)
        {
            JArray beforeSource = ElevatorInventoryReading(source), beforeTarget = ElevatorInventoryReading(destination);
            bool accepted = SpaceElevatorDomain.TryTransferCargo(source, sourceIndex, destination, out string reason);
            samples.Add(new JObject { ["name"] = name, ["accepted"] = accepted, ["reason"] = reason,
                ["beforeSource"] = beforeSource, ["beforeTarget"] = beforeTarget,
                ["source"] = ElevatorInventoryReading(source), ["target"] = ElevatorInventoryReading(destination) });
        }

        // 使用真实库存事务，样本仅配置格数和容量，不在探针内复制转移算法。
        var goods = EntityProbeData("probe:elevator-ore", 7f);
        var ground = ElevatorInventory("ground", new ItemSlot(0) { itemData = goods });
        var cabin = ElevatorInventory("cabin", new ItemSlot(0), new ItemSlot(1));
        var station = ElevatorInventory("station", new ItemSlot(0));
        Transfer("ground_to_cabin", ground, cabin);
        Transfer("cabin_to_station", cabin, station);
        Transfer("station_to_cabin", station, cabin);
        Transfer("cabin_to_ground", cabin, ground);

        var limitedSource = ElevatorInventory("capacity-source", new ItemSlot(0)
            { itemData = EntityProbeData("probe:elevator-capacity", 7f, 2f, 1f) });
        var limited = ElevatorInventory("capacity-target", new ItemSlot(0), new ItemSlot(1));
        limited.ConfigureStorageContainerCapacity(6f, 100f);
        Transfer("weight_partial", limitedSource, limited);
        Transfer("weight_full", limitedSource, limited);
        var volume = ElevatorInventory("volume-target", new ItemSlot(0));
        volume.ConfigureStorageContainerCapacity(100f, 2f);
        Transfer("volume_partial", limitedSource, volume);

        var blocked = ElevatorInventory("blocked", new ItemSlot(0));
        blocked.IsDepositBlocked = true;
        Transfer("deposit_blocked", limitedSource, blocked);
        Transfer("no_slots", limitedSource, ElevatorInventory("empty"));
        Transfer("same_inventory", limitedSource, limitedSource);
        Transfer("bad_index", limitedSource, cabin, 3);
        Transfer("missing_inventory", null, cabin);

        var equipment = EntityProbeData("probe:elevator-equipment", 1f);
        equipment.Stack.Stackable = false;
        equipment.Guid = 35791;
        equipment.ItemSpecialData = "elevator-instance-marker";
        var equipmentSource = ElevatorInventory("equipment-source", new ItemSlot(0) { itemData = equipment });
        var equipmentTarget = ElevatorInventory("equipment-target", new ItemSlot(0));
        Transfer("equipment_identity", equipmentSource, equipmentTarget);
        JObject result = ElevatorScope("elevator_cargo");
        result["samples"] = samples;
        result["sameEquipmentInstance"] = ReferenceEquals(equipment, equipmentTarget.itemSlots[0].itemData);
        return result;
    }

    private static JObject SpaceElevatorWarehouses(JObject args)
    {
        JObject result = ElevatorScope("elevator_warehouses");
        var ore = EntityProbeData("probe:elevator-warehouse-ore", 7f);
        var equipment = EntityProbeData("probe:elevator-warehouse-equipment", 1f);
        equipment.Stack.Stackable = false;
        equipment.Guid = 97531;
        equipment.ItemSpecialData = "warehouse-equipment-instance";
        var ground = ElevatorInventory("ground-warehouse", new ItemSlot(0) { itemData = ore },
            new ItemSlot(1) { itemData = equipment }, new ItemSlot(2));
        var transit = ElevatorInventory("transit", new ItemSlot(0), new ItemSlot(1), new ItemSlot(2));
        var station = ElevatorInventory("station-warehouse", new ItemSlot(0)
            { itemData = EntityProbeData(ore.IDName, 3f) }, new ItemSlot(1), new ItemSlot(2));
        JArray beforeStation = ElevatorInventoryReading(station);
        result["packed"] = SpaceElevatorDomain.TryPackCargo(ground, transit, out string packedReason);
        result["packedReason"] = packedReason;
        result["groundAfterPack"] = ElevatorInventoryReading(ground);
        result["transitAfterPack"] = ElevatorInventoryReading(transit);
        result["stationAfterPack"] = ElevatorInventoryReading(station);
        result["stationBeforePack"] = beforeStation;
        result["packedEquipmentInstance"] = ReferenceEquals(equipment, transit.itemSlots[1].itemData);
        var refill = ElevatorInventory("refill", new ItemSlot(0) { itemData = EntityProbeData(ore.IDName, 2f) });
        result["groundRefilled"] = SpaceElevatorDomain.TryTransferCargo(refill, 0, ground, out _);
        result["groundWhileInTransit"] = ElevatorInventoryReading(ground);
        result["transitAfterRefill"] = ElevatorInventoryReading(transit);
        JArray beforeBusySource = ElevatorInventoryReading(ground), beforeBusyTransit = ElevatorInventoryReading(transit);
        result["busyPackAccepted"] = SpaceElevatorDomain.TryPackCargo(ground, transit, out string busyReason);
        result["busyPackReason"] = busyReason;
        result["busySourceUnchanged"] = JToken.DeepEquals(beforeBusySource, ElevatorInventoryReading(ground));
        result["busyTransitUnchanged"] = JToken.DeepEquals(beforeBusyTransit, ElevatorInventoryReading(transit));
        result["delivered"] = SpaceElevatorDomain.DeliverCargo(transit, station);
        result["groundAfterDelivery"] = ElevatorInventoryReading(ground);
        result["stationAfterDelivery"] = ElevatorInventoryReading(station);
        result["transitAfterDelivery"] = ElevatorInventoryReading(transit);
        result["deliveredEquipmentInstance"] = ReferenceEquals(equipment, station.itemSlots[1].itemData);
        result["returnPacked"] = SpaceElevatorDomain.TryPackCargo(station, transit, out _);
        result["returnDelivered"] = SpaceElevatorDomain.DeliverCargo(transit, ground);
        result["groundReturned"] = ElevatorInventoryReading(ground);
        result["stationReturned"] = ElevatorInventoryReading(station);
        result["transitReturned"] = ElevatorInventoryReading(transit);
        result["returnedEquipmentInstance"] = ReferenceEquals(equipment, ground.itemSlots[1].itemData);

        // 满仓只延后接收，后续腾出空间继续沿正式库存接口卸货。
        var pendingEquipment = EntityProbeData("probe:pending-equipment", 1f);
        pendingEquipment.Stack.Stackable = false;
        pendingEquipment.Guid = 86420;
        var pending = ElevatorInventory("pending", new ItemSlot(0)
            { itemData = EntityProbeData("probe:pending-ore", 7f) }, new ItemSlot(1) { itemData = pendingEquipment });
        var full = ElevatorInventory("full", new ItemSlot(0) { itemData = EntityProbeData("probe:blocker", 2f) });
        JArray beforeFull = ElevatorInventoryReading(full), beforePending = ElevatorInventoryReading(pending);
        result["fullDelivered"] = SpaceElevatorDomain.DeliverCargo(pending, full);
        result["fullUnchanged"] = JToken.DeepEquals(beforeFull, ElevatorInventoryReading(full));
        result["pendingUnchanged"] = JToken.DeepEquals(beforePending, ElevatorInventoryReading(pending));
        var removed = ElevatorInventory("removed", new ItemSlot(0), new ItemSlot(1));
        SpaceElevatorDomain.TryTransferCargo(full, 0, removed, out _);
        result["partialDelivered"] = SpaceElevatorDomain.DeliverCargo(pending, full);
        result["partialTransit"] = ElevatorInventoryReading(pending);
        result["partialWarehouse"] = ElevatorInventoryReading(full);
        SpaceElevatorDomain.TryTransferCargo(full, 0, removed, out _);
        result["remainingDelivered"] = SpaceElevatorDomain.DeliverCargo(pending, full);
        result["remainingTransit"] = ElevatorInventoryReading(pending);
        result["remainingWarehouse"] = ElevatorInventoryReading(full);
        result["pendingEquipmentInstance"] = ReferenceEquals(pendingEquipment, full.itemSlots[0].itemData);

        var rejected = new JArray();
        void RejectPack(string name, Inventory_Data source, Inventory_Data target)
        {
            JArray beforeSource = ElevatorInventoryReading(source), beforeTarget = ElevatorInventoryReading(target);
            bool accepted = SpaceElevatorDomain.TryPackCargo(source, target, out string reason);
            rejected.Add(new JObject { ["name"] = name, ["accepted"] = accepted, ["reason"] = reason,
                ["sourceUnchanged"] = JToken.DeepEquals(beforeSource, ElevatorInventoryReading(source)),
                ["targetUnchanged"] = JToken.DeepEquals(beforeTarget, ElevatorInventoryReading(target)) });
        }
        var packSource = ElevatorInventory("pack-source", new ItemSlot(0)
            { itemData = EntityProbeData("probe:pack-mass", 7f, 2f) }, new ItemSlot(1)
            { itemData = EntityProbeData("probe:pack-second", 1f) });
        var packLimited = ElevatorInventory("pack-limited", new ItemSlot(0), new ItemSlot(1));
        packLimited.ConfigureStorageContainerCapacity(6f, 100f);
        RejectPack("weight_limit", packSource, packLimited);
        var packVolume = ElevatorInventory("pack-volume", new ItemSlot(0), new ItemSlot(1));
        packVolume.ConfigureStorageContainerCapacity(100f, 4f);
        RejectPack("volume_limit", packSource, packVolume);
        var packBlocked = ElevatorInventory("pack-blocked", new ItemSlot(0), new ItemSlot(1));
        packBlocked.IsDepositBlocked = true;
        RejectPack("deposit_blocked", packSource, packBlocked);
        RejectPack("slot_limit", packSource, ElevatorInventory("pack-one-slot", new ItemSlot(0)));
        RejectPack("empty_source", ElevatorInventory("pack-empty", new ItemSlot(0)), packLimited);
        RejectPack("same_inventory", packSource, packSource);
        RejectPack("missing_source", null, packLimited);
        RejectPack("missing_target", packSource, null);
        RejectPack("fractional_amount", ElevatorInventory("pack-fractional", new ItemSlot(0)
            { itemData = EntityProbeData("probe:pack-fractional", 7.5f) }),
            ElevatorInventory("pack-fractional-target", new ItemSlot(0)));
        RejectPack("overflow_amount", ElevatorInventory("pack-overflow", new ItemSlot(0)
            { itemData = EntityProbeData("probe:pack-overflow", float.MaxValue) }),
            ElevatorInventory("pack-overflow-target", new ItemSlot(0) { SlotMaxVolume = float.MaxValue }));
        result["rejectedPacks"] = rejected;
        return result;
    }
    #endregion

    #region 电梯仓库拖拽访问守卫
    private static JObject SpaceElevatorDragGuard(JObject args)
    {
        JObject result = ElevatorScope("elevator_drag_guard");
        var source = ElevatorInventory("half-source", new ItemSlot(0)
            { itemData = EntityProbeData("probe:elevator-half", 7f) });
        var hand = ElevatorInventory("half-hand", new ItemSlot(0));
        result["split"] = source.TransferItemQuantityTo(source.itemSlots[0], hand, hand.itemSlots[0], 4);
        ItemData half = hand.itemSlots[0].itemData;
        int halfProviders = 0, halfReleases = 0;
        var halfDrag = new InventoryDragTransaction(half.Stack.Amount, (_, _) => false, null, null, false, true,
            () => { halfProviders++; return hand.itemSlots[0].itemData; }, () => halfReleases++).WithAccessGuard(() => true);
        result["halfPrepared"] = halfDrag.PrepareFallback();
        result["halfAmount"] = halfDrag.DraggedAmount;
        result["halfSourceHidden"] = halfDrag.HideSourceVisual;
        result["halfSourceIdentity"] = halfDrag.TryConsumeSourceItem(data => ReferenceEquals(data, half) &&
            !ReferenceEquals(data, source.itemSlots[0].itemData));
        halfDrag.Complete(true); halfDrag.Resolve();
        result["halfProviders"] = halfProviders; result["halfReleases"] = halfReleases;
        result["halfWarehouse"] = ElevatorInventoryReading(source); result["halfHand"] = ElevatorInventoryReading(hand);

        var denied = new JArray();
        foreach (bool prepared in new[] { false, true })
        {
            bool access = true;
            int dropCalls = 0, prepareCalls = 0, providerCalls = 0, consumeCalls = 0, releases = 0;
            var drag = new InventoryDragTransaction(4f, (_, _) => { dropCalls++; return true; },
                () => { prepareCalls++; return true; }, null, !prepared, prepared,
                () => { providerCalls++; return half; }, () => releases++).WithAccessGuard(() => access);
            access = false;
            bool dropped = drag.TryDrop(new Inventory(), 0), fallback = drag.PrepareFallback();
            bool consumed = drag.TryConsumeSourceItem(_ => { consumeCalls++; return true; });
            drag.Complete(true); drag.Complete(true); drag.Resolve();
            denied.Add(new JObject { ["prepared"] = prepared, ["dropped"] = dropped, ["fallback"] = fallback,
                ["consumed"] = consumed, ["dropCalls"] = dropCalls, ["prepareCalls"] = prepareCalls,
                ["providerCalls"] = providerCalls, ["consumeCalls"] = consumeCalls, ["releases"] = releases });
        }
        result["denied"] = denied;

        var equipment = EntityProbeData("probe:elevator-guard-equipment", 1f);
        equipment.Stack.Stackable = false;
        var dragSource = ElevatorInventory("guard-source", new ItemSlot(0) { itemData = equipment });
        var dragTarget = new Inventory { Data = ElevatorInventory("guard-target", new ItemSlot(0)) };
        bool firstAccess = true, secondAccess = false;
        int compositeDrops = 0, compositeReleases = 0;
        var composite = new InventoryDragTransaction(1f, (target, index) =>
        {
            compositeDrops++;
            return dragSource.TransferItemQuantityTo(dragSource.itemSlots[0], target.Data, target.Data.itemSlots[index], 1);
        }, null, null, true, onCompleted: () => compositeReleases++);
        composite.WithAccessGuard(() => firstAccess).WithAccessGuard(() => secondAccess);
        result["secondGuardDenied"] = !composite.TryDrop(dragTarget, 0);
        firstAccess = false; secondAccess = true;
        result["firstGuardDenied"] = !composite.TryDrop(dragTarget, 0);
        firstAccess = true;
        result["compositeDropped"] = composite.TryDrop(dragTarget, 0);
        result["compositeDuplicate"] = composite.TryDrop(dragTarget, 0);
        composite.Complete(true); composite.Resolve();
        result["compositeDrops"] = compositeDrops; result["compositeReleases"] = compositeReleases;
        result["compositeInstance"] = ReferenceEquals(equipment, dragTarget.Data.itemSlots[0].itemData) &&
            dragSource.itemSlots[0].itemData == null;

        int prepares = 0, directDrops = 0, fallbackDrops = 0, fallbackReleases = 0;
        var fallbackDrag = new InventoryDragTransaction(1f, (_, _) => { directDrops++; return true; },
            () => { prepares++; return true; }, (_, _) => { fallbackDrops++; return true; }, true,
            onCompleted: () => fallbackReleases++).WithAccessGuard(() => true);
        result["fallbackPrepared"] = fallbackDrag.PrepareFallback() && fallbackDrag.PrepareFallback();
        result["fallbackSourceHidden"] = fallbackDrag.HideSourceVisual;
        result["fallbackDropped"] = fallbackDrag.TryDrop(new Inventory(), 0);
        fallbackDrag.Complete(true); fallbackDrag.Resolve();
        result["fallbackPrepares"] = prepares; result["directDrops"] = directDrops;
        result["fallbackDrops"] = fallbackDrops; result["fallbackReleases"] = fallbackReleases;
        return result;
    }
    #endregion

    #region 正式快照与站台结构
    private static JObject SpaceElevatorSnapshot(JObject args)
    {
        var item = EntityProbeData("probe:elevator-snapshot", 7f);
        item.Guid = 24681;
        item.ItemSpecialData = "elevator-persisted-instance";
        var groundItem = EntityProbeData("probe:elevator-ground-snapshot", 4f);
        groundItem.Guid = 13579;
        groundItem.ItemSpecialData = "ground-warehouse-instance";
        var stationItem = EntityProbeData("probe:elevator-station-snapshot", 9f);
        stationItem.Guid = 86421;
        stationItem.ItemSpecialData = "station-warehouse-instance";
        var elevator = new SpaceElevatorState
        {
            ElevatorId = "saved-elevator", GroundTerminalId = "ground-terminal", StationTerminalId = "station-terminal",
            StationShipId = "saved-station", GroundWorldKey = "world:surface:elevator", GroundX = -15d, GroundY = 26d,
            RiderProfileId = "transient-rider", CargoDestination = SpaceElevatorEndpoint.Station,
            CargoSnapshots = new List<string> { SpaceSession.Encode(item), string.Empty },
            GroundWarehouseSnapshots = new List<string> { string.Empty, SpaceSession.Encode(groundItem) },
            StationWarehouseSnapshots = new List<string> { SpaceSession.Encode(stationItem), string.Empty }
        };
        SpaceElevatorDomain.TryBegin(elevator, SpaceElevatorEndpoint.Station, 12d, out _);
        SpaceElevatorDomain.Step(elevator, 5d);
        var state = new SpaceSessionState();
        state.Elevators.Add(elevator);
        state.Ships.Add(new ShipState { ShipId = elevator.StationShipId,
            Anchor = new ShipAnchorState { BodyId = "anchor-body", OffsetX = -100d, OffsetY = 200d } });
        string json = JsonConvert.SerializeObject(state);
        var restored = JsonConvert.DeserializeObject<SpaceSessionState>(json)
            ?? throw new InvalidOperationException("电梯会话快照读回为空。");
        SpaceElevatorState saved = restored.Elevators[0];
        ItemData savedItem = SpaceSession.Decode(saved.CargoSnapshots[0]);
        JObject result = ElevatorScope("elevator_snapshot");
        result["stableRoundtrip"] = JToken.DeepEquals(JToken.Parse(json), JToken.Parse(JsonConvert.SerializeObject(restored)));
        result["elevatorCount"] = restored.Elevators.Count;
        result["shipCount"] = restored.Ships.Count;
        result["beforeResume"] = ElevatorReading(saved);
        result["identities"] = new JArray(saved.ElevatorId, saved.GroundTerminalId, saved.StationTerminalId, saved.StationShipId, saved.GroundWorldKey);
        result["groundX"] = saved.GroundX; result["groundY"] = saved.GroundY;
        result["cargoSlots"] = saved.CargoSnapshots.Count;
        result["emptySlot"] = saved.CargoSnapshots[1];
        result["cargoBytesUnchanged"] = saved.CargoSnapshots[0] == elevator.CargoSnapshots[0];
        result["cargoId"] = savedItem.IDName; result["cargoGuid"] = savedItem.Guid;
        result["cargoAmount"] = savedItem.Stack.Amount; result["cargoSpecial"] = savedItem.ItemSpecialData;
        result["cargoDestination"] = saved.CargoDestination.ToString();
        result["groundWarehouseBytesUnchanged"] = saved.GroundWarehouseSnapshots[1] == elevator.GroundWarehouseSnapshots[1];
        result["stationWarehouseBytesUnchanged"] = saved.StationWarehouseSnapshots[0] == elevator.StationWarehouseSnapshots[0];
        var savedGround = ElevatorInventory("saved-ground", new ItemSlot(0), new ItemSlot(1)
            { itemData = SpaceSession.Decode(saved.GroundWarehouseSnapshots[1]) });
        var savedStation = ElevatorInventory("saved-station", new ItemSlot(0)
            { itemData = SpaceSession.Decode(saved.StationWarehouseSnapshots[0]) }, new ItemSlot(1));
        result["groundWarehouse"] = ElevatorInventoryReading(savedGround);
        result["stationWarehouse"] = ElevatorInventoryReading(savedStation);
        result["warehousesDistinct"] = !ReferenceEquals(saved.GroundWarehouseSnapshots, saved.StationWarehouseSnapshots) &&
            !ReferenceEquals(saved.GroundWarehouseSnapshots, saved.CargoSnapshots) &&
            !ReferenceEquals(saved.StationWarehouseSnapshots, saved.CargoSnapshots);
        result["riderRestored"] = !string.IsNullOrEmpty(saved.RiderProfileId);
        result["anchorBody"] = restored.Ships[0].Anchor.BodyId;
        result["anchorX"] = restored.Ships[0].Anchor.OffsetX; result["anchorY"] = restored.Ships[0].Anchor.OffsetY;
        result["resumedArrival"] = SpaceElevatorDomain.Step(saved, 7d);
        result["afterResume"] = ElevatorReading(saved);
        saved.DeliveryPending = true;
        var pendingRestored = JsonConvert.DeserializeObject<SpaceElevatorState>(JsonConvert.SerializeObject(saved));
        result["pendingRestored"] = pendingRestored.DeliveryPending && !pendingRestored.Travelling &&
            pendingRestored.CargoSnapshots[0] == elevator.CargoSnapshots[0] &&
            pendingRestored.CargoDestination == SpaceElevatorEndpoint.Station;
        return result;
    }

    private static ShipPieceState ElevatorFloor(string id, int x, bool isAnchor = false) => new()
    {
        PieceId = id, Kind = ShipPieceKind.Floor, CellX = x, MassKg = 10d,
        RequiresFloorSupport = false, ProtectedAnchor = isAnchor
    };

    private static JObject SpaceElevatorStation(JObject args)
    {
        var anchor = new ShipAnchorState { BodyId = "station-body", OffsetX = 100d, OffsetY = 200d };
        var ship = new ShipState { ShipId = "elevator-station", Anchor = anchor,
            IdentityAnchorPieceId = "station-core", PlatformKind = ShipPlatformKind.Space,
            PositionX = 100d, PositionY = 200d, CellSizeMeters = 1d };
        ShipPieceState protectedFloor = ElevatorFloor("station-core", 0, true);
        ship.Pieces.Add(protectedFloor);
        ShipStructureService.RebuildAndSplit(ship);
        ShipGeometry.LocalToWorld(ship, 0d, 0d, out double beforeX, out double beforeY);
        ship.Pieces.Add(ElevatorFloor("station-bridge", 1));
        ship.Pieces.Add(ElevatorFloor("station-extension", 2));
        ship.NotifyStructureChanged();
        List<ShipState> expanded = ShipStructureService.RebuildAndSplit(ship);
        ShipGeometry.LocalToWorld(ship, 0d, 0d, out double afterX, out double afterY);
        JObject result = ElevatorScope("elevator_station");
        result["expandedGroups"] = expanded.Count; result["expandedPieces"] = ship.Pieces.Count;
        result["expandedWreck"] = ship.IsWreck; result["anchorRetained"] = ReferenceEquals(anchor, ship.Anchor);
        result["originBefore"] = new JArray(beforeX, beforeY); result["originAfter"] = new JArray(afterX, afterY);
        result["anchorDamageAccepted"] = ShipStructureService.ApplyDamage(ship, protectedFloor.PieceId, 1000d, "elevator probe");
        result["anchorHealth"] = protectedFloor.Health;
        result["bridgeDamageAccepted"] = ShipStructureService.ApplyDamage(ship, "station-bridge", 1000d, "elevator probe");
        var fragments = new JArray();
        foreach (ShipState fragment in ShipStructureService.RebuildAndSplit(ship))
        {
            var ids = new JArray();
            foreach (ShipPieceState piece in fragment.Pieces) ids.Add(piece.PieceId);
            fragments.Add(new JObject { ["id"] = fragment.ShipId, ["anchored"] = fragment.Anchor != null,
                ["wreck"] = fragment.IsWreck, ["pieces"] = ids });
        }
        result["fragments"] = fragments;
        return result;
    }
    #endregion
}
