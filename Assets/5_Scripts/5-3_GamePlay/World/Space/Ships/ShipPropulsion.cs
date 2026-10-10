using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public readonly struct ShipActuator
    {
        public readonly ShipState Ship;
        public readonly ShipPieceState Piece;
        public readonly ShipDeviceState Device;
        public readonly SpaceVector2 Direction;
        public readonly double TorquePerNewton;
        public ShipActuator(ShipState ship, ShipPieceState piece, ShipDeviceState device, double centerX, double centerY)
        {
            Ship = ship; Piece = piece; Device = device;
            Direction = SpaceVector2.FromAngle(ship.AngleRadians + (piece.QuarterTurns + 1) * Math.PI / 2d);
            ShipGeometry.LocalToWorld(ship, (piece.CellX + .5d) * ship.CellSizeMeters, (piece.CellY + .5d) * ship.CellSizeMeters, out double x, out double y);
            TorquePerNewton = (x - centerX) * Direction.Y - (y - centerY) * Direction.X;
        }
    }
    public static class ShipFuel
    {
        #region 真实混合航空燃料
        public static bool IsEligible(FluidInventory inventory, ShipPartConfiguration engine)
        {
            if (inventory == null || inventory.GasMoles <= 0m) return false;
            decimal h = inventory.GetAvailableMoles(FluidIds.Hydrogen, FluidPhase.Gas);
            decimal o = inventory.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas);
            if (h <= 0m || o <= 0m) return false;
            double ratio = (double)(h / o);
            return Math.Abs(ratio / 2d - 1d) <= engine.FuelRatioTolerance &&
                (double)((inventory.GasMoles - h - o) / inventory.GasMoles) <= engine.MaximumImpurityFraction;
        }
        public static decimal AvailableReactionMoles(FluidInventory inventory, ShipPartConfiguration engine)
            => IsEligible(inventory, engine) ? Math.Min(inventory.GetAvailableMoles(FluidIds.Hydrogen, FluidPhase.Gas) / 2m,
                inventory.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas)) : 0m;

        public static bool TryBurn(FluidInventory inventory, decimal oxygenMoles, out double energyJoules)
        {
            energyJoules = 0d;
            if (oxygenMoles <= 0m || !inventory.TryTakeRecipe(new[]
            { new FluidRequirement(FluidIds.Hydrogen, FluidPhase.Gas, oxygenMoles * 2m),
              new FluidRequirement(FluidIds.Oxygen, FluidPhase.Gas, oxygenMoles) }, out _)) return false;
            energyJoules = (double)oxygenMoles * 483600d; return true;
        }
        #endregion
    }

    public sealed partial class SpaceSession
    {
        #region 真实引擎与自动导航
        private readonly List<ShipActuator> actuators = new();
        private readonly List<FluidInventory> fuelTanks = new();
        private readonly List<MachineEntity> machineScratch = new();

        public void SetControl(string consolePieceId, Vector2 move, float turn)
        {
            controlRequests[consolePieceId] = new ShipControlRequest { ConsolePieceId = consolePieceId, Move = Vector2.ClampMagnitude(move, 1f), Turn = Mathf.Clamp(turn, -1f, 1f) };
            if (move.sqrMagnitude > .0001f || Math.Abs(turn) > .001f)
                foreach (ShipNavigationState navigation in State.Navigation)
                    if (navigation.Active && FindPiece(consolePieceId, out ShipState ship, out _) && SameAssembly(ship.ShipId, navigation.ShipId))
                    { navigation.Active = false; navigation.Status = "已手动接管"; }
        }
        public void ClearControl(string consolePieceId) => controlRequests.Remove(consolePieceId ?? "");

        public bool IsPowered(ShipState ship, ShipPieceState piece)
        {
            ShipDeviceState device = GetDevice(piece.PieceId);
            if (device?.Enabled == false || !piece.IsAlive) return false;
            if (device == null || device.Configuration.PowerWatts <= 0f) return true;
            using (MachineWorld.UseScope("ship:" + ship.ShipId))
                return int.TryParse(piece.PieceId, out int id) && MachineWorld.GetById(id)?.ElectricalPowerRatio > .999f;
        }
        public bool StartNavigation(string consoleId, Vector2 displayPoint)
        {
            if (!IsSpaceView || !FindPiece(consoleId, out ShipState ship, out ShipPieceState console) || console.Kind != ShipPieceKind.Console || !IsPowered(ship, console)) return false;
            ShipPieceState device = ship.Pieces.Find(value => value.IsAlive && value.Kind == ShipPieceKind.Navigation &&
                ShipOpticalGraph.Connected(ship, console, value));
            if (device == null) return false;
            foreach (ShipNavigationState old in State.Navigation)
                if (SameAssembly(old.ShipId, ship.ShipId)) { old.Active = false; old.Status = "已由新目标覆盖"; }
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight.Phase is not (ShipFlightPhase.Orbit or ShipFlightPhase.Descending)) return false;
            string reference = GetReference(flight.Orbit);
            SpaceVector2 absolute = UnprojectPosition(displayPoint);
            SpaceVector2 center = ReferencePosition(reference);
            State.Navigation.Add(new ShipNavigationState
            {
                ShipId = ship.ShipId, ConsolePieceId = consoleId, NavigationPieceId = device.PieceId,
                ReferenceBodyId = reference, TargetX = absolute.X - center.X, TargetY = absolute.Y - center.Y,
                Active = true, Sequence = ++State.NavigationSequence, Status = "正在导航"
            });
            return true;
        }
        public void CancelNavigation(string shipId)
        {
            foreach (ShipNavigationState task in State.Navigation)
                if (SameAssembly(shipId, task.ShipId)) { task.Active = false; task.Status = "已取消"; }
        }
        private string GetReference(OrbitState orbit) => !string.IsNullOrWhiteSpace(orbit.CapturedBodyId) && Universe.TryGetBody(orbit.CapturedBodyId, out _)
            ? orbit.CapturedBodyId : Universe.TryGetBody(orbit.ReferenceBodyId, out _) ? orbit.ReferenceBodyId : null;
        private SpaceVector2 ReferencePosition(string id) => Universe.TryGetBody(id, out BodyState body) ? body.PositionMeters : default;
        private SpaceVector2 ReferenceVelocity(string id) => Universe.TryGetBody(id, out BodyState body) ? body.VelocityMetersPerSecond : default;
        private ShipNavigationState GetNavigation(ShipAssemblyState assembly)
        {
            ShipNavigationState found = null;
            foreach (ShipNavigationState task in State.Navigation)
                if (task.Active && assembly.MemberShipIds.Contains(task.ShipId) && (found == null || found.Sequence < task.Sequence)) found = task;
            return found;
        }
        private bool NavigationConnected(ShipNavigationState task)
        {
            if (!FindPiece(task.NavigationPieceId, out ShipState ship, out ShipPieceState navigation) ||
                !FindPiece(task.ConsolePieceId, out ShipState consoleShip, out ShipPieceState console) ||
                ship.ShipId != consoleShip.ShipId || !ShipOpticalGraph.Connected(ship, console, navigation))
            {
                task.Active = false; task.NeedsManualRestart = true; task.Status = "光纤或导航设备断开，请重新启动"; return false;
            }
            return true;
        }
        private void CollectActuators(ShipAssemblyState assembly)
        {
            actuators.Clear(); fuelTanks.Clear();
            foreach (string id in assembly.MemberShipIds)
            {
                ShipState ship = GetShip(id);
                foreach (ShipPieceState piece in ship.Pieces)
                    if (piece.IsAlive && piece.Kind == ShipPieceKind.Engine && IsPowered(ship, piece))
                        actuators.Add(new ShipActuator(ship, piece, GetDevice(piece.PieceId), assembly.PositionX, assembly.PositionY));
                using (MachineWorld.UseScope("ship:" + id))
                {
                    MachineWorld.CollectNodes(machineScratch);
                    foreach (MachineEntity machine in machineScratch)
                        if (machine.Definition.Fluid?.Kind == "tank")
                        {
                            FluidInventory tank = MachineWorld.GetFluidInventory(machine);
                            if (!fuelTanks.Contains(tank)) fuelTanks.Add(tank);
                        }
                }
            }
        }

        public string DescribePropulsion(string shipId)
        {
            var assembly = ShipDockingService.RebuildAssemblies(State.Ships, State.Docking).Find(value => value.MemberShipIds.Contains(shipId));
            if (assembly == null) return FlatWorldLocalizationService.GetUiText("无有效船体");
            CollectActuators(assembly);
            double usableThrust = 0d; int consoles = 0, powered = 0;
            foreach (ShipActuator engine in actuators)
                foreach (FluidInventory tank in fuelTanks)
                    if (ShipFuel.IsEligible(tank, engine.Device.Configuration)) { usableThrust += engine.Device.Configuration.EngineThrustNewtons; break; }
            foreach (string id in assembly.MemberShipIds)
                foreach (ShipPieceState piece in GetShip(id).Pieces)
                    if (piece.IsAlive && piece.Kind == ShipPieceKind.Console) { consoles++; if (IsPowered(GetShip(id), piece)) powered++; }
            decimal h = 0m, o = 0m, gas = 0m;
            foreach (FluidInventory tank in fuelTanks)
            { h += tank.GetAvailableMoles(FluidIds.Hydrogen, FluidPhase.Gas); o += tank.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas); gas += tank.GasMoles; }
            var flight = GetFlight(shipId);
            double gravity = Universe.TryGetBody(flight.BodyId, out BodyState body) ? body.SurfaceGravity : 0d;
            string ratio = o > 0m ? (h / o).ToString("0.###") + ":1" : FlatWorldLocalizationService.GetUiText("缺氧气");
            string twr = gravity > 0d ? (usableThrust / Math.Max(.001d, assembly.MassKg * gravity)).ToString("0.##") : FlatWorldLocalizationService.GetUiText("失重");
            return FlatWorldLocalizationService.GetUiFormat("有效控制台 {0}/{1} · 可用引擎 {2} · 合格燃料推力 {3:0.##} N · 推重比 {4}", powered, consoles, actuators.Count, usableThrust, twr) + "\n" +
                FlatWorldLocalizationService.GetUiFormat("真实储气 氢 {0:0.###} mol / 氧 {1:0.###} mol · 氢氧比 {2} · 杂气 {3:0.###} mol", h, o, ratio, Math.Max(0m, gas - h - o));
        }

        // 有限非负执行器求解只分配安装引擎能提供的力与力矩。
        private void ResolveThrust(ShipAssemblyState assembly, double seconds, out SpaceVector2 force, out double torque)
        {
            force = default; torque = 0d;
            CollectActuators(assembly);
            SpaceVector2 desired = default;
            double desiredTorque = 0d;
            bool manual = false;
            foreach (ShipControlRequest request in controlRequests.Values)
            {
                if (!FindPiece(request.ConsolePieceId, out ShipState ship, out ShipPieceState console) ||
                    !assembly.MemberShipIds.Contains(ship.ShipId) || !IsPowered(ship, console)) continue;
                desired += SpaceVector2.FromAngle(ship.AngleRadians) * request.Move.x +
                           SpaceVector2.FromAngle(ship.AngleRadians + Math.PI / 2d) * request.Move.y;
                desiredTorque += request.Turn; manual |= request.Move.sqrMagnitude > .0001f || Math.Abs(request.Turn) > .001f;
            }
            double capacity = 0, torqueCapacity = 0;
            foreach (ShipActuator engine in actuators)
            { capacity += engine.Device.Configuration.EngineThrustNewtons; torqueCapacity += Math.Abs(engine.TorquePerNewton) * engine.Device.Configuration.EngineThrustNewtons; }
            desired = desired.Magnitude > 1d ? desired.Normalized : desired;
            desired *= capacity;
            desiredTorque = Math.Max(-1d, Math.Min(1d, desiredTorque)) * torqueCapacity;
            ShipNavigationState task = GetNavigation(assembly);
            if (task != null && !manual && NavigationConnected(task))
            {
                ShipState controlled = GetShip(task.ShipId);
                ShipFlightState flight = GetFlight(task.ShipId);
                Universe.UpdateReference(flight.Orbit);
                string reference = GetReference(flight.Orbit);
                if (reference != task.ReferenceBodyId)
                {
                    SpaceVector2 absolute = ReferencePosition(task.ReferenceBodyId) + new SpaceVector2(task.TargetX, task.TargetY);
                    SpaceVector2 converted = absolute - ReferencePosition(reference);
                    task.TargetX = converted.X; task.TargetY = converted.Y; task.ReferenceBodyId = reference;
                }
                bool powered = FindPiece(task.NavigationPieceId, out _, out ShipPieceState nav) && IsPowered(controlled, nav) &&
                    FindPiece(task.ConsolePieceId, out _, out ShipPieceState console) && IsPowered(controlled, console);
                if (!powered) { task.Status = "断电，保留目标并惯性滑行"; return; }
                SpaceVector2 target = ReferencePosition(reference) + new SpaceVector2(task.TargetX, task.TargetY);
                SpaceVector2 error = target - new SpaceVector2(assembly.PositionX, assembly.PositionY);
                SpaceVector2 relativeVelocity = new SpaceVector2(assembly.VelocityX, assembly.VelocityY) - ReferenceVelocity(reference);
                double acceleration = capacity / Math.Max(.001d, assembly.MassKg);
                double speed = Math.Sqrt(2d * acceleration * error.Magnitude);
                SpaceVector2 demandedAcceleration = (error.Normalized * Math.Min(speed, 100d) - relativeVelocity) / Math.Max(.2d, seconds);
                SpaceVector2 gravity = Universe.GetGravity(new SpaceVector2(assembly.PositionX, assembly.PositionY), reference, out _);
                desired = (demandedAcceleration - gravity) * assembly.MassKg;
                if (desired.Magnitude > capacity && capacity > 0d) desired = desired.Normalized * capacity;
                desiredTorque = -assembly.AngularVelocityRadiansPerSecond * assembly.InertiaKgM2 / .5d;
                task.Status = error.Magnitude < .5d && relativeVelocity.Magnitude < .1d ? "正在保持导航点" : "正在导航";
            }
            if (capacity <= 0d)
            { if (task != null && task.Active) task.Status = "能力不足，等待可用引擎恢复"; return; }
            var throttle = new double[actuators.Count];
            SpaceVector2 residual = desired;
            double residualTorque = desiredTorque;
            double leverScale = Math.Max(1d, Math.Sqrt(assembly.InertiaKgM2 / Math.Max(.001d, assembly.MassKg)));
            for (int iteration = 0; iteration < 12; iteration++)
            for (int index = 0; index < actuators.Count; index++)
            {
                ShipActuator engine = actuators[index];
                double maxForce = engine.Device.Configuration.EngineThrustNewtons;
                double lever = engine.TorquePerNewton / leverScale;
                double change = (SpaceVector2.Dot(residual, engine.Direction) + residualTorque / leverScale * lever) / (1d + lever * lever);
                double next = Math.Max(0d, Math.Min(maxForce, throttle[index] + change));
                double applied = next - throttle[index]; throttle[index] = next;
                residual -= engine.Direction * applied; residualTorque -= engine.TorquePerNewton * applied;
            }
            bool fuelMissing = false;
            for (int index = 0; index < actuators.Count; index++)
            {
                ShipActuator engine = actuators[index];
                double level = throttle[index] / engine.Device.Configuration.EngineThrustNewtons;
                if (level <= .000001d) continue;
                double actual = ConsumeEngineFuel(engine.Device.Configuration, level, seconds);
                fuelMissing |= actual + .00001d < level;
                double delivered = engine.Device.Configuration.EngineThrustNewtons * actual;
                force += engine.Direction * delivered; torque += engine.TorquePerNewton * delivered;
            }
            if (task != null && task.Active)
            {
                if (fuelMissing) task.Status = "燃料不足，保留目标；能力恢复后继续";
                else if (residual.Magnitude > Math.Max(1d, desired.Magnitude * .1d) || Math.Abs(residualTorque) > Math.Max(1d, Math.Abs(desiredTorque) * .1d))
                    task.Status = "操纵能力不足，正在尽力执行";
            }
        }
        private double ConsumeEngineFuel(ShipPartConfiguration configuration, double throttle, double seconds)
        {
            decimal request = AtmosphereService.Quantize((decimal)(configuration.FuelMolesPerSecond * throttle * seconds));
            if (request <= 0m) return 0d;
            decimal consumed = 0m;
            foreach (FluidInventory tank in fuelTanks)
            {
                decimal amount = Math.Min(request - consumed, ShipFuel.AvailableReactionMoles(tank, configuration));
                amount = AtmosphereService.Quantize(amount);
                if (amount > 0m && ShipFuel.TryBurn(tank, amount, out _)) consumed += amount;
                if (consumed >= request) break;
            }
            return throttle * (double)(consumed / request);
        }
        #endregion
    }
}
