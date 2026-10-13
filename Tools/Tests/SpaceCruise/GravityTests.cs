using FlatWorld.Spaceflight;

internal static partial class Program
{
    #region 星球引力领域回归
    private static SpaceGravitySource GravityFixture(double thickness = 0d, double gravity = 9.81d, double radius = 2000d)
    {
        var state = new UniverseState
        {
            Bodies = { new BodyState { BodyId = "earth", PlanetId = "Earth", RadiusMeters = radius,
                SurfaceGravity = gravity, AtmosphereThickness = thickness } }
        };
        var universe = new UniverseSimulation(state);
        return new SpaceGravitySource(universe, universe.GetBody("earth"), Settings);
    }

    private static double GravityEnergy(SpaceGravitySource source, SpaceVector2 position, SpaceVector2 velocity)
        => velocity.SqrMagnitude + source.AngularFrequency * source.AngularFrequency * position.SqrMagnitude;

    private static void GravityMassRules()
    {
        var light = GravityFixture();
        var heavy = GravityFixture(gravity: 39.24d);
        Check(heavy.Body.MassKg > light.Body.MassKg, "surface gravity and radius define total mass");
        Check(light.RadiusMeters > light.Body.RadiusMeters && heavy.RadiusMeters > light.RadiusMeters,
            "capture boundary extends past the planet and grows with mass");
        Check(heavy.AngularFrequency * heavy.RadiusMeters > light.AngularFrequency * light.RadiusMeters,
            "a larger mass gives greater center speed when released at the boundary");
        var dense = GravityFixture(gravity: 100d, radius: 2500d);
        var large = GravityFixture(gravity: 24.79d, radius: 4000d);
        Check(dense.Body.MassKg > large.Body.MassKg && dense.RadiusMeters > large.RadiusMeters,
            "a small but heavier planet also has a larger capture field");
    }

    private static void GravityUndampedMotion()
    {
        var source = GravityFixture();
        double quarterPeriod = Math.PI / (2d * source.AngularFrequency);
        SpaceGravity.AdvanceRelative(source, new(source.RadiusMeters, 0d), default, quarterPeriod, out var center, out var speed);
        Check(center.Magnitude < 1e-8d, "rest at the boundary crosses the center after a quarter period");
        Near(Settings.GravityReferenceCenterSpeedMetersPerSecond, speed.Magnitude, "center has the baseline maximum speed");
        SpaceGravity.AdvanceRelative(source, new(source.RadiusMeters, 0d), default, quarterPeriod * 2d, out var opposite, out var stopped);
        Near(-source.RadiusMeters, opposite.X, "motion reaches the opposite endpoint");
        Check(stopped.Magnitude < 1e-8d, "velocity is zero at the opposite endpoint");
        SpaceVector2 position = new(1000d, 0d), velocity = new(0d, 20d);
        double energy = GravityEnergy(source, position, velocity);
        // 长时间重复小步推进，检查非径向轨迹不会靠数值误差衰减或爆炸。
        for (int i = 0; i < 100000; i++)
            SpaceGravity.AdvanceRelative(source, position, velocity, .02d, out position, out velocity);
        Check(Math.Abs(GravityEnergy(source, position, velocity) - energy) < energy * 1e-8d,
            "an airless curved orbit preserves energy over many fixed steps");
    }

    private static void GravityAtmosphere()
    {
        var thin = GravityFixture(1d);
        var thick = GravityFixture(2d);
        var dense = GravityFixture(10000d);
        Check(thick.DampingPerSecond > thin.DampingPerSecond, "thicker atmosphere increases damping");
        Check(dense.DampingPerSecond < 2d * dense.AngularFrequency, "damping stays below the loss of oscillation");
        SpaceVector2 origin = new(1000d, 0d), velocity = new(0d, 20d);
        SpaceGravity.AdvanceRelative(thin, origin, velocity, 200d, out var thinPosition, out var thinVelocity);
        SpaceGravity.AdvanceRelative(thick, origin, velocity, 200d, out var thickPosition, out var thickVelocity);
        Check(GravityEnergy(thick, thickPosition, thickVelocity) < GravityEnergy(thin, thinPosition, thinVelocity),
            "thicker atmosphere removes more energy over the same time");
        Check(GravityEnergy(thin, thinPosition, thinVelocity) < GravityEnergy(thin, origin, velocity),
            "atmosphere damps both radial and transverse motion");
        Near(0d, GravityFixture().DampingPerSecond, "airless planets have no damping");
    }

    private static void GravityBoundaries()
    {
        var source = GravityFixture();
        Check(SpaceGravity.TryEntryFraction(new(source.RadiusMeters * 2d, 0d), new(-source.RadiusMeters * 4d, 0d),
            source.RadiusMeters, out double entry), "a fast object crossing the whole field still detects entry");
        Near(.25d, entry, "entry lies on the actual capture boundary");
        Check(!SpaceGravity.TryEntryFraction(new(source.RadiusMeters, 0d), new(100d, 0d), source.RadiusMeters, out _),
            "an outward object at the boundary can escape");
        double exit = SpaceGravity.FindExitSeconds(source, default, new(800d, 0d), 10d);
        SpaceGravity.AdvanceRelative(source, default, new(800d, 0d), exit, out var point, out var velocity);
        Check(Math.Abs(point.Magnitude - source.RadiusMeters) < .01d && velocity.X > 0d,
            "escape stops integration at the boundary with outward velocity");
        var orbit = OrbitFixture();
        SpaceVector2 previousVelocity = orbit.GetBody("earth").VelocityMetersPerSecond;
        orbit.AdvanceTime(.02d);
        Check((orbit.GetVelocityAtTime("earth", 0d) - previousVelocity).Magnitude < 1e-10d,
            "relative motion uses the pre-step orbital velocity");
        Check((orbit.GetVelocityAtTime("earth", .02d) - orbit.GetBody("earth").VelocityMetersPerSecond).Magnitude < 1e-10d,
            "relative motion restores the current orbital velocity");
    }
    #endregion
}
