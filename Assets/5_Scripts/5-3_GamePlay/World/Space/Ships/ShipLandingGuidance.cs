using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public static class ShipLandingGuidance
    {
        #region 控制台降落范围与实际姿态
        public static BodyState FindNearbyBody(SpaceVector2 position, double shipRadius, IReadOnlyList<BodyState> bodies)
        {
            BodyState nearest = null;
            double closest = double.PositiveInfinity;
            foreach (BodyState body in bodies)
            {
                double altitude = (position - body.PositionMeters).Magnitude - body.RadiusMeters - shipRadius;
                if (altitude > Math.Max(100d, body.RadiusMeters * .25d) || altitude >= closest) continue;
                closest = altitude;
                nearest = body;
            }
            return nearest;
        }

        public static SurfaceContact CreateDescentContact(BodyState body, SpaceVector2 position,
            SpaceVector2 velocity, double simulationSeconds) => new()
        {
            Sector = body.Sector, BodyId = body.BodyId, PlanetId = body.PlanetId, Fraction = 1d,
            SimulationSeconds = simulationSeconds, PositionMeters = position,
            SurfaceAngleRadians = Math.Atan2(position.Y - body.PositionMeters.Y, position.X - body.PositionMeters.X),
            RelativeVelocityMetersPerSecond = velocity - body.VelocityMetersPerSecond
        };

        #endregion

        #region 有真实推力的缓慢起降
        public static double TakeoffSpeed(SpaceGameplaySettings settings)
            => Math.Max(settings.MinimumSpaceExitSpeedMetersPerSecond, settings.TakeoffSpeedMetersPerSecond);

        // 起飞只等待速度和时间，不以高度或星球大小决定何时结束。
        public static bool CanCompleteTakeoff(double verticalSpeed, double elapsedSeconds, SpaceGameplaySettings settings)
            => verticalSpeed >= settings.MinimumSpaceExitSpeedMetersPerSecond && elapsedSeconds >= settings.TakeoffDurationSeconds;

        public static double SurfaceVisualRise(double height) => Math.Max(0d, height) * .04d;

        public static double LandingSpeed(double elapsedSeconds, double height, double seconds, SpaceGameplaySettings settings)
            => LandingSpeed(elapsedSeconds, height, seconds, settings, settings.EarthGravity);

        public static double ResolvePoweredLandingDurationSeconds(double gravity, SpaceGameplaySettings settings)
        {
            // 三次平滑轨迹的最大向下加速度不能超过当地重力，引擎只承担制动与悬停。
            double gravityLimitedDuration = gravity > 0d
                ? Math.Sqrt(6d * settings.SurfaceEntryHeightMeters / gravity) : settings.LandingDurationSeconds;
            return Math.Max(settings.LandingDurationSeconds, gravityLimitedDuration);
        }

        public static double LandingSpeed(double elapsedSeconds, double height, double seconds,
            SpaceGameplaySettings settings, double gravity)
        {
            double duration = ResolvePoweredLandingDurationSeconds(gravity, settings);
            double progress = Math.Clamp((elapsedSeconds + seconds) / duration, 0d, 1d);
            double targetHeight = settings.SurfaceEntryHeightMeters * (1d - progress * progress * (3d - 2d * progress));
            return Math.Min(0d, (targetHeight - height) / seconds);
        }

        public static double RequiredVerticalThrust(double mass, double gravity, double currentSpeed, double desiredSpeed, double seconds)
            => Math.Max(0d, (gravity + (desiredSpeed - currentSpeed) / seconds) * mass);

        public static SpaceMotionState CreateParkedSpaceEntry(UniverseSimulation universe, BodyState body,
            double surfaceAngle, double shipRadius, SpaceGameplaySettings settings)
            => universe.CreateSpaceEntryState(body.BodyId, surfaceAngle, shipRadius + settings.SpaceEntryClearanceMeters, 0d);
        #endregion
    }
}
