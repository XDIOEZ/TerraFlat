using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 便携气罐缓存规则采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterPortableFluidTankRules()
    {
        Register("industrial_fluids", "block_tank_stacking", WorldBlockTankStacking);
        Register("industrial_fluids", "portable_tank_cache", WorldPortableTankCache);
        Register("industrial_fluids", "portable_tank_payload", WorldPortableTankPayload);
        Register("industrial_fluids", "portable_tank_air_fill", WorldPortableTankAirFill);
        Register("industrial_fluids", "portable_tank_placement", WorldPortableTankPlacement);
        Register("industrial_fluids", "cold_temperature_path", WorldColdTemperaturePath);
        Register("industrial_fluids", "cold_wind_cache", WorldColdWindCache);
    }

    #region 方块气罐堆叠规则采样
    private static JObject WorldBlockTankStacking(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
        var machines = JsonConvert.DeserializeObject<MachineCatalogDocument>(
            Resources.Load<TextAsset>("Config/Mechanical/mechanical-catalog").text);
        var rows = new JArray();
        // 只使用正式目录的独立物品和快照，不登记或改动玩家世界中的储罐。
        foreach (string id in new[] { "GasTankBlock_Iron", "GasTankBlock_Iron_Summoner",
            "GasTankBlock_Steel", "GasTankBlock_Steel_Summoner", "PortableGasTank_Iron", "PortableGasTank_Steel" })
        {
            ItemDefinitionDto definition = definitions.Find(value => value.Id == id)
                ?? throw new InvalidOperationException("缺少气罐定义：" + id);
            Data_GeneralItem first = EntityCraftingDefinitionData(definition, 2);
            Data_GeneralItem second = EntityCraftingDefinitionData(definition, 3);
            var row = new JObject { ["id"] = id, ["stackable"] = first.Stack.Stackable,
                ["canStack"] = first.CanStackWith(second) };
            if (id.StartsWith("GasTankBlock_", StringComparison.Ordinal))
            {
                string bodyId = id.EndsWith("_Summoner", StringComparison.Ordinal) ? id[..^9] : id;
                var node = new MachineEntity { Definition = machines.Nodes.Find(value => value.Id == bodyId) };
                var original = new FluidMachineState { BodyTemperatureInitialized = true,
                    BodyTemperatureCelsius = 120, GroupOwnerId = 987, RandomState = 97, Status = "测试运行记录" };
                original.Chambers["main"] = new FluidInventoryState();
                MachinePersistence.Write(first, "fluid", original);
                MachinePersistence.Write(second, "fluid", new FluidMachineState { BodyTemperatureInitialized = true,
                    BodyTemperatureCelsius = -20, GroupOwnerId = 654, RandomState = 23 });
                MachineWorld.PrepareDismantledFluidSnapshot(node, first);
                MachineWorld.PrepareDismantledFluidSnapshot(node, second);
                FluidMachineState prepared = MachinePersistence.Read<FluidMachineState>(first, "fluid");
                var inventory = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = first } }, "block-tank-stack");
                row["repackedCanStack"] = first.CanStackWith(second);
                row["added"] = inventory.TryAddItem(second, true, out float added);
                row["addedAmount"] = added;
                row["amount"] = first.Stack.Amount;
                row["empty"] = prepared.Chambers.Count == 0;
                row["bodyTemperatureInitialized"] = prepared.BodyTemperatureInitialized;
                row["groupOwnerId"] = prepared.GroupOwnerId;
                row["randomState"] = prepared.RandomState;
                row["sourceUnchanged"] = original.Chambers.Count == 1 && original.GroupOwnerId == 987;
            }
            rows.Add(row);
        }
        return new JObject { ["tanks"] = rows };
    }
    #endregion

    // 只读正式继承目录中的两种气罐配对和共享能力，不创建当前世界物品。
    private static JObject WorldPortableTankPlacement(JObject args)
    {
        List<ItemDefinitionDto> definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
        var tanks = new JArray();
        foreach (string carrier in new[] { "PortableGasTank_Iron", "PortableGasTank_Steel" })
        foreach (string id in new[] { carrier, carrier + "_Building" })
        {
            ItemDefinitionDto definition = definitions.Find(value => value.Id == id)
                ?? throw new InvalidOperationException("缺少便携气罐定义：" + id);
            ItemModuleDefinitionDto building = definition.Modules[ModText.Building];
            Mod_Building.Building_Data state = new();
            building.Data.ToObject<Ex_ModData>().ReadData(ref state);
            ItemModuleDefinitionDto tank = definition.Modules["tank"];
            tanks.Add(new JObject
            {
                ["id"] = id, ["enabled"] = building.Enabled != false && tank.Enabled != false,
                ["role"] = (int)state.Role, ["bodyId"] = state.BuildingPrefabId,
                ["carrierId"] = state.SummonerPrefabId,
                ["sharedModules"] = new JArray(state.SharedModuleIds ?? Array.Empty<string>()),
                ["explicitPlacement"] = building.Parameters?.Value<bool?>("RequiresPlacementRequest") == true,
                ["stackable"] = definition.Stackable == true,
                ["canBePickedUp"] = definition.CanBePickedUp != false,
                ["maxHp"] = definition.Health?.MaxHp ?? 0f,
                ["configuration"] = tank.Parameters["Configuration"].DeepClone()
            });
        }
        return new JObject { ["tanks"] = tanks };
    }

    // 独立物品使用不存在于目录的身份，规则采样不加载或改写当前资源会话。
    private static ItemData WorldPortableTankItem(out Ex_ModData_MemoryPackable binary)
    {
        binary = new Ex_ModData_MemoryPackable
        {
            ModuleId = Mod_FluidTank.ModuleId,
            StableName = "system-probe:portable_tank"
        };
        return new Data_GeneralItem
        {
            IDName = "system-probe:portable_tank",
            ModuleDataDic = new Dictionary<string, ModuleData>(StringComparer.Ordinal)
            {
                [binary.StableName] = binary
            }
        };
    }

    private static JObject WorldPortableTankCache(JObject args)
    {
        ItemData item = WorldPortableTankItem(out Ex_ModData_MemoryPackable binary);
        bool found = FluidTankStorage.TryGet(item, out FluidInventory first, out FluidTankConfiguration firstConfig);
        bool foundAgain = FluidTankStorage.TryGet(item, out FluidInventory second, out FluidTankConfiguration secondConfig);
        var result = new JObject
        {
            ["found"] = found && foundAgain,
            ["wrapperReused"] = ReferenceEquals(first, second),
            ["wrapsOwnedContents"] = ReferenceEquals(first?.State, FluidTankStorage.Ensure(item).Contents),
            ["configCopies"] = !ReferenceEquals(firstConfig, secondConfig)
        };
        firstConfig.VolumeLiters = 99d;
        firstConfig.SupportsSpacesuit = false;
        firstConfig.AllowManualAirFill = true;
        FluidTankStorage.TryGet(item, out _, out FluidTankConfiguration afterExternalEdit);
        ItemData other = WorldPortableTankItem(out _);
        FluidTankStorage.TryGet(other, out FluidInventory otherInventory, out FluidTankConfiguration otherConfig);
        result["volumeAfterExternalEdit"] = afterExternalEdit.VolumeLiters;
        result["supportsSpacesuitAfterExternalEdit"] = afterExternalEdit.SupportsSpacesuit;
        result["manualFillAfterExternalEdit"] = afterExternalEdit.AllowManualAirFill;
        result["otherVolume"] = otherConfig.VolumeLiters;
        result["otherSupportsSpacesuit"] = otherConfig.SupportsSpacesuit;
        result["itemsIsolated"] = !ReferenceEquals(first, otherInventory) &&
                                  !ReferenceEquals(afterExternalEdit, otherConfig);

        FluidTankState owned = FluidTankStorage.Ensure(item);
        owned.Contents = new FluidInventoryState();
        FluidTankStorage.TryGet(item, out FluidInventory afterContentsReplacement, out _);
        result["contentsReplacementRebound"] = !ReferenceEquals(first, afterContentsReplacement) &&
                                               ReferenceEquals(owned.Contents, afterContentsReplacement.State);
        result["readContentsUsesOwnedState"] = FluidTankStorage.TryReadContents(item, out FluidInventoryState contents) &&
                                               ReferenceEquals(contents, owned.Contents);
        FluidTankStorage.Flush(item);
        byte[] stableBytes = binary.BitData;
        FluidTankStorage.Flush(item);
        result["unchangedFlushReusesBytes"] = ReferenceEquals(stableBytes, binary.BitData);

        owned.OverpressureClock = .25f;
        FluidTankStorage.Flush(item);
        byte[] clockBytes = binary.BitData;
        result["clockChangeWritesBytes"] = !ReferenceEquals(stableBytes, clockBytes);
        result["persistedClock"] = binary.GetData<FluidTankState>().OverpressureClock;
        FluidTankStorage.Flush(item);
        result["unchangedClockFlushReusesBytes"] = ReferenceEquals(clockBytes, binary.BitData);
        owned.Ruptured = true;
        FluidTankStorage.Flush(item);
        result["persistedRuptured"] = binary.GetData<FluidTankState>().Ruptured;
        result["rupturedTankRejected"] = !FluidTankStorage.TryGet(item, out _, out _);

        binary.WriteData(new FluidTankState());
        FluidTankStorage.TryGet(item, out FluidInventory afterPayloadReplacement, out _);
        result["payloadReplacementRebound"] = !ReferenceEquals(afterContentsReplacement, afterPayloadReplacement) &&
                                              ReferenceEquals(afterPayloadReplacement.State, FluidTankStorage.Ensure(item).Contents);
        result["payloadReplacementClock"] = FluidTankStorage.Ensure(item).OverpressureClock;
        result["payloadReplacementRuptured"] = FluidTankStorage.Ensure(item).Ruptured;
        byte[] replacementBytes = binary.BitData;
        FluidTankStorage.Flush(item);
        result["loadedPayloadFlushReusesBytes"] = ReferenceEquals(replacementBytes, binary.BitData);

        var replacementBinary = new Ex_ModData_MemoryPackable
        {
            ModuleId = Mod_FluidTank.ModuleId,
            StableName = binary.StableName
        };
        replacementBinary.WriteData(new FluidTankState());
        item.ModuleDataDic[binary.StableName] = replacementBinary;
        FluidTankStorage.TryGet(item, out FluidInventory afterBinaryReplacement, out _);
        result["binaryReplacementRebound"] = !ReferenceEquals(afterPayloadReplacement, afterBinaryReplacement) &&
                                             ReferenceEquals(afterBinaryReplacement.State, FluidTankStorage.Ensure(item).Contents);
        return result;
    }

    private static JObject WorldPortableTankPayload(JObject args)
    {
        FluidCatalog catalog = WorldFluidCatalog();
        FluidInventory inventory = WorldFilledFluid(catalog, 2m, 1m);
        ItemData item = WorldPortableTankItem(out Ex_ModData_MemoryPackable binary);
        double pressureBefore = inventory.GetPressureKPa(4d, .04d);
        FluidTankStorage.Write(item, inventory);
        FluidInventory decoded = new(binary.GetData<FluidTankState>().Contents, catalog);
        byte[] firstBytes = binary.BitData;
        var result = new JObject
        {
            ["writesOwnedState"] = ReferenceEquals(inventory.State, FluidTankStorage.Ensure(item).Contents),
            ["initialGasMoles"] = (double)decoded.GetGasMoles("system-probe:fluid"),
            ["initialLiquidMoles"] = (double)decoded.GetLiquidMoles("system-probe:fluid"),
            ["initialKelvin"] = decoded.GetTemperatureKelvin(),
            ["pressureBefore"] = pressureBefore,
            ["initialPressure"] = decoded.GetPressureKPa(4d, .04d)
        };
        FluidTankStorage.Write(item, inventory);
        result["unchangedWriteReusesBytes"] = ReferenceEquals(firstBytes, binary.BitData);
        result["taken"] = inventory.TryTakeExact("system-probe:fluid", FluidPhase.Gas, .5m, out FluidBatch batch);
        result["takenGasMoles"] = (double)batch.GasMoles;
        FluidTankStorage.Flush(item);
        byte[] changedBytes = binary.BitData;
        result["contentChangeWritesBytes"] = !ReferenceEquals(firstBytes, changedBytes);
        decoded = new FluidInventory(binary.GetData<FluidTankState>().Contents, catalog);
        result["remainingGasMoles"] = (double)decoded.GetGasMoles("system-probe:fluid");
        result["remainingLiquidMoles"] = (double)decoded.GetLiquidMoles("system-probe:fluid");
        result["remainingKelvin"] = decoded.GetTemperatureKelvin();
        result["remainingPressure"] = decoded.GetPressureKPa(4d, .04d);
        result["expectedPressure"] = inventory.GetPressureKPa(4d, .04d);
        FluidTankStorage.Flush(item);
        result["unchangedContentFlushReusesBytes"] = ReferenceEquals(changedBytes, binary.BitData);
        return result;
    }

    // 使用真实星球大气库存适配，分别验证混合气、有限来源、已有库存和回滚。
    private static JObject WorldPortableTankAirFill(JObject args)
    {
        static AtmosphereState Air(double pressure) => new()
        {
            DefinitionId = "system-probe:air",
            CapacityMoles = 20m,
            ReferenceMoles = 10m,
            ReferencePressureKPa = pressure,
            TemperatureKelvin = 293.15d,
            Gases = new List<AtmosphereGasAmount>
            {
                new() { FluidId = FluidIds.Oxygen, Moles = 2.1m },
                new() { FluidId = FluidIds.Nitrogen, Moles = 7.9m }
            }
        };
        const double volume = 4d, minimum = .04d;
        var air = Air(PortableGasTankAirFilling.TargetPressureKPa);
        var target = new FluidInventory();
        decimal gasBefore = air.CurrentMoles;
        bool filled = PortableGasTankAirFilling.TryFillFromSource(
            new PlanetShipAtmosphereSource(air, 20d), target, volume, minimum);
        decimal airAfter = air.CurrentMoles;
        double pressure = target.GetPressureKPa(volume, minimum);
        decimal oxygen = target.GetGasMoles(FluidIds.Oxygen);
        decimal nitrogen = target.GetGasMoles(FluidIds.Nitrogen);
        bool filledAgain = PortableGasTankAirFilling.TryFillFromSource(
            new PlanetShipAtmosphereSource(air, 20d), target, volume, minimum);

        var vacuum = Air(0d);
        foreach (AtmosphereGasAmount component in vacuum.Gases) component.Moles = 0m;
        var empty = new FluidInventory();
        bool vacuumFilled = PortableGasTankAirFilling.TryFillFromSource(
            new PlanetShipAtmosphereSource(vacuum, 20d), empty, volume, minimum);
        var thinAir = Air(60d);
        var thinTarget = new FluidInventory();
        decimal thinBefore = thinAir.CurrentMoles;
        bool thinFilled = PortableGasTankAirFilling.TryFillFromSource(
            new PlanetShipAtmosphereSource(thinAir, 20d), thinTarget, volume, minimum);

        // 默认星球只有 100 kPa，按钮资格和实际补气必须共同接受此环境。
        var normalAir = Air(100d);
        var normalSource = new PlanetShipAtmosphereSource(normalAir, 20d);
        var normalTarget = new FluidInventory();
        decimal normalBefore = normalAir.CurrentMoles;
        bool normalCanFill = PortableGasTankAirFilling.CanFillFromSource(normalSource, normalTarget, volume, minimum);
        bool normalFilled = PortableGasTankAirFilling.TryFillFromSource(normalSource, normalTarget, volume, minimum);
        decimal normalAfter = normalAir.CurrentMoles;
        bool normalCanFillAgain = PortableGasTankAirFilling.CanFillFromSource(normalSource, normalTarget, volume, minimum);
        bool normalFilledAgain = PortableGasTankAirFilling.TryFillFromSource(normalSource, normalTarget, volume, minimum);
        bool higherPressureRejected = !PortableGasTankAirFilling.CanFillFromSource(
            new PlanetShipAtmosphereSource(thinAir, 20d), target, volume, minimum);

        var occupied = new FluidInventory();
        FluidCatalog catalog = FluidCatalog.Default;
        bool seededGas = occupied.TryAdd(FluidInventory.CreateBatch(
            catalog.Find(FluidIds.CarbonDioxide), .012m, 0m, 283.15d), volume, minimum);
        bool seededLiquid = occupied.TryAdd(FluidInventory.CreateBatch(
            catalog.Find(FluidIds.PureWater), 0m, 10m, 283.15d), volume, minimum);
        decimal originalCarbon = occupied.GetGasMoles(FluidIds.CarbonDioxide);
        decimal originalWater = occupied.GetLiquidMoles(FluidIds.PureWater);
        double originalEnergy = occupied.State.TotalInternalEnergyJoules;
        double liquidSpace = occupied.GetLiquidLiters();
        var occupiedAir = Air(PortableGasTankAirFilling.TargetPressureKPa);
        decimal occupiedBefore = occupiedAir.CurrentMoles;
        bool mixedFilled = PortableGasTankAirFilling.TryFillFromSource(
            new PlanetShipAtmosphereSource(occupiedAir, 20d), occupied, volume, minimum);
        var room = new FluidInventory();
        bool roomOxygen = room.TryAdd(FluidInventory.CreateBatch(
            catalog.Find(FluidIds.Oxygen), .21m, 0m, 310d), 20d, .001d);
        bool roomNitrogen = room.TryAdd(FluidInventory.CreateBatch(
            catalog.Find(FluidIds.Nitrogen), .79m, 0m, 310d), 20d, .001d);
        double roomEnergyBefore = room.State.TotalInternalEnergyJoules;
        decimal roomMolesBefore = room.GasMoles;
        var cabinTank = new FluidInventory();
        bool roomFilled = PortableGasTankAirFilling.TryFillFromSource(
            new TankShipAtmosphereSource(room, 20d, .001d), cabinTank, volume, minimum);
        var tinyRoom = new FluidInventory();
        tinyRoom.TryAdd(FluidInventory.CreateBatch(
            catalog.Find(FluidIds.Oxygen), .01m, 0m, 310d), .1d, .001d);
        decimal tinyBefore = tinyRoom.GasMoles;
        var partialTank = new FluidInventory();
        bool partialFilled = PortableGasTankAirFilling.TryFillFromSource(
            new TankShipAtmosphereSource(tinyRoom, .1d, .001d), partialTank, volume, minimum);
        return new JObject
        {
            ["filled"] = filled, ["pressure"] = pressure,
            ["oxygen"] = (double)oxygen, ["nitrogen"] = (double)nitrogen,
            ["sourceConserved"] = airAfter + target.GasMoles == gasBefore,
            ["filledAgain"] = filledAgain,
            ["againSourceUnchanged"] = air.CurrentMoles == airAfter,
            ["temperature"] = target.GetTemperatureKelvin(),
            ["vacuumFilled"] = vacuumFilled, ["vacuumTargetEmpty"] = empty.IsEmpty,
            ["thinFilled"] = thinFilled,
            ["thinPressure"] = thinTarget.GetPressureKPa(volume, minimum),
            ["thinConserved"] = thinAir.CurrentMoles + thinTarget.GasMoles == thinBefore,
            ["normalCanFill"] = normalCanFill, ["normalFilled"] = normalFilled,
            ["normalPressure"] = normalTarget.GetPressureKPa(volume, minimum),
            ["normalConserved"] = normalAfter + normalTarget.GasMoles == normalBefore,
            ["normalCanFillAgain"] = normalCanFillAgain, ["normalFilledAgain"] = normalFilledAgain,
            ["normalRepeatUnchanged"] = normalAir.CurrentMoles == normalAfter,
            ["higherPressureRejected"] = higherPressureRejected,
            ["seededGas"] = seededGas, ["seededLiquid"] = seededLiquid, ["mixedFilled"] = mixedFilled,
            ["carbonPreserved"] = occupied.GetGasMoles(FluidIds.CarbonDioxide) == originalCarbon,
            ["liquidPreserved"] = occupied.GetLiquidMoles(FluidIds.PureWater) == originalWater,
            ["liquidSpacePreserved"] = Math.Abs(occupied.GetLiquidLiters() - liquidSpace) < .0000001d,
            ["mixedPressure"] = occupied.GetPressureKPa(volume, minimum),
            ["mixedGasAdded"] = occupied.GetGasMoles(FluidIds.Oxygen) > 0m &&
                                occupied.GetGasMoles(FluidIds.Nitrogen) > 0m,
            ["mixedConserved"] = occupiedBefore - occupiedAir.CurrentMoles ==
                                occupied.GetGasMoles(FluidIds.Oxygen) + occupied.GetGasMoles(FluidIds.Nitrogen),
            ["existingEnergyPreserved"] = occupied.State.TotalInternalEnergyJoules > originalEnergy,
            ["roomSeeded"] = roomOxygen && roomNitrogen, ["roomFilled"] = roomFilled,
            ["roomMolesConserved"] = room.GasMoles + cabinTank.GasMoles == roomMolesBefore,
            ["roomEnergyConserved"] = Math.Abs(room.State.TotalInternalEnergyJoules +
                cabinTank.State.TotalInternalEnergyJoules - roomEnergyBefore) < .00001d,
            ["roomMixedTemperature"] = cabinTank.GetTemperatureKelvin(),
            ["partialFilled"] = partialFilled,
            ["partialConserved"] = tinyRoom.GasMoles + partialTank.GasMoles == tinyBefore,
            ["partialPressure"] = partialTank.GetPressureKPa(volume, minimum),
            ["unsupportedTankExcluded"] = !FluidTankStorage.AllowsManualAirFill(WorldPortableTankItem(out _)),
            ["nullTankExcluded"] = !FluidTankStorage.AllowsManualAirFill(null)
        };
    }

    #endregion

    #region 冷机器地理温度的正式点采样
    /// <summary>验证冷区块被逐出后固定气候缓存仍按原点提供风力且保持有界。</summary>
    private static JObject WorldColdWindCache(JObject args)
    {
        var cache = new SpaceSurfaceQuery.ColdWindStrengthCache(3);
        var a = new Int2(-16, -8);
        var b = new Int2(0, -8);
        var c = new Int2(16, -8);
        var d = new Int2(32, -8);
        cache.Store(a, .25f);
        cache.Store(b, .5f);
        cache.Store(c, .75f);
        bool initial = cache.TryGet(a, out float first) && Mathf.Approximately(first, .25f) && cache.Count == 3;
        cache.Store(b, .6f);
        bool replaced = cache.TryGet(b, out float updated) && Mathf.Approximately(updated, .6f) && cache.Count == 3;
        cache.Store(d, 1f);
        bool evicted = !cache.TryGet(a, out _) && cache.TryGet(c, out float retained) &&
            Mathf.Approximately(retained, .75f) && cache.Count == 3;
        bool missing = !cache.TryGet(new Int2(-32, -8), out _);
        var small = new SpaceSurfaceQuery.ColdWindStrengthCache(0);
        small.Store(a, .25f);
        small.Store(b, .5f);
        bool bounded = small.Count == 1 && !small.TryGet(a, out _) &&
            small.TryGet(b, out float last) && Mathf.Approximately(last, .5f);
        return new JObject
        {
            ["initial"] = initial, ["replaced"] = replaced,
            ["evicted"] = evicted, ["missing"] = missing, ["bounded"] = bounded
        };
    }

    // 点读与完整生成都调用生产入口，只返回真实环境层及查询值给 Lua 对照。
    private static JObject WorldColdTemperaturePath(JObject args)
    {
        var groups = new JArray();
        foreach (string mode in new[] { "simple", "legacy", "space", "uniform" })
        {
            var numbers = new Dictionary<string, double>
            {
                ["river.enabled"] = 0d, ["airHumidity.radiusTiles"] = 1d,
                ["airHumidity.waterContribution"] = 0d, ["climate.temperature.blendRadius"] = 2d
            };
            var texts = new Dictionary<string, string> { ["climate.algorithm"] = mode == "legacy" ? "legacy" : "simple" };
            if (mode is "space" or "uniform")
            {
                texts["space.bodyId"] = "system-probe:cold-body";
                numbers[mode == "space" ? "space.climateOverride" : "space.uniformSurface"] = 1d;
                numbers["space.surfaceTemperatureCelsius"] = -40d;
                numbers["space.temperatureVariationCelsius"] = 17d;
            }
            var profile = new ChunkGenerationProfileSnapshot("system-probe:cold-temperature-" + mode, 1, 4, 4,
                numbers, texts, ecologyGlobalMultiplier: 0d);
            var topology = new ChunkGenerationTopologySnapshot(new Int2(-16, -16), new Int2(32, 32));
            var address = new FlatWorld.WorldModel.WorldAddress(global::WorldAddress.SurfaceDimensionId, new Int2(12, -16));
            using var world = new WorldRuntime("system-probe:cold-temperature-" + mode, 1);
            var request = world.BeginChunkGeneration(address, 17413, profile, topology);
            var generator = new DeterministicChunkGenerator(terrainDecorator: SpaceSurfaceTerrain.Apply);
            using var generated = generator.Generate(request, CancellationToken.None);
            bool committed = world.TryCommit(generated, out string reason);
            var samples = new JArray();
            if (committed && world.TryGetChunkTerrain(address, out ChunkTerrainData terrain))
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    int worldX = address.ChunkOrigin.X + x, worldY = address.ChunkOrigin.Y + y;
                    bool found = terrain.TryGetEnvironmentValue("temperature.celsius", x, y, out float full);
                    samples.Add(new JObject
                    {
                        ["found"] = found, ["full"] = full,
                        ["point"] = SpaceSurfaceQuery.SampleGeneratedTemperature(request, worldX, worldY),
                        ["wrapped"] = SpaceSurfaceQuery.SampleGeneratedTemperature(request, worldX + 32, worldY - 32)
                    });
                }
            SpaceSurfaceQuery.SampleGeneratedTemperature(request, 12, -16);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 32; i++) SpaceSurfaceQuery.SampleGeneratedTemperature(request, 12, -16);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            groups.Add(new JObject { ["mode"] = mode, ["committed"] = committed, ["reason"] = reason,
                ["samples"] = samples, ["pointReadBytes"] = allocated });
            generator.BeginNewWorldHydrologyEpoch(1);
        }
        return new JObject { ["groups"] = groups };
    }
    #endregion
}
