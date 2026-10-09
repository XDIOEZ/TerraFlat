using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed class PlanetShipAtmosphereSource : IShipAtmosphereSource
    {
        #region 真实星球大气适配
        private readonly AtmosphereState atmosphere;
        private readonly double temperature;
        public PlanetShipAtmosphereSource(AtmosphereState state, double temperatureCelsius)
        { atmosphere = state; temperature = temperatureCelsius; }
        public double PressureKPa => AtmosphereService.PressureKPa(atmosphere);
        public double TemperatureCelsius => temperature;
        public IReadOnlyList<FluidBatch> TakeGas(decimal maximumMoles)
        {
            var batches = new List<FluidBatch>();
            decimal total = atmosphere?.CurrentMoles ?? 0m;
            if (total <= 0m || maximumMoles <= 0m) return batches;
            decimal amount = Math.Min(maximumMoles, total);
            decimal remaining = amount;
            for (int index = 0; index < atmosphere.Gases.Count; index++)
            {
                AtmosphereGasAmount gas = atmosphere.Gases[index];
                decimal taken = index == atmosphere.Gases.Count - 1 ? Math.Min(gas.Moles, remaining)
                    : AtmosphereService.Quantize(amount * gas.Moles / total);
                if (taken <= 0m) continue;
                gas.Moles -= taken; remaining -= taken;
                batches.Add(FluidInventory.CreateBatch(FluidCatalog.Default.Find(gas.FluidId), taken, 0m, atmosphere.TemperatureKelvin));
            }
            return batches;
        }
        public void ReleaseGas(FluidBatch gas) { if (atmosphere != null) AtmosphereService.Emit(atmosphere, gas); }
        #endregion
    }
    public sealed class TankShipAtmosphereSource : IShipAtmosphereSource
    {
        #region 实际供气罐适配
        private readonly FluidInventory inventory;
        private readonly double volume, minimumVolume;
        public TankShipAtmosphereSource(FluidInventory inventory, double volume, double minimumVolume)
        { this.inventory = inventory; this.volume = volume; this.minimumVolume = minimumVolume; }
        public double PressureKPa => inventory.GetPressureKPa(volume, minimumVolume);
        public double TemperatureCelsius => inventory.GetTemperatureKelvin() - 273.15d;
        public IReadOnlyList<FluidBatch> TakeGas(decimal maximumMoles)
        {
            var batches = new List<FluidBatch>();
            decimal total = 0m;
            foreach (FluidComponentState gas in inventory.State.Components) total += inventory.GetAvailableMoles(gas.FluidId, FluidPhase.Gas);
            if (total <= 0m || maximumMoles <= 0m) return batches;
            decimal amount = Math.Min(maximumMoles, total);
            foreach (FluidComponentState gas in inventory.State.Components.ToArray())
            {
                decimal taken = AtmosphereService.Quantize(amount * inventory.GetAvailableMoles(gas.FluidId, FluidPhase.Gas) / total);
                if (taken > 0m && inventory.TryTakeExact(gas.FluidId, FluidPhase.Gas, taken, out FluidBatch batch)) batches.Add(batch);
            }
            return batches;
        }
        public void ReleaseGas(FluidBatch gas) { if (!inventory.TryAdd(gas, volume, minimumVolume)) throw new InvalidOperationException("供气回滚失败。"); }
        #endregion
    }
    public sealed partial class SpaceSession
    {
        #region 局部环境与主动供氧
        private const decimal AmbientMolesPerOxygenUnit = .0005m;
        public IShipAtmosphereSource GetOutside(ShipState ship)
        {
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight == null || flight.Phase is ShipFlightPhase.Orbit or ShipFlightPhase.Descending)
                return new PlanetShipAtmosphereSource(null, -270d);
            BodyState body = FindSurfaceBody(flight.SurfaceWorldKey);
            if (body == null || !owner.PlanetData_Dict.TryGetValue(body.PlanetId, out PlanetData planet))
                return new PlanetShipAtmosphereSource(null, -270d);
            double temperature = SpaceSurfaceQuery.GetLandingSurface(body.PlanetId, new Vector2((float)ship.PositionX, (float)ship.PositionY)).TemperatureCelsius;
            return new PlanetShipAtmosphereSource(AtmosphereService.GetOrCreate(planet), temperature);
        }
        public bool TrySample(Item item, out ItemEnvironmentSample sample)
        {
            sample = default;
            if (!running || item == null || State == null) return false;
            if (TryGetSupport(item.transform.position, out ShipState ship, out _, out Vector2 local))
            {
                int x = (int)Math.Floor(local.x / ship.CellSizeMeters), y = (int)Math.Floor(local.y / ship.CellSizeMeters);
                ShipEnvironmentSnapshot environment = ShipAtmosphereService.GetEnvironment(ship, x, y, GetOutside(ship));
                bool breathe = environment.CanBreathe;
                if (!environment.IsSealed && !IsSpaceView && AtmosphereService.TryGetForWorld(item.gameObject.scene.name, out AtmosphereState atmosphere))
                    breathe = AtmosphereService.CanBreathe(atmosphere);
                sample = new ItemEnvironmentSample(environment.PressureKPa, environment.TemperatureCelsius, breathe,
                    environment.HasFloorSupport, HasArtificialGravity(ship, local.x, local.y));
                return true;
            }
            if (IsSpaceView) { sample = new ItemEnvironmentSample(0d, -270d, false, false, false); return true; }
            return false;
        }
        public float SupplyAmbientOxygen(Item item, float maximumAmount)
        {
            if (maximumAmount <= 0f || !TryGetSupport(item.transform.position, out ShipState ship, out _, out Vector2 local)) return 0f;
            int x = (int)Math.Floor(local.x / ship.CellSizeMeters), y = (int)Math.Floor(local.y / ship.CellSizeMeters);
            ShipCompartmentState room = ship.Compartments.Find(value => value.Cells.Exists(cell => cell.X == x && cell.Y == y));
            decimal requested = AtmosphereService.Quantize((decimal)maximumAmount * AmbientMolesPerOxygenUnit);
            if (room == null)
            {
                ShipFlightState flight = GetFlight(ship.ShipId);
                if (IsSpaceView || !owner.PlanetData_Dict.TryGetValue(flight.SurfaceWorldKey, out PlanetData planet)) return 0f;
                AtmosphereState air = AtmosphereService.GetOrCreate(planet);
                foreach (AtmosphereGasAmount gas in air.Gases)
                    if (gas.FluidId == FluidIds.Oxygen)
                    {
                        decimal consumed = Math.Min(requested, gas.Moles); if (consumed <= 0m) return 0f; gas.Moles -= consumed;
                        AtmosphereService.Emit(air, FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.CarbonDioxide), consumed, 0m, air.TemperatureKelvin));
                        return (float)(consumed / AmbientMolesPerOxygenUnit);
                    }
                return 0f;
            }
            var inventory = new FluidInventory(room.Gas);
            decimal amount = Math.Min(requested, inventory.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas));
            if (amount <= 0m || !inventory.TryTakeExact(FluidIds.Oxygen, FluidPhase.Gas, amount, out _)) return 0f;
            FluidBatch exhaled = FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.CarbonDioxide), amount, 0m, ship.NormalCabinTemperatureCelsius + 273.15d);
            if (!inventory.TryAdd(exhaled, room.VolumeLiters, .01d)) throw new InvalidOperationException("舱室呼吸气体结算失败。");
            return (float)(amount / AmbientMolesPerOxygenUnit);
        }
        private void RefillCompartments(ShipState ship, float seconds)
        {
            using (MachineWorld.UseScope("ship:" + ship.ShipId))
            foreach (ShipPieceState piece in ship.Pieces)
            {
                ShipDeviceState device = GetDevice(piece.PieceId);
                if (!piece.IsAlive || device?.Configuration.Kind != ShipPieceKind.Equipment ||
                    device.Configuration.SupplyMolesPerSecond <= 0f || !IsPowered(ship, piece) ||
                    !int.TryParse(piece.PieceId, out int id) || MachineWorld.GetById(id) is not MachineEntity machine || machine.Definition.Fluid == null) continue;
                var source = new TankShipAtmosphereSource(MachineWorld.GetFluidInventory(machine), MachineWorld.GetFluidVolumeLiters(machine), MachineWorld.GetFluidMinimumGasSpaceLiters(machine));
                ShipAtmosphereService.TrySupplyGas(ship, piece.CellX, piece.CellY, source, device.TargetPressureKPa,
                    (decimal)(device.Configuration.SupplyMolesPerSecond * seconds), out _);
            }
        }
        #endregion
    }
}
