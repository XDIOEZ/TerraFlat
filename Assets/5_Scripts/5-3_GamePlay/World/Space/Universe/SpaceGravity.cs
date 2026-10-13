using System;

namespace FlatWorld.Spaceflight
{
    public readonly struct SpaceGravitySource
    {
        #region 星球引力参数缓存
        public readonly UniverseSimulation Universe;
        public readonly BodyState Body;
        public readonly double RadiusMeters, RadiusSquared, AngularFrequency, DampingPerSecond;

        public SpaceGravitySource(UniverseSimulation universe, BodyState body, SpaceGameplaySettings settings)
        {
            Universe = universe;
            Body = body;
            double massRatio = Math.Sqrt(body.SurfaceGravity / settings.EarthGravity) * body.RadiusMeters / settings.GravityReferenceRadiusMeters;
            RadiusMeters = (settings.GravityReferenceRadiusMeters + settings.GravityCaptureMarginMeters) * Math.Pow(massRatio, 2d / 3d);
            RadiusSquared = RadiusMeters * RadiusMeters;
            AngularFrequency = settings.GravityReferenceCenterSpeedMetersPerSecond * massRatio / RadiusMeters;
            DampingPerSecond = Math.Min(settings.AtmosphereDampingPerSecond * body.AtmosphereThickness,
                2d * AngularFrequency * settings.MaximumGravityDampingRatio);
            if (!SpaceVector2.Finite(RadiusSquared) || RadiusMeters <= body.RadiusMeters ||
                !SpaceVector2.Finite(AngularFrequency) || AngularFrequency <= 0d || !SpaceVector2.Finite(DampingPerSecond))
                throw new InvalidOperationException($"星球 {body.BodyId} 的引力范围必须大于星球半径，请调整引力参考半径或捕获余量。");
        }
        #endregion
    }

    public static class SpaceGravity
    {
        #region 中心引力与大气阻尼
        // 游戏只提供引力和相对大气阻力，位置、速度及接触冲量由 Unity 2D 求解。
        public static SpaceVector2 Acceleration(SpaceGravitySource source, SpaceVector2 relativePosition, SpaceVector2 relativeVelocity)
            => -relativePosition * (source.AngularFrequency * source.AngularFrequency) - relativeVelocity * source.DampingPerSecond;

        // 仅用于离线物理参考，不参与运行时太空物体运动。
        public static void AdvanceRelative(SpaceGravitySource source, SpaceVector2 position, SpaceVector2 velocity,
            double seconds, out SpaceVector2 nextPosition, out SpaceVector2 nextVelocity)
        {
            double beta = source.DampingPerSecond * .5d;
            double omegaSquared = source.AngularFrequency * source.AngularFrequency;
            double frequency = Math.Sqrt(Math.Max(0d, omegaSquared - beta * beta));
            double decay = Math.Exp(-beta * seconds);
            double cosine = Math.Cos(frequency * seconds);
            double sine = frequency > 1e-12d ? Math.Sin(frequency * seconds) / frequency : seconds;
            nextPosition = (position * (cosine + beta * sine) + velocity * sine) * decay;
            nextVelocity = (velocity * (cosine - beta * sine) - position * (omegaSquared * sine)) * decay;
        }

        public static bool TryEntryFraction(SpaceVector2 from, SpaceVector2 displacement, double radius, out double fraction)
        {
            fraction = 0d;
            SpaceVector2 end = from + displacement;
            if (Math.Min(from.X, end.X) > radius || Math.Max(from.X, end.X) < -radius ||
                Math.Min(from.Y, end.Y) > radius || Math.Max(from.Y, end.Y) < -radius) return false;
            double speedSquared = displacement.SqrMagnitude;
            double projection = SpaceVector2.Dot(from, displacement);
            if (speedSquared <= 1e-24d || projection >= 0d) return false;
            double distanceSquared = from.SqrMagnitude - radius * radius;
            double discriminant = projection * projection - speedSquared * distanceSquared;
            if (discriminant <= 0d) return false;
            double entry = (-projection - Math.Sqrt(discriminant)) / speedSquared;
            if (entry < -1e-10d || entry > 1d) return false;
            fraction = Math.Clamp(entry, 0d, 1d);
            return true;
        }

        public static double FindExitSeconds(SpaceGravitySource source, SpaceVector2 position, SpaceVector2 velocity, double seconds)
        {
            double low = 0d, high = seconds;
            for (int iteration = 0; iteration < 24; iteration++)
            {
                double middle = (low + high) * .5d;
                AdvanceRelative(source, position, velocity, middle, out SpaceVector2 sample, out _);
                if (sample.SqrMagnitude <= source.RadiusSquared) low = middle;
                else high = middle;
            }
            return high;
        }
        #endregion
    }
}
