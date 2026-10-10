using System;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 船上机器读取与移交真实环境
        public bool TryGetMachineEnvironment(string shipId, Vector2 localPosition, out ShipEnvironmentSnapshot environment,
            out decimal availableGasMoles)
        {
            environment = default; availableGasMoles = 0m;
            ShipState ship = GetShip(shipId);
            if (ship == null) return false;
            EnsureMachineCompartmentGeometry(ship);
            int x = (int)Math.Floor(localPosition.x / ship.CellSizeMeters), y = (int)Math.Floor(localPosition.y / ship.CellSizeMeters);
            environment = ShipAtmosphereService.GetEnvironment(ship, x, y, GetMachineExteriorAt(ship, localPosition));
            ShipCompartmentState room = ShipAtmosphereService.FindCompartment(ship, x, y);
            if (room == null) availableGasMoles = GetMachineOutsideAtmosphere(ship)?.CurrentMoles ?? 0m;
            else
            {
                var inventory = new FluidInventory(room.Gas);
                foreach (FluidComponentState gas in room.Gas.Components)
                    availableGasMoles += inventory.GetAvailableMoles(gas.FluidId, FluidPhase.Gas);
            }
            return true;
        }

        public bool TryExtractMachineGasTo(string shipId, Vector2 localPosition, decimal standardLiters, ref ulong randomState,
            FluidInventory target, double volumeLiters, double minimumGasSpaceLiters, out FluidBatch batch)
        {
            batch = default;
            ShipState ship = GetShip(shipId);
            if (ship == null || target == null || standardLiters <= 0m) return false;
            EnsureMachineCompartmentGeometry(ship);
            int x = (int)Math.Floor(localPosition.x / ship.CellSizeMeters), y = (int)Math.Floor(localPosition.y / ship.CellSizeMeters);
            ShipCompartmentState room = ShipAtmosphereService.FindCompartment(ship, x, y);
            if (room == null)
                return AtmosphereService.TryExtractTo(GetMachineOutsideAtmosphere(ship), standardLiters, ref randomState,
                    target, volumeLiters, minimumGasSpaceLiters, out batch);
            var inventory = new FluidInventory(room.Gas);
            decimal total = 0m;
            foreach (FluidComponentState gas in room.Gas.Components)
                total += inventory.GetAvailableMoles(gas.FluidId, FluidPhase.Gas);
            decimal requested = AtmosphereService.Quantize(FluidUnits.StandardLitersToMol(standardLiters));
            if (total <= 0m || requested <= 0m) return false;
            ulong nextRandom = randomState;
            decimal roll = FluidRandom.Next01(ref nextRandom) * total;
            FluidComponentState selected = null;
            foreach (FluidComponentState gas in room.Gas.Components)
            {
                decimal available = inventory.GetAvailableMoles(gas.FluidId, FluidPhase.Gas);
                if (available <= 0m) continue;
                selected = gas;
                if (roll < available) break;
                roll -= available;
            }
            if (selected == null) return false;
            decimal amount = Math.Min(requested, inventory.GetAvailableMoles(selected.FluidId, FluidPhase.Gas));
            FluidBatch planned = FluidInventory.CreateBatch(inventory.Catalog.Find(selected.FluidId), amount, 0m, inventory.GetTemperatureKelvin());
            if (!target.CanAdd(planned, volumeLiters, minimumGasSpaceLiters, true, true)) return false;
            // 先预检目标，再扣真实舱气；抽气结果沿原气包概率，不复制一份星球空气。
            if (!ShipAtmosphereService.TryTakeGas(ship, x, y, selected.FluidId, amount, out batch)) return false;
            if (!target.TryAdd(batch, volumeLiters, minimumGasSpaceLiters, true, true))
            {
                if (!ShipAtmosphereService.TryAddGas(ship, x, y, batch)) throw new InvalidOperationException("机器抽舱气回滚失败。");
                batch = default; return false;
            }
            randomState = nextRandom; return true;
        }

        public AtmosphereEmissionResult EmitMachineGas(string shipId, Vector2 localPosition, FluidBatch batch)
        {
            ShipState ship = GetShip(shipId) ?? throw new InvalidOperationException("机器排气时缺少真实船体。");
            EnsureMachineCompartmentGeometry(ship);
            int x = (int)Math.Floor(localPosition.x / ship.CellSizeMeters), y = (int)Math.Floor(localPosition.y / ship.CellSizeMeters);
            if (ShipAtmosphereService.FindCompartment(ship, x, y) == null)
                return AtmosphereService.Emit(GetMachineOutsideAtmosphere(ship), batch);
            if (!ShipAtmosphereService.TryAddGas(ship, x, y, batch)) throw new InvalidOperationException("真实排气批次无法交给密闭舱。");
            return new AtmosphereEmissionResult(batch.GasMoles, batch.GasMoles);
        }

        public AtmosphereState GetMachineAirSnapshot(string shipId, Vector2 localPosition)
        {
            ShipState ship = GetShip(shipId);
            if (ship == null) return null;
            EnsureMachineCompartmentGeometry(ship);
            int x = (int)Math.Floor(localPosition.x / ship.CellSizeMeters), y = (int)Math.Floor(localPosition.y / ship.CellSizeMeters);
            ShipCompartmentState room = ShipAtmosphereService.FindCompartment(ship, x, y);
            if (room == null) return GetMachineOutsideAtmosphere(ship);
            var snapshot = new AtmosphereState { DefinitionId = room.CompartmentId, CapacityMoles = FluidInventory.MaximumMoles,
                ReferenceMoles = ShipAtmosphereService.MolesAtPressure(101.325d, room.VolumeLiters, ship.NormalCabinTemperatureCelsius),
                ReferencePressureKPa = 101.325d, TemperatureKelvin = ship.NormalCabinTemperatureCelsius + 273.15d };
            foreach (FluidComponentState gas in room.Gas.Components)
                if (gas.GasMoles > 0m) snapshot.Gases.Add(new AtmosphereGasAmount { FluidId = gas.FluidId, Moles = gas.GasMoles });
            return snapshot;
        }

        public bool TryResolveMachineSurfacePoint(string shipId, Vector2 localPosition, out string world, out Vector2 position)
        {
            world = null; position = default;
            ShipState ship = GetShip(shipId);
            ShipFlightState flight = GetFlight(shipId);
            if (ship == null || flight == null || flight.Phase is not (ShipFlightPhase.Landed or ShipFlightPhase.Floating) || flight.HeightMeters > .001d)
                return false;
            ShipGeometry.LocalToWorld(ship, localPosition.x, localPosition.y, out double x, out double y);
            world = flight.SurfaceWorldKey;
            position = PressureExplosionQueue.NormalizeInWorld(world, new Vector2((float)x, (float)y));
            return !string.IsNullOrWhiteSpace(world);
        }

        // 释放物料保留当时的真实世界与绝对位置，整船回收后不再依赖已消失的船体。
        public bool TryGetMachineSpillAnchor(string shipId, Vector2 localPosition, out string world, out Vector2 position)
        {
            world = null; position = default;
            ShipState ship = GetShip(shipId);
            if (ship == null) return false;
            ShipFlightState flight = GetFlight(shipId);
            bool space = flight?.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending;
            world = space ? "SpaceScene" : flight?.SurfaceWorldKey ?? ship.WorldAddress;
            if (string.IsNullOrWhiteSpace(world)) return false;
            ShipGeometry.LocalToWorld(ship, localPosition.x, localPosition.y, out double x, out double y);
            position = new Vector2((float)x, (float)y);
            if (!space) position = PressureExplosionQueue.NormalizeInWorld(world, position);
            return true;
        }

        private AtmosphereState GetMachineOutsideAtmosphere(ShipState ship)
        {
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight == null || flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending) return null;
            return AtmosphereService.TryGetForWorld(flight.SurfaceWorldKey ?? ship.WorldAddress, out AtmosphereState atmosphere) ? atmosphere : null;
        }
        private void EnsureMachineCompartmentGeometry(ShipState ship)
        {
            // 同轮连锁破壁后立即失去旧密闭环境，未改结构的机器查询复用现有舱室。
            if (ship.AtmosphereStructureDirty) ShipAtmosphereService.RebuildCompartments(ship, GetOutside(ship));
        }
        private IShipAtmosphereSource GetMachineExteriorAt(ShipState ship, Vector2 localPosition)
        {
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight == null || flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending)
                return new PlanetShipAtmosphereSource(null, -270d);
            string world = flight.SurfaceWorldKey ?? ship.WorldAddress;
            ShipGeometry.LocalToWorld(ship, localPosition.x, localPosition.y, out double x, out double y);
            Vector2 position = PressureExplosionQueue.NormalizeInWorld(world, new Vector2((float)x, (float)y));
            double temperature = SpaceSurfaceQuery.GetLandingSurface(world, position).TemperatureCelsius;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == world && TemperatureMgr.Instance != null &&
                TemperatureMgr.Instance.TryGetAmbientTemperature(position, out float actual)) temperature = actual;
            return new PlanetShipAtmosphereSource(GetMachineOutsideAtmosphere(ship), temperature);
        }
        #endregion
    }
}
