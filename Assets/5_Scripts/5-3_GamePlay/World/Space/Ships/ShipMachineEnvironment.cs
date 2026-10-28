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
            availableGasMoles = 0m;
            if (!TryResolveMachineEnvironment(shipId, localPosition, out environment, out ShipState ship, out int x, out int y))
                return false;
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

        // 温度、压力与浸水查询只需要舱室环境，不为未使用的气体余量创建库存包装。
        public bool TryGetMachineEnvironment(string shipId, Vector2 localPosition, out ShipEnvironmentSnapshot environment)
            => TryResolveMachineEnvironment(shipId, localPosition, out environment, out _, out _, out _);

        public bool TryGetMachineAirSource(string shipId, Vector2 localPosition, out IShipAtmosphereSource source)
        {
            source = null;
            if (!TryResolveMachineEnvironment(shipId, localPosition, out _, out ShipState ship, out int x, out int y))
                return false;
            ShipCompartmentState room = ShipAtmosphereService.FindCompartment(ship, x, y);
            // 有气密舱就取舱内真实库存，敞开区域沿用所属船体的外界气源。
            source = room != null
                ? new TankShipAtmosphereSource(ShipAtmosphereService.GetCompartmentInventory(room),
                    room.VolumeLiters, ShipAtmosphereService.MinimumCabinGasSpaceLiters)
                : GetMachineExteriorAt(ship, localPosition);
            return source != null;
        }

        private bool TryResolveMachineEnvironment(string shipId, Vector2 localPosition, out ShipEnvironmentSnapshot environment,
            out ShipState ship, out int x, out int y)
        {
            environment = default; ship = ResolveAtmosphereGeometry(shipId); x = 0; y = 0;
            if (ship == null) return false;
            EnsureMachineCompartmentGeometry(ship);
            x = (int)Math.Floor(localPosition.x / ship.CellSizeMeters);
            y = (int)Math.Floor(localPosition.y / ship.CellSizeMeters);
            environment = ShipAtmosphereService.GetEnvironment(ship, x, y);
            if (!environment.IsSealed)
                environment = ShipAtmosphereService.GetEnvironment(ship, x, y, GetMachineExteriorAt(ship, localPosition));
            return true;
        }

        public bool TryExtractMachineGasTo(string shipId, Vector2 localPosition, decimal standardLiters, ref ulong randomState,
            FluidInventory target, double volumeLiters, double minimumGasSpaceLiters, out FluidBatch batch)
        {
            batch = default;
            ShipState ship = ResolveAtmosphereGeometry(shipId);
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
            ShipState ship = ResolveAtmosphereGeometry(shipId) ?? throw new InvalidOperationException("机器排气时缺少真实气密区域。");
            EnsureMachineCompartmentGeometry(ship);
            int x = (int)Math.Floor(localPosition.x / ship.CellSizeMeters), y = (int)Math.Floor(localPosition.y / ship.CellSizeMeters);
            if (ShipAtmosphereService.FindCompartment(ship, x, y) == null)
                return AtmosphereService.Emit(GetMachineOutsideAtmosphere(ship), batch);
            if (!ShipAtmosphereService.TryAddGas(ship, x, y, batch)) throw new InvalidOperationException("真实排气批次无法交给密闭舱。");
            return new AtmosphereEmissionResult(batch.GasMoles, batch.GasMoles);
        }

        public AtmosphereState GetMachineAirSnapshot(string shipId, Vector2 localPosition)
        {
            ShipState ship = ResolveAtmosphereGeometry(shipId);
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
            bool space = flight?.Phase is ShipFlightPhase.Cruising;
            world = space ? SpaceSectorCoordinates.SceneKey(ship.Sector) : flight?.SurfaceWorldKey ?? ship.WorldAddress;
            if (string.IsNullOrWhiteSpace(world)) return false;
            ShipGeometry.LocalToWorld(ship, localPosition.x, localPosition.y, out double x, out double y);
            position = new Vector2((float)x, (float)y);
            if (space)
            {
                SpaceSectorPosition point = SpaceSectorCoordinates.Normalize(ship.Sector, new SpaceVector2(x, y));
                world = SpaceSectorCoordinates.SceneKey(point.Sector);
                position = point.LocalMeters.ToVector2();
            }
            if (!space) position = PressureExplosionQueue.NormalizeInWorld(world, position);
            return true;
        }

        private AtmosphereState GetMachineOutsideAtmosphere(ShipState ship)
        {
            if (ship.RetainsWorldWithoutStructure) return null;
            if (IsGroundAtmosphere(ship))
                return AtmosphereService.TryGetForWorld(ship.WorldAddress, out AtmosphereState groundAir) ? groundAir : null;
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight == null || flight.Phase is ShipFlightPhase.Cruising) return null;
            return AtmosphereService.TryGetForWorld(flight.SurfaceWorldKey ?? ship.WorldAddress, out AtmosphereState atmosphere) ? atmosphere : null;
        }
        private void EnsureMachineCompartmentGeometry(ShipState ship)
        {
            // 同轮连锁破壁后立即失去旧密闭环境，未改结构的机器查询复用现有舱室。
            if (ship.AtmosphereStructureDirty) ShipAtmosphereService.RebuildCompartments(ship,
                IsGroundAtmosphere(ship) ? GetGroundOutside(ship.WorldAddress) : GetOutside(ship));
        }
        private IShipAtmosphereSource GetMachineExteriorAt(ShipState ship, Vector2 localPosition)
        {
            if (ship.RetainsWorldWithoutStructure) return PlanetShipAtmosphereSource.Vacuum;
            if (IsGroundAtmosphere(ship)) return GetGroundOutside(ship.WorldAddress);
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight == null || flight.Phase is ShipFlightPhase.Cruising)
                return PlanetShipAtmosphereSource.Vacuum;
            string world = flight.SurfaceWorldKey ?? ship.WorldAddress;
            ShipGeometry.LocalToWorld(ship, localPosition.x, localPosition.y, out double x, out double y);
            Vector2 position = PressureExplosionQueue.NormalizeInWorld(world, new Vector2((float)x, (float)y));
            // 已加载地表按真实世界上下文取局部温度，冷地表复用单格采样。
            bool loaded = GameplayWorldRegistry.TryGet(world, out _);
            using var worldScope = loaded ? GameplayWorldRegistry.UseWorld(world) : null;
            double temperature = loaded &&
                TemperatureMgr.Instance != null && TemperatureMgr.Instance.TryGetAmbientTemperature(position, out float actual)
                ? actual : SpaceSurfaceQuery.GetAmbientTemperature(world, position);
            return new PlanetShipAtmosphereSource(GetMachineOutsideAtmosphere(ship), temperature);
        }
        #endregion
    }
}
