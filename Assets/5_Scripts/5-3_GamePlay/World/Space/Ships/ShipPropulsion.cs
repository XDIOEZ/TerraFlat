using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public readonly struct ShipActuator
    {
        public readonly ShipDeviceState Device;
        public ShipActuator(ShipDeviceState device) => Device = device;
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
        #region 简化巡航、燃料与自动导航
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
            if (flight.Phase is not (ShipFlightPhase.Cruising or ShipFlightPhase.Descending)) return false;
            SpaceVector2 absolute = UnprojectPosition(displayPoint);
            State.Navigation.Add(new ShipNavigationState
            {
                ShipId = ship.ShipId, ConsolePieceId = consoleId, NavigationPieceId = device.PieceId,
                TargetX = absolute.X, TargetY = absolute.Y,
                Active = true, Sequence = ++State.NavigationSequence, Status = "正在导航"
            });
            return true;
        }
        public void CancelNavigation(string shipId)
        {
            foreach (ShipNavigationState task in State.Navigation)
                if (SameAssembly(shipId, task.ShipId)) { task.Active = false; task.Status = "已取消"; }
        }
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
                        actuators.Add(new ShipActuator(GetDevice(piece.PieceId)));
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

        // 操作直接指定期望速度，发动机只决定能否改变航向与速度。
        private void ResolveCruiseCommand(ShipAssemblyState assembly, double seconds, out SpaceVector2 targetVelocity, out double turn)
        {
            targetVelocity = default; turn = 0d;
            SpaceVector2 steering = default;
            bool manual = false;
            foreach (ShipControlRequest request in controlRequests.Values)
            {
                if (!FindPiece(request.ConsolePieceId, out ShipState ship, out ShipPieceState console) ||
                    !assembly.MemberShipIds.Contains(ship.ShipId) || !IsPowered(ship, console)) continue;
                steering += SpaceVector2.FromAngle(ship.AngleRadians) * request.Move.x +
                            SpaceVector2.FromAngle(ship.AngleRadians + Math.PI / 2d) * request.Move.y;
                turn += request.Turn;
                manual |= request.Move.sqrMagnitude > .0001f || Math.Abs(request.Turn) > .001f;
            }
            SpaceGameplaySettings config = SpaceGameplaySettings.Current;
            targetVelocity = steering.Magnitude > 1d ? steering.Normalized * config.CruiseMaxSpeedMetersPerSecond :
                steering * config.CruiseMaxSpeedMetersPerSecond;
            turn = Math.Clamp(turn, -1d, 1d);
            ShipNavigationState task = GetNavigation(assembly);
            if (task != null && !manual && NavigationConnected(task))
            {
                ShipState controlled = GetShip(task.ShipId);
                bool powered = FindPiece(task.NavigationPieceId, out _, out ShipPieceState nav) && IsPowered(controlled, nav) &&
                    FindPiece(task.ConsolePieceId, out _, out ShipPieceState console) && IsPowered(controlled, console);
                if (!powered) { task.Status = "断电，等待导航设备恢复"; return; }
                SpaceVector2 target = new(task.TargetX, task.TargetY);
                SpaceVector2 error = target - new SpaceVector2(assembly.PositionX, assembly.PositionY);
                SpaceVector2 velocity = new(assembly.VelocityX, assembly.VelocityY);
                double brakingDistance = Math.Max(0d, error.Magnitude - 1d);
                double speed = Math.Min(config.CruiseMaxSpeedMetersPerSecond,
                    Math.Sqrt(2d * config.CruiseBrakingMetersPerSecondSquared * brakingDistance));
                targetVelocity = error.Normalized * speed;
                turn = 0d;
                task.Status = error.Magnitude < 1d && velocity.Magnitude < .25d ? "已到达导航点" : "正在导航";
            }
            SpaceVector2 current = new(assembly.VelocityX, assembly.VelocityY);
            if ((targetVelocity - current).Magnitude < .001d && Math.Abs(turn) < .001d) return;
            CollectActuators(assembly);
            if (actuators.Count == 0)
            {
                targetVelocity = default; turn = 0d;
                if (task != null && task.Active) task.Status = "没有可用引擎";
                return;
            }
            // 松开按键时允许免费刹停；只有主动提速、转弯或改变运动方向才消耗燃料。
            bool driveNeeded = Math.Abs(turn) > .001d ||
                targetVelocity.Magnitude > .001d && (targetVelocity - current).Magnitude > .001d;
            if (!driveNeeded) return;
            foreach (ShipActuator engine in actuators)
            {
                double actual = ConsumeEngineFuel(engine.Device.Configuration, 1d, seconds);
                if (actual <= 0d) continue;
                targetVelocity = current + (targetVelocity - current) * actual;
                turn *= actual;
                return;
            }
            targetVelocity = current; turn = 0d;
            if (task != null && task.Active) task.Status = "燃料不足，等待补给";
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
