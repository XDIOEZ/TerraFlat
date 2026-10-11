using System;
using System.Collections.Generic;
using System.IO;

namespace FlatWorld.Spaceflight
{
    public sealed class UniverseSimulation
    {
        #region 椭圆轨道与游戏时钟
        private readonly Dictionary<string, BodyState> bodies = new(StringComparer.Ordinal);
        private readonly List<BodyState> orbitOrder = new();
        private readonly Dictionary<string, SpaceVector2> previousPositions = new(StringComparer.Ordinal);
        private double previousSimulationSeconds;
        public UniverseState State { get; }

        public UniverseSimulation(UniverseState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            if (!SpaceVector2.Finite(state.SimulationSeconds) || state.SimulationSeconds < 0d ||
                !Positive(state.FixedStepSeconds) || !Positive(state.MetersPerUnityUnit) || !Positive(state.OrbitTimeScale) ||
                state.Bodies == null || state.Bodies.Count == 0)
                throw new InvalidDataException("星系时间、步长、表现比例或星体列表无效");
            var planetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyState body in state.Bodies)
            {
                if (body == null || string.IsNullOrWhiteSpace(body.BodyId) || string.IsNullOrWhiteSpace(body.PlanetId) ||
                    !bodies.TryAdd(body.BodyId, body) || !planetIds.Add(body.PlanetId) ||
                    !Positive(body.RadiusMeters) || !NonNegative(body.SurfaceGravity) ||
                    !NonNegative(body.PlacementDistanceMeters) || !SpaceVector2.Finite(body.PlacementAngleRadians) ||
                    !NonNegative(body.OrbitalPeriodSeconds) || !NonNegative(body.OrbitalEccentricity) || body.OrbitalEccentricity >= 1d ||
                    !SpaceVector2.Finite(body.OrbitalPeriapsisRadians) ||
                    !SpaceVector2.Finite(body.RotationPeriodSeconds) || !SpaceVector2.Finite(body.RotationInitialRadians))
                    throw new InvalidDataException("星体身份、地址或轨道参数无效");
            }
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var path = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyState body in state.Bodies)
                BuildOrbitOrder(body, visited, path);
            UpdateBodyMotions();
            previousSimulationSeconds = State.SimulationSeconds;
            foreach (BodyState body in orbitOrder) previousPositions.Add(body.BodyId, body.PositionMeters);
        }

        private void BuildOrbitOrder(BodyState body, HashSet<string> visited, HashSet<string> path)
        {
            if (visited.Contains(body.BodyId)) return;
            if (!path.Add(body.BodyId)) throw new InvalidDataException($"星体布局层级成环：{body.BodyId}");
            if (!string.IsNullOrWhiteSpace(body.ParentBodyId))
            {
                if (!TryGetBody(body.ParentBodyId, out BodyState parent))
                    throw new InvalidDataException($"星体布局父节点不存在：{body.BodyId}");
                BuildOrbitOrder(parent, visited, path);
            }
            path.Remove(body.BodyId);
            visited.Add(body.BodyId);
            orbitOrder.Add(body);
        }

        private void UpdateBodyMotions()
        {
            // 父星体先更新，每步只求少量轨道参数，不分配集合或生成贴图。
            foreach (BodyState body in orbitOrder)
            {
                EvaluateRelativeMotion(body, State.SimulationSeconds, out SpaceVector2 position, out SpaceVector2 velocity);
                if (!string.IsNullOrWhiteSpace(body.ParentBodyId))
                {
                    BodyState parent = bodies[body.ParentBodyId];
                    position += parent.PositionMeters;
                    velocity += parent.VelocityMetersPerSecond;
                }
                body.PositionMeters = position;
                body.VelocityMetersPerSecond = velocity;
            }
        }

        private void EvaluateRelativeMotion(BodyState body, double simulationSeconds,
            out SpaceVector2 position, out SpaceVector2 velocity)
        {
            if (body.OrbitalPeriodSeconds <= 0d)
            {
                position = SpaceVector2.FromAngle(body.PlacementAngleRadians) * body.PlacementDistanceMeters;
                velocity = default;
                return;
            }
            double rate = Math.PI * 2d * State.OrbitTimeScale / body.OrbitalPeriodSeconds;
            double meanAnomaly = Math.IEEERemainder(body.PlacementAngleRadians + simulationSeconds * rate, Math.PI * 2d);
            double eccentricity = body.OrbitalEccentricity;
            double eccentricAnomaly = eccentricity < .8d ? meanAnomaly : (meanAnomaly < 0d ? -Math.PI : Math.PI);
            for (int iteration = 0; iteration < 16; iteration++)
            {
                double correction = (eccentricAnomaly - eccentricity * Math.Sin(eccentricAnomaly) - meanAnomaly) /
                    (1d - eccentricity * Math.Cos(eccentricAnomaly));
                eccentricAnomaly -= correction;
                if (Math.Abs(correction) <= 1e-12d) break;
            }
            double cosine = Math.Cos(eccentricAnomaly), sine = Math.Sin(eccentricAnomaly);
            double minorRatio = Math.Sqrt(1d - eccentricity * eccentricity);
            double axis = body.PlacementDistanceMeters;
            SpaceVector2 periapsis = SpaceVector2.FromAngle(body.OrbitalPeriapsisRadians);
            SpaceVector2 tangent = new(-periapsis.Y, periapsis.X);
            position = periapsis * (axis * (cosine - eccentricity)) + tangent * (axis * minorRatio * sine);
            double anomalyRate = rate / (1d - eccentricity * cosine);
            velocity = periapsis * (-axis * sine * anomalyRate) + tangent * (axis * minorRatio * cosine * anomalyRate);
        }

        public SpaceVector2 GetPositionAtTime(string bodyId, double simulationSeconds)
        {
            if (!NonNegative(simulationSeconds)) throw new ArgumentOutOfRangeException(nameof(simulationSeconds));
            BodyState body = GetBody(bodyId);
            // 正常固定步的乘员和散落物扫掠复用两端位置，碰撞子步才重新求轨道。
            if (Math.Abs(simulationSeconds - State.SimulationSeconds) <= 1e-9d) return body.PositionMeters;
            if (Math.Abs(simulationSeconds - previousSimulationSeconds) <= 1e-9d) return previousPositions[bodyId];
            EvaluateRelativeMotion(body, simulationSeconds, out SpaceVector2 position, out _);
            return string.IsNullOrWhiteSpace(body.ParentBodyId) ? position :
                GetPositionAtTime(body.ParentBodyId, simulationSeconds) + position;
        }

        public BodyState GetBody(string bodyId) => TryGetBody(bodyId, out BodyState body) ? body :
            throw new KeyNotFoundException($"星体不存在：{bodyId}");
        public bool TryGetBody(string bodyId, out BodyState body) => bodies.TryGetValue(bodyId ?? string.Empty, out body);
        public WorldAddress GetSurfaceAddress(string bodyId) => GetBody(bodyId).SurfaceAddress;

        public void AdvanceTime(double seconds)
        {
            if (!NonNegative(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            previousSimulationSeconds = State.SimulationSeconds;
            foreach (BodyState body in orbitOrder) previousPositions[body.BodyId] = body.PositionMeters;
            State.SimulationSeconds += seconds;
            if (!SpaceVector2.Finite(State.SimulationSeconds)) throw new InvalidOperationException("星系时间溢出");
            UpdateBodyMotions();
        }

        private static bool Positive(double value) => SpaceVector2.Finite(value) && value > 0d;
        private static bool NonNegative(double value) => SpaceVector2.Finite(value) && value >= 0d;
        #endregion

        #region 无引力航行与星球接触
        // 飞船在星球画面上方自由航行，只有显式降落指令才切换到地表。
        public void StepFreeMotion(SpaceMotionState motion, SpaceVector2 acceleration, double seconds)
        {
            ValidateMotion(motion, acceleration, seconds);
            motion.PositionMeters += motion.VelocityMetersPerSecond * seconds + acceleration * (.5d * seconds * seconds);
            motion.VelocityMetersPerSecond += acceleration * seconds;
        }

        private static void ValidateMotion(SpaceMotionState motion, SpaceVector2 acceleration, double seconds)
        {
            if (motion == null || !motion.PositionMeters.IsFinite || !motion.VelocityMetersPerSecond.IsFinite ||
                !acceleration.IsFinite || !NonNegative(motion.RadiusMeters) || !NonNegative(seconds))
                throw new ArgumentOutOfRangeException(nameof(motion));
        }

        // 独立乘员与散落物沿自身惯性移动，接触检测只减去星体本步的移动。
        public bool StepMotion(SpaceMotionState motion, SpaceVector2 acceleration, double seconds, out SurfaceContact contact)
            => StepMotionAtTime(motion, acceleration, Math.Max(0d, State.SimulationSeconds - seconds), seconds, out contact);

        public bool StepMotionAtTime(SpaceMotionState motion, SpaceVector2 acceleration, double startSeconds,
            double seconds, out SurfaceContact contact)
        {
            ValidateMotion(motion, acceleration, seconds);
            if (!NonNegative(startSeconds)) throw new ArgumentOutOfRangeException(nameof(startSeconds));
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
                SpaceVector2 bodyStart = GetPositionAtTime(body.BodyId, startSeconds);
                SpaceVector2 bodyEnd = GetPositionAtTime(body.BodyId, startSeconds + seconds);
                if (!SweptCircle(from - bodyStart, to - bodyEnd, body.RadiusMeters + radius, out double fraction) ||
                    fraction >= earliest) continue;
                earliest = fraction;
                SpaceVector2 hitPosition = SpaceVector2.Lerp(from, to, fraction);
                SpaceVector2 relative = hitPosition - SpaceVector2.Lerp(bodyStart, bodyEnd, fraction);
                contact = new SurfaceContact
                {
                    BodyId = body.BodyId, PlanetId = body.PlanetId, Fraction = fraction,
                    SimulationSeconds = startSeconds + seconds * fraction,
                    PositionMeters = hitPosition, SurfaceAngleRadians = Math.Atan2(relative.Y, relative.X),
                    RelativeVelocityMetersPerSecond = SpaceVector2.Lerp(velocityBefore, velocityAfter, fraction) -
                        (seconds > 0d ? (bodyEnd - bodyStart) / seconds : body.VelocityMetersPerSecond)
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
                VelocityMetersPerSecond = body.VelocityMetersPerSecond + radial * outwardSpeedMetersPerSecond
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
