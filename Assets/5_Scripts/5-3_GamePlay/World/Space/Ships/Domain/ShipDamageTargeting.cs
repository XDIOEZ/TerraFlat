using System;

namespace FlatWorld.Spaceflight
{
    public static class ShipDamageTargeting
    {
        #region 命中位置与部件血量映射
        // 一次攻击先冻结命中的上层部件，被打毁后也不让同一发穿透下面的地板。
        public static ShipPieceState SelectAtPoint(ShipState ship, double localX, double localY)
        {
            if (ship == null || !SpaceVector2.Finite(localX) || !SpaceVector2.Finite(localY) ||
                !SpaceVector2.Finite(ship.CellSizeMeters) || ship.CellSizeMeters <= 0d) return null;
            ShipPieceState selected = null;
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (piece?.IsAlive != true || !Contains(ship, piece, localX, localY)) continue;
                if (selected == null || ComparePriority(piece, selected) > 0) selected = piece;
            }
            return selected;
        }

        public static ShipPieceState SelectAtContact(ShipState ship, ShipPieceState contactedPiece,
            double localX, double localY)
        {
            if (ship == null || contactedPiece?.IsAlive != true || !SpaceVector2.Finite(localX) ||
                !SpaceVector2.Finite(localY) || !SpaceVector2.Finite(ship.CellSizeMeters) || ship.CellSizeMeters <= 0d) return null;
            int width = Math.Max(1, (contactedPiece.QuarterTurns & 1) == 0 ? contactedPiece.Width : contactedPiece.Height);
            int height = Math.Max(1, (contactedPiece.QuarterTurns & 1) == 0 ? contactedPiece.Height : contactedPiece.Width);
            double x = contactedPiece.CellX * ship.CellSizeMeters, y = contactedPiece.CellY * ship.CellSizeMeters;
            double inset = ship.CellSizeMeters * .001d;
            // 物理接触常落在格子边线上，向实际命中的占地内缩，避免误选隔壁高层设备。
            return SelectAtPoint(ship, Math.Clamp(localX, x + inset, x + width * ship.CellSizeMeters - inset),
                Math.Clamp(localY, y + inset, y + height * ship.CellSizeMeters - inset));
        }

        public static bool Contains(ShipState ship, ShipPieceState piece, double localX, double localY)
        {
            if (ship == null || piece == null || !SpaceVector2.Finite(ship.CellSizeMeters) ||
                ship.CellSizeMeters <= 0d || !SpaceVector2.Finite(localX) || !SpaceVector2.Finite(localY)) return false;
            int width = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height);
            int height = Math.Max(1, (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width);
            double minimumX = piece.CellX * ship.CellSizeMeters, minimumY = piece.CellY * ship.CellSizeMeters;
            double tolerance = Math.Max(1e-7d, ship.CellSizeMeters * 1e-4d);
            return localX >= minimumX - tolerance && localY >= minimumY - tolerance &&
                localX <= minimumX + width * ship.CellSizeMeters + tolerance &&
                localY <= minimumY + height * ship.CellSizeMeters + tolerance;
        }

        private static int ComparePriority(ShipPieceState left, ShipPieceState right)
        {
            int surface = SurfacePriority(left.Kind).CompareTo(SurfacePriority(right.Kind));
            if (surface != 0) return surface;
            int layer = left.Layer.CompareTo(right.Layer);
            return layer != 0 ? layer : -string.CompareOrdinal(left.PieceId, right.PieceId);
        }

        // 放置层编号不是显示高度，线路始终位于设备下面、地板上面。
        private static int SurfacePriority(ShipPieceKind kind) => kind switch
        {
            ShipPieceKind.Floor => 0,
            ShipPieceKind.Wire or ShipPieceKind.Pipe or ShipPieceKind.Fiber => 1,
            _ => 2
        };
        #endregion
    }
}
