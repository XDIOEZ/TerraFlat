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
        operations.Add("terrain/environment_pointer", WorldEnvironmentPointer);
        operations.Add("ecology/generation", WorldEcology);
        operations.Add("ecology/neighborhood_cache", WorldEcologyNeighborhoodCache);
        operations.Add("terrain/cached_generation", WorldCachedTerrainGeneration);
        operations.Add("time/calendar", WorldCalendar);
        operations.Add("environment/thermal", WorldThermal);
        operations.Add("environment/heat_sources", WorldHeatSources);
        operations.Add("environment/wind", WorldWind);
        operations.Add("environment/weather", WorldWeather);
        operations.Add("environment/natural_fog_profile", WorldNaturalFogProfile);
        operations.Add("environment/natural_fog_distribution", WorldNaturalFogDistribution);
        operations.Add("environment/natural_fog_lifecycle", WorldNaturalFogLifecycle);
        operations.Add("environment/natural_fog_validation", WorldNaturalFogValidation);
        operations.Add("surface_fluids/liquid_layer", WorldLiquidLayer);
        operations.Add("dimension/address", WorldDimensionAddress);
        RegisterCavePerformanceOperations(operations);
        RegisterColdDropProbes(operations);
        operations.Add("building/support_layer", WorldSupportLayer);
        operations.Add("building/shared_state", WorldBuildingState);
        operations.Add("agriculture/soil", WorldSoil);
        operations.Add("fishing/rig", WorldFishingRig);
        operations.Add("mechanical/load_allocation", WorldMechanicalLoads);
        operations.Add("mechanical/gear_conflict", WorldMechanicalGears);
        operations.Add("electrical/battery_commit", WorldElectricalBattery);
        operations.Add("electrical/supply_limits", WorldElectricalLimits);
        operations.Add("electrical/unlimited_supply", WorldElectricalUnlimitedSupply);
        operations.Add("industrial_fluids/reservations", WorldFluidReservations);
        operations.Add("industrial_fluids/atomic_recipe", WorldFluidRecipe);
        operations.Add("industrial_fluids/heat", WorldFluidHeat);
        operations.Add("industrial_fluids/phase_change", WorldFluidPhase);
        operations.Add("processing/recipe_selection", WorldProcessingSelection);
        operations.Add("processing/slot_identity", WorldProcessingIdentity);
        operations.Add("processing/atomic_output", WorldProcessingAtomicOutput);
        operations.Add("processing/material_heat", WorldProcessingMaterialHeat);
        foreach (string operation in new[] { "ship_structure", "ship_docking", "ship_mass",
                     "ship_propulsion", "ship_contacts", "ship_snapshots", "ship_engine_placement", "ship_optical",
                     "ship_collider_batch" })
            operations.Add("space/" + operation, _ => GameplayTestShipProbe.Capture(operation));
        operations.Add("space/cabin_topology", WorldCabinTopology);
        operations.Add("space/ship_fuel", WorldShipFuel);
        operations.Add("space/coordinates", _ => GameplayTestUniverseProbe.Capture("coordinates"));
        operations.Add("space/streaming", _ => GameplayTestUniverseProbe.Capture("streaming"));
        operations.Add("space/ship_surface_state", _ => GameplayTestShipSurfaceProbe.Capture());
        operations.Add("space/presentation_interpolation", _ => GameplayTestSpacePresentationProbe.Capture());
        foreach (string operation in new[] { "ship_local_damage", "ship_hit_targeting",
                     "ship_damage_lines", "ship_damage_fragmentation" })
            operations.Add("space/" + operation, _ => GameplayTestShipDamageProbe.Capture(operation));
    }

    private static string WorldException(Action action)
    {
        try { action(); return string.Empty; }
        catch (Exception exception) { return exception.GetType().Name; }
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
        for (int i = 0; i < 10; i++) split = ThermalRuntime.AdvanceAmbient(split, ambient, seconds / 10f);
        return new JObject
        {
            ["current"] = current, ["ambient"] = ambient,
            ["whole"] = ThermalRuntime.AdvanceAmbient(current, ambient, seconds), ["split"] = split,
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

    private static JObject WorldNaturalFogDistribution(JObject args)
    {
        var settings = ChunkWeatherConfiguration.Load();
        settings.CreateProfile(new ChunkWeatherClimate(1f, 0f, 0f, 0f, 40f))
            .TryGetElement(WeatherElementIds.Fog, out WeatherElementProfile fog);
        int[] bins = new int[5];
        // 穷举等距均匀输入网格，只采正式抽样结果，分布断言仍由 Lua 持有。
        for (int a = 0; a < 20; a++)
        for (int b = 0; b < 20; b++)
        for (int c = 0; c < 20; c++)
        {
            WeatherElementProfile sampled = settings.SampleNaturalEvent(fog, (a + 0.5f) / 20f,
                (b + 0.5f) / 20f, (c + 0.5f) / 20f);
            int bin = Math.Min(4, (int)((sampled.Intensity - 0.1f) / 0.9f * 5f));
            bins[bin]++;
        }
        WeatherElementProfile minimum = settings.SampleNaturalEvent(fog, 0f, 0f, 0f);
        WeatherElementProfile maximum = settings.SampleNaturalEvent(fog, 1f, 1f, 1f);
        WeatherElementProfile middle = settings.SampleNaturalEvent(fog, 0.2f, 0.8f, 0.5f);
        settings.CreateProfile(new ChunkWeatherClimate(0.25f, 0f, 0f, 0f, 10f))
            .TryGetElement(WeatherElementIds.Fog, out WeatherElementProfile cool);
        cool = settings.SampleNaturalEvent(cool, 0.2f, 0.8f, 0.5f);
        return new JObject
        {
            ["bins"] = new JArray(bins), ["sampleCount"] = 8000,
            ["minimumIntensity"] = minimum.Intensity, ["maximumIntensity"] = maximum.Intensity,
            ["minimumDuration"] = minimum.DurationSeconds, ["maximumDuration"] = maximum.DurationSeconds,
            ["middleIntensity"] = middle.Intensity, ["middleDuration"] = middle.DurationSeconds,
            ["coolIntensity"] = cool.Intensity, ["coolDuration"] = cool.DurationSeconds
        };
    }

    private static JObject WorldNaturalFogLifecycle(JObject args)
    {
        var settings = ChunkWeatherConfiguration.Load();
        WeatherElementRule fog = Array.Find(settings.Elements, rule => rule.Id == WeatherElementIds.Fog).Clone();
        settings.Elements = new[] { fog };
        settings.NaturalEventProbabilityMultiplier = 1f;
        settings.CheckIntervalSeconds = settings.StepSeconds = 2f;
        fog.MinimumProbability = fog.MaximumProbability = 1f;
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
        return new JObject
        {
            ["firstReady"] = firstReady, ["secondReady"] = secondReady,
            ["firstRain"] = first.RainIntensity, ["secondRain"] = second.RainIntensity,
            ["firstSnow"] = first.SnowIntensity,
            ["firstRegionX"] = first.Region.X, ["secondRegionX"] = second.Region.X,
            ["frozenReady"] = frozenReady, ["frozenTemperature"] = frozen?.Climate.TemperatureCelsius,
            ["endedPrecipitation"] = ended.Snapshot.PrecipitationIntensity,
            ["negativeRegionX"] = negative.X, ["negativeRegionY"] = negative.Y,
            ["rainProbability"] = settings.ResolveActivationProbability(new WeatherElementProfile("rain", .4f, .5f, 60f)),
            ["wind8Probability"] = settings.ResolveActivationProbability(new WeatherElementProfile("wind", .4f, .8f, 60f)),
            ["wind9Probability"] = settings.ResolveActivationProbability(new WeatherElementProfile("wind", .4f, .9f, 60f)),
            ["wind10Probability"] = settings.ResolveActivationProbability(new WeatherElementProfile("wind", .4f, 1f, 60f))
        };
    }
    #endregion

    #region 地表液体与建筑支撑
    private static JObject WorldLiquidLayer(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(1, 1);
        buffer.SetCell(0, 0, new TerrainCell(17, 0, 0, 2, 5, TerrainCellFlags.Walkable));
        using ChunkTerrainData terrain = buffer.Seal();
        int water = terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        bool added = terrain.SetLiquid(0, 0, water, 6);
        long wetRevision = terrain.LiquidRevision;
        bool repeated = terrain.SetLiquid(0, 0, water, 6);
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
            ["excessLevel"] = LiquidCellValue.NormalizeLevel(20),
            ["minimumGeneratedLevel"] = LiquidCellValue.FromGeneratedDepth(.000001),
            ["roundedGeneratedLevel"] = LiquidCellValue.FromGeneratedDepth(.21),
            ["exactGeneratedLevels"] = new JArray(LiquidCellValue.FromGeneratedDepth(.1f), LiquidCellValue.FromGeneratedDepth(.6f)),
            ["invalidGeneratedDepth"] = WorldException(() => LiquidCellValue.FromGeneratedDepth(double.NaN)),
            ["flow"] = WorldIntegerLiquidFlow()
        };
    }

    private static JObject WorldIntegerLiquidFlow()
    {
        // 只返回生产求解器的逐步原始采样，守恒与停稳判断交给 Lua。
        var settings = new LiquidFlowSettings();
        var solver = new LiquidFlowSolver();
        int[] neighbours = { 1, 2, 3, 4, -1, 0, -1, -1, 0, -1, -1, -1, -1, -1, -1, 0, -1, -1, 0, -1 };
        var cells = new List<LiquidFlowCell>();
        for (int i = 0; i < 5; i++) cells.Add(new LiquidFlowCell(0f, i == 0 ? 8 : 0,
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
        return new JObject { ["samples"] = samples, ["blocked"] = Pair(10, 0, true, 1),
            ["foreign"] = Pair(10, 2, false, 2), ["full"] = Pair(10, 10, false, 1, 1f),
            ["single"] = Pair(1, 0, false, 1), ["edge"] = Pair(6, 0, false, 1) };
    }

    private static JObject WorldSupportLayer(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(1, 1);
        buffer.SetCell(0, 0, new TerrainCell(17, 0, 0, 2, 25, TerrainCellFlags.None));
        buffer.SetLiquid(0, 0, buffer.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 6);
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
            ["planet"] = address.PlanetId, ["dimension"] = address.DimensionId, ["key"] = address.WorldKey,
            ["roundTripEquals"] = roundTrip == address, ["surfaceKey"] = surface.WorldKey,
            ["surface"] = surface.IsSurface, ["valid"] = address.IsValid,
            ["fallbackKey"] = fallback.WorldKey, ["fallbackDimension"] = fallback.DimensionId,
            ["sanitizedPlanet"] = separator.PlanetId,
            ["differentDimensionsEqual"] = address == surface
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
            ["halfWaterMultiplier"] = Mod_Grow.CalculateFarmlandGrowthMultiplier(
                new TileData_Farmland { waterValue = 5, fertilityValue = new GameValue_float(1f) }, 0f, 0f),
            ["negativeAddWater"] = WorldException(() => soil.AddWater(-1)),
            ["negativeConsumeWater"] = WorldException(() => soil.ConsumeWater(-1)),
            ["negativeAddFertility"] = WorldException(() => soil.AddFertility(-1f)),
            ["negativeConsumeFertility"] = WorldException(() => soil.ConsumeFertility(-1f))
        };
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
        ItemData Material() => new Data_GeneralItem
        {
            IDName = "system-probe:heated-material",
            Stack = new ItemStack { Amount = 4f },
            MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = current }
        };
        ItemData material = Material();
        // 直接采样熔炉复用的正式物质传热入口，业务数值由 Lua 断言。
        bool changed = ItemMatterRuntime.Advance(material, furnace, 1f, seconds);
        float next = material.MatterState.TemperatureCelsius;
        ItemMatterRuntime.Advance(material, furnace, 1f, seconds);
        ItemData split = Material();
        for (int i = 0; i < 10; i++)
            ItemMatterRuntime.Advance(split, furnace, 1f, seconds / 10f);
        return new JObject
        {
            ["changed"] = changed, ["next"] = next,
            ["second"] = material.MatterState.TemperatureCelsius,
            ["split"] = split.MatterState.TemperatureCelsius, ["amount"] = material.Stack.Amount
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
        foreach (string id in new[] { FluidIds.Hydrogen, FluidIds.Oxygen, FluidIds.Nitrogen })
            definitions.Add(new FluidDefinition(new FluidDefinitionDto { Id = id, MolarMassKgPerMol = .02d,
                LiquidDensityKgPerLiter = 1d, GasHeatCapacityJPerMolKelvin = 20d,
                LiquidHeatCapacityJPerMolKelvin = 40d, LitersPerServing = .1m }));
        catalog.Register(definitions);
        FluidBatch Gas(string id, decimal moles) => FluidInventory.CreateBatch(catalog.Find(id), moles, 0m, 293.15d);
        var fuel = new FluidInventory(catalog: catalog);
        bool hydrogenAdded = fuel.TryAdd(Gas(FluidIds.Hydrogen, 20m), 10000d, .001d);
        bool oxygenAdded = fuel.TryAdd(Gas(FluidIds.Oxygen, 10m), 10000d, .001d);
        var engine = new ShipPartConfiguration();
        bool eligible = ShipFuel.IsEligible(fuel, engine);
        decimal available = ShipFuel.AvailableReactionMoles(fuel, engine);
        bool burned = ShipFuel.TryBurn(fuel, 3m, out double energy);
        decimal hydrogenAfter = fuel.GetGasMoles(FluidIds.Hydrogen), oxygenAfter = fuel.GetGasMoles(FluidIds.Oxygen);
        bool overdraw = ShipFuel.TryBurn(fuel, 100m, out double overdrawEnergy);
        decimal hydrogenAfterFailure = fuel.GetGasMoles(FluidIds.Hydrogen), oxygenAfterFailure = fuel.GetGasMoles(FluidIds.Oxygen);
        bool impurityAdded = fuel.TryAdd(Gas(FluidIds.Nitrogen, 10m), 10000d, .001d);
        bool impureEligible = ShipFuel.IsEligible(fuel, engine);
        var single = new FluidInventory(catalog: catalog);
        bool singlePrepared = single.TryAdd(Gas(FluidIds.Hydrogen, 20m), 10000d, .001d);
        return new JObject { ["scope"] = "production-domain", ["operation"] = "ship_fuel",
            ["hydrogenAdded"] = hydrogenAdded, ["oxygenAdded"] = oxygenAdded, ["eligible"] = eligible,
            ["availableReactionMoles"] = (double)available, ["burnAccepted"] = burned,
            ["burnRequestedOxygen"] = 3d, ["energyJoules"] = energy, ["hydrogenAfter"] = (double)hydrogenAfter,
            ["oxygenAfter"] = (double)oxygenAfter, ["overdrawAccepted"] = overdraw, ["overdrawEnergy"] = overdrawEnergy,
            ["hydrogenAfterFailure"] = (double)hydrogenAfterFailure, ["oxygenAfterFailure"] = (double)oxygenAfterFailure,
            ["impurityAdded"] = impurityAdded, ["impureEligible"] = impureEligible,
            ["singleGasPrepared"] = singlePrepared, ["singleGasEligible"] = ShipFuel.IsEligible(single, engine) };
    }
    #endregion
}
