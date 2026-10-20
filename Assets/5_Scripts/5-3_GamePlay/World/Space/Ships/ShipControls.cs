using System;
using UnityEngine;
using FlatWorld.Localization;
using FlatWorld.Networking;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 指针选点与对接操作
        private string pickingNavigationConsole;
        public const float ConsoleInteractionDistance = Mod_InteractSender.DefaultMaxInteractDistance;

        // 只有靠近本船实际控制台才能操作整船面板，船体地板不作为控制台。
        public bool TryGetNearbyConsole(Player player, string shipId, out string consoleId, string requiredConsoleId = null)
        {
            consoleId = null;
            if (player == null || State == null) return false;
            ShipState ship = GetShip(shipId);
            if (ship == null || !IsVisible(ship)) return false;
            float closestDistance = ConsoleInteractionDistance;
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (!piece.IsAlive || piece.Kind != ShipPieceKind.Console) continue;
                if (requiredConsoleId != null && piece.PieceId != requiredConsoleId) continue;
                Vector2 center = DisplayPoint(ship, (piece.CellX + .5d) * ship.CellSizeMeters,
                    (piece.CellY + .5d) * ship.CellSizeMeters);
                float distance = IsSpaceView
                    ? Vector2.Distance(GetPlayerAuthoritativeDisplayPosition(player), center)
                    : WorldTopologyRuntime.Distance(GetPlayerAuthoritativeDisplayPosition(player), center);
                if (distance > closestDistance) continue;
                closestDistance = distance;
                consoleId = piece.PieceId;
            }
            return consoleId != null;
        }

        // 面板绑定实际操作部件，导航模块不能借附近控制台冒充船控入口。
        public bool CanOpenShipPanel(Player player, string shipId, string pieceId)
        {
            if (!FindPiece(pieceId, out ShipState ship, out ShipPieceState piece) || ship.ShipId != shipId ||
                piece.Kind is not (ShipPieceKind.Console or ShipPieceKind.DockingPort or ShipPieceKind.Engine or
                    ShipPieceKind.GravityGenerator or ShipPieceKind.Equipment)) return false;
            return TryGetNearbyConsole(player, shipId, out _, piece.Kind == ShipPieceKind.Console ? pieceId : null);
        }

        public string ResolveConsole(string shipId, string preferred = null)
        {
            ShipState ship = GetShip(shipId);
            if (ship == null) return null;
            ShipPieceState chosen = ship.Pieces.Find(p => p.IsAlive && p.Kind == ShipPieceKind.Console && p.PieceId == preferred && IsPowered(ship, p));
            chosen ??= ship.Pieces.Find(p => p.IsAlive && p.Kind == ShipPieceKind.Console && IsPowered(ship, p));
            return chosen?.PieceId;
        }

        public BodyState GetLandingCandidate(string shipId)
        {
            ShipFlightState flight = GetFlight(shipId);
            if (Universe == null || flight == null || flight.Phase is not (ShipFlightPhase.Cruising)) return null;
            ShipAssemblyState assembly = GetConnectedAssembly(shipId);
            if (assembly == null) return null;
            BodyState found = null;
            foreach (UniverseSimulation universe in sectorDirectory.Values)
            {
                SpaceVector2 point = SpaceSectorCoordinates.Relative(assembly.Sector, new SpaceVector2(assembly.PositionX, assembly.PositionY), universe.State.Sector);
                BodyState candidate = ShipLandingGuidance.FindNearbyBody(point, AssemblyRadius(assembly), universe.State.Bodies);
                if (candidate != null) { found = candidate; break; }
            }
            return found;
        }

        // 降落指令在固定步的接触提交阶段生效，按钮点击不直接切场景或改坐标。
        public bool RequestLanding(string consoleId) => RequestLanding(consoleId, out _);
        public bool RequestLanding(string consoleId, out string reason)
        {
            reason = null;
            if (!GameNetwork.HasStateAuthority) return SubmitLocalIntent(SpaceNetworkIntentKind.RequestLanding, consoleId);
            if (!TryGetPoweredConsole(consoleId, out ShipState ship, out _, out reason)) return false;
            if (GetFlight(ship.ShipId)?.Phase != ShipFlightPhase.Cruising)
                return RejectShipOperation("只有太空飞行中的飞船可以申请降落。", out reason);
            BodyState body = GetLandingCandidate(ship.ShipId);
            if (body == null) return RejectShipOperation("附近没有进入降落范围的星体。", out reason);
            if (GameNetwork.IsOnline && !CanTransferToNetworkSurface(body.SurfaceAddress.WorldKey, out reason)) return false;
            ShipAssemblyState assembly = GetConnectedAssembly(ship.ShipId);
            if (assembly == null) return RejectShipOperation("飞船状态尚未就绪，请稍后重试。", out reason);
            foreach (string id in assembly.MemberShipIds) GetFlight(id).RequestedLandingBodyId = body.BodyId;
            CancelNavigation(ship.ShipId);
            pickingNavigationConsole = null;
            return true;
        }
        public bool BeginPickingNavigation(string consoleId) => BeginPickingNavigation(consoleId, out _);
        public bool BeginPickingNavigation(string consoleId, out string reason)
        {
            if (!TryGetNavigationDevice(consoleId, out _, out _, out _, out reason)) return false;
            pickingNavigationConsole = consoleId; return true;
        }

        private static bool RejectShipOperation(string source, out string reason)
        { reason = FlatWorldLocalizationService.GetUiText(source); return false; }

        // 操作方共用真实控制台准入，分别说明失效、瘫痪、停用和断电。
        private bool TryGetPoweredConsole(string id, out ShipState ship, out ShipPieceState console, out string reason)
        {
            reason = null;
            bool exists = FindPiece(id, out ship, out console) && console.Kind == ShipPieceKind.Console;
            string failure = GetConsoleOperationFailure(exists, exists && console.IsOperational && !ship.IsWreck,
                exists && GetDevice(id)?.Enabled != false, exists && IsPowered(ship, console));
            if (failure != null) return RejectShipOperation(failure, out reason);
            return true;
        }

        internal static string GetConsoleOperationFailure(bool exists, bool operational, bool enabled, bool powered)
            => !exists ? "飞船控制台已失效。" : !operational ? "控制台已瘫痪或船体已成为残骸。" :
                !enabled ? "控制台已停用，请先启用。" : !powered ? "控制台未获得足够电力。" : null;

        internal static bool IsValidTargetPressure(double pressure)
            => double.IsFinite(pressure) && pressure >= 0d && pressure <= 200000d;

        internal static string GetDockConfirmationFeedback(bool connected)
            => connected ? "双方确认完成，对接连接已建立。" : "本船已确认对接，正在等待另一艘飞船确认。";

        internal static string GetNavigationDeviceFailure(bool installed, bool operational, bool enabled, bool powered)
            => !installed ? "飞船未安装自动导航设备。" : !operational ? "自动导航设备已瘫痪。" :
                !enabled ? "自动导航设备已停用，请先启用。" : !powered ? "自动导航设备未获得足够电力。" :
                "控制台与已供电的自动导航设备之间没有连通的光纤。";

        private bool TryGetNavigationDevice(string id, out ShipState ship, out ShipPieceState console,
            out ShipPieceState device, out string reason)
        {
            device = null;
            if (!TryGetPoweredConsole(id, out ship, out console, out reason)) return false;
            if (GetFlight(ship.ShipId)?.Phase != ShipFlightPhase.Cruising)
                return RejectShipOperation("自动导航只能在太空飞行中启动。", out reason);
            bool installed = false, operational = false, enabled = false, powered = false;
            foreach (ShipPieceState candidate in ship.Pieces)
            {
                if (!candidate.IsAlive || candidate.Kind != ShipPieceKind.Navigation) continue;
                installed = true;
                if (!candidate.IsOperational) continue;
                operational = true;
                if (GetDevice(candidate.PieceId)?.Enabled == false) continue;
                enabled = true;
                if (!IsPowered(ship, candidate)) continue;
                powered = true;
                if (!ShipOpticalGraph.Connected(ship, console, candidate, opticalWorkspace)) continue;
                device = candidate; return true;
            }
            return RejectShipOperation(GetNavigationDeviceFailure(installed, operational, enabled, powered), out reason);
        }

        // 面板只显示权威操作回执，成功提交请求本身不代表业务已经完成。
        public void RequestShipOperation(Player player, SpaceNetworkIntent intent, Action<bool, string> completed)
        {
            if (player == null || State == null)
            { completed?.Invoke(false, FlatWorldLocalizationService.GetUiText("玩家或太空会话尚未就绪。")); return; }
            if (!GameNetwork.HasStateAuthority)
            {
                string previousShip = FindPassenger(player.ProfileName)?.ShipId;
                if (!SpaceNetworkBridge.SendIntent(player.ProfileName, intent, (accepted, responseReason) =>
                {
                    if (accepted && intent.Kind == SpaceNetworkIntentKind.EnterConsole)
                        NotifyLocalManualConsoleEntry(player, intent.PieceId);
                    else if (accepted && intent.Kind == SpaceNetworkIntentKind.ExitConsole && player.IsLocalProfile)
                        LocalManualConsoleExitRequested?.Invoke(previousShip);
                    completed?.Invoke(accepted, responseReason);
                }))
                    completed?.Invoke(false, FlatWorldLocalizationService.GetUiText("联机连接尚未就绪，请稍后重试。"));
                return;
            }
            EnsureNetworkPassenger(player);
            bool success;
            string reason;
            if (intent.Kind == SpaceNetworkIntentKind.EnterConsole)
                success = EnterConsole(player, intent.PieceId, out reason);
            else if (intent.Kind == SpaceNetworkIntentKind.ExitConsole)
            {
                success = RequestConsoleExit(player);
                reason = success ? null : FlatWorldLocalizationService.GetUiText("当前未处于驾驶状态。");
            }
            else success = ExecuteNetworkIntent(player.ProfileName, intent, out reason);
            completed?.Invoke(success, reason);
        }
        public bool TryHandleSpacePointer(Player player)
        {
            if (player == null || string.IsNullOrWhiteSpace(pickingNavigationConsole)) return false;
            Mod_GameController controller = player.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
            if (controller == null || !controller.TryGetMouseWorldPosition(out Vector3 point)) return false;
            string console = pickingNavigationConsole; pickingNavigationConsole = null;
            SpaceSectorPosition target = UnprojectSectorPosition(point);
            RequestShipOperation(player, new SpaceNetworkIntent { Kind = SpaceNetworkIntentKind.StartNavigation,
                PieceId = console, Sector = target.Sector, X = target.LocalMeters.X, Y = target.LocalMeters.Y },
                (success, reason) => ShipPanelSession.ReportNavigationSelection(this, player, console, success, reason));
            return true;
        }
        public bool TryCancelNavigationPicking(Player player)
        {
            // 返回动作取消导航选点，手持物使用不受导航状态影响。
            if (player == null || State == null || pickingNavigationConsole == null) return false;
            pickingNavigationConsole = null;
            return true;
        }
        public bool ConfirmNearbyDock(string shipId)
        {
            if (!FlatWorld.Networking.GameNetwork.HasStateAuthority) return SubmitLocalIntent(SpaceNetworkIntentKind.ConfirmDock, ResolveConsole(shipId), shipId);
            ShipState ship = GetShip(shipId);
            string consoleId = ResolveConsole(shipId);
            Player actor = ItemMgr.Instance?.User_Player;
            if (ship == null || actor == null || !FindPiece(consoleId, out _, out ShipPieceState console) ||
                Vector2.Distance(GetPlayerAuthoritativeDisplayPosition(actor), DisplayPoint(ship, console.CellX + .5d, console.CellY + .5d)) > 3f) return false;
            ShipFlightState ownFlight = GetFlight(shipId);
            ShipDockingService.SynchronizePorts(ship);
            foreach (ShipDockingPortState own in ship.DockingPorts)
            foreach (ShipState other in State.Ships)
            {
                if (other == ship) continue;
                ShipFlightState otherFlight = GetFlight(other.ShipId);
                bool ownSpace = ownFlight.Phase is ShipFlightPhase.Cruising;
                bool otherSpace = otherFlight.Phase is ShipFlightPhase.Cruising;
                if (ownSpace != otherSpace || !ownSpace && (ownFlight.SurfaceWorldKey != otherFlight.SurfaceWorldKey || Math.Abs(ownFlight.HeightMeters - otherFlight.HeightMeters) > 1d)) continue;
                ShipDockingService.SynchronizePorts(other);
                foreach (ShipDockingPortState target in other.DockingPorts)
                    if (ShipDockingService.TryCreateCandidate(State.Ships, State.Docking, shipId, own.PortId,
                        other.ShipId, target.PortId, out ShipDockingPairState pair))
                        return ShipDockingService.Confirm(State.Ships, State.Docking, pair.PairId, shipId);
            }
            return false;
        }
        public bool DisconnectDock(string shipId, string portId) => DisconnectDock(shipId, portId, out _);
        public bool DisconnectDock(string shipId, string portId, out string reason)
        {
            reason = null;
            if (!FlatWorld.Networking.GameNetwork.HasStateAuthority) return SubmitLocalIntent(SpaceNetworkIntentKind.DisconnectDock, ResolveConsole(shipId), shipId, portId);
            foreach (ShipDockingPairState pair in State.Docking)
                if ((pair.ShipAId == shipId || pair.ShipBId == shipId) &&
                    (pair.PortAId == portId || pair.PortBId == portId) && (pair.Connected || pair.ConfirmedA || pair.ConfirmedB))
                {
                    bool connected = pair.Connected;
                    bool result = ShipDockingService.Disconnect(State.Ships, State.Docking, pair.PairId);
                    if (!result) return RejectShipOperation("对接记录已失效。", out reason);
                    reason = FlatWorldLocalizationService.GetUiText(connected ? "已解除对接连接。" : "已撤销尚未完成的对接确认。");
                    return true;
                }
            return RejectShipOperation("当前部件没有已连接或待确认的对接接口，请在对应接口上操作。", out reason);
        }
        #endregion
    }
}
