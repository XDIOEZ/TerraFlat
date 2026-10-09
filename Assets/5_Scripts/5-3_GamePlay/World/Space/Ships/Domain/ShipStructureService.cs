using System;
using System.Collections.Generic;
using System.IO;

namespace FlatWorld.Spaceflight
{
    public static class ShipStructureService
    {
        #region 整船质量与质心
        public static ShipMassProperties RecalculateMass(ShipState ship)
        {
            if (ship == null) throw new ArgumentNullException(nameof(ship));
            if (!FinitePositive(ship.CellSizeMeters)) throw new InvalidDataException("船体格子尺寸必须是有限正数");
            var masses = new List<ShipMassContribution>();
            var sources = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (piece == null || !piece.IsAlive) continue;
                if (string.IsNullOrWhiteSpace(piece.PieceId) || !sources.Add(piece.PieceId))
                    throw new InvalidDataException("船体部件身份为空或重复");
                ValidateMass(piece.MassKg);
                int width = Math.Max(1, piece.Width), height = Math.Max(1, piece.Height);
                if ((piece.QuarterTurns & 1) != 0) { int swap = width; width = height; height = swap; }
                double meterWidth = width * ship.CellSizeMeters, meterHeight = height * ship.CellSizeMeters;
                masses.Add(new ShipMassContribution
                {
                    SourceId = piece.PieceId, MassKg = piece.MassKg,
                    LocalX = (piece.CellX + width * 0.5d) * ship.CellSizeMeters,
                    LocalY = (piece.CellY + height * 0.5d) * ship.CellSizeMeters,
                    IntrinsicInertiaKgM2 = piece.MassKg * (meterWidth * meterWidth + meterHeight * meterHeight) / 12d
                });
            }
            foreach (ShipMassContribution extra in ship.MassContributions)
            {
                if (extra == null || string.IsNullOrWhiteSpace(extra.SourceId))
                    throw new InvalidDataException("船体额外载荷必须具有稳定来源身份");
                // 同一实际来源只能汇总一次，已含在部件内的货物不能再次登记。
                if (!sources.Add(extra.SourceId)) continue;
                ValidateMass(extra.MassKg);
                if (!FluidUnits.IsFinite(extra.LocalX) || !FluidUnits.IsFinite(extra.LocalY) ||
                    !FluidUnits.IsFinite(extra.IntrinsicInertiaKgM2) || extra.IntrinsicInertiaKgM2 < 0d)
                    throw new InvalidDataException("船体载荷位置或转动惯量无效");
                masses.Add(extra);
            }
            foreach (ShipCompartmentState room in ship.Compartments)
            {
                string sourceId = "cabin-gas:" + room.CompartmentId;
                if (!sources.Add(sourceId) || room.Cells.Count == 0) continue;
                double mass = ShipAtmosphereService.GetGasMassKg(room);
                double x = 0d, y = 0d;
                foreach (ShipCell cell in room.Cells)
                { x += (cell.X + 0.5d) * ship.CellSizeMeters; y += (cell.Y + 0.5d) * ship.CellSizeMeters; }
                x /= room.Cells.Count; y /= room.Cells.Count;
                double gasInertia = 0d;
                foreach (ShipCell cell in room.Cells)
                {
                    double dx = (cell.X + .5d) * ship.CellSizeMeters - x;
                    double dy = (cell.Y + .5d) * ship.CellSizeMeters - y;
                    gasInertia += mass / room.Cells.Count * (dx * dx + dy * dy + ship.CellSizeMeters * ship.CellSizeMeters / 6d);
                }
                masses.Add(new ShipMassContribution
                { SourceId = sourceId, MassKg = mass, LocalX = x, LocalY = y, IntrinsicInertiaKgM2 = gasInertia });
            }
            double total = 0d, sumX = 0d, sumY = 0d;
            foreach (ShipMassContribution entry in masses)
            { total += entry.MassKg; sumX += entry.LocalX * entry.MassKg; sumY += entry.LocalY * entry.MassKg; }
            double centerX = total > 0d ? sumX / total : ship.CenterOfMassLocalX;
            double centerY = total > 0d ? sumY / total : ship.CenterOfMassLocalY;
            double inertia = 0d;
            foreach (ShipMassContribution entry in masses)
            {
                double dx = entry.LocalX - centerX, dy = entry.LocalY - centerY;
                inertia += entry.IntrinsicInertiaKgM2 + entry.MassKg * (dx * dx + dy * dy);
            }
            ShipGeometry.Rotate(centerX - ship.CenterOfMassLocalX, centerY - ship.CenterOfMassLocalY,
                ship.AngleRadians, out double shiftX, out double shiftY);
            ship.PositionX += shiftX; ship.PositionY += shiftY;
            ship.VelocityX -= ship.AngularVelocityRadiansPerSecond * shiftY;
            ship.VelocityY += ship.AngularVelocityRadiansPerSecond * shiftX;
            ship.MassKg = total; ship.CenterOfMassLocalX = centerX; ship.CenterOfMassLocalY = centerY;
            ship.InertiaKgM2 = inertia;
            return new ShipMassProperties(total, centerX, centerY, inertia);
        }

        private static void ValidateMass(double mass)
        {
            if (!FluidUnits.IsFinite(mass) || mass < 0d) throw new InvalidDataException("船体质量必须是有限非负数");
        }
        private static bool FinitePositive(double value) => FluidUnits.IsFinite(value) && value > 0d;
        #endregion

        #region 四邻接归属与分裂
        public static List<ShipState> RebuildAndSplit(ShipState ship,
            IShipAtmosphereSource outside = null,
            Action<ShipState, ShipPieceState, string> onDetached = null)
        {
            if (ship == null) throw new ArgumentNullException(nameof(ship));
            var floors = new HashSet<ShipCell>();
            var livePieces = new List<ShipPieceState>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (piece == null || !piece.IsAlive) continue;
                if (string.IsNullOrWhiteSpace(piece.PieceId) || !ids.Add(piece.PieceId))
                    throw new InvalidDataException("同一船体部件不能重复登记");
                livePieces.Add(piece);
                if (piece.Kind == ShipPieceKind.Floor)
                    foreach (ShipCell cell in ShipGeometry.Footprint(piece)) floors.Add(cell);
            }
            List<HashSet<ShipCell>> groups = ConnectedFloors(floors);
            if (groups.Count == 0)
            {
                foreach (ShipPieceState piece in livePieces) Detach(ship, piece, onDetached);
                ShipAtmosphereService.ReleaseAllGas(ship, outside);
                ship.Pieces.Clear(); ship.MassContributions.Clear(); ship.DockingPorts.Clear();
                ship.IsWreck = true; RecalculateMass(ship);
                return new List<ShipState>();
            }
            string anchor = SelectAnchor(ship, livePieces);
            int retainedIndex = -1;
            foreach (ShipPieceState piece in livePieces)
                if (piece.PieceId == anchor) retainedIndex = FindGroup(groups, piece.CellX, piece.CellY);
            if (retainedIndex < 0) retainedIndex = 0;
            var oldRooms = new List<ShipCompartmentState>(ship.Compartments);
            var oldKnown = new List<ShipCell>(ship.EverEnclosedCells);
            var oldMasses = new List<ShipMassContribution>(ship.MassContributions);
            var oldPorts = new List<ShipDockingPortState>(ship.DockingPorts);
            var results = new List<ShipState>();
            for (int i = 0; i < groups.Count; i++)
            {
                ShipState part = CopyKinematics(ship, i == retainedIndex ? ship.ShipId : Guid.NewGuid().ToString("N"));
                // 保留缺失地板的历史资格，补回原格也不能再次免费生成舱气。
                part.EverEnclosedCells.AddRange(oldKnown);
                results.Add(part);
            }
            foreach (ShipPieceState piece in livePieces)
            {
                int group = FindGroup(groups, piece.CellX, piece.CellY);
                if (piece.RequiresFloorSupport)
                    foreach (ShipCell cell in ShipGeometry.Footprint(piece))
                        if (group < 0 || !groups[group].Contains(cell)) { group = -1; break; }
                if (group < 0) { Detach(ship, piece, onDetached); continue; }
                results[group].Pieces.Add(piece);
            }
            foreach (ShipMassContribution mass in oldMasses)
            {
                int group = FindGroup(groups, (int)Math.Floor(mass.LocalX / ship.CellSizeMeters),
                    (int)Math.Floor(mass.LocalY / ship.CellSizeMeters));
                if (!string.IsNullOrEmpty(mass.SupportPieceId))
                    group = FindPieceGroup(results, mass.SupportPieceId);
                if (group >= 0) results[group].MassContributions.Add(mass);
            }
            foreach (ShipDockingPortState port in oldPorts)
            {
                int group = FindPieceGroup(results, port.PieceId);
                if (group >= 0) results[group].DockingPorts.Add(port);
            }
            ShipAtmosphereService.TransferToSplit(oldRooms, results, outside);
            for (int i = 0; i < results.Count; i++)
            {
                results[i].IdentityAnchorPieceId = SelectAnchor(results[i], results[i].Pieces);
                results[i].IsWreck = !HasConsole(results[i]);
                ShipAtmosphereService.RebuildCompartments(results[i], outside);
                RecalculateMass(results[i]);
            }
            ShipState retained = results[retainedIndex];
            ReplaceState(ship, retained);
            results[retainedIndex] = ship;
            return results;
        }

        private static List<HashSet<ShipCell>> ConnectedFloors(HashSet<ShipCell> floors)
        {
            var pending = new HashSet<ShipCell>(floors);
            var sorted = new List<ShipCell>(floors);
            sorted.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            var result = new List<HashSet<ShipCell>>();
            foreach (ShipCell seed in sorted)
            {
                if (!pending.Remove(seed)) continue;
                var group = new HashSet<ShipCell> { seed };
                var queue = new Queue<ShipCell>(); queue.Enqueue(seed);
                while (queue.Count > 0)
                    foreach (ShipCell neighbor in ShipGeometry.Neighbors(queue.Dequeue()))
                        if (pending.Remove(neighbor)) { group.Add(neighbor); queue.Enqueue(neighbor); }
                result.Add(group);
            }
            return result;
        }

        private static int FindGroup(List<HashSet<ShipCell>> groups, int x, int y)
        {
            var cell = new ShipCell(x, y);
            for (int i = 0; i < groups.Count; i++) if (groups[i].Contains(cell)) return i;
            return -1;
        }
        private static int FindPieceGroup(List<ShipState> ships, string pieceId)
        {
            for (int i = 0; i < ships.Count; i++)
                foreach (ShipPieceState piece in ships[i].Pieces) if (piece.PieceId == pieceId) return i;
            return -1;
        }
        public static bool HasConsole(ShipState ship)
        {
            foreach (ShipPieceState piece in ship.Pieces)
                if (piece != null && piece.IsAlive && piece.Kind == ShipPieceKind.Console) return true;
            return false;
        }
        private static string SelectAnchor(ShipState ship, List<ShipPieceState> pieces)
        {
            string firstConsole = null, firstFloor = null;
            foreach (ShipPieceState piece in pieces)
            {
                if (!piece.IsAlive) continue;
                if (piece.PieceId == ship.IdentityAnchorPieceId) return piece.PieceId;
                if (piece.Kind == ShipPieceKind.Console &&
                    (firstConsole == null || StringComparer.Ordinal.Compare(piece.PieceId, firstConsole) < 0))
                    firstConsole = piece.PieceId;
                if (piece.Kind == ShipPieceKind.Floor &&
                    (firstFloor == null || StringComparer.Ordinal.Compare(piece.PieceId, firstFloor) < 0))
                    firstFloor = piece.PieceId;
            }
            return firstConsole ?? firstFloor;
        }
        private static ShipState CopyKinematics(ShipState ship, string shipId) => new ShipState
        {
            ShipId = shipId, ReferenceBodyId = ship.ReferenceBodyId, WorldAddress = ship.WorldAddress,
            PlatformKind = ship.PlatformKind, CellSizeMeters = ship.CellSizeMeters,
            CabinHeightMeters = ship.CabinHeightMeters, NormalCabinTemperatureCelsius = ship.NormalCabinTemperatureCelsius,
            PositionX = ship.PositionX, PositionY = ship.PositionY, VelocityX = ship.VelocityX, VelocityY = ship.VelocityY,
            AngleRadians = ship.AngleRadians, AngularVelocityRadiansPerSecond = ship.AngularVelocityRadiansPerSecond,
            CenterOfMassLocalX = ship.CenterOfMassLocalX, CenterOfMassLocalY = ship.CenterOfMassLocalY
        };
        private static void ReplaceState(ShipState target, ShipState source)
        {
            target.IdentityAnchorPieceId = source.IdentityAnchorPieceId; target.IsWreck = source.IsWreck;
            target.Pieces = source.Pieces; target.MassContributions = source.MassContributions;
            target.Compartments = source.Compartments; target.EverEnclosedCells = source.EverEnclosedCells;
            target.DockingPorts = source.DockingPorts; target.PositionX = source.PositionX; target.PositionY = source.PositionY;
            target.VelocityX = source.VelocityX; target.VelocityY = source.VelocityY; target.MassKg = source.MassKg;
            target.CenterOfMassLocalX = source.CenterOfMassLocalX; target.CenterOfMassLocalY = source.CenterOfMassLocalY;
            target.InertiaKgM2 = source.InertiaKgM2;
        }
        private static void Detach(ShipState ship, ShipPieceState piece,
            Action<ShipState, ShipPieceState, string> callback)
        {
            if (piece.Destroyed) return;
            piece.Destroyed = true;
            callback?.Invoke(ship, piece, "lost-floor-support");
        }
        #endregion

        #region 唯一结构伤害入口
        public static bool ApplyDamage(ShipState ship, string pieceId, double damage, string reason,
            Action<ShipState, ShipPieceState, string> onDestroyed = null)
        {
            if (ship == null || !FluidUnits.IsFinite(damage) || damage <= 0d) return false;
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (piece == null || piece.PieceId != pieceId || !piece.IsAlive) continue;
                piece.Health = Math.Max(0d, piece.Health - damage);
                if (piece.Health == 0d)
                {
                    // 先提交摧毁标记再回调，连锁伤害和保存重入不会重复结算掉落。
                    piece.Destroyed = true;
                    if (piece.Kind == ShipPieceKind.Floor || piece.SealsAtmosphere) ship.AtmosphereStructureDirty = true;
                    onDestroyed?.Invoke(ship, piece, reason);
                }
                return true;
            }
            return false;
        }
        #endregion
    }
}
