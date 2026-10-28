using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 世界与工业规则注册
    // 样本只使用独立领域对象，不登记到当前玩家、机器作用域或真实存档。
    private static void RegisterWorldOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("terrain/noise", WorldNoise);
        operations.Add("terrain/biome", WorldBiome);
        operations.Add("terrain/grass_temperature_density", WorldGrassTemperatureDensity);
        operations.Add("terrain/environment_pointer", WorldEnvironmentPointer);
        operations.Add("ecology/generation", WorldEcology);
        operations.Add("ecology/shared_resources", WorldSharedNaturalResources);
        operations.Add("ecology/companion_bind_candidates", WorldEcologyCompanionBindCandidates);
        operations.Add("ecology/neighborhood_cache", WorldEcologyNeighborhoodCache);
        operations.Add("terrain/cached_generation", WorldCachedTerrainGeneration);
        operations.Add("terrain/river_generation_config", WorldRiverGenerationConfig);
        operations.Add("terrain/river_diagnostic_aggregation", WorldRiverDiagnosticAggregation);
        operations.Add("terrain/climate_cache", WorldClimateCache);
        operations.Add("terrain/spawn_line", WorldSurfaceSpawnLine);
        operations.Add("terrain/tile_search", WorldTileSearch);
        operations.Add("terrain/cave_dirt_ground", WorldCaveDirtGround);
        operations.Add("terrain/cave_loose_resources", WorldCaveLooseResources);
        operations.Add("time/calendar", WorldCalendar);
        operations.Add("environment/thermal", WorldThermal);
        operations.Add("environment/heat_sources", WorldHeatSources);
        operations.Add("environment/wind", WorldWind);
        operations.Add("environment/weather", WorldWeather);
        operations.Add("environment/rain_visual_batch", WorldRainVisualBatch);
        operations.Add("environment/shader_snow", WorldShaderSnow);
        operations.Add("environment/natural_fog_profile", WorldNaturalFogProfile);
        operations.Add("environment/discrete_weather", WorldDiscreteWeather);
        operations.Add("environment/natural_fog_lifecycle", WorldNaturalFogLifecycle);
        operations.Add("environment/natural_fog_validation", WorldNaturalFogValidation);
        RegisterWeatherRecoveryOperations(operations);
        operations.Add("surface_fluids/liquid_layer", WorldLiquidLayer);
        operations.Add("surface_fluids/notification_dispatch", WorldLiquidNotificationDispatch);
        operations.Add("surface_fluids/buoyancy", WorldLiquidBuoyancy);
        operations.Add("dimension/address", WorldDimensionAddress);
        operations.Add("dimension/world_instances", WorldIndependentDimensionInstances);
        operations.Add("dimension/generation_presets", WorldGenerationPresetSelection);
        operations.Add("dimension/destroyed_portal_callback", WorldDestroyedPortalCallback);
        RegisterCavePerformanceOperations(operations);
        RegisterColdDropProbes(operations);
        operations.Add("building/support_layer", WorldSupportLayer);
        operations.Add("building/shared_state", WorldBuildingState);
        operations.Add("building/portable_building_health", WorldPortableBuildingHealth);
        operations.Add("agriculture/soil", WorldSoil);
        operations.Add("agriculture/plant_growth", WorldPlantGrowth);
        operations.Add("agriculture/tilling_visual_schedule", WorldTillingVisualSchedule);
        operations.Add("agriculture/soil_moisture_visual", WorldSoilMoistureVisual);
        operations.Add("agriculture/soil_commit_unchanged", WorldSoilCommitUnchanged);
        operations.Add("agriculture/farmland_scope", WorldFarmlandScope);
        operations.Add("fishing/rig", WorldFishingRig);
        operations.Add("fishing/cast_supplies", WorldFishingCastSupplies);
        operations.Add("mechanical/load_allocation", WorldMechanicalLoads);
        operations.Add("mechanical/gear_conflict", WorldMechanicalGears);
        operations.Add("mechanical/ship_scope_identity", WorldMechanicalShipScopeIdentity);
        operations.Add("mechanical/bellows_effects", WorldBellowsEffects);
        operations.Add("mechanical/conveyor_side_ports", WorldConveyorSidePorts);
        operations.Add("electrical/battery_commit", WorldElectricalBattery);
        operations.Add("electrical/supply_limits", WorldElectricalLimits);
        operations.Add("electrical/unlimited_supply", WorldElectricalUnlimitedSupply);
        operations.Add("electrical/line_connections", WorldElectricalLineConnections);
        operations.Add("electrical/engineering_vision", WorldEngineeringVision);
        operations.Add("space/fiber_connections", WorldFiberConnections);
        operations.Add("space/device_power", WorldShipDevicePower);
        operations.Add("space/pipe_geometry", WorldPipeGeometry);
        operations.Add("space/loose_drop_rules", WorldLooseDropRules);
        operations.Add("industrial_fluids/reservations", WorldFluidReservations);
        operations.Add("industrial_fluids/infinite_gas_supply", WorldInfiniteGasSupply);
        operations.Add("industrial_fluids/atomic_recipe", WorldFluidRecipe);
        operations.Add("industrial_fluids/heat", WorldFluidHeat);
        operations.Add("industrial_fluids/ambient_heat", WorldFluidAmbientHeat);
        operations.Add("industrial_fluids/phase_change", WorldFluidPhase);
        operations.Add("processing/recipe_selection", WorldProcessingSelection);
        operations.Add("processing/slot_identity", WorldProcessingIdentity);
        operations.Add("processing/atomic_output", WorldProcessingAtomicOutput);
        operations.Add("processing/fuel_absorption", WorldFuelAbsorption);
        operations.Add("processing/material_heat", WorldProcessingMaterialHeat);
        operations.Add("processing/flint_strike", WorldProcessingFlintStrike);
        foreach (string operation in new[] { "ship_structure", "ship_docking", "ship_mass",
                     "ship_propulsion", "ship_contacts", "ship_snapshots", "ship_engine_placement", "ship_optical",
                     "ship_collider_batch" })
            operations.Add("space/" + operation, _ => GameplayTestShipProbe.Capture(operation));
        operations.Add("space/cabin_topology", WorldCabinTopology);
        operations.Add("space/ship_fuel", WorldShipFuel);
        operations.Add("space/coordinates", _ => GameplayTestUniverseProbe.Capture("coordinates"));
        operations.Add("space/planet_visual_templates", WorldPlanetVisualTemplates);
        operations.Add("space/streaming", _ => GameplayTestUniverseProbe.Capture("streaming"));
        operations.Add("space/ship_surface_state", _ => GameplayTestShipSurfaceProbe.Capture());
        operations.Add("space/presentation_interpolation", _ => GameplayTestSpacePresentationProbe.Capture());
        operations.Add("space/native_physics_island_shutdown", _ => GameplayTestSpacePresentationProbe.CaptureNativePhysicsIslandShutdown());
        foreach (string operation in new[] { "ship_local_damage", "ship_hit_targeting",
                     "ship_damage_lines", "ship_damage_fragmentation" })
            operations.Add("space/" + operation, _ => GameplayTestShipDamageProbe.Capture(operation));
    }

    #region 维度入口异步回执生命周期回归
    private static JObject WorldDestroyedPortalCallback(JObject args)
    {
        GameObject holder = new GameObject("DimensionPortalCallbackProbe");
        try
        {
            // 在独立非激活对象上构建已销毁模块句柄，模拟切换后迟到的回执。
            holder.SetActive(false);
            Mod_DimensionPortal portal = holder.AddComponent<Mod_DimensionPortal>();
            bool aliveBeforeDestroy = portal != null;
            UnityEngine.Object.DestroyImmediate(holder);
            bool unityNull = portal == null;
            bool managedReferenceRemains = !ReferenceEquals(portal, null);
            portal.CancelNetworkTransitionRequest();
            return new JObject
            {
                ["aliveBeforeDestroy"] = aliveBeforeDestroy,
                ["unityNullAfterDestroy"] = unityNull,
                ["managedReferenceRemains"] = managedReferenceRemains,
                ["cancelAfterDestroySafe"] = true
            };
        }
        finally
        {
            if (holder != null) UnityEngine.Object.DestroyImmediate(holder);
        }
    }
    #endregion

    /// <summary>复用真实区块初绑的伴生物挑选入口，核验候选顺序及多次 Bind 的缓冲复用。</summary>
    private static JObject WorldEcologyCompanionBindCandidates(JObject args)
    {
        const int count = 512;
        var placements = new NaturalItemPlacement[count];
        var expected = new List<int>();
        for (int i = 0; i < count; i++)
        {
            // 宿主和伴生物交错；宿主 GUID 与伴生规则 ID 不能在候选缓冲中被丢弃。
            bool companion = i % 41 == 0;
            placements[i] = new NaturalItemPlacement(1000 + i, companion ? "probe:hive" : "probe:tree",
                i % 16, i / 16, 0f, 0f, companion ? "hive-rule" : "tree-rule",
                hostGuid: companion ? 900 + i : 0);
            if (companion) expected.Add(1000 + i);
        }
        var selected = new List<NaturalItemPlacement>(expected.Count);
        void Collect()
        {
            selected.Clear();
            foreach (NaturalItemPlacement placement in placements)
                ChunkNaturalItemRenderer.CollectInitialCompanion(placement, selected);
        }
        Collect();
        bool ordered = selected.Count == expected.Count;
        for (int i = 0; ordered && i < selected.Count; i++)
            ordered &= selected[i].Guid == expected[i] && selected[i].HostGuid == 900 + (selected[i].Guid - 1000) &&
                selected[i].ItemId == "probe:hive";

        // 全宿主与全伴生两种边界都按源列表过滤，不插入假候选或改变相对次序。
        selected.Clear();
        for (int i = 0; i < 16; i++)
            ChunkNaturalItemRenderer.CollectInitialCompanion(
                new NaturalItemPlacement(1 + i, "probe:tree", i, 0, 0, 0, "tree"), selected);
        bool hostIgnored = selected.Count == 0;
        for (int i = 0; i < 16; i++)
            ChunkNaturalItemRenderer.CollectInitialCompanion(
                new NaturalItemPlacement(101 + i, "probe:hive", i, 0, 0, 0, "hive", hostGuid: 1 + i), selected);
        bool companionAccepted = selected.Count == 16;
        for (int i = 0; companionAccepted && i < selected.Count; i++)
            companionAccepted &= selected[i].Guid == 101 + i && selected[i].HostGuid == 1 + i;

        for (int i = 0; i < 32; i++) Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) Collect();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        bool repeated = selected.Count == expected.Count;
        for (int i = 0; repeated && i < selected.Count; i++)
            repeated &= selected[i].Guid == expected[i];

        return new JObject
        {
            ["ordered"] = ordered, ["repeated"] = repeated,
            ["hostIgnored"] = hostIgnored, ["companionAccepted"] = companionAccepted,
            ["totalPlacements"] = count, ["companionCount"] = expected.Count,
            ["secondPassInspections"] = selected.Count,
            ["oldSecondPassInspections"] = count, ["allocatedBytes"] = allocated
        };
    }

    /// <summary>仅比较农业权威字段，真实耕地资格由生产提交入口校验。</summary>
    private static JObject WorldSoilCommitUnchanged(JObject args)
    {
        using ChunkTerrainData terrain = EnvironmentBatchFixture(2, 1);
        RuntimeTerrainTileSample sample = new(default, terrain, Vector2Int.zero, Vector2Int.zero,
            terrain.GetCell(0, 0), terrain.GetTopTileId(0, 0));
        var soil = new TileData_Farmland
        {
            waterValue = 4, waterSupplyRemainder = .25f, waterConsumptionRemainder = .125f
        };
        soil.fertilityValue.BaseValue = .6f;
        soil.NormalizeValues();
        bool uninitialized = !FarmlandSystem.SoilSamplingBatch.IsAlreadyCommitted(sample, soil);
        void Set(string layer, float value) => terrain.SetEnvironmentValue(layer, 0, 0, value);
        Set(FarmlandSystem.SourceLayer, 1f);
        Set(FarmlandSystem.WaterLayer, soil.waterValue);
        Set(FarmlandSystem.WaterSupplyRemainderLayer, soil.waterSupplyRemainder);
        Set(FarmlandSystem.WaterConsumptionRemainderLayer, soil.waterConsumptionRemainder);
        Set(FarmlandSystem.FertilityLayer, soil.Fertility);
        bool geographyIndependent = FarmlandSystem.SoilSamplingBatch.IsAlreadyCommitted(sample, soil);
        Set("moisture", .4f);
        Set("fertility", soil.Fertility);
        bool equal = FarmlandSystem.SoilSamplingBatch.IsAlreadyCommitted(sample, soil);
        Set(FarmlandSystem.WaterConsumptionRemainderLayer, .2f);
        bool changedRemainder = !FarmlandSystem.SoilSamplingBatch.IsAlreadyCommitted(sample, soil);
        Set(FarmlandSystem.WaterConsumptionRemainderLayer, soil.waterConsumptionRemainder);
        Set("fertility", .5f);
        bool geographicChangesIgnored = FarmlandSystem.SoilSamplingBatch.IsAlreadyCommitted(sample, soil);
        Set("fertility", soil.Fertility);
        Set(FarmlandSystem.FertilityLayer, .5f);
        bool changedFertility = !FarmlandSystem.SoilSamplingBatch.IsAlreadyCommitted(sample, soil);
        Set(FarmlandSystem.FertilityLayer, soil.Fertility);
        // 即使植物本次耗水不能继续扣到负数，干土/缺肥仍可以判为真正的无变化。
        soil.waterValue = 0;
        soil.waterSupplyRemainder = soil.waterConsumptionRemainder = 0f;
        soil.fertilityValue.BaseValue = 0f;
        soil.NormalizeValues();
        Set(FarmlandSystem.WaterLayer, 0f);
        Set(FarmlandSystem.WaterSupplyRemainderLayer, 0f);
        Set(FarmlandSystem.WaterConsumptionRemainderLayer, 0f);
        Set(FarmlandSystem.FertilityLayer, 0f);
        Set("moisture", 0f);
        Set("fertility", 0f);
        bool dryEqual = FarmlandSystem.SoilSamplingBatch.IsAlreadyCommitted(sample, soil);
        soil.AccumulateWaterConsumption(.5f);
        soil.ConsumeFertility(.25f);
        bool dryStillEqual = FarmlandSystem.SoilSamplingBatch.IsAlreadyCommitted(sample, soil);
        soil.AccumulateWaterSupply(1f);
        bool wateringChanges = !FarmlandSystem.SoilSamplingBatch.IsAlreadyCommitted(sample, soil);
        soil.waterValue = 0;
        soil.NormalizeValues();
        for (int i = 0; i < 64; i++) FarmlandSystem.SoilSamplingBatch.IsAlreadyCommitted(sample, soil);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2048; i++) FarmlandSystem.SoilSamplingBatch.IsAlreadyCommitted(sample, soil);
        long allocation = GC.GetAllocatedBytesForCurrentThread() - before;
        return new JObject
        {
            ["uninitialized"] = uninitialized,
            ["geographyIndependent"] = geographyIndependent,
            ["equal"] = equal,
            ["changedRemainder"] = changedRemainder,
            ["geographicChangesIgnored"] = geographicChangesIgnored,
            ["changedFertility"] = changedFertility,
            ["dryEqual"] = dryEqual,
            ["dryStillEqual"] = dryStillEqual,
            ["wateringChanges"] = wateringChanges,
            ["allocation"] = allocation
        };
    }

    /// <summary>采样真实耕地索引、普通地块补水拒绝和地理初始值，避免测试复制农业算法。</summary>
    private static JObject WorldFarmlandScope(JObject args)
    {
        using ChunkTerrainData terrain = EnvironmentBatchFixture(4, 2);
        IReadOnlyList<int> indices = FarmlandSystem.GetFarmlandCellIndices(terrain, 14);
        bool initiallyEmpty = indices.Count == 0;
        TerrainCell original = terrain.GetCell(0, 0);
        TerrainCell Farm() => new(14, 0, 0, original.BiomeId, original.NavigationCost, original.Flags);
        terrain.SetCell(3, 1, Farm());
        terrain.SetCell(1, 0, Farm());
        bool ordered = indices.Count == 2 && indices[0] == 1 && indices[1] == 7;
        terrain.SetCell(1, 0, original);
        bool removed = indices.Count == 1 && indices[0] == 7;
        terrain.SetEnvironmentValue(FarmlandSystem.SourceLayer, 0, 0, 1f);
        terrain.SetEnvironmentValue(FarmlandSystem.WaterLayer, 0, 0, 10f);
        terrain.SetEnvironmentValue(FarmlandSystem.FertilityLayer, 0, 0, .1f);
        terrain.SetEnvironmentValue(FarmlandSystem.WaterConsumptionRemainderLayer, 0, 0, .75f);
        terrain.SetEnvironmentValue("moisture", 0, 0, .3f);
        terrain.SetEnvironmentValue("fertility", 0, 0, .8f);
        var sample = new RuntimeTerrainTileSample(default, terrain, Vector2Int.zero, Vector2Int.zero, original, original.GroundTileId);
        var soil = new TileData_Farmland();
        long revision = terrain.Revision;
        FarmlandSystem.ReadSoilSnapshot(sample, soil);
        bool normalReadOnly = terrain.Revision == revision && soil.waterValue == 3 &&
            Mathf.Approximately(soil.Fertility, .8f) && soil.waterConsumptionRemainder == 0f;
        FarmlandSystem.EnsureSoilState(sample);
        bool supplyRejected = !FarmlandSystem.SupplySurfaceWater(sample, 3f, false) && terrain.Revision == revision;
        FarmlandSystem.ReadGeographicSoilSnapshot(sample, soil);
        bool geographicInitial = soil.waterValue == 3 && Mathf.Approximately(soil.Fertility, .8f) &&
            soil.waterSupplyRemainder == 0f && soil.waterConsumptionRemainder == 0f;
        bool environmentKeptIndex = ReferenceEquals(indices, FarmlandSystem.GetFarmlandCellIndices(terrain, 14)) &&
            indices.Count == 1 && indices[0] == 7;
        IReadOnlyList<int> remapped = FarmlandSystem.GetFarmlandCellIndices(terrain, 1);
        bool remappedTile = remapped.Count == 7 && remapped[0] == 0;
        var snow = WorldWeatherSurfaceSystem.GetSnowCellIndices(terrain);
        terrain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, 2, 0, .5f);
        bool snowAdded = snow.Count == 1 && snow[0] == 2;
        terrain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, 2, 0, 0f);
        bool snowMelted = snow.Count == 0;
        terrain.SetEnvironmentValue(WorldSnowInteraction.DepthLayer, 2, 1, .4f);
        terrain.SetEnvironmentValue(WorldSnowInteraction.EditedLayer, 2, 1, 1f);
        bool playerSnowAdded = snow.Count == 1 && snow[0] == 6;
        terrain.SetEnvironmentValue(WorldSnowInteraction.EditedLayer, 2, 1, 0f);
        bool playerSnowRemoved = snow.Count == 0;
        using ChunkTerrainData plain = EnvironmentBatchFixture(2, 1);
        bool warmSkipped = !WorldWeatherSurfaceSystem.HasSurfaceWork(plain, 3f, maySnow: false);
        bool coldKept = WorldWeatherSurfaceSystem.HasSurfaceWork(plain, 3f, maySnow: true);
        plain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, 0, 0, .2f);
        bool meltingKept = WorldWeatherSurfaceSystem.HasSurfaceWork(plain, 0f, maySnow: false);
        plain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, 0, 0, 0f);
        bool meltedSkipped = !WorldWeatherSurfaceSystem.HasSurfaceWork(plain, 3f, maySnow: false);
        return new JObject
        {
            ["initiallyEmpty"] = initiallyEmpty, ["ordered"] = ordered, ["removed"] = removed,
            ["normalReadOnly"] = normalReadOnly, ["supplyRejected"] = supplyRejected,
            ["geographicInitial"] = geographicInitial, ["environmentKeptIndex"] = environmentKeptIndex,
            ["remappedTile"] = remappedTile, ["snowAdded"] = snowAdded, ["snowMelted"] = snowMelted,
            ["playerSnowAdded"] = playerSnowAdded, ["playerSnowRemoved"] = playerSnowRemoved,
            ["warmSkipped"] = warmSkipped, ["coldKept"] = coldKept,
            ["meltingKept"] = meltingKept, ["meltedSkipped"] = meltedSkipped
        };
    }

    private static JObject WorldSoilMoistureVisual(JObject args)
    {
        using ChunkTerrainData terrain = EnvironmentBatchFixture(1, 1);
        var template = new TileData_Farmland { waterValue = (int)Number(args, "defaultWater", 4) };
        if (args["source"] != null)
            terrain.SetEnvironmentValue(FarmlandSystem.SourceLayer, 0, 0, (float)Number(args, "source", 0f));
        if (args["water"] != null)
            terrain.SetEnvironmentValue(FarmlandSystem.WaterLayer, 0, 0, (float)Number(args, "water", 0f));
        if (args["moisture"] != null)
            terrain.SetEnvironmentValue("moisture", 0, 0, (float)Number(args, "moisture", 0f));
        long revision = terrain.Revision;
        Vector4 data = ChunkTilemapRenderer.BuildFarmlandMoistureData(terrain, 0, 0, template);
        int notifications = 0;
        terrain.Changed += _ => notifications++;
        for (int i = 0; i < 32; i++)
            ChunkTilemapRenderer.BuildFarmlandMoistureData(terrain, 0, 0, template);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++)
            ChunkTilemapRenderer.BuildFarmlandMoistureData(terrain, 0, 0, template);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return new JObject
        {
            ["ratio"] = data.x, ["enabled"] = data.y, ["reserved"] = data.z == 0f && data.w == 0f,
            ["readOnly"] = terrain.Revision == revision && notifications == 0,
            ["allocatedBytes"] = allocated,
            ["waterLayer"] = ChunkTilemapRenderer.IsSoilMoistureVisualLayer(FarmlandSystem.WaterLayer),
            ["sourceLayer"] = ChunkTilemapRenderer.IsSoilMoistureVisualLayer(FarmlandSystem.SourceLayer),
            ["generatedMoistureLayer"] = ChunkTilemapRenderer.IsSoilMoistureVisualLayer("moisture"),
            ["fertilityIgnored"] = !ChunkTilemapRenderer.IsSoilMoistureVisualLayer(FarmlandSystem.FertilityLayer),
            ["progressIgnored"] = !ChunkTilemapRenderer.IsSoilMoistureVisualLayer(FarmlandSystem.WaterConsumptionRemainderLayer),
            ["genericChangeIgnored"] = !ChunkTilemapRenderer.IsSoilMoistureVisualLayer(null)
        };
    }

    /// <summary>直接采生产的锄地衰减调度，核对十秒稳态跳过、继续施工延期与开始退回的边界。</summary>
    private static JObject WorldTillingVisualSchedule(JObject args)
    {
        const float began = 100f;
        float deadline = FarmlandSystem.NextTillingVisualRefreshTime(began, began);
        int idleFramesSkipped = 0;
        for (int frame = 0; frame < 600; frame++)
        {
            float now = began + frame / 60f;
            if (!ChunkAgricultureRenderer.IsTillingRuntimeRefreshDue(now, deadline))
                idleFramesSkipped++;
        }

        bool startsOnTime = ChunkAgricultureRenderer.IsTillingRuntimeRefreshDue(
            began + FarmlandSystem.TillingRecoveryDelaySeconds, deadline);
        bool noEarlyRefresh = idleFramesSkipped == 600;
        float extendedDeadline = FarmlandSystem.NextTillingVisualRefreshTime(began + 4f, began + 4f);
        bool additionalWorkExtends = extendedDeadline == deadline + 4f &&
            !ChunkAgricultureRenderer.IsTillingRuntimeRefreshDue(deadline, extendedDeadline);
        bool decayContinues = true;
        for (int frame = 1; frame <= 150; frame++)
        {
            float now = deadline + frame / 60f;
            decayContinues &= ChunkAgricultureRenderer.IsTillingRuntimeRefreshDue(
                now, FarmlandSystem.NextTillingVisualRefreshTime(began, now));
        }

        // 空单元不需要安排衰减期，正式数据入口应返回无进度和无限远截止时间。
        var emptyAddress = new FlatWorld.WorldModel.WorldAddress("probe:tilling-visual", new Int2(0, 0));
        float missingProgress = FarmlandSystem.GetTillingVisualProgress(
            emptyAddress, Vector2Int.zero, 7, out float missingRefreshAt);
        bool emptyIgnored = missingProgress == 0f && float.IsPositiveInfinity(missingRefreshAt) &&
            !ChunkAgricultureRenderer.IsTillingRuntimeRefreshDue(began + 1000f, missingRefreshAt);

        // 静止等待阶段的数万次调用应只比较时间，无枚举与托管堆分配。
        long before = GC.GetAllocatedBytesForCurrentThread();
        int unnecessaryRefreshes = 0;
        for (int frame = 0; frame < 65536; frame++)
        {
            float now = began + (frame % 600) / 60f;
            if (ChunkAgricultureRenderer.IsTillingRuntimeRefreshDue(now, deadline))
                unnecessaryRefreshes++;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        return new JObject
        {
            ["idleFramesSkipped"] = idleFramesSkipped,
            ["startsOnTime"] = startsOnTime, ["noEarlyRefresh"] = noEarlyRefresh,
            ["additionalWorkExtends"] = additionalWorkExtends,
            ["decayContinues"] = decayContinues, ["emptyIgnored"] = emptyIgnored,
            ["unnecessaryRefreshes"] = unnecessaryRefreshes, ["steadyAllocatedBytes"] = bytes
        };
    }

    #region 船体作用域身份无分配回归

    // 正式值类型身份在切船、切地表与资源释放时只按本作用域解析前缀。
    private static JObject WorldMechanicalShipScopeIdentity(JObject args)
    {
        var alpha = new MachineScopeIdentity("ship:alpha-01");
        var beta = new MachineScopeIdentity("ship:beta-02");
        var surface = new MachineScopeIdentity("planet:surface");
        var empty = new MachineScopeIdentity("ship:");
        var caseMismatch = new MachineScopeIdentity("Ship:alpha-01");
        var invalid = new MachineScopeIdentity(null);

        bool identity = alpha.WorldKey == "ship:alpha-01" && alpha.ShipId == "alpha-01" &&
            beta.WorldKey == "ship:beta-02" && beta.ShipId == "beta-02" &&
            surface.WorldKey == "planet:surface" && surface.ShipId == null;
        bool strictPrefix = empty.ShipId == string.Empty && caseMismatch.ShipId == null &&
            caseMismatch.WorldKey == "Ship:alpha-01" && invalid.ShipId == null && invalid.WorldKey == null;

        // 重复读取必须返回同一字符串对象；旧 Substring 热路径会生成新对象。
        string firstId = alpha.ShipId;
        bool stableReference = firstId != null;
        for (int i = 0; i < 1024; i++)
            stableReference &= ReferenceEquals(firstId, alpha.ShipId);

        MachineScopeIdentity active = alpha;
        active = beta;
        bool switchedShip = active.ShipId == "beta-02" && active.WorldKey == "ship:beta-02" &&
            !ReferenceEquals(active.ShipId, alpha.ShipId);
        active = surface;
        bool switchedSurface = active.ShipId == null && active.WorldKey == surface.WorldKey;
        active = alpha;
        bool restoredShip = ReferenceEquals(active.ShipId, alpha.ShipId);

        return new JObject
        {
            ["identity"] = identity, ["strictPrefix"] = strictPrefix,
            ["stableReference"] = stableReference, ["switchedShip"] = switchedShip,
            ["switchedSurface"] = switchedSurface, ["restoredShip"] = restoredShip
        };
    }

    #endregion

    // 正式取水通知在隔离地形上验证有序投递、动态退订与稳态托管分配。
    private static JObject WorldLiquidNotificationDispatch(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(1, 1);
        buffer.SetCell(0, 0, new TerrainCell(17, 0, 0, 0, 1, TerrainCellFlags.Walkable));
        using ChunkTerrainData terrain = buffer.Seal();
        var sample = new RuntimeTerrainTileSample(default, terrain, Vector2Int.zero, Vector2Int.zero,
            terrain.GetCell(0, 0), 17);
        int hits = 0;
        Action<RuntimeTerrainTileSample> countA = _ => hits++;
        Action<RuntimeTerrainTileSample> countB = _ => hits++;
        WorldLiquidSystem.SurfaceLiquidPumped += countA;
        WorldLiquidSystem.SurfaceLiquidPumped += countB;
        try
        {
            const int iterations = 1024;
            for (int i = 0; i < 16; i++) WorldLiquidSystem.PublishSurfaceLiquidPumped(sample);
            hits = 0;
            for (int i = 0; i < iterations; i++) WorldLiquidSystem.PublishSurfaceLiquidPumped(sample);
            int hitsFromSteadyDispatch = hits;

            var received = new List<int>(16);
            Action<RuntimeTerrainTileSample> third = _ => received.Add(3);
            Action<RuntimeTerrainTileSample> first = _ =>
            {
                received.Add(1);
                WorldLiquidSystem.SurfaceLiquidPumped -= third;
            };
            Action<RuntimeTerrainTileSample> second = _ => received.Add(2);
            WorldLiquidSystem.SurfaceLiquidPumped += first;
            WorldLiquidSystem.SurfaceLiquidPumped += second;
            WorldLiquidSystem.SurfaceLiquidPumped += third;
            try
            {
                received.Clear();
                WorldLiquidSystem.PublishSurfaceLiquidPumped(sample);
                bool firstSnapshot = received.Count == 3 && received[0] == 1 && received[1] == 2 && received[2] == 3;
                received.Clear();
                WorldLiquidSystem.PublishSurfaceLiquidPumped(sample);
                bool nextSnapshot = received.Count == 2 && received[0] == 1 && received[1] == 2;
                WorldLiquidSystem.SurfaceLiquidPumped += second;
                received.Clear();
                WorldLiquidSystem.PublishSurfaceLiquidPumped(sample);
                bool duplicates = received.Count == 3 && received[0] == 1 && received[1] == 2 && received[2] == 2;
                return new JObject
                {
                    ["iterations"] = iterations, ["hits"] = hitsFromSteadyDispatch,
                    ["firstSnapshot"] = firstSnapshot,
                    ["nextSnapshot"] = nextSnapshot, ["duplicates"] = duplicates
                };
            }
            finally
            {
                WorldLiquidSystem.SurfaceLiquidPumped -= first;
                WorldLiquidSystem.SurfaceLiquidPumped -= second;
                WorldLiquidSystem.SurfaceLiquidPumped -= second;
                WorldLiquidSystem.SurfaceLiquidPumped -= third;
            }
        }
        finally
        {
            WorldLiquidSystem.SurfaceLiquidPumped -= countA;
            WorldLiquidSystem.SurfaceLiquidPumped -= countB;
        }
    }

    private static string WorldException(Action action)
    {
        try { action(); return string.Empty; }
        catch (Exception exception) { return exception.GetType().Name; }
    }
    #endregion

    #region 洞穴散矿规则采样

    // 只布置基础生态输入，散矿仍调用正式生成入口，概率和距离期望交给 Lua。
    private sealed class CaveLooseResourceFixture : IChunkEcologyNeighborhoodTagResolver,
        ICaveBaseEcologyResolver
    {
        public readonly List<(int x, int y, string item, string rule)> Sources = new();
        public bool HasHostTagInChunk(ChunkGenerationRequest request, string tag,
            CancellationToken cancellationToken) => false;
        public ChunkEcologyData GetCaveBaseEcology(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, CancellationToken cancellationToken)
        {
            var placements = new List<NaturalItemPlacement>();
            foreach (var source in Sources)
            {
                int x = request.Topology.NormalizeX(source.x) - request.Address.ChunkOrigin.X;
                int y = request.Topology.NormalizeY(source.y) - request.Address.ChunkOrigin.Y;
                if (request.Topology.IsWrapped)
                {
                    if (x < 0) x += request.Topology.Span.X;
                    if (y < 0) y += request.Topology.Span.Y;
                }
                if ((uint)x < (uint)request.Profile.Width && (uint)y < (uint)request.Profile.Height)
                    placements.Add(new NaturalItemPlacement(1000 + Sources.IndexOf(source),
                        source.item, x, y, 0f, 0f, source.rule));
            }
            return new ChunkEcologyData(placements);
        }
    }

    private static JObject WorldCaveLooseResources(JObject args)
    {
        string scenario = (string)args["scenario"] ?? "mixed";
        bool wrapped = (bool?)args["wrapped"] == true;
        double oreChance = Number(args, "ore", .02d), stoneChance = Number(args, "stone", .05d);
        var numbers = new Dictionary<string, double>
        {
            ["cave.portal.enabled"] = 0, ["cave.vine.enabled"] = 0,
            ["cave.spawn.safeRadius"] = 0, ["cave.spawn.x"] = 1000, ["cave.spawn.y"] = 1000,
            ["cave.resource.looseDensity"] = oreChance,
            ["cave.resource.looseStoneDensity"] = stoneChance,
            ["cave.resource.looseMinDistance"] = 5, ["cave.resource.looseMaxDistance"] = 12
        };
        var rules = new[]
        {
            new CaveResourceRuleSnapshot("probe.mine.iron", "Mine_Iron", 1, .04, 1),
            new CaveResourceRuleSnapshot("probe.mine.gold", "Mine_Gold", 1, .04, 2),
            new CaveResourceRuleSnapshot("probe.mine.stone", "Mine_Stone", 0, .04, 3)
        };
        var profile = new ChunkGenerationProfileSnapshot("probe:loose", 1, 16, 16, numbers,
            new Dictionary<string, string> { ["terrain.mode"] = "cave" }, caveResourceRules: rules);
        var topology = wrapped ? new ChunkGenerationTopologySnapshot(new Int2(0, 0), new Int2(32, 32)) : default;
        var fixture = new CaveLooseResourceFixture();
        if (scenario != "empty" && scenario != "occupied")
        {
            fixture.Sources.Add((-1, 8, scenario == "stone" ? "Mine_Stone" : "Mine_Iron",
                scenario == "stone" ? "probe.mine.stone" : "probe.mine.iron"));
            if (scenario != "stone")
            {
                fixture.Sources.Add((24, 24, "Mine_Gold", "probe.mine.gold"));
                fixture.Sources.Add((16, 0, "Mine_Stone", "probe.mine.stone"));
            }
        }
        fixture.Sources.Add((5, 5, "probe:flora", "probe.flora"));
        fixture.Sources.Add((6, 5, "TwineCrop", "cave.vine.twine"));
        // 被植物占去的位置没有矿山规则身份，不能成为散矿来源。
        fixture.Sources.Add((0, 15, "Mine_Gold", "probe.flora"));
        int seed = (int)Number(args, "seed", 17413);
        var request = new ChunkGenerationRequest(1,
            new FlatWorld.WorldModel.WorldAddress("cave", new Int2(0, 0)), seed, 1, profile, topology);
        using var terrain = new ChunkTerrainBuffer(16, 16);
        var cells = new JArray();
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            bool blocked = x == 7 && y == 7, wet = x == 7 && y == 8;
            terrain.SetCell(x, y, new TerrainCell(4, 0, blocked ? 7 : 0, 1, 1,
                blocked ? TerrainCellFlags.Blocking : TerrainCellFlags.Walkable));
            if (wet) terrain.SetLiquid(x, y, terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 10);
            uint state = CaveLayoutKernel.Hash(seed ^ unchecked((int)0x6c8e9cf5), x, y, 0x1b873593);
            cells.Add(new JObject { ["x"] = x, ["y"] = y, ["blocked"] = blocked,
                ["wet"] = wet, ["roll"] = CaveLayoutKernel.NextUnitDouble(ref state),
                ["safe"] = CaveLayoutKernel.IsInsideDefaultSpawnSafeArea(request, profile.Settings, x, y) });
        }
        JArray Capture(ChunkEcologyData ecology)
        {
            var rows = new JArray();
            foreach (NaturalItemPlacement p in ecology.Placements)
                rows.Add(new JObject { ["x"] = p.LocalX, ["y"] = p.LocalY,
                    ["item"] = p.ItemId, ["rule"] = p.RuleId, ["guid"] = p.Guid,
                    ["host"] = p.HostGuid, ["offsetX"] = p.OffsetX, ["offsetY"] = p.OffsetY });
            return rows;
        }
        var sources = new JArray();
        foreach (var source in fixture.Sources)
            sources.Add(new JObject { ["x"] = topology.NormalizeX(source.x),
                ["y"] = topology.NormalizeY(source.y), ["item"] = source.item,
                ["mine"] = source.rule.StartsWith("probe.mine.", StringComparison.Ordinal) });
        ChunkEcologyData result = CaveGenerationFeatureGenerator.GenerateCave(request, terrain,
            CancellationToken.None, fixture);
        // 先解析邻块再重生成当前块，结果必须不受解析顺序影响。
        fixture.GetCaveBaseEcology(new ChunkGenerationRequest(1,
            new FlatWorld.WorldModel.WorldAddress("cave", new Int2(16, 0)), seed, 1, profile, topology),
            null, CancellationToken.None);
        ChunkEcologyData repeated = CaveGenerationFeatureGenerator.GenerateCave(request, terrain,
            CancellationToken.None, fixture);
        var expandedProfile = new ChunkGenerationProfileSnapshot("probe:loose", 1, 32, 16, numbers,
            new Dictionary<string, string> { ["terrain.mode"] = "cave" }, caveResourceRules: rules);
        var expandedRequest = new ChunkGenerationRequest(1, request.Address, seed, 1, expandedProfile, topology);
        using var expandedTerrain = new ChunkTerrainBuffer(32, 16);
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 32; x++)
        {
            bool blocked = x == 7 && y == 7;
            expandedTerrain.SetCell(x, y, new TerrainCell(4, 0, blocked ? 7 : 0, 1, 1,
                blocked ? TerrainCellFlags.Blocking : TerrainCellFlags.Walkable));
            if (x == 7 && y == 8) expandedTerrain.SetLiquid(x, y,
                expandedTerrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 10);
        }
        ChunkEcologyData expanded = CaveGenerationFeatureGenerator.GenerateCave(expandedRequest,
            expandedTerrain, CancellationToken.None, fixture);
        // 通过公开配置入口获取默认设置，避免跨程序集调用内部构造函数。
        var defaults = new ChunkGenerationProfileSnapshot("probe:loose:defaults", 1, 16, 16).Settings;
        return new JObject { ["cells"] = cells, ["sources"] = sources,
            ["placements"] = Capture(result), ["repeated"] = Capture(repeated), ["expanded"] = Capture(expanded),
            ["oreChance"] = profile.Settings.CaveLooseOreDensity,
            ["stoneChance"] = profile.Settings.CaveLooseStoneDensity,
            ["defaultOre"] = defaults.CaveLooseOreDensity, ["defaultStone"] = defaults.CaveLooseStoneDensity,
            ["min"] = defaults.CaveLooseResourceMinDistance, ["max"] = defaults.CaveLooseResourceMaxDistance };
    }

    #endregion

    #region 洞穴泥土地面生成采样

    // 对比正式生成器开启与关闭泥土时的原始格子，夹具不接触当前玩家世界。
    private static JObject WorldCaveDirtGround(JObject args)
    {
        var numbers = new Dictionary<string, double>
        {
            ["cave.floorTileId"] = 4, ["cave.wallTileId"] = 7,
            ["cave.dirtGround.enabled"] = 0, ["cave.dirtGround.tileId"] = 29,
            ["cave.dirtGround.chance"] = 1, ["cave.river.enabled"] = 1,
            ["cave.river.connectionChance"] = 1, ["cave.network.extraConnectionChance"] = 1,
            ["cave.portal.enabled"] = 0, ["structure.enabled"] = 0
        };
        var texts = new Dictionary<string, string> { ["terrain.mode"] = "cave" };
        var baselineProfile = new ChunkGenerationProfileSnapshot("probe:cave-dirt", 1, 64, 64, numbers, texts);
        numbers["cave.dirtGround.enabled"] = (bool?)args["enabled"] == false ? 0 : 1;
        numbers["cave.dirtGround.chance"] = Number(args, "chance", 1);
        var dirtProfile = new ChunkGenerationProfileSnapshot("probe:cave-dirt", 1, 64, 64, numbers, texts);
        using var baselineWorld = new WorldRuntime("probe:cave-dirt-baseline", 1);
        using var dirtWorld = new WorldRuntime("probe:cave-dirt-enabled", 1);
        var address = new FlatWorld.WorldModel.WorldAddress("cave", new Int2(-32, -32));
        var generator = new DeterministicChunkGenerator();
        using var baselineResult = generator.Generate(
            baselineWorld.BeginChunkGeneration(address, 17413, baselineProfile), System.Threading.CancellationToken.None);
        using var dirtResult = generator.Generate(
            dirtWorld.BeginChunkGeneration(address, 17413, dirtProfile), System.Threading.CancellationToken.None);
        if (!baselineWorld.TryCommit(baselineResult, out string baselineError))
            throw new InvalidOperationException(baselineError);
        if (!dirtWorld.TryCommit(dirtResult, out string dirtError))
            throw new InvalidOperationException(dirtError);
        baselineWorld.TryGetChunk(address, out ChunkRuntime baselineChunk);
        dirtWorld.TryGetChunk(address, out ChunkRuntime dirtChunk);
        ChunkTerrainData baseline = baselineChunk.Terrain, dirt = dirtChunk.Terrain;
        int dirtCount = 0, waterCount = 0, wallCount = 0, groundChanges = 0, otherChanges = 0;
        var samples = new JArray();
        var neighbours = new[] { new Int2(-1, 0), new Int2(1, 0), new Int2(0, -1), new Int2(0, 1) };
        for (int y = 0; y < dirt.Height; y++)
        for (int x = 0; x < dirt.Width; x++)
        {
            TerrainCell before = baseline.GetCell(x, y), after = dirt.GetCell(x, y);
            if (before.GroundTileId != after.GroundTileId) groundChanges++;
            if (before.BlockingTileId != after.BlockingTileId || before.Flags != after.Flags ||
                before.NavigationCost != after.NavigationCost || before.BackTileId != after.BackTileId ||
                before.BiomeId != after.BiomeId || baseline.GetLiquidLevel(x, y) != dirt.GetLiquidLevel(x, y) ||
                baseline.GetLiquidTypeIndex(x, y) != dirt.GetLiquidTypeIndex(x, y)) otherChanges++;
            if (after.BlockingTileId == 7) wallCount++;
            if (dirt.GetLiquidLevel(x, y) > 0) waterCount++;
            if (after.GroundTileId != 29) continue;
            dirtCount++;
            bool adjacentWater = false;
            foreach (Int2 offset in neighbours)
            {
                int nx = x + offset.X, ny = y + offset.Y;
                if (nx >= 0 && ny >= 0 && nx < dirt.Width && ny < dirt.Height && dirt.GetLiquidLevel(nx, ny) > 0)
                    adjacentWater = true;
            }
            if (x == 0 || y == 0 || x == dirt.Width - 1 || y == dirt.Height - 1) continue;
            samples.Add(new JObject { ["ground"] = after.GroundTileId, ["blocking"] = after.BlockingTileId,
                ["walkable"] = after.IsWalkable, ["cost"] = after.NavigationCost,
                ["water"] = dirt.GetLiquidLevel(x, y), ["adjacentWater"] = adjacentWater });
        }
        return new JObject { ["dirtCount"] = dirtCount, ["waterCount"] = waterCount,
            ["wallCount"] = wallCount, ["groundChanges"] = groundChanges,
            ["otherChanges"] = otherChanges, ["samples"] = samples };
    }

    #endregion

    #region 地形与生态

    private static JObject WorldEnvironmentPointer(JObject args)
    {
        var root = new GameObject("[LuaEnvironmentPointerProbe]") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            Camera camera = root.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 12f;
            camera.pixelRect = new Rect(100f, 50f, 800f, 600f);
            root.transform.position = new Vector3(13f, -7f, -10f);
            string kind = Text(args, "kind", "valid");
            Vector2 pointer = camera.pixelRect.center;
            switch (kind)
            {
                case "positive_infinity": pointer = new Vector2(float.PositiveInfinity, float.NegativeInfinity); break;
                case "negative_infinity": pointer.x = float.NegativeInfinity; break;
                case "nan_x": pointer.x = float.NaN; break;
                case "nan_y": pointer.y = float.NaN; break;
                case "outside": pointer.x = camera.pixelRect.xMin - 1f; break;
                case "disabled_camera": camera.enabled = false; break;
            }
            // 使用正式投影入口和独立相机，非法输入不进入正在游玩的世界。
            bool accepted = EnvironmentInfoDisplay.TryProjectScreenPosition(kind == "missing_camera" ? null : camera,
                pointer, out Vector3 world);
            return new JObject
            {
                ["accepted"] = accepted, ["worldX"] = world.x, ["worldY"] = world.y,
                ["finiteWorld"] = float.IsFinite(world.x) && float.IsFinite(world.y) && float.IsFinite(world.z)
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static JObject WorldNoise(JObject args)
    {
        var config = TerrainNoiseConfig.Default(NoiseType.Height);
        var point = new Vector2((float)Number(args, "x", 12.25d), (float)Number(args, "y", -37.5d));
        int seed = (int)Number(args, "seed", 17413d);
        var invalid = config;
        invalid.octaves = 0;
        float height = (float)Number(args, "height", .75d);
        float strength = (float)Number(args, "strength", 1d);
        return new JObject
        {
            ["sample"] = TerrainNoiseKernel.Sample(config, point, seed),
            ["repeated"] = TerrainNoiseKernel.Sample(config, point, seed),
            ["otherSeed"] = TerrainNoiseKernel.Sample(config, point, seed + 1),
            ["valid"] = TerrainNoiseKernel.IsValid(config, out _),
            ["invalidOctavesAccepted"] = TerrainNoiseKernel.IsValid(invalid, out _),
            ["boosted"] = TerrainNoiseKernel.ApplyHeightBoost(height, true, strength),
            ["unboosted"] = TerrainNoiseKernel.ApplyHeightBoost(height, false, strength),
            ["lowClamped"] = TerrainNoiseKernel.ApplyHeightBoost(-1f, false, strength),
            ["highClamped"] = TerrainNoiseKernel.ApplyHeightBoost(2f, true, strength)
        };
    }

    private static JObject WorldBiome(JObject args)
    {
        var ranges = new[]
        {
            new CompiledBiomeRange { Index = 3, Temperature = new float2(.2f, .8f),
                Precipitation = new float2(.1f, .9f), Height = new float2(.3f, .7f) },
            new CompiledBiomeRange { Index = 7, Temperature = new float2(0f, 1f),
                Precipitation = new float2(0f, 1f), Height = new float2(0f, 1f) }
        };
        return new JObject
        {
            ["lowerBoundary"] = BiomeResolver.ResolveIndex(.2f, .1f, .3f, ranges),
            ["upperBoundary"] = BiomeResolver.ResolveIndex(.8f, .9f, .7f, ranges),
            ["overlap"] = BiomeResolver.ResolveIndex(.5f, .5f, .5f, ranges),
            ["fallback"] = BiomeResolver.ResolveIndex(.9f, .5f, .5f, ranges),
            ["outside"] = BiomeResolver.ResolveIndex(-.01f, .5f, .5f, ranges),
            ["empty"] = BiomeResolver.ResolveIndex(.5f, .5f, .5f, Array.Empty<CompiledBiomeRange>())
        };
    }

    /// <summary>读取正式低温密度规则的原始采样，温度边界与倍率由 Lua 断言。</summary>
    private static JObject WorldGrassTemperatureDensity(JObject args)
    {
        var numbers = new Dictionary<string, double>();
        if (args["threshold"] != null)
            numbers["grass.coldThresholdCelsius"] = Number(args, "threshold", 15d);
        if (args["multiplier"] != null)
            numbers["grass.coldDensityMultiplier"] = Number(args, "multiplier", 0.5d);
        var profile = new ChunkGenerationProfileSnapshot("system-probe:grass-temperature", 1, 4, 4, numbers);
        ChunkGenerationSettingsSnapshot settings = profile.Settings;
        double threshold = settings.GrassColdThresholdCelsius;
        return new JObject
        {
            ["threshold"] = threshold,
            ["multiplier"] = settings.GrassColdDensityMultiplier,
            ["below"] = settings.ResolveGrassTemperatureMultiplier(threshold - 0.001d),
            ["at"] = settings.ResolveGrassTemperatureMultiplier(threshold),
            ["above"] = settings.ResolveGrassTemperatureMultiplier(threshold + 0.001d),
            ["repeated"] = settings.ResolveGrassTemperatureMultiplier(threshold - 0.001d)
        };
    }

    private static JObject WorldEcology(JObject args)
    {
        var rule = new EcologySpawnRuleSnapshot("system-probe:plant", "probe-plant", 1, 1d, 1d, 1 << 1,
            .25d, .75d, 0d, 1d, 0d, 1d);
        var profile = new ChunkGenerationProfileSnapshot("system-probe:ecology", 1, 4, 4,
            ecologyRules: new[] { rule });
        int seed = (int)Number(args, "seed", 17413d);
        JArray Generate(float temperature, bool blocked, double multiplier)
        {
            var request = new ChunkGenerationRequest(1,
                new FlatWorld.WorldModel.WorldAddress("probe-world", new Int2(0, 0)), seed, 1, profile);
            using var terrain = new ChunkTerrainBuffer(4, 4);
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                terrain.SetCell(x, y, new TerrainCell(1, 0, blocked ? 2 : 0, 1, 1,
                    blocked ? TerrainCellFlags.Blocking : TerrainCellFlags.Walkable));
                terrain.SetEnvironmentValue("temperature", x, y, temperature);
                terrain.SetEnvironmentValue("precipitation", x, y, .5f);
                terrain.SetEnvironmentValue("height", x, y, .5f);
            }
            var data = ChunkEcologyGenerator.Generate(request, terrain, multiplier,
                profile.EcologyRules, CancellationToken.None);
            var values = new JArray();
            foreach (NaturalItemPlacement placement in data.Placements)
                values.Add(new JObject { ["guid"] = placement.Guid, ["x"] = placement.LocalX,
                    ["y"] = placement.LocalY, ["itemId"] = placement.ItemId, ["ruleId"] = placement.RuleId });
            return values;
        }
        return new JObject
        {
            ["warm"] = Generate(.5f, false, 1d), ["repeated"] = Generate(.5f, false, 1d),
            ["cold"] = Generate(.1f, false, 1d), ["blocked"] = Generate(.5f, true, 1d),
            ["disabled"] = Generate(.5f, false, 0d),
            ["lowerBoundary"] = rule.Matches(1, .25d, 0d, 0d),
            ["upperBoundary"] = rule.Matches(1, .75d, 1d, 1d),
            ["wrongBiome"] = rule.Matches(2, .5d, .5d, .5d)
        };
    }
    #endregion

    #region 时间与环境
    private static JObject WorldCalendar(JObject args)
    {
        var time = new TimeData();
        time.SetTotalGameTimeSeconds(Number(args, "seconds", 4321.25d));
        double beforeSeconds = time.GetTotalGameTimeSeconds(), beforeCycles = time.OrbitalCycles;
        time.SetRotationPeriod(100f);
        double afterDaySeconds = time.GetTotalGameTimeSeconds();
        time.SetOrbitalPeriod(4800f);
        var copy = time.CreateRuntimeCopy();
        copy.SetTotalGameTimeSeconds(copy.GetTotalGameTimeSeconds() + 500d);
        var circular = new JArray();
        var eccentric = new JArray();
        for (int index = 0; index < 4; index++)
        {
            circular.Add(OrbitalSeasonPhysics.GetSeasonFraction((WorldSeason)index, 0d));
            eccentric.Add(OrbitalSeasonPhysics.GetSeasonFraction((WorldSeason)index, .5d));
        }
        var firstDay = new TimeData();
        SeasonSnapshot season = SeasonCalendar.Sample(firstDay);
        return new JObject
        {
            ["beforeSeconds"] = beforeSeconds, ["afterDaySeconds"] = afterDaySeconds,
            ["afterYearSeconds"] = time.GetTotalGameTimeSeconds(), ["beforeCycles"] = beforeCycles,
            ["afterCycles"] = time.OrbitalCycles, ["dayLength"] = time.DayLength,
            ["yearDays"] = time.YearLengthDays, ["days"] = time.TotalDays, ["daySeconds"] = time.CurrentTime,
            ["copySeconds"] = copy.GetTotalGameTimeSeconds(), ["circular"] = circular,
            ["eccentric"] = eccentric, ["firstSeason"] = season.Season.ToString(), ["firstYear"] = season.Year,
            ["invalidDay"] = WorldException(() => time.SetRotationPeriod(0f)),
            ["invalidYear"] = WorldException(() => time.SetOrbitalPeriod(float.PositiveInfinity)),
            ["negativeTime"] = WorldException(() => time.SetTotalGameTimeSeconds(-1d))
        };
    }

    private static JObject WorldThermal(JObject args)
    {
        float current = (float)Number(args, "current", 20d), ambient = (float)Number(args, "ambient", 120d);
        float seconds = (float)Number(args, "seconds", 10d), split = current;
        double splitDouble = current;
        for (int i = 0; i < 10; i++) split = ThermalRuntime.AdvanceAmbient(split, ambient, seconds / 10f);
        for (int i = 0; i < 10; i++) splitDouble = ThermalRuntime.AdvanceAmbient(splitDouble, (double)ambient, seconds / 10d);
        return new JObject
        {
            ["current"] = current, ["ambient"] = ambient,
            ["whole"] = ThermalRuntime.AdvanceAmbient(current, ambient, seconds), ["split"] = split,
            ["wholeDouble"] = ThermalRuntime.AdvanceAmbient((double)current, ambient, seconds), ["splitDouble"] = splitDouble,
            ["zeroTime"] = ThermalRuntime.AdvanceAmbient(current, ambient, 0f),
            ["negativeTime"] = ThermalRuntime.AdvanceAmbient(current, ambient, -1f),
            ["invalidCurrent"] = ThermalRuntime.AdvanceAmbient(float.NaN, ambient, seconds),
            ["invalidAmbient"] = ThermalRuntime.AdvanceAmbient(current, float.NaN, seconds),
            ["limited"] = ThermalRuntime.AdvanceTowards(current, ambient, 1000f, 10f)
        };
    }

    private static JObject WorldHeatSources(JObject args)
    {
        float heat = 0f, cold = 0f;
        float center = ThermalInfluenceRuntime.DistanceWeight(0f, 36f);
        float middle = ThermalInfluenceRuntime.DistanceWeight(9f, 36f);
        ThermalInfluenceRuntime.Accumulate(-50f, 40f, center, ref heat, ref cold);
        float single = ThermalInfluenceRuntime.Compose(-50f, heat, cold);
        ThermalInfluenceRuntime.Accumulate(-50f, 40f, center, ref heat, ref cold);
        float duplicate = ThermalInfluenceRuntime.Compose(-50f, heat, cold);
        heat = cold = 0f;
        ThermalInfluenceRuntime.Accumulate(50f, 40f, center, ref heat, ref cold);
        return new JObject
        {
            ["center"] = center, ["middle"] = middle,
            ["edge"] = ThermalInfluenceRuntime.DistanceWeight(36f, 36f),
            ["outside"] = ThermalInfluenceRuntime.DistanceWeight(100f, 36f),
            ["single"] = single, ["duplicate"] = duplicate,
            ["hotAmbient"] = ThermalInfluenceRuntime.Compose(50f, heat, cold)
        };
    }

    private static JObject WorldWind(JObject args)
    {
        // 轻量掉落物批量采样与单件采样共用真实风区混合规则。
        var blend = new RegionalWindBlend(
            new float3(1f, 0f, .9f), new float3(0f, 1f, .79f),
            new float3(-1f, 0f, 1f), new float3(0f, -1f, .8f), .25f, .5f);
        Vector2 blendedVelocity = WindEnvironmentRules.ResolveBlendedPushVelocity(in blend, 8);
        Vector2 unreachableVelocity = WindEnvironmentRules.ResolveBlendedPushVelocity(in blend, 11);
        var originOnly = new RegionalWindBlend(blend.Origin, blend.Horizontal, blend.Vertical,
            blend.Diagonal, 0f, 0f);
        Vector2 originVelocity = WindEnvironmentRules.ResolveBlendedPushVelocity(in originOnly, 8);
        return new JObject
        {
            ["belowLevel"] = WindEnvironmentRules.ResolveWindLevel(.799f),
            ["thresholdLevel"] = WindEnvironmentRules.ResolveWindLevel(.8f),
            ["belowPush"] = WindEnvironmentRules.ResolvePushSpeed(.799f, 8),
            ["lightPush"] = WindEnvironmentRules.ResolvePushSpeed(.8f, 8),
            ["strongPush"] = WindEnvironmentRules.ResolvePushSpeed(.9f, 8),
            ["maximumPush"] = WindEnvironmentRules.ResolvePushSpeed(1f, 8),
            ["unreachablePush"] = WindEnvironmentRules.ResolvePushSpeed(1f, 11),
            ["lightMassLevel"] = WindEnvironmentRules.ResolveRequiredWindLevel(1f, 9.81d, 1f, 19f),
            ["cutoffMassLevel"] = WindEnvironmentRules.ResolveRequiredWindLevel(10f, 9.81d, 1f, 19f),
            ["zeroGravityLevel"] = WindEnvironmentRules.ResolveRequiredWindLevel(19f, 0d, 1f, 19f),
            ["invalidMassLevel"] = WindEnvironmentRules.ResolveRequiredWindLevel(-1f, 9.81d, 1f, 19f),
            ["blendedPushX"] = blendedVelocity.x, ["blendedPushY"] = blendedVelocity.y,
            ["unreachablePushX"] = unreachableVelocity.x, ["unreachablePushY"] = unreachableVelocity.y,
            ["originPushX"] = originVelocity.x, ["originPushY"] = originVelocity.y
        };
    }

    #region 自然大雾规则采样

    private static JObject WorldNaturalFogProfile(JObject args)
    {
        var settings = ChunkWeatherConfiguration.Load();
        var climate = new ChunkWeatherClimate((float)Number(args, "humidity", 0.5), 1f, 0f,
            (float)Number(args, "wind", 0), (float)Number(args, "temperature", 20),
            (float)Number(args, "precipitation", 0));
        bool found = settings.CreateProfile(climate).TryGetElement(WeatherElementIds.Fog, out WeatherElementProfile fog);
        new RegionalWeatherSettings().CreateProfile(climate).TryGetElement(WeatherElementIds.Fog, out WeatherElementProfile defaults);
        return new JObject
        {
            ["found"] = found, ["probability"] = fog.Probability,
            ["activationProbability"] = settings.ResolveActivationProbability(fog),
            ["intensityLimit"] = fog.Intensity, ["durationLimit"] = fog.DurationSeconds,
            ["defaultProbability"] = defaults.Probability
        };
    }

    private static JObject WorldDiscreteWeather(JObject args)
    {
        string id = (string)args["id"] ?? WeatherElementIds.Wind;
        var settings = ChunkWeatherConfiguration.Load();
        settings.CreateProfile(new ChunkWeatherClimate(1f, 1f, 0f, 1f, 40f, 1f)).TryGetElement(id, out var profile);
        settings.CreateProfile(new ChunkWeatherClimate(0f, 1f, 0f, 0f, -100f, 0f)).TryGetElement(id, out var dry);
        var defaults = new RegionalWeatherSettings();
        defaults.CreateProfile(new ChunkWeatherClimate(1f, 1f, 0f, 1f, 40f, 1f)).TryGetElement(id, out var defaultProfile);
        var samples = new JArray();
        // 穷举等距随机样本的组合，Lua核对每档及每对组合的频次。
        for (int i = 0; i < 100; i++)
        for (int j = 0; j < 100; j++)
        {
            var value = settings.SampleNaturalEvent(profile, (i + .5d) / 100d, (j + .5d) / 100d, .5d);
            samples.Add(new JObject { ["intensity"] = value.Intensity, ["duration"] = value.DurationSeconds });
        }
        var rule = Array.Find(settings.Elements, value => value.Id == id);
        var frozen = settings.Snapshot();
        var custom = rule.IntensityDistribution.Clone();
        Array.Clear(custom.Probabilities, 0, custom.Probabilities.Length);
        custom.Probabilities[4] = 1f;
        var zero = frozen.SampleNaturalEvent(profile, 0d, 0d, 0d);
        var one = frozen.SampleNaturalEvent(profile, 1d, 1d, 1d);
        rule.IntensityDistribution.Probabilities[0] = 0f;
        rule.DurationDistribution.Values[0] = 301f;
        var frozenRule = Array.Find(frozen.Elements, value => value.Id == id);
        return new JObject
        {
            ["samples"] = samples, ["checkSeconds"] = settings.CheckIntervalSeconds,
            ["probability"] = frozen.ResolveActivationProbability(profile),
            ["dryProbability"] = frozen.ResolveActivationProbability(dry),
            ["defaultProbability"] = defaults.ResolveActivationProbability(defaultProfile),
            ["afterEventProbability"] = frozen.ResolveActivationProbability(profile, 1000d, 1300d),
            ["recoveryDelay"] = frozen.ResolveNaturalRecoveryEndTime(id, 1000d) - 1000d,
            ["associations"] = frozen.Associations.Length,
            ["frozenProbability"] = frozenRule.IntensityDistribution.Probabilities[0],
            ["frozenDuration"] = frozenRule.DurationDistribution.Values[0],
            ["customValue"] = custom.Sample(.1f, 1f, .99d),
            ["minimumIntensity"] = zero.Intensity, ["maximumIntensity"] = one.Intensity,
            ["minimumDuration"] = zero.DurationSeconds, ["maximumDuration"] = one.DurationSeconds,
            ["invalidTotal"] = WorldException(settings.Validate),
            ["invalidSample"] = WorldException(() => frozen.SampleNaturalEvent(profile, double.NaN, .5d, .5d))
        };
    }

    private static JObject WorldNaturalFogLifecycle(JObject args)
    {
        var settings = ChunkWeatherConfiguration.Load();
        WeatherElementRule fog = Array.Find(settings.Elements, rule => rule.Id == WeatherElementIds.Fog).Clone();
        settings.Elements = new[] { fog };
        settings.NaturalEventProbabilityMultiplier = 1f;
        settings.CheckIntervalSeconds = settings.StepSeconds = 2f;
        fog.IntensityDistribution = fog.DurationDistribution = null;
        fog.ActivationProbability = 1f;
        fog.MinimumIntensity = fog.MaximumIntensity = 0.8f;
        fog.MinimumDurationSeconds = fog.MaximumDurationSeconds = 12f;
        var engine = new RegionalWeatherEngine(settings, 17413, default, 16, 16);
        var origin = new Int2(0, 0);
        engine.RegisterChunk(origin, settings.CreateProfile(new ChunkWeatherClimate(1f, 1f, 0f, 0f, 40f)), 0d);
        engine.TryGetSnapshot(origin, out RegionalWeatherSnapshot before);
        // 概率一只消除夹具随机性，起雾仍经过正式检查、自然事件启动和快照投影。
        RegionalWeatherSnapshot started = engine.Advance(origin, 2d).Snapshot;
        engine.RegisterChunk(new Int2(16, 0), settings.CreateProfile(new ChunkWeatherClimate(0f, 0f, 0f, 0f, -20f)), 2d);
        engine.TryGetSnapshot(new Int2(100, 1), out RegionalWeatherSnapshot neighbor);
        engine.ForcePrecipitation(origin, 2d, 0.4f, 20f);
        engine.TryGetSnapshot(origin, out RegionalWeatherSnapshot withRain);
        if (!engine.TryGetState(origin, out RegionalWeatherState state))
            throw new InvalidOperationException("自然雾夹具缺少正式区域状态。");
        var restored = new RegionalWeatherEngine(settings, 17413, default, 16, 16);
        restored.RestoreState(RegionalWeatherRecord.FromState(state).ToState());
        restored.TryGetSnapshot(origin, out RegionalWeatherSnapshot restoredSample);
        var replica = new RegionalWeatherEngine(settings, 17413, default, 16, 16);
        replica.ApplyReplicatedSnapshot(withRain);
        replica.TryGetSnapshot(origin, out RegionalWeatherSnapshot replicaSample);
        restored.Clear(origin, 2d);
        restored.TryGetSnapshot(origin, out RegionalWeatherSnapshot cleared);
        engine.ForceElements(origin, 2d, 4d, fogDensity: 0.2f);
        engine.TryGetSnapshot(origin, out RegionalWeatherSnapshot forced);
        RegionalWeatherSnapshot resumed = engine.Advance(origin, 6d, false).Snapshot;
        engine.ForceElements(origin, 6d, 2d, fogDensity: 0f);
        engine.TryGetSnapshot(origin, out RegionalWeatherSnapshot zeroForced);
        RegionalWeatherSnapshot afterZero = engine.Advance(origin, 8d, false).Snapshot;
        RegionalWeatherSnapshot expired = engine.Advance(origin, 14d, false).Snapshot;
        return new JObject
        {
            ["beforeCheck"] = before.FogDensity,
            ["startedFog"] = started.FogDensity, ["startedRemaining"] = started.RemainingSeconds,
            ["neighborFog"] = neighbor.FogDensity,
            ["rainWithFog"] = withRain.RainIntensity, ["fogWithRain"] = withRain.FogDensity,
            ["restoredFog"] = restoredSample.FogDensity, ["replicaFog"] = replicaSample.FogDensity,
            ["overrideFog"] = forced.FogDensity, ["resumedFog"] = resumed.FogDensity,
            ["zeroOverrideFog"] = zeroForced.FogDensity, ["afterZeroOverrideFog"] = afterZero.FogDensity,
            ["expiredFog"] = expired.FogDensity, ["rainAfterFog"] = expired.RainIntensity,
            ["clearedFog"] = cleared.FogDensity
        };
    }

    private static JObject WorldNaturalFogValidation(JObject args)
    {
        string Reject(Action<WeatherElementRule> change)
        {
            var settings = new RegionalWeatherSettings();
            change(Array.Find(settings.Elements, rule => rule.Id == WeatherElementIds.Fog));
            return WorldException(settings.Validate);
        }
        return new JObject
        {
            ["nonFiniteSample"] = WorldException(() => new RegionalWeatherSettings().SampleNaturalEvent(
                new WeatherElementProfile(WeatherElementIds.Fog, 1f, 1f, 240f), float.NaN, 0.5f, 0.5f)),
            ["outOfRangeSample"] = WorldException(() => new RegionalWeatherSettings().SampleNaturalEvent(
                new WeatherElementProfile(WeatherElementIds.Fog, 1f, 1f, 240f), 0.5f, 0.5f, -1f)),
            ["emptyTemperatureRange"] = Reject(rule => rule.MaximumTemperatureCelsius = rule.MinimumTemperatureCelsius),
            ["negativeTemperatureWeight"] = Reject(rule => rule.TemperatureWeight = -1f)
        };
    }

    #endregion

    private static JObject WorldWeather(JObject args)
    {
        var settings = new RegionalWeatherSettings();
        var profile = settings.CreateProfile(new ChunkWeatherClimate(.5f, 1f, 0f, .5f, 10f, .5f));
        var engine = new RegionalWeatherEngine(settings, 17413, default, 16, 16);
        var origin = new Int2(0, 0);
        engine.RegisterChunk(origin, profile, 0d);
        engine.RegisterChunk(new Int2(16, 0), settings.CreateProfile(new ChunkWeatherClimate(.1f, 0f, 1f, .1f, -20f, .1f)), 0d);
        engine.ForcePrecipitation(origin, 0d, .8f, 10f);
        bool firstReady = engine.TryGetSnapshot(new Int2(1, 1), out RegionalWeatherSnapshot first);
        bool secondReady = engine.TryGetSnapshot(new Int2(100, 1), out RegionalWeatherSnapshot second);
        bool frozenReady = engine.TryGetProfile(origin, out ChunkWeatherProfile frozen);
        RegionalWeatherAdvanceResult ended = engine.Advance(origin, 10d, false);
        Int2 negative = engine.ResolveRegion(new Int2(-1, -1));
        float heldRainTarget = .8f;
        float heldSnowTarget = .2f;
        bool unavailableTargetChanged = WeatherMgr.TryUpdatePrecipitationTargets(false, 0f, 0f,
            ref heldRainTarget, ref heldSnowTarget);
        float retainedRainTarget = heldRainTarget;
        float retainedSnowTarget = heldSnowTarget;
        bool availableTargetChanged = WeatherMgr.TryUpdatePrecipitationTargets(true, .3f, .1f,
            ref heldRainTarget, ref heldSnowTarget);
        var missingPosition = new Vector2(128.5f, 64f);
        bool neighborWindAvailable = engine.TryGetWindBlend(new Unity.Mathematics.double2(
            missingPosition.x, missingPosition.y), out _);
        bool missingPrecipitationAvailable = WeatherMgr.TryGetPrecipitationSnapshot(engine, missingPosition, out _);
        bool realClearAvailable = WeatherMgr.TryGetPrecipitationSnapshot(engine, Vector2.one, out var clearSample);
        float retainedRain = .8f, retainedSnow = .2f;
        bool windOnlyTargetChanged = WeatherMgr.TryUpdatePrecipitationTargets(missingPrecipitationAvailable,
            0f, 0f, ref retainedRain, ref retainedSnow);
        return new JObject
        {
            ["firstReady"] = firstReady, ["secondReady"] = secondReady,
            ["firstRain"] = first.RainIntensity, ["secondRain"] = second.RainIntensity,
            ["firstSnow"] = first.SnowIntensity,
            ["firstRegionX"] = first.Region.X, ["secondRegionX"] = second.Region.X,
            ["frozenReady"] = frozenReady, ["frozenTemperature"] = frozen?.Climate.TemperatureCelsius,
            ["endedPrecipitation"] = ended.Snapshot.PrecipitationIntensity,
            ["unavailableTargetChanged"] = unavailableTargetChanged,
            ["retainedRainTarget"] = retainedRainTarget, ["retainedSnowTarget"] = retainedSnowTarget,
            ["availableTargetChanged"] = availableTargetChanged,
            ["updatedRainTarget"] = heldRainTarget, ["updatedSnowTarget"] = heldSnowTarget,
            ["neighborWindAvailable"] = neighborWindAvailable,
            ["missingPrecipitationAvailable"] = missingPrecipitationAvailable,
            ["windOnlyTargetChanged"] = windOnlyTargetChanged,
            ["windOnlyRainTarget"] = retainedRain, ["windOnlySnowTarget"] = retainedSnow,
            ["realClearAvailable"] = realClearAvailable, ["realClearPrecipitation"] = clearSample.PrecipitationIntensity,
            ["negativeRegionX"] = negative.X, ["negativeRegionY"] = negative.Y,
            ["rainProbability"] = settings.ResolveActivationProbability(new WeatherElementProfile("rain", .4f, .5f, 60f)),
            ["wind8Probability"] = settings.ResolveActivationProbability(new WeatherElementProfile("wind", .4f, .8f, 60f)),
            ["wind9Probability"] = settings.ResolveActivationProbability(new WeatherElementProfile("wind", .4f, .9f, 60f)),
            ["wind10Probability"] = settings.ResolveActivationProbability(new WeatherElementProfile("wind", .4f, 1f, 60f))
        };
    }

    /// <summary>只读正式雨效资源和覆盖计算，水花仍检查独立视口采样边界。</summary>
    private static JObject WorldRainVisualBatch(JObject args)
    {
        GameObject prefab = Resources.Load<GameObject>("Weather/RainEffect");
        MeshRenderer renderer = prefab != null ? prefab.GetComponent<MeshRenderer>() : null;
        Mesh mesh = RainEffectController.BuildMesh();
        JObject meshSamples;
        try
        {
            meshSamples = CaptureWeatherMeshRawSamples(mesh, RainEffectController.MaximumDrops, 78041);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        Vector4 field = RainEffectController.ResolveFieldSettings(new Vector2(20f, 10f), 1.1f, 1.5f, 1f, .5f);
        Vector4 half = RainEffectController.ResolveFieldSettings(new Vector2(20f, 10f), 1.1f, 1.5f, .5f, .5f);
        Vector4 clear = RainEffectController.ResolveFieldSettings(new Vector2(20f, 10f), 1.1f, 1.5f, 0f, .5f);
        Vector4 large = RainEffectController.ResolveFieldSettings(new Vector2(2000f, 1000f), 1.1f, 1.5f, 1f, .5f);
        Vector4 invalid = RainEffectController.ResolveFieldSettings(new Vector2(-10f, 0f), 0f, -1f, 2f, 0f);
        Vector4 negative = RainEffectController.ResolveFieldSettings(new Vector2(20f, 10f), 1.1f, 1.5f, -1f, .5f);
        Vector4 clamped = RainEffectController.ResolveFieldSettings(new Vector2(20f, 10f), 1.1f, 1.5f, 2f, .5f);
        Vector4 drift = RainEffectController.ResolveDrift(1000000007.25d, -1000000002.5d, new Vector2(20f, 10f));
        Vector4 wrapped = RainEffectController.ResolveDrift(1000000027.25d, -1000000012.5d, new Vector2(20f, 10f));
        Rect window = RainGroundSplashController.GetSamplingBounds(
            new Vector3(10f, -4f, 0f), 20f, 1.5f, new Vector2(.2f, .4f));
        Rect narrow = RainGroundSplashController.GetSamplingBounds(
            new Vector3(-40f, 17f, 0f), 5f, 0f, Vector2.zero);
        return new JObject
        {
            ["prefabPresent"] = prefab != null,
            ["shaderController"] = prefab != null && prefab.GetComponent<RainEffectController>() != null,
            ["particleSystems"] = prefab != null ? prefab.GetComponentsInChildren<ParticleSystem>(true).Length : -1,
            ["meshFilters"] = prefab != null ? prefab.GetComponentsInChildren<MeshFilter>(true).Length : -1,
            ["meshRenderers"] = prefab != null ? prefab.GetComponentsInChildren<MeshRenderer>(true).Length : -1,
            ["shader"] = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.shader.name : "",
            ["width"] = field.x, ["height"] = field.y, ["density"] = field.z,
            ["halfDensity"] = half.z, ["clearDensity"] = clear.z, ["largeDensity"] = large.z,
            ["invalidWidth"] = invalid.x, ["invalidHeight"] = invalid.y, ["invalidDensity"] = invalid.z,
            ["negativeDensity"] = negative.z, ["clampedDensity"] = clamped.z,
            ["maximumDrops"] = RainEffectController.MaximumDrops, ["shaderBudget"] = field.w,
            ["mesh"] = meshSamples,
            ["driftX"] = drift.x, ["driftY"] = drift.y, ["wrappedX"] = wrapped.x, ["wrappedY"] = wrapped.y,
            ["minX"] = window.xMin, ["maxX"] = window.xMax,
            ["minY"] = window.yMin, ["maxY"] = window.yMax,
            ["narrowMinX"] = narrow.xMin, ["narrowMaxX"] = narrow.xMax,
            ["narrowMinY"] = narrow.yMin, ["narrowMaxY"] = narrow.yMax
        };
    }
    #endregion

    #region 地表液体与建筑支撑
    private static JObject WorldLiquidBuoyancy(JObject args)
    {
        // 独立读取正式液体目录并调用共用浮沉规则，不修改当前世界的液体注册表。
        var definitions = LiquidDefinitionFactory.BuildCatalog(LiquidDefinitionFactory.DeserializeCatalog(
            StreamingAssetsTextLoader.ReadAllText(LiquidCatalogLoader.BuiltInPath)));
        string liquidId = Text(args, "liquidId", LiquidTypeCatalog.DirtyWaterId);
        LiquidDefinition liquid = definitions.Find(definition => definition.Id == liquidId)
            ?? throw new ArgumentException("浮沉采样液体不存在：" + liquidId);
        var stack = new ItemStack
        {
            Weight = (float)Number(args, "weight", 2.5d),
            Volume = (float)Number(args, "volume", .75d),
            Amount = (float)Number(args, "amount", 1d)
        };
        float ratio = WorldItemWaterRules.ResolveWeightVolumeRatio(stack);
        float threshold = WorldItemWaterRules.ResolveSinkRatioThreshold(liquid.BuoyancyThresholdMultiplier);
        return new JObject
        {
            ["ratio"] = ratio, ["threshold"] = threshold,
            ["sinks"] = WorldItemWaterRules.ShouldSink(ratio, threshold)
        };
    }

    private static JObject WorldLiquidLayer(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(1, 1);
        buffer.SetCell(0, 0, new TerrainCell(17, 0, 0, 2, 5, TerrainCellFlags.Walkable));
        using ChunkTerrainData terrain = buffer.Seal();
        int water = terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        bool added = terrain.SetLiquid(0, 0, water, 60);
        long wetRevision = terrain.LiquidRevision;
        bool repeated = terrain.SetLiquid(0, 0, water, 60);
        bool wetChanged = terrain.IsLiquidChanged(0, 0);
        string wetId = terrain.GetLiquidId(0, 0);
        bool repeatedRevisionStable = terrain.LiquidRevision == wetRevision;
        float wetDepth = terrain.GetLiquidDepth(0, 0);
        int wetLevel = terrain.GetLiquidLevel(0, 0);
        LiquidCellSaveData saved = LiquidCellSaveData.Capture(terrain, 0, 0);
        saved = MemoryPack.MemoryPackSerializer.Deserialize<LiquidCellSaveData>(MemoryPack.MemoryPackSerializer.Serialize(saved));
        string missingId = WorldException(() => terrain.SetLiquid(0, 0, 0, 5));
        bool cleared = terrain.SetLiquid(0, 0, water, 0);
        int dryLevel = terrain.GetLiquidLevel(0, 0);
        bool dryChanged = terrain.IsLiquidChanged(0, 0);
        int dryType = terrain.GetLiquidTypeIndex(0, 0);
        saved.Apply(terrain);
        int restoredLevel = terrain.GetLiquidLevel(0, 0);
        string restoredId = terrain.GetLiquidId(0, 0);
        terrain.SetLiquid(0, 0, water, 0);
        var visualLevels = new JArray();
        foreach (int level in new[] { 0, 1, 9, 10, 11, 90, 91, 99, 100 })
            visualLevels.Add(LiquidCellValue.ToVisualLevel(level));
        return new JObject
        {
            ["added"] = added, ["repeated"] = repeated, ["repeatedRevisionStable"] = repeatedRevisionStable,
            ["wetChanged"] = wetChanged, ["wetId"] = wetId, ["wetDepth"] = wetDepth, ["wetLevel"] = wetLevel,
            ["ground"] = terrain.GetCell(0, 0).GroundTileId, ["cost"] = terrain.GetCell(0, 0).NavigationCost,
            ["cleared"] = cleared, ["dryLevel"] = dryLevel,
            ["dryType"] = dryType, ["dryChanged"] = dryChanged,
            ["restoredLevel"] = restoredLevel, ["restoredId"] = restoredId,
            ["missingId"] = missingId,
            ["negativeLevel"] = LiquidCellValue.NormalizeLevel(-1),
            ["excessLevel"] = LiquidCellValue.NormalizeLevel(200),
            ["maximumLevel"] = LiquidCellValue.MaximumLevel,
            ["litersPerUnit"] = LiquidCellValue.LitersPerUnit,
            ["visualLevels"] = visualLevels,
            ["minimumGeneratedLevel"] = LiquidCellValue.FromGeneratedDepth(.000001),
            ["roundedGeneratedLevel"] = LiquidCellValue.FromGeneratedDepth(.21),
            ["exactGeneratedLevels"] = new JArray(LiquidCellValue.FromGeneratedDepth(.1f), LiquidCellValue.FromGeneratedDepth(.6f)),
            ["invalidGeneratedDepth"] = WorldException(() => LiquidCellValue.FromGeneratedDepth(double.NaN)),
            ["flow"] = WorldIntegerLiquidFlow(),
            ["visualNotifications"] = WorldLiquidVisualNotifications()
        };
    }

    // 采样正式单格和批量通知，水量每份保存但水面只在跨档或身份变化时更新。
    private static JObject WorldLiquidVisualNotifications()
    {
        using var buffer = new ChunkTerrainBuffer(3, 1);
        int water = buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        buffer.SetLiquid(0, 0, water, 100);
        buffer.SetLiquid(1, 0, water, 20);
        buffer.SetLiquid(2, 0, water, 1);
        using ChunkTerrainData terrain = buffer.Seal();
        var flags = new JArray();
        terrain.Changed += change => { if (change.Kind == TerrainChangeKind.Liquid) flags.Add(change.AffectsLiquidVisuals); };
        foreach (int amount in new[] { 99, 90, 89, 0 }) terrain.SetLiquid(0, 0, water, amount);
        terrain.SetLiquid(0, 0, water, 100);
        var batches = new JArray();
        terrain.LiquidBatchChanged += change =>
        {
            var changed = new JArray();
            var visual = new JArray();
            foreach (int index in change.CellIndices.Span) changed.Add(index);
            foreach (int index in change.VisualCellIndices.Span) visual.Add(index);
            batches.Add(new JObject { ["changed"] = changed, ["visual"] = visual });
        };
        terrain.SetLiquidBatch(new[] { new LiquidCellUpdate(0, water, 99),
            new LiquidCellUpdate(1, water, 19), new LiquidCellUpdate(2, 0, 0) });
        terrain.SetLiquidBatch(new[] { new LiquidCellUpdate(0, water, 90),
            new LiquidCellUpdate(1, water, 10), new LiquidCellUpdate(2, water, 1) });
        return new JObject { ["singleFlags"] = flags, ["batches"] = batches,
            ["remaining"] = terrain.GetLiquidLevel(0, 0) + terrain.GetLiquidLevel(1, 0) + terrain.GetLiquidLevel(2, 0) };
    }

    private static JObject WorldIntegerLiquidFlow()
    {
        // 只返回生产求解器的逐步原始采样，守恒与停稳判断交给 Lua。
        var settings = new LiquidFlowSettings();
        var solver = new LiquidFlowSolver();
        int[] neighbours = { 1, 2, 3, 4, -1, 0, -1, -1, 0, -1, -1, -1, -1, -1, -1, 0, -1, -1, 0, -1 };
        var cells = new List<LiquidFlowCell>();
        for (int i = 0; i < 5; i++) cells.Add(new LiquidFlowCell(0f, i == 0 ? 80 : 0,
            i == 0 ? 1 : 0, i == 0 ? "probe:water" : string.Empty, false, true));
        var samples = new JArray();
        for (int tick = 0; tick < 32; tick++)
        {
            solver.Calculate(cells, neighbours, settings);
            var levels = new JArray();
            for (int i = 0; i < cells.Count; i++)
            {
                levels.Add(solver.ResultLevel[i]);
                cells[i] = new LiquidFlowCell(0f, solver.ResultLevel[i], solver.ResultType[i],
                    solver.ResultType[i] == 0 ? string.Empty : "probe:water", false, true);
            }
            samples.Add(new JObject { ["levels"] = levels, ["transfers"] = solver.TransferCount });
            if (solver.TransferCount == 0) break;
        }
        int[] pair = { -1, 1, -1, -1, 0, -1, -1, -1 };
        JObject Pair(int a, int b, bool blocked, int otherType, float height = 0f)
        {
            solver.Calculate(new[] { new LiquidFlowCell(height, a, 1, "probe:water", false, true),
                new LiquidFlowCell(0f, b, b == 0 ? 0 : otherType, "probe:other", blocked, true) }, pair, settings);
            return new JObject { ["left"] = solver.ResultLevel[0], ["right"] = solver.ResultLevel[1],
                ["leftType"] = solver.ResultType[0], ["rightType"] = solver.ResultType[1], ["transfers"] = solver.TransferCount };
        }
        var canalPairs = new JArray();
        for (int source = 0; source <= LiquidCellValue.MaximumLevel; source++)
        for (int target = 0; target <= LiquidCellValue.MaximumLevel; target++)
            canalPairs.Add(new JArray(source, target,
                WorldLiquidReplenishment.GetConnectedTransferLevel(source, target)));
        return new JObject { ["samples"] = samples, ["canalPairs"] = canalPairs,
            ["blocked"] = Pair(100, 0, true, 1),
            ["foreign"] = Pair(100, 20, false, 2), ["full"] = Pair(100, 100, false, 1, 1f),
            ["single"] = Pair(1, 0, false, 1), ["edge"] = Pair(6, 0, false, 1) };
    }

    private static JObject WorldSupportLayer(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(1, 1);
        buffer.SetCell(0, 0, new TerrainCell(17, 0, 0, 2, 25, TerrainCellFlags.None));
        buffer.SetLiquid(0, 0, buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 60);
        using ChunkTerrainData terrain = buffer.Seal();
        TerrainSupportLayer.Set(terrain, 0, 0, 90, 2);
        TerrainCell surface = TerrainSupportLayer.GetSurfaceCell(terrain, 0, 0);
        float coveredSurfaceDepth = WorldLiquidSystem.GetSurfaceDepth(terrain, 0, 0);
        int baseGround = terrain.GetCell(0, 0).GroundTileId;
        float wetDepth = terrain.GetLiquidDepth(0, 0);
        TerrainSupportLayer.Set(terrain, 0, 0, 0, 2);
        TerrainCell cleared = TerrainSupportLayer.GetSurfaceCell(terrain, 0, 0);
        return new JObject
        {
            ["surfaceGround"] = surface.GroundTileId, ["surfaceCost"] = surface.NavigationCost,
            ["surfaceWalkable"] = (surface.Flags & TerrainCellFlags.Walkable) != 0,
            ["baseGround"] = baseGround, ["baseWalkable"] = (terrain.GetCell(0, 0).Flags & TerrainCellFlags.Walkable) != 0,
            ["wetDepth"] = wetDepth, ["clearedGround"] = cleared.GroundTileId,
            ["coveredSurfaceDepth"] = coveredSurfaceDepth,
            ["clearedCost"] = cleared.NavigationCost, ["clearedWalkable"] = (cleared.Flags & TerrainCellFlags.Walkable) != 0,
            ["supportId"] = TerrainSupportLayer.GetTileId(terrain, 0, 0),
            ["clearedDepth"] = terrain.GetLiquidDepth(0, 0)
        };
    }

    private static JObject WorldBuildingState(JObject args)
    {
        const string moduleId = "system-probe:shared-module";
        var sourceModule = new Ex_ModData_MemoryPackable { ID = moduleId, Name = "carrier-state", isRunning = true,
            BitData = new byte[] { 1, 2, 3 } };
        var targetModule = new Ex_ModData_MemoryPackable { ID = moduleId, Name = "placed-state", isRunning = false,
            BitData = new byte[] { 0 } };
        var source = EntityProbeData("test:carrier", 1f);
        var target = EntityProbeData("test:building", 1f);
        source.ModuleDataDic.Add(sourceModule.Name, sourceModule);
        target.ModuleDataDic.Add(targetModule.Name, targetModule);
        BuildingModuleStateTransfer.Copy(source, target, new[] { moduleId });
        var copied = (Ex_ModData_MemoryPackable)target.ModuleDataDic["placed-state"];
        int copiedFirst = copied.BitData[0];
        copied.BitData[0] = 9;
        string duplicate = WorldException(() => BuildingModuleStateTransfer.Copy(source, target, new[] { moduleId, moduleId }));
        string missing = WorldException(() => BuildingModuleStateTransfer.Copy(source, target, new[] { "system-probe:missing" }));
        source.Stack.Amount = 2f;
        string stacked = WorldException(() => BuildingModuleStateTransfer.Copy(source, target, new[] { moduleId }));
        return new JObject
        {
            ["copiedFirst"] = copiedFirst, ["sourceFirstAfterCopyMutation"] = sourceModule.BitData[0],
            ["copiedName"] = copied.Name, ["copiedId"] = copied.ID, ["copiedEnabled"] = copied.isRunning,
            ["sourceName"] = sourceModule.Name, ["sourceEnabled"] = sourceModule.isRunning,
            ["distinctModule"] = !ReferenceEquals(copied, sourceModule),
            ["distinctPayload"] = !ReferenceEquals(copied.BitData, sourceModule.BitData),
            ["duplicate"] = duplicate, ["missing"] = missing, ["stacked"] = stacked
        };
    }

    // 从正式继承目录检查落地容器受击配置，断言交给 Lua，不创建机器或玩家世界。
    private static JObject WorldPortableBuildingHealth(JObject args)
    {
        List<ItemDefinitionDto> definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
        var buildings = new JArray();
        foreach (string id in new[] { "Coconut_Shell_Building", "ClayJar_Building", "StoneMortar_Building", "WoodenBarrel_Building" })
        {
            ItemDefinitionDto definition = definitions.Find(value => value.Id == id)
                ?? throw new InvalidOperationException("缺少可放置容器定义：" + id);
            ItemHealthDefinitionDto health = definition.Health;
            Vector2 size = health?.Collider?.Size ?? Vector2.zero;
            buildings.Add(new JObject
            {
                ["id"] = id,
                ["validationError"] = WorldException(() => MachineCombatBridge.ValidateHealth(health)),
                ["hasHp"] = health?.HasHp == true,
                ["hp"] = health?.Hp ?? 0f,
                ["maxHp"] = health?.MaxHp ?? 0f,
                ["colliderType"] = health?.Collider?.Type,
                ["triggerEnabled"] = health?.Collider != null && health.Collider.Enabled != false && health.Collider.IsTrigger != false,
                ["width"] = size.x,
                ["height"] = size.y
            });
        }
        return new JObject
        {
            ["buildings"] = buildings,
            ["missingHealth"] = WorldException(() => MachineCombatBridge.ValidateHealth(null)),
            ["zeroHealth"] = WorldException(() => MachineCombatBridge.ValidateHealth(new ItemHealthDefinitionDto { MaxHp = 0f })),
            ["missingTrigger"] = WorldException(() => MachineCombatBridge.ValidateHealth(new ItemHealthDefinitionDto()))
        };
    }
    #endregion

    #region 维度与农业钓鱼
    private static JObject WorldDimensionAddress(JObject args)
    {
        var address = new global::WorldAddress(Text(args, "planet", " 火星 "), Text(args, "dimension", " CAVE "));
        var roundTrip = global::WorldAddress.FromWorldKey(address.WorldKey);
        var surface = address.WithDimension("surface");
        var fallback = global::WorldAddress.FromWorldKey(null);
        var separator = new global::WorldAddress("A__dimension__B", "cave");
        return new JObject
        {
            ["planet"] = address.RootWorldKey, ["dimension"] = address.DimensionId, ["key"] = address.WorldKey,
            ["roundTripEquals"] = roundTrip == address, ["surfaceKey"] = surface.WorldKey,
            ["surface"] = surface.IsSurface, ["valid"] = address.IsValid,
            ["fallbackKey"] = fallback.WorldKey, ["fallbackDimension"] = fallback.DimensionId,
            ["sanitizedPlanet"] = separator.RootWorldKey,
            ["differentDimensionsEqual"] = address == surface
        };
    }

    // 只调用正式世界创建入口，Lua 验证配置继承与各世界运行状态的隔离。
    private static JObject WorldIndependentDimensionInstances(JObject args)
    {
        var save = new GameSaveData();
        var surfaceAddress = new global::WorldAddress("fixture:planet", "surface");
        PlanetData surface = save.GetOrCreateWorldInstanceData(surfaceAddress);
        surface.BodyId = "fixture-body";
        surface.Radius = 768; surface.ChunkSize = new Vector2Int(24, 12);
        surface.TopologyMode = WorldTopologyMode.Wrapped; surface.SpatialDistanceScale = 2f;
        surface.AutoGenerateMap = false; surface.GlobalTemperature = 17f;
        surface.WindStrength = .7f; surface.RainTemperatureOffset = -8f;
        surface.Atmosphere = new AtmosphereState { InitialCarbonDioxidePartialPressureKPa = .4d };
        surface.CurrentWeather = WeatherType.Storm; surface.WeatherIntensity = .9f;
        surface.WeatherDataVersion = 12; surface.WeatherEventSequence = 9;
        surface.MapData_Dict["surface-only"] = new MapSave();
        surface.Ecology.DataVersion = EcologyWorldSaveData.CurrentDataVersion;
        surface.Ecology.ProfileId = "surface.frozen";
        var caveAddress = surfaceAddress.WithDimension("cave");
        PlanetData cave = save.GetOrCreateWorldInstanceData(caveAddress);
        PlanetData other = save.GetOrCreateWorldInstanceData(new global::WorldAddress("fixture:other", "cave"));
        var standaloneAddress = new global::WorldAddress("standalone:workshop", "workshop");
        PlanetData standalone = save.GetOrCreateWorldInstanceData(standaloneAddress);
        var sample = new JObject
        {
            ["surfaceKey"] = surface.Name, ["caveKey"] = cave.Name, ["bodyId"] = cave.BodyId,
            ["rootKey"] = caveAddress.RootWorldKey, ["radius"] = cave.Radius,
            ["width"] = cave.ChunkSize.x, ["height"] = cave.ChunkSize.y,
            ["topology"] = cave.TopologyMode.ToString(), ["spatialScale"] = cave.SpatialDistanceScale,
            ["autoGenerate"] = cave.AutoGenerateMap, ["temperature"] = cave.GlobalTemperature,
            ["wind"] = cave.WindStrength, ["rainOffset"] = cave.RainTemperatureOffset,
            ["atmosphereCarbonDioxide"] = cave.Atmosphere?.InitialCarbonDioxidePartialPressureKPa,
            ["separateAtmosphere"] = !ReferenceEquals(surface.Atmosphere, cave.Atmosphere),
            ["mapCount"] = cave.MapData_Dict.Count, ["ecologyProfile"] = cave.Ecology.ProfileId,
            ["weather"] = cave.CurrentWeather.ToString(), ["weatherIntensity"] = cave.WeatherIntensity,
            ["weatherVersion"] = cave.WeatherDataVersion, ["weatherSequence"] = cave.WeatherEventSequence,
            ["separateData"] = !ReferenceEquals(surface, cave) && !ReferenceEquals(cave, other),
            ["separateMaps"] = !ReferenceEquals(surface.MapData_Dict, cave.MapData_Dict),
            ["separateEcology"] = !ReferenceEquals(surface.Ecology, cave.Ecology),
            ["separateSnow"] = !ReferenceEquals(surface.SeasonalSnow, cave.SeasonalSnow),
            ["separateWeather"] = !ReferenceEquals(surface.RegionalWeatherStates, cave.RegionalWeatherStates),
            ["standaloneKey"] = standalone.Name, ["standaloneBody"] = standalone.BodyId,
            ["standaloneRoundtrip"] = global::WorldAddress.FromWorldKey(standalone.Name) == standaloneAddress,
            ["otherBody"] = other.BodyId
        };
        cave.CurrentWeather = WeatherType.Rain;
        cave.MapData_Dict["cave-only"] = new MapSave();
        sample["sameInstanceOnRepeat"] = ReferenceEquals(cave, save.GetOrCreateWorldInstanceData(caveAddress));
        sample["repeatMapCount"] = cave.MapData_Dict.Count;
        sample["repeatWeather"] = cave.CurrentWeather.ToString();
        sample["surfaceMapCount"] = surface.MapData_Dict.Count;
        sample["surfaceWeather"] = surface.CurrentWeather.ToString();
        sample["otherMapCount"] = other.MapData_Dict.Count;
        return sample;
    }

    /// <summary>隔离 Entity World 运行正式植物 Job，Lua 持有光照和全天耗水断言。</summary>
    private static JObject WorldPlantGrowth(JObject args)
    {
        using var world = new Unity.Entities.World("PlantGrowthRules");
        var manager = world.EntityManager;
        var system = world.CreateSystemManaged<FlatWorld.AIECS.EntityPlantModuleSystem>();
        system.SetSeasons(Array.Empty<FlatWorld.AIECS.EntitySeasonPeriod>());
        float dayLength = args.Value<float?>("dayLength") ?? 1440f;
        float seconds = args.Value<float?>("seconds") ?? dayLength;
        float initialProgress = args.Value<bool?>("mature") == true ? 1f : 0f;
        byte cultivated = (byte)(args.Value<bool?>("wild") == true ? 0 : 1);
        var soilCell = new Unity.Mathematics.int2(17, -29);
        const float initialClimateStress = 0.25f;
        var entity = manager.CreateEntity(typeof(FlatWorld.AIECS.EntityPlantLifecycle),
            typeof(FlatWorld.AIECS.EntityPlantSoil), typeof(FlatWorld.AIECS.EntityGrowth),
            typeof(FlatWorld.AIECS.AiecsVital), typeof(FlatWorld.AIECS.EntityModuleAppearance),
            typeof(FlatWorld.AIECS.EntityModuleActive));
        manager.SetComponentData(entity, new FlatWorld.AIECS.EntityPlantLifecycle
        {
            Initialized = 1, WaterInitialized = 1,
            Cultivated = cultivated, GrowWild = 1, ScaleByStage = 1, SoilCell = soilCell,
            ClimateStress = initialClimateStress
        });
        manager.SetComponentData(entity, new FlatWorld.AIECS.EntityPlantSoil
        {
            Available = 1, Water = args.Value<int?>("water") ?? 10, MaxWater = 10,
            Fertility = args.Value<float?>("fertility") ?? 1f, MaxFertility = 1f,
            MinimumWaterMultiplier = 0.5f, MinimumFertilityMultiplier = 0.5f,
            WaterConsumptionMultiplier = args.Value<float?>("waterMultiplier") ?? 1f,
            FertilityPerSecond = 0.0001f,
            MinimumLightIntensity = args.Value<float?>("minimumLight") ?? FlatWorld.AIECS.EntityPlantRules.DefaultMinimumLightIntensity,
            LightIntensity = args.Value<float?>("light") ?? 1f
        });
        manager.SetComponentData(entity, new FlatWorld.AIECS.EntityGrowth
        { Progress = initialProgress, MaxProgress = 1f, Speed = 0.0001f, EnvironmentMultiplier = 1f });
        manager.SetComponentData(entity, new FlatWorld.AIECS.AiecsVital { Hp = 10f, MaxHp = 10f });
        if (args.Value<bool?>("temperatureStress") == true)
            manager.AddComponentData(entity, new FlatWorld.AIECS.EntityClimate
            {
                EnvironmentReady = 1, MinimumSurvivalTemperature = 0f, MaximumSurvivalTemperature = 40f,
                MinimumGrowthTemperature = 5f, MaximumGrowthTemperature = 35f,
                AmbientCelsius = -1f, BaselineCelsius = -1f, FatalExposureHours = 48f
            });
        system.DayLength = dayLength; system.GameTime = seconds; system.StepSeconds = seconds;
        system.GrowthMultiplier = args.Value<float?>("growthMultiplier") ?? 1f;
        system.Update(); system.Complete();
        var growth = manager.GetComponentData<FlatWorld.AIECS.EntityGrowth>(entity);
        var soil = manager.GetComponentData<FlatWorld.AIECS.EntityPlantSoil>(entity);
        float usedWater = soil.UsedWater;
        float usedFertility = soil.UsedFertility;
        if (args.Value<bool?>("repeatSameTime") == true)
        {
            system.Update(); system.Complete();
            soil = manager.GetComponentData<FlatWorld.AIECS.EntityPlantSoil>(entity);
        }
        // 非零哨兵可直接抓到旧 Burst 布局把时钟、坐标和身份字段互相覆盖的问题。
        var lifecycle = manager.GetComponentData<FlatWorld.AIECS.EntityPlantLifecycle>(entity);
        return new JObject
        {
            ["before"] = initialProgress, ["progress"] = growth.Progress,
            ["water"] = soil.Water, ["waterRemainder"] = soil.WaterConsumptionRemainder,
            ["usedWater"] = usedWater, ["usedFertility"] = usedFertility,
            ["repeatedUsedWater"] = soil.UsedWater,
            ["status"] = lifecycle.Status.ToString(),
            ["cultivated"] = lifecycle.Cultivated, ["expectedCultivated"] = cultivated,
            ["growWild"] = lifecycle.GrowWild, ["scaleByStage"] = lifecycle.ScaleByStage,
            ["harvested"] = lifecycle.Harvested,
            ["soilX"] = lifecycle.SoilCell.x, ["soilY"] = lifecycle.SoilCell.y,
            ["expectedSoilX"] = soilCell.x, ["expectedSoilY"] = soilCell.y,
            ["climateStress"] = lifecycle.ClimateStress, ["initialClimateStress"] = initialClimateStress,
            ["lastWaterWorldTime"] = lifecycle.LastWaterWorldTime, ["expectedWaterWorldTime"] = seconds,
            ["waterInitialized"] = lifecycle.WaterInitialized
        };
    }

    private static JObject WorldSoil(JObject args)
    {
        var soil = new TileData_Farmland { waterValue = 2, fertilityValue = new GameValue_float(.5f) };
        soil.AddWater(200); soil.AddFertility(2f);
        float fullWater = soil.waterValue, fullFertility = soil.Fertility;
        var clone = (TileData_Farmland)soil.Clone();
        clone.ConsumeWater(1000); clone.ConsumeFertility(5f);
        float sourceWaterAfterClone = soil.waterValue, sourceFertilityAfterClone = soil.Fertility;
        soil.ConsumeWater(1000); soil.ConsumeFertility(5f);
        var progress = new TileData_Farmland { waterValue = 2 };
        progress.AccumulateWaterSupply(.25f);
        int waterAfterPartialSupply = progress.waterValue;
        float partialSupply = progress.waterSupplyRemainder;
        for (int i = 0; i < 3; i++) progress.AccumulateWaterSupply(.25f);
        int waterAfterWholeSupply = progress.waterValue;
        progress.AccumulateWaterConsumption(.25f);
        int waterAfterPartialConsumption = progress.waterValue;
        float partialConsumption = progress.waterConsumptionRemainder;
        for (int i = 0; i < 3; i++) progress.AccumulateWaterConsumption(.25f);
        int waterAfterWholeConsumption = progress.waterValue;
        progress.AddWater(100);
        progress.AccumulateWaterSupply(.75f);
        progress.ConsumeWater(100);
        progress.AccumulateWaterConsumption(.75f);
        return new JObject
        {
            ["fullWater"] = fullWater, ["fullFertility"] = fullFertility,
            ["sourceWaterAfterClone"] = sourceWaterAfterClone, ["sourceFertilityAfterClone"] = sourceFertilityAfterClone,
            ["emptyWater"] = soil.waterValue, ["emptyFertility"] = soil.Fertility,
            ["cloneWater"] = clone.waterValue, ["cloneFertility"] = clone.Fertility,
            ["waterAfterPartialSupply"] = waterAfterPartialSupply, ["partialSupply"] = partialSupply,
            ["waterAfterWholeSupply"] = waterAfterWholeSupply,
            ["waterAfterPartialConsumption"] = waterAfterPartialConsumption, ["partialConsumption"] = partialConsumption,
            ["waterAfterWholeConsumption"] = waterAfterWholeConsumption,
            ["fullSupplyRemainder"] = progress.waterSupplyRemainder,
            ["emptyConsumptionRemainder"] = progress.waterConsumptionRemainder,
            ["frozenConsumptionBudget"] = FlatWorld.AIECS.EntityPlantRules.CalculateWaterConsumption(2, .75f, 100f, 30f),
            ["emptyConsumptionBudget"] = FlatWorld.AIECS.EntityPlantRules.CalculateWaterConsumption(0, 0f, 100f, 30f),
            ["halfWaterMultiplier"] = PlantGrowthEnvironment.CalculateFarmlandGrowthMultiplier(
                new TileData_Farmland { waterValue = 5, fertilityValue = new GameValue_float(1f) }, 0f, 0f),
            ["fullSoilMultiplier"] = PlantGrowthEnvironment.CalculateFarmlandGrowthMultiplier(
                new TileData_Farmland { waterValue = 10, fertilityValue = new GameValue_float(1f) }),
            ["halfFertilityMultiplier"] = PlantGrowthEnvironment.CalculateFarmlandGrowthMultiplier(
                new TileData_Farmland { waterValue = 10, fertilityValue = new GameValue_float(.5f) }),
            ["drySoilMultiplier"] = PlantGrowthEnvironment.CalculateFarmlandGrowthMultiplier(
                new TileData_Farmland { waterValue = 0, fertilityValue = new GameValue_float(1f) }),
            ["emptyFertilityMultiplier"] = PlantGrowthEnvironment.CalculateFarmlandGrowthMultiplier(
                new TileData_Farmland { waterValue = 10, fertilityValue = new GameValue_float(0f) }),
            ["growthDelta"] = PlantGrowthEnvironment.CalculateGrowthDelta(2f, .75f, 1.5f, 3f, .5f),
            ["pausedGrowthDelta"] = PlantGrowthEnvironment.CalculateGrowthDelta(2f, .75f, 1.5f, 0f, .5f),
            ["negativeTimeGrowthDelta"] = PlantGrowthEnvironment.CalculateGrowthDelta(2f, .75f, 1.5f, 3f, -.5f),
            ["missingWeatherMultiplier"] = PlantGrowthEnvironment.ResolveWeatherGrowthMultiplier(null, .15f),
            ["negativeAddWater"] = WorldException(() => soil.AddWater(-1)),
            ["negativeConsumeWater"] = WorldException(() => soil.ConsumeWater(-1)),
            ["negativeAddFertility"] = WorldException(() => soil.AddFertility(-1f)),
            ["negativeConsumeFertility"] = WorldException(() => soil.ConsumeFertility(-1f))
        };
    }

    private static JObject WorldFishingCastSupplies(JObject args)
    {
        float maximum = (float)Number(args, "maximum", 10);
        var hook = new Data_GeneralItem
        {
            IDName = "fishing-hook-probe", Guid = 701,
            Tags = new List<string> { Flag(args, "wrongHook", false) ? "Stone" : Mod_FishingRod.HookTag },
            Stack = new ItemStack { Amount = (float)Number(args, "hookAmount", 1), Stackable = true },
            MaxDurability = maximum, Durability = (float)Number(args, "durability", maximum)
        };
        var hookSlot = new ItemSlot(0) { itemData = Flag(args, "missingHook", false) ? null : hook };
        var baitSlot = new ItemSlot(1);
        var rig = new Mod_FishingRod.RigInventory
        {
            Data = new Inventory_Data(new List<ItemSlot> { hookSlot, baitSlot }, "抛竿耗材样本")
        };
        ItemData replacement = null, lastBait = null;
        int accepted = 0, broken = 0;
        float firstRemaining = hook.Durability, beforeLastRemaining = hook.Durability;
        try
        {
            rig.InitData();
            if (Flag(args, "replaceHookDuringCommit", false))
            {
                replacement = ItemInstanceDataFactory.CloneRuntime(hook);
                rig.Data.Event_OnBeforeDataChanged += slot =>
                {
                    if (ReferenceEquals(slot, hookSlot)) slot.itemData = replacement;
                };
            }
            int attempts = Mathf.Clamp((int)Number(args, "attempts", 1), 1, 1001);
            for (int index = 0; index < attempts; index++)
            {
                lastBait = new Data_GeneralItem
                {
                    IDName = "fishing-bait-probe", Guid = 800 + index,
                    Tags = new List<string> { "Food" },
                    Stack = new ItemStack { Amount = (float)Number(args, "baitAmount", 1), Stackable = true }
                };
                if (!Flag(args, "wrongBait", false))
                    lastBait.ModuleDataDic.Add("food", new ModData_FoodData { ID = "Mod_Food" });
                baitSlot.itemData = Flag(args, "missingBait", false) ? null : lastBait;
                ItemData expectedHook = Flag(args, "staleHook", false) ? ItemInstanceDataFactory.CloneRuntime(hook) : hook;
                ItemData expectedBait = Flag(args, "staleBait", false) ? ItemInstanceDataFactory.CloneRuntime(lastBait) : lastBait;
                if (rig.TryConsumeCastSupplies(expectedHook, expectedBait, out bool hookBroke)) accepted++;
                if (hookBroke) broken++;
                if (index == 0) firstRemaining = hook.Durability;
                if (index + 1 == maximum - 1) beforeLastRemaining = hook.Durability;
            }
            return new JObject
            {
                ["accepted"] = accepted, ["broken"] = broken, ["firstRemaining"] = firstRemaining,
                ["beforeLastRemaining"] = beforeLastRemaining, ["remaining"] = hook.Durability,
                ["hookAmount"] = hook.Stack.Amount, ["hookEmpty"] = hookSlot.itemData == null,
                ["baitAmount"] = lastBait?.Stack?.Amount ?? 0f, ["baitEmpty"] = baitSlot.itemData == null,
                ["baitRestored"] = ReferenceEquals(baitSlot.itemData, lastBait),
                ["replacementPreserved"] = replacement != null && ReferenceEquals(hookSlot.itemData, replacement),
                ["stillMaterial"] = !hook.Tags.Contains("Tool")
            };
        }
        finally { rig.UnbindRuntimeDataEvents(); }
    }

    private static JObject WorldFishingRig(JObject args)
    {
        var rig = new Mod_FishingRod.RigInventory
        {
            Data = new Inventory_Data(new List<ItemSlot> { new(0), new(1) }, "独立钓具样本")
        };
        var invalid = new Mod_FishingRod.RigInventory
        {
            Data = new Inventory_Data(new List<ItemSlot> { new(0) }, "非法钓具样本")
        };
        try
        {
            rig.InitData();
            var hook = new ItemSlot(0) { itemData = new Data_GeneralItem { Tags = new List<string> { "FishingHook" } } };
            var food = new ItemSlot(1) { itemData = new Data_GeneralItem { Tags = new List<string> { "Food" } } };
            var wrong = new ItemSlot(2) { itemData = new Data_GeneralItem { Tags = new List<string> { "Stone" } } };
            return new JObject
            {
                ["hookAccepted"] = rig.CanAcceptQuickTransfer(hook, rig.Data.itemSlots[0]),
                ["foodAccepted"] = rig.CanAcceptQuickTransfer(food, rig.Data.itemSlots[1]),
                ["foodInHookAccepted"] = rig.CanAcceptQuickTransfer(food, rig.Data.itemSlots[0]),
                ["wrongBaitAccepted"] = rig.CanAcceptQuickTransfer(wrong, rig.Data.itemSlots[1]),
                ["hookCapacity"] = rig.Data.itemSlots[0].SlotMaxVolume, ["baitCapacity"] = rig.Data.itemSlots[1].SlotMaxVolume,
                ["unlimitedSlots"] = rig.Data.HasUnlimitedSlots, ["unlimitedStack"] = rig.Data.HasUnlimitedStackSize,
                ["invalidSlots"] = WorldException(invalid.InitData)
            };
        }
        finally { rig.UnbindRuntimeDataEvents(); invalid.UnbindRuntimeDataEvents(); }
    }
    #endregion

    #region 机械动力与电力
    private static MachineEntity WorldMachineNode(int id, int x, float torque = 0f, float load = 0f, string mode = "relay") => new()
    {
        Id = id, Cell = new Vector2Int(x, 0), State = new MachineState(), Snapshot = new Data_GeneralItem(),
        Definition = new MachineDefinition { Id = "system-probe:" + id, Torque = torque, TorqueLoad = load,
            Rpm = 60f, RequiredRpm = 60f, PortMode = mode }
    };
    private static MechanicalNetworkGraph WorldMechanicalGraph(params MachineEntity[] nodes)
    {
        var graph = new MechanicalNetworkGraph(new Vector2Int(16, 16), Vector2Int.zero, default(WorldTopologyDomain));
        graph.Rebuild(nodes);
        return graph;
    }
    private static void WorldSolveMechanical(MechanicalNetwork network) => MechanicalNetworkGraph.Solve(network,
        node => node.Definition.Torque > 0f ? 1f : 0f, node => node.Definition.Rpm * node.Definition.SourceRotationDirection, 60f);

    // 独立拓扑只准备方向与转速，鼓风合并和材料受热均调用正式入口。
    private static JObject WorldBellowsEffects(JObject args)
    {
        MachineEntity Bellows(int id, Vector2Int cell, int rotation)
        {
            MachineEntity node = WorldMachineNode(id, cell.x);
            node.Cell = cell;
            node.RotationQuarterTurns = rotation;
            node.Definition.Kind = "bellows";
            node.Definition.RequiredRpm = 20f;
            node.Definition.AirflowHeatingRateBonusPercent = 10f;
            return node;
        }
        var first = Bellows(1, Vector2Int.left, 0);
        var second = Bellows(2, Vector2Int.down, 1);
        var graph = WorldMechanicalGraph(first, second);
        bool bellowsIndexed = graph.HasBellows;
        foreach (MechanicalNetwork network in graph.Networks) network.Active = true;
        JObject Sample()
        {
            MachineAirflowEffect effect = MachineWorld.SampleBellowsAirflow(graph, Vector2Int.zero);
            float rate = effect.GetHeatingRatePercent(50f, true);
            var material = new Data_GeneralItem
            {
                IDName = "system-probe:bellows-heated-material",
                Stack = new ItemStack { Amount = 1f },
                MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 500f }
            };
            ItemMatterRuntime.Advance(material, 501f, 1f, 1f, heatingRateMultiplier: rate / 100f);
            return new JObject { ["strength"] = effect.Strength, ["rate"] = rate,
                ["materialTemperature"] = material.MatterState.TemperatureCelsius };
        }
        var result = new JObject { ["none"] = Sample() };
        first.Rpm = 20f;
        result["single"] = Sample();
        second.Rpm = 20f;
        result["multiple"] = Sample();
        second.Rpm = 10f;
        result["partial"] = Sample();
        second.Rpm = -10f;
        result["reversed"] = Sample();
        second.Rpm = 40f;
        result["overspeed"] = Sample();
        second.Rpm = 0f;
        result["stopped"] = Sample();
        second.Rpm = 20f;
        second.RotationQuarterTurns = 3;
        result["misdirected"] = Sample();
        second.RotationQuarterTurns = 1;
        second.Network.Active = false;
        result["inactive"] = Sample();
        second.Network.Active = true;
        MachineAirflowEffect supplied = MachineWorld.SampleBellowsAirflow(graph, Vector2Int.zero);
        result["rejectedRate"] = supplied.GetHeatingRatePercent(50f, false);
        result["otherBaseRate"] = supplied.GetHeatingRatePercent(100f, true);
        MachineAirflowEffect combined = supplied.Combine(MachineAirflowEffect.FromSource(.5f, 10f));
        result["combined"] = new JObject { ["strength"] = combined.Strength,
            ["rate"] = combined.GetHeatingRatePercent(50f, true) };
        graph.Rebuild(Array.Empty<MachineEntity>());
        result["cleared"] = Sample();
        bool emptyFastPath = !graph.HasBellows;
        MachineEntity unrelated = WorldMachineNode(41, 0);
        unrelated.Definition.Kind = "gear";
        graph.Rebuild(new[] { unrelated });
        bool nonBellowsFastPath = !graph.HasBellows &&
            MachineWorld.SampleBellowsAirflow(graph, Vector2Int.zero).Strength == 0f;
        graph.Rebuild(new[] { first, second });
        bool restoredAfterRebuild = graph.HasBellows;
        result["bellowsIndexed"] = bellowsIndexed;
        result["emptyFastPath"] = emptyFastPath;
        result["nonBellowsFastPath"] = nonBellowsFastPath;
        result["restoredAfterRebuild"] = restoredAfterRebuild;
        var catalog = JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(
            Application.dataPath, "Resources/Config/Mechanical/mechanical-catalog.json")));
        foreach (JObject node in (JArray)catalog["Nodes"])
            if (node.Value<string>("Id") == "MechanicalBellows")
                result["configuredBonus"] = node["AirflowHeatingRateBonusPercent"];
        return result;
    }

    private static JObject WorldMechanicalLoads(JObject args)
    {
        var source = WorldMachineNode(1, 0, 100f, 0f, "output");
        var near = WorldMachineNode(2, 1, 0f, 60f);
        var far = WorldMachineNode(3, 2, 0f, 60f, "input");
        var graph = WorldMechanicalGraph(source, near, far);
        WorldSolveMechanical(graph.Networks[0]);
        var first = new JObject { ["nearSatisfied"] = near.LoadSatisfied, ["farSatisfied"] = far.LoadSatisfied,
            ["nearRpm"] = near.Rpm, ["farRpm"] = far.Rpm };
        source.Definition.Torque = 150f;
        WorldSolveMechanical(graph.Networks[0]);
        var increased = new JObject { ["nearSatisfied"] = near.LoadSatisfied, ["farSatisfied"] = far.LoadSatisfied,
            ["nearRpm"] = near.Rpm, ["farRpm"] = far.Rpm };
        source.Definition.Torque = 0f;
        WorldSolveMechanical(graph.Networks[0]);
        return new JObject { ["first"] = first, ["increased"] = increased,
            ["stoppedNearRpm"] = near.Rpm, ["stoppedFarRpm"] = far.Rpm,
            ["stoppedNearSatisfied"] = near.LoadSatisfied, ["stoppedFarSatisfied"] = far.LoadSatisfied };
    }

    private static JObject WorldMechanicalGears(JObject args)
    {
        var source = WorldMachineNode(10, 0, 100f, 0f, "output");
        var load = WorldMachineNode(11, 1, 0f, 20f, "input");
        source.Definition.Kind = load.Definition.Kind = "gear";
        var graph = WorldMechanicalGraph(source, load);
        WorldSolveMechanical(graph.Networks[0]);
        float gearRpm = load.Rpm;
        bool gearSatisfied = load.LoadSatisfied;
        var left = WorldMachineNode(20, 0, 100f, 0f, "output");
        var relay = WorldMachineNode(21, 1);
        var right = WorldMachineNode(22, 2, 100f, 0f, "output");
        right.Definition.Rpm = 90f;
        var conflictGraph = WorldMechanicalGraph(left, relay, right);
        WorldSolveMechanical(conflictGraph.Networks[0]);
        return new JObject { ["gearRpm"] = gearRpm, ["gearSatisfied"] = gearSatisfied,
            ["conflict"] = conflictGraph.Networks[0].OutputConflict, ["conflictRelayRpm"] = relay.Rpm };
    }

    private static JObject WorldElectricalBattery(JObject args)
    {
        var battery = WorldMachineNode(30, 0);
        battery.Definition.Ports = "none";
        battery.Definition.Electrical = new ElectricalDefinition { Role = "battery", CapacityJoules = 100f,
            MaxChargeWatts = 100f, MaxDischargeWatts = 100f };
        battery.ElectricalStoredJoules = 100f;
        var consumer = WorldMachineNode(31, 1);
        consumer.Definition.Ports = "none";
        consumer.Definition.Electrical = new ElectricalDefinition { Role = "consumer", PowerWatts = 50f };
        var network = new ElectricalNetwork();
        network.Nodes.AddRange(new[] { battery, consumer });
        int notifications = 0;
        for (int i = 0; i < 5; i++) ElectricalNetworkGraph.Solve(network, .1f, _ => 1f, _ => 1f, _ => notifications++, false);
        float previewEnergy = battery.ElectricalStoredJoules;
        int previewNotifications = notifications;
        ElectricalNetworkGraph.Solve(network, .1f, _ => 1f, _ => 1f, _ => notifications++, true);
        return new JObject { ["previewEnergy"] = previewEnergy, ["previewNotifications"] = previewNotifications,
            ["committedEnergy"] = battery.ElectricalStoredJoules, ["notifications"] = notifications,
            ["deliveredWatts"] = network.DeliveredWatts, ["suppliedWatts"] = consumer.ElectricalSuppliedWatts };
    }

    private static JObject WorldElectricalLimits(JObject args)
    {
        var generator = WorldMachineNode(40, 0);
        generator.Definition.Electrical = new ElectricalDefinition { Role = "generator", PowerWatts = 50f };
        var consumer = WorldMachineNode(41, 1);
        consumer.Definition.Electrical = new ElectricalDefinition { Role = "consumer", PowerWatts = 100f };
        var network = new ElectricalNetwork();
        network.Nodes.AddRange(new[] { generator, consumer });
        ElectricalNetworkGraph.Solve(network, 1f, _ => 1f, _ => 1f, null);
        var limited = new JObject { ["delivered"] = network.DeliveredWatts, ["demand"] = network.DemandWatts,
            ["ratio"] = consumer.ElectricalPowerRatio, ["current"] = consumer.ElectricalCurrentAmps };
        var conflict = WorldMachineNode(42, 2);
        conflict.Definition.Electrical = new ElectricalDefinition { Role = "generator", PowerWatts = 50f, NominalVoltage = 24f };
        network.Nodes.Add(conflict);
        ElectricalNetworkGraph.Solve(network, 1f, _ => 1f, _ => 1f, null);
        return new JObject { ["limited"] = limited, ["voltageConflict"] = network.VoltageConflict,
            ["conflictDelivered"] = network.DeliveredWatts, ["conflictSupplied"] = consumer.ElectricalSuppliedWatts };
    }

    private static JObject WorldElectricalUnlimitedSupply(JObject args)
    {
        int sourceCount = Mathf.Clamp((int)Number(args, "sources", 1d), 1, 2);
        int steps = Mathf.Clamp((int)Number(args, "steps", 1d), 1, 100);
        float seconds = (float)Number(args, "seconds", .5d);
        float ReadFactor(string key)
        {
            // Lua 用字符串描述非有限系数，不向 JSON 写入非法数值。
            return Text(args, key + "Kind", "number") switch
            {
                "number" => (float)Number(args, key, 1d),
                "nan" => float.NaN,
                "positive_infinity" => float.PositiveInfinity,
                "negative_infinity" => float.NegativeInfinity,
                _ => throw new ArgumentException("未知电网系数类型：" + key)
            };
        }
        float providerFactor = ReadFactor("providerFactor");
        float demandFactor = ReadFactor("demandFactor");
        bool commitStorage = Flag(args, "commitStorage", true);
        var nodes = new List<MachineEntity>();
        var sources = new List<MachineEntity>();
        for (int i = 0; i < sourceCount; i++)
        {
            var source = WorldMachineNode(60 + i, 0);
            source.RotationQuarterTurns = (int)Number(args, "sourceRotation", 0d);
            source.Definition.Ports = "none";
            source.Definition.Electrical = new ElectricalDefinition { Role = "generator", UnlimitedGeneration = true,
                Connection = Text(args, "sourceConnection", "cell"),
                NominalVoltage = i == 0 ? 120f : (float)Number(args, "secondSourceVoltage", 120d) };
            sources.Add(source);
            nodes.Add(source);
        }
        var consumer = WorldMachineNode(70, 2);
        consumer.Definition.Ports = "none";
        consumer.Definition.Electrical = new ElectricalDefinition { Role = "consumer",
            PowerWatts = (float)Number(args, "demandWatts", 12000d),
            NominalVoltage = (float)Number(args, "consumerVoltage", 120d) };
        nodes.Add(consumer);
        MachineEntity battery = null;
        if (Flag(args, "battery", false))
        {
            battery = WorldMachineNode(71, 2);
            battery.Definition.Ports = "none";
            battery.Definition.Electrical = new ElectricalDefinition { Role = "battery", CapacityJoules = 1000f,
                MaxChargeWatts = 100f, MaxDischargeWatts = 100f };
            battery.ElectricalStoredJoules = (float)Number(args, "batteryJoules", 250d);
            nodes.Add(battery);
        }
        for (int x = 0; x <= 2; x++)
        {
            if (x == 1 && !Flag(args, "connected", true)) continue;
            var wire = WorldMachineNode(80 + x, x);
            wire.Definition.Ports = "none";
            wire.Definition.Electrical = new ElectricalDefinition { Role = "wire", MaxCurrentAmps = 1000f,
                MaximumVoltage = (float)Number(args, "wireMaximumVoltage", 0d) };
            nodes.Add(wire);
        }

        // 独立节点先走正式接线图，再逐步采样正式解算，不借用当前玩家的机器世界。
        var graph = new ElectricalNetworkGraph(default(WorldTopologyDomain));
        graph.Rebuild(nodes);
        int notifications = 0;
        var samples = new JArray();
        for (int step = 0; step < steps; step++)
        {
            foreach (ElectricalNetwork network in graph.Networks)
                ElectricalNetworkGraph.Solve(network, seconds, _ => providerFactor, _ => demandFactor,
                    _ => notifications++, commitStorage);
            ElectricalNetwork connectedNetwork = consumer.ElectricalNetwork;
            var sourceWatts = new JArray();
            var sourceJoules = new JArray();
            foreach (MachineEntity source in sources)
            {
                sourceWatts.Add(source.ElectricalGeneratedWatts);
                sourceJoules.Add(source.ElectricalStoredJoules);
            }
            samples.Add(new JObject { ["generatedWatts"] = connectedNetwork.GeneratedWatts,
                ["voltage"] = connectedNetwork.Voltage,
                ["deliveredWatts"] = connectedNetwork.DeliveredWatts, ["demandWatts"] = connectedNetwork.DemandWatts,
                ["chargingWatts"] = connectedNetwork.ChargingWatts, ["dischargingWatts"] = connectedNetwork.DischargingWatts,
                ["wastedWatts"] = connectedNetwork.WastedWatts, ["currentAmps"] = connectedNetwork.CurrentAmps,
                ["suppliedWatts"] = consumer.ElectricalSuppliedWatts, ["powerRatio"] = consumer.ElectricalPowerRatio,
                ["voltageConflict"] = connectedNetwork.VoltageConflict,
                ["voltageIncompatible"] = connectedNetwork.VoltageIncompatible,
                ["sourceWatts"] = sourceWatts, ["sourceJoules"] = sourceJoules,
                ["batteryJoules"] = battery?.ElectricalStoredJoules ?? 0f, ["notifications"] = notifications });
        }
        var definition = new ElectricalDefinition { Role = "generator", UnlimitedGeneration = true };
        string generatorValidation = WorldException(() => definition.Validate("system-probe:unlimited"));
        definition.Role = "consumer";
        definition.PowerWatts = 1f;
        return new JObject { ["samples"] = samples, ["networkCount"] = graph.Networks.Count,
            ["generatorValidation"] = generatorValidation,
            ["consumerValidation"] = WorldException(() => definition.Validate("system-probe:invalid-unlimited")) };
    }
    #endregion

    #region 工业气液库存与热力学
    private static FluidCatalog WorldFluidCatalog(bool phaseChange = false)
    {
        var catalog = new FluidCatalog();
        var definition = new FluidDefinitionDto { Id = "system-probe:fluid", MolarMassKgPerMol = .018d,
            LiquidDensityKgPerLiter = 1d, GasHeatCapacityJPerMolKelvin = 34d,
            LiquidHeatCapacityJPerMolKelvin = 75d, LitersPerServing = .1m };
        if (phaseChange) definition.PhaseChange = new FluidPhaseChangeDto
        {
            LatentHeatJPerMol = 40000d, ReferenceBoilingKelvin = 373d, CriticalTemperatureKelvin = 650d,
            Saturation = new List<FluidSaturationPointDto> {
                new() { TemperatureKelvin = 273d, PressureKPa = .6d },
                new() { TemperatureKelvin = 373d, PressureKPa = 100d },
                new() { TemperatureKelvin = 600d, PressureKPa = 10000d } }
        };
        catalog.Register(new[] { new FluidDefinition(definition), new FluidDefinition(new FluidDefinitionDto
        {
            Id = "system-probe:other", MolarMassKgPerMol = .032d, LiquidDensityKgPerLiter = 1d,
            GasHeatCapacityJPerMolKelvin = 20d, LiquidHeatCapacityJPerMolKelvin = 40d, LitersPerServing = .1m
        }) });
        return catalog;
    }
    private static FluidInventory WorldFilledFluid(FluidCatalog catalog, decimal gas, decimal liquid = 0m, double kelvin = 300d)
    {
        var inventory = new FluidInventory(catalog: catalog);
        if (!inventory.TryAdd(FluidInventory.CreateBatch(catalog.Find("system-probe:fluid"), gas, liquid, kelvin), 100d, .001d))
            throw new InvalidOperationException("独立流体样本初始化失败。");
        return inventory;
    }

    private static JObject WorldFluidReservations(JObject args)
    {
        FluidCatalog catalog = WorldFluidCatalog();
        FluidInventory source = WorldFilledFluid(catalog, 10m);
        var target = new FluidInventory(catalog: catalog);
        bool reserved = source.TryReserve("probe-port", FluidPhase.Gas, 3m, out FluidBatch reservation);
        decimal available = source.GetAvailableMoles("system-probe:fluid", FluidPhase.Gas);
        bool transferred = source.TryTransfer("probe-port", target, 2m, 100d, .001d);
        decimal remainder = source.GetReservation("probe-port")?.GasMoles ?? 0m;
        source.ReleaseReservation("probe-port");
        return new JObject
        {
            ["reserved"] = reserved, ["reservedMoles"] = (double)reservation.GasMoles,
            ["availableAfterReservation"] = (double)available, ["transferred"] = transferred,
            ["sourceMoles"] = (double)source.GasMoles, ["targetMoles"] = (double)target.GasMoles,
            ["reservedRemainder"] = (double)remainder,
            ["availableAfterRelease"] = (double)source.GetAvailableMoles("system-probe:fluid", FluidPhase.Gas),
            ["portsAfterRelease"] = source.State.Ports.Count
        };
    }

    // 采样正式无限气源、转移和预约，具体数量由 Lua 断言。
    private static JObject WorldInfiniteGasSupply(JObject args)
    {
        FluidCatalog catalog = WorldFluidCatalog();
        var composition = new Dictionary<string, double> { ["system-probe:fluid"] = 2d, ["system-probe:other"] = 1d };
        var source = new FluidInventory(catalog: catalog);
        source.TryAdd(FluidInventory.CreateBatch(catalog.Find("system-probe:fluid"), 0m, 2m, 300d), 100d, .001d);
        source.ConfigureInfiniteGasSupply(composition, 320d, 100d);
        var target = new FluidInventory(catalog: catalog);
        var sample = source.State.Clone();
        double first = IndustrialFluidTransfer.Transfer(source, target, FluidPhase.Gas, 250d, 750d, 100d);
        double second = IndustrialFluidTransfer.Transfer(source, target, FluidPhase.Gas, 250d, 750d, 100d);
        double third = IndustrialFluidTransfer.Transfer(source, target, FluidPhase.Gas, 250d, 750d, 100d);
        double full = IndustrialFluidTransfer.Transfer(source, target, FluidPhase.Gas, 1d, 750d, 100d);
        var filtered = new FluidInventory(catalog: catalog);
        double filteredLiters = IndustrialFluidTransfer.Transfer(source, filtered, FluidPhase.Gas, 90d, 300d, 100d, "system-probe:other");
        decimal amount = FluidUnits.StandardLitersToMol(200m);
        bool reservedA = source.TryReserve("a", FluidPhase.Gas, amount, id => id == "system-probe:fluid", out FluidBatch a);
        bool reservedB = source.TryReserve("b", FluidPhase.Gas, amount, id => id == "system-probe:fluid", out FluidBatch b);
        bool reservedTake = source.TryTakeReserved("a", amount, out FluidBatch taken);
        source.ReleaseReservation("b");
        bool rejected = !source.TryTakeRecipe(new[] { new FluidRequirement("system-probe:fluid", FluidPhase.Gas, amount),
            new FluidRequirement("system-probe:missing", FluidPhase.Gas, 1m) }, out _);
        bool liquidTaken = source.TryTakeExact("system-probe:fluid", FluidPhase.Liquid, 1m, out _);
        var restored = new FluidInventory(sample, catalog);
        bool initiallyFinite = !restored.HasInfiniteGasSupply;
        restored.ConfigureInfiniteGasSupply(composition, 320d, 100d);
        bool pending = restored.TryReserve("restore", FluidPhase.Gas, amount, id => id == "system-probe:fluid", out _);
        restored.Restore(new FluidInventoryState());
        double emptyRestoreSample = (double)FluidUnits.MolToStandardLiters(restored.GasMoles);
        var liquidOnly = new FluidInventory(catalog: catalog);
        liquidOnly.TryAdd(FluidInventory.CreateBatch(catalog.Find("system-probe:other"), 0m, 2m, 280d), 100d, .001d);
        restored.Restore(liquidOnly.State);
        double liquidRestoreSample = (double)FluidUnits.MolToStandardLiters(restored.GasMoles);
        double liquidRestoreShare = (double)(restored.GetGasMoles("system-probe:fluid") / restored.GasMoles);
        bool pendingAfterRestore = pending && restored.TryTakeReserved("restore", amount, out _);
        double restoredDelivery = IndustrialFluidTransfer.Transfer(restored, new FluidInventory(catalog: catalog),
            FluidPhase.Gas, 200d, 500d, 100d);
        return new JObject {
            ["sample"] = (double)FluidUnits.MolToStandardLiters(source.GasMoles),
            ["sourceHydrogenShare"] = (double)(source.GetGasMoles("system-probe:fluid") / source.GasMoles),
            ["first"] = first, ["second"] = second, ["third"] = third, ["full"] = full,
            ["target"] = (double)FluidUnits.MolToStandardLiters(target.GasMoles),
            ["targetFirstGas"] = (double)FluidUnits.MolToStandardLiters(target.GetGasMoles("system-probe:fluid")),
            ["targetTemperature"] = target.GetTemperatureKelvin(), ["filtered"] = filteredLiters,
            ["filterRejectedGas"] = (double)filtered.GetGasMoles("system-probe:fluid"),
            ["reservations"] = reservedA && reservedB && reservedTake && a.GasMoles == amount &&
                b.GasMoles == amount && taken.GasMoles == amount && source.GetReservation("b") == null,
            ["storedReservations"] = source.State.Ports.Count, ["rejected"] = rejected,
            ["liquidTaken"] = liquidTaken, ["liquidLeft"] = (double)source.LiquidMoles,
            ["initiallyFinite"] = initiallyFinite, ["restoredInfinite"] = restored.HasInfiniteGasSupply,
            ["restoredDelivery"] = restoredDelivery, ["emptyRestoreSample"] = emptyRestoreSample,
            ["liquidRestoreSample"] = liquidRestoreSample, ["liquidRestoreShare"] = liquidRestoreShare,
            ["liquidRestoreAmount"] = (double)restored.LiquidMoles, ["pendingAfterRestore"] = pendingAfterRestore
        };
    }

    private static JObject WorldFluidRecipe(JObject args)
    {
        FluidCatalog catalog = WorldFluidCatalog();
        FluidInventory inventory = WorldFilledFluid(catalog, 10m);
        bool unavailable = inventory.TryTakeRecipe(new[] { new FluidRequirement("system-probe:fluid", FluidPhase.Gas, 2m),
            new FluidRequirement("system-probe:other", FluidPhase.Gas, 1m) }, out _);
        decimal afterFailure = inventory.GasMoles;
        bool accepted = inventory.TryTakeRecipe(new[] { new FluidRequirement("system-probe:fluid", FluidPhase.Gas, 2m),
            new FluidRequirement("system-probe:fluid", FluidPhase.Gas, 3m) }, out List<FluidBatch> batches);
        decimal taken = 0m;
        foreach (FluidBatch batch in batches) taken += batch.GasMoles;
        bool oversized = inventory.TryAdd(FluidInventory.CreateBatch(catalog.Find("system-probe:fluid"), 0m, 10000m, 300d), 1d, .001d);
        return new JObject { ["missingIngredientAccepted"] = unavailable, ["afterFailure"] = (double)afterFailure,
            ["accepted"] = accepted, ["takenMoles"] = (double)taken, ["remainingMoles"] = (double)inventory.GasMoles,
            ["oversizedLiquidAccepted"] = oversized, ["liquidAfterRejectedAdd"] = (double)inventory.LiquidMoles };
    }

    private static JObject WorldFluidHeat(JObject args)
    {
        FluidCatalog catalog = WorldFluidCatalog();
        FluidInventory hot = WorldFilledFluid(catalog, 2m, 0m, 400d), cold = WorldFilledFluid(catalog, 2m, 0m, 300d);
        double totalBefore = hot.State.TotalInternalEnergyJoules + cold.State.TotalInternalEnergyJoules;
        double transfer = FluidThermodynamics.ExchangeHeat(hot, cold, 10000d, 1d);
        long massRevision = hot.State.MassRevision;
        double added = FluidThermodynamics.AddHeat(hot, 68d);
        return new JObject
        {
            ["transfer"] = transfer, ["hotKelvin"] = hot.GetTemperatureKelvin(), ["coldKelvin"] = cold.GetTemperatureKelvin(),
            ["energyBefore"] = totalBefore, ["energyAfter"] = hot.State.TotalInternalEnergyJoules + cold.State.TotalInternalEnergyJoules,
            ["added"] = added, ["massRevisionStable"] = hot.State.MassRevision == massRevision,
            ["hotMoles"] = (double)hot.GasMoles, ["coldMoles"] = (double)cold.GasMoles,
            ["selfTransfer"] = FluidThermodynamics.ExchangeHeat(hot, hot, 100d, 1d)
        };
    }

    // 直接采样库存正式内能接口，比例与守恒的业务期望由 Lua 断言。
    private static JObject WorldFluidAmbientHeat(JObject args)
    {
        FluidCatalog catalog = WorldFluidCatalog();
        double current = Number(args, "current", 300d), target = Number(args, "ambient", 400d);
        double seconds = Number(args, "seconds", 1d);
        decimal gas = (decimal)Number(args, "gas", 2d), liquid = (decimal)Number(args, "liquid", 0d);
        FluidInventory whole = WorldFilledFluid(catalog, gas, liquid, current);
        FluidInventory split = WorldFilledFluid(catalog, gas, liquid, current);
        double beforeEnergy = whole.State.TotalInternalEnergyJoules;
        long beforeMass = whole.State.MassRevision;
        double zeroHeat = FluidThermodynamics.ExchangeHeatWithEnvironment(whole, target, 0d);
        double zeroTemperature = whole.GetTemperatureKelvin();
        double negativeHeat = FluidThermodynamics.ExchangeHeatWithEnvironment(whole, target, -1d);
        double invalidHeat = FluidThermodynamics.ExchangeHeatWithEnvironment(whole, double.NaN, seconds);
        double invalidTimeHeat = FluidThermodynamics.ExchangeHeatWithEnvironment(whole, target, double.PositiveInfinity);
        double heat = FluidThermodynamics.ExchangeHeatWithEnvironment(whole, target, seconds);
        for (int i = 0; i < 10; i++)
            FluidThermodynamics.ExchangeHeatWithEnvironment(split, target, seconds / 10d);
        whole.GetEnergyTerms(out double heatCapacity, out _);
        var empty = new FluidInventory(catalog: catalog);
        return new JObject
        {
            ["current"] = current, ["ambient"] = target,
            ["whole"] = whole.GetTemperatureKelvin(), ["split"] = split.GetTemperatureKelvin(),
            ["heat"] = heat, ["energyBefore"] = beforeEnergy,
            ["energyAfter"] = whole.State.TotalInternalEnergyJoules,
            ["heatCapacity"] = heatCapacity,
            ["gasMoles"] = (double)whole.GasMoles, ["liquidMoles"] = (double)whole.LiquidMoles,
            ["massRevisionStable"] = whole.State.MassRevision == beforeMass,
            ["zeroHeat"] = zeroHeat, ["zeroTemperature"] = zeroTemperature,
            ["negativeHeat"] = negativeHeat, ["invalidHeat"] = invalidHeat,
            ["invalidTimeHeat"] = invalidTimeHeat,
            ["emptyHeat"] = FluidThermodynamics.ExchangeHeatWithEnvironment(empty, target, seconds),
            ["emptyEnergy"] = empty.State.TotalInternalEnergyJoules
        };
    }

    private static JObject WorldFluidPhase(JObject args)
    {
        FluidCatalog catalog = WorldFluidCatalog(true);
        FluidInventory inventory = WorldFilledFluid(catalog, 0m, 10m);
        double energy = inventory.State.TotalInternalEnergyJoules;
        FluidPhaseChangeResult phase = FluidThermodynamics.AdvancePhaseChange(inventory, 100d, .001d, .1d, 10d);
        return new JObject { ["converted"] = (double)phase.ConvertedMoles,
            ["gasMoles"] = (double)inventory.GasMoles, ["liquidMoles"] = (double)inventory.LiquidMoles,
            ["energyBefore"] = energy, ["energyAfter"] = inventory.State.TotalInternalEnergyJoules,
            ["kelvin"] = inventory.GetTemperatureKelvin() };
    }
    #endregion

    #region 加工进度与来源变更
    private static JObject WorldProcessingMaterialHeat(JObject args)
    {
        float current = (float)Number(args, "current", 100d);
        float furnace = (float)Number(args, "furnace", 1000d);
        float seconds = (float)Number(args, "seconds", 1d);
        float heatingRateMultiplier = (float)Number(args, "heatingRateMultiplier", 1d);
        float? fixedTransfer = args["fixedTransfer"] == null ? null : (float)Number(args, "fixedTransfer", 10d);
        ItemData Material() => new Data_GeneralItem
        {
            IDName = "system-probe:heated-material",
            Stack = new ItemStack { Amount = 4f },
            MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = current }
        };
        ItemData material = Material();
        // 直接采样熔炉复用的正式物质传热入口，业务数值由 Lua 断言。
        bool changed = ItemMatterRuntime.Advance(material, furnace, 1f, seconds,
            transferPerSecondOverride: fixedTransfer, heatingRateMultiplier: heatingRateMultiplier);
        float next = material.MatterState.TemperatureCelsius;
        ItemMatterRuntime.Advance(material, furnace, 1f, seconds,
            transferPerSecondOverride: fixedTransfer, heatingRateMultiplier: heatingRateMultiplier);
        ItemData split = Material();
        for (int i = 0; i < 10; i++)
            ItemMatterRuntime.Advance(split, furnace, 1f, seconds / 10f,
                transferPerSecondOverride: fixedTransfer, heatingRateMultiplier: heatingRateMultiplier);
        return new JObject
        {
            ["changed"] = changed, ["next"] = next,
            ["second"] = material.MatterState.TemperatureCelsius,
            ["split"] = split.MatterState.TemperatureCelsius, ["amount"] = material.Stack.Amount
        };
    }

    private static JObject WorldProcessingFlintStrike(JObject args)
    {
        float chance = (float)Number(args, "chance", Mod_FlintStrike.DefaultSuccessChancePerClick);
        float roll = (float)Number(args, "roll", .5d);
        int hits = 0;
        // 固定采样正式概率入口，避免随机测试偶然通过或失败。
        for (int i = 0; i < 100; i++)
            if (Mod_FlintStrike.IsSuccessfulRoll((i + .5f) / 100f, chance)) hits++;
        return new JObject
        {
            ["chance"] = chance, ["accepted"] = Mod_FlintStrike.IsSuccessfulRoll(roll, chance),
            ["hits"] = hits
        };
    }

    private static JObject WorldProcessingSelection(JObject args)
    {
        var state = new RecipeProcessingState();
        var input = new Inventory { Data = state.Input };
        var output = new Inventory { Data = state.Output };
        using var processor = new RecipeProcessor(input, output, new CraftingCapabilities { StationId = "probe-station" }, state);
        var first = new RuntimeRecipe { Id = "system-probe:first" };
        processor.SelectRecipe(first, 10f);
        state.Progress = 4f;
        processor.SelectRecipe(first, 10f);
        float sameProgress = state.Progress, sameRatio = processor.Progress01;
        processor.SelectRecipe(new RuntimeRecipe { Id = "system-probe:second" }, 5f);
        float changedProgress = state.Progress;
        string invalidWork = WorldException(() => processor.SelectRecipe(first, 0f));
        processor.SelectRecipe(null, 0f);
        return new JObject { ["sameProgress"] = sameProgress, ["sameRatio"] = sameRatio,
            ["changedProgress"] = changedProgress, ["invalidWork"] = invalidWork,
            ["clearedId"] = state.RecipeId, ["clearedRatio"] = processor.Progress01,
            ["zeroAdvance"] = processor.Advance(0f), ["negativeAdvance"] = processor.Advance(-1f) };
    }

    private static JObject WorldProcessingIdentity(JObject args)
    {
        var state = new SlotProcessingState();
        state.EnsureSlots(2);
        var item = new Data_GeneralItem { IDName = "system-probe:input", Guid = 42,
            Stack = new ItemStack { Amount = 2f } };
        state.Observe(0, item);
        state.Elapsed[0] = 5f; state.PendingAmounts[0] = 3; state.PendingItems[0] = "system-probe:output";
        state.Observe(0, item);
        float unchangedElapsed = state.Elapsed[0];
        int unchangedPending = state.PendingAmounts[0];
        item.Stack.Amount = 1f;
        state.Observe(0, item);
        var changed = new JObject { ["elapsed"] = state.Elapsed[0], ["pending"] = state.PendingAmounts[0],
            ["pendingItem"] = state.PendingItems[0] };
        state.Observe(0, null);
        return new JObject { ["unchangedElapsed"] = unchangedElapsed, ["unchangedPending"] = unchangedPending,
            ["changed"] = changed, ["emptySignature"] = state.Signatures[0], ["slots"] = state.Elapsed.Count,
            ["secondElapsed"] = state.Elapsed[1], ["secondPending"] = state.PendingAmounts[1] };
    }

    private static JObject WorldProcessingAtomicOutput(JObject args)
    {
        var input = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
            { new(0) { itemData = EntityProbeData("test:processing-input", 4f) } }, "独立加工输入") };
        var output = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
            { new(0), new(1) { itemData = EntityProbeData("test:blocking-output", 1f) } }, "独立加工输出") };
        var recipe = new RuntimeRecipe { Id = "system-probe:atomic-processing" };
        var match = new CraftingRecipeMatch(recipe, false, new[] { new CraftingConsumption(0, 2f) });
        var products = new List<ItemData>();
        for (int i = 0; i < 2; i++)
        {
            var product = EntityProbeData("test:processing-output-" + i, 1f);
            product.Stack.Stackable = false;
            products.Add(product);
        }
        var state = new RecipeProcessingState();
        using var processor = new RecipeProcessor(input, output,
            new CraftingCapabilities { StationId = "system-probe:processor" }, state);
        // 正式扩展回调接入同一库存事务，避免为隔离样本注册或替换全局物品目录。
        processor.PreviewOverride = () => CraftingTransaction.CanCreate(input, output, match, products, false, out CraftingResult failure)
            ? CraftingResult.Succeeded(recipe, products) : failure;
        int commits = 0;
        processor.CommitOverride = _ =>
        {
            if (!CraftingTransaction.TryCreate(input, output, match, products, false,
                    out CraftingTransaction transaction, out CraftingResult failure)) return failure;
            if (!transaction.Commit(out failure)) return failure;
            transaction.Complete(); commits++;
            return CraftingResult.Succeeded(recipe, products);
        };
        processor.SelectRecipe(recipe, 5f);
        state.Progress = 5f;
        bool blockedAdvance = processor.Advance(1f);
        var blocked = new JObject { ["advanced"] = blockedAdvance, ["progress"] = state.Progress,
            ["input"] = EntityInventoryAmount(input.Data), ["output"] = EntityInventoryAmount(output.Data), ["commits"] = commits };
        output.Data.itemSlots[1].itemData = null;
        bool resumedAdvance = processor.Advance(1f);
        var resumed = new JObject { ["advanced"] = resumedAdvance, ["progress"] = state.Progress,
            ["input"] = EntityInventoryAmount(input.Data), ["output"] = EntityInventoryAmount(output.Data), ["commits"] = commits };
        bool fullAdvance = processor.Advance(100f);
        return new JObject { ["blocked"] = blocked, ["resumed"] = resumed,
            ["fullAdvanced"] = fullAdvance, ["finalProgress"] = state.Progress,
            ["finalInput"] = EntityInventoryAmount(input.Data), ["finalOutput"] = EntityInventoryAmount(output.Data), ["finalCommits"] = commits };
    }
    #endregion

    #region 独立船舱拓扑与燃料
    private static JObject WorldCabinTopology(JObject args)
    {
        var ship = new ShipState { ShipId = "system-probe:cabin", CellSizeMeters = 1d };
        ShipPieceState door = null;
        for (int y = 0; y < 5; y++)
        for (int x = 0; x < 5; x++)
        {
            ship.Pieces.Add(new ShipPieceState { PieceId = "floor-" + x + "-" + y, Kind = ShipPieceKind.Floor,
                CellX = x, CellY = y });
            if (x != 0 && y != 0 && x != 4 && y != 4) continue;
            var wall = new ShipPieceState { PieceId = "wall-" + x + "-" + y,
                Kind = x == 2 && y == 0 ? ShipPieceKind.Door : ShipPieceKind.Wall,
                CellX = x, CellY = y, SealsAtmosphere = true, BlocksMovement = true };
            ship.Pieces.Add(wall);
            if (wall.Kind == ShipPieceKind.Door) door = wall;
        }
        ShipAtmosphereService.RebuildCompartments(ship);
        int enclosedRooms = ship.Compartments.Count;
        int enclosedCells = enclosedRooms > 0 ? ship.Compartments[0].Cells.Count : 0;
        bool closedBlocks = door.BlocksPassage;
        door.DoorOpen = true;
        ShipAtmosphereService.RebuildCompartments(ship);
        int openRooms = ship.Compartments.Count;
        bool openBlocks = door.BlocksPassage;
        bool broken = ShipStructureService.ApplyDamage(ship, door.PieceId, door.Health, "独立舱室拓扑样本");
        ShipAtmosphereService.RebuildCompartments(ship);
        int breachedRooms = ship.Compartments.Count;
        ship.Pieces.Add(new ShipPieceState { PieceId = "repaired-door", Kind = ShipPieceKind.Door,
            CellX = 2, CellY = 0, SealsAtmosphere = true, BlocksMovement = true });
        ship.NotifyStructureChanged();
        ShipAtmosphereService.RebuildCompartments(ship);
        return new JObject { ["enclosedRooms"] = enclosedRooms, ["enclosedCells"] = enclosedCells,
            ["closedBlocks"] = closedBlocks, ["openBlocks"] = openBlocks, ["openRooms"] = openRooms,
            ["broken"] = broken, ["breachedRooms"] = breachedRooms, ["repairedRooms"] = ship.Compartments.Count };
    }

    private static JObject WorldShipFuel(JObject args)
    {
        var catalog = new FluidCatalog();
        var definitions = new List<FluidDefinition>();
        foreach (string id in new[] { FluidIds.Hydrogen, FluidIds.Oxygen, FluidIds.Nitrogen, ShipFuel.LiquidFuelId })
            definitions.Add(new FluidDefinition(new FluidDefinitionDto { Id = id, MolarMassKgPerMol = .02d,
                LiquidDensityKgPerLiter = 1d, GasHeatCapacityJPerMolKelvin = 20d,
                LiquidHeatCapacityJPerMolKelvin = 40d, LitersPerServing = .1m }));
        catalog.Register(definitions);
        JObject result = GameplayTestShipProbe.CaptureFuel(catalog);
        result["scope"] = "production-domain"; result["operation"] = "ship_fuel";
        return result;
    }
    #endregion
}
