using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using MemoryPack;
using UnityEngine;

internal static class Program
{
    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception(label);
    }

    public static void Main()
    {
        var player = new Data_Player
        {
            IDName = "test:player", Guid = 17, Name_User = "玩家", CurrentSceneName = "星球",
            hp = new Hp(42) { maxValue = 55, Weaknesses = new List<string> { "fire" } },
            defense = new Defense(1, 2, 3, 4),
            Speed = new GameValue_float { BaseValue = 2, BaseAdditive = 3, AdditiveModifier = .2f,
                MultiplicativeModifier = 1.5f, FinalAdditive = 4 },
            transform = new ItemTransform { position = new Vector3(4, -5, 6),
                rotation = new Quaternion(.1f, .2f, .3f, .4f), scale = new Vector3(2, 3, 4) },
            MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 33, Moisture = .2f, IsBurning = true, CombustionElapsedSeconds = 9 },
            PhysicsState = new ItemPhysicsRuntimeState { Velocity = new Vector2(7, -8), AngularVelocity = 3, PersistentWorldResident = true },
            Stack = new ItemStack { Amount = 1 }, MaxDurability = 1, Durability = 1
        };
        var item = ItemInstanceSnapshot.Capture(player);
        var frozen = MemoryPackSerializer.Serialize(item);
        var golden = MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(System.IO.File.ReadAllBytes("Tools/Tests/SaveArchiveContracts/item-v1.bin"));
        var goldenPlayer = (Data_Player)golden.CreateColdData();
        Check(goldenPlayer.Guid == 17 && goldenPlayer.hp.Value == 42 && goldenPlayer.MatterState.TemperatureCelsius == 33 &&
            goldenPlayer.transform.position == new Vector3(4, -5, 6), "stored v1 player fixture decodes");
        var chunk = new SqliteChunkSnapshot
        {
            Terrain = new byte[] { 1, 2, 3 },
            Ecology = new FrozenEcologyChunkSaveRecord
            { WorldKey = "世界", ChunkData = new byte[] { 4, 5 }, ChangedItems = new List<byte[]> { frozen } }
        };
        var goldenChunk = MemoryPackSerializer.Deserialize<SqliteChunkSnapshot>(System.IO.File.ReadAllBytes("Tools/Tests/SaveArchiveContracts/chunk-v1.bin"));
        Check(goldenChunk.Terrain[1] == 2 && goldenChunk.Ecology.WorldKey == "世界" &&
            ((Data_Player)MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(goldenChunk.Ecology.ChangedItems[0]).CreateColdData()).Guid == 17,
            "stored v1 chunk, ecology and embedded item fixture decode");
        var currentChunk = MemoryPackSerializer.Deserialize<SqliteChunkSnapshot>(MemoryPackSerializer.Serialize(chunk));
        Check(currentChunk.Terrain[2] == 3 && currentChunk.Ecology.ChunkData[1] == 5,
            "chunk and ecology current roundtrip");
        var restored = (Data_Player)MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(frozen).CreateColdData();
        Check(restored.Name_User == "玩家" && restored.CurrentSceneName == "星球" && restored.hp.Value == 42 &&
            restored.hp.maxValue == 55 && restored.hp.Weaknesses[0] == "fire", "player health and identity roundtrip");
        Check(restored.defense.Cutting == 1 && restored.defense.Blunt == 4 && restored.Speed.BaseAdditive == 3 &&
            restored.Speed.MultiplicativeModifier == 1.5f, "player defense and speed roundtrip");
        Check(restored.transform.position == new Vector3(4, -5, 6) && restored.transform.scale == new Vector3(2, 3, 4) &&
            restored.PhysicsState.Velocity == new Vector2(7, -8), "primitive transform roundtrip");
        Check(restored.MatterState.TemperatureCelsius == 33 && restored.MatterState.IsBurning &&
            restored.MatterState.CombustionElapsedSeconds == 9, "matter roundtrip");

        var payloadField = typeof(ItemInstanceSnapshot).GetField("matterPayload", BindingFlags.NonPublic | BindingFlags.Instance);
        payloadField.SetValue(item, MemoryPackSerializer.Serialize(new MatterOnlyInitialized { Initialized = true }));
        var defaultMatter = item.CreateColdData().MatterState;
        Check(defaultMatter.Initialized && defaultMatter.TemperatureCelsius == 20f, "missing new matter field uses declared default");
        payloadField.SetValue(item, MemoryPackSerializer.Serialize(new MatterWithFutureField
        { Initialized = true, TemperatureCelsius = 44, Moisture = .5f, IsBurning = true, CombustionElapsedSeconds = 8, Future = "retained elsewhere" }));
        Check(item.CreateColdData().MatterState.TemperatureCelsius == 44, "future matter field is skipped");

        var module = new Ex_ModData_MemoryPackable { StableName = "example", ModuleId = "test:example" };
        ExpectFailure(() => module.WriteData(new RuntimeStruct { Value = 3 }), "raw unmanaged layout rejected");
        using (ModuleBinaryPayloadCodecs.Register(new RuntimeStructCodec()))
        {
            module.WriteData(new RuntimeStruct { Value = 77 });
            Check(module.GetData<RuntimeStruct>().Value == 77, "explicit struct codec roundtrip");
            var envelope = MemoryPackSerializer.Deserialize<TestEnvelope>(module.BitData);
            Check(envelope.SchemaId == "test.struct" && envelope.SchemaVersion == 1, "codec schema and version encoded");
            envelope.SchemaVersion = 2;
            module.BitData = MemoryPackSerializer.Serialize(envelope);
            ExpectFailure(() => module.GetData<RuntimeStruct>(), "future module version rejected");
        }
        byte[] unknown = (byte[])module.BitData.Clone();
        ExpectFailure(() => module.GetData<RuntimeStruct>(), "removed codec rejects raw access");
        Check(Convert.ToBase64String(module.BitData) == Convert.ToBase64String(unknown) &&
            ModuleInstanceSnapshot.Capture(module).Matches(module), "unknown codec keeps original module bytes");
        var ingredient = new Data_GeneralItem { IDName = "test:ingredient", Guid = 19, code = "grain",
            Stack = new ItemStack { Amount = 4 }, MaxDurability = 1, Durability = 1 };
        var mortar = new MortarState
        {
            Bowl = new Inventory_Data(new List<ItemSlot> { new ItemSlot(0) { itemData = ingredient,
                SlotMaxVolume = 12, CanAcceptTags = new List<string> { "grain" } } }, "石臼")
                { ToggleActionName = "open", UIPrefabName = "bowl-ui" },
            ProcessingRecipeId = "grind", ProcessingStep = 2
        };
        module.WriteData(mortar);
        ingredient.Stack.Amount = 0;
        var restoredMortar = module.GetData<MortarState>();
        Check(restoredMortar.Bowl.itemSlots[0].itemData.Stack.Amount == 4 && restoredMortar.ProcessingStep == 2 &&
            restoredMortar.Bowl.itemSlots[0].SlotMaxVolume == 12 && restoredMortar.Bowl.itemSlots[0].CanAcceptTags[0] == "grain" &&
            restoredMortar.Bowl.Name == "石臼" && restoredMortar.Bowl.ToggleActionName == "open" && restoredMortar.Bowl.UIPrefabName == "bowl-ui",
            "nested mortar inventory snapshot roundtrip");
        var container = new Inventory_ModuleData { Data = new Dictionary<string, Inventory_Data> { ["bag"] = restoredMortar.Bowl },
            PanleRectPosition = new Vector3(1, 2, 3), InventoryInitName = "layout", BasePanelIsOpen = true };
        var containerBytes = MemoryPackSerializer.Serialize(InventoryModulePayloadSnapshot.Capture(container));
        var restoredContainer = MemoryPackSerializer.Deserialize<InventoryModulePayloadSnapshot>(containerBytes).Restore();
        Check(restoredContainer.Data["bag"].itemSlots[0].itemData.Guid == 19 && restoredContainer.PanleRectPosition == new Vector3(1, 2, 3) &&
            restoredContainer.InventoryInitName == "layout", "independent inventory module payload");
        TestTileMapSnapshot();
        TestFoodSnapshot();
        TestMachinePayloadSnapshot();
        TestAtmosphereSnapshot();
        TestTimeGradientSnapshot();
        TestTerrainRecordSnapshot();
        TestCoreRecordSnapshot();
        TestWorldRecordSnapshot();
        TestMachineArchiveMetadata();
        Console.WriteLine("PASS: v1 core, world, terrain, item, food, machine, atmosphere and module contracts");
    }

    private static void TestWorldRecordSnapshot()
    {
        var item = new Data_GeneralItem { IDName = "test:animal", Guid = 90, code = "animal",
            Stack = new ItemStack { Amount = 1 }, MaxDurability = 1, Durability = 1 };
        var planet = new PlanetData { Name = "世界", BodyId = "earth", Radius = 2400, ChunkSize = new Vector2Int(32, 16),
            TopologyMode = WorldTopologyMode.Wrapped, GenerationPresetId = "surface.default", GlobalTemperature = 18,
            GenerationNumericOverrides = new Dictionary<string, double> { ["height"] = 2.5 },
            Atmosphere = new AtmosphereState { DefinitionId = "air", CapacityMoles = 1234567890.123456789m,
                Gases = new List<AtmosphereGasAmount> { new AtmosphereGasAmount { FluidId = "oxygen", Moles = 55.55m } } },
            Ecology = new EcologyWorldSaveData { DataVersion = EcologyWorldSaveData.CurrentDataVersion,
                ProfileId = "earth", ConfigurationFingerprint = 77, Chunks = null,
                Rules = new List<EcologyRuleSaveData> { new EcologyRuleSaveData { RuleId = "flora", ItemId = "tree", SpawnChance = .2 } },
                IndependentEntities = new List<IndependentNaturalEntitySaveData> { new IndependentNaturalEntitySaveData
                    { SourceWorldKey = "世界", SourceChunkX = -16, SourceChunkY = 32, Guid = 90, Snapshot = item } },
                Generation = new WorldGenerationProfileSaveData { DataVersion = WorldGenerationProfileSaveData.CurrentDataVersion,
                    ProfileId = "earth", NumericParameters = new List<WorldGenerationNumericParameterSaveData>
                        { new WorldGenerationNumericParameterSaveData { Id = FlatWorld.WorldModel.WorldGenerationAlgorithmVersion.ParameterId, Value = 1 } } } } };
        planet.SeasonalSnow.Initialized = true;
        planet.SeasonalSnow.Coverage[10] = .7f;
        byte[] bytes = MemoryPackSerializer.Serialize(planet);
        VerifyWorld(bytes);
        VerifyWorld(System.IO.File.ReadAllBytes("Tools/Tests/SaveArchiveContracts/world-v1.bin"));
    }

    private static void VerifyWorld(byte[] bytes)
    {
        var planet = MemoryPackSerializer.Deserialize<PlanetData>(bytes);
        Check(planet.Name == "世界" && planet.BodyId == "earth" && planet.Radius == 2400 &&
            planet.ChunkSize == new Vector2Int(32, 16) && planet.TopologyMode == WorldTopologyMode.Wrapped &&
            planet.GenerationNumericOverrides["height"] == 2.5 && planet.SeasonalSnow.Coverage[10] == .7f,
            "world identity, geometry, generation overrides and snow");
        Check(planet.Atmosphere.CapacityMoles == 1234567890.123456789m && planet.Atmosphere.Gases[0].Moles == 55.55m &&
            planet.Ecology.Rules[0].ItemId == "tree" && planet.Ecology.IndependentEntities[0].Snapshot.Guid == 90 &&
            planet.Ecology.Generation.RequireSupportedGenerationAlgorithmVersion() == 1,
            "world atmosphere, ecology, independent item and frozen generation version");
    }

    private static void TestCoreRecordSnapshot()
    {
        var gradient = new SerializableGradient { colorKeys = new[] { new GradientColorKey(new Color(.2f, .4f, .6f, 1), .5f) },
            alphaKeys = new[] { new GradientAlphaKey(.9f, .3f) } };
        var time = new SerializableTimeData(120, 1440, 34560, 23, .1f,
            new[] { new SerializableKeyframe(.25f, .5f, 1, 2) }, 2, "世界", gradient, 7, "standard", "unlimited",
            0, 29, .03f, .2f, .5f, new SeasonCycleSettings { WinterTemperatureOffset = -31 }, 125,
            new List<SeasonCalendarHistoryEntry> { new SeasonCalendarHistoryEntry { EndTimeSeconds = 100,
                OrbitalPeriodSeconds = 34560, Settings = new SeasonCycleSettings { SummerTemperatureOffset = 13 } } });
        var save = new GameSaveData { saveName = "样本档", SaveSeed = "abc", Seed = 111, Time = 42,
            TimeSpeed = 1.5f, Difficulty = GameDifficultyId.Level3, CustomPlayerAttackMultiplier = 1.25f,
            DimensionStateJson = "{\"portal\":1}", SpaceStateJson = null };
        save.DayTimeData.WorldTimeDict["世界"] = time;
        save.DayTimeData.SceneLightingRateDict["世界"] = .65f;
        save.GameEventData.EventProgress["festival"] = new GameEventProgressSaveData { TriggerCount = 3, LastTriggeredDayNumber = 7 };
        save.MonsterSpawnerWorlds["世界"] = new MonsterSpawnerSaveData
        {
            EntitiesResidents = new List<AiecsResidentSaveData> { new AiecsResidentSaveData { Guid = 71, SpeciesId = "bee", Hp = 8 } },
            NaturalCountdown = new SpawnerCountdownProgress { RemainingGameSeconds = 99, PendingCount = 2 }
        };
        byte[] bytes = MemoryPackSerializer.Serialize(save);
        VerifyCore(bytes);
        VerifyCore(System.IO.File.ReadAllBytes("Tools/Tests/SaveArchiveContracts/core-v1.bin"));
    }

    private static void VerifyCore(byte[] bytes)
    {
        var save = MemoryPackSerializer.Deserialize<GameSaveData>(bytes);
        var time = save.DayTimeData.WorldTimeDict["世界"];
        Check(save.saveName == "样本档" && save.Seed == 111 && save.Time == 42 && save.Difficulty == GameDifficultyId.Level3 &&
            save.CustomPlayerAttackMultiplier == 1.25f && save.PlanetData_Dict.Count == 0 && save.PlayerData_Dict.Count == 0,
            "core identity, difficulty and separated worlds/players");
        Check(time.CurrentTime == 120 && time.TotalDays == 7 && time.OrbitalOffsetSeconds == 125 &&
            time.Seasons.WinterTemperatureOffset == -31 && time.SeasonHistory[0].Settings.SummerTemperatureOffset == 13 &&
            time.DayNightGradient.colorKeys[0].color.b == .6f && time.LightParamsKeys[0].outTangent == 2,
            "core time, seasonal history, gradient and curve");
        Check(save.GameEventData.EventProgress["festival"].TriggerCount == 3 &&
            save.MonsterSpawnerWorlds["世界"].EntitiesResidents[0].Guid == 71 &&
            save.MonsterSpawnerWorlds["世界"].NaturalCountdown.PendingCount == 2 && save.DimensionStateJson == "{\"portal\":1}",
            "core event, spawner and dimension state");
    }

    private static void TestTerrainRecordSnapshot()
    {
        var crop = new Data_GeneralItem { IDName = "test:crop", Guid = 63, code = "wheat",
            Stack = new ItemStack { Amount = 2 }, MaxDurability = 1, Durability = 1 };
        var state = new ChunkSaveRecord
        {
            PlanetName = "世界", ChunkName = "地块", ChunkPosition = new Vector2Int(-20, 40), SunlightIntensity = .8f,
            ChangedItems = new List<ItemData> { crop, null }, RemovedItemGuids = new List<int> { 55 },
            AgricultureCells = new List<AgricultureCellSaveData> {
                new AgricultureCellSaveData { LocalPosition = new Vector2Int(2, 3), Crop = crop, Water = 7, Fertility = .5f },
                new AgricultureCellSaveData { LocalPosition = new Vector2Int(4, 5), Crop = null, Water = 1 } },
            TileDeltas = new List<TileCellSaveDelta> { new TileCellSaveDelta { LocalPosition = new Vector2Int(1, 2),
                Tiles = new List<TileData> { new TileData_Farmland { ID = "soil", waterValue = 6 } } } },
            RuntimeTileDeltas = new List<RuntimeTileCellSaveDelta> { new RuntimeTileCellSaveDelta
                { LocalPosition = new Vector2Int(3, 2), GroundTileId = 14, NavigationCost = 4,
                    Flags = FlatWorld.WorldModel.TerrainCellFlags.Walkable } },
            GrassDeltas = new List<GrassCellSaveDelta> { new GrassCellSaveDelta { LocalPosition = new Vector2Int(5, 1), State = GrassCellState.Removed } },
            LiquidCells = new List<LiquidCellSaveData> { new LiquidCellSaveData { LocalPosition = new Vector2Int(7, 9), LiquidId = "water", LiquidLevel = 5 } },
            ContaminationCells = new List<ContaminationCellSaveData> { new ContaminationCellSaveData { LocalPosition = new Vector2Int(9, 1),
                Values = new List<ContaminationValueSaveData> { new ContaminationValueSaveData { ContaminationId = "smoke", Value = .4f } } } },
            SnowCells = new List<SnowCellSaveData> { new SnowCellSaveData { LocalPosition = new Vector2Int(0, 2), Edited = true, Depth = .3f } },
            SupportCells = new List<SupportCellSaveData> { new SupportCellSaveData { LocalPosition = new Vector2Int(3, 4), TileId = 99, NavigationCost = 8 } },
            GroundLayerCells = new List<GroundLayerCellSaveData> { new GroundLayerCellSaveData { LocalPosition = new Vector2Int(6, 2), ExcavatedLayers = 2,
                Covers = new List<GroundCoverLayerSaveData> { new GroundCoverLayerSaveData { UnderlyingGrass = 3,
                    UnderlyingCell = new RuntimeTileCellSaveDelta { LocalPosition = new Vector2Int(6, 2), GroundTileId = 12 } } } } }
        };
        byte[] bytes = MemoryPackSerializer.Serialize(state);
        VerifyTerrain(bytes);
        VerifyTerrain(System.IO.File.ReadAllBytes("Tools/Tests/SaveArchiveContracts/terrain-v1.bin"));
    }

    private static void VerifyTerrain(byte[] bytes)
    {
        var state = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(bytes);
        Check(state.PlanetName == "世界" && state.ChunkPosition == new Vector2Int(-20, 40) &&
            state.ChangedItems[0].Guid == 63 && state.ChangedItems[1] == null && state.RemovedItemGuids[0] == 55,
            "terrain identity and resident item");
        Check(state.AgricultureCells[0].Crop.Guid == 63 && state.AgricultureCells[0].Water == 7 && state.AgricultureCells[1].Crop == null &&
            ((TileData_Farmland)state.TileDeltas[0].Tiles[0]).waterValue == 6 &&
            state.RuntimeTileDeltas[0].GroundTileId == 14 && state.GrassDeltas[0].State == GrassCellState.Removed,
            "terrain crop, empty crop, tile and grass");
        Check(state.LiquidCells[0].LiquidId == "water" && state.ContaminationCells[0].Values[0].ContaminationId == "smoke" &&
            state.SnowCells[0].Edited && state.SupportCells[0].TileId == 99 &&
            state.GroundLayerCells[0].Covers[0].UnderlyingCell.GroundTileId == 12, "terrain independent layers");
    }

    private static void TestTimeGradientSnapshot()
    {
        var state = new TimeGradientFixture
        {
            Gradient = new SerializableGradient
            {
                colorKeys = new[] { new GradientColorKey(new Color(.1f, .2f, .3f, .4f), .75f) },
                alphaKeys = new[] { new GradientAlphaKey(.8f, .25f) }
            },
            Light = new[] { new SerializableKeyframe(1, 2, 3, 4) }
        };
        byte[] bytes = MemoryPackSerializer.Serialize(state);
        VerifyTimeGradient(bytes);
        VerifyTimeGradient(System.IO.File.ReadAllBytes("Tools/Tests/SaveArchiveContracts/time-gradient-v1.bin"));
    }

    private static void VerifyTimeGradient(byte[] bytes)
    {
        var state = MemoryPackSerializer.Deserialize<TimeGradientFixture>(bytes);
        Check(state.Gradient.colorKeys[0].color.g == .2f && state.Gradient.colorKeys[0].color.a == .4f &&
            state.Gradient.colorKeys[0].time == .75f && state.Gradient.alphaKeys[0].alpha == .8f &&
            state.Light[0].inTangent == 3 && state.Light[0].outTangent == 4, "time gradient and curve values");
    }

    private static void TestAtmosphereSnapshot()
    {
        var state = new AtmosphereState { DefinitionId = "air", CapacityMoles = 123456789012345.6789012345m,
            ReferenceMoles = 100000000000000m, ReferencePressureKPa = 98.7, TemperatureKelvin = 293.5,
            TotalEscapedMoles = 0.0000000000000000001m, Gases = new List<AtmosphereGasAmount>
                { new AtmosphereGasAmount { FluidId = "oxygen", Moles = 12.34567890123456789m } } };
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            byte[] bytes = MemoryPackSerializer.Serialize(state);
            VerifyAtmosphere(bytes);
            VerifyAtmosphere(System.IO.File.ReadAllBytes("Tools/Tests/SaveArchiveContracts/atmosphere-v1.bin"));
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    private static void VerifyAtmosphere(byte[] bytes)
    {
        var state = MemoryPackSerializer.Deserialize<AtmosphereState>(bytes);
        Check(state.CapacityMoles == 123456789012345.6789012345m && state.TotalEscapedMoles == 0.0000000000000000001m &&
            state.Gases[0].Moles == 12.34567890123456789m && state.Gases[0].FluidId == "oxygen",
            "world atmosphere decimal and gases");
    }

    private static void TestMachinePayloadSnapshot()
    {
        var inventory = new Inventory_Data(new List<ItemSlot>
        {
            new ItemSlot(0) { CanAcceptTags = new List<string> { "fuel" }, SlotMaxVolume = 13,
                itemData = new Data_GeneralItem { IDName = "test:fuel", Guid = 88, code = "coal",
                    Stack = new ItemStack { Amount = 3 }, MaxDurability = 1, Durability = 1 } }
        }, "燃料槽") { ToggleActionName = "fuel-open", UIPrefabName = "fuel-panel" };
        var fuel = new FurnaceRuntimeState { Smelting = new ModSmeltingData
        {
            Temperature = 200, InvData = new Dictionary<string, Inventory_Data> { ["fuel"] = inventory },
            PendingFuelByproductCount = new Dictionary<string, int> { ["ash"] = 2 }
        }, Fuel = new FuelData { Fuel = new Vector2(7, 11) }, Absorption = new FuelAbsorptionState { SlotIndex = 0, ElapsedSeconds = 2 } };
        var fixture = new MachinePayloadFixture
        {
            Fluid = new FluidMachineState { PortableTank = inventory, SolidFuel = fuel, FuelInventory = inventory,
                LastEmittedStandardLiters = 1.234567890123456789m, LastRetainedStandardLiters = 0.0000000000000000001m,
                Chambers = new Dictionary<string, FluidInventoryState> { ["gas"] = new FluidInventoryState
                    { Components = new List<FluidComponentState> { new FluidComponentState { FluidId = "H2", GasMoles = 3.4567890123456789m } } } } },
            Storage = new MachineStorageState { Inventories = new List<Inventory_Data> { inventory } },
            Cultivator = new CultivatorState { Plots = new List<CultivatorPlotState> { new CultivatorPlotState
                { CropId = "wheat", Growth = .5f, Seed = 22, PendingHarvest = new List<CultivatorHarvestOutput>
                    { new CultivatorHarvestOutput { ItemId = "grain", Amount = 4 } } } } }
        };
        byte[] bytes = MemoryPackSerializer.Serialize(fixture);
        VerifyMachinePayload(bytes);
        VerifyMachinePayload(System.IO.File.ReadAllBytes("Tools/Tests/SaveArchiveContracts/machine-payload-v1.bin"));
    }

    private static void VerifyMachinePayload(byte[] bytes)
    {
        var state = MemoryPackSerializer.Deserialize<MachinePayloadFixture>(bytes);
        var inventory = state.Storage.Inventories[0];
        Check(inventory.Name == "燃料槽" && inventory.ToggleActionName == "fuel-open" &&
            inventory.UIPrefabName == "fuel-panel" && inventory.itemSlots[0].SlotMaxVolume == 13 &&
            inventory.itemSlots[0].CanAcceptTags[0] == "fuel" && inventory.itemSlots[0].itemData.Guid == 88,
            "machine standalone inventory layout and item");
        Check(state.Fluid.LastEmittedStandardLiters == 1.234567890123456789m &&
            state.Fluid.LastRetainedStandardLiters == 0.0000000000000000001m &&
            state.Fluid.Chambers["gas"].Components[0].GasMoles == 3.4567890123456789m &&
            state.Fluid.SolidFuel.Smelting.InvData["fuel"].itemSlots[0].CanAcceptTags[0] == "fuel" &&
            state.Fluid.SolidFuel.Smelting.PendingFuelByproductCount["ash"] == 2 &&
            state.Fluid.SolidFuel.Fuel.Fuel == new Vector2(7, 11), "machine fluid, furnace and fuel");
        Check(state.Cultivator.Plots[0].PendingHarvest[0].Amount == 4 && state.Cultivator.Plots[0].Seed == 22,
            "machine cultivator harvest and random state");
    }

    private static void TestFoodSnapshot()
    {
        var food = new ModData_FoodData { StableName = "food", ModuleId = "test:food" };
        food.FoodData.nutrition.Water = 29;
        food.FoodData.nutrition.Max_Fat = 45;
        food.FoodData.PanelPosition = new Vector2(12, -3);
        var timer = new FoodMechanicStateData { StateKey = "food.spoilage", Timer = new FoodTimerStateData
            { Initialized = true, IsSpoilage = true, Enabled = true, ElapsedSeconds = 12.5f, TargetItemID = "spoiled" } };
        timer.SetRuntimeFloat("custom-progress", 3.25f);
        food.MechanicStates.Add(timer);
        food.MechanicStates.Add(new FoodMechanicStateData { StateKey = "mod:unknown", Payload = new byte[] { 9, 8, 7 } });
        byte[] bytes = MemoryPackSerializer.Serialize(ModuleInstanceSnapshot.Capture(food));
        VerifyFoodSnapshot(bytes);
        VerifyFoodSnapshot(System.IO.File.ReadAllBytes("Tools/Tests/SaveArchiveContracts/food-v1.bin"));
    }

    private static void VerifyFoodSnapshot(byte[] bytes)
    {
        var restored = new ModData_FoodData { StableName = "food", ModuleId = "test:food" };
        MemoryPackSerializer.Deserialize<ModuleInstanceSnapshot>(bytes).RestoreTo(restored);
        Check(restored.FoodData.nutrition.Water == 29 && restored.FoodData.nutrition.Max_Fat == 45 &&
            restored.FoodData.PanelPosition == new Vector2(12, -3), "food nutrition and panel");
        Check(restored.MechanicStates[0].Timer.ElapsedSeconds == 12.5f &&
            restored.MechanicStates[0].Timer.TargetItemID == "spoiled" &&
            restored.MechanicStates[0].Data["custom-progress"] == "3.25" &&
            restored.MechanicStates[1].Payload[2] == 7, "food timer and unknown provider payload");
    }

    private static void TestTileMapSnapshot()
    {
        var map = new Data_TileMap { IDName = "test:map", Guid = 31, position = new Vector2Int(-3, 9), TileLoaded = true,
            Stack = new ItemStack { Amount = 1 }, MaxDurability = 1, Durability = 1 };
        map.EnsureTileStorage(2, 2);
        map.EnsureEnvironmentStorage(2, 2);
        map.EnvironmentLayers.TemperatureCelsius[1, 0] = 22.5f;
        map.GrassLayer.Set(1, 0, GrassCellState.Removed);
        var cell = new Vector2Int(-2, 9);
        Check(map.SetBaseTile(cell, new TileData_Farmland { ID = "farm", position = new Vector3Int(-2, 9, 4),
            waterValue = 7, fertilityValue = new GameValue_float(0.75f), waterConsumptionRemainder = 0.25f }), "map base set");
        Check(map.PushTile(cell, new TileData_Universal { ID = "overlay" }) &&
            map.PushTile(cell, new TileData_Grass { ID = "grass", FertileValue = new GameValue_float(0.5f) }) &&
            map.PushTile(cell, new TileData_CellBuilding { ID = "wall", CurrentHp = 36 }), "map overflow set");
        byte[] bytes = MemoryPackSerializer.Serialize(ItemInstanceSnapshot.Capture(map));
        VerifyTileMap((Data_TileMap)MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(bytes).CreateColdData());
        VerifyTileMap((Data_TileMap)MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(
            System.IO.File.ReadAllBytes("Tools/Tests/SaveArchiveContracts/tilemap-v1.bin")).CreateColdData());
    }

    private static void VerifyTileMap(Data_TileMap map)
    {
        var tiles = new List<TileData>();
        Check(map.position == new Vector2Int(-3, 9) && map.TileLoaded &&
            map.EnvironmentLayers.TemperatureCelsius[1, 0] == 22.5f && map.GrassLayer.Get(1, 0) == GrassCellState.Removed,
            "map coordinates, environment and grass");
        Check(map.CopyStackTo(new Vector2Int(-2, 9), tiles) && tiles.Count == 4 &&
            tiles[0] is TileData_Farmland farm && farm.waterValue == 7 && farm.waterConsumptionRemainder == .25f &&
            farm.position == new Vector3Int(-2, 9, 4) && tiles[1].ID == "overlay" &&
            tiles[2] is TileData_Grass grass && grass.FertileValue.BaseValue == .5f &&
            tiles[3] is TileData_CellBuilding wall && wall.CurrentHp == 36, "map tile subtype and stack roundtrip");
    }

    private static void TestMachineArchiveMetadata()
    {
        var archive = new MachineArchive { NextPressureExplosionId = 98 };
        archive.RemovedGenerated["世界"] = new HashSet<int> { 3, 8 };
        archive.RecentPressureExplosionSources.Add("source");
        archive.PressureExplosions.Add(new PressureExplosionEvent
        {
            Id = 7, WorldKey = "世界", SourceId = "罐", SpatialContextJson = "{}", PresentationWorldKey = "场景",
            Center = new Vector2(-1.25f, 4.5f), Intensity = 3, PressureEnergyJoules = 4,
            ChemicalEnergyJoules = 5, ProductHeatingJoules = 6,
            Materials = new List<PressureExplosionMaterial> { new PressureExplosionMaterial
            { FluidId = "氢", GasMoles = 1.234567890123456789m, LiquidMoles = 2.0000000000001m, InternalEnergyJoules = 8 } }
        });
        archive.PendingPressureLiquidSpills.Add(new PressureLiquidSpillState
        {
            WorldKey = "船", FluidId = "水", Center = new Vector2(1, 2), LiquidMoles = 0.0000000000000000001m,
            InternalEnergyJoules = 99, NextCellIndex = 23, DetachedWorldKey = "地表", DetachedCenter = new Vector2(3, 4)
        });
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var frozen = MachineArchiveMetadataSnapshot.Capture(archive);
            archive.RemovedGenerated["世界"].Clear();
            archive.PressureExplosions[0].Materials[0].GasMoles = 0;
            var golden = MemoryPackSerializer.Deserialize<MachineArchiveMetadataSnapshot>(System.IO.File.ReadAllBytes("Tools/Tests/SaveArchiveContracts/machine-v1.bin")).Restore();
            Check(golden.PressureExplosions[0].Materials[0].GasMoles == 1.234567890123456789m &&
                golden.PendingPressureLiquidSpills[0].DetachedCenter == new Vector2(3, 4), "stored v1 machine fixture decodes");
            var result = MemoryPackSerializer.Deserialize<MachineArchiveMetadataSnapshot>(MemoryPackSerializer.Serialize(frozen)).Restore();
            Check(result.NextPressureExplosionId == 98 && result.RemovedGenerated["世界"].Contains(8), "machine identity and frozen removal");
            Check(result.PressureExplosions[0].Materials[0].GasMoles == 1.234567890123456789m &&
                result.PressureExplosions[0].Center == new Vector2(-1.25f, 4.5f), "pressure event material and center");
            Check(result.PendingPressureLiquidSpills[0].LiquidMoles == 0.0000000000000000001m &&
                result.PendingPressureLiquidSpills[0].DetachedCenter == new Vector2(3, 4), "spill decimal and coordinates");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void ExpectFailure(Action action, string label)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception(label);
    }

    private struct RuntimeStruct { public int Value; }
    private sealed class RuntimeStructCodec : IModuleBinaryPayloadCodec<RuntimeStruct>
    {
        public string SchemaId => "test.struct";
        public int CurrentVersion => 1;
        public byte[] Serialize(RuntimeStruct value) => MemoryPackSerializer.Serialize(new TaggedValue { Value = value.Value });
        public RuntimeStruct Deserialize(int version, byte[] payload) => new RuntimeStruct { Value = MemoryPackSerializer.Deserialize<TaggedValue>(payload).Value };
    }
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal partial class MatterOnlyInitialized
{
    [MemoryPackOrder(0)] public bool Initialized;
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal partial class MatterWithFutureField
{
    [MemoryPackOrder(0)] public bool Initialized;
    [MemoryPackOrder(1)] public float? TemperatureCelsius;
    [MemoryPackOrder(2)] public float Moisture;
    [MemoryPackOrder(3)] public bool IsBurning;
    [MemoryPackOrder(4)] public float CombustionElapsedSeconds;
    [MemoryPackOrder(5)] public string Future;
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal partial class TaggedValue
{
    [MemoryPackOrder(0)] public int Value;
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal partial class TestEnvelope
{
    [MemoryPackOrder(0)] public string SchemaId;
    [MemoryPackOrder(1)] public int SchemaVersion;
    [MemoryPackOrder(2)] public byte[] Payload;
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal partial class MachinePayloadFixture
{
    [MemoryPackOrder(0)] public FluidMachineState Fluid;
    [MemoryPackOrder(1)] public MachineStorageState Storage;
    [MemoryPackOrder(2)] public CultivatorState Cultivator;
}

[MemoryPackable(GenerateType.VersionTolerant)]
internal partial class TimeGradientFixture
{
    [MemoryPackOrder(0)] public SerializableGradient Gradient;
    [MemoryPackOrder(1)] public SerializableKeyframe[] Light;
}
