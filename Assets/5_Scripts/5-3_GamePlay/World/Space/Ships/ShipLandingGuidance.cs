using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public static class ShipLandingGuidance
    {
        #region 控制台降落范围与星体外侧接触
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

        // 没有降落指令时仅挡住朝内的运动，保留切向滑行与向外离开的速度。
        public static void ResolveExteriorContact(BodyState body, double shipRadius, SurfaceContact contact,
            ref SpaceVector2 position, ref SpaceVector2 velocity)
        {
            SpaceVector2 normal = (position - body.PositionMeters).Normalized;
            if (normal.SqrMagnitude < 1e-12d) normal = SpaceVector2.FromAngle(contact.SurfaceAngleRadians);
            double minimumDistance = body.RadiusMeters + shipRadius + .01d;
            if ((position - body.PositionMeters).Magnitude < minimumDistance)
                position = body.PositionMeters + normal * minimumDistance;
            double inward = SpaceVector2.Dot(velocity, normal);
            if (inward < 0d) velocity -= normal * inward;
        }
        #endregion
    }
}
