using System;

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
            if (seconds <= 0d || !SpaceVector2.Finite(seconds) || authority <= 0d || target == current) return current;
            if (target.Magnitude > settings.CruiseMaxSpeedMetersPerSecond)
                target = target.Normalized * settings.CruiseMaxSpeedMetersPerSecond;
            SpaceVector2 delta = target - current;
            double distance = delta.Magnitude;
            double change = ChangeRate(current, target, settings) * seconds * Math.Clamp(authority, 0d, 1d);
            return distance <= change ? target : current + delta.Normalized * change;
        }

        // 手动输入在已有速度上施加推力，松键及转船头都不自动刹掉平移惯性。
        public static SpaceVector2 ManualVelocity(SpaceVector2 current, SpaceVector2 steering,
            SpaceGameplaySettings settings, double seconds)
        {
            if (seconds <= 0d || steering.SqrMagnitude < 1e-12d) return current;
            if (steering.Magnitude > 1d) steering = steering.Normalized;
            double rate = SpaceVector2.Dot(steering, current) < 0d
                ? settings.CruiseBrakingMetersPerSecondSquared : settings.CruiseAccelerationMetersPerSecondSquared;
            SpaceVector2 next = current + steering * (rate * seconds);
            return next.Magnitude > settings.CruiseMaxSpeedMetersPerSecond
                ? next.Normalized * settings.CruiseMaxSpeedMetersPerSecond : next;
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

    }
}
