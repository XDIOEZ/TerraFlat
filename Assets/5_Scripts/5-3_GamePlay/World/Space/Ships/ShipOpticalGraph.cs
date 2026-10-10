using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public static class ShipOpticalGraph
    {
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
