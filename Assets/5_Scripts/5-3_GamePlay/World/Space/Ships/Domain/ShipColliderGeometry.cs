using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public readonly struct ShipCollisionRect
    {
        #region 合并后的本地格矩形
        public readonly int X, Y, Width, Height;
        public readonly bool BlocksPassage;
        public ShipCollisionRect(int x, int y, int width, int height, bool blocksPassage)
        { X = x; Y = y; Width = width; Height = height; BlocksPassage = blocksPassage; }
        #endregion
    }

    public sealed class ShipColliderGeometry
    {
        #region 船体碰撞占地缓存
        private ShipState boundShip;
        private ulong revision;
        private readonly List<(ShipPieceState Piece, bool Open)> doors = new();
        private readonly List<ShipCollisionRect> rectangles = new();
        private readonly HashSet<(int X, int Y)> blocking = new(), traversable = new();
        public IReadOnlyList<ShipCollisionRect> Rectangles => rectangles;
        public int RebuildCount { get; private set; }

        public bool Synchronize(ShipState ship)
        {
            if (ship == null) throw new ArgumentNullException(nameof(ship));
            bool changed = !ReferenceEquals(boundShip, ship) || revision != ship.StructureRevision;
            if (!changed)
                foreach (var door in doors)
                    if (door.Piece.DoorOpen != door.Open) { changed = true; break; }
            if (!changed) return false;
            boundShip = ship; revision = ship.StructureRevision;
            doors.Clear(); rectangles.Clear(); blocking.Clear(); traversable.Clear();
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (piece.Kind == ShipPieceKind.Door) doors.Add((piece, piece.DoorOpen));
                if (!piece.IsAlive || piece.Kind is ShipPieceKind.Wire or ShipPieceKind.Fiber ||
                    piece.Kind == ShipPieceKind.Door && piece.DoorOpen) continue;
                int width = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height);
                int height = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width);
                var cells = piece.BlocksPassage ? blocking : traversable;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) cells.Add((piece.CellX + x, piece.CellY + y));
            }
            // 同格重叠只保留阻挡形状，地板和设备不再产生重复接触。
            traversable.ExceptWith(blocking);
            MergeRectangles(blocking, true);
            MergeRectangles(traversable, false);
            RebuildCount++;
            return true;
        }
        #endregion

        #region 连片格子合并
        private void MergeRectangles(HashSet<(int X, int Y)> cells, bool blocksPassage)
        {
            var rows = new SortedDictionary<int, List<int>>();
            foreach (var cell in cells)
            {
                if (!rows.TryGetValue(cell.Y, out List<int> row)) rows.Add(cell.Y, row = new List<int>());
                row.Add(cell.X);
            }
            var previous = new Dictionary<(int X, int Width), int>();
            var current = new Dictionary<(int X, int Width), int>();
            foreach (var row in rows)
            {
                row.Value.Sort(); current.Clear();
                int start = 0;
                while (start < row.Value.Count)
                {
                    int end = start + 1;
                    while (end < row.Value.Count && row.Value[end] == row.Value[end - 1] + 1) end++;
                    var run = (X: row.Value[start], Width: end - start);
                    if (previous.TryGetValue(run, out int index) &&
                        rectangles[index].Y + rectangles[index].Height == row.Key)
                    {
                        ShipCollisionRect rectangle = rectangles[index];
                        rectangles[index] = new ShipCollisionRect(rectangle.X, rectangle.Y,
                            rectangle.Width, rectangle.Height + 1, blocksPassage);
                    }
                    else
                    {
                        index = rectangles.Count;
                        rectangles.Add(new ShipCollisionRect(run.X, row.Key, run.Width, 1, blocksPassage));
                    }
                    current.Add(run, index); start = end;
                }
                (previous, current) = (current, previous);
            }
        }
        #endregion
    }
}
