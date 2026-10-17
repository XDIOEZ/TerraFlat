using System;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 驾驶 HUD 只读数据与落地完成凭证
        public event Action<string> LocalManualConsoleExitRequested;
        public event Action<string> LocalManualConsoleEnterRequested;

        // 主动退出单独通知本地表现，自动触地和失去支撑仍沿用普通释放流程。
        public bool RequestConsoleExit(Player player)
        {
            if (player == null || State == null) return false;
            SpacePassengerState passenger = FindPassenger(player.ProfileName);
            string shipId = passenger?.ShipId;
            bool wasDriving = !string.IsNullOrWhiteSpace(passenger?.ConsolePieceId);
            if (!ExitConsole(player)) return false;
            if (wasDriving && player.IsLocalProfile) LocalManualConsoleExitRequested?.Invoke(shipId);
            return true;
        }

        private void NotifyLocalManualConsoleEntry(Player player, string pieceId)
        {
            if (player.IsLocalProfile && FindPiece(pieceId, out ShipState ship, out _))
                LocalManualConsoleEnterRequested?.Invoke(ship.ShipId);
        }

        // HUD 只读取已有乘员和真实飞行状态，不因展示界面创建乘员或发出驾驶指令。
        public bool TryGetShipFlightHudSnapshot(Player player, out ShipFlightHudSnapshot snapshot)
        {
            snapshot = default;
            if (State == null || player == null) return false;
            SpacePassengerState passenger = FindPassenger(player.ProfileName);
            if (passenger == null) return false;
            ShipState ship = GetShip(passenger.ShipId);
            ShipFlightState flight = ship != null ? GetFlight(ship.ShipId) : null;
            bool surfaceFlight = flight != null && flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending;
            bool driving = surfaceFlight && passenger.Supported && !string.IsNullOrWhiteSpace(passenger.ConsolePieceId) &&
                FindPiece(passenger.ConsolePieceId, out ShipState controlledShip, out ShipPieceState console) &&
                controlledShip == ship && console.Kind == ShipPieceKind.Console && console.IsOperational;
            bool landed = passenger.Supported && flight != null && flight.LandingResolved && flight.HeightMeters <= 0d &&
                flight.Phase is ShipFlightPhase.Landed or ShipFlightPhase.Floating;
            snapshot = new ShipFlightHudSnapshot(driving, landed, ship?.ShipId,
                flight?.Phase ?? default, Math.Max(0d, flight?.HeightMeters ?? 0d), flight?.VerticalSpeed ?? 0d,
                passenger.LastShipLanding);
            return true;
        }

        private void RecordShipLandingCompletion(ShipState ship, ShipFlightState flight, string world)
        {
            if (ship == null || flight == null || State == null || !flight.LandingResolved || flight.HeightMeters > 0d ||
                flight.Phase is not (ShipFlightPhase.Landed or ShipFlightPhase.Floating) || flight.SurfaceWorldKey != world) return;
            var receipt = new ShipLandingReceipt(ship.ShipId, flight.LandingEventId, world,
                flight.Phase == ShipFlightPhase.Floating, State.SimulationSeconds);
            if (!receipt.IsValid) return;
            foreach (SpacePassengerState passenger in State.Passengers)
                if (passenger.ShipId == ship.ShipId) passenger.LastShipLanding = receipt;
        }
        #endregion
    }

    public readonly struct ShipFlightHudSnapshot
    {
        #region 驾驶高度展示快照
        public readonly bool IsDrivingSurfaceFlight, HasLandedSurfaceFlight;
        public readonly string ShipId;
        public readonly ShipFlightPhase Phase;
        public readonly double HeightMeters, VerticalSpeed;
        public readonly ShipLandingReceipt LastLanding;

        public ShipFlightHudSnapshot(bool isDrivingSurfaceFlight, bool hasLandedSurfaceFlight, string shipId,
            ShipFlightPhase phase, double heightMeters, double verticalSpeed, ShipLandingReceipt lastLanding)
        {
            IsDrivingSurfaceFlight = isDrivingSurfaceFlight; HasLandedSurfaceFlight = hasLandedSurfaceFlight;
            ShipId = shipId; Phase = phase; HeightMeters = heightMeters; VerticalSpeed = verticalSpeed;
            LastLanding = lastLanding;
        }
        #endregion
    }
}
