using FlatWorld.Spaceflight;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    #region 星区坐标与连续运动回归
    private static void SectorCoordinates()
    {
        JObject config = JObject.Parse(File.ReadAllText(Path.Combine(FindEngineFixtureRoot(),
            "Assets/StreamingAssets/GameConfig/Space/solar-system.json")));
        var solarTemplate = new UniverseState { TemplateId = config.Value<string>("templateId"),
            OrbitTimeScale = config.Value<double>("orbitTimeScale"), Bodies = config["bodies"].ToObject<List<BodyState>>() };
        UniverseState solar = UniverseSectorFactory.Generate(solarTemplate, 42, default, true);
        var siriusTemplate = new UniverseState { TemplateId = config["sectorSystems"][0].Value<string>("templateId"),
            OrbitTimeScale = solarTemplate.OrbitTimeScale, Bodies = config["sectorSystems"][0]["bodies"].ToObject<List<BodyState>>() };
        UniverseState sirius = UniverseSectorFactory.Generate(siriusTemplate, 42, new(-1, 0), true);
        Check(solar.Bodies.Count == 10 && sirius.Bodies.Count == 2, "configured solar and Sirius templates instantiate independently");
        var solarSimulation = new UniverseSimulation(solar);
        var siriusSimulation = new UniverseSimulation(sirius);
        Check(solarSimulation.GetBody("earth").BodyId == "sector:0:0/body:earth", "solar earth has globally scoped identity");
        Check(siriusSimulation.GetBody("sirius_b").ParentBodyId == siriusSimulation.GetBody("sirius_a").BodyId,
            "Sirius child orbit references instance parent");
        UniverseState repeat = UniverseSectorFactory.Generate(siriusTemplate, 42, new(-1, 0), true);
        Check(repeat.Bodies[1].Seed == sirius.Bodies[1].Seed && repeat.Bodies[1].PlacementAngleRadians == sirius.Bodies[1].PlacementAngleRadians,
            "same sector regenerates same identity and seeded phase");
        Check(solarTemplate.Bodies[0].BodyId == "sun" && siriusTemplate.Bodies[0].BodyId == "sirius_a", "generation never mutates content templates");
        SpaceSectorPosition left = SpaceSectorCoordinates.Normalize(default, new(-128000.25d, 0d));
        Check(left.Sector == new SpaceSectorId(-1, 0), "left edge selects negative neighbor");
        Near(127999.75d, left.LocalMeters.X, "left crossing keeps exact displacement");
        SpaceSectorPosition corner = SpaceSectorCoordinates.Normalize(new(5, -7), new(640000.125d, -640000.25d));
        Check(corner.Sector == new SpaceSectorId(8, -10), "multi-sector diagonal crossing");
        Near(-127999.875d, corner.LocalMeters.X, "large step x remainder");
        Near(127999.75d, corner.LocalMeters.Y, "large step y remainder");
        SpaceSectorId huge = new(long.MaxValue - 1, long.MinValue + 1);
        SpaceVector2 relative = SpaceSectorCoordinates.Relative(new(long.MaxValue, long.MinValue), new(.125d, -.25d), huge);
        Near(256000.125d, relative.X, "adjacent long max preserves sub-meter x");
        Near(-256000.25d, relative.Y, "adjacent long min preserves sub-meter y");
        Check(SpaceSectorCoordinates.TryParseSceneKey(SpaceSectorCoordinates.SceneKey(huge), out SpaceSectorId parsed) && parsed == huge,
            "scene key preserves all long digits");
        Check(!SpaceSectorCoordinates.TryParseSceneKey("SpaceScene", out _), "plain scene is not a sector identity");
        string bodyId = SpaceSectorCoordinates.BodyId(huge, "moon");
        Check(SpaceSectorCoordinates.TryParseIdentity(bodyId, out parsed, out string local) && parsed == huge && local == "moon",
            "stable body identity parses huge sector");
        SpaceSectorPosition restored = JsonConvert.DeserializeObject<SpaceSectorPosition>(JsonConvert.SerializeObject(left));
        Check(restored.Sector == left.Sector && restored.LocalMeters == left.LocalMeters, "sector position JSON round trip");
        var empty = new UniverseSimulation(new UniverseState { Sector = huge });
        var motion = new SpaceMotionState { Sector = huge, PositionMeters = new(1d, 2d), VelocityMetersPerSecond = new(10d, -3d) };
        empty.AdvanceTime(2d);
        Check(!empty.StepMotion(motion, default, 2d, out _), "empty sector has no fake planet contacts");
        Check(motion.PositionMeters == new SpaceVector2(21d, -4d), "empty sector preserves real inertial motion");
        Check(motion.VelocityMetersPerSecond == new SpaceVector2(10d, -3d), "empty sector preserves velocity");
        var distantEmpty = new UniverseSimulation(new UniverseState());
        motion.PositionMeters = new(.125d, .25d);
        distantEmpty.StepMotion(motion, default, .02d, out _);
        Near(.325d, motion.PositionMeters.X, "far reference queries never round off local free displacement");
    }

    private static void SectorSurfaceContacts()
    {
        SpaceSectorId sector = new(long.MaxValue - 1, 0);
        var body = new BodyState { BodyId = SpaceSectorCoordinates.BodyId(sector, "body"), TemplateBodyId = "body",
            PlanetId = SpaceSectorCoordinates.PlanetId(sector, "planet"), RadiusMeters = 1d,
            PlacementDistanceMeters = 127999d, PlacementAngleRadians = 0d };
        var simulation = new UniverseSimulation(new UniverseState { Sector = sector, Bodies = new() { body } });
        Check(simulation.GetBody("body") == body, "local template lookup is scoped to this sector");
        var motion = new SpaceMotionState { Sector = new(sector.X + 1, 0), PositionMeters = new(-127999d, 0d),
            VelocityMetersPerSecond = new(-10d, 0d) };
        Check(simulation.StepMotionAtTime(motion, default, 0d, 1d, out SurfaceContact contact), "neighboring sector passenger hits real body");
        Near(.1d, contact.Fraction, "cross-sector contact time");
        Check(contact.Sector == sector, "contact records planet sector");
        Near(128000d, contact.PositionMeters.X, "contact location uses body sector");
        Near(-128000d, motion.PositionMeters.X, "motion keeps its original coordinate frame");
        Check(!simulation.TrySweepSurfaceContact(new(0, 0), new(127997d, 0d), new(128001d, 0d), default, default,
            0d, 0d, 1d, out _), "far sector with identical local coordinates cannot hit this body");
    }

    private static void SectorDocking()
    {
        var first = Ship("first", 0d);
        var second = Ship("second", 0d);
        first.PlatformKind = second.PlatformKind = ShipPlatformKind.Space;
        first.Sector = new(long.MaxValue - 1, 0); second.Sector = new(long.MaxValue, 0);
        first.PositionX = 127999d; second.PositionX = -127999d;
        first.MassKg = second.MassKg = 10d;
        var ships = new List<ShipState> { first, second };
        ShipAssemblyState assembly = ShipDockingService.RebuildAssemblies(ships, new() { Pair(first, second) })[0];
        Check(assembly.Sector == first.Sector, "assembly uses stable primary member reference");
        Near(128000d, assembly.PositionX, "cross-sector center of mass uses true nearby positions");
        assembly.PositionX += 3d;
        ShipDockingService.RigidlyCarryMembers(assembly, ships);
        Near(128002d, first.PositionX, "first member moves without local layout jump");
        Near(-127996d, second.PositionX, "second member keeps its own local coordinates");
    }

    private static void SectorPassengerContacts()
    {
        SpacePassengerContactState Passenger(SpaceSectorId sector, double x, double speed, double mass = 80d, bool supported = false)
            => new() { PreviousPosition = SpaceSectorCoordinates.Normalize(sector, new(x, .125d)),
                Position = SpaceSectorCoordinates.Normalize(sector, new(x + speed, .125d)),
                VelocityMetersPerSecond = new(speed, 0d), RadiusMeters = .5d, MassKg = mass, Supported = supported };
        SpaceSectorId huge = new(long.MaxValue - 1, long.MinValue + 1);
        var left = Passenger(huge, 127998d, 10d);
        var right = Passenger(new(huge.X + 1, huge.Y), -127998d, -10d);
        Check(SpacePassengerContacts.ResolvePair(left, right, 1d), "free passengers sweep each other across boundary");
        Near(0d, left.VelocityMetersPerSecond.X, "equal mass free contact stops opposing normal velocity");
        Near(0d, right.VelocityMetersPerSecond.X, "opposing passenger receives matching momentum");
        Check(SpaceSectorCoordinates.Difference(right.Position, left.Position).Magnitude >= 1d, "large long sector collision never tunnels");
        Near(.125d, left.Position.LocalMeters.Y, "high sector numbers retain fractional transverse position");

        left = Passenger(default, -2d, 10d, 100d); right = Passenger(default, 2d, -10d, 300d);
        Check(SpacePassengerContacts.ResolvePair(left, right, 1d), "unequal mass passenger contact detected");
        Near(-5d, left.VelocityMetersPerSecond.X, "lighter passenger follows inelastic momentum");
        Near(-5d, right.VelocityMetersPerSecond.X, "heavier passenger retains weighted velocity");
        Near(-4.7500075d, left.Position.LocalMeters.X, "remaining time integrates resolved velocity");

        left = Passenger(default, -2d, 10d); right = Passenger(default, 2d, 0d, supported: true);
        Check(SpacePassengerContacts.ResolvePair(left, right, 1d), "supported passenger blocks free passenger");
        Near(2d, right.Position.LocalMeters.X, "supported obstacle retains its carrier point");
        Near(0d, left.VelocityMetersPerSecond.X, "free passenger normal speed stops at supported obstacle");
        Check(left.Position.LocalMeters.X < 1d, "free passenger remains outside supported circle");

        left = Passenger(default, -2d, 4d, supported: true); right = Passenger(default, 2d, -4d, supported: true);
        Check(SpacePassengerContacts.ResolvePair(left, right, 1d), "supported passengers cannot pass through one another");
        Check(SpaceSectorCoordinates.Difference(right.Position, left.Position).X >= 1d, "supported passengers retain order and separated local points");
        Near(4d, left.VelocityMetersPerSecond.X, "supported collision never changes underlying ship velocity");

        left = Passenger(huge, .125d, 0d); right = Passenger(default, .125d, 0d);
        Check(!SpacePassengerContacts.ResolvePair(left, right, .02d), "far sectors with equal local coordinates do not collide");
        Near(.125d, left.Position.LocalMeters.X, "far exclusion never rewrites local precision");
        left = Passenger(default, -.2d, 0d); right = Passenger(default, .2d, 0d);
        Check(SpacePassengerContacts.ResolvePair(left, right, .02d), "already overlapping passengers separate without a scene");
        Check(SpaceSectorCoordinates.Difference(right.Position, left.Position).Magnitude >= 1d, "resting overlap resolves completely");
    }
    #endregion
}
