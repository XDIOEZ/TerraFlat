using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public sealed class ShipAssemblyCache
    {
        #region 对接拓扑快照
        private readonly List<(ShipState Ship, string Id, ulong StructureRevision)> ships = new();
        private readonly List<PortStamp> ports = new();
        private readonly List<PairStamp> pairs = new();
        private readonly Dictionary<string, ShipAssemblyState> byShip = new(StringComparer.Ordinal);
        private readonly List<List<ShipState>> members = new();
        private List<ShipAssemblyState> assemblies = new();
        private bool initialized;
        public int RebuildCount { get; private set; }

        private readonly struct PortStamp
        {
            private readonly ShipState ship;
            private readonly ShipPieceState piece;
            private readonly string id;
            private readonly int x, y, width, height, turns;
            private readonly bool alive;
            public PortStamp(ShipState owner, ShipPieceState value)
            {
                ship = owner; piece = value; id = value.PieceId;
                x = value.CellX; y = value.CellY; width = value.Width; height = value.Height;
                turns = value.QuarterTurns; alive = value.IsAlive;
            }
            public bool Matches(ShipState owner, ShipPieceState value) =>
                ReferenceEquals(ship, owner) && ReferenceEquals(piece, value) && id == value.PieceId &&
                x == value.CellX && y == value.CellY && width == value.Width && height == value.Height &&
                turns == value.QuarterTurns && alive == value.IsAlive;
        }

        private readonly struct PairStamp
        {
            private readonly ShipDockingPairState pair;
            private readonly string shipA, shipB, portA, portB;
            private readonly bool connected, confirmedA, confirmedB;
            public PairStamp(ShipDockingPairState value)
            {
                pair = value; shipA = value.ShipAId; shipB = value.ShipBId;
                portA = value.PortAId; portB = value.PortBId;
                connected = value.Connected; confirmedA = value.ConfirmedA; confirmedB = value.ConfirmedB;
            }
            public bool Matches(ShipDockingPairState value) => value != null && ReferenceEquals(pair, value) &&
                shipA == value.ShipAId && shipB == value.ShipBId && portA == value.PortAId && portB == value.PortBId &&
                connected == value.Connected && confirmedA == value.ConfirmedA && confirmedB == value.ConfirmedB;
        }

        private bool Matches(IReadOnlyList<ShipState> source, List<ShipDockingPairState> connections)
        {
            if (!initialized || ships.Count != source.Count || pairs.Count != connections.Count) return false;
            int portIndex = 0;
            for (int i = 0; i < source.Count; i++)
            {
                ShipState ship = source[i];
                if (ship == null || !ReferenceEquals(ships[i].Ship, ship) || ships[i].Id != ship.ShipId) return false;
                if (ships[i].StructureRevision != ship.StructureRevision) return false;
                // 正式会话由结构事务维护版本，普通列表保留离线直接修改的校验路径。
                if (source is SpaceStateCollection<ShipState>) continue;
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (piece == null || piece.Kind != ShipPieceKind.DockingPort) continue;
                    if (portIndex >= ports.Count || !ports[portIndex++].Matches(ship, piece)) return false;
                }
            }
            if (source is not SpaceStateCollection<ShipState> && portIndex != ports.Count) return false;
            for (int i = 0; i < connections.Count; i++)
                if (!pairs[i].Matches(connections[i])) return false;
            return true;
        }

        private void EnsureTopology(IReadOnlyList<ShipState> source, List<ShipDockingPairState> connections)
        {
            if (Matches(source, connections)) return;
            assemblies = ShipDockingService.RebuildAssemblies(source, connections);
            ships.Clear(); ports.Clear(); pairs.Clear(); byShip.Clear(); members.Clear();
            foreach (ShipState ship in source)
            {
                ships.Add((ship, ship.ShipId, ship.StructureRevision));
                foreach (ShipPieceState piece in ship.Pieces)
                    if (piece != null && piece.Kind == ShipPieceKind.DockingPort) ports.Add(new PortStamp(ship, piece));
            }
            foreach (ShipDockingPairState pair in connections) pairs.Add(new PairStamp(pair));
            foreach (ShipAssemblyState assembly in assemblies)
            {
                var group = new List<ShipState>(assembly.MemberShipIds.Count);
                foreach (string id in assembly.MemberShipIds)
                {
                    byShip.Add(id, assembly);
                    group.Add(ShipDockingService.FindShip(source, id));
                }
                members.Add(group);
            }
            initialized = true;
            RebuildCount++;
        }
        #endregion

        #region 运动刷新与成员查询
        // 模拟和查询各持有一个缓存，避免查询重写正在碰撞结算的组合。
        public List<ShipAssemblyState> Refresh(IReadOnlyList<ShipState> source, List<ShipDockingPairState> connections)
        {
            EnsureTopology(source, connections);
            for (int i = 0; i < assemblies.Count; i++) ShipDockingService.RefreshAssembly(assemblies[i], members[i]);
            return assemblies;
        }

        public ShipAssemblyState Find(string shipId) =>
            !string.IsNullOrEmpty(shipId) && byShip.TryGetValue(shipId, out ShipAssemblyState assembly) ? assembly : null;

        public bool AreConnected(IReadOnlyList<ShipState> source, List<ShipDockingPairState> connections, string left, string right)
        {
            EnsureTopology(source, connections);
            ShipAssemblyState assembly = Find(left);
            return assembly != null && ReferenceEquals(assembly, Find(right));
        }

        public void Clear()
        {
            ships.Clear(); ports.Clear(); pairs.Clear(); byShip.Clear(); members.Clear(); assemblies.Clear();
            initialized = false; RebuildCount = 0;
        }
        #endregion
    }
}
