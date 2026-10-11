using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public readonly struct ShipActuator
    {
        public readonly ShipDeviceState Device;
        public readonly MachineEntity Machine;
        public ShipActuator(ShipDeviceState device, MachineEntity machine) { Device = device; Machine = machine; }
    }
    public sealed partial class SpaceSession
    {
        #region 简化巡航、燃料与自动导航
        private readonly List<ShipActuator> actuators = new();
        private readonly List<FluidInventory> fuelTanks = new();
        private readonly List<MachineEntity> machineScratch = new();
        private readonly FluidNetworkGraph.SupplyWorkspace fuelSupplyWorkspace = new();
        private readonly HashSet<FluidInventory> visitedFuelInventories = new();
        private readonly Dictionary<FluidInventory, double> projectedFuelBudgets = new();
        private readonly Dictionary<FluidInventory, decimal> projectedFuelStock = new();
        private static readonly string[] engineFuelIds = { FluidIds.Hydrogen, FluidIds.Oxygen };
        private readonly ShipOpticalGraph.Workspace opticalWorkspace = new();
        private readonly Dictionary<string, float> engineOutputs = new(StringComparer.Ordinal);
        private readonly ShipThrustAllocation thrustAllocation = new();

        // 喷焰只读本步真实耗油后的出力，不把移动、点火请求或存档当成工作状态。
        public float GetEngineOutput(string pieceId)
            => running && !transitionRunning && State != null && GameManager.Instance?.IsGameplayReady == true &&
                !string.IsNullOrEmpty(pieceId) && engineOutputs.TryGetValue(pieceId, out float output) ? output : 0f;

        public void SetControl(string consolePieceId, Vector2 move, float turn)
        {
            if (State == null || string.IsNullOrEmpty(consolePieceId)) return;
            if (!controlRequests.TryGetValue(consolePieceId, out ShipControlRequest request))
                controlRequests.Add(consolePieceId, request = new ShipControlRequest { ConsolePieceId = consolePieceId });
            request.Move = Vector2.ClampMagnitude(move, 1f); request.Turn = Mathf.Clamp(turn, -1f, 1f);
            if ((move.sqrMagnitude > .0001f || Math.Abs(turn) > .001f) && FindPiece(consolePieceId, out ShipState ship, out _))
                foreach (ShipNavigationState navigation in State.Navigation)
                    if (navigation.Active && SameAssembly(ship.ShipId, navigation.ShipId))
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
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight == null || flight.Phase is not (ShipFlightPhase.Cruising or ShipFlightPhase.Descending)) return false;
            SpaceVector2 absolute = UnprojectPosition(displayPoint);
            if (!absolute.IsFinite) return false;
            ShipPieceState device = null;
            foreach (ShipPieceState candidate in ship.Pieces)
                if (candidate.IsAlive && candidate.Kind == ShipPieceKind.Navigation && IsPowered(ship, candidate) &&
                    ShipOpticalGraph.Connected(ship, console, candidate, opticalWorkspace))
                { device = candidate; break; }
            if (device == null) return false;
            ShipAssemblyState assembly = GetConnectedAssembly(ship.ShipId);
            if (assembly == null) return false;
            // 同一运动整体只保留最新任务，重新选点复用任务而不是积累历史快照。
            ShipNavigationState task = null;
            for (int i = State.Navigation.Count - 1; i >= 0; i--)
            {
                ShipNavigationState old = State.Navigation[i];
                if (!assembly.MemberShipIds.Contains(old.ShipId)) continue;
                task ??= old;
                State.Navigation.RemoveAt(i);
            }
            task ??= new ShipNavigationState();
            task.ShipId = ship.ShipId; task.ConsolePieceId = consoleId; task.NavigationPieceId = device.PieceId;
            task.TargetX = absolute.X; task.TargetY = absolute.Y;
            task.Active = true; task.NeedsManualRestart = false; task.Sequence = ++State.NavigationSequence;
            task.Status = "正在导航";
            State.Navigation.Add(task);
            return true;
        }
        public void CancelNavigation(string shipId)
        {
            foreach (ShipNavigationState task in State.Navigation)
                if (SameAssembly(shipId, task.ShipId)) { task.Active = false; task.Status = "已取消"; }
        }
        public ShipNavigationState GetLatestNavigation(string shipId) => GetNavigation(GetConnectedAssembly(shipId), false);

        private ShipNavigationState GetNavigation(ShipAssemblyState assembly, bool activeOnly = true)
        {
            if (assembly == null) return null;
            ShipNavigationState found = null;
            foreach (ShipNavigationState task in State.Navigation)
                if ((!activeOnly || task.Active) && assembly.MemberShipIds.Contains(task.ShipId) && (found == null || found.Sequence < task.Sequence)) found = task;
            return found;
        }
        private bool NavigationConnected(ShipNavigationState task)
        {
            if (!FindPiece(task.NavigationPieceId, out ShipState ship, out ShipPieceState navigation) ||
                !FindPiece(task.ConsolePieceId, out ShipState consoleShip, out ShipPieceState console) ||
                ship.ShipId != consoleShip.ShipId || !ShipOpticalGraph.Connected(ship, console, navigation, opticalWorkspace))
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
                using (MachineWorld.UseScope("ship:" + id))
                {
                    foreach (ShipPieceState piece in ship.Pieces)
                        if (piece.IsAlive && piece.Kind == ShipPieceKind.Engine &&
                            ShipEnginePlacement.TryValidateEngine(ship, piece, out _) && IsPowered(ship, piece) &&
                            int.TryParse(piece.PieceId, out int engineId) && MachineWorld.GetById(engineId) is MachineEntity machine)
                            actuators.Add(new ShipActuator(GetDevice(piece.PieceId), machine));
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
            var assembly = GetConnectedAssembly(shipId);
            if (assembly == null) return FlatWorldLocalizationService.GetUiText("无有效船体");
            CollectActuators(assembly);
            double usableThrust = GetAvailableEngineThrust(); int consoles = 0, powered = 0;
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

        // 手动推力与导航制动共用真实动力授权，无输入或失去动力时保持当前速度。
        private void ResolveCruiseCommand(ShipAssemblyState assembly, double seconds, out SpaceVector2 targetVelocity, out double turn, bool surfaceDescent = false)
        {
            SpaceVector2 current = new(assembly.VelocityX, assembly.VelocityY);
            targetVelocity = current; turn = 0d;
            SpaceVector2 steering = default;
            bool manual = false;
            foreach (ShipControlRequest request in controlRequests.Values)
            {
                if (!FindPiece(request.ConsolePieceId, out ShipState ship, out ShipPieceState console) ||
                    !assembly.MemberShipIds.Contains(ship.ShipId) || console.Kind != ShipPieceKind.Console || !IsPowered(ship, console)) continue;
                steering += SpaceVector2.FromAngle(ship.AngleRadians) * request.Move.x +
                            SpaceVector2.FromAngle(ship.AngleRadians + Math.PI / 2d) * request.Move.y;
                turn += request.Turn;
                manual |= request.Move.sqrMagnitude > .0001f || Math.Abs(request.Turn) > .001f;
            }
            SpaceGameplaySettings config = SpaceGameplaySettings.Current;
            targetVelocity = ShipCruiseGuidance.ManualVelocity(current, steering, config, seconds);
            turn = Math.Clamp(turn, -1d, 1d);
            ShipNavigationState task = surfaceDescent ? null : GetNavigation(assembly);
            if (task != null && !manual && NavigationConnected(task))
            {
                ShipState controlled = GetShip(task.ShipId);
                bool powered = FindPiece(task.NavigationPieceId, out _, out ShipPieceState nav) && IsPowered(controlled, nav) &&
                    FindPiece(task.ConsolePieceId, out _, out ShipPieceState console) && IsPowered(controlled, console);
                if (!powered) { targetVelocity = current; turn = 0d; task.Status = "断电，保持惯性，等待导航设备恢复"; return; }
                SpaceVector2 target = new(task.TargetX, task.TargetY);
                SpaceVector2 error = target - new SpaceVector2(assembly.PositionX, assembly.PositionY);
                SpaceVector2 velocity = new(assembly.VelocityX, assembly.VelocityY);
                if (error.Magnitude <= config.NavigationArrivalDistanceMeters && velocity.Magnitude <= Math.Min(.001d, config.NavigationArrivalSpeedMetersPerSecond))
                {
                    task.Active = false; task.Status = "已到达导航点";
                    targetVelocity = current; turn = 0d;
                    return;
                }
                targetVelocity = ShipCruiseGuidance.ArrivalVelocity(error, config, seconds);
                turn = error.Magnitude > config.NavigationArrivalDistanceMeters
                    ? ShipCruiseGuidance.TurnToward(controlled.AngleRadians, error, config, seconds) : 0d;
                task.Status = targetVelocity.Magnitude < config.CruiseMaxSpeedMetersPerSecond * .95d ? "接近目标，减速中" : "正在导航";
            }
            if ((targetVelocity - current).Magnitude < .001d && Math.Abs(turn) < .001d)
            { targetVelocity = current; turn = 0d; return; }
            CollectActuators(assembly);
            if (actuators.Count == 0)
            {
                targetVelocity = current; turn = 0d;
                if (task != null && task.Active) task.Status = "没有可用引擎";
                return;
            }
            double change = ShipCruiseGuidance.ChangeRate(current, targetVelocity, config) * seconds;
            SpaceVector2 requestedChange = ShipCruiseGuidance.StepVelocity(current, targetVelocity, config, seconds) - current;
            thrustAllocation.Reset(requestedChange, change, turn);
            bool ignited = false;
            foreach (ShipActuator engine in actuators)
            {
                // 地表下降的按键推力独立于点火救船的垂直推力，避免进入降落后自动爬升。
                if (!FindPiece(engine.Device.PieceId, out ShipState engineShip, out ShipPieceState enginePiece) ||
                    (!surfaceDescent && GetFlight(engineShip.ShipId)?.Ignited != true))
                {
                    thrustAllocation.Add(default, default, 0d);
                    continue;
                }
                ignited = true;
                ShipGeometry.LocalToWorld(engineShip, (enginePiece.CellX + .5d) * engineShip.CellSizeMeters,
                    (enginePiece.CellY + .5d) * engineShip.CellSizeMeters, out double x, out double y);
                thrustAllocation.Add(ShipThrustAllocation.GetThrustDirection(engineShip.AngleRadians, enginePiece.QuarterTurns),
                    new SpaceVector2(x - assembly.PositionX, y - assembly.PositionY), engine.Device.Configuration.EngineThrustNewtons);
            }
            bool requestedEngine = false;
            for (int index = 0; index < actuators.Count; index++)
            {
                double throttle = thrustAllocation.GetThrottle(index);
                if (throttle <= 0d) continue;
                requestedEngine = true;
                thrustAllocation.SetActualOutput(index, ConsumeEngineFuel(actuators[index], throttle, seconds));
            }
            // 平移与转向分别按对应喷嘴的真实供油恢复，缺某侧燃料不借其它朝向的引擎。
            SpaceVector2 actualChange = thrustAllocation.ResolveVelocityChange();
            targetVelocity = current + actualChange;
            turn *= thrustAllocation.ResolveTurnAuthority();
            if (actualChange.SqrMagnitude > 1e-12d || Math.Abs(turn) > .001d) return;
            bool navigating = task != null && task.Active;
            if (navigating) task.Status = !ignited ? "引擎已停机，保持惯性，等待点火" : !requestedEngine
                ? "当前方向没有可用喷嘴，保持惯性" : "燃料不足，保持惯性，等待补给";
        }
        private double GetAvailableEngineThrust()
        {
            projectedFuelBudgets.Clear(); projectedFuelStock.Clear();
            double thrust = 0d;
            foreach (ShipActuator engine in actuators)
                thrust += engine.Device.Configuration.EngineThrustNewtons * GetEngineFuelAuthority(engine);
            return thrust;
        }

        private double GetEngineFuelAuthority(ShipActuator engine)
        {
            if (engine.Device == null || engine.Machine.Definition.Fluid == null) return 0d;
            using var nodeScope = MachineWorld.UseNodeScope(engine.Machine);
            MachineWorld.CollectFluidSupplyRoutes(engine.Machine, "main", FluidPhase.Gas, engineFuelIds, fuelSupplyWorkspace);
            decimal required = (decimal)engine.Device.Configuration.FuelMolesPerSecond;
            if (required <= 0m) return 0d;
            decimal supplied = 0m;
            visitedFuelInventories.Clear();
            for (int index = 0; index < fuelSupplyWorkspace.Nodes.Count; index++)
            {
                FluidNetworkGraph.SupplyNode source = fuelSupplyWorkspace.Nodes[index];
                FluidInventory inventory = MachineWorld.GetFluidInventory(source.Node, source.Chamber);
                if (!visitedFuelInventories.Add(inventory) || !ShipFuel.IsEligible(inventory, engine.Device.Configuration)) continue;
                if (!projectedFuelStock.TryGetValue(inventory, out decimal remaining))
                    projectedFuelStock[inventory] = remaining = ShipFuel.AvailableReactionMoles(inventory, engine.Device.Configuration);
                double budget = MachineWorld.GetFluidSupplyStandardLiters(fuelSupplyWorkspace, index, 1d, false, projectedFuelBudgets);
                decimal amount = Math.Min(required - supplied, Math.Min(remaining, FluidUnits.StandardLitersToMol((decimal)budget) / 3m));
                if (amount <= 0m) continue;
                projectedFuelStock[inventory] = remaining - amount;
                MachineWorld.ReserveFluidSupplyProjection(fuelSupplyWorkspace, index,
                    (double)FluidUnits.MolToStandardLiters(amount * 3m), projectedFuelBudgets);
                supplied += amount;
                if (supplied >= required) break;
            }
            return (double)(supplied / required);
        }

        private double ConsumeEngineFuel(ShipActuator actuator, double throttle, double seconds)
        {
            ShipDeviceState engine = actuator.Device;
            ShipPartConfiguration configuration = engine.Configuration;
            decimal request = AtmosphereService.Quantize((decimal)(configuration.FuelMolesPerSecond * throttle * seconds));
            if (request <= 0m) return 0d;
            decimal consumed = 0m;
            using var nodeScope = MachineWorld.UseNodeScope(actuator.Machine);
            MachineWorld.CollectFluidSupplyRoutes(actuator.Machine, "main", FluidPhase.Gas, engineFuelIds, fuelSupplyWorkspace);
            visitedFuelInventories.Clear();
            for (int index = 0; index < fuelSupplyWorkspace.Nodes.Count; index++)
            {
                FluidNetworkGraph.SupplyNode source = fuelSupplyWorkspace.Nodes[index];
                FluidInventory tank = MachineWorld.GetFluidInventory(source.Node, source.Chamber);
                if (!visitedFuelInventories.Add(tank)) continue;
                decimal amount = Math.Min(request - consumed, ShipFuel.AvailableReactionMoles(tank, configuration));
                double liters = MachineWorld.GetFluidSupplyStandardLiters(fuelSupplyWorkspace, index, seconds, true);
                amount = Math.Min(amount, FluidUnits.StandardLitersToMol((decimal)liters) / 3m);
                amount = Math.Min(amount, MachineWorld.GetFluidSupplyStock(fuelSupplyWorkspace, index, FluidIds.Hydrogen, FluidPhase.Gas) / 2m);
                amount = Math.Min(amount, MachineWorld.GetFluidSupplyStock(fuelSupplyWorkspace, index, FluidIds.Oxygen, FluidPhase.Gas));
                amount = AtmosphereService.Quantize(amount);
                if (amount > 0m && ShipFuel.TryBurn(tank, amount, out _))
                {
                    // 扣真实气体后同步冻结库存与整条路径预算，不借旁船或断开的储罐补气。
                    MachineWorld.ConsumeFluidSupplyBudget(fuelSupplyWorkspace, index, new[]
                    { new FluidRequirement(FluidIds.Hydrogen, FluidPhase.Gas, amount * 2m),
                      new FluidRequirement(FluidIds.Oxygen, FluidPhase.Gas, amount) });
                    consumed += amount;
                }
                if (consumed >= request) break;
            }
            double actual = throttle * (double)(consumed / request);
            if (actual > 0d)
            {
                engineOutputs.TryGetValue(engine.PieceId, out float previous);
                engineOutputs[engine.PieceId] = Mathf.Clamp01(previous + (float)actual);
            }
            return actual;
        }
        #endregion
    }
}
