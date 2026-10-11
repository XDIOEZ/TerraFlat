using System;

namespace FlatWorld.Spaceflight
{
    public static class ShipEnginePlacement
    {
        #region 引擎边界与喷口净空
        public static bool TryValidateEngine(ShipState ship, ShipPieceState engine, out string reason)
        {
            reason = null;
            if (ship == null || engine == null || !engine.IsAlive || engine.Kind != ShipPieceKind.Engine)
            { reason = "引擎需要安装在飞船边缘的地板上。"; return false; }
            if (engine.Width != 1 || engine.Height != 1)
            { reason = "飞船引擎必须只占一个格子。"; return false; }

            bool supported = false;
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (piece == null || !piece.IsAlive) continue;
                if (piece.Kind == ShipPieceKind.Floor && Contains(piece, engine.CellX, engine.CellY)) supported = true;
                if (BlocksExhaust(engine, piece))
                { reason = "引擎必须装在船体外缘，喷口朝船外，喷射方向不能被船体挡住。"; return false; }
            }
            if (!supported)
            { reason = "引擎需要安装在飞船边缘的地板上。"; return false; }
            return true;
        }

        public static bool TryValidateAddition(ShipState ship, ShipPieceState proposal, out string reason)
        {
            if (proposal.Kind == ShipPieceKind.Engine && !TryValidateEngine(ship, proposal, out reason)) return false;
            foreach (ShipPieceState engine in ship.Pieces)
                if (engine?.IsAlive == true && engine.Kind == ShipPieceKind.Engine && BlocksExhaust(engine, proposal))
                { reason = "这里会挡住已有引擎的喷口，请保留朝船外的喷射通道。"; return false; }
            reason = null;
            return true;
        }

        // 零朝向喷口向下；直接比较整块占地，舱内缺口和凹形船体都不能误当船外。
        private static bool BlocksExhaust(ShipPieceState engine, ShipPieceState obstacle)
        {
            if (ReferenceEquals(engine, obstacle) || obstacle?.IsAlive != true ||
                obstacle.Kind is ShipPieceKind.Wire or ShipPieceKind.Pipe or ShipPieceKind.Fiber) return false;
            GetBounds(obstacle, out long minX, out long minY, out long maxX, out long maxY);
            return (engine.QuarterTurns & 3) switch
            {
                0 => minX <= engine.CellX && maxX >= engine.CellX && minY < engine.CellY,
                1 => minY <= engine.CellY && maxY >= engine.CellY && maxX > engine.CellX,
                2 => minX <= engine.CellX && maxX >= engine.CellX && maxY > engine.CellY,
                _ => minY <= engine.CellY && maxY >= engine.CellY && minX < engine.CellX
            };
        }

        private static bool Contains(ShipPieceState piece, int x, int y)
        {
            GetBounds(piece, out long minX, out long minY, out long maxX, out long maxY);
            return x >= minX && x <= maxX && y >= minY && y <= maxY;
        }

        private static void GetBounds(ShipPieceState piece, out long minX, out long minY, out long maxX, out long maxY)
        {
            int width = Math.Max(1, piece.Width), height = Math.Max(1, piece.Height);
            if ((piece.QuarterTurns & 1) != 0) (width, height) = (height, width);
            minX = piece.CellX; minY = piece.CellY;
            maxX = minX + width - 1L; maxY = minY + height - 1L;
        }
        #endregion
    }
}
