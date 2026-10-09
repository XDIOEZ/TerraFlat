using System;
using System.Collections.Generic;
using System.IO;

namespace FlatWorld.Spaceflight
{
    #region 船外真实气源与环境查询
    public interface IShipAtmosphereSource
    {
        double PressureKPa { get; }
        double TemperatureCelsius { get; }
        IReadOnlyList<FluidBatch> TakeGas(decimal maximumMoles);
        void ReleaseGas(FluidBatch gas);
    }

    public readonly struct ShipEnvironmentSnapshot
    {
        public readonly bool IsSealed;
        public readonly bool HasFloorSupport;
        public readonly double PressureKPa;
        public readonly double TemperatureCelsius;
        public readonly double OxygenPartialPressureKPa;
        public readonly double CarbonDioxidePartialPressureKPa;
        public readonly string CompartmentId;
        public bool CanBreathe => IsSealed && OxygenPartialPressureKPa >= 16d && CarbonDioxidePartialPressureKPa <= 1d;
        public ShipEnvironmentSnapshot(bool sealedRegion, bool floor, double pressure, double temperature,
            double oxygen, double carbonDioxide, string compartmentId)
        {
            IsSealed = sealedRegion; HasFloorSupport = floor; PressureKPa = pressure; TemperatureCelsius = temperature;
            OxygenPartialPressureKPa = oxygen; CarbonDioxidePartialPressureKPa = carbonDioxide; CompartmentId = compartmentId;
        }
    }
    #endregion

    public static class ShipAtmosphereService
    {
        #region 密闭边界与舱气守恒
        public static void RebuildCompartments(ShipState ship, IShipAtmosphereSource outside = null)
        {
            if (ship == null) throw new ArgumentNullException(nameof(ship));
            ValidateGeometry(ship);
            var floors = new HashSet<ShipCell>();
            var walls = new HashSet<ShipCell>();
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (piece == null || !piece.IsAlive) continue;
                if (piece.Kind == ShipPieceKind.Floor)
                    foreach (ShipCell cell in ShipGeometry.Footprint(piece)) floors.Add(cell);
                if (IsBoundary(piece))
                    foreach (ShipCell cell in ShipGeometry.Footprint(piece)) walls.Add(cell);
            }
            var unvisited = new HashSet<ShipCell>(floors);
            unvisited.ExceptWith(walls);
            var sealedRooms = new List<ShipCompartmentState>();
            var openCells = new HashSet<ShipCell>();
            var sorted = new List<ShipCell>(unvisited);
            sorted.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            foreach (ShipCell seed in sorted)
            {
                if (!unvisited.Remove(seed)) continue;
                var cells = new List<ShipCell> { seed };
                var queue = new Queue<ShipCell>(); queue.Enqueue(seed);
                bool sealedRegion = true;
                while (queue.Count > 0)
                {
                    foreach (ShipCell neighbor in ShipGeometry.Neighbors(queue.Dequeue()))
                    {
                        if (walls.Contains(neighbor)) continue;
                        if (!floors.Contains(neighbor)) { sealedRegion = false; continue; }
                        if (unvisited.Remove(neighbor)) { queue.Enqueue(neighbor); cells.Add(neighbor); }
                    }
                }
                if (!sealedRegion) { foreach (ShipCell cell in cells) openCells.Add(cell); continue; }
                sealedRooms.Add(new ShipCompartmentState { Cells = cells, VolumeLiters = cells.Count * CellVolumeLiters(ship) });
            }
            var previous = ship.Compartments ?? new List<ShipCompartmentState>();
            var roomCells = new List<HashSet<ShipCell>>();
            foreach (ShipCompartmentState room in sealedRooms) roomCells.Add(new HashSet<ShipCell>(room.Cells));
            foreach (ShipCompartmentState oldRoom in previous)
            {
                var weights = new int[sealedRooms.Count + 1];
                foreach (ShipCell cell in oldRoom.Cells)
                {
                    bool assigned = false;
                    for (int i = 0; i < roomCells.Count; i++)
                        if (roomCells[i].Contains(cell)) { weights[i]++; assigned = true; break; }
                    if (!assigned && !walls.Contains(cell)) weights[sealedRooms.Count]++;
                }
                int matches = 0, matchingIndex = -1;
                for (int i = 0; i < sealedRooms.Count; i++) if (weights[i] > 0) { matches++; matchingIndex = i; }
                if (matches == 1 && weights[sealedRooms.Count] == 0 &&
                    sealedRooms[matchingIndex].CompartmentId.Length > 0)
                    sealedRooms[matchingIndex].CompartmentId = oldRoom.CompartmentId;
                DistributeGas(oldRoom, sealedRooms, weights, outside, ship.NormalCabinTemperatureCelsius);
            }
            var known = new HashSet<ShipCell>(ship.EverEnclosedCells);
            foreach (ShipCompartmentState room in sealedRooms)
            {
                int newlyEnclosed = 0;
                foreach (ShipCell cell in room.Cells) if (known.Add(cell)) newlyEnclosed++;
                // 首次密闭仅抽取新围住体积中的真实外界气体，修补不还原旧舱气。
                if (newlyEnclosed > 0 && outside != null && outside.PressureKPa > 0d)
                {
                    decimal amount = MolesAtPressure(outside.PressureKPa,
                        newlyEnclosed * CellVolumeLiters(ship), outside.TemperatureCelsius);
                    AddFromSource(room, outside, amount, ship.NormalCabinTemperatureCelsius, out _);
                }
                NormalizeTemperature(room.Gas, ship.NormalCabinTemperatureCelsius);
            }
            ship.Compartments = sealedRooms;
            ship.EverEnclosedCells = new List<ShipCell>(known);
            ship.AtmosphereStructureDirty = false;
        }

        internal static void TransferToSplit(List<ShipCompartmentState> previous, List<ShipState> ships,
            IShipAtmosphereSource outside)
        {
            var floors = new List<HashSet<ShipCell>>();
            foreach (ShipState ship in ships)
            {
                var owned = new HashSet<ShipCell>();
                foreach (ShipPieceState piece in ship.Pieces)
                    if (piece.IsAlive && piece.Kind == ShipPieceKind.Floor)
                        foreach (ShipCell cell in ShipGeometry.Footprint(piece)) owned.Add(cell);
                floors.Add(owned);
            }
            foreach (ShipCompartmentState oldRoom in previous)
            {
                var destinations = new List<ShipCompartmentState>();
                var weights = new int[ships.Count + 1];
                for (int i = 0; i < ships.Count; i++)
                {
                    var room = new ShipCompartmentState { CompartmentId = oldRoom.CompartmentId };
                    foreach (ShipCell cell in oldRoom.Cells)
                        if (floors[i].Contains(cell)) { room.Cells.Add(cell); weights[i]++; }
                    room.VolumeLiters = room.Cells.Count * CellVolumeLiters(ships[i]);
                    destinations.Add(room);
                    if (room.Cells.Count > 0) ships[i].Compartments.Add(room);
                }
                int accounted = 0;
                for (int i = 0; i < ships.Count; i++) accounted += weights[i];
                weights[ships.Count] = oldRoom.Cells.Count - accounted;
                DistributeGas(oldRoom, destinations, weights, outside,
                    ships.Count > 0 ? ships[0].NormalCabinTemperatureCelsius : 20d);
            }
        }

        private static void DistributeGas(ShipCompartmentState source, List<ShipCompartmentState> destinations,
            int[] weights, IShipAtmosphereSource outside, double normalTemperature)
        {
            int totalWeight = 0, last = -1;
            for (int i = 0; i < weights.Length; i++) if (weights[i] > 0) { totalWeight += weights[i]; last = i; }
            if (totalWeight == 0) { ReleaseRoomGas(source, outside); return; }
            var inventory = new FluidInventory(source.Gas);
            List<FluidBatch> batches = inventory.Drain();
            foreach (FluidBatch batch in batches)
            {
                decimal remaining = batch.GasMoles;
                for (int i = 0; i < weights.Length; i++)
                {
                    if (weights[i] == 0) continue;
                    decimal amount = i == last ? remaining : AtmosphereService.Quantize(batch.GasMoles * weights[i] / totalWeight);
                    remaining -= amount;
                    if (amount <= 0m) continue;
                    FluidBatch portion = batch.Scale(amount / batch.GasMoles);
                    if (i >= destinations.Count) outside?.ReleaseGas(portion);
                    else AddGas(destinations[i], portion, normalTemperature);
                }
            }
        }

        public static void ReleaseAllGas(ShipState ship, IShipAtmosphereSource outside = null)
        {
            foreach (ShipCompartmentState room in ship.Compartments) ReleaseRoomGas(room, outside);
            ship.Compartments.Clear();
        }
        private static void ReleaseRoomGas(ShipCompartmentState room, IShipAtmosphereSource outside)
        {
            // 先清空权威库存，再交给外界排放入口，重复刷新不会再次释放。
            List<FluidBatch> batches = new FluidInventory(room.Gas).Drain();
            foreach (FluidBatch batch in batches) outside?.ReleaseGas(batch);
        }
        private static bool IsBoundary(ShipPieceState piece) => piece.SealsAtmosphere &&
            (piece.Kind == ShipPieceKind.Wall || piece.Kind == ShipPieceKind.Door || piece.Kind == ShipPieceKind.DockingPort);
        private static double CellVolumeLiters(ShipState ship) => ship.CellSizeMeters * ship.CellSizeMeters * ship.CabinHeightMeters * 1000d;
        private static void ValidateGeometry(ShipState ship)
        {
            if (!FluidUnits.IsFinite(ship.CellSizeMeters) || ship.CellSizeMeters <= 0d ||
                !FluidUnits.IsFinite(ship.CabinHeightMeters) || ship.CabinHeightMeters <= 0d ||
                !FluidUnits.IsFinite(ship.NormalCabinTemperatureCelsius) || ship.NormalCabinTemperatureCelsius <= -273.15d)
                throw new InvalidDataException("船舱格子尺寸、层高或正常温度无效");
        }
        #endregion

        #region 供气与呼吸真实库存
        public static bool TrySupplyGas(ShipState ship, int cellX, int cellY, IShipAtmosphereSource source,
            double targetPressureKPa, decimal maxMoles, out decimal received)
        {
            received = 0m;
            ShipCompartmentState room = FindCompartment(ship, cellX, cellY);
            if (room == null || source == null || maxMoles <= 0m || !FluidUnits.IsFinite(targetPressureKPa) || targetPressureKPa <= 0d)
                return false;
            decimal target = MolesAtPressure(targetPressureKPa, room.VolumeLiters, ship.NormalCabinTemperatureCelsius);
            decimal needed = Math.Max(0m, target - TotalMoles(room.Gas));
            if (needed <= 0m) return false;
            bool result = AddFromSource(room, source, Math.Min(maxMoles, needed), ship.NormalCabinTemperatureCelsius, out received);
            if (result) ShipStructureService.RecalculateMass(ship);
            return result;
        }

        public static bool TryTakeGas(ShipState ship, int cellX, int cellY, string fluidId,
            decimal requestedMoles, out FluidBatch batch)
        {
            batch = default;
            ShipCompartmentState room = FindCompartment(ship, cellX, cellY);
            if (room == null || requestedMoles <= 0m) return false;
            var inventory = new FluidInventory(room.Gas);
            decimal amount = Math.Min(requestedMoles, inventory.GetAvailableMoles(fluidId, FluidPhase.Gas));
            if (amount <= 0m || !inventory.TryTakeExact(fluidId, FluidPhase.Gas, amount, out batch)) return false;
            NormalizeTemperature(room.Gas, ship.NormalCabinTemperatureCelsius);
            ShipStructureService.RecalculateMass(ship);
            return true;
        }

        public static bool TryAddGas(ShipState ship, int cellX, int cellY, FluidBatch batch)
        {
            ShipCompartmentState room = FindCompartment(ship, cellX, cellY);
            if (room == null || !batch.IsGas) return false;
            if (!new FluidInventory(room.Gas).CanAdd(batch, room.VolumeLiters, room.VolumeLiters)) return false;
            AddGas(room, batch, ship.NormalCabinTemperatureCelsius);
            ShipStructureService.RecalculateMass(ship);
            return true;
        }

        private static bool AddFromSource(ShipCompartmentState room, IShipAtmosphereSource source,
            decimal requested, double normalTemperature, out decimal received)
        {
            received = 0m;
            if (requested <= 0m) return false;
            requested = AtmosphereService.Quantize(Math.Min(requested, FluidInventory.MaximumMoles - TotalMoles(room.Gas)));
            if (requested <= 0m) return false;
            IReadOnlyList<FluidBatch> batches = source.TakeGas(requested);
            if (batches == null) return false;
            foreach (FluidBatch batch in batches)
            {
                if (!batch.IsGas || batch.GasMoles <= 0m || batch.GasMoles > requested - received)
                    throw new InvalidDataException("船舱供气源必须返回已扣除的实际气相批次，不能超额生成气体");
                AddGas(room, batch, normalTemperature);
                received += batch.GasMoles;
            }
            return received > 0m;
        }

        private static void AddGas(ShipCompartmentState room, FluidBatch batch, double normalTemperature)
        {
            if (!batch.IsGas || room.VolumeLiters <= 0d) throw new InvalidDataException("密闭舱只接收真实气相库存");
            var inventory = new FluidInventory(room.Gas);
            if (!inventory.TryAdd(batch, room.VolumeLiters, room.VolumeLiters))
                throw new InvalidDataException("实际气包无法写入船舱库存");
            NormalizeTemperature(room.Gas, normalTemperature);
        }

        private static void NormalizeTemperature(FluidInventoryState gas, double temperature)
        {
            double energy = 0d;
            foreach (FluidComponentState component in gas.Components)
                energy += FluidCatalog.Default.Find(component.FluidId).EnergyAt(component.GasMoles, component.LiquidMoles,
                    temperature + 273.15d);
            gas.TotalInternalEnergyJoules = energy;
        }
        public static decimal MolesAtPressure(double pressureKPa, double volumeLiters, double temperatureCelsius)
        {
            if (!FluidUnits.IsFinite(pressureKPa) || pressureKPa < 0d || !FluidUnits.IsFinite(volumeLiters) || volumeLiters <= 0d ||
                !FluidUnits.IsFinite(temperatureCelsius) || temperatureCelsius <= -273.15d)
                throw new InvalidDataException("舱气压力、体积或温度无效");
            double amount = pressureKPa * volumeLiters / (FluidUnits.GasConstant * (temperatureCelsius + 273.15d));
            if (!FluidUnits.IsFinite(amount) || amount > (double)FluidInventory.MaximumMoles)
                throw new InvalidDataException("舱气数量超出流体支持范围");
            return AtmosphereService.Quantize((decimal)amount);
        }
        public static decimal TotalMoles(FluidInventoryState gas)
        {
            decimal amount = 0m;
            foreach (FluidComponentState component in gas.Components) amount += component.GasMoles;
            return amount;
        }
        public static double GetGasMassKg(ShipCompartmentState room)
        {
            double mass = 0d;
            foreach (FluidComponentState component in room.Gas.Components)
                mass += (double)component.GasMoles * FluidCatalog.Default.Find(component.FluidId).MolarMassKgPerMol;
            return mass;
        }
        #endregion

        #region 支撑与结构温压损伤
        public static ShipEnvironmentSnapshot GetEnvironment(ShipState ship, int cellX, int cellY,
            IShipAtmosphereSource outside = null)
        {
            bool support = HasFloor(ship, cellX, cellY);
            ShipCompartmentState room = FindCompartment(ship, cellX, cellY);
            if (room == null)
                return new ShipEnvironmentSnapshot(false, support, outside?.PressureKPa ?? 0d,
                    outside?.TemperatureCelsius ?? -270d, 0d, 0d, null);
            var inventory = new FluidInventory(room.Gas);
            double pressure = inventory.GetPressureKPa(room.VolumeLiters, room.VolumeLiters);
            decimal total = inventory.GasMoles;
            double oxygen = total > 0m ? pressure * (double)(inventory.GetGasMoles(FluidIds.Oxygen) / total) : 0d;
            double carbonDioxide = total > 0m ? pressure * (double)(inventory.GetGasMoles(FluidIds.CarbonDioxide) / total) : 0d;
            return new ShipEnvironmentSnapshot(true, support, pressure, ship.NormalCabinTemperatureCelsius,
                oxygen, carbonDioxide, room.CompartmentId);
        }

        public static bool HasFloor(ShipState ship, int cellX, int cellY)
        {
            var target = new ShipCell(cellX, cellY);
            foreach (ShipPieceState piece in ship.Pieces)
                if (piece != null && piece.IsAlive && piece.Kind == ShipPieceKind.Floor)
                    foreach (ShipCell cell in ShipGeometry.Footprint(piece)) if (cell.Equals(target)) return true;
            return false;
        }

        public static ShipCompartmentState FindCompartment(ShipState ship, int cellX, int cellY)
        {
            var target = new ShipCell(cellX, cellY);
            foreach (ShipCompartmentState room in ship.Compartments)
                foreach (ShipCell cell in room.Cells) if (cell.Equals(target)) return room;
            return null;
        }

        public static bool StepStructureEnvironment(ShipState ship, double pressureKPa, double temperatureCelsius,
            double seconds, Action<ShipState, ShipPieceState, string> onDestroyed = null)
        {
            if (!FluidUnits.IsFinite(seconds) || seconds <= 0d || !FluidUnits.IsFinite(pressureKPa) || pressureKPa < 0d ||
                !FluidUnits.IsFinite(temperatureCelsius)) return false;
            bool changed = false;
            foreach (ShipPieceState piece in new List<ShipPieceState>(ship.Pieces))
            {
                if (piece == null || !piece.IsAlive) continue;
                double pressureDamage = 0d;
                if (piece.Kind == ShipPieceKind.Wall || piece.Kind == ShipPieceKind.Door || piece.Kind == ShipPieceKind.DockingPort)
                    pressureDamage = OutOfRange(pressureKPa, piece.MinimumPressureKPa, piece.MaximumPressureKPa) *
                        Math.Max(0d, piece.PressureDamagePerKPaSecond);
                double temperatureDamage = OutOfRange(temperatureCelsius,
                    piece.MinimumTemperatureCelsius, piece.MaximumTemperatureCelsius) *
                    Math.Max(0d, piece.TemperatureDamagePerCelsiusSecond);
                double damage = (pressureDamage + temperatureDamage) * seconds;
                if (damage > 0d)
                    changed |= ShipStructureService.ApplyDamage(ship, piece.PieceId, damage, "outside-temperature-pressure", onDestroyed);
            }
            return changed;
        }
        private static double OutOfRange(double value, double minimum, double maximum) =>
            Math.Max(0d, Math.Max(minimum - value, value - maximum));
        #endregion
    }
}
