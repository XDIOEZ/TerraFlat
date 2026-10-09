using System;
using System.Collections.Generic;
using FlatWorld.Networking;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 权威接触步与物理作用域
        private const double ShipContactSkin = .001d;
        private const int MaximumContactSweepIterations = 128;
        private readonly Dictionary<string, ShipContactPose> shipContactStarts = new(StringComparer.Ordinal);
        private double shipContactTime = double.NaN;

        private readonly struct ShipContactPose
        {
            public readonly SpaceVector2 Position;
            public readonly SpaceVector2 Velocity;
            public readonly double Angle;
            public readonly double AngularVelocity;
            public readonly string Realm;
            public ShipContactPose(SpaceVector2 position, double angle, string realm, SpaceVector2 velocity = default, double angularVelocity = 0d)
            { Position = position; Angle = angle; Realm = realm; Velocity = velocity; AngularVelocity = angularVelocity; }
        }
        private sealed class ShipContactBody
        {
            public ShipAssemblyState Assembly;
            public string Realm;
            public double Height, HalfHeight;
            public bool Dynamic;
            public bool SurfaceStopped;
            public SurfaceContact? SurfaceContact;
            public SpaceVector2 PreviousPosition, PreviousVelocity;
            public double PreviousAngle, PreviousAngularVelocity, AngularAcceleration;
            public double StartFraction, EndFraction = 1d;
            public readonly List<ShipState> Members = new();
            public readonly List<ShipContactShape> Shapes = new();
            public SpaceVector2 Position => new(Assembly.PositionX, Assembly.PositionY);
            public double InverseMass => Dynamic && Assembly.MassKg > 0d ? 1d / Assembly.MassKg : 0d;
            public double InverseInertia => Dynamic && Assembly.InertiaKgM2 > 0d ? 1d / Assembly.InertiaKgM2 : 0d;
        }
        private sealed class ShipContactShape
        {
            public ShipState Ship;
            public ShipPieceState Piece;
            public ShipContactBody Owner;
            public ShipContactPose Previous;
            public double RadiusFromCenter;
        }
        private readonly struct ShipContactBox
        {
            public readonly SpaceVector2 Center, AxisX, AxisY;
            public readonly double HalfX, HalfY;
            public ShipContactBox(SpaceVector2 center, double angle, double halfX, double halfY)
            { Center = center; AxisX = SpaceVector2.FromAngle(angle); AxisY = new(-AxisX.Y, AxisX.X); HalfX = halfX; HalfY = halfY; }
            public double ProjectedRadius(SpaceVector2 axis)
                => HalfX * Math.Abs(SpaceVector2.Dot(AxisX, axis)) + HalfY * Math.Abs(SpaceVector2.Dot(AxisY, axis));
        }
        private readonly struct ShipContactBounds
        {
            public readonly double MinX, MinY, MaxX, MaxY;
            public ShipContactBounds(double minX, double minY, double maxX, double maxY)
            { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
            public bool Overlaps(ShipContactBounds other)
                => MinX <= other.MaxX && MaxX >= other.MinX && MinY <= other.MaxY && MaxY >= other.MinY;
            public ShipContactBounds Union(ShipContactBounds other)
                => new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));
        }
        private struct ShipContactHit
        {
            public ShipContactBody Body;
            public ShipContactShape ShapeA, ShapeB;
            public double Fraction, Penetration;
            public SpaceVector2 Normal, Point;
        }

        // 积分前记录真实起点，切换物理世界后不把两套坐标之间的迁移当成扫掠。
        public void CaptureShipContactPoses()
        {
            shipContactStarts.Clear();
            ResetSurfaceContactFrame();
            shipContactTime = Universe?.State.SimulationSeconds ?? double.NaN;
            if (State == null) return;
            foreach (ShipState ship in State.Ships)
                if (TryGetShipContactRealm(ship, out string realm, out _, out _))
                    shipContactStarts[ship.ShipId] = new ShipContactPose(new SpaceVector2(ship.PositionX, ship.PositionY), ship.AngleRadians,
                        realm, new SpaceVector2(ship.VelocityX, ship.VelocityY), ship.AngularVelocityRadiansPerSecond);
        }
        private bool TryGetShipContactRealm(ShipState ship, out string realm, out double height, out bool dynamic)
        {
            realm = null; height = 0d; dynamic = false;
            ShipFlightState flight = ship == null ? null : GetFlight(ship.ShipId);
            if (flight == null) return false;
            if (flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending)
            { realm = "orbit"; dynamic = true; return true; }
            if (string.IsNullOrWhiteSpace(flight.SurfaceWorldKey)) return false;
            realm = "surface:" + flight.SurfaceWorldKey;
            height = Math.Max(0d, flight.HeightMeters);
            dynamic = flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending;
            return true;
        }
        private ShipContactPose GetShipContactStart(ShipState ship, string realm, double seconds)
        {
            SpaceVector2 current = new(ship.PositionX, ship.PositionY);
            if (shipContactTime == Universe?.State.SimulationSeconds && shipContactStarts.TryGetValue(ship.ShipId, out ShipContactPose previous))
                return previous.Realm == realm ? previous : new ShipContactPose(current, ship.AngleRadians, realm,
                    new SpaceVector2(ship.VelocityX, ship.VelocityY), ship.AngularVelocityRadiansPerSecond);
            return new ShipContactPose(current - new SpaceVector2(ship.VelocityX, ship.VelocityY) * seconds,
                ship.AngleRadians - ship.AngularVelocityRadiansPerSecond * seconds, realm,
                new SpaceVector2(ship.VelocityX, ship.VelocityY), ship.AngularVelocityRadiansPerSecond);
        }
        private List<ShipContactBody> BuildShipContactBodies(double seconds)
        {
            var bodies = new List<ShipContactBody>();
            foreach (ShipAssemblyState assembly in ShipDockingService.RebuildAssemblies(State.Ships, State.Docking))
            {
                ShipState primary = GetShip(assembly.MemberShipIds[0]);
                if (!TryGetShipContactRealm(primary, out string realm, out double height, out bool dynamic)) continue;
                var body = new ShipContactBody { Assembly = assembly, Realm = realm, Height = height, Dynamic = dynamic };
                QueuedSurfaceContact queued = realm == "orbit" ? FindQueuedSurfaceContact(assembly) : null;
                if (queued != null)
                { body.EndFraction = queued.Contact.Fraction; body.SurfaceContact = queued.Contact; body.AngularAcceleration = queued.AngularAcceleration; }
                double totalMass = 0d;
                foreach (string id in assembly.MemberShipIds)
                {
                    ShipState ship = GetShip(id);
                    if (!TryGetShipContactRealm(ship, out string memberRealm, out _, out _) || memberRealm != realm) continue;
                    body.Members.Add(ship);
                    ShipContactPose previous = GetShipContactStart(ship, realm, seconds);
                    body.PreviousPosition += previous.Position * ship.MassKg;
                    body.PreviousVelocity += previous.Velocity * ship.MassKg; totalMass += ship.MassKg;
                    if (id == primary.ShipId)
                    { body.PreviousAngle = previous.Angle; body.PreviousAngularVelocity = previous.AngularVelocity; }
                    body.HalfHeight = Math.Max(body.HalfHeight, Math.Max(.01d, ship.CabinHeightMeters * .5d));
                    foreach (ShipPieceState piece in ship.Pieces)
                    {
                        if (!piece.IsAlive || piece.Kind is ShipPieceKind.Wire or ShipPieceKind.Fiber) continue;
                        int width = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height);
                        int heightCells = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width);
                        double dx = (piece.CellX + Math.Max(1, width) * .5d) * ship.CellSizeMeters - ship.CenterOfMassLocalX;
                        double dy = (piece.CellY + Math.Max(1, heightCells) * .5d) * ship.CellSizeMeters - ship.CenterOfMassLocalY;
                        double radius = Math.Sqrt(dx * dx + dy * dy) + .5d * ship.CellSizeMeters * Math.Sqrt((double)width * width + (double)heightCells * heightCells);
                        body.Shapes.Add(new ShipContactShape { Ship = ship, Piece = piece, Owner = body, Previous = previous, RadiusFromCenter = radius });
                    }
                }
                body.PreviousPosition = totalMass > 0d ? body.PreviousPosition / totalMass : GetShipContactStart(primary, realm, seconds).Position;
                body.PreviousVelocity = totalMass > 0d ? body.PreviousVelocity / totalMass : GetShipContactStart(primary, realm, seconds).Velocity;
                if (queued == null) body.AngularAcceleration = (assembly.AngularVelocityRadiansPerSecond - body.PreviousAngularVelocity) / seconds;
                if (body.Shapes.Count > 0) bodies.Add(body);
            }
            return bodies;
        }
        private static bool ContactRealmsMatch(ShipContactBody a, ShipContactBody b)
            => a.Realm == b.Realm && (a.Realm == "orbit" || Math.Abs(a.Height - b.Height) <= a.HalfHeight + b.HalfHeight);
        #endregion

        #region 船体之间的真实部件接触
        public void StepShipContacts(double seconds)
        {
            if (!GameNetwork.HasStateAuthority || State == null || !SpaceVector2.Finite(seconds) || seconds <= 0d) return;
            List<ShipContactBody> bodies = BuildShipContactBodies(seconds);
            double cursor = 0d;
            // 所有扫掠分数都表示同一个固定步里的真实时刻。
            for (int contactIndex = 0; contactIndex < MaximumContactSweepIterations && cursor < 1d; contactIndex++)
            {
                double horizon = 1d;
                foreach (ShipContactBody body in bodies)
                    if (!body.SurfaceStopped && body.SurfaceContact.HasValue) horizon = Math.Min(horizon, body.EndFraction);
                ShipContactBody firstA = null, firstB = null;
                ShipContactHit first = default; first.Fraction = double.PositiveInfinity;
                for (int a = 0; a < bodies.Count; a++)
                for (int b = a + 1; b < bodies.Count; b++)
                {
                    ShipContactBody left = bodies[a], right = bodies[b];
                    if (left.SurfaceStopped || right.SurfaceStopped || !ContactRealmsMatch(left, right) ||
                        left.InverseMass + right.InverseMass <= 0d || !GetBodyContactBounds(left, true).Overlaps(GetBodyContactBounds(right, true)) ||
                        !FindFirstShipContact(left, right, cursor, horizon, out ShipContactHit hit) || hit.Fraction >= first.Fraction) continue;
                    if (hit.Fraction >= horizon - 1e-10d && (left.SurfaceContact.HasValue || right.SurfaceContact.HasValue)) continue;
                    SpaceVector2 centerA = ContactBodyPositionAt(left, hit.Fraction), centerB = ContactBodyPositionAt(right, hit.Fraction);
                    if (hit.Penetration <= ShipContactSkin && SpaceVector2.Dot(
                        ContactPointVelocityAt(right, hit.Point - centerB, hit.Fraction) -
                        ContactPointVelocityAt(left, hit.Point - centerA, hit.Fraction), hit.Normal) >= 0d) continue;
                    first = hit; firstA = left; firstB = right;
                }
                if (firstA != null)
                {
                    MoveContactBodyToFraction(firstA, first.Fraction); MoveContactBodyToFraction(firstB, first.Fraction);
                    ResolveShipContact(firstA, firstB, first);
                    RepredictContactBody(firstA, first.Fraction, seconds); RepredictContactBody(firstB, first.Fraction, seconds);
                    cursor = first.Fraction;
                    continue;
                }
                foreach (ShipContactBody body in bodies)
                    if (!body.SurfaceStopped && body.SurfaceContact.HasValue && body.EndFraction <= horizon + 1e-10d)
                        body.SurfaceStopped = true;
                if (horizon >= 1d) break;
                cursor = horizon;
            }
            // 多个真实面逐次解开重叠，空格和已对接的同一整体不会生成接触约束。
            for (int iteration = 0; iteration < 4; iteration++)
            {
                bool changed = false;
                for (int a = 0; a < bodies.Count; a++)
                for (int b = a + 1; b < bodies.Count; b++)
                {
                    ShipContactBody left = bodies[a], right = bodies[b];
                    if (left.SurfaceStopped || right.SurfaceStopped || !ContactRealmsMatch(left, right) || left.InverseMass + right.InverseMass <= 0d ||
                        !GetBodyContactBounds(left, false).Overlaps(GetBodyContactBounds(right, false))) continue;
                    if (!FindDeepestShipContact(left, right, out ShipContactHit hit)) continue;
                    SpaceVector2 beforeA = left.Position, beforeB = right.Position;
                    ResolveShipContact(left, right, hit);
                    RecheckContactSeparationAgainstSurface(left, beforeA); RecheckContactSeparationAgainstSurface(right, beforeB);
                    changed = true;
                }
                if (!changed) break;
            }
            foreach (ShipContactBody body in bodies) PublishContactBody(body);
        }
        private static bool FindFirstShipContact(ShipContactBody a, ShipContactBody b, double startFraction, double endFraction, out ShipContactHit result)
        {
            result = default; result.Fraction = double.PositiveInfinity;
            foreach (ShipContactShape left in a.Shapes)
            foreach (ShipContactShape right in b.Shapes)
            {
                if (!left.Piece.IsAlive || !right.Piece.IsAlive || !GetShapeContactBounds(left, true).Overlaps(GetShapeContactBounds(right, true)) ||
                    !SweepShipBoxes(left, right, startFraction, endFraction, out ShipContactHit hit) || hit.Fraction >= result.Fraction) continue;
                result = hit;
            }
            return SpaceVector2.Finite(result.Fraction);
        }
        private static bool FindDeepestShipContact(ShipContactBody a, ShipContactBody b, out ShipContactHit result)
        {
            result = default;
            foreach (ShipContactShape left in a.Shapes)
            foreach (ShipContactShape right in b.Shapes)
            {
                if (!left.Piece.IsAlive || !right.Piece.IsAlive || !GetShapeContactBounds(left, false).Overlaps(GetShapeContactBounds(right, false))) continue;
                ShipContactBox boxA = GetShipContactBox(left, 1d), boxB = GetShipContactBox(right, 1d);
                double separation = ShipBoxSeparation(boxA, boxB, out SpaceVector2 normal);
                if (separation >= -ShipContactSkin || -separation <= result.Penetration) continue;
                result = new ShipContactHit { ShapeA = left, ShapeB = right, Fraction = 1d, Penetration = -separation,
                    Normal = normal, Point = ShipBoxContactPoint(boxA, boxB, normal) };
            }
            return result.Penetration > 0d;
        }
        private static bool SweepShipBoxes(ShipContactShape a, ShipContactShape b, double startFraction, double endFraction, out ShipContactHit hit)
        {
            hit = default;
            if (endFraction < startFraction) return false;
            double angleA = ContactShapeAngleAt(a, endFraction) - ContactShapeAngleAt(a, startFraction);
            double angleB = ContactShapeAngleAt(b, endFraction) - ContactShapeAngleAt(b, startFraction);
            if (Math.Abs(angleA) <= 1e-10d && Math.Abs(angleB) <= 1e-10d)
                return SweepTranslatedShipBoxes(a, b, startFraction, endFraction, out hit);
            SpaceVector2 deltaA = ContactShapePositionAt(a, endFraction) - ContactShapePositionAt(a, startFraction);
            SpaceVector2 deltaB = ContactShapePositionAt(b, endFraction) - ContactShapePositionAt(b, startFraction);
            double motionBound = (deltaB - deltaA).Magnitude + Math.Abs(angleA) * a.RadiusFromCenter + Math.Abs(angleB) * b.RadiusFromCenter;
            double fraction = startFraction;
            for (int iteration = 0; iteration < MaximumContactSweepIterations; iteration++)
            {
                ShipContactBox boxA = GetShipContactBox(a, fraction), boxB = GetShipContactBox(b, fraction);
                double separation = ShipBoxSeparation(boxA, boxB, out SpaceVector2 normal);
                if (separation <= ShipContactSkin)
                {
                    hit = new ShipContactHit { ShapeA = a, ShapeB = b, Fraction = fraction, Penetration = Math.Max(0d, -separation),
                        Normal = normal, Point = ShipBoxContactPoint(boxA, boxB, normal) };
                    return true;
                }
                if (motionBound <= 1e-12d) return false;
                fraction += Math.Max(1e-12d, separation / motionBound * .9d * (endFraction - startFraction));
                if (fraction > endFraction) return false;
            }
            return false;
        }
        private static bool SweepTranslatedShipBoxes(ShipContactShape a, ShipContactShape b, double startFraction, double endFraction, out ShipContactHit hit)
        {
            hit = default;
            ShipContactBox startA = GetShipContactBox(a, startFraction), startB = GetShipContactBox(b, startFraction);
            SpaceVector2 delta = startB.Center - startA.Center;
            SpaceVector2 motion = (GetShipContactBox(b, endFraction).Center - startB.Center) - (GetShipContactBox(a, endFraction).Center - startA.Center);
            double enter = 0d, exit = 1d;
            if (!SweepContactAxis(startA.AxisX, startA, startB, delta, motion, ref enter, ref exit) ||
                !SweepContactAxis(startA.AxisY, startA, startB, delta, motion, ref enter, ref exit) ||
                !SweepContactAxis(startB.AxisX, startA, startB, delta, motion, ref enter, ref exit) ||
                !SweepContactAxis(startB.AxisY, startA, startB, delta, motion, ref enter, ref exit)) return false;
            double globalFraction = startFraction + (endFraction - startFraction) * enter;
            ShipContactBox boxA = GetShipContactBox(a, globalFraction), boxB = GetShipContactBox(b, globalFraction);
            double separation = ShipBoxSeparation(boxA, boxB, out SpaceVector2 normal);
            hit = new ShipContactHit { ShapeA = a, ShapeB = b, Fraction = globalFraction, Penetration = Math.Max(0d, -separation),
                Normal = normal, Point = ShipBoxContactPoint(boxA, boxB, normal) };
            return true;
        }
        private static bool SweepContactAxis(SpaceVector2 axis, ShipContactBox a, ShipContactBox b, SpaceVector2 delta, SpaceVector2 motion,
            ref double enter, ref double exit)
        {
            double radius = a.ProjectedRadius(axis) + b.ProjectedRadius(axis) + ShipContactSkin;
            double position = SpaceVector2.Dot(delta, axis), speed = SpaceVector2.Dot(motion, axis);
            if (Math.Abs(speed) <= 1e-12d) return Math.Abs(position) <= radius;
            double first = (-radius - position) / speed, last = (radius - position) / speed;
            if (first > last) { double swap = first; first = last; last = swap; }
            enter = Math.Max(enter, first); exit = Math.Min(exit, last);
            return enter <= exit;
        }
        private static void ResolveShipContact(ShipContactBody a, ShipContactBody b, ShipContactHit hit)
        {
            SpaceVector2 offsetA = hit.Point - a.Position, offsetB = hit.Point - b.Position;
            SpaceVector2 relative = ContactPointVelocity(b, offsetB) - ContactPointVelocity(a, offsetA);
            double approaching = SpaceVector2.Dot(relative, hit.Normal);
            double angularA = SpaceVector2.Cross(offsetA, hit.Normal), angularB = SpaceVector2.Cross(offsetB, hit.Normal);
            double inverse = a.InverseMass + b.InverseMass + angularA * angularA * a.InverseInertia + angularB * angularB * b.InverseInertia;
            if (approaching < 0d && inverse > 0d)
            {
                SpaceVector2 impulse = hit.Normal * (-approaching / inverse);
                ApplyContactImpulse(a, -impulse, offsetA); ApplyContactImpulse(b, impulse, offsetB);
            }
            double inverseMass = a.InverseMass + b.InverseMass;
            if (hit.Penetration > 0d && inverseMass > 0d)
            {
                SpaceVector2 separation = hit.Normal * ((hit.Penetration + ShipContactSkin) / inverseMass);
                a.Assembly.PositionX -= separation.X * a.InverseMass; a.Assembly.PositionY -= separation.Y * a.InverseMass;
                b.Assembly.PositionX += separation.X * b.InverseMass; b.Assembly.PositionY += separation.Y * b.InverseMass;
            }
            ShipDockingService.RigidlyCarryMembers(a.Assembly, a.Members);
            ShipDockingService.RigidlyCarryMembers(b.Assembly, b.Members);
        }
        private static void MoveContactBodyToFraction(ShipContactBody body, double fraction)
        {
            if (!body.Dynamic) return;
            double localFraction = ContactPathFraction(body, fraction);
            SpaceVector2 position = ContactBodyPositionAt(body, fraction);
            SpaceVector2 velocity = SpaceVector2.Lerp(body.PreviousVelocity, new SpaceVector2(body.Assembly.VelocityX, body.Assembly.VelocityY), localFraction);
            double angle = body.PreviousAngle + (body.Assembly.AngleRadians - body.PreviousAngle) * localFraction;
            double angularVelocity = body.PreviousAngularVelocity +
                (body.Assembly.AngularVelocityRadiansPerSecond - body.PreviousAngularVelocity) * localFraction;
            body.Assembly.PositionX = position.X; body.Assembly.PositionY = position.Y;
            body.Assembly.VelocityX = velocity.X; body.Assembly.VelocityY = velocity.Y;
            body.Assembly.AngleRadians = angle; body.Assembly.AngularVelocityRadiansPerSecond = angularVelocity;
            ShipDockingService.RigidlyCarryMembers(body.Assembly, body.Members);
        }
        private static double ContactPathFraction(ShipContactBody body, double fraction)
            => body.EndFraction - body.StartFraction <= 1e-12d ? 1d
                : Math.Clamp((fraction - body.StartFraction) / (body.EndFraction - body.StartFraction), 0d, 1d);
        private static SpaceVector2 ContactBodyPositionAt(ShipContactBody body, double fraction)
            => SpaceVector2.Lerp(body.PreviousPosition, body.Position, ContactPathFraction(body, fraction));
        private static SpaceVector2 ContactPointVelocityAt(ShipContactBody body, SpaceVector2 offset, double fraction)
        {
            double localFraction = ContactPathFraction(body, fraction);
            SpaceVector2 velocity = SpaceVector2.Lerp(body.PreviousVelocity, new SpaceVector2(body.Assembly.VelocityX, body.Assembly.VelocityY), localFraction);
            double angular = body.PreviousAngularVelocity + (body.Assembly.AngularVelocityRadiansPerSecond - body.PreviousAngularVelocity) * localFraction;
            return velocity + new SpaceVector2(-offset.Y, offset.X) * angular;
        }
        private static SpaceVector2 ContactPointVelocity(ShipContactBody body, SpaceVector2 offset)
            => new(body.Assembly.VelocityX - body.Assembly.AngularVelocityRadiansPerSecond * offset.Y,
                body.Assembly.VelocityY + body.Assembly.AngularVelocityRadiansPerSecond * offset.X);
        private static void ApplyContactImpulse(ShipContactBody body, SpaceVector2 impulse, SpaceVector2 offset)
        {
            body.Assembly.VelocityX += impulse.X * body.InverseMass; body.Assembly.VelocityY += impulse.Y * body.InverseMass;
            body.Assembly.AngularVelocityRadiansPerSecond += SpaceVector2.Cross(offset, impulse) * body.InverseInertia;
        }
        private void PublishContactBody(ShipContactBody body)
        {
            ShipDockingService.RigidlyCarryMembers(body.Assembly, State.Ships);
            foreach (string id in body.Assembly.MemberShipIds)
            {
                ShipState ship = GetShip(id); ShipFlightState flight = GetFlight(id);
                if (ship == null || flight == null) continue;
                if (flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending)
                {
                    flight.Orbit.PositionMeters = new SpaceVector2(ship.PositionX, ship.PositionY);
                    flight.Orbit.VelocityMetersPerSecond = new SpaceVector2(ship.VelocityX, ship.VelocityY);
                }
                else if (flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending)
                    flight.EntrySurfaceVelocity = new SpaceVector2(Math.Sqrt(ship.VelocityX * ship.VelocityX + ship.VelocityY * ship.VelocityY), flight.EntrySurfaceVelocity.Y);
            }
        }
        #endregion

        #region 自由乘员与散落物的真实反作用
        public bool ResolveFreePassengerContact(SpacePassengerState passenger, SpaceVector2 previousPosition, double seconds)
            => ResolveFreePassengerContact(passenger, previousPosition, seconds, out _);
        public bool ResolveFreePassengerContact(SpacePassengerState passenger, SpaceVector2 previousPosition, double seconds,
            out double firstContactFraction, double movementFraction = 1d)
        {
            firstContactFraction = double.PositiveInfinity;
            if (!GameNetwork.HasStateAuthority || State == null || passenger?.FreeMotion == null || !passenger.IsInSpace ||
                passenger.Supported || passenger.SurfaceDescending || passenger.LandingResolved) return false;
            Player player = null;
            if (ItemMgr.Instance != null) ItemMgr.Instance.Player_DIC.TryGetValue(passenger.ProfileId, out player);
            Data_Player data = player?.Data;
            if (data == null) owner?.PlayerData_Dict.TryGetValue(passenger.ProfileId, out data);
            double mass = data == null ? 70d : new ShipMassReader().ReadItem(data, player, 0);
            return ResolveFreeCircleContact(passenger.FreeMotion, previousPosition, seconds, Math.Max(.001d, mass),
                Math.Max(.01d, passenger.FreeMotion.RadiusMeters), "orbit", 0d, movementFraction, out firstContactFraction);
        }
        public bool ResolveLooseItemContact(SpaceLooseItemState item, SpaceVector2 previousPosition, double seconds)
            => ResolveLooseItemContact(item, previousPosition, seconds, out _);
        public bool ResolveLooseItemContact(SpaceLooseItemState item, SpaceVector2 previousPosition, double seconds,
            out double firstContactFraction, double movementFraction = 1d)
        {
            firstContactFraction = double.PositiveInfinity;
            if (!GameNetwork.HasStateAuthority || State == null || item?.Motion == null ||
                !item.InSpace && string.IsNullOrWhiteSpace(item.WorldKey) || string.IsNullOrWhiteSpace(item.Snapshot)) return false;
            looseViews.TryGetValue(item.Id, out Item live);
            if (live != null && (live.Owner != null || live.InHand || live.itemData.Stack.Amount <= 0f)) return false;
            ItemData data = live != null ? live.itemData : looseData.TryGetValue(item.Id, out ItemData cached) ? cached : Decode(item.Snapshot);
            looseData[item.Id] = data;
            double mass = new ShipMassReader().ReadItem(data, live, 0);
            return ResolveFreeCircleContact(item.Motion, previousPosition, seconds, Math.Max(.001d, mass),
                Math.Max(.01d, item.Motion.RadiusMeters), item.InSpace ? "orbit" : "surface:" + item.WorldKey,
                item.InSpace ? 0d : item.SurfaceHeight, movementFraction, out firstContactFraction);
        }
        private bool ResolveFreeCircleContact(OrbitState motion, SpaceVector2 previousPosition, double seconds, double mass, double radius,
            string realm, double height, double movementFraction, out double firstContactFraction)
        {
            firstContactFraction = double.PositiveInfinity;
            if (!GameNetwork.HasStateAuthority || State == null || !SpaceVector2.Finite(seconds) || seconds <= 0d ||
                !SpaceVector2.Finite(movementFraction) || movementFraction <= 0d || movementFraction > 1d ||
                !previousPosition.IsFinite || !motion.PositionMeters.IsFinite || !motion.VelocityMetersPerSecond.IsFinite) return false;
            List<ShipContactBody> bodies = BuildShipContactBodies(seconds);
            SpaceVector2 from = previousPosition, destination = motion.PositionMeters;
            double fraction = 0d;
            for (int contact = 0; contact < 8 && fraction < movementFraction; contact++)
            {
                ShipContactHit best = default; best.Fraction = double.PositiveInfinity;
                ShipContactBounds circleBounds = new(Math.Min(from.X, destination.X) - radius, Math.Min(from.Y, destination.Y) - radius,
                    Math.Max(from.X, destination.X) + radius, Math.Max(from.Y, destination.Y) + radius);
                foreach (ShipContactBody body in bodies)
                {
                    if (body.Realm != realm || realm != "orbit" && Math.Abs(height - body.Height) > body.HalfHeight + radius ||
                        !circleBounds.Overlaps(GetBodyContactBounds(body, true))) continue;
                    double endFraction = Math.Min(movementFraction, body.EndFraction);
                    if (endFraction < fraction || body.SurfaceContact.HasValue && endFraction <= fraction + 1e-10d) continue;
                    SpaceVector2 sweepDestination = SpaceVector2.Lerp(from, destination,
                        Math.Clamp((endFraction - fraction) / Math.Max(1e-12d, movementFraction - fraction), 0d, 1d));
                    foreach (ShipContactShape shape in body.Shapes)
                    {
                        if (!shape.Piece.IsAlive || !shape.Piece.BlocksMovement || !circleBounds.Overlaps(GetShapeContactBounds(shape, true)) ||
                            !SweepCircleAgainstShip(from, sweepDestination, radius, fraction, endFraction, shape, out ShipContactHit hit) || hit.Fraction >= best.Fraction ||
                            body.SurfaceContact.HasValue && hit.Fraction >= body.EndFraction - 1e-10d) continue;
                        hit.Body = body; best = hit;
                    }
                }
                if (!SpaceVector2.Finite(best.Fraction)) { from = destination; break; }
                double localFraction = Math.Clamp((best.Fraction - fraction) / Math.Max(1e-12d, movementFraction - fraction), 0d, 1d);
                from = SpaceVector2.Lerp(from, destination, localFraction) + best.Normal * (best.Penetration + ShipContactSkin);
                SpaceVector2 centerAtContact = ContactBodyPositionAt(best.Body, best.Fraction);
                SpaceVector2 offset = best.Point - centerAtContact;
                SpaceVector2 relative = motion.VelocityMetersPerSecond - ContactPointVelocityAt(best.Body, offset, best.Fraction);
                double approaching = SpaceVector2.Dot(relative, best.Normal);
                double lever = SpaceVector2.Cross(offset, best.Normal);
                double inverse = 1d / mass + best.Body.InverseMass + lever * lever * best.Body.InverseInertia;
                if (approaching < 0d)
                {
                    firstContactFraction = Math.Min(firstContactFraction, best.Fraction);
                    SpaceVector2 impulse = best.Normal * (-approaching / inverse);
                    motion.VelocityMetersPerSecond += impulse / mass;
                    MoveContactBodyToFraction(best.Body, best.Fraction);
                    ApplyContactImpulse(best.Body, -impulse, offset);
                    RepredictContactBody(best.Body, best.Fraction, seconds);
                    PublishContactBody(best.Body);
                }
                else if (best.Penetration > ShipContactSkin) firstContactFraction = Math.Min(firstContactFraction, best.Fraction);
                fraction = Math.Min(movementFraction, Math.Max(fraction + 1e-8d, best.Fraction));
                destination = from + motion.VelocityMetersPerSecond * ((movementFraction - fraction) * seconds);
            }
            motion.PositionMeters = from;
            return SpaceVector2.Finite(firstContactFraction);
        }
        private static bool SweepCircleAgainstShip(SpaceVector2 from, SpaceVector2 to, double radius, double startFraction, double endFraction,
            ShipContactShape shape, out ShipContactHit hit)
        {
            hit = default;
            double angularMotion = ContactShapeAngleAt(shape, endFraction) - ContactShapeAngleAt(shape, startFraction);
            if (Math.Abs(angularMotion) <= 1e-10d)
                return SweepTranslatedCircleAgainstShip(from, to, radius, startFraction, endFraction, shape, out hit);
            SpaceVector2 end = ContactShapePositionAt(shape, endFraction);
            SpaceVector2 shipStart = ContactShapePositionAt(shape, startFraction);
            double motionBound = ((to - from) - (end - shipStart)).Magnitude +
                Math.Abs(angularMotion) * shape.RadiusFromCenter;
            double localFraction = 0d;
            for (int iteration = 0; iteration < MaximumContactSweepIterations; iteration++)
            {
                double fraction = startFraction + (endFraction - startFraction) * localFraction;
                ShipContactBox box = GetShipContactBox(shape, fraction);
                SpaceVector2 center = SpaceVector2.Lerp(from, to, localFraction);
                double separation = CircleShipBoxSeparation(center, radius, box, out SpaceVector2 normal, out SpaceVector2 point);
                if (separation <= ShipContactSkin)
                {
                    hit = new ShipContactHit { ShapeA = shape, Fraction = fraction, Penetration = Math.Max(0d, -separation), Normal = normal, Point = point };
                    return true;
                }
                if (motionBound <= 1e-12d) return false;
                localFraction += Math.Max(1e-10d, separation / motionBound * .9d);
                if (localFraction > 1d) return false;
            }
            return false;
        }
        private static bool SweepTranslatedCircleAgainstShip(SpaceVector2 from, SpaceVector2 to, double radius, double startFraction, double endFraction,
            ShipContactShape shape, out ShipContactHit hit)
        {
            hit = default;
            ShipContactBox box = GetShipContactBox(shape, startFraction);
            double separation = CircleShipBoxSeparation(from, radius, box, out SpaceVector2 normal, out SpaceVector2 point);
            if (separation <= ShipContactSkin)
            {
                hit = new ShipContactHit { ShapeA = shape, Fraction = startFraction, Penetration = Math.Max(0d, -separation), Normal = normal, Point = point };
                return true;
            }
            SpaceVector2 relative = (to - from) - (GetShipContactBox(shape, endFraction).Center - box.Center);
            SpaceVector2 offset = from - box.Center;
            SpaceVector2 local = new(SpaceVector2.Dot(offset, box.AxisX), SpaceVector2.Dot(offset, box.AxisY));
            SpaceVector2 motion = new(SpaceVector2.Dot(relative, box.AxisX), SpaceVector2.Dot(relative, box.AxisY));
            double first = double.PositiveInfinity, extent = radius + ShipContactSkin;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                if (Math.Abs(motion.X) > 1e-12d && motion.X * sign < 0d)
                {
                    double fraction = (sign * (box.HalfX + extent) - local.X) / motion.X;
                    if (fraction >= 0d && fraction <= 1d && Math.Abs(local.Y + motion.Y * fraction) <= box.HalfY) first = Math.Min(first, fraction);
                }
                if (Math.Abs(motion.Y) > 1e-12d && motion.Y * sign < 0d)
                {
                    double fraction = (sign * (box.HalfY + extent) - local.Y) / motion.Y;
                    if (fraction >= 0d && fraction <= 1d && Math.Abs(local.X + motion.X * fraction) <= box.HalfX) first = Math.Min(first, fraction);
                }
            }
            double squareSpeed = motion.SqrMagnitude;
            if (squareSpeed > 1e-24d)
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            {
                SpaceVector2 corner = new(x * box.HalfX, y * box.HalfY), delta = local - corner;
                double projected = SpaceVector2.Dot(delta, motion);
                double discriminant = projected * projected - squareSpeed * (delta.SqrMagnitude - extent * extent);
                if (discriminant < 0d) continue;
                double fraction = (-projected - Math.Sqrt(discriminant)) / squareSpeed;
                if (fraction < 0d || fraction > 1d || fraction >= first) continue;
                SpaceVector2 candidate = local + motion * fraction;
                if (candidate.X * x >= box.HalfX - 1e-9d && candidate.Y * y >= box.HalfY - 1e-9d) first = fraction;
            }
            if (!SpaceVector2.Finite(first)) return false;
            double globalFraction = startFraction + (endFraction - startFraction) * first;
            separation = CircleShipBoxSeparation(SpaceVector2.Lerp(from, to, first), radius, GetShipContactBox(shape, globalFraction), out normal, out point);
            hit = new ShipContactHit { ShapeA = shape, Fraction = globalFraction, Penetration = Math.Max(0d, -separation), Normal = normal, Point = point };
            return true;
        }
        #endregion

        #region 部件矩形、扫掠界限与接触点
        private static SpaceVector2 ContactShapePositionAt(ShipContactShape shape, double fraction)
            => SpaceVector2.Lerp(shape.Previous.Position, new SpaceVector2(shape.Ship.PositionX, shape.Ship.PositionY),
                shape.Owner == null ? fraction : ContactPathFraction(shape.Owner, fraction));
        private static double ContactShapeAngleAt(ShipContactShape shape, double fraction)
            => shape.Previous.Angle + (shape.Ship.AngleRadians - shape.Previous.Angle) *
                (shape.Owner == null ? fraction : ContactPathFraction(shape.Owner, fraction));
        private static ShipContactBox GetShipContactBox(ShipContactShape shape, double fraction)
        {
            ShipState ship = shape.Ship; ShipPieceState piece = shape.Piece;
            int width = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height);
            int height = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width);
            double x = (piece.CellX + width * .5d) * ship.CellSizeMeters - ship.CenterOfMassLocalX;
            double y = (piece.CellY + height * .5d) * ship.CellSizeMeters - ship.CenterOfMassLocalY;
            double angle = ContactShapeAngleAt(shape, fraction);
            ShipGeometry.Rotate(x, y, angle, out double dx, out double dy);
            SpaceVector2 center = ContactShapePositionAt(shape, fraction) + new SpaceVector2(dx, dy);
            return new ShipContactBox(center, angle, width * ship.CellSizeMeters * .5d, height * ship.CellSizeMeters * .5d);
        }
        private static ShipContactBounds GetShapeContactBounds(ShipContactShape shape, bool swept)
        {
            if (swept)
            {
                // 外接半径只做候选筛选，最终接触必须落在真实旋转部件上。
                double radius = shape.RadiusFromCenter + ShipContactSkin;
                return new ShipContactBounds(Math.Min(shape.Previous.Position.X, shape.Ship.PositionX) - radius,
                    Math.Min(shape.Previous.Position.Y, shape.Ship.PositionY) - radius,
                    Math.Max(shape.Previous.Position.X, shape.Ship.PositionX) + radius,
                    Math.Max(shape.Previous.Position.Y, shape.Ship.PositionY) + radius);
            }
            ShipContactBox box = GetShipContactBox(shape, 1d);
            double x = box.ProjectedRadius(new SpaceVector2(1d, 0d)) + ShipContactSkin;
            double y = box.ProjectedRadius(new SpaceVector2(0d, 1d)) + ShipContactSkin;
            return new ShipContactBounds(box.Center.X - x, box.Center.Y - y, box.Center.X + x, box.Center.Y + y);
        }
        private static ShipContactBounds GetBodyContactBounds(ShipContactBody body, bool swept)
        {
            ShipContactBounds bounds = GetShapeContactBounds(body.Shapes[0], swept);
            for (int i = 1; i < body.Shapes.Count; i++) bounds = bounds.Union(GetShapeContactBounds(body.Shapes[i], swept));
            return bounds;
        }
        private static double ShipBoxSeparation(ShipContactBox a, ShipContactBox b, out SpaceVector2 normal)
        {
            SpaceVector2 delta = b.Center - a.Center;
            normal = default;
            double greatest = double.NegativeInfinity;
            ExamineContactAxis(a.AxisX, a, b, delta, ref greatest, ref normal);
            ExamineContactAxis(a.AxisY, a, b, delta, ref greatest, ref normal);
            ExamineContactAxis(b.AxisX, a, b, delta, ref greatest, ref normal);
            ExamineContactAxis(b.AxisY, a, b, delta, ref greatest, ref normal);
            return greatest;
        }
        private static void ExamineContactAxis(SpaceVector2 axis, ShipContactBox a, ShipContactBox b, SpaceVector2 delta,
            ref double greatest, ref SpaceVector2 normal)
        {
            double distance = SpaceVector2.Dot(delta, axis);
            double separation = Math.Abs(distance) - a.ProjectedRadius(axis) - b.ProjectedRadius(axis);
            if (separation <= greatest) return;
            greatest = separation; normal = distance >= 0d ? axis : -axis;
        }
        private static SpaceVector2 ClosestShipBoxPoint(ShipContactBox box, SpaceVector2 point)
        {
            SpaceVector2 delta = point - box.Center;
            return box.Center + box.AxisX * Math.Clamp(SpaceVector2.Dot(delta, box.AxisX), -box.HalfX, box.HalfX) +
                box.AxisY * Math.Clamp(SpaceVector2.Dot(delta, box.AxisY), -box.HalfY, box.HalfY);
        }
        private static SpaceVector2 ShipBoxSupportPoint(ShipContactBox box, SpaceVector2 direction)
        {
            double x = SpaceVector2.Dot(box.AxisX, direction), y = SpaceVector2.Dot(box.AxisY, direction);
            return box.Center + box.AxisX * (Math.Abs(x) <= 1e-9d ? 0d : Math.Sign(x) * box.HalfX) +
                box.AxisY * (Math.Abs(y) <= 1e-9d ? 0d : Math.Sign(y) * box.HalfY);
        }
        private static SpaceVector2 ShipBoxContactPoint(ShipContactBox a, ShipContactBox b, SpaceVector2 normal)
        {
            if (Math.Abs(SpaceVector2.Dot(normal, a.AxisX)) > .999999d || Math.Abs(SpaceVector2.Dot(normal, a.AxisY)) > .999999d)
            {
                SpaceVector2 incident = ShipBoxSupportPoint(b, -normal);
                return (incident + ClosestShipBoxPoint(a, incident)) * .5d;
            }
            SpaceVector2 other = ShipBoxSupportPoint(a, normal);
            return (other + ClosestShipBoxPoint(b, other)) * .5d;
        }
        private static double CircleShipBoxSeparation(SpaceVector2 center, double radius, ShipContactBox box,
            out SpaceVector2 normal, out SpaceVector2 point)
        {
            point = ClosestShipBoxPoint(box, center);
            SpaceVector2 delta = center - point;
            double distance = delta.Magnitude;
            if (distance > 1e-12d) { normal = delta / distance; return distance - radius; }
            SpaceVector2 local = center - box.Center;
            double x = SpaceVector2.Dot(local, box.AxisX), y = SpaceVector2.Dot(local, box.AxisY);
            double depthX = box.HalfX - Math.Abs(x), depthY = box.HalfY - Math.Abs(y);
            if (depthX <= depthY)
            { normal = x >= 0d ? box.AxisX : -box.AxisX; point = center + normal * depthX; return -depthX - radius; }
            normal = y >= 0d ? box.AxisY : -box.AxisY; point = center + normal * depthY; return -depthY - radius;
        }
        #endregion
    }
}
