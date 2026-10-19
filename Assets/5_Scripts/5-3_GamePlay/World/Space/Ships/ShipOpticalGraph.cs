using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FlatWorld.Spaceflight
{
    public static class ShipOpticalGraph
    {
        #region 光纤连接外形
        private sealed class VisualTopology
        {
            internal ulong Revision;
            internal int Count = -1;
            internal readonly Dictionary<(int X, int Y), List<ShipPieceState>> Nodes = new();
            internal readonly HashSet<ShipPieceState> Members = new();
        }

        private static readonly ConditionalWeakTable<ShipState, VisualTopology> visualTopologies = new();

        public static bool IsOpticalEndpoint(ShipPieceKind kind)
            => kind is ShipPieceKind.Console or ShipPieceKind.Navigation;

        // 船内与地面表现共用北东南西位序，设备仍只从原点同格或四邻接入光纤。
        public static int GetConnectionMask(ShipState ship, ShipPieceState fiber)
        {
            if (ship == null || fiber?.IsAlive != true || fiber.Kind != ShipPieceKind.Fiber) return 0;
            VisualTopology topology = visualTopologies.GetValue(ship, static _ => new VisualTopology());
            if (topology.Count != ship.Pieces.Count || topology.Revision != ship.StructureRevision)
            {
                topology.Nodes.Clear(); topology.Members.Clear();
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (!piece.IsAlive || (piece.Kind != ShipPieceKind.Fiber && !IsOpticalEndpoint(piece.Kind))) continue;
                    var cell = (piece.CellX, piece.CellY);
                    if (!topology.Nodes.TryGetValue(cell, out List<ShipPieceState> members))
                        topology.Nodes.Add(cell, members = new List<ShipPieceState>(2));
                    members.Add(piece); topology.Members.Add(piece);
                }
                topology.Revision = ship.StructureRevision; topology.Count = ship.Pieces.Count;
            }
            if (!topology.Members.Contains(fiber)) return 0;
            int mask = 0;
            if (HasOpticalNeighbor(topology, fiber.CellX, fiber.CellY + 1)) mask |= 1;
            if (HasOpticalNeighbor(topology, fiber.CellX + 1, fiber.CellY)) mask |= 2;
            if (HasOpticalNeighbor(topology, fiber.CellX, fiber.CellY - 1)) mask |= 4;
            if (HasOpticalNeighbor(topology, fiber.CellX - 1, fiber.CellY)) mask |= 8;
            return mask;
        }

        private static bool HasOpticalNeighbor(VisualTopology topology, int x, int y)
        {
            if (!topology.Nodes.TryGetValue((x, y), out List<ShipPieceState> nodes)) return false;
            foreach (ShipPieceState node in nodes) if (node.IsAlive) return true;
            return false;
        }
        #endregion

        #region 光纤独立信息图
        public sealed class Workspace
        {
            internal readonly HashSet<(int X, int Y)> Cable = new();
            internal readonly HashSet<(int X, int Y)> Visited = new();
            internal readonly Queue<(int X, int Y)> Queue = new();
        }

        // 光纤仅连同船端口，不改变电力、管路或船体身份。
        public static bool Connected(ShipState ship, ShipPieceState from, ShipPieceState to, Workspace workspace = null)
        {
            if (ship == null || from == null || to == null || !from.IsAlive || !to.IsAlive) return false;
            workspace ??= new Workspace();
            var cable = workspace.Cable;
            var queue = workspace.Queue;
            var visited = workspace.Visited;
            cable.Clear(); queue.Clear(); visited.Clear();
            foreach (ShipPieceState piece in ship.Pieces)
                if (piece.IsAlive && piece.Kind == ShipPieceKind.Fiber) cable.Add((piece.CellX, piece.CellY));
            EnqueueCable(workspace, from.CellX, from.CellY);
            EnqueueCable(workspace, from.CellX + 1, from.CellY);
            EnqueueCable(workspace, from.CellX - 1, from.CellY);
            EnqueueCable(workspace, from.CellX, from.CellY + 1);
            EnqueueCable(workspace, from.CellX, from.CellY - 1);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                if (System.Math.Abs((long)cell.X - to.CellX) + System.Math.Abs((long)cell.Y - to.CellY) <= 1) return true;
                EnqueueCable(workspace, cell.X + 1, cell.Y);
                EnqueueCable(workspace, cell.X - 1, cell.Y);
                EnqueueCable(workspace, cell.X, cell.Y + 1);
                EnqueueCable(workspace, cell.X, cell.Y - 1);
            }
            return false;
        }

        private static void EnqueueCable(Workspace workspace, int x, int y)
        {
            var cell = (x, y);
            if (workspace.Cable.Contains(cell) && workspace.Visited.Add(cell)) workspace.Queue.Enqueue(cell);
        }
        #endregion
    }
}
