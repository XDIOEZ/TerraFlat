using System;
using System.Collections.Generic;
using System.IO;

namespace FlatWorld.Spaceflight
{
    public sealed class UniverseSimulation
    {
        #region 静态星系布局与游戏时钟
        private readonly Dictionary<string, BodyState> bodies = new(StringComparer.Ordinal);
        public UniverseState State { get; }

        public UniverseSimulation(UniverseState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            if (!SpaceVector2.Finite(state.SimulationSeconds) || state.SimulationSeconds < 0d ||
                !Positive(state.FixedStepSeconds) || !Positive(state.MetersPerUnityUnit) ||
                state.Bodies == null || state.Bodies.Count == 0)
                throw new InvalidDataException("星系时间、步长、表现比例或星体列表无效");
            var planetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyState body in state.Bodies)
            {
                if (body == null || string.IsNullOrWhiteSpace(body.BodyId) || string.IsNullOrWhiteSpace(body.PlanetId) ||
                    !bodies.TryAdd(body.BodyId, body) || !planetIds.Add(body.PlanetId) ||
                    !Positive(body.RadiusMeters) || !NonNegative(body.SurfaceGravity) ||
                    !NonNegative(body.PlacementDistanceMeters) || !SpaceVector2.Finite(body.PlacementAngleRadians) ||
                    !SpaceVector2.Finite(body.RotationPeriodSeconds) || !SpaceVector2.Finite(body.RotationInitialRadians))
                    throw new InvalidDataException("星体身份、地址或静态位置参数无效");
            }
            var positions = new Dictionary<string, SpaceVector2>(StringComparer.Ordinal);
            foreach (BodyState body in state.Bodies)
                body.PositionMeters = EvaluatePosition(body, positions, new HashSet<string>(StringComparer.Ordinal));
        }

        private SpaceVector2 EvaluatePosition(BodyState body, Dictionary<string, SpaceVector2> positions, HashSet<string> path)
        {
            if (positions.TryGetValue(body.BodyId, out SpaceVector2 position)) return position;
            if (!path.Add(body.BodyId)) throw new InvalidDataException($"星体布局层级成环：{body.BodyId}");
            SpaceVector2 parentPosition = default;
            if (!string.IsNullOrWhiteSpace(body.ParentBodyId))
            {
                if (!TryGetBody(body.ParentBodyId, out BodyState parent))
                    throw new InvalidDataException($"星体布局父节点不存在：{body.BodyId}");
                parentPosition = EvaluatePosition(parent, positions, path);
            }
            path.Remove(body.BodyId);
            position = parentPosition + SpaceVector2.FromAngle(body.PlacementAngleRadians) * body.PlacementDistanceMeters;
            positions.Add(body.BodyId, position);
            return position;
        }

        public BodyState GetBody(string bodyId) => TryGetBody(bodyId, out BodyState body) ? body :
            throw new KeyNotFoundException($"星体不存在：{bodyId}");
        public bool TryGetBody(string bodyId, out BodyState body) => bodies.TryGetValue(bodyId ?? string.Empty, out body);
        public WorldAddress GetSurfaceAddress(string bodyId) => GetBody(bodyId).SurfaceAddress;

        public void AdvanceTime(double seconds)
        {
            if (!NonNegative(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            State.SimulationSeconds += seconds;
            if (!SpaceVector2.Finite(State.SimulationSeconds)) throw new InvalidOperationException("星系时间溢出");
            // 星体位置始终固定，时间只用于自转和地表环境。
        }

        private static bool Positive(double value) => SpaceVector2.Finite(value) && value > 0d;
        private static bool NonNegative(double value) => SpaceVector2.Finite(value) && value >= 0d;
        #endregion

        #region 无引力航行与星球接触
        // 只按游戏设定的速度移动，不计算天体吸引、轨道捕获或公转速度。
        public bool StepMotion(SpaceMotionState motion, SpaceVector2 acceleration, double seconds, out SurfaceContact contact)
            => StepMotionAtTime(motion, acceleration, Math.Max(0d, State.SimulationSeconds - seconds), seconds, out contact);

        public bool StepMotionAtTime(SpaceMotionState motion, SpaceVector2 acceleration, double startSeconds,
            double seconds, out SurfaceContact contact)
        {
            if (motion == null || !motion.PositionMeters.IsFinite || !motion.VelocityMetersPerSecond.IsFinite ||
                !acceleration.IsFinite || !NonNegative(motion.RadiusMeters) || !NonNegative(startSeconds) || !NonNegative(seconds))
                throw new ArgumentOutOfRangeException(nameof(motion));
            SpaceVector2 from = motion.PositionMeters;
            SpaceVector2 velocityBefore = motion.VelocityMetersPerSecond;
            SpaceVector2 next = from + velocityBefore * seconds + acceleration * (.5d * seconds * seconds);
            SpaceVector2 velocityAfter = velocityBefore + acceleration * seconds;
            if (TryFindContact(from, next, velocityBefore, velocityAfter, motion.RadiusMeters, startSeconds, seconds, out contact))
            {
                motion.PositionMeters = contact.PositionMeters;
                motion.VelocityMetersPerSecond = SpaceVector2.Lerp(velocityBefore, velocityAfter, contact.Fraction);
                return true;
            }
            motion.PositionMeters = next;
            motion.VelocityMetersPerSecond = velocityAfter;
            return false;
        }

        public bool TrySweepSurfaceContact(SpaceVector2 from, SpaceVector2 to, SpaceVector2 velocityBefore,
            SpaceVector2 velocityAfter, double radius, double startSeconds, double seconds, out SurfaceContact contact)
        {
            if (!from.IsFinite || !to.IsFinite || !velocityBefore.IsFinite || !velocityAfter.IsFinite ||
                !NonNegative(radius) || !NonNegative(startSeconds) || !NonNegative(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            return TryFindContact(from, to, velocityBefore, velocityAfter, radius, startSeconds, seconds, out contact);
        }

        private bool TryFindContact(SpaceVector2 from, SpaceVector2 to, SpaceVector2 velocityBefore,
            SpaceVector2 velocityAfter, double radius, double startSeconds, double seconds, out SurfaceContact contact)
        {
            contact = default;
            double earliest = double.MaxValue;
            foreach (BodyState body in State.Bodies)
            {
                if (!SweptCircle(from - body.PositionMeters, to - body.PositionMeters, body.RadiusMeters + radius, out double fraction) ||
                    fraction >= earliest) continue;
                earliest = fraction;
                SpaceVector2 hitPosition = SpaceVector2.Lerp(from, to, fraction);
                SpaceVector2 relative = hitPosition - body.PositionMeters;
                contact = new SurfaceContact
                {
                    BodyId = body.BodyId, PlanetId = body.PlanetId, Fraction = fraction,
                    SimulationSeconds = startSeconds + seconds * fraction,
                    PositionMeters = hitPosition, SurfaceAngleRadians = Math.Atan2(relative.Y, relative.X),
                    RelativeVelocityMetersPerSecond = SpaceVector2.Lerp(velocityBefore, velocityAfter, fraction)
                };
            }
            return contact.IsValid;
        }

        public static bool SweptCircle(SpaceVector2 fromRelative, SpaceVector2 toRelative, double radius, out double fraction)
        {
            fraction = 0d;
            double c = fromRelative.SqrMagnitude - radius * radius;
            if (c <= 0d) return true;
            SpaceVector2 displacement = toRelative - fromRelative;
            double a = displacement.SqrMagnitude;
            if (a <= 1e-20d) return false;
            double b = 2d * SpaceVector2.Dot(fromRelative, displacement);
            double discriminant = b * b - 4d * a * c;
            if (discriminant < 0d) return false;
            double entry = (-b - Math.Sqrt(discriminant)) / (2d * a);
            if (entry < 0d || entry > 1d) return false;
            fraction = entry;
            return true;
        }
        #endregion

        #region 发射入口与星球自转
        public SpaceMotionState CreateSpaceEntryState(string bodyId, double surfaceAngleRadians, double heightMeters,
            double outwardSpeedMetersPerSecond)
        {
            BodyState body = GetBody(bodyId);
            if (!SpaceVector2.Finite(surfaceAngleRadians) || !NonNegative(heightMeters) || !NonNegative(outwardSpeedMetersPerSecond))
                throw new ArgumentOutOfRangeException(nameof(heightMeters));
            SpaceVector2 radial = SpaceVector2.FromAngle(surfaceAngleRadians);
            return new SpaceMotionState
            {
                PositionMeters = body.PositionMeters + radial * (body.RadiusMeters + heightMeters),
                VelocityMetersPerSecond = radial * outwardSpeedMetersPerSecond
            };
        }

        public double GetRotationRadians(string bodyId)
        {
            BodyState body = GetBody(bodyId);
            return body.RotationInitialRadians + (Math.Abs(body.RotationPeriodSeconds) > 1e-12d ?
                State.SimulationSeconds * Math.PI * 2d / body.RotationPeriodSeconds : 0d);
        }
        #endregion
    }
}
