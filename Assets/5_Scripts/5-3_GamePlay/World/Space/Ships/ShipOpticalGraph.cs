using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public static class ShipOpticalGraph
    {
        #region 光纤独立信息图
        // 光纤仅连同船端口，不改变电力、管路或船体身份。
        public static bool Connected(ShipState ship, ShipPieceState from, ShipPieceState to)
        {
            var cable = new HashSet<ShipCell>();
            foreach (ShipPieceState piece in ship.Pieces)
                if (piece.IsAlive && piece.Kind == ShipPieceKind.Fiber) cable.Add(new ShipCell(piece.CellX, piece.CellY));
            var queue = new Queue<ShipCell>();
            var visited = new HashSet<ShipCell>();
            var start = new ShipCell(from.CellX, from.CellY);
            var end = new ShipCell(to.CellX, to.CellY);
            if (cable.Contains(start)) { queue.Enqueue(start); visited.Add(start); }
            foreach (ShipCell adjacent in ShipGeometry.Neighbors(start))
                if (cable.Contains(adjacent) && visited.Add(adjacent)) queue.Enqueue(adjacent);
            while (queue.Count > 0)
            {
                ShipCell cell = queue.Dequeue();
                if (cell.Equals(end)) return true;
                foreach (ShipCell next in ShipGeometry.Neighbors(cell))
                {
                    if (next.Equals(end)) return true;
                    if (cable.Contains(next) && visited.Add(next)) queue.Enqueue(next);
                }
            }
            return false;
        }
        #endregion
    }
}
