using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

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
        // 船舱只存气相，保留极小的正空间以满足通用流体几何约束。
        internal const double MinimumCabinGasSpaceLiters = 0.001d;

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
        private static bool IsBoundary(ShipPieceState piece) => piece.SealsAtmosphere;
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
        private sealed class RoomInventoryCache
        {
            public FluidInventory Inventory;
            public FluidInventory Get(ShipCompartmentState room)
            {
                if (Inventory == null || !ReferenceEquals(Inventory.State, room.Gas) ||
                    !ReferenceEquals(Inventory.Catalog, FluidCatalog.Default))
                    Inventory = new FluidInventory(room.Gas);
                return Inventory;
            }
        }
        private static readonly ConditionalWeakTable<ShipCompartmentState, RoomInventoryCache> roomInventories = new();
        private static readonly ConditionalWeakTable<ShipCompartmentState, RoomInventoryCache>.CreateValueCallback roomInventoryFactory = _ => new();
        internal static FluidInventory GetCompartmentInventory(ShipCompartmentState room)
        {
            // 舱气包装复用真实库存对象，换气体状态或材料目录时重新绑定。
            return roomInventories.GetValue(room, roomInventoryFactory).Get(room);
        }

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
            return result;
        }

        public static bool TryTakeGas(ShipState ship, int cellX, int cellY, string fluidId,
            decimal requestedMoles, out FluidBatch batch)
        {
            batch = default;
            ShipCompartmentState room = FindCompartment(ship, cellX, cellY);
            if (room == null || requestedMoles <= 0m) return false;
            var inventory = GetCompartmentInventory(room);
            decimal amount = Math.Min(requestedMoles, inventory.GetAvailableMoles(fluidId, FluidPhase.Gas));
            if (amount <= 0m || !inventory.TryTakeExact(fluidId, FluidPhase.Gas, amount, out batch)) return false;
            NormalizeTemperature(room.Gas, ship.NormalCabinTemperatureCelsius);
            return true;
        }

        public static bool TryAddGas(ShipState ship, int cellX, int cellY, FluidBatch batch)
        {
            ShipCompartmentState room = FindCompartment(ship, cellX, cellY);
            if (room == null || !batch.IsGas) return false;
            if (!GetCompartmentInventory(room).CanAdd(batch, room.VolumeLiters, MinimumCabinGasSpaceLiters)) return false;
            AddGas(room, batch, ship.NormalCabinTemperatureCelsius);
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
            var inventory = GetCompartmentInventory(room);
            if (!inventory.TryAdd(batch, room.VolumeLiters, MinimumCabinGasSpaceLiters))
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
        private sealed class PressureDamagePattern
        {
            public bool HasLine;
            public ShipDamageLine Line;
            public readonly List<ShipPieceState> Pieces = new();
        }
        private static readonly ConditionalWeakTable<ShipState, PressureDamagePattern> pressurePatterns = new();
        private static readonly Random pressurePatternRandom = new();

        #region 舱室与支撑格子索引
        private sealed class GeometryIndex
        {
            private List<ShipCompartmentState> rooms;
            private List<ShipPieceState> pieces;
            private ulong structureRevision;
            private int roomCount, pieceCount;
            public readonly Dictionary<(int X, int Y), ShipCompartmentState> Rooms = new();
            public readonly Dictionary<(int X, int Y), ShipPieceState> Floors = new();
            public void Ensure(ShipState ship)
            {
                if (ReferenceEquals(rooms, ship.Compartments) && ReferenceEquals(pieces, ship.Pieces) &&
                    structureRevision == ship.StructureRevision && roomCount == ship.Compartments.Count && pieceCount == ship.Pieces.Count) return;
                Rooms.Clear(); Floors.Clear();
                foreach (ShipCompartmentState room in ship.Compartments)
                    foreach (ShipCell cell in room.Cells) Rooms.TryAdd((cell.X, cell.Y), room);
                foreach (ShipPieceState piece in ship.Pieces)
                {
                    if (piece?.IsAlive != true || piece.Kind != ShipPieceKind.Floor) continue;
                    int width = Math.Max(1, piece.Width), height = Math.Max(1, piece.Height);
                    if ((piece.QuarterTurns & 1) != 0) { int swap = width; width = height; height = swap; }
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                            Floors.TryAdd((piece.CellX + x, piece.CellY + y), piece);
                }
                rooms = ship.Compartments; pieces = ship.Pieces;
                roomCount = rooms.Count; pieceCount = pieces.Count; structureRevision = ship.StructureRevision;
            }
        }
        private static readonly ConditionalWeakTable<ShipState, GeometryIndex> geometryIndexes = new();
        private static readonly ConditionalWeakTable<ShipState, GeometryIndex>.CreateValueCallback geometryFactory = _ => new();
        private static GeometryIndex GetGeometryIndex(ShipState ship)
        {
            // 结构或舱室集合变化才建格子索引，环境热查询不再扫描整船。
            GeometryIndex result = geometryIndexes.GetValue(ship, geometryFactory);
            result.Ensure(ship); return result;
        }
        #endregion
        public static ShipEnvironmentSnapshot GetEnvironment(ShipState ship, int cellX, int cellY,
            IShipAtmosphereSource outside = null)
        {
            bool support = HasFloor(ship, cellX, cellY);
            ShipCompartmentState room = FindCompartment(ship, cellX, cellY);
            if (room == null)
                return new ShipEnvironmentSnapshot(false, support, outside?.PressureKPa ?? 0d,
                    outside?.TemperatureCelsius ?? -270d, 0d, 0d, null);
            var inventory = GetCompartmentInventory(room);
            double pressure = inventory.GetPressureKPa(room.VolumeLiters, MinimumCabinGasSpaceLiters);
            decimal total = inventory.GasMoles;
            double oxygen = total > 0m ? pressure * (double)(inventory.GetGasMoles(FluidIds.Oxygen) / total) : 0d;
            double carbonDioxide = total > 0m ? pressure * (double)(inventory.GetGasMoles(FluidIds.CarbonDioxide) / total) : 0d;
            return new ShipEnvironmentSnapshot(true, support, pressure, ship.NormalCabinTemperatureCelsius,
                oxygen, carbonDioxide, room.CompartmentId);
        }

        public static bool HasFloor(ShipState ship, int cellX, int cellY)
            => ship != null && GetGeometryIndex(ship).Floors.ContainsKey((cellX, cellY));

        /// <summary>使用已有结构版本索引定位具体地板，避免每次环境查询扫描整船部件。</summary>
        public static bool TryGetFloor(ShipState ship, int cellX, int cellY, out ShipPieceState floor)
        {
            floor = null;
            return ship != null && GetGeometryIndex(ship).Floors.TryGetValue((cellX, cellY), out floor);
        }

        public static ShipCompartmentState FindCompartment(ShipState ship, int cellX, int cellY)
        {
            return ship != null && GetGeometryIndex(ship).Rooms.TryGetValue((cellX, cellY), out ShipCompartmentState room)
                ? room : null;
        }

        public static bool StepStructureEnvironment(ShipState ship, double pressureKPa, double temperatureCelsius,
            double seconds, Action<ShipState, ShipPieceState, string> onDestroyed = null)
        {
            if (ship == null || !FluidUnits.IsFinite(seconds) || seconds <= 0d || !FluidUnits.IsFinite(pressureKPa) || pressureKPa < 0d ||
                !FluidUnits.IsFinite(temperatureCelsius)) return false;
            Predicate<ShipPieceState> pressureEligible = piece => piece?.IsAlive == true &&
                (piece.Kind == ShipPieceKind.Wall || piece.Kind == ShipPieceKind.Door || piece.Kind == ShipPieceKind.DockingPort) &&
                piece.PressureDamagePerKPaSecond > 0d &&
                OutOfRange(pressureKPa, piece.MinimumPressureKPa, piece.MaximumPressureKPa) > 0d;
            PressureDamagePattern pattern = pressurePatterns.GetValue(ship, _ => new PressureDamagePattern());
            bool lineStillLoaded = false;
            if (pattern.HasLine)
                foreach (ShipPieceState piece in ship.Pieces)
                    if (pressureEligible(piece) && ShipDamagePattern.Intersects(ship, piece, pattern.Line))
                    { lineStillLoaded = true; break; }
            if (!lineStillLoaded)
                pattern.HasLine = ShipDamagePattern.TryCreateLine(ship, pressurePatternRandom.NextDouble(),
                    pressurePatternRandom.NextDouble(), out pattern.Line, pressureEligible);
            bool changed = false;
            pattern.Pieces.Clear(); pattern.Pieces.AddRange(ship.Pieces);
            foreach (ShipPieceState piece in pattern.Pieces)
            {
                if (piece == null || !piece.IsAlive) continue;
                double pressureDamage = 0d;
                // 持续压力集中在同一条受力线，断开后才重新选线，避免整圈舱壁一起平均掉血。
                if (pattern.HasLine && pressureEligible(piece) && ShipDamagePattern.Intersects(ship, piece, pattern.Line))
                    pressureDamage = OutOfRange(pressureKPa, piece.MinimumPressureKPa, piece.MaximumPressureKPa) *
                        Math.Max(0d, piece.PressureDamagePerKPaSecond);
                bool interior = piece.Kind is not (ShipPieceKind.Floor or ShipPieceKind.Wall or ShipPieceKind.Door or ShipPieceKind.DockingPort) &&
                    FindCompartment(ship, piece.CellX, piece.CellY) != null;
                // 真空的占位温度不是冷空气；舱内设备读舱温，船外有真实气体时才结算环境传热。
                double materialTemperature = interior ? ship.NormalCabinTemperatureCelsius : temperatureCelsius;
                double temperatureDamage = interior || pressureKPa > 1e-6d ? OutOfRange(materialTemperature,
                    piece.MinimumTemperatureCelsius, piece.MaximumTemperatureCelsius) *
                    Math.Max(0d, piece.TemperatureDamagePerCelsiusSecond) : 0d;
                double damage = (pressureDamage + temperatureDamage) * seconds;
                if (damage > 0d)
                {
                    string reason = pressureDamage > 0d && temperatureDamage > 0d ? "outside-temperature-pressure"
                        : pressureDamage > 0d ? "outside-pressure" : "outside-temperature";
                    changed |= ShipStructureService.ApplyDamage(ship, piece.PieceId, damage, reason, onDestroyed);
                }
            }
            return changed;
        }
        private static double OutOfRange(double value, double minimum, double maximum) =>
            Math.Max(0d, Math.Max(minimum - value, value - maximum));
        #endregion
    }
}
