using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed class PlanetShipAtmosphereSource : IShipAtmosphereSource
    {
        #region 真实星球大气适配
        public static PlanetShipAtmosphereSource Vacuum { get; } = new(null, -270d);
        private readonly AtmosphereState atmosphere;
        private readonly double temperature;
        public PlanetShipAtmosphereSource(AtmosphereState state, double temperatureCelsius)
        { atmosphere = state; temperature = temperatureCelsius; }
        public double PressureKPa => AtmosphereService.PressureKPa(atmosphere);
        public double TemperatureCelsius => temperature;
        public IReadOnlyList<FluidBatch> TakeGas(decimal maximumMoles)
        {
            decimal total = atmosphere?.CurrentMoles ?? 0m;
            if (total <= 0m || maximumMoles <= 0m) return Array.Empty<FluidBatch>();
            var batches = new List<FluidBatch>();
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
            if (remaining < amount) atmosphere.Revision++;
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
        // 气罐优先从所在密闭舱扣真实气体，敞开环境才使用星球大气。
        public bool TryGetPortableTankAirSource(Item tank, out IShipAtmosphereSource source)
        {
            source = null;
            if (tank == null) return false;
            if (TryGetSupport(tank.transform.position, out ShipState ship, out _, out Vector2 local))
            {
                EnsureMachineCompartmentGeometry(ship);
                int x = (int)Math.Floor(local.x / ship.CellSizeMeters);
                int y = (int)Math.Floor(local.y / ship.CellSizeMeters);
                ShipCompartmentState room = ShipAtmosphereService.FindCompartment(ship, x, y);
                source = room != null
                    ? new TankShipAtmosphereSource(ShipAtmosphereService.GetCompartmentInventory(room),
                        room.VolumeLiters, ShipAtmosphereService.MinimumCabinGasSpaceLiters)
                    : GetOutside(ship);
                return source != null;
            }
            if (IsSpaceView) return false;
            string world = tank.gameObject.scene.name;
            Vector2 point = PressureExplosionQueue.NormalizeInWorld(world,
                WorldLocalPresentation.ToLogical((Vector2)tank.transform.position));
            if (TryGetGroundEnvironment(world, point, out _))
            {
                ShipState geometry = GetGroundAtmosphere(world, false);
                ShipCompartmentState room = ShipAtmosphereService.FindCompartment(geometry,
                    Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y));
                if (room == null) return false;
                source = new TankShipAtmosphereSource(ShipAtmosphereService.GetCompartmentInventory(room),
                    room.VolumeLiters, ShipAtmosphereService.MinimumCabinGasSpaceLiters);
                return true;
            }
            if (!AtmosphereService.TryGetForWorld(world, out AtmosphereState atmosphere))
                return false;
            source = new PlanetShipAtmosphereSource(atmosphere, atmosphere.TemperatureKelvin - 273.15d);
            return true;
        }

        private const decimal AmbientMolesPerOxygenUnit = .0005m;
        public IShipAtmosphereSource GetOutside(ShipState ship)
        {
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight == null || flight.Phase is ShipFlightPhase.Cruising)
                return PlanetShipAtmosphereSource.Vacuum;
            BodyState body = FindSurfaceBody(flight.SurfaceWorldKey);
            if (body == null || !owner.PlanetData_Dict.TryGetValue(body.PlanetId, out PlanetData planet))
                return PlanetShipAtmosphereSource.Vacuum;
            Vector2 position = new((float)ship.PositionX, (float)ship.PositionY);
            // 本地地表读实时温度，冷世界只采单格温度，避免完整区块生成。
            double temperature = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == flight.SurfaceWorldKey &&
                TemperatureMgr.Instance != null && TemperatureMgr.Instance.TryGetAmbientTemperature(position, out float ambient)
                ? ambient : SpaceSurfaceQuery.GetAmbientTemperature(body.PlanetId, position);
            return new PlanetShipAtmosphereSource(AtmosphereService.GetOrCreate(planet), temperature);
        }
        public bool TrySample(Item item, out ItemEnvironmentSample sample)
        {
            sample = default;
            if (!running || item == null || State == null) return false;
            if (TryGetSupport(item.transform.position, out ShipState ship, out _, out Vector2 local))
            {
                int x = (int)Math.Floor(local.x / ship.CellSizeMeters), y = (int)Math.Floor(local.y / ship.CellSizeMeters);
                ShipEnvironmentSnapshot environment = ShipAtmosphereService.GetEnvironment(ship, x, y);
                if (!environment.IsSealed)
                    environment = environment.WithOutside(GetOutside(ship));
                bool breathe = environment.CanBreathe;
                if (!environment.IsSealed && !IsSpaceView && AtmosphereService.TryGetForWorld(item.gameObject.scene.name, out AtmosphereState atmosphere))
                    breathe = AtmosphereService.CanBreathe(atmosphere);
                sample = new ItemEnvironmentSample(environment.PressureKPa, environment.TemperatureCelsius, breathe,
                    environment.HasFloorSupport, HasArtificialGravity(ship, local.x, local.y));
                return true;
            }
            if (IsSpaceView) { sample = new ItemEnvironmentSample(0d, -270d, false, false, false); return true; }
            return TrySampleGround(item, out sample);
        }
        public float SupplyAmbientOxygen(Item item, float maximumAmount)
        {
            if (maximumAmount <= 0f || item == null) return 0f;
            if (!TryGetSupport(item.transform.position, out ShipState ship, out _, out Vector2 local))
            {
                local = PressureExplosionQueue.NormalizeInWorld(item.gameObject.scene.name,
                    WorldLocalPresentation.ToLogical((Vector2)item.transform.position));
                if (!TryGetGroundEnvironment(item.gameObject.scene.name, local, out _)) return 0f;
                ship = GetGroundAtmosphere(item.gameObject.scene.name);
            }
            int x = (int)Math.Floor(local.x / ship.CellSizeMeters), y = (int)Math.Floor(local.y / ship.CellSizeMeters);
            ShipCompartmentState room = ShipAtmosphereService.FindCompartment(ship, x, y);
            decimal requested = AtmosphereService.Quantize((decimal)maximumAmount * AmbientMolesPerOxygenUnit);
            if (room == null)
            {
                ShipFlightState flight = GetFlight(ship.ShipId);
                if (IsSpaceView || !owner.PlanetData_Dict.TryGetValue(flight.SurfaceWorldKey, out PlanetData planet)) return 0f;
                AtmosphereState air = AtmosphereService.GetOrCreate(planet);
                foreach (AtmosphereGasAmount gas in air.Gases)
                    if (gas.FluidId == FluidIds.Oxygen)
                    {
                        decimal consumed = Math.Min(requested, gas.Moles); if (consumed <= 0m) return 0f; gas.Moles -= consumed; air.Revision++;
                        AtmosphereService.Emit(air, FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.CarbonDioxide), consumed, 0m, air.TemperatureKelvin));
                        return (float)(consumed / AmbientMolesPerOxygenUnit);
                    }
                return 0f;
            }
            var inventory = ShipAtmosphereService.GetCompartmentInventory(room);
            decimal amount = Math.Min(requested, inventory.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas));
            if (amount <= 0m || !inventory.TryTakeExact(FluidIds.Oxygen, FluidPhase.Gas, amount, out _)) return 0f;
            FluidBatch exhaled = FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.CarbonDioxide), amount, 0m, ship.NormalCabinTemperatureCelsius + 273.15d);
            if (!inventory.TryAdd(exhaled, room.VolumeLiters, ShipAtmosphereService.MinimumCabinGasSpaceLiters)) throw new InvalidOperationException("舱室呼吸气体结算失败。");
            return (float)(amount / AmbientMolesPerOxygenUnit);
        }
        private void RefillCompartments(ShipState ship, float seconds)
        {
            using (MachineWorld.UseScope(MachineWorld.GetShipScopeKey(ship.ShipId)))
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
