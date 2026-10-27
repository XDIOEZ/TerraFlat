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

        public static double ApplyLineDamage(ShipState ship, in ShipDamageLine line, double damageBudget,
            string reason, Action<ShipState, ShipPieceState, string> onDestroyed = null,
            Predicate<ShipPieceState> eligible = null,
            Func<ShipState, ShipPieceState, double, string, double?> applyOwnedDamage = null)
        {
            if (!SpaceVector2.Finite(damageBudget) || damageBudget <= 0d) return 0d;
            List<ShipPieceState> hit = SelectPieces(ship, line, eligible);
            if (hit.Count == 0) return 0d;
            // 先均分冻结的沿线名单，再逐块扣防御，摧毁和回调不能重新放大原有预算。
            double share = damageBudget / hit.Count, actualLoss = 0d;
            foreach (ShipPieceState piece in hit)
            {
                double before = piece.Health;
                ShipStructureService.ApplyPhysicalDamage(ship, piece.PieceId, share, reason, onDestroyed, applyOwnedDamage);
                actualLoss += Math.Max(0d, before - piece.Health);
            }
            return actualLoss;
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
