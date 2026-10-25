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
        Register("industrial_fluids", "liquid_block_tanks", WorldLiquidBlockTanks);
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
                // 每个返还物独立保存本罐内容，不依赖网络组主或分摊副本。
                var firstContents = new FluidInventory();
                var secondContents = new FluidInventory();
                firstContents.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.Oxygen), .1m, 0m, 293.15), 100, .001);
                secondContents.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.Nitrogen), .2m, 0m, 293.15), 100, .001);
                var original = new FluidMachineState(); original.Chambers["main"] = firstContents.State;
                var other = new FluidMachineState(); other.Chambers["main"] = secondContents.State;
                MachinePersistence.Write(first, "fluid", original);
                MachinePersistence.Write(second, "fluid", other);
                var firstSaved = MachinePersistence.Read<FluidMachineState>(first, "fluid");
                var secondSaved = MachinePersistence.Read<FluidMachineState>(second, "fluid");
                var firstDecoded = new FluidInventory(firstSaved.Chambers["main"]);
                var secondDecoded = new FluidInventory(secondSaved.Chambers["main"]);
                row["independentPayloads"] = firstDecoded.GetGasMoles(FluidIds.Oxygen) == .1m &&
                    firstDecoded.GetGasMoles(FluidIds.Nitrogen) == 0m && secondDecoded.GetGasMoles(FluidIds.Nitrogen) == .2m &&
                    secondDecoded.GetGasMoles(FluidIds.Oxygen) == 0m;
                row["differentContentsCannotStack"] = !first.CanStackWith(second);
                row["sourceUnchanged"] = firstContents.GetGasMoles(FluidIds.Oxygen) == .1m && secondContents.GetGasMoles(FluidIds.Nitrogen) == .2m;
            }
            rows.Add(row);
        }
        return new JObject { ["tanks"] = rows };
    }
    #endregion

    #region 液罐方块配置与库存采样
    private static JObject WorldLiquidBlockTanks(JObject args)
    {
        var definitions = new Dictionary<string, ItemDefinitionDto>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
            if (!definition.Abstract) definitions.Add(definition.Id, definition);
        var machines = JsonConvert.DeserializeObject<MachineCatalogDocument>(
            Resources.Load<TextAsset>("Config/Mechanical/mechanical-catalog").text);
        string recipeRoot = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RecipeCatalogLoader.RelativeRecipeRoot);
        RecipeCatalogDto catalog = RecipeRuntimeFactory.Deserialize(StreamingAssetsTextLoader.ReadAllText(
            RecipeCatalogLoader.ResolvePackagePath(recipeRoot, "crafting/chemical_machines.json")));
        List<RuntimeRecipe> recipes = RecipeRuntimeFactory.BuildCatalog(catalog, definitions.ContainsKey, out List<string> warnings);
        var rows = new JArray();
        foreach (string id in new[] { "LiquidTankBlock_Iron", "LiquidTankBlock_Steel" })
        {
            MachineDefinition machine = machines.Nodes.Find(value => value.Id == id)
                ?? throw new InvalidOperationException("缺少液罐方块定义：" + id);
            machine.Validate();
            FluidMachineDefinition fluid = machine.Fluid;
            ItemDefinitionDto body = definitions[id], carrier = definitions[id + "_Summoner"];
            var pairs = new JArray();
            foreach (ItemDefinitionDto definition in new[] { body, carrier })
            {
                Mod_Building.Building_Data placement = new();
                definition.Modules[ModText.Building].Data.ToObject<Ex_ModData>().ReadData(ref placement);
                pairs.Add(new JObject { ["id"] = definition.Id, ["role"] = (int)placement.Role,
                    ["bodyId"] = placement.BuildingPrefabId, ["carrierId"] = placement.SummonerPrefabId,
                    ["machineId"] = definition.Modules["机械扭矩模块"].Parameters["DefinitionId"],
                    ["sprite"] = definition.Visual.SpriteAddress, ["maxHp"] = definition.Health.MaxHp });
            }
            // 独立库存复用正式数量事务，采样满罐、异种拒绝、出液及拆装载荷。
            var source = new FluidInventory(); var target = new FluidInventory(); var other = new FluidInventory();
            void AddLiquid(FluidInventory inventory, string fluidId, decimal liters)
                => inventory.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(fluidId), 0m,
                    FluidUnits.LiquidLitersToMol(FluidCatalog.Default.Find(fluidId), liters), 293.15), 1000, .001);
            AddLiquid(source, FluidIds.PureWater, 150); AddLiquid(other, FluidIds.FilteredWater, 5);
            double filled = IndustrialFluidTransfer.Transfer(source, target, FluidPhase.Liquid, 150,
                fluid.GasCapacityStandardLiters, fluid.VolumeLiters);
            double overflow = IndustrialFluidTransfer.Transfer(source, target, FluidPhase.Liquid, 1,
                fluid.GasCapacityStandardLiters, fluid.VolumeLiters);
            var delivered = new FluidInventory();
            double drained = IndustrialFluidTransfer.Transfer(target, delivered, FluidPhase.Liquid, 25, 100, 100);
            double mixed = IndustrialFluidTransfer.Transfer(other, target, FluidPhase.Liquid, 5,
                fluid.GasCapacityStandardLiters, fluid.VolumeLiters);
            Data_GeneralItem first = EntityCraftingDefinitionData(carrier, 1), second = EntityCraftingDefinitionData(carrier, 1);
            bool emptyStackable = first.CanStackWith(second);
            var state = new FluidMachineState(); state.Chambers["main"] = target.State;
            var otherState = new FluidMachineState(); otherState.Chambers["main"] = other.State;
            MachinePersistence.Write(first, "fluid", state); MachinePersistence.Write(second, "fluid", otherState);
            var restored = new FluidInventory(MachinePersistence.Read<FluidMachineState>(first, "fluid").Chambers["main"]);
            var restoredOther = new FluidInventory(MachinePersistence.Read<FluidMachineState>(second, "fluid").Chambers["main"]);
            RuntimeRecipe recipe = recipes.Find(value => value.Id == "core:chemistry_" + id)
                ?? throw new InvalidOperationException("缺少液罐方块配方：" + id);
            rows.Add(new JObject { ["id"] = id, ["kind"] = fluid.Kind, ["material"] = fluid.MaterialId,
                ["capacity"] = fluid.VolumeLiters, ["manualAirFill"] = fluid.AllowManualAirFill,
                ["ports"] = JArray.FromObject(fluid.Ports), ["pairs"] = pairs,
                ["stackable"] = carrier.Stackable == true && emptyStackable,
                ["differentContentsCannotStack"] = !first.CanStackWith(second),
                ["filled"] = filled, ["overflow"] = overflow, ["drained"] = drained, ["mixed"] = mixed,
                ["sourceLeft"] = source.GetLiquidLiters(), ["stored"] = restored.GetLiquidLiters(),
                ["otherStored"] = restoredOther.GetLiquidLiters(), ["delivered"] = delivered.GetLiquidLiters(),
                ["storedFluid"] = restored.State.Components[0].FluidId,
                ["otherFluid"] = restoredOther.State.Components[0].FluidId,
                ["crafted"] = EntityCraftingHandmadeAttempt(recipe, definitions, false, false),
                ["missing"] = EntityCraftingHandmadeAttempt(recipe, definitions, true, false),
                ["full"] = EntityCraftingHandmadeAttempt(recipe, definitions, false, true) });
        }
        return new JObject { ["tanks"] = rows, ["recipeWarnings"] = new JArray(warnings) };
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

        binary.WriteData(new FluidTankState());
        FluidTankStorage.TryGet(item, out FluidInventory afterPayloadReplacement, out _);
        result["payloadReplacementRebound"] = !ReferenceEquals(afterContentsReplacement, afterPayloadReplacement) &&
                                              ReferenceEquals(afterPayloadReplacement.State, FluidTankStorage.Ensure(item).Contents);
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
    private sealed class OverdrawAirSource : IShipAtmosphereSource
    {
        public double PressureKPa => 100;
        public double TemperatureCelsius => 20;
        public decimal Taken, Returned;
        public IReadOnlyList<FluidBatch> TakeGas(decimal amount)
        {
            Taken = amount * 2;
            return new[] { FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.Oxygen), Taken, 0m, 293.15d) };
        }
        public void ReleaseGas(FluidBatch gas) => Returned += gas.GasMoles;
    }

    private static JObject WorldPortableTankAirFill(JObject args)
    {
        static FluidInventory Air(decimal moles)
        {
            var result = new FluidInventory();
            result.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.Oxygen), moles * .21m, 0m, 293.15), 100, .001);
            result.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.Nitrogen), moles * .79m, 0m, 293.15), 100, .001);
            return result;
        }
        var sourceInventory = Air(10m);
        var source = new TankShipAtmosphereSource(sourceInventory, 100, .001);
        var target = new FluidInventory();
        decimal before = sourceInventory.GasMoles;
        bool filled = PortableGasTankAirFilling.TryFillFromSource(source, target, 20d, .2d);
        decimal after = sourceInventory.GasMoles;
        bool again = PortableGasTankAirFilling.TryFillFromSource(source, target, 20d, .2d);
        var vacuum = new FluidInventory();
        var vacuumTarget = new FluidInventory();
        bool vacuumFilled = PortableGasTankAirFilling.TryFillFromSource(new TankShipAtmosphereSource(vacuum, 100, .001), vacuumTarget, 20, .2);
        var partialSource = Air(.001m);
        var partialTarget = new FluidInventory();
        bool partial = PortableGasTankAirFilling.TryFillFromSource(new TankShipAtmosphereSource(partialSource, 1, .001), partialTarget, 20, .2);
        var high = Air(1m);
        decimal highBefore = high.GasMoles, sourceBeforeHigh = sourceInventory.GasMoles;
        bool highFilled = PortableGasTankAirFilling.TryFillFromSource(source, high, 20, .2);
        var tiny = new FluidInventory();
        decimal tinyBefore = sourceInventory.GasMoles;
        bool tinyFilled = PortableGasTankAirFilling.TryFillFromSource(source, tiny, .0001, .2);
        var bad = new OverdrawAirSource(); var rejected = new FluidInventory();
        bool badFilled = PortableGasTankAirFilling.TryFillFromSource(bad, rejected, 20, .2);
        return new JObject
        {
            ["filled"] = filled, ["standardLiters"] = (double)FluidUnits.MolToStandardLiters(target.GasMoles),
            ["oxygenRatio"] = (double)(target.GetGasMoles(FluidIds.Oxygen) / target.GasMoles),
            ["conserved"] = before == sourceInventory.GasMoles + target.GasMoles + tiny.GasMoles,
            ["repeatRejected"] = !again && after == tinyBefore,
            ["vacuumRejected"] = !vacuumFilled && vacuumTarget.GasMoles == 0 && vacuumTarget.LiquidMoles == 0,
            ["partial"] = partial && partialSource.GasMoles + partialTarget.GasMoles == .001m,
            ["highPreserved"] = !highFilled && high.GasMoles == highBefore && sourceBeforeHigh == tinyBefore,
            ["tinyFilled"] = tinyFilled, ["tinyLiters"] = (double)FluidUnits.MolToStandardLiters(tiny.GasMoles),
            ["tinyConserved"] = tinyBefore == sourceInventory.GasMoles + tiny.GasMoles,
            ["rollback"] = !badFilled && rejected.GasMoles == 0 && rejected.LiquidMoles == 0 && bad.Taken == bad.Returned,
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
