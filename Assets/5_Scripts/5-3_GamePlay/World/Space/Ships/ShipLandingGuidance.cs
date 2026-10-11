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
            BodyId = body.BodyId, PlanetId = body.PlanetId, Fraction = 1d,
            SimulationSeconds = simulationSeconds, PositionMeters = position,
            SurfaceAngleRadians = Math.Atan2(position.Y - body.PositionMeters.Y, position.X - body.PositionMeters.X),
            RelativeVelocityMetersPerSecond = velocity
        };

        #endregion
    }
}
