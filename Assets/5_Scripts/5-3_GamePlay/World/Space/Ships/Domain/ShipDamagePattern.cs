using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public readonly struct ShipDamageLine
    {
        #region 船体局部坐标损伤线
        public readonly double OriginX, OriginY, DirectionX, DirectionY;
        public ShipDamageLine(double x, double y, double angleRadians)
        {
            OriginX = x; OriginY = y;
            DirectionX = Math.Cos(angleRadians); DirectionY = Math.Sin(angleRadians);
        }
        #endregion
    }

    public static class ShipDamagePattern
    {
        #region 实际部件取点与直线相交
        // 从实际活部件中取锚点，空格或已毁部件不会成为断裂线的落点。
        public static bool TryCreateLine(ShipState ship, double pointSample, double angleSample,
            out ShipDamageLine line, Predicate<ShipPieceState> eligible = null)
        {
            line = default;
            if (ship == null || !SpaceVector2.Finite(ship.CellSizeMeters) || ship.CellSizeMeters <= 0d ||
                !SpaceVector2.Finite(pointSample) || !SpaceVector2.Finite(angleSample)) return false;
            int count = 0;
            foreach (ShipPieceState piece in ship.Pieces)
                if (piece?.IsAlive == true && (eligible == null || eligible(piece))) count++;
            if (count == 0) return false;
            int index = Math.Min(count - 1, (int)(Math.Clamp(pointSample, 0d, 1d) * count));
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (piece?.IsAlive != true || eligible != null && !eligible(piece)) continue;
                if (index-- > 0) continue;
                GetBounds(ship, piece, out double x, out double y, out double width, out double height);
                line = new ShipDamageLine(x + width * .5d, y + height * .5d,
                    Math.Clamp(angleSample, 0d, 1d) * Math.PI);
                return true;
            }
            return false;
        }

        public static bool Intersects(ShipState ship, ShipPieceState piece, in ShipDamageLine line)
        {
            if (ship == null || piece?.IsAlive != true) return false;
            GetBounds(ship, piece, out double x, out double y, out double width, out double height);
            double nx = -line.DirectionY, ny = line.DirectionX;
            double signed = (x + width * .5d - line.OriginX) * nx + (y + height * .5d - line.OriginY) * ny;
            double extent = Math.Abs(nx) * width * .5d + Math.Abs(ny) * height * .5d;
            return Math.Abs(signed) <= extent + 1e-9d;
        }

        public static List<ShipPieceState> SelectPieces(ShipState ship, in ShipDamageLine line,
            Predicate<ShipPieceState> eligible = null)
        {
            var result = new List<ShipPieceState>();
            if (ship == null) return result;
            foreach (ShipPieceState piece in ship.Pieces)
                if (piece?.IsAlive == true && (eligible == null || eligible(piece)) && Intersects(ship, piece, line)) result.Add(piece);
            return result;
        }

        private static void GetBounds(ShipState ship, ShipPieceState piece,
            out double x, out double y, out double width, out double height)
        {
            x = piece.CellX * ship.CellSizeMeters; y = piece.CellY * ship.CellSizeMeters;
            width = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height) * ship.CellSizeMeters;
            height = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width) * ship.CellSizeMeters;
        }
        #endregion
    }
}
