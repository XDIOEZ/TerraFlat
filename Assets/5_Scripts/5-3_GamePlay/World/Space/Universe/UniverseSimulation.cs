using System;
using System.Collections.Generic;
using System.IO;

namespace FlatWorld.Spaceflight
{
    public sealed class UniverseSimulation
    {
        #region 星体缓存与唯一时钟
        private readonly Dictionary<string, BodyState> bodies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> depths = new(StringComparer.Ordinal);
        public UniverseState State { get; }

        // 星系数据不依赖表现节点，场景卸载后仍由会话固定步推进。
        public UniverseSimulation(UniverseState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            if (!SpaceVector2.Finite(state.SimulationSeconds) || state.SimulationSeconds < 0d ||
                !SpaceVector2.Finite(state.FixedStepSeconds) || state.FixedStepSeconds <= 0d ||
                !SpaceVector2.Finite(state.MetersPerUnityUnit) || state.MetersPerUnityUnit <= 0d ||
                state.Bodies == null || state.Bodies.Count == 0)
                throw new InvalidDataException("星系时间、步长、表现比例或星体列表无效");
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyState body in state.Bodies)
            {
                if (body == null || string.IsNullOrWhiteSpace(body.BodyId) || string.IsNullOrWhiteSpace(body.PlanetId) ||
                    !bodies.TryAdd(body.BodyId, body) || !addresses.Add(body.PlanetId) ||
                    !Positive(body.RadiusMeters) || !NonNegative(body.SurfaceGravity) ||
                    !Positive(body.InfluenceRadiusMeters) || body.InfluenceRadiusMeters < body.RadiusMeters ||
                    !NonNegative(body.OrbitRadiusMeters) || !NonNegative(body.OrbitalPeriodSeconds) ||
                    !SpaceVector2.Finite(body.InitialPhaseRadians) || !SpaceVector2.Finite(body.RotationPeriodSeconds) ||
                    !SpaceVector2.Finite(body.RotationInitialRadians))
                    throw new InvalidDataException("星体身份、地址或物理参数无效");
            }
            foreach (BodyState body in state.Bodies)
                ResolveDepth(body, new HashSet<string>(StringComparer.Ordinal));
            UpdateBodyPositions();
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
            UpdateBodyPositions();
        }

        private void UpdateBodyPositions()
        {
            var cache = new Dictionary<string, Kinematics>(StringComparer.Ordinal);
            foreach (BodyState body in State.Bodies)
            {
                Kinematics current = Evaluate(body, State.SimulationSeconds, cache);
                body.PositionMeters = current.Position;
                body.VelocityMetersPerSecond = current.Velocity;
            }
        }

        private int ResolveDepth(BodyState body, HashSet<string> path)
        {
            if (depths.TryGetValue(body.BodyId, out int depth)) return depth;
            if (!path.Add(body.BodyId)) throw new InvalidDataException($"星体层级成环：{body.BodyId}");
            if (!string.IsNullOrWhiteSpace(body.ParentBodyId))
            {
                if (!TryGetBody(body.ParentBodyId, out BodyState parent) || !Positive(body.OrbitalPeriodSeconds))
                    throw new InvalidDataException($"星体公转中心或周期无效：{body.BodyId}");
                depth = ResolveDepth(parent, path) + 1;
            }
            path.Remove(body.BodyId);
            depths.Add(body.BodyId, depth);
            return depth;
        }

        private readonly struct Kinematics
        {
            public readonly SpaceVector2 Position;
            public readonly SpaceVector2 Velocity;
            public Kinematics(SpaceVector2 position, SpaceVector2 velocity) { Position = position; Velocity = velocity; }
        }

        private Kinematics Evaluate(BodyState body, double seconds, Dictionary<string, Kinematics> cache)
        {
            if (cache.TryGetValue(body.BodyId, out Kinematics saved)) return saved;
            Kinematics parent = string.IsNullOrEmpty(body.ParentBodyId) ? default : Evaluate(GetBody(body.ParentBodyId), seconds, cache);
            double angularSpeed = body.OrbitalPeriodSeconds > 0d ?
                Math.PI * 2d / body.OrbitalPeriodSeconds * (body.OrbitClockwise ? -1d : 1d) : 0d;
            SpaceVector2 radial = SpaceVector2.FromAngle(body.InitialPhaseRadians + seconds * angularSpeed) * body.OrbitRadiusMeters;
            var result = new Kinematics(parent.Position + radial,
                parent.Velocity + new SpaceVector2(-radial.Y, radial.X) * angularSpeed);
            cache.Add(body.BodyId, result);
            return result;
        }

        private static bool Positive(double value) => SpaceVector2.Finite(value) && value > 0d;
        private static bool NonNegative(double value) => SpaceVector2.Finite(value) && value >= 0d;
        #endregion

        #region 参考星体与引力
        public SpaceVector2 GetGravity(SpaceVector2 position, string previousReferenceId, out string referenceId)
        {
            return GetGravityAt(position, previousReferenceId, State.SimulationSeconds, out referenceId);
        }

        private SpaceVector2 GetGravityAt(SpaceVector2 position, string previousReferenceId, double time, out string referenceId)
        {
            if (!position.IsFinite) throw new ArgumentOutOfRangeException(nameof(position));
            var cache = new Dictionary<string, Kinematics>(StringComparer.Ordinal);
            BodyState selected = null;
            double bestFraction = double.MaxValue;
            int bestDepth = -1;
            foreach (BodyState body in State.Bodies)
            {
                double distance = (position - Evaluate(body, time, cache).Position).Magnitude;
                double fraction = distance / body.InfluenceRadiusMeters;
                if (fraction > 1d) continue;
                int depth = depths[body.BodyId];
                if (depth < bestDepth || depth == bestDepth && fraction >= bestFraction) continue;
                selected = body; bestDepth = depth; bestFraction = fraction;
            }
            if (TryGetBody(previousReferenceId, out BodyState previous) && selected != previous &&
                (selected == null || depths[previous.BodyId] >= bestDepth))
            {
                double oldFraction = (position - Evaluate(previous, time, cache).Position).Magnitude / previous.InfluenceRadiusMeters;
                if (oldFraction <= 1.05d && (selected == null || oldFraction <= bestFraction * 1.1d)) selected = previous;
            }
            referenceId = selected?.BodyId;
            if (selected == null) return default;
            SpaceVector2 radial = position - Evaluate(selected, time, cache).Position;
            double distanceSafe = Math.Max(selected.RadiusMeters, radial.Magnitude);
            return radial.Normalized * (-selected.GravityParameter / (distanceSafe * distanceSafe));
        }

        public void UpdateReference(OrbitState orbit)
        {
            if (orbit == null) throw new ArgumentNullException(nameof(orbit));
            GetGravity(orbit.PositionMeters, orbit.ReferenceBodyId, out string reference);
            orbit.ReferenceBodyId = reference;
            orbit.CapturedBodyId = null;
            orbit.RelativeAltitudeMeters = double.NaN;
            if (!TryGetBody(reference, out BodyState body)) return;
            SpaceVector2 radial = orbit.PositionMeters - body.PositionMeters;
            SpaceVector2 velocity = orbit.VelocityMetersPerSecond - body.VelocityMetersPerSecond;
            double distance = Math.Max(radial.Magnitude, body.RadiusMeters);
            orbit.RelativeAltitudeMeters = radial.Magnitude - body.RadiusMeters;
            if (velocity.SqrMagnitude * 0.5d - body.GravityParameter / distance < 0d)
                orbit.CapturedBodyId = body.BodyId;
        }

        // 位置与速度始终存宇宙坐标，切换参考系只更新派生关系。
        public SpaceVector2 ToReferencePosition(string bodyId, SpaceVector2 worldPosition) => worldPosition - GetBody(bodyId).PositionMeters;
        public SpaceVector2 ToReferenceVelocity(string bodyId, SpaceVector2 worldVelocity) => worldVelocity - GetBody(bodyId).VelocityMetersPerSecond;
        public SpaceVector2 FromReferencePosition(string bodyId, SpaceVector2 relativePosition) => GetBody(bodyId).PositionMeters + relativePosition;
        public SpaceVector2 FromReferenceVelocity(string bodyId, SpaceVector2 relativeVelocity) => GetBody(bodyId).VelocityMetersPerSecond + relativeVelocity;
        #endregion

        #region 运动积分与高速接触
        // 会话先推进星系时间，再用同一固定步推进所有运动主体。
        public bool StepOrbit(OrbitState orbit, SpaceVector2 thrustAcceleration, double seconds, out SurfaceContact contact)
            => StepOrbitAtTime(orbit, thrustAcceleration, Math.Max(0d, State.SimulationSeconds - seconds), seconds, out contact);

        // 碰撞后只重新积分剩余时间，不重复推进星系时钟或消耗燃料。
        public bool StepOrbitAtTime(OrbitState orbit, SpaceVector2 thrustAcceleration, double startSimulationSeconds,
            double seconds, out SurfaceContact contact)
        {
            if (orbit == null) throw new ArgumentNullException(nameof(orbit));
            if (!orbit.PositionMeters.IsFinite || !orbit.VelocityMetersPerSecond.IsFinite ||
                !thrustAcceleration.IsFinite || !NonNegative(startSimulationSeconds) || !NonNegative(seconds) || !NonNegative(orbit.RadiusMeters))
                throw new ArgumentOutOfRangeException(nameof(orbit));
            contact = default;
            double elapsed = 0d;
            double startTime = startSimulationSeconds;
            while (elapsed < seconds)
            {
                double dt = Math.Min(State.FixedStepSeconds, seconds - elapsed);
                SpaceVector2 from = orbit.PositionMeters;
                SpaceVector2 velocityBefore = orbit.VelocityMetersPerSecond;
                SpaceVector2 acceleration = GetGravityAt(from, orbit.ReferenceBodyId, startTime + elapsed, out string reference) + thrustAcceleration;
                SpaceVector2 next = from + velocityBefore * dt + acceleration * (0.5d * dt * dt);
                SpaceVector2 nextAcceleration = GetGravityAt(next, reference, startTime + elapsed + dt, out string nextReference) + thrustAcceleration;
                SpaceVector2 velocityAfter = velocityBefore + (acceleration + nextAcceleration) * (0.5d * dt);
                if (TryFindContact(from, next, velocityBefore, velocityAfter, orbit.RadiusMeters, startTime + elapsed, dt, out contact))
                {
                    orbit.PositionMeters = contact.PositionMeters;
                    orbit.VelocityMetersPerSecond = SpaceVector2.Lerp(velocityBefore, velocityAfter, contact.Fraction);
                    contact.Fraction = seconds > 0d ? (elapsed + dt * contact.Fraction) / seconds : 0d;
                    orbit.ReferenceBodyId = contact.BodyId;
                    UpdateReference(orbit);
                    return true;
                }
                orbit.PositionMeters = next;
                orbit.VelocityMetersPerSecond = velocityAfter;
                orbit.ReferenceBodyId = nextReference;
                elapsed += dt;
            }
            UpdateReference(orbit);
            return false;
        }

        // 位移修正和碰撞后的短轨迹仍按同一宇宙时段扫过所有星体。
        public bool TrySweepSurfaceContact(SpaceVector2 from, SpaceVector2 to, SpaceVector2 velocityBefore,
            SpaceVector2 velocityAfter, double radius, double startSimulationSeconds, double seconds, out SurfaceContact contact)
        {
            if (!from.IsFinite || !to.IsFinite || !velocityBefore.IsFinite || !velocityAfter.IsFinite ||
                !NonNegative(radius) || !NonNegative(startSimulationSeconds) || !NonNegative(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            return TryFindContact(from, to, velocityBefore, velocityAfter, radius, startSimulationSeconds, seconds, out contact);
        }

        private bool TryFindContact(SpaceVector2 from, SpaceVector2 to, SpaceVector2 velocityBefore,
            SpaceVector2 velocityAfter, double radius, double startTime, double seconds, out SurfaceContact contact)
        {
            contact = default;
            double first = double.MaxValue;
            var startCache = new Dictionary<string, Kinematics>(StringComparer.Ordinal);
            var endCache = new Dictionary<string, Kinematics>(StringComparer.Ordinal);
            foreach (BodyState body in State.Bodies)
            {
                Kinematics start = Evaluate(body, startTime, startCache);
                Kinematics end = Evaluate(body, startTime + seconds, endCache);
                if (!SweptCircle(from - start.Position, to - end.Position, body.RadiusMeters + radius, out double fraction) || fraction >= first)
                    continue;
                first = fraction;
                double hitTime = startTime + seconds * fraction;
                SpaceVector2 hitPosition = SpaceVector2.Lerp(from, to, fraction);
                SpaceVector2 relative = hitPosition - SpaceVector2.Lerp(start.Position, end.Position, fraction);
                contact = new SurfaceContact
                {
                    BodyId = body.BodyId, PlanetId = body.PlanetId, Fraction = fraction, SimulationSeconds = hitTime,
                    PositionMeters = hitPosition, SurfaceAngleRadians = Math.Atan2(relative.Y, relative.X),
                    RelativeVelocityMetersPerSecond = RelativeSurfaceVelocity(body.BodyId, hitPosition,
                        SpaceVector2.Lerp(velocityBefore, velocityAfter, fraction), true, hitTime)
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

        #region 发射与地表速度
        public OrbitState CreateLaunchState(string bodyId, double surfaceAngleRadians, double heightMeters,
            double launchSpeedMetersPerSecond, bool clockwise = false)
        {
            BodyState body = GetBody(bodyId);
            if (!SpaceVector2.Finite(surfaceAngleRadians) || !NonNegative(heightMeters) || !NonNegative(launchSpeedMetersPerSecond))
                throw new ArgumentOutOfRangeException(nameof(heightMeters));
            SpaceVector2 radial = SpaceVector2.FromAngle(surfaceAngleRadians);
            SpaceVector2 tangent = new SpaceVector2(-radial.Y, radial.X) * (clockwise ? -1d : 1d);
            var state = new OrbitState
            {
                PositionMeters = body.PositionMeters + radial * (body.RadiusMeters + heightMeters),
                VelocityMetersPerSecond = body.VelocityMetersPerSecond + tangent * launchSpeedMetersPerSecond,
                ReferenceBodyId = bodyId
            };
            UpdateReference(state);
            return state;
        }

        public SpaceVector2 RelativeSurfaceVelocity(string bodyId, SpaceVector2 position, SpaceVector2 velocity,
            bool includeRotation = true) => RelativeSurfaceVelocity(bodyId, position, velocity, includeRotation, State.SimulationSeconds);

        private SpaceVector2 RelativeSurfaceVelocity(string bodyId, SpaceVector2 position, SpaceVector2 velocity,
            bool includeRotation, double time)
        {
            BodyState body = GetBody(bodyId);
            Kinematics center = Evaluate(body, time, new Dictionary<string, Kinematics>(StringComparer.Ordinal));
            SpaceVector2 surfaceVelocity = center.Velocity;
            if (includeRotation && Math.Abs(body.RotationPeriodSeconds) > 1e-12d)
            {
                SpaceVector2 radius = (position - center.Position).Normalized * body.RadiusMeters;
                surfaceVelocity += new SpaceVector2(-radius.Y, radius.X) * (Math.PI * 2d / body.RotationPeriodSeconds);
            }
            return velocity - surfaceVelocity;
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
