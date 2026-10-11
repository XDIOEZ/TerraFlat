using System;
using UnityEngine;

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
                    ? Vector2.Distance(player.transform.position, center)
                    : WorldTopologyRuntime.Distance(player.transform.position, center);
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
        public bool BeginPickingNavigation(string consoleId)
        {
            if (!IsSpaceView || !FindPiece(consoleId, out ShipState ship, out ShipPieceState console) ||
                console.Kind != ShipPieceKind.Console || !IsPowered(ship, console)) return false;
            pickingNavigationConsole = consoleId; return true;
        }
        public bool TryHandleSpacePointer(Player player)
        {
            if (player == null || string.IsNullOrWhiteSpace(pickingNavigationConsole)) return false;
            Mod_GameController controller = player.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
            if (controller == null || !controller.TryGetMouseWorldPosition(out Vector3 point)) return false;
            string console = pickingNavigationConsole; pickingNavigationConsole = null;
            StartNavigation(console, point); return true;
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
            ShipState ship = GetShip(shipId);
            string consoleId = ResolveConsole(shipId);
            Player actor = ItemMgr.Instance?.User_Player;
            if (ship == null || actor == null || !FindPiece(consoleId, out _, out ShipPieceState console) ||
                Vector2.Distance(actor.transform.position, DisplayPoint(ship, console.CellX + .5d, console.CellY + .5d)) > 3f) return false;
            ShipFlightState ownFlight = GetFlight(shipId);
            ShipDockingService.SynchronizePorts(ship);
            foreach (ShipDockingPortState own in ship.DockingPorts)
            foreach (ShipState other in State.Ships)
            {
                if (other == ship) continue;
                ShipFlightState otherFlight = GetFlight(other.ShipId);
                bool ownSpace = ownFlight.Phase is ShipFlightPhase.Cruising or ShipFlightPhase.Descending;
                bool otherSpace = otherFlight.Phase is ShipFlightPhase.Cruising or ShipFlightPhase.Descending;
                if (ownSpace != otherSpace || !ownSpace && (ownFlight.SurfaceWorldKey != otherFlight.SurfaceWorldKey || Math.Abs(ownFlight.HeightMeters - otherFlight.HeightMeters) > 1d)) continue;
                ShipDockingService.SynchronizePorts(other);
                foreach (ShipDockingPortState target in other.DockingPorts)
                    if (ShipDockingService.TryCreateCandidate(State.Ships, State.Docking, shipId, own.PortId,
                        other.ShipId, target.PortId, out ShipDockingPairState pair))
                        return ShipDockingService.Confirm(State.Ships, State.Docking, pair.PairId, shipId);
            }
            return false;
        }
        public bool DisconnectDock(string shipId, string portId)
        {
            foreach (ShipDockingPairState pair in State.Docking)
                if ((pair.ShipAId == shipId || pair.ShipBId == shipId) &&
                    (pair.PortAId == portId || pair.PortBId == portId))
                    return ShipDockingService.Disconnect(State.Ships, State.Docking, pair.PairId);
            return false;
        }
        #endregion
    }
}
