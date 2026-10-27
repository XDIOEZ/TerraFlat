using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RuntimeAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>宇宙测试采样只调用生产规则，Lua 负责判断结果。</summary>
public static class GameplayTestUniverseProbe
{
    #region 隔离采样入口
    private const int Seed = 17413;

    public static bool Supports(string operation) => operation is
        "catalog" or "orbits" or "coordinates" or "streaming" or "snapshots" or "surface";

    public static JObject Capture(string operation)
    {
        JObject result = operation switch
        {
            "catalog" => CaptureCatalog(),
            "orbits" => CaptureOrbits(),
            "coordinates" => CaptureCoordinates(),
            "streaming" => CaptureStreaming(),
            "snapshots" => CaptureSnapshots(),
            "surface" => CaptureSurfaces(),
            _ => throw new ArgumentException("未开放的宇宙采样操作：" + operation, nameof(operation))
        };
        result["operation"] = operation;
        result["seed"] = Seed;
        return result;
    }

    private static JObject Sector(SpaceSectorId value) => new()
    {
        ["x"] = value.X.ToString(CultureInfo.InvariantCulture),
        ["y"] = value.Y.ToString(CultureInfo.InvariantCulture)
    };
    private static JObject Vector(SpaceVector2 value) => new() { ["x"] = value.X, ["y"] = value.Y };
    private static JObject Position(SpaceSectorPosition value) => new()
        { ["sector"] = Sector(value.Sector), ["local"] = Vector(value.LocalMeters) };
    private static T Clone<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
    private static JToken Json(object value) => value == null ? JValue.CreateNull() : JToken.FromObject(value);
    private static JObject Guard(string name, Action action)
    {
        string exception = null;
        try { action(); }
        catch (Exception error) { exception = error.GetType().Name; }
        return new JObject { ["name"] = name, ["exception"] = exception };
    }
    private static JObject Body(BodyState body) => new()
    {
        ["id"] = body.BodyId, ["templateId"] = body.TemplateBodyId,
        ["sector"] = Sector(body.Sector), ["planetId"] = body.PlanetId,
        ["displayName"] = body.DisplayName, ["prefabId"] = body.PrefabId,
        ["parentId"] = body.ParentBodyId, ["surfaceProfileId"] = body.SurfaceProfileId,
        ["seed"] = body.Seed, ["radius"] = body.RadiusMeters, ["gravity"] = body.SurfaceGravity,
        ["axis"] = body.PlacementDistanceMeters, ["phase"] = body.PlacementAngleRadians,
        ["periodSeconds"] = body.OrbitalPeriodSeconds, ["eccentricity"] = body.OrbitalEccentricity,
        ["periapsisRadians"] = body.OrbitalPeriapsisRadians,
        ["rotationPeriodSeconds"] = body.RotationPeriodSeconds, ["rotationInitialRadians"] = body.RotationInitialRadians,
        ["position"] = Vector(body.PositionMeters), ["velocity"] = Vector(body.VelocityMetersPerSecond)
    };
    private static JArray Bodies(UniverseState state)
    {
        var result = new JArray();
        foreach (BodyState body in state.Bodies) result.Add(Body(body));
        return result;
    }
    private static JObject Universe(UniverseState state) => new()
    {
        ["sector"] = Sector(state.Sector), ["templateId"] = state.TemplateId, ["seed"] = state.Seed,
        ["simulationSeconds"] = state.SimulationSeconds, ["fixedStepSeconds"] = state.FixedStepSeconds,
        ["metersPerUnityUnit"] = state.MetersPerUnityUnit, ["orbitTimeScale"] = state.OrbitTimeScale,
        ["bodies"] = Bodies(state)
    };
    #endregion

    #region 目录与独立星球环境
    private static PlanetData CreatePlanet(SpaceCatalog catalog, BodyState body)
    {
        PlanetData planet = catalog.CreatePlanetData(body.BodyId);
        catalog.ApplyPlanetMetadata(body, planet);
        return planet;
    }

    private static JObject Surface(SurfaceProfile profile) => new()
    {
        ["id"] = profile.Id, ["temperatureCelsius"] = profile.TemperatureCelsius,
        ["surfaceRadiusTiles"] = profile.SurfaceRadiusTiles,
        ["pressureKPa"] = profile.PressureKPa, ["composition"] = Json(profile.AtmosphereComposition),
        ["numbers"] = Json(profile.NumericParameters), ["texts"] = Json(profile.TextParameters),
        ["tiles"] = Json(profile.TileParameters),
        ["ecologyRules"] = Json(profile.EcologyRuleIds), ["caveResourceRules"] = Json(profile.CaveResourceRuleIds),
        ["ecologyMultiplier"] = profile.EcologyMultiplier
    };

    private static JObject Planet(PlanetData planet)
    {
        var gases = new JArray();
        foreach (AtmosphereGasSnapshot gas in AtmosphereService.GetComposition(planet.Atmosphere))
            gases.Add(new JObject { ["id"] = gas.FluidId, ["fraction"] = gas.Fraction, ["moles"] = gas.Moles });
        return new JObject
        {
            ["id"] = planet.BodyId, ["name"] = planet.Name, ["displayName"] = planet.name,
            ["prefabId"] = planet.PrefabName, ["surfaceProfileId"] = planet.GenerationPresetId,
            ["radius"] = planet.PhysicalRadiusMeters, ["gravity"] = planet.SurfaceGravityMetersPerSecondSquared,
            ["mapRadius"] = planet.Radius, ["topology"] = planet.TopologyMode.ToString(),
            ["rotationDegreesPerSecond"] = planet.SelfRotateSpeed, ["globalTemperature"] = planet.GlobalTemperature,
            ["atmosphereProfileId"] = planet.AtmosphereProfileId,
            ["temperatureKelvin"] = planet.Atmosphere.TemperatureKelvin,
            ["pressureKPa"] = AtmosphereService.PressureKPa(planet.Atmosphere),
            ["gases"] = gases, ["mapCount"] = planet.MapData_Dict.Count
        };
    }

    private static JObject CaptureCatalog()
    {
        SpaceCatalog catalog = SpaceCatalog.LoadDefault();
        string templateBefore = JsonConvert.SerializeObject(catalog.Bodies);
        var sectors = new JArray();
        foreach (SpaceSectorId sector in catalog.ConfiguredSectors)
        {
            UniverseState state = catalog.GenerateSector(Seed, sector);
            var simulation = new UniverseSimulation(state);
            JObject entry = Universe(state);
            entry["repeatBodies"] = Bodies(catalog.GenerateSector(Seed, sector));
            entry["alternateBodies"] = Bodies(catalog.GenerateSector(Seed + 1, sector));
            foreach (JObject row in (JArray)entry["bodies"])
            {
                BodyState body = simulation.GetBody((string)row["id"]);
                row["surface"] = Surface(catalog.GetSurfaceProfile(body.BodyId));
                row["planet"] = Planet(CreatePlanet(catalog, body));
                row["resolvedPlanetId"] = catalog.ResolveBodyId(body.PlanetId);
                row["definitionId"] = catalog.GetBodyDefinition(body.BodyId).BodyId;
            }
            sectors.Add(entry);
        }
        SpaceSectorId emptySector = new(123456789, -987654321);
        while (ContainsSector(catalog.ConfiguredSectors, emptySector)) emptySector.X++;
        return new JObject
        {
            ["sectors"] = sectors, ["empty"] = Universe(catalog.GenerateSector(Seed, emptySector)),
            ["templateBefore"] = templateBefore, ["templateAfter"] = JsonConvert.SerializeObject(catalog.Bodies)
        };
    }

    private static bool ContainsSector(IReadOnlyList<SpaceSectorId> sectors, SpaceSectorId target)
    {
        foreach (SpaceSectorId sector in sectors) if (sector == target) return true;
        return false;
    }
    #endregion

    #region 全目录公转自转采样
    private static JObject MotionAt(UniverseState source, string bodyId, double time)
    {
        UniverseState state = Clone(source);
        state.SimulationSeconds = time;
        var simulation = new UniverseSimulation(state);
        BodyState body = simulation.GetBody(bodyId);
        SpaceVector2 parentPosition = default, parentVelocity = default;
        if (!string.IsNullOrWhiteSpace(body.ParentBodyId))
        {
            BodyState parent = simulation.GetBody(body.ParentBodyId);
            parentPosition = parent.PositionMeters;
            parentVelocity = parent.VelocityMetersPerSecond;
        }
        return new JObject
        {
            ["time"] = time, ["position"] = Vector(body.PositionMeters), ["velocity"] = Vector(body.VelocityMetersPerSecond),
            ["parentPosition"] = Vector(parentPosition), ["parentVelocity"] = Vector(parentVelocity),
            ["relativePosition"] = Vector(body.PositionMeters - parentPosition),
            ["relativeVelocity"] = Vector(body.VelocityMetersPerSecond - parentVelocity),
            ["rotation"] = simulation.GetRotationRadians(bodyId)
        };
    }

    private static JObject CaptureOrbits()
    {
        SpaceCatalog catalog = SpaceCatalog.LoadDefault();
        var bodies = new JArray();
        foreach (SpaceSectorId sector in catalog.ConfiguredSectors)
        {
            UniverseState state = catalog.GenerateSector(Seed, sector);
            foreach (BodyState body in state.Bodies)
            {
                JObject row = Body(body);
                double period = body.OrbitalPeriodSeconds > 0d ? body.OrbitalPeriodSeconds / state.OrbitTimeScale : 1d;
                row["orbitTimeScale"] = state.OrbitTimeScale;
                row["initial"] = MotionAt(state, body.BodyId, 0d);
                row["quarter"] = MotionAt(state, body.BodyId, period * .25d);
                row["half"] = MotionAt(state, body.BodyId, period * .5d);
                row["full"] = MotionAt(state, body.BodyId, period);
                if (body.OrbitalPeriodSeconds > 0d)
                {
                    double angle = (-body.PlacementAngleRadians % (Math.PI * 2d) + Math.PI * 2d) % (Math.PI * 2d);
                    double periapsisTime = angle * period / (Math.PI * 2d);
                    row["periapsis"] = MotionAt(state, body.BodyId, periapsisTime);
                    row["apoapsis"] = MotionAt(state, body.BodyId, periapsisTime + period * .5d);
                }
                row["rotationQuarter"] = MotionAt(state, body.BodyId,
                    Math.Abs(body.RotationPeriodSeconds) > 1e-12d ? Math.Abs(body.RotationPeriodSeconds) * .25d : 1d);
                double time = period * .137d, dt = Math.Max(1e-5d, period * 1e-7d);
                row["derivative"] = new JObject
                {
                    ["dt"] = dt, ["center"] = MotionAt(state, body.BodyId, time),
                    ["before"] = MotionAt(state, body.BodyId, time - dt),
                    ["after"] = MotionAt(state, body.BodyId, time + dt)
                };
                SpaceMotionState entry = new UniverseSimulation(Clone(state)).CreateSpaceEntryState(body.BodyId, .3d, 7d, 2d);
                row["spaceEntry"] = new JObject
                {
                    ["angle"] = .3d, ["height"] = 7d, ["outwardSpeed"] = 2d,
                    ["position"] = Vector(entry.PositionMeters), ["velocity"] = Vector(entry.VelocityMetersPerSecond)
                };
                bodies.Add(row);
            }
        }
        var fixture = new UniverseState
        {
            Bodies = new List<BodyState>
            {
                new() { BodyId = "fixture-moon", PlanetId = "fixture-moon", ParentBodyId = "fixture-earth",
                    RadiusMeters = .1d, PlacementDistanceMeters = 2d, OrbitalPeriodSeconds = 10d },
                new() { BodyId = "fixture-sun", PlanetId = "fixture-sun", RadiusMeters = 1d },
                new() { BodyId = "fixture-earth", PlanetId = "fixture-earth", ParentBodyId = "fixture-sun",
                    RadiusMeters = .1d, PlacementDistanceMeters = 10d, OrbitalPeriodSeconds = 100d, OrbitalEccentricity = .85d }
            }
        };
        var fixtureSimulation = new UniverseSimulation(fixture);
        var fixtureBefore = Universe(fixture);
        fixtureSimulation.AdvanceTime(50d);
        var fixtureHalf = Universe(fixture);
        fixtureSimulation.AdvanceTime(50d);
        var fixtureFull = Universe(fixture);
        var badEccentricity = Clone(fixture); badEccentricity.Bodies[2].OrbitalEccentricity = 1d;
        var missingParent = Clone(fixture); missingParent.Bodies[0].ParentBodyId = "missing";
        var cyclicParent = Clone(fixture); cyclicParent.Bodies[1].ParentBodyId = "fixture-moon";
        var duplicateBody = Clone(fixture); duplicateBody.Bodies.Add(Clone(duplicateBody.Bodies[0]));
        return new JObject
        {
            ["bodies"] = bodies,
            ["fixture"] = new JObject
            {
                ["before"] = fixtureBefore, ["half"] = fixtureHalf, ["full"] = fixtureFull,
                ["zeroRotation"] = fixtureSimulation.GetRotationRadians("fixture-sun")
            },
            ["guards"] = new JArray
            {
                Guard("negative-time", () => fixtureSimulation.AdvanceTime(-1d)),
                Guard("invalid-eccentricity", () => new UniverseSimulation(badEccentricity)),
                Guard("missing-parent", () => new UniverseSimulation(missingParent)),
                Guard("cyclic-parent", () => new UniverseSimulation(cyclicParent)),
                Guard("duplicate-body", () => new UniverseSimulation(duplicateBody)),
                Guard("nan-motion", () => fixtureSimulation.StepFreeMotion(new SpaceMotionState
                    { PositionMeters = new SpaceVector2(double.NaN, 0d) }, default, 1d))
            }
        };
    }
    #endregion

    #region 星区坐标与真实接触
    private static JObject NormalizeSample(string name, SpaceSectorId sector, double x, double y) => new()
    {
        ["name"] = name, ["input"] = Position(new SpaceSectorPosition(sector, new SpaceVector2(x, y))),
        ["output"] = Position(SpaceSectorCoordinates.Normalize(sector, new SpaceVector2(x, y)))
    };

    private static JObject CaptureCoordinates()
    {
        SpaceSectorId huge = new(long.MaxValue - 1, long.MinValue + 1);
        var normalizations = new JArray
        {
            NormalizeSample("inside", default, .125d, -.25d),
            NormalizeSample("left", default, -128000.25d, 0d),
            NormalizeSample("right", default, 128000d, 0d),
            NormalizeSample("down", default, 0d, -128000.25d),
            NormalizeSample("up", default, 0d, 128000d),
            NormalizeSample("negative-edge", default, -128000d, -128000d),
            NormalizeSample("multi-corner", new SpaceSectorId(5, -7), 640000.125d, -640000.25d),
            NormalizeSample("huge-neighbor", huge, 128000.125d, -128000.25d)
        };
        var scenes = new JArray();
        var identities = new JArray();
        foreach (SpaceSectorId sector in new[] { default(SpaceSectorId), new SpaceSectorId(-1, 0), huge,
                     new SpaceSectorId(long.MinValue, long.MaxValue) })
        {
            string key = SpaceSectorCoordinates.SceneKey(sector);
            bool parsed = SpaceSectorCoordinates.TryParseSceneKey(key, out SpaceSectorId restored);
            scenes.Add(new JObject { ["sector"] = Sector(sector), ["key"] = key,
                ["parsed"] = parsed, ["restored"] = Sector(restored) });
            foreach (string identity in new[] { SpaceSectorCoordinates.BodyId(sector, "moon"),
                         SpaceSectorCoordinates.PlanetId(sector, "月球") })
            {
                parsed = SpaceSectorCoordinates.TryParseIdentity(identity, out restored, out string local);
                identities.Add(new JObject { ["sector"] = Sector(sector), ["identity"] = identity,
                    ["parsed"] = parsed, ["restored"] = Sector(restored), ["local"] = local });
            }
        }
        var invalid = new JArray();
        foreach (string text in new[] { "SpaceScene", "SpaceSector__bad__0", "SpaceSector__0", "sector:0:0/body:", "sector:x:0/body:moon" })
            invalid.Add(new JObject { ["input"] = text,
                ["sceneParsed"] = SpaceSectorCoordinates.TryParseSceneKey(text, out _),
                ["identityParsed"] = SpaceSectorCoordinates.TryParseIdentity(text, out _, out _) });
        var guards = new JArray
        {
            Guard("positive-overflow", () => SpaceSectorCoordinates.Normalize(new SpaceSectorId(long.MaxValue, 0), new SpaceVector2(128000d, 0d))),
            Guard("negative-overflow", () => SpaceSectorCoordinates.Normalize(new SpaceSectorId(long.MinValue, 0), new SpaceVector2(-128000.25d, 0d))),
            Guard("nan-local", () => SpaceSectorCoordinates.Normalize(default, new SpaceVector2(double.NaN, 0d))),
            Guard("infinite-local", () => SpaceSectorCoordinates.Normalize(default, new SpaceVector2(double.PositiveInfinity, 0d)))
        };
        SpaceVector2 relative = SpaceSectorCoordinates.Relative(new SpaceSectorId(long.MaxValue, long.MinValue), new SpaceVector2(.125d, -.25d), huge);
        var point = SpaceSectorCoordinates.Normalize(default, new SpaceVector2(-128000.25d, .125d));
        SpaceSectorPosition restoredPoint = Clone(point);
        return new JObject
        {
            ["size"] = SpaceSectorCoordinates.SizeMeters, ["halfSize"] = SpaceSectorCoordinates.HalfSizeMeters,
            ["normalizations"] = normalizations, ["sceneKeys"] = scenes, ["identities"] = identities,
            ["invalid"] = invalid, ["guards"] = guards,
            ["relative"] = Vector(relative), ["roundTrip"] = new JObject { ["before"] = Position(point), ["after"] = Position(restoredPoint) },
            ["contacts"] = CaptureContactSamples(huge)
        };
    }

    private static JArray CaptureContactSamples(SpaceSectorId sector)
    {
        var body = new BodyState { BodyId = SpaceSectorCoordinates.BodyId(sector, "fixture"), TemplateBodyId = "fixture",
            PlanetId = SpaceSectorCoordinates.PlanetId(sector, "fixture"), RadiusMeters = 1d,
            PlacementDistanceMeters = 127999d };
        var simulation = new UniverseSimulation(new UniverseState { Sector = sector, Bodies = new List<BodyState> { body } });
        var motion = new SpaceMotionState { Sector = new SpaceSectorId(sector.X + 1, sector.Y),
            PositionMeters = new SpaceVector2(-127999d, 0d), VelocityMetersPerSecond = new SpaceVector2(-10d, 0d) };
        bool hit = simulation.StepMotionAtTime(motion, default, 0d, 1d, out SurfaceContact contact);
        var result = new JArray
        {
            new JObject { ["name"] = "neighbor", ["hit"] = hit, ["fraction"] = contact.Fraction,
                ["bodyId"] = contact.BodyId, ["sector"] = Sector(contact.Sector),
                ["contactPosition"] = Vector(contact.PositionMeters), ["motionPosition"] = Vector(motion.PositionMeters) },
            new JObject { ["name"] = "far", ["hit"] = simulation.TrySweepSurfaceContact(default(SpaceSectorId),
                new SpaceVector2(127997d, 0d), new SpaceVector2(128001d, 0d), default, default, 0d, 0d, 1d, out _) }
        };
        var empty = new UniverseSimulation(new UniverseState { Sector = sector });
        motion.Sector = sector; motion.PositionMeters = new SpaceVector2(.125d, -.25d);
        motion.VelocityMetersPerSecond = new SpaceVector2(10d, -3d);
        hit = empty.StepMotionAtTime(motion, new SpaceVector2(2d, 1d), 0d, 2d, out _);
        result.Add(new JObject { ["name"] = "empty", ["hit"] = hit,
            ["position"] = Vector(motion.PositionMeters), ["velocity"] = Vector(motion.VelocityMetersPerSecond) });
        foreach (var sample in new[]
                 {
                     ("overlap", new SpaceVector2(0d, 0d), new SpaceVector2(0d, 0d)),
                     ("tangent", new SpaceVector2(-2d, 1d), new SpaceVector2(2d, 1d)),
                     ("miss", new SpaceVector2(-2d, 1.1d), new SpaceVector2(2d, 1.1d))
                 })
        {
            hit = UniverseSimulation.SweptCircle(sample.Item2, sample.Item3, 1d, out double fraction);
            result.Add(new JObject { ["name"] = sample.Item1, ["hit"] = hit, ["fraction"] = fraction });
        }
        return result;
    }
    #endregion

    #region 多观察者星区租约
    private static JObject Observation(string profile, SpaceSectorId sector, double x, double y, double vx, double vy) => new()
    {
        ["profile"] = profile, ["sector"] = Sector(sector),
        ["position"] = Vector(new SpaceVector2(x, y)), ["velocity"] = Vector(new SpaceVector2(vx, vy))
    };
    private static SpaceSectorId ReadSector(JObject value) => new(
        long.Parse((string)value["x"], CultureInfo.InvariantCulture), long.Parse((string)value["y"], CultureInfo.InvariantCulture));
    private static JObject StreamingScenario(string name, params JArray[] observations)
    {
        var policy = new SpaceSectorStreaming();
        var frames = new JArray();
        foreach (JArray frame in observations)
        {
            policy.BeginFrame();
            foreach (JObject row in frame)
                policy.Observe((string)row["profile"], new SpaceSectorPosition(ReadSector((JObject)row["sector"]),
                    new SpaceVector2((double)row["position"]["x"], (double)row["position"]["y"])),
                    new SpaceVector2((double)row["velocity"]["x"], (double)row["velocity"]["y"]));
            policy.EndFrame();
            var ordered = new List<SpaceSectorId>(policy.Required);
            ordered.Sort((left, right) => left.X != right.X ? left.X.CompareTo(right.X) : left.Y.CompareTo(right.Y));
            var required = new JArray();
            foreach (SpaceSectorId sector in ordered) required.Add(Sector(sector));
            frames.Add(new JObject { ["observations"] = frame, ["required"] = required });
        }
        return new JObject { ["name"] = name, ["frames"] = frames };
    }

    private static JObject CaptureStreaming()
    {
        var scenarios = new JArray();
        foreach (var direction in new[] { ("left", -1, 0), ("right", 1, 0), ("down", 0, -1), ("up", 0, 1) })
            scenarios.Add(StreamingScenario(direction.Item1,
                new JArray(Observation("a", default, 0d, 0d, direction.Item2 * 100d, direction.Item3 * 100d)),
                new JArray(Observation("a", default, direction.Item2 * 121000d, direction.Item3 * 121000d,
                    direction.Item2 * 100d, direction.Item3 * 100d))));
        scenarios.Add(StreamingScenario("corner", new JArray(Observation("a", default, 121000d, -121000d, 100d, -100d))));
        scenarios.Add(StreamingScenario("return-release",
            new JArray(Observation("a", default, -121000d, 0d, -100d, 0d)),
            new JArray(Observation("a", new SpaceSectorId(-1, 0), 127000d, 0d, -100d, 0d)),
            new JArray(Observation("a", default, -127000d, 0d, 100d, 0d)),
            new JArray(Observation("a", new SpaceSectorId(-1, 0), 123000d, 0d, -100d, 0d)),
            new JArray(Observation("a", new SpaceSectorId(-1, 0), 127000d, 0d, 100d, 0d)), new JArray()));
        scenarios.Add(StreamingScenario("observers",
            new JArray(Observation("a", default, 0d, 0d, 0d, 0d), Observation("b", default, 0d, 0d, 0d, 0d)),
            new JArray(Observation("a", new SpaceSectorId(-1, 0), 123000d, 0d, -100d, 0d), Observation("b", default, 0d, 0d, 0d, 0d)),
            new JArray(Observation("a", new SpaceSectorId(-1, 0), 123000d, 0d, -100d, 0d)), new JArray()));
        scenarios.Add(StreamingScenario("fast-jump",
            new JArray(Observation("a", default, 0d, 0d, 100d, 0d)),
            new JArray(Observation("a", new SpaceSectorId(1000000000000000, 0), 0d, 0d, 1000000d, 0d))));
        scenarios.Add(StreamingScenario("max-corner", new JArray(Observation("a",
            new SpaceSectorId(long.MaxValue, long.MaxValue), 127000d, 127000d, 100d, 100d))));
        scenarios.Add(StreamingScenario("min-corner", new JArray(Observation("a",
            new SpaceSectorId(long.MinValue, long.MinValue), -127000d, -127000d, -100d, -100d))));
        return new JObject { ["transitionMeters"] = SpaceSectorStreaming.TransitionMeters,
            ["prefetchMeters"] = SpaceSectorStreaming.PrefetchMeters, ["scenarios"] = scenarios };
    }
    #endregion

    #region 会话目录往返与重新绑定
    private static JObject SessionSnapshot(SpaceSessionState state)
    {
        var sectors = new JArray();
        foreach (UniverseState sector in state.Sectors) sectors.Add(Universe(sector));
        return new JObject { ["version"] = state.Version, ["time"] = state.SimulationSeconds,
            ["navigationSequence"] = state.NavigationSequence.ToString(CultureInfo.InvariantCulture), ["sectors"] = sectors,
            ["ships"] = state.Ships.Count, ["flights"] = state.Flights.Count, ["devices"] = state.Devices.Count,
            ["passengers"] = state.Passengers.Count, ["navigation"] = state.Navigation.Count,
            ["docking"] = state.Docking.Count, ["looseItems"] = state.LooseItems.Count };
    }

    private static JObject CaptureSnapshots()
    {
        SpaceCatalog catalog = SpaceCatalog.LoadDefault();
        var state = new SpaceSessionState { SimulationSeconds = 123.456d, NavigationSequence = 17 };
        foreach (SpaceSectorId sector in catalog.ConfiguredSectors)
        {
            UniverseState universe = catalog.GenerateSector(Seed, sector);
            new UniverseSimulation(universe).AdvanceTime(state.SimulationSeconds);
            state.Sectors.Add(universe);
        }
        string json = JsonConvert.SerializeObject(state);
        JObject original = SessionSnapshot(state);
        SpaceSessionState restored = SaveDataMgr.DeserializeSpaceState(json);
        foreach (UniverseState sector in restored.Sectors) _ = new UniverseSimulation(sector);
        JObject restoredSnapshot = SessionSnapshot(restored);
        foreach (UniverseState sector in restored.Sectors)
        {
            sector.FixedStepSeconds = 1d; sector.MetersPerUnityUnit = 9d; sector.OrbitTimeScale = 3d;
            foreach (BodyState body in sector.Bodies)
            {
                body.RadiusMeters = 1d; body.SurfaceGravity = 0d; body.DisplayName = "待重新绑定";
                body.PlacementDistanceMeters = 1d; body.OrbitalPeriodSeconds = 1d;
                body.OrbitalEccentricity = 0d; body.OrbitalPeriapsisRadians = 0d; body.RotationPeriodSeconds = 1d;
            }
        }
        JObject stale = SessionSnapshot(restored);
        foreach (UniverseState sector in restored.Sectors)
        { catalog.RebindUniverse(sector); _ = new UniverseSimulation(sector); }
        var invalidVersion = JObject.Parse(json); invalidVersion["Version"] = 1;
        var invalidTime = JObject.Parse(json); invalidTime["SimulationSeconds"] = -1d;
        var duplicateSector = JObject.Parse(json);
        ((JArray)duplicateSector["Sectors"]).Add(duplicateSector["Sectors"][0].DeepClone());
        return new JObject
        {
            ["original"] = original, ["restored"] = restoredSnapshot, ["stale"] = stale,
            ["rebound"] = SessionSnapshot(restored), ["serializedHasUniverse"] = JObject.Parse(json)["Universe"] != null,
            ["guards"] = new JArray
            {
                Guard("old-version", () => SaveDataMgr.DeserializeSpaceState(invalidVersion.ToString())),
                Guard("negative-time", () => SaveDataMgr.DeserializeSpaceState(invalidTime.ToString())),
                Guard("duplicate-sector", () => SaveDataMgr.DeserializeSpaceState(duplicateSector.ToString())),
                Guard("empty-json", () => SaveDataMgr.DeserializeSpaceState(string.Empty))
            }
        };
    }
    #endregion

    #region 各星体的独立冷地形
    private static JObject CaptureSurfaces()
    {
        SpaceCatalog catalog = SpaceCatalog.LoadDefault();
        var bodies = new JArray();
        foreach (SpaceSectorId sector in catalog.ConfiguredSectors)
            foreach (BodyState body in catalog.GenerateSector(Seed, sector).Bodies)
            {
                PlanetData planet = CreatePlanet(catalog, body);
                planet.TopologyMode = WorldTopologyMode.Wrapped;
                ChunkGenerationProfileSnapshot profile;
                using (GameplayTestShowroom.SuppressProfileOverrides())
                    profile = ChunkMgr.CreateFrozenWorldGenerationProfile(planet);
                if (!WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds))
                    throw new InvalidOperationException("测试星球的环绕边界无效：" + body.BodyId);
                var topology = new ChunkGenerationTopologySnapshot(new Int2(bounds.Min.x, bounds.Min.y), new Int2(bounds.Span.x, bounds.Span.y));
                int seed = SpaceCatalog.DeriveSeed(Seed, body.PlanetId);
                JArray first = ColdSurfaceSamples(planet, profile, topology, seed);
                JArray repeated = ColdSurfaceSamples(planet, profile, topology, seed);
                bodies.Add(new JObject
                {
                    ["id"] = body.BodyId, ["planetId"] = body.PlanetId, ["surface"] = Surface(catalog.GetSurfaceProfile(body.BodyId)),
                    ["planet"] = Planet(planet), ["generationSeed"] = seed,
                    ["profile"] = new JObject { ["id"] = profile.ProfileId, ["width"] = profile.Width, ["height"] = profile.Height,
                        ["fingerprint"] = profile.GenerationFingerprint.ToString(CultureInfo.InvariantCulture),
                        ["numbers"] = Json(profile.NumericParameters), ["texts"] = Json(profile.TextParameters),
                        ["ecologyMultiplier"] = profile.EcologyGlobalMultiplier,
                        ["ecologyRuleCount"] = profile.EcologyRules.Count, ["caveResourceRuleCount"] = profile.CaveResourceRules.Count },
                    ["span"] = new JObject { ["x"] = bounds.Span.x, ["y"] = bounds.Span.y },
                    ["samples"] = first, ["repeatSamples"] = repeated,
                    ["resourceWindow"] = body.SurfaceProfileId.StartsWith("core:surface.asteroid.", StringComparison.Ordinal)
                        ? ColdOreAsteroidResourceWindow(planet, profile, topology, seed) : null,
                    ["repeatResourceWindow"] = body.SurfaceProfileId.StartsWith("core:surface.asteroid.", StringComparison.Ordinal)
                        ? ColdOreAsteroidResourceWindow(planet, profile, topology, seed) : null
                });
            }
        return new JObject { ["source"] = "isolated-cold-generation", ["bodies"] = bodies };
    }

    private static JArray ColdSurfaceSamples(PlanetData planet, ChunkGenerationProfileSnapshot profile,
        ChunkGenerationTopologySnapshot topology, int seed)
    {
        using var world = new WorldRuntime(planet.Name, 1);
        var generator = new DeterministicChunkGenerator(GameRes.RequireWorldLiquidTypes(), SpaceSurfaceTerrain.Apply);
        var samples = new JArray();
        try
        {
            foreach (Int2 point in new[] { new Int2(0, 0), new Int2(-1, -1), new Int2(topology.Span.X, topology.Span.Y) })
            {
                int x = topology.NormalizeX(point.X), y = topology.NormalizeY(point.Y);
                int originX = (int)Math.Floor(x / (double)profile.Width) * profile.Width;
                int originY = (int)Math.Floor(y / (double)profile.Height) * profile.Height;
                var address = new RuntimeAddress(global::WorldAddress.SurfaceDimensionId, new Int2(originX, originY));
                if (!world.TryGetChunkTerrain(address, out ChunkTerrainData terrain))
                {
                    ChunkGenerationRequest request = world.BeginChunkGeneration(address, seed, profile, topology);
                    using ChunkGenerationResult generated = generator.Generate(request, CancellationToken.None);
                    if (!world.TryCommit(generated, out string reason)) throw new InvalidOperationException("冷地形提交失败：" + reason);
                    if (!world.TryGetChunkTerrain(address, out terrain)) throw new InvalidOperationException("冷地形提交后缺少区块。");
                }
                int localX = x - originX, localY = y - originY;
                TerrainCell cell = terrain.GetCell(localX, localY);
                bool temperatureFound = terrain.TryGetEnvironmentValue("temperature.celsius", localX, localY, out float temperature);
                terrain.TryGetEnvironmentValue("windX", localX, localY, out float windX);
                terrain.TryGetEnvironmentValue("windY", localX, localY, out float windY);
                if (!world.TryGetChunk(address, out ChunkRuntime runtime))
                    throw new InvalidOperationException("冷地形采样缺少正式生态区块。");
                samples.Add(new JObject
                {
                    ["input"] = new JObject { ["x"] = point.X, ["y"] = point.Y },
                    ["cell"] = new JObject { ["x"] = x, ["y"] = y },
                    ["groundTileId"] = cell.GroundTileId, ["blockingTileId"] = cell.BlockingTileId,
                    ["supportTileId"] = TerrainSupportLayer.GetTileId(terrain, localX, localY), ["biomeId"] = cell.BiomeId,
                    ["walkable"] = cell.IsWalkable, ["liquidId"] = terrain.GetLiquidId(localX, localY),
                    ["liquidDepth"] = terrain.GetLiquidDepth(localX, localY), ["temperatureFound"] = temperatureFound,
                    ["temperatureCelsius"] = temperature + planet.GlobalTemperature - PlanetData.DefaultGlobalTemperature,
                    ["grass"] = terrain.GetGrass(localX, localY), ["windX"] = windX, ["windY"] = windY,
                    ["weatherElementCount"] = terrain.WeatherProfile?.Elements.Count ?? -1,
                    ["naturalItems"] = JObject.FromObject(runtime.Ecology.Placements.GroupBy(value => value.ItemId)
                        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)),
                    ["pressureKPa"] = AtmosphereService.PressureKPa(planet.Atmosphere),
                    ["terrainHash"] = terrain.ComputeStableHash().ToString(CultureInfo.InvariantCulture)
                });
            }
        }
        finally { generator.BeginNewWorldHydrologyEpoch(1); }
        return samples;
    }

    private static JObject ColdOreAsteroidResourceWindow(PlanetData planet, ChunkGenerationProfileSnapshot original,
        ChunkGenerationTopologySnapshot topology, int seed)
    {
        ChunkGenerationProfileSnapshot profile = original.WithChunkSize(128, 128, false);
        using var world = new WorldRuntime(planet.Name, 1);
        var generator = new DeterministicChunkGenerator(GameRes.RequireWorldLiquidTypes(), SpaceSurfaceTerrain.Apply);
        try
        {
            var address = new RuntimeAddress(global::WorldAddress.SurfaceDimensionId, default);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, seed, profile, topology);
            using ChunkGenerationResult generated = generator.Generate(request, CancellationToken.None);
            if (!world.TryCommit(generated, out string reason) || !world.TryGetChunk(address, out ChunkRuntime runtime))
                throw new InvalidOperationException("小行星真实矿物窗口生成失败：" + reason);
            ChunkTerrainData terrain = runtime.Terrain;
            int waterTiles = 0, grassTiles = 0;
            double maximumWind = 0d;
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                if (terrain.GetLiquidDepth(x, y) > 0f) waterTiles++;
                if (terrain.GetGrass(x, y) == ChunkTerrainData.GrassPresent) grassTiles++;
                terrain.TryGetEnvironmentValue("windX", x, y, out float windX);
                terrain.TryGetEnvironmentValue("windY", x, y, out float windY);
                maximumWind = Math.Max(maximumWind, Math.Sqrt(windX * windX + windY * windY));
            }
            uint identity = 2166136261u;
            foreach (NaturalItemPlacement placement in runtime.Ecology.Placements)
                unchecked { identity = (identity ^ (uint)placement.Guid) * 16777619u; }
            return new JObject { ["width"] = terrain.Width, ["height"] = terrain.Height,
                ["terrainHash"] = terrain.ComputeStableHash().ToString(CultureInfo.InvariantCulture),
                ["placementHash"] = identity.ToString(CultureInfo.InvariantCulture),
                ["naturalItems"] = JObject.FromObject(runtime.Ecology.Placements.GroupBy(value => value.ItemId)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)),
                ["waterTiles"] = waterTiles, ["grassTiles"] = grassTiles, ["maximumWind"] = maximumWind,
                ["weatherElementCount"] = terrain.WeatherProfile?.Elements.Count ?? -1 };
        }
        finally { generator.BeginNewWorldHydrologyEpoch(1); }
    }
    #endregion
}
