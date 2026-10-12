using System;

namespace FlatWorld.Spaceflight
{
    public sealed class SpacePassengerContactState
    {
        #region 乘员真实扫掠状态
        public SpaceSectorPosition PreviousPosition, Position;
        public SpaceVector2 VelocityMetersPerSecond;
        public double RadiusMeters = .45d, MassKg = 80d;
        public bool Supported;
        #endregion
    }

    public static class SpacePassengerContacts
    {
        #region 无场景的乘员成对接触
        private const double SeparationMeters = .00001d;

        // 每对乘员只结算一次，过渡区和无人视图都使用真实区内坐标。
        public static bool ResolvePair(SpacePassengerContactState left, SpacePassengerContactState right, double seconds)
        {
            if (left == null || right == null || ReferenceEquals(left, right)) return false;
            Validate(left); Validate(right);
            if (!SpaceVector2.Finite(seconds) || seconds <= 0d) throw new ArgumentOutOfRangeException(nameof(seconds));
            SpaceSectorId reference = left.PreviousPosition.Sector;
            SpaceVector2 fromA = left.PreviousPosition.LocalMeters;
            SpaceVector2 fromB = SpaceSectorCoordinates.Relative(right.PreviousPosition, reference);
            SpaceVector2 toA = SpaceSectorCoordinates.Relative(left.Position, reference);
            SpaceVector2 toB = SpaceSectorCoordinates.Relative(right.Position, reference);
            double radius = left.RadiusMeters + right.RadiusMeters;
            if (Math.Min(fromA.X, toA.X) - radius > Math.Max(fromB.X, toB.X) ||
                Math.Min(fromB.X, toB.X) - radius > Math.Max(fromA.X, toA.X) ||
                Math.Min(fromA.Y, toA.Y) - radius > Math.Max(fromB.Y, toB.Y) ||
                Math.Min(fromB.Y, toB.Y) - radius > Math.Max(fromA.Y, toA.Y)) return false;

            SpaceVector2 deltaA = toA - fromA, deltaB = toB - fromB;
            if (!Sweep(fromB - fromA, deltaB - deltaA, radius, out double fraction)) return false;
            SpaceVector2 contactA = fromA + deltaA * fraction, contactB = fromB + deltaB * fraction;
            SpaceVector2 separation = contactB - contactA;
            double distance = separation.Magnitude;
            SpaceVector2 normal = distance > .00000001d ? separation / distance :
                (deltaB - deltaA).Magnitude > .00000001d ? (deltaB - deltaA) / (deltaB - deltaA).Magnitude : new(1d, 0d);
            double inverseA = left.Supported ? 0d : 1d / left.MassKg;
            double inverseB = right.Supported ? 0d : 1d / right.MassKg;
            bool bothSupported = left.Supported && right.Supported;
            if (bothSupported) { inverseA = 1d / left.MassKg; inverseB = 1d / right.MassKg; }
            double totalInverse = inverseA + inverseB;
            double penetration = Math.Max(0d, radius - distance) + SeparationMeters;
            contactA -= normal * (penetration * inverseA / totalInverse);
            contactB += normal * (penetration * inverseB / totalInverse);

            if (bothSupported)
            {
                SpaceVector2 remainingA = deltaA * (1d - fraction), remainingB = deltaB * (1d - fraction);
                double closingDistance = SpaceVector2.Dot(remainingB - remainingA, normal);
                if (closingDistance < 0d)
                {
                    remainingA += normal * (closingDistance * inverseA / totalInverse);
                    remainingB -= normal * (closingDistance * inverseB / totalInverse);
                }
                toA = contactA + remainingA; toB = contactB + remainingB;
            }
            else
            {
                SpaceVector2 velocityA = left.Supported ? deltaA / seconds : left.VelocityMetersPerSecond;
                SpaceVector2 velocityB = right.Supported ? deltaB / seconds : right.VelocityMetersPerSecond;
                double closingSpeed = SpaceVector2.Dot(velocityB - velocityA, normal);
                if (closingSpeed < 0d)
                {
                    double impulse = -closingSpeed / totalInverse;
                    velocityA -= normal * (impulse * inverseA); velocityB += normal * (impulse * inverseB);
                }
                double remaining = seconds * (1d - fraction);
                if (!left.Supported) { toA = contactA + velocityA * remaining; left.VelocityMetersPerSecond = velocityA; }
                if (!right.Supported) { toB = contactB + velocityB * remaining; right.VelocityMetersPerSecond = velocityB; }
            }
            if (!left.Supported || bothSupported) left.Position = SpaceSectorCoordinates.Normalize(reference, toA);
            if (!right.Supported || bothSupported) right.Position = SpaceSectorCoordinates.Normalize(reference, toB);
            return true;
        }

        private static bool Sweep(SpaceVector2 offset, SpaceVector2 displacement, double radius, out double fraction)
        {
            fraction = 0d;
            double distance = offset.Magnitude;
            if (distance < radius) return true;
            if (SpaceVector2.Dot(offset, displacement) >= 0d) return false;
            double scale = Math.Max(radius, Math.Max(Math.Max(Math.Abs(offset.X), Math.Abs(offset.Y)),
                Math.Max(Math.Abs(displacement.X), Math.Abs(displacement.Y))));
            SpaceVector2 origin = offset / scale, delta = displacement / scale;
            double a = delta.SqrMagnitude;
            if (a <= 0d) return false;
            double b = 2d * SpaceVector2.Dot(origin, delta), c = origin.SqrMagnitude - radius / scale * (radius / scale);
            double discriminant = b * b - 4d * a * c;
            if (discriminant < 0d) return false;
            // 用等价根式避免高速扫掠中相近的大数相减。
            double divisor = -b + Math.Sqrt(discriminant);
            fraction = divisor > 0d ? 2d * c / divisor : 0d;
            return fraction >= 0d && fraction <= 1d;
        }

        private static void Validate(SpacePassengerContactState state)
        {
            if (!state.PreviousPosition.IsFinite || !state.Position.IsFinite || !state.VelocityMetersPerSecond.IsFinite ||
                !SpaceVector2.Finite(state.RadiusMeters) || state.RadiusMeters <= 0d ||
                !SpaceVector2.Finite(state.MassKg) || state.MassKg <= 0d)
                throw new ArgumentOutOfRangeException(nameof(state), "乘员接触状态无效。");
        }
        #endregion
    }
}
