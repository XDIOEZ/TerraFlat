using FlatWorld.Spaceflight;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    #region 椭圆星系与自由起飞回归
    private static UniverseSimulation OrbitFixture(double eccentricity = .2d)
        => new(new UniverseState
        {
            Bodies = new List<BodyState>
            {
                new() { BodyId = "moon", PlanetId = "moon", RadiusMeters = .1d, ParentBodyId = "earth",
                    PlacementDistanceMeters = 2d, OrbitalPeriodSeconds = 10d },
                new() { BodyId = "sun", PlanetId = "sun", RadiusMeters = 1d },
                new() { BodyId = "earth", PlanetId = "earth", RadiusMeters = .1d, ParentBodyId = "sun",
                    PlacementDistanceMeters = 10d, OrbitalPeriodSeconds = 100d, OrbitalEccentricity = eccentricity }
            }
        });

    private static void OrbitMotion()
    {
        var universe = OrbitFixture();
        BodyState earth = universe.GetBody("earth"), moon = universe.GetBody("moon");
        Near(8d, earth.PositionMeters.Magnitude, "elliptical orbit starts at periapsis");
        Near(2d, (moon.PositionMeters - earth.PositionMeters).Magnitude, "child uses parent position independent of config order");
        SpaceVector2 initial = earth.PositionMeters;
        double fastSpeed = earth.VelocityMetersPerSecond.Magnitude;
        universe.AdvanceTime(50d);
        Near(12d, earth.PositionMeters.Magnitude, "half a period reaches apoapsis");
        Check(fastSpeed > earth.VelocityMetersPerSecond.Magnitude, "Kepler motion is slower at apoapsis");
        Near(fastSpeed / earth.VelocityMetersPerSecond.Magnitude, 1.5d, "speed ratio respects eccentricity");
        universe.AdvanceTime(50d);
        Near(0d, (initial - earth.PositionMeters).Magnitude, "one orbital period returns to initial position");
        universe.AdvanceTime(1.25d);
        Near(Math.Sqrt(2d), (moon.PositionMeters - earth.PositionMeters).X, "moon continues its own orbit around moving parent");
        SpaceVector2 velocity = earth.VelocityMetersPerSecond;
        double dt = .00001d;
        SpaceVector2 before = universe.GetPositionAtTime(earth.BodyId, universe.State.SimulationSeconds - dt);
        SpaceVector2 after = universe.GetPositionAtTime(earth.BodyId, universe.State.SimulationSeconds + dt);
        Near(0d, ((after - before) / (2d * dt) - velocity).Magnitude, "analytical orbital velocity matches position derivative", 1e-8d);
        var restored = new UniverseSimulation(JsonConvert.DeserializeObject<UniverseState>(JsonConvert.SerializeObject(universe.State)));
        Near(0d, (restored.GetBody("moon").PositionMeters - moon.PositionMeters).Magnitude, "snapshot reconstructs same parent and child positions");
        Near(0d, (restored.GetBody("earth").VelocityMetersPerSecond - velocity).Magnitude, "runtime orbital velocity is reconstructed from clock");

        var config = JObject.Parse(File.ReadAllText(Path.Combine(FindEngineFixtureRoot(), "Assets/StreamingAssets/GameConfig/Space/solar-system.json")));
        var production = new UniverseSimulation(new UniverseState
        {
            OrbitTimeScale = config.Value<double>("orbitTimeScale"),
            Bodies = config["bodies"].ToObject<List<BodyState>>()
        });
        Check(production.GetBody("sun").RadiusMeters == 10000d, "sun uses the enlarged radius");
        BodyState mercury = production.GetBody("mercury");
        Check(mercury.PlacementDistanceMeters * (1d - mercury.OrbitalEccentricity) - mercury.RadiusMeters > production.GetBody("sun").RadiusMeters,
            "enlarged sun does not overlap Mercury at periapsis");
        foreach (BodyState body in production.State.Bodies)
            if (body.ParentBodyId.Length > 0)
                Check(body.OrbitalPeriodSeconds > 0d && body.OrbitalEccentricity > 0d, "all orbiting production bodies have real orbital parameters");
    }

    private static void MovingBodyEntry()
    {
        var universe = OrbitFixture(0d);
        BodyState body = universe.GetBody("earth");
        SpaceVector2 initialVelocity = body.VelocityMetersPerSecond;
        var entry = ShipLandingGuidance.CreateParkedSpaceEntry(universe, body, .3d, 7d, Settings);
        Near(body.RadiusMeters + 7d + Settings.SpaceEntryClearanceMeters, (entry.PositionMeters - body.PositionMeters).Magnitude,
            "takeoff exit is at the hull clearance instead of launch height");
        Near(0d, (entry.VelocityMetersPerSecond - initialVelocity).Magnitude, "takeoff has no outward velocity and inherits moving planet velocity");
        SpaceVector2 from = body.PositionMeters;
        SpaceVector2 to = universe.GetPositionAtTime(body.BodyId, 1d);
        var stationary = new SpaceMotionState { PositionMeters = (from + to) * .5d };
        Check(universe.StepMotionAtTime(stationary, default, 0d, 1d, out SurfaceContact contact), "moving body sweeps stationary free passenger");
        Check(contact.Fraction > 0d && contact.Fraction < 1d, "moving sweep locates time of contact");
        Near(0d, (contact.RelativeVelocityMetersPerSecond + to - from).Magnitude, "contact subtracts moving body speed");
        universe.StepFreeMotion(entry, default, 1d);
        Check(entry.VelocityMetersPerSecond == initialVelocity, "free ship does not bind itself to an orbital path");
        universe.AdvanceTime(1d);
        Near(0d, (ShipLandingGuidance.CreateDescentContact(body, body.PositionMeters, body.VelocityMetersPerSecond, 1d)
            .RelativeVelocityMetersPerSecond).Magnitude, "landing measures speed relative to the moving planet");
        for (int i = 0; i < 100; i++) universe.AdvanceTime(.02d);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) universe.AdvanceTime(.02d);
        Near(0d, GC.GetAllocatedBytesForCurrentThread() - allocated, "orbital clock updates allocate no temporary collections");
    }
    #endregion

    #region 真实推力约束的起降时长回归
    private static void SurfaceFlightTiming()
    {
        const double mass = 1000d, gravity = 9.81d, seconds = .02d;
        double requiredHeight = 200d;
        double height = 0d, speed = 0d, elapsed = 0d;
        while (height < requiredHeight - 1e-6d && elapsed < 30d)
        {
            double request = ShipLandingGuidance.RequiredVerticalThrust(mass, gravity, speed,
                ShipLandingGuidance.TakeoffSpeed(requiredHeight, Settings), seconds);
            double actual = Math.Min(mass * 200d, request);
            speed += (actual / mass - gravity) * seconds;
            height += speed * seconds; elapsed += seconds;
        }
        Check(elapsed >= Settings.TakeoffDurationSeconds - 1e-6d && elapsed < 14d,
            "healthy ship takes at least configured takeoff duration");

        height = Settings.SurfaceEntryHeightMeters; speed = 0d; elapsed = 0d;
        while (height > 1e-8d && elapsed < 20d)
        {
            double desired = ShipLandingGuidance.LandingSpeed(elapsed, height, seconds, Settings);
            double request = ShipLandingGuidance.RequiredVerticalThrust(mass, gravity, speed, desired, seconds);
            double actual = Math.Min(mass * 200d, request);
            speed += (actual / mass - gravity) * seconds;
            height += speed * seconds; elapsed += seconds;
        }
        Near(Settings.LandingDurationSeconds, elapsed, "normal descent follows configured duration", seconds + 1e-6d);
        Check(Math.Abs(speed) <= Settings.MaximumSafeLandingSpeedMetersPerSecond, "healthy descent brakes to safe touchdown speed");
        height = Settings.SurfaceEntryHeightMeters; speed = -1d;
        double requested = ShipLandingGuidance.RequiredVerticalThrust(mass, gravity, speed, 0d, seconds);
        double insufficientActualThrust = mass * gravity * .5d;
        double limitedSpeed = speed + (Math.Min(requested, insufficientActualThrust) / mass - gravity) * seconds;
        Check(limitedSpeed < speed, "underpowered engines cannot produce free braking");
        double noFuelSpeed = limitedSpeed - gravity * seconds;
        Check(noFuelSpeed < limitedSpeed, "fuel loss keeps real gravity-driven descent");
        double rescueRequest = ShipLandingGuidance.RequiredVerticalThrust(mass, gravity, noFuelSpeed,
            ShipLandingGuidance.TakeoffSpeed(requiredHeight, Settings), seconds);
        double rescueSpeed = noFuelSpeed + (Math.Min(rescueRequest, mass * 200d) / mass - gravity) * seconds;
        Check(rescueSpeed > noFuelSpeed, "reignition with available thrust can rescue falling ship");
    }
    #endregion
}
