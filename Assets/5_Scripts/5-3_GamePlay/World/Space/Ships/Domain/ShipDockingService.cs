using System;
using System.Collections.Generic;
using System.IO;

namespace FlatWorld.Spaceflight
{
    public static class ShipDockingService
    {
        #region 对接接口与双方确认
        public static void SynchronizePorts(ShipState ship)
        {
            var retained = new Dictionary<string, ShipDockingPortState>(StringComparer.Ordinal);
            foreach (ShipDockingPortState port in ship.DockingPorts)
                if (port != null && !string.IsNullOrEmpty(port.PieceId)) retained[port.PieceId] = port;
            var active = new List<ShipDockingPortState>();
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (piece == null || !piece.IsAlive || piece.Kind != ShipPieceKind.DockingPort) continue;
                if (!retained.TryGetValue(piece.PieceId, out ShipDockingPortState port))
                    port = new ShipDockingPortState { PortId = piece.PieceId, PieceId = piece.PieceId };
                if (string.IsNullOrWhiteSpace(port.PortId)) port.PortId = piece.PieceId;
                port.CellX = piece.CellX; port.CellY = piece.CellY; port.QuarterTurns = piece.QuarterTurns;
                port.Width = Math.Max(1, piece.Width); port.Height = Math.Max(1, piece.Height);
                active.Add(port);
            }
            ship.DockingPorts = active;
        }

        public static bool TryCreateCandidate(IReadOnlyList<ShipState> ships, List<ShipDockingPairState> pairs,
            string shipAId, string portAId, string shipBId, string portBId, out ShipDockingPairState pair,
            double positionToleranceMeters = 0.1d, double angleToleranceRadians = 0.1d,
            double maximumRelativeSpeed = 0.75d)
        {
            pair = null;
            if (shipAId == shipBId || !ValidTolerance(positionToleranceMeters) || !ValidTolerance(angleToleranceRadians) ||
                !ValidTolerance(maximumRelativeSpeed)) return false;
            ShipState a = FindShip(ships, shipAId), b = FindShip(ships, shipBId);
            if (a == null || b == null) return false;
            SynchronizePorts(a); SynchronizePorts(b);
            ShipDockingPortState portA = FindPort(a, portAId), portB = FindPort(b, portBId);
            if (portA == null || portB == null || !CanCapture(a, portA, b, portB,
                positionToleranceMeters, angleToleranceRadians, maximumRelativeSpeed)) return false;
            foreach (ShipDockingPairState existing in pairs)
            {
                if (SamePair(existing, shipAId, portAId, shipBId, portBId))
                {
                    pair = existing;
                    if (!existing.Connected)
                    {
                        existing.PositionToleranceMeters = positionToleranceMeters;
                        existing.AngleToleranceRadians = angleToleranceRadians;
                        existing.MaximumRelativeSpeedMetersPerSecond = maximumRelativeSpeed;
                    }
                    BindPorts(a, portA, b, portB, existing);
                    return true;
                }
                if (UsesPort(existing, shipAId, portAId) || UsesPort(existing, shipBId, portBId))
                    if (existing.Connected || existing.ConfirmedA || existing.ConfirmedB) return false;
            }
            for (int i = pairs.Count - 1; i >= 0; i--)
                if (UsesPort(pairs[i], shipAId, portAId) || UsesPort(pairs[i], shipBId, portBId)) pairs.RemoveAt(i);
            pair = new ShipDockingPairState
            {
                ShipAId = shipAId, PortAId = portAId, ShipBId = shipBId, PortBId = portBId,
                PositionToleranceMeters = positionToleranceMeters, AngleToleranceRadians = angleToleranceRadians,
                MaximumRelativeSpeedMetersPerSecond = maximumRelativeSpeed
            };
            pairs.Add(pair); BindPorts(a, portA, b, portB, pair);
            return true;
        }

        public static bool Confirm(IReadOnlyList<ShipState> ships, List<ShipDockingPairState> pairs,
            string pairId, string confirmingShipId)
        {
            ShipDockingPairState pair = FindPair(pairs, pairId);
            if (pair == null || pair.Connected) return pair != null && pair.Connected &&
                (confirmingShipId == pair.ShipAId || confirmingShipId == pair.ShipBId);
            ShipState a = FindShip(ships, pair.ShipAId), b = FindShip(ships, pair.ShipBId);
            if (a == null || b == null || !ShipStructureService.HasConsole(a) || !ShipStructureService.HasConsole(b)) return false;
            SynchronizePorts(a); SynchronizePorts(b);
            ShipDockingPortState portA = FindPort(a, pair.PortAId), portB = FindPort(b, pair.PortBId);
            if (portA == null || portB == null || !CanCapture(a, portA, b, portB, pair.PositionToleranceMeters,
                pair.AngleToleranceRadians, pair.MaximumRelativeSpeedMetersPerSecond))
            { ResetPair(ships, pair); return false; }
            if (confirmingShipId == pair.ShipAId) pair.ConfirmedA = true;
            else if (confirmingShipId == pair.ShipBId) pair.ConfirmedB = true;
            else return false;
            pair.Connected = pair.ConfirmedA && pair.ConfirmedB;
            BindPorts(a, portA, b, portB, pair);
            if (pair.Connected)
            {
                // 捕获为非弹性刚体组合，统一动量后再投影成员，不能给任一成员免费加速。
                foreach (ShipAssemblyState assembly in RebuildAssemblies(ships, pairs))
                    if (assembly.MemberShipIds.Contains(a.ShipId)) { RigidlyCarryMembers(assembly, ships); break; }
            }
            return true;
        }

        public static bool Disconnect(IReadOnlyList<ShipState> ships, List<ShipDockingPairState> pairs, string pairId)
        {
            ShipDockingPairState pair = FindPair(pairs, pairId);
            if (pair == null) return false;
            if (pair.Connected)
                foreach (ShipAssemblyState assembly in RebuildAssemblies(ships, pairs))
                    if (assembly.MemberShipIds.Contains(pair.ShipAId)) { RigidlyCarryMembers(assembly, ships); break; }
            // 成员质心已经具有整体平动与旋转切向速度，断开只撤销这对接口。
            ResetPair(ships, pair);
            return true;
        }

        public static void PortWorldPose(ShipState ship, ShipDockingPortState port,
            out double x, out double y, out double normalX, out double normalY)
        {
            double localAngle = NormalizeQuarterTurns(port.QuarterTurns) * Math.PI * 0.5d;
            int width = (port.QuarterTurns & 1) == 0 ? port.Width : port.Height;
            int height = (port.QuarterTurns & 1) == 0 ? port.Height : port.Width;
            double faceX = (port.CellX + width * 0.5d) * ship.CellSizeMeters + Math.Cos(localAngle) * width * ship.CellSizeMeters * 0.5d;
            double faceY = (port.CellY + height * 0.5d) * ship.CellSizeMeters + Math.Sin(localAngle) * height * ship.CellSizeMeters * 0.5d;
            ShipGeometry.LocalToWorld(ship, faceX, faceY, out x, out y);
            double angle = ship.AngleRadians + localAngle;
            normalX = Math.Cos(angle); normalY = Math.Sin(angle);
        }

        private static bool CanCapture(ShipState a, ShipDockingPortState portA, ShipState b, ShipDockingPortState portB,
            double positionTolerance, double angleTolerance, double speedTolerance)
        {
            if (a.PlatformKind != b.PlatformKind || a.WorldAddress != b.WorldAddress) return false;
            PortWorldPose(a, portA, out double ax, out double ay, out double anx, out double any);
            PortWorldPose(b, portB, out double bx, out double by, out double bnx, out double bny);
            double dx = ax - bx, dy = ay - by;
            if (dx * dx + dy * dy > positionTolerance * positionTolerance) return false;
            double dot = Math.Max(-1d, Math.Min(1d, -(anx * bnx + any * bny)));
            if (Math.Acos(dot) > angleTolerance) return false;
            ShipGeometry.PointVelocity(a, ax, ay, out double avx, out double avy);
            ShipGeometry.PointVelocity(b, bx, by, out double bvx, out double bvy);
            double vx = avx - bvx, vy = avy - bvy;
            return vx * vx + vy * vy <= speedTolerance * speedTolerance;
        }
        #endregion

        #region 有效接口图与运动整体
        public static List<ShipAssemblyState> RebuildAssemblies(IReadOnlyList<ShipState> ships,
            List<ShipDockingPairState> pairs)
        {
            var byId = new Dictionary<string, ShipState>(StringComparer.Ordinal);
            var neighbors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var portOwners = new Dictionary<string, ShipState>(StringComparer.Ordinal);
            foreach (ShipState ship in ships)
            {
                if (ship == null || string.IsNullOrWhiteSpace(ship.ShipId) || byId.ContainsKey(ship.ShipId))
                    throw new InvalidDataException("运动整体中的船体身份为空或重复");
                SynchronizePorts(ship);
                byId.Add(ship.ShipId, ship); neighbors.Add(ship.ShipId, new List<string>());
                foreach (ShipDockingPortState port in ship.DockingPorts)
                {
                    if (portOwners.ContainsKey(port.PortId)) throw new InvalidDataException("对接接口身份重复");
                    portOwners.Add(port.PortId, ship);
                    port.PairedShipId = null; port.PairedPortId = null;
                    port.Confirmed = false; port.Connected = false;
                }
            }
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShipDockingPairState pair in pairs)
            {
                if (pair == null) throw new InvalidDataException("对接存档含有空接口对");
                if (!portOwners.TryGetValue(pair.PortAId ?? string.Empty, out ShipState a) ||
                    !portOwners.TryGetValue(pair.PortBId ?? string.Empty, out ShipState b) || a.ShipId == b.ShipId)
                { ResetPair(ships, pair); continue; }
                // 船体断裂后按稳定接口身份重新归属，保留仍有效的真实连接。
                pair.ShipAId = a.ShipId; pair.ShipBId = b.ShipId;
                ShipDockingPortState portA = FindPort(a, pair.PortAId), portB = FindPort(b, pair.PortBId);
                if (!pair.Connected || !pair.ConfirmedA || !pair.ConfirmedB)
                { pair.Connected = false; BindPorts(a, portA, b, portB, pair); continue; }
                if (occupied.Contains(pair.PortAId) || occupied.Contains(pair.PortBId))
                { ResetPair(ships, pair); continue; }
                occupied.Add(pair.PortAId); occupied.Add(pair.PortBId);
                neighbors[a.ShipId].Add(b.ShipId); neighbors[b.ShipId].Add(a.ShipId);
                BindPorts(a, portA, b, portB, pair);
            }
            var ids = new List<string>(byId.Keys); ids.Sort(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var assemblies = new List<ShipAssemblyState>();
            foreach (string seed in ids)
            {
                if (!visited.Add(seed)) continue;
                var members = new List<ShipState>();
                var queue = new Queue<string>(); queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    string current = queue.Dequeue(); members.Add(byId[current]);
                    foreach (string other in neighbors[current]) if (visited.Add(other)) queue.Enqueue(other);
                }
                assemblies.Add(Aggregate(members));
            }
            return assemblies;
        }

        private static ShipAssemblyState Aggregate(List<ShipState> members)
        {
            members.Sort((a, b) => StringComparer.Ordinal.Compare(a.ShipId, b.ShipId));
            var assembly = new ShipAssemblyState { AssemblyId = members[0].ShipId, AngleRadians = members[0].AngleRadians };
            foreach (ShipState member in members)
            {
                if (!FluidUnits.IsFinite(member.MassKg) || member.MassKg < 0d)
                    throw new InvalidDataException("对接成员质量无效");
                assembly.MemberShipIds.Add(member.ShipId);
                assembly.MassKg += member.MassKg;
                assembly.PositionX += member.MassKg * member.PositionX;
                assembly.PositionY += member.MassKg * member.PositionY;
                assembly.VelocityX += member.MassKg * member.VelocityX;
                assembly.VelocityY += member.MassKg * member.VelocityY;
            }
            if (assembly.MassKg > 0d)
            {
                assembly.PositionX /= assembly.MassKg; assembly.PositionY /= assembly.MassKg;
                assembly.VelocityX /= assembly.MassKg; assembly.VelocityY /= assembly.MassKg;
            }
            else
            {
                assembly.PositionX = members[0].PositionX; assembly.PositionY = members[0].PositionY;
                assembly.VelocityX = members[0].VelocityX; assembly.VelocityY = members[0].VelocityY;
            }
            double angularMomentum = 0d;
            foreach (ShipState member in members)
            {
                double dx = member.PositionX - assembly.PositionX, dy = member.PositionY - assembly.PositionY;
                assembly.InertiaKgM2 += member.InertiaKgM2 + member.MassKg * (dx * dx + dy * dy);
                angularMomentum += member.InertiaKgM2 * member.AngularVelocityRadiansPerSecond +
                    member.MassKg * (dx * (member.VelocityY - assembly.VelocityY) -
                        dy * (member.VelocityX - assembly.VelocityX));
                ShipGeometry.Rotate(dx, dy, -assembly.AngleRadians, out double offsetX, out double offsetY);
                assembly.MemberTransforms.Add(new ShipAssemblyMemberState
                {
                    ShipId = member.ShipId, OffsetX = offsetX, OffsetY = offsetY,
                    RelativeAngleRadians = member.AngleRadians - assembly.AngleRadians
                });
            }
            assembly.AngularVelocityRadiansPerSecond = assembly.InertiaKgM2 > 0d
                ? angularMomentum / assembly.InertiaKgM2 : members[0].AngularVelocityRadiansPerSecond;
            return assembly;
        }

        public static void RigidlyCarryMembers(ShipAssemblyState assembly, IReadOnlyList<ShipState> ships)
        {
            foreach (ShipAssemblyMemberState transform in assembly.MemberTransforms)
            {
                ShipState member = FindShip(ships, transform.ShipId);
                if (member == null) continue;
                ShipGeometry.Rotate(transform.OffsetX, transform.OffsetY, assembly.AngleRadians, out double dx, out double dy);
                member.PositionX = assembly.PositionX + dx; member.PositionY = assembly.PositionY + dy;
                member.AngleRadians = assembly.AngleRadians + transform.RelativeAngleRadians;
                member.VelocityX = assembly.VelocityX - assembly.AngularVelocityRadiansPerSecond * dy;
                member.VelocityY = assembly.VelocityY + assembly.AngularVelocityRadiansPerSecond * dx;
                member.AngularVelocityRadiansPerSecond = assembly.AngularVelocityRadiansPerSecond;
            }
        }
        #endregion

        #region 连接身份与状态清理
        private static void BindPorts(ShipState a, ShipDockingPortState portA, ShipState b,
            ShipDockingPortState portB, ShipDockingPairState pair)
        {
            portA.PairedShipId = b.ShipId; portA.PairedPortId = portB.PortId;
            portB.PairedShipId = a.ShipId; portB.PairedPortId = portA.PortId;
            portA.Confirmed = pair.ConfirmedA; portB.Confirmed = pair.ConfirmedB;
            portA.Connected = pair.Connected; portB.Connected = pair.Connected;
        }
        private static void ResetPair(IReadOnlyList<ShipState> ships, ShipDockingPairState pair)
        {
            pair.Connected = false; pair.ConfirmedA = false; pair.ConfirmedB = false;
            ClearPort(FindShip(ships, pair.ShipAId), pair.PortAId);
            ClearPort(FindShip(ships, pair.ShipBId), pair.PortBId);
        }
        private static void ClearPort(ShipState ship, string portId)
        {
            ShipDockingPortState port = ship == null ? null : FindPort(ship, portId);
            if (port == null) return;
            port.PairedShipId = null; port.PairedPortId = null; port.Confirmed = false; port.Connected = false;
        }
        private static bool SamePair(ShipDockingPairState pair, string a, string pa, string b, string pb) =>
            pair != null && (pair.ShipAId == a && pair.PortAId == pa && pair.ShipBId == b && pair.PortBId == pb ||
                pair.ShipAId == b && pair.PortAId == pb && pair.ShipBId == a && pair.PortBId == pa);
        private static bool UsesPort(ShipDockingPairState pair, string shipId, string portId) =>
            pair != null && (pair.ShipAId == shipId && pair.PortAId == portId || pair.ShipBId == shipId && pair.PortBId == portId);
        public static ShipState FindShip(IReadOnlyList<ShipState> ships, string shipId)
        {
            foreach (ShipState ship in ships) if (ship != null && ship.ShipId == shipId) return ship;
            return null;
        }
        public static ShipDockingPortState FindPort(ShipState ship, string portId)
        {
            foreach (ShipDockingPortState port in ship.DockingPorts) if (port.PortId == portId) return port;
            return null;
        }
        private static ShipDockingPairState FindPair(List<ShipDockingPairState> pairs, string pairId)
        {
            foreach (ShipDockingPairState pair in pairs) if (pair != null && pair.PairId == pairId) return pair;
            return null;
        }
        private static bool ValidTolerance(double value) => FluidUnits.IsFinite(value) && value >= 0d;
        private static int NormalizeQuarterTurns(int value) => ((value % 4) + 4) % 4;
        #endregion
    }
}
