using System;
using System.Collections.Generic;
using FlatWorld.Networking;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 跨星区乘员成对接触
        private readonly List<SpacePassengerState> contactPassengers = new();
        private readonly List<SpacePassengerContactState> passengerContactWorkspace = new();
        private readonly List<bool> contactActive = new();

        private void CapturePassengerContactPoses()
        {
            contactPassengers.Clear();
            foreach (SpacePassengerState passenger in State.Passengers)
            {
                if (!TryGetNetworkObserver(passenger.ProfileId, out SpaceNetworkObserver observer) || !observer.InSpace) continue;
                Player player = FindNetworkPlayer(passenger.ProfileId);
                if (GameNetwork.IsOnline && player == null) continue;
                int index = contactPassengers.Count;
                if (index == passengerContactWorkspace.Count)
                { passengerContactWorkspace.Add(new SpacePassengerContactState()); contactActive.Add(false); }
                SpacePassengerContactState contact = passengerContactWorkspace[index];
                contact.PreviousPosition = contact.Position = observer.Position;
                contact.VelocityMetersPerSecond = observer.Velocity;
                contact.Supported = passenger.Supported;
                contact.RadiusMeters = .3d;
                contact.MassKg = Math.Max(1d, player?.itemData.Stack.CurrentWeight ?? 80d);
                contactPassengers.Add(passenger);
            }
        }
        private void ResolvePassengerPairs(double seconds)
        {
            for (int i = 0; i < contactPassengers.Count; i++)
            {
                SpacePassengerState passenger = contactPassengers[i];
                SpacePassengerContactState contact = passengerContactWorkspace[i];
                contactActive[i] = TryGetNetworkObserver(passenger.ProfileId, out SpaceNetworkObserver observer) && observer.InSpace;
                if (!contactActive[i]) continue;
                contact.Position = GetPassengerPosition(passenger);
                contact.Supported = passenger.Supported;
                contact.VelocityMetersPerSecond = passenger.FreeMotion.VelocityMetersPerSecond;
            }
            // 每对只结算一次，玩家两侧的场景和本机是否看见都不影响接触。
            for (int i = 0; i < contactPassengers.Count; i++)
                for (int j = i + 1; j < contactPassengers.Count; j++)
                    if (contactActive[i] && contactActive[j]) SpacePassengerContacts.ResolvePair(passengerContactWorkspace[i], passengerContactWorkspace[j], seconds);
            for (int i = 0; i < contactPassengers.Count; i++)
            {
                SpacePassengerState passenger = contactPassengers[i];
                SpacePassengerContactState contact = passengerContactWorkspace[i];
                if (!contactActive[i] || passenger.SurfaceDescending || !passenger.IsInSpace && !passenger.Supported) continue;
                ShipState carrier = passenger.Supported ? GetShip(passenger.ShipId) : null;
                if (carrier != null)
                {
                    ShipGeometry.WorldToLocal(carrier, contact.Position.Sector, contact.Position.LocalMeters.X, contact.Position.LocalMeters.Y,
                        out double x, out double y);
                    int cx = (int)Math.Floor(x / carrier.CellSizeMeters), cy = (int)Math.Floor(y / carrier.CellSizeMeters);
                    if (carrier.Pieces.Exists(piece => piece.IsAlive && piece.BlocksPassage &&
                        System.Linq.Enumerable.Contains(ShipGeometry.Footprint(piece), new ShipCell(cx, cy)))) continue;
                    passenger.LocalX = x; passenger.LocalY = y;
                    if (!ShipGeometry.HasFloor(carrier, cx, cy)) ReleasePassenger(passenger, carrier);
                }
                else
                {
                    passenger.FreeMotion.Sector = contact.Position.Sector;
                    passenger.FreeMotion.PositionMeters = contact.Position.LocalMeters;
                    passenger.FreeMotion.VelocityMetersPerSecond = contact.VelocityMetersPerSecond;
                }
            }
        }
        #endregion
    }
}
