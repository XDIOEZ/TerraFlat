using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public static class ShipCruiseGuidance
    {
        #region 巡航速度与到点制动
        public static double ChangeRate(SpaceVector2 current, SpaceVector2 target, SpaceGameplaySettings settings) =>
            SpaceVector2.Dot(target - current, current) < 0d
                ? settings.CruiseBrakingMetersPerSecondSquared : settings.CruiseAccelerationMetersPerSecondSquared;

        public static SpaceVector2 StepVelocity(SpaceVector2 current, SpaceVector2 target,
            SpaceGameplaySettings settings, double seconds, double authority = 1d)
        {
            if (seconds <= 0d || !SpaceVector2.Finite(seconds)) return current;
            SpaceVector2 delta = target - current;
            double distance = delta.Magnitude;
            double change = ChangeRate(current, target, settings) * seconds * Math.Clamp(authority, 0d, 1d);
            SpaceVector2 velocity = distance <= change ? target : current + delta.Normalized * change;
            double speed = velocity.Magnitude;
            return speed > settings.CruiseMaxSpeedMetersPerSecond
                ? velocity * (settings.CruiseMaxSpeedMetersPerSecond / speed) : velocity;
        }

        public static SpaceVector2 ArrivalVelocity(SpaceVector2 error, SpaceGameplaySettings settings, double seconds)
        {
            double distance = error.Magnitude;
            if (distance <= settings.NavigationArrivalDistanceMeters * .5d || seconds <= 0d) return default;
            double remaining = distance - settings.NavigationArrivalDistanceMeters * .5d;
            double brakingStep = settings.CruiseBrakingMetersPerSecondSquared * seconds;
            // 提前留出一个固定步的制动距离，避免临近航点反复冲过头。
            double speed = Math.Sqrt(brakingStep * brakingStep + 2d * settings.CruiseBrakingMetersPerSecondSquared * remaining) - brakingStep;
            speed = Math.Min(settings.CruiseMaxSpeedMetersPerSecond, Math.Min(speed, remaining / seconds));
            return error * (speed / distance);
        }

        public static double BrakingDistance(double speed, SpaceGameplaySettings settings, double seconds) =>
            speed * speed / (2d * settings.CruiseBrakingMetersPerSecondSquared) + speed * Math.Max(0d, seconds);

        public static double TurnToward(double heading, SpaceVector2 direction, SpaceGameplaySettings settings, double seconds)
        {
            if (direction.SqrMagnitude < 1e-12d || seconds <= 0d) return 0d;
            double error = Math.IEEERemainder(Math.Atan2(direction.Y, direction.X) - Math.PI * .5d - heading, Math.PI * 2d);
            return Math.Clamp(error / (settings.CruiseTurnDegreesPerSecond * Math.PI / 180d * seconds), -1d, 1d);
        }
        #endregion

        #region 星体外侧安全航点
        // 自动导航只到达星体外侧，手动驾驶仍可进入原有降落流程。
        public static SpaceVector2 SafeTarget(SpaceVector2 origin, SpaceVector2 target,
            IReadOnlyList<BodyState> bodies, double shipRadius, double clearance, out BodyState blocker)
        {
            blocker = null;
            SpaceVector2 route = target - origin;
            double length = route.Magnitude;
            if (length < 1e-9d) return origin;
            SpaceVector2 direction = route / length;
            double nearest = length;
            for (int i = 0; i < bodies.Count; i++)
            {
                BodyState body = bodies[i];
                double radius = body.RadiusMeters + Math.Max(0d, shipRadius) + Math.Max(0d, clearance);
                SpaceVector2 offset = origin - body.PositionMeters;
                double along = SpaceVector2.Dot(offset, direction);
                double outside = offset.SqrMagnitude - radius * radius;
                double distance;
                if (outside <= 0d)
                {
                    if (along >= 0d) continue;
                    distance = 0d;
                }
                else
                {
                    double discriminant = along * along - outside;
                    if (discriminant < 0d || along >= 0d) continue;
                    distance = -along - Math.Sqrt(discriminant);
                }
                if (distance < 0d || distance > nearest) continue;
                nearest = Math.Max(0d, distance - .01d);
                blocker = body;
            }
            return origin + direction * nearest;
        }
        #endregion
    }
}
